using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldDeliveryOrderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitSuppliesKeepTheBuildersCarriedCartMaterials(bool blacksmith)
    {
        var state = Prepared(household: Beta);
        var actor = Actor(state, Beta);
        var destination = Home(state, blacksmith ? Smith : "first-town-house-b");
        state = At(state, actor, destination.Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "cart-wood", "wood", actor, 5);
        inventory = InventoryFixture.AddLot(inventory, "cart-rope", "rope", actor, 1);
        var smith = Home(state, Smith);
        inventory = InventoryFixture.AddLot(inventory, "cart-fittings", "iron_fittings", Beta, 2,
            groundPosition: new(smith.Position.X, smith.Position.Y));
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "cart-reserve", "supply two wood to my " + (blacksmith ? "Blacksmith" : "House"));
        Assert.Equal("deliver_stock", Order(world, receipt).Action);
        await Tick(world);
        Assert.Equal(1, Stored(world, destination.InstanceId, "wood"));
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
        Assert.Equal((actor, 4), (world.Society.Inventory.GetLot("cart-wood").OwnerId,
            world.Society.Inventory.GetLot("cart-wood").Quantity));
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot("cart-rope").OwnerId,
            world.Society.Inventory.GetLot("cart-rope").Quantity));
        Assert.Equal((Beta, 2), (world.Society.Inventory.GetLot("cart-fittings").OwnerId,
            world.Society.Inventory.GetLot("cart-fittings").Quantity));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(4, world.Society.Inventory.GetLot("cart-wood").Quantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnusableCarriedVesselCannotCompleteItsBoundDelivery(bool brokenVessel)
    {
        var state = Prepared(ClinicBaseline.Value);
        var actor = Actor(state);
        var source = Home(state, House).Position;
        state = At(state, actor, source);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "clinic-herbs", "medicinal_herbs", Alpha, 4, storageBuildingId: Clinic);
        inventory = InventoryFixture.AddLot(inventory, "clinic-fuel", "wood", Alpha, 2, storageBuildingId: Clinic);
        inventory = InventoryFixture.AddLot(inventory, "promised-jug", "water_jug", Alpha, 1,
            groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, "promised-water", "fresh_water", Alpha, 4,
            containerLotId: "promised-jug");
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "vessel-usability", "supply four fresh water to my Clinic");
        await Tick(world);
        Assert.Equal((0, "promised-jug", 4),
            (Order(world, receipt).CompletedUnits, Order(world, receipt).DeliveryLotId, Order(world, receipt).DeliveryQuantity));
        var saved = At(world.ExportState(), actor, Home(state, Clinic).Position);
        using var healthy = Restore(saved);
        await Tick(healthy);
        Assert.Equal(("finished", 4), (Order(healthy, receipt).Status, Order(healthy, receipt).CompletedUnits));
        Assert.Equal(4, Stored(healthy, Clinic, "fresh_water"));
        var damaged = saved.Society.Society.Inventory with
        {
            Lots = saved.Society.Society.Inventory.Lots.Select(lot =>
                brokenVessel && lot.Id == "promised-jug" ? lot with { ConditionBasisPoints = 0 } :
                !brokenVessel && lot.Id == "promised-water" ? lot with { FreshnessBasisPoints = 0 } : lot).ToArray(),
        };
        using var refused = Restore(WithInventory(saved, damaged));
        using var replay = Reload(refused);
        await TickTogether(refused, replay);
        Assert.Equal(("blocked", 0), (Order(refused, receipt).Status, Order(refused, receipt).CompletedUnits));
        Assert.Equal(0, Stored(refused, Clinic, "fresh_water"));
        Assert.Equal((actor, Clinic, 1), (refused.Society.Inventory.GetLot("promised-jug").OwnerId,
            refused.Society.Inventory.GetLot("promised-jug").DeliveryBuildingId, refused.Society.Inventory.GetLot("promised-jug").Quantity));
        Assert.Equal((actor, Clinic, 4), (refused.Society.Inventory.GetLot("promised-water").OwnerId,
            refused.Society.Inventory.GetLot("promised-water").DeliveryBuildingId, refused.Society.Inventory.GetLot("promised-water").Quantity));
        Assert.Null(Order(refused, receipt).LastEffectId);
        Assert.DoesNotContain(refused.ExportState().Events, item => item.Kind == "owner_stock_delivered");
    }
}
