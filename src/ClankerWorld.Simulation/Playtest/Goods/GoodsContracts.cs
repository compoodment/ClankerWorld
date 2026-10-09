using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public enum GoodsUse { Holdings, ConsumeAt, Collect }

public sealed record GoodsOwners(IReadOnlyList<string> Ids)
{
    public static GoodsOwners One(string id) => new([id]);
}

public sealed record GoodsKinds(IReadOnlyList<string> Ids)
{
    public static GoodsKinds One(string id) => new([id]);
}

public sealed record GoodsRequest(GoodsUse Use, string? Actor, GoodsOwners Owners, GoodsKinds Kinds,
    string? AtBuilding = null, GridPoint? Near = null, string? Destination = null, int ExtraUnits = 0, bool Explain = false);

public enum GoodsPlaceKind { Stored, Ground, Carried, Unlocated }
public sealed record GoodsPlace(GoodsPlaceKind Kind, string? BuildingId = null,
    GridPoint? Position = null, string? CarrierId = null, string? DeliveryBuildingId = null);

public enum GoodsReason { Owner, Kind, Place, Damaged, Spoiled, Vessel, Empty, Reservation, Delivery, MarketStall, Custody, CarryRoom, Route }
public sealed record GoodsMatch(InventoryLot Lot, InventoryLot Root, GoodsPlace Place, int Quantity, int MoveUnits);
public sealed record GoodsAnswer(IReadOnlyList<GoodsMatch> Matches, IReadOnlyList<(string LotId, GoodsReason Reason)> Excluded)
{
    public long Total => Matches.Sum(match => (long)match.Quantity);
    public GoodsMatch? First => Matches.Count > 0 ? Matches[0] : null;
}
