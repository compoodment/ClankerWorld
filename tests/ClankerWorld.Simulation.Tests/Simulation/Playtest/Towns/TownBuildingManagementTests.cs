using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownBuildingManagementTests
{
    [Fact]
    public void PrivateBuildingCanChangeHouseholdWithoutChangingTownAndTerminalProductionHistorySurvivesRemoval()
    {
        var state = StartedState("town-building-history");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "historic-tailor-fiber",
            "fiber", "household:camp-alpha", 2, storageBuildingId: "first-town-house-a");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new IdleProvider()))
        {
            var setupState = setup.ExportState();
            var buildingDefinition = setupState.WorldContent!.Buildings.Single(item => item.LocalId == "tailor-shop-1x1");
            var setupTown = setupState.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
            var occupied = setupState.WorldSimulation!.Buildings.SelectMany(placed =>
                WorldContentSimulationRules.Footprint(setupState.WorldContent.Buildings.Single(item =>
                    item.CanonicalId == placed.DefinitionId), placed)).ToHashSet();
            var site = setupTown.BorderTiles.First(point => setupState.Map.IsBuildable(point) &&
                !occupied.Contains(point) && !setupState.Map.Resources.Any(item => item.Position == point));
            var placed = setup.PlaceBuilding("historic-tailor", buildingDefinition.CanonicalId, site, "household:camp-alpha");
            Assert.True(placed.Applied, placed.Failure);
            state = setup.ExportState();
        }
        var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "historic-tailor");
        var town = state.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
        var worker = state.Society.Society.Inhabitants.First(item => item.HouseholdId == building.HouseholdId).Id;
        var recipe = state.WorldContent!.Recipes.First(item => !item.IsCrop);
        var production = new WorldProductionJob("historic-production", recipe.CanonicalId,
            building.InstanceId, worker, 0, 1, WorldProductionJobState.Completed, []);
        state = state with
        {
            WorldSimulation = state.WorldSimulation with
            {
                ProductionJobs = [production],
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

        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(originalBorder, Assert.Single(restored.Towns, item => item.Id == building.TownId).BorderTiles);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(saved with { SchemaVersion = 36 }));
    }

    [Fact]
    public void RemovingAHousePreservesItsCompletedExpansionDefinitionForReload()
    {
        var state = StartedState("town-building-expansion-history");
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-b");
        var worker = state.Society.Society.Inhabitants.First(item => item.HouseholdId == house.HouseholdId).Id;
        var lotIds = state.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId || lot.DeliveryBuildingId == house.InstanceId)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !lotIds.Contains(lot.Id)).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations
                .Where(item => !lotIds.Contains(item.LotId)).ToArray(),
        };
        var expansion = new BuildingExpansionJob(
            "historic-expansion", house.InstanceId, worker, house.HouseholdId!, 0,
            house.Position, house.Position, new(1, 2, 1),
            0, 1, WorldProductionJobState.Completed, ["historic-reservation"]);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSimulation = state.WorldSimulation with { BuildingExpansions = [expansion] },
        };

        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var removed = world.RemoveBuilding(house.InstanceId, house.TownId, house.HouseholdId);
        Assert.True(removed.Applied, removed.Failure);
        var saved = world.ExportState();
        var history = Assert.Single(saved.WorldSimulation!.BuildingExpansions!);
        Assert.Equal(house.InstanceId, history.BuildingInstanceId);
        Assert.Equal(house.DefinitionId, history.DefinitionId);

        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.DoesNotContain(restored.WorldSimulation.Buildings, item => item.InstanceId == house.InstanceId);
    }

    [Fact]
    public void ReassignmentCannotGiveAHouseholdASecondBuildingOfTheSameKind()
    {
        var state = StartedState("town-building-duplicate-kind");
        var definitions = state.WorldContent!.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var houses = state.WorldSimulation!.Buildings.Where(item =>
            HouseholdBuildingKinds.KindOf(definitions[item.DefinitionId]) == "house").ToArray();
        Assert.Equal(2, houses.Length);
        Assert.Equal(2, houses.Select(item => item.HouseholdId).Distinct(StringComparer.Ordinal).Count());
        var source = houses.Single(item => item.HouseholdId == "household:camp-beta");
        var targetHouseholdId = "household:camp-alpha";
        var sourceLotIds = state.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == source.InstanceId || lot.DeliveryBuildingId == source.InstanceId)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !sourceLotIds.Contains(lot.Id)).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations
                .Where(item => !sourceLotIds.Contains(item.LotId)).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };

        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var result = world.ReassignBuilding(source.InstanceId, source.TownId, source.HouseholdId,
            targetTownId: null, targetHouseholdId: targetHouseholdId);

        Assert.False(result.Applied);
        Assert.Contains("already has a House", result.Failure, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Validate();

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new IdleProvider());
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Contains(restored.WorldSimulation.Buildings, item => item.InstanceId == source.InstanceId &&
            item.HouseholdId == source.HouseholdId);
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
    public void ActiveEquipmentRepairPreventsRemovingOrReassigningItsWorkSite()
    {
        var (state, tailorId) = TailorTestWorld.Create("town-building-repair-work", 0);
        var tick = state.Society.Society.WorldTick;
        var tailor = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == tailorId);
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "repair-site-coat", "padded_coat", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "repair-site-coat", 8_000);
        inventory = InventoryFixture.AddLot(inventory, "repair-site-cloth", "cloth", actor, 1);
        const string reservationId = "repair-site-material";
        inventory = InventoryFixture.Reserve(inventory, reservationId, actor, "repair-site-cloth", 1,
            "equipment_repair", tick + 120);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = tailor.Position,
                    HungerBasisPoints = 9_000,
                    Equipment = new(Repair: new EquipmentRepairWork("repair-site-coat", tailorId, tick, 0, [reservationId])),
                }
                : person).ToArray(),
        };

        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var reassignment = world.ReassignBuilding(tailor.InstanceId, tailor.TownId, tailor.HouseholdId,
            targetTownId: null, targetHouseholdId: "household:camp-beta");
        Assert.False(reassignment.Applied);
        Assert.Contains("active equipment repair", reassignment.Failure, StringComparison.OrdinalIgnoreCase);
        var removal = world.RemoveBuilding(tailor.InstanceId, tailor.TownId, tailor.HouseholdId);
        Assert.False(removal.Applied);
        Assert.Contains("active equipment repair", removal.Failure, StringComparison.OrdinalIgnoreCase);
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
            state.Society.Society.WorldTick, [], [], [quietBorder], Governance: TownGovernanceState.Create([]),
            Government: TownGovernmentState.Create());
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

    [Fact]
    public void ReusingARemovedBuildingIdKeepsItsOriginalCompletedExpansionAndProductionHistory()
    {
        var state = StartedState("review-expansion-history-reused-id");
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-b");
        state = WithoutBuildingStock(state, house.InstanceId);
        var worker = state.Society.Society.Inhabitants.First(item => item.HouseholdId == house.HouseholdId).Id;
        var expansion = new BuildingExpansionJob("historic-expansion", house.InstanceId, worker,
            house.HouseholdId!, 0, house.Position, house.Position, new(1, 2, 1),
            0, 1, WorldProductionJobState.Completed, ["historic-reservation"]);
        var recipe = state.WorldContent!.Recipes.First(item => !item.IsCrop);
        var production = new WorldProductionJob("historic-production", recipe.CanonicalId,
            house.InstanceId, worker, 0, 1, WorldProductionJobState.Completed, []);
        var replacement = state.WorldContent.Buildings.Single(item => item.LocalId == "tailor-shop-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in replacement.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "replacement-cost-" + cost.ResourceId,
                cost.ResourceId, house.HouseholdId!, cost.Amount);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSimulation = state.WorldSimulation! with
            {
                BuildingExpansions = [expansion],
                ProductionJobs = [production],
            },
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
        var removal = world.RemoveBuilding(house.InstanceId, house.TownId, house.HouseholdId);
        Assert.True(removal.Applied, removal.Failure);
        _ = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var expansionHistory = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(world.WorldSimulation.BuildingExpansions);
        var productionHistory = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(world.WorldSimulation.ProductionJobs);

        BuildingPlacementResult? placement = null;
        foreach (var position in state.Map.Tiles.Select(tile => tile.Position).Where(state.Map.IsBuildable))
        {
            placement = world.PlaceBuilding(house.InstanceId, replacement.CanonicalId, position, house.HouseholdId);
            if (placement.Applied) break;
        }
        Assert.NotNull(placement);
        Assert.True(placement.Applied, placement.Failure);
        Assert.Equal(replacement.CanonicalId, world.WorldSimulation.Buildings.Single(item => item.InstanceId == house.InstanceId).DefinitionId);
        Assert.NotEqual(house.DefinitionId, replacement.CanonicalId);

        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(house.DefinitionId, Assert.Single(restored.WorldSimulation.BuildingExpansions!).DefinitionId);
        Assert.Equal(expansionHistory, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(restored.WorldSimulation.BuildingExpansions));
        Assert.Equal(productionHistory, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(restored.WorldSimulation.ProductionJobs));
        Assert.Contains(restored.WorldSimulation.ProductionJobs, job => job.JobId == production.JobId &&
            job.BuildingInstanceId == house.InstanceId && job.State == WorldProductionJobState.Completed);
        restored.Validate();
    }

    [Fact]
    public void RollingBackARemovedBuildingsExpansionDefinitionIsRefusedBeforeAnyStateChanges()
    {
        var state = StartedState("review-expansion-history-package-rollback");
        var warehouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-warehouse");
        state = WithoutBuildingStock(state, warehouse.InstanceId);
        var worker = state.Towns!.Single(item => item.Id == warehouse.TownId).ResidentIds[0];
        var expansion = new BuildingExpansionJob("historic-expansion", warehouse.InstanceId, worker,
            warehouse.TownId!, 0, warehouse.Position, warehouse.Position, new(2, 3, 1),
            0, 1, WorldProductionJobState.Completed, ["historic-reservation"]);
        state = state with { WorldSimulation = state.WorldSimulation! with { BuildingExpansions = [expansion] } };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
        var removal = world.RemoveBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId);
        Assert.True(removal.Applied, removal.Failure);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var beforePackage = world.ExportState().Content!.Packages.Single(item => item.Manifest.PackageId == WarehouseContent.PackageId);
        var beforeContent = ClankerWorld.Simulation.Content.DeclarativeWorldContentCodec.Encode(world.WorldContent);
        var beforeInventory = world.Society.Inventory;

        Assert.Throws<InvalidOperationException>(() => world.RollbackContent(WarehouseContent.PackageId, "withdraw content"));

        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(beforePackage, world.ExportState().Content!.Packages.Single(item => item.Manifest.PackageId == WarehouseContent.PackageId));
        Assert.Equal(ClankerWorld.Simulation.Content.ContentPackageLifecycle.Active,
            world.ExportState().Content!.Packages.Single(item => item.Manifest.PackageId == WarehouseContent.PackageId).Lifecycle);
        Assert.Equal(beforeContent, ClankerWorld.Simulation.Content.DeclarativeWorldContentCodec.Encode(world.WorldContent));
        Assert.Same(beforeInventory, world.Society.Inventory);
        Assert.Contains(world.WorldContent.Buildings, item => item.CanonicalId == warehouse.DefinitionId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new IdleProvider());
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    [Fact]
    public void DuplicateFirstTownIsRefusedAsDamagedDataAndItsFileIsPreserved()
    {
        var state = StartedState("review-duplicate-first-town");
        var town = Assert.Single(state.Towns!);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with { Towns = [town, town] }));
        var document = System.Text.Json.Nodes.JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var towns = document["state"]!["towns"]!.AsArray();
        towns.Add(towns[0]!.DeepClone());
        var damaged = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
        var directory = Directory.CreateTempSubdirectory("duplicate-town-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.json");
            File.WriteAllBytes(path, damaged);
            var file = new PrivateWorldStateFile(path);
            Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(state.WorldSeed));
            Assert.Equal(damaged, File.ReadAllBytes(path));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static PrivateWorldRuntimeState WithoutBuildingStock(PrivateWorldRuntimeState state, string buildingId)
    {
        var removedLotIds = state.Society.Society.Inventory.Lots.Where(lot =>
            lot.StorageBuildingId == buildingId || lot.DeliveryBuildingId == buildingId)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !removedLotIds.Contains(lot.Id)).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations.Where(item => !removedLotIds.Contains(item.LotId)).ToArray(),
        };
        return state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
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
