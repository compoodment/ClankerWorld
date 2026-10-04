using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>A controlled paused-recipe continuation after genuine Council-paid Market construction.</summary>
internal static class MarketPausedPlanTradeScenario
{
    internal static async Task AssertPausedBandageBuyerTradesWithoutBorrowingAlphaStockAsync(PrivateWorldRuntimeState paidBoundary)
    {
        const string alpha = "household:camp-alpha";
        const string beta = "household:camp-beta";
        var society = paidBoundary.Society.Society;
        var seller = society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId == alpha && person.AgeBand == SocietyAgeBand.Adult).Id;
        var buyer = society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId == beta && person.AgeBand == SocietyAgeBand.Adult).Id;
        var market = Assert.Single(paidBoundary.Towns![0].Markets);
        Assert.Equal("completed", Assert.Single(paidBoundary.Towns[0].Projects, project => project.Id == market.ProjectId).Stage);
        Assert.Empty(market.Occupancies);
        Assert.Empty(market.StockReceipts);
        Assert.Empty(market.Trades);
        var stall = market.Stalls.OrderBy(item => item.SlotIndex).First();
        var recipe = Assert.Single(paidBoundary.WorldContent!.Recipes, item => item.LocalId == "house-bandages");
        Assert.Equal("cloth", Assert.Single(recipe.Inputs).ResourceId);
        Assert.Equal(1, Assert.Single(recipe.Inputs).Amount);
        var alphaHouse = Assert.Single(paidBoundary.WorldSimulation!.Buildings, building => building.HouseholdId == alpha &&
            building.DefinitionId == recipe.WorkstationBuildingId);
        var betaHouse = Assert.Single(paidBoundary.WorldSimulation.Buildings, building => building.HouseholdId == beta &&
            building.DefinitionId == recipe.WorkstationBuildingId);
        Assert.NotEqual(alphaHouse.InstanceId, betaHouse.InstanceId);
        Assert.DoesNotContain(society.Inventory.Lots, lot => lot.ItemKind == "cloth" && (lot.OwnerId == buyer || lot.OwnerId == beta));

        // Near-event arrangement only: the recipe and both Houses are real, while
        // this valid paused/no-job plan and sale/payment/equipment stock are controlled.
        // The existing expired-project tests prove emergence of the pause itself.
        var paused = new SettlementProject("build:recipe:" + recipe.CanonicalId, recipe.DisplayName,
            society.WorldTick, "paused", 4, "Materials for this work are unavailable. Choose another task for now.",
            LastTransitionTick: society.WorldTick, RequiresFreshChoice: true);
        var inventory = InventoryFixture.AddLot(society.Inventory, "paused-plan-alpha-cloth", "cloth", alpha, 1,
            storageBuildingId: alphaHouse.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "paused-plan-sale-cloth", "cloth", seller, 4);
        inventory = InventoryFixture.AddLot(inventory, "paused-plan-payment-option", "wood", buyer, 4);
        foreach (var actor in new[] { seller, buyer })
        {
            inventory = InventoryFixture.AddLot(inventory, "paused-plan-cloak:" + actor, "rain_cloak", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "paused-plan-sack:" + actor, "sack", actor, 1);
        }
        var initial = paidBoundary with
        {
            Society = paidBoundary.Society with { Society = society with { Inventory = inventory } },
            Inhabitants = paidBoundary.Inhabitants.Select(person => person.InhabitantId == seller || person.InhabitantId == buyer
                ? person with
                {
                    HungerBasisPoints = 9_500,
                    Survival = new SurvivalCondition(),
                    LastDecisionContext = null,
                    Project = person.InhabitantId == buyer ? paused : person.Project,
                    Equipment = (person.Equipment ?? new PersonalEquipment()) with
                    {
                        ClothingLotId = "paused-plan-cloak:" + person.InhabitantId,
                        CarryAidLotId = "paused-plan-sack:" + person.InhabitantId,
                    },
                }
                : person).ToArray(),
        };
        var policy = new Policy(seller, buyer, stall.BuildingId);
        PrivateWorldRuntimeState deposited;
        MarketStockReceipt receipt;
        using (var stocking = PrivateWorldRuntime.Restore(initial, policy.CreateProvider))
        {
            stocking.Validate();
            await UntilAsync(stocking, policy, paused, () => stocking.Towns[0].Markets[0].StockReceipts.Any(item =>
                item.SourceLotId == "paused-plan-sale-cloth"));
            receipt = Assert.Single(stocking.Towns[0].Markets[0].StockReceipts);
            Assert.Equal(seller, receipt.OwnerId);
            Assert.Equal(4, receipt.Quantity);
            Assert.Equal(alpha, stocking.Society.Inventory.GetLot("paused-plan-alpha-cloth").OwnerId);
            Assert.Equal(alphaHouse.InstanceId, stocking.Society.Inventory.GetLot("paused-plan-alpha-cloth").StorageBuildingId);
            deposited = AssertReplay(stocking, buyer, paused, recipe.CanonicalId);
        }

        policy.Mode = "offer";
        PrivateWorldRuntimeState pending;
        MarketTradeState trade;
        DirectBarterOffer offer;
        InventoryLot offeredPayment;
        int clothBefore;
        int paymentBefore;
        using (var offering = RestorePhase(deposited, policy))
        {
            await UntilAsync(offering, policy, paused, () => offering.Towns[0].Markets[0].Trades.Count == 1);
            trade = Assert.Single(offering.Towns[0].Markets[0].Trades);
            offer = offering.Society.Inventory.GetOffer(trade.OfferId);
            Assert.Equal(buyer, trade.BuyerId);
            Assert.Equal(seller, trade.GoodsOwnerId);
            Assert.Equal(alpha, trade.PaymentOwnerId);
            Assert.Equal(seller, offer.FirstPartyId);
            Assert.Equal(buyer, offer.SecondPartyId);
            Assert.Equal("cloth", trade.GoodsKind);
            Assert.Equal(receipt.LotId, offer.FirstLotId);
            Assert.Equal(1, offer.FirstQuantity);
            Assert.Equal(1, offer.SecondQuantity);
            Assert.Equal(DirectBarterState.Open, offer.State);
            Assert.Equal(new[] { buyer }, offer.AcceptedBy);
            offeredPayment = offering.Society.Inventory.GetLot(offer.SecondLotId);
            Assert.Equal(buyer, offeredPayment.OwnerId);
            Assert.Equal(trade.PaymentKind, offeredPayment.ItemKind);
            Assert.True(PersonalEquipmentRules.IsCarried(offeredPayment, buyer));
            Assert.True(MarketTradeRules.IsLoose(offeredPayment));
            Assert.False(PersonalEquipmentRules.IsSelected(offering.Inhabitants.Single(person => person.InhabitantId == buyer).Equipment,
                offeredPayment.Id));
            Assert.Equal(3, MarketTradeRules.AvailableQuantity(offering.Society.Inventory, offering.Society.Inventory.GetLot(receipt.LotId)));
            clothBefore = Quantity(offering, "cloth");
            paymentBefore = Quantity(offering, trade.PaymentKind);
            pending = AssertReplay(offering, buyer, paused, recipe.CanonicalId);
        }

        policy.Mode = "accept";
        using var settling = RestorePhase(pending, policy);
        await UntilAsync(settling, policy, paused, () => settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Settled);
        Assert.Equal(new[] { seller, buyer }.Order(StringComparer.Ordinal), settling.Society.Inventory.GetOffer(offer.Id).AcceptedBy);
        var bought = Assert.Single(settling.Society.Inventory.Lots, lot => lot.ProvenanceLotId == receipt.LotId && lot.OwnerId == buyer);
        Assert.Equal("cloth", bought.ItemKind);
        Assert.Equal(1, bought.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(bought, buyer));
        Assert.Null(bought.GroundPosition);
        Assert.Null(bought.StorageBuildingId);
        var paymentReceipt = Assert.Single(settling.Towns[0].Markets[0].StockReceipts, item => item.TradeOfferId == offer.Id);
        var payment = settling.Society.Inventory.GetLot(paymentReceipt.LotId);
        Assert.Equal(alpha, payment.OwnerId);
        Assert.Equal(trade.PaymentKind, payment.ItemKind);
        Assert.Equal(1, payment.Quantity);
        Assert.Null(payment.CarrierId);
        Assert.Null(payment.StorageBuildingId);
        Assert.Equal(new InventoryGroundPosition(trade.Position.X, trade.Position.Y), payment.GroundPosition);
        Assert.Equal(offeredPayment.Quantity - 1, settling.Society.Inventory.Lots.Where(lot =>
            lot.Id == offeredPayment.Id && lot.OwnerId == buyer).Sum(lot => lot.Quantity));
        Assert.Equal(clothBefore, Quantity(settling, "cloth"));
        Assert.Equal(paymentBefore, Quantity(settling, trade.PaymentKind));
        Assert.Equal(alphaHouse.InstanceId, settling.Society.Inventory.GetLot("paused-plan-alpha-cloth").StorageBuildingId);
        Assert.Equal(1, settling.Society.Inventory.GetLot("paused-plan-alpha-cloth").Quantity);
        _ = AssertReplay(settling, buyer, paused, recipe.CanonicalId);
    }

    private static PrivateWorldRuntime RestorePhase(PrivateWorldRuntimeState state, Policy policy) =>
        PrivateWorldRuntime.Restore(state with
        {
            // A controlled provider phase change is not a production wake event.
            // Clear only its observation context after a completed phase; never alter travel.
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == policy.Seller || person.InhabitantId == policy.Buyer
                ? person with { LastDecisionContext = null } : person).ToArray(),
        }, policy.CreateProvider);

    private static PrivateWorldRuntimeState AssertReplay(PrivateWorldRuntime world, string buyer, SettlementProject paused, string recipeId)
    {
        Assert.Equal(paused, world.Inhabitants.Single(person => person.InhabitantId == buyer).Project);
        Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, job => job.WorkerId == buyer && job.RecipeId == recipeId);
        world.Validate();
        MarketObservationTests.AssertProjection(world);
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var decoded = PrivateWorldRuntimeCodec.Decode(encoded);
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(decoded));
        using var restored = PrivateWorldRuntime.Restore(decoded);
        Assert.Equal(paused, restored.Inhabitants.Single(person => person.InhabitantId == buyer).Project);
        restored.Validate();
        return decoded;
    }

    private static int Quantity(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private static async Task UntilAsync(PrivateWorldRuntime world, Policy policy, SettlementProject paused, Func<bool> reached)
    {
        for (var step = 0; step < 160 && !reached(); step++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(paused, world.Inhabitants.Single(person => person.InhabitantId == policy.Buyer).Project);
        }
        Assert.True(reached(), $"Paused Market phase {policy.Mode} did not finish at tick {world.WorldTick}; chosen: " +
            JsonSerializer.Serialize(policy.Chosen.TakeLast(12).Select(item => new { item.Actor, item.Id, item.Tick })));
    }

    private sealed class Policy(string seller, string buyer, string stallId)
    {
        internal string Seller { get; } = seller;
        internal string Buyer { get; } = buyer;
        internal string Mode { get; set; } = "stock";
        internal ConcurrentQueue<(string Actor, string Id, long Tick)> Chosen { get; } = new();
        internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor, stallId);

        private sealed class Provider(Policy policy, string actor, string destination) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var candidates = request.Observation.Candidates;
                var local = candidates.Where(candidate => candidate.DestinationId == destination).ToArray();
                CognitionCandidate? choice = policy.Mode switch
                {
                    "stock" when actor == policy.Seller => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                        local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                            candidate.Description.Contains(" cloth at your borrowed stall;", StringComparison.Ordinal)),
                    "offer" when actor == policy.Buyer => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal) &&
                        candidate.Description.Contains(" for 1 cloth;", StringComparison.Ordinal)),
                    "accept" when actor == policy.Seller => local.FirstOrDefault(candidate => candidate.Id.StartsWith("market_continue:", StringComparison.Ordinal)),
                    _ => null,
                };
                choice ??= candidates.FirstOrDefault(candidate => candidate.DeterministicPriority <= 5 &&
                    candidate.Id is "consume_food" or "collect_shared_food" or "take_food_from_pot" or "make_room_for_food" or
                        "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth");
                choice ??= candidates.Single(candidate => candidate.Id == "safe_idle");
                policy.Chosen.Enqueue((actor, choice.Id, request.Observation.WorldTick));
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    choice.Id, 1, candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal)));
            }
        }
    }
}
