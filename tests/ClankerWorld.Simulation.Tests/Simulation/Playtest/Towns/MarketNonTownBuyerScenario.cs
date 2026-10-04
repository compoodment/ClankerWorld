using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// A separate continuation from actual paid buildings, borrowing and deposited stock.
/// Controlled personal payment and clothing are test inputs, not extraction proof.
/// </summary>
internal static class MarketNonTownBuyerScenario
{
    private const string Buyer = "agent:00000000000000000000000000000875";
    private const string Payment = "000-market-visitor-payment-stone";
    private const string Cloak = "zzz-market-visitor-cloak";
    private const string BuyerName = "Market visitor 875";

    internal static async Task AssertAsync(PrivateWorldRuntime stockedWorld, string seller, string stallId)
    {
        var boundary = stockedWorld.ExportState();
        var market = boundary.Towns!.SelectMany(town => town.Markets).Single(item =>
            item.Stalls.Any(stall => stall.BuildingId == stallId));
        var owningTown = boundary.Towns!.Single(item => item.Markets.Any(candidate => candidate.Id == market.Id));
        Assert.Equal("completed", owningTown.Projects.Single(project => project.Id == market.ProjectId).Stage);
        var selectedStall = market.Stalls.Single(item => item.BuildingId == stallId);
        var site = MarketContent.StallSite(market.Site, selectedStall.SlotIndex);
        var occupancy = market.Occupancies.Single(item => item.StallBuildingId == stallId && item.EndedTick is null);
        Assert.Equal(seller, occupancy.SellerAgentId);
        Assert.NotNull(occupancy.SellerHouseholdId);
        Assert.Empty(market.Trades);
        Assert.DoesNotContain(boundary.Society.Society.Inhabitants, person => person.Id == Buyer);
        var stockReceipt = market.StockReceipts.Single(item => item.OccupancyId == occupancy.Id && item.ItemKind == "wooden_axe");
        var goodsBefore = boundary.Society.Society.Inventory.GetLot(stockReceipt.LotId);
        Assert.Equal(4, goodsBefore.Quantity);
        Assert.True(MarketTradeRules.IsAt(goodsBefore, site));

        var start = OutsidePlacement(boundary, site);
        var household = "household:" + Buyer;
        var policy = new Policy(seller, stallId);
        PrivateWorldRuntimeState placed;
        using (var adding = PrivateWorldRuntime.Restore(boundary, policy.ProviderFor))
        {
            adding.ValidateAgentPlacement(Buyer, start, household, expectedTownId: null);
            Assert.Equal(household, adding.AddAgent(Buyer, start, household, expectedTownId: null));
            placed = adding.ExportState();
            AssertOutsideMembership(adding, household);
            Assert.Equal(start, adding.Inhabitants.Single(person => person.InhabitantId == Buyer).Position);
            Assert.Contains(placed.Events, item => item.Kind == "agent_added" && item.Detail == Buyer);
            Assert.Contains(placed.Events, item => item.Kind == "town_membership_evaluated" && item.Detail == Buyer + ":unaffiliated");
        }
        var inventory = InventoryFixture.AddLot(placed.Society.Society.Inventory, Payment, "stone", Buyer, 1);
        inventory = InventoryFixture.AddLot(inventory, Cloak, "rain_cloak", Buyer, 1);
        var prepared = placed with
        {
            Society = placed.Society with { Society = placed.Society.Society with { Inventory = inventory } },
        };
        // Preserve the public placement, its pending identity and all existing physical/civic records.
        var setupBytes = PrivateWorldRuntimeCodec.Encode(prepared);
        using var walking = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(setupBytes), policy.ProviderFor);
        if (walking.Society.IsPaused) walking.Resume();
        var visited = new HashSet<GridPoint> { start };
        for (var step = 0; step < 80 && walking.Inhabitants.Single(person => person.InhabitantId == Buyer).IdentityChoicePending; step++)
        {
            Assert.True((await walking.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(10);
            visited.Add(walking.Inhabitants.Single(person => person.InhabitantId == Buyer).Position);
            AssertOutsideMembership(walking, household);
        }
        Assert.False(walking.Inhabitants.Single(person => person.InhabitantId == Buyer).IdentityChoicePending);
        var identityBytes = PrivateWorldRuntimeCodec.Encode(walking.ExportState());
        using var trading = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(identityBytes), policy.ProviderFor);
        Assert.Equal(identityBytes, PrivateWorldRuntimeCodec.Encode(trading.ExportState()));
        await AssertTradeAsync(trading, policy, visited, household, market, occupancy, stockReceipt, goodsBefore, seller, site);
    }

