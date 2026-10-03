using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class InventoryHandcartTests
{
    [Fact]
    public void PartialLoadPreservesCarriedReservedRemainderAndAccountsCargoOnce()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("cart", "handcart", "owner", 1, 10_000, 10_000, 0, GroundPosition: new(2, 3)),
            new("ballast", "stone", "owner", 30, 10_000, 10_000, 0, ContainerLotId: "cart"),
            new("wood", "wood", "owner", 5, 10_000, 10_000, 0),
        ]);
        inventory = InventoryFixture.Reserve(inventory, "held", "owner", "wood", 3, "future_work", 100);
        var loaded = InventoryFixture.LoadHandcart(inventory, "partial", "owner", "cart", "wood", 2);
        Assert.Equal(3, loaded.GetLot("wood").Quantity);
        Assert.Null(loaded.GetLot("wood").GroundPosition);
        Assert.Null(loaded.GetLot("wood").ContainerLotId);
        Assert.Equal(inventory.GetReservation("held"), loaded.GetReservation("held"));
        Assert.Equal(3, PersonalEquipmentRules.CarriedQuantity(loaded, "owner", null));
        Assert.Equal(32, loaded.Lots.Where(lot => lot.ContainerLotId == "cart").Sum(lot => lot.Quantity));
        Assert.Equal(5, loaded.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(loaded));
        Assert.Equal(InventoryDigest.State(loaded), InventoryDigest.State(restored));
        var selected = restored.Lots.Single(lot => lot.ContainerLotId == "cart" && lot.ItemKind == "wood");
        var unloaded = InventoryFixture.UnloadHandcart(restored, "unload", "owner", "cart", selected.Id, 2);
        Assert.Equal(5, PersonalEquipmentRules.CarriedQuantity(unloaded, "owner", null));
        Assert.Equal(30, unloaded.Lots.Where(lot => lot.ContainerLotId == "cart").Sum(lot => lot.Quantity));
    }

    [Fact]
    public void CargoInAnyonesCustodyCannotBeLoaded()
    {
        foreach (var carrier in new[] { "owner", "borrower" })
        {
            var inventory = InventoryFixture.CreateGenesis([
                new("cart", "handcart", "owner", 1, 10_000, 10_000, 0, GroundPosition: new(2, 3)),
                new("wood", "wood", "owner", 5, 10_000, 10_000, 0, CarrierId: carrier),
            ]);
            Assert.Throws<InvalidOperationException>(() =>
                InventoryFixture.LoadHandcart(inventory, "custody", "owner", "cart", "wood", 2));
        }
    }

    [Fact]
    public void BrokenCartUnloadsSpoiledGoodsAndTransferKeepsEveryPhysicalFact()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("cart", "handcart", "owner", 1, 0, 10_000, 0, GroundPosition: new(2, 3)),
            new("crop", "potato", "owner", 12, 7_000, 0, 0, ContainerLotId: "cart"),
            new("other", "wood", "other-household", 20, 10_000, 10_000, 0),
        ]);
        var changed = InventoryFixture.Transfer(inventory, "gift", "owner", "recipient", "cart", 1, "gift");
        Assert.Equal(inventory.GetLot("other"), changed.GetLot("other"));
        Assert.Equal(inventory.GetLot("cart") with { OwnerId = "recipient" }, changed.GetLot("cart"));
        Assert.Equal(inventory.GetLot("crop") with { OwnerId = "recipient" }, changed.GetLot("crop"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.MoveHandcart(changed, "recipient", "cart", new(3, 3), 20));
        var unloaded = InventoryFixture.UnloadHandcart(changed, "ground", "recipient", "cart", "crop", 12, ontoGround: true);
        Assert.Equal((12, 7_000, 0, new InventoryGroundPosition(2, 3)),
            (unloaded.GetLot("crop").Quantity, unloaded.GetLot("crop").ConditionBasisPoints,
                unloaded.GetLot("crop").FreshnessBasisPoints, unloaded.GetLot("crop").GroundPosition));
        Assert.Null(unloaded.GetLot("crop").ContainerLotId);
    }

    [Fact]
    public void RejectedLoadingAndReservedTravelLeaveCheckpointUnchanged()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("cart", "handcart", "owner", 1, 10_000, 10_000, 0, GroundPosition: new(2, 3)),
            new("wood", "wood", "owner", 33, 10_000, 10_000, 0),
            new("remote", "stone", "owner", 1, 10_000, 10_000, 0, GroundPosition: new(9, 9)),
        ]);
        var digest = InventoryDigest.State(inventory);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.LoadHandcart(inventory, "full", "owner", "cart", "wood", 33));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.LoadHandcart(inventory, "remote", "owner", "cart", "remote", 1));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.LoadHandcart(inventory, "theft", "other", "cart", "wood", 1));
        Assert.Equal(digest, InventoryDigest.State(inventory));
        var loaded = InventoryFixture.LoadHandcart(inventory, "load", "owner", "cart", "wood", 2);
        var cargo = loaded.Lots.Single(lot => lot.ContainerLotId == "cart");
        var reserved = InventoryFixture.Reserve(loaded, "held", "owner", cargo.Id, 1, "work", 100);
        var heldDigest = InventoryDigest.State(reserved);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.MoveHandcart(reserved, "owner", "cart", new(3, 3), 20));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.UnloadHandcart(reserved, "held-unload", "owner", "cart", cargo.Id, 1));
        Assert.Equal(heldDigest, InventoryDigest.State(reserved));
    }
}
