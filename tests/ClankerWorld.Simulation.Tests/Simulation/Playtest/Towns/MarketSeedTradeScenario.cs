using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Normal field work and one exact Market seed purchase after a genuine paid Market.</summary>
internal static class MarketSeedTradeScenario
{
    internal static async Task AssertOneMissingSeedIsBoughtAndPlantedWithoutOverbuyAsync(PrivateWorldRuntimeState paidBoundary)
    {
        const string alpha = "household:camp-alpha";
        var society = paidBoundary.Society.Society;
        var buyer = society.Inhabitants.First(person => person.HouseholdId == alpha && person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand == SocietyAgeBand.Adult).Id;
        var seller = society.Inhabitants.First(person => person.HouseholdId != alpha && person.HouseholdId is not null &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand == SocietyAgeBand.Adult).Id;
        var sellerHousehold = society.GetInhabitant(seller).HouseholdId!;
        var market = Assert.Single(paidBoundary.Towns![0].Markets);
        Assert.Equal("completed", Assert.Single(paidBoundary.Towns[0].Projects, project => project.Id == market.ProjectId).Stage);
        Assert.Empty(market.Occupancies);
        Assert.Empty(market.StockReceipts);
        Assert.Empty(paidBoundary.Fields!);
        var farmhouse = Assert.Single(paidBoundary.WorldSimulation!.Buildings, building => building.HouseholdId == alpha &&
            paidBoundary.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("farmhouse", StringComparer.Ordinal));
        Assert.Equal("first-town-farmhouse", farmhouse.InstanceId);
        var stall = market.Stalls.OrderBy(item => item.SlotIndex).First();

        // Controlled near-event arrangement. Actual Town food is consumed as
        // meals and existing spare seed is held by actual reservations. Nothing is
        // deleted, and no map, title, building, field or actor position is invented.
        var initial = FarmFieldTests.FeedFarmTownFromAvailableStock(paidBoundary, alpha);
        var inventory = initial.Society.Society.Inventory;
        var holds = new List<string>();
        foreach (var lot in inventory.Lots.Where(lot => lot.ItemKind == FarmFieldRules.GrainSeed &&
                     (lot.OwnerId == alpha || lot.OwnerId == buyer)).ToArray())
        {
            var available = MarketTradeRules.AvailableQuantity(inventory, lot);
            if (available <= 0) continue;
            var hold = "seed-proof-hold:" + lot.Id;
            inventory = InventoryFixture.Reserve(inventory, hold, lot.OwnerId, lot.Id, available,
                "controlled_seed_hold", long.MaxValue);
            holds.Add(hold);
        }
        Assert.NotEmpty(holds);
        inventory = InventoryFixture.AddLot(inventory, "seed-proof-buyer-hoe", FarmFieldRules.Hoe, buyer, 1);
        inventory = InventoryFixture.AddLot(inventory, "seed-proof-sale-stock", FarmFieldRules.GrainSeed, seller, 2);
        inventory = InventoryFixture.AddLot(inventory, "seed-proof-payment-option", "wood", buyer, 4);
        foreach (var actor in new[] { buyer, seller })
        {
            inventory = InventoryFixture.AddLot(inventory, "seed-proof-cloak:" + actor, "rain_cloak", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "seed-proof-sack:" + actor, "sack", actor, 1);
        }
        initial = initial with
        {
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = inventory } },
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == buyer || person.InhabitantId == seller
                ? person with
                {
                    HungerBasisPoints = 9_500,
                    Survival = new SurvivalCondition(),
                    LastDecisionContext = null,
                    Equipment = (person.Equipment ?? new PersonalEquipment()) with
                    {
                        ClothingLotId = "seed-proof-cloak:" + person.InhabitantId,
                        CarryAidLotId = "seed-proof-sack:" + person.InhabitantId,
                    },
                }
                : person).ToArray(),
        };
        var heldClaims = holds.Select(inventory.GetReservation).ToArray();
        var policy = new Policy(buyer, seller, stall.BuildingId);
        PrivateWorldRuntimeState prepared;
        FarmFieldState field;
        using (var tilling = PrivateWorldRuntime.Restore(initial, policy.CreateProvider))
        {
            await UntilAsync(tilling, policy, () => tilling.Fields.Any(item => item.HouseholdId == alpha &&
                item.Stage == FarmFieldStage.Prepared && item.Work is null));
            field = Assert.Single(tilling.Fields);
            Assert.Equal(alpha, field.HouseholdId);
            Assert.Contains(policy.Choices, item => item.Actor == buyer && item.Id.StartsWith("farm:Till:", StringComparison.Ordinal));
            Assert.Contains(tilling.ExportState().Events, item => item.Kind == "field_work_started" && item.Detail.EndsWith(":Till", StringComparison.Ordinal));
            prepared = AssertReplay(tilling);
        }

        policy.Mode = "stock";
        PrivateWorldRuntimeState deposited;
        MarketStockReceipt receipt;
        using (var stocking = RestorePhase(prepared, policy))
        {
            await UntilAsync(stocking, policy, () => stocking.Towns[0].Markets[0].StockReceipts.Any(item => item.SourceLotId == "seed-proof-sale-stock"));
            receipt = Assert.Single(stocking.Towns[0].Markets[0].StockReceipts);
            Assert.Equal(2, receipt.Quantity);
            deposited = AssertReplay(stocking);
        }

        policy.Mode = "offer";
        PrivateWorldRuntimeState pending;
        DirectBarterOffer offer;
        MarketTradeState trade;
        InventoryLot offeredPayment;
        int seedBefore;
        int paymentBefore;
        using (var offering = RestorePhase(deposited, policy))
        {
            await UntilAsync(offering, policy, () => offering.Towns[0].Markets[0].Trades.Count == 1);
            trade = Assert.Single(offering.Towns[0].Markets[0].Trades);
            offer = offering.Society.Inventory.GetOffer(trade.OfferId);
            Assert.Equal(FarmFieldRules.GrainSeed, trade.GoodsKind);
            Assert.Equal(receipt.LotId, offer.FirstLotId);
            Assert.Equal(1, offer.FirstQuantity);
            Assert.Equal(1, offer.SecondQuantity);
            Assert.Equal(new[] { buyer }, offer.AcceptedBy);
            offeredPayment = offering.Society.Inventory.GetLot(offer.SecondLotId);
            Assert.Equal(buyer, offeredPayment.OwnerId);
            Assert.True(PersonalEquipmentRules.IsCarried(offeredPayment, buyer));
            seedBefore = Quantity(offering, FarmFieldRules.GrainSeed);
            paymentBefore = Quantity(offering, trade.PaymentKind);
            pending = AssertReplay(offering);
        }

        policy.Mode = "accept";
        PrivateWorldRuntimeState boughtState;
        string boughtId;
        using (var settling = RestorePhase(pending, policy))
        {
            await UntilAsync(settling, policy, () => settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Settled);
            Assert.Equal(new[] { seller, buyer }.Order(StringComparer.Ordinal), settling.Society.Inventory.GetOffer(offer.Id).AcceptedBy);
            var bought = Assert.Single(settling.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ProvenanceLotId == receipt.LotId);
            boughtId = bought.Id;
            Assert.Equal(FarmFieldRules.GrainSeed, bought.ItemKind);
            Assert.Equal(1, bought.Quantity);
            Assert.True(PersonalEquipmentRules.IsCarried(bought, buyer));
            Assert.Null(bought.GroundPosition);
            Assert.Null(bought.StorageBuildingId);
            Assert.Equal(1, settling.Society.Inventory.GetLot(receipt.LotId).Quantity);
            var paymentReceipt = Assert.Single(settling.Towns[0].Markets[0].StockReceipts, item => item.TradeOfferId == offer.Id);
            var payment = settling.Society.Inventory.GetLot(paymentReceipt.LotId);
            Assert.Equal(sellerHousehold, payment.OwnerId);
            Assert.Equal(trade.PaymentKind, payment.ItemKind);
            Assert.Equal(1, payment.Quantity);
            Assert.Equal(new InventoryGroundPosition(trade.Position.X, trade.Position.Y), payment.GroundPosition);
            Assert.Equal(offeredPayment.Quantity - 1, settling.Society.Inventory.Lots.Where(lot => lot.Id == offeredPayment.Id &&
                lot.OwnerId == buyer).Sum(lot => lot.Quantity));
            Assert.Equal(seedBefore, Quantity(settling, FarmFieldRules.GrainSeed));
            Assert.Equal(paymentBefore, Quantity(settling, trade.PaymentKind));
            Assert.All(heldClaims, claim => Assert.Equal(claim, settling.Society.Inventory.GetReservation(claim.Id)));
            boughtState = AssertReplay(settling);
        }

        // Force a fresh observed choice while one purchased unit is still held.
        // One supplier unit remains, so refusal to offer another buy is not vacuous.
        policy.Mode = "held";
        PrivateWorldRuntimeState heldState;
        using (var held = RestorePhase(boughtState, policy))
        {
            Assert.True((await held.AdvanceOneTickAsync()).Advanced);
            var observed = policy.Observations.Where(item => item.Actor == buyer && item.Mode == "held").ToArray();
            Assert.NotEmpty(observed);
            Assert.All(observed, item => Assert.DoesNotContain(item.Candidates, candidate =>
                candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal) &&
                candidate.Description.Contains(" for 1 grain_seed;", StringComparison.Ordinal)));
            Assert.Equal(1, held.Society.Inventory.GetLot(boughtId).Quantity);
            Assert.True(PersonalEquipmentRules.IsCarried(held.Society.Inventory.GetLot(boughtId), buyer));
            Assert.Equal(1, held.Society.Inventory.GetLot(receipt.LotId).Quantity);
            Assert.Single(held.Towns[0].Markets[0].Trades);
            heldState = AssertReplay(held);
        }

        policy.Mode = "plant";
        policy.PlantId = $"farm:Plant:{field.Position.X}:{field.Position.Y}:{FarmFieldRules.Grain}";
        using var planting = RestorePhase(heldState, policy);
        await UntilAsync(planting, policy, () => planting.Fields.Any(item => item.Position == field.Position &&
            item.Crop == FarmFieldRules.Grain && item.Stage is FarmFieldStage.Planted or FarmFieldStage.Growing));
        Assert.Contains(policy.Choices, item => item.Actor == buyer && item.Id == policy.PlantId);
        var consumed = Assert.Single(planting.Society.Inventory.Reservations, claim => claim.LotId == boughtId && claim.Purpose == "field_planting");
        Assert.Equal(1, consumed.Quantity);
        Assert.Equal(InventoryReservationState.Completed, consumed.State);
        Assert.Equal(seedBefore - 1, Quantity(planting, FarmFieldRules.GrainSeed));
        Assert.Equal(paymentBefore, Quantity(planting, trade.PaymentKind));
        Assert.All(heldClaims, claim => Assert.Equal(claim, planting.Society.Inventory.GetReservation(claim.Id)));
        _ = AssertReplay(planting);
    }

    private static PrivateWorldRuntime RestorePhase(PrivateWorldRuntimeState state, Policy policy) => PrivateWorldRuntime.Restore(state with
    {
        // Controlled phase wake after completion; preserve every actual position,
        // field, lot, reservation and project. Never force arrival or authority.
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == policy.Buyer || person.InhabitantId == policy.Seller
            ? person with { LastDecisionContext = null } : person).ToArray(),
    }, policy.CreateProvider);

    private static PrivateWorldRuntimeState AssertReplay(PrivateWorldRuntime world)
    {
        world.Validate();
        MarketObservationTests.AssertProjection(world);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var decoded = PrivateWorldRuntimeCodec.Decode(bytes);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(decoded));
        using var restored = PrivateWorldRuntime.Restore(decoded);
        restored.Validate();
        return decoded;
    }

    private static int Quantity(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private static async Task UntilAsync(PrivateWorldRuntime world, Policy policy, Func<bool> reached)
    {
        for (var step = 0; step < 160 && !reached(); step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(reached(), $"Seed phase {policy.Mode} not reached at tick {world.WorldTick}; observations: " +
            JsonSerializer.Serialize(policy.Observations.TakeLast(8).Select(item => new
            {
                item.Actor,
                item.Mode,
                Candidates = item.Candidates.Select(candidate => new { candidate.Id, candidate.Description }),
            })));
    }

    private sealed class Policy(string buyer, string seller, string stallId)
    {
        internal string Buyer { get; } = buyer;
        internal string Seller { get; } = seller;
        internal string Mode { get; set; } = "till";
        internal string? PlantId { get; set; }
        internal ConcurrentQueue<(string Actor, string Id)> Choices { get; } = new();
        internal ConcurrentQueue<(string Actor, string Mode, CognitionCandidate[] Candidates)> Observations { get; } = new();
        internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor, stallId);
        private sealed class Provider(Policy policy, string actor, string stall) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var candidates = request.Observation.Candidates;
                policy.Observations.Enqueue((actor, policy.Mode, candidates.ToArray()));
                var local = candidates.Where(item => item.DestinationId == stall).ToArray();
                CognitionCandidate? choice = policy.Mode switch
                {
                    "till" when actor == policy.Buyer => candidates.FirstOrDefault(item => item.Id.StartsWith("farm:Till:", StringComparison.Ordinal)),
                    "stock" when actor == policy.Seller => local.FirstOrDefault(item => item.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                        local.FirstOrDefault(item => item.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                            item.Description.Contains(" grain_seed at your borrowed stall;", StringComparison.Ordinal)),
                    "offer" when actor == policy.Buyer => local.FirstOrDefault(item => item.Id.StartsWith("market_buy:", StringComparison.Ordinal) &&
                        item.Description.Contains(" for 1 grain_seed;", StringComparison.Ordinal)),
                    "accept" when actor == policy.Seller => local.FirstOrDefault(item => item.Id.StartsWith("market_continue:", StringComparison.Ordinal)),
                    "plant" when actor == policy.Buyer => candidates.FirstOrDefault(item => item.Id == policy.PlantId),
                    _ => null,
                };
                choice ??= candidates.FirstOrDefault(item => item.DeterministicPriority <= 5 && item.Id is "consume_food" or "collect_shared_food" or
                    "take_food_from_pot" or "make_room_for_food" or "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth");
                choice ??= candidates.Single(item => item.Id == "safe_idle");
                policy.Choices.Enqueue((actor, choice.Id));
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    choice.Id, 1, candidates.ToDictionary(item => item.Id, item => item.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal)));
            }
        }
    }
}
