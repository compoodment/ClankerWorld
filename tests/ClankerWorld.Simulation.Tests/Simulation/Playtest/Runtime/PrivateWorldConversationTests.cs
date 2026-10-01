using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldConversationTests
{
    private const string InitiatorId = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InviteeId = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ListenerId = "founder:cccccccccccccccccccccccccccccccc";
    private const string DistantId = "founder:dddddddddddddddddddddddddddddddd";

    [Fact]
    public async Task AcceptedTurnIsBoundedPublicAndOnlyActualNearbyListenersLearnIt()
    {
        var provider = new ConversationProvider(new HashSet<string>(StringComparer.Ordinal)
        {
            InitiatorId, InviteeId,
        });
        using var world = NewWorld("nearby-conversation", provider);
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
        Assert.DoesNotContain(DistantId, turn.ListenerIds);
        var requestedBeforeSave = provider.TurnRequests.Count;
        Assert.InRange(requestedBeforeSave, 1, 2);
        Assert.Empty(provider.TurnRequests[0].PublicHistory);

        var saved = world.ExportState();
        Assert.Equal(29, saved.SchemaVersion);
        Assert.Single(saved.Conversations!.Single().Turns);
        Assert.Contains(saved.Society.Society.Beliefs!, item =>
            item.OwnerId == ListenerId && item.SourceTurnId == turn.Id &&
            item.Provenance == SocietyBeliefProvenance.Hearsay && item.Statement == turn.Text);
        Assert.DoesNotContain(saved.Society.Society.Beliefs!, item => item.OwnerId == DistantId && item.SourceTurnId == turn.Id);
        Assert.DoesNotContain(saved.Events, item => item.Detail.Contains(turn.Text, StringComparison.Ordinal));

        world.Pause();
        var paused = world.ExportState();
        Assert.Equal(AgentConversationStatus.Suspended, paused.Conversations!.Single().Status);
        Assert.Equal(AgentConversationInterruption.OwnerPaused, paused.Conversations.Single().Interruption);
        var encoded = PrivateWorldRuntimeCodec.Encode(paused);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(encoded), _ => provider);
        Assert.Equal(AgentConversationStatus.Suspended, restored.Conversations.Single().Status);
        Assert.Single(Assert.Single(restored.Conversations).Turns);
        Assert.Equal(requestedBeforeSave, provider.TurnRequests.Count);
    }

    private static PrivateWorldRuntime NewWorld(string seed, ConversationProvider provider)
    {
        var world = new PrivateWorldRuntime(seed, id => id is InitiatorId or InviteeId
                ? provider
                : new DeterministicDecisionProvider(),
            startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(InitiatorId, new GridPoint(0, 0));
        world.PlaceFounder(InviteeId, new GridPoint(1, 0));
        world.PlaceFounder(ListenerId, new GridPoint(2, 0));
        world.PlaceFounder(DistantId, new GridPoint(10, 10));
        return world;
    }

    private sealed class ConversationProvider(IReadOnlySet<string> assignedAgents) :
        IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        long IAgentConversationProvider.ProviderEpoch => 1;
        public List<AgentConversationTurnRequest> TurnRequests { get; } = [];

        public bool CanSpeakAs(string agentId) => assignedAgents.Contains(agentId);

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate =>
                observation.InhabitantId == InitiatorId && candidate.Id == $"talk:{InviteeId}") ??
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
                ChosenPersonality: observation.NeedsPersonality ? "patient and curious" : null,
                ChosenAspiration: observation.NeedsAspiration ? "learn the valley" : null));
        }

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(
            AgentConversationTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Validate();
            TurnRequests.Add(request);
            return ValueTask.FromResult(new AgentConversationTurnResponse(
                request.RequestId,
                request.ConversationId,
                request.Revision,
                request.RunEpoch,
                request.SpeakerId,
                "A public turn with no world command.",
                AgentConversationDisposition.Continue));
        }
    }
}
