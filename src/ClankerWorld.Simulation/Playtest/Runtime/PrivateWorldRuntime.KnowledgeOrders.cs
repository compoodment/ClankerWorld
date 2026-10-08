using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? KnowledgeOrderCandidateFor(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var kind = instruction.Order!.TargetKnowledgeKind!;
        if (!AdultResident(actor)) return null;
        if (KnowledgeWritingFor(actor) is { } project)
            return project.Kind == kind && project.SourceArtifactId is null &&
                (project.OrderInstructionId is null || IsCurrentKnowledgeOrderProject(project))
                ? new("write_knowledge", "Continue the requested writing using its reserved materials and learned sites.", 0) : null;
        var facts = FactsToWrite(actor, kind);
        return MayMakeKnowledgeArtifact(actor) && facts.Length > 0 && !AlreadyWrote(actor, kind, facts) && CanSupplyWriting(actor, kind)
            ? new("write_knowledge", "Prepare and write the requested item using real materials and personally learned sites.", 0) : null;
    }

    private string KnowledgeOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var kind = instruction.Order!.TargetKnowledgeKind!;
        if (!AdultResident(actor)) return "Only an adult can write a record, map or book.";
        if (KnowledgeWritingFor(actor) is { } project &&
            (project.Kind != kind || project.SourceArtifactId is not null ||
             project.OrderInstructionId is not null && !IsCurrentKnowledgeOrderProject(project)))
            return "Another writing job is in progress. Cancel this order to finish that work first.";
        if (!MayMakeKnowledgeArtifact(actor)) return "The writing limit has been reached.";
        var facts = FactsToWrite(actor, kind);
        if (facts.Length == 0) return "Waiting for personally learned sites to write about.";
        if (KnowledgeWritingFor(actor) is null && AlreadyWrote(actor, kind, facts))
            return "These sites have already been written. Waiting to learn something new.";
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
            ApplyKnowledgeWritingCandidate(actor, person, KnowledgeWritePrefix + kind);
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
                Order = current.Order! with { KnowledgeWritingProjectId = project.Id },
            };
        }
        ContinueKnowledgeWriting(project);
    }

    private bool IsCurrentKnowledgeOrderProject(AgentKnowledgeWritingProject project) =>
        PendingInstructionFor(project.ActorId) is { } instruction && instruction.InstructionId == project.OrderInstructionId &&
        instruction.Order is { Action: "write_knowledge" } order && order.TargetKnowledgeKind == project.Kind &&
        order.KnowledgeWritingProjectId == project.Id && project.SourceArtifactId is null;

    private void ClearKnowledgeOrderBinding(AgentKnowledgeWritingProject project)
    {
        var instruction = instructionsByIdempotency.Values.FirstOrDefault(item => item.InstructionId == project.OrderInstructionId);
        if (instruction?.Order is not { Action: "write_knowledge" } order || order.KnowledgeWritingProjectId != project.Id) return;
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
        CreditOrderEffect(instruction, "writing:artifact:" + artifactId, 1);
    }

    private void CancelKnowledgeWritingForOrder(OwnerQueuedInstruction instruction)
    {
        if (instruction.Order?.Action == "write_knowledge" && KnowledgeWritingFor(instruction.TargetInhabitantId) is { } project &&
            project.OrderInstructionId == instruction.InstructionId)
            CancelKnowledgeWriting(project, "owner_order_cancelled");
    }

    private static void ValidateKnowledgeOrderBindings(PrivateWorldKnowledgeState knowledge,
        IEnumerable<OwnerQueuedInstruction> instructions)
    {
        var saved = instructions.ToArray();
        var orders = saved.Where(item => item.Order?.Action == "write_knowledge")
            .ToDictionary(item => item.InstructionId, StringComparer.Ordinal);
        var projects = knowledge.WritingProjects.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var project in knowledge.WritingProjects.Where(item => item.OrderInstructionId is not null))
        {
            if (!orders.TryGetValue(project.OrderInstructionId!, out var instruction) ||
                instruction.TargetInhabitantId != project.ActorId || instruction.Order!.TargetKnowledgeKind != project.Kind ||
                instruction.Order.KnowledgeWritingProjectId != project.Id || project.SourceArtifactId is not null ||
                !IsActiveOrder(instruction.Order.Status) || instruction.Order.Status == "queued" ||
                saved.Where(item => item.TargetInhabitantId == project.ActorId && item.Order is { } order && IsActiveOrder(order.Status))
                    .OrderBy(item => item.SubmissionSequence).FirstOrDefault()?.InstructionId != instruction.InstructionId)
                throw new InvalidDataException("Ordered writing has no matching current owner task.");
        }
        foreach (var artifact in knowledge.Artifacts.Where(item => item.OrderInstructionId is not null))
        {
            if (!orders.TryGetValue(artifact.OrderInstructionId!, out var instruction) ||
                instruction.TargetInhabitantId != artifact.CreatorId || instruction.Order!.TargetKnowledgeKind != artifact.Kind ||
                artifact.SourceArtifactId is not null || artifact.CreatedTick < instruction.SubmittedTick)
                throw new InvalidDataException("A written artifact does not belong to its recorded owner task.");
        }
        foreach (var instruction in orders.Values)
        {
            var order = instruction.Order!;
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
