using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class InventoryIndexTests
{
    [Fact]
    public void AuthorityChangesRefreshIndexedFamiliesReservationsPlacesAndOwnershipWithoutChangingEarlierSnapshots()
    {
        var initial = InventoryFixture.CreateGenesis([
            new("pot", "storage_pot", "alpha", 1, 10_000, 10_000, 0, StorageBuildingId: "home"),
            new("food", "berries", "alpha", 5, 10_000, 10_000, 0, StorageBuildingId: "home"),
        ]);
        var first = InventoryIndex.For(initial);
        Assert.Same(first, InventoryIndex.For(initial));
        Assert.Equal(6, first.StoredAt("home").Sum(lot => lot.Quantity));
        var contained = InventoryFixture.PutIntoContainer(initial, "pack", "alpha", "pot", "food", 3);
        var packed = InventoryIndex.For(contained);
        Assert.NotSame(first, packed);
        Assert.Equal(1, first.FamilyQuantity("pot"));
        Assert.Equal(4, packed.FamilyQuantity("pot"));
        var contents = Assert.Single(packed.ContentsOf("pot"));
        Assert.Equal("pot", packed.Root(contents).Id);
        var reserved = InventoryFixture.Reserve(contained, "cook", "alpha", contents.Id, 1, "cook", 20);
        var held = InventoryIndex.For(reserved);
        Assert.Equal(1, held.ReservedQuantity(contents.Id));
        Assert.True(held.HasReservedFamily("pot"));
        Assert.False(InventoryRules.CanMoveFamily(held, held.Find("pot")!));
        Assert.False(InventoryRules.CanTakeOut(held, held.Find(contents.Id)!, "alpha", 1));
        var bytes = InventoryCheckpointCodec.Encode(reserved);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(reserved, "held", "alpha", "bravo", "pot", 1, "gift"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.TakeFromContainer(reserved, "held", "alpha", "alpha", "pot", contents.Id, 1));
        Assert.Equal(bytes, InventoryCheckpointCodec.Encode(reserved));
        var released = InventoryFixture.ReleaseReservation(reserved, "cook");
        var free = InventoryIndex.For(released);
        Assert.Equal(0, free.ReservedQuantity(contents.Id));
        Assert.False(free.HasReservedFamily("pot"));
        Assert.True(InventoryRules.CanTakeOut(free, free.Find(contents.Id)!, "alpha", 1));
        var taken = InventoryFixture.TakeFromContainer(released, "take", "alpha", "alpha", "pot", contents.Id, 1);
        Assert.Equal(3, InventoryIndex.For(taken).FamilyQuantity("pot"));
        var delivered = InventoryFixture.Transfer(taken, "deliver", "alpha", "bravo", "pot", 1,
            "delivery", destinationDeliveryBuildingId: "destination");
        var current = InventoryIndex.For(delivered);
        Assert.Equal(3, current.InboundQuantity("destination"));
        Assert.Equal(new[] { contents.Id, "pot" }.Order(StringComparer.Ordinal), current.InboundTo("destination").Select(lot => lot.Id));
        Assert.All(current.OwnedBy("bravo"), lot => Assert.Equal("destination", lot.DeliveryBuildingId));
        Assert.DoesNotContain(current.StoredAt("home"), lot => lot.Id == "pot" || lot.ContainerLotId == "pot");
        Assert.True(held.HasReservedFamily("pot"));
        Assert.Equal(bytes, InventoryCheckpointCodec.Encode(reserved));
        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(delivered));
        Assert.Equal(current.Lots, InventoryIndex.For(restored).Lots);
        Assert.Equal(3, InventoryIndex.For(restored).InboundQuantity("destination"));
    }
}
