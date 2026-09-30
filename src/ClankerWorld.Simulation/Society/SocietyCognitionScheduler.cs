using ClankerWorld.Simulation.Cognition;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Society;

public sealed record SocietyCognitionScheduleEntry(
    string ScheduleId,
    string InhabitantId,
    int Priority,
    long EnqueuedTick,
    IReadOnlyList<string> TriggerIds,
    InhabitantObservation Observation);

public sealed record SocietyCognitionSchedulerEvent(
    long EventId,
    long WorldTick,
    string Kind,
    string Detail);

public sealed record SocietyCognitionDispatchResult(
    string InhabitantId,
    CognitionAdmissionResult Admission);

public sealed record SocietyDeferredCognitionRequest(
    string InhabitantId,
    CognitionDecisionRequest Request,
    Func<CancellationToken, ValueTask<CognitionDecisionResponse>> DecideAsync);

public sealed record SocietyCognitionSchedulerState(
    int SchemaVersion,
    int MaxQueueLength,
    int MaxDispatchPerCycle,
    IReadOnlyList<SocietyCognitionScheduleEntry> Queue,
    IReadOnlyList<CognitionRuntimeState> Runtimes,
    IReadOnlyList<SocietyCognitionSchedulerEvent> Events,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long EventHistoryFloor = 0);

/// <summary>
/// Fair multi-inhabitant cognition admission. Each inhabitant has one
/// CognitionRuntime and therefore one in-flight request; the shared queue is
/// bounded and ordered by priority, age, inhabitant ID, and schedule ID.
/// </summary>
public sealed class SocietyCognitionScheduler
{
    public const int StateSchemaVersion = 1;

    private readonly Dictionary<string, CognitionRuntime> runtimes;
    private readonly List<SocietyCognitionScheduleEntry> queue = [];
    private readonly List<SocietyCognitionSchedulerEvent> events = [];
    private readonly int maxQueueLength;
    private readonly int maxDispatchPerCycle;
    private readonly Func<string, IDecisionProvider> providerFactory;
    private readonly double minimumConfidence;
    private long nextEventId = 1;
    private long eventHistoryFloor;

