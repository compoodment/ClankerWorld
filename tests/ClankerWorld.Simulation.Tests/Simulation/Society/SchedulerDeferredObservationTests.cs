using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class SchedulerDeferredObservationTests
{
    private const string Actor = "alice";
    private static readonly HashSet<string> NoExclusions = new(StringComparer.Ordinal);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NewChoiceOrGuidanceRemainsAvailableAfterAnOlderReplyAndReload(bool guidance, bool reload)
    {
        var scheduler = CreateScheduler();
        var earlier = Observation(1);
        Assert.True(scheduler.Enqueue(Entry(earlier, "routine_tick")));
        var first = Preview(scheduler);
        var newer = Observation(2) with
        {
            Candidates = guidance ? earlier.Candidates :
                [new("safe_idle", "Rest safely."), new("read_proposal", "Read the new proposal.")],
            ObserverGuidance = guidance ? [Guidance()] : [],
        };
        Assert.True(scheduler.Enqueue(Entry(newer, "new_information")));

        var firstDecision = scheduler.CompleteDeferred(first, Reply(first), null, 0, Legal(newer));

        Assert.NotNull(firstDecision);
        Assert.True(firstDecision.Admission.Accepted);
        Assert.Equal(earlier.ObservationDigest, firstDecision.Admission.Intention!.ObservationDigest);
        Assert.Null(firstDecision.Admission.ObserverGuidance);
        var retained = Assert.Single(scheduler.ExportState().Queue);
        Assert.Equal(newer.ObservationDigest, retained.Observation.ObservationDigest);
        Assert.Contains("new_information", retained.TriggerIds);
        if (reload)
        {
            var saved = JsonSerializer.Serialize(scheduler.ExportState());
            scheduler = SocietyCognitionScheduler.Restore(
                JsonSerializer.Deserialize<SocietyCognitionSchedulerState>(saved)!, _ => new PreviewProvider());
            Assert.Equal(saved, JsonSerializer.Serialize(scheduler.ExportState()));
        }

        var next = Preview(scheduler);
        Assert.NotEqual(first.RequestId, next.RequestId);
        Assert.Equal(newer.ObservationDigest, next.Observation.ObservationDigest);
        if (guidance) Assert.Equal(Guidance(), Assert.Single(next.Observation.ObserverGuidance!));
        else Assert.Contains(next.Observation.Candidates, candidate => candidate.Id == "read_proposal");

        // A duplicate completion of the old call must not consume the retained work.
        Assert.Null(scheduler.CompleteDeferred(first, Reply(first), null, 0, Legal(newer)));
        Assert.Equal(next.RequestId, Preview(scheduler).RequestId);
        var completed = scheduler.CompleteDeferred(next, Reply(next), null, 0, Legal(newer));
        Assert.NotNull(completed);
        Assert.True(completed.Admission.Accepted);
        if (guidance) Assert.Equal(Guidance(), Assert.Single(completed.Admission.ObserverGuidance!.Messages));
        Assert.Empty(scheduler.ExportState().Queue);
        Assert.Empty(scheduler.PreviewHostedRequests(NoExclusions));
        var durableRuntime = Assert.Single(scheduler.ExportState().Runtimes);
        Assert.Equal(3, durableRuntime.NextRequestSequence);
        Assert.Equal([first.RequestId, next.RequestId], durableRuntime.Events
            .Where(item => item.Kind == "cognition_requested").Select(item => item.Detail).ToArray());
        scheduler.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterTickDigestAndHungerWithoutNewChoicesDoNotRequestAnotherCall(bool removeChoice)
    {
        var scheduler = CreateScheduler();
        var earlier = Observation(1) with
        {
            Candidates = [new("safe_idle", "Rest safely."), new("seek_food", "Seek nearby food.")],
        };
        Assert.True(scheduler.Enqueue(Entry(earlier, "routine_tick")));
        var first = Preview(scheduler);
        var later = Observation(5) with
        {
            HungerBasisPoints = earlier.HungerBasisPoints + 40,
            Candidates = removeChoice ? [new("safe_idle", "Rest safely.")] : earlier.Candidates.Reverse().ToArray(),
        };
        Assert.True(scheduler.Enqueue(Entry(later, "routine_tick")));

        var result = scheduler.CompleteDeferred(first, Reply(first), null, 0, Legal(later));

        Assert.NotNull(result);
        Assert.True(result.Admission.Accepted);
        Assert.Equal(first.Observation.ObservationDigest, result.Admission.Intention!.ObservationDigest);
        Assert.Empty(scheduler.ExportState().Queue);
        Assert.Empty(scheduler.PreviewHostedRequests(NoExclusions));
        Assert.Single(scheduler.CaptureRuntime(Actor).Requests);
        Assert.Equal(2, Assert.Single(scheduler.ExportState().Runtimes).NextRequestSequence);
    }

    [Fact]
    public void QueuingAnOlderNameRetryPreservesTheNewerObservationAndOtherTriggers()
    {
        var scheduler = CreateScheduler();
        var original = Observation(1) with { NeedsName = true };
        var newer = Observation(3) with
        {
            NeedsName = true,
            Candidates = [new("safe_idle", "Rest safely."), new("read_proposal", "Read the new proposal.")],
            ObserverGuidance = [Guidance()],
        };
        Assert.True(scheduler.Enqueue(Entry(newer, "new_information")));
        Assert.True(scheduler.Enqueue(Entry(original, SocietyCognitionScheduler.NameRetryTriggerId)));

        var queued = Assert.Single(scheduler.ExportState().Queue);
        Assert.Equal(newer.ObservationDigest, queued.Observation.ObservationDigest);
        Assert.Contains("new_information", queued.TriggerIds);
        Assert.Contains(SocietyCognitionScheduler.NameRetryTriggerId, queued.TriggerIds);
        var retry = Preview(scheduler);
        Assert.True(retry.Observation.IsNameRetry);
        Assert.True(retry.Observation.NeedsName);
        Assert.Equal(newer.DecisionGeneration, retry.Observation.DecisionGeneration);
        Assert.Contains(retry.Observation.Candidates, candidate => candidate.Id == "read_proposal");
        Assert.Equal(Guidance(), Assert.Single(retry.Observation.ObserverGuidance!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletingNamingKeepsNewerWorkWithoutAnotherNamingAttempt(bool retryingName)
    {
        var scheduler = CreateScheduler();
        var original = Observation(1) with { NeedsName = true };
        Assert.True(scheduler.Enqueue(Entry(original,
            retryingName ? SocietyCognitionScheduler.NameRetryTriggerId : "routine_tick")));
        var retry = Preview(scheduler);
        Assert.Equal(retryingName, retry.Observation.IsNameRetry);
        var newer = Observation(2) with
        {
            NeedsName = true,
            Candidates = [new("safe_idle", "Rest safely."), new("read_proposal", "Read the new proposal.")],
        };
        Assert.True(scheduler.Enqueue(Entry(newer, "new_information")));

        var result = scheduler.CompleteDeferred(retry, Reply(retry), null, 0, Legal(newer));

        Assert.NotNull(result);
        Assert.True(result.Admission.Accepted);
        var retained = Assert.Single(scheduler.ExportState().Queue);
        Assert.Contains("new_information", retained.TriggerIds);
        Assert.DoesNotContain(SocietyCognitionScheduler.NameRetryTriggerId, retained.TriggerIds);
        if (retryingName) Assert.Single(retained.TriggerIds);

        // The host closes naming after admitting the reply. This also applies
        // when the consumed retry trigger has already left the retained queue.
        scheduler.SyncInhabitants([SocietyFixture.CreateFounder(Actor, "Alice Vale") with { NeedsName = false }]);
        Assert.False(Assert.Single(scheduler.ExportState().Queue).Observation.NeedsName);
        var next = Preview(scheduler);
        Assert.NotEqual(retry.RequestId, next.RequestId);
        Assert.False(next.Observation.IsNameRetry);
        Assert.False(next.Observation.NeedsName);
        Assert.Contains(next.Observation.Candidates, candidate => candidate.Id == "read_proposal");
        Assert.NotNull(scheduler.CompleteDeferred(next, Reply(next), null, 0, Legal(newer)));
        Assert.Empty(scheduler.ExportState().Queue);
    }

    private static SocietyCognitionScheduler CreateScheduler() => new(
        [SocietyFixture.CreateFounder(Actor, "Alice")], _ => new PreviewProvider());

    private static InhabitantObservation Observation(int generation) => new(
        Actor, generation, 0, generation, $"sha256:observation-{generation}", 100,
        [new("safe_idle", "Rest safely.")])
    { WorldId = "scheduler-new-information" };

    private static SocietyCognitionScheduleEntry Entry(InhabitantObservation observation, string trigger) =>
        new($"tick:{observation.WorldTick}:{trigger}", Actor, 1, observation.WorldTick, [trigger], observation);

    private static CognitionObserverGuidance Guidance() => new(
        "instruction-1", "owner:test", Actor, "suggestive", "Read the new proposal.", 2, 0, 1, null, true);

    private static CognitionDecisionRequest Preview(SocietyCognitionScheduler scheduler) =>
        Assert.Single(scheduler.PreviewHostedRequests(NoExclusions)).Request;

    private static HashSet<string> Legal(InhabitantObservation observation) =>
        observation.Candidates.Select(candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);

    private static CognitionDecisionResponse Reply(CognitionDecisionRequest request) => new(
        request.RequestId, Actor, DecisionProviderKind.LargeLanguageModel, request.ProviderEpoch,
        request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
        "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 });

    private sealed class PreviewProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The test completes captured requests explicitly.");
    }
}
