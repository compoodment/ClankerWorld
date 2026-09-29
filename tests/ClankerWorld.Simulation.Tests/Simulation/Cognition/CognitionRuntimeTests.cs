using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class CognitionRuntimeTests
{
    [Fact]
    public async Task DefaultProviderIsDeterministicAndChoosesTheSafestCandidate()
    {
        var runtime = new CognitionRuntime("actor-scout");
        var result = await runtime.RequestAndDecideAsync(CreateObservation());

        Assert.Equal(DecisionProviderKind.Deterministic, runtime.ProviderKind);
        Assert.True(result.Accepted);
        Assert.False(result.FellBack);
        Assert.Equal("safe_idle", result.Intention?.CandidateId);
        Assert.Equal("cognition_decision_applied", runtime.Capture().Events[^1].Kind);
    }

    [Fact]
    public void ValidProviderResponseBecomesAnInspectableIntention()
    {
        var runtime = new CognitionRuntime("actor-scout", new FixedProvider(DecisionProviderKind.Jev, 7));
        var request = runtime.IssueRequest(CreateObservation());

        var result = runtime.ApplyResponse(ResponseFor(
            request,
            DecisionProviderKind.Jev,
            providerEpoch: 7,
            selectedCandidateId: "seek_food",
            confidence: 0.84));

        Assert.True(result.Accepted);
        Assert.False(result.FellBack);
        Assert.Equal("seek_food", result.Intention?.CandidateId);
        Assert.Equal(DecisionProviderKind.Jev, result.Intention?.Provider);
        Assert.Null(runtime.Capture().InFlightRequestId);
        Assert.Equal(CognitionRequestState.Applied, runtime.Capture().Requests.Single().State);
    }

    [Fact]
    public void LowConfidenceProviderResponseFallsBackToDeterministicCandidate()
    {
        var runtime = new CognitionRuntime("actor-scout", new FixedProvider(DecisionProviderKind.Jev, 2));
        var request = runtime.IssueRequest(CreateObservation());

        var result = runtime.ApplyResponse(ResponseFor(
            request,
            DecisionProviderKind.Jev,
            providerEpoch: 2,
            selectedCandidateId: "seek_food",
            confidence: 0.2));

        Assert.True(result.Accepted);
        Assert.True(result.FellBack);
        Assert.Equal("safe_idle", result.Intention?.CandidateId);
        Assert.Equal(CognitionRequestState.Fallback, runtime.Capture().Requests.Single().State);
    }

    [Fact]
    public void StaleObservationDigestIsRejectedWithoutChangingTheCurrentIntention()
    {
        var runtime = new CognitionRuntime("actor-scout", new FixedProvider(DecisionProviderKind.Jev, 0));
        var request = runtime.IssueRequest(CreateObservation());

        var rejected = runtime.ApplyResponse(ResponseFor(
            request,
            DecisionProviderKind.Jev,
            providerEpoch: 0,
            selectedCandidateId: "seek_food",
            confidence: 0.9) with
        {
            ObservationDigest = "sha256:old-observation",
        });

        Assert.False(rejected.Accepted);
        Assert.Equal("observation_digest", rejected.Outcome);
        Assert.Null(runtime.Capture().CurrentIntention);
        Assert.Equal(CognitionRequestState.Rejected, runtime.Capture().Requests.Single().State);
    }

    [Fact]
    public void DuplicateResponseAfterAdmissionCannotApplyTwice()
    {
        var runtime = new CognitionRuntime("actor-scout", new FixedProvider(DecisionProviderKind.Jev, 0));
        var request = runtime.IssueRequest(CreateObservation());
        var response = ResponseFor(request, DecisionProviderKind.Jev, 0, "seek_food", 0.9);

        Assert.True(runtime.ApplyResponse(response).Accepted);
        var duplicate = runtime.ApplyResponse(response);

        Assert.False(duplicate.Accepted);
        Assert.Equal("superseded_request", duplicate.Outcome);
        Assert.Single(runtime.Capture().Requests);
        Assert.Equal(CognitionRequestState.Applied, runtime.Capture().Requests.Single().State);
    }

    [Fact]
    public void OnlyOneRequestMayBeInFlight()
    {
        var runtime = new CognitionRuntime("actor-scout");
        _ = runtime.IssueRequest(CreateObservation());

        var exception = Assert.Throws<InvalidOperationException>(() => runtime.IssueRequest(
            CreateObservation(decisionGeneration: 1)));

        Assert.Contains("one cognition request", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PauseSupersedesAnInFlightRequestAndLateResponseCannotApply()
    {
        var runtime = new CognitionRuntime("actor-scout", new FixedProvider(DecisionProviderKind.Jev, 0));
        var request = runtime.IssueRequest(CreateObservation());

        Assert.True(runtime.Pause(worldTick: 4));
        var late = runtime.ApplyResponse(ResponseFor(
            request,
            DecisionProviderKind.Jev,
            providerEpoch: 0,
            selectedCandidateId: "seek_food",
            confidence: 0.9));

        Assert.False(late.Accepted);
        Assert.Equal("superseded_request", late.Outcome);
        Assert.Null(runtime.Capture().CurrentIntention);
        Assert.True(runtime.Resume(worldTick: 4));
        Assert.Equal(1, runtime.Capture().RunEpoch);
    }

    [Fact]
    public async Task ProviderFailureUsesLocalFallbackInsteadOfMutatingTheWorld()
    {
        var runtime = new CognitionRuntime("actor-scout", new ThrowingProvider());

        var result = await runtime.RequestAndDecideAsync(CreateObservation() with
        {
            Candidates =
            [
                new CognitionCandidate("build:home", "Build a home.", 0),
                new CognitionCandidate("safe_idle", "Wait safely.", 100),
            ],
        });

        Assert.True(result.Accepted);
        Assert.True(result.FellBack);
        Assert.Equal("safe_idle", result.Intention?.CandidateId);
        Assert.Contains("provider_failure", result.Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavedCognitionStateRestoresTheLastIntentionAndEventSequence()
    {
        var runtime = new CognitionRuntime("actor-scout");
        _ = await runtime.RequestAndDecideAsync(CreateObservation());
        var before = runtime.Capture();

        var restored = CognitionRuntime.Restore(runtime.ExportState());
        var after = restored.Capture();

        Assert.Equal(before.CurrentIntention, after.CurrentIntention);
        Assert.Equal(before.Events, after.Events);
        Assert.Null(after.InFlightRequestId);
        Assert.Equal(before.Events[^1].EventId + 1, after.Events[^1].EventId + 1);
    }

    private static InhabitantObservation CreateObservation(
        long runEpoch = 0,
        long decisionGeneration = 0) => new(
        "actor-scout",
        WorldTick: 4,
        runEpoch,
        decisionGeneration,
        "sha256:observation-4",
        HungerBasisPoints: 2_000,
        [
            new CognitionCandidate("safe_idle", "Continue the current safe routine.", 0),
            new CognitionCandidate("seek_food", "Travel toward available food.", 10, "berry-patch"),
        ]);

    private static CognitionDecisionResponse ResponseFor(
        CognitionDecisionRequest request,
        DecisionProviderKind provider,
        long providerEpoch,
        string selectedCandidateId,
        double confidence) => new(
        request.RequestId,
        request.Observation.InhabitantId,
        provider,
        providerEpoch,
        request.Observation.RunEpoch,
        request.Observation.DecisionGeneration,
        request.Observation.ObservationDigest,
        selectedCandidateId,
        confidence,
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [selectedCandidateId] = confidence,
        });

    private sealed class FixedProvider(DecisionProviderKind kind, long providerEpoch) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;

        public long ProviderEpoch => providerEpoch;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResponseFor(request, kind, providerEpoch, "seek_food", 0.9));
    }

    private sealed class ThrowingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;

        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider unavailable");
    }
}
