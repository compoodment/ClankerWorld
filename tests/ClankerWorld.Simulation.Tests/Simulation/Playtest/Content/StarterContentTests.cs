using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class StarterContentTests
{
    [Fact]
    public async Task StarterPackActivatesOnTheNextTickAndSurvivesRestoreWithoutDuplication()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        for (var tick = 0; tick < 5; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
        }

        Assert.True(runtime.StageStarterContent());
        Assert.False(runtime.StageStarterContent());
        Assert.Empty(runtime.ExportState().WorldContent!.Buildings);
        _ = await runtime.AdvanceOneTickAsync();
        var state = runtime.ExportState();
        Assert.Equal(4, state.WorldContent!.Buildings.Count);
        Assert.Equal(3, state.WorldContent.Recipes.Count);
        Assert.Equal(ContentPackageLifecycle.Active, Assert.Single(state.Content!.Packages).Lifecycle);

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.False(restored.StageStarterContent());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void PausedExistingWorldIsNotMigratedUntilItResumes()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        runtime.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
        Assert.False(runtime.StageStarterContent());
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
    }

    [Fact]
    public async Task DefaultStarterContentCreatesActualBuildingsAndUsesFoodWithoutInjectedFixtures()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        Assert.True(runtime.StageStarterContent());
        for (var tick = 0; tick < 900; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
        }

        Assert.NotEmpty(runtime.WorldSimulation.Buildings);
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "build_completed");
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "household_food_collected");
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "food_consumed");
    }
}
