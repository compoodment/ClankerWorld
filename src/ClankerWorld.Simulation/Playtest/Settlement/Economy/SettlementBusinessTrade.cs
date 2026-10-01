using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private BusinessTradeState businessTrade = BusinessTradeState.Empty;
    public BusinessTradeState BusinessTrade => businessTrade;
    private const int BusinessOfferLifetime = 120;

    private BusinessActionResult BusinessAction(Func<BusinessActionResult> action)
    {
        gate.Wait();
        try { return action(); }
        finally { gate.Release(); }
    }

    public BusinessActionResult ListBusinessGoods(string sellerId, string buildingId,
        string goodsLotId, int goodsQuantity, string paymentKind, int paymentQuantity) =>
        BusinessAction(() => ListBusinessGoodsCore(sellerId, buildingId, goodsLotId,
            goodsQuantity, paymentKind, paymentQuantity));

    private BusinessActionResult ListBusinessGoodsCore(string sellerId, string buildingId,
        string goodsLotId, int goodsQuantity, string paymentKind, int paymentQuantity)
    {
        var site = BusinessSite(buildingId);
        var goods = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == goodsLotId);
        if (site?.HouseholdId is not { } householdId || !AdultResident(sellerId) ||
            HouseholdFor(sellerId) != householdId || !IsWithinInteractionRange(inhabitants[sellerId].Position, site.Position, ResourceInteractionRange) ||
            goods is null || goods.OwnerId != householdId || goods.StorageBuildingId != site.InstanceId ||
            !BusinessCatalogAccepts(site, goods.ItemKind) || !BusinessLotCanMove(goods) ||
            goodsQuantity <= 0 || BusinessSaleAllowance(site, goods) < goodsQuantity ||
            string.IsNullOrWhiteSpace(paymentKind) || paymentKind != paymentKind.Trim() || paymentQuantity <= 0 ||
            paymentKind == goods.ItemKind)
            return new(false, Failure: "A household adult at its business must offer actual on-site goods and exact different payment terms.");
        var id = "business-listing:" + businessTrade.NextSequence;
        var listing = new BusinessListing(id, sellerId, householdId, buildingId, goodsLotId,
            goodsQuantity, paymentKind, paymentQuantity, WorldTick + BusinessOfferLifetime);
        businessTrade = businessTrade with
        {
            NextSequence = businessTrade.NextSequence + 1,
            Listings = businessTrade.Listings.Append(listing).ToArray(),
        };
        AppendEvent("business_goods_listed", $"{sellerId}|{buildingId}|{goods.ItemKind}|{goodsQuantity}|{paymentKind}|{paymentQuantity}");
        return new(true, id);
    }

    public BusinessActionResult AcceptBusinessListing(string buyerId, string listingId, string paymentLotId) =>
        BusinessAction(() => AcceptBusinessListingCore(buyerId, listingId, paymentLotId));

    private BusinessActionResult AcceptBusinessListingCore(string buyerId, string listingId, string paymentLotId)
    {
        var listing = businessTrade.Listings.FirstOrDefault(item => item.Id == listingId);
        if (listing is null || !ListingIsLive(listing) || !AdultResident(buyerId) ||
            buyerId == listing.SellerId || HouseholdFor(buyerId) == listing.HouseholdId ||
            businessTrade.Offers.Any(offer => offer.BuyerId == buyerId && offer.State == BusinessOfferState.Open))
            return new(false, Failure: "The listing or customer is unavailable.");
        var site = BusinessSite(listing.BuildingId)!;
        var inventory = society.Checkpoint.Inventory;
        var payment = inventory.Lots.FirstOrDefault(lot => lot.Id == paymentLotId);
        var goods = inventory.GetLot(listing.GoodsLotId);
        if (payment is null || payment.OwnerId != buyerId || payment.StorageBuildingId is not null ||
            payment.DeliveryBuildingId is not null || !BusinessLotCanMove(payment) ||
            IsEquippedLot(buyerId, payment.Id) || payment.ItemKind != listing.PaymentKind ||
            AvailableLotQuantity(payment) < listing.PaymentQuantity ||
            FindUnoccupiedRoute(buyerId, inhabitants[buyerId].Position, site.Position, ResourceInteractionRange).Count == 0)
            return new(false, Failure: "Bring the exact personally carried payment along a reachable route.");
        var goodsLoad = InventoryFixture.TransferLoadQuantity(inventory, goods.Id, listing.GoodsQuantity);
        var paymentLoad = InventoryFixture.TransferLoadQuantity(inventory, payment.Id, listing.PaymentQuantity);
        var carrySpace = Math.Max(0, goodsLoad - paymentLoad);
        var storageSpace = Math.Max(0, paymentLoad - goodsLoad);
        if (BusinessCarryingRoom(buyerId) < carrySpace || BusinessStorageRoom(site.InstanceId) < storageSpace)
            return new(false, Failure: "The customer or business lacks uncommitted room for this exact exchange.");
        var id = "business-offer:" + businessTrade.NextSequence;
        var expiry = Math.Min(listing.ExpiryTick, WorldTick + BusinessOfferLifetime);
        var offer = new BusinessOffer(id, listing.Id, listing.SellerId, listing.HouseholdId, buyerId,
            site.InstanceId, goods.Id, goods.ItemKind, listing.GoodsQuantity, payment.Id, payment.ItemKind, listing.PaymentQuantity,
            carrySpace, storageSpace, expiry, BusinessOfferState.Open);
        var contents = inventory.Lots.Where(lot => lot.ContainerLotId == goods.Id || lot.ContainerLotId == payment.Id)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).Select((lot, index) => new BusinessContainerCommitment(
                id + ":content:" + index, lot.ContainerLotId!, lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity)).ToArray();
        if (contents.Any(content => AvailableLotQuantity(inventory.GetLot(content.LotId)) != content.Quantity))
            return new(false, Failure: "A vessel's contents are already committed elsewhere.");
        offer = offer with { Contents = contents };
        InventoryReservation[] reservations =
        [
            new(id + ":goods", listing.HouseholdId, goods.Id, listing.GoodsQuantity,
                "business_exchange", expiry, true, InventoryReservationState.Reserved),
            new(id + ":payment", buyerId, payment.Id, listing.PaymentQuantity,
                "business_exchange", expiry, true, InventoryReservationState.Reserved),
            .. contents.Select(content => new InventoryReservation(content.ReservationId, content.OwnerId,
                content.LotId, content.Quantity, "business_exchange", expiry, true, InventoryReservationState.Reserved)),
        ];
        ApplyInventoryTransition(checkpoint => InventoryFixture.ReserveTogether(checkpoint, reservations));
        businessTrade = businessTrade with
        {
            NextSequence = businessTrade.NextSequence + 1,
            Offers = businessTrade.Offers.Append(offer).ToArray(),
        };
        AppendEvent("business_offer_accepted", $"{buyerId}|{id}|{site.InstanceId}");
        return new(true, id);
    }

    public BusinessActionResult SettleBusinessOffer(string buyerId, string offerId) =>
        BusinessAction(() => SettleBusinessOfferCore(buyerId, offerId));

    private BusinessActionResult SettleBusinessOfferCore(string buyerId, string offerId)
    {
        var offer = businessTrade.Offers.FirstOrDefault(item => item.Id == offerId);
        if (offer is null || offer.State != BusinessOfferState.Open || offer.BuyerId != buyerId)
            return new(false, Failure: "This customer has no open exchange with that identity.");
        if (OfferInvalidReason(offer) is { } invalid)
        {
            CancelBusinessOfferCore(offer.SellerId, offer.Id, invalid);
            return new(false, offer.Id, invalid);
        }
        var site = BusinessSite(offer.BuildingId)!;
        if (!IsWithinInteractionRange(inhabitants[buyerId].Position, site.Position, ResourceInteractionRange) ||
            !IsWithinInteractionRange(inhabitants[offer.SellerId].Position, site.Position, ResourceInteractionRange))
            return new(false, offer.Id, "The customer and seller must both reach the stocked transaction location.");
        if (BusinessCarryingRoom(buyerId, offer.Id) < offer.ReservedCarrySpace ||
            BusinessStorageRoom(site.InstanceId, offer.Id) < offer.ReservedStorageSpace)
            return new(false, offer.Id, "Receiving space changed; make room or cancel the exchange.");
        // Build both transfers from one immutable inventory snapshot. A rejection commits neither side.
        ApplyInventoryTransition(inventory =>
        {
            var released = ReleaseBusinessReservations(inventory, offer, "business_settled");
            var paid = InventoryFixture.Transfer(released, offer.Id + ":payment", buyerId,
                offer.HouseholdId, offer.PaymentLotId, offer.PaymentQuantity, "business_payment", site.InstanceId);
            return InventoryFixture.Transfer(paid, offer.Id + ":goods", offer.HouseholdId,
                buyerId, offer.GoodsLotId, offer.GoodsQuantity, "business_purchase");
        }, offer.Id);
        ReadTradedKnowledge(offer.SellerId, buyerId, offer.GoodsLotId, offer.PaymentLotId);
        SetBusinessOffer(offer with { State = BusinessOfferState.Settled, Blocker = null });
        businessTrade = businessTrade with { Listings = businessTrade.Listings.Where(item => item.Id != offer.ListingId).ToArray() };
        AppendEvent("business_exchange_completed", $"{buyerId}|{offer.SellerId}|{offer.Id}|{site.InstanceId}");
        return new(true, offer.Id);
    }

    public BusinessActionResult CancelBusinessOffer(string actor, string offerId) =>
        BusinessAction(() => CancelBusinessOfferCore(actor, offerId, "cancelled"));

    private BusinessActionResult CancelBusinessOfferCore(string actor, string offerId, string reason)
    {
        var offer = businessTrade.Offers.FirstOrDefault(item => item.Id == offerId);
        if (offer is null || offer.State != BusinessOfferState.Open || actor != offer.BuyerId && actor != offer.SellerId)
            return new(false, Failure: "Only a participant may cancel an open exchange.");
        ApplyInventoryTransition(inventory => ReleaseBusinessReservations(inventory, offer, reason));
        SetBusinessOffer(offer with { State = BusinessOfferState.Cancelled, Blocker = reason });
        AppendEvent("business_offer_cancelled", $"{actor}|{offer.Id}|{reason}");
        return new(true, offer.Id);
    }

    private static InventoryCheckpoint ReleaseBusinessReservations(InventoryCheckpoint inventory,
        BusinessOffer offer, string reason)
    {
        var current = inventory;
        foreach (var id in new[] { offer.Id + ":goods", offer.Id + ":payment" }
                     .Concat((offer.Contents ?? []).Select(content => content.ReservationId)))
            if (current.Reservations.Any(item => item.Id == id && item.State is InventoryReservationState.Reserved or
                    InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed))
                current = InventoryFixture.ReleaseReservation(current, id, reason);
        return current;
    }

    private void SetBusinessOffer(BusinessOffer updated) => businessTrade = businessTrade with
    {
        Offers = businessTrade.Offers.Select(offer => offer.Id == updated.Id ? updated : offer).ToArray(),
    };

    private int ReservedBusinessCarrySpace(string actor, string? exceptOffer = null) =>
        businessTrade.Offers.Where(offer => offer.State == BusinessOfferState.Open &&
            offer.BuyerId == actor && offer.Id != exceptOffer).Sum(offer => offer.ReservedCarrySpace);

    private int ReservedBusinessStorageSpace(string buildingId, string? exceptOffer = null) =>
        businessTrade.Offers.Where(offer => offer.State == BusinessOfferState.Open &&
            offer.BuildingId == buildingId && offer.Id != exceptOffer).Sum(offer => offer.ReservedStorageSpace);

    private int BusinessCarryingRoom(string actor, string? exceptOffer = null) => Math.Max(0,
        CarryEquipmentRules.Room(society.Checkpoint.Inventory, inhabitants[actor]) - ReservedBusinessCarrySpace(actor, exceptOffer));

    private int BusinessStorageRoom(string buildingId, string? exceptOffer = null) => StorageRoom(buildingId, exceptOffer);

    private int BusinessTransferQuantity(InventoryLot lot, int maximum, int availableRoom)
    {
        var quantity = Math.Min(maximum, AvailableLotQuantity(lot));
        if (quantity <= 0 || availableRoom <= 0) return 0;
        var contents = society.Checkpoint.Inventory.Lots.Where(content => content.ContainerLotId == lot.Id)
            .Sum(content => content.Quantity);
        return contents > 0
            ? quantity == lot.Quantity && quantity + contents <= availableRoom ? quantity : 0
            : Math.Min(quantity, availableRoom);
    }

    private PlacedBuilding? BusinessSite(string buildingId) => worldSimulation.Buildings.FirstOrDefault(building =>
        building.InstanceId == buildingId && building.HouseholdId is not null &&
        worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
            definition.Tags.Any(tag => tag is "farmhouse" or "blacksmith" or "tailor" or "store" or
                "restaurant" or "clinic" or BusinessContent.StallKind)));

    private bool BusinessLotCanMove(InventoryLot lot) => !RejectedBusinessStock(lot) &&
        lot.ContainerLotId is null && lot.GroundPosition is null &&
        (!inhabitants.ContainsKey(lot.OwnerId) || !IsEquippedLot(lot.OwnerId, lot.Id));

    private long BusinessSaleAllowance(PlacedBuilding site, InventoryLot lot) => Math.Min(AvailableLotQuantity(lot),
        FoodItems.IsPlantingStock(lot.ItemKind)
            ? Math.Max(0, HouseholdCropStock(site.HouseholdId!, lot.ItemKind) - FarmPlantingReserve(site.HouseholdId!, lot.ItemKind))
            : long.MaxValue);

    private bool BusinessCatalogAccepts(PlacedBuilding site, string kind) =>
        BusinessCatalogAccepts(worldContent.Buildings.Single(definition => definition.CanonicalId == site.DefinitionId), kind);

    private static bool BusinessCatalogAccepts(BuildingDefinition definition, string kind)
    {
        var tags = definition.Tags;
        if (tags.Any(tag => tag is "store" or BusinessContent.StallKind)) return true;
        if (tags.Contains("blacksmith", StringComparer.Ordinal)) return ToolCapabilities.ForItem(kind) is not null ||
            OrnamentContent.IsOrnament(kind) || CombatGearContent.IsGear(kind);
        if (tags.Contains("tailor", StringComparer.Ordinal)) return CarryEquipmentRules.IsClothing(kind) ||
            kind is "cloth" or "leather" or "sack" or "leather_satchel";
        if (tags.Contains("farmhouse", StringComparer.Ordinal)) return FoodItems.IsEdible(kind) || FoodItems.IsFarmStock(kind) ||
            FoodItems.IsPlantingStock(kind) || kind is "seed" or "flour";
        if (tags.Contains("restaurant", StringComparer.Ordinal)) return FoodItems.IsEdible(kind);
        if (tags.Contains("clinic", StringComparer.Ordinal)) return kind is "bandage" or "medicine";
        return false;
    }

    private bool ListingIsLive(BusinessListing listing) => listing.ExpiryTick >= WorldTick &&
        AdultResident(listing.SellerId) && HouseholdFor(listing.SellerId) == listing.HouseholdId &&
        BusinessSite(listing.BuildingId) is { } site && site.HouseholdId == listing.HouseholdId &&
        society.Checkpoint.Inventory.Lots.Any(lot => lot.Id == listing.GoodsLotId &&
            lot.OwnerId == listing.HouseholdId && lot.StorageBuildingId == listing.BuildingId &&
            BusinessLotCanMove(lot) && BusinessSaleAllowance(site, lot) >= listing.GoodsQuantity &&
            BusinessCatalogAccepts(site, lot.ItemKind));

    private string? OfferInvalidReason(BusinessOffer offer)
    {
        if (offer.ExpiryTick < WorldTick) return "The exchange expired.";
        if (!AdultResident(offer.BuyerId) || !AdultResident(offer.SellerId) ||
            HouseholdFor(offer.SellerId) != offer.HouseholdId ||
            BusinessSite(offer.BuildingId) is not { } site || site.HouseholdId != offer.HouseholdId)
            return "A participant or household permission changed.";
        var inventory = society.Checkpoint.Inventory;
        var goods = inventory.Lots.FirstOrDefault(lot => lot.Id == offer.GoodsLotId);
        var payment = inventory.Lots.FirstOrDefault(lot => lot.Id == offer.PaymentLotId);
        if (goods is null || payment is null || !BusinessLotCanMove(goods) || !BusinessLotCanMove(payment) ||
            goods.OwnerId != offer.HouseholdId || goods.StorageBuildingId != offer.BuildingId ||
            goods.ItemKind != offer.GoodsKind || payment.ItemKind != offer.PaymentKind ||
            payment.OwnerId != offer.BuyerId || payment.StorageBuildingId is not null || payment.DeliveryBuildingId is not null ||
            goods.Quantity < offer.GoodsQuantity || payment.Quantity < offer.PaymentQuantity ||
            !BusinessCatalogAccepts(site, goods.ItemKind)) return "An exact reserved lot or its location changed.";
        if (FoodItems.IsPlantingStock(goods.ItemKind) && HouseholdCropStock(offer.HouseholdId, goods.ItemKind) <
            FarmPlantingReserve(offer.HouseholdId, goods.ItemKind))
            return "The household needs its planting stock; the exchange was released.";
        foreach (var (id, lot, owner, quantity) in new[]
                 {
                     (offer.Id + ":goods", offer.GoodsLotId, offer.HouseholdId, offer.GoodsQuantity),
                     (offer.Id + ":payment", offer.PaymentLotId, offer.BuyerId, offer.PaymentQuantity),
                 })
            if (!inventory.Reservations.Any(reservation => reservation.Id == id && reservation.LotId == lot &&
                    reservation.OwnerId == owner && reservation.Quantity == quantity &&
                    reservation.State == InventoryReservationState.Reserved))
                return "An exchange reservation was released.";
        foreach (var content in offer.Contents ?? [])
            if (!inventory.Lots.Any(lot => lot.Id == content.LotId && lot.ContainerLotId == content.ContainerId &&
                    lot.OwnerId == content.OwnerId && lot.ItemKind == content.ItemKind && lot.Quantity == content.Quantity &&
                    lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0) ||
                !inventory.Reservations.Any(reservation => reservation.Id == content.ReservationId &&
                    reservation.LotId == content.LotId && reservation.OwnerId == content.OwnerId &&
                    reservation.Quantity == content.Quantity && reservation.State == InventoryReservationState.Reserved))
                return "A vessel's exact committed contents changed.";
        if (BusinessCarryingRoom(offer.BuyerId, offer.Id) < offer.ReservedCarrySpace ||
            BusinessStorageRoom(offer.BuildingId, offer.Id) < offer.ReservedStorageSpace)
            return "The promised receiving space no longer fits; reservations were released.";
        if (FindUnoccupiedRoute(offer.BuyerId, inhabitants[offer.BuyerId].Position, site.Position, ResourceInteractionRange).Count == 0 ||
            FindUnoccupiedRoute(offer.SellerId, inhabitants[offer.SellerId].Position, site.Position, ResourceInteractionRange).Count == 0)
            return "The transaction location is no longer reachable.";
        return null;
    }

    private void MaintainBusinessTrades()
    {
        foreach (var offer in businessTrade.Offers.Where(item => item.State == BusinessOfferState.Open).ToArray())
            if (OfferInvalidReason(offer) is { } reason)
                CancelBusinessOfferCore(offer.SellerId, offer.Id, reason);
        var pendingListings = businessTrade.Offers.Where(item => item.State == BusinessOfferState.Open)
            .Select(item => item.ListingId).ToHashSet(StringComparer.Ordinal);
        var receipts = businessTrade.Offers.Where(offer => offer.State == BusinessOfferState.Settled &&
            society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == offer.HouseholdId &&
                lot.StorageBuildingId == offer.BuildingId &&
                (lot.Id == offer.PaymentLotId || lot.ProvenanceLotId == offer.PaymentLotId))).ToArray();
        var receiptOfferIds = receipts.Select(offer => offer.Id).ToHashSet(StringComparer.Ordinal);
        businessTrade = businessTrade with
        {
            Listings = businessTrade.Listings.Where(item => ListingIsLive(item) || pendingListings.Contains(item.Id)).ToArray(),
            Offers = businessTrade.Offers.Where(item => item.State == BusinessOfferState.Open).Concat(receipts).Concat(
                businessTrade.Offers.Where(item => item.State != BusinessOfferState.Open && item.ExpiryTick + 120 >= WorldTick &&
                    !receiptOfferIds.Contains(item.Id))
                    .TakeLast(64)).ToArray(),
        };
        MaintainMarketStalls();
        MaintainBusinessToolOrders();
    }
}
