using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketHistoryRuntimeTests
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
        Assert.Equal(40, visits.Count);
        Assert.Equal(40, receipts.Count);
        Assert.Equal(32, PaidMarketWorld.Market(state).Occupancies.Count);
        Assert.Equal(32, PaidMarketWorld.Market(state).StockReceipts.Count);
        Assert.Empty(PaidMarketWorld.Market(state).Trades);
        Assert.Equal(market.Stalls, PaidMarketWorld.Market(state).Stalls);
        Assert.Equal(40, state.Events.Count(item => item.Kind == "market_stock_delivered"));
    }

    private sealed class HistoryWorld : IDisposable
    {
        private readonly MarketRulesPolicy policy;
        private readonly string actor;
        private readonly string stall;
        private string wanted = "";
        private PrivateWorldRuntime live;
        private PrivateWorldRuntime? replay;

        internal HistoryWorld(PrivateWorldRuntimeState state, string actor, string stall)
        {
            this.actor = actor;
            this.stall = stall;
            policy = new MarketRulesPolicy
            {
                Choose = (person, candidates) => person == this.actor
                    ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(wanted, StringComparison.Ordinal) &&
                        candidate.DestinationId == this.stall) : null,
            };
            live = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        }

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

        internal async Task<PrivateWorldRuntimeState> ChooseAsync(string prefix, Func<PrivateWorldRuntime, bool> reached)
        {
            wanted = prefix;
            var before = policy.Chosen.Count;
            for (var step = 0; step < 8 && !reached(live); step++)
            {
                Assert.True((await live.AdvanceOneTickAsync()).Advanced);
                live.Validate();
                if (replay is null) continue;
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(live.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            Assert.True(reached(live), $"No native {prefix} transition at {live.WorldTick}.");
            Assert.Contains(policy.Chosen.Skip(before), choice => choice.Actor == actor && choice.Id.StartsWith(prefix, StringComparison.Ordinal));
            return live.ExportState();
        }

        public void Dispose()
        {
            replay?.Dispose();
            live.Dispose();
        }
    }
}
