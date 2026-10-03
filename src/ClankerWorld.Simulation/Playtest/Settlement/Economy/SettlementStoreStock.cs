using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // Trial shelf target and load size. Goods above this target stay at their source.
    private const int StoreShelfTarget = 8;
    private sealed record StoreStockLoad(PlacedBuilding Store, InventoryLot Goods, int Quantity);

    private StoreStockLoad? NextStoreLoad(string actor)
    {
        if (!AdultResident(actor) || CarriedHouseDelivery(actor) is not null ||
            inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseholdBuildingWithTag(householdId, "store") is not { } store)
            return null;
        var inventory = society.Checkpoint.Inventory;
        var room = RemainingDeliveryRoom(inventory, store.InstanceId);
        if (room == 0) return null;
        var protectedToolIds = BestUsableToolIds(inventory, actor);
        foreach (var lot in inventory.Lots.Where(lot => IsLooseBusinessLot(lot) &&
                     BusinessRules.MaySell("store", lot.ItemKind) && lot.StorageBuildingId != store.InstanceId &&
                     // Own carried goods, or household stock nobody is carrying.
                     (lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) ||
                      lot.OwnerId == householdId && lot.CarrierId is null &&
                         (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                             building.InstanceId == lot.StorageBuildingId && building.HouseholdId == householdId))) &&
                     !protectedToolIds.Contains(lot.Id) &&
                     !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id))
                     .OrderBy(lot => lot.OwnerId == actor ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal))
        {
            var reserve = IsEdibleFood(lot.ItemKind) ? lot.OwnerId == actor ? 2 :
                2 * society.Checkpoint.GetHousehold(householdId).MemberIds.Count(id => inhabitants.ContainsKey(id)) : 0;
            var surplus = Math.Max(0, (lot.OwnerId == actor ? SpareCarriedQuantity(actor, lot) : AvailableLotQuantity(lot)) - reserve);
            var shelf = inventory.Lots.Where(stock => stock.StorageBuildingId == store.InstanceId &&
                stock.ItemKind == lot.ItemKind).Sum(stock => stock.Quantity);
            var quantity = Math.Min(Math.Min(surplus, StoreShelfTarget - shelf), Math.Min(HouseHaulLoadQuantity, room));
            if (lot.OwnerId != actor) quantity = Math.Min(quantity, FreeCarryCapacity(actor));
            if (quantity <= 0) continue;
            var source = lot.OwnerId == actor ? inhabitants[actor].Position : HouseholdStockPosition(lot);
            var range = lot.OwnerId == actor ? 0 : HouseholdStockInteractionRange(lot);
            if (!IsWithinInteractionRange(inhabitants[actor].Position, source, range) &&
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, source, range).Count == 0 ||
                FindUnoccupiedRoute(actor, source, store.Position, 0).Count == 0 && source != store.Position)
                continue;
            return new(store, lot, quantity);
        }
        return null;
    }

    private void AddStoreStockCandidate(List<CognitionCandidate> candidates, string actor)
    {
        if (NextStoreLoad(actor) is { } load)
            candidates.Add(new("business_stock_store",
                $"Carry {load.Quantity} {load.Goods.ItemKind} into the household Store before offering it for sale.",
                38, load.Store.InstanceId));
    }

    private void StockStore(string actor, PlaytestInhabitantState state)
    {
        if (NextStoreLoad(actor) is not { } load) return;
        var householdId = load.Store.HouseholdId!;
        if (load.Goods.OwnerId == actor)
        {
            if (state.Position != load.Store.Position)
            {
                MoveToward(actor, state, load.Store.Position, "store_stock", 0);
                return;
            }
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"store-stock:{WorldTick}:{actor}", actor, householdId, load.Goods.Id, load.Quantity,
                "store_stock_delivered", destinationStorageBuildingId: load.Store.InstanceId));
            AppendEvent("store_stock_delivered", $"{actor}:{load.Goods.Id}:{load.Quantity}:{load.Store.InstanceId}");
            return;
        }
        var source = HouseholdStockPosition(load.Goods);
        var range = HouseholdStockInteractionRange(load.Goods);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "store_stock", range);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"store-pickup:{WorldTick}:{actor}", householdId, actor, load.Goods.Id, load.Quantity,
            "store_stock_collected", destinationDeliveryBuildingId: load.Store.InstanceId));
        AppendEvent("store_stock_collected", $"{actor}:{load.Goods.Id}:{load.Quantity}:{load.Store.InstanceId}");
    }
}
