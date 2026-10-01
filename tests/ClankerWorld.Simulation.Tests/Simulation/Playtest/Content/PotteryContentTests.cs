using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PotteryContentTests
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void PotteryManifestDefinesHouseMadeStoragePotAndWaterJug()
    {
        var preview = PrivateWorldRuntime.PreviewWorldContent(
            [StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(), PotteryContent.Create()],
            [PotteryContent.PackageId]);

        Assert.True(preview.IsValid, preview.Diagnostic);
        var pot = Assert.Single(preview.WorldContent.Recipes, recipe => recipe.LocalId == "storage-pot");
        var jug = Assert.Single(preview.WorldContent.Recipes, recipe => recipe.LocalId == "water-jug");
        Assert.Equal(HouseContent.House1x1().CanonicalId, pot.WorkstationBuildingId);
        Assert.Equal(HouseContent.House1x1().CanonicalId, jug.WorkstationBuildingId);
        Assert.Equal([new ContentQuantity("clay", 2), new ContentQuantity("wood", 1)], pot.Inputs);
        Assert.Equal([new ContentQuantity("clay", 2), new ContentQuantity("wood", 1)], jug.Inputs);
        Assert.Equal([new ContentQuantity(InventoryContainerRules.StoragePot, 1)], pot.Outputs);
        Assert.Equal([new ContentQuantity(InventoryContainerRules.WaterJug, 1)], jug.Outputs);
    }

    [Fact]
    public async Task GeneratedFirstTownDigsClayAtReachableFreshwaterBankAndMakesBothVesselsAtItsHouse()
    {
        using var setup = NormalPathWorld.CreateGenerated("pottery-bank-to-house", _ => new IdleProvider());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await setup.AdvanceOneTickAsync()).Advanced);

        var initial = setup.ExportState();
        var clay = Assert.Single(initial.Map.Resources, resource => resource.Id == "settlement-clay");
        Assert.Equal("clay", clay.Kind);
        Assert.False(clay.IsRenewable);
        Assert.True(initial.Map.IsReachableFromCampOnFoot(clay.Position));
        Assert.Equal(WaterKind.Land, initial.Map.HydrologyAt(clay.Position));
        Assert.Contains(CardinalNeighbors(initial.Map, clay.Position), point =>
            initial.Map.HydrologyAt(point) is WaterKind.River or WaterKind.Lake);
        Assert.Equal(16, initial.WorldSystems!.Ecology.GetResource(clay.Id).Quantity);

        var householdId = initial.Society.Society.Inhabitants.First(person => person.HouseholdId is not null).HouseholdId!;
        var actor = initial.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == householdId && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var houseDefinition = setup.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var house = setup.WorldSimulation.Buildings.SingleOrDefault(building =>
            building.DefinitionId == houseDefinition.CanonicalId && building.HouseholdId == householdId);
        if (house is null)
        {
            var buildingTiles = setup.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(setup.WorldContent.Buildings.Single(definition =>
                    definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
            var site = initial.Map.Tiles.Select(tile => tile.Position).First(point =>
                initial.Map.IsBuildable(point) && initial.Map.IsReachableFromCampOnFoot(point) &&
                !buildingTiles.Contains(point) && !setup.RoadTiles.Contains(point) &&
                !initial.Map.CampObjects.Any(item => item.Position == point) &&
                !initial.Map.Resources.Any(item => item.Position == point) &&
                !initial.Inhabitants.Any(person => person.Position == point));
            var placement = setup.PlaceBuilding("pottery-house", houseDefinition.CanonicalId, site, householdId);
            Assert.True(placement.Applied, placement.Failure);
            house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "pottery-house");
        }

        var supplier = new PrefixCandidateProvider("supply_workstation:");
        var supplyState = setup.ExportState();
        supplyState = supplyState with
        {
            Inhabitants = supplyState.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    HungerBasisPoints = 10_000,
                    Survival = person.Survival is { } condition
                        ? condition with { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 }
                        : null,
                }
                : person).ToArray(),
        };

        var currentInventory = supplyState.Society.Society.Inventory;
        var adult = supplyState.Inhabitants.Single(person => person.InhabitantId == actor);
        var freeCapacity = PersonalEquipmentRules.FreeCapacity(currentInventory, actor, adult.Equipment);
        if (freeCapacity > 0)
            currentInventory = InventoryFixture.AddLot(currentInventory, "test-workstation-full-load", "test_load",
                actor, freeCapacity, currentInventory.WorldTick);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(currentInventory, actor, adult.Equipment));
        var fullLoadState = supplyState with
        {
            Society = supplyState.Society with
            {
                Society = supplyState.Society.Society with { Inventory = currentInventory },
            },
        };
        var fullLoadCandidates = await ObservePersistedCandidates(fullLoadState, actor);
        Assert.DoesNotContain("supply_workstation:clay", fullLoadCandidates);

        var fullStorageInventory = supplyState.Society.Society.Inventory;
        var onSiteQuantity = fullStorageInventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId)
            .Sum(lot => lot.Quantity);
        var houseStorageRoom = Math.Max(0, BuildingStorageRules.UnitsPerTile - onSiteQuantity);
        if (houseStorageRoom > 0)
            fullStorageInventory = InventoryFixture.AddLot(fullStorageInventory, "test-workstation-full-storage", "test_storage",
                householdId, houseStorageRoom, fullStorageInventory.WorldTick, storageBuildingId: house.InstanceId);
        var fullStorageState = supplyState with
        {
            Society = supplyState.Society with
            {
                Society = supplyState.Society.Society with { Inventory = fullStorageInventory },
            },
        };
        var fullStorageCandidates = await ObservePersistedCandidates(fullStorageState, actor);
        Assert.DoesNotContain("supply_workstation:clay", fullStorageCandidates);

        using var supplying = PrivateWorldRuntime.Restore(supplyState, id => id == actor
            ? supplier : new IdleProvider());
        for (var tick = 0; tick < 160 &&
             HouseQuantity(supplying, householdId, house.InstanceId, "clay") < 4; tick++)
            Assert.True((await supplying.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(supplier.OfferedWorkstationCandidates, id => id == "supply_workstation:clay");
        Assert.Contains(supplying.ExportState().Events, item => item.Kind == "material_gathered" &&
            item.Detail.StartsWith(actor + ":clay:", StringComparison.Ordinal));
        Assert.Contains(supplying.ExportState().Events, item => item.Kind == "workstation_supplied" &&
            item.Detail.EndsWith(":" + house.InstanceId, StringComparison.Ordinal));
        var supplyEvents = supplying.ExportState().Events.Where(item =>
            item.Kind is "material_gathered" or "workstation_supplied").Select(item => item.Detail).ToArray();
        Assert.True(HouseQuantity(supplying, householdId, house.InstanceId, "clay") == 4,
            string.Join(" | ", supplyEvents));
        Assert.Equal(15, supplying.WorldSystems!.Ecology.GetResource(clay.Id).Quantity);

        var productionState = supplying.ExportState();
        var noJugState = SetActorCondition(productionState, actor, 10_000, house.Position);
        var noJugCandidates = await ObservePersistedCandidates(noJugState, actor);
        Assert.Empty(ContainerActionCandidates(noJugCandidates));

        var otherAdult = productionState.Society.Society.Inhabitants.First(person => person.HouseholdId == householdId &&
            person.Id != actor && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var foreignJugInventory = InventoryFixture.AddLot(productionState.Society.Society.Inventory,
            "test-foreign-carried-jug", InventoryContainerRules.WaterJug, otherAdult, 1,
            productionState.Society.Society.WorldTick);
        var foreignJugState = SetActorCondition(productionState with
        {
            Society = productionState.Society with
            {
                Society = productionState.Society.Society with { Inventory = foreignJugInventory },
            },
        }, actor, 10_000, house.Position);
        var foreignJugCandidates = await ObservePersistedCandidates(foreignJugState, actor);
        Assert.Empty(ContainerActionCandidates(foreignJugCandidates));

        var freshwaterShores = FreshWaterShorePoints(productionState.Map);
        var occupiedTiles = productionState.Inhabitants.Select(person => person.Position)
            .Concat(setup.WorldSimulation.Buildings.Select(building => building.Position))
            .Concat(setup.RoadTiles)
            .Concat(productionState.Map.CampObjects.Select(item => item.Position))
            .Concat(productionState.Map.Resources.Select(item => item.Position))
            .ToHashSet();
        var blockedShoreOrigins = productionState.Map.Tiles.Select(tile => tile.Position)
            .Where(point => !occupiedTiles.Contains(point) && productionState.Map.IsPassable(point) &&
                productionState.Map.HydrologyAt(point) == WaterKind.Land)
            .Where(point => freshwaterShores.All(shore => !productionState.Map.IsReachableOnFoot(point, shore)))
            .ToArray();
        Assert.NotEmpty(freshwaterShores);
        Assert.NotEmpty(blockedShoreOrigins);
        var jugAtBlockedShore = InventoryFixture.AddLot(productionState.Society.Society.Inventory,
            "test-jug-with-no-reachable-shore", InventoryContainerRules.WaterJug, actor, 1,
            productionState.Society.Society.WorldTick);
        var blockedShoreState = SetActorCondition(productionState with
        {
            Society = productionState.Society with
            {
                Society = productionState.Society.Society with { Inventory = jugAtBlockedShore },
            },
        }, actor, 10_000, blockedShoreOrigins[0]);
        var blockedShoreCandidates = await ObservePersistedCandidates(blockedShoreState, actor);
        Assert.DoesNotContain(blockedShoreCandidates,
            id => id.StartsWith("fill_water_jug:", StringComparison.Ordinal));

        var woodStock = InventoryFixture.AddLot(productionState.Society.Society.Inventory,
            "test-house-pottery-wood", "wood", householdId, 4,
            productionState.Society.Society.WorldTick, storageBuildingId: house.InstanceId);
        productionState = productionState with
        {
            Society = productionState.Society with
            {
                Society = productionState.Society.Society with { Inventory = woodStock },
            },
        };
        foreach (var recipeLocalId in new[] { "storage-pot", "water-jug" })
        {
            using var current = PrivateWorldRuntime.Restore(productionState, _ => new IdleProvider());
            var recipe = current.WorldContent.Recipes.Single(item => item.LocalId == recipeLocalId);
            var started = current.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
            Assert.True(started.Applied, started.Failure);
            var checkpoint = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(current.ExportState()));
            using var resumed = PrivateWorldRuntime.Restore(checkpoint, _ => new IdleProvider());
            for (var tick = 0; tick < recipe.DurationTicks; tick++)
                Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(WorldProductionJobState.Completed,
                resumed.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId).State);
            Assert.Contains(resumed.Society.Inventory.Lots, lot => lot.OwnerId == householdId &&
                lot.StorageBuildingId == house.InstanceId && lot.ItemKind == recipe.Outputs[0].ResourceId &&
                lot.Quantity == 1);
            productionState = resumed.ExportState();
        }

        var storedJug = productionState.Society.Society.Inventory.Lots.Single(lot =>
            lot.ItemKind == InventoryContainerRules.WaterJug);
        var lockInventory = InventoryFixture.AddLot(productionState.Society.Society.Inventory,
            "test-reserved-water", InventoryContainerRules.FreshWater, householdId, 1,
            productionState.Society.Society.WorldTick, containerLotId: storedJug.Id,
            storageBuildingId: house.InstanceId);
        lockInventory = InventoryFixture.Reserve(lockInventory, "test-water-jug-lock", householdId,
            "test-reserved-water", 1, "keep this vessel family in place", productionState.Society.Society.WorldTick + 100);
        var lockedJugState = SetActorCondition(productionState with
        {
            Society = productionState.Society with
            {
                Society = productionState.Society.Society with { Inventory = lockInventory },
            },
        }, actor, 10_000, house.Position);
        var lockedJugCandidates = await ObservePersistedCandidates(lockedJugState, actor);
        Assert.DoesNotContain("collect_water_jug", lockedJugCandidates);
        Assert.DoesNotContain(lockedJugCandidates, id => id.StartsWith("fill_water_jug:", StringComparison.Ordinal));

        var jugId = productionState.Society.Society.Inventory.Lots.Single(lot =>
            lot.ItemKind == InventoryContainerRules.WaterJug).Id;
        var collector = new PrefixCandidateProvider("collect_water_jug");
        // The generated-world supplier may have left this actor on safe_idle
        // with a recent cognition context. Reset that context so the controlled
        // collector provider receives a fresh choice without waiting for the
        // ordinary safe-idle reevaluation interval.
        var collectionStartState = productionState with
        {
            Inhabitants = productionState.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { LastDecisionContext = null }
                : person).ToArray(),
        };
        using var collecting = PrivateWorldRuntime.Restore(collectionStartState, id => id == actor
            ? collector : new IdleProvider());
        for (var tick = 0; tick < 80 && collecting.Society.Inventory.GetLot(jugId).OwnerId != actor; tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(collector.OfferedCandidates, id => id == "collect_water_jug");
        Assert.Equal(actor, collecting.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Null(collecting.Society.Inventory.GetLot(jugId).StorageBuildingId);
        Assert.Contains(collecting.ExportState().Events, item => item.Kind == "water_jug_collected" &&
            item.Detail.StartsWith(actor + ":" + jugId, StringComparison.Ordinal));

        var heldJugState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState()));
        var waterLoop = new WaterLoopProvider();
        using var filling = PrivateWorldRuntime.Restore(heldJugState, id => id == actor
            ? waterLoop : new IdleProvider());
        for (var tick = 0; tick < 500 &&
             filling.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == jugId).Sum(lot => lot.Quantity) <
             InventoryContainerRules.WaterJugCapacity; tick++)
            Assert.True((await filling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(InventoryContainerRules.WaterJugCapacity,
            filling.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == jugId).Sum(lot => lot.Quantity));
        Assert.Equal(actor, filling.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Null(filling.Society.Inventory.GetLot(jugId).StorageBuildingId);

        var fullJugState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(filling.ExportState()));
        var fullJugCandidates = await ObservePersistedCandidates(fullJugState, actor);
        Assert.DoesNotContain(fullJugCandidates,
            id => id.StartsWith("fill_water_jug:", StringComparison.Ordinal));

        for (var tick = 0; tick < 500 && filling.Society.Inventory.GetLot(jugId).StorageBuildingId != house.InstanceId; tick++)
            Assert.True((await filling.AdvanceOneTickAsync()).Advanced);
        var filledJug = filling.Society.Inventory.GetLot(jugId);
        var water = Assert.Single(filling.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId);
        Assert.Equal(householdId, filledJug.OwnerId);
        Assert.Equal(house.InstanceId, filledJug.StorageBuildingId);
        Assert.Equal(InventoryContainerRules.FreshWater, water.ItemKind);
        Assert.Equal(InventoryContainerRules.WaterJugCapacity, water.Quantity);
        Assert.Equal(house.InstanceId, water.StorageBuildingId);
        var fillEvent = Assert.Single(filling.ExportState().Events, item => item.Kind == "water_jug_filled");
        var shoreParts = fillEvent.Detail[(fillEvent.Detail.LastIndexOf(':') + 1)..].Split(',');
        var fillPoint = new GridPoint(int.Parse(shoreParts[0], CultureInfo.InvariantCulture),
            int.Parse(shoreParts[1], CultureInfo.InvariantCulture));
        var filledMap = filling.ExportState().Map;
        Assert.Equal(WaterKind.Land, filledMap.HydrologyAt(fillPoint));
        Assert.Contains(CardinalNeighbors(filledMap, fillPoint), point =>
            filledMap.HydrologyAt(point) is WaterKind.River or WaterKind.Lake);

        using var waterConsumer = PrivateWorldRuntime.Restore(filling.ExportState(), _ => new IdleProvider());
        var waterRecipePackage = WaterConsumerFixture();
        waterConsumer.ProposeContent(waterRecipePackage);
        var resolution = waterConsumer.ResolveContent(waterRecipePackage.PackageId);
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        waterConsumer.ValidateContent(waterRecipePackage.PackageId, resolution);
        waterConsumer.ApproveContent(waterRecipePackage.PackageId);
        waterConsumer.StageContent(waterRecipePackage.PackageId);
        Assert.True((await waterConsumer.AdvanceOneTickAsync()).Advanced);
        var waterRecipe = waterConsumer.WorldContent.Recipes.Single(recipe => recipe.LocalId == "water-input-fixture");
        var waterLotId = Assert.Single(waterConsumer.Society.Inventory.Lots,
            lot => lot.ContainerLotId == jugId).Id;
        var waterJob = waterConsumer.StartProduction(waterRecipe.CanonicalId, house.InstanceId, actor);
        Assert.True(waterJob.Applied, waterJob.Failure);
        var reservedState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waterConsumer.ExportState()));
        Assert.Contains(reservedState.WorldSimulation!.ProductionJobs.Single(job => job.JobId == waterJob.JobId)
            .InputReservationIds, reservationId =>
                reservedState.Society.Society.Inventory.GetReservation(reservationId).LotId == waterLotId);
        using var waterResumed = PrivateWorldRuntime.Restore(reservedState, _ => new IdleProvider());
        for (var tick = 0; tick < waterRecipe.DurationTicks; tick++)
            Assert.True((await waterResumed.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(waterResumed.Society.Inventory.Lots, lot => lot.Id == waterLotId);
        Assert.Equal(householdId, waterResumed.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Equal(house.InstanceId, waterResumed.Society.Inventory.GetLot(jugId).StorageBuildingId);
        Assert.DoesNotContain(waterResumed.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId);

        var reloadEmptyJug = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waterResumed.ExportState()));
        var refillProvider = new WaterLoopProvider();
        using var refilling = PrivateWorldRuntime.Restore(reloadEmptyJug, id => id == actor
            ? refillProvider : new IdleProvider());
        for (var tick = 0; tick < 500 &&
             (!refilling.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == jugId) ||
              refilling.Society.Inventory.GetLot(jugId).StorageBuildingId != house.InstanceId); tick++)
            Assert.True((await refilling.AdvanceOneTickAsync()).Advanced);
        var refillCandidates = refillProvider.OfferedCandidates.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var refillProgress = refilling.ExportState();
        Assert.True(refillCandidates.Any(id => id.StartsWith("fill_water_jug:", StringComparison.Ordinal)),
            $"Offered [{string.Join(", ", refillCandidates)}] at tick {refillProgress.Society.Society.WorldTick}; " +
            $"actor status {refillProgress.Society.Society.Inhabitants.Single(person => person.Id == actor).Status}, " +
            $"position {refillProgress.Inhabitants.Single(person => person.InhabitantId == actor).Position}");
        Assert.Contains(refilling.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId &&
            lot.ItemKind == InventoryContainerRules.FreshWater && lot.Quantity == InventoryContainerRules.WaterJugCapacity);
        Assert.Equal(householdId, refilling.Society.Inventory.GetLot(jugId).OwnerId);

        var potId = refilling.Society.Inventory.Lots.Single(lot =>
            lot.ItemKind == InventoryContainerRules.StoragePot).Id;
        var foodState = refilling.ExportState();
        var foodInventory = foodState.Society.Society.Inventory with
        {
            Lots = foodState.Society.Society.Inventory.Lots.Where(lot =>
                    !(lot.OwnerId == householdId && lot.StorageBuildingId == house.InstanceId &&
                      lot.ContainerLotId is null && InventoryContainerRules.IsFood(lot.ItemKind)))
                .Concat([new InventoryLot("test-pot-food", "berries", householdId, 3, 10_000, 10_000,
                    foodState.Society.Society.WorldTick, StorageBuildingId: house.InstanceId)])
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
        };
        foodState = foodState with
        {
            Society = foodState.Society with
            {
                Society = foodState.Society.Society with { Inventory = foodInventory },
            },
        };
        var houseCapacity = BuildingStorageRules.Capacity(
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == house.DefinitionId), house)!.Value;
        var storedBeforeFiller = foodInventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId)
            .Sum(lot => lot.Quantity);
        foodInventory = InventoryFixture.AddLot(foodInventory, "test-pot-capacity-filler", "wood", householdId,
            houseCapacity - storedBeforeFiller, storageBuildingId: house.InstanceId);
        foodState = foodState with
        {
            Society = foodState.Society with
            {
                Society = foodState.Society.Society with { Inventory = foodInventory },
            },
        };
        Assert.Equal(houseCapacity, foodInventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId)
            .Sum(lot => lot.Quantity));
        Assert.Equal(3, foodInventory.Lots.Where(lot => lot.Id == "test-pot-food").Sum(lot => lot.Quantity));
        foodState = SetActorCondition(foodState, actor, 10_000, house.Position);

        var carriedFoodInventory = foodState.Society.Society.Inventory with
        {
            Lots = foodState.Society.Society.Inventory.Lots.Where(lot => lot.Id != "test-pot-food")
                .Concat([
                    new InventoryLot("test-pot-storage-replacement", "wood", householdId, 3, 10_000, 10_000,
                        foodState.Society.Society.WorldTick, StorageBuildingId: house.InstanceId),
                    new InventoryLot("test-carried-pot-food", "berries", actor, 2, 10_000, 10_000,
                        foodState.Society.Society.WorldTick),
                ])
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
        };
        var carriedFoodState = foodState with
        {
            Society = foodState.Society with
            {
                Society = foodState.Society.Society with { Inventory = carriedFoodInventory },
            },
        };
        var carriedFoodProvider = new PrefixCandidateProvider("store_food_in_pot");
        using var storingCarriedFood = PrivateWorldRuntime.Restore(carriedFoodState, id => id == actor
            ? carriedFoodProvider : new IdleProvider());
        for (var tick = 0; tick < 5 && carriedFoodProvider.OfferedCandidates.IsEmpty; tick++)
            Assert.True((await storingCarriedFood.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(carriedFoodProvider.OfferedCandidates, id => id == "safe_idle");
        Assert.DoesNotContain(carriedFoodProvider.OfferedCandidates, id => id == "store_food_in_pot");
        Assert.Equal(houseCapacity, storingCarriedFood.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        Assert.Contains(storingCarriedFood.Society.Inventory.Lots, lot => lot.Id == "test-carried-pot-food" &&
            lot.OwnerId == actor && lot.Quantity == 2 && lot.ContainerLotId is null);

        var foodStorageProvider = new PrefixCandidateProvider("store_food_in_pot");
        using var storingFood = PrivateWorldRuntime.Restore(foodState, id => id == actor
            ? foodStorageProvider : new IdleProvider());
        for (var tick = 0; tick < 5 &&
             !storingFood.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == potId); tick++)
            Assert.True((await storingFood.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(foodStorageProvider.OfferedCandidates, id => id == "store_food_in_pot");
        var storedFood = Assert.Single(storingFood.Society.Inventory.Lots,
            lot => lot.ContainerLotId == potId);
        Assert.Equal("berries", storedFood.ItemKind);
        Assert.Equal(3, storedFood.Quantity);
        Assert.Equal(3, storingFood.Society.Inventory.Lots.Where(lot => lot.Id == "test-pot-food")
            .Sum(lot => lot.Quantity));
        Assert.Equal(householdId, storingFood.Society.Inventory.GetLot(potId).OwnerId);
        Assert.Equal(houseCapacity, storingFood.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));

        var foodReload = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(storingFood.ExportState()));
        var reloadedStoredFood = Assert.Single(foodReload.Society.Society.Inventory.Lots,
            lot => lot.Id == "test-pot-food");
        Assert.Equal(3, reloadedStoredFood.Quantity);
        Assert.Equal(potId, reloadedStoredFood.ContainerLotId);
        Assert.Equal(householdId, reloadedStoredFood.OwnerId);
        Assert.Equal(houseCapacity, foodReload.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        foodReload = SetActorCondition(foodReload, actor, 0, house.Position);
        var inventoryWithoutCarriedFood = foodReload.Society.Society.Inventory with
        {
            Lots = foodReload.Society.Society.Inventory.Lots.Where(lot =>
                    !(lot.OwnerId == actor && lot.ContainerLotId is null && lot.DeliveryBuildingId is null &&
                      InventoryContainerRules.IsFood(lot.ItemKind)))
                .Append(new InventoryLot("ground-owned-berries", "berries", actor, 2, 10_000, 10_000,
                    foodReload.Society.Society.WorldTick,
                    GroundPosition: new InventoryGroundPosition(house.Position.X, house.Position.Y)))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
        };
        foodReload = foodReload with
        {
            Society = foodReload.Society with
            {
                Society = foodReload.Society.Society with { Inventory = inventoryWithoutCarriedFood },
            },
        };
        var foodTaker = new PrefixCandidateProvider("take_food_from_pot");
        using var takingFood = PrivateWorldRuntime.Restore(foodReload, id => id == actor
            ? foodTaker : new IdleProvider());
        for (var tick = 0; tick < 30 &&
             !takingFood.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "berries" &&
                 lot.Quantity == 1 && lot.ContainerLotId is null); tick++)
            Assert.True((await takingFood.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(foodTaker.OfferedCandidates, id => id == "take_food_from_pot");
        Assert.DoesNotContain(foodTaker.OfferedCandidates, id => id == "store_food_in_pot" ||
            id == "collect_water_jug" || id == "return_water_jug" ||
            id.StartsWith("fill_water_jug:", StringComparison.Ordinal));
        var remainingPotFood = Assert.Single(takingFood.Society.Inventory.Lots,
            lot => lot.ContainerLotId == potId);
        Assert.Equal(2, remainingPotFood.Quantity);
        Assert.Equal(householdId, takingFood.Society.Inventory.GetLot(potId).OwnerId);
        Assert.Contains(takingFood.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "berries" && lot.Quantity == 1 && lot.ContainerLotId is null);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task WorkstationWaterSupplyMovesOnlyContainerFamiliesWithinTheHaulLoad(int waterQuantity,
        bool familyFits)
    {
        const string householdId = "household:camp-alpha";
        const string sourceHouseId = "first-town-house-a";
        const string jugId = "supply-test-jug";
        const string waterId = "supply-test-water";
        var (initial, _) = TailorTestWorld.Create($"container-workstation-supply-{waterQuantity}", 0);
        var actor = initial.Society.Society.Inhabitants.First(person => person.HouseholdId == householdId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        using var setup = PrivateWorldRuntime.Restore(initial, _ => new IdleProvider());
        var package = WaterWorkstationFixture(waterQuantity);
        setup.ProposeContent(package);
        var resolution = setup.ResolveContent(package.PackageId);
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        setup.ValidateContent(package.PackageId, resolution);
        setup.ApproveContent(package.PackageId);
        setup.StageContent(package.PackageId);
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);

        var shopDefinition = setup.WorldContent.Buildings.Single(item => item.LocalId == "test-water-workstation");
        const string shopId = "test-water-workstation";
        var sourceHouse = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == sourceHouseId);
        var placed = Enumerable.Range(-4, 9).SelectMany(dy => Enumerable.Range(-4, 9)
                .Select(dx => new GridPoint(sourceHouse.Position.X + dx, sourceHouse.Position.Y + dy)))
            .OrderBy(point => Math.Abs(point.X - sourceHouse.Position.X) + Math.Abs(point.Y - sourceHouse.Position.Y))
            .Select(point => setup.PlaceBuilding("test-water-workstation", shopDefinition.CanonicalId, point, householdId))
            .FirstOrDefault(result => result.Applied);
        Assert.True(placed?.Applied, "A reachable test workstation should fit beside the household House.");

        var state = SetActorCondition(setup.ExportState(), actor, 10_000, sourceHouse.Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, jugId,
            InventoryContainerRules.WaterJug, householdId, 1, state.Society.Society.WorldTick,
            storageBuildingId: sourceHouseId);
        foreach (var building in setup.WorldSimulation.Buildings.Where(building =>
                     building.HouseholdId == householdId && building.InstanceId != shopId))
        {
            var definition = setup.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            foreach (var input in setup.WorldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == definition.CanonicalId)
                         .SelectMany(recipe => recipe.Inputs).GroupBy(item => item.ResourceId))
                inventory = InventoryFixture.AddLot(inventory,
                    $"supply-fixture-buffer:{building.InstanceId}:{input.Key}", input.Key, householdId,
                    input.Max(item => item.Amount) * 2, state.Society.Society.WorldTick,
                    storageBuildingId: building.InstanceId);
        }
        inventory = InventoryFixture.AddLot(inventory, waterId, InventoryContainerRules.FreshWater,
            householdId, waterQuantity, state.Society.Society.WorldTick, containerLotId: jugId,
            storageBuildingId: sourceHouseId);
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };

        var provider = new ContainerSupplyProvider();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor
            ? provider : new IdleProvider());
        var attempts = familyFits ? 80 : 1;
        for (var tick = 0; tick < attempts && (familyFits
                 ? world.Society.Inventory.GetLot(jugId).StorageBuildingId != shopId
                 : !provider.OfferedCandidates.Contains("supply_workstation:fresh_water")); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        if (!familyFits)
        {
            Assert.DoesNotContain(provider.OfferedCandidates, id => id == "supply_workstation:fresh_water");
            Assert.Equal(sourceHouseId, world.Society.Inventory.GetLot(jugId).StorageBuildingId);
            Assert.Equal(jugId, world.Society.Inventory.GetLot(waterId).ContainerLotId);
            Assert.Equal(waterQuantity, world.Society.Inventory.GetLot(waterId).Quantity);
            world.Validate();
            return;
        }

        var actorState = world.Inhabitants.Single(person => person.InhabitantId == actor);
        var actorLots = world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor)
            .Select(lot => $"{lot.Id}:{lot.ItemKind}:{lot.Quantity}:storage={lot.StorageBuildingId}:delivery={lot.DeliveryBuildingId}");
        Assert.True(provider.OfferedCandidates.Contains("supply_workstation:fresh_water"),
            $"Actor free capacity {PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor, actorState.Equipment)}; " +
            $"actor lots [{string.Join(" | ", actorLots)}]; " +
            $"actor hunger {actorState.HungerBasisPoints}; candidates [{string.Join(" | ", provider.OfferedCandidates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}]; " +
            $"source jug {world.Society.Inventory.GetLot(jugId)}; recipes " +
            $"[{string.Join(" | ", world.WorldContent.Recipes.Where(item => item.WorkstationBuildingId == shopDefinition.CanonicalId).Select(item => item.LocalId))}]");
        Assert.Equal(householdId, world.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Equal(shopId, world.Society.Inventory.GetLot(jugId).StorageBuildingId);
        Assert.Equal(shopId, world.Society.Inventory.GetLot(waterId).StorageBuildingId);
        Assert.Equal(jugId, world.Society.Inventory.GetLot(waterId).ContainerLotId);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.Id == jugId || lot.ContainerLotId == jugId)
            .Sum(lot => lot.Quantity));

        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "water-input-fixture");
        var started = world.StartProduction(recipe.CanonicalId, shopId, actor);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < recipe.DurationTicks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(householdId, world.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Equal(shopId, world.Society.Inventory.GetLot(jugId).StorageBuildingId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == waterId);
        world.Validate();
    }

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    public async Task PotFoodAndReturningJugsLeaveRoomForInboundHouseDeliveries(bool returningJug,
        int incomingQuantity, bool fits)
    {
        using var setup = NormalPathWorld.CreateGenerated("container-inbound-room", _ => new IdleProvider());
        for (var tick = 0; tick < 8; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        const string householdId = "household:camp-alpha";
        var house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var adults = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == householdId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).ToArray();
        var actor = adults[0].Id;
        var deliverer = adults[1].Id;
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                !(lot.OwnerId == householdId && InventoryContainerRules.IsFood(lot.ItemKind))).ToArray(),
        };
        if (returningJug)
        {
            inventory = InventoryFixture.AddLot(inventory, "inbound-test-vessel", InventoryContainerRules.WaterJug, actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "inbound-test-water", InventoryContainerRules.FreshWater,
                actor, InventoryContainerRules.WaterJugCapacity, containerLotId: "inbound-test-vessel");
        }
        else
        {
            inventory = InventoryFixture.AddLot(inventory, "inbound-test-vessel", InventoryContainerRules.StoragePot,
                householdId, 1, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "inbound-test-food", "food", actor, 2);
        }
        var room = returningJug ? 1 + InventoryContainerRules.WaterJugCapacity : 1;
        var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "inbound-test-storage", "test_storage", householdId,
            BuildingStorageRules.UnitsPerTile - stored - room, storageBuildingId: house.InstanceId);
        if (incomingQuantity > 0)
        {
            inventory = InventoryFixture.AddLot(inventory, "inbound-test-delivery", "wood", deliverer, incomingQuantity);
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == "inbound-test-delivery"
                    ? lot with { DeliveryBuildingId = house.InstanceId } : lot).ToArray(),
            };
        }
        var totalBefore = inventory.Lots.Sum(lot => lot.Quantity);
        state = SetActorCondition(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        }, actor, 10_000, house.Position) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, HungerBasisPoints = 10_000, Equipment = null, LastDecisionContext = null }
                : person).ToArray(),
        };
        var action = returningJug ? "return_water_jug" : "store_food_in_pot";
        var eventKind = returningJug ? "water_jug_returned" : "food_stored_in_pot";
        var provider = new PrefixCandidateProvider(action);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? provider : new IdleProvider());
        for (var tick = 0; tick < 10 && (fits
                 ? !world.ExportState().Events.Any(item => item.Kind == eventKind)
                 : provider.OfferedCandidates.IsEmpty); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(fits, provider.OfferedCandidates.Contains(action));
        Assert.Equal(fits, world.ExportState().Events.Any(item => item.Kind == eventKind));
        Assert.Equal(totalBefore, world.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.Equal(BuildingStorageRules.UnitsPerTile - (fits ? 0 : room), world.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        if (returningJug)
        {
            Assert.Equal(fits ? householdId : actor, world.Society.Inventory.GetLot("inbound-test-vessel").OwnerId);
            Assert.Equal("inbound-test-vessel", world.Society.Inventory.GetLot("inbound-test-water").ContainerLotId);
        }
        else
        {
            Assert.Equal(fits ? 1 : 2, world.Society.Inventory.GetLot("inbound-test-food").Quantity);
            Assert.Equal(fits ? 1 : 0, world.Society.Inventory.Lots.Where(lot =>
                lot.ContainerLotId == "inbound-test-vessel").Sum(lot => lot.Quantity));
        }
        if (incomingQuantity > 0)
        {
            var incoming = world.Society.Inventory.GetLot("inbound-test-delivery");
            Assert.Equal(inventory.GetLot(incoming.Id) with { LastProcessedTick = incoming.LastProcessedTick }, incoming);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        resumed.Validate();
    }

    [Fact]
    public async Task FoodPotStorageIsNotOfferedAcrossDisconnectedLand()
    {
        using var setup = NormalPathWorld.CreateGenerated("pot-storage-unreachable", _ => new IdleProvider());
        for (var tick = 0; tick < 8; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        const string householdId = "household:camp-alpha";
        var house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == householdId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var origin = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsPassable(point) &&
            !state.Map.IsReachableOnFoot(point, house.Position) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "unreachable-test-pot",
            InventoryContainerRules.StoragePot, householdId, 1, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "unreachable-test-food", "food", householdId, 2,
            storageBuildingId: house.InstanceId);
        state = SetActorCondition(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        }, actor, 10_000, origin);
        var candidates = await ObservePersistedCandidates(state, actor);
        Assert.DoesNotContain("store_food_in_pot", candidates);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task CollectingAStoredJugLeavesCarrySpaceForWaterAcrossReload(int storedWater, bool fits)
    {
        using var setup = NormalPathWorld.CreateGenerated("jug-leaves-water-room", _ => new IdleProvider());
        for (var tick = 0; tick < 8; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        const string householdId = "household:camp-alpha";
        const string jugId = "last-slot-jug";
        var house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == householdId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, jugId, InventoryContainerRules.WaterJug,
            householdId, 1, storageBuildingId: house.InstanceId);
        if (storedWater > 0)
            inventory = InventoryFixture.AddLot(inventory, "last-slot-water", InventoryContainerRules.FreshWater,
                householdId, storedWater, containerLotId: jugId, storageBuildingId: house.InstanceId);
        var familyQuantity = 1 + storedWater;
        var capacity = PersonalEquipmentRules.Capacity(inventory, actor, null);
        inventory = InventoryFixture.AddLot(inventory, "last-slot-ballast", "test_cargo", actor,
            capacity - familyQuantity - (fits ? 1 : 0));
        Assert.Equal(familyQuantity + (fits ? 1 : 0), PersonalEquipmentRules.FreeCapacity(inventory, actor, null));
        state = SetActorCondition(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        }, actor, 10_000, house.Position);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = null, LastDecisionContext = null } : person).ToArray(),
        };
        var beforeTotal = inventory.Lots.Sum(lot => lot.Quantity);
        var provider = new WaterLoopProvider();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? provider : new IdleProvider());
        for (var tick = 0; tick < 10 && (fits
                 ? world.Society.Inventory.GetLot(jugId).OwnerId != actor
                 : provider.OfferedCandidates.IsEmpty); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(fits, provider.OfferedCandidates.Contains("collect_water_jug"));
        Assert.Equal(fits ? actor : householdId, world.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Equal(beforeTotal, world.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.Equal(storedWater, world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == jugId)
            .Sum(lot => lot.Quantity));
        if (!fits)
        {
            Assert.DoesNotContain(world.ExportState().Events, item =>
                item.Kind is "water_jug_collected" or "water_jug_returned" or "water_jug_filled");
            Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot(jugId).StorageBuildingId);
            world.Validate();
            return;
        }

        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => id == actor ? provider : new IdleProvider());
        for (var tick = 0; tick < 500 && !resumed.ExportState().Events.Any(item => item.Kind == "water_jug_filled"); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "water_jug_filled");
        Assert.Equal(storedWater + 1, resumed.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == jugId)
            .Sum(lot => lot.Quantity));
        Assert.Equal(beforeTotal + 1, resumed.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.Equal(inventory.GetLot("last-slot-ballast") with
        {
            LastProcessedTick = resumed.Society.Inventory.GetLot("last-slot-ballast").LastProcessedTick,
        }, resumed.Society.Inventory.GetLot("last-slot-ballast"));
        resumed.Validate();
    }

    private static int HouseQuantity(PrivateWorldRuntime world, string ownerId, string buildingId, string itemKind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == ownerId && lot.StorageBuildingId == buildingId &&
            lot.ItemKind == itemKind).Sum(lot => lot.Quantity);

    private static PrivateWorldRuntimeState SetActorCondition(PrivateWorldRuntimeState state, string actor,
        int hunger, GridPoint position) => state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = position,
                    HungerBasisPoints = hunger,
                    Survival = person.Survival is { } survival
                        ? survival with { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 }
                        : null,
                }
                : person).ToArray(),
        };

    private static ContentPackageManifest WaterConsumerFixture()
    {
        const string packageId = "test-water-consumer-v1";
        const string packageIdentity = "test-water-consumer-v1:1.0.0:contained-water-input";
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageIdentity)));
        var version = ContentVersion.Parse("1.0.0");
        var recipe = new RecipeDefinition(digest, "water-input-fixture", version, "Use water in a test recipe",
            [new(InventoryContainerRules.FreshWater, InventoryContainerRules.WaterJugCapacity)],
            [new("test-water-product", 1)], 2, HouseContent.House1x1().CanonicalId, ["test", "water-input"]);
        var definition = new ContentDefinition(RecipeDefinition.SchemaKind, recipe.LocalId, version,
            recipe.DisplayName, recipe.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                recipe.WorkstationBuildingId,
                recipe.Tags,
            }, PayloadOptions));
        return new ContentPackageManifest(packageId, version, digest,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")))],
            [definition], []);
    }

    private static ContentPackageManifest WaterWorkstationFixture(int waterAmount)
    {
        const string packageId = "test-water-workstation-v1";
        var version = ContentVersion.Parse("1.0.0");
        var identity = $"{packageId}:1.0.0:water-workstation:{waterAmount}";
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var workshop = new BuildingDefinition(digest, "test-water-workstation", version, "Test water workstation",
            1, 1, 1, [], ["store"]);
        var recipe = new RecipeDefinition(digest, "water-input-fixture", version, "Use water in a test recipe",
            [new(InventoryContainerRules.FreshWater, waterAmount)], [new("test-water-product", 1)], 2,
            workshop.CanonicalId, ["test", "water-input"]);
        var buildingDefinition = new ContentDefinition(BuildingDefinition.SchemaKind, workshop.LocalId, version,
            workshop.DisplayName, workshop.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "building/v1",
                workshop.Width,
                workshop.Height,
                workshop.Capacity,
                buildCosts = workshop.BuildCosts,
                workshop.Tags,
            }, PayloadOptions));
        var recipeDefinition = new ContentDefinition(RecipeDefinition.SchemaKind, recipe.LocalId, version,
            recipe.DisplayName, recipe.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                recipe.WorkstationBuildingId,
                recipe.Tags,
            }, PayloadOptions));
        return new ContentPackageManifest(packageId, version, digest, [], [buildingDefinition, recipeDefinition], []);
    }

    private static IEnumerable<GridPoint> CardinalNeighbors(SeededMap map, GridPoint point)
    {
        foreach (var (dx, dy) in new (int X, int Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
        {
            var neighbor = map.WrapColumn(new GridPoint(point.X + dx, point.Y + dy));
            if (map.Contains(neighbor))
                yield return neighbor;
        }
    }

    private static async Task<string[]> ObservePersistedCandidates(PrivateWorldRuntimeState state, string actor)
    {
        var persisted = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        var observer = new PrefixCandidateProvider("safe_idle");
        using var world = PrivateWorldRuntime.Restore(persisted, id => id == actor
            ? observer : new IdleProvider());
        for (var tick = 0; tick < 60 && observer.OfferedCandidates.IsEmpty; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.False(observer.OfferedCandidates.IsEmpty, "The saved actor should reach a cognition observation.");
        return observer.OfferedCandidates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> ContainerActionCandidates(IEnumerable<string> candidates) => candidates.Where(id =>
        id is "collect_water_jug" or "return_water_jug" ||
        id.StartsWith("fill_water_jug:", StringComparison.Ordinal));

    private static GridPoint[] FreshWaterShorePoints(SeededMap map)
    {
        var shores = new HashSet<GridPoint>();
        foreach (var water in map.Tiles.Select(tile => tile.Position)
                     .Where(point => map.HydrologyAt(point) is WaterKind.River or WaterKind.Lake))
        {
            foreach (var point in CardinalNeighbors(map, water))
            {
                if (map.HydrologyAt(point) == WaterKind.Land && map.IsPassable(point))
                    shores.Add(point);
            }
        }
        return shores.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == "safe_idle" ? 1d : 0d)));
    }

    private sealed class PrefixCandidateProvider(string prefix) : IDecisionProvider
    {
        public ConcurrentBag<string> OfferedWorkstationCandidates { get; } = [];
        public ConcurrentBag<string> OfferedCandidates { get; } = [];

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                OfferedCandidates.Add(candidate.Id);
            foreach (var candidate in request.Observation.Candidates.Where(candidate =>
                         candidate.Id.StartsWith("supply_workstation:", StringComparison.Ordinal)))
                OfferedWorkstationCandidates.Add(candidate.Id);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal)) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }

    private sealed class WaterLoopProvider : IDecisionProvider
    {
        public ConcurrentBag<string> OfferedCandidates { get; } = [];

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                OfferedCandidates.Add(candidate.Id);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("fill_water_jug:", StringComparison.Ordinal)) ??
                request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "collect_water_jug") ??
                request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "return_water_jug") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }

    private sealed class ContainerSupplyProvider : IDecisionProvider
    {
        public ConcurrentBag<string> OfferedCandidates { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                OfferedCandidates.Add(candidate.Id);
            var choice = request.Observation.Candidates.FirstOrDefault(candidate =>
                             candidate.Id == "supply_workstation:fresh_water") ??
                         request.Observation.Candidates.FirstOrDefault(candidate =>
                             candidate.Id == "haul_household_stock") ??
                         request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}
