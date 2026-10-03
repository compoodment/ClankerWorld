using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int HouseHaulLoadQuantity = 4;

    private InventoryLot? CarriedHouseDelivery(string actor)
    {
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots
            .Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) && lot.ContainerLotId is null &&
                lot.DeliveryBuildingId is not null &&
                (InventoryContainerRules.IsContainer(lot.ItemKind)
                    ? !HasActiveContainerReservation(inventory, lot.Id) && !UnusableDeliveryStock(inventory, lot)
                    : AvailableLotQuantity(lot) > 0))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private static bool UnusableDeliveryStock(InventoryCheckpoint inventory, InventoryLot root) =>
        root.ConditionBasisPoints == 0 || root.FreshnessBasisPoints == 0 ||
        InventoryContainerRules.IsContainer(root.ItemKind) && inventory.Lots.Any(lot =>
            lot.ContainerLotId == root.Id && (lot.ConditionBasisPoints == 0 || lot.FreshnessBasisPoints == 0));

    private InventoryLot? RecoverableHouseholdDelivery(string actor, PlaytestInhabitantState state)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            !IsWithinInteractionRange(state.Position, SettlementStoragePosition, ResourceInteractionRange) &&
            FindUnoccupiedRoute(actor, state.Position, SettlementStoragePosition, ResourceInteractionRange).Count == 0)
            return null;
        var inventory = society.Checkpoint.Inventory;
        var equipment = state.Equipment;
        return inventory.Lots.Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
                lot.ContainerLotId is null && lot.Quantity > 0 && lot.DeliveryBuildingId is { } destinationId &&
                worldSimulation.Buildings.Any(building => building.InstanceId == destinationId && building.HouseholdId == householdId) &&
                lot.Id != equipment?.ClothingLotId && lot.Id != equipment?.CarryAidLotId && lot.Id != equipment?.Repair?.LotId &&
                lot.ItemKind is not ("field_map" or "field_record") &&
                UnusableDeliveryStock(inventory, lot) && !HasActiveContainerReservation(inventory, lot.Id))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private void AddRecoverHouseholdDeliveryCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (RecoverableHouseholdDelivery(actor, state) is not null)
            candidates.Add(new("recover_household_delivery",
                "Return unusable delivery supplies to your household's pile at camp.", 18));
    }

    private void RecoverHouseholdDelivery(string actor, PlaytestInhabitantState state)
    {
        if (RecoverableHouseholdDelivery(actor, state) is not { } stock)
            return;
        var camp = SettlementStoragePosition;
        if (!IsWithinInteractionRange(state.Position, camp, ResourceInteractionRange))
        {
            MoveToward(actor, state, camp, "recover_household_delivery", ResourceInteractionRange);
            return;
        }
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId!;
        var quantity = InventoryContainerRules.IsContainer(stock.ItemKind) ? 1 : stock.Quantity;
        var physicalQuantity = HouseDeliveryPhysicalQuantity(society.Checkpoint.Inventory, stock);
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"house-delivery-recovery:{WorldTick}:{actor}:{stock.Id}", actor, householdId, stock.Id, quantity,
            "household_delivery_recovered", destinationGroundPosition: new InventoryGroundPosition(camp.X, camp.Y)));
        AppendEvent("household_delivery_recovered", $"{actor}:{stock.Id}:{physicalQuantity}:camp");
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
        var room = Math.Max(0, StorageRoom(buildingId) - otherInbound);
        if (worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == buildingId) is { } building &&
            IsFarmStorage(building))
            room = Math.Min(room, Math.Max(0, FarmStorageFree(buildingId, includeDeliveries: false) - otherInbound));
        return room;
    }

    private static int HouseDeliveryPhysicalQuantity(InventoryCheckpoint inventory, InventoryLot carried) =>
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
        var room = StorageRoomAfterInboundDeliveries(destinationId);
        if (worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == destinationId) is { } building &&
            IsFarmStorage(building))
            room = Math.Min(room, FarmStorageFree(destinationId));
        var capacity = Math.Min(FreeCarryCapacity(actor), room);
        if (capacity == 0)
            return 0;
        return InventoryContainerRules.IsContainer(stock.ItemKind)
            ? ContainerFamilyQuantity(inventory, stock.Id) <= capacity ? 1 : 0
            : Math.Min(HouseHaulLoadQuantity, Math.Min(capacity, AvailableLotQuantity(stock)));
    }

    /// <summary>
    /// Loose household stock to carry to the House. With an actor and House,
    /// only stock that actor can pick up now is returned, so a vessel load too
    /// large to haul does not hide the stock behind it.
    /// </summary>
    private InventoryLot? UnlocatedHouseholdStock(string householdId, string? actor = null,
        string? houseId = null) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.CarrierId is null && lot.ContainerLotId is null &&
                lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                (InventoryContainerRules.IsContainer(lot.ItemKind)
                    ? !HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id) &&
                        !UnusableDeliveryStock(society.Checkpoint.Inventory, lot)
                    : AvailableLotQuantity(lot) > 0) &&
                (!FarmFieldRules.IsFarmStock(lot.ItemKind) || FarmhouseForHousehold(householdId) is null))
            .OrderBy(lot => lot.ItemKind == "food" ? 0 : 1)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal)
            .FirstOrDefault(lot => actor is null || houseId is null ||
                HouseHaulPickupQuantity(actor, lot, houseId) > 0);

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
        if (HouseForHousehold(householdId) is not { } house || StorageRoomAfterInboundDeliveries(house.InstanceId) == 0)
            return;
        if (BuildingPreparationToolToStore(actor, householdId) is not null &&
            (state.Position == house.Position || FindUnoccupiedRoute(actor, state.Position, house.Position, 0).Count > 0))
        {
            candidates.Add(new("haul_household_stock",
                "Store a carried work tool in the household House so building materials fit in your load.",
                24, house.InstanceId));
            return;
        }
        if (FreeCarryCapacity(actor) > 0 && UnlocatedHouseholdStock(householdId, actor, house.InstanceId) is { } stock &&
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
        var quantity = Math.Min(StorageRoomAfterInboundDeliveries(house.InstanceId), Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(food) - 1));
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
        if (BuildingPreparationToolToStore(actor, householdId) is { } preparationTool)
        {
            if (state.Position != houseForPickup.Position)
            {
                MoveToward(actor, state, houseForPickup.Position, "household_stock", 0);
                return;
            }
            var storedQuantity = Math.Min(1, AvailableLotQuantity(preparationTool));
            if (storedQuantity == 0 || StorageRoom(houseForPickup.InstanceId) == 0) return;
            ApplyInventoryTransition(inventory => preparationTool.OwnerId == householdId
                ? InventoryFixture.Relocate(inventory, $"house-preparation-tool:{WorldTick}:{actor}", preparationTool.Id,
                    householdId, storedQuantity, storageBuildingId: houseForPickup.InstanceId)
                : InventoryFixture.Transfer(inventory, $"house-preparation-tool:{WorldTick}:{actor}", actor, householdId,
                    preparationTool.Id, storedQuantity, "household_preparation_tool_stored", destinationStorageBuildingId: houseForPickup.InstanceId));
            AppendEvent("household_preparation_tool_stored",
                $"{actor}:{preparationTool.ItemKind}:{storedQuantity}:{houseForPickup.InstanceId}");
            return;
        }
        if (UnlocatedHouseholdStock(householdId, actor, houseForPickup.InstanceId) is not { } stock)
            return;
        var source = HouseholdStockPosition(stock);
        var range = HouseholdStockInteractionRange(stock);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "household_stock", range);
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
        var cargo = SpareCargoForFood(actor, FoodRoomMissing(actor, state));
        if (cargo.Count == 0 ||
            SpareCargoDestination(actor, state, cargo.Sum(move => move.PhysicalQuantity)) is not { } destination)
            return;
        var existing = candidates.FindIndex(candidate => candidate.Id == "make_room_for_food");
        if (existing >= 0)
        {
            candidates[existing] = candidates[existing] with
            {
                DeterministicPriority = Math.Min(candidates[existing].DeterministicPriority, priority),
            };
            return;
        }
        candidates.Add(new("make_room_for_food",
            "Set down spare supplies with your household so food fits in your load.", priority,
            destination.StorageBuildingId));
    }

    private int FoodRoomMissing(string actor, PlaytestInhabitantState state)
    {
        var required = state.HungerBasisPoints < 7_000 && !PreferredFood(actor, actor).Any()
            ? MinimumFoodPickupCarryUnits(actor, state.Position, forDependent: false) : 0;
        var dependentRequired = CaregiverFoodCarryRequirement(actor, state);
        if (dependentRequired > 0)
            required = required > 0 ? Math.Min(required, dependentRequired) : dependentRequired;
        return Math.Max(0, required - FreeCarryCapacity(actor));
    }

    private sealed record SpareCargoMove(InventoryLot Lot, int TransferQuantity, int PhysicalQuantity);

    private List<SpareCargoMove> SpareCargoForFood(string actor, int missing, string? protectedLotId = null,
        params string[] additionallyProtectedLotIds)
    {
        if (missing <= 0) return [];
        var equipment = inhabitants[actor].Equipment;
        var inventory = society.Checkpoint.Inventory;
        var spare = inventory.Lots
            .Where(lot => (lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor)) &&
                PersonalEquipmentRules.IsCarried(lot, actor) && lot.ContainerLotId is null &&
                lot.Id != protectedLotId && !additionallyProtectedLotIds.Contains(lot.Id, StringComparer.Ordinal) &&
                lot.DeliveryBuildingId is null &&
                !PersonalEquipmentRules.IsSelected(equipment, lot.Id) &&
                !AgentKnowledgeRules.IsArtifactKind(lot.ItemKind) &&
                !society.Checkpoint.Inventory.Reservations.Any(reservation => reservation.LotId == lot.Id &&
                    reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                        InventoryReservationState.Committed) &&
                (!InventoryContainerRules.IsContainer(lot.ItemKind) || !HasActiveContainerReservation(inventory, lot.Id)))
            // Tools speed up later gathering, so they are set down last.
            .OrderBy(lot => lot.ItemKind is "tool" or "wooden_axe" or "wooden_pickaxe" ? 1 : 0)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal);
        var moves = new List<SpareCargoMove>();
        foreach (var lot in spare)
        {
            var vessel = InventoryContainerRules.IsContainer(lot.ItemKind);
            var quantity = vessel ? 1 : Math.Min(missing, lot.Quantity);
            var physicalQuantity = vessel ? ContainerFamilyQuantity(inventory, lot.Id) : quantity;
            moves.Add(new(lot, quantity, physicalQuantity));
            missing -= physicalQuantity;
            if (missing <= 0) return moves;
        }
        return [];
    }

    /// <summary>The household House when it has room, otherwise the household's pile at camp.</summary>
    private (GridPoint Position, int Range, string? StorageBuildingId)? SpareCargoDestination(string actor,
        PlaytestInhabitantState state, int quantity)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId!;
        if (HouseForHousehold(householdId) is { } house && StorageRoomAfterInboundDeliveries(house.InstanceId) >= quantity &&
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
        var cargo = SpareCargoForFood(actor, FoodRoomMissing(actor, state));
        StoreSpareCargo(actor, state, householdId, cargo);
    }

    private bool StoreSpareCargo(string actor, PlaytestInhabitantState state, string householdId,
        List<SpareCargoMove> cargo)
    {
        if (cargo.Count == 0 ||
            SpareCargoDestination(actor, state, cargo.Sum(move => move.PhysicalQuantity)) is not { } destination)
            return false;
        if (!IsWithinInteractionRange(state.Position, destination.Position, destination.Range))
        {
            MoveToward(actor, state, destination.Position, "make_room", destination.Range);
            return true;
        }
        foreach (var move in cargo)
        {
            var lot = move.Lot;
            var quantity = move.TransferQuantity;
            ApplyInventoryTransition(inventory => lot.OwnerId == householdId
                ? InventoryFixture.Relocate(inventory, $"spare-cargo:{WorldTick}:{actor}:{lot.Id}", lot.Id, householdId, quantity,
                    storageBuildingId: destination.StorageBuildingId, groundPosition: destination.StorageBuildingId is null
                        ? new InventoryGroundPosition(destination.Position.X, destination.Position.Y) : null)
                : InventoryFixture.Transfer(inventory, $"spare-cargo:{WorldTick}:{actor}:{lot.Id}", actor, householdId, lot.Id, quantity,
                    "spare_cargo_stored", destination.StorageBuildingId, destinationGroundPosition: destination.StorageBuildingId is null
                        ? new InventoryGroundPosition(destination.Position.X, destination.Position.Y) : null));
            AppendEvent("spare_cargo_stored", $"{actor}:{lot.ItemKind}:{quantity}:{destination.StorageBuildingId ?? "camp"}");
        }
        return true;
    }
}
