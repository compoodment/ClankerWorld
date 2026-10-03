using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Society;

public sealed record SocietyWorldRuntimeState(
    int SchemaVersion,
    SocietyCheckpoint Society,
    SocietyCognitionSchedulerState Cognition);

public sealed record SocietyWorldCapture(
    SocietyCheckpoint Society,
    SocietyCognitionSchedulerState Cognition);

public sealed record SocietyDispatchCycleResult(
    SocietyCheckpoint Society,
    IReadOnlyList<SocietyCognitionDispatchResult> Decisions);

/// <summary>
/// The Phase 4 composition root. Society is the authoritative state boundary;
/// cognition is a derived, bounded service that is reconciled after every
/// lifecycle mutation. Saving this record captures both together without
/// allowing a provider or client to mutate society directly.
/// </summary>
public sealed class SocietyWorldRuntime : IDisposable
{
    public const int StateSchemaVersion = 1;

    private readonly SemaphoreSlim gate = new(1, 1);
    private SocietyCheckpoint society;
    private SocietyCognitionScheduler cognition;

    public SocietyWorldRuntime(
        SocietyCheckpoint checkpoint,
        Func<string, IDecisionProvider>? providerFactory = null,
        int maxCognitionQueueLength = 64,
        int maxCognitionDispatchPerCycle = 8)
    {
        SocietyFixture.Validate(checkpoint);
        society = checkpoint;
        cognition = new SocietyCognitionScheduler(
            checkpoint.Inhabitants,
            providerFactory,
            maxCognitionQueueLength,
            maxCognitionDispatchPerCycle);
    }

    public SocietyCheckpoint Checkpoint => society;

    public SocietyWorldCapture Capture()
    {
        gate.Wait();
        try
        {
            return new SocietyWorldCapture(society, cognition.ExportState());
        }
        finally
        {
            gate.Release();
        }
    }

    public SocietyWorldRuntimeState ExportState()
    {
        var capture = Capture();
        return new SocietyWorldRuntimeState(StateSchemaVersion, capture.Society, capture.Cognition);
    }

    public static SocietyWorldRuntime Restore(
        SocietyWorldRuntimeState state,
        Func<string, IDecisionProvider>? providerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != StateSchemaVersion)
        {
            throw new InvalidDataException("The Phase 4 runtime state schema is unsupported.");
        }

        SocietyFixture.Validate(state.Society);
        var scheduler = SocietyCognitionScheduler.Restore(
            state.Cognition,
            providerFactory);
        scheduler.SyncInhabitants(state.Society.Inhabitants,
            state.Society.Births.Select(birth => birth.ChildId).ToHashSet(StringComparer.Ordinal));
        var runtime = new SocietyWorldRuntime(
            state.Society,
            providerFactory,
            state.Cognition.MaxQueueLength,
            state.Cognition.MaxDispatchPerCycle)
        {
            cognition = scheduler,
        };
        runtime.Validate();
        return runtime;
    }

    public SocietyOperationResult Apply(Func<SocietyCheckpoint, SocietyOperationResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        gate.Wait();
        try
        {
            var result = operation(society);
            SocietyFixture.Validate(result.Checkpoint);
            society = result.Checkpoint;
            cognition.SyncInhabitants(society.Inhabitants,
                society.Births.Select(birth => birth.ChildId).ToHashSet(StringComparer.Ordinal));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public SocietyOperationResult AdvanceTo(long targetTick, IReadOnlyList<SocietyTownStore>? townStores = null) =>
        Apply(checkpoint => SocietyFixture.AdvanceTo(checkpoint, targetTick, townStores));

    public SocietyOperationResult Pause() => Apply(SocietyFixture.Pause);

    public SocietyOperationResult Resume() => Apply(SocietyFixture.Resume);

    public bool EnqueueCognition(SocietyCognitionScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        gate.Wait();
        try
        {
            var inhabitant = society.GetInhabitant(entry.InhabitantId);
            if (inhabitant.Status != SocietyInhabitantStatus.Active)
            {
                throw new InvalidOperationException("Dead inhabitants cannot receive cognition work.");
            }

            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
                return false;
            }

            if (entry.Observation.WorldTick > society.WorldTick)
            {
                throw new InvalidOperationException("Cognition cannot observe beyond the authoritative world tick.");
            }

            var child = society.Births.Any(birth => birth.ChildId == inhabitant.Id);
            return cognition.Enqueue(child
                ? entry with { Observation = entry.Observation with { RequiresPersonalProvider = true } }
                : entry);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<SocietyDispatchCycleResult> DispatchCognitionAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var decisions = await cognition.DispatchAsync(cancellationToken).ConfigureAwait(false);
            return new SocietyDispatchCycleResult(society, decisions);
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask<SocietyDispatchCycleResult> DispatchDeterministicCognitionAsync(
        CancellationToken cancellationToken = default) =>
        DispatchDeterministicCognitionAsync(null, cancellationToken);

    internal async ValueTask<SocietyDispatchCycleResult> DispatchDeterministicCognitionAsync(
        IReadOnlySet<string>? excludedInhabitantIds, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new SocietyDispatchCycleResult(society,
                await cognition.DispatchDeterministicAsync(excludedInhabitantIds, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            gate.Release();
        }
    }

    public IReadOnlyList<SocietyDeferredCognitionRequest> PreviewHostedRequests(IReadOnlySet<string> excludedIds)
    {
        gate.Wait();
        try { return cognition.PreviewHostedRequests(excludedIds); }
        finally { gate.Release(); }
    }

    public IReadOnlySet<string> PendingHostedInhabitantIds()
    {
        gate.Wait();
        try { return cognition.PendingHostedInhabitantIds(); }
        finally { gate.Release(); }
    }

    public long CurrentProviderEpoch(string inhabitantId)
    {
        gate.Wait();
        try { return cognition.CurrentProviderEpoch(inhabitantId); }
        finally { gate.Release(); }
    }

    public SocietyCognitionDispatchResult? CompleteDeferredCognition(
        CognitionDecisionRequest request, CognitionDecisionResponse? response, string? failure,
        IReadOnlySet<string> legalCandidateIds, bool? decisionContextChanged = null)
    {
        gate.Wait();
        try { return cognition.CompleteDeferred(request, response, failure, society.RunEpoch, legalCandidateIds, decisionContextChanged); }
        finally { gate.Release(); }
    }

    internal void CompleteQueuedIdentityChoice(string inhabitantId, string personality, string aspiration)
    {
        gate.Wait();
        try { cognition.CompleteQueuedIdentityChoice(inhabitantId, society.GetInhabitant(inhabitantId).Name, personality, aspiration); }
        finally { gate.Release(); }
    }

    public void Validate()
    {
        SocietyFixture.Validate(society);
        cognition.Validate();
        var activeIds = society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .OrderBy(item => item, StringComparer.Ordinal);
        if (!activeIds.SequenceEqual(cognition.InhabitantIds.OrderBy(item => item, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Phase 4 society and cognition runtime populations disagree.");
        }
    }

    public void Dispose() => gate.Dispose();
}
