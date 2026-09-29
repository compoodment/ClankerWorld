using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldDeferredCognitionTests
{
    [Fact]
    public async Task ActorEventRetainsItsLocationAcrossSaveAndViewerProjection()
    {
        using var world = new PrivateWorldRuntime("located-events");
        for (var tick = 0; tick < 12; tick++) await world.AdvanceOneTickAsync();
        var located = Assert.Single(world.ExportState().Events
            .Where(item => item.Kind == "inhabitant_moved")
            .Take(1));
        Assert.NotNull(located.Position);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved);
        var projected = new OwnerWorldObservationStore(restored).GetEventsAfter(0).Events
            .Single(item => item.EventId == located.EventId);
        Assert.Equal(located.Position!.Value.X, projected.Position?.X);
        Assert.Equal(located.Position.Value.Y, projected.Position?.Y);
    }

    [Fact]
    public async Task SlowHostedFounderDoesNotHoldWorldOrOtherFounders()
    {
        var hosted = new HeldHostedProvider();
        using var world = new PrivateWorldRuntime("deferred-founder", id =>
            id == "founder-scout" ? hosted : new DeterministicDecisionProvider());

        var first = await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(first.Advanced);
        Assert.Equal(1, world.WorldTick);
        Assert.Contains(first.Decisions, item => item.InhabitantId != "founder-scout" && item.Admission.Accepted);
        await hosted.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains(world.ExportState().Society.Cognition.Queue, item => item.InhabitantId == "founder-scout");
        Assert.Contains(new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants
                .Single(item => item.Id == "founder-scout").DecisionFactors,
            item => item.Key == "decision-pending");

        for (var tick = 0; tick < 3; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).Advanced);
        Assert.Equal(4, world.WorldTick);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "hosted_decision_completed");

        hosted.Release.TrySetResult(true);
        await hosted.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var admitted = await AdvanceUntilAcceptedAsync(world, "founder-scout");
        Assert.Contains(admitted.Events, item => item.Kind == "hosted_decision_completed");
        Assert.DoesNotContain(world.ExportState().Society.Cognition.Queue, item => item.InhabitantId == "founder-scout");
    }

    [Fact]
    public async Task AcceptedPersonalModelThoughtIsSavedAndShownOnlyOnItsOwnersProfile()
    {
        var hosted = new HeldHostedProvider(kind: DecisionProviderKind.LargeLanguageModel,
            privateThought: "I should gather food before the others wake.");
        using var world = new PrivateWorldRuntime("private-thoughts", id =>
            id == "founder-scout" ? hosted : new DeterministicDecisionProvider());
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await hosted.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        hosted.Release.TrySetResult(true);
        await hosted.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        _ = await AdvanceUntilAcceptedAsync(world, "founder-scout");

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, saved.SchemaVersion);
        using var restored = PrivateWorldRuntime.Restore(saved);
        var people = new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants;
        Assert.Equal("I should gather food before the others wake.",
            Assert.Single(people.Single(person => person.Id == "founder-scout").RecentPrivateThoughts).Text);
        Assert.All(people.Where(person => person.Id != "founder-scout"),
            person => Assert.Empty(person.RecentPrivateThoughts));
        Assert.DoesNotContain(world.ExportState().Events,
            item => item.Detail.Contains("I should gather food", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcceptedPersonalDecisionCanNameAnAgentButCannotUndoAPlayerRename()
    {
        var firstId = "founder:" + Guid.NewGuid().ToString("N");
        var secondId = "founder:" + Guid.NewGuid().ToString("N");
        var first = new HeldHostedProvider(kind: DecisionProviderKind.LargeLanguageModel, chosenName: "Aster");
        var second = new HeldHostedProvider(kind: DecisionProviderKind.LargeLanguageModel, chosenName: "Ignored");
        using var world = new PrivateWorldRuntime("chosen-names", id => id == firstId ? first :
            id == secondId ? second : new DeterministicDecisionProvider(), startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(firstId, new GridPoint(0, 0));
        world.PlaceFounder(secondId, new GridPoint(1, 2));
        world.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new GridPoint(2, 2));
        world.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new GridPoint(3, 2));
        world.StartWorld();
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(first.NeedsNameObserved);
        first.Release.TrySetResult(true);
        await first.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        _ = await AdvanceUntilAcceptedAsync(world, firstId);
        Assert.Equal("Aster", world.Society.GetInhabitant(firstId).Name);
        Assert.False(world.Society.GetInhabitant(firstId).NeedsName);

        Assert.True(world.RenameAgent(secondId, "Player-picked"));
        second.Release.TrySetResult(true);
        await second.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        _ = await AdvanceUntilAcceptedAsync(world, secondId);
        Assert.Equal("Player-picked", world.Society.GetInhabitant(secondId).Name);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("Aster", restored.Society.GetInhabitant(firstId).Name);
        Assert.Equal("Player-picked", restored.Society.GetInhabitant(secondId).Name);
    }

    [Fact]
    public async Task PausedRequestCannotActAndSavedQueueCanBeRetriedAfterReload()
    {
        var hosted = new HeldHostedProvider(ignoreCancellation: true,
            kind: DecisionProviderKind.LargeLanguageModel, privateThought: "This stale thought must vanish.");
        using var world = new PrivateWorldRuntime("deferred-reload", id =>
            id == "founder-scout" ? hosted : new DeterministicDecisionProvider());
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await hosted.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        hosted.Release.TrySetResult(true);
        await hosted.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "hosted_decision_completed");
        Assert.Empty(world.Inhabitants.Single(person => person.InhabitantId == "founder-scout").RecentThoughts ?? []);

        var replacement = new HeldHostedProvider(kind: DecisionProviderKind.LargeLanguageModel,
            privateThought: "This new decision is mine.");
        using var restored = PrivateWorldRuntime.Restore(saved, id =>
            id == "founder-scout" ? replacement : new DeterministicDecisionProvider());
        restored.Resume();
        Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        await replacement.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        replacement.Release.TrySetResult(true);
        await replacement.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        _ = await AdvanceUntilAcceptedAsync(restored, "founder-scout");
        Assert.Equal("This new decision is mine.",
            Assert.Single(restored.Inhabitants.Single(person => person.InhabitantId == "founder-scout").RecentThoughts!).Text);
    }

    private static async Task<PrivateWorldStepResult> AdvanceUntilAcceptedAsync(PrivateWorldRuntime world, string inhabitantId)
    {
        // The provider signals just before its outer task completes. Admission
        // belongs to a later committed tick, not necessarily the first one.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var step = await world.AdvanceOneTickNonBlockingAsync();
            Assert.True(step.Advanced);
            if (step.Decisions.Any(item => item.InhabitantId == inhabitantId && item.Admission.Accepted))
                return step;
            await Task.Delay(10);
        }
        throw new TimeoutException($"The completed hosted decision for {inhabitantId} was not admitted within 20 ticks.");
    }

    private sealed class HeldHostedProvider(
        bool ignoreCancellation = false,
        DecisionProviderKind kind = DecisionProviderKind.Jev,
        string? privateThought = null,
        string? chosenName = null) : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 1;
        public bool NeedsNameObserved { get; private set; }

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            NeedsNameObserved = request.Observation.NeedsName;
            Started.TrySetResult(true);
            if (ignoreCancellation) await Release.Task;
            else await Release.Task.WaitAsync(cancellationToken);
            var selected = request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            var probabilities = request.Observation.Candidates.ToDictionary(item => item.Id,
                item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal);
            Returned.TrySetResult(true);
            return new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1d, probabilities,
                PrivateThought: privateThought, ChosenName: chosenName);
        }
    }
}
