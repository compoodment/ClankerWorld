using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private int GoodsQuantity(InventoryIndex index, InventoryLot lot, GoodsRequest request)
    {
        var quantity = request.Use == GoodsUse.ConsumeAt
            ? InventoryRules.ReservableQuantity(index, lot) : InventoryRules.UsableQuantity(index, lot);
        if (request.Use != GoodsUse.Collect || PersonalEquipmentRules.IsCarried(index.Root(lot), request.Actor!))
            return quantity;
        var room = FreeCarryCapacity(request.Actor!);
        var root = index.Root(lot);
        return InventoryContainerRules.IsContainer(root.ItemKind)
            ? index.FamilyQuantity(root.Id) <= room ? quantity : 0 : Math.Min(quantity, room);
    }

    private GoodsReason? CollectGoodsExclusion(string actor, InventoryIndex index, InventoryLot lot,
        Dictionary<(GridPoint Position, int Range), bool>? routes)
    {
        if (!inhabitants.TryGetValue(actor, out var person)) return GoodsReason.Custody;
        var root = index.Root(lot);
        if (root.CarrierId is { } carrier && carrier != actor) return GoodsReason.Custody;
        if (!MayCollectGoodsAt(actor, root)) return GoodsReason.Place;
        if (InventoryContainerRules.IsContainer(root.ItemKind) && !InventoryRules.CanMoveFamily(index, root))
            return GoodsReason.Reservation;
        if (handcartHitches.Any(hitch => hitch.CartLotId == root.Id) || inhabitants.Values.Any(person =>
                PersonalEquipmentRules.IsSelected(person.Equipment, root.Id))) return GoodsReason.Custody;
        if (PersonalEquipmentRules.IsCarried(root, actor)) return null;
        var room = FreeCarryCapacity(actor);
        if (room <= 0 || InventoryContainerRules.IsContainer(root.ItemKind) && index.FamilyQuantity(root.Id) > room)
            return GoodsReason.CarryRoom;
        return GoodsRouteExclusion(actor, root, routes);
    }

    private GoodsReason? GoodsRouteExclusion(string actor, InventoryLot root,
        Dictionary<(GridPoint Position, int Range), bool>? routes)
    {
        if (!inhabitants.TryGetValue(actor, out var person)) return GoodsReason.Custody;
        var key = (HouseholdStockPosition(root), HouseholdStockInteractionRange(root));
        if (routes is not null && routes.TryGetValue(key, out var reachable)) return reachable ? null : GoodsReason.Route;
        reachable = FindUnoccupiedRoute(actor, person.Position, key.Item1, key.Item2).Count > 0;
        routes?.Add(key, reachable);
        return reachable ? null : GoodsReason.Route;
    }

    private bool MayCollectGoodsAt(string actor, InventoryLot root)
    {
        if (root.OwnerId == actor)
            return PersonalEquipmentRules.IsCarried(root, actor) || PersonalGoodsAwaitingCollection(actor).Any(lot => lot.Id == root.Id);
        if (root.OwnerId == HouseholdFor(actor)) return true;
        if (PersonalEquipmentRules.IsCarried(root, actor) && worldSimulation.Buildings.Any(building =>
                building.HouseholdId == root.OwnerId && worldContent.Buildings.Any(definition =>
                    definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(AnimalContent.YardTag)) &&
                MaySupplyAnimalYard(actor, building))) return true;
        if (root.StorageBuildingId is not { } storage) return false;
        var building = worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == storage);
        if (building is null) return false;
        if (building.TownId == root.OwnerId && MayCollectWarehouseStock(actor, building)) return true;
        return building.HouseholdId == root.OwnerId && worldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(AnimalContent.YardTag)) &&
            MaySupplyAnimalYard(actor, building);
    }
}
