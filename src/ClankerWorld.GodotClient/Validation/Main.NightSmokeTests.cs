using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Night follows the host's darkness: daylight draws nothing, dusk eases
    /// in instead of jumping, and full night is a gentle wash over the ground
    /// at overview and detail zoom alike. The wash lies under map labels,
    /// agent markers, weather and every panel, and looks the same in both
    /// themes.
    /// </summary>
    private async Task VerifyNightWashAsync(OwnerWorldSnapshot map)
    {
        var (zoomBefore, centerBefore) = (cameraZoom, cameraCenterTiles);
        RenderMap(map with { DarknessBasisPoints = 0 });
        nightLayer.Settle();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (nightLayer.ShownDarkness != 0 || nightLayer.CurrentWash.A != 0 || nightLayer.DrawnArea.HasArea())
            throw new InvalidOperationException("Daylight must leave the map untinted.");
        var layers = mapStage.GetChildren();
        if (nightLayer.GetParent() != mapStage || layers.IndexOf(nightLayer) != layers.IndexOf(terrainLayer) + 1 ||
            layers.IndexOf(objectLayer) < layers.IndexOf(nightLayer) || layers.IndexOf(entityLayer) < layers.IndexOf(nightLayer) ||
            layers.IndexOf(weatherLayer) < layers.IndexOf(nightLayer) || nightLayer.GetChildCount() != 0 ||
            nightLayer.MouseFilter != Control.MouseFilterEnum.Ignore || mapCanvas.GetChildren().IndexOf(uiLayer) < mapCanvas.GetChildren().IndexOf(mapStage) ||
            nightLayer.ZIndex != terrainLayer.ZIndex || objectLayer.ZIndex < nightLayer.ZIndex || entityLayer.ZIndex < nightLayer.ZIndex)
            throw new InvalidOperationException("The night wash must lie just over the ground, under map labels, agents, weather and panels, and let clicks through.");

        // The host reports darkness once a tick; the wash eases between readings.
        RenderMap(map with { DarknessBasisPoints = 10_000 });
        nightLayer._Process(0.1);
        if (nightLayer.ShownDarkness is <= 0 or >= 0.1f)
            throw new InvalidOperationException($"Dusk must fade in rather than jump to full night: {nightLayer.ShownDarkness}.");
        nightLayer.Settle();
        var wash = nightLayer.CurrentWash;
        if (!Mathf.IsEqualApprox(wash.A, NightLayer.FullNightAlpha) || wash.A > 0.4f + 0.001f)
            throw new InvalidOperationException($"Full night must stay a gentle wash: alpha {wash.A}.");
        // Washing every color toward the same blue keeps their order; check
        // that neighbors players must tell apart keep at least half their contrast.
        Color Washed(Color ground) => ground.Lerp(wash with { A = 1 }, wash.A);
        foreach (var (first, second) in new (Color, Color)[]
        {
            (TerrainTextures.BaseColor(TerrainStyle.Grass), TerrainTextures.BaseColor(TerrainStyle.Ocean)),
            (TerrainTextures.BaseColor(TerrainStyle.Sand), TerrainTextures.BaseColor(TerrainStyle.Ocean)),
            (TerrainTextures.BaseColor(TerrainStyle.Grass), TerrainTextures.BaseColor(TerrainStyle.Sand)),
            (TerrainTextures.BaseColor(TerrainStyle.Grass), TerrainTextures.BaseColor(TerrainStyle.ForestGrass)),
            (TerrainTextures.BaseColor(TerrainStyle.Mountain), TerrainTextures.BaseColor(TerrainStyle.Snow)),
            (RoadSprites.Dirt, TerrainTextures.BaseColor(TerrainStyle.Grass)),
        })
        {
            var day = UiTheme.Contrast(first, second);
            var night = UiTheme.Contrast(Washed(first), Washed(second));
            if (night - 1 < (day - 1) / 2)
                throw new InvalidOperationException($"Night must keep the ground readable: contrast {day:0.00} by day, {night:0.00} at night.");
        }

        // The wash covers exactly the visible map when zoomed out to the
        // flat overview and when zoomed in to full ground detail.
        foreach (var detailed in new[] { false, true })
        {
            cameraZoom = detailed ? maximumCameraZoom : minimumCameraZoom;
            RenderMap(map with { DarknessBasisPoints = 10_000 });
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var stride = terrainLayer.Stride;
            var world = terrainLayer.World!;
            var visible = new Rect2(terrainLayer.VisibleTiles.Position * stride, terrainLayer.VisibleTiles.Size * stride)
                .Intersection(terrainLayer.WrapsEastWest
                    ? new Rect2(-1e7f, 0, 2e7f, world.Height * stride)
                    : new Rect2(0, 0, world.Width * stride, world.Height * stride));
            if (terrainLayer.DrawsGroundTextures != detailed || !visible.HasArea() ||
                !nightLayer.DrawnArea.Grow(0.5f).Encloses(visible) || !visible.Grow(0.5f).Encloses(nightLayer.DrawnArea))
                throw new InvalidOperationException($"Night must cover the visible map at {(detailed ? "detail" : "overview")} zoom: drew {nightLayer.DrawnArea}, visible {visible}.");
        }

        // Labels, markers and panels keep their own colors in either theme.
        var themeBefore = displayPreferences.Theme;
        var other = UiTheme.Current == UiTheme.Dark ? UiThemeChoice.Light : UiThemeChoice.Dark;
        themeChoice.Select((int)other);
        SetUiTheme((int)other);
        if (nightLayer.CurrentWash != wash || objectLayer.Modulate != Colors.White || entityLayer.Modulate != Colors.White ||
            objectLayer.SelfModulate != Colors.White || entityLayer.SelfModulate != Colors.White)
            throw new InvalidOperationException("Night must look the same in both themes and leave map labels and agents untinted.");
        themeChoice.Select((int)UiTheme.Parse(themeBefore));
        SetUiTheme((int)UiTheme.Parse(themeBefore));

        (cameraZoom, cameraCenterTiles) = (zoomBefore, centerBefore);
        RenderMap(map);
        nightLayer.Settle();
    }
}
