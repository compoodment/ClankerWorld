using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed record KnowledgeWritingResult(bool Applied, string? ArtifactId = null, string? Failure = null);

public sealed partial class PrivateWorldRuntime
{
    private const string KnowledgeWritePrefix = "knowledge_write:";
    private const string KnowledgeReadPrefix = "knowledge_read:";
    private const string KnowledgeCopyPrefix = "knowledge_copy:";
    private const string KnowledgeSupplyPrefix = "knowledge_supply:";

    private static ContentQuantity[] WritingCosts(string kind) => kind == "book"
        ? [new("paper", 2), new("cloth", 1)] : [new("paper", 1)]; // Trial costs, no invisible writing supplies.

    public KnowledgeWritingResult WriteKnowledgeArtifact(string actor, string kind, IReadOnlyList<GridPoint> positions)
    {
        gate.Wait();
        try { return WriteKnowledgeArtifactCore(actor, kind, positions); }
        finally { gate.Release(); }
    }

    private KnowledgeWritingResult WriteKnowledgeArtifactCore(string actor, string kind, IReadOnlyList<GridPoint> positions,
        string? copiedFromArtifactId = null)
    {
        if (!AdultResident(actor) || kind is not ("field_map" or "field_record" or "book") || positions is null ||
            positions.Count is < 1 or > AgentKnowledgeRules.MaximumFactsPerArtifact || positions.Distinct().Count() != positions.Count ||
            kind == "field_record" && positions.Count != 1)
            return new(false, Failure: "An adult may write a bounded map, one-site record or book.");
        var facts = positions.Select(position => knowledge.Facts.FirstOrDefault(fact => fact.OwnerId == actor && fact.Position == position)).ToArray();
        if (facts.Any(fact => fact is null)) return new(false, Failure: "Write only sites this author has actually discovered or learned.");
        if (copiedFromArtifactId is { } sourceId)
        {
            var source = AccessibleKnowledgeArtifacts(actor).FirstOrDefault(artifact => artifact.Id == sourceId);
            if (source is null || source.Kind != kind || source.Facts.Count != positions.Count ||
                source.Facts.Any(fact => !positions.Contains(fact.Position)))
                return new(false, Failure: "Copy only the bounded contents of the actual source item.");
            facts = source.Facts.Select(fact => fact with
            {
                Id = KnowledgeFactId(actor, fact.Position),
                OwnerId = actor,
                LearnedTick = WorldTick,
                Acquisition = "read",
                SourceAgentId = source.CreatorId,
                SourceArtifactId = source.Id,
            }).Cast<AgentKnowledgeFact?>().ToArray();
        }
        var person = inhabitants[actor];
        var house = HouseForHousehold(HouseholdFor(actor));
        if (house is null || person.Position != house.Position)
            return new(false, Failure: "Bring the author to their household House to write with its supplies.");
        if (CarryingRoom(actor) < 1) return new(false, Failure: "The author needs room to carry the finished knowledge item.");
        var costs = WritingCosts(kind);
        if (!HasIngredientsAtBuilding(costs, house.HouseholdId!, house.InstanceId))
            return new(false, Failure: kind == "book" ? "The House needs two paper and one cloth for a book." : "The House needs one paper for a written map or record.");
        if (knowledge.Artifacts.Count(item => item.CreatorId == actor) >= AgentKnowledgeRules.MaximumArtifactsPerCreator ||
            knowledge.Artifacts.Count >= AgentKnowledgeRules.MaximumArtifactsInWorld)
            return new(false, Failure: "This world's bounded knowledge artifact ledger is full.");
        var sequence = knowledge.Artifacts.Count + 1;
        var artifactId = $"knowledge-artifact-{sequence:D6}";
        var lotId = $"knowledge-lot-{sequence:D6}";
        IReadOnlyList<string> reservations = [];
        ApplyInventoryTransition(inventory =>
        {
            var current = ReserveQuantities(inventory, costs, "knowledge-writing:" + artifactId,
                WorldTick, house.HouseholdId!, out reservations, house.InstanceId);
            foreach (var reservation in reservations) current = InventoryFixture.ConsumeReservation(current, reservation);
            return InventoryFixture.AddLot(current, lotId, kind, actor, 1, WorldTick);
        });
        var title = (kind switch { "book" => "Book", "field_record" => "Field record", _ => "Field map" }) + $" · {facts.Length} site(s)";
        knowledge = knowledge with
        {
            Artifacts = knowledge.Artifacts.Append(new AgentKnowledgeArtifact(artifactId, actor, lotId, kind,
                title, WorldTick, facts.Select(fact => fact!).ToArray(), house.InstanceId, reservations, copiedFromArtifactId)).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent(copiedFromArtifactId is null ? "agent_knowledge_artifact_created" : "agent_knowledge_artifact_copied",
            $"{actor}|{artifactId}|{kind}|{facts.Length}");
        return new(true, artifactId);
    }

    private AgentKnowledgeArtifact[] AccessibleKnowledgeArtifacts(string actor, bool requirePresence = true)
    {
        var person = inhabitants[actor];
        var ids = society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.ItemKind is ("field_map" or "field_record" or "book") && lot.ContainerLotId is null &&
                lot.GroundPosition is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) == 1 &&
                (lot.OwnerId == actor && lot.StorageBuildingId is null || lot.OwnerId == HouseholdFor(actor) &&
                    lot.StorageBuildingId is { } storage && worldSimulation.Buildings.Any(building => building.InstanceId == storage &&
                        building.HouseholdId == lot.OwnerId && (!requirePresence || building.Position == person.Position))))
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        return knowledge.Artifacts.Where(artifact => ids.Contains(artifact.LotId)).OrderBy(artifact => artifact.Id, StringComparer.Ordinal).ToArray();
    }

    public EquipmentChangeResult ReadKnowledgeArtifact(string actor, string artifactId)
    {
        gate.Wait();
        try { return ReadKnowledgeArtifactCore(actor, artifactId); }
        finally { gate.Release(); }
    }

    private EquipmentChangeResult ReadKnowledgeArtifactCore(string actor, string artifactId)
    {
        if (!AdultResident(actor) || AccessibleKnowledgeArtifacts(actor).FirstOrDefault(artifact => artifact.Id == artifactId) is not { } artifact)
            return new(false, "Carry the real knowledge item, or read your household's copy at its storage building.");
        var learned = LearnArtifactFacts(actor, artifact.CreatorId, artifact, "read");
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_knowledge_artifact_read", $"{artifact.CreatorId}|{actor}|{artifact.Id}|{learned}");
        return new(true);
    }

    public KnowledgeWritingResult CopyKnowledgeArtifact(string actor, string artifactId)
    {
        gate.Wait();
        try { return CopyKnowledgeArtifactCore(actor, artifactId); }
        finally { gate.Release(); }
    }

    private KnowledgeWritingResult CopyKnowledgeArtifactCore(string actor, string artifactId)
    {
        if (!AdultResident(actor) || AccessibleKnowledgeArtifacts(actor).FirstOrDefault(artifact => artifact.Id == artifactId) is not { } artifact)
            return new(false, Failure: "Bring the actual source copy to the author before copying it.");
        if (artifact.Facts.Any(fact => !KnowsMapFact(actor, fact.Position)))
            return new(false, Failure: "Read the actual source first; copying cannot invent knowledge.");
        return WriteKnowledgeArtifactCore(actor, artifact.Kind, artifact.Facts.Select(fact => fact.Position).ToArray(), artifact.Id);
    }

    private void AddPhysicalKnowledgeCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        var accessible = AccessibleKnowledgeArtifacts(actor, requirePresence: false);
        foreach (var artifact in accessible)
        {
            var lot = society.Checkpoint.Inventory.GetLot(artifact.LotId);
            if (lot.StorageBuildingId is { } storage &&
                worldSimulation.Buildings.Single(building => building.InstanceId == storage).Position is { } location &&
                person.Position != location && FindUnoccupiedRoute(actor, person.Position, location, 0).Count == 0) continue;
            if (artifact.Facts.Any(fact => !KnowsMapFact(actor, fact.Position)))
                candidates.Add(new(KnowledgeReadPrefix + artifact.Id, $"Read the sites actually written in {artifact.Title}.", 49));
            else if (CarryingRoom(actor) > 0 && artifact.CreatorId != actor && !knowledge.Artifacts.Any(copy => copy.CreatorId == actor && copy.CopiedFromArtifactId == artifact.Id) &&
                HouseForHousehold(HouseholdFor(actor)) is { } copyingHouse &&
                HasIngredientsAtBuilding(WritingCosts(artifact.Kind), copyingHouse.HouseholdId!, copyingHouse.InstanceId))
                candidates.Add(new(KnowledgeCopyPrefix + artifact.Id, $"Copy {artifact.Title} at the House with real paper, preserving its discoveries.", 53));
        }
        AddKnowledgeSupplyCandidate(candidates, actor);
        if (CarryingRoom(actor) == 0 || knowledge.Artifacts.Count(item => item.CreatorId == actor) >= AgentKnowledgeRules.MaximumArtifactsPerCreator ||
            HouseForHousehold(HouseholdFor(actor)) is not { } house) return;
        var unrecorded = knowledge.Facts.Where(fact => fact.OwnerId == actor && !knowledge.Artifacts.Any(artifact =>
                artifact.CreatorId == actor && artifact.Facts.Any(written => written.Position == fact.Position))).Take(AgentKnowledgeRules.MaximumFactsPerArtifact).ToArray();
        if (unrecorded.Length > 0 && HasIngredientsAtBuilding(WritingCosts("field_map"), house.HouseholdId!, house.InstanceId))
            candidates.Add(new(KnowledgeWritePrefix + (unrecorded.Length == 1 ? "field_record" : "field_map"),
                "Write your actual learned sites at the House with one paper, then carry the copy.", 50, house.InstanceId));
        if (knowledge.Facts.Any(fact => fact.OwnerId == actor) && !knowledge.Artifacts.Any(artifact => artifact.CreatorId == actor && artifact.Kind == "book") &&
            HasIngredientsAtBuilding(WritingCosts("book"), house.HouseholdId!, house.InstanceId))
            candidates.Add(new(KnowledgeWritePrefix + "book", "Bind a book of your learned sites at the House with paper and a cloth cover.", 54, house.InstanceId));
    }

    private void WriteOrCopyKnowledgeCandidate(string actor, PlaytestInhabitantState person, string candidateId)
    {
        if (HouseForHousehold(HouseholdFor(actor)) is not { } house) return;
        if (candidateId.StartsWith(KnowledgeCopyPrefix, StringComparison.Ordinal) &&
            AccessibleKnowledgeArtifacts(actor, requirePresence: false).FirstOrDefault(item =>
                item.Id == candidateId[KnowledgeCopyPrefix.Length..]) is { } source &&
            society.Checkpoint.Inventory.GetLot(source.LotId) is { StorageBuildingId: { } storage } lot && storage != house.InstanceId)
        {
            var sourceBuilding = worldSimulation.Buildings.Single(building => building.InstanceId == storage);
            if (person.Position != sourceBuilding.Position)
                MoveToward(actor, person, sourceBuilding.Position, "knowledge_copy_source", 0);
            else if (CarryingRoom(actor) > 0)
            {
                ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                    $"knowledge-copy-source:{WorldTick}:{actor}", lot.OwnerId, actor, lot.Id, 1, "knowledge_source_collected"));
                AppendEvent("knowledge_source_collected", $"{actor}|{source.Id}|{sourceBuilding.InstanceId}");
            }
            return;
        }
        if (person.Position != house.Position) { MoveToward(actor, person, house.Position, "knowledge_writing", 0); return; }
        if (candidateId.StartsWith(KnowledgeCopyPrefix, StringComparison.Ordinal))
        {
            CopyKnowledgeArtifactCore(actor, candidateId[KnowledgeCopyPrefix.Length..]);
            return;
        }
        var kind = candidateId[KnowledgeWritePrefix.Length..];
        var facts = knowledge.Facts.Where(fact => fact.OwnerId == actor && (kind == "book" || !knowledge.Artifacts.Any(artifact =>
                artifact.CreatorId == actor && artifact.Facts.Any(written => written.Position == fact.Position))))
            .Take(AgentKnowledgeRules.MaximumFactsPerArtifact).ToArray();
        WriteKnowledgeArtifactCore(actor, kind, facts.Select(fact => fact.Position).ToArray());
    }

    private void ReadKnowledgeCandidate(string actor, PlaytestInhabitantState person, string artifactId)
    {
        var artifact = AccessibleKnowledgeArtifacts(actor, requirePresence: false).FirstOrDefault(item => item.Id == artifactId);
        if (artifact is null) return;
        var lot = society.Checkpoint.Inventory.GetLot(artifact.LotId);
        if (lot.StorageBuildingId is { } storage && worldSimulation.Buildings.Single(building => building.InstanceId == storage).Position is { } point && person.Position != point)
        {
            MoveToward(actor, person, point, "knowledge_reading", 0);
            return;
        }
        ReadKnowledgeArtifactCore(actor, artifactId);
    }
}
