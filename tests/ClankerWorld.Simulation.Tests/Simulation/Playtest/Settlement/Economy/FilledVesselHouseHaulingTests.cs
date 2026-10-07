using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class FilledVesselHouseHaulingTests
{
    private const string Jug = "0-capacity-house-jug";
    private const string Water = "capacity-house-water";

    [Fact]
    public async Task AWholeFilledJugWalksIntoTheHouseWhenItsFiveRealUnitsFitAcrossReload()
    {
        var (state, actor, housePosition) = await FoodCapacityTestFixture.Generated("oversized-loose-jug");
        var inventory = JugInventory(state, actor);
        state = FoodCapacityTestFixture.WithInventory(state, inventory);
        Assert.Equal(5, inventory.Lots.Where(lot => lot.Id == Jug || lot.ContainerLotId == Jug).Sum(lot => lot.Quantity));
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        var sourcePosition = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        Assert.NotEqual(sourcePosition, housePosition);
        Assert.True(state.Map.IsReachableOnFoot(sourcePosition, housePosition));
        Assert.True(HouseRoom(state) >= 5);
        var choices = new FoodCapacityTestFixture.Choices("haul_household_stock");
        using var world = FoodCapacityTestFixture.Restore(state, actor, choices);
        var pickedUp = false;
        var started = world.WorldTick;
        var leftHouse = false;
        for (var tick = 0; world.WorldTick - started < 40 && world.Society.Inventory.GetLot(Jug).StorageBuildingId != FoodCapacityTestFixture.House; tick++)
        {
            var beforePosition = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var position = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            leftHouse |= position != housePosition;
            if (position != beforePosition) Assert.True(state.Map.CanFootStep(beforePosition, position));
            Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null), 0, 8);
            if (!pickedUp && world.Society.Inventory.GetLot(Jug).OwnerId == actor)
            {
                pickedUp = true;
                Assert.InRange(state.Map.FootDistance(position, sourcePosition), 0, 1);
                var carriedJug = world.Society.Inventory.GetLot(Jug);
                var carriedWater = world.Society.Inventory.GetLot(Water);
                Assert.Equal(FoodCapacityTestFixture.House, carriedJug.DeliveryBuildingId);
                Assert.Equal((actor, Jug, 4, carriedJug.DeliveryBuildingId),
                    (carriedWater.OwnerId, carriedWater.ContainerLotId, carriedWater.Quantity, carriedWater.DeliveryBuildingId));
                Assert.Equal(5, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
                if (world.WorldTick - started < 40)
                    await FoodCapacityTestFixture.AssertReplay(world, actor, "haul_household_stock");
            }
        }

        Assert.True(pickedUp, "The legal five-unit vessel family was never picked up.");
        Assert.True(leftHouse, "The carrier must actually leave the House to collect the camp vessel.");
        Assert.Equal(housePosition, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.InRange(world.WorldTick - started, 1, 40);
        Assert.Contains(choices.Offers, offer => offer.Contains("haul_household_stock", StringComparer.Ordinal));
        var result = world.Society.Inventory;
        Assert.Equal((FoodCapacityTestFixture.Household, FoodCapacityTestFixture.House, 1),
            (result.GetLot(Jug).OwnerId, result.GetLot(Jug).StorageBuildingId, result.GetLot(Jug).Quantity));
        Assert.Equal((FoodCapacityTestFixture.Household, FoodCapacityTestFixture.House, Jug, 4),
            (result.GetLot(Water).OwnerId, result.GetLot(Water).StorageBuildingId, result.GetLot(Water).ContainerLotId,
                result.GetLot(Water).Quantity));
        Assert.Null(result.GetLot(Jug).DeliveryBuildingId);
        Assert.Null(result.GetLot(Water).DeliveryBuildingId);
        Assert.Equal(2, result.Events.Count(item => item.Kind == "container_transferred" && item.Detail.Contains(Jug, StringComparison.Ordinal)));
        Assert.DoesNotContain(result.Lots, lot => lot.ItemKind == InventoryContainerRules.FreshWater && lot.ContainerLotId is null);
        await FoodCapacityTestFixture.AssertReplay(world, actor, "haul_household_stock");
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("incoming")]
    [InlineData("reserved")]
    [InlineData("foreign")]
    public async Task FilledFamiliesStayIntactWhenActualCapacityRightsOrReservationsRefuseThem(string obstruction)
    {
        var (state, actor, _) = await FoodCapacityTestFixture.Generated("oversized-loose-jug");
        var inventory = JugInventory(state, actor);
        if (obstruction == "carry")
        {
            inventory = InventoryFixture.AddLot(inventory, "protected-haul-load", "stone", actor, 4);
            inventory = InventoryFixture.Reserve(inventory, "protected-haul-reservation", actor,
                "protected-haul-load", 4, "capacity_control", inventory.WorldTick + 100);
        }
        else if (obstruction is "storage" or "incoming")
        {
            var room = HouseRoom(FoodCapacityTestFixture.WithInventory(state, inventory));
            inventory = InventoryFixture.AddLot(inventory, "house-room-control", "stone", FoodCapacityTestFixture.Household,
                room - (obstruction == "storage" ? 4 : 5), storageBuildingId: FoodCapacityTestFixture.House);
            if (obstruction == "incoming")
            {
                var other = state.Society.Society.Inhabitants.First(person => person.HouseholdId == FoodCapacityTestFixture.Household &&
                    person.Id != actor).Id;
                inventory = InventoryFixture.AddLot(inventory, "promised-house-space", "stone",
                    FoodCapacityTestFixture.Household, 1);
                inventory = InventoryFixture.Transfer(inventory, "promise-house-space",
                    FoodCapacityTestFixture.Household, other, "promised-house-space", 1, "household_stock_picked_up",
                    destinationDeliveryBuildingId: FoodCapacityTestFixture.House);
                Assert.Equal((other, FoodCapacityTestFixture.House, 1),
                    (inventory.GetLot("promised-house-space").OwnerId,
                        inventory.GetLot("promised-house-space").DeliveryBuildingId,
                        inventory.GetLot("promised-house-space").Quantity));
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(inventory, other,
                    state.Inhabitants.Single(person => person.InhabitantId == other).Equipment), 1, 8);
            }
        }
        else if (obstruction == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "reserved-jug-child", FoodCapacityTestFixture.Household,
                Water, 1, "capacity_control", inventory.WorldTick + 100);
        else
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == Jug || lot.ContainerLotId == Jug
                    ? lot with { OwnerId = "household:camp-beta" } : lot).ToArray(),
            };
        state = FoodCapacityTestFixture.WithInventory(state, inventory);
        var before = inventory.Lots.Where(lot => lot.Id == Jug || lot.ContainerLotId == Jug)
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.StorageBuildingId, lot.DeliveryBuildingId, lot.ContainerLotId)).ToArray();
        using var world = FoodCapacityTestFixture.Restore(state, actor, new FoodCapacityTestFixture.Choices("haul_household_stock"));
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(before, world.Society.Inventory.Lots.Where(lot => lot.Id == Jug || lot.ContainerLotId == Jug)
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.StorageBuildingId, lot.DeliveryBuildingId, lot.ContainerLotId)).ToArray());
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "household_stock_picked_up" &&
            item.Detail.Contains(Jug, StringComparison.Ordinal));
        if (obstruction == "reserved")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("reserved-jug-child").State);
        await FoodCapacityTestFixture.AssertReplay(world, actor, "haul_household_stock");
    }

    [Fact]
    public async Task DivisibleLooseStockRetainsItsFourUnitHaulLimit()
    {
        var (state, actor, _) = await FoodCapacityTestFixture.Generated("oversized-loose-jug");
        var inventory = EmptyLooseInventory(state, actor);
        inventory = InventoryFixture.AddLot(inventory, "loose-six-wood", "wood", FoodCapacityTestFixture.Household, 6);
        using var world = FoodCapacityTestFixture.Restore(FoodCapacityTestFixture.WithInventory(state, inventory), actor,
            new FoodCapacityTestFixture.Choices("haul_household_stock"));
        for (var tick = 0; tick < 40 && !world.ExportState().Events.Any(item => item.Kind == "household_stock_picked_up"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, world.Society.Inventory.GetLot("loose-six-wood").Quantity);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "loose-six-wood" && lot.OwnerId == actor)
            .Sum(lot => lot.Quantity));
        await FoodCapacityTestFixture.AssertReplay(world, actor, "haul_household_stock");
    }

    private static InventoryCheckpoint EmptyLooseInventory(PrivateWorldRuntimeState state, string actor) =>
        state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                !(lot.OwnerId == FoodCapacityTestFixture.Household && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null)).ToArray(),
        };

    private static InventoryCheckpoint JugInventory(PrivateWorldRuntimeState state, string actor)
    {
        var inventory = EmptyLooseInventory(state, actor);
        inventory = InventoryFixture.AddLot(inventory, Jug, InventoryContainerRules.WaterJug, FoodCapacityTestFixture.Household, 1);
        return InventoryFixture.AddLot(inventory, Water, InventoryContainerRules.FreshWater, FoodCapacityTestFixture.Household, 4,
            containerLotId: Jug);
    }

    private static int HouseRoom(PrivateWorldRuntimeState state)
    {
        using var world = PrivateWorldRuntime.Restore(state, _ => new FoodCapacityTestFixture.Choices());
        var house = world.WorldSimulation.Buildings.Single(building => building.InstanceId == FoodCapacityTestFixture.House);
        var definition = world.WorldContent.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        return BuildingStorageRules.Capacity(definition, house)!.Value - state.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
    }
}
