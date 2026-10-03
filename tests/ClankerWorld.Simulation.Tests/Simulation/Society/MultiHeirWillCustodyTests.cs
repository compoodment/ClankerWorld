using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class MultiHeirWillCustodyTests
{
    private const string EstateId = "estate:alice:0";
    private static readonly string[] FounderIds = ["alice", "bob", "cara", "drew"];

    [Theory]
    [InlineData("ground")]
    [InlineData("house")]
    [InlineData("custodian")]
    public void PersonalSharesPreserveTheirActualPhysicalLocation(string location)
    {
        var checkpoint = Genesis();
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "wood", "wood", "alice", 5);
        inventory = Locate(inventory, "wood", 5, location);
        checkpoint = checkpoint with { Inventory = inventory };
        var original = checkpoint.Inventory.GetLot("wood");

        var held = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Accident).Checkpoint;

        Assert.Equal(original with { OwnerId = EstateId }, held.Inventory.GetLot("wood"));
        Assert.Equal(original.StorageBuildingId, Assert.Single(held.GetEstate(EstateId).FrozenLots!).StorageBuildingId);
        var settled = Settle(Accept(held, ["bob", "cara"]));
        var shares = settled.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "wood").ToArray();
        Assert.Equal(5, shares.Sum(lot => lot.Quantity));
        Assert.Equal(["bob", "cara"], shares.Select(lot => lot.OwnerId));
        Assert.All(shares, lot =>
        {
            Assert.Equal(original.StorageBuildingId, lot.StorageBuildingId);
            Assert.Equal(original.GroundPosition, lot.GroundPosition);
            Assert.Equal(original.CarrierId, lot.CarrierId);
            Assert.Equal(original.ConditionBasisPoints, lot.ConditionBasisPoints);
            Assert.Equal(original.FreshnessBasisPoints, lot.FreshnessBasisPoints);
        });
        AssertRoundtrip(settled);
    }

    [Theory]
    [InlineData("ground")]
    [InlineData("house")]
    [InlineData("custodian")]
    public void FilledVesselKeepsItsFamilyAndPhysicalLocationThroughEscrowAndInheritance(string location)
    {
        var checkpoint = Genesis();
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "pot", InventoryContainerRules.StoragePot, "alice", 1);
        inventory = InventoryFixture.AddLot(inventory, "berries", "berries", "alice", 3, containerLotId: "pot");
        inventory = Locate(inventory, "pot", 1, location);
        checkpoint = checkpoint with { Inventory = inventory };
        var original = checkpoint.Inventory.Lots.ToArray();

        var held = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Accident).Checkpoint;
        Assert.Equal(original.Select(lot => lot with { OwnerId = EstateId }), held.Inventory.Lots);
        var settled = Settle(Accept(held, ["bob", "cara"]));

        Assert.Equal(original.Select(lot => lot with { OwnerId = "bob" }), settled.Inventory.Lots);
        Assert.Equal("pot", settled.Inventory.GetLot("berries").ContainerLotId);
        AssertRoundtrip(settled);
    }

    [Fact]
    public void CommunalFallbackPreservesActualHouseStorageAndFrozenOrigin()
    {
        var checkpoint = Genesis(createHousehold: false);
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "wood", "wood", "alice", 4, storageBuildingId: "old-house");
        inventory = InventoryFixture.AddLot(inventory, "pot", InventoryContainerRules.StoragePot, "alice", 1, storageBuildingId: "old-house");
        inventory = InventoryFixture.AddLot(inventory, "berries", "berries", "alice", 2,
            storageBuildingId: "old-house", containerLotId: "pot");
        checkpoint = checkpoint with { Inventory = inventory };
        var original = checkpoint.Inventory.Lots.ToArray();

        var held = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Accident).Checkpoint;
        var settled = Settle(held);

        Assert.Equal(original.Select(lot => lot with { OwnerId = "settlement:communal" }), settled.Inventory.Lots);
        var estate = settled.GetEstate(EstateId);
        Assert.True(estate.Settled);
        Assert.All(estate.FrozenLots!, lot => Assert.Equal("old-house", lot.StorageBuildingId));
        AssertRoundtrip(settled);
    }

    [Fact]
    public void EscrowRetainsLivingForeignCustodyAndClearsOnlyTheDeceasedOwnerCustody()
    {
        var checkpoint = Genesis();
        var lots = new[]
        {
            new InventoryLot("own-carried", "wood", "alice", 1, 10_000, 10_000, 0, CarrierId: "alice"),
            new InventoryLot("foreign-held", "stone", "alice", 1, 10_000, 10_000, 0, CarrierId: "drew"),
            new InventoryLot("borrowed", "fiber", "bob", 1, 10_000, 10_000, 0, CarrierId: "alice"),
        };
        checkpoint = checkpoint with { Inventory = InventoryFixture.CreateGenesis(lots) };

        var held = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Accident).Checkpoint;

        Assert.Null(held.Inventory.GetLot("own-carried").CarrierId);
        Assert.Equal(EstateId, held.Inventory.GetLot("own-carried").OwnerId);
        Assert.Equal("drew", held.Inventory.GetLot("foreign-held").CarrierId);
        Assert.Equal("alice", held.Inventory.GetLot("borrowed").CarrierId);
        var settled = Settle(Accept(held, ["drew"]));
        var inherited = Assert.Single(settled.Inventory.Lots, lot => lot.ProvenanceLotId == "foreign-held");
        Assert.Equal("drew", inherited.OwnerId);
        // The owner already physically holds this item: no separate custodian is needed.
        Assert.Null(inherited.CarrierId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("quantity")]
    [InlineData("kind")]
    [InlineData("extra")]
    [InlineData("storage")]
    public void AcceptedUnsettledWillCannotRestoreDifferentPhysicalStock(string alteration)
    {
        var held = StoredAcceptedWill();
        var original = held.Inventory.GetLot("wood");
        var lots = alteration switch
        {
            "missing" => [],
            "quantity" => new[] { original with { Quantity = 3 } },
            "kind" => new[] { original with { ItemKind = "stone" } },
            "extra" => new[] { original with { Id = "extra", Quantity = 1 }, original },
            "storage" => new[] { original with { StorageBuildingId = "other-house" } },
            _ => throw new ArgumentOutOfRangeException(nameof(alteration)),
        };
        var forged = held with { Inventory = held.Inventory with { Lots = lots } };

        Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(forged));
        Assert.Throws<InvalidDataException>(() => SocietyCheckpointCodec.Encode(forged));
    }

    [Fact]
    public void EqualWillCannotRestoreAnAllocationThatConservesQuantityButChangesTheDivision()
    {
        var held = StoredAcceptedWill();
        Assert.Equal([new SocietyWillBequest("wood", "bob", 1), new SocietyWillBequest("wood", "cara", 1)],
            held.GetEstate(EstateId).WillBequests);
        var forged = held with
        {
            Estates = held.Estates.Select(estate => estate.Id == EstateId ? estate with
            {
                WillBequests = [new SocietyWillBequest("wood", "bob", 2)],
            } : estate).ToArray(),
        };

        Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(forged));
        Assert.Throws<InvalidDataException>(() => SocietyCheckpointCodec.Encode(forged));
    }

    [Fact]
    public void AcceptedEqualDivisionSurvivesAnHeirsLaterDeathAndUsesTheLivingHouseholdFallback()
    {
        var held = StoredAcceptedWill();
        held = SocietyFixture.Kill(held, "bob", SocietyDeathCause.Accident).Checkpoint;
        AssertRoundtrip(held);

        var settled = Settle(held);

        var inherited = Assert.Single(settled.Inventory.Lots, lot => lot.ProvenanceLotId == "wood");
        Assert.Equal(("cara", 2, "old-house"), (inherited.OwnerId, inherited.Quantity, inherited.StorageBuildingId));
        AssertRoundtrip(settled);
    }

    [Fact]
    public void OnlyTheActualVesselRecipientHearsFinalWordsWhenOtherNamedHeirsReceiveNothing()
    {
        var checkpoint = Genesis();
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "pot", InventoryContainerRules.StoragePot, "alice", 1);
        inventory = InventoryFixture.AddLot(inventory, "berries", "berries", "alice", 2, containerLotId: "pot");
        checkpoint = checkpoint with { Inventory = inventory };
        var held = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Accident).Checkpoint;
        var accepted = Accept(held, ["bob", "cara", "drew"], "Share the harvest.");

        var settled = Settle(accepted);

        Assert.All(settled.Inventory.Lots, lot => Assert.Equal("bob", lot.OwnerId));
        var memory = Assert.Single(settled.Memories, item => item.Id.StartsWith("final-words:", StringComparison.Ordinal));
        Assert.Equal(("bob", "alice", "private", "Alice's final words were: 'Share the harvest.'"),
            (memory.OwnerId, memory.SubjectId, memory.Visibility, memory.Summary));
        AssertRoundtrip(settled);
    }

    private static SocietyCheckpoint StoredAcceptedWill()
    {
        var checkpoint = Genesis();
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "wood", "wood", "alice", 2,
                storageBuildingId: "old-house"),
        };
        return Accept(SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Accident).Checkpoint, ["bob", "cara"]);
    }

    private static SocietyCheckpoint Genesis(bool createHousehold = true)
    {
        var config = new SocietyConfig(TicksPerWorldDay: 2, DaysPerWorldYear: 2, BaseNaturalMortalityBasisPoints: 0);
        var checkpoint = SocietyFixture.CreateGenesis("will-custody",
            FounderIds
                .Select(id => SocietyFixture.CreateFounder(id, char.ToUpperInvariant(id[0]) + id[1..], config: config)),
            config: config);
        return createHousehold
            ? SocietyFixture.CreateHousehold(checkpoint, "home", "Home", ["alice", "bob", "cara"]).Checkpoint
            : checkpoint;
    }

    private static InventoryCheckpoint Locate(InventoryCheckpoint inventory, string lotId, int quantity, string location) =>
        InventoryFixture.Relocate(inventory, "initial-location", lotId, "alice", quantity,
            carrierId: location == "custodian" ? "drew" : null,
            storageBuildingId: location == "house" ? "old-house" : null,
            groundPosition: location == "ground" ? new InventoryGroundPosition(4, 7) : null);

    private static SocietyCheckpoint Accept(SocietyCheckpoint held, IReadOnlyList<string> heirs, string? words = null)
    {
        held = SocietyFixture.MarkWillStarted(held, EstateId).Checkpoint;
        var accepted = SocietyFixture.ResolveWill(held, EstateId,
            new SocietyWillDirective(heirs, CognitionWillContext.EqualSplit), "accepted", words).Checkpoint;
        Assert.Equal("accepted", accepted.GetEstate(EstateId).WillStatus);
        return accepted;
    }

    private static SocietyCheckpoint Settle(SocietyCheckpoint checkpoint) =>
        SocietyFixture.AdvanceTo(checkpoint, checkpoint.GetEstate(EstateId).ExpiryTick).Checkpoint;

    private static void AssertRoundtrip(SocietyCheckpoint checkpoint)
    {
        var encoded = SocietyCheckpointCodec.Encode(checkpoint);
        Assert.Equal(encoded, SocietyCheckpointCodec.Encode(SocietyCheckpointCodec.Decode(encoded)));
    }
}
