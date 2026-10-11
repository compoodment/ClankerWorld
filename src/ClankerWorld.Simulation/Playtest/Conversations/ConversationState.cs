using System.Collections.ObjectModel;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Immutable conversation state; every write replaces the snapshot.</summary>
public sealed record ConversationState
{
    public ConversationState(IEnumerable<AgentConversation> conversations,
        IEnumerable<AgentConversationDailyBudget> budgets, IEnumerable<AgentMarriage> marriages)
        : this(Array.AsReadOnly(conversations.ToArray()), Array.AsReadOnly(budgets.ToArray()),
            Array.AsReadOnly(marriages.ToArray()))
    { }

    private ConversationState(ReadOnlyCollection<AgentConversation> conversations,
        ReadOnlyCollection<AgentConversationDailyBudget> budgets, ReadOnlyCollection<AgentMarriage> marriages)
    {
        Conversations = conversations;
        Budgets = budgets;
        Marriages = marriages;
    }

    public ReadOnlyCollection<AgentConversation> Conversations { get; }
    public ReadOnlyCollection<AgentConversationDailyBudget> Budgets { get; }
    public ReadOnlyCollection<AgentMarriage> Marriages { get; }

    internal ConversationState WithConversations(IEnumerable<AgentConversation> value) =>
        new(Array.AsReadOnly(value.ToArray()), Budgets, Marriages);
    internal ConversationState WithBudgets(IEnumerable<AgentConversationDailyBudget> value) =>
        new(Conversations, Array.AsReadOnly(value.ToArray()), Marriages);
    internal ConversationState WithMarriages(IEnumerable<AgentMarriage> value) =>
        new(Conversations, Budgets, Array.AsReadOnly(value.ToArray()));
}
