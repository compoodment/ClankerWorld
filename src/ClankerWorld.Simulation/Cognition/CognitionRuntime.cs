using System.Globalization;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Cognition;

public enum CognitionRequestState
{
    InFlight,
    Applied,
    Fallback,
    Rejected,
    Superseded,
}

public sealed record CognitionIntention(
    string InhabitantId,
    string CandidateId,
    DecisionProviderKind Provider,
    double Confidence,
    long WorldTick,
    long RunEpoch,
    long DecisionGeneration,
    string ObservationDigest,
    CognitionUsage? Usage = null);

public sealed record CognitionRequestRecord(
    CognitionDecisionRequest Request,
    CognitionRequestState State,
    string? Outcome,
    CognitionIntention? Intention,
    CognitionUsage? Usage = null);

public sealed record CognitionEvent(
    long EventId,
    long WorldTick,
    string Kind,
    string Detail);

public sealed record CognitionRuntimeState(
    int SchemaVersion,
    string InhabitantId,
    long RunEpoch,
    bool IsPaused,
    long DecisionGeneration,
    long NextRequestSequence,
    CognitionIntention? CurrentIntention,
    IReadOnlyList<CognitionEvent> Events,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long EventHistoryFloor = 0);

public sealed record CognitionRuntimeSnapshot(
    string InhabitantId,
    DecisionProviderKind ProviderKind,
    long RunEpoch,
    bool IsPaused,
    long DecisionGeneration,
    string? InFlightRequestId,
    CognitionIntention? CurrentIntention,
    IReadOnlyList<CognitionRequestRecord> Requests,
    IReadOnlyList<CognitionEvent> Events);

public sealed record CognitionAdmissionResult(
    bool Accepted,
    bool FellBack,
    string Outcome,
    CognitionIntention? Intention,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CognitionMemoryCompactionScore>? MemoryCompactionScores = null);

/// <summary>
/// The first Phase 3 cognition boundary. It owns request admission and
/// provider-result validation, but not physical execution. A host builds the
/// observation and legal candidates, calls <see cref="IssueRequest"/>, sends
/// the request to its optional provider, and submits the typed response here.
/// </summary>
public sealed class CognitionRuntime
{
    public const int StateSchemaVersion = 1;

    private readonly object sync = new();
    private readonly IDecisionProvider provider;
    private readonly double minimumConfidence;
    private readonly List<CognitionEvent> events = [];
    private readonly Dictionary<string, CognitionRequestRecord> requests =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> retiredRequestIds = new(StringComparer.Ordinal);
    private CognitionRequestRecord? inFlight;
    private CognitionIntention? currentIntention;
    private bool isPaused;
    private long runEpoch;
    private long decisionGeneration;
    private long nextRequestSequence = 1;
    private long nextEventId = 1;
    private long eventHistoryFloor;

