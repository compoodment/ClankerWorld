using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Developer tools read each agent's planned route and the host's tick time
/// from the server. Both are read-only readouts: never saved, and never able
/// to change what a tick does.
/// </summary>
public sealed class DeveloperToolsObservationTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PlannedRoutesFollowTheServersOwnStepsAndReachTheClient()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        Assert.Empty(runtime.ExportStateWithDiagnostics().Diagnostics.PlannedRoutes);
        Assert.Null(runtime.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds);

        var (state, diagnostics) = await AdvanceUntilSomeoneWalksAsync(runtime);
        Assert.NotNull(diagnostics.LastTickMilliseconds);
        Assert.True(diagnostics.LastTickMilliseconds >= 0);
        var positions = state.Inhabitants.ToDictionary(item => item.InhabitantId, item => item.Position, StringComparer.Ordinal);
        foreach (var (id, route) in diagnostics.PlannedRoutes)
        {
            // The route starts beside the agent and every step is one foot step on.
            Assert.NotEmpty(route.Steps);
            var previous = positions[id];
            foreach (var step in route.Steps)
            {
                Assert.Equal(1, state.Map.FootDistance(previous, step));
                previous = step;
            }
            Assert.False(string.IsNullOrWhiteSpace(route.Reason));
        }

        var snapshot = new OwnerWorldObservationStore(runtime).GetSnapshot();
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(
            JsonSerializer.Serialize(snapshot, WireOptions), WireOptions)!;
        Assert.NotNull(client.LastTickMilliseconds);
        foreach (var person in client.Inhabitants)
        {
            if (!diagnostics.PlannedRoutes.TryGetValue(person.Id, out var route))
            {
                Assert.Null(person.PlannedRoute);
                continue;
            }
            var projected = Assert.IsType<ClankerWorld.GodotClient.UI.OwnerWorldPlannedRoute>(person.PlannedRoute);
            Assert.Equal(route.Reason, projected.Reason);
            Assert.Equal((route.Destination.X, route.Destination.Y), (projected.Destination.X, projected.Destination.Y));
            Assert.Equal(route.Steps.Count, projected.StepCount);
            Assert.Equal(route.Steps.Select(step => (step.X, step.Y)),
                projected.Steps.Select(step => (step.X, step.Y)));
        }
    }

    [Fact]
    public async Task ReadoutsAreNeverSavedAndDoNotChangeTheWorld()
    {
        using var observed = new PrivateWorldRuntime("playtest-alpha");
        using var unobserved = new PrivateWorldRuntime("playtest-alpha");
        var (walking, _) = await AdvanceUntilSomeoneWalksAsync(observed);
        while (unobserved.WorldTick < observed.WorldTick)
            _ = await unobserved.AdvanceOneTickAsync();

        // Reading the readouts every tick leaves the world exactly as it would be otherwise.
        var saved = PrivateWorldRuntimeCodec.Encode(walking);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(unobserved.ExportState()), saved);
        var savedText = System.Text.Encoding.UTF8.GetString(saved);
        Assert.DoesNotContain("plannedRoute", savedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tickMilliseconds", savedText, StringComparison.OrdinalIgnoreCase);

        // A restored save starts with no route or timing until its next tick.
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        var (_, restoredDiagnostics) = restored.ExportStateWithDiagnostics();
        Assert.Empty(restoredDiagnostics.PlannedRoutes);
        Assert.Null(restoredDiagnostics.LastTickMilliseconds);
        Assert.All(new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants,
            person => Assert.Null(person.PlannedRoute));

        // Loading a checkpoint into the running host clears the old readouts too.
        observed.Pause();
        observed.LoadPausedCheckpoint(walking);
        var (_, loadedDiagnostics) = observed.ExportStateWithDiagnostics();
        Assert.Empty(loadedDiagnostics.PlannedRoutes);
        Assert.Null(loadedDiagnostics.LastTickMilliseconds);
    }

    [Fact]
    public void PlannedRouteProjectionSendsABoundedNumberOfSteps()
    {
        var steps = Enumerable.Range(1, 300).Select(x => new GridPoint(x, 0)).ToArray();
        var route = new PlaytestPlannedRoute("explore", new GridPoint(300, 0), steps);
        var projected = OwnerWorldObservationStore.ToPlannedRoute(route)!;
        Assert.Equal(ViewerPlannedRoute.StepLimit, projected.Steps.Count);
        Assert.Equal(300, projected.StepCount);
        Assert.Equal(new ViewerPosition(1, 0), projected.Steps[0]);
        Assert.Null(OwnerWorldObservationStore.ToPlannedRoute(null));
    }

    private static async Task<(PrivateWorldRuntimeState State, PrivateWorldDiagnostics Diagnostics)> AdvanceUntilSomeoneWalksAsync(
        PrivateWorldRuntime runtime)
    {
        for (var tick = 0; tick < 600; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
            var observation = runtime.ExportStateWithDiagnostics();
            if (observation.Diagnostics.PlannedRoutes.Count > 0) return observation;
        }
        throw new InvalidOperationException("No agent planned a route of more than one step in 600 ticks.");
    }
}
