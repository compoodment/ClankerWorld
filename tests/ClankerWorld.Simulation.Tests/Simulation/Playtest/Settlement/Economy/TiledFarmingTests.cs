using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TiledFarmingTests
{
    [Fact]
    public void GeneratedFertilityIsDerivedForEveryLandTileAndIsNotAnObject()
    {
        using var first = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        using var second = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var map = first.ExportState().Map;
        var fertility = new LandFertility(map, first.ExportState().WorldSeed);
        var secondFertility = new LandFertility(second.ExportState().Map, second.ExportState().WorldSeed);
        Assert.DoesNotContain(map.Resources, resource => resource.Kind == "fertile_land" || resource.NaturalObjectKind == "fertile_soil");
        Assert.All(map.Tiles, tile => Assert.InRange(fertility.At(tile.Position), 0, 100));
        Assert.Equal(map.Tiles.Select(tile => fertility.At(tile.Position)),
            second.ExportState().Map.Tiles.Select(tile => secondFertility.At(tile.Position)));
        var town = Assert.Single(first.Towns);
        Assert.True(town.BorderTiles.Count(fertility.CanFarm) >= 8);
        var packed = new OwnerWorldObservationStore(first).GetSnapshot().PackedMapLayers!;
        Assert.Equal("map-layers-v2", packed.Encoding);
        var scores = Convert.FromBase64String(packed.Fertility!);
        Assert.Equal(map.Width * map.Height, scores.Length);
        Assert.All(map.Tiles, tile => Assert.Equal(fertility.At(tile.Position), scores[tile.Position.Y * map.Width + tile.Position.X]));
    }

    [Fact]
    public async Task BuiltInChooserTillsPlantsAndPhysicallyHarvestsAGeneratedWorld()
    {
        var recorder = new ActionCoverageRecorder();
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => recorder);
        var household = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse").HouseholdId!;
        using var world = PrivateWorldRuntime.Restore(FarmFieldTests.FeedHouseholdFromAvailableStock(setup.ExportState(), household), _ => recorder);
        // Two generated days prove the real bootstrap and ripening. The farmer
        // may still be finishing a valid 30-tick hauling intention, then must
        // physically return to harvest and carry the crop within the existing
        // 1,800-tick ordinary farming bound.
        for (var tick = 0; tick < world.WorldSystems.Config.TicksPerDay * 2 &&
            !world.ExportState().Events.Any(item => item.Kind == "field_harvest_collected"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_prepared");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_planted");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_tended");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_ready");
        while (world.WorldTick < 1_800 && !world.ExportState().Events.Any(item => item.Kind == "field_harvest_collected"))
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        Assert.Contains(state.Events, item => item.Kind == "field_prepared");
        Assert.Contains(state.Events, item => item.Kind == "field_planted");
        Assert.Contains(state.Events, item => item.Kind == "field_harvested");
        Assert.Contains(state.Events, item => item.Kind == "field_harvest_collected");
        Assert.NotEmpty(world.Fields);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.ItemKind is "grain" or "potatoes" or "cultivated_greens");
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.DeliveryBuildingId is { } destination &&
            world.Fields.Any(field => (lot.ProvenanceLotId ?? lot.Id).StartsWith(
                    FarmFieldRules.FieldId(field.Position) + ":harvest:", StringComparison.Ordinal) &&
                world.Society.Inhabitants.Any(person => person.Id == lot.OwnerId && person.HouseholdId == field.HouseholdId) &&
                world.WorldSimulation.Buildings.Any(building => building.InstanceId == destination && building.HouseholdId == field.HouseholdId)));
        Assert.DoesNotContain(recorder.Chosen.Keys, key => key.Contains("fertile_land", StringComparison.Ordinal));
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ActionCoverageRecorder());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task BuiltInChooserKeepsReplacementSeedAndCompletesASecondCropCycle()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder());
        var household = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse").HouseholdId!;
        using var first = PrivateWorldRuntime.Restore(FarmFieldTests.FeedHouseholdFromAvailableStock(setup.ExportState(), household), _ => new ActionCoverageRecorder());
        for (var tick = 0; tick < first.WorldSystems.Config.TicksPerDay * 2 && !first.Fields.Any(field => field.Cycle > 0); tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_prepared");
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_planted");
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_tended");
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_ready");
        while (first.WorldTick < 1_800 && !first.Fields.Any(field => field.Cycle > 0))
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(first.Fields, field => field.Cycle > 0);
        var initialHarvest = first.Fields.First(field => field.Cycle > 0);
        var initialReserve = first.Society.Inventory.GetReservation(initialHarvest.ReplantingReservationId!);
        Assert.Equal(1, initialReserve.Quantity);
        Assert.Equal(InventoryReservationState.Reserved, initialReserve.State);
        // The ordinary chooser can farm either household. Account for the
        // harvested household's real produce when creating its next shortage.
        var next = FarmFieldTests.FeedHouseholdFromAvailableStock(first.ExportState(), initialHarvest.HouseholdId);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(next)), _ => new ActionCoverageRecorder());
        for (var tick = 0; tick < world.WorldSystems.Config.TicksPerDay * 2 &&
            !world.Fields.Any(field => field.Position == initialHarvest.Position && field.Cycle >= 2); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(world.Fields.Any(field => field.Position == initialHarvest.Position && field.Cycle >= 2), string.Join("\n", world.Fields));
        var repeated = world.Fields.Single(field => field.Position == initialHarvest.Position);
        Assert.Equal(initialHarvest.HouseholdId, repeated.HouseholdId);
        var reserve = world.Society.Inventory.GetReservation(repeated.ReplantingReservationId!);
        Assert.Equal(1, reserve.Quantity);
        Assert.Equal(InventoryReservationState.Reserved, reserve.State);
        var seed = world.Society.Inventory.GetLot(reserve.LotId);
        Assert.Equal(repeated.HouseholdId, seed.OwnerId);
        Assert.Equal(FarmFieldRules.PlantingItem(repeated.Crop!), seed.ItemKind);
        Assert.Equal(new InventoryGroundPosition(repeated.Position.X, repeated.Position.Y), seed.GroundPosition);
        Assert.True(world.ExportState().Events.Count(item => item.Kind == "field_planted") >= 2);
        world.Validate();
    }

    [Fact]
    public async Task TillingNeedsAPresentHouseholdAdultAndHoeAndSurvivesAnInterruptedTurn()
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-tool-permissions");
        var outsider = state.Society.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
        var hoe = state.Society.Society.Inventory.GetLot("carried-hoe");
        var without = FarmFieldTests.WithInventory(state, state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != hoe.Id).ToArray() });
        using (var withoutHoe = FarmFieldTests.Restore(without))
        {
            var before = PrivateWorldRuntimeCodec.Encode(withoutHoe.ExportState());
            Assert.False(withoutHoe.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(withoutHoe.ExportState()));
        }
        using var working = FarmFieldTests.Restore(state);
        Assert.True(working.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.True((await working.AdvanceOneTickAsync()).Advanced);
        var interrupted = PrivateWorldRuntimeCodec.Encode(working.ExportState());
        Assert.False(working.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.False(working.StartFieldWork(outsider, point, FarmWorkKind.Till).Accepted);
        Assert.Equal(interrupted, PrivateWorldRuntimeCodec.Encode(working.ExportState()));
        using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(interrupted));
        for (var work = 1; work < 8; work++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var field = Assert.Single(restored.Fields);
        Assert.Equal(FarmFieldStage.Prepared, field.Stage);
        Assert.Equal(household, field.HouseholdId);
        Assert.Equal(6_000, restored.Society.Inventory.GetLot(hoe.Id).ConditionBasisPoints);
        var farmhouse = restored.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        Assert.False(restored.StartFieldWork(actor, farmhouse.Position, FarmWorkKind.Till).Accepted);
        Assert.False(restored.StartFieldWork(actor, state.RoadTiles![0], FarmWorkKind.Till).Accepted);
        Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Fields);
    }

    [Fact]
    public async Task ACarriedSickleHalvesHarvestWorkAndKeepsItsWearAcrossReload()
    {
        var (world, actor, point) = await ReadyField();
        using (world)
        {
            var inventory = InventoryFixture.AddLot(world.Society.Inventory, "field-sickle", "sickle", actor, 1, world.WorldTick);
            using var equipped = FarmFieldTests.Restore(FarmFieldTests.WithInventory(world.ExportState(), inventory));
            Assert.True(equipped.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
            Assert.True((await equipped.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(2, equipped.Fields.Single().Work!.RemainingTicks);
            var encoded = PrivateWorldRuntimeCodec.Encode(equipped.ExportState());
            using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(9_750, restored.Society.Inventory.GetLot("field-sickle").ConditionBasisPoints);
            Assert.Equal(FarmFieldStage.Harvested, restored.Fields.Single().Stage);
        }
    }

    [Fact]
    public async Task HarvestCanStillBeCollectedWhenTheLastStrokeBreaksTheHoe()
    {
        var (world, actor, point) = await ReadyField();
        using (world)
        {
            Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
            for (var work = 0; work < 3; work++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(1, world.Fields.Single().Work!.RemainingTicks);
            var hoe = world.Society.Inventory.Lots.Single(lot => lot.OwnerId == actor && lot.ItemKind == "iron_hoe");
            var inventory = InventoryFixture.ChangeCondition(world.Society.Inventory, hoe.Id, actor,
                125 - hoe.ConditionBasisPoints, "fixture-last-use");
            using var lastStroke = FarmFieldTests.Restore(FarmFieldTests.WithInventory(world.ExportState(), inventory));
            Assert.True((await lastStroke.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(FarmFieldStage.Harvested, lastStroke.Fields.Single().Stage);
            Assert.Equal(0, lastStroke.Society.Inventory.GetLot(hoe.Id).ConditionBasisPoints);
            var grain = Assert.Single(lastStroke.Society.Inventory.Lots, lot => lot.ItemKind == "grain" && lot.GroundPosition is not null);
            Assert.True(grain.Quantity > 4);
            using var carrying = PrivateWorldRuntime.Restore(lastStroke.ExportState(), id => new FarmCarryProvider(id == actor));
            for (var tick = 0; tick < 4 && !carrying.ExportState().Events.Any(item => item.Kind == "field_harvest_collected"); tick++)
                Assert.True((await carrying.AdvanceOneTickAsync()).Advanced);
            Assert.Contains(carrying.ExportState().Events, item => item.Kind == "field_harvest_collected");
            var carried = Assert.Single(carrying.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.DeliveryBuildingId is not null);
            Assert.Equal("grain", carried.ItemKind);
            Assert.Equal(4, carried.Quantity);
            Assert.Equal("first-town-farmhouse", carried.DeliveryBuildingId);
            Assert.Equal(grain.Id, carried.ProvenanceLotId);
            Assert.Null(carried.GroundPosition);
            Assert.Equal(grain.Quantity - 4, carrying.Society.Inventory.GetLot(grain.Id).Quantity);
            Assert.Equal(new InventoryGroundPosition(point.X, point.Y), carrying.Society.Inventory.GetLot(grain.Id).GroundPosition);
            var replant = carrying.Society.Inventory.GetReservation(carrying.Fields.Single().ReplantingReservationId!);
            Assert.Equal(1, replant.Quantity);
            Assert.Equal(InventoryReservationState.Reserved, replant.State);
            Assert.Equal(0, carrying.Society.Inventory.GetLot(hoe.Id).ConditionBasisPoints);
            using var restored = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(carrying.ExportState())));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(carrying.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
    }

    private static async Task<(PrivateWorldRuntime World, string Actor, GridPoint Point)> ReadyField()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var farmhouse = generated.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == farmhouse.HouseholdId).Id;
        var occupied = generated.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var point = generated.Towns.Single().BorderTiles.Where(tile => fertility.CanFarm(tile) && !occupied.Contains(tile) &&
                !generated.RoadTiles.Contains(tile) && !state.Map.Resources.Any(resource => resource.Position == tile) &&
                !state.Inhabitants.Any(person => person.Position == tile))
            .OrderByDescending(fertility.At).ThenBy(tile => state.Map.FootDistance(tile, farmhouse.Position)).First();
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = point } : person).ToArray() };
        var world = FarmFieldTests.Restore(FarmTestFields.Prepare(state, actor, point));
        await FarmTestFields.PlantAndGrow(world, actor, point);
        return (world, actor, point);
    }

    [Fact]
    public void OrchardPlantingConsumesOnlyAnOrchardSeedAndPreservesTheSaplingAcrossReload()
    {
        using var source = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = source.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var occupied = source.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(source.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = state.Map.Tiles.Where(tile => TreeGrowthRules.GroundRefusal(state.Map, tile.Position) is null &&
            !occupied.Contains(tile.Position) && !source.RoadTiles.Contains(tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
            !state.Map.Resources.Any(item => item.Position == tile.Position)).OrderBy(tile =>
                state.Map.FootDistance(state.Inhabitants[0].Position, tile.Position)).First().Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "orchard-planting-stock", "orchard_seed", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "wood-tree-planting-stock", "tree_seed", actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = point } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(TreePlantingRefusal.NotATreeSeed,
            world.PlantTree(actor, TreeGrowthRules.Orchard, "wood-tree-planting-stock", point).Refusal);
        Assert.True(world.PlantTree(actor, TreeGrowthRules.Orchard, "orchard-planting-stock", point).Planted);
        Assert.Equal(1, world.Society.Inventory.GetLot("orchard-planting-stock").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("wood-tree-planting-stock").Quantity);
        var tree = world.ExportState().Map.Resources.Single(resource => resource.Id == TreeGrowthRules.PlantedTreeId(point));
        Assert.Equal("fruit", tree.Kind);
        Assert.True(world.WorldSystems.Ecology.GetResource(tree.Id).IsPlanted);
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void OrchardSeedMaturesWithoutFruitUntilAutumn()
    {
        var tree = new MapResource(TreeGrowthRules.PlantedTreeId(new GridPoint(2, 3)), "fruit", new GridPoint(2, 3), true, TreeGrowthRules.Orchard);
        var sapling = TreeGrowthRules.PlantedSapling(tree, 0);
        var config = WorldSystemsConfig.Default;
        var spring = WorldCalendarRules.FromTick(config.TicksPerDay * TreeGrowthRules.SaplingGrowthDays, config);
        var mature = EcologyRules.Regenerate(sapling, spring, config);
        Assert.False(mature.IsPlanted);
        Assert.Equal(0, mature.Quantity);
        var autumn = spring with { Season = SeasonKind.Autumn, DayIndex = spring.DayIndex + TreeGrowthRules.OrchardRefruitDays };
        Assert.Equal(1, EcologyRules.Regenerate(mature, autumn, config).Quantity);
        Assert.NotEqual(TreeGrowthRules.SeedItem(TreeGrowthRules.Orchard), TreeGrowthRules.SeedItem(TreeGrowthRules.Broadleaf));
    }
}
