using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void RefreshTownLandHearings()
    {
        foreach (var town in towns.ToArray())
        {
            if (town.Governance is { } council && (town.LandHearings.Cases.Any(landCase => landCase.Status != "settled" ||
                    landCase.ReopenRequests.Any(request => request.Status == "pending")) ||
                town.LandHearings.Transfers.Any(transfer => transfer.Status == "pending")))
                SaveTownGovernance(town, council, town.Government);
        }
    }

    private static void ValidateLandHearings(SeededMap map, SocietyCheckpoint checkpoint,
        IReadOnlyList<TownRuntimeState> savedTowns, IReadOnlyList<HouseholdLandUseRight> rights,
        IReadOnlyList<HouseholdLandUseRequest> requests, IReadOnlyList<TownLandTitleRecord> titles, int day)
    {
        var knownAgents = checkpoint.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var knownHouseholds = checkpoint.Households.Select(household => household.Id).ToHashSet(StringComparer.Ordinal);
        var households = checkpoint.Inhabitants.ToDictionary(person => person.Id, person => person.HouseholdId, StringComparer.Ordinal);
        foreach (var town in savedTowns)
        {
            if (town.LandHearings is null)
                throw new InvalidDataException("A saved Town is missing its land-hearing history.");
            var adults = checkpoint.Inhabitants.Where(person => town.ResidentIds.Contains(person.Id, StringComparer.Ordinal) &&
                    person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
                .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
            var currentParties = town.LandHearings.Cases.ToDictionary(item => item.Id,
                item => (IReadOnlyList<TownLandCaseParty>)TownLandCasePartyRules.CurrentParties(town,
                    TownLandHearingRules.CurrentRevision(item).Tiles, rights, requests, checkpoint.Inhabitants,
                    checkpoint.WorldTick, item), StringComparer.Ordinal);
            TownLandHearingValidation.Validate(map, checkpoint.WorldTick, town.Id, town.LandHearings,
                rights.Where(right => right.TownId == town.Id).ToArray(), titles, knownAgents, knownHouseholds,
                town.Governance, day, town.Government, adults, households, currentParties);
            foreach (var proposal in town.Governance?.Proposals.Where(proposal => proposal.Kind == "land_hearing") ?? [])
                TownLandGovernmentFilingRules.Validate(proposal, town, map, titles, knownHouseholds);
        }
    }
}
