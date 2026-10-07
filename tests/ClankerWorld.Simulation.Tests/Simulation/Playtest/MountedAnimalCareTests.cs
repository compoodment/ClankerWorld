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
    public async Task ARiderCanPrepareAndCompleteCareForAnotherAnimal(bool dismountFirst)
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var tiles = WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition =>
            definition.CanonicalId == yard.DefinitionId), yard).ToArray();
        var cowPosition = tiles.First(point => point != yard.Position && !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point));
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("mounted-care-horse", "Moss", "horse", "female", -(long)AnimalRules.Definition("horse").AdultDays * day,
            yard.Position, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day);
        var cow = new AnimalState("mounted-care-cow", "Fern", "cow", "female", -(long)AnimalRules.Definition("cow").AdultDays * day,
            cowPosition, "household:" + home, home, yard.InstanceId);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "mounted-care-saddle", "saddle", home, 1, groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "mounted-care-feed", "grain", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "mounted-care-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "mounted-care-water", "fresh_water", actor, 2, containerLotId: "mounted-care-jug");
        state = At(state, actor, yard.Position, inventory, [horse, cow]) with
        { Inhabitants = At(state, actor, yard.Position, inventory, [horse, cow]).Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray() };
        using var preparing = RestoreMountedCare(state, actor);
        var saddle = preparing.SubmitInstruction(new("mounted-care-saddle", "owner", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).SaddleLotId is not null);
        Assert.Equal("finished", preparing.ExportState().Instructions!.Single(item => item.InstructionId == saddle.InstructionId).Order!.Status);
        Assert.Equal("mounted-care-saddle", preparing.Animals.Single(animal => animal.Id == horse.Id).SaddleLotId);
        Assert.Equal(0, PersonalEquipmentRules.AvailableQuantity(preparing.Society.Inventory, preparing.Society.Inventory.GetLot("mounted-care-saddle")));
        var mount = preparing.SubmitInstruction(new("mounted-care-mount", "owner", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor);
        Assert.Equal("finished", preparing.ExportState().Instructions!.Single(item => item.InstructionId == mount.InstructionId).Order!.Status);
        Assert.Empty(preparing.ExportState().AnimalWorld.SupplyTrips);
        if (dismountFirst)
        {
            preparing.SubmitInstruction(new("mounted-care-dismount", "owner", actor, OwnerInstructionKind.MustDo, "dismount Moss"));
            await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId is null);
        }
        var care = preparing.SubmitInstruction(new("mounted-care-cow", "owner", actor, OwnerInstructionKind.MustDo, "care for Fern"));
        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var world = RestoreMountedCare(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreMountedCare(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 60 && world.ExportState().Instructions!.Single(item => item.InstructionId == care.InstructionId).Order!.Status != "finished"; tick++)
        {
            var wasRiding = world.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (wasRiding && world.Animals.Single(animal => animal.Id == horse.Id).RiderId is null)
            {
                Assert.Equal(0, world.ExportState().Instructions!.Single(item => item.InstructionId == care.InstructionId).Order!.CompletedUnits);
                Assert.Equal(0, world.Animals.Single(animal => animal.Id == cow.Id).CareUntilTick);
                Assert.Equal(2, world.Society.Inventory.GetLot("mounted-care-feed").Quantity);
                Assert.Equal(2, world.Society.Inventory.GetLot("mounted-care-water").Quantity);
                Assert.Equal(horse.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
            }
        }
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == care.InstructionId).Order!;
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        var cared = world.Animals.Single(animal => animal.Id == cow.Id);
        var parked = world.Animals.Single(animal => animal.Id == horse.Id);
        Assert.True(cared.CareUntilTick > world.WorldTick);
        Assert.True(parked.CareUntilTick > world.WorldTick);
        Assert.Null(parked.RiderId);
        Assert.Equal(horse.Position, parked.Position);
        Assert.Equal(cowPosition, cared.Position);
        Assert.Equal(cowPosition, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "mounted-care-feed" or "mounted-care-water");
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot("mounted-care-jug").OwnerId, world.Society.Inventory.GetLot("mounted-care-jug").Quantity));
        Assert.Equal((home, 1, new InventoryGroundPosition(parked.Position.X, parked.Position.Y)),
            (world.Society.Inventory.GetLot("mounted-care-saddle").OwnerId, world.Society.Inventory.GetLot("mounted-care-saddle").Quantity,
                world.Society.Inventory.GetLot("mounted-care-saddle").GroundPosition));
        Assert.Empty(world.ExportState().AnimalWorld.SupplyTrips);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = RestoreMountedCare(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        world.Validate();
        reload.Validate();
    }

    private static PrivateWorldRuntime RestoreMountedCare(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
}
