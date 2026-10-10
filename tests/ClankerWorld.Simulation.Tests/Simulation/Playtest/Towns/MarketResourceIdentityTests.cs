using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketResourceIdentityTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(129)]
    public async Task LongResourceStockAndPaymentSurviveNativeMarketPhases(int resourceLength)
    {
        var state = await PaidMarketWorld.StateAsync();
        var seller = state.Society.Society.Inhabitants.First(person => person.HouseholdId is not null &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand == SocietyAgeBand.Adult).Id;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var buyer = state.Society.Society.Inhabitants.First(person => person.HouseholdId is not null && person.HouseholdId != household &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand == SocietyAgeBand.Adult).Id;
        var stockKind = new string('x', resourceLength);
        var paymentKind = new string('y', resourceLength);
        new ContentQuantity(stockKind, 2).Validate();
        new ContentQuantity(paymentKind, 2).Validate();
        var stall = PaidMarketWorld.Market(state).Stalls.First(item => item.SlotIndex == 0);
        // Controlled inventory and nearby actor positions on a genuinely paid Market.
        // Native borrowing, deposits, quote, seller acceptance and payment deposit follow.
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "long-market-stock", stockKind, seller, 2);
        inventory = InventoryFixture.AddLot(inventory, "long-market-axes", "wooden_axe", seller, 2);
        inventory = InventoryFixture.AddLot(inventory, "long-market-best-tool", "iron_axe", seller, 1);
        inventory = InventoryFixture.AddLot(inventory, "a-long-market-payment", paymentKind, buyer, 2);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != buyer ||
                lot.Id.StartsWith("market-rules-", StringComparison.Ordinal) || lot.Id == "a-long-market-payment").ToArray(),
        };
        var entrance = MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, 0);
        state = PaidMarketWorld.At(PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), seller, entrance), buyer, entrance);
        var phase = "stock";
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) =>
            {
                var local = candidates.Where(candidate => candidate.DestinationId == stall.BuildingId).ToArray();
                if (phase == "stock" && actor == seller)
                    return local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                        local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                            (candidate.Description.Contains(stockKind, StringComparison.Ordinal) ||
                             candidate.Description.Contains("wooden_axe", StringComparison.Ordinal)));
                if (phase == "offer" && actor == buyer)
                    return local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal));
                if (phase == "accept" && actor == seller)
                    return local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_continue:", StringComparison.Ordinal));
                return null;
            },
        };
        var directory = Directory.CreateTempSubdirectory("long-market-resource-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            async Task<PrivateWorldRuntimeState> RunPhaseAsync(PrivateWorldRuntimeState initial, Func<PrivateWorldRuntime, bool> done)
            {
                using var world = PrivateWorldRuntime.Restore(initial, policy.CreateProvider);
                file.Save(world);
                using var host = new PrivateWorldRuntimeService(world, file, presence);
                for (var tick = 0; tick < 120 && !done(world); tick++)
                {
                    Assert.True(await host.TryAdvanceOnceAsync(), phase);
                    Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), File.ReadAllBytes(file.Path));
                }
                Assert.True(done(world), phase + ": " + string.Join(", ", policy.Chosen.TakeLast(12).Select(choice => choice.Actor + "=" + choice.Id)));
                using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)), policy.CreateProvider);
                Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
                return restored.ExportState();
            }
            state = await RunPhaseAsync(state, world => PaidMarketWorld.Market(world).StockReceipts.Count == 2);
            Assert.Contains(PaidMarketWorld.Market(state).StockReceipts, receipt => receipt.ItemKind == stockKind);
            var damagedDeposit = PaidMarketWorld.WithMarket(state, market => market with
            {
                StockReceipts = market.StockReceipts.Select(receipt => receipt.ItemKind == stockKind
                    ? receipt with { ItemKind = stockKind[..^1] + "z" } : receipt).ToArray(),
            });
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(damagedDeposit));
            phase = "offer";
            state = await RunPhaseAsync(state, world => PaidMarketWorld.Market(world).Trades.Count == 1);
            var trade = Assert.Single(PaidMarketWorld.Market(state).Trades);
            Assert.Equal(("wooden_axe", paymentKind), (trade.GoodsKind, trade.PaymentKind));
            Assert.Equal(DirectBarterState.Open, state.Society.Society.Inventory.GetOffer(trade.OfferId).State);
            var damagedTrade = PaidMarketWorld.WithMarket(state, market => market with
            {
                Trades = [trade with { PaymentKind = paymentKind[..^1] + "z" }],
            });
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(damagedTrade));
            phase = "accept";
            state = await RunPhaseAsync(state, world => world.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Settled);
            var receipt = Assert.Single(PaidMarketWorld.Market(state).StockReceipts, item => item.TradeOfferId == trade.OfferId);
            Assert.Equal(paymentKind, receipt.ItemKind);
            var paid = state.Society.Society.Inventory.GetLot(receipt.LotId);
            Assert.Equal((paymentKind, household, 1), (paid.ItemKind, paid.OwnerId, paid.Quantity));
            Assert.Equal(2, state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == paymentKind).Sum(lot => lot.Quantity));
            Assert.Contains(policy.Chosen, choice => choice.Id.StartsWith("market_borrow:", StringComparison.Ordinal));
            Assert.Contains(policy.Chosen, choice => choice.Id.StartsWith("market_deposit:", StringComparison.Ordinal));
            Assert.Contains(policy.Chosen, choice => choice.Id.StartsWith("market_buy:", StringComparison.Ordinal));
            Assert.Contains(policy.Chosen, choice => choice.Id.StartsWith("market_continue:", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
