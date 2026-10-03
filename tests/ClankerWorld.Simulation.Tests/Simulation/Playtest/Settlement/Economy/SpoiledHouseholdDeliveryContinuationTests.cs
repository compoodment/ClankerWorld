using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SpoiledHouseholdDeliveryTests
{
    [Fact]
    public async Task RecoveredSpoiledPotStaysAtCampWhileUsefulHouseholdGoodsReachTheirHouseAcrossReload()
    {
        var (pickedUp, actor, camp) = await PickedUpPot();
        using var recovering = Restore(pickedUp, actor, DeliveryPolicy());
        for (var tick = 0; tick < 96 && recovering.Society.Inventory.GetLot(Pot).OwnerId == actor; tick++)
            Assert.True((await recovering.AdvanceOneTickAsync()).Advanced);
        AssertReturnedSpoiledPot(recovering, camp);
        Assert.Single(recovering.ExportState().Events, item => item.Kind == "household_delivery_recovered" &&
            item.Detail == $"{actor}:{Pot}:4:camp");

        const string useful = "zz-recovery-useful-stone";
        var returned = recovering.ExportState();
        var inventory = InventoryFixture.AddLot(returned.Society.Society.Inventory, useful, "stone", Household, 2,
            groundPosition: new(camp.X, camp.Y));
        var ready = FoodCapacityTestFixture.WithInventory(returned, inventory);
        var quantities = inventory.Lots.Select(lot => (lot.Id, lot.Quantity)).ToArray();
        var pickups = returned.Events.Count(item => item.Kind == "household_stock_picked_up" &&
            item.Detail.StartsWith($"{actor}:{Pot}:", StringComparison.Ordinal));
        Assert.Equal(1, pickups);
        var bytes = PrivateWorldRuntimeCodec.Encode(ready);
        var choices = DeliveryPolicy();
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, choices);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, DeliveryPolicy());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        // Keep the just-completed recovery intention and context. A normal
        // subsequent haul must choose useful stock without promising the
        // already recovered, unusable family to the House again.
        for (var tick = 0; tick < 120 && world.Society.Inventory.GetLot(useful).StorageBuildingId != House; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            AssertReturnedSpoiledPot(world, camp);
        }

        Assert.Contains(choices.Offers, offer => offer.Contains("haul_household_stock", StringComparer.Ordinal));
        Assert.Equal((Household, House, 2), (world.Society.Inventory.GetLot(useful).OwnerId,
            world.Society.Inventory.GetLot(useful).StorageBuildingId, world.Society.Inventory.GetLot(useful).Quantity));
        Assert.Equal(pickups, world.ExportState().Events.Count(item => item.Kind == "household_stock_picked_up" &&
            item.Detail.StartsWith($"{actor}:{Pot}:", StringComparison.Ordinal)));
        Assert.Single(world.ExportState().Events, item => item.Kind == "household_delivery_recovered" &&
            item.Detail == $"{actor}:{Pot}:4:camp");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
            item.Detail == $"{actor}:{useful}:2:{House}");
        Assert.Equal(quantities, world.Society.Inventory.Lots.Select(lot => (lot.Id, lot.Quantity)).ToArray());
        Assert.Equal(inventory.Reservations, world.Society.Inventory.Reservations);
        var finalBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var finalReload = Restore(PrivateWorldRuntimeCodec.Decode(finalBytes), actor, DeliveryPolicy());
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
        AssertReturnedSpoiledPot(finalReload, camp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FarmGrainCanLeaveAMixedPotWithoutRecreatingAnUnusableWholeFamilyDeliveryAcrossReload(
        bool spoiledSibling)
    {
        var (state, actor, camp) = await PreparedDelivery();
        const string potId = "00-farm-recovery-pot";
        const string grainId = "farm-recovery-pot-grain";
        const string greensId = "farm-recovery-pot-greens";
        var inventory = state.Society.Society.Inventory;
        foreach (var stock in new[] { FirstGreens, SecondGreens })
            inventory = InventoryFixture.Reserve(inventory, "farm-recovery-control:" + stock, Household,
                stock, inventory.GetLot(stock).Quantity, "retained_recovery_control", long.MaxValue);
        inventory = InventoryFixture.AddLot(inventory, potId, InventoryContainerRules.StoragePot, Household, 1,
            groundPosition: new(camp.X, camp.Y));
        inventory = InventoryFixture.AddLot(inventory, grainId, FarmFieldRules.Grain, Household, 1,
            groundPosition: new(camp.X, camp.Y));
        inventory = InventoryFixture.AddLot(inventory, greensId, "cultivated_greens", Household, 1,
            freshnessBasisPoints: spoiledSibling ? 1 : 10_000, groundPosition: new(camp.X, camp.Y));
        inventory = InventoryFixture.PutIntoContainer(inventory, "farm-recovery-fill-grain", Household,
            potId, grainId, 1);
        inventory = InventoryFixture.PutIntoContainer(inventory, "farm-recovery-fill-greens", Household,
            potId, greensId, 1);
        Assert.Equal(3, inventory.Lots.Where(lot => lot.Id == potId || lot.ContainerLotId == potId).Sum(lot => lot.Quantity));
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        Assert.True(state.Map.FootDistance(state.Inhabitants.Single(person => person.InhabitantId == actor).Position, camp) >= 3);
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == Household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("farmhouse", StringComparer.Ordinal));
        var bytes = PrivateWorldRuntimeCodec.Encode(FoodCapacityTestFixture.WithInventory(state, inventory));
        var choices = new DeliveryChoices("haul_farm_grain", "haul_household_stock", "recover_household_delivery");
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, choices);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor,
            new DeliveryChoices("haul_farm_grain", "haul_household_stock", "recover_household_delivery"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var walked = false;
        var previous = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        for (var tick = 0; tick < 120 && !GrainReachedFarmhouse(world); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var position = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            if (position != previous)
            {
                Assert.True(state.Map.CanFootStep(previous, position));
                walked = true;
            }
            previous = position;
            if (spoiledSibling)
            {
                // One real grain serving can leave a usable pot. Its spoiled
                // sibling and the pot must stay physically at camp, instead
                // of travelling in a delivery that can only be recovered.
                Assert.Equal((Household, new InventoryGroundPosition(camp.X, camp.Y)),
                    (world.Society.Inventory.GetLot(potId).OwnerId, world.Society.Inventory.GetLot(potId).GroundPosition));
                Assert.Null(world.Society.Inventory.GetLot(potId).DeliveryBuildingId);
            }
        }

        Assert.Contains(choices.Offers, offer => offer.Contains("haul_farm_grain", StringComparer.Ordinal));
        Assert.True(walked);
        Assert.True(GrainReachedFarmhouse(world), "Usable grain must physically reach the Farmhouse, even with a spoiled sibling in its pot.");
        Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.Id == grainId || lot.ProvenanceLotId == grainId)
            .Sum(lot => lot.Quantity));
        Assert.Equal((Household, 1, potId), (world.Society.Inventory.GetLot(greensId).OwnerId,
            world.Society.Inventory.GetLot(greensId).Quantity, world.Society.Inventory.GetLot(greensId).ContainerLotId));
        Assert.Equal(inventory.Reservations, world.Society.Inventory.Reservations);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "household_delivery_recovered" &&
            item.Detail.StartsWith($"{actor}:{potId}:", StringComparison.Ordinal));
        if (spoiledSibling)
        {
            Assert.Equal(0, world.Society.Inventory.GetLot(greensId).FreshnessBasisPoints);
            Assert.Contains(world.Society.Inventory.Events, item => item.Kind == "lot_spoiled" && item.Detail == greensId);
            Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.Id == potId || lot.ContainerLotId == potId)
                .Sum(lot => lot.Quantity));
            Assert.Contains(world.Society.Inventory.Events, item => item.Kind == "container_contents_taken" &&
                item.Detail.Contains($":{potId}:{grainId}:1:{actor}", StringComparison.Ordinal));
        }
        else
        {
            // A fitting intact family remains a legal whole-vessel delivery.
            Assert.Equal(farmhouse.InstanceId, world.Society.Inventory.GetLot(potId).StorageBuildingId);
            Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.Id == potId || lot.ContainerLotId == potId)
                .Sum(lot => lot.Quantity));
            Assert.True(world.Society.Inventory.GetLot(greensId).FreshnessBasisPoints > 0);
        }
        var finalBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var finalReload = Restore(PrivateWorldRuntimeCodec.Decode(finalBytes), actor, new DeliveryChoices());
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));

        bool GrainReachedFarmhouse(PrivateWorldRuntime current) => current.Society.Inventory.Lots.Any(lot =>
            (lot.Id == grainId || lot.ProvenanceLotId == grainId) && lot.OwnerId == Household &&
            lot.Quantity == 1 && lot.StorageBuildingId == farmhouse.InstanceId);
    }

    private static void AssertReturnedSpoiledPot(PrivateWorldRuntime world, GridPoint camp)
    {
        Assert.Equal((Household, 1, new InventoryGroundPosition(camp.X, camp.Y)),
            (world.Society.Inventory.GetLot(Pot).OwnerId, world.Society.Inventory.GetLot(Pot).Quantity,
                world.Society.Inventory.GetLot(Pot).GroundPosition));
        Assert.Equal((Household, 3, Pot, 0), (world.Society.Inventory.GetLot(PotGreens).OwnerId,
            world.Society.Inventory.GetLot(PotGreens).Quantity, world.Society.Inventory.GetLot(PotGreens).ContainerLotId,
            world.Society.Inventory.GetLot(PotGreens).FreshnessBasisPoints));
        foreach (var lot in world.Society.Inventory.Lots.Where(lot => lot.Id == Pot || lot.ContainerLotId == Pot))
        {
            Assert.Null(lot.CarrierId);
            Assert.Null(lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
        }
    }
}
