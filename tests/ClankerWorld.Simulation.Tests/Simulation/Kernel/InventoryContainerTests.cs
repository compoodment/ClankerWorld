using System.Text;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class InventoryContainerTests
{
    [Fact]
    public void PotStoresFoodAndSlowsSpoilageAcrossSmallTickRates()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new InventoryLot("pot", InventoryContainerRules.StoragePot, "alpha", 1, 10_000, 10_000, 0),
            new InventoryLot("loose", "berries", "alpha", 2, 10_000, 10_000, 0),
        ]);
        inventory = InventoryFixture.PutIntoContainer(inventory, "put", "alpha", "pot", "loose", 1);

        var firstTick = InventoryFixture.ProcessSpoilage(inventory, 1, 1);
        var secondTick = InventoryFixture.ProcessSpoilage(firstTick, 2, 1);

        Assert.Equal(9_999, firstTick.GetLot("loose").FreshnessBasisPoints);
        Assert.Equal(10_000, firstTick.Lots.Single(lot => lot.ContainerLotId == "pot").FreshnessBasisPoints);
        Assert.Equal(9_998, secondTick.GetLot("loose").FreshnessBasisPoints);
        Assert.Equal(9_999, secondTick.Lots.Single(lot => lot.ContainerLotId == "pot").FreshnessBasisPoints);
        Assert.Equal(1, secondTick.GetLot("pot").Quantity);
    }

    [Fact]
    public void ContainerTransferMovesItsContentsTogetherAndRejectsLotOnlyMovement()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new InventoryLot("pot", InventoryContainerRules.StoragePot, "alpha", 1, 10_000, 10_000, 0),
            new InventoryLot("berries", "berries", "alpha", 5, 10_000, 10_000, 0),
        ]);
        inventory = InventoryFixture.PutIntoContainer(inventory, "put", "alpha", "pot", "berries", 3);
        var content = inventory.Lots.Single(lot => lot.ContainerLotId == "pot");
        var beforeRejectedMove = InventoryCheckpointCodec.Encode(inventory);

        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(
            inventory, "peel", "alpha", "bravo", content.Id, 1, "supply"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(
            inventory, "split-vessel", "alpha", "bravo", "pot", 2, "supply"));
        Assert.Equal(beforeRejectedMove, InventoryCheckpointCodec.Encode(inventory));

        var moved = InventoryFixture.Transfer(inventory, "move-pot", "alpha", "bravo", "pot", 1, "carry");

        Assert.Equal("bravo", moved.GetLot("pot").OwnerId);
        Assert.Equal("bravo", moved.GetLot(content.Id).OwnerId);
        Assert.Equal("pot", moved.GetLot(content.Id).ContainerLotId);
        Assert.Equal(5, moved.Lots.Where(lot => lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        Assert.Equal(InventoryDigest.State(moved), InventoryDigest.State(
            InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(moved))));
    }

    [Fact]
    public void ReservedJugContentsBlockVesselMovementAndCanBeConsumedWithoutLosingTheJug()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [new InventoryLot("jug", InventoryContainerRules.WaterJug, "alpha", 1, 10_000, 10_000, 0)]);
        inventory = InventoryFixture.AddLot(inventory, "water", InventoryContainerRules.FreshWater, "alpha", 4,
            containerLotId: "jug");
        inventory = InventoryFixture.Reserve(inventory, "water-input", "alpha", "water", 2, "recipe", 10);
        var beforeRejectedMove = InventoryCheckpointCodec.Encode(inventory);

        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(
            inventory, "move-reserved-jug", "alpha", "bravo", "jug", 1, "carry"));
        Assert.Equal(beforeRejectedMove, InventoryCheckpointCodec.Encode(inventory));

        inventory = InventoryFixture.ConsumeReservation(inventory, "water-input");
        Assert.Equal(1, inventory.GetLot("jug").Quantity);
        Assert.Equal(2, inventory.GetLot("water").Quantity);
        inventory = InventoryFixture.Reserve(inventory, "last-water", "alpha", "water", 2, "recipe", 10);
        inventory = InventoryFixture.ConsumeReservation(inventory, "last-water");

        Assert.Equal(1, inventory.GetLot("jug").Quantity);
        Assert.DoesNotContain(inventory.Lots, lot => lot.ItemKind == InventoryContainerRules.FreshWater);
        inventory = InventoryFixture.AddLot(inventory, "refill", InventoryContainerRules.FreshWater, "alpha", 4,
            containerLotId: "jug");
        Assert.Equal(4, inventory.GetLot("refill").Quantity);
    }

    [Fact]
    public void InvalidContainerRelationshipsAreRejectedByCheckpointAndSaveBoundaries()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new InventoryLot("jug", InventoryContainerRules.WaterJug, "alpha", 1, 10_000, 10_000, 0),
            new InventoryLot("pot", InventoryContainerRules.StoragePot, "alpha", 1, 10_000, 10_000, 0),
        ]);
        Assert.Throws<InvalidDataException>(() => InventoryFixture.CreateGenesis(
            [new InventoryLot("loose-water", InventoryContainerRules.FreshWater, "alpha", 1, 10_000, 10_000, 0)]));
        Assert.Throws<InvalidDataException>(() => InventoryFixture.AddLot(inventory, "bad-water", "fresh_water", "alpha", 1,
            containerLotId: "pot"));

        inventory = InventoryFixture.AddLot(inventory, "water", InventoryContainerRules.FreshWater, "alpha", 1,
            containerLotId: "jug");
        var invalidOwner = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "water" ? lot with { OwnerId = "bravo" } : lot).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => InventoryCheckpointCodec.Encode(invalidOwner));

        var validBytes = Encoding.UTF8.GetString(InventoryCheckpointCodec.Encode(inventory));
        var orphanedBytes = validBytes.Replace("\"containerLotId\":\"jug\"", "\"containerLotId\":\"missing-jug\"", StringComparison.Ordinal);
        Assert.NotEqual(validBytes, orphanedBytes);
        Assert.Throws<InvalidDataException>(() => InventoryCheckpointCodec.Decode(Encoding.UTF8.GetBytes(orphanedBytes)));
    }

    [Fact]
    public void VesselCapacityAndOwnerChecksRejectContainerChangesAtomically()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new InventoryLot("pot", InventoryContainerRules.StoragePot, "alpha", 1, 10_000, 10_000, 0),
            new InventoryLot("too-much-food", "berries", "alpha", 9, 10_000, 10_000, 0),
            new InventoryLot("other-food", "berries", "bravo", 1, 10_000, 10_000, 0),
        ]);
        var before = InventoryCheckpointCodec.Encode(inventory);

        Assert.Throws<InvalidOperationException>(() => InventoryFixture.PutIntoContainer(
            inventory, "overfill", "alpha", "pot", "too-much-food", 9));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.PutIntoContainer(
            inventory, "wrong-owner", "bravo", "pot", "other-food", 1));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(inventory));
    }

    [Fact]
    public void BrokenVesselCannotSupplyOrAcceptContentsButCanStillMoveAsACompleteFamily()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [new InventoryLot("jug", InventoryContainerRules.WaterJug, "alpha", 1, 10_000, 10_000, 0)]);
        inventory = InventoryFixture.AddLot(inventory, "water", InventoryContainerRules.FreshWater, "alpha", 2,
            containerLotId: "jug");
        inventory = InventoryFixture.Reserve(inventory, "input", "alpha", "water", 1, "recipe", 10);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "jug" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        };
        var beforeRejectedOperations = InventoryCheckpointCodec.Encode(inventory);

        var released = InventoryFixture.ReleaseReservation(inventory, "input");
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Reserve(
            released, "new-input", "alpha", "water", 1, "recipe", 10));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.ConsumeReservation(inventory, "input"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.AddLot(
            inventory, "more-water", InventoryContainerRules.FreshWater, "alpha", 1, containerLotId: "jug"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.TakeFromContainer(
            released, "take", "alpha", "alpha", "jug", "water", 1));
        Assert.Equal(beforeRejectedOperations, InventoryCheckpointCodec.Encode(inventory));

        var moved = InventoryFixture.Transfer(released, "move-broken-jug", "alpha", "bravo", "jug", 1, "repair");
        Assert.Equal("bravo", moved.GetLot("jug").OwnerId);
        Assert.Equal(0, moved.GetLot("jug").ConditionBasisPoints);
        Assert.Equal("bravo", moved.GetLot("water").OwnerId);
        Assert.Equal(10_000, moved.GetLot("water").FreshnessBasisPoints);
        Assert.Equal(2, moved.GetLot("water").Quantity);
    }
}
