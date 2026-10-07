using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketSpoiledStockCollectionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, "left", 4)]
    [InlineData(true, "left", 4)]
    [InlineData(true, "borrowed", 4)]
    [InlineData(true, "partial", 2)]
    [InlineData(true, "reserved", 0)]
    [InlineData(true, "full", 0)]
    [InlineData(true, "stranger", 0)]
    [InlineData(true, "urgent", 0)]
    public async Task PhysicalCollectionRespectsStockAndReceivingAuthority(bool spoil, string boundary, int expectedCollected)
    {
        var state = await PaidMarketWorld.StateAsync();
        var owner = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "collection-starting-berries", "berries", owner, 6, freshnessBasisPoints: 140);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), owner,
            MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, 0)) with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
        };
        var phase = "deposit";
        var collector = owner;
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == collector ? candidates.FirstOrDefault(candidate =>
                phase == "deposit" && (candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal) ||
                    candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" berries ", StringComparison.Ordinal)) ||
                phase == "leave" && candidate.Id.StartsWith("market_leave:", StringComparison.Ordinal) ||
                phase == "collect" && candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" berries ", StringComparison.Ordinal)) ??
                    candidates.Single(candidate => candidate.Id == "safe_idle")
                : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        await Until(world, () => PaidMarketWorld.Market(world).StockReceipts.Count == 1, 20);
        var receipt = Assert.Single(PaidMarketWorld.Market(world).StockReceipts);
        Assert.Equal((owner, 4), (receipt.OwnerId, receipt.Quantity));
        AssertReload(world);
        if (boundary != "borrowed")
        {
            phase = "leave";
            await Until(world, () => world.ExportState().Events.Any(item => item.Kind == "market_stall_left"), 12);
        }
        state = world.ExportState();
        if (boundary is "partial" or "reserved")
        {
            // A controlled live claim on actual deposited stock survives its spoilage.
            inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "collection-held-stock", owner,
                receipt.LotId, boundary == "partial" ? 2 : 4, "retain displayed goods", long.MaxValue, isExclusive: false);
            state = PaidMarketWorld.WithInventory(state, inventory);
        }
        using var waiting = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        phase = "idle";
        if (spoil)
        {
            await Until(waiting, () => waiting.Society.Inventory.GetLot(receipt.LotId).FreshnessBasisPoints == 0, 100);
        }
        AssertReload(waiting);
        state = waiting.ExportState();
        if (boundary == "stranger")
        {
            collector = state.Inhabitants.First(person => person.InhabitantId != owner).InhabitantId;
            state = PaidMarketWorld.At(state, collector, state.Inhabitants.Single(person => person.InhabitantId == owner).Position);
        }
        if (boundary == "full")
        {
            inventory = state.Society.Society.Inventory;
            var equipment = state.Inhabitants.Single(person => person.InhabitantId == owner).Equipment;
            var room = PersonalEquipmentRules.Capacity(inventory, owner, equipment) -
                PersonalEquipmentRules.CarriedQuantity(inventory, owner, equipment);
            Assert.True(room > 0);
            state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(inventory, "collection-capacity-wood", "wood", owner, room));
        }
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == collector ? person with
            {
                LastDecisionContext = null,
                HungerBasisPoints = boundary == "urgent" ? 1_000 : person.HungerBasisPoints,
            } : person).ToArray(),
        };
        // Earlier choices belong to the pre-boundary world, even when they share its last tick.
        policy.Offered.Clear();
        using var collecting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
        var before = collecting.Society.Inventory.GetLot(receipt.LotId);
        Assert.Equal(4, before.Quantity);
        Assert.NotNull(before.GroundPosition);
        phase = "collect";
        using var replay = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        var unchanged = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
        Assert.False((await collecting.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(unchanged, PrivateWorldRuntimeCodec.Encode(collecting.ExportState()));
        for (var tick = 0; tick < 12 && !collecting.ExportState().Events.Any(item => item.Kind == "market_stock_collected"); tick++)
        {
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(collecting.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var stock = collecting.Society.Inventory.Lots.Where(lot => MarketTradeRules.IsReceiptLot(receipt, lot)).ToArray();
        AssertReload(collecting);
        var carried = stock.Where(lot => PersonalEquipmentRules.IsCarried(lot, collector)).Sum(lot => lot.Quantity);
        output.WriteLine($"spoil={spoil}; boundary={boundary}; collected={carried}; ground=" +
            stock.Where(lot => lot.GroundPosition is not null).Sum(lot => lot.Quantity));
        Assert.Equal(4, stock.Sum(lot => lot.Quantity));
        Assert.All(stock, lot => Assert.Equal(owner, lot.OwnerId));
        Assert.Equal(expectedCollected, carried);
        Assert.Equal(4 - expectedCollected, stock.Where(lot => lot.GroundPosition is not null).Sum(lot => lot.Quantity));
        if (spoil) Assert.All(stock, lot => Assert.Equal(0, lot.FreshnessBasisPoints));
        if (expectedCollected == 0)
            Assert.DoesNotContain(policy.Offered.Where(item => item.Actor == collector && item.Tick >= state.Society.Society.WorldTick)
                .SelectMany(item => item.Candidates), candidate => candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" berries ", StringComparison.Ordinal));
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done());
    }

    private static void AssertReload(PrivateWorldRuntime world)
    {
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
