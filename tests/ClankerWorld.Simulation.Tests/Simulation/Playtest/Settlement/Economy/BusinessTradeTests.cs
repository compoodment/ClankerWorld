using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    private const string Farmhouse = "first-town-farmhouse";

    [Fact]
    public async Task HouseholdBusinessAndCustomerMakeExactBarterThroughOrdinaryChoicesAcrossReload()
    {
        var state = Initial("business-ordinary");
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        state = Stock(Stock(state, "a-business-fruit", "fruit", Alpha, 5, Farmhouse), "buyer-payment", "wood", Beta, 2, "first-town-house-b");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == buyer
            ? person with { HungerBasisPoints = 7_000 } : person).ToArray()
        };
        IDecisionProvider Provider(string actor) => new Preferred(actor == seller ? ["business_serve:", "business_list:"] :
            actor == buyer ? ["business_collect:", "business_buy:", "business_payment:"] : []);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 240 && !world.BusinessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Settled); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.BusinessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Open))
                {
                    Assert.Equal(5, world.Society.Inventory.GetLot("a-business-fruit").Quantity);
                    Assert.Equal(1, world.Society.Inventory.GetLot("buyer-payment").Quantity);
                    Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ItemKind == "wood" &&
                        lot.Quantity == 1 && lot.ProvenanceLotId == "buyer-payment" && lot.StorageBuildingId is null);
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            var offer = Assert.Single(world.BusinessTrade.Offers, offer => offer.State == BusinessOfferState.Settled);
            Assert.Equal(buyer, offer.BuyerId);
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ItemKind == "fruit" &&
                lot.Quantity == 1 && lot.StorageBuildingId is null);
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == Alpha && lot.ItemKind == "wood" &&
                lot.Quantity == 1 && lot.StorageBuildingId == Farmhouse && lot.ProvenanceLotId == "buyer-payment");
            Assert.Equal(5, world.Society.Inventory.Lots.Where(lot => lot.Id == "a-business-fruit" ||
                lot.ProvenanceLotId == "a-business-fruit").Sum(lot => lot.Quantity));
            Assert.DoesNotContain(world.Society.Households.Single(household => household.Id == Alpha).MemberIds, id => id == buyer);
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public async Task AcceptedGoodsExpireWithoutTransfersAndASecondCustomerCannotReserveTheSameUnit()
    {
        var state = Initial("business-expiry");
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        var competitor = state.Society.Society.Inhabitants.Last(person => person.HouseholdId == Beta).Id;
        state = Stock(Stock(Stock(state, "only-stock", "flour", Alpha, 1, Farmhouse), "buyer-payment", "wood", buyer, 1),
            "competing-payment", "wood", competitor, 1);
        state = At(state, seller, Building(state, Farmhouse).Position);
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var listing = world.ListBusinessGoods(seller, Farmhouse, "only-stock", 1, "wood", 1);
        Assert.True(listing.Applied, listing.Failure);
        var accepted = world.AcceptBusinessListing(buyer, listing.Id!, "buyer-payment");
        Assert.True(accepted.Applied, accepted.Failure);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.AcceptBusinessListing(competitor, listing.Id!, "competing-payment").Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new Preferred([]));
        for (var tick = 0; tick < 121; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(BusinessOfferState.Cancelled, restored.BusinessTrade.Offers.Single(offer => offer.Id == accepted.Id).State);
        Assert.Equal(Alpha, restored.Society.Inventory.GetLot("only-stock").OwnerId);
        Assert.Equal(buyer, restored.Society.Inventory.GetLot("buyer-payment").OwnerId);
        Assert.All(restored.Society.Inventory.Reservations.Where(reservation => reservation.Id.StartsWith(accepted.Id!, StringComparison.Ordinal)),
            reservation => Assert.Equal(InventoryReservationState.Released, reservation.State));
        Assert.Equal(1, restored.Society.Inventory.GetLot("only-stock").Quantity);
        restored.Validate();
    }

    [Fact]
    public void FullReceivingSpaceAndRemoteSaleStockAreRefusedWithoutChangingState()
    {
        var state = Initial("business-capacity");
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        state = Stock(Stock(Stock(state, "remote-fruit", "fruit", Alpha, 3, "first-town-house-a"),
            "bulk-seeds", "grain_seed", Alpha, 8, Farmhouse), "full-hands-payment", "wood", buyer, 32);
        state = At(state, seller, Building(state, Farmhouse).Position);
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.ListBusinessGoods(seller, Farmhouse, "remote-fruit", 1, "wood", 1).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var listing = world.ListBusinessGoods(seller, Farmhouse, "bulk-seeds", 8, "wood", 1);
        Assert.True(listing.Applied, listing.Failure);
        before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.AcceptBusinessListing(buyer, listing.Id!, "full-hands-payment").Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Empty(world.BusinessTrade.Offers);
        Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.Purpose == "business_exchange");
    }

    [Fact]
    public async Task StoreHaulsPartialLoadsFromHouseBeforeTheCustomerCanBuyThem()
    {
        var state = Initial("business-store-delivery");
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        state = Stock(Stock(Stock(Stock(state, "store-cost-wood", "wood", Alpha, 8, "first-town-house-a"),
            "store-cost-stone", "stone", Alpha, 2, "first-town-house-a"),
            "a-store-fruit", "fruit", Alpha, 10, "first-town-house-a"), "buyer-payment", "wood", buyer, 2);
        using var setup = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var definition = setup.WorldContent.Buildings.Single(definition => definition.LocalId == "store-1x1");
        Assert.Contains(state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && state.Map.IsReachableFromCampOnFoot(tile.Position))
            .OrderBy(tile => state.Map.FootDistance(Building(state, Farmhouse).Position, tile.Position)), tile =>
            setup.PlaceBuilding("test-store", definition.CanonicalId, tile.Position, Alpha).Applied);
        state = setup.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == buyer
            ? person with { HungerBasisPoints = 7_000 } : person).ToArray()
        };
        IDecisionProvider Provider(string actor) => new Preferred(actor == seller ?
            ["business_serve:", "business_list:test-store", "haul_household_stock", "business_stock:test-store"] :
            actor == buyer ? ["business_collect:", "business_buy:"] : []);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 360 && !world.Society.Inventory.Lots.Any(lot => lot.OwnerId == buyer && lot.ItemKind == "fruit"); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.Society.Inventory.Lots.Any(lot => lot.OwnerId == seller && lot.DeliveryBuildingId == "test-store" && lot.ItemKind == "fruit"))
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    Assert.DoesNotContain(world.BusinessTrade.Listings, listing => listing.BuildingId == "test-store");
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ItemKind == "fruit");
            Assert.Equal(10, world.Society.Inventory.Lots.Where(lot => lot.Id == "a-store-fruit" ||
                lot.ProvenanceLotId == "a-store-fruit" || lot.ProvenanceLotId?.StartsWith("a-store-fruit#transfer:", StringComparison.Ordinal) == true)
                .Sum(lot => lot.Quantity));
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public async Task OrdinaryMarketBuildReservesItsPlotAndVisitorBuysOnlyListedStockThenStallIsReleased()
    {
        var state = await MarketConstructionStart();
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        using var builder = PrivateWorldRuntime.Restore(state, actor => new Preferred(actor == seller
            ? ["continue_project", "build:building:" + BusinessContent.Market().CanonicalId] : []));
        for (var tick = 0; tick < 360 && builder.BusinessTrade.Markets.Count == 0; tick++)
            Assert.True((await builder.AdvanceOneTickAsync()).Advanced);
        var plot = Assert.Single(builder.BusinessTrade.Markets);
        Assert.Equal(30, builder.MarketStallPositions(plot.MarketId).Count);
        Assert.DoesNotContain(builder.Society.Inventory.Lots, lot => lot.Id is "market-cost-wood" or "market-cost-stone");
        state = Stock(Stock(builder.ExportState(), "stall-fruit", "fruit", seller, 6), "visitor-wood", "wood", buyer, 1);
        var position = builder.MarketStallPositions(plot.MarketId)[0];
        state = At(state, seller, position);
        var visitorTown = TownBorderRules.CreateFirstTown(state.Map, [buyer], founded: true, originSite: state.Inhabitants
            .Single(person => person.InhabitantId == buyer).Position) with
        { Id = "town:visitor", Name = "Visitor Town" };
        state = state with
        {
            Towns = state.Towns!.Select(town => town with
            { ResidentIds = town.ResidentIds.Where(id => id != buyer).ToArray() }).Append(visitorTown).ToArray(),
            TownCouncils = state.TownCouncils!.Select(council => council with { MemberIds = council.MemberIds.Where(id => id != buyer).ToArray() })
                .Append(new(visitorTown.Id, "open", builder.WorldTick, [buyer], Laws: [])).ToArray()
        };
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == buyer
            ? person with { HungerBasisPoints = 7_000 } : person).ToArray()
        };
        using var together = PrivateWorldRuntime.Restore(state, actor => new Preferred(actor == seller
            ? ["business_serve:", "business_list:", "market_deliver:"] : actor == buyer
                ? ["business_collect:", "business_buy:"] : []));
        for (var tick = 0; tick < 240 && !together.BusinessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Settled); tick++)
            Assert.True((await together.AdvanceOneTickAsync()).Advanced);
        var stall = Assert.Single(together.BusinessTrade.Stalls);
        Assert.Contains(together.BusinessTrade.Offers, offer => offer.State == BusinessOfferState.Settled && offer.BuyerId == buyer);
        Assert.Single(together.BusinessTrade.Stalls);
        var receipt = together.Society.Inventory.Lots.Single(lot => lot.StorageBuildingId == stall.BuildingId && lot.ItemKind == "wood");
        Assert.Equal("wood", receipt.ItemKind);
        Assert.Equal(Alpha, receipt.OwnerId);
        var bytes = PrivateWorldRuntimeCodec.Encode(together.ExportState());
        Assert.False(together.WithdrawBusinessStock(buyer, stall.BuildingId, receipt.Id, 1).Applied);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(together.ExportState()));
        using var withdrawing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor => new Preferred(actor == seller
            ? ["market_withdraw:", "haul_household_stock"] : []));
        for (var tick = 0; tick < 120 && withdrawing.Society.Inventory.Lots.Any(lot =>
            lot.StorageBuildingId == stall.BuildingId && lot.ItemKind == "fruit"); tick++)
            Assert.True((await withdrawing.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(withdrawing.Society.Inventory.Lots, lot => lot.StorageBuildingId == stall.BuildingId && lot.ItemKind == "fruit");
        Assert.Single(withdrawing.BusinessTrade.Stalls);
        Assert.Equal(Alpha, withdrawing.Society.Inventory.GetLot(receipt.Id).OwnerId);
        Assert.Contains(withdrawing.ExportState().Events, item => item.Kind == "business_stock_withdrawn");
        var returnState = At(withdrawing.ExportState(), seller, Building(withdrawing.ExportState(), stall.BuildingId).Position);
        using var collectingReceipt = PrivateWorldRuntime.Restore(returnState, _ => new Preferred([]));
        Assert.True(collectingReceipt.WithdrawBusinessStock(seller, stall.BuildingId, receipt.Id, 1).Applied);
        Assert.Empty(collectingReceipt.BusinessTrade.Stalls);
        Assert.DoesNotContain(collectingReceipt.WorldSimulation.Buildings, building => building.InstanceId == stall.BuildingId);
        Assert.Equal(seller, collectingReceipt.Society.Inventory.GetLot(receipt.Id).OwnerId);
        Assert.Contains(collectingReceipt.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ItemKind == "fruit");
        collectingReceipt.Validate();
    }

    [Fact]
    public void ReceivingPromisesPreventOverbookingAndRetainPlantingSeed()
    {
        var state = Initial("business-promised-space");
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        var second = state.Society.Society.Inhabitants.Last(person => person.HouseholdId == Beta).Id;
        state = WithInventory(state, state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != Alpha || lot.ItemKind != "grain_seed").ToArray() });
        state = Stock(Stock(Stock(state, "seed-stock", "grain_seed", Alpha, 3, Farmhouse), "pay-first", "wood", buyer, 9),
            "pay-second", "wood", second, 9);
        var site = Building(state, Farmhouse);
        var capacity = BuildingStorageRules.Capacity(state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == site.DefinitionId), site)!.Value;
        var used = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == Farmhouse).Sum(lot => lot.Quantity);
        state = Stock(state, "storage-filler", "stone", Alpha, capacity - used - 8, Farmhouse);
        using var world = PrivateWorldRuntime.Restore(At(state, seller, site.Position), _ => new Preferred([]));
        var firstListing = world.ListBusinessGoods(seller, Farmhouse, "seed-stock", 1, "wood", 9);
        Assert.True(firstListing.Applied, firstListing.Failure);
        var accepted = world.AcceptBusinessListing(buyer, firstListing.Id!, "pay-first");
        Assert.True(accepted.Applied, accepted.Failure);
        Assert.Equal(8, Assert.Single(world.BusinessTrade.Offers).ReservedStorageSpace);
        var secondListing = world.ListBusinessGoods(seller, Farmhouse, "seed-stock", 1, "wood", 9);
        Assert.True(secondListing.Applied, secondListing.Failure);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.AcceptBusinessListing(second, secondListing.Id!, "pay-second").Applied);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False(world.ListBusinessGoods(seller, Farmhouse, "seed-stock", 2, "wood", 9).Applied);
        Assert.True(world.CancelBusinessOffer(buyer, accepted.Id!).Applied);
        Assert.True(world.AcceptBusinessListing(second, secondListing.Id!, "pay-second").Applied);
        world.Validate();
    }

    [Fact]
    public void VesselExchangeReservesAllContentsAndRejectsChangedSavedPromises()
    {
        var state = Initial("business-vessel-sale");
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        const string tailor = "sale-tailor";
        state = Stock(Stock(state, "store-cost-wood", "wood", Alpha, 8), "store-cost-stone", "stone", Alpha, 2);
        using var setup = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var store = setup.WorldContent.Buildings.Single(definition => definition.LocalId == "store-1x1");
        Assert.Contains(state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && state.Map.IsReachableFromCampOnFoot(tile.Position))
            .OrderBy(tile => state.Map.FootDistance(Building(state, Farmhouse).Position, tile.Position)), tile =>
            setup.PlaceBuilding(tailor, store.CanonicalId, tile.Position, Alpha).Applied);
        state = setup.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "stocked-pot", "storage_pot", Alpha, 1,
            storageBuildingId: tailor, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "pot-berries", "berries", Alpha, 8,
            storageBuildingId: tailor, containerLotId: "stocked-pot");
        state = WithInventory(state, inventory);
        state = Stock(state, "buyer-payment", "wood", buyer, 1);
        var site = Building(state, tailor);
        state = At(At(state, seller, site.Position), buyer, state.Map.FootNeighbors(site.Position).First());
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var listing = world.ListBusinessGoods(seller, tailor, "stocked-pot", 1, "wood", 1);
        Assert.True(listing.Applied, listing.Failure);
        var accepted = world.AcceptBusinessListing(buyer, listing.Id!, "buyer-payment");
        Assert.True(accepted.Applied, accepted.Failure);
        Assert.Equal(8, Assert.Single(world.BusinessTrade.Offers).ReservedCarrySpace);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Reserve(world.Society.Inventory, "unrelated-meal", Alpha,
            "pot-berries", 1, "cooking", world.WorldTick + 10));
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var offer = Assert.Single(saved.BusinessTrade!.Offers);
        var corrupted = saved with { BusinessTrade = saved.BusinessTrade with { Offers = [offer with { ReservedCarrySpace = 0 }] } };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(corrupted));
        using var restored = PrivateWorldRuntime.Restore(saved, _ => new Preferred([]));
        Assert.True(restored.SettleBusinessOffer(buyer, accepted.Id!).Applied);
        Assert.Equal(buyer, restored.Society.Inventory.GetLot("stocked-pot").OwnerId);
        var contents = restored.Society.Inventory.GetLot("pot-berries");
        Assert.Equal(buyer, contents.OwnerId);
        Assert.Equal("stocked-pot", contents.ContainerLotId);
        Assert.Equal(8, contents.Quantity);
        Assert.Null(contents.StorageBuildingId);
        restored.Validate();
    }

    [Fact]
    public void RejectedStockCleanupKeepsTheVesselAndSpoiledContentsOnLegalGroundAcrossReload()
    {
        var state = Initial("business-stale-cleanup");
        var seller = Actor(state, Alpha);
        var site = Building(state, Farmhouse);
        var ground = state.Map.FootNeighbors(site.Position).First(point => state.Map.IsBuildable(point) &&
            !state.WorldSimulation!.Buildings.Any(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Contains(point)) &&
            !state.Map.Resources.Any(resource => resource.Position == point) && !state.Map.CampObjects.Any(item => item.Position == point));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "stale-pot", "storage_pot", Alpha, 1,
            storageBuildingId: Farmhouse, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "stale-berries", "berries", Alpha, 8, freshnessBasisPoints: 0,
            storageBuildingId: Farmhouse, containerLotId: "stale-pot");
        using var world = PrivateWorldRuntime.Restore(At(WithInventory(state, inventory), seller, ground), _ => new Preferred([]));
        Assert.True(world.ClearRejectedBusinessStock(seller, Farmhouse, "stale-pot").Applied);
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.Id is "stale-pot" or "stale-berries"), lot =>
        {
            Assert.Equal(Alpha, lot.OwnerId);
            Assert.Null(lot.StorageBuildingId);
            Assert.Equal(new InventoryGroundPosition(ground.X, ground.Y), lot.GroundPosition);
        });
        Assert.Equal(0, world.Society.Inventory.GetLot("stale-berries").FreshnessBasisPoints);
        Assert.Equal(8, world.Society.Inventory.GetLot("stale-berries").Quantity);
        Assert.Equal("stale-pot", world.Society.Inventory.GetLot("stale-berries").ContainerLotId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new Preferred([]));
        Assert.Equal(0, CarryEquipmentRules.Load(restored.Society.Inventory, seller));
        Assert.False(restored.WithdrawBusinessStock(seller, Farmhouse, "stale-pot", 1).Applied);
        restored.Validate();
    }

    [Fact]
    public async Task RequestedToolReportsMissingInputsThenConsumesActualStockAndStaysPrivateUntilPaid()
    {
        var state = Initial("business-tool-order");
        var buyer = Actor(state, Alpha);
        var smith = Actor(state, Beta);
        const string blacksmith = "first-town-blacksmith";
        state = At(At(state, smith, Building(state, blacksmith).Position), buyer,
            state.Map.FootNeighbors(Building(state, blacksmith).Position).First());
        using var waiting = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var requested = waiting.RequestBusinessTool(buyer, blacksmith, "iron_hoe");
        Assert.True(requested.Applied, requested.Failure);
        var order = Assert.Single(waiting.BusinessTrade.ToolOrders);
        Assert.Contains("Missing actual inputs", order.Blocker, StringComparison.Ordinal);
        var recipe = waiting.WorldContent.Recipes.Single(recipe => recipe.CanonicalId == order.RecipeId);
        state = waiting.ExportState();
        foreach (var input in recipe.Inputs) state = Stock(state, "order-input-" + input.ResourceId, input.ResourceId, Beta, input.Amount, blacksmith);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), actor =>
            new Preferred(actor == smith ? ["business_make_tool:"] : []));
        for (var tick = 0; tick < 90 && !world.BusinessTrade.ToolOrders.Any(order => order.State == "ready"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var completed = world.BusinessTrade.ToolOrders.Single(order => order.Id == requested.Id);
        Assert.Equal("ready", completed.State);
        Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.JobId == completed.JobId && job.State == WorldProductionJobState.Completed);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == Beta && lot.StorageBuildingId == blacksmith && lot.ItemKind == "iron_hoe");
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == buyer && lot.ItemKind == "iron_hoe");
        Assert.All(recipe.Inputs, input => Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "order-input-" + input.ResourceId));
        world.Validate();
    }

    private static PrivateWorldRuntimeState Initial(string seed)
    {
        using var world = NormalPathWorld.CreateGenerated(seed, _ => new Preferred([]));
        return world.ExportState() with
        {
            Survival = new SettlementSurvivalState(world.WorldTick, []),
            Inhabitants = world.Inhabitants.Select(person => person with { HungerBasisPoints = 9_000, Survival = new SurvivalCondition() }).ToArray(),
        };
    }

    private static string Actor(PrivateWorldRuntimeState state, string household) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;

    private static async Task<PrivateWorldRuntimeState> MarketConstructionStart()
    {
        foreach (var seed in new[] { "business-market-visitor", "probe-a", "market-clear-1", "market-clear-2", "market-clear-3", "market-clear-4", "market-clear-5", "market-clear-6" })
        {
            var state = Initial(seed);
            var seller = Actor(state, Alpha);
            state = Stock(Stock(state, "market-cost-wood", "wood", Alpha, 20, "first-town-house-a"),
                "market-cost-stone", "stone", Alpha, 8, "first-town-house-a");
            var chooser = new Preferred(["continue_project", "build:building:" + BusinessContent.Market().CanonicalId]);
            using var world = PrivateWorldRuntime.Restore(state, actor => actor == seller ? chooser : new Preferred([]));
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (chooser.Offered.Any(candidate => candidate.StartsWith("build:building:" + BusinessContent.Market().CanonicalId, StringComparison.Ordinal)))
                return world.ExportState();
        }
        throw new InvalidOperationException("The bounded generated Market fixtures did not contain the required clear plot.");
    }

    private static PlacedBuilding Building(PrivateWorldRuntimeState state, string id) => state.WorldSimulation!.Buildings.Single(building => building.InstanceId == id);

    private static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, string actor, GridPoint position) => state with
    { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = position } : person).ToArray() };

    private static PrivateWorldRuntimeState Stock(PrivateWorldRuntimeState state, string id, string kind, string owner,
        int quantity, string? building = null) => state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                { Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, id, kind, owner, quantity, storageBuildingId: building) }
            },
        };

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Preferred(string[] prefixes) : IDecisionProvider
    {
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            var selected = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1, new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}
