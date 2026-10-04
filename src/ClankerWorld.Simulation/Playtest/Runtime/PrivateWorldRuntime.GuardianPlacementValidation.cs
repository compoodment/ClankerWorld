using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateGuardianPlacements(
        IEnumerable<PlaytestInhabitantState> physical,
        SocietyCheckpoint checkpoint,
        IReadOnlyList<TownRuntimeState> towns,
        SeededMap map,
        int schemaVersion)
    {
        var physicalPeople = physical.ToArray();
        if (!physicalPeople.Any(person => person.GuardianPlacement is not null)) return;

        var people = checkpoint.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var physicalIds = physicalPeople.Select(person => person.InhabitantId).ToHashSet(StringComparer.Ordinal);
        var relationships = checkpoint.Relationships.ToDictionary(edge => edge.Id, StringComparer.Ordinal);
        var householdIds = checkpoint.Households.Select(household => household.Id).ToHashSet(StringComparer.Ordinal);
        var townIds = towns.Select(town => town.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var state in physicalPeople)
        {
            if (state.GuardianPlacement is not { } placement) continue;
            if (schemaVersion < GuardianPlacementSchemaVersion ||
                !people.TryGetValue(state.InhabitantId, out var child) ||
                child.Status != SocietyInhabitantStatus.Active ||
                child.AgeBand is not (SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent) ||
                state.GuardianSearch is not null ||
                !GuardianPlacementId(placement.CaregiverId) ||
                !people.TryGetValue(placement.CaregiverId, out var caregiver) ||
                caregiver.Status != SocietyInhabitantStatus.Active ||
                caregiver.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                !physicalIds.Contains(caregiver.Id) || child.PrimaryCaregiverId != caregiver.Id ||
                !GuardianPlacementId(placement.CareRelationshipId) || placement.CareRevision <= 0 ||
                !relationships.TryGetValue(placement.CareRelationshipId, out var care) ||
                care.Type != SocietyRelationshipType.Caregiver || care.State != SocietyRelationshipState.Accepted ||
                care.ProposerId != caregiver.Id || care.TargetId != child.Id || care.Revision != placement.CareRevision ||
                placement.StartedTick < 0 || placement.StartedTick < care.ProposedTick ||
                placement.StartedTick > checkpoint.WorldTick ||
                placement.Stage is not ("collecting" or "escorting") ||
                placement.Blocker is { } blocker &&
                    (string.IsNullOrWhiteSpace(blocker) || blocker.Length > 512 || blocker.Any(char.IsControl)) ||
                placement.DestinationHouseholdId is { } household &&
                    (!GuardianPlacementId(household) || !householdIds.Contains(household)) ||
                placement.DestinationTownId is { } town &&
                    (!GuardianPlacementId(town) || !townIds.Contains(town)))
                throw new InvalidDataException("The saved dependent guardian placement or accepted care link is invalid.");

            var hasAnyHouse = placement.HouseId is not null || placement.HouseDefinitionId is not null ||
                placement.HousePlacedTick is not null || placement.HousePosition is not null;
            var hasCompleteHouse = GuardianPlacementId(placement.HouseId) &&
                GuardianPlacementId(placement.HouseDefinitionId) &&
                placement.HousePlacedTick is >= 0 && placement.HousePlacedTick <= checkpoint.WorldTick &&
                placement.HousePosition is { } position && map.Contains(position) &&
                placement.DestinationHouseholdId is not null && placement.DestinationTownId is not null;
            if (hasAnyHouse && !hasCompleteHouse || placement.Stage == "escorting" && !hasCompleteHouse)
                throw new InvalidDataException("The saved dependent guardian placement has an incomplete House destination.");

            // A caregiver can change household or Town, and a chosen House can
            // be removed, reassigned or filled before the next action. Keep
            // those pending routes loadable; execution rechecks the destination.
        }
    }

    // Building and society identities have no general length cap. Keep their
    // native normalization instead of introducing a smaller save-only limit.
    private static bool GuardianPlacementId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim();
}
