using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class RestaurantBusinessPipelineTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    private const string JugId = "restaurant-pipeline-jug";

    [Fact]
    public async Task RestaurantBuysRealMilledFlourRefillsAndDeliversInputsThenSellsBreadToAnOutsiderAcrossReload()
    {
        var fixture = CreateFixture();
        using var world = Restore(fixture);
        var beforeUnauthorized = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var unauthorized = world.StartProduction(fixture.Recipe.CanonicalId, fixture.Restaurant.InstanceId, fixture.Customer);
        Assert.False(unauthorized.Applied);
        Assert.Contains("member", unauthorized.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeUnauthorized, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        await AdvanceUntil(world, () => world.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == fixture.Recipe.CanonicalId && job.State == WorldProductionJobState.Running));
        var baking = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.RecipeId == fixture.Recipe.CanonicalId);
        Assert.Equal(Beta, baking.OwnerId);
        Assert.Equal(fixture.Cook, baking.WorkerId);
        Assert.Equal(fixture.Restaurant.InstanceId, baking.BuildingInstanceId);
        Assert.Equal(2, world.BusinessTrades.Count(trade => trade.BuyerId == fixture.Cook && trade.GoodsKind == "flour"));
        var paymentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trade in world.BusinessTrades.Where(trade => trade.BuyerId == fixture.Cook))
        {
            var offer = world.Society.Inventory.GetOffer(trade.OfferId);
            Assert.Equal(DirectBarterState.Settled, offer.State);
            Assert.Equal(1, offer.FirstQuantity);
            Assert.Equal(1, offer.SecondQuantity);
            Assert.Equal("berries", trade.PaymentKind);
            Assert.Equal(Alpha, trade.SellerHouseholdId);
            Assert.NotNull(trade.SellerActorId);
            var seller = world.Society.GetInhabitant(trade.SellerActorId);
            Assert.Equal(Alpha, seller.HouseholdId);
            Assert.Equal(SocietyInhabitantStatus.Active, seller.Status);
            Assert.True(seller.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
            Assert.Equal(new[] { Alpha, fixture.Cook }.Order(StringComparer.Ordinal), offer.AcceptedBy);
            var producedFlour = Assert.Single(world.WorldSimulation.ProductionJobs,
                job => offer.FirstLotId == job.JobId + ":output:00");
            Assert.Equal(fixture.MillRecipe.CanonicalId, producedFlour.RecipeId);
            Assert.Equal(fixture.Miller, producedFlour.WorkerId);
            Assert.Equal(WorldProductionJobState.Completed, producedFlour.State);
            var splitPaymentId = offer.SecondLotId + "#barter:" + offer.Id;
            var paymentId = world.Society.Inventory.Lots.Any(lot => lot.Id == splitPaymentId)
                ? splitPaymentId : offer.SecondLotId;
            Assert.True(paymentIds.Add(paymentId));
            var payment = world.Society.Inventory.GetLot(paymentId);
            Assert.Equal(Alpha, payment.OwnerId);
            Assert.Equal(fixture.Farmhouse.InstanceId, payment.StorageBuildingId);
            Assert.Equal(1, payment.Quantity);
        }
        Assert.Equal(2, paymentIds.Count);
        Assert.Equal(4, world.Society.Inventory.GetLot("restaurant-house-flour").Quantity);
        Assert.Equal(fixture.House.InstanceId, world.Society.Inventory.GetLot("restaurant-house-flour").StorageBuildingId);
        Assert.Equal(2, world.Society.Inventory.GetLot("restaurant-house-wood").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot("restaurant-house-fresh_water").Quantity);
        var claims = baking.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
        Assert.All(claims, claim => Assert.Equal(InventoryReservationState.Reserved, claim.State));
        Assert.All(claims, claim =>
        {
            var input = world.Society.Inventory.GetLot(claim.LotId);
            Assert.Equal(Beta, input.OwnerId);
            Assert.Equal(fixture.Restaurant.InstanceId, input.StorageBuildingId);
        });
        Assert.Equal(2, claims.Where(claim => world.Society.Inventory.GetLot(claim.LotId).ItemKind == "flour")
            .Sum(claim => claim.Quantity));
        Assert.Equal(1, claims.Where(claim => world.Society.Inventory.GetLot(claim.LotId).ItemKind == "wood")
            .Sum(claim => claim.Quantity));
        Assert.Equal(1, claims.Where(claim => world.Society.Inventory.GetLot(claim.LotId).ItemKind == InventoryContainerRules.FreshWater)
            .Sum(claim => claim.Quantity));
        Assert.Equal(JugId, world.Society.Inventory.GetLot(Assert.Single(claims,
            claim => world.Society.Inventory.GetLot(claim.LotId).ItemKind == InventoryContainerRules.FreshWater).LotId).ContainerLotId);
        Assert.Equal(fixture.Restaurant.InstanceId, world.Society.Inventory.GetLot(JugId).StorageBuildingId);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "water_jug_collected" &&
            item.Detail == fixture.Cook + ":" + JugId);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "water_jug_filled" &&
            item.Detail.StartsWith(fixture.Cook + ":" + JugId + ":", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail.StartsWith(fixture.Cook + ":restaurant-house-extra-wood:", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(fixture.Cook + ":", StringComparison.Ordinal));
        Assert.NotEqual(fixture.State.Inhabitants.Single(person => person.InhabitantId == fixture.Cook).Position,
            world.Inhabitants.Single(person => person.InhabitantId == fixture.Cook).Position);
        await AssertStrictReplayAndDiscard(world, fixture);

        await AdvanceUntil(world, () => world.BusinessTrades.Any(trade => trade.BuyerId == fixture.Customer &&
            world.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Open));
        var sale = Assert.Single(world.BusinessTrades, trade => trade.BuyerId == fixture.Customer);
        var quoted = world.Society.Inventory.GetOffer(sale.OfferId);
        Assert.Equal("bread", sale.GoodsKind);
        Assert.Equal("wood", sale.PaymentKind);
        Assert.Equal(2, quoted.FirstQuantity);
        Assert.Equal(1, quoted.SecondQuantity);
        Assert.Equal(baking.JobId + ":output:00", quoted.FirstLotId);
        Assert.Equal(new[] { fixture.Customer }, quoted.AcceptedBy);
        Assert.Equal(2, world.Society.Inventory.GetLot(quoted.FirstLotId).Quantity);
        Assert.All(world.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "barter:" + sale.OfferId),
            reservation => Assert.Equal(InventoryReservationState.Reserved, reservation.State));
        await AssertStrictReplayAndDiscard(world, fixture);

        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = Restore(fixture, PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        await AdvanceUntil(resumed, () => resumed.Inhabitants.Single(person => person.InhabitantId == fixture.Customer)
            .Survival?.LastMealKind == "bread");
        Assert.Equal(DirectBarterState.Settled, resumed.Society.Inventory.GetOffer(sale.OfferId).State);
        Assert.Equal(fixture.Cook, resumed.BusinessTrades.Single(trade => trade.OfferId == sale.OfferId).SellerActorId);
        var purchased = Assert.Single(resumed.Society.Inventory.Lots, lot => lot.OwnerId == fixture.Customer &&
            lot.ItemKind == "bread" && (lot.Id == quoted.FirstLotId || lot.ProvenanceLotId == quoted.FirstLotId));
        Assert.Equal(1, purchased.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, fixture.Customer));
        var mealPayment = resumed.Society.Inventory.GetLot("restaurant-customer-payment");
        Assert.Equal(Beta, mealPayment.OwnerId);
        Assert.Equal(fixture.Restaurant.InstanceId, mealPayment.StorageBuildingId);
        Assert.Equal(1, mealPayment.Quantity);
        Assert.Equal(Alpha, resumed.Society.GetInhabitant(fixture.Customer).HouseholdId);
        Assert.Equal(4, resumed.Society.Inventory.GetLot("restaurant-house-flour").Quantity);
        Assert.Equal(fixture.House.InstanceId, resumed.Society.Inventory.GetLot("restaurant-house-flour").StorageBuildingId);
        Assert.Equal(fixture.Restaurant.InstanceId, resumed.Society.Inventory.GetLot(JugId).StorageBuildingId);
        Assert.All(baking.InputReservationIds.Select(resumed.Society.Inventory.GetReservation),
            claim => Assert.Equal(InventoryReservationState.Completed, claim.State));
        var afterUnauthorized = PrivateWorldRuntimeCodec.Encode(resumed.ExportState());
        Assert.False(resumed.StartProduction(fixture.Recipe.CanonicalId, fixture.Restaurant.InstanceId, fixture.Customer).Applied);
        Assert.Equal(afterUnauthorized, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        resumed.Validate();
    }

    [Theory]
    [InlineData("store")]
    [InlineData("oversized-pot")]
    public async Task RestaurantStillPurchasesRealFlourWhenItsOtherHeldStockHasNoExecutableWorkstationPickup(string sourceKind)
    {
        var fixture = CreateFixture(purchaseOnly: true, inaccessibleSource: sourceKind);
        using var world = Restore(fixture);
        await AdvanceUntil(world, () => world.WorldSimulation.ProductionJobs.Count(job =>
            job.RecipeId == fixture.MillRecipe.CanonicalId && job.State == WorldProductionJobState.Completed) == 4);
        await Advance(world, 30);
        Assert.Equal(2, world.BusinessTrades.Count);
        Assert.All(world.BusinessTrades, trade =>
        {
            Assert.Equal("flour", trade.GoodsKind);
            Assert.Equal(Alpha, trade.SellerHouseholdId);
            Assert.Equal(DirectBarterState.Settled, world.Society.Inventory.GetOffer(trade.OfferId).State);
        });
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == fixture.Cook && lot.ItemKind == "flour")
            .Sum(lot => lot.Quantity));
        var unused = world.Society.Inventory.GetLot("restaurant-withheld-flour");
        Assert.Equal(4, unused.Quantity);
        Assert.Equal(Beta, unused.OwnerId);
        Assert.Equal("restaurant-withheld-source", unused.StorageBuildingId);
        Assert.Equal(4, world.Society.Inventory.GetLot("restaurant-house-flour").Quantity);
        if (sourceKind == "oversized-pot")
        {
            Assert.Equal("restaurant-withheld-pot", unused.ContainerLotId);
            Assert.Equal(9, 1 + world.Society.Inventory.Lots.Where(lot => lot.ContainerLotId == unused.ContainerLotId)
                .Sum(lot => lot.Quantity));
            Assert.True(9 > PersonalEquipmentRules.Capacity(world.Society.Inventory, fixture.Cook, null));
        }
        else
        {
            Assert.Null(unused.ContainerLotId);
            var source = world.WorldSimulation.Buildings.Single(building => building.InstanceId == unused.StorageBuildingId);
            Assert.Contains("store", world.WorldContent.Buildings.Single(definition => definition.CanonicalId == source.DefinitionId).Tags);
        }
        world.Validate();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task RestaurantIngredientQuoteKeepsItsExactPairAndDoesNotOverbuyAMissingSingleGreensServing(
        int restaurantGreens, int expectedPurchases)
    {
        var fixture = CreateFixture(purchaseOnly: true, restaurantGreens: restaurantGreens, farmhouseGreens: 2);
        // Farmhouse edible produce is quoted in pairs; raw potatoes are not.
        // Satisfy the separate flour deficit in the initial premises so this
        // control exercises only the exact greens quote and its remaining gap.
        fixture = fixture with
        {
            State = WithInventory(fixture.State, InventoryFixture.AddLot(fixture.State.Society.Society.Inventory,
                "restaurant-quote-flour", "flour", Beta, 4, storageBuildingId: fixture.Restaurant.InstanceId)),
        };
        using var world = Restore(fixture);
        await Advance(world, 50);
        var purchases = world.BusinessTrades.Where(trade => trade.BuyerId == fixture.Cook && trade.GoodsKind == "cultivated_greens").ToArray();
        Assert.Equal(expectedPurchases, purchases.Length);
        if (expectedPurchases > 0)
        {
            var offer = world.Society.Inventory.GetOffer(Assert.Single(purchases).OfferId);
            Assert.Equal(2, offer.FirstQuantity);
            Assert.Equal(1, offer.SecondQuantity);
            Assert.Equal(DirectBarterState.Settled, offer.State);
            Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == fixture.Cook && lot.ItemKind == "cultivated_greens")
                .Sum(lot => lot.Quantity));
        }
        else
        {
            Assert.Equal(2, world.Society.Inventory.GetLot("restaurant-farm-greens").Quantity);
            Assert.Equal(Alpha, world.Society.Inventory.GetLot("restaurant-farm-greens").OwnerId);
            Assert.Equal(fixture.Farmhouse.InstanceId, world.Society.Inventory.GetLot("restaurant-farm-greens").StorageBuildingId);
        }
        world.Validate();
    }

    [Theory]
    [InlineData("porridge", "porridge")]
    [InlineData("berry-porridge", "berry_porridge")]
    [InlineData("fruit-porridge", "fruit_porridge")]
    public async Task RestaurantQuotesTwoActualCookedServingsForOneWoodAndCustomerEatsTheirPaidMeal(
        string recipeLocalId, string mealKind)
    {
        var fixture = CreateFixture(recipeLocalId, kitchenReady: true);
        using var world = Restore(fixture);
        await AdvanceUntil(world, () => world.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == fixture.Recipe.CanonicalId && job.State == WorldProductionJobState.Running));
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.RecipeId == fixture.Recipe.CanonicalId);
        var inputClaims = job.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
        Assert.All(inputClaims, claim => Assert.Equal(InventoryReservationState.Reserved, claim.State));
        Assert.Contains(inputClaims, claim => world.Society.Inventory.GetLot(claim.LotId).ItemKind == InventoryContainerRules.FreshWater);
        Assert.Contains(inputClaims, claim => world.Society.Inventory.GetLot(claim.LotId).ItemKind == "wood");
        Assert.Equal(fixture.Restaurant.InstanceId, world.Society.Inventory.GetLot(JugId).StorageBuildingId);
        await AdvanceUntil(world, () => world.BusinessTrades.Any(trade => trade.BuyerId == fixture.Customer));
        var sale = Assert.Single(world.BusinessTrades, trade => trade.BuyerId == fixture.Customer);
        var offer = world.Society.Inventory.GetOffer(sale.OfferId);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.Equal(WorldProductionJobState.Completed,
            world.WorldSimulation.ProductionJobs.Single(item => item.JobId == job.JobId).State);
        Assert.Equal(job.JobId + ":output:00", offer.FirstLotId);
        Assert.Equal(mealKind, sale.GoodsKind);
        Assert.Equal(2, offer.FirstQuantity);
        Assert.Equal(1, offer.SecondQuantity);
        Assert.Equal("wood", sale.PaymentKind);
        Assert.Equal(2, world.Society.Inventory.GetLot(offer.FirstLotId).Quantity);
        await AssertStrictReplayAndDiscard(world, fixture);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(fixture, PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        await AdvanceUntil(restored, () => restored.Inhabitants.Single(person => person.InhabitantId == fixture.Customer)
            .Survival?.LastMealKind == mealKind);
        Assert.Equal(DirectBarterState.Settled, restored.Society.Inventory.GetOffer(offer.Id).State);
        Assert.Equal(new[] { Beta, fixture.Customer }.Order(StringComparer.Ordinal),
            restored.Society.Inventory.GetOffer(offer.Id).AcceptedBy);
        Assert.Equal(Beta, restored.Society.Inventory.GetLot("restaurant-customer-payment").OwnerId);
        Assert.Equal(fixture.Restaurant.InstanceId, restored.Society.Inventory.GetLot("restaurant-customer-payment").StorageBuildingId);
        Assert.Equal(Alpha, restored.Society.GetInhabitant(fixture.Customer).HouseholdId);
        Assert.Equal(1, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == fixture.Customer && lot.ItemKind == mealKind)
            .Sum(lot => lot.Quantity));
        restored.Validate();
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public async Task IngredientPurchasesPreserveActualPersonalReserveAndNeverSpendHouseholdOwnedCargo(
        bool householdOwnsPayment, int purchases)
    {
        var fixture = CreateFixture(purchaseOnly: true, householdOwnsPayment: householdOwnsPayment);
        using var world = Restore(fixture);
        await AdvanceUntil(world, () => world.WorldSimulation.ProductionJobs.Count(job =>
            job.RecipeId == fixture.MillRecipe.CanonicalId && job.State == WorldProductionJobState.Completed) >= purchases + 2);
        await Advance(world, 30);
        Assert.Equal(purchases, world.BusinessTrades.Count);
        Assert.All(world.BusinessTrades, trade =>
            Assert.Equal(DirectBarterState.Settled, world.Society.Inventory.GetOffer(trade.OfferId).State));
        Assert.Equal(4 - purchases, world.Society.Inventory.GetLot("restaurant-buyer-payment").Quantity);
        Assert.Equal(householdOwnsPayment ? Beta : fixture.Cook,
            world.Society.Inventory.GetLot("restaurant-buyer-payment").OwnerId);
        Assert.Equal(purchases, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == fixture.Cook && lot.ItemKind == "flour")
            .Sum(lot => lot.Quantity));
        Assert.True(world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Alpha &&
            lot.StorageBuildingId == fixture.Farmhouse.InstanceId && lot.ItemKind == "flour").Sum(lot => lot.Quantity) >= 2);
        Assert.Equal(4, world.Society.Inventory.GetLot("restaurant-house-flour").Quantity);
        Assert.Equal(fixture.State.Inhabitants.Single(person => person.InhabitantId == fixture.Cook).Position,
            world.Inhabitants.Single(person => person.InhabitantId == fixture.Cook).Position);
        Assert.True(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, fixture.Cook, null) <
            PersonalEquipmentRules.BaseCapacity);
        var recent = fixture.CookProvider.SeenObservations.Last();
        Assert.DoesNotContain(recent.Candidates, candidate => candidate.Id == "business_shop:" + fixture.Farmhouse.InstanceId);
        await AssertStrictReplayAndDiscard(world, fixture);
    }

    [Theory]
    [InlineData(6, true, true)]
    [InlineData(7, true, false)]
    [InlineData(0, false, true)]
    public async Task RestaurantRealOutputRequiresPhysicalCustomerAndNetRoomForExactTwoServingQuote(
        int ballast, bool near, bool mayBuy)
    {
        var fixture = CreateFixture("porridge", kitchenReady: true, customerBallast: ballast, customerNear: near);
        if (!near) fixture = WithNearbyRemoteCustomer(fixture);
        using var world = Restore(fixture);
        await AdvanceUntil(world, () =>
        {
            AssertNoRemoteMealQuote(world, fixture);
            return world.WorldSimulation.ProductionJobs.Any(job =>
                job.RecipeId == fixture.Recipe.CanonicalId && job.State == WorldProductionJobState.Completed);
        });
        for (var tick = 0; tick < 35; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            AssertNoRemoteMealQuote(world, fixture);
        }
        if (!near)
            await AdvanceUntil(world, () =>
            {
                AssertNoRemoteMealQuote(world, fixture);
                return world.BusinessTrades.Any(trade => trade.BuyerId == fixture.Customer &&
                    world.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Settled);
            }, limit: 120);
        var sales = world.BusinessTrades.Where(trade => trade.BuyerId == fixture.Customer).ToArray();
        Assert.Equal(mayBuy ? 1 : 0, sales.Length);
        if (mayBuy)
        {
            var offer = world.Society.Inventory.GetOffer(Assert.Single(sales).OfferId);
            Assert.Equal(2, offer.FirstQuantity);
            Assert.Equal(1, offer.SecondQuantity);
            Assert.Equal(DirectBarterState.Settled, offer.State);
            if (!near)
            {
                Assert.NotEqual(fixture.State.Inhabitants.Single(person => person.InhabitantId == fixture.Customer).Position,
                    world.Inhabitants.Single(person => person.InhabitantId == fixture.Customer).Position);
                Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                    item.Detail.StartsWith(fixture.Customer + ":", StringComparison.Ordinal));
                Assert.True(fixture.State.Map.FootDistance(
                    world.Inhabitants.Single(person => person.InhabitantId == fixture.Customer).Position,
                    fixture.Restaurant.Position) <= 1);
            }
        }
        else
        {
            var output = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "porridge");
            Assert.Equal(2, output.Quantity);
            Assert.Equal(Beta, output.OwnerId);
            Assert.Equal(fixture.Restaurant.InstanceId, output.StorageBuildingId);
            Assert.Equal(1, world.Society.Inventory.GetLot("restaurant-customer-payment").Quantity);
            Assert.DoesNotContain(world.Society.Inventory.Offers, offer => offer.SecondPartyId == fixture.Customer);
        }
        world.Validate();
    }

    [Theory]
    [InlineData("ordinary", true)]
    [InlineData("empty-shelf", true)]
    [InlineData("household-payment", false)]
    [InlineData("full", false)]
    [InlineData("foreign-town", true)]
    public async Task RestaurantMealVisitUsesPublicMenuAndPersonalAppetiteAndPaymentInAnyTown(string control, bool mayVisit)
    {
        var fixture = WithNearbyRemoteCustomer(CreateFixture("porridge", kitchenReady: true, customerNear: false));
        var initial = fixture.State;
        var inventory = initial.Society.Society.Inventory with
        {
            Lots = initial.Society.Society.Inventory.Lots.Where(lot => control != "empty-shelf" || lot.Id != "restaurant-raw-grain")
                .Select(lot => control == "household-payment" && lot.Id == "restaurant-customer-payment"
                    ? lot with { OwnerId = Alpha, CarrierId = fixture.Customer } : lot).ToArray(),
        };
        initial = WithInventory(initial, inventory);
        if (control == "full")
            initial = initial with
            {
                Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == fixture.Customer
                    ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
            };
        if (control == "foreign-town")
            initial = WithForeignTownResident(initial, fixture.Customer);
        fixture = fixture with { State = initial };
        var customerProvider = new PipelineProvider("business_continue:", "business_shop:" + fixture.Restaurant.InstanceId, "consume_food");
        using var world = PrivateWorldRuntime.Restore(initial, id => id == fixture.Customer ? customerProvider :
            id == fixture.Cook ? CookProvider(fixture.Farmhouse, fixture.Recipe, true, false) : new PipelineProvider("safe_idle"));
        var start = initial.Inhabitants.Single(person => person.InhabitantId == fixture.Customer).Position;
        long? arrivedTick = null;
        for (var tick = 0; tick < 100; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            AssertNoRemoteMealQuote(world, fixture);
            var position = world.Inhabitants.Single(person => person.InhabitantId == fixture.Customer).Position;
            if (initial.Map.FootDistance(position, fixture.Restaurant.Position) <= 1) arrivedTick ??= world.WorldTick;
        }
        var visits = customerProvider.SeenObservations.SelectMany(observation => observation.Candidates)
            .Where(candidate => candidate.Id == "business_shop:" + fixture.Restaurant.InstanceId &&
                candidate.Description.StartsWith("Visit this Restaurant", StringComparison.Ordinal)).ToArray();
        Assert.Equal(mayVisit, visits.Length > 0);
        Assert.All(visits, visit =>
        {
            Assert.DoesNotContain("porridge", visit.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("stocked", visit.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("restaurant-raw-grain", visit.Description, StringComparison.Ordinal);
        });
        var finish = world.Inhabitants.Single(person => person.InhabitantId == fixture.Customer).Position;
        if (mayVisit)
        {
            Assert.NotNull(arrivedTick);
            Assert.NotEqual(start, finish);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                item.Detail.StartsWith(fixture.Customer + ":", StringComparison.Ordinal));
        }
        else Assert.Equal(start, finish);
        var sales = world.BusinessTrades.Where(trade => trade.BuyerId == fixture.Customer).ToArray();
        if (control is "ordinary" or "foreign-town")
        {
            var sale = Assert.Single(sales);
            Assert.True(sale.ProposedTick >= arrivedTick!.Value);
            var offer = world.Society.Inventory.GetOffer(sale.OfferId);
            Assert.Equal(DirectBarterState.Settled, offer.State);
            Assert.Equal(2, offer.FirstQuantity);
            Assert.Equal(1, offer.SecondQuantity);
            var purchased = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == fixture.Customer &&
                lot.ItemKind == "porridge" && (lot.Id == offer.FirstLotId || lot.ProvenanceLotId == offer.FirstLotId));
            Assert.Equal(2, purchased.Quantity);
            Assert.True(PersonalEquipmentRules.IsCarried(purchased, fixture.Customer));
            var payment = world.Society.Inventory.GetLot("restaurant-customer-payment");
            Assert.Equal(1, payment.Quantity);
            Assert.Equal(Beta, payment.OwnerId);
            Assert.Equal(fixture.Restaurant.InstanceId, payment.StorageBuildingId);
        }
        else
        {
            Assert.Empty(sales);
            Assert.Equal(1, world.Society.Inventory.GetLot("restaurant-customer-payment").Quantity);
            Assert.Equal(control == "household-payment" ? Alpha : fixture.Customer,
                world.Society.Inventory.GetLot("restaurant-customer-payment").OwnerId);
        }
        if (control == "empty-shelf")
        {
            Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, job => job.RecipeId == fixture.Recipe.CanonicalId);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == "porridge");
        }
        await AssertStrictReplayAndDiscard(world, fixture);
        if (control == "foreign-town")
        {
            var eatingState = world.ExportState() with
            {
                Inhabitants = world.ExportState().Inhabitants.Select(person => person.InhabitantId == fixture.Customer
                    ? person with { HungerBasisPoints = 3_000 } : person).ToArray(),
            };
            using var eating = PrivateWorldRuntime.Restore(eatingState,
                id => id == fixture.Customer ? new PipelineProvider("consume_food") : new PipelineProvider("safe_idle"));
            await AdvanceUntil(eating, () => eating.Inhabitants.Single(person => person.InhabitantId == fixture.Customer)
                .Survival?.LastMealKind == "porridge", 20);
            Assert.Equal("town:visiting-resident", eating.Towns.Single(town => town.ResidentIds.Contains(fixture.Customer)).Id);
            Assert.Equal(Alpha, eating.Society.GetInhabitant(fixture.Customer).HouseholdId);
            Assert.False(eating.StartProduction(fixture.Recipe.CanonicalId, fixture.Restaurant.InstanceId, fixture.Customer).Applied);
            eating.Validate();
        }
    }

    private static void AssertNoRemoteMealQuote(PrivateWorldRuntime world, Fixture fixture)
    {
        var customer = world.Inhabitants.Single(person => person.InhabitantId == fixture.Customer);
        if (fixture.State.Map.FootDistance(customer.Position, fixture.Restaurant.Position) <= 1) return;
        Assert.DoesNotContain(world.BusinessTrades, trade => trade.BuyerId == fixture.Customer);
        Assert.DoesNotContain(world.Society.Inventory.Offers, offer => offer.SecondPartyId == fixture.Customer);
        Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.LotId == "restaurant-customer-payment" &&
            reservation.State == InventoryReservationState.Reserved);
        Assert.Equal(1, world.Society.Inventory.GetLot("restaurant-customer-payment").Quantity);
    }

    private static Fixture WithNearbyRemoteCustomer(Fixture fixture)
    {
        var initial = fixture.State;
        var occupied = initial.Inhabitants.Where(person => person.InhabitantId != fixture.Customer)
            .Select(person => person.Position).Concat(initial.WorldSimulation!.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(initial.WorldContent!.Buildings.Single(definition =>
                    definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var position = initial.Map.Tiles.Select(tile => tile.Position).First(point => initial.Map.IsPassable(point) &&
            initial.Map.IsReachableFromCampOnFoot(point) && !occupied.Contains(point) &&
            initial.Map.FootDistance(point, fixture.Restaurant.Position) is >= 4 and <= 6);
        return fixture with
        {
            State = initial with
            {
                Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == fixture.Customer
                    ? person with { Position = position } : person).ToArray(),
            },
        };
    }

    [Theory]
    [InlineData(false, 4, true, 2, false)]
    [InlineData(false, 4, false, 0, false)]
    [InlineData(true, 4, true, 0, false)]
    [InlineData(false, 2, true, 0, false)]
    [InlineData(false, 4, true, 2, true)]
    public async Task RestaurantIngredientVisitWalksBeforeQuotingAndRequiresItsOwnSpendablePayment(
        bool householdOwnsPayment, int paymentQuantity, bool millHasGrain, int expectedPurchases, bool visitorFromAnotherTown)
    {
        var fixture = CreateFixture(purchaseOnly: true, householdOwnsPayment: householdOwnsPayment);
        var initial = fixture.State;
        var occupied = initial.Inhabitants.Where(person => person.InhabitantId != fixture.Cook)
            .Select(person => person.Position).ToHashSet();
        var start = initial.Map.Tiles.Select(tile => tile.Position).Where(point => initial.Map.IsPassable(point) &&
                initial.Map.IsReachableFromCampOnFoot(point) && !occupied.Contains(point) &&
                initial.Map.FootDistance(point, fixture.Farmhouse.Position) >= 4)
            .OrderBy(point => initial.Map.FootDistance(point, fixture.Farmhouse.Position)).First();
        var inventory = initial.Society.Society.Inventory with
        {
            Lots = initial.Society.Society.Inventory.Lots.Where(lot => millHasGrain || lot.Id != "restaurant-mill-grain")
                .Select(lot => lot.Id == "restaurant-buyer-payment" ? lot with { Quantity = paymentQuantity } : lot).ToArray(),
        };
        fixture = fixture with
        {
            State = WithInventory(initial with
            {
                Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == fixture.Cook
                    ? person with { Position = start } : person).ToArray(),
            }, inventory),
        };
        if (visitorFromAnotherTown)
            fixture = fixture with { State = WithForeignTownResident(fixture.State, fixture.Cook) };
        using var world = Restore(fixture);
        var mayVisit = !householdOwnsPayment && paymentQuantity > 2;
        for (var tick = 0; tick < 200; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var position = world.Inhabitants.Single(person => person.InhabitantId == fixture.Cook).Position;
            if (initial.Map.FootDistance(position, fixture.Farmhouse.Position) > 1)
            {
                Assert.DoesNotContain(world.BusinessTrades, trade => trade.BuyerId == fixture.Cook);
                Assert.DoesNotContain(world.Society.Inventory.Offers, offer => offer.SecondPartyId == fixture.Cook);
                Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation =>
                    reservation.Purpose.StartsWith("barter:", StringComparison.Ordinal));
            }
        }
        var visits = fixture.CookProvider.SeenObservations.SelectMany(observation => observation.Candidates)
            .Where(candidate => candidate.Id == "business_shop:" + fixture.Farmhouse.InstanceId &&
                candidate.Description.StartsWith("Visit this Town shop", StringComparison.Ordinal)).ToArray();
        Assert.Equal(mayVisit, visits.Length > 0);
        Assert.All(visits, visit =>
        {
            Assert.DoesNotContain("flour", visit.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("stocked", visit.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("restaurant-mill-grain", visit.Description, StringComparison.Ordinal);
        });
        var finish = world.Inhabitants.Single(person => person.InhabitantId == fixture.Cook).Position;
        if (mayVisit)
        {
            Assert.NotEqual(start, finish);
            Assert.True(initial.Map.FootDistance(finish, fixture.Farmhouse.Position) <= 1);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                item.Detail.StartsWith(fixture.Cook + ":", StringComparison.Ordinal));
        }
        else Assert.Equal(start, finish);
        var purchases = world.BusinessTrades.Where(trade => trade.BuyerId == fixture.Cook).ToArray();
        Assert.Equal(expectedPurchases, purchases.Length);
        Assert.All(purchases, trade =>
        {
            Assert.Equal("flour", trade.GoodsKind);
            var offer = world.Society.Inventory.GetOffer(trade.OfferId);
            Assert.Equal(DirectBarterState.Settled, offer.State);
            Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.JobId + ":output:00" == offer.FirstLotId &&
                job.RecipeId == fixture.MillRecipe.CanonicalId && job.State == WorldProductionJobState.Completed);
        });
        Assert.Equal(paymentQuantity - expectedPurchases, world.Society.Inventory.GetLot("restaurant-buyer-payment").Quantity);
        Assert.Equal(householdOwnsPayment ? Beta : fixture.Cook,
            world.Society.Inventory.GetLot("restaurant-buyer-payment").OwnerId);
        Assert.Equal(4, world.Society.Inventory.GetLot("restaurant-house-flour").Quantity);
        if (!millHasGrain)
        {
            Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, job => job.RecipeId == fixture.MillRecipe.CanonicalId);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == Alpha && lot.ItemKind == "flour");
        }
        await AssertStrictReplayAndDiscard(world, fixture);
    }

    private sealed record Fixture(PrivateWorldRuntimeState State, string Cook, string Miller, string Customer,
        PlacedBuilding Restaurant, PlacedBuilding Farmhouse, PlacedBuilding House, RecipeDefinition Recipe,
        RecipeDefinition MillRecipe, bool KitchenReady, bool PurchaseOnly, PipelineProvider CookProvider);

    private static Fixture CreateFixture(string recipeLocalId = "bread", bool kitchenReady = false,
        bool purchaseOnly = false, bool householdOwnsPayment = false, int customerBallast = 0, bool customerNear = true,
        int restaurantPotatoes = 2, int farmhousePotatoes = 0, string? inaccessibleSource = null,
        int restaurantGreens = 2, int farmhouseGreens = 0)
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new PipelineProvider("safe_idle"));
        var state = generated.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == Beta &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var farmhouse = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == Alpha &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("farmhouse"));
        var restaurantDefinition = state.WorldContent!.Buildings.Single(definition => definition.LocalId == "restaurant-1x2");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in restaurantDefinition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "restaurant-paid-build-" + cost.ResourceId,
                cost.ResourceId, Beta, cost.Amount, storageBuildingId: house.InstanceId);
        var materialsBefore = restaurantDefinition.BuildCosts.ToDictionary(cost => cost.ResourceId,
            cost => inventory.Lots.Where(lot => lot.OwnerId == Beta && lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
        using (var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new PipelineProvider("safe_idle")))
        {
            _ = state.Map.Tiles.Select(tile => tile.Position)
                .Where(point => state.Map.IsReachableFromCampOnFoot(point))
                .OrderBy(point => state.Map.FootDistance(house.Position, point))
                .First(point => placing.PlaceBuilding("restaurant-pipeline", restaurantDefinition.CanonicalId, point, Beta).Applied);
            foreach (var cost in restaurantDefinition.BuildCosts)
                Assert.Equal(materialsBefore[cost.ResourceId] - cost.Amount,
                    placing.Society.Inventory.Lots.Where(lot => lot.OwnerId == Beta && lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
            placing.Validate();
            state = placing.ExportState();
        }
        if (inaccessibleSource is not null)
        {
            var sourceDefinition = state.WorldContent!.Buildings.Where(definition =>
                    definition.Tags.Contains(inaccessibleSource == "store" ? "store" : "farmhouse"))
                .OrderBy(definition => definition.Width * definition.Height).First();
            state = AddPaidSource(state, house, sourceDefinition);
        }
        var restaurant = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "restaurant-pipeline");
        var cook = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Beta).Id;
        var alphaAdults = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == Alpha).ToArray();
        Assert.Equal(2, alphaAdults.Length);
        var miller = alphaAdults[0].Id;
        var customer = alphaAdults[1].Id;
        var recipe = state.WorldContent!.Recipes.Single(item => item.WorkstationBuildingId == restaurant.DefinitionId &&
            item.LocalId == recipeLocalId);
        var millRecipe = state.WorldContent!.Recipes.Single(item => item.WorkstationBuildingId == farmhouse.DefinitionId &&
            item.LocalId == "mill-grain");

        // Initial premises only: no cooked Restaurant or Farmhouse output is
        // injected. House flour is existing private stock the Restaurant must preserve.
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind is "field_map" or "field_record" ||
                lot.OwnerId != Alpha && lot.OwnerId != Beta && lot.OwnerId != cook && lot.OwnerId != customer).ToArray(),
        };
        // This scripted pipeline isolates cooking and trade. Existing usable shared
        // tools keep the House's wood reserve at two cooking batches, not tool inputs.
        var adults = state.Society.Society.Inhabitants.Count(person => person.HouseholdId == Beta &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
        inventory = InventoryFixture.AddLot(inventory, "restaurant-fixture-axes", "wooden_axe", Beta, adults,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "restaurant-fixture-picks", "wooden_pickaxe", Beta, adults,
            storageBuildingId: house.InstanceId);
        var houseInputs = state.WorldContent!.Recipes.Where(item => item.WorkstationBuildingId == house.DefinitionId &&
                item.Tags.Any(tag => tag is "named-meal" or "pottery"))
            .SelectMany(item => item.Inputs).GroupBy(input => input.ResourceId)
            .Select(group => new ContentQuantity(group.Key, group.Max(input => input.Amount) * 2)).ToArray();
        inventory = AddInitialInputs(inventory, house, houseInputs, "restaurant-house");
        if (inaccessibleSource is not null)
        {
            var pot = inaccessibleSource == "oversized-pot" ? "restaurant-withheld-pot" : null;
            if (pot is not null)
                inventory = InventoryFixture.AddLot(inventory, pot, InventoryContainerRules.StoragePot, Beta, 1,
                    storageBuildingId: "restaurant-withheld-source");
            inventory = InventoryFixture.AddLot(inventory, "restaurant-withheld-flour", "flour", Beta, 4,
                storageBuildingId: "restaurant-withheld-source", containerLotId: pot);
            if (pot is not null)
                inventory = InventoryFixture.AddLot(inventory, "restaurant-withheld-fruit", "fruit", Beta, 4,
                    storageBuildingId: "restaurant-withheld-source", containerLotId: pot);
        }
        // This real extra wood is carried from the House; its own target2 stays.
        inventory = InventoryFixture.AddLot(inventory, "restaurant-house-extra-wood", "wood", Beta, 2,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "restaurant-mill-grain", "grain", Alpha, 4,
            storageBuildingId: farmhouse.InstanceId);
        if (farmhousePotatoes > 0)
            inventory = InventoryFixture.AddLot(inventory, "restaurant-farm-potatoes", "potatoes", Alpha, farmhousePotatoes,
                storageBuildingId: farmhouse.InstanceId);
        if (farmhouseGreens > 0)
            inventory = InventoryFixture.AddLot(inventory, "restaurant-farm-greens", "cultivated_greens", Alpha, farmhouseGreens,
                storageBuildingId: farmhouse.InstanceId);
        foreach (var (kind, quantity) in new (string, int)[]
            { ("grain", 2), ("fruit", 2), ("potatoes", restaurantPotatoes), ("cultivated_greens", restaurantGreens) }.Where(item => item.Item2 > 0))
            inventory = InventoryFixture.AddLot(inventory, "restaurant-raw-" + kind, kind, Beta, quantity,
                storageBuildingId: restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, JugId, InventoryContainerRules.WaterJug, Beta, 1,
            storageBuildingId: restaurant.InstanceId);
        if (kitchenReady)
        {
            inventory = InventoryFixture.AddLot(inventory, "restaurant-raw-berries", "berries", Beta, 2,
                storageBuildingId: restaurant.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "restaurant-raw-wood", "wood", Beta, 1,
                storageBuildingId: restaurant.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "restaurant-raw-water", InventoryContainerRules.FreshWater, Beta, 1,
                storageBuildingId: restaurant.InstanceId, containerLotId: JugId);
        }
        inventory = InventoryFixture.AddLot(inventory, "restaurant-buyer-payment", "berries",
            householdOwnsPayment ? Beta : cook, 4);
        if (householdOwnsPayment)
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == "restaurant-buyer-payment"
                    ? lot with { CarrierId = cook } : lot).ToArray(),
            };
        inventory = InventoryFixture.AddLot(inventory, "restaurant-customer-payment", "wood", customer, 1);
        if (customerBallast > 0)
            inventory = InventoryFixture.AddLot(inventory, "restaurant-customer-ballast", "stone", customer, customerBallast);
        state = WithInventory(state, inventory);
        var footprints = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != cook && person.InhabitantId != miller &&
                person.InhabitantId != customer).Select(person => person.Position).Concat(footprints).ToHashSet();
        occupied.Add(farmhouse.Position);
        var cookPosition = kitchenReady ? restaurant.Position : Neighbor(farmhouse.Position);
        occupied.Add(cookPosition);
        var customerPosition = customerNear ? Neighbor(restaurant.Position) : state.Map.Tiles.Select(tile => tile.Position)
            .First(point => state.Map.IsPassable(point) && state.Map.IsReachableFromCampOnFoot(point) &&
                Distance(point, restaurant.Position) > 4 && !occupied.Contains(point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == cook ? cookPosition : person.InhabitantId == miller ? farmhouse.Position :
                    person.InhabitantId == customer ? customerPosition : person.Position,
                HungerBasisPoints = person.InhabitantId == customer ? 6_000 : 9_000,
                LastDecisionContext = null,
                Project = null,
                Equipment = person.InhabitantId == cook || person.InhabitantId == customer ? null : person.Equipment,
            }).ToArray(),
        };
        var cookProvider = CookProvider(farmhouse, recipe, kitchenReady, purchaseOnly);
        return new(state, cook, miller, customer, restaurant, farmhouse, house, recipe, millRecipe,
            kitchenReady, purchaseOnly, cookProvider);

        GridPoint Neighbor(GridPoint site) => state.Map.Tiles.Select(tile => tile.Position).First(point =>
            Distance(point, site) == 1 && state.Map.IsPassable(point) && !occupied.Contains(point));
    }

    private static InventoryCheckpoint AddInitialInputs(InventoryCheckpoint inventory, PlacedBuilding site,
        IEnumerable<ContentQuantity> inputs, string prefix)
    {
        foreach (var input in inputs)
        {
            var water = input.ResourceId is InventoryContainerRules.FreshWater or "milk";
            var jugId = prefix + "-jug-" + input.ResourceId;
            if (water)
                inventory = InventoryFixture.AddLot(inventory, jugId, InventoryContainerRules.WaterJug, site.HouseholdId!, 1,
                    storageBuildingId: site.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, prefix + "-" + input.ResourceId, input.ResourceId,
                site.HouseholdId!, input.Amount, storageBuildingId: site.InstanceId, containerLotId: water ? jugId : null);
        }
        return inventory;
    }

    private static PrivateWorldRuntimeState AddPaidSource(PrivateWorldRuntimeState state, PlacedBuilding house,
        BuildingDefinition definition)
    {
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "restaurant-withheld-build-" + cost.ResourceId,
                cost.ResourceId, Beta, cost.Amount, storageBuildingId: house.InstanceId);
        using var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new PipelineProvider("safe_idle"));
        _ = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsReachableFromCampOnFoot(point))
            .OrderBy(point => state.Map.FootDistance(house.Position, point))
            .First(point => placing.PlaceBuilding("restaurant-withheld-source", definition.CanonicalId, point, Beta).Applied);
        placing.Validate();
        return placing.ExportState();
    }

    private static PipelineProvider CookProvider(PlacedBuilding farmhouse, RecipeDefinition recipe, bool ready, bool purchaseOnly) =>
        ready ? new("business_continue:", "build:recipe:" + recipe.CanonicalId) :
        purchaseOnly ? new("business_continue:", "business_shop:" + farmhouse.InstanceId) :
        new("business_continue:", "business_shop:" + farmhouse.InstanceId, "build:recipe:" + recipe.CanonicalId,
            "fill_water_jug:", "collect_water_jug", "haul_household_stock", "supply_workstation:");

    private static PrivateWorldRuntime Restore(Fixture fixture, PrivateWorldRuntimeState? state = null,
        bool useInitialProvider = true) => PrivateWorldRuntime.Restore(state ?? fixture.State, id =>
        id == fixture.Cook ? useInitialProvider && state is null ? fixture.CookProvider :
            CookProvider(fixture.Farmhouse, fixture.Recipe, fixture.KitchenReady, fixture.PurchaseOnly) :
        id == fixture.Miller ? fixture.KitchenReady ? new PipelineProvider("safe_idle") :
            new PipelineProvider("business_continue:", "build:recipe:" + fixture.MillRecipe.CanonicalId) :
        id == fixture.Customer ? new PipelineProvider("business_continue:", "business_shop:" + fixture.Restaurant.InstanceId, "consume_food") :
            new PipelineProvider("safe_idle"));

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };

    private static int Distance(GridPoint left, GridPoint right) => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private static async Task AssertStrictReplayAndDiscard(PrivateWorldRuntime source, Fixture fixture)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(source.ExportState());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(bytes)));
        using var first = Restore(fixture, PrivateWorldRuntimeCodec.Decode(bytes));
        using var second = Restore(fixture, PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(first.ExportState()));
        Assert.False((await first.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(first.ExportState()));
        for (var tick = 0; tick < 12; tick++)
        {
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
            Assert.True((await second.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(first.ExportState()), PrivateWorldRuntimeCodec.Encode(second.ExportState()));
        }
        first.Validate();
        second.Validate();
    }

    private static async Task Advance(PrivateWorldRuntime world, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done, int limit = 700)
    {
        for (var tick = 0; tick < limit && !done(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(), "tick=" + world.WorldTick + " | " + string.Join(" | ", world.ExportState().Events.TakeLast(14)
            .Select(item => item.Kind + ":" + item.Detail)));
    }

    private sealed class PipelineProvider(params string[] prefixes) : IDecisionProvider
    {
        public ConcurrentQueue<InhabitantObservation> SeenObservations { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            SeenObservations.Enqueue(request.Observation);
            var choice = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id == prefix || candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(candidate => candidate is not null) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}
