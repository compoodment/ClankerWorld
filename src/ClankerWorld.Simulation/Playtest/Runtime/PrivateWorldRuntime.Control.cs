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
    private static readonly HashSet<string> CompoundInstructionWords =
        new(StringComparer.Ordinal) { "and", "or", "then" };
    private static readonly HashSet<string> HarvestModifierInstructionWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "some", "one", "two", "three", "four", "five", "six", "seven", "eight",
        "nine", "ten", "fresh", "ripe", "wild", "local", "nearby", "available", "few", "several", "many",
        "from", "at", "near", "by", "to", "tile",
    };
    private static readonly HashSet<string> UnsupportedInstructionOperationWords = new(StringComparer.Ordinal)
    {
        "build", "builds", "built", "construct", "constructs", "craft", "crafts", "make", "makes",
        "repair", "repairs", "fix", "fixes", "farm", "farms", "plant", "plants", "sow", "sows", "till",
        "mine", "mines", "dig", "digs", "cut", "cuts", "chop", "chops", "deliver", "delivers", "carry",
        "carries", "store", "stores", "place", "places", "cook", "cooks", "buy", "sell", "trade",
    };
    private static readonly HashSet<string> GenericFoodTargetInstructionWords = new(StringComparer.Ordinal)
    {
        "food", "berry", "berries", "fruit", "wild", "greens", "source", "site", "patch", "orchard",
        "nearby", "nearest", "closest", "known", "available",
    };
    private static readonly HashSet<string> SupportedFoodObjectInstructionWords = new(StringComparer.Ordinal)
    {
        "food", "berry", "berries", "fruit", "wild", "greens", "item", "items", "piece", "pieces",
        "serving", "servings",
    };

    private string? InstructionCandidate(string text)
    {
        // Whole words only, so "heat" is not "eat" and "good" is not "go".
        var words = InstructionWords(text);
        if (words.Overlaps(UnsupportedInstructionOperationWords) ||
            words.Overlaps(CompoundInstructionWords))
            return null;
        if (HasNegatedFoodInstruction(text)) return null;

        var namesFoodTarget = words.Contains("food") || words.Overlaps(BerryInstructionWords) ||
            words.Overlaps(FruitInstructionWords) || WildGreenInstructionWords.All(words.Contains) ||
            map.Resources.Any(resource => (resource.Kind is "food" or "fruit") &&
                ContainsWholeResourceId(text, resource.Id));
        if (words.Overlaps(HarvestInstructionWords))
        {
            return namesFoodTarget && !HasUnsupportedHarvestObject(text) ? "harvest_food" : null;
        }

        if (words.Overlaps(EatInstructionWords))
        {
            return HasUnsupportedEatObject(text) ? null : "consume_food";
        }

        if (words.Overlaps(TravelInstructionWords))
        {
            return namesFoodTarget ? "seek_food" : null;
        }

        return null;
    }

    private bool HasUnsupportedHarvestObject(string text)
    {
        var verb = Regex.Match(text, @"\b(?:harvest|harvests|harvesting|gather|gathers|gathering)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!verb.Success) return false;

        var tail = text[(verb.Index + verb.Length)..];
        var tokens = Regex.Matches(tail, @"[\p{L}\p{N}_-]+")
            .Select(match => match.Value.ToLowerInvariant()).ToArray();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (HarvestModifierInstructionWords.Contains(token) ||
                int.TryParse(token, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                continue;
            if (token is "food" or "berry" or "berries" or "fruit") return false;
            if (token == "greens")
                return !tokens.Take(index).Contains("wild", StringComparer.Ordinal);
            if (map.Resources.Any(resource => (resource.Kind is "food" or "fruit") &&
                    resource.Id.Equals(token, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }

        return false;
    }

    private static bool HasUnsupportedEatObject(string text)
    {
        var match = Regex.Match(text,
            @"\b(?:eat|eats|eating)\s+(?:(?:the|a|an|some|one|two|three|four|five|six|seven|eight|nine|ten|all|any|[-+]?(?:\d+(?:\.\d+)?|\.\d+))\s+)*(?<object>[\p{L}]+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && !SupportedFoodObjectInstructionWords.Contains(match.Groups["object"].Value.ToLowerInvariant());
    }

    private static bool HasNegatedFoodInstruction(string text) =>
        Regex.IsMatch(text,
            @"\b(?:(?:do|does|did)\s+not|don['’]t|doesn['’]t|didn['’]t|never|not)\s+(?:to\s+)?(?:eat|eats|eating|harvest|harvests|harvesting|gather|gathers|gathering|go|goes|going|travel|travels|traveling|find|finds|finding|seek|seeks|seeking)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private OwnerInstructionOrder? ParseInstructionOrder(string text)
    {
        var action = InstructionCandidate(text);
        if (action is null) return null;
        var words = InstructionWords(text);
        if (!TryParseRequestedUnits(text, out var explicitUnits)) return null;
        var requestedUnits = explicitUnits ?? 1;
        var repeat = text.Contains("until cancelled", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("until canceled", StringComparison.OrdinalIgnoreCase) ||
            words.Overlaps(new HashSet<string>(["repeat", "repeatedly", "keep"], StringComparer.Ordinal));
        var targetKind = words.Contains("berry") || words.Contains("berries") ? "berries"
            : words.Contains("fruit") ? "fruit"
            : words.Contains("wild") && words.Contains("greens") ? "wild_greens"
            : null;
        var targetResourceId = map.Resources
            .Where(resource => (resource.Kind is "food" or "fruit") && ContainsWholeResourceId(text, resource.Id))
            .OrderByDescending(resource => resource.Id.Length)
            .Select(resource => resource.Id)
            .FirstOrDefault();
        if (ContainsUnrecognizedExplicitFoodTarget(text, targetResourceId)) return null;
        var targetPosition = ParseOrderTargetPosition(text);
        if (targetResourceId is not null) targetPosition = null;
        if (targetResourceId is { } exactResourceId && targetKind is { } requestedKind &&
            map.Resources.Single(resource => resource.Id == exactResourceId) is { } exactResource &&
            FoodKnowledgeKind(exactResource) != requestedKind) return null;
        return new OwnerInstructionOrder(action, "queued", requestedUnits, 0,
            action switch
            {
                "seek_food" => "arrivals",
                "harvest_food" when explicitUnits is null => "harvests",
                _ => "food_items",
            }, repeat, explicitUnits is not null, targetKind, targetResourceId, targetPosition);
    }

    private static bool ContainsWholeResourceId(string text, string resourceId) =>
        Regex.IsMatch(text,
            $@"(?<![\p{{L}}\p{{N}}_-]){Regex.Escape(resourceId)}(?![\p{{L}}\p{{N}}_-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private bool ContainsUnrecognizedExplicitFoodTarget(string text, string? targetResourceId)
    {
        foreach (Match match in Regex.Matches(text,
                     @"\b(?:at|near|by|from|to|in)\s+(?:(?:the|a|an)\s+)?(?<target>[\p{L}\p{N}_-]+)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var target = match.Groups["target"].Value;
            if (int.TryParse(target, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out _) ||
                target.Equals("tile", StringComparison.OrdinalIgnoreCase))
                continue;
            if (targetResourceId is not null && ContainsWholeResourceId(target, targetResourceId)) continue;
            if (map.Resources.Any(resource => (resource.Kind is "food" or "fruit") &&
                    ContainsWholeResourceId(target, resource.Id)))
                continue;
            if (InstructionWords(target).All(GenericFoodTargetInstructionWords.Contains)) continue;
            return true;
        }
        return false;
    }

    private static GridPoint? ParseOrderTargetPosition(string text)
    {
        var match = Regex.Match(text,
            @"\b(?:at|near|by|from|in)\s*(?:tile\s*)?(?<x>-?\d{1,7})\s*[,/]\s*(?<y>-?\d{1,7})\b",
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

    private static bool TryParseRequestedUnits(string text, out int? requestedUnits)
    {
        requestedUnits = null;
        if (Regex.IsMatch(text,
                @"(?<![\p{L}\p{N}])[-+]?(?:\d+\.\d+|\.\d+)\s+(?:wild\s+greens|food|berries|berry|fruit|items?|pieces?|servings?)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        if (Regex.IsMatch(text,
                @"(?<![\p{L}\p{N}])[-+]\s*\d+\s+(?:wild\s+greens|food|berries|berry|fruit|items?|pieces?|servings?)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        var match = Regex.Match(text,
            @"(?<![\p{L}\p{N},+-])\b(?<amount>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?:wild\s+greens|food|berries|berry|fruit|items?|pieces?|servings?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return true;

        var amount = match.Groups["amount"].Value;
        var parsed = int.TryParse(amount, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var numeric)
            ? numeric
            : amount.ToLowerInvariant() switch
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
        if (parsed is < 1 or > 1000) return false;
        requestedUnits = parsed;
        return true;
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
