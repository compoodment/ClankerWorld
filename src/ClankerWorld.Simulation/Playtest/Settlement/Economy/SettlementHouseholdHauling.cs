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
                AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? UnlocatedHouseholdStock(string householdId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId is null &&
                AvailableLotQuantity(lot) > 0 &&
                (!FarmFieldRules.IsFarmStock(lot.ItemKind) || FarmhouseForHousehold(householdId) is null))
            .OrderBy(lot => lot.ItemKind == "food" ? 0 : 1)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddHouseHaulCandidate(List<CognitionCandidate> candidates,
        string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (householdId is null)
            return;
        if (CarriedHouseDelivery(actor) is { } carried && StorageRoom(carried.DeliveryBuildingId!) > 0)
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
        if (FreeCarryCapacity(actor) > 0 && UnlocatedHouseholdStock(householdId) is { } stock &&
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
        .FirstOrDefault(lot => lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 1);

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

    private void HaulHouseholdStock(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null)
            return;
        if (CarriedHouseDelivery(actor) is { } carried && StorageRoom(carried.DeliveryBuildingId!) > 0)
        {
            var house = worldSimulation.Buildings.Single(building =>
                building.InstanceId == carried.DeliveryBuildingId);
            if (state.Position != house.Position)
            {
                MoveToward(actor, state, house.Position, "household_stock", 0);
                return;
            }
            var deliveredQuantity = Math.Min(StorageRoom(house.InstanceId), AvailableLotQuantity(carried));
            if (IsFarmStorage(house))
                deliveredQuantity = Math.Min(deliveredQuantity, FarmStorageFree(house.InstanceId, includeDeliveries: false));
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
        var inbound = society.Checkpoint.Inventory.Lots.Where(lot => lot.DeliveryBuildingId == houseForPickup.InstanceId).Sum(lot => lot.Quantity);
        var quantity = Math.Min(Math.Max(0, StorageRoom(houseForPickup.InstanceId) - inbound), Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(stock)));
        quantity = Math.Min(quantity, FreeCarryCapacity(actor));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"house-haul-pickup:{WorldTick}:{actor}", householdId, actor,
            stock.Id, quantity, "household_stock_picked_up",
            destinationDeliveryBuildingId: houseForPickup.InstanceId));
        AppendEvent("household_stock_picked_up",
            $"{actor}:{stock.Id}:{quantity}:{houseForPickup.InstanceId}");
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
        if (missing == 0 || SpareCargo(actor).Sum(lot => lot.Quantity) < missing ||
            SpareCargoDestination(actor, state, missing) is not { } destination)
            return;
        candidates.Add(new("make_room_for_food",
            "Set down spare supplies with your household so gathered food fits in your load.", priority,
            destination.StorageBuildingId));
    }

    private int FoodRoomMissing(string actor, PlaytestInhabitantState state)
    {
        var required = AvailableFoodSource(actor, state.Position) is { } source ? FoodHarvestCarryUnits(source)
            : AvailableSharedFood(actor) is not null ? 1 : 0;
        return Math.Max(0, required - FreeCarryCapacity(actor));
    }

    private IEnumerable<InventoryLot> SpareCargo(string actor)
    {
        var equipment = inhabitants[actor].Equipment;
        return society.Checkpoint.Inventory.Lots
            .Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
                lot.Id != equipment?.ClothingLotId && lot.Id != equipment?.CarryAidLotId &&
                lot.ItemKind is not ("field_map" or "field_record") &&
                !society.Checkpoint.Inventory.Reservations.Any(reservation => reservation.LotId == lot.Id &&
                    reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                        InventoryReservationState.Committed))
            // Tools speed up later gathering, so they are set down last.
            .OrderBy(lot => lot.ItemKind is "tool" or "wooden_axe" or "wooden_pickaxe" ? 1 : 0)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal);
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
        if (missing == 0 || SpareCargoDestination(actor, state, missing) is not { } destination)
            return;
        if (!IsWithinInteractionRange(state.Position, destination.Position, destination.Range))
        {
            MoveToward(actor, state, destination.Position, "make_room", destination.Range);
            return;
        }
        foreach (var lot in SpareCargo(actor).ToArray())
        {
            if (missing == 0) break;
            var quantity = Math.Min(missing, lot.Quantity);
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"spare-cargo:{WorldTick}:{actor}:{lot.Id}", actor, householdId, lot.Id, quantity,
                "spare_cargo_stored", destination.StorageBuildingId,
                destinationGroundPosition: destination.StorageBuildingId is null
                    ? new InventoryGroundPosition(destination.Position.X, destination.Position.Y) : null));
            AppendEvent("spare_cargo_stored", $"{actor}:{lot.ItemKind}:{quantity}:{destination.StorageBuildingId ?? "camp"}");
            missing -= quantity;
        }
    }
}
