using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int HouseHaulLoadQuantity = 4;

    private InventoryLot? CarriedHouseDelivery(string actor) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is not null &&
                lot.ContainerLotId is null && lot.CartId is null && lot.AnimalId is null &&
                AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? UnlocatedHouseholdStock(string householdId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId is null && lot.ContainerLotId is null && lot.CartId is null && lot.AnimalId is null &&
                AvailableLotQuantity(lot) > 0 &&
                (!FarmFieldRules.IsFarmStock(lot.ItemKind) || FarmhouseForHousehold(householdId) is null))
            .OrderBy(lot => FoodItems.IsEdible(lot.ItemKind) ? 0 : 1)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddHouseHaulCandidate(List<CognitionCandidate> candidates,
        string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (householdId is null)
            return;
        if (CarriedHouseDelivery(actor) is { } carried && CanDeliverHouseholdLoad(carried))
        {
            candidates.Add(new("haul_household_stock",
                "Deliver already collected household supplies to their building.", 18,
                carried.DeliveryBuildingId));
            return;
        }
        if (HouseForHousehold(householdId) is not { } house || StorageRoom(house.InstanceId) == 0)
            return;
        if (BuildingPreparationToolToStore(actor, householdId) is not null &&
            (state.Position == house.Position || FindUnoccupiedRoute(actor, state.Position, house.Position, 0).Count > 0))
        {
            candidates.Add(new("haul_household_stock",
                "Store a carried work tool in the household House so building materials fit in your load.",
                24, house.InstanceId));
            return;
        }
        if (UnlocatedHouseholdStock(householdId) is { } stock &&
            HouseholdPickupQuantity(actor, stock, house.InstanceId) > 0 &&
            (IsWithinInteractionRange(state.Position, HouseholdStockPosition(stock), HouseholdStockInteractionRange(stock)) ||
             FindUnoccupiedRoute(actor, state.Position, HouseholdStockPosition(stock), HouseholdStockInteractionRange(stock)).Count > 0) &&
            FindUnoccupiedRoute(actor, HouseholdStockPosition(stock), house.Position, 0).Count > 0)
            candidates.Add(new("haul_household_stock",
                "Carry a load of household supplies to the household's House.", 28, house.InstanceId));
        if (state.HungerBasisPoints >= 6_000 && PersonalSpareFood(actor) is not null &&
            (state.Position == house.Position ||
             FindUnoccupiedRoute(actor, state.Position, house.Position, 0).Count > 0))
            candidates.Add(new("store_household_food",
                "Bring personally carried spare food into the household House.", 30, house.InstanceId));
    }

    private InventoryLot? PersonalSpareFood(string actor) => PreferredFood(actor, actor)
        .FirstOrDefault(lot => lot.ContainerLotId is null && lot.CartId is null && lot.AnimalId is null &&
            lot.GroundPosition is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 1);

    private void StoreHouseholdFood(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || state.HungerBasisPoints < 6_000 ||
            HouseForHousehold(householdId) is not { } house || PersonalSpareFood(actor) is not { } food)
            return;
        if (state.Position != house.Position)
        {
            MoveToward(actor, state, house.Position, "household_food", 0);
            return;
        }
        var quantity = Math.Min(StorageRoom(house.InstanceId), Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(food) - 1));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"house-food-storage:{WorldTick}:{actor}", actor, householdId, food.Id,
            quantity, "household_food_stored", house.InstanceId));
        AppendEvent("household_food_stored", $"{actor}:{food.Id}:{quantity}:{house.InstanceId}");
    }

    private bool CanDeliverHouseholdLoad(InventoryLot lot) => lot.ContainerCapacity > 0
        ? InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, 1) <= StorageRoom(lot.DeliveryBuildingId!)
        : StorageRoom(lot.DeliveryBuildingId!) > 0;

    private void HaulHouseholdStock(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null)
            return;
        if (CarriedHouseDelivery(actor) is { } carried && CanDeliverHouseholdLoad(carried))
        {
            var house = worldSimulation.Buildings.Single(building =>
                building.InstanceId == carried.DeliveryBuildingId);
            if (state.Position != house.Position)
            {
                MoveToward(actor, state, house.Position, "household_stock", 0);
                return;
            }
            var deliveredQuantity = carried.ContainerCapacity > 0 ? 1 : Math.Min(StorageRoom(house.InstanceId), AvailableLotQuantity(carried));
            if (deliveredQuantity == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"house-haul-delivery:{WorldTick}:{actor}", actor, householdId,
                carried.Id, deliveredQuantity, "household_stock_delivered", house.InstanceId));
            AppendEvent("household_stock_delivered",
                $"{actor}:{carried.Id}:{deliveredQuantity}:{house.InstanceId}");
            return;
        }
        if (HouseForHousehold(householdId) is not { } houseForPickup)
            return;
        if (BuildingPreparationToolToStore(actor, householdId) is { } preparationTool)
        {
            if (state.Position != houseForPickup.Position)
            {
                MoveToward(actor, state, houseForPickup.Position, "household_stock", 0);
                return;
            }
            var storedQuantity = Math.Min(1, AvailableLotQuantity(preparationTool));
            if (storedQuantity == 0 || StorageRoom(houseForPickup.InstanceId) == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"house-preparation-tool:{WorldTick}:{actor}", actor, householdId, preparationTool.Id,
                storedQuantity, "household_preparation_tool_stored", destinationStorageBuildingId: houseForPickup.InstanceId));
            AppendEvent("household_preparation_tool_stored",
                $"{actor}:{preparationTool.ItemKind}:{storedQuantity}:{houseForPickup.InstanceId}");
            return;
        }
        if (UnlocatedHouseholdStock(householdId) is not { } stock)
            return;
        var source = HouseholdStockPosition(stock);
        var range = HouseholdStockInteractionRange(stock);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "household_stock", range);
            return;
        }
        var quantity = HouseholdPickupQuantity(actor, stock, houseForPickup.InstanceId);
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"house-haul-pickup:{WorldTick}:{actor}", householdId, actor,
            stock.Id, quantity, "household_stock_picked_up",
            destinationDeliveryBuildingId: houseForPickup.InstanceId));
        AppendEvent("household_stock_picked_up",
            $"{actor}:{stock.Id}:{quantity}:{houseForPickup.InstanceId}");
    }

    private int HouseholdPickupQuantity(string actor, InventoryLot stock, string buildingId)
    {
        var inventory = society.Checkpoint.Inventory;
        var inbound = inventory.Lots.Where(lot => lot.DeliveryBuildingId == buildingId).Sum(lot => lot.Quantity);
        var room = Math.Min(CarryingRoom(actor), Math.Max(0, StorageRoom(buildingId) - inbound));
        if (stock.ContainerCapacity > 0)
            return AvailableLotQuantity(stock) > 0 && !inventory.Reservations.Any(reservation =>
                inventory.Lots.Any(content => content.ContainerLotId == stock.Id && content.Id == reservation.LotId) &&
                reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                    InventoryReservationState.Committed) &&
                InventoryFixture.TransferLoadQuantity(inventory, stock.Id, 1) <= room ? 1 : 0;
        return Math.Min(room, Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(stock)));
    }

    private InventoryLot? PersonalGoodsForStorage(string actor)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            HouseForHousehold(household) is not { } house)
            return null;
        var room = StorageRoom(house.InstanceId);
        return SpareCargo(actor).Where(lot => !PersonalEquipmentRules.IsGarment(lot.ItemKind) &&
                !PersonalEquipmentRules.IsCarryAid(lot.ItemKind) &&
                (!FoodItems.IsEdible(lot.ItemKind) && ToolCapabilities.ForItem(lot.ItemKind) is null &&
                    !VesselRules.IsVessel(lot.ItemKind) || AvailableLotQuantity(lot) > 1) &&
                PersonalGoodsStorageQuantity(actor, lot, room) > 0)
            .OrderByDescending(lot => lot.Quantity).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private int PersonalGoodsStorageQuantity(string actor, InventoryLot goods, int room)
    {
        var available = AvailableLotQuantity(goods);
        if (room <= 0 || available == 0) return 0;
        if (goods.ContainerCapacity > 0)
        {
            var inventory = society.Checkpoint.Inventory;
            if (InventoryFixture.TransferLoadQuantity(inventory, goods.Id, 1) > room) return 0;
            // A food vessel cannot be split. Keep it if moving it would leave
            // the carrier without a personal serving.
            if (inventory.Lots.Any(lot => lot.ContainerLotId == goods.Id && FoodItems.IsEdible(lot.ItemKind) &&
                    AvailableLotQuantity(lot) > 0) &&
                !inventory.Lots.Any(lot => PersonalEquipmentRules.IsCarried(lot, actor) &&
                    lot.ContainerLotId != goods.Id && FoodItems.IsEdible(lot.ItemKind) && AvailableLotQuantity(lot) > 0))
                return 0;
            return 1;
        }
        var reserve = FoodItems.IsEdible(goods.ItemKind) || ToolCapabilities.ForItem(goods.ItemKind) is not null ? 1 : 0;
        return Math.Min(room, Math.Min(HouseHaulLoadQuantity, Math.Max(0, available - reserve)));
    }

    private bool CanStoreCarriedGoods(string actor)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            HouseForHousehold(household) is not { } house || PersonalGoodsForStorage(actor) is null)
            return false;
        var position = inhabitants[actor].Position;
        return position == house.Position || FindUnoccupiedRoute(actor, position, house.Position, 0).Count > 0;
    }

    private void AddStoreCarriedGoodsCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (CarryingRoom(actor) > 1 || !CanStoreCarriedGoods(actor)) return;
        var house = HouseForHousehold(society.Checkpoint.GetInhabitant(actor).HouseholdId!)!;
        candidates.Add(new("store_carried_goods", "Deliver carried supplies to the household House to make room.",
            16, house.InstanceId));
    }

    private void StoreCarriedGoods(string actor, PlaytestInhabitantState person)
    {
        if (!CanStoreCarriedGoods(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            HouseForHousehold(household) is not { } house || PersonalGoodsForStorage(actor) is not { } goods) return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "store_carried_goods", 0);
            return;
        }
        var quantity = PersonalGoodsStorageQuantity(actor, goods, StorageRoom(house.InstanceId));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"carried-stock:{WorldTick}:{actor}", actor, household, goods.Id, quantity,
            "carried_goods_stored", house.InstanceId));
        AppendEvent("carried_goods_stored", $"{actor}|{goods.ItemKind}|{quantity}|{house.InstanceId}");
    }

    /// <summary>
    /// A hungry adult whose load leaves no room for a food pickup may make room
    /// by setting down spare supplies for the household. Maps, field records,
    /// worn gear, reserved goods and delivery loads stay carried.
    /// </summary>
    private void AddMakeRoomForFoodCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state, int priority)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is null)
            return;
        var missing = FoodRoomMissing(actor, state);
        var load = SpareCargoLoadToFree(actor, missing);
        if (load == 0 ||
            SpareCargoDestination(actor, state, load) is not { } destination)
            return;
        candidates.Add(new("make_room_for_food",
            "Set down spare supplies with your household so gathered food fits in your load.", priority,
            destination.StorageBuildingId));
    }

    private int FoodRoomMissing(string actor, PlaytestInhabitantState state)
    {
        var required = AvailableFoodSource(actor, state.Position) is { } source ? FoodHarvestCarryUnits(source) : int.MaxValue;
        if (AvailableSharedFood(actor, ignoreCarryingRoom: true) is { } shared)
            required = Math.Min(required, shared.ItemKind == "milk" && shared.ContainerLotId is { } vesselId
                ? InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, vesselId, 1) : 1);
        if (required == int.MaxValue) return 0;
        return Math.Max(0, required - CarryingRoom(actor));
    }

    private IEnumerable<InventoryLot> SpareCargo(string actor)
    {
        var inventory = society.Checkpoint.Inventory;
        return society.Checkpoint.Inventory.Lots
            .Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
                lot.ContainerLotId is null && lot.CartId is null && lot.AnimalId is null &&
                !IsEquippedLot(actor, lot.Id) && inhabitants[actor].Equipment?.Repair?.LotId != lot.Id &&
                lot.ItemKind is not ("field_map" or "field_record") &&
                !inventory.Reservations.Any(reservation =>
                    (reservation.LotId == lot.Id || inventory.Lots.Any(content =>
                        content.ContainerLotId == lot.Id && content.Id == reservation.LotId)) &&
                    reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                        InventoryReservationState.Committed))
            // Tools speed up later gathering, so they are set down last.
            .OrderBy(lot => lot.ItemKind is "tool" or "wooden_axe" or "wooden_pickaxe" ? 1 : 0)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal);
    }

    private int SpareCargoLoadToFree(string actor, int missing)
    {
        if (missing <= 0) return 0;
        var load = 0;
        foreach (var lot in SpareCargo(actor))
        {
            var quantity = lot.ContainerCapacity > 0 ? 1 : Math.Min(missing - load, lot.Quantity);
            load += InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity);
            if (load >= missing) return load;
        }
        return 0;
    }

    /// <summary>The household House when it has room, otherwise the household's pile at camp.</summary>
    private (GridPoint Position, int Range, string? StorageBuildingId)? SpareCargoDestination(string actor,
        PlaytestInhabitantState state, int quantity)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId!;
        if (HouseForHousehold(householdId) is { } house && StorageRoom(house.InstanceId) >= quantity &&
            (state.Position == house.Position || FindUnoccupiedRoute(actor, state.Position, house.Position, 0).Count > 0))
            return (house.Position, 0, house.InstanceId);
        var camp = SettlementStoragePosition;
        return IsWithinInteractionRange(state.Position, camp, ResourceInteractionRange) ||
            FindUnoccupiedRoute(actor, state.Position, camp, ResourceInteractionRange).Count > 0
            ? (camp, ResourceInteractionRange, null) : null;
    }

    private void MakeRoomForFood(string actor, PlaytestInhabitantState state)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            return;
        var missing = FoodRoomMissing(actor, state);
        var load = missing == 0 ? 0 : SpareCargoLoadToFree(actor, missing);
        if (load == 0 || SpareCargoDestination(actor, state, load) is not { } destination)
            return;
        if (!IsWithinInteractionRange(state.Position, destination.Position, destination.Range))
        {
            MoveToward(actor, state, destination.Position, "make_room", destination.Range);
            return;
        }
        foreach (var lot in SpareCargo(actor).ToArray())
        {
            if (missing == 0) break;
            var quantity = lot.ContainerCapacity > 0 ? 1 : Math.Min(missing, lot.Quantity);
            var transferLoad = InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity);
            if (destination.StorageBuildingId is { } storage && transferLoad > StorageRoom(storage)) continue;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"spare-cargo:{WorldTick}:{actor}:{lot.Id}", actor, householdId, lot.Id, quantity,
                "spare_cargo_stored", destination.StorageBuildingId,
                destinationGroundPosition: destination.StorageBuildingId is null
                    ? new InventoryGroundPosition(destination.Position.X, destination.Position.Y) : null));
            AppendEvent("spare_cargo_stored", $"{actor}:{lot.ItemKind}:{quantity}:{destination.StorageBuildingId ?? "camp"}");
            missing = FoodRoomMissing(actor, inhabitants[actor]);
        }
    }
}
