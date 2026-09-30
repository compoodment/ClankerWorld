using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateBuildingExpansionState(WorldContentSimulationState simulation,
        DeclarativeWorldContentState content, SocietyCheckpoint society, SeededMap map, int schemaVersion)
    {
        if (schemaVersion < 29 && (simulation.Buildings.Any(item => item.Footprint is not null) ||
                simulation.BuildingExpansions is { Count: > 0 } || simulation.GuestInvitations is { Count: > 0 }))
            throw new InvalidDataException("Building expansions and guest invitations require private-world schema 29.");
        var buildings = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var people = society.Inhabitants.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var allJobIds = simulation.ProductionJobs.Concat(simulation.CropBuilds ?? []).Select(item => item.JobId)
            .ToHashSet(StringComparer.Ordinal);
        var activeBuildings = new HashSet<string>(StringComparer.Ordinal);
        var activeExpansionTiles = new HashSet<GridPoint>();
        var expansionJobs = simulation.BuildingExpansions ?? [];
        foreach (var job in expansionJobs)
        {
            ContentPackageRules.ValidateLocalId(job.JobId);
            if (!allJobIds.Add(job.JobId) || !buildings.TryGetValue(job.BuildingInstanceId, out var building) ||
                !people.ContainsKey(job.WorkerId) ||
                job.OwnerId != (building.HouseholdId ?? building.TownId) ||
                !definitions.TryGetValue(building.DefinitionId, out var definition) ||
                job.TargetFootprint is null || !BuildingStorageRules.IsSupported(definition, job.TargetFootprint) ||
                job.ExpectedRevision != job.TargetFootprint.Revision - 1 ||
                job.StartedTick < 0 || job.StartedTick > society.WorldTick || job.CompletionTick <= job.StartedTick ||
                job.State is not (WorldProductionJobState.Running or WorldProductionJobState.Completed or WorldProductionJobState.Cancelled) ||
                job.State == WorldProductionJobState.Running && job.CompletionTick <= society.WorldTick ||
                job.InputReservationIds is null || job.InputReservationIds.Count == 0 ||
                job.InputReservationIds.Distinct(StringComparer.Ordinal).Count() != job.InputReservationIds.Count ||
                !map.Contains(job.TargetPosition))
                throw new InvalidDataException("The saved building expansion is malformed.");
            if (job.State != WorldProductionJobState.Running) continue;
            if (!activeBuildings.Add(job.BuildingInstanceId) || building.Position != job.ExpectedPosition ||
                (building.Footprint?.Revision ?? 0) != job.ExpectedRevision)
                throw new InvalidDataException("The saved expansion no longer refers to its original building footprint.");
            var target = BuildingStorageRules.WithSize(definition, job.TargetFootprint.Width, job.TargetFootprint.Height);
            var targetTiles = WorldContentSimulationRules.Footprint(target, job.TargetPosition).ToArray();
            if (!WorldContentSimulationRules.Footprint(definition, building).All(targetTiles.Contains) ||
                targetTiles.Any(tile => !map.IsBuildable(tile)) ||
                targetTiles.Any(tile => !activeExpansionTiles.Add(tile)))
                throw new InvalidDataException("The saved expansion has an invalid or competing footprint.");
            var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in job.InputReservationIds)
            {
                var reservation = society.Inventory.Reservations.SingleOrDefault(item => item.Id == id);
                var lot = reservation is null ? null : society.Inventory.Lots.SingleOrDefault(item => item.Id == reservation.LotId);
                if (reservation is null || lot is null || reservation.Purpose != job.JobId ||
                    reservation.OwnerId != lot.OwnerId || lot.OwnerId != job.OwnerId && lot.OwnerId != job.WorkerId ||
                    reservation.State != InventoryReservationState.Reserved || reservation.ExpiryTick != job.CompletionTick)
                    throw new InvalidDataException("The expansion is missing its reserved materials.");
                reserved[lot.ItemKind] = reserved.GetValueOrDefault(lot.ItemKind) + reservation.Quantity;
            }
            var expected = BuildingStorageRules.ExpansionCosts(definition, building, job.TargetFootprint);
            if (expected.Count != reserved.Count || expected.Any(cost => reserved.GetValueOrDefault(cost.ResourceId) != cost.Amount))
                throw new InvalidDataException("The expansion's reserved materials do not match its footprint.");
        }
        if (!expansionJobs.Select(item => item.JobId).SequenceEqual(expansionJobs.Select(item => item.JobId).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Building expansions are not in canonical order.");
        var invitationIds = new HashSet<(string House, string Guest)>();
        var invitations = simulation.GuestInvitations ?? [];
        foreach (var invitation in invitations)
        {
            if (!invitationIds.Add((invitation.HouseInstanceId, invitation.GuestId)) ||
                !buildings.TryGetValue(invitation.HouseInstanceId, out var house) || house.HouseholdId is null ||
                !definitions[house.DefinitionId].Tags.Contains("house", StringComparer.Ordinal) ||
                !people.ContainsKey(invitation.GuestId) || !people.ContainsKey(invitation.InvitedById) ||
                invitation.ChangedTick < 0 || invitation.ChangedTick > society.WorldTick)
                throw new InvalidDataException("The saved House guest invitation is malformed.");
        }
        if (!invitations.SequenceEqual(invitations.OrderBy(item => item.HouseInstanceId, StringComparer.Ordinal)
                .ThenBy(item => item.GuestId, StringComparer.Ordinal)))
            throw new InvalidDataException("House guest invitations are not in canonical order.");
    }
}
