using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private IEnumerable<GridPoint> LooseStockTiles() => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.GroundPosition is not null && lot.Quantity > 0).Select(lot =>
            new GridPoint(lot.GroundPosition!.X, lot.GroundPosition.Y)).Distinct();

    private bool RejectedBusinessStock(InventoryLot lot) => lot.ConditionBasisPoints == 0 ||
        lot.FreshnessBasisPoints == 0 || society.Checkpoint.Inventory.Lots.Any(content =>
            content.ContainerLotId == lot.Id && (content.ConditionBasisPoints == 0 || content.FreshnessBasisPoints == 0));

    private InventoryLot? BusinessStockToClear(string buildingId) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.StorageBuildingId == buildingId && lot.ContainerLotId is null && RejectedBusinessStock(lot))
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private bool LooseStockGroundIsLegal(GridPoint point) => map.IsBuildable(point) &&
        !map.CampObjects.Any(item => item.Position == point) && !map.Resources.Any(item => item.Position == point) &&
        !worldSimulation.Buildings.Any(building => WorldContentSimulationRules.Footprint(
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Contains(point));

    private GridPoint? BusinessCleanupGround(string actor, PlacedBuilding site) => map.FootNeighbors(site.Position)
        .Where(LooseStockGroundIsLegal).OrderBy(point => map.FootDistance(inhabitants[actor].Position, point))
        .ThenBy(point => point.Y).ThenBy(point => point.X).Select(point => (GridPoint?)point)
        .FirstOrDefault(point => FindUnoccupiedRoute(actor, inhabitants[actor].Position, point!.Value, 0).Count > 0);

    public BusinessActionResult ClearRejectedBusinessStock(string actor, string buildingId, string lotId) =>
        BusinessAction(() => ClearRejectedBusinessStockCore(actor, buildingId, lotId));

    private BusinessActionResult ClearRejectedBusinessStockCore(string actor, string buildingId, string lotId)
    {
        var site = BusinessSite(buildingId);
        var inventory = society.Checkpoint.Inventory;
        var lot = inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (site?.HouseholdId is not { } householdId || !AdultResident(actor) || HouseholdFor(actor) != householdId ||
            lot is null || lot.OwnerId != householdId || lot.StorageBuildingId != buildingId ||
            lot.ContainerLotId is not null || !RejectedBusinessStock(lot) ||
            !LooseStockGroundIsLegal(inhabitants[actor].Position) ||
            !IsWithinInteractionRange(inhabitants[actor].Position, site.Position, ResourceInteractionRange))
            return new(false, Failure: "A household adult on clear ground beside its business may remove rejected stock without deleting it.");
        var groupIds = inventory.Lots.Where(item => item.Id == lot.Id || item.ContainerLotId == lot.Id)
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var offers = businessTrade.Offers.Where(offer => offer.State == BusinessOfferState.Open &&
            (groupIds.Contains(offer.GoodsLotId) || groupIds.Contains(offer.PaymentLotId) ||
             (offer.Contents ?? []).Any(content => groupIds.Contains(content.LotId)))).ToArray();
        var releasable = offers.SelectMany(offer => new[] { offer.Id + ":goods", offer.Id + ":payment" }
            .Concat((offer.Contents ?? []).Select(content => content.ReservationId))).ToHashSet(StringComparer.Ordinal);
        if (inventory.Reservations.Any(reservation => groupIds.Contains(reservation.LotId) &&
                reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                    InventoryReservationState.Committed && !releasable.Contains(reservation.Id)))
            return new(false, Failure: "Other work still reserves this stock; cancel that work first.");
        foreach (var offer in offers) CancelBusinessOfferCore(offer.SellerId, offer.Id, "Rejected stock was physically removed.");
        var position = inhabitants[actor].Position;
        ApplyInventoryTransition(checkpoint => checkpoint with
        {
            Lots = checkpoint.Lots.Select(item => groupIds.Contains(item.Id) ? item with
            {
                StorageBuildingId = null,
                DeliveryBuildingId = null,
                GroundPosition = new InventoryGroundPosition(position.X, position.Y),
            } : item).ToArray(),
        });
        businessTrade = businessTrade with { Listings = businessTrade.Listings.Where(listing => !groupIds.Contains(listing.GoodsLotId)).ToArray() };
        MaintainMarketStalls();
        AppendEvent("business_stock_cleared", $"{actor}|{buildingId}|{lot.Id}|{lot.Quantity}|{position.X}|{position.Y}");
        return new(true, lot.Id);
    }
}
