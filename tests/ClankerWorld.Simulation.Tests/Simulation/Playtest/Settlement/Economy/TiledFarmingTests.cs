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
