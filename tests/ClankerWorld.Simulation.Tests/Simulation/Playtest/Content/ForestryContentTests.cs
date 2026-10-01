using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ForestryContentTests
{
    [Fact]
    public async Task RetiredTimberCropIsNotStagedAcrossPauseReloadAndResume()
    {
        using var seed = new PrivateWorldRuntime("forestry-migration", _ => new ActionCoverageRecorder(chooseIdle: true));
        seed.StageStarterContent();
        for (var tick = 0; tick < 2; tick++) Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        seed.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(seed.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.DoesNotContain(world.WorldContent.Recipes, recipe => recipe.LocalId == "managed-coppice");
        world.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(restored.WorldContent.Recipes, recipe => recipe.LocalId == "managed-coppice");
        Assert.Empty(restored.WorldSimulation.CropBuilds!);
    }

    [Fact]
    public void PlantedTimberWaitsForItsSavedSaplingClockAndDoesNotRefillOtherWildWood()
    {
        // Timber now uses the same typed tree lifecycle as the visible forest,
        // rather than a second crop-production job that manufactures wood stock.
        var planted = new MapResource(TreeGrowthRules.PlantedTreeId(new(2, 3)), "construction", new(2, 3), true, TreeGrowthRules.Broadleaf);
        var wild = TreeGrowthRules.GeneratedTree(new("unrelated-wild-tree", "construction", new(3, 3), true, TreeGrowthRules.Conifer));
        var felled = EcologyRules.Harvest(wild, 1);
        Assert.True(felled.IsValid);
        wild = felled.Resource!;
        Assert.Equal(0, wild.Quantity);
        var sapling = TreeGrowthRules.PlantedSapling(planted, 0);
        var config = WorldSystemsConfig.Default;
        var before = WorldCalendarRules.FromTick(config.TicksPerDay * (TreeGrowthRules.SaplingGrowthDays - 1), config);
        var waitingState = EcologyRules.Advance(new([sapling, wild]), before, config);
        var waiting = waitingState.GetResource(planted.Id);
        Assert.True(waiting.IsPlanted);
        Assert.Equal(0, waiting.Quantity);
        Assert.Equal(TreeGrowthRules.SaplingGrowthDays, waiting.NextRegenerationDay);
        var saved = System.Text.Json.JsonSerializer.Deserialize<EcologyState>(System.Text.Json.JsonSerializer.Serialize(waitingState))!;
        var grown = EcologyRules.Advance(saved,
            WorldCalendarRules.FromTick(config.TicksPerDay * TreeGrowthRules.SaplingGrowthDays, config), config);
        var ready = grown.GetResource(planted.Id);
        Assert.False(ready.IsPlanted);
        Assert.Equal(1, ready.Quantity);
        Assert.Equal(EcologyResourceState.Available, ready.State);
        Assert.Equal(wild, grown.GetResource(wild.Id));
        Assert.Equal(0, grown.GetResource(wild.Id).Quantity);
        Assert.NotEqual(wild.Id, ready.Id);
    }
}
