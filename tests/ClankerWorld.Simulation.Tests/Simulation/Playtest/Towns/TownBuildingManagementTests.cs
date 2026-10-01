using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownBuildingManagementTests
{
    [Fact]
    public void PrivateBuildingCanChangeHouseholdWithoutChangingTownAndTerminalHistorySurvivesRemoval()
    {
        var state = StartedState("town-building-history");
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new IdleProvider()))
        {
            var setupState = setup.ExportState();
            var house = setupState.WorldContent!.Buildings.Single(item => item.LocalId == "house-1x1");
            var setupTown = setupState.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
            var occupied = setupState.WorldSimulation!.Buildings.SelectMany(placed =>
                WorldContentSimulationRules.Footprint(setupState.WorldContent.Buildings.Single(item =>
                    item.CanonicalId == placed.DefinitionId), placed)).ToHashSet();
            var site = setupTown.BorderTiles.First(point => setupState.Map.IsBuildable(point) &&
                !occupied.Contains(point) && !setupState.Map.Resources.Any(item => item.Position == point));
            var placed = setup.PlaceBuilding("historic-house", house.CanonicalId, site, "household:camp-alpha");
            Assert.True(placed.Applied, placed.Failure);
            state = setup.ExportState();
        }
        var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "historic-house");
        var town = state.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
        var worker = state.Society.Society.Inhabitants.First(item => item.HouseholdId == building.HouseholdId).Id;
        var recipe = state.WorldContent!.Recipes.First(item => !item.IsCrop);
        var expansion = new BuildingExpansionJob(
            "historic-expansion", building.InstanceId, worker, building.HouseholdId!, 0,
            building.Position, building.Position, new(1, 2, 1),
            0, 1, WorldProductionJobState.Completed, ["historic-reservation"]);
        var production = new WorldProductionJob("historic-production", recipe.CanonicalId,
            building.InstanceId, worker, 0, 1, WorldProductionJobState.Completed, []);
        state = state with
        {
            WorldSimulation = state.WorldSimulation with
            {
                ProductionJobs = [production],
                BuildingExpansions = [expansion],
            },
        };

        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var originalBorder = town.BorderTiles.ToArray();
        var reassigned = world.ReassignBuilding(building.InstanceId, building.TownId, building.HouseholdId,
            targetTownId: null, targetHouseholdId: "household:camp-beta");
        Assert.True(reassigned.Applied, reassigned.Failure);
        var movedOwner = Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == building.InstanceId);
        Assert.Equal(building.TownId, movedOwner.TownId);
        Assert.Equal("household:camp-beta", movedOwner.HouseholdId);
        Assert.Equal(originalBorder, Assert.Single(world.Towns, item => item.Id == building.TownId).BorderTiles);
        Assert.Contains(building.InstanceId, Assert.Single(world.Towns, item => item.Id == building.TownId).AssignedBuildingIds);

        var stale = world.ReassignBuilding(building.InstanceId, building.TownId, building.HouseholdId,
            targetTownId: null, targetHouseholdId: "household:camp-alpha");
        Assert.False(stale.Applied);
        Assert.Contains("owner changed", stale.Failure, StringComparison.OrdinalIgnoreCase);

        var removed = world.RemoveBuilding(building.InstanceId, building.TownId, "household:camp-beta");
        Assert.True(removed.Applied, removed.Failure);
        var saved = world.ExportState();
        Assert.DoesNotContain(saved.WorldSimulation!.Buildings, item => item.InstanceId == building.InstanceId);
        Assert.DoesNotContain(building.InstanceId, Assert.Single(world.Towns, item => item.Id == building.TownId).AssignedBuildingIds);
        Assert.Equal(originalBorder, Assert.Single(world.Towns, item => item.Id == building.TownId).BorderTiles);
        Assert.Contains(saved.WorldSimulation.ProductionJobs, item => item.JobId == production.JobId &&
            item.BuildingInstanceId == building.InstanceId && item.State == WorldProductionJobState.Completed);
        Assert.Contains(saved.WorldSimulation.BuildingExpansions!, item => item.JobId == expansion.JobId &&
            item.BuildingInstanceId == building.InstanceId && item.DefinitionId == building.DefinitionId);

        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(originalBorder, Assert.Single(restored.Towns, item => item.Id == building.TownId).BorderTiles);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(saved with { SchemaVersion = 34 }));
    }

    [Fact]
    public void LastFarmhouseCannotBeRemovedOrReassignedDuringItsHouseholdsFieldWork()
    {
        var (state, workerId, _, position) = FarmFieldTests.PreparedFarmer("town-building-field-work");
        using var world = FarmFieldTests.Restore(state);
        Assert.True(world.StartFieldWork(workerId, position, FarmWorkKind.Till).Accepted);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");

        var removal = world.RemoveBuilding(farmhouse.InstanceId, farmhouse.TownId, farmhouse.HouseholdId);
        Assert.False(removal.Applied);
        Assert.Contains("field work", removal.Failure, StringComparison.OrdinalIgnoreCase);
        var reassignment = world.ReassignBuilding(farmhouse.InstanceId, farmhouse.TownId, farmhouse.HouseholdId,
            targetTownId: null, targetHouseholdId: "household:camp-beta");
        Assert.False(reassignment.Applied);
        Assert.Contains("field work", reassignment.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Validate();
    }

    [Fact]
    public async Task EmptyTownWarehouseCanBeReassignedOffBorderAndItsStockIsSalvagedAfterReload()
    {
        var state = StartedState("town-empty-warehouse-salvage");
        var warehouse = state.WorldSimulation!.Buildings.Single(building =>
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("warehouse", StringComparer.Ordinal));
        var firstTown = state.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
        using var stockedWorld = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var beforeRefusal = PrivateWorldRuntimeCodec.Encode(stockedWorld.ExportState());
        var blockedRemoval = stockedWorld.RemoveBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId);
        Assert.False(blockedRemoval.Applied);
        Assert.Contains("Empty this building", blockedRemoval.Failure, StringComparison.Ordinal);
        Assert.Equal(beforeRefusal, PrivateWorldRuntimeCodec.Encode(stockedWorld.ExportState()));

        state = stockedWorld.ExportState();
        firstTown = state.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
        var quietBorder = state.Map.Tiles.Select(tile => tile.Position)
            .First(point => state.Map.IsLand(point) && !firstTown.BorderTiles.Contains(point));
        var quietTown = new TownRuntimeState("town:quiet-yard", "Quiet Yard", "founded",
            state.Society.Society.WorldTick, [], [], [quietBorder]);
        var warehouseLotIds = state.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == warehouse.InstanceId).Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !warehouseLotIds.Contains(lot.Id)).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations
                .Where(item => !warehouseLotIds.Contains(item.LotId)).ToArray(),
        };
        state = state with
        {
            Towns = [firstTown, quietTown],
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var originalBorders = world.Towns.ToDictionary(item => item.Id, item => item.BorderTiles.ToArray(), StringComparer.Ordinal);
        var reassigned = world.ReassignBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId,
            targetTownId: quietTown.Id, targetHouseholdId: null);
        Assert.True(reassigned.Applied, reassigned.Failure);
        Assert.DoesNotContain(warehouse.InstanceId, Assert.Single(world.Towns, item => item.Id == firstTown.Id).AssignedBuildingIds);
        Assert.Contains(warehouse.InstanceId, Assert.Single(world.Towns, item => item.Id == quietTown.Id).AssignedBuildingIds);
        Assert.All(world.Towns, town => Assert.Equal(originalBorders[town.Id], town.BorderTiles));
        world.Validate();

        var reassignedState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var stockInventory = reassignedState.Society.Society.Inventory;
        var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        var costs = workshop.BuildCosts;
        var costKinds = costs.Select(item => item.ResourceId).ToHashSet(StringComparer.Ordinal);
        var removedCostLots = stockInventory.Lots.Where(lot => lot.OwnerId == "household:camp-alpha" &&
            costKinds.Contains(lot.ItemKind)).Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        stockInventory = stockInventory with
        {
            Lots = stockInventory.Lots.Where(lot => !removedCostLots.Contains(lot.Id)).ToArray(),
            Reservations = stockInventory.Reservations.Where(item => !removedCostLots.Contains(item.LotId)).ToArray(),
        };
        foreach (var cost in costs)
            stockInventory = InventoryFixture.AddLot(stockInventory, "quiet-yard-" + cost.ResourceId,
                cost.ResourceId, quietTown.Id, cost.Amount, storageBuildingId: warehouse.InstanceId);
        reassignedState = reassignedState with
        {
            Society = reassignedState.Society with
            {
                Society = reassignedState.Society.Society with { Inventory = stockInventory },
            },
        };

        var workerId = reassignedState.Society.Society.Inhabitants.First(item =>
            item.HouseholdId == "household:camp-alpha").Id;
        var occupied = reassignedState.WorldSimulation!.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(reassignedState.WorldContent!.Buildings.Single(definition =>
                definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var site = reassignedState.Map.Tiles.Select(tile => tile.Position).First(point =>
            reassignedState.Map.IsBuildable(point) && !occupied.Contains(point) &&
            !reassignedState.Map.Resources.Any(resource => resource.Position == point) &&
            !reassignedState.Map.CampObjects.Any(item => item.Position == point));
        reassignedState = reassignedState with
        {
            Inhabitants = reassignedState.Inhabitants.Select(person => person.InhabitantId == workerId
                ? person with
                {
                    Position = warehouse.Position,
                    HungerBasisPoints = 10_000,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(workshop.CanonicalId, site),
                        workshop.DisplayName, reassignedState.Society.Society.WorldTick, "acquiring",
                        LastTransitionTick: reassignedState.Society.Society.WorldTick),
                }
                : person).ToArray(),
        };
        using var salvager = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(reassignedState)), _ => new IdleProvider());
        var beforePickup = salvager.Society.Inventory.Lots.Where(lot => lot.OwnerId == quietTown.Id &&
            lot.StorageBuildingId == warehouse.InstanceId && costKinds.Contains(lot.ItemKind)).Sum(lot => lot.Quantity);
        Assert.True((await salvager.AdvanceOneTickAsync()).Advanced);
        var collected = Assert.Single(salvager.ExportState().Events, item => item.Kind == "town_resource_collected");
        Assert.Contains($":{warehouse.InstanceId}", collected.Detail, StringComparison.Ordinal);
        var carried = Assert.Single(salvager.Society.Inventory.Lots, lot => lot.OwnerId == workerId && costKinds.Contains(lot.ItemKind));
        Assert.Null(carried.StorageBuildingId);
        Assert.Null(carried.DeliveryBuildingId);
        Assert.Null(carried.GroundPosition);
        Assert.Equal(beforePickup, salvager.Society.Inventory.Lots.Where(lot =>
            lot.OwnerId == quietTown.Id && lot.StorageBuildingId == warehouse.InstanceId && costKinds.Contains(lot.ItemKind))
            .Sum(lot => lot.Quantity) + carried.Quantity);

        var saved = PrivateWorldRuntimeCodec.Encode(salvager.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new IdleProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(originalBorders[quietTown.Id], Assert.Single(restored.Towns, item => item.Id == quietTown.Id).BorderTiles);
    }

    private static PrivateWorldRuntimeState StartedState(string seed)
    {
        using var setup = PrivateWorldRuntime.Restore(
            GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions(seed, WorldSizePreset.Small)),
            _ => new IdleProvider());
        setup.StageStarterContent();
        return setup.ExportState();
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = request.Observation.Candidates.Where(item => item.Id == "safe_idle").ToArray(),
                },
            }, cancellationToken);
    }
}
