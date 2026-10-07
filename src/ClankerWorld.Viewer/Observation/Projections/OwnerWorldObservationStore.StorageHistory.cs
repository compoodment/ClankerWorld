using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Viewer.Observation;

public sealed partial class OwnerWorldObservationStore
{
    private static Dictionary<string, ViewerBuildingStorageChange[]> RecentBuildingStorageChanges(InventoryCheckpoint inventory)
    {
        var history = new Dictionary<string, List<ViewerBuildingStorageChange>>(StringComparer.Ordinal);
        // Read the bounded retained stream once, rather than scanning it for every building.
        for (var index = inventory.Events.Count - 1; index >= 0; index--)
        {
            var item = inventory.Events[index];
            foreach (var change in item.StorageChanges ?? [])
            {
                if (!history.TryGetValue(change.BuildingId, out var rows))
                    history[change.BuildingId] = rows = [];
                if (rows.Count < 10)
                    rows.Add(new(item.EventId, item.WorldTick, change.ItemKind, change.QuantityChange));
            }
        }
        return history.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
