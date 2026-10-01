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

            var sequence = nextInstructionSequence++;
            var instruction = new OwnerQueuedInstruction(
                $"private-instruction-{sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)}",
                idempotencyKey,
                request.IssuerId.Trim(),
                targetId,
                request.Kind,
                request.Text.Trim(),
                WorldTick,
                society.Checkpoint.RunEpoch,
                sequence,
                OwnerInstructionState.Queued);
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
                !completedInstructionIds.Contains(item.InstructionId))
            .Where(item => InstructionCandidate(item.Text) is not null)
            .OrderBy(item => item.SubmissionSequence)
            .FirstOrDefault();

    private bool HasNewObserverGuidanceFor(string inhabitantId) =>
        ObserverGuidanceInstructionsFor(inhabitantId).Any(item => item.GuidancePromptedTick is null);

    private OwnerQueuedInstruction[] ObserverGuidanceInstructionsFor(string inhabitantId)
    {
        var messages = instructionsByIdempotency.Values
            .Where(item => item.TargetInhabitantId == inhabitantId &&
                !completedInstructionIds.Contains(item.InstructionId) &&
                (item.Kind == OwnerInstructionKind.Suggestive || InstructionCandidate(item.Text) is not null))
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

        if (words.Overlaps(BerryInstructionWords))
        {
            return "seek_food";
        }

        if (words.Overlaps(EatInstructionWords))
        {
            return "consume_food";
        }

        if (words.Overlaps(TravelInstructionWords))
        {
            return "seek_food";
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
                         InstructionCandidate(item.Text) is null)
                     .OrderBy(item => item.SubmissionSequence)
                     .ToArray())
        {
            completedInstructionIds.Add(order.InstructionId);
            AppendEvent("instruction_not_understood", $"{inhabitantId}:{order.InstructionId}");
        }
    }

    private static bool Matches(OwnerQueuedInstruction existing, OwnerInstructionRequest request) =>
        existing.IssuerId == request.IssuerId.Trim() &&
        existing.TargetInhabitantId == request.TargetInhabitantId.Trim() &&
        existing.Kind == request.Kind &&
        existing.Text == request.Text.Trim();

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
