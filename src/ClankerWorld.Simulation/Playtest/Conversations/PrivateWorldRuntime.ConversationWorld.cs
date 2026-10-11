using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed class ConversationWorldAdapter(PrivateWorldRuntime runtime) : IConversationWorld
    {
        public long WorldTick => runtime.WorldTick;
        public string WorldSeed => runtime.worldSeed;
        public SocietyCheckpoint Society => runtime.society.Checkpoint;
        public IReadOnlyDictionary<string, PlaytestInhabitantState> Inhabitants => runtime.inhabitants;
        public long WorldDayAt(long tick) => WorldCalendarRules.FromTick(tick, runtime.worldSystems.Config).DayIndex;
        public bool NeedsUrgentFood(PlaytestInhabitantState person) => PrivateWorldRuntime.NeedsUrgentFood(person);
        public bool NeedsUrgentWarmth(PlaytestInhabitantState person) => runtime.NeedsUrgentWarmth(person);
        public bool IsAboardBoat(string agentId) => runtime.PassengerBoat(agentId) is not null;
        public int FootDistance(GridPoint from, GridPoint to) => runtime.map.FootDistance(from, to);
        public bool IsWithinInteractionRange(GridPoint from, GridPoint to, int range) =>
                runtime.IsWithinInteractionRange(from, to, range);
        public bool HasExplicitConversationProvider(string agentId) => runtime.HasExplicitConversationProvider(agentId);
        public bool ActiveTalkOrderUses(string conversationId) => runtime.ActiveTalkOrderUses(conversationId);
        public void IncreaseTrust(string from, string to, int amount, string reason) => runtime.IncreaseTrust(from, to, amount, reason);
        public void AppendEvent(string kind, string detail, GridPoint? position) => runtime.AppendEvent(kind, detail, position);
        public SocietyOperationResult Apply(Func<SocietyCheckpoint, SocietyOperationResult> operation) => runtime.society.Apply(operation);
        public SocietyOperationResult RenameFromPlayer(SocietyCheckpoint checkpoint, string agentId, string name) =>
                runtime.RenameFromPlayer(checkpoint, agentId, name);
    }
}
