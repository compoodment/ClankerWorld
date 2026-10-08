using System.Globalization;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConstructionOrderTests
{
    [Fact]
    public async Task AFirstHouseConsumesOnlyUnpromisedWoodAndPreservesAnotherBuildingsDelivery()
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        const string destination = "paid-delivery-clinic";
        using (var clinic = Restore(state))
        {
            var placed = clinic.PlaceBuilding(destination, setup.DefinitionId, setup.Site, Alpha);
            Assert.True(placed.Applied, placed.Failure);
            state = clinic.ExportState();
        }
        var formerHouse = Home(state);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != Alpha && lot.OwnerId != setup.Actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "a-promised-house-wood", "wood", Alpha, 1, storageBuildingId: House);
        inventory = InventoryFixture.Transfer(inventory, "prepare-clinic-delivery", Alpha, setup.Actor,
            "a-promised-house-wood", 1, "household_stock_picked_up", destinationDeliveryBuildingId: destination);
        inventory = InventoryFixture.AddLot(inventory, "z-unpromised-house-wood", "wood", setup.Actor, 8);
        inventory = InventoryFixture.AddLot(inventory, "house-builder-basket", "basket", setup.Actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == setup.Actor
                ? person with { Equipment = new(CarryAidLotId: "house-builder-basket"), LastDecisionContext = null }
                : person).ToArray(),
        };
        using var world = Restore(state);
        var promised = world.Society.Inventory.GetLot("a-promised-house-wood");
        Assert.Equal(destination, promised.DeliveryBuildingId);
        Assert.True(world.RemoveBuilding(formerHouse.InstanceId, formerHouse.TownId, Alpha).Applied);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"build one House at ({formerHouse.Position.X}, {formerHouse.Position.Y})");
        var receipt = Submit(world, setup.Actor, "unpromised-first-house", text);
        await ReachWork(world, setup.Actor);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        var retained = world.Society.Inventory.GetLot(promised.Id);
        Assert.Equal(world.Society.Inventory.WorldTick, retained.LastProcessedTick);
        Assert.Equal(promised with { LastProcessedTick = retained.LastProcessedTick }, retained);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "z-unpromised-house-wood");
        Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.LotId == promised.Id);
        var proof = Assert.Single(world.WorldSimulation.ConstructionReceipts!);
        Assert.Equal((Alpha, setup.Actor), (proof.OwnerId, proof.MaterialOwnerId));
        var payment = Assert.Single(world.Society.Inventory.Reservations,
            reservation => proof.InputReservationIds.Contains(reservation.Id, StringComparer.Ordinal));
        Assert.Equal(("z-unpromised-house-wood", 8, InventoryReservationState.Completed),
            (payment.LotId, payment.Quantity, payment.State));
        using var restored = Reload(world);
        Assert.Equal(retained, restored.Society.Inventory.GetLot(promised.Id));
    }
}
