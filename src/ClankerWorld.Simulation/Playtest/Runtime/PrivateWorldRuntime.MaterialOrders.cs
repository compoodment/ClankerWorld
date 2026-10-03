using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record MaterialGatherEffect(string LotId, string ItemKind, int Quantity, string ResourceId);

    private CognitionCandidate? MaterialOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        if (FreeCarryCapacity(actor) == 0) return null;
        var known = KnownMaterialOrderSources(instruction, person).ToArray();
        var toolCache = new Dictionary<(ToolFamily Family, int Tier), ToolDefinition?>();
        foreach (var source in known)
        {
            if (!CanGatherFromSource(actor, order.TargetMaterialKind!, source, toolCache) ||
                ProjectMaterialHarvest(actor, order.TargetMaterialKind!, source) is { } plan &&
                FreeCarryCapacity(actor) < plan.Quantity + plan.TreeSeedQuantity ||
                !MaterialOrderRouteIsOpen(actor, person.Position, source.Position))
                continue;
            return new CognitionCandidate("gather_material", "Gather the requested material using normal tools and carrying space.", 0, source.Id);
        }

        if (known.Length > 0 || !CanExploreForOrder(person))
            return null;
        if (MaterialOrderTarget(instruction) is { } target)
        {
            if (!map.Contains(target) || IsWithinInteractionRange(person.Position, target, ResourceInteractionRange) ||
                !MaterialOrderRouteIsOpen(actor, person.Position, target))
                return null;
            return new CognitionCandidate("inspect_material_site", "Travel to observe the material site named by this order.", 0);
        }
        if (order.TargetResourceId is not null) return null;
        return new CognitionCandidate("explore", "Explore nearby terrain to discover the requested material.", 0);
    }

    private IEnumerable<MapResource> KnownMaterialOrderSources(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order!;
        var knownPositions = knowledge.Facts.Where(fact => fact.OwnerId == instruction.TargetInhabitantId &&
                fact.ResourceKinds.Any(kind => kind == order.TargetMaterialKind ||
                    order.TargetMaterialKind == "wood" && kind == "construction"))
            .Select(fact => fact.Position).ToHashSet();
        return map.Resources.Where(source =>
                PrivateWorldInstructionOrderParser.MatchesMaterial(source, order.TargetMaterialKind!) &&
                resources.GetValueOrDefault(source.Id) == ResourceState.Available &&
                (order.TargetResourceId is null || source.Id == order.TargetResourceId) &&
                (order.TargetPosition is null || source.Position == order.TargetPosition) &&
                (knownPositions.Contains(source.Position) ||
                 IsWithinInteractionRange(person.Position, source.Position, ResourceInteractionRange)))
            .OrderBy(source => map.FootDistance(person.Position, source.Position))
            .ThenBy(source => source.Id, StringComparer.Ordinal);
    }

    private GridPoint? MaterialOrderTarget(OwnerQueuedInstruction instruction) =>
        instruction.Order!.TargetPosition ?? (instruction.Order.TargetResourceId is { } id
            ? map.Resources.SingleOrDefault(source => source.Id == id)?.Position : null);

    private bool MaterialOrderRouteIsOpen(string actor, GridPoint position, GridPoint target) =>
        IsWithinInteractionRange(position, target, ResourceInteractionRange) ||
        FindUnoccupiedRoute(actor, position, target, ResourceInteractionRange).Count > 0;

    private void ExecuteMaterialOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person, string candidate)
    {
        var actor = instruction.TargetInhabitantId;
        if (candidate == "inspect_material_site")
        {
            if (MaterialOrderTarget(instruction) is { } target && map.Contains(target))
            {
                MoveToward(actor, person, target, "owner_order_material_site", ResourceInteractionRange);
                var reached = inhabitants[actor].Position;
                if (reached != person.Position) RecordKnowledgeFact(actor, reached);
                if (IsWithinInteractionRange(reached, target, ResourceInteractionRange))
                    RecordKnowledgeFact(actor, target);
            }
            return;
        }

        // Recheck the selected site's tools, room and route at execution time.
        var candidateNow = MaterialOrderCandidateFor(instruction, person);
        var source = candidateNow?.Id == "gather_material"
            ? map.Resources.Single(item => item.Id == candidateNow.DestinationId) : null;
        if (source is null)
        {
            SetOrderStatus(instruction, "blocked", MaterialOrderBlockedReason(instruction, person));
            return;
        }
        if (IsWithinInteractionRange(person.Position, source.Position, ResourceInteractionRange))
            RecordKnowledgeFact(actor, source.Position);
        var effect = GatherProjectMaterial(actor, person, instruction.Order!.TargetMaterialKind!, source);
        var position = inhabitants[actor].Position;
        if (position != person.Position) RecordKnowledgeFact(actor, position);
        if (effect is not null)
            CreditOrderEffect(instruction, $"gather:{effect.LotId}",
                instruction.Order.ProgressUnit == "harvests" ? 1 : effect.Quantity);
    }

    private string MaterialOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AgePermitsCandidate(actor, "gather_material"))
            return "This agent is too young to gather these materials.";
        if (instruction.Order!.TargetPosition is { } target && !map.Contains(target))
            return "The requested tile is outside this world.";
        if (FreeCarryCapacity(actor) == 0)
            return "Carrying space is full; make room before gathering materials.";
        var sources = KnownMaterialOrderSources(instruction, person).ToArray();
        if (sources.Length > 0)
        {
            var toolCache = new Dictionary<(ToolFamily Family, int Tier), ToolDefinition?>();
            var usable = sources.Where(source => CanGatherFromSource(actor, instruction.Order.TargetMaterialKind!, source, toolCache)).ToArray();
            if (usable.Length == 0)
                return "A usable gathering tool is needed; carry one or make a shared one accessible.";
            if (usable.All(source => ProjectMaterialHarvest(actor, instruction.Order.TargetMaterialKind!, source) is { } plan &&
                    FreeCarryCapacity(actor) < plan.Quantity + plan.TreeSeedQuantity))
                return "Carrying space is too small for a whole gathering load; make room first.";
            return "No open walking route reaches a suitable material source right now.";
        }
        if (MaterialOrderTarget(instruction) is { } site && IsWithinInteractionRange(person.Position, site, ResourceInteractionRange))
            return "The requested site has no available matching material right now.";
        return !CanExploreForOrder(person)
            ? "The agent needs to be fed and warm before searching for materials."
            : "The requested material site is not reachable right now.";
    }
}
