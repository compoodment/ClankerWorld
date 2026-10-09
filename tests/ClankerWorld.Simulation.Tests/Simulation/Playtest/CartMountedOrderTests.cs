using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Fact]
    public async Task CartAttachmentOrderWaitsForANativeRiderToDismount()
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("cart-order-horse", "Moss", "horse", "female", -7L * day, yard.Position,
            "household:" + home, home, yard.InstanceId, CareUntilTick: day);
        var inventory = InventoryFixture.Relocate(InventoryFixture.AddLot(state.Society.Society.Inventory, "cart-saddle", "saddle", home, 1),
            "carry-cart-saddle", "cart-saddle", home, 1);
        inventory = InventoryFixture.AddLot(inventory, "mounted-cart", "handcart", actor, 1,
            groundPosition: new(yard.Position.X, yard.Position.Y));
        using var world = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, inventory, [horse]),
            id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
        world.SubmitInstruction(new("saddle-cart-horse", "owner:test", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(world, () => world.Animals.Single().SaddleLotId is not null);
        world.SubmitInstruction(new("mount-cart-horse", "owner:test", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(world, () => world.Animals.Single().RiderId == actor);
        var receipt = world.SubmitInstruction(new("mounted-attach", "owner:test", actor, OwnerInstructionKind.MustDo, "Pull handcart mounted-cart"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Contains("Dismount", order.BlockedReason!, StringComparison.Ordinal);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Equal(actor, world.Animals.Single().RiderId);
        Assert.Empty(world.ExportState().HandcartHitches!);
        using var loaded = PrivateWorldRuntime.Restore(world.ExportState(), _ => new AnimalChooser());
        loaded.Validate();
    }
}
