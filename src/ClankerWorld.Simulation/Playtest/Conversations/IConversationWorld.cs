using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>The coordinator services needed by conversation and marriage rules.</summary>
public interface IConversationWorld
{
    long WorldTick { get; }
    string WorldSeed { get; }
    SocietyCheckpoint Society { get; }
    IReadOnlyDictionary<string, PlaytestInhabitantState> Inhabitants { get; }
    long WorldDayAt(long tick);
    bool NeedsUrgentFood(PlaytestInhabitantState person);
    bool NeedsUrgentWarmth(PlaytestInhabitantState person);
    bool IsAboardBoat(string agentId);
    int FootDistance(GridPoint from, GridPoint destination);
    bool IsWithinInteractionRange(GridPoint from, GridPoint destination, int range);
    bool HasExplicitConversationProvider(string agentId);
    bool ActiveTalkOrderUses(string conversationId);
    void IncreaseTrust(string from, string subject, int amount, string reason);
    void AppendEvent(string kind, string detail, GridPoint? position = null);
    SocietyOperationResult Apply(Func<SocietyCheckpoint, SocietyOperationResult> operation);
    SocietyOperationResult RenameFromPlayer(SocietyCheckpoint checkpoint, string agentId, string name);
}
