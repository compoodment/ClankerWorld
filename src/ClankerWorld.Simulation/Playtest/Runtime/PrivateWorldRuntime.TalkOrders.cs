using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsConversationOrder(string action) => action is "talk_to" or "propose_marriage";

    private const string TalkOrderTask = "attempt to talk with the named person; agreement and resumption remain each participant's choice";

    private OwnerInstructionOrder? ParseTalkOrder(string text, string actor)
    {
        if (society.Checkpoint.GetInhabitant(actor).AgeBand == SocietyAgeBand.Infant) return null;
        if (text.StartsWith("please ", StringComparison.OrdinalIgnoreCase)) text = text[7..].TrimStart();
        foreach (var prefix in new[] { "talk to ", "talk with ", "speak to ", "speak with " })
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var name = text[prefix.Length..].Trim();
            var people = society.Checkpoint.Inhabitants.Where(person => person.Id != actor).ToArray();
            var targets = people.Where(person => person.Id == name).ToArray();
            if (targets.Length == 0 && InhabitantNameRules.CanonicalKey(name) is { } key)
                targets = people.Where(person => string.Equals(InhabitantNameRules.CanonicalKey(person.Name), key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(InhabitantNameRules.FirstNameKey(person.Name), key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (targets.Length != 1) return null;
            return new("talk_to", "queued", 1, 0, "conversations", false, TargetAgentId: targets[0].Id);
        }
        return null;
    }

    private static bool IsLinkedTalkOrder(OwnerQueuedInstruction instruction) => instruction.Order is { TalkConversationId: not null } order && IsConversationOrder(order.Action);

    private bool TalkOrderHasConversation(OwnerQueuedInstruction instruction) => instruction.Order is { } order && IsConversationOrder(order.Action) &&
        (IsLinkedTalkOrder(instruction) || ConversationFor(instruction.TargetInhabitantId) is not null);

    private CognitionCandidate? TalkOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person) =>
        IsLinkedTalkOrder(instruction) || TalkOrderBlocker(instruction, person) is null
            ? new(instruction.Order!.Action, IsLinkedTalkOrder(instruction) ? "Wait for the actual conversation's outcome, preserving each person's choices." :
                "Reach the named person and attempt one face-to-face invitation.", 0, instruction.Order!.TargetAgentId ?? CurrentMarriageOrderPartner(instruction.TargetInhabitantId))
            : null;

    private string? TalkOrderBlocker(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (instruction.Order!.Action == "propose_marriage" && MarriageOrderBlocker(instruction) is { } marriageBlocker)
            return marriageBlocker;
        var target = instruction.Order.TargetAgentId ?? CurrentMarriageOrderPartner(actor)!;
        if (!inhabitants.TryGetValue(target, out var other) || society.Checkpoint.GetInhabitant(target).Status != SocietyInhabitantStatus.Active)
            return "The named person is no longer available.";
        if (society.Checkpoint.GetInhabitant(target).AgeBand == SocietyAgeBand.Infant)
            return "The named person is too young for this conversation.";
        if (PassengerBoat(actor) is not null || PassengerBoat(target) is not null)
            return "Waiting for both people to be ashore for a face-to-face conversation.";
        if (ConversationFor(actor) is not null || ConversationFor(target) is not null)
            return "Waiting for another conversation to finish.";
        if (NeedsUrgentFood(other) || NeedsUrgentWarmth(other)) return "The named person's urgent food or warmth needs come first.";
        if (!HasExplicitConversationProvider(actor) || !HasExplicitConversationProvider(target))
            return "Both people need their own available conversation model.";
        if (!HasConversationAllowance(actor) || !HasConversationAllowance(target))
            return "Waiting for both people's conversation allowance.";
        if (conversations.Count >= AgentConversationRules.MaximumSavedConversations &&
            !conversations.Any(conversation => conversation.Status == AgentConversationStatus.Closed && !ActiveTalkOrderUses(conversation.Id)))
            return "Waiting for space in the conversation history.";
        return IsWithinInteractionRange(person.Position, other.Position, ResourceInteractionRange) ||
            FindUnoccupiedRoute(actor, person.Position, other.Position, ResourceInteractionRange).Count > 0
                ? null : "Waiting for a legal accessible route to the named person.";
    }

    private void ExecuteTalkOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (IsLinkedTalkOrder(instruction))
        {
            CompleteClosedTalkOrders();
            if (UnacceptedMarriageOrderBlocker(instruction) is { } eligibilityBlocker)
                SetOrderStatus(instruction, "blocked", eligibilityBlocker);
            else if (ConversationForOrder(instruction)?.Status == AgentConversationStatus.Suspended)
                SetOrderStatus(instruction, "blocked", "Conversation stopped; both people must choose to resume, or you may end it.");
            else if (instruction.Order!.Action == "propose_marriage" && marriages.Any(item => item.Consent.Id == instruction.Order.TalkConversationId && item.CompletedTick is null) &&
                ConversationForOrder(instruction)?.Outcome == "participant_unavailable")
                SetOrderStatus(instruction, "blocked", "Marriage was accepted, but the shared surname remains unfinished because a participant is unavailable.");
            return;
        }
        if (TalkOrderBlocker(instruction, person) is { } blocker)
        {
            SetOrderStatus(instruction, "blocked", blocker);
            return;
        }
        var actor = instruction.TargetInhabitantId;
        if (instruction.Order!.TargetAgentId is null)
        {
            var bound = instructionsByIdempotency[instruction.IdempotencyKey];
            instruction = bound with { Order = bound.Order! with { TargetAgentId = CurrentMarriageOrderPartner(actor) } };
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction;
        }
        var target = instruction.Order!.TargetAgentId!;
        if (!IsWithinInteractionRange(person.Position, inhabitants[target].Position, ResourceInteractionRange))
        {
            MoveToward(actor, person, inhabitants[target].Position, "owner_order_talk", ResourceInteractionRange);
            return;
        }
        if (!ProposeConversation(actor, target))
        {
            SetOrderStatus(instruction, "blocked", "Waiting for a face-to-face invitation to become possible.");
            return;
        }
        var conversation = ConversationFor(actor)!;
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        instructionsByIdempotency[current.IdempotencyKey] = current with
        { Order = current.Order! with { TalkConversationId = conversation.Id, Status = "doing", BlockedReason = null } };
    }

    private void ApplyTalkOrderConversationDecision(OwnerQueuedInstruction instruction, SocietyCognitionDispatchResult decision)
    {
        var actor = instruction.TargetInhabitantId;
        var candidate = decision.Admission.Intention!.CandidateId;
        var person = inhabitants[actor];
        if ((NeedsUrgentFood(person) || NeedsUrgentWarmth(person)) && IsSurvivalCandidate(actor, candidate))
        {
            SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
            if (!decision.Admission.FellBack && decision.Admission.Intention.Provider == DecisionProviderKind.LargeLanguageModel &&
                IsMarketFoodCandidate(actor, candidate))
                ApplyMarketCandidate(actor, person, candidate);
            else
                ApplyCandidate(actor, person, candidate, reportIdle: true);
            return;
        }
        var conversation = IsLinkedTalkOrder(instruction) ? ConversationForOrder(instruction) : ConversationFor(actor);
        if (conversation is null) return;
        // An order requires the attempt, never affirmative wrap-up or resumption consent.
        if (!decision.Admission.FellBack && decision.Admission.Intention.Provider == DecisionProviderKind.LargeLanguageModel &&
            ConversationCandidates(actor).Any(item => item.Id == candidate))
            ApplyConversationCandidate(actor, candidate);
        CompleteClosedTalkOrders();
        if (UnacceptedMarriageOrderBlocker(instruction) is { } eligibilityBlocker)
            SetOrderStatus(instruction, "blocked", eligibilityBlocker);
    }

    private bool ActiveTalkOrderUses(string conversationId) => instructionsByIdempotency.Values.Any(instruction =>
        instruction.Order is { } order && IsConversationOrder(order.Action) && IsActiveOrder(order.Status) && order.TalkConversationId == conversationId);

    private void CompleteClosedTalkOrders()
    {
        foreach (var instruction in instructionsByIdempotency.Values.Where(item => IsLinkedTalkOrder(item) && IsActiveOrder(item.Order!.Status)).ToArray())
        {
            var conversation = conversations.FirstOrDefault(item => item.Id == instruction.Order!.TalkConversationId);
            if (conversation?.Status != AgentConversationStatus.Closed) continue;
            var outcome = instruction.Order!.Action == "propose_marriage" ? MarriageOrderOutcome(conversation, marriages) : conversation.Outcome;
            if (outcome is null) continue; // Accepted marriage still has its own unfinished surname session.
            var current = instruction with { Order = instruction.Order! with { TalkOutcome = outcome } };
            instructionsByIdempotency[current.IdempotencyKey] = current;
            CreditOrderEffect(current, TalkOrderEffectId(current), 1);
        }
    }

    private static string TalkOrderEffectId(OwnerQueuedInstruction instruction) =>
        (instruction.Order!.Action == "propose_marriage" ? "marriage-order:" : "talk-order:") +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{instruction.InstructionId}|{instruction.TargetInhabitantId}|{instruction.Order!.TargetAgentId}|{instruction.Order.TalkConversationId}|{instruction.Order.TalkOutcome}")));

    private static bool IsTalkOutcome(string? outcome) => outcome is "agreed" or "refused" or "deadline" or "daily_limit" or
        "disagreed" or "withdrawn" or "participant_unavailable";

    private static void ValidateTalkOrderBindings(IEnumerable<OwnerQueuedInstruction> instructions, IEnumerable<AgentConversation> conversations, IEnumerable<AgentMarriage> marriageRecords)
    {
        var marriages = marriageRecords.ToArray();
        var records = conversations.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instruction in instructions)
        {
            if (instruction.Order is not { TalkConversationId: { } id } order || !IsConversationOrder(order.Action)) continue;
            if (order.Action == "propose_marriage" && order.TalkOutcome == "married" &&
                !marriages.Any(marriage => marriage.Consent.Id == id && marriage.InitiatorId == instruction.TargetInhabitantId &&
                    marriage.InviteeId == order.TargetAgentId && marriage.CompletedTick is not null))
                throw new InvalidDataException("A finished marriage order lost its completed marriage record.");
            if (!linked.Add(id)) throw new InvalidDataException("Several talk orders share one invitation.");
            if (!records.TryGetValue(id, out var conversation))
            {
                if (IsActiveOrder(order.Status)) throw new InvalidDataException("An active talk order lost its conversation.");
                continue; // Completed/cancelled tasks may outlive bounded closed conversation history.
            }
            if (conversation.Kind != AgentConversationKind.Ordinary || conversation.InitiatorId != instruction.TargetInhabitantId ||
                conversation.InviteeId != order.TargetAgentId || conversation.CreatedTick < instruction.SubmittedTick ||
                order.CompletedUnits == 1 && (conversation.Status != AgentConversationStatus.Closed || (order.Action == "propose_marriage" ? MarriageOrderOutcome(conversation, marriages) : conversation.Outcome) != order.TalkOutcome))
                throw new InvalidDataException("A talk order does not match its actual invitation or outcome.");
        }
    }
}