    public SocietyCognitionScheduler(
        IEnumerable<SocietyInhabitant> inhabitants,
        Func<string, IDecisionProvider>? providerFactory = null,
        int maxQueueLength = 64,
        int maxDispatchPerCycle = 8,
        double minimumConfidence = 0.5)
    {
        ArgumentNullException.ThrowIfNull(inhabitants);
        if (maxQueueLength <= 0 || maxDispatchPerCycle <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueueLength));
        }

        this.maxQueueLength = maxQueueLength;
        this.maxDispatchPerCycle = maxDispatchPerCycle;
        this.providerFactory = providerFactory ?? (_ => new DeterministicDecisionProvider());
        this.minimumConfidence = minimumConfidence;
        runtimes = inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(
                item => item.Id,
                item => new CognitionRuntime(item.Id, this.providerFactory(item.Id), minimumConfidence),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<string> InhabitantIds => runtimes.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();

    public bool Enqueue(SocietyCognitionScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateEntry(entry);
        if (!runtimes.ContainsKey(entry.InhabitantId))
        {
            throw new InvalidOperationException($"Unknown inhabitant '{entry.InhabitantId}'.");
        }

        var existing = queue.SingleOrDefault(item => item.InhabitantId == entry.InhabitantId);
        if (existing is not null)
        {
            var merged = existing with
            {
                Priority = Math.Max(existing.Priority, entry.Priority),
                EnqueuedTick = Math.Min(existing.EnqueuedTick, entry.EnqueuedTick),
                TriggerIds = existing.TriggerIds.Concat(entry.TriggerIds)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                Observation = entry.Observation,
            };
            queue[queue.IndexOf(existing)] = merged;
            AppendEvent(entry.EnqueuedTick, "cognition_trigger_coalesced", entry.InhabitantId);
            return true;
        }

        if (queue.Count >= maxQueueLength)
        {
            AppendEvent(entry.EnqueuedTick, "cognition_backpressure", entry.ScheduleId);
            return false;
        }

        queue.Add(entry);
        AppendEvent(entry.EnqueuedTick, "cognition_queued", entry.ScheduleId);
        return true;
    }

    /// <summary>
    /// Reconciles cognition runtimes with the authoritative lifecycle set.
    /// Dead inhabitants cannot receive new work, and newborns get a fresh
    /// provider binding without disturbing surviving runtimes or queued work.
    /// </summary>
    public void SyncInhabitants(IEnumerable<SocietyInhabitant> inhabitants, IReadOnlySet<string>? bornChildIds = null)
    {
        ArgumentNullException.ThrowIfNull(inhabitants);
        var current = inhabitants.ToArray();
        var activeIds = current
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var removedId in runtimes.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            runtimes.Remove(removedId);
            queue.RemoveAll(entry => entry.InhabitantId == removedId);
            AppendEvent(0, "cognition_runtime_removed", removedId);
        }

        foreach (var inhabitant in current
                     .Where(item => item.Status == SocietyInhabitantStatus.Active)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (runtimes.ContainsKey(inhabitant.Id))
            {
                continue;
            }

            runtimes.Add(
                inhabitant.Id,
                new CognitionRuntime(inhabitant.Id, providerFactory(inhabitant.Id), minimumConfidence));
            AppendEvent(0, "cognition_runtime_added", inhabitant.Id);
        }

        // A save from before child-provider routing can contain an already
        // queued observation. Reconcile it before hosted preview or dispatch.
        var infants = current.Where(item => item.AgeBand == SocietyAgeBand.Infant)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        queue.RemoveAll(entry => infants.Contains(entry.InhabitantId));
        if (bornChildIds is not null)
        {
            for (var index = 0; index < queue.Count; index++)
                if (bornChildIds.Contains(queue[index].InhabitantId))
                    queue[index] = queue[index] with
                    {
                        Observation = queue[index].Observation with { RequiresPersonalProvider = true },
                    };
        }
    }

    public async ValueTask<IReadOnlyList<SocietyCognitionDispatchResult>> DispatchAsync(
        CancellationToken cancellationToken = default) =>
        await DispatchEligibleAsync(_ => true, cancellationToken).ConfigureAwait(false);

    public ValueTask<IReadOnlyList<SocietyCognitionDispatchResult>> DispatchDeterministicAsync(
        CancellationToken cancellationToken = default) =>
        DispatchEligibleAsync(entry => runtimes[entry.InhabitantId].ProviderKindFor(entry.Observation) ==
            DecisionProviderKind.Deterministic, cancellationToken);

    public IReadOnlyList<SocietyDeferredCognitionRequest> PreviewHostedRequests(
        IReadOnlySet<string> excludedInhabitantIds)
    {
        ArgumentNullException.ThrowIfNull(excludedInhabitantIds);
        var selected = OrderedQueue()
            .Where(entry => !excludedInhabitantIds.Contains(entry.InhabitantId) &&
                runtimes[entry.InhabitantId].ProviderKindFor(entry.Observation) != DecisionProviderKind.Deterministic)
            .Take(maxDispatchPerCycle)
            .ToArray();
        var previews = new List<SocietyDeferredCognitionRequest>(selected.Length);
        foreach (var entry in selected)
        {
            var runtime = runtimes[entry.InhabitantId];
            try
            {
                var request = runtime.PreviewRequest(entry.Observation);
                previews.Add(new SocietyDeferredCognitionRequest(
                    entry.InhabitantId,
                    request,
                    token => runtime.DecidePreviewAsync(request, token)));
            }
            catch (InvalidOperationException)
            {
                // The durable queue may outlive a pause/resume boundary. A
                // later observation will replace the stale decision point.
            }
        }
        return previews;
    }

    public IReadOnlySet<string> PendingHostedInhabitantIds() => queue
        .Where(entry => runtimes[entry.InhabitantId].ProviderKindFor(entry.Observation) != DecisionProviderKind.Deterministic)
        .Select(entry => entry.InhabitantId)
        .ToHashSet(StringComparer.Ordinal);

    public long CurrentProviderEpoch(string inhabitantId) => GetRuntime(inhabitantId).ProviderEpoch;

    public SocietyCognitionDispatchResult? CompleteDeferred(
        CognitionDecisionRequest originalRequest,
        CognitionDecisionResponse? response,
        string? failure,
        long currentRunEpoch,
        IReadOnlySet<string> legalCandidateIds)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        var inhabitantId = originalRequest.Observation.InhabitantId;
        var entry = queue.SingleOrDefault(item => item.InhabitantId == inhabitantId);
        if (entry is null || !runtimes.TryGetValue(inhabitantId, out var runtime) ||
            originalRequest.Observation.RunEpoch != currentRunEpoch ||
            originalRequest.ProviderEpoch != runtime.ProviderEpoch)
            return null;
        var currentRequest = runtime.PreviewRequest(entry.Observation);
        if (currentRequest.RequestId != originalRequest.RequestId ||
            currentRequest.Observation.RunEpoch != currentRunEpoch ||
            response is not null &&
                originalRequest.Observation.Candidates.Any(candidate => candidate.Id == response.SelectedCandidateId) &&
                !legalCandidateIds.Contains(response.SelectedCandidateId))
            return null;

        var issued = runtime.IssueRequest(originalRequest.Observation);
        CognitionAdmissionResult admission = response is null
            ? runtime.FailRequest(issued.RequestId, failure ?? "provider_failure")
            : runtime.ApplyResponse(response);
        queue.Remove(entry);
        AppendEvent(entry.Observation.WorldTick,
            admission.Accepted ? "cognition_dispatched" : "cognition_dispatch_rejected",
            $"{inhabitantId}:{admission.Outcome}");
        return new SocietyCognitionDispatchResult(inhabitantId, admission);
    }

    private IOrderedEnumerable<SocietyCognitionScheduleEntry> OrderedQueue() => queue
        .OrderByDescending(item => item.Priority)
        .ThenBy(item => item.EnqueuedTick)
        .ThenBy(item => item.InhabitantId, StringComparer.Ordinal)
        .ThenBy(item => item.ScheduleId, StringComparer.Ordinal);

    private async ValueTask<IReadOnlyList<SocietyCognitionDispatchResult>> DispatchEligibleAsync(
        Func<SocietyCognitionScheduleEntry, bool> predicate,
        CancellationToken cancellationToken)
    {
        var selected = OrderedQueue()
            .Where(predicate)
            .Take(maxDispatchPerCycle)
            .ToArray();

        async Task<SocietyCognitionDispatchResult> DispatchOneAsync(SocietyCognitionScheduleEntry entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = runtimes[entry.InhabitantId];
            CognitionAdmissionResult admission;
            try
            {
                admission = await runtime.RequestAndDecideAsync(entry.Observation, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                admission = new CognitionAdmissionResult(false, false, exception.Message, null);
            }

            if (string.Equals(admission.Outcome, "provider_cancelled", StringComparison.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new SocietyCognitionDispatchResult(entry.InhabitantId, admission);
        }

        // Each inhabitant owns an independent cognition runtime, so the
        // bounded selection may call hosted providers concurrently. Results
        // are still committed in the deterministic scheduler order below.
        // If cancellation interrupts the batch, every selected queue entry
        // remains durable for a later retry.
        var dispatched = await Task.WhenAll(selected.Select(DispatchOneAsync)).ConfigureAwait(false);
        var results = new List<SocietyCognitionDispatchResult>(selected.Length);
        for (var index = 0; index < selected.Length; index++)
        {
            var entry = selected[index];
            var result = dispatched[index];
            queue.Remove(entry);
            results.Add(result);
            AppendEvent(
                entry.Observation.WorldTick,
                result.Admission.Accepted ? "cognition_dispatched" : "cognition_dispatch_rejected",
                $"{entry.InhabitantId}:{result.Admission.Outcome}");
        }

        return results;
    }

    public CognitionRuntimeSnapshot CaptureRuntime(string inhabitantId) =>
        GetRuntime(inhabitantId).Capture();

    public SocietyCognitionSchedulerState ExportState()
    {
        var runtimeStates = runtimes.Values
            .OrderBy(runtime => runtime.InhabitantId, StringComparer.Ordinal)
            .Select(runtime => runtime.ExportState())
            .ToArray();
        return new SocietyCognitionSchedulerState(
            StateSchemaVersion,
            maxQueueLength,
            maxDispatchPerCycle,
            queue.OrderBy(item => item.ScheduleId, StringComparer.Ordinal).ToArray(),
            runtimeStates,
            events.ToArray(), eventHistoryFloor);
    }

    public static SocietyCognitionScheduler Restore(
        SocietyCognitionSchedulerState state,
        Func<string, IDecisionProvider>? providerFactory = null,
        double minimumConfidence = 0.5)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != StateSchemaVersion ||
            state.MaxQueueLength <= 0 || state.MaxDispatchPerCycle <= 0)
        {
            throw new InvalidDataException("The society cognition scheduler state is invalid.");
        }

        var inhabitants = state.Runtimes
            .Select(item => new SocietyInhabitant(
                item.InhabitantId,
                item.InhabitantId,
                0,
                SocietyInhabitantStatus.Active,
                SocietyAgeBand.Adult,
                10_000,
                null,
                null,
                SocietyWorkRole.Unassigned,
                0))
            .ToArray();
        var scheduler = new SocietyCognitionScheduler(
            inhabitants,
            providerFactory,
            state.MaxQueueLength,
            state.MaxDispatchPerCycle,
            minimumConfidence);
        scheduler.queue.AddRange(state.Queue);
        foreach (var runtimeState in state.Runtimes)
        {
            if (!scheduler.runtimes.ContainsKey(runtimeState.InhabitantId))
            {
                throw new InvalidDataException("A scheduler runtime has no matching inhabitant.");
            }

            scheduler.runtimes[runtimeState.InhabitantId] = CognitionRuntime.Restore(
                runtimeState,
                providerFactory?.Invoke(runtimeState.InhabitantId),
                minimumConfidence);
        }

        scheduler.events.AddRange(state.Events);
        scheduler.eventHistoryFloor = state.EventHistoryFloor;
        scheduler.nextEventId = checked(scheduler.eventHistoryFloor + scheduler.events.Count + 1L);
        scheduler.Validate();
        return scheduler;
    }

    public void Validate()
    {
        if (queue.Count > maxQueueLength || eventHistoryFloor < 0)
        {
            throw new InvalidDataException("The society cognition queue exceeds its configured limit.");
        }

        var expected = checked(eventHistoryFloor + 1);
        var previousTick = 0L;
        foreach (var schedulerEvent in events)
        {
            if (schedulerEvent.EventId != expected || schedulerEvent.WorldTick < previousTick)
            {
                throw new InvalidDataException("Society cognition events are not ordered.");
            }

            expected++;
            previousTick = schedulerEvent.WorldTick;
        }

        foreach (var entry in queue)
        {
            ValidateEntry(entry);
            if (!runtimes.ContainsKey(entry.InhabitantId))
            {
                throw new InvalidDataException("A queued cognition entry references an unknown inhabitant.");
            }
        }
    }

    private CognitionRuntime GetRuntime(string inhabitantId)
    {
        var id = string.IsNullOrWhiteSpace(inhabitantId) ? inhabitantId : inhabitantId.Trim();
        return runtimes.TryGetValue(id, out var runtime)
            ? runtime
            : throw new KeyNotFoundException($"Unknown inhabitant '{id}'.");
    }

    private void AppendEvent(long worldTick, string kind, string detail)
    {
        var committedTick = events.Count == 0
            ? worldTick
            : Math.Max(worldTick, events[^1].WorldTick);
        events.Add(new SocietyCognitionSchedulerEvent(
            checked(nextEventId++),
            committedTick,
            kind,
            detail));
    }

    private static void ValidateEntry(SocietyCognitionScheduleEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.InhabitantId);
        if (entry.Priority < 0 || entry.EnqueuedTick < 0 || entry.TriggerIds.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }

        entry.Observation.Validate();
        if (entry.Observation.InhabitantId != entry.InhabitantId ||
            entry.TriggerIds.Any(string.IsNullOrWhiteSpace) ||
            entry.TriggerIds.Distinct(StringComparer.Ordinal).Count() != entry.TriggerIds.Count)
        {
            throw new InvalidDataException("A cognition schedule entry has invalid identity or triggers.");
        }
    }
}
