using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Runs the small live-fixture clock only when the host explicitly enables it.
/// Test hosts remain clock-independent and exercise the same runtime manually.
/// </summary>
public sealed class LiveWorldRuntimeService(LiveSeededWorldRuntime runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(1d / KernelClock.ScheduledTicksPerSecond));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            runtime.TryAdvanceOneAction();
        }
    }
}
