using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    private const string InitiatorId = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InviteeId = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ListenerId = "founder:cccccccccccccccccccccccccccccccc";
    private const string DistantId = "founder:dddddddddddddddddddddddddddddddd";
    private static readonly string?[] PublicTurnEffects = ["none"];
    private static readonly string?[] WrapUpEffects = ["none", "mutual_trust"];

    [Fact]
    public async Task NormalConfiguredProviderRoutesTurnsProjectsOnlyHeardClaimsAndWaitsForSharedEffectConsent()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-normal-conversation-");
        var handler = new ConversationHttpHandler(InviteeId, holdInitialChoices: true);
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), new(
                "deterministic", null, null, null, null, null, null));
            _ = store.Configure(new("planning", "openai", "world-default-model", "world-default-key", false));
            _ = store.Configure(Personal(InitiatorId, "agent-a-model", "agent-a-key"));
            _ = store.Configure(Personal(InviteeId, "agent-b-model", "agent-b-key"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var router = new ConfigurableDecisionProvider(store, new ConversationHttpClientFactory(handler),
                usageStore: usage);
            using var world = NewWorld("configured-conversation", _ => router);
            var initialPositions = world.Inhabitants.ToDictionary(item => item.InhabitantId, item => item.Position,
                StringComparer.Ordinal);
            var initialLots = world.Society.Inventory.Lots.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            world.StartWorld();

            for (var attempt = 0; attempt < 40 &&
                 (!handler.InitiatorTalkStarted.Task.IsCompleted || !handler.StaleInviteeTalkStarted.Task.IsCompleted); attempt++)
            {
                await AdvanceConfiguredConversationTickAsync(world, handler);
            }

            Assert.True(handler.InitiatorTalkStarted.Task.IsCompleted,
                "The configured initiator should receive a personal-model request with a legal talk action.");
            Assert.True(handler.StaleInviteeTalkStarted.Task.IsCompleted,
                "The configured invitee should have an older decision in flight before the proposal commits.");
            handler.ReleaseInitiatorTalk();

            for (var attempt = 0; attempt < 20 && world.Conversations.All(item =>
                     item.Status != AgentConversationStatus.Proposed); attempt++)
            {
                await AdvanceConfiguredConversationTickAsync(world, handler);
            }

            var oldInvitation = Assert.Single(world.Conversations, item => item.Status == AgentConversationStatus.Proposed);
            Assert.Equal(InitiatorId, oldInvitation.InitiatorId);
            Assert.Equal(InviteeId, oldInvitation.InviteeId);

            for (var attempt = 0; attempt < AgentConversationRules.ProposalLifetimeTicks + 20 &&
                 world.Conversations.All(item => item.Id == oldInvitation.Id || item.Status != AgentConversationStatus.Proposed); attempt++)
            {
                await AdvanceConfiguredConversationTickAsync(world, handler);
            }

            var expiredInvitation = Assert.Single(world.Conversations, item => item.Id == oldInvitation.Id);
            Assert.Equal(AgentConversationStatus.Closed, expiredInvitation.Status);
            Assert.Equal("deadline", expiredInvitation.Outcome);
            var newerInvitation = Assert.Single(world.Conversations, item => item.Status == AgentConversationStatus.Proposed);
            Assert.NotEqual(oldInvitation.Id, newerInvitation.Id);
            Assert.Equal(InitiatorId, newerInvitation.InitiatorId);
            Assert.Equal(InviteeId, newerInvitation.InviteeId);

            // The invitee's earlier hosted decision selected the now-stale
            // inverse proposal. It must be discarded, then refreshed against
            // the newer invitation's distinct ID before any consent is applied.
            handler.ReleaseStaleInviteeTalk();
            for (var attempt = 0; attempt < 120 && !handler.WrapUpAcceptanceStarted.Task.IsCompleted; attempt++)
            {
                await AdvanceConfiguredConversationTickAsync(world, handler);
            }

            var snapshotBeforeConsent = world.ExportState();
            Assert.True(handler.WrapUpAcceptanceStarted.Task.IsCompleted,
                $"The configured host provider should reach the second participant's explicit wrap-up decision at tick {world.WorldTick}. " +
                $"Conversations: {string.Join("; ", world.Conversations.Select(item => $"{item.Id}:{item.Status}:turns={item.Turns.Count}:rev={item.Revision}"))}. " +
                $"Recent events: {string.Join("; ", snapshotBeforeConsent.Events.Where(item => item.Kind.StartsWith("conversation", StringComparison.Ordinal) || item.Kind == "cognition_rejected").TakeLast(24).Select(item => $"{item.Kind}:{item.Detail}"))}. " +
                $"Decisions: {string.Join("; ", handler.DecisionCalls)}");
            var awaitingConsent = Assert.Single(world.Conversations, item => item.Status == AgentConversationStatus.WrapUp);
            Assert.Equal(newerInvitation.Id, awaitingConsent.Id);
            Assert.Contains(handler.InviteeDecisionCalls, call =>
                call.Contains($"conversation_accept:{newerInvitation.Id}", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.InviteeDecisionCalls, call =>
                call.Contains($"conversation_accept:{oldInvitation.Id}", StringComparison.Ordinal));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "conversation_proposed" &&
                item.Detail.Contains($":{InviteeId}:{InitiatorId}", StringComparison.Ordinal));
            Assert.Single(awaitingConsent.Turns, turn => turn.IsWrapUp);
            Assert.InRange(awaitingConsent.WrapUpAcceptedBy.Count, 0, 1);
            Assert.Equal(0, TrustScore(world, InitiatorId, InviteeId));
            Assert.Equal(0, TrustScore(world, InviteeId, InitiatorId));

            handler.ReleaseWrapUpAcceptance();
            for (var attempt = 0; attempt < 40 && world.Conversations.Any(item =>
                         item.Id == awaitingConsent.Id && item.Status != AgentConversationStatus.Closed); attempt++)
            {
                await AdvanceConfiguredConversationTickAsync(world, handler);
            }

            var conversation = Assert.Single(world.Conversations, item => item.Id == awaitingConsent.Id);
            Assert.Equal(AgentConversationStatus.Closed, conversation.Status);
            Assert.Equal("agreed", conversation.Outcome);
            Assert.Equal(AgentConversationEffect.MutualTrust, conversation.WrapUpEffect);
            var publicTurns = conversation.Turns.Where(turn => !turn.IsWrapUp).ToArray();
            Assert.Equal(AgentConversationRules.MaximumPublicTurns, publicTurns.Length);
            Assert.Equal(3, publicTurns.Count(turn => turn.SpeakerId == InitiatorId));
            Assert.Equal(3, publicTurns.Count(turn => turn.SpeakerId == InviteeId));
            Assert.All(publicTurns, turn =>
            {
                var otherParticipantId = turn.SpeakerId == InitiatorId ? InviteeId : InitiatorId;
                Assert.Contains(otherParticipantId, turn.ListenerIds);
                Assert.Contains(turn.ListenerIds, listenerId => listenerId != InitiatorId && listenerId != InviteeId);
            });

            var saved = world.ExportState();
            foreach (var publicTurn in publicTurns)
            {
                foreach (var listenerId in publicTurn.ListenerIds.Where(listenerId => listenerId != publicTurn.SpeakerId))
                    Assert.Contains(saved.Society.Society.Beliefs!, belief =>
                        belief.OwnerId == listenerId && belief.SourceTurnId == publicTurn.Id &&
                        belief.Provenance == SocietyBeliefProvenance.Hearsay && belief.Statement == publicTurn.Text);
                Assert.DoesNotContain(saved.Society.Society.Beliefs!, belief =>
                    belief.SourceTurnId == publicTurn.Id && !publicTurn.ListenerIds.Contains(belief.OwnerId));
            }
            Assert.DoesNotContain(saved.Events, item =>
                conversation.Turns.Any(turn => item.Detail.Contains(turn.Text, StringComparison.Ordinal)));

            Assert.Equal(1, TrustScore(world, InitiatorId, InviteeId));
            Assert.Equal(1, TrustScore(world, InviteeId, InitiatorId));
            Assert.Equal(initialPositions, world.Inhabitants.ToDictionary(item => item.InhabitantId, item => item.Position,
                StringComparer.Ordinal));
            Assert.Equal(initialLots, world.Society.Inventory.Lots.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());

            var providerCalls = handler.ConversationCalls.ToArray();
            Assert.Equal(AgentConversationRules.MaximumPublicTurns + 1, providerCalls.Length);
            Assert.Equal(new[] { InitiatorId, InviteeId, InitiatorId, InviteeId, InitiatorId, InviteeId },
                providerCalls.Take(AgentConversationRules.MaximumPublicTurns).Select(call => call.SpeakerId));
            foreach (var call in providerCalls)
            {
                var expectedModel = call.SpeakerId == InitiatorId ? "agent-a-model" : "agent-b-model";
                var expectedKey = call.SpeakerId == InitiatorId ? "agent-a-key" : "agent-b-key";
                Assert.Equal(expectedModel, call.Model);
                Assert.Equal($"Bearer {expectedKey}", call.Authorization);
                Assert.DoesNotContain(ConversationHttpHandler.PrivateDecisionThought, call.Context, StringComparison.Ordinal);
                using var context = JsonDocument.Parse(call.Context);
                var purpose = context.RootElement.GetProperty("purpose").GetString();
                var allowed = context.RootElement.GetProperty("allowed_effects").EnumerateArray()
                    .Select(item => item.GetString()).ToArray();
                if (purpose == "public_turn")
                    Assert.Equal(PublicTurnEffects, allowed);
                else
                    Assert.Equal(WrapUpEffects, allowed);
            }

            var capturedUsage = usage.Capture();
            Assert.Equal(AgentConversationRules.MaximumPublicTurns + 1,
                capturedUsage.Rows.Where(row => row.Role == "conversation").Sum(row => row.Completed));
            Assert.Equal(2, saved.ConversationBudgets!.Single(item => item.AgentId == InitiatorId).Count);
            Assert.Equal(1, saved.ConversationBudgets!.Single(item => item.AgentId == InviteeId).Count);
        }
        finally
        {
            handler.ReleaseWrapUpAcceptance();
            handler.ReleaseInitiatorTalk();
            handler.ReleaseStaleInviteeTalk();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PendingInvitationSurvivesPrivateWorldFileReloadAndCanStillBeAccepted()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        });
        var directory = Directory.CreateTempSubdirectory("clankerworld-pending-invitation-");
        var path = Path.Combine(directory.FullName, "world.json");
        try
        {
            using var world = NewWorld("pending-invitation-reload", provider);
            world.StartWorld();
            for (var attempt = 0; attempt < 50 && world.Conversations.All(item =>
                     item.Status != AgentConversationStatus.Proposed); attempt++)
            {
                _ = await world.AdvanceOneTickNonBlockingAsync();
                await Task.Delay(5);
            }

            var beforeSave = Assert.Single(world.Conversations, item => item.Status == AgentConversationStatus.Proposed);
            Assert.Null(beforeSave.CurrentSpeakerId);
            var savedBudget = world.ConversationBudgets.Single(item => item.AgentId == InitiatorId);
            var file = new PrivateWorldStateFile(path, id =>
                id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());
            file.Save(world);
            var exactSavedBytes = File.ReadAllBytes(path);

            using var restored = file.LoadOrCreate("pending-invitation-reload");
            Assert.Equal(exactSavedBytes, File.ReadAllBytes(path));
            var afterReload = Assert.Single(restored.Conversations, item => item.Status == AgentConversationStatus.Proposed);
            Assert.Equal(beforeSave.Id, afterReload.Id);
            Assert.Null(afterReload.CurrentSpeakerId);
            Assert.Equal(beforeSave.CreatedTick, afterReload.CreatedTick);
            Assert.Equal(beforeSave.ProposalDeadlineTick, afterReload.ProposalDeadlineTick);
            Assert.Equal(beforeSave.Revision, afterReload.Revision);
            Assert.Equal(savedBudget, restored.ConversationBudgets.Single(item => item.AgentId == InitiatorId));
            Assert.Empty(provider.TurnRequests);

            restored.Resume();
            for (var attempt = 0; attempt < 90 && restored.Conversations.Single(item => item.Id == beforeSave.Id)
                     .Turns.Count == 0; attempt++)
            {
                _ = await restored.AdvanceOneTickNonBlockingAsync();
                await Task.Delay(5);
            }

            var accepted = Assert.Single(restored.Conversations, item => item.Id == beforeSave.Id);
            Assert.NotEmpty(accepted.Turns);
            Assert.Contains(provider.TurnRequests, request => request.ConversationId == beforeSave.Id);
            Assert.Equal(1, restored.ConversationBudgets.Single(item => item.AgentId == InviteeId).Count);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ProviderRouteChangedDuringPreparedTickCannotAdmitTurnOrListenerMemory()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        });
        using var world = NewWorld("conversation-provider-route-race", provider);
        world.StartWorld();

        for (var attempt = 0; attempt < 40 && provider.TurnRequests.Count == 0; attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }

        var request = Assert.Single(provider.TurnRequests);
        Assert.Equal(AgentConversationStatus.AwaitingSpeaker, Assert.Single(world.Conversations).Status);
        var firstCommitCheck = 0;
        var step = await world.AdvanceOneTickNonBlockingAsync(() =>
        {
            if (Interlocked.Exchange(ref firstCommitCheck, 1) == 0)
                provider.CurrentProviderEpoch++;
            return true;
        });

        Assert.True(step.Advanced);
        var conversation = Assert.Single(world.Conversations);
        Assert.Equal(AgentConversationStatus.Suspended, conversation.Status);
        Assert.Equal(AgentConversationInterruption.ProviderUnavailable, conversation.Interruption);
        Assert.Empty(conversation.Turns);
        var saved = world.ExportState();
        Assert.DoesNotContain(saved.Society.Society.Beliefs ?? [], belief =>
            belief.OwnerId == ListenerId && belief.Statement == "A public turn with no world command.");
        Assert.DoesNotContain(saved.Events, item =>
            item.Detail.Contains("A public turn with no world command.", StringComparison.Ordinal));
        Assert.Contains(saved.Events, item =>
            item.Kind == "conversation_interrupted" &&
            item.Detail == $"{request.ConversationId}:provider_unavailable");
    }

    [Fact]
    public async Task AcceptedTurnIsBoundedPublicAndOnlyActualNearbyListenersLearnIt()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        });
        // Bystanders stay nearby through the save boundary; only the two participants can speak.
        using var world = NewWorld("nearby-conversation", _ => provider);
        world.StartWorld();

        for (var attempt = 0; attempt < 40 && world.Conversations.All(item => item.Turns.Count == 0); attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }

        var conversation = Assert.Single(world.Conversations, item => item.Turns.Count > 0);
        var turn = Assert.Single(conversation.Turns);
        Assert.Equal(InitiatorId, turn.SpeakerId);
        Assert.Equal("A public turn with no world command.", turn.Text);
        Assert.Contains(InviteeId, turn.ListenerIds);
        Assert.Contains(ListenerId, turn.ListenerIds);
        Assert.Contains(turn.ListenerIds, listenerId => listenerId != InitiatorId && listenerId != InviteeId);

        for (var attempt = 0; attempt < 40 && world.Conversations.Single().Turns.Count < 2; attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }

        conversation = Assert.Single(world.Conversations, item => item.Turns.Count >= 2);
        var secondTurn = conversation.Turns[1];
        Assert.NotEqual(turn.SpeakerId, secondTurn.SpeakerId);
        Assert.Contains(InitiatorId, secondTurn.ListenerIds);
        Assert.Contains(ListenerId, secondTurn.ListenerIds);
        var requestedBeforeSave = provider.TurnRequests.Count;
        Assert.InRange(requestedBeforeSave, 2, AgentConversationRules.MaximumPublicTurns);
        Assert.Empty(provider.TurnRequests[0].PublicHistory);
        Assert.Equal(turn, Assert.Single(provider.TurnRequests[1].PublicHistory));

        var saved = world.ExportState();
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, saved.SchemaVersion);
        Assert.Equal(2, saved.Conversations!.Single().Turns.Count);
        var budgets = saved.ConversationBudgets!;
        Assert.Equal(1, budgets.Single(item => item.AgentId == InitiatorId).Count);
        Assert.Equal(1, budgets.Single(item => item.AgentId == InviteeId).Count);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with
        { SchemaVersion = PrivateWorldRuntime.ConversationSchemaVersion - 1 }));
        foreach (var heardTurn in conversation.Turns.Take(2))
        {
            foreach (var ownerId in heardTurn.ListenerIds.Where(ownerId => ownerId != heardTurn.SpeakerId))
                Assert.Contains(saved.Society.Society.Beliefs!, item =>
                    item.OwnerId == ownerId && item.SourceTurnId == heardTurn.Id &&
                    item.Provenance == SocietyBeliefProvenance.Hearsay && item.Statement == heardTurn.Text);
            Assert.DoesNotContain(saved.Society.Society.Beliefs!, item =>
                item.SourceTurnId == heardTurn.Id && !heardTurn.ListenerIds.Contains(item.OwnerId));
        }
        Assert.DoesNotContain(saved.Events, item => item.Detail.Contains(turn.Text, StringComparison.Ordinal) ||
            item.Detail.Contains(secondTurn.Text, StringComparison.Ordinal));

        var projectedConversation = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Conversations);
        Assert.Equal(2, projectedConversation.Turns.Count);
        var projectedTurn = projectedConversation.Turns[0];
        Assert.Equal(InitiatorId, projectedTurn.SpeakerId);
        Assert.Equal(turn.Text, projectedTurn.Text);
        Assert.Equal(turn.ListenerIds, projectedTurn.ListenerIds);

        world.Pause();
        var paused = world.ExportState();
        var pausedConversation = Assert.Single(paused.Conversations!);
        Assert.Equal(AgentConversationStatus.Suspended, pausedConversation.Status);
        Assert.Equal(AgentConversationInterruption.OwnerPaused, pausedConversation.Interruption);
        var encoded = PrivateWorldRuntimeCodec.Encode(paused);
        var nullConversationEntry = JsonNode.Parse(encoded) ?? throw new InvalidDataException("Encoded state was empty.");
        nullConversationEntry["state"]!["conversations"] = new JsonArray((JsonNode?)null);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(
            Encoding.UTF8.GetBytes(nullConversationEntry.ToJsonString())));

        var nullTurnEntry = JsonNode.Parse(encoded) ?? throw new InvalidDataException("Encoded state was empty.");
        nullTurnEntry["state"]!["conversations"]![0]!["turns"]![0] = null;
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(
            Encoding.UTF8.GetBytes(nullTurnEntry.ToJsonString())));

        var nullBudgetEntry = JsonNode.Parse(encoded) ?? throw new InvalidDataException("Encoded state was empty.");
        nullBudgetEntry["state"]!["conversationBudgets"]![0] = null;
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(
            Encoding.UTF8.GetBytes(nullBudgetEntry.ToJsonString())));

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(encoded), _ => provider);
        var restoredConversation = Assert.Single(restored.Conversations!);
        Assert.Equal(AgentConversationStatus.Suspended, restoredConversation.Status);
        Assert.Equal(2, restoredConversation.Turns.Count);
        Assert.Equal(requestedBeforeSave, provider.TurnRequests.Count);

        restored.Resume();
        for (var attempt = 0; attempt < 80 && restored.Conversations.Single().Turns.Count < 3; attempt++)
        {
            _ = await restored.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }

        var resumedConversation = Assert.Single(restored.Conversations);
        Assert.Equal(3, resumedConversation.Turns.Count);
        Assert.Contains(provider.TurnRequests, request => request.PublicHistory.Count == 2 &&
            request.PublicHistory[0] == turn && request.PublicHistory[1] == secondTurn);
        var afterResume = restored.ExportState();
        var thirdTurn = resumedConversation.Turns[2];
        Assert.Equal(secondTurn.SpeakerId == InitiatorId ? InviteeId : InitiatorId, thirdTurn.SpeakerId);
        Assert.Contains(thirdTurn.ListenerIds, listenerId => listenerId != InitiatorId && listenerId != InviteeId);
        foreach (var listenerId in thirdTurn.ListenerIds.Where(listenerId => listenerId != thirdTurn.SpeakerId))
            Assert.Contains(afterResume.Society.Society.Beliefs!, item =>
                item.OwnerId == listenerId && item.SourceTurnId == thirdTurn.Id &&
                item.Provenance == SocietyBeliefProvenance.Hearsay && item.Statement == thirdTurn.Text);
        Assert.DoesNotContain(afterResume.Society.Society.Beliefs!, item =>
            item.SourceTurnId == thirdTurn.Id && !thirdTurn.ListenerIds.Contains(item.OwnerId));
    }

    [Fact]
    public async Task TrimmingOldConversationKeepsCorrectedPrivateHearsayAndCompactionAcrossAnotherHeardTurn()
    {
        const string seed = "conversation-history-trimming";
        const string oldConversationId = "conversation:000-old-heard-claim";
        const string oldBeliefId = "conversation-belief:old-source";
        const string correctionId = "conversation-belief:corrected-source";
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        });
        using var setup = NewWorld(seed, provider);
        setup.StartWorld();
        var initial = setup.ExportState();
        var society = initial.Society.Society;
        var old = ClosedConversationWithOneTurn(oldConversationId, society.WorldTick, society.RunEpoch);
        var conversations = new List<AgentConversation> { old.Conversation };
        for (var index = 0; index < AgentConversationRules.MaximumSavedConversations - 1; index++)
        {
            var proposal = AgentConversationRules.Propose(
                $"conversation:closed:{index:D2}", InitiatorId, InviteeId, society.WorldTick, society.RunEpoch);
            Assert.True(AgentConversationRules.TryDeclineProposal(
                proposal, InviteeId, society.WorldTick, out var refused));
            conversations.Add(refused);
        }

        var oldClaim = new SocietyAgentBelief(
            oldBeliefId, ListenerId, old.Turn.Text, SocietyBeliefProvenance.Hearsay,
            5_000, old.Turn.WorldTick, old.Turn.SpeakerId, SourceTurnId: old.Turn.Id);
        society = SocietyFixture.RecordAgentBelief(society, oldClaim);
        var correctedClaim = SocietyFixture.CorrectAgentBelief(society, ListenerId, oldClaim.Id,
            new SocietyAgentBelief(
                correctionId, ListenerId, "I later learned the speaker had been mistaken about the trail.",
                SocietyBeliefProvenance.Hearsay, 7_500, old.Turn.WorldTick,
                old.Turn.SpeakerId, SourceTurnId: old.Turn.Id));
        society = SocietyFixture.RecordAgentMemoryCompaction(correctedClaim, ListenerId,
        [
            new SocietyAgentMemoryImportance(
                correctionId, SocietyMemorySourceKind.Belief, old.Turn.WorldTick, 8_000, 8_500,
                old.Turn.WorldTick),
        ]);

        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Society = initial.Society with { Society = society },
            Conversations = conversations,
            ConversationBudgets = [],
        }, id => id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());
        for (var attempt = 0; attempt < 50 && world.Conversations.All(item =>
                 item.Status != AgentConversationStatus.Proposed); attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }

        var newConversation = Assert.Single(world.Conversations, item => item.Status == AgentConversationStatus.Proposed);
        Assert.Equal(AgentConversationRules.MaximumSavedConversations, world.Conversations.Count);
        Assert.DoesNotContain(world.Conversations, item => item.Id == oldConversationId);
        var afterTrim = world.ExportState().Society.Society;
        var retainedOldClaim = Assert.Single(afterTrim.Beliefs!, item => item.Id == oldBeliefId);
        var retainedCorrection = Assert.Single(afterTrim.Beliefs!, item => item.Id == correctionId);
        Assert.Null(retainedOldClaim.SourceTurnId);
        Assert.Null(retainedCorrection.SourceTurnId);
        Assert.Equal(old.Turn.SpeakerId, retainedCorrection.SourceAgentId);
        Assert.Equal("I later learned the speaker had been mistaken about the trail.", retainedCorrection.Statement);
        Assert.Equal(correctionId, retainedOldClaim.SupersededByBeliefId);
        Assert.Equal(oldBeliefId, retainedCorrection.SupersedesBeliefId);
        Assert.Contains(Assert.Single(afterTrim.MemoryCompactions!, item => item.OwnerId == ListenerId).Sources,
            item => item.Kind == SocietyMemorySourceKind.Belief && item.SourceId == correctionId);

        for (var attempt = 0; attempt < 90 && world.Conversations.Single(item => item.Id == newConversation.Id)
                 .Turns.Count == 0; attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }
        var firstNewTurn = Assert.Single(world.Conversations.Single(item => item.Id == newConversation.Id).Turns);
        Assert.Contains(ListenerId, firstNewTurn.ListenerIds);
        var fileDirectory = Directory.CreateTempSubdirectory("clankerworld-trimmed-conversation-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(fileDirectory.FullName, "world.json"), id =>
                id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());
            file.Save(world);
            using var reloaded = file.LoadOrCreate(seed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()), File.ReadAllBytes(file.Path));
            var afterReloadSociety = reloaded.ExportState().Society.Society;
            Assert.Contains(afterReloadSociety.Beliefs!, item => item.Id == correctionId && item.SourceTurnId is null &&
                item.SupersedesBeliefId == oldBeliefId);
            Assert.Contains(Assert.Single(afterReloadSociety.MemoryCompactions!, item => item.OwnerId == ListenerId).Sources,
                item => item.Kind == SocietyMemorySourceKind.Belief && item.SourceId == correctionId);

            reloaded.Resume();
            for (var attempt = 0; attempt < 120 && reloaded.Conversations.Single(item => item.Id == newConversation.Id)
                     .Turns.Count < 2; attempt++)
            {
                _ = await reloaded.AdvanceOneTickNonBlockingAsync();
                await Task.Delay(5);
            }
            var retainedConversation = Assert.Single(reloaded.Conversations, item => item.Id == newConversation.Id);
            Assert.True(retainedConversation.Turns.Count >= 2);
            var heardTurns = retainedConversation.Turns.Take(2).ToArray();
            Assert.All(heardTurns, turn =>
            {
                Assert.Contains(turn.SpeakerId == InitiatorId ? InviteeId : InitiatorId, turn.ListenerIds);
                Assert.Contains(turn.ListenerIds, listenerId => listenerId != InitiatorId && listenerId != InviteeId);
            });
            var afterSecondTurn = reloaded.ExportState();
            foreach (var turn in heardTurns)
                foreach (var listenerId in turn.ListenerIds)
                    Assert.Contains(afterSecondTurn.Society.Society.Beliefs!, item =>
                        item.OwnerId == listenerId && item.SourceTurnId == turn.Id && item.Statement == turn.Text);

            reloaded.Pause();
            var roundTripState = reloaded.ExportState();
            var roundTripBytes = PrivateWorldRuntimeCodec.Encode(roundTripState);
            using var finalRestore = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(roundTripBytes),
                id => id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());
            var expectedAfterRestore = roundTripState with
            {
                Conversations = roundTripState.Conversations!.Select(item => item.Id == newConversation.Id
                    ? item with
                    {
                        ResumeAcceptedBy = [],
                        Interruption = AgentConversationInterruption.Restored,
                        Revision = item.Revision + 1,
                        LastUpdatedTick = reloaded.WorldTick,
                    } : item).ToArray(),
            };
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(expectedAfterRestore),
                PrivateWorldRuntimeCodec.Encode(finalRestore.ExportState()));
            var finalSociety = finalRestore.ExportState().Society.Society;
            Assert.Contains(finalSociety.Beliefs!, item => item.Id == correctionId && item.SourceTurnId is null &&
                item.SupersededByBeliefId is null);
            Assert.Contains(Assert.Single(finalSociety.MemoryCompactions!, item => item.OwnerId == ListenerId).Sources,
                item => item.Kind == SocietyMemorySourceKind.Belief && item.SourceId == correctionId);
        }
        finally
        {
            fileDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TimedOutProviderSuspendsWithoutSavingItsErrorOrUnadmittedText()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        })
        {
            ConversationFailure = new TimeoutException("secret provider payload must not be saved"),
        };
        using var world = NewWorld("conversation-timeout", provider);
        world.StartWorld();

        for (var attempt = 0; attempt < 40 && world.Conversations.All(item =>
                 item.Status != AgentConversationStatus.Suspended); attempt++)
        {
            _ = await world.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(5);
        }

        var conversation = Assert.Single(world.Conversations);
        Assert.Equal(AgentConversationStatus.Suspended, conversation.Status);
        Assert.Equal(AgentConversationInterruption.ProviderTimedOut, conversation.Interruption);
        Assert.Empty(conversation.Turns);
        Assert.Single(provider.TurnRequests);
        var saved = world.ExportState();
        Assert.DoesNotContain(saved.Events, item => item.Detail.Contains("secret provider payload", StringComparison.Ordinal));
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, saved.SchemaVersion);
    }

    [Fact]
    public async Task ConversationProviderDeadlineCancelsUnderlyingProviderOperation()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        })
        {
            BlockConversationUntilCanceled = true,
        };
        var request = new AgentConversationTurnRequest(
            "conversation-timeout-check", "conversation:timeout-check", 1, 0, 0,
            AgentConversationPurpose.PublicTurn, InitiatorId, "Aster", InviteeId, "Rowan",
            "patient", "learn", [], [AgentConversationEffect.None]);

        var timeout = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await AgentConversationProviderExecution.SpeakAsync(
                provider, request, TimeSpan.FromMilliseconds(25), CancellationToken.None));
        await provider.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(AgentConversationInterruption.ProviderTimedOut,
            AgentConversationFailureClassifier.Classify(timeout));
        Assert.Single(provider.TurnRequests);
    }

    private static PrivateWorldRuntime NewWorld(string seed, ConversationProvider provider) =>
        NewWorld(seed, id => id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());

    private static (AgentConversation Conversation, AgentConversationTurn Turn) ClosedConversationWithOneTurn(
        string id,
        long worldTick,
        long runEpoch)
    {
        var proposal = AgentConversationRules.Propose(id, InitiatorId, InviteeId, worldTick, runEpoch);
        Assert.True(AgentConversationRules.TryAcceptProposal(proposal, InviteeId, worldTick, runEpoch, out var accepted));
        Assert.True(AgentConversationRules.TryBeginTurn(accepted, worldTick, runEpoch, out var begun));
        var request = new AgentConversationTurnRequest(
            $"{id}:request", id, begun.Revision, runEpoch, worldTick, AgentConversationPurpose.PublicTurn,
            InitiatorId, "Aster", InviteeId, "Rowan", "patient", "learn", [], [AgentConversationEffect.None]);
        var response = new AgentConversationTurnResponse(
            request.RequestId, id, request.Revision, runEpoch, InitiatorId,
            "A public claim about the trail.", AgentConversationDisposition.Continue);
        Assert.True(AgentConversationRules.TryAdmitTurn(
            begun, request, response, [InviteeId, ListenerId], worldTick, runEpoch,
            out var admitted, out var turn));
        Assert.NotNull(turn);
        var closed = AgentConversationRules.CloseUnavailable(admitted, worldTick);
        AgentConversationRules.Validate(closed, worldTick);
        return (closed, turn);
    }

    private static PrivateWorldRuntime NewWorld(string seed, Func<string, IDecisionProvider> providerFactory)
    {
        var world = new PrivateWorldRuntime(seed, providerFactory, startPace: WorldStartPace.FounderSetup);
        var map = world.ExportState().Map;
        var blocked = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        var available = (from y in Enumerable.Range(0, map.Height)
                         from x in Enumerable.Range(0, map.Width)
                         let point = new GridPoint(x, y)
                         where map.IsBuildable(point) && !blocked.Contains(point)
                         select point).ToArray();

        GridPoint[]? placements = null;
        foreach (var initiator in available)
        {
            foreach (var invitee in available.Where(point =>
                         point != initiator && map.FootDistance(initiator, point) == 1))
            {
                var listeners = available.Where(point => point != initiator && point != invitee &&
                        map.FootDistance(initiator, point) <= 3 && map.FootDistance(invitee, point) <= 3)
                    .Take(1).ToArray();
                if (listeners.Length == 0) continue;
                var distant = available.Where(point => point != initiator && point != invitee && point != listeners[0] &&
                        map.FootDistance(initiator, point) > 3 && map.FootDistance(invitee, point) > 3)
                    .Take(1).ToArray();
                if (distant.Length == 0) continue;
                placements = [initiator, invitee, listeners[0], distant[0]];
                break;
            }
            if (placements is not null) break;
        }

        if (placements is null)
            throw new InvalidOperationException("The conversation test map needs three nearby and one distant buildable tile.");
        world.PlaceFounder(InitiatorId, placements[0]);
        world.PlaceFounder(InviteeId, placements[1]);
        world.PlaceFounder(ListenerId, placements[2]);
        world.PlaceFounder(DistantId, placements[3]);
        return world;
    }

    private static async Task AdvanceConfiguredConversationTickAsync(
        PrivateWorldRuntime world, ConversationHttpHandler handler)
    {
        // Only this fixture advances ticks, so pending work can be snapshotted
        // between ticks. Await its tasks before advancing simulation deadlines;
        // deliberately held responses wait only for their request-start barrier.
        static object[] PendingWork(PrivateWorldRuntime runtime, string fieldName)
        {
            var field = typeof(PrivateWorldRuntime).GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);
            var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(field.GetValue(runtime));
            return pending.Values.Cast<object>().ToArray();
        }

        static Task WorkTask(object pending) =>
            Assert.IsAssignableFrom<Task>(pending.GetType().GetProperty("Task")!.GetValue(pending));

        var decisions = PendingWork(world, "pendingHosted").Select(pending =>
        {
            var request = Assert.IsType<CognitionDecisionRequest>(
                pending.GetType().GetProperty("Request")!.GetValue(pending));
            return handler.HeldDecisionStarted(request) ?? WorkTask(pending);
        });
        var turns = PendingWork(world, "pendingConversationTurns").Select(WorkTask);
        await Task.WhenAll(decisions.Concat(turns)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
    }

    private static OwnerProviderConfigurationAction Personal(string agentId, string model, string key) =>
        new("personal", "openai", model, key, false, agentId, Guid.NewGuid().ToString("N"), $"{agentId} test credential");

    private static int TrustScore(PrivateWorldRuntime world, string ownerId, string subjectId) =>
        world.Inhabitants.Single(item => item.InhabitantId == ownerId).SocialStanding?
            .SingleOrDefault(item => item.SubjectId == subjectId)?.Trust ?? 0;

    private sealed class ConversationProvider(IReadOnlySet<string> assignedAgents) :
        IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long CurrentProviderEpoch { get; set; } = 1;
        public long ProviderEpoch => CurrentProviderEpoch;
        long IAgentConversationProvider.ProviderEpoch => CurrentProviderEpoch;
        public List<AgentConversationTurnRequest> TurnRequests { get; } = [];
        public ConcurrentQueue<CognitionDecisionRequest> PlanningRequests { get; } = new();
        public string? OnlyResumingAgentId { get; init; }
        public AgentConversationEffect SuggestedWrapUpEffect { get; init; }
        public bool HoldWrapUpChoices { get; init; }
        public bool EndSuspendedConversations { get; set; }
        public Exception? ConversationFailure { get; init; }
        public bool BlockConversationUntilCanceled { get; init; }
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanSpeakAs(string agentId) => assignedAgents.Contains(agentId);

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var observation = request.Observation;
            PlanningRequests.Enqueue(request);
            if (HoldWrapUpChoices && observation.Candidates.Any(candidate =>
                    candidate.Id.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal)))
                return new ValueTask<CognitionDecisionResponse>(WaitForPlanningCancellationAsync(cancellationToken));
            var selected = observation.Candidates.FirstOrDefault(candidate =>
                observation.InhabitantId == InitiatorId && candidate.Id == $"talk:{InviteeId}") ??
                observation.Candidates.FirstOrDefault(candidate => EndSuspendedConversations &&
                    candidate.Id.StartsWith("conversation_end:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal) &&
                    (OnlyResumingAgentId is null || observation.InhabitantId == OnlyResumingAgentId)) ??
                observation.Candidates.FirstOrDefault(candidate =>
                    observation.InhabitantId == InviteeId && candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => candidate.Id == "safe_idle") ??
                observation.Candidates[0];
            return ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId,
                observation.InhabitantId,
                Kind,
                ProviderEpoch,
                observation.RunEpoch,
                observation.DecisionGeneration,
                observation.ObservationDigest,
                selected.Id,
                1d,
                observation.Candidates.ToDictionary(item => item.Id,
                    item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                ChosenName: observation.NeedsName ? observation.Self?.Name ?? "Test Agent" : null,
                ChosenPersonality: observation.NeedsPersonality ? "patient and curious" : null,
                ChosenAspiration: observation.NeedsAspiration ? "learn the valley" : null));
        }

        private static async Task<CognitionDecisionResponse> WaitForPlanningCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
        }

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(
            AgentConversationTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Validate();
            TurnRequests.Add(request);
            if (ConversationFailure is { } failure) throw failure;
            if (BlockConversationUntilCanceled)
                return new ValueTask<AgentConversationTurnResponse>(WaitForCancellationAsync(cancellationToken));
            return ValueTask.FromResult(new AgentConversationTurnResponse(
                request.RequestId,
                request.ConversationId,
                request.Revision,
                request.RunEpoch,
                request.SpeakerId,
                "A public turn with no world command.",
                AgentConversationDisposition.Continue,
                request.Purpose == AgentConversationPurpose.WrapUp ? SuggestedWrapUpEffect : AgentConversationEffect.None));
        }

        private async Task<AgentConversationTurnResponse> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
            throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
        }
    }

    private sealed class ConversationHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record ConversationProviderCall(string SpeakerId, string Model, string? Authorization, string Context);

    private sealed class ConversationHttpHandler(string blockedInviteeId, bool holdInitialChoices = false) : HttpMessageHandler
    {
        public const string PrivateDecisionThought = "private decision thought must never enter a conversation";
        private readonly TaskCompletionSource releaseWrapUpAcceptance = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseInitiatorTalk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseStaleInviteeTalk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int initiatorTalkHeld;
        private int staleInviteeTalkHeld;
        public TaskCompletionSource WrapUpAcceptanceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InitiatorTalkStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StaleInviteeTalkStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<ConversationProviderCall> ConversationCalls { get; } = new();
        public ConcurrentQueue<string> DecisionCalls { get; } = new();
        public ConcurrentQueue<string> InviteeDecisionCalls { get; } = new();

        public Task? HeldDecisionStarted(CognitionDecisionRequest request)
        {
            var agentId = request.Observation.InhabitantId;
            var candidates = request.Observation.Candidates;
            if (holdInitialChoices && agentId == InitiatorId && !releaseInitiatorTalk.Task.IsCompleted &&
                candidates.Any(candidate => candidate.Id == $"talk:{InviteeId}"))
                return InitiatorTalkStarted.Task;
            if (holdInitialChoices && agentId == blockedInviteeId && !releaseStaleInviteeTalk.Task.IsCompleted &&
                candidates.Any(candidate => candidate.Id == $"talk:{InitiatorId}") &&
                !candidates.Any(candidate => candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)))
                return StaleInviteeTalkStarted.Task;
            if (agentId == blockedInviteeId && !releaseWrapUpAcceptance.Task.IsCompleted &&
                candidates.Any(candidate => candidate.Id.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal)))
                return WrapUpAcceptanceStarted.Task;
            return null;
        }

        public void ReleaseWrapUpAcceptance() => releaseWrapUpAcceptance.TrySetResult();
        public void ReleaseInitiatorTalk() => releaseInitiatorTalk.TrySetResult();
        public void ReleaseStaleInviteeTalk() => releaseStaleInviteeTalk.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = JsonDocument.Parse(body);
            var root = payload.RootElement;
            var model = root.GetProperty("model").GetString()!;
            var authorization = request.Headers.Authorization?.ToString();
            var userContext = root.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var contextDocument = JsonDocument.Parse(userContext);
            var context = contextDocument.RootElement;

            string answer;
            if (context.TryGetProperty("agent_id", out var agentProperty))
            {
                var agentId = agentProperty.GetString()!;
                var candidates = context.GetProperty("candidates").EnumerateArray()
                    .Select(candidate => candidate.GetProperty("id").GetString()!).ToArray();
                var selected = agentId == InitiatorId
                    ? candidates.FirstOrDefault(candidate => candidate == $"talk:{InviteeId}")
                    : null;
                selected ??= agentId == InviteeId
                    ? candidates.FirstOrDefault(candidate => candidate.StartsWith("conversation_accept:", StringComparison.Ordinal))
                    : null;
                selected ??= agentId == InviteeId
                    ? candidates.FirstOrDefault(candidate => candidate == $"talk:{InitiatorId}")
                    : null;
                selected ??= candidates.FirstOrDefault(candidate => candidate.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal));
                selected ??= candidates.FirstOrDefault(candidate => candidate == "safe_idle") ?? candidates.FirstOrDefault();
                if (selected is null)
                    throw new InvalidDataException("The controlled decision request had no candidate.");
                DecisionCalls.Enqueue($"{agentId}:{string.Join(',', candidates)}=>{selected}");
                if (agentId == blockedInviteeId)
                    InviteeDecisionCalls.Enqueue($"{string.Join(',', candidates)}=>{selected}");

                if (holdInitialChoices && agentId == InitiatorId && selected == $"talk:{InviteeId}" &&
                    Interlocked.Exchange(ref initiatorTalkHeld, 1) == 0)
                {
                    InitiatorTalkStarted.TrySetResult();
                    await releaseInitiatorTalk.Task.WaitAsync(cancellationToken);
                }

                if (holdInitialChoices && agentId == blockedInviteeId &&
                    candidates.Any(candidate => candidate == $"talk:{InitiatorId}") &&
                    !candidates.Any(candidate => candidate.StartsWith("conversation_accept:", StringComparison.Ordinal)) &&
                    Interlocked.Exchange(ref staleInviteeTalkHeld, 1) == 0)
                {
                    StaleInviteeTalkStarted.TrySetResult();
                    await releaseStaleInviteeTalk.Task.WaitAsync(cancellationToken);
                }

                if (agentId == blockedInviteeId &&
                    selected.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal))
                {
                    WrapUpAcceptanceStarted.TrySetResult();
                    await releaseWrapUpAcceptance.Task.WaitAsync(cancellationToken);
                }

                var decision = new Dictionary<string, object?>
                {
                    ["selected_candidate_id"] = selected,
                    ["confidence"] = 1d,
                    ["private_thought"] = PrivateDecisionThought,
                };
                if (context.GetProperty("needs_personality").GetBoolean())
                    decision["chosen_personality"] = "patient and curious";
                if (context.GetProperty("needs_aspiration").GetBoolean())
                    decision["chosen_aspiration"] = "learn the valley";
                if (context.GetProperty("needs_name").GetBoolean())
                    decision["chosen_name"] = agentId switch
                    {
                        InitiatorId => "Aster Vale",
                        InviteeId => "Rowan Vale",
                        ListenerId => "Mira Vale",
                        _ => "Ilya Vale",
                    };
                answer = JsonSerializer.Serialize(decision);
            }
            else
            {
                var speakerId = context.GetProperty("speaker").GetProperty("id").GetString()!;
                var purpose = context.GetProperty("purpose").GetString()!;
                ConversationCalls.Enqueue(new(speakerId, model, authorization, userContext));
                answer = JsonSerializer.Serialize(new
                {
                    utterance = purpose == "wrap_up"
                        ? "We could trust one another after talking this through."
                        : "I would give you every storehouse key if words could change the world.",
                    disposition = "continue",
                    effect = purpose == "wrap_up" ? "mutual_trust" : "none",
                });
            }

            var responseBody = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = answer } } },
                usage = new { prompt_tokens = 7, completion_tokens = 3 },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
