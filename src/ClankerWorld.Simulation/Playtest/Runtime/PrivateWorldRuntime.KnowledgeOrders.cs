using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsKnowledgeOrder(string action) => action is "write_knowledge" or "copy_knowledge";

    private static bool MatchesKnowledgeSource(OwnerInstructionOrder order, string? source, bool bound) =>
        order.Action == "write_knowledge" ? source is null && order.KnowledgeCopySourceArtifactId is null :
        order.Action == "copy_knowledge" && source is not null &&
            (order.KnowledgeCopySourceArtifactId == source || !bound && order.KnowledgeCopySourceArtifactId is null);

    private CognitionCandidate? KnowledgeOrderCandidateFor(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var kind = instruction.Order!.TargetKnowledgeKind!;
        if (!AdultResident(actor)) return null;
        if (KnowledgeWritingFor(actor) is { } project)
            return project.Kind == kind && MatchesKnowledgeSource(instruction.Order!, project.SourceArtifactId, project.OrderInstructionId is not null) &&
                (project.OrderInstructionId is null || IsCurrentKnowledgeOrderProject(project))
                ? new(instruction.Order!.Action, "Continue the requested writing using its reserved materials and learned contents.", 0) : null;
        if (instruction.Order!.Action == "copy_knowledge")
            return MayMakeKnowledgeArtifact(actor) && KnowledgeCopySourceFor(instruction) is not null && CanSupplyWriting(actor, kind)
                ? new("copy_knowledge", "Prepare a paid copy from the held source and contents you already know.", 0) : null;
        var facts = FactsToWrite(actor, kind);
        var recipes = RecipesToWrite(actor, kind);
        return MayMakeKnowledgeArtifact(actor) && facts.Length + recipes.Length > 0 && !AlreadyWrote(actor, kind, facts, recipes) && CanSupplyWriting(actor, kind)
            ? new("write_knowledge", "Prepare and write the requested item using real materials and personally learned contents.", 0) : null;
    }

    private string KnowledgeOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var kind = instruction.Order!.TargetKnowledgeKind!;
        if (!AdultResident(actor)) return "Only an adult can write or copy a record, map or book.";
        if (KnowledgeWritingFor(actor) is { } project &&
            (project.Kind != kind || !MatchesKnowledgeSource(instruction.Order!, project.SourceArtifactId, project.OrderInstructionId is not null) ||
             project.OrderInstructionId is not null && !IsCurrentKnowledgeOrderProject(project)))
            return "Another writing job is in progress. Cancel this order to finish that work first.";
        if (!MayMakeKnowledgeArtifact(actor)) return "The writing limit has been reached.";
        if (instruction.Order!.Action == "copy_knowledge") return KnowledgeCopyBlockedReason(instruction);
        var facts = FactsToWrite(actor, kind);
        var recipes = RecipesToWrite(actor, kind);
        if (facts.Length + recipes.Length == 0) return "Waiting for personally learned sites or recipes to write about.";
        if (KnowledgeWritingFor(actor) is null && AlreadyWrote(actor, kind, facts, recipes))
            return "These contents have already been written. Waiting to learn something new.";
        return kind == "book"
            ? "Waiting for permitted paper and cloth, a reachable supply and carrying space."
            : "Waiting for permitted paper, a reachable supply and carrying space.";
    }

    private void ExecuteKnowledgeOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var kind = instruction.Order!.TargetKnowledgeKind!;
        if (KnowledgeOrderCandidateFor(instruction) is null)
        {
            SetOrderStatus(instruction, "blocked", KnowledgeOrderBlockedReason(instruction));
            return;
        }
        if (KnowledgeWritingFor(actor) is null)
        {
            var inventory = society.Checkpoint.Inventory;
            var candidate = KnowledgeWritePrefix + kind;
            if (instruction.Order!.Action == "copy_knowledge")
            {
                var source = KnowledgeCopySourceFor(instruction)!;
                var current = instructionsByIdempotency[instruction.IdempotencyKey];
                instructionsByIdempotency[instruction.IdempotencyKey] = current with
                {
                    Order = current.Order! with { KnowledgeCopySourceArtifactId = source.Id },
                };
                candidate = KnowledgeCopyPrefix + source.Id;
            }
            ApplyKnowledgeWritingCandidate(actor, person, candidate);
            if (KnowledgeWritingFor(actor) is null)
            {
                if (inhabitants[actor].Position == person.Position && society.Checkpoint.Inventory == inventory)
                    SetOrderStatus(instruction, "blocked", KnowledgeOrderBlockedReason(instruction));
                return;
            }
        }
        var project = KnowledgeWritingFor(actor)!;
        if (project.OrderInstructionId is null)
        {
            project = project with { OrderInstructionId = instruction.InstructionId };
            ReplaceKnowledgeWriting(project);
            var current = instructionsByIdempotency[instruction.IdempotencyKey];
            instructionsByIdempotency[instruction.IdempotencyKey] = current with
            {
                Order = current.Order! with
                {
                    KnowledgeWritingProjectId = project.Id,
                    KnowledgeCopySourceArtifactId = project.SourceArtifactId
                },
            };
        }
        ContinueKnowledgeWriting(project);
        var updated = instructionsByIdempotency[instruction.IdempotencyKey];
        if (KnowledgeWritingFor(actor) is null && IsActiveOrder(updated.Order!.Status) &&
            updated.Order.CompletedUnits == instruction.Order!.CompletedUnits)
            SetOrderStatus(updated, "blocked", KnowledgeOrderBlockedReason(updated));
    }

    private bool IsCurrentKnowledgeOrderProject(AgentKnowledgeWritingProject project) =>
        PendingInstructionFor(project.ActorId) is { } instruction && instruction.InstructionId == project.OrderInstructionId &&
        instruction.Order is { } order && IsKnowledgeOrder(order.Action) && order.TargetKnowledgeKind == project.Kind &&
        order.KnowledgeWritingProjectId == project.Id && MatchesKnowledgeSource(order, project.SourceArtifactId, bound: true);

    private void ClearKnowledgeOrderBinding(AgentKnowledgeWritingProject project)
    {
        var instruction = instructionsByIdempotency.Values.FirstOrDefault(item => item.InstructionId == project.OrderInstructionId);
        if (instruction?.Order is not { } order || !IsKnowledgeOrder(order.Action) || order.KnowledgeWritingProjectId != project.Id) return;
        instructionsByIdempotency[instruction.IdempotencyKey] = instruction with
        {
            Order = order with { KnowledgeWritingProjectId = null },
        };
    }

    private void CreditKnowledgeOrderCompletion(AgentKnowledgeWritingProject project, string artifactId)
    {
        if (project.OrderInstructionId is null || !IsCurrentKnowledgeOrderProject(project)) return;
        var instruction = PendingInstructionFor(project.ActorId)!;
        ClearKnowledgeOrderBinding(project);
        if (instruction.Order!.Action == "copy_knowledge")
        {
            var current = instructionsByIdempotency[instruction.IdempotencyKey];
            instructionsByIdempotency[instruction.IdempotencyKey] = current with
            {
                Order = current.Order! with { KnowledgeCopySourceArtifactId = null },
            };
        }
        CreditOrderEffect(instruction, "writing:artifact:" + artifactId, 1);
    }

    private void CancelKnowledgeWritingForOrder(OwnerQueuedInstruction instruction)
    {
        if (instruction.Order is { } order && IsKnowledgeOrder(order.Action) && KnowledgeWritingFor(instruction.TargetInhabitantId) is { } project &&
            project.OrderInstructionId == instruction.InstructionId)
            CancelKnowledgeWriting(project, "owner_order_cancelled");
    }

    private static void ValidateKnowledgeOrderBindings(PrivateWorldKnowledgeState knowledge,
        IEnumerable<OwnerQueuedInstruction> instructions)
    {
        var saved = instructions.ToArray();
        var orders = saved.Where(item => item.Order is { } order && IsKnowledgeOrder(order.Action))
            .ToDictionary(item => item.InstructionId, StringComparer.Ordinal);
        var projects = knowledge.WritingProjects.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var project in knowledge.WritingProjects.Where(item => item.OrderInstructionId is not null))
        {
            if (!orders.TryGetValue(project.OrderInstructionId!, out var instruction) ||
                instruction.TargetInhabitantId != project.ActorId || instruction.Order!.TargetKnowledgeKind != project.Kind ||
                instruction.Order.KnowledgeWritingProjectId != project.Id || !MatchesKnowledgeSource(instruction.Order, project.SourceArtifactId, bound: true) ||
                !IsActiveOrder(instruction.Order.Status) || instruction.Order.Status == "queued" ||
                saved.Where(item => item.TargetInhabitantId == project.ActorId && item.Order is { } order && IsActiveOrder(order.Status))
                    .OrderBy(item => item.SubmissionSequence).FirstOrDefault()?.InstructionId != instruction.InstructionId)
                throw new InvalidDataException("Ordered writing has no matching current owner task.");
        }
        foreach (var artifact in knowledge.Artifacts.Where(item => item.OrderInstructionId is not null))
        {
            if (!orders.TryGetValue(artifact.OrderInstructionId!, out var instruction) ||
                instruction.TargetInhabitantId != artifact.CreatorId || instruction.Order!.TargetKnowledgeKind != artifact.Kind ||
                (instruction.Order.Action == "copy_knowledge") != (artifact.SourceArtifactId is not null) ||
                artifact.CreatedTick < instruction.SubmittedTick)
                throw new InvalidDataException("A written artifact does not belong to its recorded owner task.");
        }
        foreach (var instruction in orders.Values)
        {
            var order = instruction.Order!;
            if (order.KnowledgeCopySourceArtifactId is { } sourceId &&
                !knowledge.Artifacts.Any(item => item.Id == sourceId && item.Kind == order.TargetKnowledgeKind))
                throw new InvalidDataException("A copying order has no matching source artifact.");
            if (order.KnowledgeWritingProjectId is { } projectId &&
                (!projects.TryGetValue(projectId, out var project) || project.OrderInstructionId != instruction.InstructionId))
                throw new InvalidDataException("A writing order is bound to an unrelated job.");
            var completed = knowledge.Artifacts.Where(item => item.OrderInstructionId == instruction.InstructionId)
                .OrderBy(item => item.CreatedTick).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
            if (order.CompletedUnits != completed.Length || completed.Length > 0 &&
                order.LastEffectId != "writing:artifact:" + completed[^1].Id)
                throw new InvalidDataException("Writing order progress has no matching completed physical artifacts.");
        }
    }
}
