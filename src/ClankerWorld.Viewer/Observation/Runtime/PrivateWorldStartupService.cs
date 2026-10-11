namespace ClankerWorld.Viewer.Observation;

/// <summary>Start simulation and catalog work only once the active checkpoint is usable.</summary>
public sealed class PrivateWorldStartupService(IServiceProvider services,
    PrivateWorldStartupRecovery recovery) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await recovery.WaitUntilReadyAsync(stoppingToken);
        _ = services.GetRequiredService<WorldCatalogStore>();
        var advancing = services.GetRequiredService<PrivateWorldRuntimeService>();
        await advancing.StartAsync(stoppingToken);
        try { await advancing.ExecuteTask!; }
        finally
        {
            await advancing.StopAsync(CancellationToken.None);
            var companion = services.GetRequiredService<Control.OwnerPairingHostOptions>().IsCompanionHost;
            if (advancing.SaveBeforeShutdown(pauseWorld: companion) == ShutdownSaveOutcome.WriteFailed)
                Environment.ExitCode = 1;
        }
    }
}