    public CognitionRuntime(
        string inhabitantId,
        IDecisionProvider? provider = null,
        double minimumConfidence = 0.5)
    {
        InhabitantId = NormalizeRequiredText(inhabitantId, nameof(inhabitantId));
        if (double.IsNaN(minimumConfidence) || double.IsInfinity(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        this.provider = provider ?? new DeterministicDecisionProvider();
        this.minimumConfidence = minimumConfidence;
    }

    public string InhabitantId { get; }

    public DecisionProviderKind ProviderKind => provider.Kind;

    public long ProviderEpoch => provider.ProviderEpoch;

    public DecisionProviderKind ProviderKindFor(InhabitantObservation observation) => provider.KindFor(observation);

    /// <summary>
    /// Reserves no state. A host may send this exact request outside the world
    /// tick, then admit its answer through IssueRequest/ApplyResponse only if
    /// the same decision point and provider binding are still current.
    /// </summary>
    public CognitionDecisionRequest PreviewRequest(InhabitantObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();
        if (!string.Equals(observation.InhabitantId, InhabitantId, StringComparison.Ordinal))
            throw new ArgumentException("The observation belongs to a different inhabitant.", nameof(observation));
        lock (sync)
        {
            if (isPaused || inFlight is not null || observation.RunEpoch < runEpoch)
                throw new InvalidOperationException("This cognition decision point is no longer available.");
            return new CognitionDecisionRequest(
                $"cognition-{nextRequestSequence.ToString("D10", CultureInfo.InvariantCulture)}",
                provider.ProviderEpoch,
                observation);
        }
    }

    public ValueTask<CognitionDecisionResponse> DecidePreviewAsync(
        CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
        provider.DecideAsync(request, cancellationToken);

    public CognitionDecisionRequest IssueRequest(InhabitantObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();
        if (!string.Equals(observation.InhabitantId, InhabitantId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The observation belongs to a different inhabitant.", nameof(observation));
        }

        lock (sync)
        {
            if (isPaused)
            {
                throw new InvalidOperationException("Cognition is paused.");
            }

            if (inFlight is not null)
            {
                throw new InvalidOperationException("Only one cognition request may be in flight for an inhabitant.");
            }

            if (observation.RunEpoch < runEpoch)
            {
                throw new InvalidOperationException("The observation belongs to an older run epoch.");
            }

            runEpoch = observation.RunEpoch;
            decisionGeneration = Math.Max(decisionGeneration, observation.DecisionGeneration);
            var request = new CognitionDecisionRequest(
                $"cognition-{nextRequestSequence.ToString("D10", CultureInfo.InvariantCulture)}",
                provider.ProviderEpoch,
                observation);
            nextRequestSequence = checked(nextRequestSequence + 1);
            inFlight = new CognitionRequestRecord(request, CognitionRequestState.InFlight, null, null);
            requests.Add(request.RequestId, inFlight);
            AppendEvent(observation.WorldTick, "cognition_requested", request.RequestId);
            return request;
        }
    }

    /// <summary>
    /// Convenience path for hosts that want the optional provider called by
    /// this runtime. Tests and remote adapters can instead call IssueRequest
    /// and ApplyResponse separately to control latency and failure precisely.
    /// </summary>
    public async ValueTask<CognitionAdmissionResult> RequestAndDecideAsync(
        InhabitantObservation observation,
        CancellationToken cancellationToken = default)
    {
        var request = IssueRequest(observation);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var response = await provider.DecideAsync(request, cancellationToken).ConfigureAwait(false);
                return ApplyResponse(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return FailRequest(request.RequestId, "provider_cancelled");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (attempt == 0)
                {
                    lock (sync)
                    {
                        AppendEvent(
                            request.Observation.WorldTick,
                            "cognition_retry_requested",
                            exception.GetType().Name);
                    }

                    continue;
                }

                return FailRequest(request.RequestId, $"provider_failure:{exception.GetType().Name}");
            }
        }

        return FailRequest(request.RequestId, "provider_failure:retry_exhausted");
    }

    public CognitionAdmissionResult ApplyResponse(CognitionDecisionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        lock (sync)
        {
            if (inFlight is null)
            {
                var outcome = retiredRequestIds.Contains(response.RequestId)
                    ? "superseded_request"
                    : "no_in_flight_request";
                AppendEvent(response.RunEpoch, "cognition_response_rejected", $"{response.RequestId}:{outcome}");
                return Rejected(outcome);
            }

            var request = inFlight.Request;
            var rejection = ValidateResponse(request, response);
            if (rejection is not null)
            {
                // An unusable answer to this decision is terminal, not a reason
                // to spend another hosted call on the same queued decision.
                if (rejection is "malformed_response" or "candidate_not_legal")
                    return ApplyFallbackLocked(request, rejection);
                RetireInFlight(CognitionRequestState.Rejected, rejection, null);
                AppendEvent(request.Observation.WorldTick, "cognition_response_rejected", $"{request.RequestId}:{rejection}");
                return Rejected(rejection);
            }

            if (response.Confidence < minimumConfidence)
            {
                return ApplyFallbackLocked(request, $"low_confidence:{response.Confidence.ToString("0.###", CultureInfo.InvariantCulture)}");
            }

            var candidate = request.Observation.Candidates.Single(candidate =>
                string.Equals(candidate.Id, response.SelectedCandidateId, StringComparison.Ordinal));
            var intention = new CognitionIntention(
                InhabitantId,
                candidate.Id,
                response.Provider,
                response.Confidence,
                request.Observation.WorldTick,
                request.Observation.RunEpoch,
                request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest,
                response.Usage);
            currentIntention = intention;
            RetireInFlight(CognitionRequestState.Applied, "provider_decision", intention, response.Usage);
            AppendEvent(request.Observation.WorldTick, "cognition_decision_applied", $"{response.Provider}:{candidate.Id}");
            if (response.Usage is not null)
            {
                AppendEvent(request.Observation.WorldTick, "cognition_usage_recorded", FormatUsage(response.Usage));
            }
            return new CognitionAdmissionResult(
                true,
                false,
                "provider_decision",
                intention,
                response.Provider == DecisionProviderKind.Jev ? response.MemoryCompactionScores : null);
        }
    }

    public CognitionAdmissionResult FailRequest(string requestId, string reason)
    {
        var normalizedRequestId = NormalizeRequiredText(requestId, nameof(requestId));
        var normalizedReason = NormalizeRequiredText(reason, nameof(reason));
        lock (sync)
        {
            if (inFlight is null || !string.Equals(inFlight.Request.RequestId, normalizedRequestId, StringComparison.Ordinal))
            {
                AppendEvent(runEpoch, "cognition_failure_ignored", $"{normalizedRequestId}:{normalizedReason}");
                return Rejected("no_in_flight_request");
            }

            return ApplyFallbackLocked(inFlight.Request, normalizedReason);
        }
    }

    public bool Pause(long worldTick = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        lock (sync)
        {
            if (isPaused)
            {
                return false;
            }

            isPaused = true;
            if (inFlight is not null)
            {
                retiredRequestIds.Add(inFlight.Request.RequestId);
                RetireInFlight(CognitionRequestState.Superseded, "paused", null);
            }

            AppendEvent(worldTick, "cognition_paused", "requested");
            return true;
        }
    }

    public bool Resume(long worldTick = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        lock (sync)
        {
            if (!isPaused)
            {
                return false;
            }

            isPaused = false;
            runEpoch = checked(runEpoch + 1);
            AppendEvent(worldTick, "cognition_resumed", $"epoch:{runEpoch.ToString(CultureInfo.InvariantCulture)}");
            return true;
        }
    }

    public CognitionRuntimeState ExportState()
    {
        lock (sync)
        {
            if (inFlight is not null)
            {
                retiredRequestIds.Add(inFlight.Request.RequestId);
                var worldTick = inFlight.Request.Observation.WorldTick;
                RetireInFlight(CognitionRequestState.Superseded, "saved_before_provider_response", null);
                AppendEvent(worldTick, "cognition_request_superseded", "saved_before_provider_response");
            }

            return new CognitionRuntimeState(
                StateSchemaVersion,
                InhabitantId,
                runEpoch,
                isPaused,
                decisionGeneration,
                nextRequestSequence,
                currentIntention,
                events.ToArray(), eventHistoryFloor);
        }
    }

    public static CognitionRuntime Restore(
        CognitionRuntimeState state,
        IDecisionProvider? provider = null,
        double minimumConfidence = 0.5)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != StateSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported cognition state schema '{state.SchemaVersion}'.");
        }

        var runtime = new CognitionRuntime(state.InhabitantId, provider, minimumConfidence);
        if (state.RunEpoch < 0 || state.DecisionGeneration < 0 || state.NextRequestSequence <= 0)
        {
            throw new InvalidDataException("Cognition state contains an invalid epoch or sequence.");
        }

        lock (runtime.sync)
        {
            runtime.runEpoch = state.RunEpoch;
            runtime.isPaused = state.IsPaused;
            runtime.decisionGeneration = state.DecisionGeneration;
            runtime.nextRequestSequence = state.NextRequestSequence;
            runtime.currentIntention = state.CurrentIntention;
            runtime.eventHistoryFloor = state.EventHistoryFloor;
            runtime.events.AddRange(state.Events ?? throw new InvalidDataException("Cognition events are missing."));
            runtime.ValidateEvents();
            runtime.nextEventId = checked(runtime.eventHistoryFloor + runtime.events.Count + 1L);
        }

        return runtime;
    }

    public CognitionRuntimeSnapshot Capture()
    {
        lock (sync)
        {
            return new CognitionRuntimeSnapshot(
                InhabitantId,
                provider.Kind,
                runEpoch,
                isPaused,
                decisionGeneration,
                inFlight?.Request.RequestId,
                currentIntention,
                requests.Values.OrderBy(request => request.Request.RequestId, StringComparer.Ordinal).ToArray(),
                events.ToArray());
        }
    }

    private CognitionAdmissionResult ApplyFallbackLocked(CognitionDecisionRequest request, string reason)
    {
        // A provider outage or uncertain answer is not consent to an unrelated
        // planning, social, or owner-instructed action. Ordinary deterministic
        // providers still use candidate priorities when they are configured.
        var candidate = request.Observation.Candidates.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, "safe_idle", StringComparison.Ordinal));
        if (candidate is null)
        {
            currentIntention = null;
            RetireInFlight(CognitionRequestState.Rejected, $"{reason}:no_safe_idle", null);
            AppendEvent(request.Observation.WorldTick, "cognition_fallback_rejected", $"{reason}:no_safe_idle");
            return Rejected($"{reason}:no_safe_idle");
        }
        var intention = new CognitionIntention(
            InhabitantId,
            candidate.Id,
            DecisionProviderKind.Deterministic,
            1d,
            request.Observation.WorldTick,
            request.Observation.RunEpoch,
            request.Observation.DecisionGeneration,
            request.Observation.ObservationDigest);
        currentIntention = intention;
        RetireInFlight(CognitionRequestState.Fallback, reason, intention);
        AppendEvent(request.Observation.WorldTick, "cognition_fallback_applied", $"{reason}:{candidate.Id}");
        return new CognitionAdmissionResult(true, true, reason, intention);
    }

