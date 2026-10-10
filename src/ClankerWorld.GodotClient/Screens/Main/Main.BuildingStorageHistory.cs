using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly VBoxContainer buildingStorageHistorySection = new() { Visible = false };
    private readonly VBoxContainer buildingStorageHistoryRows = new();
    private string? renderedBuildingStorageHistory;

    private void BuildBuildingStorageHistory()
    {
        buildingStorageHistorySection.AddThemeConstantOverride("separation", 4);
        buildingStorageHistorySection.AddChild(new Label { Text = "RECENT STORAGE CHANGES", ThemeTypeVariation = "SectionLabel" });
        buildingStorageHistoryRows.AddThemeConstantOverride("separation", 3);
        buildingStorageHistorySection.AddChild(buildingStorageHistoryRows);
        buildingDetailsContent.AddChild(buildingStorageHistorySection);
    }

    private void RenderBuildingStorageHistory(OwnerWorldSnapshot snapshot, OwnerWorldPlacedBuilding building)
    {
        var changes = building.RecentStorageChanges;
        buildingStorageHistorySection.Visible = changes is not null &&
            (changes.Count > 0 || building.StoredItems is not null || building.StorageCapacity is > 0);
        var rows = buildingStorageHistorySection.Visible
            ? changes!.Select(change => $"{DisplayWorldClock(change.WorldTick)} · " +
                $"{(change.QuantityChange > 0 ? "+" : "")}{change.QuantityChange} {GameUiText.ItemName(change.ItemKind)}").ToArray()
            : [];
        var signature = snapshot.WorldId + "|" + building.InstanceId + "|" +
            buildingStorageHistorySection.Visible + "|" + string.Join('|', rows);
        if (signature == renderedBuildingStorageHistory) return;
        renderedBuildingStorageHistory = signature;
        foreach (var child in buildingStorageHistoryRows.GetChildren())
        {
            buildingStorageHistoryRows.RemoveChild(child);
            child.QueueFree();
        }
        if (!buildingStorageHistorySection.Visible) return;
        if (rows.Length == 0) rows = ["No recent recorded changes."];
        foreach (var row in rows)
            buildingStorageHistoryRows.AddChild(new Label
            {
                Text = row,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
    }
}
