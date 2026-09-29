using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string KnowledgeSharePrefix = "knowledge_share:";

    private bool RecordKnowledgeFact(string actor, GridPoint position)
    {
        if (knowledge.Facts.Any(fact => fact.OwnerId == actor && fact.Position == position))
            return false;

        var ownedCount = knowledge.Facts.Count(fact => fact.OwnerId == actor);
        if (ownedCount >= AgentKnowledgeRules.MaximumFactsPerAgent)
            return false;

        if (map.TerrainKindAt(position) is not { } terrain)
            return false;
        var resourcesAtTile = map.Resources.Where(item => item.Position == position)
            .Select(item => item.Kind).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Take(AgentKnowledgeRules.MaximumResourceKindsPerFact).ToArray();
        var fact = new AgentKnowledgeFact(
            KnowledgeFactId(actor, position), actor, actor, position,
            terrain.ToString(), resourcesAtTile, WorldTick, "firsthand");
        knowledge = knowledge with
        {
            Facts = knowledge.Facts.Append(fact).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_knowledge_learned", $"{actor}|firsthand|1");
        return true;
    }

    private void CreateKnowledgeArtifact(string actor, IReadOnlyList<GridPoint> positions)
    {
        var facts = positions.Distinct()
            .Select(position => knowledge.Facts.FirstOrDefault(fact => fact.OwnerId == actor && fact.Position == position))
            .Where(fact => fact is not null)
            .Cast<AgentKnowledgeFact>()
            .Take(AgentKnowledgeRules.MaximumFactsPerArtifact).ToArray();
        if (facts.Length == 0)
            return;

        var createdByActor = knowledge.Artifacts.Count(item => item.CreatorId == actor);
        if (createdByActor >= AgentKnowledgeRules.MaximumArtifactsPerCreator ||
            knowledge.Artifacts.Count >= AgentKnowledgeRules.MaximumArtifactsInWorld)
        {
            AppendEvent("agent_knowledge_artifact_limited", $"{actor}|artifact_cap|{facts.Length}");
            return;
        }

        var sequence = knowledge.Artifacts.Count + 1;
        var kind = facts.Length == 1 ? "field_record" : "field_map";
        var artifactId = $"knowledge-artifact-{sequence:D6}";
        var lotId = $"knowledge-lot-{sequence:D6}";
        var title = facts.Length == 1 ? "Field record · 1 site" : $"Field map · {facts.Length} sites";
        ApplyInventoryTransition(inventory => InventoryFixture.AddLot(
            inventory, lotId, kind, actor, 1, WorldTick));
        knowledge = knowledge with
        {
            Artifacts = knowledge.Artifacts.Append(new AgentKnowledgeArtifact(
                artifactId, actor, lotId, kind, title, WorldTick, facts)).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_knowledge_artifact_created", $"{actor}|{artifactId}|{kind}|{facts.Length}");
    }

    private void AddKnowledgeCandidates(
        List<CognitionCandidate> candidates,
        string actor,
        PlaytestInhabitantState person)
    {
        if (NeedsUrgentWarmth(person))
            return;

        foreach (var artifact in HeldKnowledgeArtifacts(actor))
        {
            foreach (var target in inhabitants.Values
                .Where(item => item.InhabitantId != actor && AdultResident(item.InhabitantId) &&
                    IsWithinInteractionRange(person.Position, item.Position, 1))
                 .OrderBy(item => item.InhabitantId, StringComparer.Ordinal))
            {
                if (knowledge.Facts.Count(fact => fact.OwnerId == target.InhabitantId) >= AgentKnowledgeRules.MaximumFactsPerAgent ||
                    !artifact.Facts.Any(fact => !KnowsMapFact(target.InhabitantId, fact.Position)))
                    continue;
                var targetName = society.Checkpoint.GetInhabitant(target.InhabitantId).Name;
                candidates.Add(new CognitionCandidate(
                    KnowledgeSharePrefix + artifact.Id + "|" + target.InhabitantId,
                    $"Share {artifact.Title} with {targetName}; they can copy sites they do not already know, and you keep your copy.",
                    52));
            }
        }
    }

    private void ApplyKnowledgeShare(string actor, PlaytestInhabitantState person, string candidateId)
    {
        var encoded = candidateId[KnowledgeSharePrefix.Length..];
        var divider = encoded.LastIndexOf('|');
        if (divider <= 0 || divider == encoded.Length - 1)
            return;
        var artifactId = encoded[..divider];
        var recipientId = encoded[(divider + 1)..];
        if (!inhabitants.TryGetValue(recipientId, out var recipient) || !AdultResident(recipientId) ||
            !IsWithinInteractionRange(person.Position, recipient.Position, 1))
            return;
        var artifact = HeldKnowledgeArtifacts(actor).FirstOrDefault(item => item.Id == artifactId);
        if (artifact is null)
            return;
        var learned = LearnArtifactFacts(recipientId, actor, artifact, "shared");
        if (learned > 0)
        {
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("agent_knowledge_shared", $"{actor}|{recipientId}|{artifact.Id}|{learned}");
        }
    }

    private void ReadTradedKnowledge(string firstPartyId, string secondPartyId, string firstLotId, string secondLotId)
    {
        ReadIfKnowledgeArtifact(firstLotId, firstPartyId, secondPartyId);
        ReadIfKnowledgeArtifact(secondLotId, secondPartyId, firstPartyId);
    }

    private void ReadIfKnowledgeArtifact(string lotId, string sourceAgentId, string recipientId)
    {
        var artifact = knowledge.Artifacts.FirstOrDefault(item => item.LotId == lotId);
        if (artifact is null)
            return;
        var learned = LearnArtifactFacts(recipientId, sourceAgentId, artifact, "read");
        if (learned > 0)
        {
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("agent_knowledge_artifact_read", $"{sourceAgentId}|{recipientId}|{artifact.Id}|{learned}");
        }
    }

    private int LearnArtifactFacts(
        string recipientId,
        string sourceAgentId,
        AgentKnowledgeArtifact artifact,
        string acquisition)
    {
        var facts = knowledge.Facts.ToList();
        var learned = 0;
        foreach (var fact in artifact.Facts)
        {
            if (facts.Any(existing => existing.OwnerId == recipientId && existing.Position == fact.Position) ||
                facts.Count(existing => existing.OwnerId == recipientId) >= AgentKnowledgeRules.MaximumFactsPerAgent)
                continue;

            facts.Add(fact with
            {
                Id = KnowledgeFactId(recipientId, fact.Position),
                OwnerId = recipientId,
                LearnedTick = WorldTick,
                Acquisition = acquisition,
                SourceAgentId = sourceAgentId,
                SourceArtifactId = artifact.Id,
            });
            learned++;
        }
        if (learned > 0)
        {
            knowledge = knowledge with { Facts = facts.ToArray() };
        }
        return learned;
    }

    private bool KnowsMapFact(string agentId, GridPoint position) =>
        knowledge.Facts.Any(fact => fact.OwnerId == agentId && fact.Position == position);

    private CognitionKnowledgeFact[] KnownMapFactsForCognition(string agentId) =>
        knowledge.Facts.Where(fact => fact.OwnerId == agentId)
            .OrderByDescending(fact => fact.LearnedTick)
            .ThenBy(fact => fact.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(fact => new CognitionKnowledgeFact(
                fact.Position.X, fact.Position.Y, fact.Terrain, fact.ResourceKinds,
                fact.DiscovererId, fact.LearnedTick, fact.Acquisition))
            .ToArray();

    private AgentKnowledgeArtifact[] HeldKnowledgeArtifacts(string ownerId)
    {
        var heldLotIds = society.Checkpoint.Inventory.Lots
            .Where(item => item.OwnerId == ownerId && item.ItemKind is ("field_map" or "field_record"))
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return knowledge.Artifacts.Where(item => heldLotIds.Contains(item.LotId))
            .OrderBy(item => item.CreatedTick).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static string KnowledgeFactId(string ownerId, GridPoint position) =>
        $"knowledge-fact:{ownerId}:{position.X}:{position.Y}";
}
