using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldDeliveryOrderTests
{
    [Theory]
    [InlineData("matching")]
    [InlineData("other-kind")]
    [InlineData("other-destination")]
    public async Task ExistingPromisedCargoIsAdoptedOnlyForItsExactKindAndDestination(string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = Home(state, House);
        state = At(state, actor, SourceNear(state, house.Position));
        var kind = boundary == "other-kind" ? "wood" : "cloth";
        var destination = boundary == "other-destination" ? Farmhouse : House;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "prior-shipment", kind, Alpha, 2);
        inventory = InventoryFixture.Transfer(inventory, "previous-native-pickup", Alpha, actor, "prior-shipment", 2,
            "household_stock_picked_up", destinationDeliveryBuildingId: destination);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "adopt-delivery", "haul two cloth to my House");
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_picked_up");
        using var replay = Reload(world);
        if (boundary == "matching")
        {
            Assert.Equal(("prior-shipment", 2), (Order(world, receipt).DeliveryLotId, Order(world, receipt).DeliveryQuantity));
            await FinishTogether(world, replay, receipt);
            Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Equal((Alpha, House), (world.Society.Inventory.GetLot("prior-shipment").OwnerId,
                world.Society.Inventory.GetLot("prior-shipment").StorageBuildingId));
            Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        }
        else
        {
            await TickTogether(world, replay);
            Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Null(Order(world, receipt).DeliveryLotId);
            Assert.Equal((actor, destination, 2), (world.Society.Inventory.GetLot("prior-shipment").OwnerId,
                world.Society.Inventory.GetLot("prior-shipment").DeliveryBuildingId, world.Society.Inventory.GetLot("prior-shipment").Quantity));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        }
    }

    [Fact]
    public async Task DestinationReassignmentAfterBindingBlocksPersonalFoodWithoutRedirectingItsTransfer()
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = Home(state, House);
        state = At(state, actor, SourceNear(state, house.Position));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != "first-town-house-b").ToArray(),
        }, "bound-food", "berries", actor, 3);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "bound-food-delivery", "deliver two berries to my House");
        await Tick(world);
        Assert.Equal((House, Alpha, house.Position, "bound-food", 0),
            (Order(world, receipt).TargetStorageBuildingId, Order(world, receipt).TargetStorageOwnerId,
                Order(world, receipt).TargetStoragePosition, Order(world, receipt).DeliveryLotId, Order(world, receipt).CompletedUnits));
        var otherHouse = Home(state, "first-town-house-b");
        var removed = world.RemoveBuilding(otherHouse.InstanceId, otherHouse.TownId, Beta);
        Assert.True(removed.Applied, removed.Failure);
        var reassigned = world.ReassignBuilding(House, house.TownId, Alpha, null, Beta);
        Assert.True(reassigned.Applied, reassigned.Failure);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0, House, Alpha), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits,
            Order(world, receipt).TargetStorageBuildingId, Order(world, receipt).TargetStorageOwnerId));
        Assert.Equal((actor, 3), (world.Society.Inventory.GetLot("bound-food").OwnerId, world.Society.Inventory.GetLot("bound-food").Quantity));
        Assert.Equal(0, Stored(world, House, "berries"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
    }

    [Fact]
    public async Task AnUrgentMealPausesTheShipmentWithoutCreditingItAndResumesTheSameCargoAfterReload()
    {
        var state = ClothAtSource();
        var actor = Actor(state);
        using var initial = Restore(state);
        var receipt = Submit(initial, actor, "urgent-delivery", "haul two cloth to my House");
        await Tick(initial);
        var shipment = Order(initial, receipt).DeliveryLotId!;
        state = initial.ExportState();
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "urgent-berries", "berries", actor, 1)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        using var world = Restore(state);
        await Tick(world);
        Assert.Equal(("interrupted", 0, shipment), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).DeliveryLotId));
        Assert.Equal((actor, House), (world.Society.Inventory.GetLot(shipment).OwnerId, world.Society.Inventory.GetLot(shipment).DeliveryBuildingId));
        Assert.Equal(0, Stored(world, House, "cloth"));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(2, Order(world, receipt).CompletedUnits);
        Assert.Equal((Alpha, House), (world.Society.Inventory.GetLot(shipment).OwnerId, world.Society.Inventory.GetLot(shipment).StorageBuildingId));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "urgent-berries");
    }

    [Fact]
    public async Task CancellationLeavesThePickedUpPromiseAndOnlyTheQueuedOrdersActualDeliveryEarnsCredit()
    {
        var state = ClothAtSource();
        var actor = Actor(state);
        using var world = Restore(state);
        var cancelled = Submit(world, actor, "first-delivery", "haul two cloth to my House");
        var queued = Submit(world, actor, "queued-delivery", "haul two cloth to my House", queue: true);
        await Tick(world);
        var shipment = Order(world, cancelled).DeliveryLotId!;
        Assert.Equal(("queued", 0), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.True(world.CancelOrder(new("stop-delivery", "owner:test", world.Society.WorldId, actor, cancelled.InstructionId)).Changed);
        Assert.Equal((actor, House, 2), (world.Society.Inventory.GetLot(shipment).OwnerId,
            world.Society.Inventory.GetLot(shipment).DeliveryBuildingId, world.Society.Inventory.GetLot(shipment).Quantity));
        Assert.Equal(0, Stored(world, House, "cloth"));
        using var replay = Reload(world);
        await FinishTogether(world, replay, queued);
        Assert.Equal(("cancelled", 0), (Order(world, cancelled).Status, Order(world, cancelled).CompletedUnits));
        Assert.Null(Order(world, cancelled).LastEffectId);
        Assert.Equal(("finished", 2), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_picked_up");
        Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        Assert.Equal(2, Stored(world, House, "cloth"));
    }

    [Fact]
    public async Task RepeatingDeliveryCountsCompletedLoadsAndKeepsItsQueueWaitingAcrossReload()
    {
        var state = ClothAtSource(8);
        var actor = Actor(state);
        using var world = Restore(state);
        var repeating = Submit(world, actor, "repeat-delivery", "keep hauling cloth to my House");
        var queued = Submit(world, actor, "after-repeat", "haul one cloth to my House", queue: true);
        for (var tick = 0; tick < 20 && Order(world, repeating).CompletedUnits < 2; tick++) await Tick(world);
        Assert.Equal(("delivery_loads", 2, true),
            (Order(world, repeating).ProgressUnit, Order(world, repeating).CompletedUnits, Order(world, repeating).RepeatUntilCancelled));
        Assert.NotEqual("finished", Order(world, repeating).Status);
        Assert.Equal(("queued", 0), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.Equal(8, Stored(world, House, "cloth"));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(2, Order(world, repeating).CompletedUnits);
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind == "owner_stock_delivered"));
    }

    [Fact]
    public async Task ADelayedModelReplyCannotReviveACancelledDeliveryOrInventACompletedShipment()
    {
        var state = ClothAtSource();
        var actor = Actor(state);
        var provider = new DeliveryChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new DeliveryChoices());
        var receipt = Submit(world, actor, "held-delivery", "haul two cloth to my House");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(world.CancelOrder(new("cancel-held-delivery", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Null(Order(world, receipt).LastEffectId);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
            Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "cloth" &&
                (lot.OwnerId == Alpha || lot.OwnerId == actor)).Sum(lot => lot.Quantity));
            using var restored = Reload(world);
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task StrictReloadRejectsMalformedShipmentBindingsAndAnUnsupportedOrderNeverQueriesTheModel()
    {
        var state = ClothAtSource();
        var actor = Actor(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "validated-delivery", "haul two cloth to my House");
        await Tick(world);
        var saved = world.ExportState();
        var order = Order(world, receipt);
        foreach (var bad in new[]
        {
            order with { DeliveryQuantity = 3 },
            order with { DeliveryQuantity = null },
            order with { DeliveryRoute = "household_food" },
            order with { TargetStorageOwnerId = "household:missing" },
            order with { TargetStoragePosition = null },
        })
            Assert.Throws<InvalidDataException>(() => Restore(saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                    ? item with { Order = bad } : item).ToArray(),
            }));
        var provider = new DeliveryChoices(DecisionProviderKind.LargeLanguageModel);
        using var unsupported = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new DeliveryChoices());
        var unknown = Submit(unsupported, actor, "unsupported-delivery", "supply two photons to my Clinic");
        Assert.Equal("not_understood", Order(unsupported, unknown).Status);
        Assert.Empty(provider.Requests);
        Assert.Contains(unknown.InstructionId, unsupported.ExportState().CompletedInstructionIds!);
        Assert.DoesNotContain(unsupported.ExportState().Events, item => item.Kind is "owner_stock_picked_up" or "owner_stock_delivered");
    }

    private static PrivateWorldRuntimeState ClothAtSource(int quantity = 2)
    {
        var state = Prepared();
        var actor = Actor(state);
        var source = SourceNear(state, Home(state, House).Position);
        return At(WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "delivery-cloth", "cloth", Alpha, quantity, groundPosition: new(source.X, source.Y))), actor, source);
    }
}
