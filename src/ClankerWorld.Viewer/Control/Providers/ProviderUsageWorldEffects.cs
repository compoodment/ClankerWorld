using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Applies the installation's model-call accounting to the active private
/// world: reaching the limit pauses and saves it, and the 80% warning adds one
/// saved Event Log line. Both hold the world mutation gate, so they land in one
/// whole world while a load or world switch is under way.
/// </summary>
/// <remarks>
/// Conversation startup and synchronous provider cancellation callbacks can
/// reserve or finish model calls on the thread holding the runtime gate. That
/// gate is not reentrant, so on that thread the work moves to another task that
/// waits for the world. Anywhere else it runs at once, so a hosted decision's
/// late reply cannot be admitted ahead of the pause.
/// </remarks>
public sealed class ProviderUsageWorldEffects(
    PrivateWorldRuntime runtime,
    PrivateWorldStateFile stateFile,
    ProviderUsageStore usage,
    object worldMutationGate,
    ILogger logger)
{
    public void PauseAtLimit() => Apply(TryPauseAtLimit);

    public void RecordWarning(ProviderUsageWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        Apply(wait => TryRecordWarning(warning, wait));
    }

    private void Apply(Func<TimeSpan, bool> work)
    {
        var wait = runtime.IsInvokingProviderUnderGateOnThisThread ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;
        if (!work(wait)) _ = Task.Run(() => work(Timeout.InfiniteTimeSpan));
    }

    private bool TryPauseAtLimit(TimeSpan wait)
    {
        var pauseNeeded = false;
        return TryChangeWorld(wait,
            () => runtime.TryPause(wait, () => pauseNeeded = usage.Capture().LimitReached), "paused",
            outcome => ProviderUsageTelemetry.LimitReached(logger, outcome, runtime.WorldTick),
            didChange: () => pauseNeeded);
    }

    private bool TryRecordWarning(ProviderUsageWarning warning, TimeSpan wait) => TryChangeWorld(wait,
        () => runtime.TryRecordModelCallWarning(warning.Attempts, warning.AttemptLimit, wait), "event_log",
        outcome => ProviderUsageTelemetry.WarningReached(logger, outcome, warning.Attempts, warning.AttemptLimit,
            runtime.WorldTick));

    /// <summary>False means a gate was busy and nothing changed.</summary>
    private bool TryChangeWorld(TimeSpan wait, Func<bool> change, string changed, Action<string> report,
        Func<bool>? didChange = null)
    {
        var outcome = "failed";
        var entered = false;
        try
        {
            Monitor.TryEnter(worldMutationGate, wait, ref entered);
            if (!entered || !change()) return false;
            if (didChange is not null && !didChange()) outcome = "superseded";
            else
            {
                // A failed save leaves the change for the world's next save.
                outcome = changed + "_unsaved";
                stateFile.Save(runtime);
                outcome = changed;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Reported below with the outcome reached.
        }
        finally
        {
            if (entered) Monitor.Exit(worldMutationGate);
        }
        report(outcome);
        return true;
    }
}
