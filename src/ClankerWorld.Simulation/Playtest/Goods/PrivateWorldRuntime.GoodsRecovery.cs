using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private GoodsRequest? PersonalRecoveryRequest(string actor, GoodsUse use, string? kind = null)
    {
        var kinds = kind is not null ? new[] { kind } : InventoryIndex.For(society.Checkpoint.Inventory)
            .OwnedBy(actor).Select(lot => lot.ItemKind).Distinct(StringComparer.Ordinal).ToArray();
        return kinds.Length == 0 ? null : new(use, actor, GoodsOwners.One(actor), new GoodsKinds(kinds));
    }

    private IEnumerable<InventoryLot> RecoverablePersonalGoods(string actor, string? kind = null) =>
        PersonalRecoveryRequest(actor, GoodsUse.Recover, kind) is { } request
            ? FindGoods(request).Matches.Select(match => match.Lot) : [];

    private GoodsReason? RecoveryGoodsExclusion(GoodsRequest request, InventoryIndex index, InventoryLot lot,
        Dictionary<(GridPoint Position, int Range), bool>? routes)
    {
        var actor = request.Actor!;
        if (!inhabitants.ContainsKey(actor)) return GoodsReason.Custody;
        if (PersonalRecoveryPropertyExclusion(actor, index, lot) is { } reason) return reason;
        if (!MayRecoverPersonalGoodsAt(actor, lot)) return GoodsReason.Place;
        if (request.Use == GoodsUse.RecoveryHoldings) return null;
        var room = FreeCarryCapacity(actor);
        if (room <= 0 || InventoryContainerRules.IsContainer(lot.ItemKind) && index.FamilyQuantity(lot.Id) > room)
            return GoodsReason.CarryRoom;
        return PersonalRecoveryRouteExclusion(actor, lot, routes);
    }

    private GoodsReason? PersonalRecoveryPropertyExclusion(string actor, InventoryIndex index, InventoryLot lot)
    {
        // Recovery moves physical property, including damaged vessels and spoiled contents.
        // A parked cart is pulled with its cargo rather than carried as a recovery load.
        if (lot.OwnerId != actor) return GoodsReason.Owner;
        if (PersonalEquipmentRules.IsCarried(lot, actor) || lot.CarrierId is not null) return GoodsReason.Custody;
        if (lot.ItemKind == InventoryContainerRules.Handcart || lot.ContainerLotId is not null) return GoodsReason.Place;
        if (MarketTradeRules.IsLoose(lot) && OnMarketStall(lot)) return GoodsReason.MarketStall;
        if (lot.DeliveryBuildingId is not null) return GoodsReason.Delivery;
        if (Math.Max(0, InventoryRules.UnreservedQuantity(index, lot)) <= 0 ||
            InventoryContainerRules.IsContainer(lot.ItemKind) && !InventoryRules.CanMoveFamily(index, lot))
            return GoodsReason.Reservation;
        return null;
    }

    private bool MayRecoverPersonalGoodsAt(string actor, InventoryLot lot) => lot.GroundPosition is not null ||
        lot.StorageBuildingId is { } storageId && worldSimulation.Buildings.Any(building => building.InstanceId == storageId &&
            (building.HouseholdId is { } home && (society.Checkpoint.GetInhabitant(actor).HouseholdId == home ||
             inhabitants[actor].Departures?.Any(departure => departure.HouseholdId == home) == true ||
             HasCareGroupDepartureFrom(actor, home) || IsStoredSettledBequest(actor, lot, storageId)) ||
             towns.Any(town => town.LandHearings.Cases.Any(item => item.Property?.Transfer?.PriorBuilding.InstanceId == storageId))));

    private GoodsReason? PersonalRecoveryRouteExclusion(string actor, InventoryLot lot,
        Dictionary<(GridPoint Position, int Range), bool>? routes)
    {
        var position = inhabitants[actor].Position;
        var key = (HouseholdStockPosition(lot), lot.GroundPosition is not null ? ResourceInteractionRange : 1);
        if (routes is not null && routes.TryGetValue(key, out var reachable)) return reachable ? null : GoodsReason.Route;
        reachable = IsWithinInteractionRange(position, key.Item1, key.Item2) ||
            FindUnoccupiedRoute(actor, position, key.Item1, key.Item2).Count > 0;
        routes?.Add(key, reachable);
        return reachable ? null : GoodsReason.Route;
    }
}
