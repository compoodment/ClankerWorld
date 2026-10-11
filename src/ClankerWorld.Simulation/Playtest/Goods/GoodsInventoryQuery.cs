using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

// Inventory-only selection is also used by captured-checkpoint guidance. Context
// callbacks add native permissions and routes; none are retained or cached.
internal static class GoodsInventoryQuery
{
    internal static GoodsAnswer FindGoods(InventoryCheckpoint inventory, GoodsRequest request,
        Func<InventoryIndex, InventoryLot, GoodsReason?> context,
        Func<InventoryIndex, InventoryLot, GoodsMatch> match)
    {
        Validate(request);
        var index = InventoryIndex.For(inventory);
        var matches = new List<GoodsMatch>();
        var excluded = new List<(string LotId, GoodsReason Reason)>();
        IEnumerable<InventoryLot> candidates = request.Explain ? index.Lots : request.AtBuilding is { } building
            ? index.StoredAt(building) : request.Owners.Ids.Distinct(StringComparer.Ordinal)
                .SelectMany(index.OwnedBy).OrderBy(lot => lot.Id, StringComparer.Ordinal);
        foreach (var lot in candidates)
        {
            var reason = Exclusion(request, index, lot) ?? context(index, lot);
            if (reason is { } failure)
            {
                if (request.Explain) excluded.Add((lot.Id, failure));
                continue;
            }
            matches.Add(match(index, lot));
        }
        return new(matches.AsReadOnly(), excluded.AsReadOnly());
    }

    internal static GoodsAnswer FindConsumableGoods(InventoryCheckpoint inventory, GoodsRequest request,
        Func<InventoryIndex, InventoryLot, GoodsReason?> context)
    {
        Validate(request);
        if (request.Use != GoodsUse.ConsumeAt)
            throw new ArgumentException("Inventory-only selection requires ConsumeAt; other uses need native world rules.", nameof(request));
        return FindGoods(inventory, request, context, (index, lot) => MakeMatch(index, lot, request));
    }

    internal static void Validate(GoodsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Owners);
        ArgumentNullException.ThrowIfNull(request.Kinds);
        if (request.Use is not (GoodsUse.Holdings or GoodsUse.ConsumeAt or GoodsUse.Collect or GoodsUse.ReachableHoldings or
                GoodsUse.RecoveryHoldings or GoodsUse.Recover) ||
            request.Owners.Ids is not { Count: > 0 } || request.Kinds.Ids is not { Count: > 0 } ||
            request.Owners.Ids.Any(string.IsNullOrWhiteSpace) || request.Kinds.Ids.Any(string.IsNullOrWhiteSpace) ||
            request.Near is not null || request.Destination is not null || request.ExtraUnits != 0 ||
            request.Use is (GoodsUse.Collect or GoodsUse.ReachableHoldings or GoodsUse.RecoveryHoldings or GoodsUse.Recover) &&
                string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("This goods query supports exact owners and kinds for Holdings, ConsumeAt, and actor-bound ReachableHoldings, Collect or personal recovery; destination uses are separate steps.", nameof(request));
    }

    internal static bool IsRecovery(GoodsUse use) => use is GoodsUse.RecoveryHoldings or GoodsUse.Recover;

    internal static int Quantity(InventoryIndex index, InventoryLot lot, GoodsUse use) => IsRecovery(use)
        ? Math.Max(0, InventoryRules.UnreservedQuantity(index, lot))
        : use == GoodsUse.ConsumeAt ? InventoryRules.ReservableQuantity(index, lot) : InventoryRules.UsableQuantity(index, lot);

    internal static GoodsReason? Exclusion(GoodsRequest request, InventoryIndex index, InventoryLot lot)
    {
        if (!request.Owners.Ids.Contains(lot.OwnerId, StringComparer.Ordinal) ||
            IsRecovery(request.Use) && lot.OwnerId != request.Actor) return GoodsReason.Owner;
        if (!request.Kinds.Ids.Contains(lot.ItemKind, StringComparer.Ordinal)) return GoodsReason.Kind;
        if (request.AtBuilding is { } building && lot.StorageBuildingId != building) return GoodsReason.Place;
        if (!IsRecovery(request.Use))
        {
            if (lot.ConditionBasisPoints <= 0) return GoodsReason.Damaged;
            if (lot.FreshnessBasisPoints <= 0) return GoodsReason.Spoiled;
            if (!InventoryRules.HasUsableContainer(index, lot) ||
                request.Use == GoodsUse.ConsumeAt && InventoryContainerRules.IsContainer(lot.ItemKind)) return GoodsReason.Vessel;
        }
        if (lot.Quantity == 0) return GoodsReason.Empty;
        if (Quantity(index, lot, request.Use) <= 0) return GoodsReason.Reservation;
        return null;
    }

    private static GoodsMatch MakeMatch(InventoryIndex index, InventoryLot lot, GoodsRequest request)
    {
        var root = index.Root(lot);
        var place = root.StorageBuildingId is { } building ? new GoodsPlace(GoodsPlaceKind.Stored, building,
            DeliveryBuildingId: root.DeliveryBuildingId) :
            root.GroundPosition is { } ground ? new GoodsPlace(GoodsPlaceKind.Ground,
                Position: new(ground.X, ground.Y), DeliveryBuildingId: root.DeliveryBuildingId) :
            new GoodsPlace(root.CarrierId is null ? GoodsPlaceKind.Unlocated : GoodsPlaceKind.Carried,
                CarrierId: root.CarrierId, DeliveryBuildingId: root.DeliveryBuildingId);
        var quantity = Quantity(index, lot, request.Use);
        return new(lot, root, place, quantity,
            InventoryContainerRules.IsContainer(lot.ItemKind) ? index.FamilyQuantity(lot.Id) : quantity);
    }
}
