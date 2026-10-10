using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldMarriageOrderTests
{
    private static readonly Lazy<Task<byte[]>> Fixture = new(async () =>
    {
        using var world = NormalPathWorld.CreateGenerated("marriage-order-audit", id => new PersonalProvider(id));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var people = world.Inhabitants.Select(person => person.InhabitantId).ToArray();
        Assert.True(world.RenameAgent(people[0], "Aster Ash"));
        Assert.True(world.RenameAgent(people[1], "Rowan Reed"));
        var state = SettlementWeatherTestFixture.WithWeather(world.ExportState(), WeatherKind.Clear);
        var source = state.Society.Society;
        if (!source.Relationships.Any(item => item.Type == SocietyRelationshipType.Partnership && item.State == SocietyRelationshipState.Accepted &&
            (item.ProposerId == people[0] || item.TargetId == people[0])))
        {
            var proposed = SocietyFixture.ProposeRelationship(source, new SocietyRelationshipProposal("marriage-order-partnership", 1,
                SocietyRelationshipType.Partnership, people[0], people[1], source.WorldTick));
            source = SocietyFixture.AcceptRelationship(proposed.Checkpoint, "marriage-order-partnership", 1, people[1]).Checkpoint;
        }
        var partnership = source.Relationships.Single(item => item.Type == SocietyRelationshipType.Partnership &&
            item.State == SocietyRelationshipState.Accepted && (item.ProposerId == people[0] || item.TargetId == people[0]));
        Assert.Equal(people[1], partnership.ProposerId == people[0] ? partnership.TargetId : partnership.ProposerId);
        var site = state.Map.FootNeighbors(state.Inhabitants[0].Position).First(point => state.Map.IsPassable(point) &&
            state.Inhabitants.All(person => person.Position != point));
        return PrivateWorldRuntimeCodec.Encode(state with
        {
            Society = state.Society with { Society = source },
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == people[1] ? site : person.Position,
                HungerBasisPoints = 9_500,
                Survival = new(),
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        });
    });

    [Fact]
    public async Task MarriageOrderUsesBothPersonalModelsAndWaitsForTheActualSurnameCompletion()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var partner = state.Inhabitants[1].InhabitantId;
        var providers = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => new PersonalProvider(person.InhabitantId));
        using var world = PrivateWorldRuntime.Restore(state, id => providers[id]);
        var receipt = world.SubmitInstruction(new("marriage", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        var submitted = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("propose_marriage", submitted.Action);
        Assert.Equal(partner, submitted.TargetAgentId);
        await Until(world, () => world.Marriages.Count == 1);
        var accepted = Assert.Single(world.Marriages);
        Assert.Null(accepted.CompletedTick);
        Assert.Equal(0, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal("Aster Ash", world.Society.GetInhabitant(actor).Name);
        Assert.Equal("Rowan Reed", world.Society.GetInhabitant(partner).Name);
        await Until(world, () => world.ExportState().CompletedInstructionIds!.Contains(receipt.InstructionId));
        var completed = Assert.Single(world.Marriages);
        Assert.NotNull(completed.CompletedTick);
        Assert.Equal(2, completed.Consent.WrapUpAcceptedBy.Count);
        Assert.Equal("married", Assert.Single(world.ExportState().Instructions!).Order!.TalkOutcome);
        Assert.Equal(completed.Consent.Id, Assert.Single(world.ExportState().Instructions!).Order!.TalkConversationId);
        Assert.Equal(1, Assert.Single(world.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Contains(providers[actor].Turns, turn => turn.Purpose == AgentConversationPurpose.SurnameChoice);
        Assert.Contains(providers[partner].Turns, turn => turn.Purpose == AgentConversationPurpose.SurnameChoice);
        Assert.Contains(providers[actor].Turns, turn => turn.RequestedActivity == "propose_marriage");
        Assert.All(providers[partner].Turns, turn => Assert.Null(turn.RequestedActivity));
        Assert.All(providers[actor].Turns.Where(turn => turn.Purpose == AgentConversationPurpose.SurnameChoice), turn => Assert.Null(turn.RequestedActivity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "marriage_accepted");
        Assert.Single(world.ExportState().Events, item => item.Kind == "marriage_surname_agreed");
        Assert.Equal(state.Society.Society.GetInhabitant(actor).HouseholdId, world.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(state.Society.Society.GetInhabitant(partner).HouseholdId, world.Society.GetInhabitant(partner).HouseholdId);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => providers[id]);
        await Tick(loaded);
        Assert.Single(loaded.Marriages);
        Assert.Equal(1, Assert.Single(loaded.ExportState().Instructions!).Order!.CompletedUnits);
    }

    [Fact]
    public async Task MarriageApproachesTheBoundPartnerAndReportsChangedEligibility()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var partner = state.Inhabitants[1].InhabitantId;
        var origin = state.Inhabitants[0].Position;
        var destination = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsPassable(point) &&
            state.Map.FootDistance(origin, point) > 2 && state.Map.IsReachableOnFoot(origin, point) &&
            state.Inhabitants.All(person => person.Position != point)).OrderBy(point => state.Map.FootDistance(origin, point)).First();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == partner
                ? person with { Position = destination } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new PersonalProvider(id));
        var receipt = world.SubmitInstruction(new("approach-marriage", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        await Tick(world);
        Assert.Empty(world.Conversations);
        await Until(world, () => world.Conversations.Any(item => item.Turns.Count > 0));
        Assert.True(world.ExportState().Map.FootDistance(world.Inhabitants.Single(person => person.InhabitantId == actor).Position,
            world.Inhabitants.Single(person => person.InhabitantId == partner).Position) <= 1);
        var conversationId = Assert.Single(world.Conversations).Id;
        Assert.True(world.RenameAgent(actor, "Aster"));
        Assert.False(AgentMarriageRules.CanPropose(world.Society, world.Marriages, actor, partner));
        await Until(world, () => Assert.Single(world.ExportState().Instructions!).Order!.Status == "blocked");
        var blocked = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Contains("chosen full names", blocked.BlockedReason!, StringComparison.Ordinal);
        Assert.Equal(partner, blocked.TargetAgentId);
        Assert.Equal(conversationId, blocked.TalkConversationId);
        Assert.Empty(world.Marriages);
        Assert.True(world.RenameAgent(actor, "Aster Ash"));
        await Tick(world);
        var recovered = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("doing", recovered.Status);
        Assert.Null(recovered.BlockedReason);
        Assert.Equal(0, recovered.CompletedUnits);
        Assert.Equal(conversationId, recovered.TalkConversationId);
        await Until(world, () => world.Marriages.Count == 1);
        Assert.Null(Assert.Single(world.Marriages).CompletedTick);
        var surnamePending = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("doing", surnamePending.Status);
        Assert.Null(surnamePending.BlockedReason);
        Assert.Equal(0, surnamePending.CompletedUnits);
        await Until(world, () => world.ExportState().CompletedInstructionIds!.Contains(receipt.InstructionId));
        Assert.Equal("married", Assert.Single(world.ExportState().Instructions!).Order!.TalkOutcome);
        Assert.Single(world.ExportState().Events, item => item.Kind == "conversation_proposed");
    }

    [Theory]
    [InlineData("invitation", "refused")]
    [InlineData("actor_wrapup", "proposal_declined")]
    [InlineData("partner_wrapup", "proposal_declined")]
    [InlineData("no_proposal", "not_proposed")]
    public async Task RefusalAndNoProposalNeverClaimMarriage(string choice, string outcome)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var partner = state.Inhabitants[1].InhabitantId;
        var providers = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => new PersonalProvider(person.InhabitantId));
        providers[partner].DeclineInvitation = choice == "invitation";
        providers[actor].DeclineWrapUp = choice == "actor_wrapup";
        providers[partner].DeclineWrapUp = choice == "partner_wrapup";
        foreach (var provider in providers.Values) provider.ProposeMarriage = choice != "no_proposal";
        using var world = PrivateWorldRuntime.Restore(state, id => providers[id]);
        world.SubmitInstruction(new("declined", "owner:test", actor, OwnerInstructionKind.MustDo, "Please propose marriage to my partner"));
        await Until(world, () => Assert.Single(world.ExportState().Instructions!).Order!.Status == "finished");
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal((outcome, 1), (order.TalkOutcome, order.CompletedUnits));
        Assert.Empty(world.Marriages);
        Assert.Equal("Aster Ash", world.Society.GetInhabitant(actor).Name);
        Assert.Equal("Rowan Reed", world.Society.GetInhabitant(partner).Name);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "marriage_accepted");
        Assert.All(providers[partner].Turns, turn => Assert.Null(turn.RequestedActivity));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => providers[id]);
        await Tick(loaded);
        Assert.Equal(order, Assert.Single(loaded.ExportState().Instructions!).Order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReloadedProposalAndSurnameNeedBothFreshResumeChoices(bool surname)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var partner = state.Inhabitants[1].InhabitantId;
        var providers = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => new PersonalProvider(person.InhabitantId));
        using var world = PrivateWorldRuntime.Restore(state, id => providers[id]);
        var receipt = world.SubmitInstruction(new("resume-marriage", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        await Until(world, () => surname ? world.Marriages.Count == 1 : world.Conversations.Any(item => item.Turns.Count > 0));
        var active = world.Conversations.Single(item => item.Kind == (surname ? AgentConversationKind.MarriageSurname : AgentConversationKind.Ordinary));
        foreach (var provider in providers.Values) provider.ResumeAllowed = false;
        var calls = providers.Values.Sum(provider => provider.Turns.Count);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => providers[id]);
        Assert.Equal(calls, providers.Values.Sum(provider => provider.Turns.Count));
        Assert.Equal(AgentConversationStatus.Suspended, loaded.Conversations.Single(item => item.Id == active.Id).Status);
        for (var tick = 0; tick < 4; tick++) await Tick(loaded);
        Assert.Empty(loaded.Conversations.Single(item => item.Id == active.Id).ResumeAcceptedBy);
        Assert.Equal(0, Assert.Single(loaded.ExportState().Instructions!).Order!.CompletedUnits);
        Assert.Equal(surname ? "surname_suspended" : "suspended", Assert.Single(new OwnerWorldObservationStore(loaded).GetSnapshot().Instructions).Order!.TalkStatus);
        providers[actor].ResumeAllowed = true;
        await Until(loaded, () => loaded.Conversations.Single(item => item.Id == active.Id).ResumeAcceptedBy.Count == 1);
        Assert.Equal([actor], loaded.Conversations.Single(item => item.Id == active.Id).ResumeAcceptedBy);
        Assert.Equal(active.Turns.Count, loaded.Conversations.Single(item => item.Id == active.Id).Turns.Count);
        using var halfReloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(loaded.ExportState())), id => providers[id]);
        Assert.Empty(halfReloaded.Conversations.Single(item => item.Id == active.Id).ResumeAcceptedBy);
        providers[partner].ResumeAllowed = true;
        await Until(halfReloaded, () => halfReloaded.ExportState().CompletedInstructionIds!.Contains(receipt.InstructionId));
        Assert.Single(halfReloaded.Marriages);
        Assert.Single(halfReloaded.ExportState().Events, item => item.Kind == "marriage_accepted");
        Assert.Equal("married", Assert.Single(halfReloaded.ExportState().Instructions!).Order!.TalkOutcome);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancelOrReplaceDoesNotInventOrUndoConsent(bool replace, bool accepted)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var providers = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => new PersonalProvider(person.InhabitantId));
        using var world = PrivateWorldRuntime.Restore(state, id => providers[id]);
        var receipt = world.SubmitInstruction(new("cancel-marriage", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        if (accepted) await Until(world, () => world.Marriages.Count == 1);
        if (replace) world.SubmitInstruction(new("replace-marriage", "owner:test", actor, OwnerInstructionKind.MustDo, "Eat food"));
        else world.CancelOrder(new("cancel-marriage-receipt", "owner:test", world.Society.WorldId, actor, receipt.InstructionId));
        if (accepted) await Until(world, () => world.Marriages.Single().CompletedTick is not null);
        else for (var tick = 0; tick < 6; tick++) await Tick(world);
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(("cancelled", 0), (order.Status, order.CompletedUnits));
        Assert.Null(order.TalkOutcome);
        Assert.Equal(accepted ? 1 : 0, world.Marriages.Count);
        Assert.Equal(accepted ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "marriage_accepted"));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => providers[id]);
        Assert.Equal(order, loaded.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrChangedPartnerBlocksWithoutSelectingAnotherPerson(bool alreadyBound)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var partner = state.Inhabitants[1].InhabitantId;
        var relationship = state.Society.Society.Relationships.Single(item => item.Type == SocietyRelationshipType.Partnership && item.State == SocietyRelationshipState.Accepted &&
            (item.ProposerId == actor || item.TargetId == actor));
        using var original = PrivateWorldRuntime.Restore(state, id => new PersonalProvider(id));
        if (alreadyBound) original.SubmitInstruction(new("old-partner", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        var saved = original.ExportState();
        saved = saved with
        {
            Society = saved.Society with
            {
                Society = saved.Society.Society with
                { Relationships = saved.Society.Society.Relationships.Where(item => item.Id != relationship.Id).ToArray() }
            }
        };
        using var world = PrivateWorldRuntime.Restore(saved, id => new PersonalProvider(id));
        if (!alreadyBound) world.SubmitInstruction(new("no-partner", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        await Tick(world);
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Equal(alreadyBound ? partner : null, order.TargetAgentId);
        Assert.Contains(alreadyBound ? "no longer" : "eligible current partner", order.BlockedReason!, StringComparison.Ordinal);
        Assert.Empty(world.Conversations);
        Assert.Empty(world.Marriages);
    }

    [Fact]
    public async Task MarriageOrderReplaysAndRejectsForgedBindingsAndAtomicTicks()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Fixture.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, id => new PersonalProvider(id));
        world.SubmitInstruction(new("marriage-replay", "owner:test", actor, OwnerInstructionKind.MustDo, "Propose marriage"));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => new PersonalProvider(id));
        for (var tick = 0; tick < 100 && Assert.Single(world.ExportState().Instructions!).Order!.Status != "finished"; tick++)
        {
            await WaitForWork(world);
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.False((await world.AdvanceOneTickNonBlockingAsync(() => false)).Advanced);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            await Tick(world);
            await Tick(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var current = world.ExportState();
            var instruction = Assert.Single(current.Instructions!);
            if (instruction.Order!.TalkConversationId is not null)
            {
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(current with
                { Instructions = [instruction with { Order = instruction.Order with { TargetAgentId = current.Inhabitants[2].InhabitantId } }] }));
                if (instruction.Order.CompletedUnits == 0)
                    Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(current with { Conversations = [] }));
            }
        }
        var completed = world.ExportState();
        var finished = Assert.Single(completed.Instructions!);
        Assert.Equal("married", finished.Order!.TalkOutcome);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Instructions = [finished with { Order = finished.Order with { LastEffectId = "marriage-order:forged" } }] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(completed with
        { Marriages = completed.Marriages!.Select(item => item with { CompletedTick = null, ChosenSurname = null, SurnameReceipt = null }).ToArray() }));
        Assert.Single(world.ExportState().Events, item => item.Kind == "marriage_accepted");
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_order_finished");
        var trimmed = completed with
        {
            Conversations = [],
            Society = completed.Society with
            {
                Society = completed.Society.Society with { Beliefs = completed.Society.Society.Beliefs!.Select(item => item with { SourceTurnId = null }).ToArray() }
            }
        };
        using var trimmedReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(trimmed)), id => new PersonalProvider(id));
        Assert.Equal(finished.Order, Assert.Single(trimmedReload.ExportState().Instructions!).Order);
        var changedOutcome = finished with { Order = finished.Order! with { TalkOutcome = "proposal_declined" } };
        var forgedReceipt = "marriage-order:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{changedOutcome.InstructionId}|{changedOutcome.TargetInhabitantId}|{changedOutcome.Order!.TargetAgentId}|{changedOutcome.Order.TalkConversationId}|{changedOutcome.Order.TalkOutcome}")));
        changedOutcome = changedOutcome with { Order = changedOutcome.Order! with { LastEffectId = forgedReceipt } };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(trimmed with { Instructions = [changedOutcome] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(trimmed with { Marriages = [] }));
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> complete)
    {
        for (var tick = 0; tick < 100 && !complete(); tick++) await Tick(world);
        Assert.True(complete(), "The native marriage-order stage did not complete: " +
            string.Join("; ", world.ExportState().Instructions!.Select(item => $"{item.Order!.Status}/{item.Order.BlockedReason}/{item.Order.TalkOutcome}")) +
            " conversations=" + string.Join("; ", world.Conversations.Select(item => $"{item.Kind}/{item.Status}/{item.Outcome}")) +
            " recent=" + string.Join("; ", world.ExportState().Events.TakeLast(8).Select(item => $"{item.Kind}:{item.Detail}")));
    }

    private static async Task Tick(PrivateWorldRuntime world)
    {
        await WaitForWork(world);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
    }

    private static async Task WaitForWork(PrivateWorldRuntime world)
    {
        var tasks = new List<Task>();
        foreach (var name in new[] { "pendingHosted", "pendingConversationTurns" })
        {
            var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(typeof(PrivateWorldRuntime)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world));
            tasks.AddRange(pending.Values.Cast<object>().Select(item => (Task)item.GetType().GetProperty("Task")!.GetValue(item)!));
        }
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class PersonalProvider(string owner) : IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public List<AgentConversationTurnRequest> Turns { get; } = [];
        public bool DeclineInvitation { get; set; }
        public bool DeclineWrapUp { get; set; }
        public bool ProposeMarriage { get; set; } = true;
        public bool ResumeAllowed { get; set; } = true;
        public bool CanSpeakAs(string agentId) => agentId == owner;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Assert.Equal(owner, request.Observation.InhabitantId);
            var observation = request.Observation;
            var candidate = observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(DeclineWrapUp ? "conversation_wrapup_decline:" : "conversation_wrapup_accept:", StringComparison.Ordinal) ||
                ResumeAllowed && item.Id.StartsWith("conversation_resume:", StringComparison.Ordinal) ||
                item.Id.StartsWith(DeclineInvitation ? "conversation_decline:" : "conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, owner, Kind, ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, candidate.Id, 1,
                observation.Candidates.ToDictionary(item => item.Id, item => item.Id == candidate.Id ? 1d : 0d),
                ChosenPersonality: observation.NeedsPersonality ? "patient" : null,
                ChosenAspiration: observation.NeedsAspiration ? "live well together" : null));
        }
        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Assert.Equal(owner, request.SpeakerId);
            Turns.Add(request);
            return ValueTask.FromResult(new AgentConversationTurnResponse(request.RequestId, request.ConversationId, request.Revision,
                request.RunEpoch, owner, "I would like us to share a life together.", AgentConversationDisposition.Continue,
                ProposeMarriage && request.Purpose == AgentConversationPurpose.WrapUp && request.AllowedEffects.Contains(AgentConversationEffect.Marriage)
                    ? AgentConversationEffect.Marriage : AgentConversationEffect.None,
                SurnameChoice: request.Purpose == AgentConversationPurpose.SurnameChoice ? request.AllowedSurnames[0] : null));
        }
    }
}
