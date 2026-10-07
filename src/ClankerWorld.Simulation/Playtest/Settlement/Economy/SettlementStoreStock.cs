using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // Trial shelf target and load size. Goods above this target stay at their source.
    private const int StoreShelfTarget = 8;
    private sealed record StoreStockLoad(PlacedBuilding Store, InventoryLot Goods, int Quantity);

    private StoreStockLoad? NextStoreLoad(string actor, string? itemKind = null, string? buildingId = null,
        string? sourceLotId = null, bool allowOrdinaryProject = false)
    {
        if (!AdultResident(actor) || CarriedHouseDelivery(actor) is not null ||
            inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } project &&
                (!allowOrdinaryProject || !OrdinaryProjectCanYieldToOrder(project)) ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseholdBuildingWithTag(householdId, "store") is not { } store ||
            buildingId is not null && store.InstanceId != buildingId)
            return null;
        var inventory = society.Checkpoint.Inventory;
        var room = RemainingDeliveryRoom(inventory, store.InstanceId);
        if (room == 0) return null;
        var protectedToolIds = BestUsableToolIds(inventory, actor);
        var sources = inventory.Lots.Where(lot => IsLooseBusinessLot(lot) && !OnBorrowedMarketStall(lot) &&
                     (itemKind is null || lot.ItemKind == itemKind) &&
                     BusinessRules.MaySell("store", lot.ItemKind) && lot.StorageBuildingId != store.InstanceId &&
                     // Own carried goods, or household stock nobody is carrying.
                     (lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) ||
                      lot.OwnerId == householdId && lot.CarrierId is null &&
                         (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                             building.InstanceId == lot.StorageBuildingId && building.HouseholdId == householdId))) &&
                     !protectedToolIds.Contains(lot.Id) &&
                     !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id))
                     .OrderBy(lot => lot.OwnerId == actor ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        var foodSurpluses = new Dictionary<(string OwnerId, string ItemKind), int>();
        foreach (var lot in sources)
        {
            if (sourceLotId is not null && lot.Id != sourceLotId) continue;
            var surplus = StockQuantity(lot);
            if (IsEdibleFood(lot.ItemKind))
            {
                var key = (lot.OwnerId, lot.ItemKind);
                if (!foodSurpluses.TryGetValue(key, out var foodSurplus))
                {
                    var reserve = lot.OwnerId == actor ? 2 : 2 * society.Checkpoint.GetHousehold(householdId)
                        .MemberIds.Count(id => inhabitants.ContainsKey(id));
                    // A bound lot limits this load, not the stock that keeps the owner's reserve.
                    foodSurplus = Math.Max(0, sources.Where(source => source.OwnerId == lot.OwnerId &&
                        source.ItemKind == lot.ItemKind && CanStockFrom(source)).Sum(StockQuantity) - reserve);
                    foodSurpluses.Add(key, foodSurplus);
                }
                surplus = Math.Min(surplus, foodSurplus);
            }
            var shelf = inventory.Lots.Where(stock => stock.StorageBuildingId == store.InstanceId &&
                stock.ItemKind == lot.ItemKind).Sum(stock => stock.Quantity);
            var quantity = Math.Min(Math.Min(surplus, StoreShelfTarget - shelf), Math.Min(HouseHaulLoadQuantity, room));
            if (lot.OwnerId != actor) quantity = Math.Min(quantity, FreeCarryCapacity(actor));
            if (quantity <= 0) continue;
            if (!CanStockFrom(lot)) continue;
            return new(store, lot, quantity);
        }
        return null;

        int StockQuantity(InventoryLot lot) => lot.OwnerId == actor ? SpareCarriedQuantity(actor, lot) : AvailableLotQuantity(lot);
        bool CanStockFrom(InventoryLot lot)
        {
            var source = lot.OwnerId == actor ? inhabitants[actor].Position : HouseholdStockPosition(lot);
            var range = lot.OwnerId == actor ? 0 : HouseholdStockInteractionRange(lot);
            return (IsWithinInteractionRange(inhabitants[actor].Position, source, range) ||
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, source, range).Count > 0) &&
                (source == store.Position || FindUnoccupiedRoute(actor, source, store.Position, 0).Count > 0);
        }
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

    private DeliveryOrderPlan? GetStoreOrderPlan(string actor, PlaytestInhabitantState person,
        OwnerInstructionOrder order, int maximumQuantity)
    {
        if (NextStoreLoad(actor, order.TargetItemKind, order.TargetStorageBuildingId, order.DeliveryLotId,
                allowOrdinaryProject: true) is not { } load ||
            !DeliveryDestinationMatches(order, load.Store)) return null;
        var quantity = Math.Min(maximumQuantity, load.Quantity);
        return quantity <= 0 ? null : new DeliveryOrderPlan("store_stock", load.Store, load.Store.HouseholdId!,
            load.Goods, load.Goods, quantity, quantity, DirectDelivery: load.Goods.OwnerId == actor);
    }
}
