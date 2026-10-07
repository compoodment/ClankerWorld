using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldDeliveryOrderTests
{
    private const string Restaurant = "meal-delivery-restaurant";
    private static readonly Lazy<byte[]> RestaurantBaseline = new(() =>
        WithBuilding(Restaurant, RestaurantContent.Restaurant1x2().CanonicalId));
    private const string MealStore = "named-meal-store";
    private static readonly Lazy<byte[]> MealStoreBaseline = new(() => WithBuilding(MealStore,
        ContentDefinitionPayloadCodec.ApplyPackage(new([], []), BusinessContent.Create()).Buildings
            .Single(building => building.LocalId == "store-1x1").CanonicalId));

    [Theory]
    [InlineData("bread")]
    [InlineData("restaurant_meal")]
    public async Task NamedMealStoreStockingCountsRealDepositsAndStopsAtThePersonalFoodReserve(string kind)
    {
        var state = Prepared(MealStoreBaseline.Value);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "personal-meals", kind, actor, 7);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "stock-meals", "stock six " + kind.Replace('_', ' ') + " in my Store");
        Assert.Equal("deliver_stock", Order(world, receipt).Action);
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(0, Stored(world, MealStore, kind));
        using var replay = Reload(world);
        for (var tick = 0; tick < 60 && Order(world, receipt) is not { Status: "blocked", CompletedUnits: 5 }; tick++)
            await TickTogether(world, replay);
        Assert.Equal(("blocked", 5), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(5, Stored(world, MealStore, kind));
        Assert.Equal((actor, 2), (world.Society.Inventory.GetLot("personal-meals").OwnerId,
            world.Society.Inventory.GetLot("personal-meals").Quantity));
        Assert.Null(Order(world, receipt).DeliveryLotId);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
    }

    [Theory]
    [InlineData("grain")]
    [InlineData("potatoes")]
    public async Task NamedHouseCookingInputsCountOnlyTheirFinalPhysicalDelivery(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var source = SourceNear(state, Home(state, House).Position);
        state = At(state, actor, source);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "meal-input", kind, Alpha, 3,
            groundPosition: new(source.X, source.Y));
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "house-cooking-input", "supply two " + kind + " to my House");
        Assert.Equal("deliver_stock", Order(world, receipt).Action);
        await Tick(world);
        Assert.Equal((0, House, "workstation_input", 2),
            (Order(world, receipt).CompletedUnits, Order(world, receipt).TargetStorageBuildingId,
                Order(world, receipt).DeliveryRoute, Order(world, receipt).DeliveryQuantity));
        Assert.Equal(0, Stored(world, House, kind));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(2, Stored(world, House, kind));
        Assert.Equal((Alpha, 1), (world.Society.Inventory.GetLot("meal-input").OwnerId,
            world.Society.Inventory.GetLot("meal-input").Quantity));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public async Task RestaurantFlourSupplyPreservesTheHousesTwoBatchCookingReserve(int flour, bool surplus)
    {
        var state = Prepared(RestaurantBaseline.Value);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "house-cooking-flour", "flour", Alpha,
            flour, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "restaurant-flour", "supply two flour to my Restaurant");
        Assert.Equal("deliver_stock", Order(world, receipt).Action);
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        if (!surplus)
        {
            Assert.Equal("blocked", Order(world, receipt).Status);
            Assert.Equal(4, Stored(world, House, "flour"));
            Assert.Equal(0, Stored(world, Restaurant, "flour"));
            Assert.Null(Order(world, receipt).DeliveryLotId);
            using var refused = Reload(world);
            await TickTogether(world, refused);
            return;
        }
        Assert.Equal((Restaurant, "workstation_input", 2),
            (Order(world, receipt).TargetStorageBuildingId, Order(world, receipt).DeliveryRoute,
                Order(world, receipt).DeliveryQuantity));
        Assert.Equal(4, Stored(world, House, "flour"));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(2, Stored(world, Restaurant, "flour"));
        Assert.Equal(4, Stored(world, House, "flour"));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, false)]
    public async Task RestaurantWaterSupplyKeepsSourceReservesAndNeverSplitsAnOversizedJug(int water, bool fits)
    {
        var state = Prepared(RestaurantBaseline.Value);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "z-reserve-jug", "water_jug", Alpha,
            1, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "z-reserve-water", "fresh_water", Alpha, 2,
            storageBuildingId: House, containerLotId: "z-reserve-jug");
        inventory = InventoryFixture.AddLot(inventory, "a-supply-jug", "water_jug", Alpha, 1, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "a-supply-water", "fresh_water", Alpha, water,
            storageBuildingId: House, containerLotId: "a-supply-jug");
        if (!fits)
            // Otherwise the other, smaller jug is a legitimate complete shipment.
            inventory = InventoryFixture.Reserve(inventory, "reserve-jug-held", Alpha, "z-reserve-water", 1,
                "other_work", 100);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "restaurant-water", "supply two fresh water to my Restaurant");
        Assert.Equal("deliver_stock", Order(world, receipt).Action);
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(2, world.Society.Inventory.GetLot("z-reserve-water").Quantity);
        Assert.Equal(House, world.Society.Inventory.GetLot("z-reserve-jug").StorageBuildingId);
        if (!fits)
        {
            Assert.Equal("blocked", Order(world, receipt).Status);
            Assert.Equal((House, water), (world.Society.Inventory.GetLot("a-supply-water").StorageBuildingId,
                world.Society.Inventory.GetLot("a-supply-water").Quantity));
            Assert.Equal(House, world.Society.Inventory.GetLot("a-supply-jug").StorageBuildingId);
            Assert.Equal(InventoryReservationState.Reserved,
                world.Society.Inventory.GetReservation("reserve-jug-held").State);
            using var refused = Reload(world);
            await TickTogether(world, refused);
            return;
        }
        Assert.Equal(("a-supply-jug", 2), (Order(world, receipt).DeliveryLotId, Order(world, receipt).DeliveryQuantity));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal((Alpha, Restaurant, 1), (world.Society.Inventory.GetLot("a-supply-jug").OwnerId,
            world.Society.Inventory.GetLot("a-supply-jug").StorageBuildingId, world.Society.Inventory.GetLot("a-supply-jug").Quantity));
        Assert.Equal(("a-supply-jug", Restaurant, 2), (world.Society.Inventory.GetLot("a-supply-water").ContainerLotId,
            world.Society.Inventory.GetLot("a-supply-water").StorageBuildingId, world.Society.Inventory.GetLot("a-supply-water").Quantity));
        Assert.Equal(2, Stored(world, House, "fresh_water"));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
    }
}
