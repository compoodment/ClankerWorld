using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public OwnerInstructionReceipt SubmitInstruction(OwnerInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInstructionRequest(request);
        gate.Wait();
        try
        {
            var idempotencyKey = request.IdempotencyKey.Trim();
            if (instructionsByIdempotency.TryGetValue(idempotencyKey, out var existing))
            {
                if (!Matches(existing, request))
                {
                    throw new InvalidOperationException(
                        "An idempotency key cannot be reused for a different instruction request.");
                }

                return instructionReceipts[idempotencyKey];
            }

            var targetId = request.TargetInhabitantId.Trim();
            var target = society.Checkpoint.Inhabitants.SingleOrDefault(item => item.Id == targetId);
            if (target is null || target.Status != SocietyInhabitantStatus.Active)
            {
                throw new ArgumentException($"No active inhabitant with ID '{targetId}' exists.", nameof(request));
            }
            if (target.AgeBand == SocietyAgeBand.Infant)
            {
                throw new ArgumentException("Infants cannot carry out owner instructions; direct care through an adult caregiver.", nameof(request));
            }

            var text = request.Text.Trim();
            var hasPendingOrder = PendingInstructionFor(targetId) is not null;
            var parsedOrder = request.Kind == OwnerInstructionKind.MustDo ? ParseInstructionOrder(text) : null;
            if (request.Kind == OwnerInstructionKind.MustDo && !request.Queue)
                ReplacePendingOrders(targetId);
            if (parsedOrder is not null)
                parsedOrder = parsedOrder with { Status = request.Queue && hasPendingOrder ? "queued" : "waiting" };

            var sequence = nextInstructionSequence++;
            var instruction = new OwnerQueuedInstruction(
                $"private-instruction-{sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)}",
                idempotencyKey,
                request.IssuerId.Trim(),
                targetId,
                request.Kind,
                text,
                WorldTick,
                society.Checkpoint.RunEpoch,
                sequence,
                OwnerInstructionState.Queued,
                Queue: request.Queue,
                Order: request.Kind != OwnerInstructionKind.MustDo ? null : parsedOrder ??
                    new OwnerInstructionOrder("unknown", "not_understood", 0, 0, "none", false));
            instructionsByIdempotency.Add(instruction.IdempotencyKey, instruction);
            var receipt = new OwnerInstructionReceipt(
                instruction.InstructionId,
                instruction.IdempotencyKey,
                instruction.SubmittedTick,
                instruction.RunEpoch,
                sequence);
            instructionReceipts.Add(instruction.IdempotencyKey, receipt);
            AppendEvent("instruction_queued", $"{instruction.InstructionId}:{ToWireValue(instruction.Kind)}");
            CloseOrdersNotUnderstood(targetId);
            return receipt;
        }
        finally
        {
            gate.Release();
        }
    }

    public OwnerOrderControlReceipt CancelOrder(OwnerOrderCancelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOrderCancelRequest(request);
        gate.Wait();
        try
        {
            var key = request.IdempotencyKey.Trim();
            if (orderCancellations.TryGetValue(key, out var existing))
            {
                if (!Matches(existing, request))
                    throw new InvalidOperationException("An idempotency key cannot be reused for a different order cancellation.");
                return existing.Receipt;
            }
            if (!string.Equals(request.WorldId.Trim(), society.Checkpoint.WorldId, StringComparison.Ordinal))
                throw new InvalidOperationException("The cancellation belongs to another world.");
            var instruction = instructionsByIdempotency.Values.SingleOrDefault(item =>
                item.InstructionId == request.OrderId.Trim());
            if (instruction is null || instruction.Kind != OwnerInstructionKind.MustDo ||
                instruction.TargetInhabitantId != request.TargetInhabitantId.Trim())
                throw new ArgumentException("The order does not belong to this active agent in this world.", nameof(request));

            var changed = instruction.Order is { } order && IsActiveOrder(order.Status) &&
                !completedInstructionIds.Contains(instruction.InstructionId);
            var status = instruction.Order?.Status ?? "not_understood";
            if (changed)
            {
                instructionsByIdempotency[instruction.IdempotencyKey] = instruction with
                {
                    Order = instruction.Order! with
                    {
                        Status = "cancelled",
                        BlockedReason = null,
                        WaitForDecisionAfterFailure = false,
                    },
                };
                completedInstructionIds.Add(instruction.InstructionId);
                status = "cancelled";
                AppendEvent("instruction_order_cancelled", $"{instruction.TargetInhabitantId}:{instruction.InstructionId}:owner");
            }
            var receipt = new OwnerOrderControlReceipt(
                instruction.InstructionId, status, changed, WorldTick, nextEventId - 1);
            orderCancellations.Add(key, new OwnerOrderCancellation(
                key, request.IssuerId.Trim(), request.WorldId.Trim(), request.TargetInhabitantId.Trim(),
                request.OrderId.Trim(), receipt));
            checkpointSchemaVersion = StateSchemaVersion;
            return receipt;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool SetLifePace(int rate)
    {
        gate.Wait();
        try
        {
            var before = society.Checkpoint.LifeClock;
            society.Apply(checkpoint => SocietyFixture.SetLifePace(checkpoint, rate));
            if (before == society.Checkpoint.LifeClock) return false;
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("life_pace_changed", rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool SetJevEnabled(bool enabled)
    {
        gate.Wait();
        try
        {
            if (!society.Checkpoint.IsPaused)
                throw new InvalidOperationException("Pause the world before changing Jev assistance.");
            if (jevEnabled == enabled) return false;
            foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
            foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
            jevEnabled = enabled;
            jevPolicyRevision = checked(jevPolicyRevision + 1);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("jev_assistance_changed", enabled ? "enabled" : "disabled");
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Pause()
    {
        gate.Wait();
        try
        {
            var wasPaused = society.Checkpoint.IsPaused;
            var result = society.Pause();
            if (!wasPaused && result.Checkpoint.IsPaused)
            {
                foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
                foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
                foreach (var id in pendingConversationTurns.Keys.ToArray())
                    CancelPendingConversationTurn(id, AgentConversationInterruption.OwnerPaused);
                SuspendAllConversations(AgentConversationInterruption.OwnerPaused);
                AppendEvent("paused", "owner_request");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Resume()
    {
        gate.Wait();
        try
        {
            if (founderSetup is { Started: false })
                throw new InvalidOperationException("Place four founders and explicitly start the world before time can run.");
            var wasPaused = society.Checkpoint.IsPaused;
            var result = society.Resume();
            if (wasPaused && !result.Checkpoint.IsPaused)
            {
                AppendEvent("resumed", $"epoch:{result.Checkpoint.RunEpoch}");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private OwnerQueuedInstruction? PendingInstructionFor(string inhabitantId) =>
        instructionsByIdempotency.Values
            .Where(item => item.TargetInhabitantId == inhabitantId &&
                item.Kind == OwnerInstructionKind.MustDo &&
                item.Order is not null && IsActiveOrder(item.Order.Status) &&
                !completedInstructionIds.Contains(item.InstructionId))
            .OrderBy(item => item.SubmissionSequence)
            .FirstOrDefault();

    private void ReplacePendingOrders(string inhabitantId)
    {
        foreach (var instruction in instructionsByIdempotency.Values
                     .Where(item => item.TargetInhabitantId == inhabitantId &&
                         item.Kind == OwnerInstructionKind.MustDo && item.Order is { } order &&
                         IsActiveOrder(order.Status) && !completedInstructionIds.Contains(item.InstructionId))
                     .OrderBy(item => item.SubmissionSequence).ToArray())
        {
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction with
            {
                Order = instruction.Order! with
                {
                    Status = "cancelled",
                    BlockedReason = "Replaced by a newer order.",
                    WaitForDecisionAfterFailure = false,
                },
            };
            completedInstructionIds.Add(instruction.InstructionId);
            AppendEvent("instruction_order_cancelled", $"{inhabitantId}:{instruction.InstructionId}:replaced");
        }
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private static bool IsActiveOrder(string status) =>
        status is "queued" or "waiting" or "doing" or "interrupted" or "blocked";

    private bool HasNewObserverGuidanceFor(string inhabitantId) =>
        ObserverGuidanceInstructionsFor(inhabitantId).Any(item => item.GuidancePromptedTick is null);

    private OwnerQueuedInstruction[] ObserverGuidanceInstructionsFor(string inhabitantId)
    {
        var messages = instructionsByIdempotency.Values
            .Where(item => item.TargetInhabitantId == inhabitantId &&
                !completedInstructionIds.Contains(item.InstructionId) &&
                (item.Kind == OwnerInstructionKind.Suggestive ||
                    item.InstructionId == PendingInstructionFor(inhabitantId)?.InstructionId))
            .OrderBy(item => item.SubmissionSequence)
            .ToList();
        var selected = messages.Take(InhabitantObservation.MaximumObserverGuidanceCount).ToList();
        if (messages.Count > selected.Count &&
            PendingInstructionFor(inhabitantId) is { } operativeOrder &&
            selected.All(item => item.InstructionId != operativeOrder.InstructionId))
        {
            selected[^1] = operativeOrder;
            selected.Sort((left, right) => left.SubmissionSequence.CompareTo(right.SubmissionSequence));
        }

        return selected.ToArray();
    }

    private CognitionObserverGuidance[] ObserverGuidanceFor(string inhabitantId) =>
        ObserverGuidanceInstructionsFor(inhabitantId)
            .Select(item => new CognitionObserverGuidance(
                item.InstructionId,
                item.IssuerId,
                item.TargetInhabitantId,
                ToWireValue(item.Kind),
                item.Text,
                item.SubmittedTick,
                item.RunEpoch,
                item.SubmissionSequence,
                item.Kind == OwnerInstructionKind.MustDo
                    ? UnderstoodTaskFor(InstructionCandidate(item.Text))
                    : null,
                item.ObserverReply is null))
            .ToArray();

    private static string? UnderstoodTaskFor(string? candidate) => candidate switch
    {
        "consume_food" => "eat one carried food item",
        "seek_food" => "travel within gathering range of an available food source",
        "harvest_food" => "gather several food servings from a nearby food source",
        _ => null,
    };

    private static readonly HashSet<string> HarvestInstructionWords =
        new(StringComparer.Ordinal) { "harvest", "harvests", "harvesting", "gather", "gathers", "gathering" };
    private static readonly HashSet<string> BerryInstructionWords =
        new(StringComparer.Ordinal) { "berry", "berries" };
    private static readonly HashSet<string> FruitInstructionWords =
        new(StringComparer.Ordinal) { "fruit" };
    private static readonly HashSet<string> WildGreenInstructionWords =
        new(StringComparer.Ordinal) { "wild", "greens" };
    private static readonly HashSet<string> EatInstructionWords =
        new(StringComparer.Ordinal) { "eat", "eats", "eating", "food", "hungry" };
    private static readonly HashSet<string> TravelInstructionWords =
        new(StringComparer.Ordinal) { "go", "goes", "going", "travel", "travels", "traveling", "travelling", "move", "moves", "moving" };

    private static string? InstructionCandidate(string text)
    {
        // Whole words only, so "heat" is not "eat" and "good" is not "go".
        var words = InstructionWords(text);
        if (words.Overlaps(HarvestInstructionWords))
        {
            return "harvest_food";
        }

        if (words.Overlaps(EatInstructionWords))
        {
            return "consume_food";
        }

        if (words.Overlaps(TravelInstructionWords) || words.Overlaps(BerryInstructionWords) ||
            words.Overlaps(FruitInstructionWords) ||
            WildGreenInstructionWords.All(words.Contains))
        {
            return "seek_food";
        }

        return null;
    }

    private OwnerInstructionOrder? ParseInstructionOrder(string text)
    {
        var action = InstructionCandidate(text);
        if (action is null) return null;
        var words = InstructionWords(text);
        var explicitUnits = ParseRequestedUnits(text);
        var requestedUnits = explicitUnits ?? 1;
        var repeat = text.Contains("until cancelled", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("until canceled", StringComparison.OrdinalIgnoreCase) ||
            words.Overlaps(new HashSet<string>(["repeat", "repeatedly", "keep"], StringComparer.Ordinal));
        var targetKind = words.Contains("berry") || words.Contains("berries") ? "berries"
            : words.Contains("fruit") ? "fruit"
            : words.Contains("wild") && words.Contains("greens") ? "wild_greens"
            : null;
        var targetResourceId = map.Resources.FirstOrDefault(resource =>
            text.Contains(resource.Id, StringComparison.OrdinalIgnoreCase))?.Id;
        var targetPosition = ParseOrderTargetPosition(text);
        if (targetResourceId is not null) targetPosition = null;
        return new OwnerInstructionOrder(action, "queued", requestedUnits, 0,
            action switch
            {
                "seek_food" => "arrivals",
                "harvest_food" when explicitUnits is null => "harvests",
                _ => "food_items",
            }, repeat, explicitUnits is not null, targetKind, targetResourceId, targetPosition);
    }

    private static GridPoint? ParseOrderTargetPosition(string text)
    {
        var match = Regex.Match(text,
            @"\b(?:at|near|by|from)\s+(?:tile\s+)?(?<x>-?\d{1,7})\s*[,/]\s*(?<y>-?\d{1,7})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["x"].Value,
                   System.Globalization.NumberStyles.AllowLeadingSign,
                   System.Globalization.CultureInfo.InvariantCulture, out var x) &&
               int.TryParse(match.Groups["y"].Value,
                   System.Globalization.NumberStyles.AllowLeadingSign,
                   System.Globalization.CultureInfo.InvariantCulture, out var y)
            ? new GridPoint(x, y)
            : null;
    }

    private static int? ParseRequestedUnits(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            if (!char.IsLetterOrDigit(text[index]))
            {
                index++;
                continue;
            }
            var start = index++;
            while (index < text.Length && char.IsLetterOrDigit(text[index])) index++;
            var token = text[start..index].ToLowerInvariant();
            if (int.TryParse(token, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var numeric))
                return numeric is >= 1 and <= 1000 ? numeric : null;
            var wordQuantity = token switch
            {
                "one" => 1,
                "two" => 2,
                "three" => 3,
                "four" => 4,
                "five" => 5,
                "six" => 6,
                "seven" => 7,
                "eight" => 8,
                "nine" => 9,
                "ten" => 10,
                _ => 0,
            };
            if (wordQuantity > 0) return wordQuantity;
        }
        return null;
    }

    private static HashSet<string> InstructionWords(string text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && char.IsLetter(text[index]))
            {
                if (start < 0) start = index;
                continue;
            }

            if (start >= 0)
            {
                words.Add(text[start..index].ToLowerInvariant());
                start = -1;
            }
        }

        return words;
    }

    // A direct order that names no action the game can carry out is closed
    // at once. It never waits for a decision or holds up later instructions.
    private void CloseOrdersNotUnderstood(string inhabitantId)
    {
        foreach (var order in instructionsByIdempotency.Values
                     .Where(item => item.TargetInhabitantId == inhabitantId &&
                         item.Kind == OwnerInstructionKind.MustDo &&
                         !completedInstructionIds.Contains(item.InstructionId) &&
                         item.Order?.Action == "unknown")
                     .OrderBy(item => item.SubmissionSequence)
                     .ToArray())
        {
            instructionsByIdempotency[order.IdempotencyKey] = order with
            {
                Order = order.Order! with { Status = "not_understood" },
            };
            completedInstructionIds.Add(order.InstructionId);
            AppendEvent("instruction_not_understood", $"{inhabitantId}:{order.InstructionId}");
        }
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private static bool Matches(OwnerQueuedInstruction existing, OwnerInstructionRequest request) =>
        existing.IssuerId == request.IssuerId.Trim() &&
        existing.TargetInhabitantId == request.TargetInhabitantId.Trim() &&
        existing.Kind == request.Kind &&
        existing.Text == request.Text.Trim() &&
        existing.Queue == request.Queue;

    private static bool Matches(OwnerOrderCancellation existing, OwnerOrderCancelRequest request) =>
        existing.IssuerId == request.IssuerId.Trim() &&
        existing.WorldId == request.WorldId.Trim() &&
        existing.TargetInhabitantId == request.TargetInhabitantId.Trim() &&
        existing.OrderId == request.OrderId.Trim();

    private static void ValidateOrderCancelRequest(OwnerOrderCancelRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IssuerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetInhabitantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderId);
        foreach (var value in new[] { request.IdempotencyKey, request.IssuerId, request.WorldId,
                     request.TargetInhabitantId, request.OrderId })
            if (value.Trim().Length > 128 || value.Any(char.IsControl))
                throw new ArgumentOutOfRangeException(nameof(request), "Order cancellation identities must be bounded and contain no control characters.");
    }

    private static void ValidateInstructionRequest(OwnerInstructionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IssuerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetInhabitantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        var text = request.Text.Trim();
        if (text.Length > OwnerQueuedInstruction.MaximumTextLength || text.Any(char.IsControl))
            throw new ArgumentOutOfRangeException(nameof(request), "Instruction text must be at most 512 characters without control characters.");
    }

    private static string ToWireValue(OwnerInstructionKind kind) => kind switch
    {
        OwnerInstructionKind.Suggestive => "suggestive",
        OwnerInstructionKind.MustDo => "must_do",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

}
