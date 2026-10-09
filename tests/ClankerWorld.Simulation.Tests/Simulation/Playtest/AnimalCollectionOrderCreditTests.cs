using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MilkCollectionCreditsTheOrderWithAFetchedOrCarriedJug(bool alreadyCarried, bool repeat)
    {
        var (state, actor, home, yard) = CreateYard("animal-milk-sale");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var cow = new AnimalState("collection-credit-cow", "Moss", "cow", "female",
            -(long)AnimalRules.Definition("cow").AdultDays * day, yard.Position, "household:" + home, home,
            yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day,
            ProductProgressTicks: AnimalRules.Definition("cow").ProductDays * day - 1);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "collection-credit-jug", "water_jug", home, 1,
            storageBuildingId: yard.InstanceId);
        if (alreadyCarried)
            inventory = InventoryFixture.Relocate(inventory, "carry-credit-jug", "collection-credit-jug", home, 1, carrierId: actor);
        using var producing = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, inventory, [cow]), _ => new AnimalChooser());
        Assert.True((await producing.AdvanceOneTickAsync()).Advanced);
        var ready = producing.Animals.Single(animal => animal.Id == cow.Id);
        var productId = Assert.IsType<string>(ready.ReadyProductLotId);
        var reservationId = Assert.IsType<string>(ready.ReadyProductReservationId);
        var initial = PrivateWorldRuntimeCodec.Encode(producing.ExportState());
        using var world = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(initial), actor);
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var instruction = world.SubmitInstruction(new("collect-credit", "owner", actor, OwnerInstructionKind.MustDo,
            repeat ? "repeat collect from Moss" : "collect from Moss"));
        var queued = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(queued, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(queued), actor);
        for (var tick = 0; tick < 6; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Null(world.Animals.Single(animal => animal.Id == cow.Id).ReadyProductLotId);
        var collected = world.Society.Inventory.GetLot(productId);
        Assert.Equal((home, "milk", 2, "collection-credit-jug"),
            (collected.OwnerId, collected.ItemKind, collected.Quantity, collected.ContainerLotId));
        Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, collected, actor));
        Assert.Equal(actor, world.Society.Inventory.GetLot("collection-credit-jug").CarrierId);
        Assert.NotEqual(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(reservationId).State);
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!;
        Assert.Equal(1, order.CompletedUnits);
        if (repeat) Assert.NotEqual("finished", order.Status);
        else Assert.Equal("finished", order.Status);
        world.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(final), actor);
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        for (var tick = 0; tick < 2; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            Assert.Equal(1, loaded.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.CompletedUnits);
            Assert.Equal(2, loaded.Society.Inventory.GetLot(productId).Quantity);
        }
    }
}
