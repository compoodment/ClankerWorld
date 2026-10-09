using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketBlacksmithDemandTests
{
    private static readonly string[] BuyerActions = ["market_buy:", "market_continue:"];
    private static readonly string[] SellerActions = ["market_continue:"];
    private static readonly Lazy<Task<byte[]>> Smith = new(BuildSmithAsync);
    private static readonly Dictionary<string, Lazy<Task<byte[]>>> Stocked = new(StringComparer.Ordinal)
    {
        ["iron"] = new(() => StockAsync("iron")),
        ["gold"] = new(() => StockAsync("gold")),
    };

    [Theory]
    [InlineData("iron", "active", 2)]
    [InlineData("iron", "missing", 2)]
    [InlineData("iron", "left", 0)]
    [InlineData("gold", "active", 4)]
    [InlineData("gold", "missing", 4)]
    [InlineData("gold", "left", 0)]
    public async Task PurchasesUseAvailableMetalWithoutTakingAHousematesSaleStock(string kind, string boundary, int expected)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Stocked[kind].Value);
        var (foreignSeller, seller, buyer) = Actors(state);
        var household = PaidMarketWorld.HouseholdOf(state, buyer);
        var ownReceipt = Assert.Single(PaidMarketWorld.Market(state).StockReceipts, item => item.OwnerId == household);
        if (boundary == "missing")
            state = PaidMarketWorld.WithInventory(state, state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Where(lot => !MarketTradeRules.IsReceiptLot(ownReceipt, lot)).ToArray(),
            });
        if (boundary == "left")
        {
            using var leaving = PrivateWorldRuntime.Restore(Fresh(state), Policy(seller, "market_leave:").CreateProvider);
            for (var tick = 0; tick < 12 && PaidMarketWorld.Market(leaving).Occupancies.Any(item => item.SellerAgentId == seller && item.EndedTick is null); tick++)
                Assert.True((await leaving.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(PaidMarketWorld.Market(leaving).Occupancies, item => item.SellerAgentId == seller && item.EndedTick is null);
            state = leaving.ExportState();
        }
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != buyer || lot.ItemKind != "wood").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "smith-market-payment", "wood", buyer, 4);
        state = PaidMarketWorld.At(Fresh(PaidMarketWorld.WithInventory(state, inventory)), buyer,
            MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, 0));
        var choices = BuyingPolicy(foreignSeller, buyer);
        using var world = PrivateWorldRuntime.Restore(state, choices.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            BuyingPolicy(foreignSeller, buyer).CreateProvider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 30; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var final = world.ExportState();
        var market = PaidMarketWorld.Market(final);
        var trades = market.Trades.Where(item => item.BuyerId == buyer).ToArray();
        Assert.Equal(expected, trades.Length);
        Assert.Equal(expected != 0, choices.OfferedTo(buyer).Any(item => item.Id.StartsWith("market_buy:", StringComparison.Ordinal)));
        Assert.Equal(expected, final.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == buyer && lot.ItemKind == kind).Sum(lot => lot.Quantity));
        Assert.Equal(4 - expected, final.Society.Society.Inventory.Lots.Where(lot => lot.Id == "smith-market-payment" && lot.OwnerId == buyer).Sum(lot => lot.Quantity));
        Assert.All(trades, trade =>
        {
            Assert.NotNull(trade.SettledTick);
            Assert.Null(trade.CancellationReason);
            Assert.Equal((foreignSeller, kind, "wood"), (trade.SellerAgentId, trade.GoodsKind, trade.PaymentKind));
            var offer = final.Society.Society.Inventory.GetOffer(trade.OfferId);
            Assert.Equal(DirectBarterState.Settled, offer.State);
            Assert.Equal((1, 1), (offer.FirstQuantity, offer.SecondQuantity));
            Assert.Contains(buyer, offer.AcceptedBy);
            Assert.Contains(trade.GoodsOwnerId, offer.AcceptedBy);
            Assert.Equal("smith-market-payment", offer.SecondLotId);
        });
        var foreignHousehold = PaidMarketWorld.HouseholdOf(state, foreignSeller);
        Assert.Equal(expected, final.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == foreignHousehold &&
            (lot.Id == "smith-market-payment" || lot.ProvenanceLotId == "smith-market-payment")).Sum(lot => lot.Quantity));
        Assert.Equal(4 - expected, final.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind && lot.OwnerId == foreignHousehold).Sum(lot => lot.Quantity));
        Assert.Equal(boundary == "missing" ? 0 : 4, final.Society.Society.Inventory.Lots.Where(lot => MarketTradeRules.IsReceiptLot(ownReceipt, lot)).Sum(lot => lot.Quantity));
        Assert.Equal(boundary == "left" ? 1 : 2, market.Occupancies.Count(item => item.EndedTick is null));
        Assert.Equal(boundary == "missing" ? 4 : 8, final.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity));
        Assert.Equal(boundary == "left", choices.OfferedTo(buyer).Any(item => item.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
            item.Description.Contains(" " + kind + " ", StringComparison.Ordinal)));
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(final);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static async Task<byte[]> BuildSmithAsync()
    {
        var state = await PaidMarketWorld.StateAsync();
        var (_, _, buyer) = Actors(state);
        var household = PaidMarketWorld.HouseholdOf(state, buyer);
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in BlacksmithContent.Blacksmith1x2().BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "market-smith-cost-" + cost.ResourceId, cost.ResourceId, household, cost.Amount);
        using var builder = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory));
        foreach (var tile in PaidMarketWorld.Town(state).BorderTiles.OrderBy(point => state.Map.FootDistance(PaidMarketWorld.Market(state).Site, point)))
            if (builder.PlaceBuilding("market-demand-smith", BlacksmithContent.Blacksmith1x2().CanonicalId, tile, household).Applied) break;
        Assert.Contains(builder.WorldSimulation.Buildings, item => item.InstanceId == "market-demand-smith" && item.HouseholdId == household);
        builder.Validate();
        return PrivateWorldRuntimeCodec.Encode(builder.ExportState());
    }

    private static async Task<byte[]> StockAsync(string kind)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Smith.Value);
        var (foreignSeller, seller, _) = Actors(state);
        var inventory = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != kind).ToArray() };
        foreach (var person in new[] { foreignSeller, seller })
        {
            var id = "market-smith-stock:" + person;
            var household = PaidMarketWorld.HouseholdOf(state, person);
            inventory = InventoryFixture.AddLot(inventory, id, kind, household, 4);
            inventory = InventoryFixture.Relocate(inventory, "market-smith-cargo:" + person, id, household, 4, carrierId: person);
        }
        state = PaidMarketWorld.WithInventory(state, inventory) with
        { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        foreach (var (person, slot) in new[] { (foreignSeller, 0), (seller, 1) })
        {
            var household = PaidMarketWorld.HouseholdOf(state, person);
            state = PaidMarketWorld.At(Fresh(state), person, MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, slot));
            using var stocking = PrivateWorldRuntime.Restore(state, Policy(person, "market_borrow:", "market_deposit:").CreateProvider);
            for (var tick = 0; tick < 30 && !PaidMarketWorld.Market(stocking).StockReceipts.Any(item => item.OwnerId == household); tick++)
                Assert.True((await stocking.AdvanceOneTickAsync()).Advanced);
            var receipt = Assert.Single(PaidMarketWorld.Market(stocking).StockReceipts, item => item.OwnerId == household);
            Assert.Equal((kind, 4), (receipt.ItemKind, receipt.Quantity));
            state = stocking.ExportState();
        }
        Assert.Equal(2, PaidMarketWorld.Market(state).Occupancies.Count(item => item.EndedTick is null));
        using var validated = PrivateWorldRuntime.Restore(state);
        validated.Validate();
        return PrivateWorldRuntimeCodec.Encode(validated.ExportState());
    }

    private static (string ForeignSeller, string Seller, string Buyer) Actors(PrivateWorldRuntimeState state)
    {
        var foreignSeller = state.Inhabitants[0].InhabitantId;
        var foreignHousehold = PaidMarketWorld.HouseholdOf(state, foreignSeller);
        var seller = state.Society.Society.Inhabitants.First(person => person.HouseholdId is not null && person.HouseholdId != foreignHousehold).Id;
        return (foreignSeller, seller, state.Society.Society.Inhabitants.First(person => person.HouseholdId == PaidMarketWorld.HouseholdOf(state, seller) && person.Id != seller).Id);
    }

    private static PrivateWorldRuntimeState Fresh(PrivateWorldRuntimeState state) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person with
        { HungerBasisPoints = 9_500, Survival = new SurvivalCondition(), LastDecisionContext = null, Project = null }).ToArray(),
    };

    private static MarketRulesPolicy Policy(string actor, params string[] actions) => new()
    {
        Choose = (person, candidates) => person == actor
            ? actions.Select(prefix => candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(candidate => candidate is not null) ?? candidates.Single(candidate => candidate.Id == "safe_idle")
            : candidates.Single(candidate => candidate.Id == "safe_idle"),
    };

    private static MarketRulesPolicy BuyingPolicy(string seller, string buyer) => new()
    {
        Choose = (person, candidates) => (person == buyer ? BuyerActions :
                person == seller ? SellerActions : Array.Empty<string>())
            .Select(prefix => candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
            .FirstOrDefault(candidate => candidate is not null) ?? candidates.Single(candidate => candidate.Id == "safe_idle"),
    };
}