    private string? ValidateResponse(
        CognitionDecisionRequest request,
        CognitionDecisionResponse response)
    {
        if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
        {
            return "request_id";
        }

        if (!string.Equals(response.InhabitantId, InhabitantId, StringComparison.Ordinal))
        {
            return "inhabitant_id";
        }

        if (response.ProviderEpoch != request.ProviderEpoch || response.ProviderEpoch != provider.ProviderEpoch)
        {
            return "provider_epoch";
        }

        if (response.Provider != provider.KindFor(request.Observation))
        {
            return "provider_kind";
        }

        if (response.RunEpoch != request.Observation.RunEpoch || response.RunEpoch != runEpoch)
        {
            return "run_epoch";
        }

        if (response.DecisionGeneration != request.Observation.DecisionGeneration ||
            response.DecisionGeneration != decisionGeneration)
        {
            return "decision_generation";
        }

        if (!string.Equals(response.ObservationDigest, request.Observation.ObservationDigest, StringComparison.Ordinal))
        {
            return "observation_digest";
        }

        try
        {
            response.Validate();
        }
        catch (ArgumentException)
        {
            return "malformed_response";
        }

        if (!request.Observation.Candidates.Any(candidate =>
                string.Equals(candidate.Id, response.SelectedCandidateId, StringComparison.Ordinal)))
        {
            return "candidate_not_legal";
        }

        if (response.MemoryCompactionScores is { Count: > 0 } memoryScores)
        {
            if (response.Provider != DecisionProviderKind.Jev || request.Observation.MemoryCompactionCandidates is not { } memoryCandidates)
                return "memory_compaction_not_requested";
            foreach (var score in memoryScores)
            {
                if (!string.Equals(score.OwnerId, InhabitantId, StringComparison.Ordinal) ||
                    !memoryCandidates.Any(candidate => candidate.Id == score.Id && candidate.OwnerId == score.OwnerId &&
                        candidate.Kind == score.Kind && candidate.SourceTick == score.SourceTick))
                    return "memory_compaction_source";
            }
        }

        return null;
    }

