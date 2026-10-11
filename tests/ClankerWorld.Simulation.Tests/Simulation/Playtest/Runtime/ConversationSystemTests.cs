using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ConversationSystemTests
{
    private static readonly string[] ListenerIds = ["agent-b", "listener"];
    private static readonly string[] AdmissionEvents = ["conversation_proposed", "conversation_accepted", "conversation_turn_admitted"];
    private static readonly int[] BeforeMutation = [1];
    private static readonly int[] AfterMutation = [1, 2];
    [Fact]
    public void AdmissionAndDiscardedProposalKeepSeparateSnapshotsAndListenerMemories()
    {
        var world = new FakeWorld();
        var system = new ConversationSystem(new([], [], []));
        var empty = system.State;
        Assert.True(system.ProposeConversation(world, "agent-a", "agent-b"));
        var invitation = Assert.Single(system.State.Conversations);
        Assert.Empty(empty.Conversations);
        Assert.True(system.IsConversationBusy("agent-a"));
        Assert.False(system.IsConversationBusy("agent-b"));
        Assert.True(system.AcceptConversation(world, "agent-b", invitation.Id));
        Assert.True(AgentConversationRules.TryBeginTurn(system.ConversationFor("agent-a")!, 0, 1, out var started));
        system.SetConversation(started);
        var snapshot = system.State;
        var request = new AgentConversationTurnRequest("request:test", started.Id, started.Revision, 1, 0,
            AgentConversationPurpose.PublicTurn, "agent-a", "Aster Ash", "agent-b", "Rowan Reed",
            "patient", "build a home", [], [AgentConversationEffect.None]);
        var reply = new AgentConversationTurnResponse(request.RequestId, request.ConversationId,
            request.Revision, request.RunEpoch, "agent-a", "A public statement.", AgentConversationDisposition.Continue);
        world.AdvanceTo(1);
        var discarded = new ConversationSystem(snapshot);
        var discardedWorld = new FakeWorld();
        discardedWorld.AdvanceTo(1);
        discarded.ApplyConversationTurnOutcome(discardedWorld, request, reply, null, 1, () => false);
        Assert.Equal(AgentConversationInterruption.ProviderUnavailable,
            discarded.ConversationFor("agent-a")!.Interruption);
        Assert.Same(snapshot, system.State);
        Assert.Equal(AgentConversationStatus.AwaitingSpeaker, system.ConversationFor("agent-a")!.Status);
        Assert.Empty(world.Society.AllBeliefs());

        system.ApplyConversationTurnOutcome(world, request, reply, null, 1, () => true);
        var admitted = Assert.Single(system.State.Conversations);
        var turn = Assert.Single(admitted.Turns);
        Assert.Equal(ListenerIds, turn.ListenerIds);
        Assert.Equal("agent-b", admitted.CurrentSpeakerId);
        Assert.Equal(ListenerIds, world.Society.AllBeliefs().Select(item => item.OwnerId));
        Assert.All(world.Society.AllBeliefs(), belief => Assert.Equal(turn.Id, belief.SourceTurnId));
        Assert.Equal(AdmissionEvents,
            world.Events.Select(item => item.Kind));
        Assert.Empty(snapshot.Conversations[0].Turns);
        // An already admitted reply cannot append another turn or memory.
        system.ApplyConversationTurnOutcome(world, request, reply, null, 1, () => true);
        Assert.Single(system.State.Conversations[0].Turns);
        Assert.Equal(2, world.Society.AllBeliefs().Count());
        Assert.Equal(3, world.Events.Count);
    }

    [Fact]
    public void DerivedConversationLookupKeepsPositionNeedsAndDailyBudgetAsLiveQueries()
    {
        var world = new FakeWorld();
        var system = new ConversationSystem(new([], [], []));
        Assert.Contains(system.ConversationCandidates(world, "agent-a"), item => item.Id == "talk:agent-b");
        Assert.True(system.ProposeConversation(world, "agent-a", "agent-b"));
        var proposed = system.State;
        var invitation = system.ConversationFor("agent-b")!;
        Assert.Contains(system.ConversationCandidates(world, "agent-b"), item => item.Id == "conversation_accept:" + invitation.Id);
        world.Move("agent-b", new GridPoint(4, 0));
        Assert.DoesNotContain(system.ConversationCandidates(world, "agent-b"), item => item.Id.StartsWith("conversation_accept:", StringComparison.Ordinal));
        world.Move("agent-b", new GridPoint(1, 0));
        world.Urgent.Add("agent-b");
        Assert.False(system.AcceptConversation(world, "agent-b", invitation.Id));
        Assert.Same(proposed, system.State);
        world.Urgent.Clear();
        Assert.True(system.DeclineConversation(world, "agent-b", invitation.Id));
        Assert.Null(system.ConversationFor("agent-a"));
        Assert.Equal(AgentConversationStatus.Proposed, proposed.Conversations[0].Status);
        Assert.Equal(1, Assert.Single(system.State.Budgets).Count);
        world.AdvanceTo(360);
        system.UpdateConversationsForTick(world, 360);
        Assert.Empty(system.State.Budgets);
        Assert.Contains(system.ConversationCandidates(world, "agent-a"), item => item.Id == "talk:agent-b");
    }

    [Fact]
    public void DerivedVerificationDetectsMutationBehindAnUnchangedKey()
    {
        Assert.True(DerivedVerification.RecomputeOnHit);
        var key = new List<int> { 1 };
        var cache = new Derived<List<int>, int[]>(items => items.ToArray(), (left, right) => left.SequenceEqual(right));
        Assert.Equal(BeforeMutation, cache.Get(key));
        key.Add(2);
        Assert.Throws<InvalidOperationException>(() => cache.Get(key));
        Assert.Equal(AfterMutation, cache.Get([1, 2]));
    }

    private sealed class FakeWorld : IConversationWorld
    {
        private readonly Dictionary<string, PlaytestInhabitantState> people = new(StringComparer.Ordinal)
        {
            ["agent-a"] = new("agent-a", new(0, 0), 10_000, 0, "patient", "build a home"),
            ["agent-b"] = new("agent-b", new(1, 0), 10_000, 0, "kind", "share a life"),
            ["listener"] = new("listener", new(2, 0), 10_000, 0, "curious", "learn"),
        };
        public SocietyCheckpoint Society { get; private set; } = SocietyFixture.CreateGenesis("conversation-system-test",
            [SocietyFixture.CreateFounder("agent-a", "Aster Ash"), SocietyFixture.CreateFounder("agent-b", "Rowan Reed"),
             SocietyFixture.CreateFounder("listener", "Vale Oak")]) with
        { RunEpoch = 1 };
        public long WorldTick => Society.WorldTick;
        public string WorldSeed => "conversation-system-test";
        public IReadOnlyDictionary<string, PlaytestInhabitantState> Inhabitants => people;
        public HashSet<string> Urgent { get; } = new(StringComparer.Ordinal);
        public List<PlaytestWorldEvent> Events { get; } = [];
        public void AdvanceTo(long tick) => Society = Society with
        {
            WorldTick = tick,
            Inventory = Society.Inventory with { WorldTick = tick },
        };
        public void Move(string id, GridPoint position) => people[id] = people[id] with { Position = position };
        public long WorldDayAt(long tick) => tick / 360;
        public bool NeedsUrgentFood(PlaytestInhabitantState person) => Urgent.Contains(person.InhabitantId);
        public bool NeedsUrgentWarmth(PlaytestInhabitantState person) => false;
        public bool IsAboardBoat(string agentId) => false;
        public int FootDistance(GridPoint from, GridPoint destination) => Math.Abs(from.X - destination.X) + Math.Abs(from.Y - destination.Y);
        public bool IsWithinInteractionRange(GridPoint from, GridPoint destination, int range) => FootDistance(from, destination) <= range;
        public bool HasExplicitConversationProvider(string agentId) => true;
        public bool ActiveTalkOrderUses(string conversationId) => false;
        public void IncreaseTrust(string from, string subject, int amount, string reason) => throw new NotSupportedException();
        public void AppendEvent(string kind, string detail, GridPoint? position = null) =>
            Events.Add(new(Events.Count + 1, WorldTick, kind, detail, position));
        public SocietyOperationResult Apply(Func<SocietyCheckpoint, SocietyOperationResult> operation)
        {
            var result = operation(Society);
            Society = result.Checkpoint;
            return result;
        }
        public SocietyOperationResult RenameFromPlayer(SocietyCheckpoint checkpoint, string agentId, string name) =>
            throw new NotSupportedException();
    }
}
