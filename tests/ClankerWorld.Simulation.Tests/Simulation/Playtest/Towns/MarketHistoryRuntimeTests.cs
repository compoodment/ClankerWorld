using System.Diagnostics;
using Xunit.Abstractions;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketHistoryRuntimeTests(ITestOutputHelper output)
{
    private const string Seller = "founder:00000000000000000000000000000001";
    private const string Stock = "history-personal-cloth";

    [Fact]
    public async Task RepeatedNativeBorrowingAndDepositsRetireClosedRecordsWithoutChangingGoods()
    {
        var state = await PaidMarketWorld.StateAsync();
        var market = PaidMarketWorld.Market(state);
        var stall = market.Stalls.Single(item => item.SlotIndex == 0);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state,
            InventoryFixture.AddLot(state.Society.Society.Inventory, Stock, "cloth", Seller, 4)),
            Seller, MarketContent.StallEntrance(market.Site, 0));
        using var history = new HistoryWorld(state, Seller, stall.BuildingId);
        var visits = new HashSet<string>(StringComparer.Ordinal);
        var receipts = new HashSet<string>(StringComparer.Ordinal);
        for (var cycle = 0; cycle < 40; cycle++)
        {
            history.Checkpoint(cycle is 31 or 32 or 39);
            state = await history.ChooseAsync("market_borrow:",
                world => PaidMarketWorld.Market(world).Occupancies.Any(item => item.EndedTick is null));
            visits.Add(Assert.Single(PaidMarketWorld.Market(state).Occupancies, item => item.EndedTick is null).Id);
            state = await history.ChooseAsync("market_deposit:",
                world => world.Society.Inventory.GetLot(Stock).GroundPosition is not null);
            receipts.Add(PaidMarketWorld.Market(state).StockReceipts[^1].Id);
            state = await history.ChooseAsync("market_collect:",
                world => world.Society.Inventory.GetLot(Stock).GroundPosition is null);
            state = await history.ChooseAsync("market_leave:",
                world => PaidMarketWorld.Market(world).Occupancies.All(item => item.EndedTick is not null));
            var goods = state.Society.Society.Inventory.GetLot(Stock);
            Assert.Equal((Seller, "cloth", 4, (InventoryGroundPosition?)null),
                (goods.OwnerId, goods.ItemKind, goods.Quantity, goods.GroundPosition));
        }
        output.WriteLine("40 native visits: {0} occupancies, {1} receipts, {2} ticks. Median native tick: first 32 = {3:F2} ms; last 32 = {4:F2} ms.",
            PaidMarketWorld.Market(state).Occupancies.Count, PaidMarketWorld.Market(state).StockReceipts.Count,
            history.TickMilliseconds.Count, Median(history.TickMilliseconds.Take(32)), Median(history.TickMilliseconds.TakeLast(32)));
        Assert.Equal(40, visits.Count);
        Assert.Equal(40, receipts.Count);
        Assert.Equal(32, PaidMarketWorld.Market(state).Occupancies.Count);
        Assert.Equal(32, PaidMarketWorld.Market(state).StockReceipts.Count);
        Assert.Empty(PaidMarketWorld.Market(state).Trades);
        Assert.Equal(market.Stalls, PaidMarketWorld.Market(state).Stalls);
        Assert.Equal(40, state.Events.Count(item => item.Kind == "market_stock_delivered"));
    }

    private static double Median(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static readonly Lazy<Task<byte[]>> ClosedTrades = new(BuildClosedTradeStateAsync,
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static async Task<PrivateWorldRuntimeState> ClosedTradeStateAsync() =>
        PrivateWorldRuntimeCodec.Decode(await ClosedTrades.Value);

    private static async Task<byte[]> BuildClosedTradeStateAsync()
    {
        const string buyer = "founder:00000000000000000000000000000003";
        const string axes = "history-personal-axes";
        var state = await PaidMarketWorld.StateAsync();
        var market = PaidMarketWorld.Market(state);
        var stall = market.Stalls.Single(item => item.SlotIndex == 0);
        var position = MarketContent.StallSite(market.Site, 0);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, axes, "wooden_axe", Seller, 4);
        inventory = InventoryFixture.AddLot(inventory, "history-protected-axe", "iron_axe", Seller, 1);
        inventory = InventoryFixture.AddLot(inventory, Stock, "cloth", Seller, 2);
        inventory = InventoryFixture.AddLot(inventory, "history-payment-wood", "wood", buyer, 4);
        inventory = InventoryFixture.AddLot(inventory, "history-payment-reserve", "wood", buyer, 16,
            storageBuildingId: PaidMarketWorld.HouseOf(state, PaidMarketWorld.HouseholdOf(state, buyer)).InstanceId);
        Assert.InRange(PersonalEquipmentRules.CarriedQuantity(inventory, buyer, state.Inhabitants.Single(person => person.InhabitantId == buyer).Equipment),
            0, PersonalEquipmentRules.SackCapacity);
        state = PaidMarketWorld.At(PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory),
            Seller, MarketContent.StallEntrance(market.Site, 0)), buyer, position);
        using var history = new HistoryWorld(state, Seller, stall.BuildingId);
        state = await history.ChooseAsync("market_borrow:",
            world => PaidMarketWorld.Market(world).Occupancies.Any(item => item.EndedTick is null));
        state = await history.ChooseAsync("market_deposit:",
            world => world.Society.Inventory.GetLot(axes).GroundPosition is not null, material: "wooden_axe");
        var sourceReceipt = Assert.Single(PaidMarketWorld.Market(state).StockReceipts).Id;
        var offers = new HashSet<string>(StringComparer.Ordinal);
        var household = PaidMarketWorld.HouseholdOf(state, Seller);
        var house = PaidMarketWorld.HouseOf(state, household).InstanceId;
        for (var cycle = 0; cycle < 40; cycle++)
        {
            history.Checkpoint(cycle is 31 or 32 or 39);
            state = await history.ChooseAsync("market_buy:",
                world => PaidMarketWorld.Market(world).Trades.Any(item => item.SettledTick is null && item.CancellationReason is null), buyer);
            var trade = Assert.Single(PaidMarketWorld.Market(state).Trades, item => item.SettledTick is null && item.CancellationReason is null);
            offers.Add(trade.OfferId);
            if (cycle % 2 == 0)
                state = await history.ChooseAsync("market_cancel:",
                    world => world.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Cancelled);
            else
            {
                state = await history.ChooseAsync("market_continue:",
                    world => world.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Settled);
                var acquired = Assert.Single(state.Society.Society.Inventory.Lots,
                    lot => lot.OwnerId == buyer && lot.ItemKind == "wooden_axe");
                Assert.Equal(1, acquired.Quantity);
                // Controlled stock replenishment: the buyer explicitly returns the purchased
                // axe through real inventory transitions, allowing another genuine Market offer.
                // No Market receipt, acceptance, occupancy, building or actor position is edited.
                history.ArrangeInventory(items => InventoryFixture.Transfer(
                    InventoryFixture.Relocate(items, "history-return-ground:" + cycle, acquired.Id, buyer, 1,
                        groundPosition: new(position.X, position.Y)), "history-return-owner:" + cycle,
                    buyer, Seller, acquired.Id, 1, "history_fixture_return", destinationGroundPosition: new(position.X, position.Y)));
                if (cycle % 8 == 7)
                {
                    state = await history.ChooseAsync("market_collect:",
                        world => !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == household && lot.ItemKind == "wood" &&
                            MarketTradeRules.IsAt(lot, position)), material: "wood", maxSteps: 24);
                    // The stress fixture stores collected household payments in their real
                    // House between batches; the unrelated hauling journey is not simulated.
                    history.ArrangeInventory(items =>
                    {
                        foreach (var lot in items.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood" && lot.CarrierId == Seller).ToArray())
                            items = InventoryFixture.Relocate(items, "history-store-payment:" + cycle + ":" + lot.Id,
                                lot.Id, household, lot.Quantity, storageBuildingId: house);
                        return items;
                    });
                    if (cycle < 39)
                        history.ArrangeInventory(items => InventoryFixture.Relocate(items, "history-payment-load:" + cycle,
                            "history-payment-reserve", buyer, 4, carrierId: buyer));
                }
            }
        }
        Assert.Equal(40, offers.Count);
        Assert.Equal(20, state.Events.Count(item => item.Kind == "market_trade_completed"));
        Assert.Equal(20, state.Events.Count(item => item.Kind == "market_trade_cancelled"));
        for (var cycle = 0; cycle < 33; cycle++)
        {
            history.Checkpoint(cycle is 31 or 32);
            state = await history.ChooseAsync("market_deposit:",
                world => world.Society.Inventory.GetLot(Stock).GroundPosition is not null, material: "cloth");
            state = await history.ChooseAsync("market_collect:",
                world => world.Society.Inventory.GetLot(Stock).GroundPosition is null, material: "cloth");
        }
        state = await history.ChooseAsync("market_leave:",
            world => PaidMarketWorld.Market(world).Occupancies.All(item => item.EndedTick is not null));
        var retained = PaidMarketWorld.Market(state);
        Assert.Equal(32, retained.Trades.Count);
        Assert.DoesNotContain(retained.StockReceipts, receipt => receipt.Id == sourceReceipt);
        Assert.Equal(48, retained.StockReceipts.Count);
        Assert.Equal(32, state.Society.Society.Inventory.Offers.Count(offer => offer.Id.StartsWith(MarketTradeRules.OfferPrefix, StringComparison.Ordinal)));
        Assert.Equal(64, state.Society.Society.Inventory.Reservations.Count(claim => offers.Any(id => claim.Id == id + ":first" || claim.Id == id + ":second")));
        foreach (var trade in retained.Trades)
        {
            var accepted = state.Society.Society.Inventory.GetOffer(trade.OfferId).AcceptedBy;
            Assert.Equal(trade.SettledTick is not null ? new[] { buyer, Seller }.Order(StringComparer.Ordinal) : new[] { buyer }, accepted);
        }
        Assert.Equal(4, state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == Seller && lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity));
        Assert.Equal(20, state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood" && lot.StorageBuildingId == house &&
            lot.Id.StartsWith("history-payment-", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        Assert.Equal(market.Stalls, retained.Stalls);
        return PrivateWorldRuntimeCodec.Encode(state);
    }

    internal sealed class HistoryWorld : IDisposable
    {
        private readonly MarketRulesPolicy policy;
        private readonly string actor;
        private readonly string stall;
        private string wanted = "";
        private string chooser;
        private string? material;
        private PrivateWorldRuntime live;
        private PrivateWorldRuntime? replay;

        internal HistoryWorld(PrivateWorldRuntimeState state, string actor, string stall)
        {
            this.actor = actor;
            chooser = actor;
            this.stall = stall;
            policy = new MarketRulesPolicy
            {
                Choose = (person, candidates) => person == chooser
                    ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(wanted, StringComparison.Ordinal) &&
                        candidate.DestinationId == this.stall &&
                        (material is null || candidate.Description.Contains(" " + material + " ", StringComparison.Ordinal) ||
                            candidate.Description.Contains(" " + material + ";", StringComparison.Ordinal))) : null,
            };
            live = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        }

        internal void AssertUnavailable(string person, Func<CognitionCandidate, bool> forbidden)
        {
            // Include the first frame in this world: an unchanged safe-idle candidate
            // set is deliberately cached, so absence need not call the provider again.
            var observed = policy.Offered.Where(item => item.Actor == person).ToArray();
            Assert.NotEmpty(observed);
            Assert.All(observed, item => Assert.DoesNotContain(item.Candidates, candidate => forbidden(candidate)));
            live.Validate();
        }

        internal List<double> TickMilliseconds { get; } = [];

        internal void Checkpoint(bool compare)
        {
            replay?.Dispose();
            replay = null;
            if (!compare) return;
            // Exercise strict checkpoints immediately before/after retirement and at the final
            // retained window. Ordinary phases use one uninterrupted native world.
            var bytes = PrivateWorldRuntimeCodec.Encode(live.ExportState());
            live.Dispose();
            live = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
            replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        }

        internal void ArrangeInventory(Func<InventoryCheckpoint, InventoryCheckpoint> change)
        {
            var state = live.ExportState();
            replay?.Dispose();
            replay = null;
            live.Dispose();
            live = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, change(state.Society.Society.Inventory)), policy.CreateProvider);
        }

        internal async Task<PrivateWorldRuntimeState> ChooseAsync(string prefix, Func<PrivateWorldRuntime, bool> reached,
            string? chooseActor = null, string? material = null, int maxSteps = 8)
        {
            wanted = prefix;
            chooser = chooseActor ?? actor;
            this.material = material;
            var before = policy.Chosen.Count;
            for (var step = 0; step < maxSteps && !reached(live); step++)
            {
                var started = Stopwatch.GetTimestamp();
                Assert.True((await live.AdvanceOneTickAsync()).Advanced);
                TickMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                live.Validate();
                if (replay is null) continue;
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(live.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            Assert.True(reached(live), $"No native {prefix} transition at {live.WorldTick}; actor {chooser} at {live.ExportState().Inhabitants.Single(item => item.InhabitantId == chooser).Position}; selected {string.Join(", ", policy.Chosen.Skip(before).Where(item => item.Actor == chooser).TakeLast(4).Select(item => item.Id))}; offered: {string.Join(", ", policy.Offered.Where(item => item.Actor == chooser).Last().Candidates.Where(candidate => candidate.Id.StartsWith("market_", StringComparison.Ordinal)).Select(candidate => candidate.Description))}.");
            Assert.Contains(policy.Chosen.Skip(before), choice => choice.Actor == chooser && choice.Id.StartsWith(prefix, StringComparison.Ordinal));
            return live.ExportState();
        }

        public void Dispose()
        {
            replay?.Dispose();
            live.Dispose();
        }
    }
}
