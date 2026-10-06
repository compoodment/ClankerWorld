using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldCustodyOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string OtherHousehold = "household:camp-beta";
    private const string House = "first-town-house-a";
    private const string OtherHouse = "first-town-house-b";
    private static readonly Lazy<byte[]> Baseline = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("personal-storage-orders", _ => new CustodyChoices());
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData("iron", "iron")]
    [InlineData("cloth", "cloth")]
    [InlineData("grain_seed", "grain seeds")]
    [InlineData("tree_seed", "tree seeds")]
    [InlineData("potatoes", "potatoes")]
    [InlineData("bandage", "bandages")]
    [InlineData("medicine", "medicine")]
    [InlineData("gold_ornament", "gold ornaments")]
    public async Task ExactGoodsAreCollectedAndStoredWithRealReceiptsAndUnchangedOwnership(string kind, string subject)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "custody-goods", kind, actor, 3,
            conditionBasisPoints: 6_000, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "other-goods", "rope", actor, 1, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var collected = Submit(world, actor, "collect-exact", "collect two " + subject);
        await Tick(world);
        Assert.Equal(("collect_goods", "finished", 2, "goods_items", kind),
            (Order(world, collected).Action, Order(world, collected).Status, Order(world, collected).CompletedUnits,
                Order(world, collected).ProgressUnit, Order(world, collected).TargetItemKind));
        Assert.Equal(2, Carried(world, actor, kind));
        Assert.Equal(1, world.Society.Inventory.GetLot("custody-goods").Quantity);
        Assert.Equal(House, world.Society.Inventory.GetLot("custody-goods").StorageBuildingId);
        Assert.NotNull(Order(world, collected).LastEffectId);
        var stored = Submit(world, actor, "store-exact", "store two " + subject);
        await Tick(world);
        Assert.Equal(("store_goods", "finished", 2, "goods_items"),
            (Order(world, stored).Action, Order(world, stored).Status, Order(world, stored).CompletedUnits,
                Order(world, stored).ProgressUnit));
        Assert.NotEqual(Order(world, collected).LastEffectId, Order(world, stored).LastEffectId);
        Assert.Equal(0, Carried(world, actor, kind));
        var lots = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind && lot.OwnerId == actor).ToArray();
        Assert.Equal(3, lots.Sum(lot => lot.Quantity));
        Assert.All(lots, lot =>
        {
            Assert.Equal(House, lot.StorageBuildingId);
            Assert.Equal(6_000, lot.ConditionBasisPoints);
            Assert.Null(lot.CarrierId);
        });
        Assert.Equal(1, world.Society.Inventory.GetLot("other-goods").Quantity);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
        AssertPinned(Order(world, stored), Home(state));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == stored.InstructionId).Order!;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldInstructionOrder>(
            JsonSerializer.Serialize(projected, json), json)!;
        Assert.Equal((kind, "goods_items", 2), (client.TargetItemKind, client.ProgressUnit, client.CompletedUnits));
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData("storage_pot", "storage pots", "grain", 8)]
    [InlineData("water_jug", "water jugs", "fresh_water", 4)]
    public async Task AFullVesselMovesAsOneItemWithEveryContentLotPreserved(string kind, string subject, string contents, int amount)
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithCarryAid(state);
        var inventory = AddVessel(state.Society.Society.Inventory, actor, kind, contents, amount, House);
        using var world = Restore(WithInventory(state, inventory));
        var collected = Submit(world, actor, "collect-vessel", "collect one " + subject);
        await Tick(world);
        Assert.Equal(("finished", 1, "goods_items"),
            (Order(world, collected).Status, Order(world, collected).CompletedUnits, Order(world, collected).ProgressUnit));
        Assert.Equal(amount + 1, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));
        AssertFamily(world, actor, null, amount);
        using var replay = Reload(world);
        var stored = Submit(world, actor, "store-vessel", "store one " + subject);
        var replayStored = Submit(replay, actor, "store-vessel", "store one " + subject);
        Assert.Equal(stored, replayStored);
        await TickTogether(world, replay);
        Assert.Equal(("finished", 1), (Order(world, stored).Status, Order(world, stored).CompletedUnits));
        AssertFamily(world, actor, House, amount);
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind is "personal_goods_collected" or "personal_goods_stored"));
    }

    [Theory]
    [InlineData("collect", "carry-room")]
    [InlineData("collect", "reserved-content")]
    [InlineData("store", "house-room")]
    [InlineData("store", "reserved-content")]
    public async Task VesselCustodyNeverMovesOnlyTheShellOrBypassesAReservedChild(string action, string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = AddVessel(state.Society.Society.Inventory, actor, "water_jug", "fresh_water", 4,
            action == "collect" ? House : null);
        if (boundary == "carry-room") inventory = InventoryFixture.AddLot(inventory, "ballast", "wood", actor, 4);
        if (boundary == "house-room") inventory = FillHouse(state, inventory, House, 4);
        if (boundary == "reserved-content") inventory = InventoryFixture.Reserve(inventory,
            "held-water", actor, "vessel-contents", 1, "other_work", 1_000);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "blocked-vessel", action + " one water jug");
        for (var tick = 0; tick < 2; tick++) await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        AssertFamily(world, actor, action == "collect" ? House : null, 4);
        Assert.DoesNotContain(world.ExportState().Events,
            item => item.Kind is "personal_goods_collected" or "personal_goods_stored");
        if (boundary == "reserved-content")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("held-water").State);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task AFullBorrowedJugReturnsToItsLenderOnlyAfterRoomForTheWholeFamilyExists()
    {
        var state = Prepared(atOtherHouse: true);
        var actor = Actor(state);
        var inventory = AddVessel(state.Society.Society.Inventory, OtherHousehold, "water_jug", "fresh_water", 4, OtherHouse);
        inventory = InventoryFixture.Relocate(inventory, "borrow-jug", "vessel", OtherHousehold, 1, carrierId: actor);
        inventory = FillHouse(state, inventory, OtherHouse, 4);
        using var initial = Restore(WithInventory(state, inventory));
        var receipt = Submit(initial, actor, "return-jug", "return one borrowed water jug");
        await Tick(initial);
        Assert.Equal(("blocked", 0), (Order(initial, receipt).Status, Order(initial, receipt).CompletedUnits));
        AssertFamily(initial, OtherHousehold, null, 4, actor);
        state = initial.ExportState();
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != "storage-filler").ToArray(),
        });
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        using var replay = Reload(world);
        for (var tick = 0; tick < 6 && Order(world, receipt).Status != "finished"; tick++) await TickTogether(world, replay);
        Assert.Equal(("finished", 1, "goods_items"),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        AssertPinned(Order(world, receipt), Home(state, OtherHouse));
        AssertFamily(world, OtherHousehold, OtherHouse, 4);
        Assert.Single(world.ExportState().Events, item => item.Kind == "borrowed_goods_returned");
        Assert.Equal(0, Carried(world, actor, "water_jug"));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => (lot.Id is "vessel" or "vessel-contents") && lot.OwnerId == actor);
    }

    [Theory]
    [InlineData("cloth")]
    [InlineData("bread")]
    [InlineData("restaurant_meal")]
    public async Task ACountedBorrowedReturnSpansTwoLoansWithoutDonatingTheBorrowersOwnGoods(string kind)
    {
        var state = Prepared(atOtherHouse: true);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "loan-a", kind, OtherHousehold, 2,
            storageBuildingId: OtherHouse);
        inventory = InventoryFixture.AddLot(inventory, "loan-b", kind, OtherHousehold, 2, storageBuildingId: OtherHouse);
        inventory = InventoryFixture.Relocate(inventory, "borrow-cloth-a", "loan-a", OtherHousehold, 2, carrierId: actor);
        inventory = InventoryFixture.Relocate(inventory, "borrow-cloth-b", "loan-b", OtherHousehold, 2, carrierId: actor);
        inventory = InventoryFixture.Reserve(inventory, "loan-b-held", OtherHousehold, "loan-b", 1, "other_work", 1_000);
        inventory = InventoryFixture.AddLot(inventory, "my-cloth", kind, actor, 2);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "partial-return", "return three borrowed " + kind.Replace('_', ' '));
        await Tick(world);
        Assert.Equal(("doing", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(OtherHouse, world.Society.Inventory.GetLot("loan-a").StorageBuildingId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("loan-b").CarrierId);
        Assert.Null(Order(world, receipt).TargetLotId);
        var firstReceipt = Order(world, receipt).LastEffectId;
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("return_borrowed", "finished", 3, "goods_items"),
            (Order(world, receipt).Action, Order(world, receipt).Status, Order(world, receipt).CompletedUnits,
                Order(world, receipt).ProgressUnit));
        var remaining = world.Society.Inventory.GetLot("loan-b");
        Assert.Equal((OtherHousehold, actor, 1, (string?)null),
            (remaining.OwnerId, remaining.CarrierId, remaining.Quantity, remaining.StorageBuildingId));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("loan-b-held").State);
        Assert.Equal(1, world.Society.Inventory.GetReservation("loan-b-held").Quantity);
        var returned = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "loan-b");
        Assert.Equal((OtherHousehold, (string?)null, 1, OtherHouse),
            (returned.OwnerId, returned.CarrierId, returned.Quantity, returned.StorageBuildingId));
        Assert.Equal((actor, 2, (string?)null),
            (world.Society.Inventory.GetLot("my-cloth").OwnerId, world.Society.Inventory.GetLot("my-cloth").Quantity,
                world.Society.Inventory.GetLot("my-cloth").StorageBuildingId));
        Assert.NotNull(Order(world, receipt).LastEffectId);
        Assert.NotEqual(firstReceipt, Order(world, receipt).LastEffectId);
        Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == OtherHousehold &&
            lot.ItemKind == kind && lot.StorageBuildingId == OtherHouse).Sum(lot => lot.Quantity));
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind == "borrowed_goods_returned"));
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task AWalkingReturnKeepsItsSelectedLoanAndCannotCreditSomebodyElsesDelivery()
    {
        var state = Prepared(distant: true, atOtherHouse: true);
        var actor = Actor(state);
        var inventory = state.Society.Society.Inventory;
        foreach (var id in new[] { "loan-a", "loan-b" })
        {
            inventory = InventoryFixture.AddLot(inventory, id, "cloth", OtherHousehold, 2, storageBuildingId: OtherHouse);
            inventory = InventoryFixture.Relocate(inventory, "borrow-" + id, id, OtherHousehold, 2, carrierId: actor);
        }
        using var initial = Restore(WithInventory(state, inventory));
        var receipt = Submit(initial, actor, "bound-loan", "return two borrowed cloth");
        await Tick(initial);
        Assert.Equal(0, Order(initial, receipt).CompletedUnits);
        Assert.Equal("loan-a", Order(initial, receipt).TargetLotId);
        AssertPinned(Order(initial, receipt), Home(state, OtherHouse));
        state = initial.ExportState();
        state = WithInventory(state, InventoryFixture.Relocate(state.Society.Society.Inventory,
            "external-delivery", "loan-a", OtherHousehold, 2, storageBuildingId: OtherHouse));
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0, "loan-a"),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).TargetLotId));
        Assert.Equal(actor, world.Society.Inventory.GetLot("loan-b").CarrierId);
        Assert.Equal(2, world.Society.Inventory.GetLot("loan-b").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "borrowed_goods_returned");
    }

    [Theory]
    [InlineData("reserved-loan")]
    [InlineData("reserved-content")]
    [InlineData("delivery")]
    [InlineData("private-owner")]
    public async Task ReturnOrdersDoNotTakeProtectedCargoOrInventALenderHouse(string boundary)
    {
        var state = Prepared(atOtherHouse: true);
        var actor = Actor(state);
        var otherPerson = state.Society.Society.Inhabitants.First(person => person.HouseholdId == OtherHousehold).Id;
        var owner = boundary == "private-owner" ? otherPerson : OtherHousehold;
        var inventory = boundary == "reserved-loan"
            ? InventoryFixture.AddLot(state.Society.Society.Inventory, "vessel", "cloth", owner, 1, storageBuildingId: OtherHouse)
            : AddVessel(state.Society.Society.Inventory, owner, "water_jug", "fresh_water", 4, OtherHouse);
        inventory = InventoryFixture.Relocate(inventory, "borrow-protected", "vessel", owner, 1, carrierId: actor);
        if (boundary == "delivery")
        {
            // Delivery custody remains personal property until the promised household transfer occurs.
            inventory = InventoryFixture.Transfer(inventory, "promise-vessel", owner, actor, "vessel", 1, "delivery",
                destinationDeliveryBuildingId: House);
        }
        if (boundary is "reserved-loan" or "reserved-content")
            inventory = InventoryFixture.Reserve(inventory, "protected-loan", owner,
                boundary == "reserved-loan" ? "vessel" : "vessel-contents", 1, "other_work", 1_000);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "protected-return",
            boundary == "reserved-loan" ? "return borrowed cloth" : "return borrowed water jugs");
        await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(inventory.GetLot("vessel") with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("vessel"));
        if (boundary != "reserved-loan") Assert.Equal(4, world.Society.Inventory.GetLot("vessel-contents").Quantity);
        if (boundary is "reserved-loan" or "reserved-content")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("protected-loan").State);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "borrowed_goods_returned");
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData("wood", "move")]
    [InlineData("basket", "remove")]
    [InlineData("cloth", "reassign")]
    [InlineData("medicine", "departure")]
    public async Task StorageKeepsItsOriginalHouseIdentityOwnerAndPositionAcrossWorldChanges(string kind, string change)
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "pinned-goods", kind, actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "rebuild-materials", "wood", Household, 64);
        using var world = Restore(WithInventory(state, inventory));
        var original = Home(state);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"store two {kind} in my House at ({original.Position.X}, {original.Position.Y})");
        var receipt = Submit(world, actor, "pinned-destination", text);
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertPinned(Order(world, receipt), original);
        Assert.Null(world.Society.Inventory.GetLot("pinned-goods").StorageBuildingId);
        if (change == "departure") Assert.True(world.DisplaceAdult(actor));
        else if (change == "reassign")
        {
            var other = Home(world.ExportState(), OtherHouse);
            Assert.True(world.RemoveBuilding(other.InstanceId, other.TownId, other.HouseholdId).Applied);
            var reassigned = world.ReassignBuilding(House, original.TownId, Household, null, OtherHousehold);
            Assert.True(reassigned.Applied, reassigned.Failure);
        }
        else
        {
            Assert.True(world.RemoveBuilding(House, original.TownId, Household).Applied);
            var id = change == "move" ? House : "replacement-house";
            IEnumerable<GridPoint> candidates = change == "move"
                ? Nearby(original.Position).Where(point => point != original.Position)
                : [original.Position];
            Assert.Contains(candidates, position => world.PlaceBuilding(id, original.DefinitionId, position, Household).Applied);
        }
        using var replay = Reload(world);
        for (var tick = 0; tick < 3; tick++) await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.False(string.IsNullOrWhiteSpace(Order(world, receipt).BlockedReason));
        AssertPinned(Order(world, receipt), original);
        Assert.Null(world.Society.Inventory.GetLot("pinned-goods").StorageBuildingId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("pinned-goods").OwnerId);
        Assert.Equal(2, world.Society.Inventory.GetLot("pinned-goods").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
    }

    [Fact]
    public async Task StoringOrnamentsLeavesTheSelectedWornUnitWithItsOwner()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "worn-ornament", "gold_ornament", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "spare-ornament", "gold_ornament", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = new(OrnamentLotId: "worn-ornament") } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "store-ornaments", "store two gold ornaments");
        await Tick(world);
        Assert.Equal(("doing", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(House, world.Society.Inventory.GetLot("spare-ornament").StorageBuildingId);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("worn-ornament"), actor));
        Assert.Equal("worn-ornament", world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.OrnamentLotId);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CustodyOrdersInterruptOnlyTheOrdinaryFieldWorkUsingTheMovedTool(bool borrowed, bool moveSpare)
    {
        var state = Prepared();
        var actor = Actor(state);
        var destination = Home(state, borrowed ? OtherHouse : House);
        var occupied = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.RoadTiles ?? []).Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var point = state.Map.FootNeighbors(destination.Position).First(candidate =>
            fertility.CanFarm(candidate) && !occupied.Contains(candidate));
        Assert.InRange(state.Map.FootDistance(point, destination.Position), 0, 1);
        var owner = borrowed ? OtherHousehold : actor;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "working-hoe", "wooden_hoe", owner, 1,
            storageBuildingId: borrowed ? OtherHouse : null);
        if (borrowed)
            inventory = InventoryFixture.Relocate(inventory, "borrow-working-hoe", "working-hoe", owner, 1, carrierId: actor);
        if (moveSpare)
            inventory = InventoryFixture.AddLot(inventory, "a-spare-hoe", "wooden_hoe", actor, 1, conditionBasisPoints: 5_000);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = point } : person).ToArray(),
        };
        using var world = Restore(state);
        var started = world.StartFieldWork(actor, point, FarmWorkKind.Till);
        Assert.True(started.Accepted, started.Message);
        var work = Assert.Single(world.Fields).Work!;
        Assert.Equal("working-hoe", work.HoeLotId);
        Assert.Null(work.OrderInstructionId);
        var text = borrowed ? "return one borrowed wooden hoe" : "store one wooden hoe";
        var receipt = Submit(world, actor, "used-tool", text);
        await Tick(world);
        // Validate before another tick can silently discard the now-invalid tool reference.
        using var replay = Reload(world);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(point, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        if (moveSpare)
        {
            Assert.Equal("working-hoe", Assert.Single(world.Fields).Work!.HoeLotId);
            Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("working-hoe"), actor));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "field_work_interrupted");
        }
        else
        {
            Assert.Empty(world.Fields);
            Assert.Single(world.ExportState().Events, item => item.Kind == "field_work_interrupted");
        }
        Assert.Single(world.ExportState().Events,
            item => item.Kind == (borrowed ? "borrowed_goods_returned" : "personal_goods_stored"));
        var moved = world.Society.Inventory.GetLot(moveSpare ? "a-spare-hoe" : "working-hoe");
        Assert.Equal((owner, destination.InstanceId, 1, (string?)null),
            (moved.OwnerId, moved.StorageBuildingId, moved.Quantity, moved.CarrierId));
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task CancelledRepeatingStorageKeepsCompletedLoadsAndLeavesQueuedCollectionToRun()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "repeat-a", "cloth", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "repeat-b", "cloth", actor, 1);
        using var world = Restore(WithInventory(state, inventory));
        var repeat = Submit(world, actor, "repeat-store", "keep storing cloth until cancelled");
        var queued = world.SubmitInstruction(new("queue-collect", "owner:test", actor, OwnerInstructionKind.MustDo,
            "collect one cloth", Queue: true));
        await Tick(world);
        Assert.Equal(1, Order(world, repeat).CompletedUnits);
        Assert.Equal("queued", Order(world, queued).Status);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(2, Order(world, repeat).CompletedUnits);
        Assert.NotEqual("finished", Order(world, repeat).Status);
        Assert.Equal("queued", Order(world, queued).Status);
        var cancel = new OwnerOrderCancelRequest("cancel-repeat", "owner:test", world.Society.WorldId, actor, repeat.InstructionId);
        Assert.True(world.CancelOrder(cancel).Changed);
        Assert.True(replay.CancelOrder(cancel).Changed);
        for (var tick = 0; tick < 5 && Order(world, queued).Status != "finished"; tick++) await TickTogether(world, replay);
        Assert.Equal(("cancelled", 2), (Order(world, repeat).Status, Order(world, repeat).CompletedUnits));
        Assert.Equal(("finished", 1), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.Equal(1, Carried(world, actor, "cloth"));
        Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "cloth" && lot.StorageBuildingId == House).Sum(lot => lot.Quantity));
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind == "personal_goods_stored"));
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
    }

    [Theory]
    [InlineData("collect one cloth")]
    [InlineData("store one cloth")]
    [InlineData("return one borrowed cloth")]
    public void CustodySavesRejectAnAgentTarget(string text)
    {
        var state = Prepared();
        var actor = Actor(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "agent-target-boundary", text);
        var saved = world.ExportState();
        Assert.NotEqual("unknown", Order(world, receipt).Action);
        using var valid = Restore(saved);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(saved), PrivateWorldRuntimeCodec.Encode(valid.ExportState()));
        var corrupt = saved with
        {
            Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = item.Order! with { TargetAgentId = actor } } : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => Restore(corrupt));
    }

    [Fact]
    public async Task StrictSavesRejectIncompleteDestinationBindingsAndWrongGoodsContracts()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "saved-cloth", "cloth", actor, 2));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "save-boundary", "store two cloth");
        await Tick(world);
        var saved = world.ExportState();
        var order = Order(world, receipt);
        AssertPinned(order, Home(state));
        foreach (var invalid in new[]
        {
            order with { TargetItemKind = null }, order with { TargetItemKind = "fresh_water" },
            order with { TargetStorageBuildingId = null }, order with { TargetStorageOwnerId = null },
            order with { TargetStoragePosition = null }, order with { TargetStoragePosition = new(10_000_001, 0) },
            order with { TargetLotId = "saved-cloth" }, order with { TargetEquipmentKind = "basket" },
            order with { ProgressUnit = "production_batches" },
        })
        {
            var corrupt = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                    ? item with { Order = invalid } : item).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal("finished", Order(world, receipt).Status);
        var finished = world.ExportState();
        var completedOrder = Order(world, receipt);
        var wrongAction = completedOrder with
        {
            LastEffectId = "collect:personal:" + completedOrder.LastEffectId!["store:personal:".Length..],
        };
        Assert.Throws<InvalidDataException>(() => Restore(finished with
        {
            Instructions = finished.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = wrongAction } : item).ToArray(),
        }));
    }

    [Fact]
    public async Task UnsupportedLooseWaterNeverQueriesTheModelOrReplacesAnExistingOrder()
    {
        var state = Prepared();
        var actor = Actor(state);
        var provider = new CustodyChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new CustodyChoices());
        var current = Submit(world, actor, "current-custody", "store cloth");
        var unknown = Submit(world, actor, "loose-water", "collect two fresh water");
        Assert.Equal("not_understood", Order(world, unknown).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        Assert.Empty(provider.Requests);
        for (var tick = 0; tick < 3; tick++) await Tick(world);
        Assert.DoesNotContain(provider.Requests, request => request.OperativeOrderInstructionId == unknown.InstructionId ||
            request.ObserverGuidance?.Any(message => message.InstructionId == unknown.InstructionId) == true);
        Assert.Contains(unknown.InstructionId, world.ExportState().CompletedInstructionIds!);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
    }

    private static PrivateWorldRuntimeState Prepared(bool distant = false, bool atOtherHouse = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        var actor = Actor(state);
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                lot.StorageBuildingId is not (House or OtherHouse)).ToArray(),
        });
        var house = Home(state, atOtherHouse ? OtherHouse : House);
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position).ToHashSet();
        bool Clear(GridPoint point) => state.Map.IsPassable(point) && !occupied.Contains(point) &&
            state.Map.Resources.All(resource => resource.Position != point) && state.Map.CampObjects.All(item => item.Position != point);
        var position = distant ? (from near in state.Map.FootNeighbors(house.Position)
                                  where Clear(near)
                                  from far in state.Map.FootNeighbors(near)
                                  where Clear(far) && state.Map.FootDistance(far, house.Position) >= 2
                                  select far).First() : house.Position;
        return state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                HungerBasisPoints = 10_000,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static PrivateWorldRuntimeState WithCarryAid(PrivateWorldRuntimeState state)
    {
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "carrying-basket", "basket", actor, 1));
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = new(CarryAidLotId: "carrying-basket") } : person).ToArray(),
        };
    }
    private static InventoryCheckpoint AddVessel(InventoryCheckpoint inventory, string owner, string kind,
        string contentKind, int amount, string? building)
    {
        inventory = InventoryFixture.AddLot(inventory, "vessel", kind, owner, 1, storageBuildingId: building);
        return InventoryFixture.AddLot(inventory, "vessel-contents", contentKind, owner, amount,
            storageBuildingId: building, containerLotId: "vessel");
    }
    private static InventoryCheckpoint FillHouse(PrivateWorldRuntimeState state, InventoryCheckpoint inventory, string id, int room)
    {
        var building = Home(state, id);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var existing = inventory.Lots.Where(lot => lot.StorageBuildingId == id).Sum(lot => lot.Quantity);
        var quantity = BuildingStorageRules.Capacity(definition, building)!.Value - existing - room;
        Assert.True(quantity > 0);
        return InventoryFixture.AddLot(inventory, "storage-filler", "stone", building.HouseholdId!, quantity, storageBuildingId: id);
    }
    private static void AssertFamily(PrivateWorldRuntime world, string owner, string? storage, int contents, string? carrier = null)
    {
        var vessel = world.Society.Inventory.GetLot("vessel");
        var content = world.Society.Inventory.GetLot("vessel-contents");
        Assert.Equal((owner, storage, carrier, 1), (vessel.OwnerId, vessel.StorageBuildingId, vessel.CarrierId, vessel.Quantity));
        Assert.Equal((owner, storage, carrier, contents, "vessel"),
            (content.OwnerId, content.StorageBuildingId, content.CarrierId, content.Quantity, content.ContainerLotId));
        Assert.Null(vessel.GroundPosition);
        Assert.Null(content.GroundPosition);
    }
    private static void AssertPinned(OwnerInstructionOrder order, PlacedBuilding house) =>
        Assert.Equal((house.InstanceId, house.HouseholdId, house.Position),
            (order.TargetStorageBuildingId, order.TargetStorageOwnerId, order.TargetStoragePosition));
    private static IEnumerable<GridPoint> Nearby(GridPoint point) => Enumerable.Range(-5, 11).SelectMany(y =>
        Enumerable.Range(-5, 11).Select(x => new GridPoint(point.X + x, point.Y + y)));
    private static PlacedBuilding Home(PrivateWorldRuntimeState state, string id = House) => state.WorldSimulation!.Buildings.Single(building => building.InstanceId == id);
    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new CustodyChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        return replay;
    }
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static int Carried(PrivateWorldRuntime world, string actor, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, lot, actor)).Sum(lot => lot.Quantity);
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private sealed class CustodyChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Requests.Enqueue(request.Observation);
            var selected = request.Observation.OperativeOrderInstructionId is not null
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id is
                    "collect_goods" or "store_goods" or "return_borrowed" or "store_material" or "store_equipment")?.Id ?? "safe_idle"
                : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
