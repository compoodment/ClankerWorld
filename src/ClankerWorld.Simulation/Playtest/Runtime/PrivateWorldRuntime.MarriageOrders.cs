using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string MarriageOrderTask = "attempt marriage with your current partner; both people keep their consent and surname choices";

    private OwnerInstructionOrder? ParseMarriageOrder(string text, string actor)
    {
        text = text.Trim();
        if (text.StartsWith("please ", StringComparison.OrdinalIgnoreCase)) text = text[7..].TrimStart();
        if (!AdultResident(actor) || !text.Equals("propose marriage", StringComparison.OrdinalIgnoreCase) &&
            !text.Equals("propose marriage to my partner", StringComparison.OrdinalIgnoreCase)) return null;
        return new("propose_marriage", "queued", 1, 0, "marriage_attempts", false,
            TargetAgentId: CurrentMarriageOrderPartner(actor));
    }

    private string? CurrentMarriageOrderPartner(string actor)
    {
        var partnership = society.Checkpoint.Relationships.SingleOrDefault(item => item.Type == SocietyRelationshipType.Partnership &&
            item.State == SocietyRelationshipState.Accepted && (item.ProposerId == actor || item.TargetId == actor));
        return partnership is null ? null : partnership.ProposerId == actor ? partnership.TargetId : partnership.ProposerId;
    }

    private string? MarriageOrderBlocker(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor)) return "Only an adult can propose marriage.";
        var target = instruction.Order!.TargetAgentId ?? CurrentMarriageOrderPartner(actor);
        if (target is null) return "Waiting for an eligible current partner before attempting marriage.";
        if (AgentMarriageRules.Partnership(society.Checkpoint, actor, target) is null)
            return "The selected person is no longer your current partner; this order will not choose someone else.";
        return AgentMarriageRules.CanPropose(society.Checkpoint, marriages, actor, target) ? null :
            "This partnership is not currently eligible for marriage; both adults need chosen full names and must be free to marry.";
    }

    private string? UnacceptedMarriageOrderBlocker(OwnerQueuedInstruction instruction) =>
        instruction.Order is { Action: "propose_marriage" } order && !marriages.Any(item => item.Consent.Id == order.TalkConversationId)
            ? MarriageOrderBlocker(instruction) : null;

    private AgentConversation? ConversationForOrder(OwnerQueuedInstruction instruction)
    {
        var id = instruction.Order!.TalkConversationId;
        if (instruction.Order.Action == "propose_marriage" && marriages.FirstOrDefault(item => item.Consent.Id == id) is { } marriage)
            id = marriage.SurnameConversationId;
        return conversations.FirstOrDefault(item => item.Id == id);
    }

    private string? RequestedConversationActivity(AgentConversation conversation, string speaker) =>
        conversation.Kind == AgentConversationKind.Ordinary && instructionsByIdempotency.Values.Any(instruction =>
            instruction.TargetInhabitantId == speaker && instruction.Order is { Action: "propose_marriage" } order &&
            IsActiveOrder(order.Status) && order.TalkConversationId == conversation.Id && MarriageOrderBlocker(instruction) is null)
                ? "propose_marriage" : null;

    private static string? MarriageOrderOutcome(AgentConversation conversation, IEnumerable<AgentMarriage> marriages)
    {
        if (conversation.Status != AgentConversationStatus.Closed) return null;
        if (conversation.Outcome == "agreed" && conversation.WrapUpEffect == AgentConversationEffect.Marriage)
            return marriages.FirstOrDefault(item => item.Consent.Id == conversation.Id)?.CompletedTick is not null ? "married" : null;
        if (conversation.Outcome == "disagreed" && conversation.WrapUpEffect == AgentConversationEffect.Marriage) return "proposal_declined";
        return conversation.Outcome is "agreed" or "disagreed" ? "not_proposed" : conversation.Outcome;
    }

    private static bool IsValidMarriageOrder(OwnerQueuedInstruction instruction, HashSet<string> people)
    {
        var order = instruction.Order!;
        return (order.TargetAgentId is null ? order.TalkConversationId is null && order.CompletedUnits == 0 :
                people.Contains(order.TargetAgentId) && order.TargetAgentId != instruction.TargetInhabitantId) &&
            order.RequestedUnits == 1 && order.CompletedUnits is >= 0 and <= 1 && !order.RepeatUntilCancelled && !order.QuantityIsExplicit &&
            order.ProgressUnit == "marriage_attempts" && order.TargetFoodKind is null && order.TargetResourceId is null && order.TargetPosition is null &&
            order.Status != "not_understood" && (order.Status == "finished") == (order.CompletedUnits == 1) &&
            (order.TalkConversationId is null || order.TalkConversationId.Length <= 512 && IsValidProductionBindingId(order.TalkConversationId)) &&
            (order.CompletedUnits == 0 ? order.TalkOutcome is null && order.LastEffectId is null :
                order.TalkConversationId is not null && order.TalkOutcome is ("married" or "proposal_declined" or "not_proposed" or
                    "refused" or "deadline" or "daily_limit" or "withdrawn" or "participant_unavailable") && order.LastEffectId == TalkOrderEffectId(instruction));
    }
}
