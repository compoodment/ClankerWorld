using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class BusinessTradeTests
{
    [Fact]
    public async Task ShopBarterReservesExactOnSiteGoodsAndSettlesAfterReload()
    {
        var (state, buyer, seller, shopId) = CreateShopState();
        var buyerProvider = new ShopProvider("business_shop:");
        using var offering = PrivateWorldRuntime.Restore(state, id => id == buyer ? buyerProvider : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && offering.BusinessTrades.Count == 0; step++)
            Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
        var binding = Assert.Single(offering.BusinessTrades);
        var offer = offering.Society.Inventory.GetOffer(binding.OfferId);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.Equal(1, offer.FirstQuantity);
        Assert.Equal(3, offer.SecondQuantity);
        Assert.Equal("shop-axe", offer.FirstLotId);
        Assert.Equal("buyer-payment", offer.SecondLotId);
        Assert.Equal(shopId, offering.Society.Inventory.GetLot(offer.FirstLotId).StorageBuildingId);
        Assert.Equal(2, offering.Society.Inventory.GetLot("shop-axe").Quantity);
        Assert.Equal(6, offering.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.All(offering.Society.Inventory.Reservations.Where(reservation => reservation.Purpose == "barter:" + offer.Id),
            reservation => Assert.Equal(InventoryReservationState.Reserved, reservation.State));
        var snapshot = new OwnerWorldObservationStore(offering).GetSnapshot();
        var displayed = Assert.Single(snapshot.PlacedBuildings.Single(building => building.InstanceId == shopId).Trades);
        Assert.Equal("wooden_axe", displayed.GoodsKind);
        Assert.Equal(1, displayed.GoodsQuantity);
        Assert.Equal("wood", displayed.PaymentKind);
        Assert.Equal(3, displayed.PaymentQuantity);
        Assert.Equal("open", displayed.Status);
        Assert.Contains(snapshot.Inhabitants.Single(person => person.Id == buyer).SocialNotes,
            note => note.Contains("Both traders must meet there", StringComparison.Ordinal));
        var bytes = PrivateWorldRuntimeCodec.Encode(offering.ExportState());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(bytes)));
        state = PrivateWorldRuntimeCodec.Decode(bytes);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        var woodBefore = TotalQuantity(state.Society.Society.Inventory, "wood");
        var axesBefore = TotalQuantity(state.Society.Society.Inventory, "wooden_axe");
        using var settling = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new ShopProvider("business_continue:") : new ShopProvider("safe_idle"));
        for (var step = 0; step < 40 && settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Open; step++)
            Assert.True((await settling.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Settled, settling.Society.Inventory.GetOffer(offer.Id).State);
        var purchased = Assert.Single(settling.Society.Inventory.Lots, lot => lot.OwnerId == buyer &&
            lot.ItemKind == "wooden_axe" && lot.ProvenanceLotId == "shop-axe");
        Assert.Equal(1, purchased.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, buyer));
        var payment = Assert.Single(settling.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "buyer-payment");
        Assert.Equal(3, payment.Quantity);
        Assert.Equal(binding.SellerHouseholdId, payment.OwnerId);
        Assert.Equal(shopId, payment.StorageBuildingId);
        Assert.Null(payment.DeliveryBuildingId);
        Assert.Equal(3, settling.Society.Inventory.GetLot("buyer-payment").Quantity);
        Assert.Equal(woodBefore, TotalQuantity(settling.Society.Inventory, "wood"));
        Assert.Equal(axesBefore, TotalQuantity(settling.Society.Inventory, "wooden_axe"));
        Assert.Equal(state.Society.Society.GetInhabitant(buyer).HouseholdId, settling.Society.GetInhabitant(buyer).HouseholdId);
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

    private static (PrivateWorldRuntimeState State, string Buyer, string Seller, string ShopId) CreateShopState()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ShopProvider("safe_idle"));
        var state = setup.ExportState();
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
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
