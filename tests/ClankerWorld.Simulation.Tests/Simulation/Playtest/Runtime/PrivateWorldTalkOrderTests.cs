using System.Reflection;
using System.Text.Json;
using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    private static readonly JsonSerializerOptions TalkHostJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions TalkGameJson = new() { PropertyNameCaseInsensitive = true };
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TalkOrderRunsARealInvitationAndWaitsForItsConsentedOutcome(bool decline)
    {
        var provider = new TalkOrderProvider(decline);
        using var world = NewWorld("talk-order-outcome", id => id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider());
        world.StartWorld();
        var receipt = world.SubmitInstruction(new("ordered-talk", "owner:test", InitiatorId,
            OwnerInstructionKind.MustDo, "Talk to " + InviteeId));
        var submitted = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("talk_to", submitted.Action);
        Assert.Equal(InviteeId, submitted.TargetAgentId);
        var destination = world.ExportState().Map.FootNeighbors(world.Inhabitants.Single(item => item.InhabitantId == InitiatorId).Position)
            .First(point => world.ExportState().Map.IsPassable(point) && world.Inhabitants.All(item => item.Position != point));
        world.SubmitInstruction(new("after-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo,
            $"Move to ({destination.X}, {destination.Y})", Queue: true));
        var originalPosition = world.Inhabitants.Single(item => item.InhabitantId == InitiatorId).Position;
        for (var tick = 0; tick < 40 && world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!.Status != "finished"; tick++)
        {
            await AdvanceTalkOrderTick(world);
            if (world.Conversations.Any(item => item.Status != AgentConversationStatus.Closed))
                Assert.Equal(originalPosition, world.Inhabitants.Single(item => item.InhabitantId == InitiatorId).Position);
        }
        var conversation = Assert.Single(world.Conversations);
        Assert.Equal(AgentConversationStatus.Closed, conversation.Status);
        Assert.Equal(decline ? "refused" : "agreed", conversation.Outcome);
        Assert.Equal((InitiatorId, InviteeId), (conversation.InitiatorId, conversation.InviteeId));
        var completed = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("finished", completed.Status);
        Assert.Equal(1, completed.CompletedUnits);
        Assert.Equal(InviteeId, completed.TargetAgentId);
        Assert.Equal(conversation.Id, ReceivedTalkOrder(world).TalkConversationId);
        Assert.Equal("closed", ReceivedTalkOrder(world).TalkStatus);
        Assert.Equal(decline ? "refused" : "agreed", ReceivedTalkOrder(world).TalkOutcome);
        Assert.Single(world.ExportState().Events, item => item.Kind == "conversation_proposed");
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_order_finished" && item.Detail.Contains(receipt.InstructionId, StringComparison.Ordinal));
        if (decline) Assert.Empty(provider.TurnRequests);
        else
        {
            Assert.Equal(AgentConversationRules.MaximumPublicTurns, conversation.Turns.Count(turn => !turn.IsWrapUp));
            Assert.Contains(provider.TurnRequests, request => request.SpeakerId == InitiatorId);
            Assert.Contains(provider.TurnRequests, request => request.SpeakerId == InviteeId);
        }
        Assert.Equal(0, TrustScore(world, InitiatorId, InviteeId));
        Assert.Equal(0, TrustScore(world, InviteeId, InitiatorId));
        for (var tick = 0; tick < 8 && world.Inhabitants.Single(item => item.InhabitantId == InitiatorId).Position != destination; tick++)
            await AdvanceTalkOrderTick(world);
        Assert.Equal(destination, world.Inhabitants.Single(item => item.InhabitantId == InitiatorId).Position);
        Assert.Single(world.Conversations);
    }

    [Fact]
    public async Task QueuedTalkKeepsTheNamedPersonAcrossRenameSaveAndTypo()
    {
        var provider = new TalkOrderProvider(decline: true);
        Func<string, IDecisionProvider> factory = id => id is InitiatorId or InviteeId ? provider : new DeterministicDecisionProvider();
        using var setup = NewWorld("talk-order-name", factory);
        setup.StartWorld();
        var initial = setup.ExportState();
        using var namedSociety = SocietyWorldRuntime.Restore(initial.Society);
        namedSociety.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, InviteeId, "Élodie Reed"));
        using var named = PrivateWorldRuntime.Restore(initial with { Society = namedSociety.ExportState() }, factory);
        var held = named.SubmitInstruction(new("hold-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Move to (1000,1000)"));
        var talk = named.SubmitInstruction(new("named-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo,
            "Please speak with E\u0301lodie", Queue: true));
        named.SubmitInstruction(new("talk-typo", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to Élodi"));
        var queued = named.ExportState();
        Assert.Equal("queued", queued.Instructions!.Single(item => item.InstructionId == talk.InstructionId).Order!.Status);
        Assert.Equal(InviteeId, queued.Instructions!.Single(item => item.InstructionId == talk.InstructionId).Order!.TargetAgentId);
        using var renamed = SocietyWorldRuntime.Restore(queued.Society);
        renamed.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, InviteeId, "River Reed"));
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            queued with { Society = renamed.ExportState() })), factory);
        world.CancelOrder(new("release-talk", "owner:test", world.Society.WorldId, InitiatorId, held.InstructionId));
        for (var tick = 0; tick < 30 && !world.ExportState().CompletedInstructionIds!.Contains(talk.InstructionId); tick++)
            await AdvanceTalkOrderTick(world);
        Assert.Equal(InviteeId, Assert.Single(world.Conversations).InviteeId);
        Assert.Equal("refused", Assert.Single(world.Conversations).Outcome);
        Assert.Equal(1, world.ExportState().Instructions!.Single(item => item.InstructionId == talk.InstructionId).Order!.CompletedUnits);
        Assert.Equal("not_understood", world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "talk-typo").Order!.Status);
    }

    [Fact]
    public async Task GeneratedWorldTalkApproachesOnFootAndReplaysItsActualInvitation()
    {
        var provider = new TalkOrderProvider(decline: true);
        using var world = NormalPathWorld.CreateGenerated("normal-talk-order", _ => provider);
        var people = world.Inhabitants.OrderBy(item => item.InhabitantId, StringComparer.Ordinal).ToArray();
        provider.SpeakIds.UnionWith(people.Select(item => item.InhabitantId));
        var pair = (from actor in people
                    from target in people
                    where actor.InhabitantId != target.InhabitantId
                    orderby world.ExportState().Map.FootDistance(actor.Position, target.Position) descending
                    select (actor, target)).First();
        Assert.True(world.ExportState().Map.FootDistance(pair.actor.Position, pair.target.Position) > 1);
        provider.DeclineAgent = pair.target.InhabitantId;
        var order = world.SubmitInstruction(new("generated-talk", "owner:test", pair.actor.InhabitantId,
            OwnerInstructionKind.MustDo, "Talk to " + pair.target.InhabitantId));
        var initial = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), _ => provider);
        for (var tick = 0; tick < 40 && !world.ExportState().CompletedInstructionIds!.Contains(order.InstructionId); tick++)
        {
            await AdvanceTalkOrderTick(world);
            await AdvanceTalkOrderTick(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (world.Conversations.Count != 0)
                Assert.True(world.ExportState().Map.FootDistance(
                    world.Inhabitants.Single(item => item.InhabitantId == pair.actor.InhabitantId).Position,
                    world.Inhabitants.Single(item => item.InhabitantId == pair.target.InhabitantId).Position) <= 1);
        }
        Assert.Equal("refused", Assert.Single(world.Conversations).Outcome);
        Assert.Equal(1, world.ExportState().Instructions!.Single().Order!.CompletedUnits);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "instruction_order_finished" && item.WorldTick < Assert.Single(world.Conversations).CreatedTick);
    }

    [Fact]
    public async Task LoadedTalkWaitsForTwoFreshResumeChoicesWithoutAnotherInvitation()
    {
        var provider = new TalkOrderProvider();
        using var world = NewWorld("loaded-talk-order", _ => provider);
        world.StartWorld();
        var receipt = world.SubmitInstruction(new("saved-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk with " + InviteeId));
        for (var tick = 0; tick < 20 && world.Conversations.All(item => item.Turns.Count == 0); tick++)
            await AdvanceTalkOrderTick(world);
        var active = Assert.Single(world.Conversations);
        Assert.NotEmpty(active.Turns);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => provider);
        Assert.Equal(AgentConversationStatus.Suspended, Assert.Single(restored.Conversations).Status);
        Assert.Equal("suspended", ReceivedTalkOrder(restored).TalkStatus);
        for (var tick = 0; tick < 5; tick++) await AdvanceTalkOrderTick(restored);
        Assert.Equal(active.Turns.Count, Assert.Single(restored.Conversations).Turns.Count);
        Assert.Empty(Assert.Single(restored.Conversations).ResumeAcceptedBy);
        provider.ResumeAgents.TryAdd(InitiatorId, 0);
        for (var tick = 0; tick < 40 && Assert.Single(restored.Conversations).ResumeAcceptedBy.Count == 0; tick++)
            await AdvanceTalkOrderTick(restored);
        Assert.Equal([InitiatorId], Assert.Single(restored.Conversations).ResumeAcceptedBy);
        Assert.Equal(active.Turns.Count, Assert.Single(restored.Conversations).Turns.Count);
        using var halfReloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState())), _ => provider);
        Assert.Empty(Assert.Single(halfReloaded.Conversations).ResumeAcceptedBy);
        provider.ResumeAgents.TryAdd(InviteeId, 0);
        for (var tick = 0; tick < 70 && !halfReloaded.ExportState().CompletedInstructionIds!.Contains(receipt.InstructionId); tick++)
            await AdvanceTalkOrderTick(halfReloaded);
        var completed = Assert.Single(halfReloaded.Conversations);
        Assert.Equal(active.Id, completed.Id);
        Assert.Equal("agreed", completed.Outcome);
        Assert.Equal(1, halfReloaded.ExportState().Instructions!.Single().Order!.CompletedUnits);
        Assert.Single(halfReloaded.ExportState().Events, item => item.Kind == "conversation_proposed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingOrReplacingTalkBeforeInvitationDoesNotSendIt(bool replace)
    {
        var provider = new TalkOrderProvider();
        using var world = NewWorld("cancel-talk-order", _ => provider);
        world.StartWorld();
        var talk = world.SubmitInstruction(new("cancel-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to " + InviteeId));
        if (replace) world.SubmitInstruction(new("replace-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Move to (1000,1000)"));
        else world.CancelOrder(new("cancel-talk-receipt", "owner:test", world.Society.WorldId, InitiatorId, talk.InstructionId));
        for (var tick = 0; tick < 5; tick++) await AdvanceTalkOrderTick(world);
        Assert.Empty(world.Conversations);
        Assert.Equal("cancelled", world.ExportState().Instructions!.Single(item => item.InstructionId == talk.InstructionId).Order!.Status);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => provider);
        Assert.Empty(restored.Conversations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TalkReportsMissingModelOrAllowanceWithoutSpendingAnInvitation(bool allowance)
    {
        var provider = new TalkOrderProvider();
        Func<string, IDecisionProvider> factory = id => !allowance && id == InviteeId ? new DeterministicDecisionProvider() : provider;
        using var setup = NewWorld("blocked-talk-order", factory);
        setup.StartWorld();
        var initial = setup.ExportState();
        using var world = PrivateWorldRuntime.Restore(allowance ? initial with
        {
            ConversationBudgets = [new AgentConversationDailyBudget(InviteeId,
                initial.Society.Society.WorldTick / initial.Society.Society.Config.TicksPerWorldDay,
                AgentConversationRules.MaximumConversationsPerWorldDay)],
        } : initial, factory);
        world.SubmitInstruction(new("blocked-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to " + InviteeId));
        for (var tick = 0; tick < 4; tick++) await AdvanceTalkOrderTick(world);
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Contains(allowance ? "allowance" : "model", order.BlockedReason);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Empty(world.Conversations);
        Assert.DoesNotContain(world.ConversationBudgets, item => item.AgentId == InitiatorId);
    }

    [Fact]
    public async Task TalkSaveRejectsChangedPersonLostInvitationAndForgedProgress()
    {
        var provider = new TalkOrderProvider(decline: true);
        using var world = NewWorld("talk-save-bindings", _ => provider);
        world.StartWorld();
        world.SubmitInstruction(new("validated-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to " + InviteeId));
        for (var tick = 0; tick < 10 && world.Conversations.Count == 0; tick++) await AdvanceTalkOrderTick(world);
        var proposed = world.ExportState();
        Assert.Equal("proposed", ReceivedTalkOrder(world).TalkStatus);
        Assert.Null(ReceivedTalkOrder(world).TalkOutcome);
        var instruction = Assert.Single(proposed.Instructions!);
        Assert.Equal(Assert.Single(proposed.Conversations!).Id, instruction.Order!.TalkConversationId);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(proposed with { Conversations = [] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(proposed with
        { Instructions = [instruction with { Order = instruction.Order with { TargetAgentId = ListenerId } }] }));
        using var proposedReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(proposed)), _ => provider);
        for (var tick = 0; tick < 20 && Assert.Single(proposedReload.Conversations).Status != AgentConversationStatus.Closed; tick++)
            await AdvanceTalkOrderTick(proposedReload);
        var completed = proposedReload.ExportState();
        var finished = Assert.Single(completed.Instructions!);
        Assert.Equal(1, finished.Order!.CompletedUnits);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Instructions = [finished with { Order = finished.Order with { LastEffectId = "talk-order:forged" } }] }));
        using var finalReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(completed)), _ => provider);
        for (var tick = 0; tick < 4; tick++) await AdvanceTalkOrderTick(finalReload);
        Assert.Single(finalReload.Conversations);
        Assert.Equal(finished.Order, Assert.Single(finalReload.ExportState().Instructions!).Order);
        // Finished task receipts outlive the bounded conversation history.
        using var trimmed = PrivateWorldRuntime.Restore(completed with { Conversations = [] }, _ => provider);
        Assert.Equal("closed", ReceivedTalkOrder(trimmed).TalkStatus);
        Assert.Equal("refused", ReceivedTalkOrder(trimmed).TalkOutcome);
    }

    [Fact]
    public async Task ReplacingATalkTaskLetsItsConversationEndBeforeTheNextInvitation()
    {
        var provider = new TalkOrderProvider();
        provider.SpeakIds.Add(ListenerId);
        using var world = NewWorld("replace-active-talk", _ => provider);
        world.StartWorld();
        var first = world.SubmitInstruction(new("first-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to " + InviteeId));
        for (var tick = 0; tick < 20 && world.Conversations.All(item => item.Turns.Count == 0); tick++) await AdvanceTalkOrderTick(world);
        Assert.NotEmpty(Assert.Single(world.Conversations).Turns);
        var second = world.SubmitInstruction(new("next-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to " + ListenerId));
        for (var tick = 0; tick < 70 && !world.ExportState().CompletedInstructionIds!.Contains(second.InstructionId); tick++) await AdvanceTalkOrderTick(world);
        Assert.Equal(2, world.Conversations.Count);
        Assert.All(world.Conversations, item => Assert.Equal("agreed", item.Outcome));
        var orders = world.ExportState().Instructions!;
        Assert.Equal(("cancelled", 0), (orders.Single(item => item.InstructionId == first.InstructionId).Order!.Status,
            orders.Single(item => item.InstructionId == first.InstructionId).Order!.CompletedUnits));
        Assert.Equal(("finished", 1), (orders.Single(item => item.InstructionId == second.InstructionId).Order!.Status,
            orders.Single(item => item.InstructionId == second.InstructionId).Order!.CompletedUnits));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedTalkTickKeepsInvitationAndCompletionAtomic(bool completion)
    {
        var provider = new TalkOrderProvider(decline: true);
        using var world = NewWorld("talk-order-rollback", _ => provider);
        world.StartWorld();
        world.SubmitInstruction(new("atomic-talk", "owner:test", InitiatorId, OwnerInstructionKind.MustDo, "Talk to " + InviteeId));
        if (completion)
            for (var tick = 0; tick < 10 && world.Conversations.Count == 0; tick++) await AdvanceTalkOrderTick(world);
        await WaitForTalkOrderWork(world);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickNonBlockingAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 20 && Assert.Single(world.ExportState().Instructions!).Order!.Status != "finished"; tick++)
            await AdvanceTalkOrderTick(world);
        Assert.Equal("refused", Assert.Single(world.Conversations).Outcome);
        Assert.Equal(1, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Single(world.ExportState().Events, item => item.Kind == "conversation_proposed");
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_order_finished");
    }

    private static ClankerWorld.GodotClient.UI.OwnerWorldInstructionOrder ReceivedTalkOrder(PrivateWorldRuntime world) =>
        Assert.Single(JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldInstruction[]>(
            JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
                TalkHostJson), TalkGameJson)!, item => item.Order?.Action == "talk_to").Order!;

    private static async Task AdvanceTalkOrderTick(PrivateWorldRuntime world)
    {
        await WaitForTalkOrderWork(world);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
    }

    private static async Task WaitForTalkOrderWork(PrivateWorldRuntime world)
    {
        // Await the work this fixture actually started, rather than advancing
        // invitation deadlines while a scheduled fake response has not run.
        var tasks = new List<Task>();
        foreach (var name in new[] { "pendingHosted", "pendingConversationTurns" })
        {
            var field = typeof(PrivateWorldRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(field.GetValue(world));
            tasks.AddRange(pending.Values.Cast<object>().Select(item =>
                Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item))));
        }
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class TalkOrderProvider(bool decline = false) : IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentBag<AgentConversationTurnRequest> TurnRequests { get; } = [];
        public HashSet<string> SpeakIds { get; } = [InitiatorId, InviteeId];
        public ConcurrentDictionary<string, byte> ResumeAgents { get; } = new(StringComparer.Ordinal);
        public string DeclineAgent { get; set; } = InviteeId;
        public bool CanSpeakAs(string agentId) => SpeakIds.Contains(agentId);
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate => decline && observation.InhabitantId == DeclineAgent &&
                    candidate.Id.StartsWith("conversation_decline:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => !decline && candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => ResumeAgents.ContainsKey(observation.InhabitantId) && candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => candidate.Id == "safe_idle") ?? observation.Candidates[0];
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected.Id, 1d, observation.Candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                ChosenName: observation.NeedsName ? observation.Self?.Name ?? "Test Agent" : null,
                ChosenPersonality: observation.NeedsPersonality ? "patient and curious" : null,
                ChosenAspiration: observation.NeedsAspiration ? "learn the valley" : null));
        }
        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            TurnRequests.Add(request);
            return ValueTask.FromResult(new AgentConversationTurnResponse(request.RequestId, request.ConversationId,
                request.Revision, request.RunEpoch, request.SpeakerId, "A public turn with no world command.",
                AgentConversationDisposition.Continue, AgentConversationEffect.None));
        }
    }
}
