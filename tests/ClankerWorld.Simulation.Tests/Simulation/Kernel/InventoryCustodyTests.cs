using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class InventoryCustodyTests
{
    [Fact]
    public void SplittingOrChangingTheOwnerOfStockInPlaceCreatesNoPhysicalStorageChange()
    {
        var inventory = InventoryFixture.AddLot(InventoryFixture.CreateGenesis([]), "wood", "wood", "first", 4,
            storageBuildingId: "house");
        Assert.Equal(new InventoryStorageChange("house", "wood", 4), Assert.Single(inventory.Events[^1].StorageChanges!));
        var split = InventoryFixture.SplitLot(inventory, "wood", 1, "split-wood");
        Assert.Null(split.Events[^1].StorageChanges);
        var transferred = InventoryFixture.Transfer(split, "gift", "first", "second", "split-wood", 1,
            "gift", destinationStorageBuildingId: "house");
        Assert.Null(transferred.Events[^1].StorageChanges);
        Assert.Equal(4, transferred.Lots.Sum(lot => lot.Quantity));
        Assert.Equal("second", transferred.GetLot("split-wood").OwnerId);
        Assert.Equal("house", transferred.GetLot("split-wood").StorageBuildingId);
        Assert.Equal(InventoryDigest.Events(transferred.Events), InventoryDigest.Events(
            InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(transferred)).Events));
    }

    [Fact]
    public void ABorrowedBrokenAxeCanBeReturnedWithoutBecomingPersonalProperty()
    {
        var inventory = InventoryFixture.CreateGenesis(
            [new("axe", "stone_axe", "household", 1, 0, 10_000, 0, CarrierId: "departing-adult")]);
        var before = InventoryCheckpointCodec.Encode(inventory);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Relocate(inventory, "take", "axe",
            "departing-adult", 1, carrierId: "departing-adult"));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));
        var returned = InventoryFixture.Relocate(inventory, "return", "axe", "household", 1, storageBuildingId: "old-house");
        Assert.Equal(("household", "old-house", 0),
            (returned.GetLot("axe").OwnerId, returned.GetLot("axe").StorageBuildingId, returned.GetLot("axe").ConditionBasisPoints));
        Assert.Null(returned.GetLot("axe").CarrierId);
    }

    [Fact]
    public void APartialPickupLeavesReservedStockAtItsSourceAndConservesQuantity()
    {
        var inventory = InventoryFixture.CreateGenesis(
            [new("food", "food", "household", 6, 10_000, 7_000, 0, StorageBuildingId: "house")]);
        inventory = InventoryFixture.Reserve(inventory, "care", "household", "food", 2, "child_food", 20);
        Assert.Null(inventory.Events[^1].StorageChanges);
        var before = InventoryCheckpointCodec.Encode(inventory);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Relocate(inventory, "too-much", "food",
            "household", 5, carrierId: "adult"));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));
        var moved = InventoryFixture.Relocate(inventory, "pickup", "food", "household", 4, carrierId: "adult");
        var remaining = moved.GetLot("food");
        var carried = Assert.Single(moved.Lots, lot => lot.CarrierId == "adult");
        var pickup = moved.Events[^1];
        Assert.Equal(inventory.EventHistoryFloor + inventory.Events.Count + 1, pickup.EventId);
        Assert.Equal("inventory_relocated", pickup.Kind);
        Assert.Equal(new InventoryStorageChange("house", "food", -4), Assert.Single(pickup.StorageChanges!));
        Assert.Equal((2, "house", "household"), (remaining.Quantity, remaining.StorageBuildingId, remaining.OwnerId));
        Assert.Equal((4, "household", "food", 7_000),
            (carried.Quantity, carried.OwnerId, carried.ProvenanceLotId, carried.FreshnessBasisPoints));
        Assert.Equal(6, moved.Lots.Sum(lot => lot.Quantity));
        Assert.Equal("food", moved.GetReservation("care").LotId);
        Assert.Equal(InventoryReservationState.Reserved, moved.GetReservation("care").State);
        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(moved));
        var fed = InventoryFixture.ConsumeReservation(restored, "care");
        Assert.Equal(pickup.StorageChanges, restored.Events[^1].StorageChanges);
        Assert.Equal(new InventoryStorageChange("house", "food", -2), Assert.Single(fed.Events[^1].StorageChanges!));
        Assert.Equal(4, fed.Lots.Sum(lot => lot.Quantity));
        Assert.Equal(carried, fed.GetLot(carried.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnershipTransferClearsThePreviousCarrierAndPreservesAnyRemainder(bool partial)
    {
        var inventory = InventoryFixture.CreateGenesis(
            [new("wood", "wood", "household", 3, 6_000, 10_000, 0, CarrierId: "borrower")]);
        var donated = InventoryFixture.Transfer(inventory, "donation", "household", "recipient", "wood",
            partial ? 1 : 3, "donation", destinationStorageBuildingId: "recipient-house");
        var received = Assert.Single(donated.Lots, lot => lot.OwnerId == "recipient");
        Assert.Null(received.CarrierId);
        Assert.Equal("recipient-house", received.StorageBuildingId);
        Assert.Equal(6_000, received.ConditionBasisPoints);
        Assert.Equal(3, donated.Lots.Sum(lot => lot.Quantity));
        if (partial)
        {
            var remainder = donated.GetLot("wood");
            Assert.Equal(("household", "borrower", 2), (remainder.OwnerId, remainder.CarrierId, remainder.Quantity));
        }
    }

    [Theory]
    [InlineData("adult", "house", false)]
    [InlineData("adult", null, true)]
    [InlineData(null, "house", true)]
    [InlineData(" adult ", null, false)]
    [InlineData("", null, false)]
    [InlineData(null, " house ", false)]
    public void InvalidDestinationCannotPublishAConflictingOrMalformedLocation(string? carrier,
        string? storage, bool onGround)
    {
        var inventory = InventoryFixture.CreateGenesis(
            [new("coat", "clothing", "adult", 1, 10_000, 10_000, 0, StorageBuildingId: "source")]);
        var before = InventoryCheckpointCodec.Encode(inventory);
        Assert.Throws<InvalidDataException>(() => InventoryFixture.Relocate(inventory, "invalid", "coat", "adult", 1,
            carrierId: carrier, storageBuildingId: storage, groundPosition: onGround ? new(2, 3) : null));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));
    }
}
