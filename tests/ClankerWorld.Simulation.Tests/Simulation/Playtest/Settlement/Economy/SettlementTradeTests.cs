using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementTradeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecipientIndependentlyAcceptsOrDeclinesAcrossPauseAndRestart(bool decline)
    {
        using var seed = new PrivateWorldRuntime("settlement-trade-test");
        var state = seed.ExportState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        state = WithTradeGoods(state, first, second);
        IDecisionProvider Provider(string id) => new TradeProvider(id == first ? "trade_propose:" : id == second
            ? decline ? "trade_decline:" : "trade_accept:" : "safe_idle");
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        await world.AdvanceOneTickAsync();
        var offered = Assert.Single(world.ExportState().Society.Society.Inventory.Offers);
        Assert.Equal(DirectBarterState.Open, offered.State);
        Assert.Equal([first], offered.AcceptedBy);
        Assert.Contains(new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == second).SocialNotes,
            note => note.Contains("undecided", StringComparison.Ordinal));
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        restored.Resume();
        for (var tick = 0; tick < 120 && restored.ExportState().Society.Society.Inventory.GetOffer(offered.Id).State == DirectBarterState.Open; tick++)
        {
            await restored.AdvanceOneTickAsync();
        }
        var final = restored.ExportState();
        var inventory = final.Society.Society.Inventory;
        Assert.Equal(decline ? DirectBarterState.Cancelled : DirectBarterState.Settled, inventory.GetOffer(offered.Id).State);
        Assert.All(inventory.Reservations, reservation => Assert.Equal(decline ? InventoryReservationState.Released : InventoryReservationState.Completed, reservation.State));
        if (decline)
        {
            Assert.DoesNotContain(inventory.Lots, lot => lot.OwnerId == second && lot.ItemKind == "food");
            Assert.Empty(final.Society.Society.Memories);
            Assert.All(final.Inhabitants, person => Assert.True(person.SocialStanding is null or []));
            Assert.Contains(final.Events, item => item.Kind == "settlement_trade_declined");
        }
        else
        {
            Assert.Contains(inventory.Lots, lot => lot.OwnerId == second && lot.ItemKind == "food" && lot.Quantity == 1);
            Assert.Contains(inventory.Lots, lot => lot.OwnerId == first && lot.ItemKind == "clothing" && lot.Quantity == 1);
            Assert.Equal(2, final.Society.Society.Memories.Count);
            Assert.Equal(1, final.Inhabitants.Single(person => person.InhabitantId == first).SocialStanding!
                .Single(item => item.SubjectId == second).Trust);
            Assert.Equal(1, final.Inhabitants.Single(person => person.InhabitantId == second).SocialStanding!
                .Single(item => item.SubjectId == first).Trust);
            Assert.Contains(final.Events, item => item.Kind == "settlement_trade_completed");
        }
    }

    [Fact]
    public async Task TradeJournalReportsOutcomeWithoutCopyingFreeTextNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-trade-log-");
        try
        {
            using var seed = new PrivateWorldRuntime("trade-journal");
            var state = seed.ExportState();
            var first = state.Inhabitants[0].InhabitantId;
            var second = state.Inhabitants[1].InhabitantId;
            state = WithTradeGoods(state, first, second);
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == second
                            ? person with { Name = "free-text-secret-marker" } : person).ToArray(),
                    },
                },
            };
            using var world = PrivateWorldRuntime.Restore(state, id => new TradeProvider(id == first ? "trade_propose:" : "safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("test-owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_trade tick=1 event=settlement_trade_offered", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("free-text-secret-marker", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ProposerCanWithdrawBeforeTheRecipientAccepts()
    {
        using var seed = new PrivateWorldRuntime("withdraw-settlement-trade");
        var state = seed.ExportState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        var withdraw = false;
        using var world = PrivateWorldRuntime.Restore(WithTradeGoods(state, first, second),
            id => new TradeProvider(id == first ? withdraw ? "trade_decline:" : "trade_propose:" : "safe_idle"));
        await world.AdvanceOneTickAsync();
        withdraw = true;
        await world.AdvanceOneTickAsync();
        var inventory = world.ExportState().Society.Society.Inventory;
        Assert.Equal(DirectBarterState.Cancelled, Assert.Single(inventory.Offers).State);
        Assert.All(inventory.Reservations, reservation => Assert.Equal(InventoryReservationState.Released, reservation.State));
        Assert.Equal(4, inventory.GetLot("trade-food").Quantity);
    }

    [Fact]
    public async Task IgnoredOfferExpiresWithoutRepeatedProposalsOrLostItems()
    {
        using var seed = new PrivateWorldRuntime("ignored-settlement-trade");
        var state = seed.ExportState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(WithTradeGoods(state, first, second),
            id => new TradeProvider(id == first ? "trade_propose:" : "safe_idle"));
        for (var tick = 0; tick < 125; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var inventory = world.ExportState().Society.Society.Inventory;
        Assert.Equal(DirectBarterState.Cancelled, Assert.Single(inventory.Offers).State);
        Assert.All(inventory.Reservations, item => Assert.Equal(InventoryReservationState.Released, item.State));
        Assert.Equal(4, inventory.GetLot("trade-food").Quantity);
        Assert.Equal(2, inventory.GetLot("trade-clothes").Quantity);
    }

    [Theory]
    [InlineData("spoiled")]
    [InlineData("reservation_released")]
    public async Task UnusableOfferCancelsAndReleasesBothSidesInsteadOfBlockingTicks(string fault)
    {
        using var seed = new PrivateWorldRuntime("spoiled-settlement-trade");
        var state = seed.ExportState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(WithTradeGoods(state, first, second),
            id => new TradeProvider(id == first ? "trade_propose:" : "safe_idle"));
        await world.AdvanceOneTickAsync();
        state = world.ExportState();
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id != "trade-food" ? lot : fault switch
                        {
                            "spoiled" => lot with { FreshnessBasisPoints = 0 },
                            "ownership_changed" => lot with { OwnerId = "household:camp-alpha" },
                            _ => lot,
                        }).ToArray(),
                        Reservations = state.Society.Society.Inventory.Reservations.Select(reservation =>
                            fault == "reservation_released" && reservation.LotId == "trade-food"
                                ? reservation with { State = InventoryReservationState.Released } : reservation).ToArray(),
                    },
                },
            },
        };
        using var restored = PrivateWorldRuntime.Restore(state, _ => new TradeProvider("trade_accept:"));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Cancelled, Assert.Single(restored.ExportState().Society.Society.Inventory.Offers).State);
        Assert.All(restored.ExportState().Society.Society.Inventory.Reservations,
            reservation => Assert.Equal(InventoryReservationState.Released, reservation.State));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
    }

    private static PrivateWorldRuntimeState WithTradeGoods(PrivateWorldRuntimeState state, string first, string second) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 7_500 }).ToArray(),
        Society = state.Society with
        {
            Society = state.Society.Society with
            {
                Inventory = InventoryFixture.AddLot(
                    InventoryFixture.AddLot(state.Society.Society.Inventory, "trade-food", "food", first, 4),
                    "trade-clothes", "clothing", second, 2),
            },
        },
    };

    private sealed class TradeProvider(string prefix) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}
