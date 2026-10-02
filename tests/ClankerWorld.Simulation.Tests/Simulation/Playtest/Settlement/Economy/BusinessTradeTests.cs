using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class BusinessTradeTests
{
    [Fact]
    public async Task OlderOpenExchangeRemainsVisibleWhenRecentCancelledOffersFillHistory()
    {
        var (state, buyer, _, shopId) = CreateShopState();
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer
            ? new ShopProvider("business_shop:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && world.BusinessTrades.Count == 0; step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var open = Assert.Single(world.BusinessTrades);
        for (var step = 0; step < 8; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        state = world.ExportState();
        var inventory = state.Society.Society.Inventory;
        var trades = state.BusinessTrades!.ToList();
        for (var index = 1; index <= 8; index++)
        {
            var proposed = open.ProposedTick + index;
            var id = $"business-trade:{proposed}:{buyer}:{shopId}";
            inventory = InventoryFixture.CreateDirectBarterOffer(inventory,
                new(id, 1, open.SellerHouseholdId, buyer, "shop-axe", 1, "buyer-payment", 3,
                    state.Society.Society.WorldTick + 120));
            inventory = InventoryFixture.CancelDirectBarterOffer(inventory, id, 1, open.SellerHouseholdId);
            trades.Add(open with { OfferId = id, ProposedTick = proposed, CancellationReason = "The seller declined." });
        }
        state = WithInventory(state, inventory) with
        {
            BusinessTrades = trades.OrderBy(trade => trade.OfferId, StringComparer.Ordinal).ToArray(),
        };
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new ShopProvider("safe_idle"));
        var snapshot = new OwnerWorldObservationStore(restored).GetSnapshot();
        var visible = snapshot.PlacedBuildings.Single(building => building.InstanceId == shopId).Trades;
        Assert.Equal(8, visible.Count);
        Assert.Equal(open.OfferId, visible[0].OfferId);
        Assert.Equal("open", visible[0].Status);
        Assert.Contains(snapshot.Inhabitants.Single(person => person.Id == buyer).SocialNotes,
            note => note.Contains("Both traders must meet there", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "wooden_axe", 3)]
    [InlineData(true, "wooden_axe", 3)]
    [InlineData(false, "iron", 1)]
    [InlineData(true, "iron", 1)]
    public async Task ShopBarterReservesExactOnSiteGoodsAndSettlesAfterReload(
        bool visitorFromAnotherTown, string goodsKind, int paymentUnits)
    {
        var (state, buyer, seller, shopId) = CreateShopState();
        if (goodsKind == "iron") state = WithBuyerBlacksmith(state, buyer);
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "shop-axe"
                ? lot with { ItemKind = goodsKind } : lot).ToArray(),
        });
        if (visitorFromAnotherTown)
        {
            var firstTown = Assert.Single(state.Towns!);
            var visitorHome = state.Map.Tiles.Select(tile => tile.Position).First(position =>
                state.Map.IsLand(position) && !firstTown.BorderTiles.Contains(position));
            state = state with
            {
                Towns = [firstTown with { ResidentIds = firstTown.ResidentIds.Where(id => id != buyer).ToArray() },
                    new TownRuntimeState("town:visitor-home", "Visitor Home", "founded", state.Society.Society.WorldTick,
                        [buyer], [], [visitorHome], visitorHome)],
            };
        }
        var buyerProvider = new ShopProvider("business_shop:");
        using var offering = PrivateWorldRuntime.Restore(state, id => id == buyer ? buyerProvider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && offering.BusinessTrades.Count == 0; step++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        var binding = Assert.Single(offering.BusinessTrades);
        var offer = offering.Society.Inventory.GetOffer(binding.OfferId);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.Equal(1, offer.FirstQuantity);
        Assert.Equal(paymentUnits, offer.SecondQuantity);
        Assert.Equal("shop-axe", offer.FirstLotId);
        Assert.Equal("buyer-payment", offer.SecondLotId);
        Assert.Equal(shopId, offering.Society.Inventory.GetLot(offer.FirstLotId).StorageBuildingId);
        Assert.Equal(2, offering.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.Equal(6, offering.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.All(offering.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "barter:" + offer.Id),
            reservation => Assert.Equal(InventoryReservationState.Reserved, reservation.State));
        var snapshot = new OwnerWorldObservationStore(offering).GetSnapshot();
        var displayed = Assert.Single(snapshot.PlacedBuildings.Single(building => building.InstanceId == shopId).Trades);
        Assert.Equal(goodsKind, displayed.GoodsKind);
        Assert.Equal(1, displayed.GoodsQuantity);
        Assert.Equal("wood", displayed.PaymentKind);
        Assert.Equal(paymentUnits, displayed.PaymentQuantity);
        Assert.Equal("open", displayed.Status);
        Assert.Contains(snapshot.Inhabitants.Single(person => person.Id == buyer).SocialNotes,
            note => note.Contains("Both traders must meet there", StringComparison.Ordinal));
        var bytes = PrivateWorldRuntimeCodec.Encode(offering.ExportState());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(bytes)));
        state = PrivateWorldRuntimeCodec.Decode(bytes);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            BusinessTrades = [.. state.BusinessTrades!, null!],
        }));
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        var woodBefore = TotalQuantity(state.Society.Society.Inventory, "wood");
        var goodsBefore = TotalQuantity(state.Society.Society.Inventory, goodsKind);
        using var settling = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new ShopProvider("business_continue:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Open; step++)
            Assert.True((await settling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Settled, settling.Society.Inventory.GetOffer(offer.Id).State);
        var purchased = Assert.Single(settling.Society.Inventory.Lots, lot => lot.OwnerId == buyer &&
            lot.ItemKind == goodsKind && lot.ProvenanceLotId == "shop-axe");
        Assert.Equal(1, purchased.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, buyer));
        var payment = Assert.Single(settling.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "buyer-payment");
        Assert.Equal(paymentUnits, payment.Quantity);
        Assert.Equal(binding.SellerHouseholdId, payment.OwnerId);
        Assert.Equal(shopId, payment.StorageBuildingId);
        Assert.Null(payment.DeliveryBuildingId);
        Assert.Equal(6 - paymentUnits, settling.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.Equal(woodBefore, TotalQuantity(settling.Society.Inventory, "wood"));
        Assert.Equal(goodsBefore, TotalQuantity(settling.Society.Inventory, goodsKind));
        Assert.Equal(state.Society.Society.GetInhabitant(buyer).HouseholdId, settling.Society.GetInhabitant(buyer).HouseholdId);
        Assert.Equal(visitorFromAnotherTown ? "town:visitor-home" : TownBorderRules.FirstTownId,
            settling.Towns.Single(town => town.ResidentIds.Contains(buyer)).Id);
        var recipe = settling.WorldContent.Recipes.Single(item => item.WorkstationBuildingId ==
            state.WorldSimulation!.Buildings.Single(building => building.InstanceId == shopId).DefinitionId &&
            item.Outputs.Any(output => output.ResourceId == "wooden_axe"));
        var privateWork = settling.StartProduction(recipe.CanonicalId, shopId, buyer);
        Assert.False(privateWork.Applied);
        Assert.Contains("household", privateWork.Failure!, StringComparison.OrdinalIgnoreCase);
        displayed = Assert.Single(new OwnerWorldObservationStore(settling).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == shopId).Trades);
        Assert.Equal("settled", displayed.Status);
        settling.Validate();
    }

    [Theory]
    [InlineData("blacksmith", "iron_axe", "wooden_axe", 10_000, true)]
    [InlineData("blacksmith", "stone_axe", "iron_axe", 10_000, false)]
    [InlineData("blacksmith", "wooden_axe", "wooden_axe", 10_000, false)]
    [InlineData("blacksmith", "wooden_axe", "iron_axe", 0, true)]
    [InlineData("tailor", "rain_cloak", "clothing", 1_000, true)]
    [InlineData("tailor", "clothing", "padded_coat", 10_000, false)]
    [InlineData("tailor", "padded_coat", "padded_coat", 10_000, false)]
    public async Task ShopBuyersConsiderUsefulEquipmentUpgradesWithoutBuyingInferiorReplacements(
        string shopKind, string goodsKind, string heldKind, int heldCondition, bool wants)
    {
        var (state, buyer, seller, shopId) = CreateShopState(shopKind);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != shopId).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "upgrade-goods", goodsKind,
            state.Society.Society.GetInhabitant(seller).HouseholdId!, 1, storageBuildingId: shopId);
        inventory = InventoryFixture.AddLot(inventory, "held-equipment", heldKind, buyer, 1,
            conditionBasisPoints: heldCondition);
        if (shopKind == "tailor")
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == "buyer-payment"
                    ? lot with { ItemKind = "cloth" } : lot).ToArray(),
            };
        state = WithInventory(state, inventory);
        var buyerProvider = new ShopProvider("business_shop:");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == buyer ? buyerProvider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 10 && (wants ? world.BusinessTrades.Count == 0 : buyerProvider.Seen.IsEmpty); step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(buyerProvider.Seen);
        Assert.Equal(wants, buyerProvider.Seen.Contains("business_shop:" + shopId));
        Assert.Equal(wants ? 1 : 0, world.BusinessTrades.Count);
        Assert.Equal(inventory.GetLot("held-equipment") with
        {
            LastProcessedTick = world.Society.Inventory.GetLot("held-equipment").LastProcessedTick,
        }, world.Society.Inventory.GetLot("held-equipment"));
        if (!wants)
        {
            Assert.Equal(1, world.Society.Inventory.GetLot("upgrade-goods").Quantity);
            world.Validate();
            return;
        }
        var trade = Assert.Single(world.BusinessTrades);
        var offer = world.Society.Inventory.GetOffer(trade.OfferId);
        Assert.Equal("upgrade-goods", offer.FirstLotId);
        Assert.Equal(1, offer.FirstQuantity);
        var totalBefore = world.Society.Inventory.Lots.Sum(lot => lot.Quantity);
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        using var resumed = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new ShopProvider("business_continue:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && resumed.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Open; step++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Settled, resumed.Society.Inventory.GetOffer(offer.Id).State);
        Assert.Equal(buyer, resumed.Society.Inventory.GetLot("upgrade-goods").OwnerId);
        Assert.Equal(buyer, resumed.Society.Inventory.GetLot("held-equipment").OwnerId);
        Assert.Equal(totalBefore, resumed.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        resumed.Validate();
    }

    [Theory]
    [InlineData("wooden_axe", false)]
    [InlineData("wooden_axe", true)]
    [InlineData("wooden_pickaxe", false)]
    [InlineData("wooden_pickaxe", true)]
    public async Task StorePaymentPreservesTheBuyersBestUsableTool(string heldKind, bool hasMaterialPayment)
    {
        var (state, buyer, seller, shopId) = CreateShopState("store");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != shopId &&
                (hasMaterialPayment || lot.Id != "buyer-payment")).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "upgrade-goods", "iron_axe",
            state.Society.Society.GetInhabitant(seller).HouseholdId!, 1, storageBuildingId: shopId);
        inventory = InventoryFixture.AddLot(inventory, "a-held-tool", heldKind, buyer, 1);
        state = WithInventory(state, inventory);
        var provider = new ShopProvider("business_shop:");
        using var offering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            actor => actor == buyer ? provider : new ShopProvider("safe_idle"));
        for (var tick = 0; tick < 10 && (hasMaterialPayment ? offering.BusinessTrades.Count == 0 : provider.Seen.IsEmpty); tick++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(provider.Seen);
        Assert.Equal(hasMaterialPayment, provider.Seen.Contains("business_shop:" + shopId));
        Assert.Equal(buyer, offering.Society.Inventory.GetLot("a-held-tool").OwnerId);
        Assert.Equal(1, offering.Society.Inventory.GetLot("a-held-tool").Quantity);
        if (!hasMaterialPayment)
        {
            Assert.Empty(offering.BusinessTrades);
            Assert.DoesNotContain(offering.Society.Inventory.Reservations,
                reservation => reservation.Purpose.StartsWith("barter:", StringComparison.Ordinal));
            Assert.Equal(1, offering.Society.Inventory.GetLot("upgrade-goods").Quantity);
            offering.Validate();
            return;
        }
        var trade = Assert.Single(offering.BusinessTrades);
        var offer = offering.Society.Inventory.GetOffer(trade.OfferId);
        Assert.Equal("buyer-payment", offer.SecondLotId);
        Assert.Equal(1, offer.SecondQuantity);
        var beforeSettlement = offering.Society.Inventory.Lots.Sum(lot => lot.Quantity);
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(offering.ExportState()));
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        using var settling = PrivateWorldRuntime.Restore(state, actor => actor == seller
            ? new ShopProvider("business_continue:") : new ShopProvider("safe_idle"));
        for (var tick = 0; tick < 40 && settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Open; tick++)
            Assert.True((await settling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Settled, settling.Society.Inventory.GetOffer(offer.Id).State);
        Assert.Equal(buyer, settling.Society.Inventory.GetLot("a-held-tool").OwnerId);
        Assert.Equal(1, settling.Society.Inventory.GetLot("a-held-tool").Quantity);
        Assert.Equal(buyer, settling.Society.Inventory.GetLot("upgrade-goods").OwnerId);
        Assert.Equal(beforeSettlement, settling.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        settling.Validate();
    }

    [Fact]
    public async Task AShopCannotOfferTheSameHouseholdsRemoteStock()
    {
        var (state, buyer, _, shopId) = CreateShopState();
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "shop-axe"
                ? lot with { StorageBuildingId = null } : lot).ToArray(),
        });
        var provider = new ShopProvider("business_shop:");
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer ? provider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 35; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("business_shop:" + shopId, provider.Seen);
        Assert.Empty(world.BusinessTrades);
        Assert.DoesNotContain(world.Society.Inventory.Offers, offer => offer.Id.StartsWith("business-trade:", StringComparison.Ordinal));
        Assert.Equal(2, world.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.Equal(6, world.Society.Inventory.GetLot("buyer-payment").Quantity);
        world.Validate();
    }

    [Theory]
    [InlineData("no-workshop")]
    [InlineData("reserved")]
    [InlineData("remote")]
    public async Task RefinedIronSaleRequiresRealAvailableShopStockAndAUseForTheBuyer(string refusal)
    {
        var (state, buyer, _, shopId) = CreateShopState();
        if (refusal != "no-workshop") state = WithBuyerBlacksmith(state, buyer);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "shop-axe"
                ? lot with { ItemKind = "iron", StorageBuildingId = refusal == "remote" ? "first-town-house-b" : shopId }
                : lot).ToArray(),
        };
        if (refusal == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "iron-reserved-for-work", "household:camp-beta",
                "shop-axe", 2, "pending_trade", state.Society.Society.WorldTick + 120);
        state = WithInventory(state, inventory);
        var provider = new ShopProvider("business_shop:");
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == buyer ? provider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && provider.Seen.IsEmpty; step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(provider.Seen);
        Assert.DoesNotContain("business_shop:" + shopId, provider.Seen);
        Assert.Empty(world.BusinessTrades);
        Assert.Equal(2, world.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.Equal(6, world.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.Equal(refusal == "remote" ? "first-town-house-b" : shopId,
            world.Society.Inventory.GetLot("shop-axe").StorageBuildingId);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new ShopProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        restored.Validate();
        Assert.Empty(restored.BusinessTrades);
        Assert.Equal(2, restored.Society.Inventory.GetLot("shop-axe").Quantity);
        if (refusal == "reserved")
            Assert.Equal(InventoryReservationState.Reserved,
                restored.Society.Inventory.GetReservation("iron-reserved-for-work").State);
    }

    [Fact]
    public async Task FullShopRefusesAQuoteWhosePaymentWouldOverflowItsStock()
    {
        var (state, buyer, _, shopId) = CreateShopState();
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == shopId);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == shop.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(definition, shop)!.Value;
        var stock = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == shopId).Sum(lot => lot.Quantity);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "full-shop-ballast", "stone", shop.HouseholdId!, capacity - stock, storageBuildingId: shopId));
        var provider = new ShopProvider("business_shop:");
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer ? provider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 35; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("business_shop:" + shopId, provider.Seen);
        Assert.Empty(world.BusinessTrades);
        Assert.Equal(capacity, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == shopId).Sum(lot => lot.Quantity));
        Assert.Equal(6, world.Society.Inventory.GetLot("buyer-payment").Quantity);
        world.Validate();
    }

    [Fact]
    public async Task ExpiryReleasesBothLotsWithoutChangingTheirOwnerOrLocation()
    {
        var (state, buyer, _, _) = CreateShopState();
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer
            ? new ShopProvider("business_shop:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && world.BusinessTrades.Count == 0; step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var binding = Assert.Single(world.BusinessTrades);
        var offer = world.Society.Inventory.GetOffer(binding.OfferId);
        state = world.ExportState();
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new ShopProvider("safe_idle"));
        while (restored.WorldTick <= offer.ExpiryTick) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Cancelled, restored.Society.Inventory.GetOffer(offer.Id).State);
        Assert.All(restored.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "barter:" + offer.Id),
            reservation => Assert.Equal(InventoryReservationState.Released, reservation.State));
        Assert.Equal(binding.SellerHouseholdId, restored.Society.Inventory.GetLot("shop-axe").OwnerId);
        Assert.Equal(binding.BuildingInstanceId, restored.Society.Inventory.GetLot("shop-axe").StorageBuildingId);
        Assert.Equal(2, restored.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(restored.Society.Inventory.GetLot("buyer-payment"), buyer));
        Assert.Equal(6, restored.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.NotNull(Assert.Single(restored.BusinessTrades).CancellationReason);
        restored.Validate();
    }

    [Fact]
    public async Task StoreStockRequiresPickupAndDeliveryAndRetainsItsPartialSourceAcrossReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new ShopProvider("safe_idle"));
        var state = generated.ExportState();
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId is not null &&
            state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var household = house.HouseholdId!;
        var seller = state.Society.Society.GetHousehold(household).MemberIds[0];
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "store-build-wood", "wood", household, 8);
        inventory = InventoryFixture.AddLot(inventory, "store-build-stone", "stone", household, 2);
        using var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ShopProvider("safe_idle"));
        var definition = placing.WorldContent.Buildings.Single(item => item.LocalId == "store-1x1");
        var position = state.Map.Tiles.Where(tile => tile.Position != house.Position)
            .OrderBy(tile => Math.Abs(tile.Position.X - house.Position.X) + Math.Abs(tile.Position.Y - house.Position.Y))
            .Select(tile => tile.Position).First(point => placing.PlaceBuilding("household-store", definition.CanonicalId, point, household).Applied);
        state = placing.ExportState();
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != household && lot.OwnerId != seller).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "store-source-cloth", "cloth", household, 12, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == seller ? person with
            {
                Position = house.Position,
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 8_000,
            } : person).ToArray(),
        };
        using var collecting = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new ShopProvider("haul_household_stock", "business_stock_store") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && !collecting.Society.Inventory.Lots.Any(lot => lot.OwnerId == seller &&
                 lot.DeliveryBuildingId == "household-store"); step++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var carried = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == seller &&
            lot.DeliveryBuildingId == "household-store");
        Assert.Equal(4, carried.Quantity);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(8, collecting.Society.Inventory.GetLot("store-source-cloth").Quantity);
        Assert.DoesNotContain(collecting.Society.Inventory.Lots, lot => lot.StorageBuildingId == "household-store");
        Assert.NotEqual(position, collecting.Inhabitants.Single(person => person.InhabitantId == seller).Position);
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState()));
        using var delivering = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new ShopProvider("haul_household_stock", "business_stock_store") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 80 && !delivering.Society.Inventory.Lots.Any(lot => lot.StorageBuildingId == "household-store"); step++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        var delivered = Assert.Single(delivering.Society.Inventory.Lots, lot => lot.StorageBuildingId == "household-store");
        Assert.Equal(household, delivered.OwnerId);
        Assert.Equal(4, delivered.Quantity);
        Assert.Null(delivered.DeliveryBuildingId);
        Assert.Equal(position, delivering.Inhabitants.Single(person => person.InhabitantId == seller).Position);
        Assert.Equal(8, delivering.Society.Inventory.GetLot("store-source-cloth").Quantity);
        Assert.Equal(12, delivering.Society.Inventory.Lots.Where(lot => lot.ItemKind == "cloth" &&
            (lot.Id == "store-source-cloth" || lot.ProvenanceLotId == "store-source-cloth")).Sum(lot => lot.Quantity));
        delivering.Validate();
    }

    [Theory]
    [InlineData("wooden_axe")]
    [InlineData("wooden_pickaxe")]
    public async Task StoreDoesNotOfferTheOnlyCarriedToolForStocking(string toolKind)
    {
        var (state, seller, household, houseId, storeId) = CreateStoreStockFixture();
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [], Offers = [] };
        inventory = InventoryFixture.AddLot(inventory, "only-stock-tool", toolKind, seller, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 8_000,
            }).ToArray(),
        };
        var provider = new ShopProvider("business_stock_store");
        using var world = PrivateWorldRuntime.Restore(state, id => id == seller ? provider : new ShopProvider("safe_idle"));
        Assert.Equal(household, world.Society.GetInhabitant(seller).HouseholdId);
        Assert.Contains(world.WorldSimulation.Buildings, building => building.InstanceId == storeId);
        Assert.Equal("only-stock-tool", ToolProgressionRules.BestUsableTool(world.Society.Inventory, seller,
            ToolProgressionRules.Find(toolKind)!.Family)!.Id);

        for (var step = 0; step < 35; step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains("safe_idle", provider.Seen);
        Assert.DoesNotContain("business_stock_store", provider.Seen);
        Assert.Equal("only-stock-tool", Assert.Single(world.Society.Inventory.Lots).Id);
        Assert.Equal(seller, world.Society.Inventory.GetLot("only-stock-tool").OwnerId);
        Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("only-stock-tool"), seller));
        Assert.Equal(10_000, world.Society.Inventory.GetLot("only-stock-tool").ConditionBasisPoints);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind.StartsWith("store_stock_", StringComparison.Ordinal));
        world.Validate();
    }

    [Fact]
    public async Task StoreStockDeliversRealWoodAcrossReloadWithoutRestockingBestTools()
    {
        var (state, seller, household, houseId, storeId) = CreateStoreStockFixture();
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [], Offers = [] };
        inventory = InventoryFixture.AddLot(inventory, "store-wood-source", "wood", household, 8,
            storageBuildingId: houseId);
        inventory = InventoryFixture.AddLot(inventory, "seller-best-axe", "wooden_axe", seller, 1);
        inventory = InventoryFixture.AddLot(inventory, "seller-best-pickaxe", "wooden_pickaxe", seller, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 8_000,
            }).ToArray(),
        };
        var axeCondition = inventory.GetLot("seller-best-axe").ConditionBasisPoints;
        var pickaxeCondition = inventory.GetLot("seller-best-pickaxe").ConditionBasisPoints;
        using var collecting = PrivateWorldRuntime.Restore(state,
            id => id == seller ? new ShopProvider("haul_household_stock", "business_stock_store") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 80 && !collecting.Society.Inventory.Lots.Any(lot => lot.Id == "store-wood-source" &&
                 lot.DeliveryBuildingId == storeId); step++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);

        var carried = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == seller &&
            lot.ItemKind == "wood" && lot.DeliveryBuildingId == storeId);
        Assert.Equal(4, carried.Quantity);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(4, collecting.Society.Inventory.GetLot("store-wood-source").Quantity);
        Assert.Equal(8, TotalQuantity(collecting.Society.Inventory, "wood"));
        Assert.Equal("seller-best-axe", ToolProgressionRules.BestUsableTool(collecting.Society.Inventory, seller,
            ToolFamily.Axe)!.Id);
        Assert.Equal("seller-best-pickaxe", ToolProgressionRules.BestUsableTool(collecting.Society.Inventory, seller,
            ToolFamily.Pickaxe)!.Id);
        Assert.Equal(axeCondition, collecting.Society.Inventory.GetLot("seller-best-axe").ConditionBasisPoints);
        Assert.Equal(pickaxeCondition, collecting.Society.Inventory.GetLot("seller-best-pickaxe").ConditionBasisPoints);

        var save = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
        var reloadedState = PrivateWorldRuntimeCodec.Decode(save);
        Assert.Equal(save, PrivateWorldRuntimeCodec.Encode(reloadedState));
        var deliveryProvider = new ShopProvider("haul_household_stock", "business_stock_store");
        using var delivering = PrivateWorldRuntime.Restore(reloadedState,
            id => id == seller ? deliveryProvider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 80 && !delivering.Society.Inventory.Lots.Any(lot =>
                 lot.ItemKind == "wood" && lot.StorageBuildingId == storeId); step++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);

        var delivered = delivering.Society.Inventory.Lots.FirstOrDefault(lot => lot.ItemKind == "wood" &&
            lot.StorageBuildingId == storeId);
        Assert.True(delivered is not null, $"Wood delivery did not complete; actor={delivering.Inhabitants.Single(person =>
            person.InhabitantId == seller).Position}, store={delivering.WorldSimulation.Buildings.Single(building =>
            building.InstanceId == storeId).Position}, choices={string.Join(',', deliveryProvider.Seen.Distinct().Order())}");
        Assert.Equal(household, delivered.OwnerId);
        Assert.Equal(4, delivered.Quantity);
        Assert.Null(delivered.DeliveryBuildingId);
        Assert.Equal(4, delivering.Society.Inventory.GetLot("store-wood-source").Quantity);
        Assert.Equal(8, TotalQuantity(delivering.Society.Inventory, "wood"));
        Assert.Equal("seller-best-axe", ToolProgressionRules.BestUsableTool(delivering.Society.Inventory, seller,
            ToolFamily.Axe)!.Id);
        Assert.Equal("seller-best-pickaxe", ToolProgressionRules.BestUsableTool(delivering.Society.Inventory, seller,
            ToolFamily.Pickaxe)!.Id);
        Assert.Equal(axeCondition, delivering.Society.Inventory.GetLot("seller-best-axe").ConditionBasisPoints);
        Assert.Equal(pickaxeCondition, delivering.Society.Inventory.GetLot("seller-best-pickaxe").ConditionBasisPoints);
        delivering.Validate();
    }

    private static (PrivateWorldRuntimeState State, string Seller, string Household, string HouseId, string StoreId)
        CreateStoreStockFixture()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new ShopProvider("safe_idle"));
        var state = generated.ExportState();
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId is not null &&
            state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var household = house.HouseholdId!;
        var seller = state.Society.Society.GetHousehold(household).MemberIds[0];
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "store-1x1");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "test-store-build-wood", "wood", household, 8);
        inventory = InventoryFixture.AddLot(inventory, "test-store-build-stone", "stone", household, 2);
        using var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ShopProvider("safe_idle"));
        var position = state.Map.Tiles.Where(tile => tile.Position != house.Position)
            .OrderBy(tile => Math.Abs(tile.Position.X - house.Position.X) + Math.Abs(tile.Position.Y - house.Position.Y))
            .Select(tile => tile.Position).First(point => placing.PlaceBuilding("test-store-stock", definition.CanonicalId,
                point, household).Applied);
        state = placing.ExportState();
        var storeId = Assert.Single(state.WorldSimulation!.Buildings, building =>
            building.DefinitionId == definition.CanonicalId && building.HouseholdId == household).InstanceId;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == seller
                ? person with
                {
                    Position = house.Position,
                    Equipment = null,
                    Project = null,
                    LastDecisionContext = null,
                    HungerBasisPoints = 8_000
                }
                : person).ToArray(),
        };
        return (state, seller, household, house.InstanceId, storeId);
    }

    [Fact]
    public async Task ShopHouseholdCanDeclineWithoutTransferringEitherLot()
    {
        var (state, buyer, seller, _) = CreateShopState();
        using var offering = PrivateWorldRuntime.Restore(state, id => id == buyer
            ? new ShopProvider("business_shop:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && offering.BusinessTrades.Count == 0; step++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        var binding = Assert.Single(offering.BusinessTrades);
        state = offering.ExportState();
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        using var declining = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new ShopProvider("business_cancel:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && declining.Society.Inventory.GetOffer(binding.OfferId).State == DirectBarterState.Open; step++)
            Assert.True((await declining.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Cancelled, declining.Society.Inventory.GetOffer(binding.OfferId).State);
        Assert.Equal(2, declining.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.Equal(binding.BuildingInstanceId, declining.Society.Inventory.GetLot("shop-axe").StorageBuildingId);
        Assert.Equal(6, declining.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.Equal(buyer, declining.Society.Inventory.GetLot("buyer-payment").OwnerId);
        Assert.All(declining.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "barter:" + binding.OfferId),
            reservation => Assert.Equal(InventoryReservationState.Released, reservation.State));
        declining.Validate();
    }

    [Fact]
    public async Task PendingOfferHoldsShopReceivingSpaceAgainstAnotherCustomer()
    {
        var (state, buyer, _, shopId) = CreateShopState();
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == shopId);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == shop.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(definition, shop)!.Value;
        var stored = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == shopId).Sum(lot => lot.Quantity);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "shop-space-ballast", "stone", shop.HouseholdId!, capacity - stored - 2, storageBuildingId: shopId));
        using var offering = PrivateWorldRuntime.Restore(state, id => id == buyer
            ? new ShopProvider("business_shop:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && offering.BusinessTrades.Count == 0; step++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        var firstOffer = Assert.Single(offering.BusinessTrades);
        state = offering.ExportState();
        var otherBuyer = state.Society.Society.Inhabitants.First(person => person.Id != buyer &&
            person.HouseholdId == state.Society.Society.GetInhabitant(buyer).HouseholdId).Id;
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != otherBuyer).Select(person => person.Position).ToHashSet();
        var nearby = state.Map.Tiles.Select(tile => tile.Position).First(position => state.Map.IsPassable(position) &&
            Math.Abs(position.X - shop.Position.X) + Math.Abs(position.Y - shop.Position.Y) == 1 && !occupied.Contains(position));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != otherBuyer).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "other-buyer-payment", "wood", otherBuyer, 6);
        state = WithInventory(state, inventory);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == otherBuyer ? person with
            {
                Position = nearby,
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 8_000,
            } : person).ToArray(),
        };
        var provider = new ShopProvider("business_shop:");
        using var competing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == otherBuyer ? provider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 35; step++) Assert.True((await competing.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("business_shop:" + shopId, provider.Seen);
        Assert.Equal(firstOffer.OfferId, Assert.Single(competing.BusinessTrades).OfferId);
        Assert.Equal(DirectBarterState.Open, competing.Society.Inventory.GetOffer(firstOffer.OfferId).State);
        Assert.Equal(capacity - 2, competing.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == shopId).Sum(lot => lot.Quantity));
        Assert.Equal(6, competing.Society.Inventory.GetLot("other-buyer-payment").Quantity);
        competing.Validate();
    }

    [Theory]
    [InlineData("wood", 0, false, 0)]
    [InlineData("wood", 1, false, 1)]
    [InlineData("wood", 2, true, 0)]
    [InlineData("wood", 3, true, 1)]
    [InlineData("iron_ore", 0, false, 0)]
    [InlineData("iron_ore", 1, false, 1)]
    [InlineData("iron_ore", 2, true, 0)]
    [InlineData("iron_ore", 3, true, 1)]
    public async Task BlacksmithCarriedInputDeliveryRespectsPhysicalAndPromisedReceivingSpaceAcrossReload(
        string itemKind, int physicalRoom, bool pendingExchange, int expectedDelivered)
    {
        var (state, buyer, seller, shopId) = CreateShopState();
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == shopId);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == shop.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(definition, shop)!.Value;
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != seller &&
                (lot.StorageBuildingId != shopId || lot.Id == "shop-axe")).ToArray(),
        };
        // Only the tested input is missing. Other recipe inputs cannot hide it
        // behind a different dedicated supply candidate.
        foreach (var (kind, quantity) in new[] { ("wood", 6), ("stone", 4), ("iron_ore", 4), ("iron", 4) })
            if (kind != itemKind)
                inventory = InventoryFixture.AddLot(inventory, "delivery-stock-" + kind, kind,
                    shop.HouseholdId!, quantity, storageBuildingId: shopId);
        inventory = InventoryFixture.AddLot(inventory, "direct-shop-input", itemKind, seller, 4);
        var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == shopId).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "delivery-space-ballast", "clay", shop.HouseholdId!,
            capacity - stored - physicalRoom, storageBuildingId: shopId);
        string? offerId = null;
        if (pendingExchange)
        {
            var proposedTick = state.Society.Society.WorldTick;
            offerId = $"business-trade:{proposedTick}:{buyer}:{shopId}";
            inventory = InventoryFixture.AcceptDirectBarterOffer(
                InventoryFixture.CreateDirectBarterOffer(inventory,
                    new(offerId, 1, shop.HouseholdId!, buyer, "shop-axe", 1, "buyer-payment", 3,
                        proposedTick + 120)), offerId, 1, buyer);
            state = state with
            {
                BusinessTrades = [new(offerId, shopId, shop.HouseholdId!, buyer, shop.Position,
                    proposedTick, "wooden_axe", "wood")],
            };
        }
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == seller ? person with
            {
                Position = shop.Position,
                Equipment = null,
                Project = null,
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
                HungerBasisPoints = 8_000,
            } : person).ToArray(),
        };
        var candidate = itemKind == "wood" ? "haul_smith_input" : "deliver_smith_ore";
        var provider = new ShopProvider(candidate);
        using var delivering = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == seller ? provider : new ShopProvider("safe_idle"));
        var quantityBefore = TotalQuantity(delivering.Society.Inventory, itemKind);
        for (var step = 0; step < 35; step++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        var remaining = delivering.Society.Inventory.GetLot("direct-shop-input");
        Assert.Equal((seller, 4 - expectedDelivered, (string?)null),
            (remaining.OwnerId, remaining.Quantity, remaining.StorageBuildingId));
        Assert.Equal(capacity - physicalRoom + expectedDelivered,
            delivering.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == shopId).Sum(lot => lot.Quantity));
        Assert.Equal(quantityBefore, TotalQuantity(delivering.Society.Inventory, itemKind));
        if (expectedDelivered == 0)
            Assert.DoesNotContain(candidate, provider.Seen);
        else
            Assert.Contains(candidate, provider.Seen);
        if (offerId is not null)
        {
            Assert.Equal(DirectBarterState.Open, delivering.Society.Inventory.GetOffer(offerId).State);
            Assert.All(delivering.Society.Inventory.Reservations.Where(reservation =>
                    reservation.Purpose == "barter:" + offerId),
                reservation => Assert.Equal(InventoryReservationState.Reserved, reservation.State));
        }
        delivering.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(delivering.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new ShopProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task FullCustomerCannotReserveMoreProduceThanTheirPaymentFrees()
    {
        var (state, buyer, _, shopId) = CreateShopState("farmhouse");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id switch
            {
                "shop-axe" => lot with { ItemKind = FarmFieldRules.Greens },
                "buyer-payment" => lot with { ItemKind = FarmFieldRules.Grain, Quantity = 1 },
                _ => lot,
            }).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "buyer-ballast", "stone", buyer, 7);
        state = WithInventory(state, inventory);
        var provider = new ShopProvider("business_shop:");
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer ? provider : new ShopProvider("safe_idle"));
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, buyer, null));
        for (var step = 0; step < 35; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("business_shop:" + shopId, provider.Seen);
        Assert.Empty(world.BusinessTrades);
        Assert.Equal(2, world.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("buyer-payment").Quantity);
        world.Validate();
    }

    [Fact]
    public async Task CheckpointRejectsAnExchangeWithAnOutOfMapTransactionLocation()
    {
        var (state, buyer, _, _) = CreateShopState();
        using var offering = PrivateWorldRuntime.Restore(state, id => id == buyer
            ? new ShopProvider("business_shop:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && offering.BusinessTrades.Count == 0; step++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        state = offering.ExportState();
        state = state with { BusinessTrades = [Assert.Single(state.BusinessTrades!) with { Position = new(-1, -1) }] };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state));
    }

    private static (PrivateWorldRuntimeState State, string Buyer, string Seller, string ShopId) CreateShopState(string kind = "blacksmith")
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ShopProvider("safe_idle"));
        var state = setup.ExportState();
        if (!state.WorldSimulation!.Buildings.Any(building => state.WorldContent!.Buildings
                .Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains(kind)))
        {
            var definition = state.WorldContent!.Buildings.Where(item => item.Tags.Contains(kind))
                .OrderBy(item => item.Width * item.Height).First();
            var house = state.WorldSimulation.Buildings.First(building => building.HouseholdId is not null &&
                state.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            var buildingInventory = state.Society.Society.Inventory;
            foreach (var cost in definition.BuildCosts)
                buildingInventory = InventoryFixture.AddLot(buildingInventory, "shop-building-" + cost.ResourceId,
                    cost.ResourceId, house.HouseholdId!, cost.Amount, storageBuildingId: house.InstanceId);
            using var placing = PrivateWorldRuntime.Restore(WithInventory(state, buildingInventory), _ => new ShopProvider("safe_idle"));
            _ = state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, house.Position))
                .Select(tile => tile.Position).First(point =>
                    placing.PlaceBuilding("test-business-" + kind, definition.CanonicalId, point, house.HouseholdId).Applied);
            placing.Validate();
            state = placing.ExportState();
        }
        var shop = state.WorldSimulation!.Buildings.Single(building =>
            state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains(kind));
        var seller = state.Society.Society.GetHousehold(shop.HouseholdId!).MemberIds[0];
        var buyer = state.Society.Society.Inhabitants.First(person => person.HouseholdId != shop.HouseholdId).Id;
        var taken = state.Inhabitants.Where(person => person.InhabitantId != buyer && person.InhabitantId != seller)
            .Select(person => person.Position).ToHashSet();
        var buyerPosition = state.Map.Tiles.Select(tile => tile.Position).First(position =>
            Math.Abs(position.X - shop.Position.X) + Math.Abs(position.Y - shop.Position.Y) == 1 &&
            state.Map.IsPassable(position) && !taken.Contains(position));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != buyer).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "shop-axe", "wooden_axe", shop.HouseholdId!, 2,
            conditionBasisPoints: 7_600, storageBuildingId: shop.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "buyer-payment", "wood", buyer, 6);
        state = WithInventory(state, inventory);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == buyer ? buyerPosition : person.InhabitantId == seller ? shop.Position : person.Position,
                HungerBasisPoints = 7_500,
                LastDecisionContext = null,
                Project = null,
                Equipment = person.InhabitantId == buyer ? null : person.Equipment,
            }).ToArray(),
        };
        return (state, buyer, seller, shop.InstanceId);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };

    private static PrivateWorldRuntimeState WithBuyerBlacksmith(PrivateWorldRuntimeState state, string buyer)
    {
        var household = state.Society.Society.GetInhabitant(buyer).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var blacksmith = state.WorldContent!.Buildings.Single(item => item.LocalId == "blacksmith-1x2");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in blacksmith.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "buyer-workshop-" + cost.ResourceId,
                cost.ResourceId, household, cost.Amount, storageBuildingId: house.InstanceId);
        using var placing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new ShopProvider("safe_idle"));
        _ = state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, house.Position))
            .Select(tile => tile.Position).First(point =>
                placing.PlaceBuilding("buyer-blacksmith", blacksmith.CanonicalId, point, household).Applied);
        placing.Validate();
        return placing.ExportState();
    }

    private static int TotalQuantity(InventoryCheckpoint inventory, string kind) => inventory.Lots
        .Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private sealed class ShopProvider(params string[] prefixes) : IDecisionProvider
    {
        public ConcurrentBag<string> Seen { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Seen.Add(candidate.Id);
            var choice = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}
