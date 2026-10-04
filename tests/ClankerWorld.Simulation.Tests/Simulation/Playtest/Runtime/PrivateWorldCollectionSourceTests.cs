using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldCollectionOrderTests
{
    [Theory]
    [InlineData("collect two wood from (12, 4)")]
    [InlineData("collect two wood at tile 12,4")]
    [InlineData("collect my two wood from tile (12, 4)")]
    [InlineData("please collect two wood at 12/4.")]
    public void CollectionSourceCoordinatesAreRecognizedWithoutGuessing(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var order = Order(world, Submit(world, Actor(state), "source-text", text));
        Assert.Equal(("collect_material", "wood", 2, new GridPoint(12, 4)),
            (order.Action, order.TargetMaterialKind, order.RequestedUnits, order.TargetPosition));
        Assert.Null(order.TargetResourceId);
    }

    [Fact]
    public async Task CollectionSourceOverridesNearerGoodsAndSurvivesTravelQuantityAndReload()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var position = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-nearer", "wood", actor, 3,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "collect-source-a", "wood", actor, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "collect-source-b", "wood", actor, 2, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "source", $"collect three wood from ({source.X}, {source.Y})");
        Assert.Equal("waiting", Order(world, receipt).Status);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.NotEqual(position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 20 && Order(world, receipt).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal(("finished", 3, source),
            (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits, Order(restored, receipt).TargetPosition));
        var nearby = restored.Society.Inventory.GetLot("collect-nearer");
        Assert.Equal(3, nearby.Quantity);
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), nearby.GroundPosition);
        Assert.Equal(1, restored.Society.Inventory.GetLot("collect-source-b").Quantity);
        Assert.Equal(House, restored.Society.Inventory.GetLot("collect-source-b").StorageBuildingId);
        var projected = Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal((source.X, source.Y), (projected.TargetX, projected.TargetY));
        restored.Validate();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("other-owner")]
    [InlineData("reserved")]
    [InlineData("promised")]
    [InlineData("outside-map")]
    public async Task CollectionSourceCannotFallBackToOtherGoods(string boundary)
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var source = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-elsewhere", "clay", actor, 3, storageBuildingId: House);
        if (boundary is not ("empty" or "outside-map"))
        {
            inventory = InventoryFixture.AddLot(inventory, "collect-unavailable", "clay",
                boundary == "other-owner" ? Household : actor, 2, groundPosition: new(source.X, source.Y));
            if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory, "source-reserved", actor,
                "collect-unavailable", 2, "other_work", 120);
            if (boundary == "promised") inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == "collect-unavailable" ? lot with
                {
                    GroundPosition = null,
                    CarrierId = actor,
                    DeliveryBuildingId = House,
                } : lot).ToArray(),
            };
        }
        if (boundary == "outside-map") source = new(-1, source.Y);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "unavailable-source", $"collect clay at ({source.X}, {source.Y})");
        Assert.Equal("waiting", Order(world, receipt).Status);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains(boundary == "outside-map" ? "outside" : "requested tile", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(House, world.Society.Inventory.GetLot("collect-elsewhere").StorageBuildingId);
        Assert.Equal(3, world.Society.Inventory.GetLot("collect-elsewhere").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(source, Order(restored, receipt).TargetPosition);
        Assert.Equal("blocked", Order(restored, receipt).Status);
    }

    [Fact]
    public async Task CollectionSourceKeepsItsRemainingQuantityWhenTheTileRunsOut()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var source = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-first", "stone", actor, 2,
            groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, "collect-elsewhere", "stone", actor, 3, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "partial-source", $"collect three stone from ({source.X}, {source.Y})");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        inventory = InventoryFixture.AddLot(saved.Society.Society.Inventory, "collect-replenished", "stone", actor, 2,
            groundPosition: new(source.X, source.Y));
        using var restored = Restore(WithInventory(saved, inventory));
        for (var tick = 0; tick < 65 && Order(restored, receipt).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("finished", 3, source),
            (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits, Order(restored, receipt).TargetPosition));
        Assert.Equal(1, restored.Society.Inventory.GetLot("collect-replenished").Quantity);
        Assert.Equal(3, restored.Society.Inventory.GetLot("collect-elsewhere").Quantity);
        Assert.Equal(House, restored.Society.Inventory.GetLot("collect-elsewhere").StorageBuildingId);
        Assert.Equal(2, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_collected"));
        restored.Validate();
    }

    [Fact]
    public async Task CollectionSourceDoesNotFollowGoodsMovedAwayDuringTravel()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var originalPosition = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-moving", "wood", actor, 2, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "moved-source", $"collect wood from ({source.X}, {source.Y})");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        var saved = world.ExportState();
        inventory = InventoryFixture.Relocate(saved.Society.Society.Inventory, "move-source-away", "collect-moving", actor, 2,
            groundPosition: new(originalPosition.X, originalPosition.Y));
        using var restored = Restore(WithInventory(saved, inventory));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0, source),
            (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits, Order(restored, receipt).TargetPosition));
        Assert.NotNull(restored.Society.Inventory.GetLot("collect-moving").GroundPosition);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "personal_goods_collected");
    }

    [Fact]
    public async Task CollectionSourceQueueAndCancellationKeepEachOrdersOwnTile()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var firstSource = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var secondSource = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-queue-first", "clay", actor, 1,
            groundPosition: new(firstSource.X, firstSource.Y));
        inventory = InventoryFixture.AddLot(inventory, "collect-queue-second", "clay", actor, 1, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "first-source", $"collect clay from ({firstSource.X}, {firstSource.Y})");
        var second = Submit(world, actor, "second-source", $"collect clay from ({secondSource.X}, {secondSource.Y})", queue: true);
        Assert.Equal("queued", Order(world, second).Status);
        world.Pause();
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(firstSource, Order(restored, first).TargetPosition);
        Assert.Equal(secondSource, Order(restored, second).TargetPosition);
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        restored.Resume();
        var before = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        Assert.False((await restored.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(restored, first).Status);
        Assert.Equal(0, Order(restored, second).CompletedUnits);
        Assert.True(restored.CancelOrder(new("cancel-source", "owner:test", restored.Society.WorldId, actor, second.InstructionId)).Changed);
        using var cancelled = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState())));
        for (var tick = 0; tick < 3; tick++) Assert.True((await cancelled.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("cancelled", Order(cancelled, second).Status);
        Assert.Equal(House, cancelled.Society.Inventory.GetLot("collect-queue-second").StorageBuildingId);
        Assert.Single(cancelled.ExportState().Events, item => item.Kind == "personal_goods_collected");
    }
}
