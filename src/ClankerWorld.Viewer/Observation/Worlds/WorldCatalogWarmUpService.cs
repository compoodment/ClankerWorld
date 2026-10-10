namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Checks inactive saved worlds once in the background after the host starts,
/// so the first Load World list does not wait for every restore.
/// </summary>
public sealed class WorldCatalogWarmUpService : BackgroundService
{
    private readonly Func<WorldSelectionCoordinator> selection;
    private readonly PrivateWorldStartupRecovery? recovery;

    public WorldCatalogWarmUpService(WorldSelectionCoordinator selection) => this.selection = () => selection;

    public WorldCatalogWarmUpService(IServiceProvider services, PrivateWorldStartupRecovery recovery)
    {
        selection = services.GetRequiredService<WorldSelectionCoordinator>;
        this.recovery = recovery;
    }

    // Run off the startup path; WarmUp stops between worlds when the host stops.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (recovery is not null) await recovery.WaitUntilReadyAsync(stoppingToken);
        await Task.Run(() => selection().WarmUp(stoppingToken), CancellationToken.None);
    }
}
