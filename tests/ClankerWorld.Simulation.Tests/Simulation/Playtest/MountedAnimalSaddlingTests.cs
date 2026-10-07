using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARiderCanSaddleAnotherHorse(bool dismountFirst)
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var tiles = WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition =>
            definition.CanonicalId == yard.DefinitionId), yard).ToArray();
        var targetPosition = tiles.First(point => point != yard.Position && !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point));
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("mounted-saddle-horse", "Moss", "horse", "female", -(long)AnimalRules.Definition("horse").AdultDays * day,
            yard.Position, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day);
        var target = horse with { Id = "mounted-saddle-target", Name = "Fern", Position = targetPosition };
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "00-mounted-saddle", "saddle", home, 1, groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.Relocate(InventoryFixture.AddLot(inventory, "01-target-saddle", "saddle", home, 1),
            "carry-target-saddle", "01-target-saddle", home, 1, carrierId: actor);
        state = At(state, actor, yard.Position, inventory, [horse, target]);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray() };
        using var preparing = RestoreMountedSaddling(state, actor);
        preparing.SubmitInstruction(new("prepare-saddle", "owner", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).SaddleLotId is not null);
        Assert.Equal("00-mounted-saddle", preparing.Animals.Single(animal => animal.Id == horse.Id).SaddleLotId);
        preparing.SubmitInstruction(new("prepare-mount", "owner", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor);
        if (dismountFirst)
        {
            preparing.SubmitInstruction(new("prepare-dismount", "owner", actor, OwnerInstructionKind.MustDo, "dismount Moss"));
            await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId is null);
        }
        var instruction = preparing.SubmitInstruction(new("saddle-other-horse", "owner", actor, OwnerInstructionKind.MustDo, "saddle Fern"));
        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var world = RestoreMountedSaddling(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreMountedSaddling(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 8 && world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.Status != "finished"; tick++)
        {
            var wasRiding = world.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (wasRiding && world.Animals.Single(animal => animal.Id == horse.Id).RiderId is null)
            {
                Assert.Null(world.Animals.Single(animal => animal.Id == target.Id).SaddleLotId);
                Assert.Equal(actor, world.Society.Inventory.GetLot("01-target-saddle").CarrierId);
                Assert.Equal(0, world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!.CompletedUnits);
                Assert.Equal(horse.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
            }
        }
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == instruction.InstructionId).Order!;
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        Assert.Equal("01-target-saddle", world.Animals.Single(animal => animal.Id == target.Id).SaddleLotId);
        Assert.Equal(targetPosition, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Null(world.Animals.Single(animal => animal.Id == horse.Id).RiderId);
        Assert.Equal(horse.Position, world.Animals.Single(animal => animal.Id == horse.Id).Position);
        foreach (var animal in world.Animals)
        {
            var saddle = world.Society.Inventory.GetLot(animal.SaddleLotId!);
            Assert.Equal((home, 1, new InventoryGroundPosition(animal.Position.X, animal.Position.Y)),
                (saddle.OwnerId, saddle.Quantity, saddle.GroundPosition));
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(animal.SaddleReservationId!).State);
        }
        using var loaded = RestoreMountedSaddling(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor);
        loaded.Validate();
        world.Validate();
    }

    private static PrivateWorldRuntime RestoreMountedSaddling(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
}
