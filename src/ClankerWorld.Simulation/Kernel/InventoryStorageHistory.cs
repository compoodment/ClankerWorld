namespace ClankerWorld.Simulation.Kernel;

/// <summary>Physical stock changes travel with the committed event, rather than a second ledger.</summary>
public static class InventoryStorageHistory
{
    public static IReadOnlyList<InventoryStorageChange>? Changes(
        IReadOnlyList<InventoryLot> before, IReadOnlyList<InventoryLot> after) =>
        ToChanges(Difference(before, after));

    /// <summary>
    /// Covers direct location edits around kernel operations without recording their changes twice.
    /// Existing event IDs and receipt kinds remain intact.
    /// </summary>
    public static InventoryCheckpoint RecordTransition(InventoryCheckpoint before, InventoryCheckpoint after)
    {
        if (ReferenceEquals(before.Lots, after.Lots)) return after;
        var difference = Difference(before.Lots, after.Lots);
        var previousEvent = before.EventHistoryFloor + before.Events.Count;
        foreach (var item in after.Events.Where(item => item.EventId > previousEvent))
            foreach (var change in item.StorageChanges ?? [])
                Add(difference, (change.BuildingId, change.ItemKind), -change.QuantityChange);
        if (ToChanges(difference) is not { } remaining) return after;

        var events = after.Events.ToArray();
        if (events.Length > 0 && events[^1].EventId > previousEvent)
        {
            foreach (var change in events[^1].StorageChanges ?? [])
                Add(difference, (change.BuildingId, change.ItemKind), change.QuantityChange);
            events[^1] = events[^1] with { StorageChanges = ToChanges(difference) };
        }
        else
        {
            events = [.. events, new(after.EventHistoryFloor + events.Length + 1L, after.WorldTick,
                "storage_changed", string.Empty) { StorageChanges = remaining }];
        }
        return after with { Events = events };
    }

    public static void Validate(IReadOnlyList<InventoryEvent> events)
    {
        foreach (var item in events)
        {
            if (item.StorageChanges is not { } changes) continue;
            if (changes.Count == 0) throw new InvalidDataException("Stored inventory changes must not be empty.");
            InventoryStorageChange? previous = null;
            foreach (var change in changes)
            {
                if (change is null || !Canonical(change.BuildingId) || !Canonical(change.ItemKind) ||
                    change.QuantityChange == 0 || change.QuantityChange == long.MinValue ||
                    previous is not null && (StringComparer.Ordinal.Compare(previous.BuildingId, change.BuildingId) > 0 ||
                        previous.BuildingId == change.BuildingId && StringComparer.Ordinal.Compare(previous.ItemKind, change.ItemKind) >= 0))
                    throw new InvalidDataException("Stored inventory changes must have unique ordered building/item identities and nonzero quantities.");
                previous = change;
            }
        }
    }

    private static bool Canonical(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();

    private static Dictionary<(string Building, string Kind), long> Difference(
        IReadOnlyList<InventoryLot> before, IReadOnlyList<InventoryLot> after)
    {
        var difference = new Dictionary<(string Building, string Kind), long>();
        foreach (var lot in before)
            if (lot.StorageBuildingId is { } building) Add(difference, (building, lot.ItemKind), -lot.Quantity);
        foreach (var lot in after)
            if (lot.StorageBuildingId is { } building) Add(difference, (building, lot.ItemKind), lot.Quantity);
        return difference;
    }

    private static void Add(Dictionary<(string Building, string Kind), long> totals,
        (string Building, string Kind) key, long quantity) =>
        totals[key] = checked(totals.GetValueOrDefault(key) + quantity);

    private static InventoryStorageChange[]? ToChanges(Dictionary<(string Building, string Kind), long> totals)
    {
        var changes = totals.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key.Building, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Kind, StringComparer.Ordinal)
            .Select(pair => new InventoryStorageChange(pair.Key.Building, pair.Key.Kind, pair.Value)).ToArray();
        return changes.Length == 0 ? null : changes;
    }
}
