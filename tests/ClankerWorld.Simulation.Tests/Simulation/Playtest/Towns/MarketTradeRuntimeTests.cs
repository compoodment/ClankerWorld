using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Invoked after the construction test reaches a genuine paid Market. This does not
/// invent Council approvals, land titles or placed buildings to make a trading test pass.
/// </summary>
internal static class MarketTradeScenario
{
    internal static async Task AssertPipelineAsync(PrivateWorldRuntime paidWorld)
    {
        var boundary = paidWorld.ExportState();
        var market = Assert.Single(Assert.Single(boundary.Towns!).Markets);
        Assert.Equal("completed", Assert.Single(boundary.Towns![0].Projects, project => project.Id == market.ProjectId).Stage);
        Assert.Equal(2, market.Stalls.Count);
        Assert.Empty(market.Occupancies);
        Assert.Empty(market.StockReceipts);
        Assert.Empty(market.Trades);
        paidWorld.Validate();
        foreach (var personalStock in new[] { false, true })
            await AssertPhysicalExchangeAsync(boundary, personalStock);
    }

    private static async Task AssertPhysicalExchangeAsync(PrivateWorldRuntimeState boundary, bool personalStock)
    {
        var society = boundary.Society.Society;
        var seller = society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId is not null && person.AgeBand == SocietyAgeBand.Adult).Id;
        var household = society.GetInhabitant(seller).HouseholdId!;
        var successor = society.GetHousehold(household).MemberIds.First(id => id != seller);
        var buyer = society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId != household && person.AgeBand == SocietyAgeBand.Adult).Id;
        var market = Assert.Single(boundary.Towns![0].Markets);
        var stall = market.Stalls.OrderBy(item => item.SlotIndex).First();
        var stockOwner = personalStock ? seller : household;
        var stockId = personalStock ? "market-personal-spare-axes" : "market-household-axes";
        var sourceHouse = boundary.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            boundary.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = InventoryFixture.AddLot(society.Inventory, stockId, "wooden_axe", stockOwner, 4,
            storageBuildingId: personalStock ? null : sourceHouse.InstanceId);
        // A stronger personal tool is protected while four real spare axes may be offered.
        inventory = InventoryFixture.AddLot(inventory, "market-seller-best-axe", "iron_axe", seller, 1);
        inventory = InventoryFixture.AddLot(inventory, "market-buyer-payment", "wood", buyer, 4);
        inventory = InventoryFixture.AddLot(inventory, "market-successor-owned-stock", "cloth", successor, 1);
        foreach (var actor in new[] { seller, buyer, successor })
        {
            inventory = InventoryFixture.AddLot(inventory, "market-test-cloak:" + actor, "rain_cloak", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "market-test-sack:" + actor, "sack", actor, 1);
        }
        var state = boundary with
        {
            Society = boundary.Society with { Society = society with { Inventory = inventory } },
            Inhabitants = boundary.Inhabitants.Select(person => new[] { seller, buyer, successor }.Contains(person.InhabitantId)
                ? person with
                {
                    HungerBasisPoints = 9_500,
                    Survival = new SurvivalCondition(),
                    Project = null,
                    LastDecisionContext = null,
                    Equipment = new PersonalEquipment("market-test-cloak:" + person.InhabitantId, "market-test-sack:" + person.InhabitantId),
                }
                : person).ToArray(),
        };
        var policy = new Policy(seller, buyer, successor, stall.BuildingId) { Mode = "stock" };
        using var stocking = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        await UntilAsync(stocking, () => stocking.Towns[0].Markets[0].StockReceipts.Any(receipt => receipt.SourceLotId == stockId ||
            receipt.SourceLotId.StartsWith(stockId + "#", StringComparison.Ordinal)), policy);
        var depositedMarket = stocking.Towns[0].Markets[0];
        var occupancy = Assert.Single(depositedMarket.Occupancies);
        var deposit = Assert.Single(depositedMarket.StockReceipts);
        Assert.Equal(seller, occupancy.SellerAgentId);
        Assert.Equal(household, occupancy.SellerHouseholdId);
        Assert.Equal(stall.BuildingId, occupancy.StallBuildingId);
        Assert.Equal(stockOwner, deposit.OwnerId);
        Assert.Equal(4, deposit.Quantity);
        var deposited = stocking.Society.Inventory.GetLot(deposit.LotId);
        Assert.Equal(stockOwner, deposited.OwnerId);
        Assert.Equal(4, deposited.Quantity);
        Assert.Null(deposited.CarrierId);
        Assert.Null(deposited.StorageBuildingId);
        Assert.Equal(new InventoryGroundPosition(MarketContent.StallSite(market.Site, stall.SlotIndex).X,
            MarketContent.StallSite(market.Site, stall.SlotIndex).Y), deposited.GroundPosition);
        Assert.Equal(4, StockFamilyQuantity(stocking.Society.Inventory, stockId));
        Assert.Equal(seller, stocking.Society.Inventory.GetLot("market-seller-best-axe").OwnerId);
        MarketObservationTests.AssertProjection(stocking);
        AssertDepositTamperRefused(stocking.ExportState(), deposit, personalStock ? household : seller);
        policy.Mode = "offer";
        await UntilAsync(stocking, () => stocking.Towns[0].Markets[0].Trades.Count == 1, policy);
        var offeredMarket = stocking.Towns[0].Markets[0];
        var trade = Assert.Single(offeredMarket.Trades);
        var offer = stocking.Society.Inventory.GetOffer(trade.OfferId);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.Equal(new[] { buyer }, offer.AcceptedBy);
        Assert.Equal(stockOwner, trade.GoodsOwnerId);
        Assert.Equal(household, trade.PaymentOwnerId);
        Assert.Equal(deposit.LotId, offer.FirstLotId);
        Assert.Equal("market-buyer-payment", offer.SecondLotId);
        Assert.Equal(1, offer.FirstQuantity);
        Assert.Equal(1, offer.SecondQuantity);
        Assert.Equal(3, MarketTradeRules.AvailableQuantity(stocking.Society.Inventory, stocking.Society.Inventory.GetLot(deposit.LotId)));
        Assert.All(stocking.Society.Inventory.Reservations.Where(claim => claim.Purpose == "barter:" + offer.Id),
            claim => Assert.Equal(InventoryReservationState.Reserved, claim.State));
        MarketObservationTests.AssertProjection(stocking);
        var pending = stocking.ExportState();
        var bytes = PrivateWorldRuntimeCodec.Encode(pending);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(bytes)));
        var forged = pending with
        {
            Towns = pending.Towns!.Select(town => town with
            {
                Markets = town.Markets.Select(item => item with
                {
                    Occupancies = item.Occupancies.Append(occupancy with { Id = occupancy.Id + ":forged" }).ToArray(),
                }).ToArray(),
            }).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forged));
        var woodBefore = Total(stocking.Society.Inventory, "wood");
        var axesBefore = Total(stocking.Society.Inventory, "wooden_axe");
        policy.Mode = "accept";
        using var settling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        await UntilAsync(settling, () => settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Settled, policy);
        var purchased = settling.Society.Inventory.Lots.Single(lot => lot.ProvenanceLotId == deposit.LotId &&
            lot.OwnerId == buyer && lot.ItemKind == "wooden_axe");
        Assert.Equal(1, purchased.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, buyer));
        Assert.Null(purchased.GroundPosition);
        var paymentReceipt = settling.Towns[0].Markets[0].StockReceipts.Single(receipt => receipt.TradeOfferId == offer.Id);
        var payment = settling.Society.Inventory.GetLot(paymentReceipt.LotId);
        Assert.Equal(household, payment.OwnerId);
        Assert.Equal(1, payment.Quantity);
        Assert.Null(payment.CarrierId);
        Assert.Equal(deposited.GroundPosition, payment.GroundPosition);
        Assert.Equal(3, settling.Society.Inventory.GetLot("market-buyer-payment").Quantity);
        Assert.Equal(woodBefore, Total(settling.Society.Inventory, "wood"));
        Assert.Equal(axesBefore, Total(settling.Society.Inventory, "wooden_axe"));
        MarketObservationTests.AssertProjection(settling);
        settling.Validate();
        // A separate continuation from the same genuine open offer proves departure,
        // released reservations, and a same-household successor's lack of old-stock authority.
        policy.Mode = "leave";
        using var leaving = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        await UntilAsync(leaving, () => leaving.Towns[0].Markets[0].Occupancies[0].EndedTick is not null, policy);
        Assert.Equal(DirectBarterState.Cancelled, leaving.Society.Inventory.GetOffer(offer.Id).State);
        Assert.All(leaving.Society.Inventory.Reservations.Where(claim => claim.Purpose == "barter:" + offer.Id),
            claim => Assert.Equal(InventoryReservationState.Released, claim.State));
        Assert.Equal(4, leaving.Society.Inventory.GetLot(deposit.LotId).Quantity);
        Assert.Equal(stockOwner, leaving.Society.Inventory.GetLot(deposit.LotId).OwnerId);
        MarketObservationTests.AssertProjection(leaving);
        policy.Mode = "successor";
        await UntilAsync(leaving, () => leaving.Towns[0].Markets[0].Occupancies.Any(item => item.SellerAgentId == successor &&
            item.EndedTick is null), policy);
        var newMarket = leaving.Towns[0].Markets[0];
        var newOccupancy = newMarket.Occupancies.Single(item => item.SellerAgentId == successor && item.EndedTick is null);
        Assert.Equal(stall.BuildingId, newOccupancy.StallBuildingId);
        Assert.False(MarketTradeRules.MaySell(newMarket, newOccupancy, leaving.Society.Inventory.GetLot(deposit.LotId),
            leaving.Society, successor));
        MarketObservationTests.AssertProjection(leaving);
        policy.Mode = "blocked-buy";
        for (var tick = 0; tick < 8; tick++) Assert.True((await leaving.AdvanceOneTickAsync()).Advanced);
        Assert.Single(leaving.Towns[0].Markets[0].Trades);
        Assert.DoesNotContain(policy.Seen.Where(item => item.Actor == buyer && item.Tick > newOccupancy.StartedTick),
            item => item.Id.StartsWith("market_buy:", StringComparison.Ordinal));
        policy.Mode = "retrieve";
        var retriever = personalStock ? seller : successor;
        var carriedBefore = leaving.Society.Inventory.Lots.Where(lot => lot.OwnerId == stockOwner &&
            PersonalEquipmentRules.IsCarried(lot, retriever) && lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity);
        await UntilAsync(leaving, () => leaving.Society.Inventory.Lots.Where(lot => lot.OwnerId == stockOwner &&
            PersonalEquipmentRules.IsCarried(lot, retriever) && lot.ItemKind == "wooden_axe").Sum(lot => lot.Quantity) > carriedBefore, policy);
        Assert.Equal(4, StockFamilyQuantity(leaving.Society.Inventory, stockId));
        Assert.All(leaving.Society.Inventory.Lots.Where(lot => lot.Id == stockId || lot.Id.StartsWith(stockId + "#", StringComparison.Ordinal)),
            lot => Assert.Equal(stockOwner, lot.OwnerId));
        MarketObservationTests.AssertProjection(leaving);
        leaving.Validate();
    }

    private static void AssertDepositTamperRefused(PrivateWorldRuntimeState state, MarketStockReceipt receipt, string falseOwner)
    {
        var market = state.Towns![0].Markets[0];
        var changed = receipt with
        {
            OwnerId = falseOwner,
            Id = MarketTradeRules.ReceiptId(receipt.OccupancyId,
            receipt.SourceLotId, falseOwner, receipt.Quantity, receipt.DepositedTick, Array.IndexOf(market.StockReceipts.ToArray(), receipt))
        };
        var damaged = state with
        {
            Towns = state.Towns.Select(town => town with
            {
                Markets = town.Markets.Select(item => item with
                {
                    StockReceipts = item.StockReceipts.Select(entry => entry.Id == receipt.Id ? changed : entry).ToArray(),
                }).ToArray()
            }).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == receipt.LotId ? lot with { OwnerId = falseOwner } : lot).ToArray(),
                    },
                }
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(damaged));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(damaged)));
    }

    private static async Task UntilAsync(PrivateWorldRuntime world, Func<bool> reached, Policy policy)
    {
        for (var tick = 0; tick < 160 && !reached(); tick++)
        {
            var before = world.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var state = world.ExportState();
            foreach (var person in state.Inhabitants)
            {
                if (before[person.InhabitantId] != person.Position)
                    Assert.True(state.Map.CanFootStep(before[person.InhabitantId], person.Position));
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(state.Society.Society.Inventory, person.InhabitantId, person.Equipment),
                    0, PersonalEquipmentRules.Capacity(state.Society.Society.Inventory, person.InhabitantId, person.Equipment));
            }
        }
        Assert.True(reached(), $"Market phase {policy.Mode} not reached at tick {world.WorldTick}; choices: " +
            string.Join(", ", policy.Seen.TakeLast(12).Select(item => item.Id)));
    }

    private static int Total(InventoryCheckpoint inventory, string kind) => inventory.Lots.Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static int StockFamilyQuantity(InventoryCheckpoint inventory, string id) =>
        inventory.Lots.Where(lot => lot.Id == id || lot.Id.StartsWith(id + "#", StringComparison.Ordinal)).Sum(lot => lot.Quantity);

    private sealed class Policy(string seller, string buyer, string successor, string stallId)
    {
        internal string Seller { get; } = seller;
        internal string Buyer { get; } = buyer;
        internal string Successor { get; } = successor;
        internal string StallId { get; } = stallId;
        internal string Mode { get; set; } = "stock";
        internal ConcurrentQueue<(string Actor, string Id, long Tick)> Seen { get; } = new();
        internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor);
        private sealed class Provider(Policy policy, string actor) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var candidates = request.Observation.Candidates;
                foreach (var candidate in candidates) policy.Seen.Enqueue((actor, candidate.Id, request.Observation.WorldTick));
                var local = candidates.Where(candidate => candidate.DestinationId == policy.StallId).ToArray();
                CognitionCandidate? selected = policy.Mode switch
                {
                    "stock" when actor == policy.Seller => local.FirstOrDefault(candidate =>
                        candidate.Id.StartsWith("market_load:", StringComparison.Ordinal) && candidate.Description.Contains("wooden_axe", StringComparison.Ordinal)) ??
                        local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                        local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                            candidate.Description.Contains("wooden_axe", StringComparison.Ordinal)),
                    "offer" or "blocked-buy" when actor == policy.Buyer => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal)),
                    "accept" when actor == policy.Seller => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_continue:", StringComparison.Ordinal)),
                    "leave" when actor == policy.Seller => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_leave:", StringComparison.Ordinal)),
                    "successor" when actor == policy.Successor => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)),
                    "retrieve" when actor == policy.Seller || actor == policy.Successor => local.FirstOrDefault(candidate =>
                        candidate.Id.StartsWith("market_collect:", StringComparison.Ordinal) && candidate.Description.Contains("wooden_axe", StringComparison.Ordinal)),
                    _ => null,
                };
                selected ??= candidates.FirstOrDefault(candidate => candidate.DeterministicPriority <= 5 &&
                    candidate.Id is "consume_food" or "collect_shared_food" or "take_food_from_pot" or "make_room_for_food" or
                        "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth");
                selected ??= candidates.Single(candidate => candidate.Id == "safe_idle");
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    selected.Id, 1, candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
            }
        }
    }
}
