namespace ClankerWorld.Viewer.Observation;

/// <summary>Start simulation and catalog work only once the active checkpoint is usable.</summary>
public sealed class PrivateWorldStartupService(IServiceProvider services,
    PrivateWorldStartupRecovery recovery) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await recovery.WaitUntilReadyAsync(stoppingToken);
        _ = services.GetRequiredService<WorldCatalogStore>();
        using var advancing = new PrivateWorldRuntimeService(
            services.GetRequiredService<ClankerWorld.Simulation.Playtest.PrivateWorldRuntime>(),
            services.GetRequiredService<PrivateWorldStateFile>(),
            services.GetRequiredService<OwnerClientPresenceLease>(),
            services.GetRequiredService<ILogger<PrivateWorldRuntimeService>>(),
            services.GetRequiredService<WorldAutosaveStore>(),
            services.GetRequiredService<ManualWorldSaveStore>(),
            services.GetRequiredService<Control.ProviderConfigurationStore>());
        await advancing.StartAsync(stoppingToken);
        try { await advancing.ExecuteTask!; }
        finally
        {
            await advancing.StopAsync(CancellationToken.None);
            advancing.SaveBeforeShutdown();
        }
    }
}
