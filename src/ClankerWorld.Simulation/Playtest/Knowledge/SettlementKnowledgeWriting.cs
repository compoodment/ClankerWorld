using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string KnowledgeWritePrefix = "knowledge_write:";
    private const string KnowledgeCopyPrefix = "knowledge_copy:";
    private const string KnowledgeReadPrefix = "knowledge_read:";

    private AgentKnowledgeWritingProject? KnowledgeWritingFor(string actor) =>
        knowledge.WritingProjects.SingleOrDefault(project => project.ActorId == actor);

    private bool MayMakeKnowledgeArtifact(string actor) => inhabitants.ContainsKey(actor) && AdultResident(actor) &&
        knowledge.Artifacts.Count < AgentKnowledgeRules.MaximumArtifactsInWorld &&
        knowledge.Artifacts.Count(item => item.CreatorId == actor) < AgentKnowledgeRules.MaximumArtifactsPerCreator;

    private AgentKnowledgeFact[] FactsToWrite(string actor, string kind, AgentKnowledgeArtifact? source = null)
    {
        var learned = knowledge.Facts.Where(fact => fact.OwnerId == actor)
            .OrderByDescending(fact => fact.LearnedTick).ThenBy(fact => fact.Id, StringComparer.Ordinal).ToArray();
        if (source is not null)
        {
            var copied = source.Facts.Select(fact => learned.FirstOrDefault(item => AgentKnowledgeRules.SameDiscovery(item, fact))).ToArray();
            return copied.Any(fact => fact is null) ? [] : copied.Cast<AgentKnowledgeFact>().ToArray();
        }
        return learned.Take(kind == "field_record" ? 1 : AgentKnowledgeRules.MaximumFactsPerArtifact).ToArray();
    }

    private bool AlreadyWrote(string actor, string kind, AgentKnowledgeFact[] facts) =>
        knowledge.Artifacts.Any(artifact => artifact.CreatorId == actor && artifact.Kind == kind &&
            artifact.Facts.Count == facts.Length && artifact.Facts.All(fact => facts.Any(other => AgentKnowledgeRules.SameDiscovery(fact, other))));

    private IEnumerable<InventoryLot> CarriedWritingMaterials(string actor, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == kind && PersonalEquipmentRules.IsCarried(lot, actor) &&
            lot.ContainerLotId is null && lot.DeliveryBuildingId is null && lot.ConditionBasisPoints > 0 &&
            lot.FreshnessBasisPoints > 0 && AvailableLotQuantity(lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal);

    private bool CanSupplyWriting(string actor, string kind)
    {
        var missing = AgentKnowledgeRules.WritingMaterials(kind).Select(input =>
            (input.Kind, Quantity: Math.Max(0, input.Quantity - CarriedWritingMaterials(actor, input.Kind).Sum(AvailableLotQuantity)))).ToArray();
        return missing.Sum(input => input.Quantity) <= FreeCarryCapacity(actor) &&
            missing.All(input => input.Quantity == 0 || SharedItem(input.Kind, actor) is not null);
    }

    private void AddKnowledgeWritingCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (!AdultResident(actor) || NeedsUrgentFood(person) || NeedsUrgentWarmth(person)) return;
        if (KnowledgeWritingFor(actor) is { } project)
        {
            var label = $"Continue writing your {project.Kind.Replace('_', ' ')}: {project.WorkDone}/{project.WorkRequired} work, with real materials reserved.";
            candidates.Add(new("knowledge_continue", label, 54));
            candidates.Add(new(project.CandidateId, label, 54));
            return;
        }
        foreach (var artifact in HeldKnowledgeArtifacts(actor))
        {
            if (knowledge.Facts.Count(fact => fact.OwnerId == actor) < AgentKnowledgeRules.MaximumFactsPerAgent &&
                artifact.Facts.Any(fact => !KnowsMapFact(actor, fact.Position)))
                candidates.Add(new(KnowledgeReadPrefix + artifact.Id, $"Read {artifact.Title} and learn only the sites actually written in it.", 53));
            if (!MayMakeKnowledgeArtifact(actor)) continue;
            var facts = FactsToWrite(actor, artifact.Kind, artifact);
            if (facts.Length > 0 && !AlreadyWrote(actor, artifact.Kind, facts) && CanSupplyWriting(actor, artifact.Kind))
                candidates.Add(new(KnowledgeCopyPrefix + artifact.Id,
                    $"Copy {artifact.Title} using real paper{(artifact.Kind == "book" ? " and a cloth cover" : "")}; keep its original discovery sources.", 58));
        }
        if (!MayMakeKnowledgeArtifact(actor)) return;
        foreach (var kind in new[] { "field_record", "field_map", "book" })
        {
            var facts = FactsToWrite(actor, kind);
            if (facts.Length == 0 || AlreadyWrote(actor, kind, facts) || !CanSupplyWriting(actor, kind)) continue;
            var materials = string.Join(" and ", AgentKnowledgeRules.WritingMaterials(kind).Select(input => $"{input.Quantity} {input.Kind}"));
            candidates.Add(new(KnowledgeWritePrefix + kind,
                $"Write a {kind.Replace('_', ' ')} containing {facts.Length} personally learned site(s), using {materials} and {AgentKnowledgeRules.WritingWork(kind)} work (trial amounts).", 56));
        }
    }

    private void ApplyKnowledgeWritingCandidate(string actor, PlaytestInhabitantState person, string candidate)
    {
        if (!AdultResident(actor) || NeedsUrgentFood(person) || NeedsUrgentWarmth(person)) return;
        if (candidate.StartsWith(KnowledgeReadPrefix, StringComparison.Ordinal))
        {
            if (HeldKnowledgeArtifacts(actor).FirstOrDefault(item => item.Id == candidate[KnowledgeReadPrefix.Length..]) is { } reading)
                ReadIfKnowledgeArtifact(reading.LotId, reading.CreatorId, actor);
            return;
        }
        if (KnowledgeWritingFor(actor) is { } existing)
        {
            if (candidate == "knowledge_continue" || candidate == existing.CandidateId) ContinueKnowledgeWriting(existing);
            return;
        }
        if (!MayMakeKnowledgeArtifact(actor)) return;
        AgentKnowledgeArtifact? source = null;
        string kind;
        if (candidate.StartsWith(KnowledgeCopyPrefix, StringComparison.Ordinal))
        {
            source = HeldKnowledgeArtifacts(actor).FirstOrDefault(item => item.Id == candidate[KnowledgeCopyPrefix.Length..]);
            if (source is null) return;
            kind = source.Kind;
        }
        else if (candidate.StartsWith(KnowledgeWritePrefix, StringComparison.Ordinal)) kind = candidate[KnowledgeWritePrefix.Length..];
        else return;
        if (!AgentKnowledgeRules.IsArtifactKind(kind)) return;
        var facts = FactsToWrite(actor, kind, source);
        if (facts.Length == 0 || AlreadyWrote(actor, kind, facts) || !CanSupplyWriting(actor, kind)) return;
        foreach (var input in AgentKnowledgeRules.WritingMaterials(kind))
        {
            var missing = input.Quantity - CarriedWritingMaterials(actor, input.Kind).Sum(AvailableLotQuantity);
            if (missing <= 0) continue;
            CollectKnowledgeMaterial(actor, person, input.Kind, missing);
            return;
        }
        var id = "knowledge-work-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{actor}|{WorldTick}|{society.Checkpoint.Inventory.Reservations.Count}")));
        var materials = new List<AgentKnowledgeMaterial>();
        var inventory = society.Checkpoint.Inventory;
        foreach (var input in AgentKnowledgeRules.WritingMaterials(kind))
        {
            var remaining = input.Quantity;
            foreach (var lot in CarriedWritingMaterials(actor, input.Kind))
            {
                var quantity = Math.Min(remaining, PersonalEquipmentRules.AvailableQuantity(inventory, lot));
                if (quantity <= 0) continue;
                var reservationId = $"{id}:{materials.Count}";
                inventory = InventoryFixture.Reserve(inventory, reservationId, actor, lot.Id, quantity,
                    AgentKnowledgeRules.MaterialPurpose(id, input.Kind), long.MaxValue);
                materials.Add(new(reservationId, lot.Id, input.Kind, quantity));
                remaining -= quantity;
                if (remaining == 0) break;
            }
            if (remaining != 0) return;
        }
        ApplyInventoryTransition(_ => inventory);
        knowledge = knowledge with
        {
            WritingProjects = knowledge.WritingProjects.Append(new AgentKnowledgeWritingProject(
                id, actor, kind, candidate, source?.Id, facts, WorldTick, WorldTick, 0, materials)).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_knowledge_writing_started", $"{actor}|{kind}|{id}");
    }

    private void CollectKnowledgeMaterial(string actor, PlaytestInhabitantState person, string kind, int missing)
    {
        var item = SharedItem(kind, actor);
        if (item is null || item.DeliveryBuildingId is not null || item.ConditionBasisPoints <= 0 || item.FreshnessBasisPoints <= 0) return;
        var quantity = Math.Min(missing, Math.Min(FreeCarryCapacity(actor), AvailableLotQuantity(item)));
        if (quantity <= 0) return;
        var position = HouseholdStockPosition(item);
        var range = HouseholdStockInteractionRange(item);
        if (!IsWithinInteractionRange(person.Position, position, range))
        {
            MoveToward(actor, person, position, "knowledge_materials", range);
            return;
        }
        // SharedItem resolves household/Town permission at pickup time; physical possession alone grants none.
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"knowledge-material:{WorldTick}:{CognitionDiscovererId(actor)}:{kind}", item.OwnerId, actor, item.Id, quantity,
            "knowledge_material_collected"));
        AppendEvent("agent_knowledge_material_collected", $"{actor}|{kind}|{quantity}");
    }

    private bool HasLiveWritingInputs(AgentKnowledgeWritingProject project) => project.Materials.All(input =>
        society.Checkpoint.Inventory.Reservations.FirstOrDefault(item => item.Id == input.ReservationId) is
            { State: InventoryReservationState.Reserved } reservation && reservation.OwnerId == project.ActorId &&
        society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == input.LotId) is { } lot &&
        lot.OwnerId == project.ActorId && lot.ItemKind == input.ItemKind && lot.Quantity >= input.Quantity &&
        lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 && lot.ContainerLotId is null &&
        lot.DeliveryBuildingId is null && PersonalEquipmentRules.IsCarried(lot, project.ActorId));

    private bool HasWritingSource(AgentKnowledgeWritingProject project) => project.SourceArtifactId is null ||
        HeldKnowledgeArtifacts(project.ActorId).Any(item => item.Id == project.SourceArtifactId);

    private void ContinueKnowledgeWriting(AgentKnowledgeWritingProject project)
    {
        var actor = project.ActorId;
        if (!HasLiveWritingInputs(project) || !HasWritingSource(project))
        {
            CancelKnowledgeWriting(project, "materials_or_source_unavailable");
            return;
        }
        var person = inhabitants[actor];
        if (project.LastWorkedTick >= WorldTick || !SettlementIllnessRules.AllowsWork(actor, WorldTick, person.Survival?.IllnessBasisPoints ?? 0)) return;
        var work = project.WorkDone + 1;
        if (work < project.WorkRequired)
        {
            ReplaceKnowledgeWriting(project with { WorkDone = work, LastWorkedTick = WorldTick });
            return;
        }
        if (!MayMakeKnowledgeArtifact(actor) || FreeCarryCapacity(actor) + project.Materials.Sum(input => input.Quantity) < 1) return;
        var sequence = knowledge.Artifacts.Count + 1;
        var artifactId = $"knowledge-artifact-{sequence:D6}";
        var lotId = $"knowledge-lot-{sequence:D6}";
        var title = (project.Kind == "book" ? "Book" : project.Kind == "field_map" ? "Field map" : "Field record") +
            $" · {project.Facts.Count} site{(project.Facts.Count == 1 ? "" : "s")}";
        var inventory = society.Checkpoint.Inventory;
        foreach (var input in project.Materials) inventory = InventoryFixture.ConsumeReservation(inventory, input.ReservationId);
        inventory = InventoryFixture.AddLot(inventory, lotId, project.Kind, actor, 1, WorldTick);
        ApplyInventoryTransition(_ => inventory);
        knowledge = knowledge with
        {
            WritingProjects = knowledge.WritingProjects.Where(item => item.Id != project.Id).ToArray(),
            Artifacts = knowledge.Artifacts.Append(new AgentKnowledgeArtifact(
                artifactId, actor, lotId, project.Kind, title, WorldTick, project.Facts)
            {
                WritingProjectId = project.Id, Materials = project.Materials, SourceArtifactId = project.SourceArtifactId,
            }).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_knowledge_artifact_created", $"{actor}|{artifactId}|{project.Kind}|{project.Facts.Count}");
    }

    private void ReplaceKnowledgeWriting(AgentKnowledgeWritingProject project)
    {
        knowledge = knowledge with { WritingProjects = knowledge.WritingProjects.Select(item => item.Id == project.Id ? project : item).ToArray() };
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private void CancelKnowledgeWriting(AgentKnowledgeWritingProject project, string reason)
    {
        ApplyInventoryTransition(inventory => project.Materials.Aggregate(inventory,
            (current, input) => current.Reservations.Any(item => item.Id == input.ReservationId)
                ? InventoryFixture.ReleaseReservation(current, input.ReservationId, "knowledge_writing_cancelled") : current));
        knowledge = knowledge with { WritingProjects = knowledge.WritingProjects.Where(item => item.Id != project.Id).ToArray() };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_knowledge_writing_cancelled", $"{project.ActorId}|{project.Kind}|{reason}");
    }

    private void MaintainKnowledgeWriting()
    {
        foreach (var project in knowledge.WritingProjects.ToArray())
            if (!MayMakeKnowledgeArtifact(project.ActorId) ||
                !HasLiveWritingInputs(project) || !HasWritingSource(project))
                CancelKnowledgeWriting(project, "materials_or_source_unavailable");
    }
}
