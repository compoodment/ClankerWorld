using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Rules of a paid Market checked from one shared, genuinely built Market instead of the long construction run.</summary>
public sealed class MarketRulesTests
{
    private const string Founder1 = "founder:00000000000000000000000000000001";
    private const string Founder2 = "founder:00000000000000000000000000000002";
    private const string Founder3 = "founder:00000000000000000000000000000003";
    private const string Founder4 = "founder:00000000000000000000000000000004";
    private const string StallProposalPrefix = "civic|town:first|project|market-stall-1x1|";

    [Fact]
    public async Task RemovingTheHallSettlesAnApprovedStallProjectBeforeTheWorldIsSaved()
    {
        var state = PaidMarketWorld.WithApprovedStallProject(await PaidMarketWorld.StateAsync(), 1, out var projectId);
        using var world = PrivateWorldRuntime.Restore(state, new MarketRulesPolicy().CreateProvider);
        var town = Assert.Single(world.Towns);
        Assert.Equal("supplying", town.Projects.Single(project => project.Id == projectId).Stage);
        var removed = world.RemoveBuilding(PaidMarketWorld.Market(world).HallBuildingId, town.Id, null);
        Assert.True(removed.Applied, removed.Failure);
        Assert.Equal(world.WorldTick, PaidMarketWorld.Market(world).RemovedTick);
        var project = world.Towns[0].Projects.Single(item => item.Id == projectId);
        Assert.Equal("cancelled", project.Stage);
        Assert.NotNull(project.Blocker);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task AFinishedMarketKeepsItsFreeSlotsAndAislesClearOfOtherUses()
    {
        var state = await PaidMarketWorld.StateAsync();
        var market = PaidMarketWorld.Market(state);
        var site = MarketContent.SiteTiles(market.Site).ToHashSet();
        var freeSlot = PaidMarketWorld.StallTile(state, 1);
        var aisle = new GridPoint(market.Site.X + 4, market.Site.Y + 3);
        Assert.Contains(aisle, site);
        Assert.DoesNotContain(aisle, state.RoadTiles!);
        var policy = new MarketRulesPolicy();
        using var world = PrivateWorldRuntime.Restore(PaidMarketWorld.At(state, Founder1, aisle), policy.CreateProvider);
        var house = HouseContent.House1x1().CanonicalId;
        foreach (var tile in new[] { freeSlot, aisle })
        {
            var placed = world.PlaceBuilding("market-rules-house-" + tile.X + "-" + tile.Y, house, tile, "household:camp-alpha");
            Assert.False(placed.Applied);
            Assert.Contains("overlaps", placed.Failure, StringComparison.Ordinal);
            var requested = world.RequestHouseholdLandUse("request:plaza-" + tile.X + "-" + tile.Y, Founder1, "town:first", [tile]);
            Assert.False(requested.Applied);
        }
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var offered = policy.OfferedTo(Founder1).ToArray();
        var land = Assert.Single(offered.Select(candidate => candidate.Description).Distinct(),
            description => description.Contains("free Town land nearest you", StringComparison.Ordinal));
        var free = land[land.IndexOf("nearest you:", StringComparison.Ordinal)..];
        Assert.False(site.Any(tile => free.Contains(FormattableString.Invariant($"({tile.X}, {tile.Y})"), StringComparison.Ordinal)), land);
        foreach (var candidate in offered.Where(item => item.Id.StartsWith("civic|town:first|project|town-hall-3x4|", StringComparison.Ordinal)))
        {
            var point = candidate.Id.Split('|')[4].Split(',');
            var origin = new GridPoint(int.Parse(point[0], CultureInfo.InvariantCulture), int.Parse(point[1], CultureInfo.InvariantCulture));
            var plan = new TownProjectPayload("Hall", TownHallContent.Hall3x4().CanonicalId, origin, TownHallContent.Entrance(origin), TownHallContent.Hall3x4().BuildCosts);
            Assert.DoesNotContain(TownProjectRules.Footprint(plan).Append(plan.Entrance), site.Contains);
        }
    }

    [Fact]
    public void ARoadOnAnAisleDoesNotFreeAnotherProjectsProtectedDoorway()
    {
        var map = new SeededMap(12, 10, 0,
            (from y in Enumerable.Range(0, 10)
             from x in Enumerable.Range(0, 12)
             select new TerrainTile(new GridPoint(x, y), TerrainKind.Meadow)).ToArray(), [], [], "market-rules-fixture");
        var origin = new GridPoint(5, 2);
        var shape = MarketContent.SiteTiles(new(0, 0)).ToHashSet();
        var aisles = MarketContent.PlazaTiles(new(0, 0)).Except(Enumerable.Range(0, MarketContent.MaximumStalls)
            .Select(slot => MarketContent.StallSite(new(0, 0), slot))).ToHashSet();
        var costs = map.Tiles.ToDictionary(tile => tile.Position, _ => 100);
        var doorway = new GridPoint(origin.X + 4, origin.Y + 5);
        Assert.Contains(new GridPoint(4, 5), aisles);
        TownLayoutContext Context(IEnumerable<GridPoint> protectedTiles) => new(map, null, [doorway], costs, [], [],
            roadTiles: [doorway], requiredLandTiles: MarketContent.SiteTiles(origin), requiredEntranceOffset: new(1, 2),
            requiredFootprintOffsets: shape, permittedRoadOffsets: aisles, protectedTiles: protectedTiles);
        Assert.True(TownLayoutService.TryEvaluateConstructionSite(Context([]), MarketContent.Hall2x2(), origin, out _));
        Assert.False(TownLayoutService.TryEvaluateConstructionSite(Context([doorway]), MarketContent.Hall2x2(), origin, out _));
    }

    [Fact]
    public async Task HouseholdRoutinesLeaveStallStockAloneWhileAHousemateBorrowsTheStallAndBringItHomeAfterwards()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "a-stall-food", "food", "household:camp-alpha", 4,
            groundPosition: new(stall.X, stall.Y));
        inventory = InventoryFixture.AddLot(inventory, "a-stall-grain", FarmFieldRules.Grain, "household:camp-alpha", 4,
            groundPosition: new(stall.X, stall.Y));
        state = PaidMarketWorld.WithInventory(state, inventory);
        var policy = new MarketRulesPolicy(DecisionProviderKind.Deterministic)
        {
            Choose = (actor, candidates) => actor != Founder2 ? null :
                candidates.FirstOrDefault(candidate => candidate.Id == "haul_farm_grain") ??
                candidates.FirstOrDefault(candidate => candidate.Id == "haul_household_stock"),
        };
        using (var borrowed = PrivateWorldRuntime.Restore(PaidMarketWorld.Borrowing(state, Founder1, 0), policy.CreateProvider))
        {
            for (var tick = 0; tick < 40; tick++)
            {
                Assert.True((await borrowed.AdvanceOneTickAsync()).Advanced);
                Assert.Single(PaidMarketWorld.Market(borrowed).Occupancies, item => item.EndedTick is null);
                foreach (var id in new[] { "a-stall-food", "a-stall-grain" })
                {
                    var lot = borrowed.Society.Inventory.GetLot(id);
                    Assert.Equal((4, "household:camp-alpha", (string?)null, new InventoryGroundPosition(stall.X, stall.Y)),
                        (lot.Quantity, lot.OwnerId, lot.CarrierId, lot.GroundPosition));
                }
            }
            Assert.Contains(policy.Chosen, item => item.Actor == Founder2 && item.Id is "haul_farm_grain" or "haul_household_stock");
        }