    private void RetireInFlight(
        CognitionRequestState state,
        string outcome,
        CognitionIntention? intention,
        CognitionUsage? usage = null)
    {
        if (inFlight is null)
        {
            return;
        }

        var retired = inFlight with { State = state, Outcome = outcome, Intention = intention, Usage = usage };
        requests[retired.Request.RequestId] = retired;
        retiredRequestIds.Add(retired.Request.RequestId);
        inFlight = null;
    }

    private void AppendEvent(long worldTick, string kind, string detail)
    {
        events.Add(new CognitionEvent(nextEventId++, worldTick, kind, detail));
    }

    private static CognitionAdmissionResult Rejected(string outcome) =>
        new(false, false, outcome, null);

    private static string FormatUsage(CognitionUsage usage) =>
        $"model:{usage.ModelId ?? "unknown"}:input_tokens:{usage.InputTokens}:output_tokens:{usage.OutputTokens}";

    private void ValidateEvents()
    {
        if (eventHistoryFloor < 0)
        {
            throw new InvalidDataException("The cognition event history floor is invalid.");
        }
        var expectedId = checked(eventHistoryFloor + 1);
        foreach (var worldEvent in events)
        {
            if (worldEvent.EventId != expectedId || worldEvent.WorldTick < 0 ||
                string.IsNullOrWhiteSpace(worldEvent.Kind) || string.IsNullOrWhiteSpace(worldEvent.Detail))
            {
                throw new InvalidDataException("Cognition events are not a canonical committed sequence.");
            }

            expectedId++;
        }
    }

    private static string NormalizeRequiredText(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}