    private static async Task AssertTradeAsync(PrivateWorldRuntime walking, Policy policy, HashSet<GridPoint> visited,
        string household, TownMarketState market, MarketStallOccupancy occupancy, MarketStockReceipt stockReceipt,
        InventoryLot goodsBefore, string seller, GridPoint site)
    {
        for (var step = 0; step < 400 && !CurrentMarket(walking, market.Id).Trades.Any(item => item.BuyerId == Buyer); step++)
        {
            await Step(walking, visited);
            AssertOutsideMembership(walking, household);
            var visitor = walking.Society.GetInhabitant(Buyer);
            Assert.Equal(SocietyInhabitantStatus.Active, visitor.Status);
            Assert.True(visitor.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
        }
        var offeredMarket = CurrentMarket(walking, market.Id);
        Assert.True(offeredMarket.Trades.Any(item => item.BuyerId == Buyer),
            "Outside-Town buyer made no offer; selected: " + JsonSerializer.Serialize(policy.Selected.TakeLast(24)
                .Select(item => new { item.Actor, item.Id })) +
            "; physical: " + JsonSerializer.Serialize(walking.Inhabitants.Where(person => person.InhabitantId == Buyer ||
                person.InhabitantId == seller).Select(person => new
                {
                    person.InhabitantId,
                    person.Position,
                    person.HungerBasisPoints,
                    person.Survival,
                    person.IdentityChoicePending,
                    person.Equipment,
                    person.Project,
                })) + "; occupancy: " + JsonSerializer.Serialize(offeredMarket.Occupancies) +
            "; offered: " + JsonSerializer.Serialize(policy.Offered.TakeLast(16)) + "; intentions: " +
            JsonSerializer.Serialize(walking.ExportState().Society.Cognition.Runtimes.Where(runtime =>
                runtime.InhabitantId == Buyer || runtime.InhabitantId == seller).Select(runtime => new
                {
                    runtime.InhabitantId,
                    runtime.CurrentIntention,
                })));
        var trade = Assert.Single(offeredMarket.Trades, item => item.BuyerId == Buyer);
        var offer = walking.Society.Inventory.GetOffer(trade.OfferId);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.Equal(Buyer, trade.BuyerId);
        Assert.Equal(seller, trade.SellerAgentId);
        Assert.Equal(stockReceipt.LotId, offer.FirstLotId);
        Assert.Equal(Payment, offer.SecondLotId);
        Assert.Equal((1, 1), (offer.FirstQuantity, offer.SecondQuantity));
        Assert.Equal(new[] { Buyer }, offer.AcceptedBy);
        Assert.True(visited.Count > 2, "The outside-Town buyer must walk through real local steps.");
        Assert.Contains(policy.Selected, item => item.Actor == Buyer && item.Id.StartsWith("market_buy:", StringComparison.Ordinal));
        Assert.False(walking.Inhabitants.Single(person => person.InhabitantId == Buyer).IdentityChoicePending);
        Assert.Equal("Curious and careful", walking.Inhabitants.Single(person => person.InhabitantId == Buyer).Personality);
        Assert.Equal("Find useful goods through fair trade", walking.Inhabitants.Single(person => person.InhabitantId == Buyer).Aspiration);
        if (policy.NameRequested) Assert.Equal(BuyerName, walking.Society.GetInhabitant(Buyer).Name);
        Assert.Equal(Buyer, walking.Society.Inventory.GetLot(Payment).OwnerId);
        Assert.True(PersonalEquipmentRules.IsCarried(walking.Society.Inventory.GetLot(Payment), Buyer));
        var claims = walking.Society.Inventory.Reservations.Where(item => item.Purpose == "barter:" + offer.Id).ToArray();
        Assert.Equal(2, claims.Length);
        Assert.All(claims, claim => Assert.Equal(InventoryReservationState.Reserved, claim.State));
        Assert.Equal(new[] { Payment, stockReceipt.LotId }.Order(StringComparer.Ordinal),
            claims.Select(claim => claim.LotId).Order(StringComparer.Ordinal));
        Assert.All(claims, claim => Assert.Equal(1, claim.Quantity));
        Assert.Equal(goodsBefore.OwnerId, walking.Society.Inventory.GetLot(offer.FirstLotId).OwnerId);

        var openBytes = PrivateWorldRuntimeCodec.Encode(walking.ExportState());
        Assert.Equal(openBytes, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(openBytes)));
        policy.Accept = true;
        var twinPolicy = new Policy(seller, policy.StallId) { Accept = true };
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(openBytes), twinPolicy.ProviderFor);
        Assert.Equal(openBytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        // Fresh normal scheduling after an actual offer; no context, epoch or actor reset.
        for (var step = 0; step < 110 && walking.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Open; step++)
        {
            await Step(walking, visited);
            await Step(replay, null);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(walking.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(DirectBarterState.Settled, walking.Society.Inventory.GetOffer(offer.Id).State);
        Assert.Contains(policy.Selected, item => item.Actor == seller && item.Id.StartsWith("market_continue:", StringComparison.Ordinal));
        var purchased = Assert.Single(walking.Society.Inventory.Lots, lot =>
            lot.OwnerId == Buyer && lot.ItemKind == "wooden_axe" && lot.ProvenanceLotId == stockReceipt.LotId);
        Assert.Equal(1, purchased.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, Buyer));
        Assert.Null(purchased.StorageBuildingId);
        Assert.Null(purchased.GroundPosition);
        var remaining = walking.Society.Inventory.GetLot(stockReceipt.LotId);
        Assert.Equal(goodsBefore.OwnerId, remaining.OwnerId);
        Assert.Equal(3, remaining.Quantity);
        Assert.Equal(goodsBefore.GroundPosition, remaining.GroundPosition);
        Assert.Equal(4, remaining.Quantity + purchased.Quantity);
        // Whole-unit payment retains its original ID, rather than requiring a split.
        var paid = walking.Society.Inventory.GetLot(Payment);
        Assert.Equal(occupancy.SellerHouseholdId, paid.OwnerId);
        Assert.Equal("stone", paid.ItemKind);
        Assert.Equal(1, paid.Quantity);
        Assert.Null(paid.CarrierId);
        Assert.Null(paid.StorageBuildingId);
        Assert.Equal(new InventoryGroundPosition(site.X, site.Y), paid.GroundPosition);
        var paymentReceipt = Assert.Single(CurrentMarket(walking, market.Id).StockReceipts, item => item.TradeOfferId == offer.Id);
        Assert.Equal(Payment, paymentReceipt.LotId);
        Assert.Equal(occupancy.SellerHouseholdId, paymentReceipt.OwnerId);
        Assert.Equal(1, paymentReceipt.Quantity);
        AssertOutsideMembership(walking, household);
        AssertOutsideMembership(replay, household);
        Assert.All(walking.Society.Inventory.Reservations.Where(item => item.Purpose == "barter:" + offer.Id),
            claim => Assert.Equal(InventoryReservationState.Completed, claim.State));
        walking.Validate();
        var settledBytes = PrivateWorldRuntimeCodec.Encode(walking.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(settledBytes), twinPolicy.ProviderFor);
        Assert.Equal(settledBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static TownMarketState CurrentMarket(PrivateWorldRuntime world, string marketId) =>
        world.Towns.SelectMany(town => town.Markets).Single(market => market.Id == marketId);

    private static void AssertOutsideMembership(PrivateWorldRuntime world, string household)
    {
        Assert.Equal(household, world.Society.GetInhabitant(Buyer).HouseholdId);
        Assert.DoesNotContain(world.Towns, town => town.ResidentIds.Contains(Buyer));
        Assert.DoesNotContain(world.Towns, town => town.Governance!.Members.Contains(Buyer));
    }

    private static GridPoint OutsidePlacement(PrivateWorldRuntimeState state, GridPoint stall)
    {
        var definitions = state.WorldContent!.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var footprints = state.WorldSimulation!.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building)).ToHashSet();
        var candidates = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsBuildable(point) &&
            !footprints.Contains(point) && !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) && !state.Inhabitants.Any(person => person.Position == point) &&
            !state.Fields!.Any(field => field.Position == point) &&
            !state.Towns!.Any(town => town.BorderTiles.Contains(point)) &&
            !state.TownLandTitles!.Any(title => title.Tiles.Contains(point)) &&
            !state.HouseholdLandUseRights!.Any(right => right.Tiles.Contains(point)) &&
            !state.HouseholdLandUseRequests!.Any(request => request.Tiles.Contains(point)) &&
            state.Map.FootDistance(point, stall) is >= 4 and <= 24 && state.Map.IsReachableOnFoot(point, stall))
            .OrderBy(point => state.Map.FootDistance(point, stall)).ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
        Assert.NotEmpty(candidates);
        return candidates[0];
    }

    private static async Task Step(PrivateWorldRuntime world, HashSet<GridPoint>? visited)
    {
        var before = world.Inhabitants.Single(person => person.InhabitantId == Buyer).Position;
        // Identity was admitted through actual hosted ticks above. Synchronous
        // admission keeps subsequent replay independent of background task timing.
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var after = world.Inhabitants.Single(person => person.InhabitantId == Buyer).Position;
        if (before != after) Assert.True(world.ExportState().Map.CanFootStep(before, after));
        visited?.Add(after);
    }

    private sealed class Policy(string seller, string stallId)
    {
        internal string Seller { get; } = seller;
        internal string StallId { get; } = stallId;
        internal bool Accept { get; set; }
        internal bool NameRequested { get; private set; }
        internal ConcurrentQueue<(string Actor, string Id)> Selected { get; } = new();
        internal ConcurrentQueue<string> Offered { get; } = new();
        internal IDecisionProvider ProviderFor(string actor) => new Provider(this, actor);

        private sealed class Provider(Policy policy, string actor) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;

            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var observation = request.Observation;
                var candidates = observation.Candidates;
                if (actor == Buyer)
                    policy.Offered.Enqueue(JsonSerializer.Serialize(new
                    {
                        observation.WorldTick,
                        observation.NeedsName,
                        observation.NeedsPersonality,
                        observation.NeedsAspiration,
                        Candidates = candidates.Select(candidate => candidate.Id).ToArray(),
                    }));
                CognitionCandidate? selected = null;
                if (actor == Buyer && !observation.NeedsPersonality && !observation.NeedsAspiration)
                    selected = candidates.FirstOrDefault(candidate => candidate.Id == "wear_clothing") ??
                        candidates.FirstOrDefault(candidate => candidate.DestinationId == policy.StallId &&
                            candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal));
                if (actor == policy.Seller && policy.Accept)
                    selected = candidates.FirstOrDefault(candidate => candidate.DestinationId == policy.StallId &&
                        candidate.Id.StartsWith("market_continue:", StringComparison.Ordinal));
                selected ??= candidates.Where(candidate => candidate.DeterministicPriority <= 5 && candidate.Id is
                    "consume_food" or "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth")
                    .OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault();
                selected ??= candidates.Single(candidate => candidate.Id == "safe_idle");
                policy.Selected.Enqueue((actor, selected.Id));
                if (actor == Buyer && observation.NeedsName) policy.NameRequested = true;
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, request.ProviderEpoch,
                    observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                    candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                    ChosenName: actor == Buyer && observation.NeedsName ? BuyerName : null,
                    ChosenPersonality: actor == Buyer && observation.NeedsPersonality ? "Curious and careful" : null,
                    ChosenAspiration: actor == Buyer && observation.NeedsAspiration ? "Find useful goods through fair trade" : null));
            }
        }
    }
}