        using var free = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        bool Recovered() => free.Society.Inventory.Lots.All(lot => lot.GroundPosition != new InventoryGroundPosition(stall.X, stall.Y)) &&
            free.Society.Inventory.Lots.Where(lot => lot.ItemKind == "food" && lot.OwnerId == "household:camp-alpha" &&
                lot.StorageBuildingId == "first-town-house-a").Sum(lot => lot.Quantity) == 36 &&
            free.Society.Inventory.Lots.Where(lot => lot.ItemKind == FarmFieldRules.Grain && lot.OwnerId == "household:camp-alpha" &&
                lot.StorageBuildingId is "first-town-farmhouse").Sum(lot => lot.Quantity) == 4;
        for (var tick = 0; tick < 160 && !Recovered(); tick++) Assert.True((await free.AdvanceOneTickAsync()).Advanced);
        Assert.True(Recovered(), "Household stock left on a free stall must be hauled home by the ordinary routines.");
    }

    [Fact]
    public async Task BuiltInRulesNeverPickAMarketChoiceOverOrdinaryWork()
    {
        var policy = new MarketRulesPolicy(DecisionProviderKind.Deterministic)
        {
            Choose = (_, candidates) => candidates.OrderBy(candidate => candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First(),
        };
        using var world = PrivateWorldRuntime.Restore(await PaidMarketWorld.StateAsync(), policy.CreateProvider);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var withMarketChoices = policy.Offered.Where(item => item.Candidates.Any(candidate => candidate.Id.StartsWith("market_", StringComparison.Ordinal))).ToArray();
        Assert.NotEmpty(withMarketChoices);
        foreach (var observation in withMarketChoices)
        {
            var idle = observation.Candidates.Single(candidate => candidate.Id == "safe_idle").DeterministicPriority;
            Assert.All(observation.Candidates.Where(candidate => candidate.Id.StartsWith("market_", StringComparison.Ordinal)),
                candidate => Assert.True(candidate.DeterministicPriority > idle));
            var chosen = policy.Chosen.Single(item => item.Actor == observation.Actor && item.Tick == observation.Tick).Id;
            Assert.False(chosen.StartsWith("market_", StringComparison.Ordinal));
            Assert.NotEqual("safe_idle", chosen);
        }
    }

    [Fact]
    public async Task ALoadTakenFromRealHouseholdSurplusCanStillBeDepositedAtTheStall()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stallId = PaidMarketWorld.Market(state).Stalls.Single(stall => stall.SlotIndex == 0).BuildingId;
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor != Founder1 ? null :
                candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" food ", StringComparison.Ordinal)) ??
                candidates.FirstOrDefault(candidate => candidate.DestinationId == stallId && candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                candidates.FirstOrDefault(candidate => candidate.DestinationId == stallId && candidate.Id.StartsWith("market_load:", StringComparison.Ordinal) &&
                    candidate.Description.Contains("household-owned food ", StringComparison.Ordinal)),
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        bool Deposited() => PaidMarketWorld.Market(world).StockReceipts.Any(receipt => receipt.ItemKind == "food");
        for (var tick = 0; tick < 240 && !Deposited(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(Deposited(), "Chosen: " + string.Join(", ", policy.Chosen.Where(item => item.Actor == Founder1).Select(item => item.Id)));
        var receipt = Assert.Single(PaidMarketWorld.Market(world).StockReceipts);
        Assert.Equal(("household:camp-alpha", 4), (receipt.OwnerId, receipt.Quantity));
        Assert.Equal(28, world.Society.Inventory.GetLot("food:camp-alpha").Quantity);
        var events = world.ExportState().Events;
        Assert.Single(events, item => item.Kind == "market_stock_loaded");
        Assert.DoesNotContain(events, item => item.Kind == "market_stock_collected");
        Assert.Contains(policy.Chosen, item => item.Id.StartsWith("market_load:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HouseholdGoodsCollectedFromAStallCanBeCarriedBackIntoTheHouse()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "a-stall-food", "food",
            "household:camp-alpha", 4, groundPosition: new(stall.X, stall.Y)));
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor != Founder1 ? null :
                candidates.FirstOrDefault(candidate => candidate.Id == "household_return:a-stall-food") ??
                candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" food ", StringComparison.Ordinal)),
        };
        using var world = PrivateWorldRuntime.Restore(PaidMarketWorld.Borrowing(state, Founder1, 0), policy.CreateProvider);
        bool Stored() => world.Society.Inventory.GetLot("a-stall-food") is { StorageBuildingId: "first-town-house-a", CarrierId: null, OwnerId: "household:camp-alpha", Quantity: 4 };
        for (var tick = 0; tick < 120 && !Stored(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(Stored(), "Chosen: " + string.Join(", ", policy.Chosen.Where(item => item.Actor == Founder1).Select(item => item.Id)));
        var events = world.ExportState().Events;
        Assert.Single(events, item => item.Kind == "market_stock_collected");
        Assert.Single(events, item => item.Kind == "borrowed_goods_returned");
        world.Validate();
    }

    [Fact]
    public async Task AnIdleTickLeavesTheMarketRecordUntouched()
    {
        using var world = PrivateWorldRuntime.Restore(await PaidMarketWorld.StateAsync(), new MarketRulesPolicy().CreateProvider);
        var before = PaidMarketWorld.Market(world);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Same(before, PaidMarketWorld.Market(world));
    }

    [Fact]
    public async Task MoreGoodsThanAStallHoldsStillLoad()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "dropped-estate", "cloth",
            "household:camp-beta", MarketTradeRules.StallCapacity + 4, groundPosition: new(stall.X, stall.Y)));
        using var world = PrivateWorldRuntime.Restore(state);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(saved)));
    }

    [Theory]
    [InlineData("duplicate-trade")]
    [InlineData("duplicate-offer")]
    [InlineData("far-future-offer")]
    public async Task ForgedTradeLedgersAreRefusedAsInvalidData(string damage)
    {
        var state = PaidMarketWorld.Borrowing(await PaidMarketWorld.StateAsync(), Founder3, 0);
        var market = PaidMarketWorld.Market(state);
        var stall = market.Stalls.Single(item => item.SlotIndex == 0);
        var occupancy = Assert.Single(market.Occupancies);
        var worldTick = state.Society.Society.WorldTick;
        var proposedTick = damage == "far-future-offer" ? long.MaxValue - 5 : worldTick;
        var trade = new MarketTradeState("market-trade:forged", occupancy.Id, stall.BuildingId, Founder3, "household:camp-beta",
            "household:camp-beta", Founder1, PaidMarketWorld.StallTile(state, 0), proposedTick, "cloth", "wood");
        var offer = new DirectBarterOffer(trade.OfferId, 1, "household:camp-beta", Founder1, "goods-lot", 1, "payment-lot", 1,
            proposedTick + 1, DirectBarterState.Open, [Founder1]);
        var inventory = state.Society.Society.Inventory;
        var receipts = market.StockReceipts;
        if (damage == "duplicate-trade")
        {
            var receiptId = MarketTradeRules.ReceiptId(occupancy.Id, "stall-source", "household:camp-beta", 2, worldTick, 0, trade.OfferId);
            var eventId = inventory.EventHistoryFloor + inventory.Events.Count + 1;
            inventory = inventory with
            {
                Events = inventory.Events.Append(new InventoryEvent(eventId, worldTick, "inventory_relocated",
                    receiptId + ":deposit:household:camp-beta:stall-source:2")).ToArray(),
            };
            receipts = [new MarketStockReceipt(receiptId, occupancy.Id, Founder3, "household:camp-beta", "stall-source", "stall-source",
                "wood", 2, worldTick, eventId, trade.OfferId)];
        }
        inventory = inventory with { Offers = damage == "duplicate-offer" ? [offer, offer] : [offer] };
        var forged = PaidMarketWorld.WithMarket(state, item => item with
        {
            Trades = damage == "duplicate-trade" ? [trade, trade] : [trade],
            StockReceipts = receipts,
        });
        Assert.Throws<InvalidDataException>(() => MarketTradeValidation.Validate(forged.Towns!, forged.Society.Society with { Inventory = inventory },
            forged.Map, forged.WorldSimulation!, forged.WorldContent!, damage == "far-future-offer" ? long.MaxValue : worldTick, forged.Inhabitants));
    }

    [Fact]
    public async Task ADuplicatedStallBuildingIsRefusedAsInvalidData()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = state.WorldSimulation!.Buildings.First(building => building.DefinitionId == MarketContent.Stall1x1().CanonicalId);
        var forged = state with { WorldSimulation = state.WorldSimulation with { Buildings = state.WorldSimulation.Buildings.Append(stall).ToArray() } };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
    }

    [Theory]
    [InlineData("both-borrowed", 1)]
    [InlineData("one-borrowed", 0)]
    [InlineData("stall-project-approved", 0)]
    [InlineData("no-standing-stall", 0)]
    public async Task AnExtraStallIsProposedOnlyWhileEveryStandingStallIsBorrowedAndNoneIsUnderway(string situation, int expectedSlots)
    {
        var state = await PaidMarketWorld.StateAsync();
        var slot1 = PaidMarketWorld.StallTile(state, 1);
        if (situation != "no-standing-stall") state = PaidMarketWorld.Borrowing(state, Founder3, 0);
        if (situation is "both-borrowed" or "stall-project-approved") state = PaidMarketWorld.Borrowing(state, Founder4, 4);
        if (situation == "stall-project-approved") state = PaidMarketWorld.WithApprovedStallProject(state, 1, out _);
        // This rule checks whether another stall is needed. Put the proposer at
        // the known vacant slot so real construction's final walking positions
        // do not turn this into a separate route-availability test.
        state = PaidMarketWorld.At(state, Founder1, slot1);
        var policy = new MarketRulesPolicy();
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        if (situation == "no-standing-stall")
            foreach (var stall in PaidMarketWorld.Market(world).Stalls)
                Assert.True(world.RemoveBuilding(stall.BuildingId, "town:first", null).Applied);
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var offered = policy.OfferedTo(Founder1).Select(candidate => candidate.Id)
            .Where(id => id.StartsWith(StallProposalPrefix, StringComparison.Ordinal)).Distinct().ToArray();
        Assert.Equal(expectedSlots, offered.Length);
        if (expectedSlots == 1) Assert.Equal(StallProposalPrefix + slot1.X + "," + slot1.Y, offered[0]);
    }
}
