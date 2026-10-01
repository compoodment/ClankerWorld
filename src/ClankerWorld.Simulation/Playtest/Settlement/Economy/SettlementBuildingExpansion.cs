using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record BuildingFootprintRevision(int Width, int Height, int Revision);

public sealed record BuildingExpansionJob(
    string JobId, string BuildingInstanceId, string WorkerId, string OwnerId,
    int ExpectedRevision, GridPoint ExpectedPosition, GridPoint TargetPosition,
    BuildingFootprintRevision TargetFootprint, long StartedTick, long CompletionTick,
    WorldProductionJobState State, IReadOnlyList<string> InputReservationIds,
    string? Failure = null);

public static class BuildingStorageRules
{
    // Trial stock units per tile. These are storage sizes, never occupant or work-slot limits.
    public const int UnitsPerTile = 64;
    public const int NearlyFullPercent = 80;

    public static BuildingDefinition EffectiveDefinition(BuildingDefinition definition, PlacedBuilding building) =>
        building.Footprint is { } footprint
            ? WithSize(definition, footprint.Width, footprint.Height) : definition;

    public static BuildingDefinition WithSize(BuildingDefinition definition, int width, int height) =>
        new(definition.PackageDigest, definition.LocalId, definition.Version, definition.DisplayName,
            width, height, definition.Capacity, definition.BuildCosts, definition.Tags);

    public static int? Capacity(BuildingDefinition definition, PlacedBuilding building) =>
        definition.Tags.Any(tag => tag is "house" or "warehouse")
            ? UnitsPerTile * (building.Footprint?.Width ?? definition.Width) *
                (building.Footprint?.Height ?? definition.Height) : null;

    public static IReadOnlyList<ContentQuantity> ExpansionCosts(BuildingDefinition definition,
        PlacedBuilding building, BuildingFootprintRevision target)
    {
        var current = EffectiveDefinition(definition, building);
        var extraTiles = target.Width * target.Height - current.Width * current.Height;
        return definition.Tags.Contains("warehouse", StringComparer.Ordinal)
            ? [new("wood", 4 * extraTiles), new("stone", 2 * extraTiles)] : [new("wood", 4 * extraTiles)];
    }

    public static bool IsSupported(BuildingDefinition definition, BuildingFootprintRevision footprint) =>
        definition.Tags.Contains("house", StringComparer.Ordinal)
            ? (footprint.Width, footprint.Height, footprint.Revision) is (1, 2, 1) or (2, 1, 1) or (2, 2, 2)
            : definition.Tags.Contains("warehouse", StringComparer.Ordinal) &&
                (footprint.Width, footprint.Height, footprint.Revision) is (2, 3, 1) or (3, 2, 1);
}

public sealed partial class PrivateWorldRuntime
{
    private const string ExpandBuildingPrefix = "expand_building:";
    // Provisional duration; paid model calls do not advance construction.
    private const int BuildingExpansionTicks = 20;

