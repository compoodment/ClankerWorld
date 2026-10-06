using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketHistorySaveTests
{
    [Theory]
    [InlineData("missing-counter")]
    [InlineData("missing-boundary")]
    [InlineData("missing-receipt-sequence")]
    [InlineData("missing-trade-source")]
    [InlineData("negative-counter")]
    [InlineData("boundary-past-counter")]
    [InlineData("missing-live-suffix")]
    [InlineData("duplicate-sequence")]
    [InlineData("changed-kind")]
    [InlineData("unknown-source")]
    [InlineData("missing-payment")]
    [InlineData("missing-offer")]
    public async Task DamagedRetirementAndLivePaymentBindingsAreRefused(string damage)
    {
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var root = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var market = root["state"]!["towns"]![0]!["markets"]![0]!;
        var receipts = market["stockReceipts"]!.AsArray();
        var trades = market["trades"]!.AsArray();
        switch (damage)
        {
            case "missing-counter": market.AsObject().Remove("nextStockReceiptSequence"); break;
            case "missing-boundary": market.AsObject().Remove("retiredStockReceiptThrough"); break;
            case "missing-receipt-sequence": receipts[0]!.AsObject().Remove("sequence"); break;
            case "missing-trade-source": trades[0]!.AsObject().Remove("stockReceiptSequence"); break;
            case "negative-counter": market["nextStockReceiptSequence"] = -1; break;
            case "boundary-past-counter": market["retiredStockReceiptThrough"] = market["nextStockReceiptSequence"]!.GetValue<long>(); break;
            case "missing-live-suffix": receipts.RemoveAt(receipts.Count - 1); break;
            case "duplicate-sequence": receipts[1]!["sequence"] = receipts[0]!["sequence"]!.GetValue<long>(); break;
            case "changed-kind": receipts[^1]!["itemKind"] = "stone"; break;
            case "unknown-source": trades[0]!["stockReceiptSequence"] = market["nextStockReceiptSequence"]!.GetValue<long>(); break;
            case "missing-payment":
                receipts.Remove(receipts.First(receipt => receipt!["tradeOfferId"] is not null));
                break;
            case "missing-offer":
                var offers = root["state"]!["society"]!["society"]!["inventory"]!["offers"]!.AsArray();
                var id = trades[0]!["offerId"]!.GetValue<string>();
                offers.Remove(offers.First(offer => offer!["id"]!.GetValue<string>() == id));
                break;
        }
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public async Task RemovalKeepsPaidHistoryAndOwnersCanCollectAfterSourceReceiptsRetire()
    {
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var town = PaidMarketWorld.Town(state);
        var market = PaidMarketWorld.Market(state);
        var before = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity);
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == "founder:00000000000000000000000000000001"
                ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
                    candidate.Description.Contains("wooden_axe", StringComparison.Ordinal)) : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        foreach (var stall in market.Stalls)
            Assert.True(world.RemoveBuilding(stall.BuildingId, town.Id, null).Applied);
        Assert.True(world.RemoveBuilding(market.HallBuildingId, town.Id, null).Applied);
        for (var tick = 0; tick < 12 && world.Society.Inventory.Lots.Any(lot => lot.ItemKind == "wooden_axe" && lot.GroundPosition is not null); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotNull(PaidMarketWorld.Market(world).RemovedTick);
        Assert.All(PaidMarketWorld.Market(world).Stalls, stall => Assert.NotNull(stall.RemovedTick));
        Assert.Equal(before, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == "wooden_axe" && lot.GroundPosition is not null);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), policy.CreateProvider);
        restored.Validate();
    }

    [Fact]
    public async Task ARejectedDepositDoesNotCommitItsReceiptOrRetirementBoundary()
    {
        const string seller = "founder:00000000000000000000000000000001";
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var market = PaidMarketWorld.Market(state);
        var stall = market.Stalls.Single(item => item.SlotIndex == 0);
        using (var preparation = new MarketHistoryRuntimeTests.HistoryWorld(state, seller, stall.BuildingId))
            state = await preparation.ChooseAsync("market_borrow:",
                world => PaidMarketWorld.Market(world).Occupancies.Any(item => item.EndedTick is null));
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == seller ? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                candidate.Description.Contains(" cloth ", StringComparison.Ordinal)) : null,
        };
        using var control = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        using var rejected = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        var before = PrivateWorldRuntimeCodec.Encode(state);
        var initial = PaidMarketWorld.Market(state);
        Assert.True((await control.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(initial.NextStockReceiptSequence + 1, PaidMarketWorld.Market(control).NextStockReceiptSequence);
        Assert.True(PaidMarketWorld.Market(control).RetiredStockReceiptThrough > initial.RetiredStockReceiptThrough);
        Assert.False((await rejected.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
        rejected.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(rejected.ExportState());
        Assert.False((await rejected.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
        rejected.Resume();
        Assert.True((await rejected.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PaidMarketWorld.Market(control).NextStockReceiptSequence, PaidMarketWorld.Market(rejected).NextStockReceiptSequence);
        Assert.Equal(PaidMarketWorld.Market(control).RetiredStockReceiptThrough, PaidMarketWorld.Market(rejected).RetiredStockReceiptThrough);
        rejected.Validate();
    }

    [Fact]
    public async Task SameHouseholdSuccessorNeedsAFreshDepositAfterHistoricalAuthorityRetires()
    {
        const string seller = "founder:00000000000000000000000000000001";
        const string successor = "founder:00000000000000000000000000000002";
        const string buyer = "founder:00000000000000000000000000000003";
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var market = PaidMarketWorld.Market(state);
        var stall = market.Stalls.Single(item => item.SlotIndex == 0);
        var position = MarketContent.StallSite(market.Site, 0);
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var inventory = state.Society.Society.Inventory;
        // A real ownership transfer makes old stock household property. Sharing the
        // owner with the successor must still confer no earlier occupancy's authority.
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == seller && lot.ItemKind == "wooden_axe").ToArray())
            inventory = InventoryFixture.Transfer(inventory, "history-household-stock:" + lot.Id, seller, household,
                lot.Id, lot.Quantity, "history_fixture_gift", destinationGroundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "history-successor-cloth", "cloth", successor, 1);
        inventory = InventoryFixture.AddLot(inventory, "history-successor-payment", "wood", buyer, 1);
        state = PaidMarketWorld.At(PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), buyer, position),
            successor, MarketContent.StallEntrance(market.Site, 0));
        using var history = new MarketHistoryRuntimeTests.HistoryWorld(state, successor, stall.BuildingId);
        state = await history.ChooseAsync("market_borrow:", world => PaidMarketWorld.Market(world).Occupancies.Any(item => item.EndedTick is null));
        state = await history.ChooseAsync("market_deposit:", world => world.Society.Inventory.GetLot("history-successor-cloth").GroundPosition is not null,
            material: "cloth");
        state = await history.ChooseAsync("market_collect:", world => !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == household &&
            lot.ItemKind == "wooden_axe" && MarketTradeRules.IsAt(lot, position)), material: "wooden_axe", maxSteps: 16);
        history.AssertUnavailable(buyer, candidate => candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal) &&
            candidate.Description.Contains("wooden_axe", StringComparison.Ordinal));
        state = await history.ChooseAsync("market_deposit:", world => world.Society.Inventory.Lots.Any(lot => lot.OwnerId == household &&
            lot.ItemKind == "wooden_axe" && MarketTradeRules.IsAt(lot, position)), material: "wooden_axe");
        state = await history.ChooseAsync("market_buy:", world => PaidMarketWorld.Market(world).Trades.Any(item =>
            item.SettledTick is null && item.CancellationReason is null), buyer, material: "wooden_axe");
        var trade = Assert.Single(PaidMarketWorld.Market(state).Trades, item => item.SettledTick is null && item.CancellationReason is null);
        Assert.Equal(successor, trade.SellerAgentId);
        Assert.Equal(household, trade.GoodsOwnerId);
        Assert.Equal(4, state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity));
        var damaged = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var damagedMarket = damaged["state"]!["towns"]![0]!["markets"]![0]!;
        var sources = damagedMarket["stockReceipts"]!.AsArray();
        sources.Remove(sources.Single(receipt => receipt!["sequence"]!.GetValue<long>() == trade.StockReceiptSequence));
        damagedMarket["retiredStockReceiptThrough"] = trade.StockReceiptSequence;
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(damaged.ToJsonString())));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        restored.Validate();
    }

    [Fact]
    public async Task DepartedSellerKeepsPersonalStockAndRetiredHistoryThroughReplay()
    {
        const string seller = "founder:00000000000000000000000000000001";
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == seller ? candidates.SingleOrDefault(candidate => candidate.Id == "household_leave") : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        for (var tick = 0; tick < 8 && world.Society.GetInhabitant(seller).HouseholdId is not null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(world.Society.GetInhabitant(seller).HouseholdId);
        Assert.Contains(policy.Chosen, choice => choice.Actor == seller && choice.Id == "household_leave");
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == seller && lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity));
        policy.Choose = (_, _) => null;
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), policy.CreateProvider);
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
    }

    [Fact]
    public async Task DeadSellersEstateOwnsStockWithoutRecreatingHistoricalSaleAuthority()
    {
        const string seller = "founder:00000000000000000000000000000001";
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var physical = state.Inhabitants.Single(person => person.InhabitantId == seller);
        var checkpoint = SocietyFixture.Kill(state.Society.Society, seller, SocietyDeathCause.Accident).Checkpoint;
        var estate = Assert.Single(checkpoint.Estates, item => item.DeceasedId == seller);
        // Apply the runtime's physical drop and council transition after the real death
        // operation; the Market's closed records and retired boundary are untouched.
        var inventory = checkpoint.Inventory;
        if (inventory.Lots.Any(lot => lot.CarrierId == seller))
            inventory = InventoryFixture.DropCarrierGoods(inventory, seller, new(physical.Position.X, physical.Position.Y));
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == estate.Id && lot.ContainerLotId is null &&
            lot.CarrierId is null && lot.StorageBuildingId is null && lot.GroundPosition is null).ToArray())
            inventory = InventoryFixture.Relocate(inventory, "history-death:" + lot.Id, lot.Id, estate.Id, lot.Quantity,
                groundPosition: new(physical.Position.X, physical.Position.Y));
        checkpoint = checkpoint with { Inventory = inventory };
        state = state with
        {
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != seller).ToArray(),
            DeceasedInhabitants = (state.DeceasedInhabitants ?? []).Append(new(seller, checkpoint.WorldTick,
                checkpoint.AgeAt(checkpoint.GetInhabitant(seller), checkpoint.WorldTick), physical)).ToArray(),
            Towns = state.Towns!.Select(town =>
            {
                var residents = town.ResidentIds.Where(id => id != seller).ToArray();
                var adults = residents.Where(id => checkpoint.Inhabitants.Any(person => person.Id == id &&
                    person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder));
                return town with
                {
                    ResidentIds = residents,
                    Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed,
                        adults, checkpoint.WorldTick, state.WorldSystems!.Config.TicksPerDay),
                };
            }).ToArray(),
        };
        Assert.Equal(4, inventory.Lots.Where(lot => lot.OwnerId == estate.Id && lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity));
        var policy = new MarketRulesPolicy();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.DoesNotContain(PaidMarketWorld.Market(world).Trades, trade => trade.SettledTick is null && trade.CancellationReason is null);
        Assert.Equal(32, PaidMarketWorld.Market(world).Trades.Count);
        world.Validate();
    }

    [Fact]
    public async Task ArchivedInventoryEvidenceReloadsAndContinuesAfterMarketRetirement()
    {
        var state = await MarketHistoryRuntimeTests.ClosedTradeStateAsync();
        var inventory = state.Society.Society.Inventory;
        var cloth = inventory.GetLot("history-personal-cloth");
        // Produce real, property-preserving inventory events rather than inventing receipt history.
        for (var index = 0; index < PrivateWorldHistory.CompactionThreshold + 1; index++)
            inventory = InventoryFixture.Relocate(inventory, "history-archive:" + index, cloth.Id, cloth.OwnerId,
                cloth.Quantity, carrierId: cloth.OwnerId);
        state = PaidMarketWorld.WithInventory(state, inventory);
        var directory = Directory.CreateTempSubdirectory("clankerworld-market-history-");
        try
        {
            var policy = new MarketRulesPolicy();
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), policy.CreateProvider);
            using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
            Assert.True(file.Save(world));
            var compacted = world.ExportState();
            Assert.True(compacted.Society.Society.Inventory.EventHistoryFloor > 0);
            Assert.Equal(32, PaidMarketWorld.Market(compacted).Trades.Count);
            Assert.Equal(48, PaidMarketWorld.Market(compacted).StockReceipts.Count);
            using var restored = file.LoadOrCreate(state.WorldSeed);
            using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)), policy.CreateProvider);
            restored.Resume();
            replay.Resume();
            for (var step = 0; step < 4; step++)
            {
                Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            var archive = Assert.Single(Directory.GetFiles(file.Path + ".history"));
            File.WriteAllText(archive, "corrupt");
            Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(state.WorldSeed));
        }
        finally { directory.Delete(recursive: true); }
    }
}
