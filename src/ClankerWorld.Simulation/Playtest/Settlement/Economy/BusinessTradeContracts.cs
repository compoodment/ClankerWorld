using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public enum BusinessOfferState
{
    Open,
    Settled,
    Cancelled,
}

/// <summary>A seller-approved exact exchange backed by stock at this building.</summary>
public sealed record BusinessListing(
    string Id,
    string SellerId,
    string HouseholdId,
    string BuildingId,
    string GoodsLotId,
    int GoodsQuantity,
    string PaymentKind,
    int PaymentQuantity,
    long ExpiryTick);

/// <summary>Both real lots and their destination space are committed until settlement or cancellation.</summary>
public sealed record BusinessOffer(
    string Id,
    string ListingId,
    string SellerId,
    string HouseholdId,
    string BuyerId,
    string BuildingId,
    string GoodsLotId,
    string GoodsKind,
    int GoodsQuantity,
    string PaymentLotId,
    string PaymentKind,
    int PaymentQuantity,
    int ReservedCarrySpace,
    int ReservedStorageSpace,
    long ExpiryTick,
    BusinessOfferState State,
    string? Blocker = null,
    IReadOnlyList<BusinessContainerCommitment>? Contents = null);

public sealed record BusinessContainerCommitment(string ReservationId, string ContainerId,
    string LotId, string ItemKind, string OwnerId, int Quantity);

public sealed record MarketPlot(string MarketId, string TownId, GridPoint Position);

/// <summary>A household holds the stall while it still has actual stock there, including barter receipts.</summary>
public sealed record MarketStall(string MarketId, string BuildingId, string HouseholdId);

public sealed record BusinessToolOrder(
    string Id,
    string BuyerId,
    string HouseholdId,
    string BuildingId,
    string RecipeId,
    string ToolKind,
    long ExpiryTick,
    string State,
    string? JobId = null,
    string? Blocker = null);

public sealed record BusinessTradeState(
    long NextSequence,
    IReadOnlyList<BusinessListing> Listings,
    IReadOnlyList<BusinessOffer> Offers,
    IReadOnlyList<MarketPlot> Markets,
    IReadOnlyList<MarketStall> Stalls,
    IReadOnlyList<BusinessToolOrder> ToolOrders)
{
    public static BusinessTradeState Empty { get; } = new(1, [], [], [], [], []);
}

public sealed record BusinessActionResult(bool Applied, string? Id = null, string? Failure = null);
