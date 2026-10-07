using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyBuildingStorageHistoryAsync(OwnerWorldSnapshot baseMap)
    {
        var house = new OwnerWorldPlacedBuilding("storage-history-ui", "test/house", new(2, 2), 0,
            "House", ["house"], StoredItems: [new("wood", 2)], StorageCapacity: 128, StoredQuantity: 2)
        {
            RecentStorageChanges = [new(2, 20, "wood", -1), new(1, 10, "wood", 3)],
        };
        var map = baseMap with { WorldTick = 30, PlacedBuildings = [house], ProductionJobs = [] };
        ClearBuildingSelection();
        RenderMap(map);
        SelectBuilding(house.InstanceId);
        buildingDetailsButton.EmitSignal(BaseButton.SignalName.Pressed);
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string[] Rows() => buildingStorageHistoryRows.GetChildren().OfType<Label>().Select(label => label.Text).ToArray();
        if (!buildingDetailsPanel.Visible || !buildingStorageHistorySection.Visible || Rows().Length != 2 ||
            !Rows()[0].EndsWith("· -1 Wood", StringComparison.Ordinal) || !Rows()[1].EndsWith("· +3 Wood", StringComparison.Ordinal))
            throw new InvalidOperationException("Details must display recorded storage additions/removals and their recorded times in newest-first order.");
        RenderBuildingCard(map with { PlacedBuildings = [house with { RecentStorageChanges = [new(2, 20, "wood", -2)] }] });
        if (Rows().Length != 1 || !Rows()[0].EndsWith("· -2 Wood", StringComparison.Ordinal))
            throw new InvalidOperationException("Changed storage facts must refresh independently of the building, world time or event identity.");
        RenderBuildingCard(map with
        {
            WorldId = "other-history-world",
            PlacedBuildings = [house with
        {
            RecentStorageChanges = Enumerable.Range(1, 10).Reverse().Select(index =>
                new OwnerWorldBuildingStorageChange(index, index, "grain", index)).ToArray(),
        }]
        });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (Rows().Length != 10 || !Rows()[0].EndsWith("· +10 Grain", StringComparison.Ordinal) ||
            Rows().Any(row => row.Contains("Wood", StringComparison.Ordinal)) ||
            !mapCanvas.GetGlobalRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()))
            throw new InvalidOperationException("World changes must replace storage history, and the complete recent list must stay inside scrolling Details.");
        RenderBuildingCard(map with { PlacedBuildings = [house with { RecentStorageChanges = [] }] });
        if (!buildingStorageHistorySection.Visible || Rows() is not ["No recent recorded changes."])
            throw new InvalidOperationException("A known empty recent history must say no changes were recorded.");
        RenderBuildingCard(map with { PlacedBuildings = [house with { RecentStorageChanges = null }] });
        if (buildingStorageHistorySection.Visible || Rows().Length != 0)
            throw new InvalidOperationException("A host without recorded history must clear it without guessing from item totals.");
        var other = house with { InstanceId = "another-history-building", RecentStorageChanges = [new(3, 25, "stone", 4)] };
        selectedBuildingId = other.InstanceId;
        RenderBuildingCard(map with { PlacedBuildings = [other] });
        if (Rows().Length != 1 || !Rows()[0].EndsWith("· +4 Stone", StringComparison.Ordinal))
            throw new InvalidOperationException("Selecting another building must replace the previous building's history.");
        ClearBuildingSelection();
        RenderMap(baseMap);
    }
}
