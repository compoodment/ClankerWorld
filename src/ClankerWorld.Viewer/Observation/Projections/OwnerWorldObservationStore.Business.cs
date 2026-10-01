using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Viewer.Observation;

public sealed partial class OwnerWorldObservationStore
{
    private static ViewerBusinessTrade? BusinessSnapshot(PrivateWorldRuntimeState state)
    {
        if (state.BusinessTrade is not { } business) return null;
        var inventory = state.Society.Society.Inventory;
        var tick = state.Society.Society.WorldTick;
        var listings = business.Listings.Where(listing => listing.ExpiryTick >= tick).Select(listing =>
        {
            var stock = inventory.Lots.FirstOrDefault(lot => lot.Id == listing.GoodsLotId);
            if (stock is null || stock.Quantity < listing.GoodsQuantity || stock.ConditionBasisPoints <= 0 ||
                stock.FreshnessBasisPoints <= 0 || stock.OwnerId != listing.HouseholdId || stock.StorageBuildingId != listing.BuildingId)
                return null;
            return new ViewerBusinessListing(listing.Id, listing.SellerId, listing.BuildingId, listing.HouseholdId,
                stock.ItemKind, listing.GoodsQuantity, listing.PaymentKind, listing.PaymentQuantity, listing.ExpiryTick,
                inventory.Lots.Where(lot => lot.ContainerLotId == stock.Id && lot.Quantity > 0)
                    .Select(lot => new ViewerInventoryContent(lot.ItemKind, lot.Quantity)).ToArray());
        }).OfType<ViewerBusinessListing>().ToArray();
        return new(listings,
            business.Offers.Select(offer => new ViewerBusinessOffer(offer.Id, offer.SellerId, offer.BuyerId, offer.BuildingId,
                offer.GoodsKind, offer.GoodsQuantity, offer.PaymentKind, offer.PaymentQuantity, offer.State.ToString().ToLowerInvariant(), offer.Blocker)).ToArray(),
            business.Stalls.Select(stall => new ViewerMarketStall(stall.MarketId, stall.BuildingId, stall.HouseholdId,
                inventory.Lots.Where(lot => lot.StorageBuildingId == stall.BuildingId).Sum(lot => lot.Quantity))).ToArray(),
            business.ToolOrders.Select(order => new ViewerBusinessToolOrder(order.Id, order.BuyerId, order.BuildingId,
                order.ToolKind, order.State, order.Blocker)).ToArray());
    }
}
