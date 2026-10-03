using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Fact]
    public async Task NormalMarriageChoosesSurnameAndPlayerRenameUpdatesBothOrNeither()
    {
        var first = new MarriagePersonalProvider(InitiatorId);
        var second = new MarriagePersonalProvider(InviteeId);
        IDecisionProvider Route(string id) => id == InitiatorId ? first : id == InviteeId ? second : new DeterministicDecisionProvider();
        using var world = MarriedWorldSetup("normal-marriage", Route);
        await AdvanceMarriageUntil(world, () => world.Marriages.Any(item => item.CompletedTick is not null));

        var marriage = Assert.Single(world.Marriages);
        Assert.Equal("agreed", marriage.Consent.Outcome);
        Assert.Equal(AgentConversationEffect.Marriage, marriage.Consent.WrapUpEffect);
        Assert.Equal(2, marriage.Consent.WrapUpAcceptedBy.Count);
        Assert.Equal("Ash", marriage.ChosenSurname);
        Assert.False(marriage.UsedTieBreak);
        Assert.Equal("Aster Ash", world.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Ash", world.Society.GetInhabitant(InviteeId).Name);
        var receipt = Assert.IsType<AgentConversation>(marriage.SurnameReceipt);
        Assert.Equal(2, receipt.Turns.Count);
        Assert.Equal(new[] { InitiatorId, InviteeId }, receipt.Turns.Select(turn => turn.SpeakerId));
        Assert.Contains(first.TurnRequests, request => request.Purpose == AgentConversationPurpose.SurnameChoice);
        Assert.Contains(second.TurnRequests, request => request.Purpose == AgentConversationPurpose.SurnameChoice);
        Assert.All(first.TurnRequests, request => Assert.Equal(InitiatorId, request.SpeakerId));
        Assert.All(second.TurnRequests, request => Assert.Equal(InviteeId, request.SpeakerId));
        Assert.All(receipt.Turns, turn => Assert.Single(turn.ListenerIds));

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.Contains(snapshot.Inhabitants.Single(item => item.Id == InitiatorId).SocialNotes,
            note => note.Contains("Married to Rowan Ash", StringComparison.Ordinal));
        Assert.Contains(snapshot.Conversations, item => item.Kind == "marriage_surname" && item.ChosenSurname == "Ash");
        Assert.True(world.RenameAgent(InitiatorId, "Aster Vale"));
        Assert.Equal("Rowan Vale", world.Society.GetInhabitant(InviteeId).Name);
        Assert.Equal("Vale", Assert.Single(world.Marriages).CurrentSurname);
        Assert.Equal("Ash", Assert.Single(world.Marriages).ChosenSurname);
        Assert.Equal(receipt, Assert.Single(world.Marriages).SurnameReceipt);
        var unchanged = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<InhabitantNameTakenException>(() => world.RenameAgent(InitiatorId, "Willow Pine"));
        Assert.Equal(unchanged, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(unchanged), Route);
        Assert.Equal("Aster Vale", reloaded.Society.GetInhabitant(InitiatorId).Name);
        Assert.Equal("Rowan Vale", reloaded.Society.GetInhabitant(InviteeId).Name);
        Assert.Equal("Ash", Assert.Single(reloaded.Marriages).ChosenSurname);
        Assert.Equal("Vale", Assert.Single(reloaded.Marriages).CurrentSurname);
    }

    private static PrivateWorldRuntime MarriedWorldSetup(string seed, Func<string, IDecisionProvider> route)
    {
        using var setup = NewWorld(seed, route);
        Assert.True(setup.RenameAgent(InitiatorId, "Aster Ash"));
        Assert.True(setup.RenameAgent(InviteeId, "Rowan Reed"));
        Assert.True(setup.RenameAgent(ListenerId, "Willow Stone"));
        Assert.True(setup.RenameAgent(DistantId, "Mira Pine"));
        setup.StartWorld();
        var state = setup.ExportState();
        var proposal = SocietyFixture.ProposeRelationship(state.Society.Society, new SocietyRelationshipProposal(
            "partnership:marriage-test", 1, SocietyRelationshipType.Partnership, InitiatorId, InviteeId,
            state.Society.Society.WorldTick));
        var accepted = SocietyFixture.AcceptRelationship(proposal.Checkpoint, "partnership:marriage-test", 1, InviteeId);
        Assert.Equal(SocietyRelationshipState.Accepted, accepted.Checkpoint.GetRelationship("partnership:marriage-test").State);
        return PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = accepted.Checkpoint } }, route);
    }

    private static async Task AdvanceMarriageUntil(PrivateWorldRuntime world, Func<bool> finished)
    {
        for (var attempt = 0; attempt < 160 && !finished(); attempt++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(3);
        }
        Assert.True(finished(), "The normal personal-model marriage path did not reach its expected transition.");
    }

    private sealed class MarriagePersonalProvider(string ownerId) : IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public List<AgentConversationTurnRequest> TurnRequests { get; } = [];
        public bool CanSpeakAs(string agentId) => agentId == ownerId;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Assert.Equal(ownerId, request.Observation.InhabitantId);
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("conversation_wrapup_accept:", StringComparison.Ordinal) ||
                candidate.Id.StartsWith("conversation_resume:", StringComparison.Ordinal) ||
                candidate.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => ownerId == InitiatorId && candidate.Id == $"talk:{InviteeId}") ??
                observation.Candidates.First(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, ownerId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1d,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d),
                ChosenPersonality: observation.NeedsPersonality ? "patient" : null,
                ChosenAspiration: observation.NeedsAspiration ? "live well together" : null));
        }

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Assert.Equal(ownerId, request.SpeakerId);
            TurnRequests.Add(request);
            return ValueTask.FromResult(new AgentConversationTurnResponse(request.RequestId, request.ConversationId,
                request.Revision, request.RunEpoch, ownerId, "I would like us to share a life together.", AgentConversationDisposition.Continue,
                request.Purpose == AgentConversationPurpose.WrapUp && request.AllowedEffects.Contains(AgentConversationEffect.Marriage)
                    ? AgentConversationEffect.Marriage : AgentConversationEffect.None,
                SurnameChoice: request.Purpose == AgentConversationPurpose.SurnameChoice ? "Ash" : null));
        }
    }
}
