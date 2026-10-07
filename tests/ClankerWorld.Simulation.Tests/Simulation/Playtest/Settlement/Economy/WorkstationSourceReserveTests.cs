using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorkstationSourceReserveTests
{
    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task RestaurantSupplyLeavesTwoActualHouseCookingBatchesAndMovesOnlySurplusAcrossReload(
        int sourceWood, int moved)
    {
        var fixture = Prepared("wood", sourceWood);
        var choices = new Choices("supply_workstation:wood", "haul_household_stock");
        using var world = Restore(fixture.State, fixture.Actor, choices);
        for (var tick = 0; tick < 30 && !world.Society.Inventory.Lots.Any(lot =>
            lot.ProvenanceLotId == "reserve-source-wood" && lot.OwnerId == fixture.Actor); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Equal(2, world.Society.Inventory.GetLot("reserve-source-wood").Quantity);
        var picked = world.Society.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "reserve-source-wood").ToArray();
        Assert.Equal(moved, picked.Sum(lot => lot.Quantity));
        Assert.Equal(sourceWood + 1, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            (lot.Id.StartsWith("reserve-", StringComparison.Ordinal) || lot.ProvenanceLotId == "reserve-source-wood"))
            .Sum(lot => lot.Quantity));
        if (moved == 0)
        {
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
                item.Detail.Contains("reserve-source-wood", StringComparison.Ordinal));
            Assert.Equal(fixture.House.InstanceId, world.Society.Inventory.GetLot("reserve-source-wood").StorageBuildingId);
        }
        else
        {
            var load = Assert.Single(picked);
            Assert.Equal((fixture.Actor, fixture.Restaurant.InstanceId, 1),
                (load.OwnerId, load.DeliveryBuildingId, load.Quantity));
            var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), fixture.Actor,
                new Choices("supply_workstation:wood", "haul_household_stock"));
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            await AdvanceTogether(world, replay, () => world.Society.Inventory.GetLot(load.Id).StorageBuildingId ==
                fixture.Restaurant.InstanceId, 96);
            Assert.Equal((fixture.Household, fixture.Restaurant.InstanceId, 1, (string?)null),
                (world.Society.Inventory.GetLot(load.Id).OwnerId, world.Society.Inventory.GetLot(load.Id).StorageBuildingId,
                    world.Society.Inventory.GetLot(load.Id).Quantity, world.Society.Inventory.GetLot(load.Id).DeliveryBuildingId));
            Assert.Equal(2, world.Society.Inventory.GetLot("reserve-source-wood").Quantity);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
                item.Detail.Contains(load.Id, StringComparison.Ordinal));
        }
        AssertClaimsRemain(world, fixture);
        world.Validate();
    }

    [Fact]
    public async Task AProtectedFirstLotDoesNotHideLaterLooseStockOrCreateAReciprocalPickup()
    {
        var fixture = Prepared("wood", 2);
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "zz-spare-camp-wood", "wood", fixture.Household, 1);
        var choices = new Choices("supply_workstation:wood", "haul_household_stock");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor, choices);
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot("zz-spare-camp-wood").StorageBuildingId ==
            fixture.Restaurant.InstanceId, 96);
        var supplied = world.Society.Inventory.GetLot("zz-spare-camp-wood");
        Assert.Equal((fixture.Household, 1), (supplied.OwnerId, supplied.Quantity));
        Assert.Equal(2, world.Society.Inventory.GetLot("reserve-source-wood").Quantity);
        Assert.Equal(fixture.House.InstanceId, world.Society.Inventory.GetLot("reserve-source-wood").StorageBuildingId);
        for (var tick = 0; tick < 40; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(fixture.Restaurant.InstanceId, world.Society.Inventory.GetLot(supplied.Id).StorageBuildingId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail.Contains("reserve-source-wood", StringComparison.Ordinal));
        AssertClaimsRemain(world, fixture);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            fixture.Actor, new Choices("supply_workstation:wood", "haul_household_stock"));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task OnlyActualUnclaimedIncomingWoodCanCoverPartOfTheHouseReserve(bool incomingClaimed, int moved)
    {
        var fixture = Prepared("wood", 2);
        var deliverer = fixture.State.Society.Society.Inhabitants.Single(person =>
            person.HouseholdId == fixture.Household && person.Id != fixture.Actor).Id;
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "actual-incoming-wood", "wood", fixture.Household, 1);
        inventory = InventoryFixture.Transfer(inventory, "actual-house-delivery-promise", fixture.Household,
            deliverer, "actual-incoming-wood", 1, "household_stock_picked_up",
            destinationDeliveryBuildingId: fixture.House.InstanceId);
        var incoming = inventory.GetLot("actual-incoming-wood");
        Assert.Equal((deliverer, fixture.House.InstanceId, 1),
            (incoming.OwnerId, incoming.DeliveryBuildingId, incoming.Quantity));
        if (incomingClaimed)
            inventory = InventoryFixture.Reserve(inventory, "actual-incoming-claim", deliverer,
                incoming.Id, 1, "independent-incoming-work", long.MaxValue);
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor,
            new Choices("supply_workstation:wood", "haul_household_stock"));
        for (var tick = 0; tick < 32; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2 - moved, world.Society.Inventory.GetLot("reserve-source-wood").Quantity);
        Assert.Equal(moved, world.Society.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "reserve-source-wood")
            .Sum(lot => lot.Quantity));
        var unchangedPromise = world.Society.Inventory.GetLot(incoming.Id);
        Assert.Equal((incoming.OwnerId, incoming.DeliveryBuildingId, incoming.Quantity, incoming.ProvenanceLotId),
            (unchangedPromise.OwnerId, unchangedPromise.DeliveryBuildingId, unchangedPromise.Quantity,
                unchangedPromise.ProvenanceLotId));
        if (incomingClaimed)
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("actual-incoming-claim").State);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWholePotCannotRemoveAnotherNeededIngredientOrAnActiveChildClaim(bool claimed)
    {
        var fixture = Prepared("grain", 5);
        var inventory = fixture.State.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "a-protected-pot", InventoryContainerRules.StoragePot,
            fixture.Household, 1, storageBuildingId: fixture.House.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "pot-grain", "grain", fixture.Household, 1,
            storageBuildingId: fixture.House.InstanceId, containerLotId: "a-protected-pot");
        inventory = InventoryFixture.AddLot(inventory, "pot-berries", "berries", fixture.Household, 2,
            storageBuildingId: fixture.House.InstanceId, containerLotId: "a-protected-pot");
        // Other berries are genuine inputs, but remain unavailable under a real independent claim.
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == fixture.Household &&
            lot.StorageBuildingId == fixture.House.InstanceId && lot.ItemKind == "berries" && lot.Id != "pot-berries").ToArray())
            inventory = InventoryFixture.Reserve(inventory, "berry-claim:" + lot.Id, fixture.Household,
                lot.Id, lot.Quantity, "independent-berry-use", long.MaxValue);
        if (claimed)
            inventory = InventoryFixture.Reserve(inventory, "pot-child-claim", fixture.Household,
                "pot-grain", 1, "independent-grain-use", long.MaxValue);
        var choices = new Choices("supply_workstation:grain", "haul_household_stock");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor, choices);
        // The loose surplus may still supply grain; the whole family must stay at its source.
        for (var tick = 0; tick < 32; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal((fixture.Household, fixture.House.InstanceId, 1),
            (world.Society.Inventory.GetLot("a-protected-pot").OwnerId,
                world.Society.Inventory.GetLot("a-protected-pot").StorageBuildingId,
                world.Society.Inventory.GetLot("a-protected-pot").Quantity));
        foreach (var id in new[] { "pot-grain", "pot-berries" })
        {
            Assert.Equal("a-protected-pot", world.Society.Inventory.GetLot(id).ContainerLotId);
            Assert.Equal(fixture.House.InstanceId, world.Society.Inventory.GetLot(id).StorageBuildingId);
        }
        Assert.Equal(1, world.Society.Inventory.GetLot("pot-grain").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot("pot-berries").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail.Contains("a-protected-pot", StringComparison.Ordinal));
        if (claimed) Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("pot-child-claim").State);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task JugSupplyCountsAllChildrenTogetherBeforeMovingTheWholeFamily()
    {
        var fixture = Prepared(InventoryContainerRules.FreshWater, 0);
        var inventory = fixture.State.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "a-source-jug", InventoryContainerRules.WaterJug,
            fixture.Household, 1, storageBuildingId: fixture.House.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "source-water-a", InventoryContainerRules.FreshWater,
            fixture.Household, 2, storageBuildingId: fixture.House.InstanceId, containerLotId: "a-source-jug");
        inventory = InventoryFixture.AddLot(inventory, "source-water-b", InventoryContainerRules.FreshWater,
            fixture.Household, 1, storageBuildingId: fixture.House.InstanceId, containerLotId: "a-source-jug");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor,
            new Choices("supply_workstation:fresh_water", "haul_household_stock"));
        for (var tick = 0; tick < 32; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(fixture.House.InstanceId, world.Society.Inventory.GetLot("a-source-jug").StorageBuildingId);
        Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == "a-source-jug").Sum(lot => lot.Quantity));
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == "a-source-jug"), lot =>
        {
            Assert.Equal(fixture.Household, lot.OwnerId);
            Assert.Equal(fixture.House.InstanceId, lot.StorageBuildingId);
        });
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail.Contains("a-source-jug", StringComparison.Ordinal));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task RefillLeavesTheRestaurantsNeededWaterAndWalksAnActualLaterEmptyJugThroughTheShoreAndHome()
    {
        var fixture = Prepared(InventoryContainerRules.FreshWater, 2);
        var inventory = fixture.State.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "a-restaurant-partial-jug", InventoryContainerRules.WaterJug,
            fixture.Household, 1, storageBuildingId: fixture.Restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "restaurant-protected-water", InventoryContainerRules.FreshWater,
            fixture.Household, 2, storageBuildingId: fixture.Restaurant.InstanceId,
            containerLotId: "a-restaurant-partial-jug");
        // The existing one-unit destination water is also a real independent claim.
        var originalWater = inventory.Lots.Single(lot => lot.Id == "reserve-destination-fresh_water");
        inventory = InventoryFixture.Reserve(inventory, "restaurant-water-claim", fixture.Household,
            originalWater.Id, 1, "independent-cooking-use", long.MaxValue);
        inventory = InventoryFixture.AddLot(inventory, "zz-empty-refill-jug", InventoryContainerRules.WaterJug,
            fixture.Household, 1, storageBuildingId: fixture.Restaurant.InstanceId);
        var choices = new Choices("collect_water_jug", "fill_water_jug", "return_water_jug");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor, choices);
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot("zz-empty-refill-jug").OwnerId == fixture.Actor, 96);
        Assert.Equal(fixture.Restaurant.InstanceId, world.Society.Inventory.GetLot("a-restaurant-partial-jug").StorageBuildingId);
        Assert.Equal(2, world.Society.Inventory.GetLot("restaurant-protected-water").Quantity);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), fixture.Actor,
            new Choices("collect_water_jug", "fill_water_jug", "return_water_jug"));
        await AdvanceTogether(world, replay, () => world.ExportState().Events.Any(item => item.Kind == "water_jug_returned" &&
            item.Detail.Contains("zz-empty-refill-jug", StringComparison.Ordinal)), 300);
        var returned = world.Society.Inventory.GetLot("zz-empty-refill-jug");
        Assert.Equal((fixture.Household, fixture.House.InstanceId, 1),
            (returned.OwnerId, returned.StorageBuildingId, returned.Quantity));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == returned.Id).Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(fixture.Actor + ":", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "water_jug_filled" &&
            item.Detail.Contains(returned.Id, StringComparison.Ordinal));
        Assert.Equal(fixture.Restaurant.InstanceId, world.Society.Inventory.GetLot("a-restaurant-partial-jug").StorageBuildingId);
        Assert.Equal(2, world.Society.Inventory.GetLot("restaurant-protected-water").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("restaurant-water-claim").State);
    }

    [Fact]
    public async Task AnActorAlreadyAtTheHouseCanCollectItsActualEmptyJugForRefill()
    {
        var fixture = Prepared(InventoryContainerRules.FreshWater, 2);
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "at-source-empty-jug", InventoryContainerRules.WaterJug, fixture.Household, 1,
            storageBuildingId: fixture.House.InstanceId);
        var choices = new Choices("collect_water_jug");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor, choices);
        Assert.Equal(fixture.House.Position, world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Position);
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot("at-source-empty-jug").OwnerId == fixture.Actor, 8);
        Assert.Contains(choices.Offered, item => item.Id == "collect_water_jug" && item.DestinationId == "at-source-empty-jug");
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(fixture.Actor + ":", StringComparison.Ordinal));
        var jug = world.Society.Inventory.GetLot("at-source-empty-jug");
        Assert.Equal((fixture.Actor, 1, (string?)null), (jug.OwnerId, jug.Quantity, jug.StorageBuildingId));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "water_jug_collected" &&
            item.Detail == fixture.Actor + ":at-source-empty-jug");
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task WaterInAReallyBrokenSiblingJugCannotAuthorizeRemovingTheOnlyUsableSourceWater()
    {
        var fixture = Prepared(InventoryContainerRules.FreshWater, 1);
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "broken-sibling-jug", InventoryContainerRules.WaterJug, fixture.Household, 1,
            storageBuildingId: fixture.House.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "broken-sibling-water", InventoryContainerRules.FreshWater,
            fixture.Household, 3, storageBuildingId: fixture.House.InstanceId, containerLotId: "broken-sibling-jug");
        inventory = InventoryFixture.WearSingleUnit(inventory, "broken-sibling-jug", 10_000);
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor,
            new Choices("supply_workstation:fresh_water", "haul_household_stock"));
        for (var tick = 0; tick < 32; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var usable = world.Society.Inventory.GetLot("reserve-source-fresh_water");
        Assert.Equal((fixture.Household, fixture.House.InstanceId, 1),
            (usable.OwnerId, usable.StorageBuildingId, usable.Quantity));
        Assert.Equal(0, world.Society.Inventory.GetLot("broken-sibling-jug").ConditionBasisPoints);
        Assert.Equal(3, world.Society.Inventory.GetLot("broken-sibling-water").Quantity);
        Assert.Equal("broken-sibling-jug", world.Society.Inventory.GetLot("broken-sibling-water").ContainerLotId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail.Contains("fixture-jug:" + fixture.House.InstanceId, StringComparison.Ordinal));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task UsableOnsiteGrainInAMixedPotProtectsItsReserveWhileOnlyLooseSurplusIsDelivered()
    {
        var fixture = Prepared("grain", 2);
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "a-mixed-surplus-pot", InventoryContainerRules.StoragePot, fixture.Household, 1,
            storageBuildingId: fixture.House.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "mixed-surplus-grain", "grain", fixture.Household, 1,
            storageBuildingId: fixture.House.InstanceId, containerLotId: "a-mixed-surplus-pot");
        inventory = InventoryFixture.AddLot(inventory, "mixed-spoiling-berries", "berries", fixture.Household, 2,
            freshnessBasisPoints: 0, storageBuildingId: fixture.House.InstanceId, containerLotId: "a-mixed-surplus-pot");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor,
            new Choices("supply_workstation:grain", "haul_household_stock"));
        await AdvanceUntil(world, () => world.Society.Inventory.Lots.Any(lot =>
            lot.ProvenanceLotId == "reserve-source-grain" && lot.OwnerId == fixture.Actor), 32);
        var load = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "reserve-source-grain");
        Assert.Equal(0, world.Society.Inventory.GetLot("mixed-spoiling-berries").FreshnessBasisPoints);
        Assert.Equal(2, world.Society.Inventory.GetLot("mixed-spoiling-berries").Quantity);
        Assert.Equal((fixture.Actor, fixture.Restaurant.InstanceId, 1),
            (load.OwnerId, load.DeliveryBuildingId, load.Quantity));
        Assert.Equal(1, world.Society.Inventory.GetLot("reserve-source-grain").Quantity);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), fixture.Actor,
            new Choices("supply_workstation:grain", "haul_household_stock"));
        await AdvanceTogether(world, replay, () => world.Society.Inventory.GetLot(load.Id).StorageBuildingId ==
            fixture.Restaurant.InstanceId, 96);
        foreach (var id in new[] { "a-mixed-surplus-pot", "mixed-surplus-grain", "mixed-spoiling-berries" })
        {
            var lot = world.Society.Inventory.GetLot(id);
            Assert.Equal(fixture.Household, lot.OwnerId);
            Assert.Equal(fixture.House.InstanceId, lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
        }
        Assert.Equal("a-mixed-surplus-pot", world.Society.Inventory.GetLot("mixed-surplus-grain").ContainerLotId);
        Assert.Equal("a-mixed-surplus-pot", world.Society.Inventory.GetLot("mixed-spoiling-berries").ContainerLotId);
        // The actual one-grain recipes keep two batches: one loose unit and
        // one usable on-site unit in the otherwise undeliverable mixed pot.
        Assert.Equal(1, world.Society.Inventory.GetLot("reserve-source-grain").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("mixed-surplus-grain").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot("mixed-spoiling-berries").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("a-mixed-surplus-pot").Quantity);
        Assert.Equal(10_000, world.Society.Inventory.GetLot("a-mixed-surplus-pot").ConditionBasisPoints);
        Assert.Equal((fixture.Household, fixture.Restaurant.InstanceId, 1),
            (world.Society.Inventory.GetLot(load.Id).OwnerId, world.Society.Inventory.GetLot(load.Id).StorageBuildingId,
                world.Society.Inventory.GetLot(load.Id).Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
            item.Detail.StartsWith(fixture.Actor + ":" + load.Id + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail.Contains("a-mixed-surplus-pot", StringComparison.Ordinal));
        AssertClaimsRemain(world, fixture);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 1)]
    public async Task OnlyADeliverableUnclaimedMemberFamilyCanCoverTheHouseGrainReserve(
        bool spoiledSibling, bool expiredSiblingClaim, int moved)
    {
        var fixture = Prepared("grain", 2);
        var deliverer = fixture.State.Society.Society.Inhabitants.Single(person =>
            person.HouseholdId == fixture.Household && person.Id != fixture.Actor).Id;
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "incoming-family-pot", InventoryContainerRules.StoragePot, fixture.Household, 1);
        inventory = InventoryFixture.AddLot(inventory, "incoming-family-grain", "grain", fixture.Household, 1,
            containerLotId: "incoming-family-pot");
        inventory = InventoryFixture.AddLot(inventory, "incoming-family-berries", "berries", fixture.Household, 1,
            freshnessBasisPoints: spoiledSibling ? 0 : 10_000, containerLotId: "incoming-family-pot");
        inventory = InventoryFixture.Transfer(inventory, "real-member-family-promise", fixture.Household, deliverer,
            "incoming-family-pot", 1, "household_stock_picked_up",
            destinationDeliveryBuildingId: fixture.House.InstanceId);
        if (!spoiledSibling)
            inventory = InventoryFixture.Reserve(inventory, "actual-family-sibling-claim", deliverer,
                "incoming-family-berries", 1, "independent-incoming-work", expiredSiblingClaim ? 0 : long.MaxValue);
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor,
            new Choices("supply_workstation:grain", "haul_household_stock"));
        for (var tick = 0; tick < 32; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2 - moved, world.Society.Inventory.GetLot("reserve-source-grain").Quantity);
        Assert.Equal(moved, world.Society.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "reserve-source-grain")
            .Sum(lot => lot.Quantity));
        foreach (var id in new[] { "incoming-family-pot", "incoming-family-grain", "incoming-family-berries" })
        {
            var lot = world.Society.Inventory.GetLot(id);
            Assert.Equal((deliverer, fixture.House.InstanceId, (string?)null, 1),
                (lot.OwnerId, lot.DeliveryBuildingId, lot.StorageBuildingId, lot.Quantity));
        }
        Assert.Equal("incoming-family-pot", world.Society.Inventory.GetLot("incoming-family-grain").ContainerLotId);
        Assert.Equal("incoming-family-pot", world.Society.Inventory.GetLot("incoming-family-berries").ContainerLotId);
        if (spoiledSibling) Assert.Equal(0, world.Society.Inventory.GetLot("incoming-family-berries").FreshnessBasisPoints);
        else Assert.Equal(expiredSiblingClaim ? InventoryReservationState.Released : InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation("actual-family-sibling-claim").State);
        AssertClaimsRemain(world, fixture);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), fixture.Actor,
            new Choices("supply_workstation:grain", "haul_household_stock"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task BrokenDestinationContentsDoNotHideItsRealUsableWaterShortage()
    {
        var fixture = Prepared(InventoryContainerRules.FreshWater, 2);
        var inventory = InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
            "destination-broken-jug", InventoryContainerRules.WaterJug, fixture.Household, 1,
            storageBuildingId: fixture.Restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "destination-broken-water", InventoryContainerRules.FreshWater,
            fixture.Household, 3, storageBuildingId: fixture.Restaurant.InstanceId, containerLotId: "destination-broken-jug");
        inventory = InventoryFixture.WearSingleUnit(inventory, "destination-broken-jug", 10_000);
        inventory = InventoryFixture.AddLot(inventory, "actual-spare-source-jug", InventoryContainerRules.WaterJug,
            fixture.Household, 1, storageBuildingId: fixture.House.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "actual-spare-source-water", InventoryContainerRules.FreshWater,
            fixture.Household, 1, storageBuildingId: fixture.House.InstanceId, containerLotId: "actual-spare-source-jug");
        var choices = new Choices("supply_workstation:fresh_water", "haul_household_stock");
        using var world = Restore(FarmFieldTests.WithInventory(fixture.State, inventory), fixture.Actor, choices);
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot("actual-spare-source-jug").OwnerId == fixture.Actor, 32);
        Assert.Contains(choices.Offered, item => item.Id == "supply_workstation:fresh_water" &&
            item.DestinationId == fixture.Restaurant.InstanceId);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), fixture.Actor,
            new Choices("supply_workstation:fresh_water", "haul_household_stock"));
        await AdvanceTogether(world, replay, () => world.Society.Inventory.GetLot("actual-spare-source-jug").StorageBuildingId ==
            fixture.Restaurant.InstanceId, 96);
        Assert.Equal((fixture.Household, fixture.Restaurant.InstanceId, 1),
            (world.Society.Inventory.GetLot("actual-spare-source-water").OwnerId,
                world.Society.Inventory.GetLot("actual-spare-source-water").StorageBuildingId,
                world.Society.Inventory.GetLot("actual-spare-source-water").Quantity));
        Assert.Equal(0, world.Society.Inventory.GetLot("destination-broken-jug").ConditionBasisPoints);
        Assert.Equal(3, world.Society.Inventory.GetLot("destination-broken-water").Quantity);
        Assert.Equal("destination-broken-jug", world.Society.Inventory.GetLot("destination-broken-water").ContainerLotId);
        Assert.Equal(2, world.Society.Inventory.GetLot("reserve-source-fresh_water").Quantity);
        Assert.Equal(fixture.House.InstanceId, world.Society.Inventory.GetLot("reserve-source-fresh_water").StorageBuildingId);
    }

    private sealed record Fixture(PrivateWorldRuntimeState State, string Actor, string Household,
        PlacedBuilding House, PlacedBuilding Restaurant, string[] Claims);

    private static Fixture Prepared(string missing, int sourceQuantity)
    {
        using var generated = NormalPathWorld.CreateGenerated("concrete-meal-porridge", _ => new Choices());
        var state = generated.ExportState();
        const string household = "household:camp-alpha";
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var house = generated.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var definition = generated.WorldContent.Buildings.Single(building => building.LocalId == "restaurant-1x2");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "restaurant-build-wood", "wood", household, 8);
        inventory = InventoryFixture.AddLot(inventory, "restaurant-build-stone", "stone", household, 2);
        var woodBefore = inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var stoneBefore = inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "stone").Sum(lot => lot.Quantity);
        using var placement = Restore(FarmFieldTests.WithInventory(state, inventory));
        var result = state.Map.Tiles.Select(tile => tile.Position).Where(point =>
                Math.Abs(point.X - house.Position.X) <= 8 && Math.Abs(point.Y - house.Position.Y) <= 8 &&
                state.Map.IsReachableFromCampOnFoot(point))
            .OrderBy(point => state.Map.FootDistance(house.Position, point))
            .Select(point => placement.PlaceBuilding("reserve-test-restaurant", definition.CanonicalId, point, household))
            .First(item => item.Applied);
        var restaurant = placement.WorldSimulation.Buildings.Single(building => building.InstanceId == result.InstanceId);
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(placement.ExportState(), household);
        inventory = state.Society.Society.Inventory;
        Assert.Equal(woodBefore - 8, inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(stoneBefore - 2, inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "stone").Sum(lot => lot.Quantity));
        var claims = new List<string>();
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == household &&
            !InventoryContainerRules.IsContainer(lot.ItemKind) && PersonalEquipmentRules.AvailableQuantity(inventory, lot) > 0).ToArray())
        {
            var id = "independent-existing:" + lot.Id;
            inventory = InventoryFixture.Reserve(inventory, id, household, lot.Id,
                PersonalEquipmentRules.AvailableQuantity(inventory, lot), "independent-existing-work", long.MaxValue);
            claims.Add(id);
        }
        // These controls isolate cooking reserves. Give each adult usable shared
        // tools after the independent claims, so bootstrapping adds no wood demand.
        var adults = state.Society.Society.Inhabitants.Count(person => person.HouseholdId == household &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
        inventory = InventoryFixture.AddLot(inventory, "reserve-fixture-axes", "wooden_axe", household, adults,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "reserve-fixture-picks", "wooden_pickaxe", household, adults,
            storageBuildingId: house.InstanceId);
        // Provision the other real recipe inputs so these are transport/retention controls,
        // rather than a test of which missing ingredient is ranked first.
        foreach (var site in new[] { house, restaurant })
        {
            foreach (var input in placement.WorldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == site.DefinitionId &&
                    !recipe.IsCrop && !(recipe.Inputs.Any(item => item.ResourceId == "food") &&
                        recipe.Outputs.Any(item => item.ResourceId == "food")))
                .SelectMany(recipe => recipe.Inputs).GroupBy(input => input.ResourceId))
            {
                var amount = input.Key == missing ? site == house ? sourceQuantity : 1 : input.Max(item => item.Amount) * 2;
                if (amount == 0) continue;
                string? containerId = null;
                if (input.Key is InventoryContainerRules.FreshWater or "milk")
                {
                    containerId = "fixture-jug:" + site.InstanceId + (input.Key == "milk" ? ":milk" : "");
                    inventory = InventoryFixture.AddLot(inventory, containerId, InventoryContainerRules.WaterJug,
                        household, 1, storageBuildingId: site.InstanceId);
                }
                var id = site == house ? "reserve-source-" + input.Key : "reserve-destination-" + input.Key;
                inventory = InventoryFixture.AddLot(inventory, id, input.Key, household, amount,
                    storageBuildingId: site.InstanceId, containerLotId: containerId);
            }
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = house.Position,
                    HungerBasisPoints = 9_000,
                    Survival = new SurvivalCondition(),
                    LastDecisionContext = null
                } : person).ToArray(),
        };
        using var valid = Restore(state);
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(valid.ExportState()));
        return new(state, actor, household, house, restaurant, claims.ToArray());
    }

    private static void AssertClaimsRemain(PrivateWorldRuntime world, Fixture fixture) =>
        Assert.All(fixture.Claims, id => Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(id).State));

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string? actor = null, Choices? choices = null) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? choices ?? new Choices() : new Choices());

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(), string.Join(" | ", world.ExportState().Events.TakeLast(10).Select(item => item.Kind + ":" + item.Detail)));
    }

    private static async Task AdvanceTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(done());
    }

    private sealed class Choices(params string[] wanted) : IDecisionProvider
    {
        public ConcurrentBag<CognitionCandidate> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Offered.Add(candidate);
            var selected = wanted.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id == prefix || candidate.Id.StartsWith(prefix + ":", StringComparison.Ordinal)))
                .FirstOrDefault(candidate => candidate is not null) ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