    private int StoredQuantity(string buildingId) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.StorageBuildingId == buildingId).Sum(lot => lot.Quantity);

    private int StorageRoom(string buildingId)
    {
        var building = worldSimulation.Buildings.Single(item => item.InstanceId == buildingId);
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        return BuildingStorageRules.Capacity(definition, building) is { } capacity
            ? Math.Max(0, capacity - StoredQuantity(buildingId) - ReservedStorageGrowth(buildingId)) : int.MaxValue;
    }

    private int ReservedStorageGrowth(string buildingId) => worldSimulation.ProductionJobs
        .Where(job => job.BuildingInstanceId == buildingId && job.State == WorldProductionJobState.Running)
        .Sum(job =>
        {
            var recipe = worldContent.Recipes.Single(item => item.CanonicalId == job.RecipeId);
            return Math.Max(0, recipe.Outputs.Sum(item => item.Amount) - job.InputReservationIds
                .Select(society.Checkpoint.Inventory.GetReservation).Where(reservation =>
                    society.Checkpoint.Inventory.GetLot(reservation.LotId).StorageBuildingId == buildingId)
                .Sum(reservation => reservation.Quantity));
        });

    private IEnumerable<InventoryLot> ExpansionMaterialLots(string actor, PlacedBuilding building) =>
        society.Checkpoint.Inventory.Lots.Where(lot =>
            lot.OwnerId == actor && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null ||
            lot.OwnerId == (building.HouseholdId ?? building.TownId) && lot.StorageBuildingId == building.InstanceId);

    private bool HasExpansionMaterials(string actor, PlacedBuilding building, IReadOnlyList<ContentQuantity> costs) =>
        costs.All(cost => ExpansionMaterialLots(actor, building).Where(lot => lot.ItemKind == cost.ResourceId)
            .Sum(AvailableLotQuantity) >= cost.Amount);

    private InventoryCheckpoint ReserveExpansionMaterials(InventoryCheckpoint inventory, string actor,
        PlacedBuilding building, IReadOnlyList<ContentQuantity> costs, string jobId, long completion,
        out IReadOnlyList<string> reservationIds)
    {
        var ids = new List<string>();
        foreach (var cost in costs)
        {
            var remaining = cost.Amount;
            foreach (var lot in ExpansionMaterialLots(actor, building).Where(lot => lot.ItemKind == cost.ResourceId)
                         .OrderBy(lot => lot.Id, StringComparer.Ordinal))
            {
                var quantity = Math.Min(remaining, AvailableLotQuantity(lot));
                if (quantity <= 0) continue;
                var id = $"{jobId}:material:{ids.Count}";
                inventory = InventoryFixture.Reserve(inventory, id, lot.OwnerId, lot.Id, quantity, jobId, completion);
                ids.Add(id);
                remaining -= quantity;
                if (remaining == 0) break;
            }
            if (remaining != 0) throw new InvalidOperationException("The expansion materials are no longer available.");
        }
        reservationIds = ids;
        return inventory;
    }

    private bool MayExpandBuilding(string actor, PlacedBuilding building, out string failure)
    {
        failure = "Only an adult household member or a current Town resident can expand this building.";
        if (!AdultResident(actor)) return false;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        if (definition.Tags.Contains("house", StringComparer.Ordinal))
        {
            if (building.HouseholdId != society.Checkpoint.GetInhabitant(actor).HouseholdId) return false;
        }
        else if (definition.Tags.Contains("warehouse", StringComparer.Ordinal))
        {
            if (building.TownId is null || TownForResident(actor) != building.TownId) return false;
        }
        else return false;
        if (StoredQuantity(building.InstanceId) * 100 < BuildingStorageRules.Capacity(definition, building) *
                BuildingStorageRules.NearlyFullPercent)
        {
            failure = "The building needs to be nearly full before its storage can be expanded.";
            return false;
        }
        if ((worldSimulation.BuildingExpansions ?? []).Any(job => job.BuildingInstanceId == building.InstanceId &&
                job.State == WorldProductionJobState.Running))
        {
            failure = "An expansion is already in progress for this building.";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private IEnumerable<(GridPoint Position, BuildingFootprintRevision Footprint)> ExpansionShapes(PlacedBuilding building)
    {
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var current = BuildingStorageRules.EffectiveDefinition(definition, building);
        var revision = (building.Footprint?.Revision ?? 0) + 1;
        (int Width, int Height)[] sizes = definition.Tags.Contains("house", StringComparer.Ordinal)
            ? revision == 1 ? [(1, 2), (2, 1)] : revision == 2 ? [(2, 2)] : []
            : revision == 1 ? [(2, 3), (3, 2)] : [];
        foreach (var size in sizes)
        {
            for (var dy = current.Height - size.Height; dy <= 0; dy++)
                for (var dx = current.Width - size.Width; dx <= 0; dx++)
                    yield return (new GridPoint(building.Position.X + dx, building.Position.Y + dy),
                        new BuildingFootprintRevision(size.Width, size.Height, revision));
        }
    }

    private bool CanFitExpansion(PlacedBuilding building, GridPoint position,
        BuildingFootprintRevision footprint, out string failure, string? ownJobId = null)
    {
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        failure = "The expansion overlaps terrain, a resource, a Road, another building, or another expansion.";
        var target = BuildingStorageRules.WithSize(definition, footprint.Width, footprint.Height);
        if (!BuildingStorageRules.IsSupported(definition, footprint) ||
            !WorldContentSimulationRules.Fits(map, worldSimulation.Buildings
                .Where(item => item.InstanceId != building.InstanceId)
                .Select(item => (item, BuildingStorageRules.EffectiveDefinition(
                    worldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId), item))), target, position))
            return false;
        var tiles = WorldContentSimulationRules.Footprint(target, position).ToHashSet();
        var original = WorldContentSimulationRules.Footprint(definition, building);
        if (!original.All(tiles.Contains) || tiles.Any(RoadAndBridgeTiles().Contains) ||
            (worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running &&
                job.JobId != ownJobId).Any(job => ExpansionTiles(job).Any(tiles.Contains))) return false;
        var town = towns.SingleOrDefault(item => item.Id == building.TownId);
        if (town is not null && (!TownBorderRules.IsWithinOrAdjacent(town, position, target.Width, target.Height) ||
            towns.Where(item => item.Id != town.Id).Any(item => item.BorderTiles.Any(tiles.Contains))))
        {
            failure = "The expansion does not fit the building's Town boundary or overlaps another Town.";
            return false;
        }
        if (building.Entrance is { } entrance && !WorldContentSimulationRules.IsEntrance(target, position, entrance))
        {
            failure = "The expansion would obstruct the building's entrance.";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private IEnumerable<GridPoint> ExpansionTiles(BuildingExpansionJob job) =>
        Enumerable.Range(0, job.TargetFootprint.Height).SelectMany(dy =>
            Enumerable.Range(0, job.TargetFootprint.Width).Select(dx =>
                new GridPoint(job.TargetPosition.X + dx, job.TargetPosition.Y + dy)));

    private void AddBuildingExpansionCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var building in worldSimulation.Buildings)
        {
            if (!MayExpandBuilding(actor, building, out _) || !ExpansionShapes(building).Any(shape =>
                    CanFitExpansion(building, shape.Position, shape.Footprint, out _))) continue;
            var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            var shape = ExpansionShapes(building).First();
            if (!CanAcquireProjectInputs(BuildingStorageRules.ExpansionCosts(definition, building, shape.Footprint),
                    HouseholdFor(actor), actor)) continue;
            candidates.Add(new(ExpandBuildingPrefix + building.InstanceId,
                $"Expand the nearly full {definition.DisplayName} without moving or sharing its stock.", 24, building.InstanceId));
        }
    }

    private void ApplyBuildingExpansionCandidate(string actor, PlaytestInhabitantState state, string buildingId)
    {
        var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == buildingId);
        if (building is null || !MayExpandBuilding(actor, building, out _)) return;
        var shape = ExpansionShapes(building).FirstOrDefault(shape => CanFitExpansion(building, shape.Position, shape.Footprint, out _));
        if (shape.Footprint is null) return;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var costs = BuildingStorageRules.ExpansionCosts(definition, building, shape.Footprint);
        var missing = costs.FirstOrDefault(cost => !HasExpansionMaterials(actor, building, [cost]));
        if (missing.Amount > 0)
        {
            if (SharedItem(missing.ResourceId, actor) is not null)
            {
                CollectEquipment(actor, state, missing.ResourceId);
            }
            else if (MaterialSource(missing.ResourceId, actor) is { } source)
                GatherProjectMaterial(actor, state, missing.ResourceId, source);
            return;
        }
        if (state.Position != building.Position) MoveToward(actor, state, building.Position, "building_expansion", 0);
        else StartBuildingExpansionCore(actor, buildingId);
    }

    public ProductionStartResult StartBuildingExpansion(string actor, string buildingId)
    {
        gate.Wait();
        try { return StartBuildingExpansionCore(actor, buildingId); }
        finally { gate.Release(); }
    }

    private ProductionStartResult StartBuildingExpansionCore(string actor, string buildingId)
    {
        var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == buildingId);
        if (building is null) return ProductionStartResult.Rejected(buildingId, "The building is not placed.");
        if (!MayExpandBuilding(actor, building, out var failure)) return ProductionStartResult.Rejected(buildingId, failure);
        if (inhabitants[actor].Position != building.Position)
            return ProductionStartResult.Rejected(buildingId, "The worker must be at the building before expansion starts.");
        var shape = ExpansionShapes(building).FirstOrDefault(shape => CanFitExpansion(building, shape.Position, shape.Footprint, out _));
        if (shape.Footprint is null)
            return ProductionStartResult.Rejected(buildingId, "There is no legal space for this building's next footprint.");
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var owner = building.HouseholdId ?? building.TownId!;
        var costs = BuildingStorageRules.ExpansionCosts(definition, building, shape.Footprint);
        if (!HasExpansionMaterials(actor, building, costs))
            return ProductionStartResult.Rejected(buildingId, "Bring the expansion materials to the building or carry them to the site.");
        var jobId = $"expansion-{worldSimulation.NextProductionJobSequence:D8}";
        var completion = WorldTick + BuildingExpansionTicks;
        IReadOnlyList<string> reservations = [];
        ApplyInventoryTransition(inventory => ReserveExpansionMaterials(inventory, actor, building, costs, jobId, completion, out reservations));
        var job = new BuildingExpansionJob(jobId, buildingId, actor, owner, building.Footprint?.Revision ?? 0,
            building.Position, shape.Position, shape.Footprint, WorldTick, completion, WorldProductionJobState.Running, reservations);
        worldSimulation = worldSimulation with
        {
            BuildingExpansions = (worldSimulation.BuildingExpansions ?? []).Append(job).OrderBy(item => item.JobId, StringComparer.Ordinal).ToArray(),
            NextProductionJobSequence = worldSimulation.NextProductionJobSequence + 1,
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("building_expansion_started", $"{buildingId}:{jobId}");
        return new ProductionStartResult(true, jobId, buildingId, null);
    }

    private void ProcessBuildingExpansions(long targetTick)
    {
        foreach (var job in (worldSimulation.BuildingExpansions ?? []).Where(item => item.State == WorldProductionJobState.Running).ToArray())
        {
            var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == job.BuildingInstanceId);
            string? failure = null;
            if (building is null || building.Position != job.ExpectedPosition ||
                (building.Footprint?.Revision ?? 0) != job.ExpectedRevision) failure = "The building changed before its expansion completed.";
            else if (!AdultResident(job.WorkerId) ||
                building.HouseholdId is { } houseOwner && (houseOwner != job.OwnerId || HouseholdFor(job.WorkerId) != houseOwner) ||
                building.HouseholdId is null && TownForResident(job.WorkerId) != building.TownId)
                failure = "The worker no longer has permission to expand this building.";
            else if (!CanFitExpansion(building, job.TargetPosition, job.TargetFootprint, out var fitFailure, job.JobId))
                failure = fitFailure;
            else if (job.InputReservationIds.Any(id => society.Checkpoint.Inventory.GetReservation(id) is not
            { State: InventoryReservationState.Reserved, ExpiryTick: var expiry } || expiry < targetTick ||
                society.Checkpoint.Inventory.GetLot(society.Checkpoint.Inventory.GetReservation(id).LotId) is not
                { ConditionBasisPoints: > 0, FreshnessBasisPoints: > 0 }))
                failure = "The expansion materials are no longer usable.";
            if (failure is null && job.CompletionTick > targetTick) continue;
            ApplyInventoryTransition(inventory =>
            {
                foreach (var id in job.InputReservationIds)
                    if (failure is null) inventory = InventoryFixture.ConsumeReservation(inventory, id);
                    else if (inventory.GetReservation(id).State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed)
                        inventory = InventoryFixture.ReleaseReservation(inventory, id, "expansion_cancelled");
                return inventory;
            });
            if (failure is null)
            {
                var expanded = building! with { Position = job.TargetPosition, Footprint = job.TargetFootprint };
                worldSimulation = worldSimulation with { Buildings = worldSimulation.Buildings.Select(item => item.InstanceId == expanded.InstanceId ? expanded : item).ToArray() };
                if (towns.SingleOrDefault(item => item.Id == expanded.TownId) is { } town)
                    SetTown(town with { BorderTiles = TownBorderRules.Expand(map, town, ExpansionTiles(job)) });
            }
            worldSimulation = worldSimulation with
            {
                BuildingExpansions = (worldSimulation.BuildingExpansions ?? []).Select(item => item.JobId == job.JobId
                    ? item with { State = failure is null ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled, Failure = failure } : item).ToArray(),
            };
            AppendEvent(failure is null ? "building_expanded" : "building_expansion_cancelled",
                $"{job.BuildingInstanceId}:{job.JobId}" + (failure is null ? "" : $":{failure}"));
        }
    }
}
