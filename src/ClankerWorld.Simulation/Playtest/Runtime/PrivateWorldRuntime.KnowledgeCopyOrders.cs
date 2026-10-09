using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private AgentKnowledgeArtifact? KnowledgeCopySourceFor(OwnerQueuedInstruction instruction) =>
        HeldKnowledgeArtifacts(instruction.TargetInhabitantId).FirstOrDefault(source =>
        {
            var order = instruction.Order!;
            if (source.Kind != order.TargetKnowledgeKind || order.KnowledgeCopySourceArtifactId is { } bound && source.Id != bound) return false;
            var facts = FactsToWrite(instruction.TargetInhabitantId, source.Kind, source);
            return facts.Length > 0 && !AlreadyWrote(instruction.TargetInhabitantId, source.Kind, facts);
        });

    private string KnowledgeCopyBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        var source = HeldKnowledgeArtifacts(actor).FirstOrDefault(item => item.Kind == order.TargetKnowledgeKind &&
            (order.KnowledgeCopySourceArtifactId is null || order.KnowledgeCopySourceArtifactId == item.Id));
        if (source is null) return "Waiting to own and carry the requested source record, map or book.";
        var facts = FactsToWrite(actor, source.Kind, source);
        if (facts.Length == 0) return "Waiting to learn all sites in the held source before copying it.";
        if (KnowledgeCopySourceFor(instruction) is null)
            return "These contents have already been written. Waiting for another held account to copy.";
        return source.Kind == "book"
            ? "Waiting for permitted paper and cloth, a reachable supply and carrying space."
            : "Waiting for permitted paper, a reachable supply and carrying space.";
    }
}
