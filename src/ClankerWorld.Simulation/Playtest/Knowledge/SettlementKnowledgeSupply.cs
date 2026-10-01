using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record KnowledgeSupplyNeed(PlacedBuilding House, string Kind, int Missing, InventoryLot Source);

    private KnowledgeSupplyNeed? NeededKnowledgeSupply(string actor)
    {
        if (HouseForHousehold(HouseholdFor(actor)) is not { } house || StorageRoom(house.InstanceId) == 0 ||
            !knowledge.Facts.Any(fact => fact.OwnerId == actor) ||
            knowledge.Artifacts.Count(item => item.CreatorId == actor) >= AgentKnowledgeRules.MaximumArtifactsPerCreator)
            return null;
        var wantsBook = !knowledge.Artifacts.Any(item => item.CreatorId == actor && item.Kind == "book");
        var copy = AccessibleKnowledgeArtifacts(actor, requirePresence: false).FirstOrDefault(item =>
            item.CreatorId != actor && item.Facts.All(fact => KnowsMapFact(actor, fact.Position)) &&
            !knowledge.Artifacts.Any(existing => existing.CreatorId == actor && existing.CopiedFromArtifactId == item.Id));
        var unrecorded = knowledge.Facts.Any(fact => fact.OwnerId == actor && !knowledge.Artifacts.Any(item =>
            item.CreatorId == actor && item.Facts.Any(written => written.Position == fact.Position)));
        if (!wantsBook && !unrecorded && copy is null) return null;
        var inventory = society.Checkpoint.Inventory;
        foreach (var cost in WritingCosts(wantsBook ? "book" : copy?.Kind ?? "field_map"))
        {
            var stocked = inventory.Lots.Where(lot => lot.OwnerId == house.HouseholdId &&
                lot.StorageBuildingId == house.InstanceId && lot.ItemKind == cost.ResourceId).Sum(AvailableLotQuantity);
            var missing = cost.Amount - stocked;
            if (missing <= 0) continue;
            var source = inventory.Lots.Where(lot => lot.ItemKind == cost.ResourceId && lot.ContainerLotId is null &&
                    lot.GroundPosition is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
                    (lot.OwnerId == actor && lot.StorageBuildingId is null ||
                     lot.OwnerId == house.HouseholdId && lot.StorageBuildingId != house.InstanceId &&
                     CarryingRoom(actor) > 0 && CanReachSharedItem(actor, lot)))
                .OrderBy(lot => lot.OwnerId == actor ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            if (source is not null) return new(house, cost.ResourceId, missing, source);
        }
        return null;
    }

    private void AddKnowledgeSupplyCandidate(List<CognitionCandidate> candidates, string actor)
    {
        if (NeededKnowledgeSupply(actor) is not { } need) return;
        candidates.Add(new(KnowledgeSupplyPrefix + need.Kind,
            $"Carry {need.Kind} into the household House to write learned sites with actual supplies.", 25, need.House.InstanceId));
    }

    private void SupplyKnowledgeWriting(string actor, PlaytestInhabitantState person, string kind)
    {
        if (!AdultResident(actor) || NeededKnowledgeSupply(actor) is not { } need || need.Kind != kind) return;
        var source = need.Source;
        if (source.OwnerId == actor)
        {
            if (person.Position != need.House.Position)
            {
                MoveToward(actor, person, need.House.Position, "knowledge_supply", 0);
                return;
            }
            var quantity = Math.Min(StorageRoom(need.House.InstanceId), Math.Min(need.Missing, AvailableLotQuantity(source)));
            if (quantity == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"knowledge-supply:{WorldTick}:{actor}", actor, need.House.HouseholdId!, source.Id, quantity,
                "knowledge_supplies_delivered", need.House.InstanceId));
            AppendEvent("knowledge_supplies_delivered", $"{actor}|{kind}|{quantity}|{need.House.InstanceId}");
            return;
        }
        var location = HouseholdStockPosition(source);
        var range = HouseholdStockInteractionRange(source);
        if (!IsWithinInteractionRange(person.Position, location, range))
        {
            MoveToward(actor, person, location, "knowledge_supply", range);
            return;
        }
        var amount = Math.Min(CarryingRoom(actor), Math.Min(need.Missing, AvailableLotQuantity(source)));
        if (amount == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"knowledge-supply-pickup:{WorldTick}:{actor}", source.OwnerId, actor, source.Id, amount,
            "knowledge_supplies_collected", destinationDeliveryBuildingId: need.House.InstanceId));
    }
}
