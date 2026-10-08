using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData("sheep", false, false)]
    [InlineData("sheep", true, false)]
    [InlineData("cow", false, false)]
    [InlineData("cow", true, false)]
    [InlineData("sheep", false, true)]
    public async Task ARiderCanCollectFromAnotherAnimal(string species, bool dismountFirst, bool fullMountedLoad)
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var tiles = WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition =>
            definition.CanonicalId == yard.DefinitionId), yard).ToArray();
        var sheepPosition = tiles.First(point => point != yard.Position && !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point));
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("mounted-collect-horse", "Moss", "horse", "female", -(long)AnimalRules.Definition("horse").AdultDays * day,
            yard.Position, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day);
        var sheep = new AnimalState("mounted-collect-product-animal", "Fern", species, "female", -(long)AnimalRules.Definition(species).AdultDays * day,
            sheepPosition, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day,
            ProductProgressTicks: AnimalRules.Definition(species).ProductDays * day - 1);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "mounted-collect-saddle", "saddle", home, 1, groundPosition: new(yard.Position.X, yard.Position.Y));
        if (species == "cow")
            inventory = InventoryFixture.Relocate(InventoryFixture.AddLot(inventory, "mounted-collect-jug", "water_jug", home, 1),
                "mounted-collect-carry-jug", "mounted-collect-jug", home, 1, carrierId: actor);
        state = At(state, actor, yard.Position, inventory, [horse, sheep]);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray() };
        using var preparing = RestoreMountedCollection(state, actor);
        preparing.SubmitInstruction(new("mounted-collect-saddle", "owner", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).SaddleLotId is not null);
        preparing.SubmitInstruction(new("mounted-collect-mount", "owner", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor);
        Assert.NotNull(preparing.Animals.Single(animal => animal.Id == sheep.Id).ReadyProductLotId);
        var ready = preparing.Animals.Single(animal => animal.Id == sheep.Id);
        var productId = ready.ReadyProductLotId!;
        var productHeld = ready.ReadyProductReservationId!;
        if (dismountFirst)
        {
            preparing.SubmitInstruction(new("mounted-collect-dismount", "owner", actor, OwnerInstructionKind.MustDo, "dismount Moss"));
            await Until(preparing, () => preparing.Animals.Single(animal => animal.Id == horse.Id).RiderId is null);
        }
        var collect = preparing.SubmitInstruction(new("mounted-collect-wool", "owner", actor, OwnerInstructionKind.MustDo, "collect from Fern"));
        var checkpoint = preparing.ExportState();
        if (fullMountedLoad)
            checkpoint = checkpoint with
            {
                Society = checkpoint.Society with
                {
                    Society = checkpoint.Society.Society with
                    {
                        Inventory = InventoryFixture.AddLot(checkpoint.Society.Society.Inventory, "mounted-collect-stone", "stone", actor, 11)
                    }
                }
            };
        var bytes = PrivateWorldRuntimeCodec.Encode(checkpoint);
        using var world = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 8 && world.ExportState().Instructions!.Single(item => item.InstructionId == collect.InstructionId).Order!.Status != "finished"; tick++)
        {
            var wasRiding = world.Animals.Single(animal => animal.Id == horse.Id).RiderId == actor;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (wasRiding && world.Animals.Single(animal => animal.Id == horse.Id).RiderId is null)
            {
                Assert.Equal(0, world.ExportState().Instructions!.Single(item => item.InstructionId == collect.InstructionId).Order!.CompletedUnits);
                Assert.Equal(productId, world.Animals.Single(animal => animal.Id == sheep.Id).ReadyProductLotId);
                Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(productHeld).State);
                Assert.Equal(horse.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
            }
        }
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == collect.InstructionId).Order!;
        Assert.Null(world.Animals.Single(animal => animal.Id == horse.Id).RiderId);
        Assert.Equal(horse.Position, world.Animals.Single(animal => animal.Id == horse.Id).Position);
        Assert.Equal((home, new InventoryGroundPosition(horse.Position.X, horse.Position.Y)),
            (world.Society.Inventory.GetLot("mounted-collect-saddle").OwnerId, world.Society.Inventory.GetLot("mounted-collect-saddle").GroundPosition));
        Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(world.Animals.Single(animal => animal.Id == horse.Id).SaddleReservationId!).State);
        if (fullMountedLoad)
        {
            Assert.NotEqual("finished", order.Status);
            Assert.Equal(0, order.CompletedUnits);
            Assert.Equal(productId, world.Animals.Single(animal => animal.Id == sheep.Id).ReadyProductLotId);
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(productHeld).State);
            var product = world.Society.Inventory.GetLot(productId);
            Assert.Equal((home, 2, new InventoryGroundPosition(sheepPosition.X, sheepPosition.Y)),
                (product.OwnerId, product.Quantity, product.GroundPosition));
            Assert.Equal(11, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone").Sum(lot => lot.Quantity));
            Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
            Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone" &&
                lot.GroundPosition == new InventoryGroundPosition(horse.Position.X, horse.Position.Y)).Sum(lot => lot.Quantity));
            using var fullReload = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor);
            fullReload.Validate();
            world.Validate();
            return;
        }
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        var collected = world.Society.Inventory.GetLot(productId);
        Assert.Equal((home, 2), (collected.OwnerId, collected.Quantity));
        Assert.Equal(AnimalRules.Definition(species).Product, collected.ItemKind);
        Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, collected, actor));
        Assert.NotEqual(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(productHeld).State);
        if (species == "cow")
        {
            Assert.Equal("mounted-collect-jug", collected.ContainerLotId);
            Assert.Equal((home, actor), (world.Society.Inventory.GetLot("mounted-collect-jug").OwnerId,
                world.Society.Inventory.GetLot("mounted-collect-jug").CarrierId));
        }
        else Assert.Null(collected.ContainerLotId);
        Assert.Null(world.Animals.Single(animal => animal.Id == sheep.Id).ReadyProductLotId);
        Assert.Equal(sheepPosition, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        using var loaded = RestoreMountedCollection(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor);
        loaded.Validate();
        world.Validate();
    }

    private static PrivateWorldRuntime RestoreMountedCollection(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
}
