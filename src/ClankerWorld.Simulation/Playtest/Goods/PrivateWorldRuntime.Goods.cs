using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    /// <summary>Read-only, deterministic selection. Recheck immediately before applying an action.</summary>
    public GoodsAnswer FindGoods(GoodsRequest request) => FindGoods(society.Checkpoint.Inventory, request);

    public GoodsMatch? RecheckGoods(GoodsRequest request, string lotId) =>
        RecheckGoods(society.Checkpoint.Inventory, request, lotId);

    private static void ValidateGoodsRequest(GoodsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Owners);
        ArgumentNullException.ThrowIfNull(request.Kinds);
        if (request.Use is not (GoodsUse.Holdings or GoodsUse.ConsumeAt) ||
            request.Owners.Ids is not { Count: > 0 } || request.Kinds.Ids is not { Count: > 0 } ||
            request.Owners.Ids.Any(string.IsNullOrWhiteSpace) || request.Kinds.Ids.Any(string.IsNullOrWhiteSpace) ||
            request.Near is not null || request.Destination is not null || request.ExtraUnits != 0)
            throw new ArgumentException("This goods query supports exact owners and kinds for Holdings or ConsumeAt; travel and destination uses are separate steps.", nameof(request));
    }

    private GoodsMatch? RecheckGoods(InventoryCheckpoint inventory, GoodsRequest request, string lotId)
    {
        ValidateGoodsRequest(request);
        var index = InventoryIndex.For(inventory);
        return index.Find(lotId) is { } lot && GoodsExclusion(request, index, lot) is null
            ? MakeGoodsMatch(index, lot, request.Use) : null;
    }

    private GoodsAnswer FindGoods(InventoryCheckpoint inventory, GoodsRequest request)
    {
        ValidateGoodsRequest(request);
        var index = InventoryIndex.For(inventory);
        var matches = new List<GoodsMatch>();
        var excluded = new List<(string LotId, GoodsReason Reason)>();
        IEnumerable<InventoryLot> candidates = request.Explain ? index.Lots : request.AtBuilding is { } building
            ? index.StoredAt(building) : request.Owners.Ids.Distinct(StringComparer.Ordinal)
                .SelectMany(index.OwnedBy).OrderBy(lot => lot.Id, StringComparer.Ordinal);
        foreach (var lot in candidates)
        {
            var reason = GoodsExclusion(request, index, lot);
            if (reason is { } failure)
            {
                if (request.Explain) excluded.Add((lot.Id, failure));
                continue;
            }
            matches.Add(MakeGoodsMatch(index, lot, request.Use));
        }
        return new(matches.AsReadOnly(), excluded.AsReadOnly());
    }

    private GoodsMatch MakeGoodsMatch(InventoryIndex index, InventoryLot lot, GoodsUse use)
    {
        var quantity = use == GoodsUse.ConsumeAt
            ? InventoryRules.ReservableQuantity(index, lot) : InventoryRules.UsableQuantity(index, lot);
        return new(lot, index.Root(lot), GoodsPlaceFor(index.Root(lot)), quantity,
            InventoryContainerRules.IsContainer(lot.ItemKind) ? index.FamilyQuantity(lot.Id) : quantity);
    }

    private GoodsReason? GoodsExclusion(GoodsRequest request, InventoryIndex index, InventoryLot lot)
    {
        if (!request.Owners.Ids.Contains(lot.OwnerId, StringComparer.Ordinal)) return GoodsReason.Owner;
        if (!request.Kinds.Ids.Contains(lot.ItemKind, StringComparer.Ordinal)) return GoodsReason.Kind;
        if (request.AtBuilding is { } building && lot.StorageBuildingId != building) return GoodsReason.Place;
        if (lot.ConditionBasisPoints <= 0) return GoodsReason.Damaged;
        if (lot.FreshnessBasisPoints <= 0) return GoodsReason.Spoiled;
        if (!InventoryRules.HasUsableContainer(index, lot) ||
            request.Use == GoodsUse.ConsumeAt && InventoryContainerRules.IsContainer(lot.ItemKind)) return GoodsReason.Vessel;
        if (lot.Quantity == 0) return GoodsReason.Empty;
        if ((request.Use == GoodsUse.ConsumeAt ? InventoryRules.ReservableQuantity(index, lot) :
                InventoryRules.UsableQuantity(index, lot)) <= 0) return GoodsReason.Reservation;
        if (request.Use == GoodsUse.Holdings)
        {
            var root = index.Root(lot);
            if (root.DeliveryBuildingId is not null) return GoodsReason.Delivery;
            if (OnBorrowedMarketStall(root)) return GoodsReason.MarketStall;
        }
        return null;
    }

    private GoodsPlace GoodsPlaceFor(InventoryLot root)
    {
        if (root.StorageBuildingId is { } id)
            return new(GoodsPlaceKind.Stored, id,
                worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == id)?.Position,
                DeliveryBuildingId: root.DeliveryBuildingId);
        if (root.GroundPosition is { } ground)
            return new(GoodsPlaceKind.Ground, Position: new GridPoint(ground.X, ground.Y), DeliveryBuildingId: root.DeliveryBuildingId);
        var carrier = root.CarrierId ?? (inhabitants.ContainsKey(root.OwnerId) ? root.OwnerId : null);
        return carrier is not null
            ? new(GoodsPlaceKind.Carried, Position: inhabitants.GetValueOrDefault(carrier)?.Position,
                CarrierId: carrier, DeliveryBuildingId: root.DeliveryBuildingId)
            : new(GoodsPlaceKind.Unlocated, DeliveryBuildingId: root.DeliveryBuildingId);
    }
}
