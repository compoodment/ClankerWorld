using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// Light on-map feedback that never takes clicks: a compact readout of the
/// ground under the pointer, with Town-site advice during paused setup. The
/// HUD's pause control already shows when time is stopped.
/// </summary>
public partial class Main
{
    private readonly PanelContainer hoverReadout = new();
    private readonly Label hoverReadoutLabel = new();
    private Vector2I? hoverReadoutTile;
    private OwnerWorldSnapshot? hoverReadoutSnapshot;
    private bool hoverReadoutTownSiteMode;

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
            Math.Max(14, UiSize.X - hoverReadout.Size.X - 14),
            Math.Max(14, UiSize.Y - hoverReadout.Size.Y - 14));
    }

    /// <summary>Names the hovered ground in one line; refreshes when the tile or observed world changes.</summary>
    private void UpdateHoverReadout(OwnerWorldSnapshot? snapshot, Vector2I? tile)
    {
        if (snapshot is null || tile is not { } point || terrainMap is null || !MapContains(snapshot, point.X, point.Y))
        {
            hoverReadoutTile = null;
            hoverReadoutSnapshot = null;
            hoverReadoutTownSiteMode = false;
            hoverReadout.Hide();
            return;
        }
        var townSiteMode = choosingFirstTownSite;
        if (hoverReadoutTile == point && ReferenceEquals(hoverReadoutSnapshot, snapshot) &&
            hoverReadoutTownSiteMode == townSiteMode && hoverReadout.Visible) return;
        hoverReadoutTile = point;
        hoverReadoutSnapshot = snapshot;
        hoverReadoutTownSiteMode = townSiteMode;
        var siteAdvice = townSiteMode
            ? terrainLayer.CurrentTownSiteGuidance?.At(point.X, point.Y)
            : null;
        hoverReadoutLabel.Text = HoverSummary(snapshot, terrainMap, point, siteAdvice);
        hoverReadout.Show();
        PositionMapHud();
    }

    /// <summary>The saved bridge whose deck covers this tile, if any.</summary>
    private static OwnerWorldBridge? BridgeAt(OwnerWorldSnapshot snapshot, Vector2I tile) =>
        snapshot.Bridges.FirstOrDefault(bridge => bridge.Span.Any(point => point.X == tile.X && point.Y == tile.Y));

    private static string HoverSummary(OwnerWorldSnapshot snapshot, WorldTerrainMap terrain, Vector2I tile,
        TownSiteAssessment? siteAdvice = null)
    {
        var parts = new List<string>();
        var water = WorldTerrainMap.HydrologyName(terrain.HydrologyAt(tile.X, tile.Y));
        parts.Add(water is not null and not "Land"
            ? water
            : WorldTerrainMap.SurfaceName(terrain.SurfaceAt(tile.X, tile.Y)) ?? WorldTerrainMap.NameFor(terrain.At(tile.X, tile.Y)));
        if (WorldTerrainMap.VegetationName(terrain.VegetationAt(tile.X, tile.Y)) is { } vegetation and not "None")
            parts.Add(vegetation);
        if (terrain.IsHillAt(tile.X, tile.Y)) parts.Add("Hills");
        var building = snapshot.PlacedBuildings.FirstOrDefault(item =>
            tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
            tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height);
        if (building is not null) parts.Add(building.DisplayName ?? Pretty(building.DefinitionId));
        else if (snapshot.Resources.FirstOrDefault(item => item.Position.X == tile.X && item.Position.Y == tile.Y) is { } resource)
            parts.Add(resource.TreeKind is { } tree ? $"{Pretty(tree)} tree"
                : WorldTerrainMap.NaturalObjectName(resource.NaturalObjectKind) ?? $"{Pretty(resource.Kind)} site");
        if (snapshot.RoadTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) parts.Add("Road");
        if (BridgeAt(snapshot, tile) is not null) parts.Add("Bridge");
        if (snapshot.Towns.FirstOrDefault(town => town.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) is { } owner)
            parts.Add(owner.Name);
        var summary = string.Join(" · ", parts);
        return siteAdvice is { } advice
            ? $"{summary}\nTown-site advice: {TownSiteFactorSummary(advice)}"
            : summary;
    }

    private static string TownSiteFactorSummary(TownSiteAssessment advice)
    {
        var roadSpace = advice.OpenGroundForRoads switch
        {
            >= 0.7f => "room for Roads",
            >= 0.35f => "some road space",
            _ => "little open ground for Roads",
        };
        return string.Join(" · ",
            advice.HasNearbyFood ? "nearby food" : "no food nearby",
            advice.HasNearbyFarmland ? "fertile ground nearby" : "little fertile ground nearby",
            advice.HasNearbyWood ? "nearby wood" : "no wood nearby",
            advice.HasNearbyStone ? "nearby stone" : "no stone nearby",
            roadSpace);
    }
}
