using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldDeliveryPublicStockOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string OtherHousehold = "household:camp-beta";
    private const string House = "first-town-house-a";
    private const string Warehouse = "first-town-warehouse";
    private const string Store = "order-stock-store";
    private static readonly Lazy<byte[]> Baseline = new(CreateBaseline);

    [Fact]
    public async Task StoredAndCollectedWoodCanStillBeDonatedWithTheBuiltInChooser()
    {
        var state = ShelterOrderTestFixture.WithClearWeather(ShelterOrderTestFixture.Prepared());
        var actor = Actor(state);
        var house = ShelterOrderTestFixture.House(state);
        state = ShelterOrderTestFixture.At(state, actor, house.Position);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "split-wood", "wood", actor, 8));
        IDecisionProvider Provider(string id) => id == actor ? new DeterministicDecisionProvider() : new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        var store = Submit(world, actor, "store-before-donation", "store four wood in my House");
        await Complete(store);
        var collect = Submit(world, actor, "collect-before-donation", "collect four wood");
        await Complete(collect);
        var carried = world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood" &&
            PersonalEquipmentRules.IsCarried(lot, actor)).ToArray();
        Assert.Equal(2, carried.Length);
        Assert.All(carried, lot => Assert.Equal(4, lot.Quantity));
        var warehouse = Building(state, Warehouse);
        var walk = Submit(world, actor, "walk-before-donation", string.Create(CultureInfo.InvariantCulture,
            $"go to ({warehouse.Position.X},{warehouse.Position.Y})"));
        await Complete(walk);
        var donate = Submit(world, actor, "donate-after-collection", "donate four wood to my Town Warehouse");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 12 && Order(world, donate).Status != "finished"; tick++)
            await TickTogether(world, replay);
        Assert.Equal(("finished", 4), (Order(world, donate).Status, Order(world, donate).CompletedUnits));
        Assert.Equal(4, Stored(world, Warehouse, "wood"));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood" &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        await TickTogether(world, replay);
        Assert.Equal(4, Stored(world, Warehouse, "wood"));
        world.Validate();

        async Task Complete(OwnerInstructionReceipt receipt)
        {
            for (var tick = 0; tick < 20 && Order(world, receipt).Status != "finished"; tick++)
                await Tick(world);
            Assert.Equal("finished", Order(world, receipt).Status);
        }
    }

    [Theory]
    [InlineData(4, 4, 0, 4, 4)]
    [InlineData(1, 7, 0, 4, 4)]
    [InlineData(2, 6, 2, 4, 2)]
    [InlineData(2, 2, 0, 4, 0)]
    [InlineData(4, 4, 0, 1, 1)]
    public async Task SplitDonationsShareOneUsableReservePerKindAndRespectReservationsAndRoomAcrossReplay(
        int firstQuantity, int secondQuantity, int reserved, int room, int expectedDonation)
    {
        var state = Prepared(Warehouse);
        var actor = Actor(state);
        var warehouse = Building(state, Warehouse);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "split-a", "wood", actor, firstQuantity);
        inventory = InventoryFixture.AddLot(inventory, "split-b", "wood", actor, secondQuantity);
        inventory = InventoryFixture.AddLot(inventory, "personal-stone", "stone", actor, 4);
        if (reserved > 0)
            inventory = InventoryFixture.Reserve(inventory, "protected-split", actor, "split-b", reserved, "other_work", 1_000);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == warehouse.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(definition, warehouse)!.Value;
        inventory = InventoryFixture.AddLot(inventory, "warehouse-stone", "stone", warehouse.TownId!, capacity - room,
            storageBuildingId: Warehouse);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "split-donation", "donate four wood to my Town Warehouse");
        using var replay = Reload(world);
        for (var tick = 0; tick < 12; tick++)
            await TickTogether(world, replay);
        Assert.Equal((expectedDonation == 4 ? "finished" : "blocked", expectedDonation),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(expectedDonation, Stored(world, Warehouse, "wood"));
        Assert.Equal(firstQuantity + secondQuantity, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(firstQuantity + secondQuantity - expectedDonation, world.Society.Inventory.Lots.Where(lot =>
            lot.OwnerId == actor && lot.ItemKind == "wood" && PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal((actor, 4, (string?)null), (world.Society.Inventory.GetLot("personal-stone").OwnerId,
            world.Society.Inventory.GetLot("personal-stone").Quantity, world.Society.Inventory.GetLot("personal-stone").StorageBuildingId));
        if (reserved > 0)
        {
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("protected-split").State);
            Assert.True(world.Society.Inventory.GetLot("split-b").Quantity >= reserved);
        }
        world.Validate();
    }

    [Theory]
    [InlineData(3, 3, "finished")]
    [InlineData(8, 6, "blocked")]
    public async Task TownDonationsKeepFourPersonalUnitsAndCreditOnlyRealDepositsAcrossReplay(
        int requested, int delivered, string finalStatus)
    {
        var state = Prepared(Warehouse, adjacent: true);
        var actor = Actor(state);
        var warehouse = Building(state, Warehouse);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "donation-wood", "wood", actor, 10, conditionBasisPoints: 7_000));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "donate", $"donate {requested} wood to my Town Warehouse");
        await Tick(world);
        Assert.Equal(("deliver_stock", 0, "goods_items"),
            (Order(world, receipt).Action, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        AssertPinned(Order(world, receipt), warehouse, warehouse.TownId!);
        Assert.Equal(10, world.Society.Inventory.GetLot("donation-wood").Quantity);
        Assert.Equal(actor, world.Society.Inventory.GetLot("donation-wood").OwnerId);
        Assert.Equal(0, Stored(world, Warehouse, "wood"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");

        using var replay = Reload(world);
        var previous = 0;
        for (var step = 0; step < 12 && Order(world, receipt).Status != finalStatus; step++)
        {
            await TickTogether(world, replay);
            var completed = Order(world, receipt).CompletedUnits;
            Assert.InRange(completed - previous, 0, 4);
            Assert.Equal(completed, Stored(world, Warehouse, "wood"));
            previous = completed;
        }
        Assert.Equal((finalStatus, delivered), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(10 - delivered, world.Society.Inventory.GetLot("donation-wood").Quantity);
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Warehouse), lot =>
        {
            Assert.Equal(warehouse.TownId, lot.OwnerId);
            Assert.Equal(7_000, lot.ConditionBasisPoints);
            Assert.Null(lot.DeliveryBuildingId);
        });
        Assert.Equal(10, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.StartsWith("delivery:stock:", Order(world, receipt).LastEffectId, StringComparison.Ordinal);
        if (finalStatus == "blocked")
        {
            var cancellation = new OwnerOrderCancelRequest("stop-donation", "owner:test", world.Society.WorldId, actor, receipt.InstructionId);
            Assert.True(world.CancelOrder(cancellation).Changed);
            Assert.Equal(world.CancelOrder(cancellation), replay.CancelOrder(cancellation));
        }
        await TickTogether(world, replay);
        Assert.Equal(delivered, Order(world, receipt).CompletedUnits);
        Assert.Equal(delivered, Stored(world, Warehouse, "wood"));
    }

    [Fact]
    public async Task AnUnrelatedFoodPromiseDoesNotBlockADirectWoodDonation()
    {
        var state = Prepared(Warehouse, adjacent: true);
        var actor = Actor(state);
        var warehouse = Building(state, Warehouse);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "spare-wood", "wood", actor, 8);
        inventory = InventoryFixture.AddLot(inventory, "promised-food", "food", Household, 1, storageBuildingId: House);
        inventory = InventoryFixture.Transfer(inventory, "pick-up-food", Household, actor, "promised-food", 1,
            "delivery", destinationDeliveryBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "donate-with-promise", "donate two wood to my Town Warehouse");
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertPinned(Order(world, receipt), warehouse, warehouse.TownId!);
        Assert.Equal(("spare-wood", 2), (Order(world, receipt).DeliveryLotId, Order(world, receipt).DeliveryQuantity));
        Assert.Equal((actor, 8), (world.Society.Inventory.GetLot("spare-wood").OwnerId,
            world.Society.Inventory.GetLot("spare-wood").Quantity));
        Assert.Equal(0, Stored(world, Warehouse, "wood"));

        using var replay = Reload(world);
        for (var step = 0; step < 4 && Order(world, receipt).Status != "finished"; step++)
            await TickTogether(world, replay);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(6, world.Society.Inventory.GetLot("spare-wood").Quantity);
        Assert.Equal(2, Stored(world, Warehouse, "wood"));
        var promised = world.Society.Inventory.GetLot("promised-food");
        Assert.Equal((actor, 1, House, (string?)null),
            (promised.OwnerId, promised.Quantity, promised.DeliveryBuildingId, promised.StorageBuildingId));
        var deposit = Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        Assert.Equal($"{actor}:spare-wood:2:{Warehouse}", deposit.Detail);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_picked_up");
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Warehouse),
            lot => Assert.Equal(warehouse.TownId, lot.OwnerId));
    }

    [Fact]
    public async Task APausedWalkingDonationKeepsItsBoundSourceWhenANewerEligibleLotAppears()
    {
        var state = Prepared(Warehouse, adjacent: true);
        var actor = Actor(state);
        var warehouse = Building(state, Warehouse);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "z-bound-wood", "wood", actor, 8));
        using var walking = Restore(state);
        var receipt = Submit(walking, actor, "bound-donation", "donate two wood to my Town Warehouse");
        await Tick(walking);
        Assert.Equal(0, Order(walking, receipt).CompletedUnits);
        Assert.Equal(("z-bound-wood", 2), (Order(walking, receipt).DeliveryLotId, Order(walking, receipt).DeliveryQuantity));
        AssertPinned(Order(walking, receipt), warehouse, warehouse.TownId!);
        walking.Pause();
        var saved = walking.ExportState();
        saved = WithInventory(saved, InventoryFixture.AddLot(saved.Society.Society.Inventory,
            "a-new-wood", "wood", actor, 8));
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        using var replay = Reload(resumed);
        var pausedBytes = PrivateWorldRuntimeCodec.Encode(resumed.ExportState());
        Assert.False((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(pausedBytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        Assert.Equal(("z-bound-wood", 2), (Order(resumed, receipt).DeliveryLotId, Order(resumed, receipt).DeliveryQuantity));
        resumed.Resume();
        replay.Resume();
        for (var step = 0; step < 4 && Order(resumed, receipt).Status != "finished"; step++)
            await TickTogether(resumed, replay);
        Assert.Equal(("finished", 2), (Order(resumed, receipt).Status, Order(resumed, receipt).CompletedUnits));
        AssertPinned(Order(resumed, receipt), warehouse, warehouse.TownId!);
        Assert.Equal((actor, 6), (resumed.Society.Inventory.GetLot("z-bound-wood").OwnerId,
            resumed.Society.Inventory.GetLot("z-bound-wood").Quantity));
        Assert.Equal((actor, 8), (resumed.Society.Inventory.GetLot("a-new-wood").OwnerId,
            resumed.Society.Inventory.GetLot("a-new-wood").Quantity));
        Assert.Equal(2, Stored(resumed, Warehouse, "wood"));
        Assert.Equal(16, resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        var deposit = Assert.Single(resumed.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        Assert.Equal($"{actor}:z-bound-wood:2:{Warehouse}", deposit.Detail);
        Assert.StartsWith("delivery:stock:", Order(resumed, receipt).LastEffectId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("borrowed")]
    [InlineData("household-stock")]
    public async Task TownDonationCannotDonateReservedOrSomebodyElsesWood(string boundary)
    {
        var state = Prepared(Warehouse);
        var actor = Actor(state);
        var owner = boundary == "reserved" ? actor : boundary == "borrowed" ? OtherHousehold : Household;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "protected-donation", "wood", owner, 8);
        if (boundary == "borrowed")
            inventory = InventoryFixture.Relocate(inventory, "borrow-donation", "protected-donation", owner, 8, carrierId: actor);
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "donation-reserved", actor, "protected-donation", 4, "other_work", 1_000);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "protected-donate", "donate two wood to my Town Warehouse");
        await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal((owner, 8), (world.Society.Inventory.GetLot("protected-donation").OwnerId,
            world.Society.Inventory.GetLot("protected-donation").Quantity));
        Assert.Equal(0, Stored(world, Warehouse, "wood"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        if (boundary == "reserved")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("donation-reserved").State);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData("food")]
    [InlineData("grain")]
    public async Task TownFoodDonationsAreRefusedWithoutReplacingTheExistingOrderOrMovingStock(string kind)
    {
        var state = Prepared(Warehouse);
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "not-town-stock", kind, actor, 8));
        using var world = Restore(state);
        var current = Submit(world, actor, "current-donation", "donate two wood to my Town Warehouse");
        var rejected = Submit(world, actor, "food-donation", $"donate two {kind} to my Town Warehouse");
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        await Tick(world);
        Assert.Equal((actor, 8, (string?)null), (world.Society.Inventory.GetLot("not-town-stock").OwnerId,
            world.Society.Inventory.GetLot("not-town-stock").Quantity, world.Society.Inventory.GetLot("not-town-stock").StorageBuildingId));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task AWalkingDonationDoesNotRebindToAReplacementWarehouseAtTheSameTile()
    {
        var state = Prepared(Warehouse, adjacent: true);
        var actor = Actor(state);
        var original = Building(state, Warehouse);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == original.DefinitionId);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "pinned-donation", "wood", actor, 8);
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "replacement-" + cost.ResourceId, cost.ResourceId, Household, cost.Amount);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "pinned-donate", string.Create(CultureInfo.InvariantCulture,
            $"donate two wood to my Town Warehouse at ({original.Position.X}, {original.Position.Y})"));
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertPinned(Order(world, receipt), original, original.TownId!);
        Assert.True(world.RemoveBuilding(original.InstanceId, original.TownId, original.HouseholdId).Applied);
        var placed = world.PlaceBuilding("replacement-warehouse", original.DefinitionId, original.Position);
        Assert.True(placed.Applied, placed.Failure);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        AssertPinned(Order(world, receipt), original, original.TownId!);
        Assert.Equal((actor, 8), (world.Society.Inventory.GetLot("pinned-donation").OwnerId,
            world.Society.Inventory.GetLot("pinned-donation").Quantity));
        Assert.Equal(0, Stored(world, "replacement-warehouse", "wood"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
    }

    [Fact]
    public async Task StoreOrdersCreditTheFinalDepositAndCapBothLoadsAndTheRequestedQuantityAcrossReplay()
    {
        var state = Prepared(House);
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "store-cloth", "cloth", Household, 12, conditionBasisPoints: 7_000, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "stock-cloth", "stock five cloth in my Store");
        await Tick(world);
        var carried = Assert.Single(world.Society.Inventory.Lots,
            lot => lot.ItemKind == "cloth" && lot.DeliveryBuildingId == Store);
        Assert.Equal((actor, 4, (string?)null), (carried.OwnerId, carried.Quantity, carried.StorageBuildingId));
        Assert.Equal(8, world.Society.Inventory.GetLot("store-cloth").Quantity);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).LastEffectId);
        Assert.Equal(0, Stored(world, Store, "cloth"));
        AssertPinned(Order(world, receipt), Building(state, Store), Household);
        Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_picked_up");
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        using var replay = Reload(world);
        var previous = 0;
        for (var step = 0; step < 48 && Order(world, receipt).Status != "finished"; step++)
        {
            await TickTogether(world, replay);
            var completed = Order(world, receipt).CompletedUnits;
            Assert.InRange(completed - previous, 0, 4);
            Assert.Equal(completed, Stored(world, Store, "cloth"));
            previous = completed;
        }
        Assert.Equal(("finished", 5, "goods_items"),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        Assert.Equal(7, world.Society.Inventory.GetLot("store-cloth").Quantity);
        Assert.Equal(12, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "cloth").Sum(lot => lot.Quantity));
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Store), lot =>
        {
            Assert.Equal(Household, lot.OwnerId);
            Assert.Equal(7_000, lot.ConditionBasisPoints);
            Assert.Null(lot.DeliveryBuildingId);
        });
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind == "owner_stock_picked_up"));
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind == "owner_stock_delivered"));
        await TickTogether(world, replay);
        Assert.Equal(5, Stored(world, Store, "cloth"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoreOrdersKeepTheNativePersonalAndHouseholdFoodReserves(bool householdStock)
    {
        var state = Prepared(householdStock ? House : Store);
        var actor = Actor(state);
        var owner = householdStock ? Household : actor;
        var reserve = householdStock ? 2 * state.Society.Society.GetHousehold(Household).MemberIds
            .Count(id => state.Inhabitants.Any(person => person.InhabitantId == id)) : 2;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reserve-berries", "berries", owner,
            reserve + 4, storageBuildingId: householdStock ? House : null);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "stock-food", "stock six berries in my Store");
        for (var step = 0; step < 24 && Order(world, receipt).Status != "blocked"; step++) await Tick(world);
        Assert.Equal(("blocked", 4), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(reserve, world.Society.Inventory.GetLot("reserve-berries").Quantity);
        Assert.Equal(owner, world.Society.Inventory.GetLot("reserve-berries").OwnerId);
        Assert.Equal(4, Stored(world, Store, "berries"));
        Assert.Equal(reserve + 4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(reserve, world.Society.Inventory.GetLot("reserve-berries").Quantity);
    }

    [Fact]
    public async Task StoreOrdersStopAtTheEightItemShelfTargetWithoutLosingTheRemainingCargo()
    {
        var state = Prepared(Store);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "shelf-cloth", "cloth", Household, 6, storageBuildingId: Store);
        inventory = InventoryFixture.AddLot(inventory, "spare-cloth", "cloth", actor, 8);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "fill-shelf", "stock four cloth in my Store");
        await Tick(world);
        Assert.Equal(("doing", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        await Tick(world);
        Assert.Equal(("blocked", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(8, Stored(world, Store, "cloth"));
        Assert.Equal((actor, 6), (world.Society.Inventory.GetLot("spare-cloth").OwnerId,
            world.Society.Inventory.GetLot("spare-cloth").Quantity));
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoreOrdersProtectTheBestToolButCanStockAnActualSpare(bool betterTool)
    {
        var state = Prepared(Store);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "wooden-axe", "wooden_axe", actor, 1);
        if (betterTool) inventory = InventoryFixture.AddLot(inventory, "iron-axe", "iron_axe", actor, 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "stock-tool", "stock one wooden axe in my Store");
        await Tick(world);
        Assert.Equal((betterTool ? "finished" : "blocked", betterTool ? 1 : 0),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(betterTool ? Household : actor, world.Society.Inventory.GetLot("wooden-axe").OwnerId);
        Assert.Equal(betterTool ? Store : null, world.Society.Inventory.GetLot("wooden-axe").StorageBuildingId);
        var best = ToolProgressionRules.BestUsableTool(world.Society.Inventory, actor, ToolFamily.Axe)!;
        Assert.Equal(betterTool ? "iron-axe" : "wooden-axe", best.Id);
        Assert.Equal(10_000, best.ConditionBasisPoints);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoreOrdersCannotPickUpForeignOrReservedStock(bool reserved)
    {
        var state = Prepared(House);
        var actor = Actor(state);
        var owner = reserved ? Household : OtherHousehold;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "protected-cloth", "cloth", owner, 8,
            storageBuildingId: reserved ? House : "first-town-house-b");
        if (reserved) inventory = InventoryFixture.Reserve(inventory, "cloth-reserved", owner, "protected-cloth", 8, "other_work", 1_000);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "protected-stock", "stock two cloth in my Store");
        await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal((owner, 8), (world.Society.Inventory.GetLot("protected-cloth").OwnerId,
            world.Society.Inventory.GetLot("protected-cloth").Quantity));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "owner_stock_picked_up" or "owner_stock_delivered");
        if (reserved) Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("cloth-reserved").State);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCollectionsShareOnePersonalStoreFoodReserveAcrossReplay(bool split)
    {
        var state = Prepared(split ? House : Store);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collected-berries", "berries", actor, 4,
            storageBuildingId: split ? House : null);
        IDecisionProvider Provider(string id) => id == actor ? new DeterministicDecisionProvider() : new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), Provider);
        if (split)
        {
            await Complete(Submit(world, actor, "collect-first", "collect two berries"));
            await Complete(Submit(world, actor, "collect-second", "collect two berries"));
            Assert.Equal(2, CarriedLots().Length);
            Assert.All(CarriedLots(), lot => Assert.Equal(2, lot.Quantity));
            var store = Building(state, Store);
            await Complete(Submit(world, actor, "walk-to-store", string.Create(CultureInfo.InvariantCulture,
                $"go to ({store.Position.X},{store.Position.Y})")));
        }
        var receipt = Submit(world, actor, "stock-collected-food", "stock two berries in my Store");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 12 && Order(world, receipt).Status != "finished"; tick++)
            await TickTogether(world, replay);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(2, Stored(world, Store, "berries"));
        Assert.Equal(2, CarriedLots().Sum(lot => lot.Quantity));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Store), lot => Assert.Equal(Household, lot.OwnerId));
        await TickTogether(world, replay);
        Assert.Equal(2, Stored(world, Store, "berries"));
        world.Validate();

        InventoryLot[] CarriedLots() => world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            lot.ItemKind == "berries" && PersonalEquipmentRules.IsCarried(lot, actor)).ToArray();
        async Task Complete(OwnerInstructionReceipt instruction)
        {
            for (var tick = 0; tick < 20 && Order(world, instruction).Status != "finished"; tick++) await Tick(world);
            Assert.Equal("finished", Order(world, instruction).Status);
        }
    }

    private static byte[] CreateBaseline()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new PublicStockChoices());
        var state = generated.ExportState();
        var house = Building(state, House);
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "store-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "store-build-" + cost.ResourceId, cost.ResourceId, Household, cost.Amount);
        using var placing = Restore(WithInventory(state, inventory));
        var result = state.Map.Tiles.Select(tile => tile.Position).Where(point => point != house.Position)
            .OrderBy(point => state.Map.FootDistance(point, house.Position))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .Select(point => placing.PlaceBuilding(Store, definition.CanonicalId, point, Household))
            .First(attempt => attempt.Applied);
        Assert.Equal(Store, result.InstanceId);
        return PrivateWorldRuntimeCodec.Encode(placing.ExportState());
    }

    private static PrivateWorldRuntimeState Prepared(string buildingId, bool adjacent = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        var actor = Actor(state);
        var building = Building(state, buildingId);
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position).ToHashSet();
        var position = adjacent ? state.Map.FootNeighbors(building.Position)
            .OrderBy(point => Math.Abs(point.X - building.Position.X) + Math.Abs(point.Y - building.Position.Y))
            .First(point => state.Map.IsPassable(point) && !occupied.Contains(point))
            : building.Position;
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [], Offers = [] };
        return WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 10_000,
            }).ToArray(),
        };
    }

    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.GetHousehold(Household).MemberIds[0];
    private static PlacedBuilding Building(PrivateWorldRuntimeState state, string id) => state.WorldSimulation!.Buildings.Single(building => building.InstanceId == id);
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static int Stored(PrivateWorldRuntime world, string building, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.StorageBuildingId == building && lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static void AssertPinned(OwnerInstructionOrder order, PlacedBuilding building, string owner) =>
        Assert.Equal((building.InstanceId, owner, building.Position),
            (order.TargetStorageBuildingId, order.TargetStorageOwnerId, order.TargetStoragePosition));
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new PublicStockChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        return replay;
    }
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    private sealed class PublicStockChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var selected = request.Observation.OperativeOrderInstructionId is not null
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "deliver_stock")?.Id ?? "safe_idle"
                : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
