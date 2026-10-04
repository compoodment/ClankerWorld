using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            var parsedOrder = request.Kind == OwnerInstructionKind.MustDo ? ParseInstructionOrder(text, targetId) : null;
            if (parsedOrder is not null && !request.Queue)
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
                CancelRepairForOrder(instruction);
                CancelFieldWorkForOrder(instruction);
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

    // Provider startup and synchronous cancellation completions can reserve or
    // finish a model call on the thread holding this nonreentrant runtime gate.
    [ThreadStatic] private static PrivateWorldRuntime? providerInvocationUnderGate;

    /// <summary>
    /// True while this thread invokes a provider or its cancellation callbacks
    /// under the runtime gate. Code reached from those callbacks must not wait
    /// for this nonreentrant gate.
    /// </summary>
    public bool IsInvokingProviderUnderGateOnThisThread => ReferenceEquals(providerInvocationUnderGate, this);

    private void CancelProviderCall(CancellationTokenSource cancellation, bool underRuntimeGate)
    {
        var outerInvocation = providerInvocationUnderGate;
        if (underRuntimeGate) providerInvocationUnderGate = this;
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            providerInvocationUnderGate = outerInvocation;
        }
    }

    public void Pause()
    {
        gate.Wait();
        try
        {
            PauseCore();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Pauses like <see cref="Pause"/>, but returns false without pausing if
    /// the runtime stays busy for <paramref name="wait"/> or this thread is
    /// invoking a provider or its cancellation callbacks under the runtime gate.
    /// </summary>
    public bool TryPause(TimeSpan wait)
    {
        if (IsInvokingProviderUnderGateOnThisThread || !gate.Wait(wait)) return false;
        try
        {
            PauseCore();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private void PauseCore()
    {
        var wasPaused = society.Checkpoint.IsPaused;
        var result = society.Pause();
        if (!wasPaused && result.Checkpoint.IsPaused)
        {
            foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
            foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
            CancelIdentityMoments();
            foreach (var id in pendingConversationTurns.Keys.ToArray())
                CancelPendingConversationTurn(id, AgentConversationInterruption.OwnerPaused);
            SuspendAllConversations(AgentConversationInterruption.OwnerPaused);
            AppendEvent("paused", "owner_request");
        }
    }

    /// <summary>
    /// Writes the installation's 80% model-call warning into this world's
    /// Event Log. The count and limit stay in installation accounting; the
    /// event only records what the player was told, like a pause. Returns
    /// false, recording nothing, when the runtime stays busy for
    /// <paramref name="wait"/> or this thread invokes a provider or its
    /// cancellation callbacks under the runtime gate.
    /// </summary>
    public bool TryRecordModelCallWarning(long attempts, long attemptLimit, TimeSpan wait)
    {
        if (attemptLimit < 1 || attempts < 1 || attempts > attemptLimit)
            throw new ArgumentOutOfRangeException(nameof(attempts), "A model-call warning needs a used count within a positive limit.");
        if (IsInvokingProviderUnderGateOnThisThread || !gate.Wait(wait)) return false;
        try
        {
            AppendEvent("model_call_warning", FormattableString.Invariant($"used:{attempts}:limit:{attemptLimit}"));
            return true;
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
            CancelRepairForOrder(instruction);
            CancelFieldWorkForOrder(instruction);
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
                    ? UnderstoodTaskFor(item.Order?.Action)
                    : null,
                item.ObserverReply is null))
            .ToArray();

    private static string? UnderstoodTaskFor(string? candidate) => candidate switch
    {
        "consume_food" => "eat one carried food item",
        "move_to" => "travel to the exact tile named in this order",
        "seek_food" => "travel within gathering range of an available food source",
        "harvest_food" => "gather several food servings from a nearby food source",
        "gather_material" => "gather the requested material from a natural source",
        "till_field" => "till a field for your household",
        "plant_field" => "plant the requested crop in your household field",
        "tend_field" => "tend your household crop",
        "harvest_field" => "harvest your household crop",
        "repair_tool" => "repair your own worn tool",
        "repair_equipment" => "repair your own worn clothing or carrying aid",
        "collect_material" => "collect your own stored or dropped material",
        "collect_food" => "collect your own stored or dropped food",
        "collect_equipment" => "collect your own stored or dropped equipment",
        "store_material" => "store your own carried material in your House",
        "accept_guardianship" => "accept primary care of the named child through their guardian search",
        _ => null,
    };

    private OwnerInstructionOrder? ParseInstructionOrder(string text, string actor)
    {
        return ParseGuardianOrder(text, actor) ?? PrivateWorldInstructionOrderParser.Parse(text, map.Resources, FoodKnowledgeKind);
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

    // Refuse everything a save would refuse before the request touches live
    // state, so a bad request cannot leave the world unable to save.
    private static void ValidateInstructionRequest(OwnerInstructionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IssuerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetInhabitantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        if (!IsValidInstructionIdentifier(request.IdempotencyKey.Trim()))
            throw new ArgumentOutOfRangeException(nameof(request), "Instruction idempotency key must be at most 128 characters without control characters.");
        if (!IsValidInstructionIdentifier(request.IssuerId.Trim()))
            throw new ArgumentOutOfRangeException(nameof(request), "Instruction issuer ID must be at most 128 characters without control characters.");
        if (request.Kind is not (OwnerInstructionKind.Suggestive or OwnerInstructionKind.MustDo))
            throw new ArgumentOutOfRangeException(nameof(request), "Instruction kind must be suggestive or must_do.");
        var text = request.Text.Trim();
        if (text.Length > OwnerQueuedInstruction.MaximumTextLength || text.Any(char.IsControl))
            throw new ArgumentOutOfRangeException(nameof(request), "Instruction text must be at most 512 characters without control characters.");
    }

    private static bool IsValidInstructionIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= OwnerQueuedInstruction.MaximumIdentifierLength &&
        !value.Any(char.IsControl);

    private static string ToWireValue(OwnerInstructionKind kind) => kind switch
    {
        OwnerInstructionKind.Suggestive => "suggestive",
        OwnerInstructionKind.MustDo => "must_do",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

}
