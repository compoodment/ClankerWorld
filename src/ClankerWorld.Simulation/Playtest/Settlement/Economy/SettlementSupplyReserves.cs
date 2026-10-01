using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // Stock at a workstation is available to move only above its next needed
    // batch. Physical House writing uses the same rule as recipe production.
    private int HouseholdSupplySpareQuantity(InventoryLot lot, string destinationId)
    {
        if (lot.ContainerLotId is not null || lot.GroundPosition is not null || lot.DeliveryBuildingId is not null ||
            lot.CartId is not null || lot.AnimalId is not null) return 0;
        var available = AvailableLotQuantity(lot);
        if (available == 0 || lot.StorageBuildingId is not { } sourceId || sourceId == destinationId)
            return available;
        var source = worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == sourceId);
        if (source?.HouseholdId != lot.OwnerId) return 0;
        var reserve = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == source.DefinitionId &&
                NeedsRecipeOutput(recipe, lot.OwnerId)).SelectMany(recipe => recipe.Inputs)
            .Where(input => input.ResourceId == lot.ItemKind).Select(input => input.Amount).DefaultIfEmpty(0).Max();
        if (worldContent.Buildings.Any(definition => definition.CanonicalId == source.DefinitionId &&
                definition.Tags.Contains("house", StringComparer.Ordinal)))
            foreach (var actor in inhabitants.Keys.Where(actor => HouseholdFor(actor) == lot.OwnerId && AdultResident(actor)))
                if (WantedKnowledgeWritingCosts(actor) is { } costs)
                    reserve = Math.Max(reserve, costs.Where(cost => cost.ResourceId == lot.ItemKind)
                        .Select(cost => cost.Amount).DefaultIfEmpty(0).Max());
        var stocked = society.Checkpoint.Inventory.Lots.Where(stock => stock.OwnerId == lot.OwnerId &&
                stock.StorageBuildingId == sourceId && stock.ItemKind == lot.ItemKind && stock.ContainerLotId is null &&
                stock.GroundPosition is null && stock.DeliveryBuildingId is null).Sum(AvailableLotQuantity);
        return Math.Min(available, Math.Max(0, stocked - reserve));
    }

    private ContentQuantity[]? WantedKnowledgeWritingCosts(string actor)
    {
        if (!AdultResident(actor) || !knowledge.Facts.Any(fact => fact.OwnerId == actor) ||
            knowledge.Artifacts.Count(item => item.CreatorId == actor) >= AgentKnowledgeRules.MaximumArtifactsPerCreator ||
            knowledge.Artifacts.Count >= AgentKnowledgeRules.MaximumArtifactsInWorld) return null;
        if (!knowledge.Artifacts.Any(item => item.CreatorId == actor && item.Kind == "book")) return WritingCosts("book");
        var copy = AccessibleKnowledgeArtifacts(actor, requirePresence: false).FirstOrDefault(item =>
            item.CreatorId != actor && item.Facts.All(fact => KnowsMapFact(actor, fact.Position)) &&
            !knowledge.Artifacts.Any(existing => existing.CreatorId == actor && existing.CopiedFromArtifactId == item.Id));
        if (copy is not null) return WritingCosts(copy.Kind);
        return knowledge.Facts.Any(fact => fact.OwnerId == actor && !knowledge.Artifacts.Any(item =>
            item.CreatorId == actor && item.Facts.Any(written => written.Position == fact.Position)))
            ? WritingCosts("field_map") : null;
    }
}
