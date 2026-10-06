using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class QueuedCognitionContextTests
{
    private const string Actor = "founder-scout";
    [Fact]
    public async Task CompletingHeldObservationKeepsTheNewerQueuedChoice()
    {
        var provider = new HeldIdleProvider();
        var scheduler = new SocietyCognitionScheduler([SocietyFixture.CreateFounder("alice", "Alice")], _ => provider);
        var observation = new InhabitantObservation("alice", 0, 0, 0, "old", 8_000,
            [new("safe_idle", "Wait", 0)]);
        Assert.True(scheduler.Enqueue(new("old", "alice", 1, 0, ["old"], observation)));
        var preview = Assert.Single(scheduler.PreviewHostedRequests(new HashSet<string>()));
        var pending = preview.DecideAsync(default).AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(scheduler.Enqueue(new("new", "alice", 2, 1, ["new"], observation with
        {
            WorldTick = 1,
            ObservationDigest = "new",
            Candidates = [new("safe_idle", "Wait", 0), new("read_proposal", "Read new proposal", 1)],
        })));
        provider.Release.TrySetResult(true);
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        var admitted = scheduler.CompleteDeferred(preview.Request, response, null, 0,
            new HashSet<string> { "safe_idle", "read_proposal" });
        Assert.NotNull(admitted);
        Assert.True(admitted.Admission.Accepted);
        var next = Assert.Single(scheduler.PreviewHostedRequests(new HashSet<string>()));
        Assert.Contains(next.Request.Observation.Candidates, candidate => candidate.Id == "read_proposal");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleReplyDoesNotHideAChoiceQueuedWhileItWasHeld(bool newFoodChoice)
    {
        var provider = new HeldIdleProvider();
        using var world = CreateWorld(provider, newFoodChoice ? 7_020 : 9_000);
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var first = Assert.Single(provider.Requests);
            Assert.DoesNotContain(first.Candidates, candidate => candidate.Id is "seek_food" or "harvest_food");
            for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            var held = world.ExportState();
            var queued = Assert.Single(held.Society.Cognition.Queue, item => item.InhabitantId == Actor);
            if (newFoodChoice)
                Assert.Contains(queued.Observation.Candidates, candidate => candidate.Id is "seek_food" or "harvest_food");
            Assert.Single(provider.Requests);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await AdvanceUntil(world, () => Accepted(world) >= 1);
            var admittedAt = world.WorldTick;
            for (var tick = 0; tick < 35 && provider.Requests.Count < 2; tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                await Task.Delay(1);
            }
            Assert.True(world.WorldTick - admittedAt < 300);
            Assert.Equal(newFoodChoice ? 2 : 1, provider.Requests.Count);
            if (newFoodChoice)
            {
                Assert.Contains(provider.Requests.Last().Candidates, candidate => candidate.Id is "seek_food" or "harvest_food");
                await AdvanceUntil(world, () => Accepted(world) == 2);
                Assert.Equal(2, provider.Requests.Count);
            }
            Assert.DoesNotContain(world.ExportState().Society.Cognition.Queue, entry => entry.InhabitantId == Actor);
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewGuidanceGetsItsOwnReplyAndSurvivesReloadWithTheObsoleteReplyHeld(bool reload)
    {
        var provider = new HeldIdleProvider(holdSecond: true, ignoreCancellation: true, replyText: "Fresh reply.");
        using var world = CreateWorld(provider);
        HeldIdleProvider? replacement = null;
        PrivateWorldRuntime? restored = null;
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(Assert.Single(provider.Requests).ObserverGuidance ?? []);
            var message = world.SubmitInstruction(new OwnerInstructionRequest("new-held-guidance", "owner:test", Actor,
                OwnerInstructionKind.Suggestive, "Look for a grove after you eat."));
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Contains(Assert.Single(world.ExportState().Society.Cognition.Queue,
                entry => entry.InhabitantId == Actor).Observation.ObserverGuidance!,
                item => item.InstructionId == message.InstructionId);

            provider.Release.TrySetResult(true);
            await AdvanceUntil(world, () => provider.SecondStarted.Task.IsCompleted);
            Assert.Equal(2, provider.Requests.Count);
            Assert.Equal(1, Accepted(world));
            var fresh = provider.Requests.Last();
            Assert.Equal(message.InstructionId, Assert.Single(fresh.ObserverGuidance!).InstructionId);
            Assert.Null(Instruction(world, message.InstructionId).ObservedTick);

            var active = world;
            if (reload)
            {
                world.Pause();
                var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
                var saved = PrivateWorldRuntimeCodec.Decode(bytes);
                Assert.Contains(Assert.Single(saved.Society.Cognition.Queue, entry => entry.InhabitantId == Actor)
                    .Observation.ObserverGuidance!, item => item.InstructionId == message.InstructionId);
                replacement = new HeldIdleProvider(replyText: "Reply after reload.");
                restored = Restore(saved, replacement);
                restored.Resume();
                Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
                await replacement.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                var resumed = Assert.Single(replacement.Requests);
                Assert.NotEqual(fresh.RunEpoch, resumed.RunEpoch);
                Assert.Equal(restored.Society.RunEpoch, resumed.RunEpoch);
                Assert.Equal(message.InstructionId, Assert.Single(resumed.ObserverGuidance!).InstructionId);

                // The obsolete provider deliberately ignores cancellation and answers after the new epoch started.
                provider.ReleaseSecond.TrySetResult(true);
                await provider.SecondReturned.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                Assert.Null(Instruction(restored, message.InstructionId).ObservedTick);
                replacement.Release.TrySetResult(true);
                active = restored;
            }
            else
            {
                provider.ReleaseSecond.TrySetResult(true);
            }

            await AdvanceUntil(active, () => Instruction(active, message.InstructionId).ObservedTick is not null);
            Assert.Equal(reload ? "Reply after reload." : "Fresh reply.", Instruction(active, message.InstructionId).ObserverReply);
            Assert.Contains(message.InstructionId, active.ExportState().CompletedInstructionIds ?? []);
            Assert.Equal(2, provider.Requests.Count);
            if (replacement is not null) Assert.Single(replacement.Requests);
            active.Validate();
            using var roundtripped = Restore(PrivateWorldRuntimeCodec.Decode(
                PrivateWorldRuntimeCodec.Encode(active.ExportState())), new HeldIdleProvider());
            Assert.Equal(Instruction(active, message.InstructionId), Instruction(roundtripped, message.InstructionId));
        }
        finally
        {
            provider.Release.TrySetResult(true);
            provider.ReleaseSecond.TrySetResult(true);
            replacement?.Release.TrySetResult(true);
            restored?.Dispose();
        }
    }

    [Fact]
    public async Task RefusedPreparedTickPreservesTheQueuedChoiceAndHeldRequest()
    {
        var provider = new HeldIdleProvider();
        using var world = CreateWorld(provider, 7_020);
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Contains(Assert.Single(world.ExportState().Society.Cognition.Queue,
                entry => entry.InhabitantId == Actor).Observation.Candidates, candidate => candidate.Id == "seek_food");
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            var preparedAdmission = false;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!preparedAdmission)
            {
                var permitted = true;
                var refused = await world.AdvanceOneTickNonBlockingAsync(
                    commitPermitted: () => permitted,
                    prepareChildModelSelections: (proposed, events) =>
                    {
                        // This public preparation boundary runs before the final commit guard.
                        // Wait for the actual prepared admission instead of guessing when its worker finished.
                        preparedAdmission = events.Any(item => item.Kind == "hosted_decision_completed" &&
                            item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
                        if (preparedAdmission)
                        {
                            Assert.Equal(1, Accepted(proposed));
                            Assert.Contains(Assert.Single(proposed.ExportState().Society.Cognition.Queue,
                                entry => entry.InhabitantId == Actor).Observation.Candidates, candidate => candidate.Id == "seek_food");
                        }
                        permitted = false;
                        return [];
                    }, cancellationToken: deadline.Token);
                Assert.False(refused.Advanced);
                Assert.Equal("waiting_for_client", refused.Outcome);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                Assert.Equal(0, Accepted(world));
                if (!preparedAdmission) await Task.Delay(5, deadline.Token);
            }
            Assert.Single(provider.Requests);
            await AdvanceUntil(world, () => Accepted(world) == 2);
            Assert.Equal(2, provider.Requests.Count);
            Assert.Contains(provider.Requests.Last().Candidates, candidate => candidate.Id == "seek_food");
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task RetainedGuidanceUsesTheIdentityChosenByTheEarlierReply()
    {
        var provider = new HeldIdleProvider(holdSecond: true, replyText: "I heard the new message.", chooseIdentity: true);
        using var world = CreateWorld(provider, identityPending: true);
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var first = Assert.Single(provider.Requests);
            Assert.True(first.NeedsPersonality);
            Assert.True(first.NeedsAspiration);
            var message = world.SubmitInstruction(new OwnerInstructionRequest("identity-held-guidance", "owner:test", Actor,
                OwnerInstructionKind.Suggestive, "Look for a grove after you eat."));
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            provider.Release.TrySetResult(true);
            await AdvanceUntil(world, () => provider.SecondStarted.Task.IsCompleted);
            var fresh = provider.Requests.Last();
            var person = world.Inhabitants.Single(item => item.InhabitantId == Actor);
            Assert.False(person.IdentityChoicePending);
            Assert.Equal("Patient and curious", person.Personality);
            Assert.Equal("Explore the riverbanks", person.Aspiration);
            Assert.False(fresh.NeedsPersonality);
            Assert.False(fresh.NeedsAspiration);
            Assert.False(fresh.NeedsName);
            Assert.NotNull(fresh.Self);
            Assert.Equal(person.Personality, fresh.Self.Personality);
            Assert.Equal(person.Aspiration, fresh.Self.Aspiration);
            Assert.Equal(world.Society.GetInhabitant(Actor).Name, fresh.Self.Name);
            Assert.Equal(message.InstructionId, Assert.Single(fresh.ObserverGuidance!).InstructionId);
            provider.ReleaseSecond.TrySetResult(true);
            await AdvanceUntil(world, () => Instruction(world, message.InstructionId).ObservedTick is not null);
            Assert.Equal(2, provider.Requests.Count);
            Assert.Single(provider.Requests, observation => observation.NeedsPersonality || observation.NeedsAspiration);
            Assert.Equal("Patient and curious", world.Inhabitants.Single(item => item.InhabitantId == Actor).Personality);
            Assert.Single(world.ExportState().Events, item => item.Kind == "agent_identity_chosen" && item.Detail == Actor);
            world.Validate();
        }
        finally
        {
            provider.Release.TrySetResult(true);
            provider.ReleaseSecond.TrySetResult(true);
        }
    }

    [Fact]
    public async Task RetainedGuidanceResumesInTheNewEpochWhileFieldWorkIsPending()
    {
        var (state, farmer, _, point) = await FarmFieldTests.ReadyFarmer("retained-farm-guidance");
        var provider = new HeldIdleProvider(holdSecond: true, ignoreCancellation: true,
            replyText: "Old farm reply.", firstCandidatePrefix: "farm:Harvest:");
        using var world = PrivateWorldRuntime.Restore(state, id => id == farmer ? provider : new QuietProvider());
        var replacement = new HeldIdleProvider(replyText: "Farm reply after reload.");
        PrivateWorldRuntime? restored = null;
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains(Assert.Single(provider.Requests).Candidates, item => item.Id.StartsWith("farm:Harvest:", StringComparison.Ordinal));
            var message = world.SubmitInstruction(new OwnerInstructionRequest("farm-held-guidance", "owner:test", farmer,
                OwnerInstructionKind.Suggestive, "Look for a grove after you eat."));
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            provider.Release.TrySetResult(true);
            await AdvanceUntil(world, () => provider.SecondStarted.Task.IsCompleted);
            var field = Assert.Single(world.Fields);
            Assert.Equal(point, field.Position);
            Assert.NotNull(field.Work);
            Assert.Equal(FarmWorkKind.Harvest, field.Work.Kind);
            Assert.Equal(farmer, field.Work.WorkerId);
            var oldEpoch = provider.Requests.Last().RunEpoch;
            world.Pause();
            var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
                id => id == farmer ? replacement : new QuietProvider());
            restored.Resume();
            await AdvanceUntil(restored, () => replacement.Started.Task.IsCompleted);
            var fresh = Assert.Single(replacement.Requests);
            Assert.NotEqual(oldEpoch, fresh.RunEpoch);
            Assert.Equal(restored.Society.RunEpoch, fresh.RunEpoch);
            Assert.Equal(message.InstructionId, Assert.Single(fresh.ObserverGuidance!).InstructionId);
            Assert.Equal(field.Work.RemainingTicks, Assert.Single(restored.Fields).Work!.RemainingTicks);
            provider.ReleaseSecond.TrySetResult(true);
            await provider.SecondReturned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Null(Instruction(restored, message.InstructionId).ObservedTick);
            replacement.Release.TrySetResult(true);
            await AdvanceUntil(restored, () => Instruction(restored, message.InstructionId).ObservedTick is not null);
            Assert.Equal("Farm reply after reload.", Instruction(restored, message.InstructionId).ObserverReply);
            await AdvanceUntil(restored, () => Assert.Single(restored.Fields).Work is null);
            Assert.Equal(FarmFieldStage.Harvested, Assert.Single(restored.Fields).Stage);
            Assert.Single(restored.ExportState().Events, item => item.Kind == "field_harvested");
            Assert.Equal(2, provider.Requests.Count);
            restored.Validate();
        }
        finally
        {
            provider.Release.TrySetResult(true);
            provider.ReleaseSecond.TrySetResult(true);
            replacement.Release.TrySetResult(true);
            restored?.Dispose();
        }
    }

    private static PrivateWorldRuntime CreateWorld(HeldIdleProvider provider, int fullness = 9_000, bool identityPending = false)
    {
        using var initial = new PrivateWorldRuntime("queued-idle-context");
        var state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { HungerBasisPoints = fullness, IdentityChoicePending = identityPending } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Where(lot => !InventoryContainerRules.IsFood(lot.ItemKind)).ToArray(),
                    },
                },
            },
        };
        return Restore(state, provider);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, HeldIdleProvider provider) =>
        PrivateWorldRuntime.Restore(state, id => id == Actor ? provider : new QuietProvider());

    private static OwnerQueuedInstruction Instruction(PrivateWorldRuntime world, string instructionId) =>
        Assert.Single(world.ExportState().Instructions!, instruction => instruction.InstructionId == instructionId);

    private static int Accepted(PrivateWorldRuntime world) => world.ExportState().Events.Count(item =>
        item.Kind == "hosted_decision_completed" && item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done)
    {
        for (var tick = 0; tick < 40 && !done(); tick++)
        {
            // Bound a stuck tick, not the combined cost of valid ticks and
            // scheduling delays. The forty-tick behavior bound stays separate.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token)).Advanced);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                Assert.Fail($"World tick {world.WorldTick} stalled during poll {tick + 1} of 40.");
            }
            if (!done()) await Task.Delay(5);
        }
        Assert.True(done(), "The queued decision was not admitted within forty ticks.");
    }

    private sealed class HeldIdleProvider(bool holdSecond = false, bool ignoreCancellation = false, string? replyText = null,
        string? firstCandidatePrefix = null, bool chooseIdentity = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SecondReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request.Observation);
            var ordinal = Requests.Count;
            var second = ordinal == 2;
            if (second) SecondStarted.TrySetResult(true);
            Started.TrySetResult(true);
            var release = second && holdSecond ? ReleaseSecond.Task : Release.Task;
            if (ignoreCancellation) await release;
            else await release.WaitAsync(cancellationToken);
            var message = request.Observation.ObserverGuidance?.FirstOrDefault(item => item.ReplyAllowed);
            var selected = ordinal == 1 && firstCandidatePrefix is not null
                ? request.Observation.Candidates.First(item => item.Id.StartsWith(firstCandidatePrefix, StringComparison.Ordinal)).Id
                : "safe_idle";
            var response = Reply(request, Kind, ProviderEpoch, selected) with
            {
                ObserverReplies = message is null || replyText is null ? [] : [new(message.InstructionId, replyText)],
                ChosenPersonality = chooseIdentity ? second ? "Must not replace identity" : "Patient and curious" : null,
                ChosenAspiration = chooseIdentity ? second ? "Must not replace aspiration" : "Explore the riverbanks" : null,
            };
            if (second) SecondReturned.TrySetResult(true);
            Returned.TrySetResult(true);
            return response;
        }
    }

    private sealed class QuietProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Reply(request, Kind, ProviderEpoch));
    }

    private static CognitionDecisionResponse Reply(CognitionDecisionRequest request, DecisionProviderKind kind, long epoch,
        string selected = "safe_idle") =>
        new(request.RequestId, request.Observation.InhabitantId, kind, epoch, request.Observation.RunEpoch,
            request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected, 1,
            request.Observation.Candidates.ToDictionary(item => item.Id, item => item.Id == selected ? 1d : 0d));
}
