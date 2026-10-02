using System.Globalization;
using System.Runtime.CompilerServices;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Physical storage and fresh-water actions for reusable household vessels.</summary>
public sealed partial class PrivateWorldRuntime
{
    private const string FillWaterJugPrefix = "fill_water_jug:";
    private static readonly ConditionalWeakTable<SeededMap, GridPoint[]> FreshWaterShoreCache = new();

    private sealed record PotFoodChoice(InventoryLot Pot, InventoryLot Food);

    private InventoryLot? CarriedContainer(string actor, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) && lot.ItemKind == kind &&
            lot.ContainerLotId is null && lot.DeliveryBuildingId is null)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private static int ContainerContentsQuantity(InventoryCheckpoint inventory, string containerId) =>
        inventory.Lots.Where(lot => lot.ContainerLotId == containerId).Sum(lot => lot.Quantity);

    private static int ContainerFamilyQuantity(InventoryCheckpoint inventory, string containerId) =>
        inventory.Lots.Where(lot => lot.Id == containerId || lot.ContainerLotId == containerId)
            .Sum(lot => lot.Quantity);

    private static int InboundDeliveryQuantity(InventoryCheckpoint inventory, string buildingId) =>
        inventory.Lots.Where(lot => lot.DeliveryBuildingId == buildingId).Sum(lot => lot.Quantity);

    private int StorageRoomAfterInboundDeliveries(string buildingId) =>
        Math.Max(0, StorageRoom(buildingId) - InboundDeliveryQuantity(society.Checkpoint.Inventory, buildingId));

    private static bool HasActiveContainerReservation(InventoryCheckpoint inventory, string containerId) =>
        inventory.Reservations.Any(reservation =>
            (reservation.LotId == containerId || inventory.Lots.Any(lot =>
                lot.Id == reservation.LotId && lot.ContainerLotId == containerId)) &&
            reservation.State is InventoryReservationState.Reserved or
                InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed);

    private void AddContainerCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState person)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house)
            return;

        var inventory = society.Checkpoint.Inventory;
        var carriedJug = CarriedContainer(actor, InventoryContainerRules.WaterJug);
        if (carriedJug is not null)
        {
            var contents = ContainerContentsQuantity(inventory, carriedJug.Id);
            var shore = carriedJug.ConditionBasisPoints > 0 &&
                contents < InventoryContainerRules.WaterJugCapacity &&
                FreeCarryCapacity(actor) > 0 &&
                !HasActiveContainerReservation(inventory, carriedJug.Id)
                    ? FindFreshWaterShore(actor, person.Position)
                    : null;
            if (shore is { } fillAt)
            {
                var pointText = $"{fillAt.X.ToString(CultureInfo.InvariantCulture)},{fillAt.Y.ToString(CultureInfo.InvariantCulture)}";
                candidates.Add(new CognitionCandidate(
                    $"{FillWaterJugPrefix}{pointText}",
                    "Fill the water jug at a reachable riverbank or lakeshore.", 18,
                    $"shore:{pointText}"));
            }
            else if (!HasActiveContainerReservation(inventory, carriedJug.Id) &&
                     ContainerFamilyQuantity(inventory, carriedJug.Id) <= StorageRoomAfterInboundDeliveries(house.InstanceId) &&
                     FindUnoccupiedRoute(actor, person.Position, house.Position, 0).Count > 0)
            {
                candidates.Add(new CognitionCandidate("return_water_jug",
                    "Carry the water jug back to its household House.", 22, house.InstanceId));
            }
        }
        else
        {
            var storedJug = inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                    lot.ItemKind == InventoryContainerRules.WaterJug && lot.StorageBuildingId == house.InstanceId &&
                    lot.ContainerLotId is null && lot.ConditionBasisPoints > 0 &&
                    ContainerContentsQuantity(inventory, lot.Id) < InventoryContainerRules.WaterJugCapacity &&
                    // Leave at least one carrying place for fresh water after pickup.
                    ContainerFamilyQuantity(inventory, lot.Id) < FreeCarryCapacity(actor) &&
                    !HasActiveContainerReservation(inventory, lot.Id))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            if (storedJug is not null && FindFreshWaterShore(actor, house.Position) is not null &&
                FindUnoccupiedRoute(actor, person.Position, house.Position, 0).Count > 0)
            {
                candidates.Add(new CognitionCandidate("collect_water_jug",
                    "Collect the household's reusable jug before fetching fresh water.", 26, storedJug.Id));
            }
        }

        AddEmptyVesselReturnCandidate(candidates, actor, person);
        AddFoodPotCandidates(candidates, actor, person, householdId, house);
    }

    private void AddFoodPotCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState person, string householdId, PlacedBuilding house)
    {
        var inventory = society.Checkpoint.Inventory;
        var foodToStore = FindFoodToStore(actor, householdId, house.InstanceId);
        if (foodToStore is not null &&
            FindUnoccupiedRoute(actor, person.Position, house.Position, 0).Count > 0)
        {
            candidates.Add(new CognitionCandidate("store_food_in_pot",
                "Put available household food into its storage pot.", 24, foodToStore.Pot.Id));
        }

        AddFoodPotRetrievalCandidate(candidates, actor, person, householdId, house);
    }

    private void AddUrgentFoodPotCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState person)
    {
        if (!NeedsUrgentFood(person) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house)
            return;

        AddFoodPotRetrievalCandidate(candidates, actor, person, householdId, house);
    }

    private void AddFoodPotRetrievalCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState person, string householdId, PlacedBuilding house)
    {
        var inventory = society.Checkpoint.Inventory;
        // Like collecting other household food, a serving follows the Town's
        // food policy, and only food that can be eaten now is offered.
        if (person.HungerBasisPoints < 7_000 && MayCollectSharedFood(actor) &&
            !inventory.Lots.Any(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
                lot.ContainerLotId is null && lot.DeliveryBuildingId is null && IsEdibleFood(lot.ItemKind) &&
                AvailableLotQuantity(lot) > 0) &&
            FreeCarryCapacity(actor) > 0 &&
            FindFoodInPot(householdId, house.InstanceId) is { } storedFood &&
            FindUnoccupiedRoute(actor, person.Position, house.Position, 0).Count > 0)
        {
            candidates.Add(new CognitionCandidate("take_food_from_pot",
                "Take one serving from the household's storage pot.",
                NeedsUrgentFood(person) ? 1 : 4, storedFood.Food.Id));
        }
    }

    private PotFoodChoice? FindFoodToStore(string actor, string householdId, string houseId)
    {
        var inventory = society.Checkpoint.Inventory;
        var houseStorageRoom = StorageRoomAfterInboundDeliveries(houseId);
        var pots = inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.ItemKind == InventoryContainerRules.StoragePot && lot.StorageBuildingId == houseId &&
                lot.ContainerLotId is null && lot.ConditionBasisPoints > 0 &&
                !HasActiveContainerReservation(inventory, lot.Id))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal);
        foreach (var pot in pots)
        {
            var contentRoom = InventoryContainerRules.StoragePotCapacity -
                ContainerContentsQuantity(inventory, pot.Id);
            if (contentRoom <= 0)
                continue;
            var food = inventory.Lots.Where(lot =>
                    InventoryContainerRules.IsFood(lot.ItemKind) && lot.ContainerLotId is null &&
                    lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 &&
                    AvailableLotQuantity(lot) > (lot.OwnerId == actor ? 1 : 0) &&
                    (lot.OwnerId == actor && houseStorageRoom > 0 &&
                         PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null ||
                     lot.OwnerId == householdId && lot.StorageBuildingId == houseId))
                .OrderBy(lot => lot.OwnerId == actor ? 0 : 1)
                .ThenBy(lot => lot.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (food is not null)
                return new PotFoodChoice(pot, food);
        }
        return null;
    }

    private GridPoint[] FreshWaterShorePositions() =>
        FreshWaterShoreCache.GetValue(map, static currentMap =>
        {
            var shores = new HashSet<GridPoint>();
            foreach (var water in currentMap.Tiles.Select(tile => tile.Position)
                         .Where(point => currentMap.HydrologyAt(point) is WaterKind.River or WaterKind.Lake))
            {
                foreach (var (dx, dy) in new (int X, int Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                {
                    var candidate = currentMap.WrapColumn(new GridPoint(water.X + dx, water.Y + dy));
                    if (currentMap.Contains(candidate) && currentMap.HydrologyAt(candidate) == WaterKind.Land &&
                        currentMap.IsPassable(candidate))
                        shores.Add(candidate);
                }
            }
            return shores.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        });

    private GridPoint? FindFreshWaterShore(string actor, GridPoint origin)
    {
        foreach (var point in FreshWaterShorePositions()
                     .Where(point => map.IsReachableOnFoot(origin, point))
                     .OrderBy(point => map.FootDistance(origin, point))
                     .ThenBy(point => point.Y).ThenBy(point => point.X))
        {
            if (point == origin || FindUnoccupiedRoute(actor, origin, point, 0).Count > 0)
                return point;
        }
        return null;
    }

    private void CollectWaterJug(string actor, PlaytestInhabitantState person)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house)
            return;
        var inventory = society.Checkpoint.Inventory;
        var jug = inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.ItemKind == InventoryContainerRules.WaterJug && lot.StorageBuildingId == house.InstanceId &&
                lot.ContainerLotId is null && lot.ConditionBasisPoints > 0 &&
                ContainerContentsQuantity(inventory, lot.Id) < InventoryContainerRules.WaterJugCapacity &&
                ContainerFamilyQuantity(inventory, lot.Id) < FreeCarryCapacity(actor) &&
                !HasActiveContainerReservation(inventory, lot.Id))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        if (jug is null || ContainerFamilyQuantity(inventory, jug.Id) >= FreeCarryCapacity(actor))
            return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "collect_water_jug", 0);
            return;
        }
        ApplyInventoryTransition(current => InventoryFixture.Transfer(current,
            $"water-jug-pickup:{WorldTick}:{actor}", householdId, actor, jug.Id, 1,
            "water_jug_collected"));
        AppendEvent("water_jug_collected", $"{actor}:{jug.Id}");
    }

    private void FillWaterJug(string actor, PlaytestInhabitantState person, string target)
    {
        if (!TryParsePoint(target, out var shore) || !FreshWaterShorePositions().Contains(shore) ||
            CarriedContainer(actor, InventoryContainerRules.WaterJug) is not { ConditionBasisPoints: > 0 } jug)
            return;
        var inventory = society.Checkpoint.Inventory;
        if (!map.IsReachableOnFoot(person.Position, shore) ||
            FindUnoccupiedRoute(actor, person.Position, shore, 0).Count == 0)
            return;
        if (person.Position != shore)
        {
            MoveToward(actor, person, shore, "fill_water_jug", 0);
            return;
        }
        var remaining = Math.Min(
            InventoryContainerRules.WaterJugCapacity - ContainerContentsQuantity(inventory, jug.Id),
            FreeCarryCapacity(actor));
        if (remaining <= 0 || HasActiveContainerReservation(inventory, jug.Id))
            return;
        ApplyInventoryTransition(current => InventoryFixture.AddLot(current,
            $"{jug.Id}#water:{WorldTick.ToString(CultureInfo.InvariantCulture)}",
            InventoryContainerRules.FreshWater, actor, remaining, WorldTick, containerLotId: jug.Id));
        AppendEvent("water_jug_filled", $"{actor}:{jug.Id}:{remaining}:{shore.X},{shore.Y}");
    }

    private void ReturnWaterJug(string actor, PlaytestInhabitantState person)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            CarriedContainer(actor, InventoryContainerRules.WaterJug) is not { } jug)
            return;
        var inventory = society.Checkpoint.Inventory;
        if (ContainerFamilyQuantity(inventory, jug.Id) > StorageRoomAfterInboundDeliveries(house.InstanceId))
            return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "return_water_jug", 0);
            return;
        }
        ApplyInventoryTransition(current => InventoryFixture.Transfer(current,
            $"water-jug-return:{WorldTick}:{actor}", actor, householdId, jug.Id, 1,
            "water_jug_returned", destinationStorageBuildingId: house.InstanceId));
        AppendEvent("water_jug_returned", $"{actor}:{jug.Id}:{house.InstanceId}");
    }

    private void StoreFoodInPot(string actor, PlaytestInhabitantState person)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            FindFoodToStore(actor, householdId, house.InstanceId) is not { } choice)
            return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "store_food_in_pot", 0);
            return;
        }

        var inventory = society.Checkpoint.Inventory;
        var contentRoom = InventoryContainerRules.StoragePotCapacity -
            ContainerContentsQuantity(inventory, choice.Pot.Id);
        var storageRoom = choice.Food.OwnerId == actor ? StorageRoomAfterInboundDeliveries(house.InstanceId) : int.MaxValue;
        var available = AvailableLotQuantity(choice.Food) - (choice.Food.OwnerId == actor ? 1 : 0);
        var quantity = Math.Min(available, Math.Min(contentRoom, storageRoom));
        if (quantity <= 0)
            return;

        var operationId = $"food-pot:{WorldTick}:{actor}";
        ApplyInventoryTransition(current =>
        {
            var sourceId = choice.Food.Id;
            if (choice.Food.OwnerId == actor)
            {
                current = InventoryFixture.Transfer(current, operationId, actor, householdId, choice.Food.Id,
                    quantity, "food_stored_in_pot", destinationStorageBuildingId: house.InstanceId);
                if (quantity != choice.Food.Quantity)
                    sourceId = $"{choice.Food.Id}#transfer:{operationId}";
            }
            return InventoryFixture.PutIntoContainer(current, operationId, householdId, choice.Pot.Id, sourceId, quantity);
        });
        AppendEvent("food_stored_in_pot", $"{actor}:{choice.Pot.Id}:{choice.Food.Id}:{quantity}");
    }

    private void TakeFoodFromPot(string actor, PlaytestInhabitantState person)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            FindFoodInPot(householdId, house.InstanceId) is not { } choice)
            return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "take_food_from_pot", 0);
            return;
        }
        var quantity = Math.Min(1, AvailableLotQuantity(choice.Food));
        if (FreeCarryCapacity(actor) < quantity)
            return;
        if (quantity <= 0)
            return;
        ApplyInventoryTransition(inventory => InventoryFixture.TakeFromContainer(inventory,
            $"food-pot-take:{WorldTick}:{actor}", householdId, actor, choice.Pot.Id, choice.Food.Id, quantity));
        AppendEvent("food_taken_from_pot", $"{actor}:{choice.Pot.Id}:{choice.Food.Id}:{quantity}");
    }

    private PotFoodChoice? FindFoodInPot(string householdId, string houseId)
    {
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.ItemKind == InventoryContainerRules.StoragePot && lot.StorageBuildingId == houseId &&
                lot.ContainerLotId is null && lot.ConditionBasisPoints > 0 &&
                !HasActiveContainerReservation(inventory, lot.Id))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(pot => inventory.Lots.Where(lot => lot.ContainerLotId == pot.Id &&
                    IsEdibleFood(lot.ItemKind) && lot.ConditionBasisPoints > 0 &&
                    lot.FreshnessBasisPoints > 0 && AvailableLotQuantity(lot) > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal)
                .Select(food => new PotFoodChoice(pot, food)).FirstOrDefault())
            .FirstOrDefault(choice => choice is not null);
    }

    private static bool TryParsePoint(string value, out GridPoint point)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            point = new GridPoint(x, y);
            return true;
        }
        point = default;
        return false;
    }
}
