using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    public async Task ReplacingMilkCollectionDoesNotBlockCareForAnotherAnimal(bool finishFirst, bool cancelFirst, bool retainedTrip)
    {
        var (state, actor, home, yard) = CreateYard("animal-milk-sale");
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == home &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var tiles = WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition =>
            definition.CanonicalId == yard.DefinitionId), yard).ToArray();
        var day = state.WorldSystems!.Config.TicksPerDay;
        var tick = state.Society.Society.WorldTick;
        var born = -(long)AnimalRules.Definition("cow").AdultDays * day;
        var moss = new AnimalState("replacement-moss", "Moss", "cow", "female", born,
            tiles[0], "household:" + home, home, yard.InstanceId, CareUntilTick: tick + day,
            ProductProgressTicks: AnimalRules.Definition("cow").ProductDays * day - 1);
        var fern = new AnimalState("replacement-fern", "Fern", "cow", "female", born,
            tiles[1], "household:" + home, home, yard.InstanceId);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "replacement-empty-jug", "water_jug", home, 1, storageBuildingId: house.InstanceId);
        var ground = new InventoryGroundPosition(house.Position.X, house.Position.Y);
        inventory = InventoryFixture.AddLot(inventory, "replacement-feed", "grain", home, 6, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "replacement-water-jug", "water_jug", home, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "replacement-water", "fresh_water", home, 2, containerLotId: "replacement-water-jug");
        state = At(state, actor, house.Position, inventory, [moss, fern]) with
        {
            Inhabitants = At(state, actor, house.Position, inventory, [moss, fern]).Inhabitants.Select(person =>
            person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray()
        };
        using var setup = PrivateWorldRuntime.Restore(state, _ => new AnimalChooser());
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        Assert.NotNull(setup.Animals.Single(animal => animal.Id == moss.Id).ReadyProductLotId);
        using var collecting = RestoreAnimalOrder(setup.ExportState(), actor);
        var old = collecting.SubmitInstruction(new("replacement-collect", "owner", actor, OwnerInstructionKind.MustDo, "collect from Moss"));
        await Until(collecting, () => collecting.ExportState().AnimalWorld.SupplyTrips.Any(trip => trip.ActorId == actor && trip.AnimalId == moss.Id));
        var pickedTrips = collecting.ExportState().AnimalWorld.SupplyTrips;
        Assert.Equal((home, actor), (collecting.Society.Inventory.GetLot("replacement-empty-jug").OwnerId,
            collecting.Society.Inventory.GetLot("replacement-empty-jug").CarrierId));
        if (finishFirst) await Until(collecting, () => collecting.ExportState().Instructions!.Single(instruction => instruction.InstructionId == old.InstructionId).Order!.Status == "finished");
        if (cancelFirst) Assert.True(collecting.CancelOrder(new("replacement-cancel", "owner", collecting.Society.WorldId, actor, old.InstructionId)).Changed);
        var current = collecting.SubmitInstruction(new("replacement-care", "owner", actor, OwnerInstructionKind.MustDo, "care for Fern"));
        Assert.DoesNotContain(collecting.ExportState().AnimalWorld.SupplyTrips, trip => trip.ActorId == actor);
        var checkpoint = collecting.ExportState();
        // Before the repair, a valid saved replacement retained this actual native pickup trip.
        if (retainedTrip) checkpoint = checkpoint with { AnimalWorld = checkpoint.AnimalWorld with { SupplyTrips = pickedTrips } };
        var bytes = PrivateWorldRuntimeCodec.Encode(checkpoint);
        using var world = RestoreAnimalOrder(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreAnimalOrder(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var step = 0; step < 80 && world.ExportState().Instructions!.Single(instruction => instruction.InstructionId == current.InstructionId).Order!.Status != "finished"; step++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var final = world.ExportState();
        Assert.Equal(finishFirst ? "finished" : "cancelled", final.Instructions!.Single(instruction => instruction.InstructionId == old.InstructionId).Order!.Status);
        var order = final.Instructions!.Single(instruction => instruction.InstructionId == current.InstructionId).Order!;
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        Assert.True(world.Animals.Single(animal => animal.Id == fern.Id).CareUntilTick > world.WorldTick);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("replacement-feed", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "replacement-water");
        Assert.Equal(home, world.Society.Inventory.GetLot("replacement-empty-jug").OwnerId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("replacement-empty-jug").CarrierId);
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "milk").Sum(lot => lot.Quantity));
        Assert.Equal(!finishFirst, world.Animals.Single(animal => animal.Id == moss.Id).ReadyProductLotId is not null);
        Assert.DoesNotContain(final.AnimalWorld.SupplyTrips, trip => trip.ActorId == actor);
        bytes = PrivateWorldRuntimeCodec.Encode(final);
        using var reload = RestoreAnimalOrder(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        world.Validate();
        reload.Validate();
    }

    private static PrivateWorldRuntime RestoreAnimalOrder(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
}
