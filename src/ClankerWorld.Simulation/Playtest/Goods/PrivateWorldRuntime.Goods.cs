using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    /// <summary>Read-only, deterministic selection. Recheck immediately before applying an action.</summary>
    public GoodsAnswer FindGoods(GoodsRequest request) => FindGoods(society.Checkpoint.Inventory, request);

    public GoodsMatch? RecheckGoods(GoodsRequest request, string lotId) =>
        RecheckGoods(society.Checkpoint.Inventory, request, lotId);

    private GoodsMatch? RecheckGoods(InventoryCheckpoint inventory, GoodsRequest request, string lotId)
    {
        GoodsInventoryQuery.Validate(request);
        var index = InventoryIndex.For(inventory);
        return index.Find(lotId) is { } lot && GoodsExclusion(request, index, lot) is null
            ? MakeGoodsMatch(index, lot, request) : null;
    }

    private GoodsAnswer FindGoods(InventoryCheckpoint inventory, GoodsRequest request)
    {
        var routes = request.Use is GoodsUse.Collect or GoodsUse.ReachableHoldings or GoodsUse.Recover
            ? new Dictionary<(GridPoint Position, int Range), bool>() : null;
        return GoodsInventoryQuery.FindGoods(inventory, request,
            (index, lot) => GoodsContextExclusion(request, index, lot, routes),
            (index, lot) => MakeGoodsMatch(index, lot, request));
    }

    private GoodsMatch MakeGoodsMatch(InventoryIndex index, InventoryLot lot, GoodsRequest request)
    {
        var quantity = GoodsQuantity(index, lot, request);
        var root = index.Root(lot);
        return new(lot, root, GoodsPlaceFor(root), quantity,
            InventoryContainerRules.IsContainer(lot.ItemKind) ? index.FamilyQuantity(lot.Id) :
                request.Use == GoodsUse.Collect && InventoryContainerRules.IsContainer(root.ItemKind)
                    ? index.FamilyQuantity(root.Id) : quantity);
    }

    private GoodsReason? GoodsExclusion(GoodsRequest request, InventoryIndex index, InventoryLot lot,
        Dictionary<(GridPoint Position, int Range), bool>? routes = null)
    {
        return GoodsInventoryQuery.Exclusion(request, index, lot) ?? GoodsContextExclusion(request, index, lot, routes);
    }

    private GoodsReason? GoodsContextExclusion(GoodsRequest request, InventoryIndex index, InventoryLot lot,
        Dictionary<(GridPoint Position, int Range), bool>? routes = null)
    {
        if (GoodsInventoryQuery.IsRecovery(request.Use)) return RecoveryGoodsExclusion(request, index, lot, routes);
        if (request.Use is GoodsUse.Holdings or GoodsUse.Collect or GoodsUse.ReachableHoldings)
        {
            var root = index.Root(lot);
            if (root.DeliveryBuildingId is not null) return GoodsReason.Delivery;
            if (OnBorrowedMarketStall(root)) return GoodsReason.MarketStall;
        }
        if (request.Use == GoodsUse.Collect) return CollectGoodsExclusion(request.Actor!, index, lot, routes);
        if (request.Use == GoodsUse.ReachableHoldings) return GoodsRouteExclusion(request.Actor!, index.Root(lot), routes);
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
