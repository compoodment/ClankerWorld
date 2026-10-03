using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class InventorySpoiledContainerTests
{
    private static readonly HashSet<string> SpoilingFoodKinds = new(["berries"], StringComparer.Ordinal);

    [Fact]
    public void SpoiledContentsLeaveThePotAsOwnedPhysicalStockAndTheSameVesselCanBeRefilled()
    {
        var inventory = Spoil(FilledPot());
        var pot = inventory.GetLot("pot");
        var spoiled = inventory.GetLot("berries");
        Assert.Equal(0, spoiled.FreshnessBasisPoints);
        Assert.Equal(8, spoiled.Quantity);
        Assert.Contains(inventory.Events, item => item is { Kind: "lot_spoiled", Detail: "berries" });

        inventory = InventoryFixture.TakeFromContainer(inventory, "take-three", "alpha", "bravo", "pot", "berries", 3);
        var taken = inventory.GetLot("berries#taken:take-three");
        Assert.Equal("bravo", taken.OwnerId);
        Assert.Equal(3, taken.Quantity);
        Assert.Equal(0, taken.FreshnessBasisPoints);
        Assert.Equal(spoiled.ConditionBasisPoints, taken.ConditionBasisPoints);
        Assert.Equal(spoiled.LastProcessedTick, taken.LastProcessedTick);
        Assert.Equal("berries", taken.ProvenanceLotId);
        Assert.Null(taken.ContainerLotId);
        Assert.Null(taken.StorageBuildingId);
        Assert.Null(taken.DeliveryBuildingId);
        Assert.Null(taken.GroundPosition);
        Assert.Equal(spoiled with { Quantity = 5 }, inventory.GetLot("berries"));
        Assert.Equal(pot, inventory.GetLot("pot"));
        Assert.Contains(inventory.Events, item => item is
        {
            Kind: "container_contents_taken", Detail: "take-three:pot:berries:3:bravo",
        });
        inventory = RoundTrip(inventory);

        var replay = InventoryFixture.TakeFromContainer(inventory, "take-rest", "alpha", "bravo", "pot", "berries", 5);
        inventory = InventoryFixture.TakeFromContainer(RoundTrip(inventory), "take-rest", "alpha", "bravo", "pot", "berries", 5);
        Assert.Equal(InventoryCheckpointCodec.Encode(replay), InventoryCheckpointCodec.Encode(inventory));
        Assert.Equal("berries", inventory.GetLot("berries").Id);
        Assert.Equal("bravo", inventory.GetLot("berries").OwnerId);
        Assert.Null(inventory.GetLot("berries").ContainerLotId);
        Assert.Null(inventory.GetLot("berries").StorageBuildingId);
        Assert.DoesNotContain(inventory.Lots, lot => lot.ContainerLotId == "pot");
        Assert.Equal(8, inventory.Lots.Where(lot => lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        Assert.All(inventory.Lots.Where(lot => lot.ItemKind == "berries"), lot => Assert.Equal(0, lot.FreshnessBasisPoints));

        inventory = InventoryFixture.AddLot(inventory, "new-berries", "berries", "alpha", 8,
            storageBuildingId: "house-alpha");
        inventory = InventoryFixture.PutIntoContainer(inventory, "refill", "alpha", "pot", "new-berries", 8);
        Assert.Equal(pot, inventory.GetLot("pot"));
        Assert.Equal("pot", inventory.GetLot("new-berries").ContainerLotId);
        Assert.Equal(8, inventory.GetLot("new-berries").Quantity);
        Assert.Equal(10_000, inventory.GetLot("new-berries").FreshnessBasisPoints);
        Assert.Equal(16, inventory.Lots.Where(lot => lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        Assert.Equal(2, inventory.Events.Count(item => item.Kind == "container_contents_taken"));
        _ = RoundTrip(inventory);
    }

    [Theory]
    [InlineData("house-bravo", null)]
    [InlineData(null, "delivery-bravo")]
    public void SpoiledWithdrawalPreservesTheRequestedPhysicalDestinationAcrossReload(string? storage, string? delivery)
    {
        var inventory = Spoil(FilledPot());
        var pot = inventory.GetLot("pot");
        inventory = InventoryFixture.TakeFromContainer(inventory, "take", "alpha", "bravo", "pot", "berries", 8,
            destinationStorageBuildingId: storage, destinationDeliveryBuildingId: delivery);
        var taken = inventory.GetLot("berries");
        Assert.Equal(8, taken.Quantity);
        Assert.Equal("bravo", taken.OwnerId);
        Assert.Equal(0, taken.FreshnessBasisPoints);
        Assert.Equal(storage, taken.StorageBuildingId);
        Assert.Equal(delivery, taken.DeliveryBuildingId);
        Assert.Null(taken.ContainerLotId);
        Assert.Null(taken.GroundPosition);
        Assert.Equal(pot, inventory.GetLot("pot"));
        _ = RoundTrip(inventory);
    }

    [Fact]
    public void AReservedSiblingStillBlocksSpoiledWithdrawalUntilItsActualReservationIsReleased()
    {
        var inventory = FilledPot(4);
        inventory = InventoryFixture.AddLot(inventory, "sibling", "berries", "alpha", 4,
            storageBuildingId: "house-alpha");
        inventory = InventoryFixture.PutIntoContainer(inventory, "put-sibling", "alpha", "pot", "sibling", 4);
        inventory = InventoryFixture.Reserve(inventory, "sibling-input", "alpha", "sibling", 1, "recipe", 20);
        inventory = Spoil(inventory);
        var before = InventoryCheckpointCodec.Encode(inventory);

        var error = Assert.Throws<InvalidOperationException>(() => InventoryFixture.TakeFromContainer(
            inventory, "refused", "alpha", "bravo", "pot", "berries", 1));
        Assert.Equal("A vessel or its contents are reserved and cannot be moved.", error.Message);
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));

        inventory = InventoryFixture.ReleaseReservation(inventory, "sibling-input");
        inventory = InventoryFixture.TakeFromContainer(inventory, "allowed", "alpha", "bravo", "pot", "berries", 1);
        Assert.Equal(InventoryReservationState.Released, inventory.GetReservation("sibling-input").State);
        Assert.Equal(4, inventory.GetLot("sibling").Quantity);
        Assert.Equal("pot", inventory.GetLot("sibling").ContainerLotId);
        Assert.Equal(3, inventory.GetLot("berries").Quantity);
        Assert.Equal(1, inventory.GetLot("berries#taken:allowed").Quantity);
        Assert.Equal(8, inventory.Lots.Where(lot => lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        _ = RoundTrip(inventory);
    }

    [Fact]
    public void PhysicalWithdrawalDoesNotMakeSpoiledGoodsConsumableReservableOrTradable()
    {
        var inventory = Spoil(FilledPot());
        inventory = InventoryFixture.TakeFromContainer(inventory, "take", "alpha", "bravo", "pot", "berries", 8);
        inventory = InventoryFixture.AddLot(inventory, "wood", "wood", "alpha", 1);
        var before = InventoryCheckpointCodec.Encode(inventory);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Reserve(
            inventory, "eat", "bravo", "berries", 1, "food", 20));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.CreateDirectBarterOffer(inventory,
            new DirectBarterProposal("barter", 1, "bravo", "alpha", "berries", 1, "wood", 1, 20)));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));

        var reserved = InventoryFixture.Reserve(FilledPot(), "food-input", "alpha", "berries", 1, "food", 20);
        reserved = Spoil(reserved);
        var beforeConsumption = InventoryCheckpointCodec.Encode(reserved);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.ConsumeReservation(reserved, "food-input"));
        Assert.Equal(beforeConsumption, InventoryCheckpointCodec.Encode(reserved));
        Assert.Equal(InventoryReservationState.Reserved, reserved.GetReservation("food-input").State);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("wrong-vessel")]
    [InlineData("zero-quantity")]
    [InlineData("excess-quantity")]
    [InlineData("two-destinations")]
    public void InvalidPhysicalWithdrawalLeavesAllSpoiledStockAndThePotUnchanged(string refusal)
    {
        var inventory = Spoil(FilledPot());
        var before = InventoryCheckpointCodec.Encode(inventory);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.TakeFromContainer(
            inventory, "refused", refusal == "wrong-owner" ? "bravo" : "alpha", "bravo",
            refusal == "wrong-vessel" ? "other-pot" : "pot", "berries",
            refusal == "zero-quantity" ? 0 : refusal == "excess-quantity" ? 9 : 1,
            destinationStorageBuildingId: refusal == "two-destinations" ? "house-bravo" : null,
            destinationDeliveryBuildingId: refusal == "two-destinations" ? "delivery-bravo" : null));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));
        _ = RoundTrip(inventory);
    }

    private static InventoryCheckpoint FilledPot(int quantity = 8)
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new InventoryLot("pot", InventoryContainerRules.StoragePot, "alpha", 1, 10_000, 10_000, 0,
                StorageBuildingId: "house-alpha"),
            new InventoryLot("other-pot", InventoryContainerRules.StoragePot, "alpha", 1, 10_000, 10_000, 0,
                StorageBuildingId: "house-alpha"),
            new InventoryLot("berries", "berries", "alpha", quantity, 10_000, 10_000, 0,
                StorageBuildingId: "house-alpha"),
        ]);
        return InventoryFixture.PutIntoContainer(inventory, "fill", "alpha", "pot", "berries", quantity);
    }

    private static InventoryCheckpoint Spoil(InventoryCheckpoint inventory) =>
        RoundTrip(InventoryFixture.ProcessSpoilage(inventory, 8, 2500, SpoilingFoodKinds));

    private static InventoryCheckpoint RoundTrip(InventoryCheckpoint inventory)
    {
        var bytes = InventoryCheckpointCodec.Encode(inventory);
        var restored = InventoryCheckpointCodec.Decode(bytes);
        Assert.Equal(bytes, InventoryCheckpointCodec.Encode(restored));
        return restored;
    }
}
