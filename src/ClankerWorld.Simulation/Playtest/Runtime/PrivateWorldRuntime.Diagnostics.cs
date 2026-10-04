using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// The route an agent is walking, as the server planned it on its latest
/// movement step: why, where to, and the tiles still ahead of it.
/// </summary>
public sealed record PlaytestPlannedRoute(string Reason, GridPoint Destination, IReadOnlyList<GridPoint> Steps);

/// <summary>
/// Read-only readouts for Developer tools, taken from the committed world.
/// They are never saved and nothing in the simulation reads them back, so
/// they cannot change what a tick does.
/// </summary>
/// <param name="PlannedRoutes">The route of each agent who walked, or waited out a slow step, in the latest tick.</param>
/// <param name="LastTickMilliseconds">How long the host took to work out the latest tick, before saving it.</param>
public sealed record PrivateWorldDiagnostics(
    IReadOnlyDictionary<string, PlaytestPlannedRoute> PlannedRoutes,
    double? LastTickMilliseconds);

/// <summary>
/// Identifies the live observer's timeline. This is host metadata, never saved
/// world identity: loading a checkpoint changes its generation, and creating a
/// new runtime starts a new instance.
/// </summary>
public sealed record PrivateWorldObserverTimeline(string InstanceId, long Generation);

public sealed partial class PrivateWorldRuntime
{
    private static readonly IReadOnlyDictionary<string, PlaytestPlannedRoute> NoPlannedRoutes =
        new Dictionary<string, PlaytestPlannedRoute>(StringComparer.Ordinal);

    // Developer tools only; see PrivateWorldDiagnostics. A proposed tick
    // starts empty and fills this as agents move; the committed tick's routes
    // replace the live ones.
    private Dictionary<string, PlaytestPlannedRoute> plannedRoutes = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, PlaytestPlannedRoute> previousPlannedRoutes = NoPlannedRoutes;
    private double? lastTickMilliseconds;
    // Keep these on the long-lived runtime. Prepared ticks and history
    // compaction must not replace them when committing their simulation state.
    private readonly string observerInstanceId = Guid.NewGuid().ToString("N");
    private long observerGeneration;

    /// <summary>The committed state and its diagnostics, read together so both describe the same tick.</summary>
    public (PrivateWorldRuntimeState State, PrivateWorldDiagnostics Diagnostics) ExportStateWithDiagnostics()
    {
        var (state, diagnostics, _) = ExportObservation();
        return (state, diagnostics);
    }

    /// <summary>Captures state, diagnostics and its observer timeline under the same runtime gate.</summary>
    public (PrivateWorldRuntimeState State, PrivateWorldDiagnostics Diagnostics, PrivateWorldObserverTimeline Timeline)
        ExportObservation()
    {
        gate.Wait();
        try
        {
            return (CaptureState(), new PrivateWorldDiagnostics(
                new Dictionary<string, PlaytestPlannedRoute>(plannedRoutes, StringComparer.Ordinal),
                lastTickMilliseconds), new PrivateWorldObserverTimeline(observerInstanceId, observerGeneration));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Remembers the rest of a route after one step along it; the next tile is already taken.</summary>
    private void RecordPlannedRoute(string inhabitantId, string reason, GridPoint destination, List<GridPoint> route)
    {
        if (route.Count > 2)
            plannedRoutes[inhabitantId] = new PlaytestPlannedRoute(reason, destination, route.GetRange(2, route.Count - 2));
    }

    /// <summary>An agent waiting out a slow step keeps the route it was already walking.</summary>
    private void KeepPlannedRoute(string inhabitantId, string reason, GridPoint destination)
    {
        if (previousPlannedRoutes.TryGetValue(inhabitantId, out var walking) &&
            walking.Destination == destination && string.Equals(walking.Reason, reason, StringComparison.Ordinal))
            plannedRoutes[inhabitantId] = walking;
    }
}
