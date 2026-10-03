using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int WarehouseLoadQuantity = 4;
    private static readonly HashSet<string> WarehouseResourceKinds =
        new(StringComparer.Ordinal) { "wood", "stone", "fiber", "tree_seed" };
    private static readonly HashSet<string> WarehouseFoodKinds =
        new(StringComparer.Ordinal) { "food", "fruit", "grain", "flour", "potatoes", "berries", "wild_greens", "cultivated_greens", "bread", "porridge", "stew", "simple_meal", "berry_porridge", "fruit_porridge", "restaurant_meal" };

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
        foreach (var town in towns.Where(item => item.Id == residentTownId || item.ResidentIds.Count == 0)
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
        (TownForResident(actor) == townId || town.ResidentIds.Count == 0) &&
        WarehousesForTown(townId).Any(item => item.InstanceId == warehouse.InstanceId);

    private IEnumerable<InventoryLot> AvailableWarehouseStock(string actor, string? itemKind = null) =>
        WarehousesAccessibleTo(actor).Where(warehouse => MayCollectWarehouseStock(actor, warehouse))
            .SelectMany(warehouse => society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.OwnerId == warehouse.TownId && lot.StorageBuildingId == warehouse.InstanceId &&
                lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
                (itemKind is null || lot.ItemKind == itemKind) &&
                AvailableLotQuantity(lot) > 0))
            .Where(lot => CanReachSharedItem(actor, lot));

    private InventoryLot? PersonalWarehouseSurplus(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
            lot.DeliveryBuildingId is null && WarehouseResourceKinds.Contains(lot.ItemKind) &&
            AvailableLotQuantity(lot) > WarehouseLoadQuantity)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddWarehouseStockCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (WarehouseForResident(actor) is not { } warehouse || StorageRoom(warehouse.InstanceId) == 0 || PersonalWarehouseSurplus(actor) is not { } surplus ||
            state.Position != warehouse.Position &&
            FindUnoccupiedRoute(actor, state.Position, warehouse.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("store_town_resources",
            $"Carry spare {surplus.ItemKind} to the Town Warehouse as communal stock for residents.",
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
        var quantity = Math.Min(StorageRoom(warehouse.InstanceId), Math.Min(WarehouseLoadQuantity, AvailableLotQuantity(surplus) - WarehouseLoadQuantity));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"warehouse-stock:{WorldTick}:{actor}", actor, warehouse.TownId!, surplus.Id,
            quantity, "town_resources_stored", warehouse.InstanceId));
        AppendEvent("town_resources_stored", $"{actor}:{surplus.Id}:{quantity}:{warehouse.InstanceId}");
    }
}
