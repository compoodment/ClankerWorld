using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false, "available")]
    [InlineData(true, "available")]
    [InlineData(true, "reserved")]
    [InlineData(true, "delivery")]
    public async Task CollectedMilkCanReturnToItsHouseDespiteAnAvailableStoreButProtectsCommittedCargo(bool hasStore, string boundary)
    {
        var (state, actor, home, yard) = CreateYard("milk-jug-return-audit");
        if (hasStore)
        {
            var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "store-1x1");
            var costs = state.Society.Society.Inventory;
            foreach (var cost in definition.BuildCosts)
                costs = InventoryFixture.AddLot(costs, "return-store-cost-" + cost.ResourceId, cost.ResourceId,
                    home, cost.Amount, groundPosition: new(yard.Position.X, yard.Position.Y));
            using var building = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, costs, []), _ => new AnimalChooser());
            foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, yard.Position)))
                if (building.PlaceBuilding("return-store", definition.CanonicalId, tile.Position, home).Applied) break;
            Assert.Single(building.WorldSimulation.Buildings, item => item.InstanceId == "return-store");
            state = building.ExportState();
        }
        var day = state.WorldSystems!.Config.TicksPerDay;
        // Bounded product clock, not a proof of the full daily care cycle. Collection remains native.
        var cow = new AnimalState("return-cow", "Moss", "cow", "female", -(long)AnimalRules.Definition("cow").AdultDays * day,
            yard.Position, "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day,
            ProductProgressTicks: AnimalRules.Definition("cow").ProductDays * day - 1);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "return-jug", "water_jug", home, 1,
            storageBuildingId: yard.InstanceId);
        using var collecting = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, inventory, [cow]),
            id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
        Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var milkId = Assert.Single(collecting.Animals).ReadyProductLotId!;
        Assert.NotNull(milkId);
        collecting.SubmitInstruction(new("collect-return-milk", "owner", actor, OwnerInstructionKind.MustDo, "collect from Moss"));
        await Until(collecting, () => collecting.Animals.Single().ReadyProductLotId is null);
        inventory = collecting.Society.Inventory;
        Assert.Equal((home, actor, 2, "return-jug"), (inventory.GetLot(milkId).OwnerId,
            inventory.GetLot(milkId).CarrierId, inventory.GetLot(milkId).Quantity, inventory.GetLot(milkId).ContainerLotId));
        var house = collecting.WorldSimulation.Buildings.Single(item => item.HouseholdId == home &&
            collecting.WorldContent.Buildings.Single(definition => definition.CanonicalId == item.DefinitionId).Tags.Contains("house"));
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "return-held-milk", home, milkId, 1, "other_work", collecting.WorldTick + day);
        if (boundary == "delivery")
            inventory = InventoryFixture.Transfer(inventory, "return-milk-promise", home, actor, "return-jug", 1,
                "delivery", destinationDeliveryBuildingId: "return-store");
        state = collecting.ExportState() with
        { Society = collecting.ExportState().Society with { Society = collecting.Society with { Inventory = inventory } } };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? new AnimalChooser("return_borrowed") : new AnimalChooser());
        world.SubmitInstruction(new("return-collected-jug", "owner", actor, OwnerInstructionKind.MustDo, "return one borrowed water jug"));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? new AnimalChooser("return_borrowed") : new AnimalChooser());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 60; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var order = world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "return-collected-jug").Order!;
            if (order.Status == "finished" || boundary != "available" && tick >= 3) break;
        }
        var result = world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "return-collected-jug").Order!;
        var jug = world.Society.Inventory.GetLot("return-jug");
        var milk = world.Society.Inventory.GetLot(milkId);
        var owner = boundary == "delivery" ? actor : home;
        Assert.Equal((owner, 1, owner, 2, jug.Id), (jug.OwnerId, jug.Quantity, milk.OwnerId, milk.Quantity, milk.ContainerLotId));
        Assert.True(jug.ConditionBasisPoints > 0);
        Assert.True(milk.FreshnessBasisPoints > 0);
        if (boundary == "available")
        {
            Assert.Equal(("finished", 1), (result.Status, result.CompletedUnits));
            Assert.Equal((house.InstanceId, (string?)null, house.InstanceId, (string?)null),
                (jug.StorageBuildingId, jug.CarrierId, milk.StorageBuildingId, milk.CarrierId));
        }
        else
        {
            Assert.Equal(("blocked", 0), (result.Status, result.CompletedUnits));
            Assert.Equal((inventory.GetLot("return-jug").CarrierId, inventory.GetLot(milkId).CarrierId, (string?)null, (string?)null),
                (jug.CarrierId, milk.CarrierId, jug.StorageBuildingId, milk.StorageBuildingId));
            Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, milk, actor));
            if (boundary == "reserved") Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("return-held-milk").State);
            else Assert.Equal("return-store", jug.DeliveryBuildingId);
        }
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new AnimalChooser());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        world.Validate();
    }
}
