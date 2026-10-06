using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConstructionOrderTests
{
    [Fact]
    public async Task RestaurantOrderPaysItsExactCostAtItsNamedSiteAndReplaysTravelWorkAndCompletion()
    {
        var setup = await Baseline.Value;
        using var world = Restore(Decode(setup));
        var receipt = Submit(world, setup.Actor, "paid-restaurant", BuildRestaurant(setup.Site));
        Assert.Equal(("construct_building", "restaurant", "waiting"),
            (Order(world, receipt).Action, Order(world, receipt).TargetBuildingKind, Order(world, receipt).Status));
        await Tick(world);
        var started = Order(world, receipt);
        Assert.Equal((Alpha, setup.Site, 0),
            (started.ConstructionOwnerId, started.ConstructionPosition, started.CompletedUnits));
        Assert.NotEqual(setup.Site, Person(world, setup.Actor).Position);
        AssertInputs(world, 10, 4);
        using (var travelling = Reload(world))
        {
            for (var tick = 0; tick < 40 && Person(world, setup.Actor).Project!.WorkDone < 3; tick++)
                await TickTogether(world, travelling);
        }
        Assert.Equal("working", Person(world, setup.Actor).Project!.Stage);
        Assert.InRange(Person(world, setup.Actor).Project!.WorkDone, 3, 9);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertInputs(world, 10, 4);
        using var working = Reload(world);
        await FinishTogether(world, working, receipt);
        var completed = Order(world, receipt);
        var building = Assert.Single(world.WorldSimulation.Buildings,
            item => item.InstanceId == completed.ConstructionInstanceId);
        Assert.Equal((RestaurantContent.Restaurant1x2().CanonicalId, Alpha, setup.Site),
            (building.DefinitionId, building.HouseholdId, building.Position));
        Assert.Equal(("finished", 1), (completed.Status, completed.CompletedUnits));
        AssertInputs(world, 2, 2);
        var proof = Assert.Single(world.WorldSimulation.ConstructionReceipts!);
        Assert.Equal((receipt.InstructionId, setup.Actor, building.InstanceId, building.DefinitionId, Alpha, Alpha, setup.Site),
            (proof.InstructionId, proof.ActorId, proof.BuildingInstanceId, proof.DefinitionId, proof.OwnerId, proof.MaterialOwnerId, proof.Position));
        var paid = proof.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
        Assert.All(paid, item => Assert.Equal(InventoryReservationState.Completed, item.State));
        Assert.Equal(8, paid.Where(item => item.LotId == "construction-wood").Sum(item => item.Quantity));
        Assert.Equal(2, paid.Where(item => item.LotId == "construction-stone").Sum(item => item.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "build_completed");
        Assert.True(world.RemoveBuilding(building.InstanceId, building.TownId, Alpha).Applied);
        using var removed = Reload(world);
        await TickTogether(world, removed);
        Assert.Equal(("finished", 1, completed.LastEffectId),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).LastEffectId));
        AssertInputs(world, 2, 2);
        Assert.Single(world.WorldSimulation.ConstructionReceipts!);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == building.InstanceId);
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("foreign")]
    public async Task RestaurantOrdersCannotUseReservedOrForeignConstructionStock(string boundary)
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var inventory = state.Society.Society.Inventory;
        inventory = boundary == "reserved"
            ? InventoryFixture.Reserve(inventory, "restaurant-held", Alpha, "construction-wood", 10,
                "other_work", state.Society.Society.WorldTick + 100)
            : InventoryFixture.Transfer(inventory, "restaurant-foreign", Alpha, Beta, "construction-wood", 10,
                "other_household_stock", destinationStorageBuildingId: "first-town-house-b");
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, setup.Actor, "protected-restaurant", BuildRestaurant(setup.Site));
        Assert.Equal("construct_building", Order(world, receipt).Action);
        for (var tick = 0; tick < 3; tick++) await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).LastEffectId);
        Assert.DoesNotContain(world.WorldSimulation.Buildings,
            item => item.DefinitionId == RestaurantContent.Restaurant1x2().CanonicalId);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.Equal((boundary == "reserved" ? Alpha : Beta, 10),
            (world.Society.Inventory.GetLot("construction-wood").OwnerId, world.Society.Inventory.GetLot("construction-wood").Quantity));
        Assert.Equal(4, world.Society.Inventory.GetLot("construction-stone").Quantity);
        if (boundary == "reserved") Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation("restaurant-held").State);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task CancellingPartialRestaurantWorkPreservesStockAndOnlyTheQueuedOrderCanFinish()
    {
        var setup = await Baseline.Value;
        using var world = Restore(Decode(setup));
        var first = Submit(world, setup.Actor, "cancelled-restaurant", BuildRestaurant(setup.Site));
        Assert.Equal("construct_building", Order(world, first).Action);
        var queued = Submit(world, setup.Actor, "queued-restaurant", BuildRestaurant(setup.Site), queue: true);
        for (var tick = 0; tick < 40 && Person(world, setup.Actor).Project?.WorkDone is not >= 3; tick++)
            await Tick(world);
        Assert.InRange(Person(world, setup.Actor).Project!.WorkDone, 3, 9);
        Assert.Equal(("queued", 0), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.True(world.CancelOrder(new("stop-restaurant", "owner:test", world.Society.WorldId,
            setup.Actor, first.InstructionId)).Changed);
        Assert.Equal(("cancelled", 0), (Order(world, first).Status, Order(world, first).CompletedUnits));
        AssertInputs(world, 10, 4);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        using var replay = Reload(world);
        await FinishTogether(world, replay, queued);
        Assert.NotEqual(Order(world, first).ConstructionInstanceId, Order(world, queued).ConstructionInstanceId);
        Assert.Equal(("cancelled", 0), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Equal(queued.InstructionId, Assert.Single(world.WorldSimulation.ConstructionReceipts!).InstructionId);
        AssertInputs(world, 2, 2);
        Assert.DoesNotContain(world.WorldSimulation.Buildings,
            item => item.InstanceId == Order(world, first).ConstructionInstanceId);
    }

    private static string BuildRestaurant(GridPoint site) =>
        string.Create(CultureInfo.InvariantCulture, $"build one Restaurant at ({site.X}, {site.Y})");
}
