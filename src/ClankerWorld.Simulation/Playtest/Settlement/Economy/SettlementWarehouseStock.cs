using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int WarehouseLoadQuantity = 4;
    private static readonly HashSet<string> WarehouseResourceKinds =
        new(StringComparer.Ordinal) { "wood", "stone", "fiber", "tree_seed" };

    private PlacedBuilding? WarehouseForResident(string actor)
    {
        var townId = TownForResident(actor);
        return townId is null ? null : WarehousesForTown(townId).FirstOrDefault();
    }

    private IEnumerable<PlacedBuilding> WarehousesForTown(string townId) => worldSimulation.Buildings
        .Where(building => building.TownId == townId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("warehouse", StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal);

    private IEnumerable<PlacedBuilding> WarehousesAccessibleTo(string actor)
    {
        if (!inhabitants.ContainsKey(actor)) yield break;
        var residentTownId = TownForResident(actor);
        foreach (var town in towns.Where(item => item.Id == residentTownId || item.IsAbandoned)
                     .OrderBy(item => item.Id == residentTownId ? 0 : 1)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
            foreach (var warehouse in WarehousesForTown(town.Id))
                yield return warehouse;
    }

    private PlacedBuilding? WarehouseWithAvailableStock(string actor, string itemKind) =>
        WarehousesAccessibleTo(actor).FirstOrDefault(warehouse =>
            society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == warehouse.TownId &&
                lot.StorageBuildingId == warehouse.InstanceId && lot.ItemKind == itemKind &&
                AvailableLotQuantity(lot) > 0));

    private bool MayCollectWarehouseStock(string actor, PlacedBuilding warehouse) =>
        inhabitants.ContainsKey(actor) && warehouse.TownId is { } townId &&
        towns.SingleOrDefault(item => item.Id == townId) is { } town &&
        (TownForResident(actor) == townId || town.IsAbandoned) &&
        WarehousesForTown(townId).Any(item => item.InstanceId == warehouse.InstanceId);

    private IEnumerable<InventoryLot> AvailableWarehouseStock(string actor, string? itemKind = null) =>
        WarehouseStockLots(actor, itemKind).Where(lot => CanReachSharedItem(actor, lot));

    /// <summary>Town Warehouse stock the actor may collect, before checking for a route to it.</summary>
    private IEnumerable<InventoryLot> WarehouseStockLots(string actor, string? itemKind = null) =>
        WarehousesAccessibleTo(actor).Where(warehouse => MayCollectWarehouseStock(actor, warehouse))
            .SelectMany(warehouse => society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.OwnerId == warehouse.TownId && lot.StorageBuildingId == warehouse.InstanceId &&
                lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
                (itemKind is null || lot.ItemKind == itemKind) &&
                AvailableLotQuantity(lot) > 0));

    private IEnumerable<InventoryLot> PersonalWarehouseLots(string actor, string? itemKind = null) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
                lot.DeliveryBuildingId is null && WarehouseResourceKinds.Contains(lot.ItemKind) &&
                (itemKind is null || lot.ItemKind == itemKind) && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal);

    private long PersonalWarehouseAvailableQuantity(string actor, string itemKind) =>
        PersonalWarehouseLots(actor, itemKind).Sum(lot => (long)AvailableLotQuantity(lot));

    private (InventoryLot Lot, int Quantity)? PersonalWarehouseSurplus(string actor, string? itemKind = null,
        string? sourceLotId = null)
    {
        var lots = PersonalWarehouseLots(actor, itemKind).ToArray();
        // Keep four usable units of each kind, regardless of how storage or collection split them.
        // A bound source limits the transfer, not which carried lots can satisfy the personal reserve.
        var spareByKind = lots.GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Math.Max(0L, group.Sum(lot => (long)AvailableLotQuantity(lot)) - WarehouseLoadQuantity),
                StringComparer.Ordinal);
        foreach (var lot in lots)
        {
            if (sourceLotId is not null && lot.Id != sourceLotId) continue;
            var quantity = (int)Math.Min(AvailableLotQuantity(lot), spareByKind[lot.ItemKind]);
            if (quantity > 0) return (lot, quantity);
        }
        return null;
    }

    private void AddWarehouseStockCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (WarehouseForResident(actor) is not { } warehouse || StorageRoom(warehouse.InstanceId) == 0 || PersonalWarehouseSurplus(actor) is not { } surplus ||
            state.Position != warehouse.Position &&
            FindUnoccupiedRoute(actor, state.Position, warehouse.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("store_town_resources",
            $"Carry spare {surplus.Lot.ItemKind} to the Town Warehouse as communal stock for residents.",
            32, warehouse.InstanceId));
    }

    private void StoreTownResources(string actor, PlaytestInhabitantState state)
    {
        if (!AdultResident(actor) || WarehouseForResident(actor) is not { } warehouse ||
            PersonalWarehouseSurplus(actor) is not { } surplus)
            return;
        if (state.Position != warehouse.Position)
        {
            MoveToward(actor, state, warehouse.Position, "town_warehouse", 0);
            return;
        }
        var quantity = Math.Min(StorageRoom(warehouse.InstanceId), Math.Min(WarehouseLoadQuantity, surplus.Quantity));
        if (quantity == 0) return;
        var availableBefore = PersonalWarehouseAvailableQuantity(actor, surplus.Lot.ItemKind);
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"warehouse-stock:{WorldTick}:{actor}", actor, warehouse.TownId!, surplus.Lot.Id,
            quantity, "town_resources_stored", warehouse.InstanceId));
        RecordNonviolentGoodsCompletion(actor, surplus.Lot, warehouse.TownId!, quantity, warehouse.InstanceId,
            $"warehouse-stock:{WorldTick}:{actor}", "public_service_goods", availableBefore);
        AppendEvent("town_resources_stored", $"{actor}:{surplus.Lot.Id}:{quantity}:{warehouse.InstanceId}");
    }

    private DeliveryOrderPlan? GetTownOrderPlan(string actor, PlaytestInhabitantState person,
        OwnerInstructionOrder order, int maximumQuantity)
    {
        if (!AdultResident(actor) || TownForResident(actor) is not { } townId ||
            WarehousesForTown(townId).FirstOrDefault(building => DeliveryDestinationMatches(order, building)) is not { } warehouse ||
            PersonalWarehouseSurplus(actor, order.TargetItemKind, order.DeliveryLotId) is not { } surplus)
            return null;
        var quantity = Math.Min(maximumQuantity, Math.Min(StorageRoom(warehouse.InstanceId),
            Math.Min(WarehouseLoadQuantity, surplus.Quantity)));
        return quantity <= 0 ? null : new DeliveryOrderPlan("town_surplus", warehouse, townId,
            surplus.Lot, surplus.Lot, quantity, quantity, DirectDelivery: true);
    }
}
