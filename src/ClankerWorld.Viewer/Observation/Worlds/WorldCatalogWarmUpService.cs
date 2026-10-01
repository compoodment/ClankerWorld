namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Checks inactive saved worlds once in the background after the host starts,
/// so the first Load World list does not wait for every restore.
/// </summary>
public sealed class WorldCatalogWarmUpService(WorldSelectionCoordinator selection) : BackgroundService
{
    // Run off the startup path; WarmUp stops between worlds when the host stops.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => selection.WarmUp(stoppingToken), CancellationToken.None);
}
