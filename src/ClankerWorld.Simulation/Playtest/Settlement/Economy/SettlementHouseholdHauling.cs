using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int HouseHaulLoadQuantity = 4;

    private InventoryLot? CarriedHouseDelivery(string actor)
    {
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots
            .Where(lot => lot.OwnerId == actor && lot.ContainerLotId is null && lot.DeliveryBuildingId is not null &&
                (InventoryContainerRules.IsContainer(lot.ItemKind)
                    ? !HasActiveContainerReservation(inventory, lot.Id)
                    : AvailableLotQuantity(lot) > 0))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private int HouseDeliveryRoom(InventoryLot carried)
    {
        var inventory = society.Checkpoint.Inventory;
        var buildingId = carried.DeliveryBuildingId!;
        var familyIds = InventoryContainerRules.IsContainer(carried.ItemKind)
            ? inventory.Lots.Where(lot => lot.Id == carried.Id || lot.ContainerLotId == carried.Id)
                .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>([carried.Id], StringComparer.Ordinal);
        var otherInbound = inventory.Lots.Where(lot => lot.DeliveryBuildingId == buildingId &&
                !familyIds.Contains(lot.Id))
            .Sum(lot => lot.Quantity);
        return Math.Max(0, StorageRoom(buildingId) - otherInbound);
    }

    private int HouseDeliveryPhysicalQuantity(InventoryCheckpoint inventory, InventoryLot carried) =>
        InventoryContainerRules.IsContainer(carried.ItemKind)
            ? ContainerFamilyQuantity(inventory, carried.Id)
            : carried.Quantity;

    private bool CanDeliverHouseDelivery(InventoryLot carried)
    {
        var inventory = society.Checkpoint.Inventory;
        var familyQuantity = HouseDeliveryPhysicalQuantity(inventory, carried);
        return familyQuantity <= HouseDeliveryRoom(carried);
    }

    private int HouseHaulPickupQuantity(string actor, InventoryLot stock, string destinationId)
    {
        var inventory = society.Checkpoint.Inventory;
        var inbound = InboundDeliveryQuantity(inventory, destinationId);
        var room = Math.Max(0, StorageRoom(destinationId) - inbound);
        var capacity = Math.Min(HouseHaulLoadQuantity, Math.Min(FreeCarryCapacity(actor), room));
        if (capacity == 0)
            return 0;
        return InventoryContainerRules.IsContainer(stock.ItemKind)
            ? ContainerFamilyQuantity(inventory, stock.Id) <= capacity ? 1 : 0
            : Math.Min(capacity, AvailableLotQuantity(stock));
    }

    private InventoryLot? UnlocatedHouseholdStock(string householdId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.ContainerLotId is null && lot.StorageBuildingId is null &&
                (InventoryContainerRules.IsContainer(lot.ItemKind)
                    ? !HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id)
                    : AvailableLotQuantity(lot) > 0) &&
                (lot.ItemKind != "grain" || FarmhouseForHousehold(householdId) is null))
            .OrderBy(lot => lot.ItemKind == "food" ? 0 : 1)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddHouseHaulCandidate(List<CognitionCandidate> candidates,
        string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (householdId is null)
            return;
        if (CarriedHouseDelivery(actor) is { } carried)
        {
            if (CanDeliverHouseDelivery(carried))
                candidates.Add(new("haul_household_stock",
                    "Deliver already collected household supplies to their building.", 18,
                    carried.DeliveryBuildingId));
            return;
        }
        if (HouseForHousehold(householdId) is not { } house || StorageRoom(house.InstanceId) == 0)
            return;
        var camp = SettlementStoragePosition;
        if (UnlocatedHouseholdStock(householdId) is { } stock &&
            HouseHaulPickupQuantity(actor, stock, house.InstanceId) > 0 &&
            FindUnoccupiedRoute(actor, state.Position, camp, ResourceInteractionRange).Count > 0 &&
            FindUnoccupiedRoute(actor, camp, house.Position, 0).Count > 0)
            candidates.Add(new("haul_household_stock",
                "Carry a load of household supplies to the household's House.", 28, house.InstanceId));
        if (state.HungerBasisPoints >= 6_000 && PersonalSpareFood(actor) is not null &&
            (state.Position == house.Position ||
             FindUnoccupiedRoute(actor, state.Position, house.Position, 0).Count > 0))
            candidates.Add(new("store_household_food",
                "Bring personally carried spare food into the household House.", 30, house.InstanceId));
    }

    private InventoryLot? PersonalSpareFood(string actor) => PreferredFood(actor, actor)
        .FirstOrDefault(lot => lot.ContainerLotId is null && lot.DeliveryBuildingId is null &&
            !InventoryContainerRules.IsContainer(lot.ItemKind) && AvailableLotQuantity(lot) > 1);

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
        if (CarriedHouseDelivery(actor) is { } carried)
        {
            if (!CanDeliverHouseDelivery(carried))
                return;
            var house = worldSimulation.Buildings.Single(building =>
                building.InstanceId == carried.DeliveryBuildingId);
            if (state.Position != house.Position)
            {
                MoveToward(actor, state, house.Position, "household_stock", 0);
                return;
            }
            var deliveryRoom = HouseDeliveryRoom(carried);
            var deliveredQuantity = InventoryContainerRules.IsContainer(carried.ItemKind)
                ? HouseDeliveryPhysicalQuantity(society.Checkpoint.Inventory, carried) <= deliveryRoom ? 1 : 0
                : Math.Min(deliveryRoom, AvailableLotQuantity(carried));
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
        if (UnlocatedHouseholdStock(householdId) is not { } stock)
            return;
        var camp = SettlementStoragePosition;
        if (!IsWithinInteractionRange(state.Position, camp, ResourceInteractionRange))
        {
            MoveToward(actor, state, camp, "household_stock", ResourceInteractionRange);
            return;
        }
        var quantity = HouseHaulPickupQuantity(actor, stock, houseForPickup.InstanceId);
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"house-haul-pickup:{WorldTick}:{actor}", householdId, actor,
            stock.Id, quantity, "household_stock_picked_up",
            destinationDeliveryBuildingId: houseForPickup.InstanceId));
        AppendEvent("household_stock_picked_up",
            $"{actor}:{stock.Id}:{quantity}:{houseForPickup.InstanceId}");
    }
}
