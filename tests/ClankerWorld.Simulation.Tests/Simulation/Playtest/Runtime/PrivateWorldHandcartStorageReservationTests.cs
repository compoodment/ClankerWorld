using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldDeliveryOrderTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("handcart")]
    [InlineData("iron-fittings")]
    public async Task GroundHandcartOutputLeavesTheLastRealBlacksmithStorageUnitAvailable(string productionKind)
    {
        var runningCart = productionKind == "handcart";
        var storedOutput = productionKind == "iron-fittings";
        var state = Prepared(household: Beta);
        var builder = Actor(state, Beta);
        var deliverer = state.Society.Society.GetHousehold(Beta).MemberIds.First(id => id != builder);
        var smith = Home(state, Smith);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == (storedOutput ? "iron-fittings" : "handcart"));
        Assert.True(recipe.DurationTicks > 12);
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [], Offers = [] };
        inventory = InventoryFixture.AddLot(inventory, "cart-storage-stone", "stone", Beta, storedOutput ? 126 : 127, storageBuildingId: Smith);
        if (storedOutput)
            inventory = InventoryFixture.AddLot(inventory, "cart-storage-iron", "iron", Beta, 1, storageBuildingId: Smith);
        else
        {
            inventory = InventoryFixture.AddLot(inventory, "cart-storage-wood", "wood", builder, 4);
            inventory = InventoryFixture.AddLot(inventory, "cart-storage-fittings", "iron_fittings", builder, 2);
            inventory = InventoryFixture.AddLot(inventory, "cart-storage-rope", "rope", builder, 1);
        }
        inventory = InventoryFixture.AddLot(inventory, "cart-storage-delivery", "wood", deliverer, 1);
        state = At(WithInventory(state, inventory), builder, smith.Position);
        state = At(state, deliverer, SourceNear(state, smith.Position));
        using var world = Restore(state);
        string? jobId = null;
        if (productionKind != "none")
        {
            var started = world.StartProduction(recipe.CanonicalId, Smith, builder);
            Assert.True(started.Applied, started.Failure);
            jobId = Assert.IsType<string>(started.JobId);
            var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == jobId);
            Assert.Equal(builder, job.WorkerId);
            Assert.Equal(storedOutput ? 1 : 7, job.InputReservationIds.Select(world.Society.Inventory.GetReservation).Sum(item => item.Quantity));
            Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
                world.Society.Inventory.GetReservation(id).State));
        }
        var receipt = Submit(world, deliverer, "last-slot-supply", "supply one wood to my Blacksmith");
        using var paired = Reload(world);
        var beforeDiscard = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(beforeDiscard, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 12; tick++) await TickTogether(world, paired);
        if (productionKind != "none")
            Assert.Equal(WorldProductionJobState.Running, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == jobId).State);
        if (storedOutput)
        {
            Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Equal(0, Stored(world, Smith, "wood"));
            for (var tick = 0; tick < recipe.DurationTicks + 8 && world.WorldSimulation.ProductionJobs.Single(item => item.JobId == jobId)
                     .State != WorldProductionJobState.Completed; tick++) await TickTogether(world, paired);
            var completed = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == jobId);
            Assert.Equal(WorldProductionJobState.Completed, completed.State);
            Assert.All(completed.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
                world.Society.Inventory.GetReservation(id).State));
            Assert.Equal(2, Stored(world, Smith, "iron_fittings"));
            var fittings = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "iron_fittings");
            Assert.Equal((Beta, Smith, 2), (fittings.OwnerId, fittings.StorageBuildingId, fittings.Quantity));
            Assert.Equal(126, Stored(world, Smith, "stone"));
            Assert.Equal(0, Stored(world, Smith, "iron"));
            Assert.Equal(128, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Smith).Sum(lot => lot.Quantity));
            Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("cart-storage-delivery"), deliverer));
            Assert.Equal(deliverer, world.Society.Inventory.GetLot("cart-storage-delivery").OwnerId);
            Assert.Equal(0, Order(world, receipt).CompletedUnits);
            world.Validate(); paired.Validate();
            using var storedReload = Reload(world);
            storedReload.Validate();
            return;
        }
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(127, Stored(world, Smith, "stone"));
        Assert.Equal(1, Stored(world, Smith, "wood"));
        var delivery = Assert.Single(world.Society.Inventory.Lots, lot => lot.Id == "cart-storage-delivery");
        Assert.Equal((Beta, Smith, 1), (delivery.OwnerId, delivery.StorageBuildingId, delivery.Quantity));
        Assert.Equal(128, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Smith).Sum(lot => lot.Quantity));
        if (runningCart)
        {
            for (var tick = 0; tick < recipe.DurationTicks + 8 && world.WorldSimulation.ProductionJobs.Single(item => item.JobId == jobId)
                     .State != WorldProductionJobState.Completed; tick++) await TickTogether(world, paired);
            var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == jobId);
            Assert.Equal(WorldProductionJobState.Completed, job.State);
            Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
                world.Society.Inventory.GetReservation(id).State));
            var cart = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == InventoryContainerRules.Handcart);
            Assert.Equal((builder, 1, new InventoryGroundPosition(smith.Position.X, smith.Position.Y)),
                (cart.OwnerId, cart.Quantity, cart.GroundPosition!.Value));
            Assert.Null(cart.StorageBuildingId);
            Assert.Equal(128, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Smith).Sum(lot => lot.Quantity));
            Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            Assert.Equal(1, Order(world, receipt).CompletedUnits);
        }
        world.Validate(); paired.Validate();
        using var restored = Reload(world);
        restored.Validate();
    }
}
