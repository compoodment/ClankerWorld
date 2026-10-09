using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(true, true, 5, "z-capacity-empty-jug")]
    [InlineData(false, true, 5, "z-capacity-empty-jug")]
    [InlineData(true, true, 3, "a-capacity-jug")]
    [InlineData(true, false, 5, null)]
    public async Task MilkCollectionChoosesAJugWithRoomForThePendingBatch(bool fullerJug, bool emptyJug, int stone, string? expectedJug)
    {
        var (state, actor, home, yard) = CreateYard("animal-milk-sale");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var tick = state.Society.Society.WorldTick;
        var cow = new AnimalState("capacity-cow", "Moss", "cow", "female",
            -(long)AnimalRules.Definition("cow").AdultDays * day, yard.Position, "household:" + home, home, yard.InstanceId,
            CareUntilTick: tick + day, ProductProgressTicks: AnimalRules.Definition("cow").ProductDays * day - 1);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        state = At(state, actor, yard.Position, inventory, [cow]);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Equipment = null } : person).ToArray(),
        };
        using var producing = PrivateWorldRuntime.Restore(state, _ => new AnimalChooser());
        Assert.True((await producing.AdvanceOneTickAsync()).Advanced);
        var productId = producing.Animals.Single().ReadyProductLotId;
        Assert.NotNull(productId);
        Assert.Equal(("milk", home, 2), (producing.Society.Inventory.GetLot(productId).ItemKind,
            producing.Society.Inventory.GetLot(productId).OwnerId, producing.Society.Inventory.GetLot(productId).Quantity));
        state = producing.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "capacity-stone", "stone", actor, stone);
        if (fullerJug)
        {
            inventory = InventoryFixture.AddLot(inventory, "a-capacity-jug", "water_jug", home, 1, storageBuildingId: yard.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "capacity-existing-milk", "milk", home, 2,
                storageBuildingId: yard.InstanceId, containerLotId: "a-capacity-jug");
        }
        if (emptyJug) inventory = InventoryFixture.AddLot(inventory, "z-capacity-empty-jug", "water_jug", home, 1, storageBuildingId: yard.InstanceId);
        state = At(state, actor, yard.Position, inventory, state.AnimalWorld.Animals.ToArray());
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = RestoreAnimalOrder(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.SubmitInstruction(new("capacity-collect", "owner", actor, OwnerInstructionKind.MustDo, "collect from Moss"));
        for (var step = 0; step < 40 && world.Animals.Single().ReadyProductLotId is not null; step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2 + (fullerJug ? 2 : 0), world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "milk").Sum(lot => lot.Quantity));
        Assert.Equal(stone, world.Society.Inventory.GetLot("capacity-stone").Quantity);
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "milk"), lot => Assert.Equal(home, lot.OwnerId));
        if (expectedJug is not null)
        {
            Assert.Null(world.Animals.Single().ReadyProductLotId);
            var jug = world.Society.Inventory.GetLot(expectedJug);
            Assert.Equal(home, jug.OwnerId);
            Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, jug, actor));
            Assert.Equal(expectedJug == "a-capacity-jug" ? 4 : 2,
                world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == expectedJug).Sum(lot => lot.Quantity));
            Assert.Contains(world.ExportState().Events, item => item.Kind == "animal_product_collected");
        }
        else
        {
            Assert.Equal(productId, world.Animals.Single().ReadyProductLotId);
            Assert.DoesNotContain(world.ExportState().AnimalWorld.SupplyTrips, trip => trip.ActorId == actor);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "animal_product_collected");
        }
        if (fullerJug && expectedJug != "a-capacity-jug")
        {
            Assert.Equal(yard.InstanceId, world.Society.Inventory.GetLot("a-capacity-jug").StorageBuildingId);
            Assert.Equal(2, world.Society.Inventory.GetLot("capacity-existing-milk").Quantity);
        }
        if (emptyJug && expectedJug != "z-capacity-empty-jug")
        {
            Assert.Equal(yard.InstanceId, world.Society.Inventory.GetLot("z-capacity-empty-jug").StorageBuildingId);
            Assert.Null(world.Society.Inventory.GetLot("z-capacity-empty-jug").CarrierId);
        }
        world.Validate();
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }
}
