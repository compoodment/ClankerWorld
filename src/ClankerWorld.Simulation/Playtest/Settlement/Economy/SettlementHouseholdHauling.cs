using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int HouseHaulLoadQuantity = 4;

    private InventoryLot? CarriedHouseDelivery(string actor) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == actor && lot.DeliveryBuildingId is not null &&
                AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? UnlocatedHouseholdStock(string householdId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId is null &&
                AvailableLotQuantity(lot) > 0 &&
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
            candidates.Add(new("haul_household_stock",
                "Deliver already collected household supplies to their building.", 18,
                carried.DeliveryBuildingId));
            return;
        }
        if (HouseForHousehold(householdId) is not { } house)
            return;
        var camp = SettlementStoragePosition;
        if (UnlocatedHouseholdStock(householdId) is not null &&
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
        var quantity = Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(food) - 1);
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
            var house = worldSimulation.Buildings.Single(building =>
                building.InstanceId == carried.DeliveryBuildingId);
            if (state.Position != house.Position)
            {
                MoveToward(actor, state, house.Position, "household_stock", 0);
                return;
            }
            var deliveredQuantity = AvailableLotQuantity(carried);
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
        var quantity = Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(stock));
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"house-haul-pickup:{WorldTick}:{actor}", householdId, actor,
            stock.Id, quantity, "household_stock_picked_up",
            destinationDeliveryBuildingId: houseForPickup.InstanceId));
        AppendEvent("household_stock_picked_up",
            $"{actor}:{stock.Id}:{quantity}:{houseForPickup.InstanceId}");
    }
}
