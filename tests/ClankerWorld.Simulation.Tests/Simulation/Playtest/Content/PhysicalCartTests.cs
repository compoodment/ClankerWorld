using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PhysicalCartTests
{
    [Fact]
    public void BrokenCartsRepairAndSpoiledCargoCanBePutDownWithoutDestroyingThePhysicalLots()
    {
        var position = new InventoryGroundPosition(2, 3);
        var inventory = InventoryFixture.CreateGenesis([
            new("broken-cart", "handcart", "alice", 1, 0, 10_000, 0, GroundPosition: position),
            new("spoiled-cargo", "berries", "alice", 4, 10_000, 0, 0, GroundPosition: position, CartId: "cart:broken-cart"),
        ]);
        var repaired = InventoryFixture.RestoreCartCondition(inventory, "broken-cart", "alice");
        Assert.Equal(10_000, repaired.GetLot("broken-cart").ConditionBasisPoints);
        Assert.Equal(inventory.GetLot("spoiled-cargo"), repaired.GetLot("spoiled-cargo"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.RestoreCartCondition(inventory, "broken-cart", "bob"));
        var dropped = InventoryFixture.PutDownCartCargo(repaired, "spoiled-cargo", "alice", "cart:broken-cart", new(3, 3));
        Assert.Equal(4, dropped.GetLot("spoiled-cargo").Quantity);
        Assert.Equal(0, dropped.GetLot("spoiled-cargo").FreshnessBasisPoints);
        Assert.Equal(new InventoryGroundPosition(3, 3), dropped.GetLot("spoiled-cargo").GroundPosition);
        Assert.Null(dropped.GetLot("spoiled-cargo").CartId);
        Assert.Equal(repaired.GetLot("broken-cart"), dropped.GetLot("broken-cart"));
    }

    [Fact]
    public void ReservedCargoCannotBeDroppedAndAReservedCartCannotBeRepaired()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("cart", "handcart", "alice", 1, 5_000, 10_000, 0),
            new("wood", "wood", "alice", 8, 10_000, 10_000, 0, GroundPosition: new(1, 1), CartId: "cart:cart"),
        ]);
        inventory = InventoryFixture.Reserve(inventory, "cart-commitment", "alice", "cart", 1, "gift", 10);
        inventory = InventoryFixture.Reserve(inventory, "cargo-commitment", "alice", "wood", 1, "trade", 10);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.RestoreCartCondition(inventory, "cart", "alice"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.PutDownCartCargo(inventory, "wood", "alice", "cart:cart", new(2, 1)));
        Assert.Equal(8, inventory.GetLot("wood").Quantity);
        Assert.Equal(5_000, inventory.GetLot("cart").ConditionBasisPoints);
        Assert.All(inventory.Reservations, reservation => Assert.Equal(InventoryReservationState.Reserved, reservation.State));
    }

    [Fact]
    public void LoadingSplitsActualStockAndMovesAWholeVesselWithoutChangingOwnersOrCargoIdentity()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("wood", "wood", "alice", 70, 10_000, 10_000, 0),
            new("jug", "water_jug", "alice", 1, 10_000, 10_000, 0, ContainerCapacity: 8),
            new("water", "water", "alice", 8, 10_000, 10_000, 0, ContainerLotId: "jug"),
        ]);
        inventory = InventoryFixture.LoadCart(inventory, "load-wood", "alice", "wood", 60, "cart-one", new(2, 3));
        inventory = InventoryFixture.LoadCart(inventory, "load-jug", "alice", "jug", 1, "cart-one", new(2, 3));
        Assert.Equal(10, inventory.GetLot("wood").Quantity);
        var wood = Assert.Single(inventory.Lots, lot => lot.ItemKind == "wood" && lot.CartId == "cart-one");
        Assert.Equal("wood", wood.ProvenanceLotId);
        Assert.Equal(60, wood.Quantity);
        Assert.Equal(69, inventory.Lots.Where(lot => lot.CartId == "cart-one").Sum(lot => lot.Quantity));
        Assert.All(inventory.Lots.Where(lot => lot.CartId == "cart-one"), lot =>
        { Assert.Equal("alice", lot.OwnerId); Assert.Equal(new InventoryGroundPosition(2, 3), lot.GroundPosition); });
        Assert.Equal("jug", inventory.GetLot("water").ContainerLotId);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(inventory, "remote", "alice", "bob", wood.Id, 1, "gift"));
        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(inventory));
        Assert.Equal(InventoryDigest.State(inventory), InventoryDigest.State(restored));
        restored = InventoryFixture.UnloadCart(restored, "unload-jug", "alice", "alice", "jug", 1, "cart-one");
        Assert.All(restored.Lots.Where(lot => lot.Id is "jug" or "water"), lot =>
        { Assert.Null(lot.CartId); Assert.Null(lot.GroundPosition); });
        Assert.Equal(8, restored.GetLot("water").Quantity);
    }

    [Fact]
    public void CartCapacityStorageAccessAndGiftChecksPreserveActualTownStockAcrossReload()
    {
        using var initial = NormalPathWorld.CreateGenerated("cart-capacity", _ => new ActionCoverageRecorder(true));
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "actual-cart", "handcart", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "town-cart-wood", "wood", warehouse.TownId!, 130, storageBuildingId: warehouse.InstanceId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = warehouse.Position } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(true));
        Assert.True(world.DeployCart(actor, "actual-cart").Applied);
        var cart = Assert.Single(world.WorldSimulation.Carts!);
        Assert.False(world.LoadCartGoods(actor, cart.Id, "town-cart-wood", 130).Applied);
        Assert.Equal(130, world.Society.Inventory.GetLot("town-cart-wood").Quantity);
        Assert.True(world.LoadCartGoods(actor, cart.Id, "town-cart-wood", 128).Applied);
        var cargo = Assert.Single(world.Society.Inventory.Lots, lot => lot.CartId == cart.Id);
        Assert.Equal(warehouse.TownId, cargo.OwnerId);
        Assert.False(world.UnloadCartGoods(actor, cart.Id, cargo.Id, 128).Applied);
        Assert.Equal(128, world.Society.Inventory.GetLot(cargo.Id).Quantity);
        Assert.True(world.PullCart(actor, cart.Id).Applied);
        Assert.True(world.ParkCart(actor).Applied);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new ActionCoverageRecorder(true));
        loaded.Validate();
        Assert.Equal(cargo, loaded.Society.Inventory.GetLot(cargo.Id));
        var projected = Assert.Single(new OwnerWorldObservationStore(loaded).GetSnapshot().Carts!);
        Assert.Equal(128, projected.Load);
        Assert.Null(projected.PullerId);
        Assert.True(loaded.UnloadCartGoods(actor, cart.Id, cargo.Id, 128, warehouse.InstanceId).Applied);
        Assert.DoesNotContain(loaded.Society.Inventory.Lots, lot => lot.CartId == cart.Id);
        Assert.Equal(130, loaded.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.StorageBuildingId == warehouse.InstanceId &&
            lot.Id.StartsWith("town-cart-wood", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
    }
}
