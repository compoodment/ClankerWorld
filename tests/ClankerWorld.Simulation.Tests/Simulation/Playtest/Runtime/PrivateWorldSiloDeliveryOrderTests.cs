using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldDeliveryOrderTests
{
    [Theory]
    [InlineData("potatoes", "Farmhouse")]
    [InlineData("grain", "Silo")]
    public async Task SiloFallbackDoesNotSubstituteAnotherItemOrHaulStockToItsOwnSourceAcrossReload(string kind, string destination)
    {
        var state = Prepared(SiloBaseline.Value);
        var actor = Actor(state);
        state = At(state, actor, Home(state, Silo).Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "ordered-silo-grain", "grain", Alpha, 8,
            storageBuildingId: Silo);
        using var world = RestoreSiloOrder(WithInventory(state, inventory), actor);
        var receipt = Submit(world, actor, "silo-constraint", "haul two " + kind + " to my " + destination);
        Assert.Equal(kind, Order(world, receipt).TargetItemKind);
        await Tick(world);
        Assert.Null(Order(world, receipt).DeliveryLotId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = RestoreSiloOrder(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 8; tick++) await TickTogether(world, replay);
        Assert.Equal(("blocked", 0, kind), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).TargetItemKind));
        Assert.Null(Order(world, receipt).DeliveryLotId);
        var grain = world.Society.Inventory.GetLot("ordered-silo-grain");
        Assert.Equal((Alpha, Silo, 8), (grain.OwnerId, grain.StorageBuildingId, grain.Quantity));
        Assert.Null(grain.DeliveryBuildingId);
        Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.LotId == grain.Id);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "owner_stock_picked_up" or "owner_stock_delivered");
        world.Validate();
    }

    [Fact]
    public async Task OrderedSiloGrainReachesTheFarmhouseInTheRequestedQuantityAcrossReplay()
    {
        var state = Prepared(SiloBaseline.Value);
        var actor = Actor(state);
        state = At(state, actor, Home(state, Silo).Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "ordered-silo-grain", "grain", Alpha, 8,
            storageBuildingId: Silo);
        using var world = RestoreSiloOrder(WithInventory(state, inventory), actor);
        var receipt = Submit(world, actor, "silo-farmhouse", "haul two grain to my Farmhouse");
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        var shipment = world.Society.Inventory.GetLot(Order(world, receipt).DeliveryLotId!);
        Assert.Equal(("grain", actor, Farmhouse, 2), (shipment.ItemKind, shipment.OwnerId, shipment.DeliveryBuildingId, shipment.Quantity));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = RestoreSiloOrder(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(2, Order(world, receipt).CompletedUnits);
        Assert.Equal(2, Stored(world, Farmhouse, "grain"));
        Assert.Equal(6, Stored(world, Silo, "grain"));
        Assert.Equal(8, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain" &&
            (lot.OwnerId == Alpha || lot.OwnerId == actor)).Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        world.Validate();
    }

    private static PrivateWorldRuntime RestoreSiloOrder(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? new DeterministicDecisionProvider() : new DeliveryChoices());
}
