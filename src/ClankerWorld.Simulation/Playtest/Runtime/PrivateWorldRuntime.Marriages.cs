using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public IReadOnlyList<AgentMarriage> Marriages => marriages.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

    private bool CanProposeMarriage(AgentConversation conversation) => conversation.Kind == AgentConversationKind.Ordinary &&
        AgentMarriageRules.CanPropose(society.Checkpoint, marriages, conversation.InitiatorId, conversation.InviteeId);

    private bool IsAcceptedSurnameSession(AgentConversation conversation) =>
        conversation.Kind == AgentConversationKind.MarriageSurname &&
        marriages.SingleOrDefault(item => item.SurnameConversationId == conversation.Id) is { CompletedTick: null } marriage &&
        marriage.InitiatorId == conversation.InitiatorId && marriage.InviteeId == conversation.InviteeId &&
        AgentMarriageRules.IsAdult(society.Checkpoint.GetInhabitant(marriage.InitiatorId)) &&
        AgentMarriageRules.IsAdult(society.Checkpoint.GetInhabitant(marriage.InviteeId)) &&
        AgentMarriageRules.Partnership(society.Checkpoint, marriage.InitiatorId, marriage.InviteeId)?.Id == marriage.PartnershipId;

    private void StartAcceptedMarriage(AgentConversation consent)
    {
        var partnership = AgentMarriageRules.Partnership(society.Checkpoint, consent.InitiatorId, consent.InviteeId)!;
        var id = AgentMarriageRules.Id(consent.Id);
        var marriage = new AgentMarriage(id, partnership, consent,
            society.Checkpoint.GetInhabitant(consent.InitiatorId).Name,
            society.Checkpoint.GetInhabitant(consent.InviteeId).Name,
            id + ":surname", WorldTick);
        // Consent is retained in the marriage receipt even when ordinary dialogue is compacted.
        marriages.Add(marriage);
        TrimConversationHistory();
        conversations.Add(AgentConversationRules.Propose(marriage.SurnameConversationId, marriage.InitiatorId,
            marriage.InviteeId, WorldTick, society.Checkpoint.RunEpoch) with
        {
            Kind = AgentConversationKind.MarriageSurname,
            Status = AgentConversationStatus.Ready,
            AcceptedParticipantIds = [marriage.InitiatorId, marriage.InviteeId],
            CurrentSpeakerId = marriage.InitiatorId,
        });
        AppendEvent("marriage_accepted", $"{marriage.InitiatorNameAtAcceptance} and {marriage.InviteeNameAtAcceptance} agreed to marry; their shared surname is still undecided.",
            inhabitants[marriage.InitiatorId].Position);
    }

    private void CompleteMarriageSurname(AgentConversation receipt)
    {
        var index = marriages.FindIndex(item => item.SurnameConversationId == receipt.Id);
        var marriage = marriages[index];
        var surname = AgentMarriageRules.ResolveSurname(worldSeed, marriage, receipt, out var usedTieBreak);
        RenameSpouses(marriage, marriage.InitiatorId,
            AgentMarriageRules.WithSurname(society.Checkpoint.GetInhabitant(marriage.InitiatorId).Name, surname));
        marriages[index] = marriage with
        {
            ChosenSurname = surname,
            CompletedTick = WorldTick,
            UsedTieBreak = usedTieBreak,
            SurnameReceipt = receipt,
        };
        var couple = society.Checkpoint.GetInhabitant(marriage.InitiatorId).Name + " and " + society.Checkpoint.GetInhabitant(marriage.InviteeId).Name;
        AppendEvent(usedTieBreak ? "marriage_surname_draw" : "marriage_surname_agreed",
            usedTieBreak ? $"{couple} completed their marriage. A draw chose {surname} after four turns without agreement."
                : $"{couple} completed their marriage and agreed to share the surname {surname}.", inhabitants[marriage.InitiatorId].Position);
    }

    private bool CanCompleteMarriageSurname(AgentConversation receipt)
    {
        var marriage = marriages.Single(item => item.SurnameConversationId == receipt.Id);
        var surname = AgentMarriageRules.ResolveSurname(worldSeed, marriage, receipt, out _);
        return new[] { marriage.InitiatorId, marriage.InviteeId }.All(agentId =>
            AgentMarriageRules.WithSurname(society.Checkpoint.GetInhabitant(agentId).Name, surname).Length <= 48);
    }

    private SocietyOperationResult RenameSpouses(AgentMarriage marriage, string agentId, string name)
    {
        var otherId = agentId == marriage.InitiatorId ? marriage.InviteeId : marriage.InitiatorId;
        var surname = InhabitantNameRules.SurnameKey(name) ?? throw new ArgumentException("Choose a full name with a surname.", nameof(name));
        var otherName = AgentMarriageRules.WithSurname(society.Checkpoint.GetInhabitant(otherId).Name, surname);
        // Society.Apply publishes only the final valid checkpoint. Either refusal leaves both people untouched.
        return society.Apply(checkpoint =>
        {
            var first = SocietyFixture.RenameInhabitant(checkpoint, agentId, name);
            var second = SocietyFixture.RenameInhabitant(first.Checkpoint, otherId, otherName);
            return new SocietyOperationResult(second.Checkpoint, NewEvents: [.. first.NewEvents ?? [], .. second.NewEvents ?? []]);
        });
    }
}
