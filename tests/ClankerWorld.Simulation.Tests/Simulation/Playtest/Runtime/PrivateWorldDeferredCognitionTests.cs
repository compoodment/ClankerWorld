using System.Collections.Concurrent;
using System.Text;
using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldDeferredCognitionTests
{
    private const string NameTargetId = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NameOwnerId = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task DuplicateNameIsRetriedOnceWithCanonicalComparisonAndASeparateProviderRequest()
    {
        var provider = new SequencedHostedProvider(
            new NameReply("e\u0301LODIE   Vale", SelectedCandidateId: "unknown_action"),
            new NameReply("Maia Vale"));
        using var world = CreateNameTestWorld("duplicate-name-retry", provider);
        const string takenName = "  Élodie\u00a0Vale  ";
        Assert.True(world.RenameAgent(NameOwnerId, takenName));
        var placeholder = world.Society.GetInhabitant(NameTargetId).Name;
        world.StartWorld();

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var first = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        var rejectedAction = Assert.Single(first.Decisions, item => item.InhabitantId == NameTargetId).Admission;
        Assert.True(rejectedAction.FellBack);
        Assert.Equal("candidate_not_legal", rejectedAction.Outcome);
        Assert.Equal(placeholder, world.Society.GetInhabitant(NameTargetId).Name);
        Assert.True(world.Society.GetInhabitant(NameTargetId).NeedsName);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "agent_name_retry_requested" && item.Detail == NameTargetId);

        var retry = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        Assert.True(Assert.Single(retry.Decisions, item => item.InhabitantId == NameTargetId).Admission.Accepted);
        Assert.Equal(2, provider.CallCount);
        Assert.False(provider.ObservedRequests.ElementAt(0).IsNameRetry);
        Assert.True(provider.ObservedRequests.ElementAt(1).IsNameRetry);
        Assert.True(provider.ObservedRequests.ElementAt(1).NeedsName);
        Assert.Equal("Maia Vale", world.Society.GetInhabitant(NameTargetId).Name);
        Assert.False(world.Society.GetInhabitant(NameTargetId).NeedsName);
        Assert.DoesNotContain(world.ExportState().Events, item =>
            item.Detail.Contains("Élodie", StringComparison.OrdinalIgnoreCase) ||
            item.Detail.Contains("Maia Vale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateNameRetryConsumesASecondConfiguredProviderAllowance()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-name-retry-meter-");
        try
        {
            var configuration = new ProviderConfigurationStore(
                Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = configuration.Configure(new("routine", "openai", "retry-meter-model", "retry-meter-key", false));
            _ = configuration.Configure(new("planning", "openai", "retry-meter-model", "retry-meter-key", false));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            _ = usage.Configure(new ProviderUsageLimitAction(2));
            var handler = new SequencedNameResponseHandler("Taken Name", "Maia Vale");
            var router = new ConfigurableDecisionProvider(configuration,
                new FixedHttpClientFactory(handler), usageStore: usage);
            using var world = CreateNameTestWorld("duplicate-name-retry-meter", router);
            Assert.True(world.RenameAgent(NameOwnerId, "Taken Name"));
            world.StartWorld();

            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
            Assert.InRange(handler.RequestCount, 1, 2);
            Assert.InRange(usage.Capture().Attempts, 1, 2);
            Assert.True(world.Society.GetInhabitant(NameTargetId).NeedsName);

            _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
            Assert.Equal(2, handler.RequestCount);
            var metering = usage.Capture();
            Assert.Equal(2, metering.Attempts);
            Assert.Equal(2, metering.Completed);
            Assert.Equal(2, metering.Rows.Sum(row => row.Attempts));
            Assert.Equal("Maia Vale", world.Society.GetInhabitant(NameTargetId).Name);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MalformedReplyDoesNotApplyItsNameOrStartTheDuplicateNameRetry()
    {
        var provider = new SequencedHostedProvider(new NameReply("Taken Name", 1.2));
        using var world = CreateNameTestWorld("malformed-name-reply", provider);
        Assert.True(world.RenameAgent(NameOwnerId, "Taken Name"));
        var placeholder = world.Society.GetInhabitant(NameTargetId).Name;
        world.StartWorld();

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var result = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        var admission = Assert.Single(result.Decisions, item => item.InhabitantId == NameTargetId).Admission;

        Assert.True(admission.FellBack);
        Assert.Equal("malformed_response", admission.Outcome);
        Assert.Equal(placeholder, world.Society.GetInhabitant(NameTargetId).Name);
        Assert.True(world.Society.GetInhabitant(NameTargetId).NeedsName);
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "agent_name_retry_requested");
    }

    [Fact]
    public async Task FailedNameRetryKeepsPlaceholderAndClosesTheNamingAttempt()
    {
        var provider = new SequencedHostedProvider(
            [new NameReply("Taken Name")], false, false, true);
        using var world = CreateNameTestWorld("failed-name-retry", provider);
        Assert.True(world.RenameAgent(NameOwnerId, "Taken Name"));
        var placeholder = world.Society.GetInhabitant(NameTargetId).Name;
        world.StartWorld();

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        await provider.SecondFailed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);

        var namedAgent = world.Society.GetInhabitant(NameTargetId);
        Assert.Equal(placeholder, namedAgent.Name);
        Assert.False(namedAgent.NeedsName);
        var requests = provider.ObservedRequests.ToArray();
        Assert.True(requests.Length >= 2);
        Assert.True(requests[1].IsNameRetry);
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "agent_name_retry_unusable" && item.Detail == NameTargetId);
        Assert.Single(world.ExportState().Events,
            item => item.Kind == "agent_name_retry_requested" && item.Detail == NameTargetId);
        Assert.DoesNotContain(world.ExportState().Society.Cognition.Queue,
            entry => entry.InhabitantId == NameTargetId && entry.TriggerIds.Contains(
                SocietyCognitionScheduler.NameRetryTriggerId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task SecondDuplicateKeepsPlaceholderAndDoesNotStartAnotherNamingRequest()
    {
        var provider = new SequencedHostedProvider(
            new NameReply("Taken Name"),
            new NameReply("taken   name"));
        using var world = CreateNameTestWorld("duplicate-name-exhausted", provider);
        Assert.True(world.RenameAgent(NameOwnerId, "Taken Name"));
        var placeholder = world.Society.GetInhabitant(NameTargetId).Name;
        world.StartWorld();

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        // How many ticks a hosted reply takes depends on the runner, so an ordinary
        // re-evaluation may also start; only the two naming requests may ask for a name.
        Assert.Equal(2, provider.ObservedRequests.Count(request => request.NeedsName || request.IsNameRetry));
        Assert.Equal(placeholder, world.Society.GetInhabitant(NameTargetId).Name);
        Assert.False(world.Society.GetInhabitant(NameTargetId).NeedsName);
        Assert.Contains(world.ExportState().Events,
            item => item.Kind == "agent_name_retry_exhausted" && item.Detail == NameTargetId);

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(2, provider.ObservedRequests.Count(request => request.NeedsName || request.IsNameRetry));
    }

    [Fact]
    public async Task SameTickDuplicateNamesGoToStableIdWinner()
    {
        var firstProvider = new SequencedHostedProvider(new NameReply("Shared Name"));
        var secondProvider = new SequencedHostedProvider(
            new NameReply("Shared Name"),
            new NameReply("Second Name"));
        using var world = new PrivateWorldRuntime("same-tick-duplicate-names", id => id switch
        {
            NameTargetId => firstProvider,
            NameOwnerId => secondProvider,
            _ => new DeterministicDecisionProvider(),
        }, startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(NameTargetId, new GridPoint(0, 0));
        world.PlaceFounder(NameOwnerId, new GridPoint(1, 2));
        world.PlaceFounder("founder:cccccccccccccccccccccccccccccccc", new GridPoint(2, 2));
        world.PlaceFounder("founder:dddddddddddddddddddddddddddddddd", new GridPoint(3, 2));
        var secondPlaceholder = world.Society.GetInhabitant(NameOwnerId).Name;
        world.StartWorld();

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await Task.WhenAll(firstProvider.FirstReturned.Task, secondProvider.FirstReturned.Task)
            .WaitAsync(TimeSpan.FromSeconds(3));
        var simultaneous = await world.AdvanceOneTickNonBlockingAsync();
        Assert.True(simultaneous.Advanced);
        Assert.Contains(simultaneous.Decisions, item => item.InhabitantId == NameTargetId && item.Admission.Accepted);
        Assert.Contains(simultaneous.Decisions, item => item.InhabitantId == NameOwnerId && item.Admission.Accepted);
        Assert.Equal("Shared Name", world.Society.GetInhabitant(NameTargetId).Name);
        Assert.Equal(secondPlaceholder, world.Society.GetInhabitant(NameOwnerId).Name);
        Assert.True(world.Society.GetInhabitant(NameOwnerId).NeedsName);

        _ = await AdvanceUntilAcceptedAsync(world, NameOwnerId);
        Assert.True(secondProvider.ObservedRequests.ElementAt(1).IsNameRetry);
        Assert.Equal("Second Name", world.Society.GetInhabitant(NameOwnerId).Name);
    }

    [Fact]
    public async Task DuplicateNameRetryMarkerSurvivesPauseSaveRestoreAndIgnoresCanceledReply()
    {
        var provider = new SequencedHostedProvider(
            [new NameReply("Taken Name"), new NameReply("Restored Name")],
            holdSecond: true,
            ignoreSecondCancellation: true);
        using var world = CreateNameTestWorld("duplicate-name-retry-save", provider);
        Assert.True(world.RenameAgent(NameOwnerId, "Taken Name"));
        var placeholder = world.Society.GetInhabitant(NameTargetId).Name;
        world.StartWorld();

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        await provider.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(provider.SecondObservation!.IsNameRetry);
        var completedBeforeCancellation = world.ExportState().Events.Count(item =>
            item.Kind == "hosted_decision_completed");
        world.Pause();
        var savedJson = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var saved = PrivateWorldRuntimeCodec.Decode(savedJson);
        Assert.Contains(SocietyCognitionScheduler.NameRetryTriggerId,
            saved.Society.Cognition.Queue.Single(item => item.InhabitantId == NameTargetId).TriggerIds);
        Assert.False(Encoding.UTF8.GetString(savedJson).Contains("isNameRetry", StringComparison.OrdinalIgnoreCase));

        provider.ReleaseSecond.TrySetResult(true);
        await provider.SecondReturned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(completedBeforeCancellation, world.ExportState().Events.Count(item =>
            item.Kind == "hosted_decision_completed"));
        Assert.Equal(placeholder, world.Society.GetInhabitant(NameTargetId).Name);

        var restoredProvider = new SequencedHostedProvider(new NameReply("Restored Name"));
        using var restored = PrivateWorldRuntime.Restore(saved, id => id == NameTargetId
            ? restoredProvider
            : new DeterministicDecisionProvider());
        restored.Resume();
        Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        await restoredProvider.FirstReturned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var completed = await AdvanceUntilAcceptedAsync(restored, NameTargetId);
        Assert.True(Assert.Single(completed.Decisions, item => item.InhabitantId == NameTargetId).Admission.Accepted);
        Assert.True(Assert.Single(restoredProvider.ObservedRequests).IsNameRetry);
        Assert.Equal("Restored Name", restored.Society.GetInhabitant(NameTargetId).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayerRenameCancelsQueuedNamingWorkAndPreservesOtherTriggers(bool otherWork)
    {
        var provider = new SequencedHostedProvider(
            [new NameReply("Taken Name"), new NameReply("Retry Name")], holdSecond: true);
        using var world = CreateNameTestWorld("rename-queued-name-retry", provider, quietOthers: true);
        Assert.True(world.RenameAgent(NameOwnerId, "Taken Name"));
        world.StartWorld();
        await world.AdvanceOneTickNonBlockingAsync();
        _ = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        await provider.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        world.Pause();
        var saved = world.ExportState();
        saved = saved with
        {
            Society = saved.Society with
            {
                Cognition = saved.Society.Cognition with
                {
                    Queue = saved.Society.Cognition.Queue.Select(entry => entry.InhabitantId == NameTargetId
                        ? entry with
                        {
                            TriggerIds = otherWork ? [SocietyCognitionScheduler.NameRetryTriggerId, "other_work"]
                            : [SocietyCognitionScheduler.NameRetryTriggerId]
                        } : entry).ToArray(),
                }
            }
        };
        var freshProvider = new SequencedHostedProvider(new NameReply("Unexpected Retry"));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)),
            id => id == NameTargetId ? freshProvider : new QuietDecisionProvider());
        Assert.True(restored.RenameAgent(NameTargetId, "Player Name"));
        var queued = restored.ExportState().Society.Cognition.Queue.SingleOrDefault(entry => entry.InhabitantId == NameTargetId);
        if (otherWork) Assert.Equal(["other_work"], queued!.TriggerIds);
        else Assert.Null(queued);
        restored.Resume();
        await restored.AdvanceOneTickNonBlockingAsync();
        await Task.Delay(100);
        Assert.DoesNotContain(freshProvider.ObservedRequests, request => request.IsNameRetry || request.NeedsName);
        if (!otherWork) Assert.Equal(0, freshProvider.CallCount);
        Assert.Equal("Player Name", restored.Society.GetInhabitant(NameTargetId).Name);
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
            privateThought: "I should gather food before the others wake.", chosenName: "Aster Vale");
        using var world = CreateNameTestWorld("private-thoughts", hosted);
        world.StartWorld();
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await hosted.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(hosted.NeedsNameObserved);
        Assert.True(world.RenameAgent(NameTargetId, "Player-picked"));
        Assert.False(world.Society.IsPaused);
        hosted.Release.TrySetResult(true);
        await hosted.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var admitted = await AdvanceUntilAcceptedAsync(world, NameTargetId);
        Assert.False(Assert.Single(admitted.Decisions, item => item.InhabitantId == NameTargetId).Admission.FellBack);
        Assert.Equal("Player-picked", world.Society.GetInhabitant(NameTargetId).Name);

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, saved.SchemaVersion);
        using var restored = PrivateWorldRuntime.Restore(saved);
        Assert.Equal("Player-picked", restored.Society.GetInhabitant(NameTargetId).Name);
        var people = new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants;
        Assert.Equal("I should gather food before the others wake.",
            Assert.Single(people.Single(person => person.Id == NameTargetId).RecentPrivateThoughts).Text);
        Assert.All(people.Where(person => person.Id != NameTargetId),
            person => Assert.Empty(person.RecentPrivateThoughts));
        Assert.DoesNotContain(world.ExportState().Events,
            item => item.Detail.Contains("I should gather food", StringComparison.Ordinal));
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

    [Fact]
    public async Task PausedObserverReplyRetriesTheSameMessageAndCannotAttachToANewerMessage()
    {
        const string targetId = "founder-scout";
        var staleProvider = new HeldGuidanceReplyProvider("Stale reply must not be saved.");
        using var world = new PrivateWorldRuntime("paused-observer-guidance", id =>
            id == targetId ? staleProvider : new DeterministicDecisionProvider());
        var first = world.SubmitInstruction(new OwnerInstructionRequest("observer-first", "owner:test", targetId,
            OwnerInstructionKind.Suggestive, "Try the berries beside the river."));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await staleProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var initialObservation = Assert.Single(staleProvider.ObservedRequests);
        Assert.Equal(world.Society.WorldId, initialObservation.WorldId);
        Assert.Equal(targetId, initialObservation.InhabitantId);
        Assert.Equal(first.InstructionId, Assert.Single(initialObservation.ObserverGuidance!).InstructionId);

        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        staleProvider.Release.TrySetResult(true);
        await staleProvider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var pausedInstruction = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == first.InstructionId);
        Assert.Null(pausedInstruction.ObservedTick);
        Assert.Null(pausedInstruction.ObserverReply);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "hosted_decision_completed");

        var replacement = new HeldGuidanceReplyProvider("Fresh reply for the first message.");
        using var restored = PrivateWorldRuntime.Restore(saved, id =>
            id == targetId ? replacement : new DeterministicDecisionProvider());
        var second = restored.SubmitInstruction(new OwnerInstructionRequest("observer-second", "owner:test", targetId,
            OwnerInstructionKind.Suggestive, "Look for a grove after you eat."));
        restored.Resume();
        Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        await replacement.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var retryObservation = Assert.Single(replacement.ObservedRequests);
        Assert.Equal(saved.Society.Society.WorldId, retryObservation.WorldId);
        Assert.Equal(targetId, retryObservation.InhabitantId);
        Assert.Equal([first.InstructionId, second.InstructionId], retryObservation.ObserverGuidance!
            .Select(item => item.InstructionId).ToArray());
        replacement.Release.TrySetResult(true);
        _ = await AdvanceUntilAcceptedAsync(restored, targetId);

        var final = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var firstSaved = Assert.Single(final.Instructions!, item => item.InstructionId == first.InstructionId);
        var secondSaved = Assert.Single(final.Instructions!, item => item.InstructionId == second.InstructionId);
        Assert.NotNull(firstSaved.ObservedTick);
        Assert.Equal("Fresh reply for the first message.", firstSaved.ObserverReply);
        Assert.Contains(first.InstructionId, final.CompletedInstructionIds ?? []);
        Assert.NotNull(secondSaved.ObservedTick);
        Assert.Null(secondSaved.ObserverReply);
        Assert.Contains(second.InstructionId, final.CompletedInstructionIds ?? []);

        using var roundtripped = PrivateWorldRuntime.Restore(final);
        var projection = new OwnerWorldObservationStore(roundtripped).GetSnapshot();
        var targetMessages = projection.Instructions.Where(item => item.TargetInhabitantId == targetId).ToArray();
        Assert.Equal(2, targetMessages.Length);
        Assert.Equal(first.InstructionId, targetMessages[0].InstructionId);
        Assert.Equal(firstSaved.ObserverReply, targetMessages[0].ObserverReply);
        Assert.Equal(second.InstructionId, targetMessages[1].InstructionId);
        Assert.NotNull(targetMessages[1].ObservedTick);
        Assert.Null(targetMessages[1].ObserverReply);
        Assert.DoesNotContain(projection.Instructions, item => item.TargetInhabitantId != targetId);
        roundtripped.Validate();
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

    private static PrivateWorldRuntime CreateNameTestWorld(
        string seed,
        IDecisionProvider provider,
        bool quietOthers = false)
    {
        var world = new PrivateWorldRuntime(seed, id => id == NameTargetId
                ? provider
                : quietOthers ? new QuietDecisionProvider() : new DeterministicDecisionProvider(),
            startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(NameTargetId, new GridPoint(0, 0));
        world.PlaceFounder(NameOwnerId, new GridPoint(1, 2));
        world.PlaceFounder("founder:cccccccccccccccccccccccccccccccc", new GridPoint(2, 2));
        world.PlaceFounder("founder:dddddddddddddddddddddddddddddddd", new GridPoint(3, 2));
        return world;
    }

    private sealed class QuietDecisionProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, "safe_idle", 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == "safe_idle" ? 1d : 0d, StringComparer.Ordinal)));
        }
    }

    private sealed record NameReply(
        string? Name,
        double Confidence = 1d,
        string SelectedCandidateId = "safe_idle");

    private sealed class SequencedHostedProvider(
        IReadOnlyList<NameReply> replies,
        bool holdSecond = false,
        bool ignoreSecondCancellation = false,
        bool failSecond = false) : IDecisionProvider
    {
        private int callCount;

        public SequencedHostedProvider(params NameReply[] replies)
            : this((IReadOnlyList<NameReply>)replies)
        {
        }

        public ConcurrentQueue<InhabitantObservation> ObservedRequests { get; } = new();
        public TaskCompletionSource<bool> FirstReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SecondReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SecondFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InhabitantObservation? SecondObservation { get; private set; }
        public int CallCount => Volatile.Read(ref callCount);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref callCount);
            var observation = request.Observation;
            ObservedRequests.Enqueue(observation);
            if (call == 2)
            {
                SecondObservation = observation;
                SecondStarted.TrySetResult(true);
                if (holdSecond)
                {
                    if (ignoreSecondCancellation)
                        await ReleaseSecond.Task;
                    else
                        await ReleaseSecond.Task.WaitAsync(cancellationToken);
                }
            }

            if (failSecond && observation.IsNameRetry)
            {
                if (call == 2)
                {
                    SecondFailed.TrySetResult(true);
                    SecondReturned.TrySetResult(true);
                }
                throw new HttpRequestException("test transport failure");
            }

            var reply = replies[Math.Min(call - 1, replies.Count - 1)];
            var selected = reply.SelectedCandidateId;
            var probabilities = observation.Candidates.ToDictionary(candidate => candidate.Id,
                candidate => candidate.Id == selected ? 1d : 0d, StringComparer.Ordinal);
            var response = new CognitionDecisionResponse(
                request.RequestId,
                observation.InhabitantId,
                Kind,
                ProviderEpoch,
                observation.RunEpoch,
                observation.DecisionGeneration,
                observation.ObservationDigest,
                selected,
                reply.Confidence,
                probabilities,
                Usage: new CognitionUsage("test-model", 10, 2),
                ChosenName: reply.Name);
            if (call == 1) FirstReturned.TrySetResult(true);
            if (call == 2) SecondReturned.TrySetResult(true);
            return response;
        }
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SequencedNameResponseHandler(params string[] names) : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref requestCount);
            _ = await request.Content!.ReadAsStringAsync(cancellationToken);
            var answer = JsonSerializer.Serialize(new
            {
                selected_candidate_id = "safe_idle",
                confidence = 1d,
                chosen_name = names[Math.Min(call - 1, names.Length - 1)],
            });
            var body = JsonSerializer.Serialize(new
            {
                model = "retry-meter-model",
                choices = new[] { new { message = new { role = "assistant", content = answer } } },
                usage = new { prompt_tokens = 10, completion_tokens = 2 },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
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

    private sealed class HeldGuidanceReplyProvider(string replyText) : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<InhabitantObservation> ObservedRequests { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedRequests.Enqueue(request.Observation);
            Started.TrySetResult(true);
            await Release.Task;
            var observation = request.Observation;
            var selected = observation.Candidates.Single(item => item.Id == "safe_idle");
            var probabilities = observation.Candidates.ToDictionary(item => item.Id,
                item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal);
            var firstMessage = observation.ObserverGuidance?.FirstOrDefault(message => message.ReplyAllowed);
            var response = new CognitionDecisionResponse(
                request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected.Id, 1d, probabilities,
                ObserverReplies: firstMessage is null ? [] : [new(firstMessage.InstructionId, replyText)]);
            Returned.TrySetResult(true);
            return response;
        }
    }
}
