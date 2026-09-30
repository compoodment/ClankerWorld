using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// Light on-map feedback that never takes clicks: a one-line readout of the
/// ground under the pointer so the map can be explored without opening the
/// tile card. The HUD's pause control already shows when time is stopped.
/// </summary>
public partial class Main
{
    private readonly PanelContainer hoverReadout = new();
    private readonly Label hoverReadoutLabel = new();
    private Vector2I? hoverReadoutTile;
    private OwnerWorldSnapshot? hoverReadoutSnapshot;

    private void BuildMapHud(Control canvas)
    {
        hoverReadout.ThemeTypeVariation = "HudPanel";
        hoverReadout.AddChild(hoverReadoutLabel);
        hoverReadout.Hide();

        foreach (var control in new Control[] { hoverReadout, hoverReadoutLabel })
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
        hoverReadout.ZIndex = 60;
        hoverReadout.Resized += PositionMapHud;
        canvas.AddChild(hoverReadout);
    }

    private void PositionMapHud()
    {
        hoverReadout.Size = hoverReadout.GetCombinedMinimumSize();
        hoverReadout.Position = new Vector2(
            Math.Max(14, mapCanvas.Size.X - hoverReadout.Size.X - 14),
            Math.Max(14, mapCanvas.Size.Y - hoverReadout.Size.Y - 14));
    }

    /// <summary>Names the hovered ground in one line; refreshes when the tile or observed world changes.</summary>
    private void UpdateHoverReadout(OwnerWorldSnapshot? snapshot, Vector2I? tile)
    {
        if (snapshot is null || tile is not { } point || terrainMap is null || !MapContains(snapshot, point.X, point.Y))
        {
            hoverReadoutTile = null;
            hoverReadoutSnapshot = null;
            hoverReadout.Hide();
            return;
        }
        if (hoverReadoutTile == point && ReferenceEquals(hoverReadoutSnapshot, snapshot) && hoverReadout.Visible) return;
        hoverReadoutTile = point;
        hoverReadoutSnapshot = snapshot;
        hoverReadoutLabel.Text = HoverSummary(snapshot, terrainMap, point);
        hoverReadout.Show();
        PositionMapHud();
    }

    private static string HoverSummary(OwnerWorldSnapshot snapshot, WorldTerrainMap terrain, Vector2I tile)
    {
        var parts = new List<string>();
        var water = WorldTerrainMap.HydrologyName(terrain.HydrologyAt(tile.X, tile.Y));
        parts.Add(water is not null and not "Land"
            ? water
            : WorldTerrainMap.SurfaceName(terrain.SurfaceAt(tile.X, tile.Y)) ?? WorldTerrainMap.NameFor(terrain.At(tile.X, tile.Y)));
        if (WorldTerrainMap.VegetationName(terrain.VegetationAt(tile.X, tile.Y)) is { } vegetation and not "None")
            parts.Add(vegetation);
        var building = snapshot.PlacedBuildings.FirstOrDefault(item =>
            tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
            tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height);
        if (building is not null) parts.Add(building.DisplayName ?? Pretty(building.DefinitionId));
        else if (snapshot.Resources.FirstOrDefault(item => item.Position.X == tile.X && item.Position.Y == tile.Y) is { } resource)
            parts.Add(resource.TreeKind is { } tree ? $"{Pretty(tree)} tree"
                : WorldTerrainMap.NaturalObjectName(resource.NaturalObjectKind) ?? $"{Pretty(resource.Kind)} site");
        if (snapshot.RoadTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) parts.Add("Road");
        if (snapshot.Towns.FirstOrDefault(town => town.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) is { } owner)
            parts.Add(owner.Name);
        return $"{string.Join(" · ", parts)}   {tile.X}, {tile.Y}";
    }
}
