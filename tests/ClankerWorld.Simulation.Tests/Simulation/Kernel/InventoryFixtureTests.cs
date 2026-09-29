using System.Text;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class InventoryFixtureTests
{
    [Theory]
    [InlineData("alpha")]
    public void EitherPartyCanDeclineWithoutTransferringOrRetainingReservations(string party)
    {
        var offered = InventoryFixture.CreateDirectBarterOffer(Genesis(),
            new DirectBarterProposal("decline", 1, "alpha", "bravo", "alpha-wood", 1, "bravo-food", 1, 20));
        offered = InventoryFixture.AcceptDirectBarterOffer(offered, "decline", 1, "alpha");
        var bytes = InventoryCheckpointCodec.Encode(offered);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.CancelDirectBarterOffer(offered, "decline", 2, party));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.CancelDirectBarterOffer(offered, "decline", 1, "outsider"));
        Assert.Equal(bytes, InventoryCheckpointCodec.Encode(offered));
        var cancelled = InventoryFixture.CancelDirectBarterOffer(offered, "decline", 1, party);
        Assert.Equal(DirectBarterState.Cancelled, cancelled.GetOffer("decline").State);
        Assert.All(cancelled.Reservations, item => Assert.Equal(InventoryReservationState.Released, item.State));
        Assert.Equal(offered.Lots, cancelled.Lots);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.AcceptDirectBarterOffer(cancelled, "decline", 1, "bravo"));
    }

    [Theory]
    [InlineData(false)]
    public void ExpiredBarterCannotAcquireEitherPartysAcceptance(bool firstAlreadyAccepted)
    {
        var offered = InventoryFixture.CreateDirectBarterOffer(Genesis(),
            new DirectBarterProposal("expiry", 1, "alpha", "bravo", "alpha-wood", 1, "bravo-food", 1, 2));
        if (firstAlreadyAccepted)
        {
            offered = InventoryFixture.AcceptDirectBarterOffer(offered, "expiry", 1, "alpha");
        }
        var expired = offered with { WorldTick = 3 };
        var before = InventoryCheckpointCodec.Encode(expired);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.AcceptDirectBarterOffer(expired,
            "expiry", 1, firstAlreadyAccepted ? "bravo" : "alpha"));
        Assert.Equal(before, InventoryCheckpointCodec.Encode(expired));
        var released = InventoryFixture.ReleaseExpiredReservations(expired, 3);
        Assert.Equal(DirectBarterState.Cancelled, released.GetOffer("expiry").State);
        Assert.All(released.Reservations, item => Assert.Equal(InventoryReservationState.Released, item.State));
        Assert.Equal(InventoryCheckpointCodec.Encode(released), InventoryCheckpointCodec.Encode(InventoryFixture.ReleaseExpiredReservations(released, 3)));
    }

    [Fact]
    public void LotSplitSpoilageAndSaveRestoreRemainCanonicalAndDoNotDecayTwice()
    {
        var genesis = Genesis();
        var split = InventoryFixture.SplitLot(genesis, "alpha-berries", 2, "alpha-berries:split");
        var spoiled = InventoryFixture.ProcessSpoilage(split, 3, 100);
        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(spoiled));
        var processedAgain = InventoryFixture.ProcessSpoilage(restored, 3, 100);
        var fullySpoiled = InventoryFixture.ProcessSpoilage(processedAgain, 6, 100);

        Assert.Equal(2, split.GetLot("alpha-berries").Quantity);
        Assert.Equal("alpha-berries", split.GetLot("alpha-berries:split").ProvenanceLotId);
        Assert.All(spoiled.Lots.Where(lot => lot.ItemKind == "berries"), lot => Assert.Equal(300, lot.FreshnessBasisPoints));
        Assert.Equal(InventoryDigest.State(restored), InventoryDigest.State(processedAgain));
        Assert.Equal(InventoryDigest.Events(restored.Events), InventoryDigest.Events(processedAgain.Events));
        Assert.All(fullySpoiled.Lots.Where(lot => lot.ItemKind == "berries"), lot => Assert.Equal(0, lot.FreshnessBasisPoints));
        Assert.Equal(InventoryDigest.State(spoiled), InventoryDigest.State(restored));
    }

    [Fact]
    public void ReservationExpiryReleasesOnlyTheReservedQuantity()
    {
        var reserved = InventoryFixture.Reserve(Genesis(), "reserve-1", "alpha", "alpha-berries", 1, "meal", 2);
        var atExpiry = InventoryFixture.ReleaseExpiredReservations(reserved, 2);
        Assert.Equal(InventoryReservationState.Reserved, atExpiry.GetReservation("reserve-1").State);
        Assert.Equal(InventoryReservationState.Completed, InventoryFixture.ConsumeReservation(atExpiry, "reserve-1").GetReservation("reserve-1").State);
        var released = InventoryFixture.ReleaseExpiredReservations(reserved, 3);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.ConsumeReservation(reserved with { WorldTick = 3 }, "reserve-1"));

        Assert.Equal(InventoryReservationState.Reserved, reserved.GetReservation("reserve-1").State);
        Assert.Equal(InventoryReservationState.Released, released.GetReservation("reserve-1").State);
        Assert.Contains(released.Events, item => item is { Kind: "reservation_released", Detail: "reserve-1" });
    }

    [Fact]
    public void ExactRevisionAcceptanceSettlesAtomicallyAndRejectsStaleAcceptanceWithoutMutation()
    {
        var proposal = new DirectBarterProposal("offer-1", 3, "alpha", "bravo", "alpha-wood", 2, "bravo-food", 1, 10);
        var offered = InventoryFixture.CreateDirectBarterOffer(Genesis(), proposal);
        var firstAccepted = InventoryFixture.AcceptDirectBarterOffer(offered, "offer-1", 3, "alpha");
        var beforeStaleState = InventoryDigest.State(firstAccepted);
        var beforeStaleEvents = InventoryDigest.Events(firstAccepted.Events);

        Assert.Throws<InvalidOperationException>(() => InventoryFixture.AcceptDirectBarterOffer(firstAccepted, "offer-1", 2, "bravo"));
        Assert.Equal(beforeStaleState, InventoryDigest.State(firstAccepted));
        Assert.Equal(beforeStaleEvents, InventoryDigest.Events(firstAccepted.Events));

        var settled = InventoryFixture.AcceptDirectBarterOffer(firstAccepted, "offer-1", 3, "bravo");
        Assert.Equal(DirectBarterState.Settled, settled.GetOffer("offer-1").State);
        Assert.Equal(InventoryReservationState.Completed, settled.GetReservation("offer-1:first").State);
        Assert.Equal(InventoryReservationState.Completed, settled.GetReservation("offer-1:second").State);
        Assert.Equal("bravo", settled.GetLot("alpha-wood").OwnerId);
        Assert.Equal("alpha", settled.GetLot("bravo-food").OwnerId);
        Assert.Contains(settled.Events, item => item is { Kind: "barter_settled", Detail: "offer-1:r3" });
    }

    [Fact]
    public void CheckpointCodecRoundTripsDelimiterBearingAndUnicodeValues()
    {
        var checkpoint = new InventoryCheckpoint(
            7,
            [new InventoryLot("lot|1", "berries,🍓\nkind", "owner|1", 2, 10_000, 9_000, 7, "source|lot")],
            [new InventoryReservation("reserve|1", "owner|1", "lot|1", 1, "meal\nwith|separators", 12, true, InventoryReservationState.Reserved)],
            [new DirectBarterOffer("offer|1", 2, "owner|1", "other,owner", "lot|1", 1, "other-lot", 1, 20, DirectBarterState.Open, ["owner|1", "other\nowner"])],
            [new InventoryEvent(1, 7, "event|kind", "detail,with\nseparators")]);

        var restored = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(checkpoint));

        Assert.Equal(checkpoint.WorldTick, restored.WorldTick);
        Assert.Equal(checkpoint.Lots, restored.Lots);
        Assert.Equal(checkpoint.Reservations, restored.Reservations);
        var expectedOffer = checkpoint.Offers.Single();
        var actualOffer = restored.Offers.Single();
        Assert.Equal(expectedOffer.Id, actualOffer.Id);
        Assert.Equal(expectedOffer.Revision, actualOffer.Revision);
        Assert.Equal(expectedOffer.FirstPartyId, actualOffer.FirstPartyId);
        Assert.Equal(expectedOffer.SecondPartyId, actualOffer.SecondPartyId);
        Assert.Equal(expectedOffer.FirstLotId, actualOffer.FirstLotId);
        Assert.Equal(expectedOffer.FirstQuantity, actualOffer.FirstQuantity);
        Assert.Equal(expectedOffer.SecondLotId, actualOffer.SecondLotId);
        Assert.Equal(expectedOffer.SecondQuantity, actualOffer.SecondQuantity);
        Assert.Equal(expectedOffer.ExpiryTick, actualOffer.ExpiryTick);
        Assert.Equal(expectedOffer.State, actualOffer.State);
        Assert.Equal(
            expectedOffer.AcceptedBy.OrderBy(id => id, StringComparer.Ordinal),
            actualOffer.AcceptedBy);
        Assert.Equal(checkpoint.Events, restored.Events);
        Assert.Equal(InventoryDigest.State(checkpoint), InventoryDigest.State(restored));
    }

    [Fact]
    public void CheckpointCodecStillReadsLegacyV1Saves()
    {
        var legacy = string.Join(
            '\n',
            "clankerworld.inventory-fixture/v1",
            "tick=3",
            "lot=food-lot|food|alice|2|10000|9000|3|-",
            "event=1|3|created|food-lot",
            string.Empty);

        var restored = InventoryCheckpointCodec.Decode(Encoding.UTF8.GetBytes(legacy));

        Assert.Equal(3, restored.WorldTick);
        Assert.Equal("food-lot", restored.Lots.Single().Id);
        Assert.Equal("food-lot", restored.Events.Single().Detail);
    }

    private static InventoryCheckpoint Genesis() => InventoryFixture.CreateGenesis(
    [
        new InventoryLot("alpha-berries", "berries", "alpha", 4, 10_000, 600, 0),
        new InventoryLot("alpha-wood", "wood", "alpha", 2, 10_000, 10_000, 0),
        new InventoryLot("bravo-food", "food", "bravo", 1, 10_000, 10_000, 0),
    ]);
}
