using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int WarehouseLoadQuantity = 4;
    private static readonly HashSet<string> WarehouseResourceKinds =
        new(StringComparer.Ordinal) { "wood", "stone", "fiber", "seed" };
    private static readonly HashSet<string> WarehouseFoodKinds =
        new(StringComparer.Ordinal) { "food", "fruit", "grain", "flour", "potato", "greens", "bread", "porridge", "stew" };

    private PlacedBuilding? WarehouseForResident(string actor)
    {
        var townId = TownForResident(actor);
        return townId is null ? null : worldSimulation.Buildings
            .Where(building => building.TownId == townId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("warehouse", StringComparer.Ordinal)))
            .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();
    }

    private InventoryLot? PersonalWarehouseSurplus(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null &&
            lot.DeliveryBuildingId is null && WarehouseResourceKinds.Contains(lot.ItemKind) &&
            AvailableLotQuantity(lot) > WarehouseLoadQuantity)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddWarehouseStockCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (WarehouseForResident(actor) is not { } warehouse || PersonalWarehouseSurplus(actor) is not { } surplus ||
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
        var quantity = Math.Min(WarehouseLoadQuantity, AvailableLotQuantity(surplus) - WarehouseLoadQuantity);
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"warehouse-stock:{WorldTick}:{actor}", actor, warehouse.TownId!, surplus.Id,
            quantity, "town_resources_stored", warehouse.InstanceId));
        AppendEvent("town_resources_stored", $"{actor}:{surplus.Id}:{quantity}:{warehouse.InstanceId}");
    }
}
