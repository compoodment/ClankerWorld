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
        Assert.DoesNotContain(map.Resources, resource => resource.Kind == "fertile_land" || resource.NaturalObjectKind == "fertile_soil");
        Assert.All(map.Tiles.Where(tile => map.IsLand(tile.Position)), tile =>
            Assert.InRange(map.FertilityAt(tile.Position), LandFertility.Poor, LandFertility.Rich));
        Assert.Equal(map.Tiles.Select(tile => map.FertilityAt(tile.Position)),
            second.ExportState().Map.Tiles.Select(tile => second.ExportState().Map.FertilityAt(tile.Position)));
        var town = Assert.Single(first.Towns);
        Assert.True(town.BorderTiles.Count(point => LandFertilityRules.IsFarmable(map, point)) >= 8);
        var packed = new OwnerWorldObservationStore(first).GetSnapshot().PackedMapLayers!;
        Assert.Equal("map-layers-v3", packed.Encoding);
        Assert.Equal(map.Width * map.Height, Convert.FromBase64String(packed.Fertility!).Length);
    }

    [Fact]
    public async Task BuiltInChooserTillsPlantsAndPhysicallyHarvestsAGeneratedWorld()
    {
        var recorder = new ActionCoverageRecorder();
        using var world = NormalPathWorld.CreateGenerated("probe-a", _ => recorder);
        for (var tick = 0; tick < 240 && !world.ExportState().Events.Any(item => item.Kind == "field_harvest_collected"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        Assert.Contains(state.Events, item => item.Kind == "field_prepared");
        Assert.Contains(state.Events, item => item.Kind == "field_planted");
        Assert.Contains(state.Events, item => item.Kind == "field_harvested");
        Assert.Contains(state.Events, item => item.Kind == "field_harvest_collected");
        Assert.NotEmpty(world.WorldSimulation.Fields!);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.ItemKind is "grain" or "potatoes" or "cultivated_greens");
        Assert.DoesNotContain(recorder.Chosen.Keys, key => key.Contains("fertile_land", StringComparison.Ordinal));
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ActionCoverageRecorder());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        await world.AdvanceOneTickAsync();
        await restored.AdvanceOneTickAsync();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task BuiltInChooserKeepsReplacementSeedAndCompletesASecondCropCycle()
    {
        using var world = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder());
        for (var tick = 0; tick < 480 && !HasSecondHarvest(world); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(HasSecondHarvest(world), string.Join("\n", world.ExportState().Events.Where(item => item.Kind.StartsWith("field", StringComparison.Ordinal)).Select(item => item.Kind + ":" + item.Detail).TakeLast(20)));
        Assert.True(world.WorldSimulation.CropBuilds!.Count(job => job.State == WorldProductionJobState.Completed) >= 2);
        var household = world.WorldSimulation.Fields![0].HouseholdId;
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == household &&
            lot.ItemKind is "grain_seed" or "greens_seed" && lot.Quantity >= world.FarmPlantingReserve(household, lot.ItemKind));
        world.Validate();
    }

    private static bool HasSecondHarvest(PrivateWorldRuntime world)
    {
        var harvestedIds = world.ExportState().Events.Where(item => item.Kind == "field_harvested")
            .Select(item => item.Detail[(item.Detail.LastIndexOf(':') + 1)..]).ToHashSet(StringComparer.Ordinal);
        return (world.WorldSimulation.CropBuilds ?? []).Where(job => harvestedIds.Contains(job.JobId))
            .GroupBy(job => job.BuildingInstanceId).Any(group => group.Count() >= 2);
    }

    [Fact]
    public async Task TillingNeedsAPresentHouseholdAdultAndHoeAndSurvivesAnInterruptedTurn()
    {
        using var source = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = source.ExportState();
        var farmhouse = source.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == farmhouse.HouseholdId).Id;
        var outsider = state.Society.Society.Inhabitants.First(person => person.HouseholdId != farmhouse.HouseholdId).Id;
        var footprints = source.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(source.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = source.Towns.Single().BorderTiles.First(tile => LandFertilityRules.IsFarmable(state.Map, tile) &&
            !footprints.Contains(tile) && !source.RoadTiles.Contains(tile) && !state.Map.Resources.Any(resource => resource.Position == tile));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = point } : person).ToArray(),
        };
        using (var withoutHoe = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true)))
        {
            var before = PrivateWorldRuntimeCodec.Encode(withoutHoe.ExportState());
            Assert.False(withoutHoe.TillField(actor, point).Applied);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(withoutHoe.ExportState()));
        }
        var hoe = state.Society.Society.Inventory.Lots.Single(lot => lot.ItemKind == "wooden_hoe" && lot.OwnerId == farmhouse.HouseholdId);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "test-hoe-pickup", hoe.OwnerId, actor, hoe.Id, 1, "field-work");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var working = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True(working.TillField(actor, point).Applied);
        var interrupted = PrivateWorldRuntimeCodec.Encode(working.ExportState());
        Assert.False(working.TillField(actor, point).Applied);
        Assert.False(working.TillField(outsider, point).Applied);
        Assert.Equal(interrupted, PrivateWorldRuntimeCodec.Encode(working.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(interrupted), _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var work = 1; work < 8; work++)
        {
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.True(restored.TillField(actor, point).Applied);
        }
        var field = Assert.Single(restored.WorldSimulation.Fields!);
        Assert.Equal(FarmFieldStage.Prepared, field.Stage);
        Assert.Equal(farmhouse.HouseholdId, field.HouseholdId);
        Assert.Equal(6_000, restored.Society.Inventory.Lots.Single(lot => lot.OwnerId == actor && lot.ItemKind == "wooden_hoe").ConditionBasisPoints);
        Assert.False(restored.TillField(actor, farmhouse.Position).Applied);
        Assert.False(restored.TillField(actor, source.RoadTiles[0]).Applied);
        Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Fields);
    }

    [Fact]
    public async Task ACarriedSickleHalvesHarvestWorkAndKeepsItsWearAcrossReload()
    {
        var (world, actor, point) = await ReadyField();
        using (world)
        {
            var state = world.ExportState();
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "field-sickle", "sickle", actor, 1, world.WorldTick);
            using var equipped = PrivateWorldRuntime.Restore(state with
            {
                Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            }, _ => new ActionCoverageRecorder(chooseIdle: true));
            Assert.True(equipped.HarvestField(actor, point).Applied);
            Assert.Equal(2, equipped.WorldSimulation.Fields!.Single().WorkDone);
            var encoded = PrivateWorldRuntimeCodec.Encode(equipped.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => new ActionCoverageRecorder(chooseIdle: true));
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.True(restored.HarvestField(actor, point).Completed);
            Assert.Equal(9_750, restored.Society.Inventory.GetLot("field-sickle").ConditionBasisPoints);
            Assert.Equal(FarmFieldStage.Harvested, restored.WorldSimulation.Fields!.Single().Stage);
        }
    }

    [Fact]
    public async Task HarvestCanStillBeCollectedWhenTheLastStrokeBreaksTheHoe()
    {
        var (world, actor, point) = await ReadyField();
        using (world)
        {
            for (var work = 0; work < 3; work++)
            {
                Assert.True(world.HarvestField(actor, point).Applied);
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            }
            var state = world.ExportState();
            var hoe = state.Society.Society.Inventory.Lots.Single(lot => lot.OwnerId == actor && lot.ItemKind == "iron_hoe");
            var inventory = InventoryFixture.ChangeCondition(state.Society.Society.Inventory, hoe.Id, actor,
                125 - hoe.ConditionBasisPoints, "fixture-last-use");
            using var lastStroke = PrivateWorldRuntime.Restore(state with
            {
                Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            }, _ => new ActionCoverageRecorder(chooseIdle: true));
            Assert.True(lastStroke.HarvestField(actor, point).Completed);
            Assert.Equal(0, lastStroke.Society.Inventory.GetLot(hoe.Id).ConditionBasisPoints);
            Assert.Contains(lastStroke.Society.Inventory.Lots, lot => lot.GroundPosition is not null && lot.Quantity > 4);
            using var carrying = PrivateWorldRuntime.Restore(lastStroke.ExportState(), id => new FarmCarryProvider(id == actor));
            for (var tick = 0; tick < 4 && !carrying.ExportState().Events.Any(item => item.Kind == "field_harvest_collected"); tick++)
                Assert.True((await carrying.AdvanceOneTickAsync()).Advanced);
            Assert.Contains(carrying.ExportState().Events, item => item.Kind == "field_harvest_collected");
            var carried = carrying.Society.Inventory.Lots.Single(lot => lot.OwnerId == actor && lot.DeliveryBuildingId is not null);
            Assert.Equal(4, carried.Quantity);
            Assert.Null(carried.GroundPosition);
        }
    }

    private static async Task<(PrivateWorldRuntime World, string Actor, GridPoint Point)> ReadyField()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var farmhouse = generated.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == farmhouse.HouseholdId).Id;
        var occupied = generated.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = generated.Towns.Single().BorderTiles.OrderBy(tile => state.Map.FootDistance(tile, farmhouse.Position))
            .First(tile => LandFertilityRules.IsFarmable(state.Map, tile) && !occupied.Contains(tile) &&
                !generated.RoadTiles.Contains(tile) && !state.Map.Resources.Any(resource => resource.Position == tile) &&
                !state.Inhabitants.Any(person => person.Position == tile));
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = point } : person).ToArray() };
        var recipe = generated.WorldContent.Recipes.Single(item => item.LocalId == "universal-grain-field");
        var world = PrivateWorldRuntime.Restore(FarmTestFields.Prepare(state, actor, point, recipe), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True(world.StartProduction(recipe.CanonicalId, WorldBuildSiteRules.FieldSiteId(point), actor).Applied);
        for (var tick = 0; tick < recipe.DurationTicks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
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
        Assert.NotEqual(TreeGrowthRules.SeedFor(TreeGrowthRules.Orchard), TreeGrowthRules.SeedFor(TreeGrowthRules.Broadleaf));
    }
}
