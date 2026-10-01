using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed record WaterCollectionWork(string JugLotId, GridPoint Shore, long LastWorkedTick, int WorkDone);
public sealed record VesselActionResult(bool Applied, bool Completed, string? Failure = null);

public sealed partial class PrivateWorldRuntime
{
    private GridPoint[]? freshWaterShoreTiles;

    private void StagePotteryContent() => StageBuiltInContent(PotteryContent.PackageId,
        HouseContent.PackageId, PotteryContent.Create, "pottery_content_staged");

    private InventoryLot? PersonalJug(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == "water_jug" && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            lot.GroundPosition is null && AvailableLotQuantity(lot) > 0 &&
            society.Checkpoint.Inventory.Lots.Where(item => item.ContainerLotId == lot.Id).All(item => item.ItemKind == "water"))
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private int HouseholdVesselQuantity(string ownerId, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && lot.GroundPosition is null && AvailableLotQuantity(lot) > 0 &&
            (lot.OwnerId == ownerId || inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == ownerId))
        .Sum(lot => lot.Quantity);

    private bool IsFreshWaterShore(GridPoint shore) => IsFreshWaterShore(map, shore);

    private static bool IsFreshWaterShore(SeededMap map, GridPoint shore)
    {
        if (!map.IsPassable(shore) || map.HydrologyAt(shore) is WaterKind.Ocean or WaterKind.Lake or WaterKind.River)
            return false;
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var x = shore.X + dx;
            if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
            var water = new GridPoint(x, shore.Y + dy);
            if (water.X < 0 || water.X >= map.Width || water.Y < 0 || water.Y >= map.Height)
                continue;
            var kind = map.HydrologyAt(water);
            if (kind is WaterKind.Lake or WaterKind.River || kind is null &&
                map.Tiles.Any(tile => tile.Position == water && tile.Terrain is TerrainKind.Lake or TerrainKind.River))
                return true;
        }
        return false;
    }

    private GridPoint? ReachableFreshWaterShore(string actor, GridPoint origin)
    {
        freshWaterShoreTiles ??= map.Tiles.Where(tile => IsFreshWaterShore(tile.Position))
            .Select(tile => tile.Position).ToArray();
        foreach (var point in freshWaterShoreTiles.OrderBy(point => map.FootDistance(origin, point))
                     .ThenBy(point => point.Y).ThenBy(point => point.X).Take(32))
            if (point == origin || FindUnoccupiedRoute(actor, origin, point, 0).Count > 0)
                return point;
        return null;
    }

    private (string Kind, int Missing)? PotteryInputNeeded(string householdId, string houseId)
    {
        if (!worldContent.Recipes.Any(recipe => recipe.Tags.Contains("pottery", StringComparer.Ordinal)))
            return null;
        if (HouseholdVesselQuantity(householdId, "water_jug") >= 2 &&
            HouseholdVesselQuantity(householdId, "storage_pot") >= 1)
            return null;
        foreach (var (kind, target) in new[] { ("clay", 2), ("wood", 1) })
        {
            var stocked = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.StorageBuildingId == houseId && lot.ItemKind == kind).Sum(AvailableLotQuantity);
            var incoming = society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.DeliveryBuildingId == houseId && lot.ItemKind == kind).Sum(AvailableLotQuantity);
            if (target > stocked + incoming) return (kind, target - stocked - incoming);
        }
        return null;
    }

    private void AddPotteryCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house)
            return;
        if (PotteryInputNeeded(householdId, house.InstanceId) is { } need && CarriedHouseDelivery(actor) is null &&
            (society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == need.Kind &&
                AvailableLotQuantity(lot) > 0) || SpareHouseholdStock(householdId, need.Kind, house.InstanceId) is not null ||
                MaterialSource(need.Kind, actor) is not null))
            candidates.Add(new("pottery_supply", "Bring clay and fuel home to make reusable pottery.", 26, house.InstanceId));

        var householdWater = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
            lot.StorageBuildingId == house.InstanceId && lot.ItemKind == "water").Sum(AvailableLotQuantity);
        if (PersonalJug(actor) is { } jug)
        {
            var contents = society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId == jug.Id)
                .Sum(lot => lot.Quantity);
            if (contents > 0 && InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, jug.Id, 1) <= StorageRoom(house.InstanceId) &&
                (person.Position == house.Position ||
                FindUnoccupiedRoute(actor, person.Position, house.Position, 0).Count > 0))
                candidates.Add(new("water_deliver", "Carry the filled jug home for cooking and care.", 19, house.InstanceId));
            else if (contents == 0 && CarryingRoom(actor) > 0 &&
                (person.WaterWork?.Shore ?? ReachableFreshWaterShore(actor, person.Position)) is { } shore)
                candidates.Add(new("water_fill", "Take the jug to a river or lake and collect fresh water.", 23,
                    $"water-shore:{shore.X},{shore.Y}"));
        }
        else if (householdWater < 4 && CarryingRoom(actor) > 0 && EmptyHouseholdJug(actor) is not null &&
            ReachableFreshWaterShore(actor, person.Position) is not null)
            candidates.Add(new("water_collect_jug", "Collect an empty household jug to bring back fresh water.", 22));

        if (person.Position == house.Position && PotFoodToStore(householdId, house.InstanceId) is not null)
            candidates.Add(new("pot_store_food", "Put household food in a storage pot to help it keep longer.", 31,
                house.InstanceId));
    }

    private InventoryLot? EmptyHouseholdJug(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == HouseholdFor(actor) && lot.ItemKind == "water_jug" &&
            lot.GroundPosition is null && AvailableLotQuantity(lot) > 0 && CanReachSharedItem(actor, lot) &&
            !society.Checkpoint.Inventory.Lots.Any(item => item.ContainerLotId == lot.Id))
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void CollectEmptyWaterJug(string actor, PlaytestInhabitantState person)
    {
        if (!AdultResident(actor) || CarryingRoom(actor) == 0 || PersonalJug(actor) is not null || EmptyHouseholdJug(actor) is not { } jug)
            return;
        var position = HouseholdStockPosition(jug);
        var range = HouseholdStockInteractionRange(jug);
        if (!IsWithinInteractionRange(person.Position, position, range))
        {
            MoveToward(actor, person, position, "water_jug", range);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"water-jug-pickup:{WorldTick}:{actor}", jug.OwnerId, actor, jug.Id, 1, "water_jug_collected"));
        AppendEvent("water_jug_collected", $"{actor}:{jug.Id}");
    }

    private void SupplyPottery(string actor, PlaytestInhabitantState person)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            PotteryInputNeeded(householdId, house.InstanceId) is not { } need)
            return;
        var carried = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == need.Kind &&
                lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        if (carried is not null)
        {
            if (person.Position != house.Position)
            {
                MoveToward(actor, person, house.Position, "pottery_supply", 0);
                return;
            }
            var amount = Math.Min(StorageRoom(house.InstanceId), Math.Min(need.Missing, AvailableLotQuantity(carried)));
            if (amount == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"pottery-supply:{WorldTick}:{actor}", actor, householdId, carried.Id, amount,
                "pottery_supplied", house.InstanceId));
            AppendEvent("pottery_supplied", $"{actor}:{need.Kind}:{amount}:{house.InstanceId}");
            return;
        }
        if (SpareHouseholdStock(householdId, need.Kind, house.InstanceId) is { } stock)
        {
            var location = HouseholdStockPosition(stock);
            var range = HouseholdStockInteractionRange(stock);
            if (!IsWithinInteractionRange(person.Position, location, range))
            {
                MoveToward(actor, person, location, "pottery_supply", range);
                return;
            }
            var amount = Math.Min(CarryingRoom(actor), Math.Min(need.Missing,
                Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(stock))));
            if (amount == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"pottery-pickup:{WorldTick}:{actor}", householdId, actor, stock.Id,
                amount,
                "pottery_input_picked_up", destinationDeliveryBuildingId: house.InstanceId));
            return;
        }
        if (MaterialSource(need.Kind, actor) is { } source)
            GatherProjectMaterial(actor, person, need.Kind, source);
    }

    public VesselActionResult FillWaterJug(string actor, string jugLotId, GridPoint shore)
    {
        gate.Wait();
        try { return FillWaterJugCore(actor, jugLotId, shore); }
        finally { gate.Release(); }
    }

    private VesselActionResult FillWaterJugCore(string actor, string jugLotId, GridPoint shore)
    {
        if (!AdultResident(actor) || !inhabitants.TryGetValue(actor, out var person))
            return new(false, false, "An adult must do this work.");
        var inventory = society.Checkpoint.Inventory;
        var jug = inventory.Lots.FirstOrDefault(lot => lot.Id == jugLotId && lot.OwnerId == actor &&
            lot.ItemKind == "water_jug" && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            lot.GroundPosition is null && AvailableLotQuantity(lot) > 0);
        if (jug is null) return new(false, false, "Carry a usable water jug first.");
        if (inventory.Lots.Any(lot => lot.ContainerLotId == jug.Id && lot.ItemKind != "water"))
            return new(false, false, "Empty the milk jug before collecting fresh water.");
        if (!IsFreshWaterShore(shore)) return new(false, false, "Collect fresh water beside a river or lake.");
        if (person.Position != shore) return new(false, false, "Bring the jug to the water's edge first.");
        var room = Math.Min(CarryingRoom(actor), InventoryFixture.ContainerRoom(inventory, jug.Id));
        if (room == 0) return new(false, false, "The jug or the collector's hands are full.");
        if (inventory.Reservations.Any(reservation => reservation.LotId == jug.Id &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                InventoryReservationState.Committed))
            return new(false, false, "This jug is set aside for another task.");
        var work = person.WaterWork is { } saved && saved.JugLotId == jug.Id && saved.Shore == shore
            ? saved : new WaterCollectionWork(jug.Id, shore, WorldTick, 0);
        if (work.WorkDone > 0 && work.LastWorkedTick == WorldTick)
            return new(false, false, "Water collection needs work time.");
        if (!SettlementIllnessRules.AllowsWork(actor, WorldTick, person.Survival?.IllnessBasisPoints ?? 0))
            return new(false, false, "The collector needs time to recover.");
        var done = work.WorkDone + 1;
        if (done < VesselRules.FillingWorkTicks)
        {
            checkpointSchemaVersion = StateSchemaVersion;
            inhabitants[actor] = person with { WaterWork = work with { WorkDone = done, LastWorkedTick = WorldTick } };
            return new(true, false);
        }
        ApplyInventoryTransition(current => InventoryFixture.AddLot(current,
            $"water:{WorldTick}:{actor}:{jug.Id}", "water", actor, room, WorldTick, containerLotId: jug.Id));
        inhabitants[actor] = person with { WaterWork = null };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("water_collected", $"{actor}:{jug.Id}:{room}");
        return new(true, true);
    }

    private void CollectFreshWater(string actor, PlaytestInhabitantState person)
    {
        if (PersonalJug(actor) is not { } jug ||
            (person.WaterWork?.Shore ?? ReachableFreshWaterShore(actor, person.Position)) is not { } shore)
            return;
        if (person.Position != shore)
            MoveToward(actor, person, shore, "fresh_water", 0);
        else
            FillWaterJugCore(actor, jug.Id, shore);
    }

    private void DeliverWaterJug(string actor, PlaytestInhabitantState person)
    {
        if (PersonalJug(actor) is not { } jug || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            !society.Checkpoint.Inventory.Lots.Any(lot => lot.ContainerLotId == jug.Id))
            return;
        if (InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, jug.Id, 1) > StorageRoom(house.InstanceId))
            return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "water_delivery", 0);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"water-delivery:{WorldTick}:{actor}", actor, householdId, jug.Id, 1, "water_delivered", house.InstanceId));
        inhabitants[actor] = inhabitants[actor] with { WaterWork = null };
        AppendEvent("water_delivered", $"{actor}:{jug.Id}:{house.InstanceId}");
    }

    private (InventoryLot Food, InventoryLot Pot)? PotFoodToStore(string householdId, string houseId)
    {
        var inventory = society.Checkpoint.Inventory;
        var pot = inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == houseId &&
                lot.ItemKind == "storage_pot" && AvailableLotQuantity(lot) > 0 &&
                InventoryFixture.ContainerRoom(inventory, lot.Id) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        var food = inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == houseId &&
                lot.ContainerLotId is null && VesselRules.IsPotFood(lot.ItemKind) && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.FreshnessBasisPoints).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        return pot is not null && food is not null ? (food, pot) : null;
    }

    private void StoreFoodInPot(string actor, PlaytestInhabitantState person)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house || person.Position != house.Position ||
            PotFoodToStore(householdId, house.InstanceId) is not { } pair)
            return;
        var quantity = Math.Min(AvailableLotQuantity(pair.Food),
            InventoryFixture.ContainerRoom(society.Checkpoint.Inventory, pair.Pot.Id));
        ApplyInventoryTransition(inventory => InventoryFixture.StoreInContainer(inventory,
            $"pot-storage:{WorldTick}:{actor}", householdId, pair.Food.Id, quantity, pair.Pot.Id));
        AppendEvent("food_potted", $"{actor}:{pair.Pot.Id}:{pair.Food.ItemKind}:{quantity}");
    }

    private static void ValidateVessels(PrivateWorldRuntimeState state)
    {
        var lots = state.Society.Society.Inventory.Lots;
        InventoryFixture.ValidatePortableContainers(lots);
        foreach (var lot in lots)
        {
            if (lot.ContainerCapacity != VesselRules.Capacity(lot.ItemKind))
                throw new InvalidDataException("A saved vessel has an invalid capacity.");
            if (lot.ItemKind is "water" or "milk" && lot.ContainerLotId is null)
                throw new InvalidDataException("Saved water and milk must be inside a jug.");
            if (lot.ContainerLotId is { } id)
            {
                var vessel = lots.Single(item => item.Id == id);
                if (vessel.ItemKind == "water_jug" ? lot.ItemKind is not ("water" or "milk") : !VesselRules.IsPotFood(lot.ItemKind))
                    throw new InvalidDataException("The vessel contains an unsuitable item.");
            }
        }
        if (lots.Where(lot => lot.ContainerLotId is not null && lot.ItemKind is "water" or "milk")
            .GroupBy(lot => lot.ContainerLotId).Any(group => group.Select(lot => lot.ItemKind).Distinct(StringComparer.Ordinal).Count() > 1))
            throw new InvalidDataException("Keep milk and fresh water in separate jugs.");
        foreach (var person in state.Inhabitants)
            if (person.WaterWork is { } work && (work.LastWorkedTick < 0 || work.LastWorkedTick > state.Society.Society.WorldTick ||
                work.WorkDone is < 1 or >= VesselRules.FillingWorkTicks || !IsFreshWaterShore(state.Map, work.Shore) ||
                !lots.Any(lot => lot.Id == work.JugLotId && lot.OwnerId == person.InhabitantId &&
                    lot.ItemKind == "water_jug" && lot.StorageBuildingId is null && lot.ConditionBasisPoints > 0)))
                throw new InvalidDataException("Saved water collection does not match a present collector and jug.");
        if (state.SchemaVersion < 29 && (lots.Any(lot => lot.ContainerCapacity > 0 || lot.ContainerLotId is not null) ||
            state.Inhabitants.Any(person => person.WaterWork is not null)))
            throw new InvalidDataException("Pottery and collected water require the current private-world save schema.");
    }
}
