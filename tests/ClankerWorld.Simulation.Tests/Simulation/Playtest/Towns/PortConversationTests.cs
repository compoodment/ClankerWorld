using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatTests
{
    private static readonly JsonSerializerOptions BoatConversationJsonOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task ActualBoatPassengerCannotReceiveALandInvitationButCanTalkAfterLanding()
    {
        using var docked = await DockedWorldAsync();
        var state = WithJug(docked.ExportState());
        var passenger = state.Inhabitants[0].InhabitantId;
        var landActor = state.Inhabitants[1].InhabitantId;
        state = AtConversationPort(state, passenger, landActor, "port-one");
        var household = state.Society.Society.GetInhabitant(passenger).HouseholdId!;
        var food = state.Society.Society.Inventory.Lots.First(lot => lot.OwnerId == household && FoodItems.IsEdible(lot.ItemKind) && lot.Quantity > 1);
        var inventory = InventoryFixture.Transfer(state.Society.Society.Inventory, "boat-conversation-food", household, passenger,
            food.Id, 1, "boat_food_collected");
        var carriedFood = Assert.Single(inventory.Lots, lot => lot.OwnerId == passenger && FoodItems.IsEdible(lot.ItemKind));
        Assert.Equal(food.Id, carriedFood.ProvenanceLotId);
        var destinationLand = Geometry(state, "port-two").LandTiles;
        var blockers = state.Inhabitants.Skip(2).Take(2).ToArray();
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == passenger
                ? person with { HungerBasisPoints = 3_500 }
                : person.InhabitantId == blockers[0].InhabitantId ? person with { Position = destinationLand[0] }
                : person.InhabitantId == blockers[1].InhabitantId ? person with { Position = destinationLand[1] }
                : person).ToArray()
        };
        using var departing = PrivateWorldRuntime.Restore(state, _ => new Pick());
        var boatId = Assert.Single(departing.Boats).Id;
        Assert.True(departing.StartBoatJourney(boatId, passenger, "port-two").Applied);
        var aboard = departing.ExportState();
        var formerPassengerPosition = state.Inhabitants.Single(person => person.InhabitantId == passenger).Position;
        aboard = aboard with
        {
            Inhabitants = aboard.Inhabitants.Select(person => person.InhabitantId == landActor
                ? person with { Position = formerPassengerPosition, LastDecisionContext = null }
                : person).ToArray()
        };
        var provider = new BoatConversationProvider(landActor, passenger, accept: true);
        using var world = PrivateWorldRuntime.Restore(aboard, id => provider.CanSpeakAs(id) ? provider : new Pick());
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(provider.PlanningRequests, request => request.Observation.InhabitantId == landActor);
        Assert.DoesNotContain(provider.PlanningRequests, request => request.Observation.InhabitantId == landActor &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"talk:{passenger}"));
        // Actual food keeps a meaningful aboard choice available. The blocked landing
        // leaves time for the saved decision cadence without forcing another poll.
        for (var tick = 0; tick < 40 && provider.PlanningRequests.All(request => request.Observation.InhabitantId != passenger); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(provider.PlanningRequests, request => request.Observation.InhabitantId == passenger &&
            request.Observation.Candidates.Any(candidate => candidate.Id == "consume_food"));
        Assert.All(provider.PlanningRequests.Where(request => request.Observation.InhabitantId == passenger), request =>
            Assert.All(request.Observation.Candidates, candidate => Assert.True(candidate.Id is "consume_food" or "safe_idle")));
        Assert.Empty(world.Conversations);
        Assert.Empty(world.ConversationBudgets);
        Assert.Empty(provider.TurnRequests);
        Assert.NotNull(Assert.Single(world.Boats).Journey);
        Assert.Equal(Assert.Single(world.Boats).Position, world.Inhabitants.Single(person => person.InhabitantId == passenger).Position);
        Assert.Equal((passenger, 1), (world.Society.Inventory.GetLot("aboard-jug").OwnerId, world.Society.Inventory.GetLot("aboard-jug").Quantity));
        Assert.Equal((passenger, 2, "aboard-jug"), (world.Society.Inventory.GetLot("aboard-water").OwnerId,
            world.Society.Inventory.GetLot("aboard-water").Quantity, world.Society.Inventory.GetLot("aboard-water").ContainerLotId));

        Assert.Equal((passenger, 1, food.Id), (world.Society.Inventory.GetLot(carriedFood.Id).OwnerId,
            world.Society.Inventory.GetLot(carriedFood.Id).Quantity, world.Society.Inventory.GetLot(carriedFood.Id).ProvenanceLotId));
        Assert.Equal(food.Quantity - 1, world.Society.Inventory.GetLot(food.Id).Quantity);
        var unblocked = world.ExportState();
        unblocked = unblocked with
        {
            Inhabitants = unblocked.Inhabitants.Select(person => blockers.FirstOrDefault(blocker => blocker.InhabitantId == person.InhabitantId) is { } blocker
                ? person with { Position = blocker.Position } : person).ToArray()
        };
        using var traveling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(unblocked)), _ => new Pick());
        for (var tick = 0; tick < 100 && traveling.Boats[0].Journey is not null; tick++)
            Assert.True((await traveling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("port-two", traveling.Boats[0].DockedPortId);
        Assert.Null(traveling.Boats[0].Journey);
        var landed = AtConversationPort(traveling.ExportState(), passenger, landActor, "port-two");
        var onLandProvider = new BoatConversationProvider(landActor, passenger, accept: true);
        using var onLand = PrivateWorldRuntime.Restore(landed, id => onLandProvider.CanSpeakAs(id) ? onLandProvider : new Pick());
        for (var tick = 0; tick < 40 && onLand.Conversations.All(item => item.Turns.Count == 0); tick++)
            Assert.True((await onLand.AdvanceOneTickAsync()).Advanced);
        var conversation = Assert.Single(onLand.Conversations);
        Assert.Equal((landActor, passenger), (conversation.InitiatorId, conversation.InviteeId));
        Assert.Equal(2, conversation.AcceptedParticipantIds.Count);
        Assert.NotEmpty(conversation.Turns);
        Assert.NotEmpty(onLandProvider.TurnRequests);
        Assert.Contains(onLandProvider.PlanningRequests, request => request.Observation.InhabitantId == landActor &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"talk:{passenger}"));
        Assert.Contains(onLandProvider.PlanningRequests, request => request.Observation.InhabitantId == passenger &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"conversation_accept:{conversation.Id}"));
        onLand.Validate();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BoardingRefusesEitherParticipantUntilTheirRealInvitationOrConversationEnds(bool accepted, bool passengerInitiates)
    {
        using var docked = await DockedWorldAsync();
        var state = docked.ExportState();
        var passenger = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        state = AtConversationPort(state, passenger, other, "port-one");
        var provider = new BoatConversationProvider(passengerInitiates ? passenger : other,
            passengerInitiates ? other : passenger, accepted);
        using var world = PrivateWorldRuntime.Restore(state, id => provider.CanSpeakAs(id) ? provider : new Pick());
        for (var tick = 0; tick < 40 && (world.Conversations.Count == 0 ||
             accepted && world.Conversations[0].AcceptedParticipantIds.Count < 2); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var conversation = Assert.Single(world.Conversations);
        Assert.Equal(accepted ? 2 : 1, conversation.AcceptedParticipantIds.Count);
        if (accepted) Assert.True(conversation.Status is AgentConversationStatus.Ready or AgentConversationStatus.AwaitingSpeaker or AgentConversationStatus.WrapUp);
        else Assert.Equal(AgentConversationStatus.Proposed, conversation.Status);
        Assert.Contains(provider.PlanningRequests, request => request.Observation.InhabitantId == conversation.InitiatorId &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"talk:{conversation.InviteeId}"));
        if (accepted) Assert.Contains(provider.PlanningRequests, request => request.Observation.InhabitantId == conversation.InviteeId &&
            request.Observation.Candidates.Any(candidate => candidate.Id == $"conversation_accept:{conversation.Id}"));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var callsBefore = provider.TurnRequests.Count;
        var boat = Assert.Single(world.Boats);

        var refusal = world.StartBoatJourney(boat.Id, passenger, "port-two");

        Assert.False(refusal.Applied);
        Assert.Contains("conversation", refusal.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(callsBefore, provider.TurnRequests.Count);
        Assert.Null(Assert.Single(world.Boats).Journey);
        Assert.Equal(conversation, Assert.Single(world.Conversations));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), id => provider.CanSpeakAs(id) ? provider : new Pick());
        Assert.False(loaded.StartBoatJourney(boat.Id, passenger, "port-two").Applied);
        Assert.Null(Assert.Single(loaded.Boats).Journey);
        Assert.Equal(conversation.Id, Assert.Single(loaded.Conversations).Id);

        if (accepted)
        {
            world.Pause();
            Assert.Equal(AgentConversationStatus.Suspended, Assert.Single(world.Conversations).Status);
            world.Resume();
        }
        provider.EndConversation = true;
        for (var tick = 0; tick < 40 && world.Conversations.Any(item => item.Status != AgentConversationStatus.Closed); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var ended = Assert.Single(world.Conversations, item => item.Id == conversation.Id);
        Assert.All(world.Conversations, item => Assert.Equal(AgentConversationStatus.Closed, item.Status));
        Assert.Equal(AgentConversationStatus.Closed, ended.Status);
        Assert.Equal(conversation.Id, ended.Id);
        Assert.Equal(accepted ? "withdrawn" : "refused", ended.Outcome);
        Assert.Contains(provider.PlanningRequests, request => request.Observation.Candidates.Any(candidate =>
            candidate.Id == (accepted ? $"conversation_end:{conversation.Id}" : $"conversation_decline:{conversation.Id}")));
        Assert.True(world.StartBoatJourney(boat.Id, passenger, "port-two").Applied);
        Assert.Equal(passenger, Assert.Single(world.Boats).Journey!.PassengerId);
        world.Validate();
        var boardedBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var boardedReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(boardedBytes), _ => new Pick());
        Assert.Equal(passenger, Assert.Single(boardedReload.Boats).Journey!.PassengerId);
        Assert.Equal(world.Conversations.Select(item => item.Id), boardedReload.Conversations.Select(item => item.Id));
        Assert.All(boardedReload.Conversations, item => Assert.Equal(AgentConversationStatus.Closed, item.Status));
        Assert.Equal(boardedBytes, PrivateWorldRuntimeCodec.Encode(boardedReload.ExportState()));
    }

    [Theory]
    [InlineData(AgentConversationStatus.Proposed)]
    [InlineData(AgentConversationStatus.Ready)]
    [InlineData(AgentConversationStatus.Suspended)]
    public async Task NativeCheckpointRefusesUnfinishedDialogueForAnActualLivingBoatPassenger(AgentConversationStatus status)
    {
        using var docked = await DockedWorldAsync();
        var original = docked.ExportState();
        var passenger = original.Inhabitants[0].InhabitantId;
        var other = original.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(AtConversationPort(original, passenger, other, "port-one"), _ => new Pick());
        var boat = Assert.Single(world.Boats);
        Assert.True(world.StartBoatJourney(boat.Id, passenger, "port-two").Applied);
        var aboard = world.ExportState();
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, aboard.SchemaVersion);
        Assert.Equal(passenger, Assert.Single(aboard.BoatTransport!.Boats).Journey!.PassengerId);
        var society = aboard.Society.Society;
        var conversation = AgentConversationRules.Propose("conversation:tampered-boat", other, passenger, society.WorldTick, society.RunEpoch);
        if (status != AgentConversationStatus.Proposed)
        {
            Assert.True(AgentConversationRules.TryAcceptProposal(conversation, passenger, society.WorldTick, society.RunEpoch, out conversation));
            if (status == AgentConversationStatus.Suspended)
                conversation = AgentConversationRules.Suspend(conversation, AgentConversationInterruption.OwnerPaused, society.WorldTick);
        }
        AgentConversationRules.Validate(conversation, society.WorldTick);
        var day = society.WorldTick / society.Config.TicksPerWorldDay;
        AgentConversationDailyBudget[] budgets = status == AgentConversationStatus.Proposed
            ? [new(other, day, 1)] : [new(other, day, 1), new(passenger, day, 1)];
        var invalid = aboard with { Conversations = [conversation], ConversationBudgets = budgets };

        var encodeError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
        Assert.Contains("boat passenger", encodeError.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));
        var json = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(aboard))!;
        json["state"]!["conversations"] = JsonSerializer.SerializeToNode(invalid.Conversations, BoatConversationJsonOptions);
        json["state"]!["conversationBudgets"] = JsonSerializer.SerializeToNode(budgets, BoatConversationJsonOptions);
        var decodeError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Contains("boat passenger", decodeError.Message, StringComparison.Ordinal);
        Assert.Empty(world.Conversations);
        Assert.NotNull(Assert.Single(world.Boats).Journey);
    }

    private static PrivateWorldRuntimeState AtConversationPort(PrivateWorldRuntimeState state, string passenger, string other, string portId)
    {
        var lands = Geometry(state, portId).LandTiles;
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == passenger || person.InhabitantId == other
                ? person with
                {
                    Position = person.InhabitantId == passenger ? lands[0] : lands[1],
                    HungerBasisPoints = 10_000,
                    Project = null,
                    LastDecisionContext = null,
                } : person).ToArray()
        };
    }

    private sealed class BoatConversationProvider(string initiator, string invitee, bool accept) : IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 2;
        public ConcurrentQueue<CognitionDecisionRequest> PlanningRequests { get; } = new();
        public ConcurrentQueue<AgentConversationTurnRequest> TurnRequests { get; } = new();
        public bool EndConversation { get; set; }
        public bool CanSpeakAs(string agentId) => agentId == initiator || agentId == invitee;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Validate();
            PlanningRequests.Enqueue(request);
            var observation = request.Observation;
            var chosen = observation.Candidates.FirstOrDefault(candidate => EndConversation &&
                (candidate.Id.StartsWith("conversation_decline:", StringComparison.Ordinal) || candidate.Id.StartsWith("conversation_end:", StringComparison.Ordinal))) ??
                observation.Candidates.FirstOrDefault(candidate => !EndConversation && observation.InhabitantId == initiator && candidate.Id == $"talk:{invitee}") ??
                observation.Candidates.FirstOrDefault(candidate => accept && observation.InhabitantId == invitee &&
                    candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, chosen.Id,
                1d, observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == chosen.Id ? 1d : 0d, StringComparer.Ordinal),
                ChosenName: observation.NeedsName ? observation.Self?.Name ?? "Traveler" : null,
                ChosenPersonality: observation.NeedsPersonality ? "patient and curious" : null,
                ChosenAspiration: observation.NeedsAspiration ? "learn about the town" : null));
        }

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Validate();
            TurnRequests.Enqueue(request);
            return ValueTask.FromResult(new AgentConversationTurnResponse(request.RequestId, request.ConversationId, request.Revision,
                request.RunEpoch, request.SpeakerId, "I am glad we can talk here on land.", AgentConversationDisposition.Continue));
        }
    }
}
