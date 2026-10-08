using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
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

    private static bool IsLinkedTalkOrder(OwnerQueuedInstruction instruction) => instruction.Order is { Action: "talk_to", TalkConversationId: not null };

    private bool TalkOrderHasConversation(OwnerQueuedInstruction instruction) => instruction.Order is { Action: "talk_to" } &&
        (IsLinkedTalkOrder(instruction) || ConversationFor(instruction.TargetInhabitantId) is not null);

    private CognitionCandidate? TalkOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person) =>
        IsLinkedTalkOrder(instruction) || TalkOrderBlocker(instruction, person) is null
            ? new("talk_to", IsLinkedTalkOrder(instruction) ? "Wait for the actual conversation's outcome, preserving each person's choices." :
                "Reach the named person and attempt one face-to-face invitation.", 0, instruction.Order!.TargetAgentId)
            : null;

    private string? TalkOrderBlocker(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var target = instruction.Order!.TargetAgentId!;
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
            if (conversations.FirstOrDefault(item => item.Id == instruction.Order!.TalkConversationId)?.Status == AgentConversationStatus.Suspended)
                SetOrderStatus(instruction, "blocked", "Conversation stopped; both people must choose to resume, or you may end it.");
            return;
        }
        if (TalkOrderBlocker(instruction, person) is { } blocker)
        {
            SetOrderStatus(instruction, "blocked", blocker);
            return;
        }
        var actor = instruction.TargetInhabitantId;
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
            ApplyCandidate(actor, person, candidate, reportIdle: true);
            return;
        }
        var conversation = instruction.Order!.TalkConversationId is { } id
            ? conversations.FirstOrDefault(item => item.Id == id) : ConversationFor(actor);
        if (conversation is null) return;
        // An order requires the attempt, never affirmative wrap-up or resumption consent.
        if (!decision.Admission.FellBack && decision.Admission.Intention.Provider == DecisionProviderKind.LargeLanguageModel &&
            ConversationCandidates(actor).Any(item => item.Id == candidate))
            ApplyConversationCandidate(actor, candidate);
        CompleteClosedTalkOrders();
    }

    private bool ActiveTalkOrderUses(string conversationId) => instructionsByIdempotency.Values.Any(instruction =>
        instruction.Order is { Action: "talk_to" } order && IsActiveOrder(order.Status) && order.TalkConversationId == conversationId);

    private void CompleteClosedTalkOrders()
    {
        foreach (var instruction in instructionsByIdempotency.Values.Where(item => IsLinkedTalkOrder(item) && IsActiveOrder(item.Order!.Status)).ToArray())
        {
            var conversation = conversations.FirstOrDefault(item => item.Id == instruction.Order!.TalkConversationId);
            if (conversation?.Status != AgentConversationStatus.Closed) continue;
            var current = instruction with { Order = instruction.Order! with { TalkOutcome = conversation.Outcome } };
            instructionsByIdempotency[current.IdempotencyKey] = current;
            CreditOrderEffect(current, TalkOrderEffectId(current), 1);
        }
    }

    private static string TalkOrderEffectId(OwnerQueuedInstruction instruction) => "talk-order:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{instruction.InstructionId}|{instruction.TargetInhabitantId}|{instruction.Order!.TargetAgentId}|{instruction.Order.TalkConversationId}|{instruction.Order.TalkOutcome}")));

    private static bool IsTalkOutcome(string? outcome) => outcome is "agreed" or "refused" or "deadline" or "daily_limit" or
        "disagreed" or "withdrawn" or "participant_unavailable";

    private static void ValidateTalkOrderBindings(IEnumerable<OwnerQueuedInstruction> instructions, IEnumerable<AgentConversation> conversations)
    {
        var records = conversations.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instruction in instructions)
        {
            if (instruction.Order is not { Action: "talk_to", TalkConversationId: { } id } order) continue;
            if (!linked.Add(id)) throw new InvalidDataException("Several talk orders share one invitation.");
            if (!records.TryGetValue(id, out var conversation))
            {
                if (IsActiveOrder(order.Status)) throw new InvalidDataException("An active talk order lost its conversation.");
                continue; // Completed/cancelled tasks may outlive bounded closed conversation history.
            }
            if (conversation.Kind != AgentConversationKind.Ordinary || conversation.InitiatorId != instruction.TargetInhabitantId ||
                conversation.InviteeId != order.TargetAgentId || conversation.CreatedTick < instruction.SubmittedTick ||
                order.CompletedUnits == 1 && (conversation.Status != AgentConversationStatus.Closed || conversation.Outcome != order.TalkOutcome))
                throw new InvalidDataException("A talk order does not match its actual invitation or outcome.");
        }
    }
}
