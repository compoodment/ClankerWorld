using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public IReadOnlyList<AgentMarriage> Marriages => marriages.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

    private int FindMarriageIndex(Predicate<AgentMarriage> predicate) => conversationSystem.MarriageIndex(predicate);

    private void SetMarriageAt(int index, AgentMarriage marriage) => conversationSystem.SetMarriageAt(index, marriage);

    private bool CanProposeMarriage(AgentConversation conversation) =>
        conversationSystem.CanProposeMarriage(conversationWorld, conversation);

    private bool IsAcceptedSurnameSession(AgentConversation conversation) =>
        conversationSystem.IsAcceptedSurnameSession(conversationWorld, conversation);

    private void MaintainMarriages() =>
        conversationSystem.MaintainMarriages(conversationWorld);

    private void StartAcceptedMarriage(AgentConversation consent) =>
        conversationSystem.StartAcceptedMarriage(conversationWorld, consent);

    private void CompleteMarriageSurname(AgentConversation receipt) =>
        conversationSystem.CompleteMarriageSurname(conversationWorld, receipt);

    private bool CanCompleteMarriageSurname(AgentConversation receipt) =>
        conversationSystem.CanCompleteMarriageSurname(conversationWorld, receipt);

    private void RetainUnavailableSurnameSession(AgentConversation session) =>
        conversationSystem.RetainUnavailableSurnameSession(conversationWorld, session);

    private SocietyOperationResult RenameSpouses(AgentMarriage marriage, string agentId, string name, bool fromPlayer = false) =>
        ConversationSystem.RenameSpouses(conversationWorld, marriage, agentId, name, fromPlayer);


}
