using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string KnowledgeReadOrderTask = "read one personally held written record, map or book and learn only its written facts";

    private OwnerInstructionOrder? ParseKnowledgeReadOrder(string text)
    {
        text = text.Trim();
        if (text.StartsWith("please ", StringComparison.OrdinalIgnoreCase)) text = text[7..].TrimStart();
        if (text.Equals("read", StringComparison.OrdinalIgnoreCase))
            return new("read_knowledge", "queued", 1, 0, "knowledge_reads", false);
        if (!text.StartsWith("read ", StringComparison.OrdinalIgnoreCase)) return null;
        var subject = text[5..].Trim();
        if (subject.StartsWith("a ", StringComparison.OrdinalIgnoreCase)) subject = subject[2..];
        else if (subject.StartsWith("my ", StringComparison.OrdinalIgnoreCase)) subject = subject[3..];
        var completeSubject = subject;
        string? kind = null;
        foreach (var (name, value) in new[] { ("field record", "field_record"), ("written record", "field_record"),
                     ("record", "field_record"), ("field map", "field_map"), ("map", "field_map"), ("book", "book") })
        {
            if (!subject.Equals(name, StringComparison.OrdinalIgnoreCase) && !subject.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) continue;
            kind = value;
            subject = subject[name.Length..].Trim();
            break;
        }
        string? target = null;
        if (subject.Length != 0)
        {
            var matches = knowledge.Artifacts.Where(item => (kind is null || item.Kind == kind) &&
                (item.Id == subject || item.LotId == subject || (item.Title.Equals(subject, StringComparison.OrdinalIgnoreCase) || item.Title.Equals(completeSubject, StringComparison.OrdinalIgnoreCase)))).ToArray();
            if (matches.Length != 1) return null;
            target = matches[0].Id;
        }
        else if (kind is null) return null;
        return new("read_knowledge", "queued", 1, 0, "knowledge_reads", false, TargetItemKind: kind)
        { TargetKnowledgeArtifactId = target };
    }

    private AgentKnowledgeArtifact? KnowledgeReadOrderArtifact(OwnerQueuedInstruction instruction) =>
        instruction.Order!.TargetKnowledgeArtifactId is { } id
            ? knowledge.Artifacts.FirstOrDefault(item => item.Id == id)
            : HeldKnowledgeArtifacts(instruction.TargetInhabitantId).FirstOrDefault(item =>
                (instruction.Order.TargetItemKind is null || item.Kind == instruction.Order.TargetItemKind) &&
                item.Facts.Any(fact => !KnowsMapFact(instruction.TargetInhabitantId, fact.Position)));

    private string? KnowledgeReadOrderBlocker(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor)) return "Only an adult or elder resident can read this written item.";
        if (KnowledgeWritingFor(actor) is not null) return "Cancel this read order to finish the current writing work first.";
        var artifact = KnowledgeReadOrderArtifact(instruction);
        if (artifact is null) return instruction.Order!.TargetKnowledgeArtifactId is null
            ? "Waiting for a personally held written item with something new to read."
            : "The named written item is no longer available.";
        if (!HeldKnowledgeArtifacts(actor).Any(item => item.Id == artifact.Id))
            return "The named written item must be personally owned, carried and available to read.";
        if (knowledge.Facts.Count(fact => fact.OwnerId == actor) >= AgentKnowledgeRules.MaximumFactsPerAgent)
            return "The agent's map-knowledge ledger is full.";
        if (!artifact.Facts.Any(fact => !KnowsMapFact(actor, fact.Position)))
            return "The agent already knows every site written in this item.";
        return null;
    }

    private CognitionCandidate? KnowledgeReadOrderCandidate(OwnerQueuedInstruction instruction) =>
        KnowledgeReadOrderBlocker(instruction) is null && KnowledgeReadOrderArtifact(instruction) is { } artifact
            ? new("read_knowledge", $"Read {artifact.Title}; learn only its actual written sites.", 0, artifact.Id) : null;

    private void ExecuteKnowledgeReadOrder(OwnerQueuedInstruction instruction)
    {
        if (KnowledgeReadOrderBlocker(instruction) is { } blocker)
        {
            SetOrderStatus(instruction, "blocked", blocker);
            return;
        }
        var artifact = KnowledgeReadOrderArtifact(instruction)!;
        var actor = instruction.TargetInhabitantId;
        var before = knowledge.Facts.Where(fact => fact.OwnerId == actor).Select(fact => fact.Position).ToHashSet();
        var learned = ReadIfKnowledgeArtifact(artifact.LotId, artifact.CreatorId, actor);
        if (learned <= 0)
        {
            SetOrderStatus(instruction, "blocked", "The written item could not add any new knowledge.");
            return;
        }
        var sites = knowledge.Facts.Where(fact => fact.OwnerId == actor && !before.Contains(fact.Position) &&
                fact.Acquisition == "read" && fact.SourceArtifactId == artifact.Id && fact.LearnedTick == WorldTick)
            .Select(fact => fact.Position).OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        if (sites.Length != learned) throw new InvalidDataException("The native read effect lost its learned sites.");
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        current = current with
        {
            Order = current.Order! with
            { TargetKnowledgeArtifactId = artifact.Id, KnowledgeReadCompletion = new(WorldTick, sites) }
        };
        instructionsByIdempotency[current.IdempotencyKey] = current;
        CreditOrderEffect(current, KnowledgeReadOrderEffectId(current), 1);
    }

    private static string KnowledgeReadOrderEffectId(OwnerQueuedInstruction instruction)
    {
        var completion = instruction.Order!.KnowledgeReadCompletion!;
        return "read-order:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{instruction.InstructionId}|{instruction.TargetInhabitantId}|{instruction.Order.TargetKnowledgeArtifactId}|{completion.WorldTick}|" +
            string.Join(';', completion.LearnedSites.Select(point => $"{point.X},{point.Y}")))));
    }

    private static bool IsValidKnowledgeReadOrderShape(OwnerInstructionOrder order, OwnerQueuedInstruction instruction, long worldTick) =>
        order.TargetItemKind is null or "field_record" or "field_map" or "book" &&
        order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null && order.TargetPosition is null &&
        order.RequestedUnits == 1 && order.CompletedUnits is >= 0 and <= 1 && !order.RepeatUntilCancelled && !order.QuantityIsExplicit &&
        order.ProgressUnit == "knowledge_reads" && order.Status != "not_understood" &&
        (order.Status == "finished") == (order.CompletedUnits == 1) &&
        (order.TargetKnowledgeArtifactId is null || IsKnowledgeArtifactOrderId(order.TargetKnowledgeArtifactId)) &&
        (order.CompletedUnits == 0 ? order.KnowledgeReadCompletion is null && order.LastEffectId is null :
            order.TargetKnowledgeArtifactId is not null && order.KnowledgeReadCompletion is { } completion &&
            completion.WorldTick >= instruction.SubmittedTick && completion.WorldTick <= worldTick &&
            completion.LearnedSites is { Count: >= 1 and <= AgentKnowledgeRules.MaximumFactsPerArtifact } &&
            completion.LearnedSites.All(point => point is { X: >= -10_000_000 and <= 10_000_000, Y: >= -10_000_000 and <= 10_000_000 }) &&
            completion.LearnedSites.Distinct().Count() == completion.LearnedSites.Count &&
            completion.LearnedSites.SequenceEqual(completion.LearnedSites.OrderBy(point => point.Y).ThenBy(point => point.X)) &&
            order.LastEffectId == KnowledgeReadOrderEffectId(instruction));

    private static bool IsKnowledgeArtifactOrderId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id == id.Trim() && !id.Any(char.IsControl);

    private static void ValidateKnowledgeReadOrderBindings(IEnumerable<OwnerQueuedInstruction> instructions, PrivateWorldKnowledgeState knowledge)
    {
        var readingOrders = instructions.Where(instruction => instruction.Order is
            { Action: "read_knowledge", TargetKnowledgeArtifactId: not null }).ToArray();
        if (readingOrders.Length == 0) return;
        var artifacts = knowledge.Artifacts.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var facts = knowledge.Facts.ToDictionary(item => (item.OwnerId, item.Position));
        foreach (var instruction in readingOrders)
        {
            if (instruction.Order is not { Action: "read_knowledge", TargetKnowledgeArtifactId: { } id } order) continue;
            artifacts.TryGetValue(id, out var artifact);
            if (artifact is not null && order.TargetItemKind is { } kind && kind != artifact.Kind)
                throw new InvalidDataException("A reading order names another kind of written item.");
            if (order.KnowledgeReadCompletion is not { } completion) continue;
            foreach (var site in completion.LearnedSites)
            {
                var written = artifact?.Facts.FirstOrDefault(fact => fact.Position == site);
                if (!facts.TryGetValue((instruction.TargetInhabitantId, site), out var learned) || learned.LearnedTick < completion.WorldTick ||
                    artifact is not null && written is null || learned.LearnedTick == completion.WorldTick &&
                    (written is null || learned.Id != KnowledgeFactId(instruction.TargetInhabitantId, site) ||
                     learned.Acquisition != "read" || learned.SourceArtifactId != id || learned.SourceAgentId != artifact!.CreatorId ||
                     !AgentKnowledgeRules.SameDiscovery(learned, written)))
                    throw new InvalidDataException("A reading order does not match its actual learned sites and provenance.");
            }
        }
    }
}
