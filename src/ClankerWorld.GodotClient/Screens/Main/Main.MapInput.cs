using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void UpdateMapGeometry(OwnerWorldSnapshot snapshot)
    {
        if (!HasMap(snapshot) || mapCanvas.Size.X <= 0 || mapCanvas.Size.Y <= 0)
        {
            return;
        }

        var (mapWidth, mapHeight) = MapDimensions(snapshot);
        var availableWidth = Math.Max(1, mapCanvas.Size.X - 36 - ((mapWidth - 1) * TileGap));
        var availableHeight = Math.Max(1, mapCanvas.Size.Y - 36 - ((mapHeight - 1) * TileGap));
        var fittedTileSize = (int)Math.Floor(Math.Min(availableWidth / mapWidth, availableHeight / mapHeight));
        var baseTileSize = Math.Clamp(fittedTileSize, 12, 220);
        if (mapWidth >= 256 && mapHeight >= 128)
        {
            // Small and Medium maps stop before their north/south edges become
            // black letterbox space. Larger maps share the same 8 px overview
            // floor, independent of their total geographic area.
            var minimumTileSize = mapWidth <= 512 && mapHeight <= 256
                ? Math.Max(8, (int)MathF.Ceiling(MathF.Max(
                    mapCanvas.Size.X / (mapWidth * 0.7f),
                    mapCanvas.Size.Y / (mapHeight * 0.7f))))
                : 8;
            // Frame a similar number of world rows at maximum zoom-in on a
            // 720p or 1440p display instead of fixing the maximum to 48 px.
            var maximumTileSize = Math.Max(minimumTileSize,
                Math.Clamp((int)MathF.Ceiling(mapCanvas.Size.Y / 14f), 48, 256));
            minimumCameraZoom = Math.Max(0.65f, minimumTileSize / (float)baseTileSize);
            maximumCameraZoom = Math.Max(minimumCameraZoom, maximumTileSize / (float)baseTileSize);
        }
        else
        {
            minimumCameraZoom = 0.65f;
            maximumCameraZoom = 4f;
        }
        cameraZoom = Math.Clamp(cameraZoom, minimumCameraZoom, maximumCameraZoom);
        currentTileSize = Math.Clamp((int)MathF.Round(baseTileSize * cameraZoom), 8, 880);

        var stageSize = new Vector2(
            (mapWidth * currentTileSize) + ((mapWidth - 1) * TileGap),
            (mapHeight * currentTileSize) + ((mapHeight - 1) * TileGap));
        mapStage.Size = stageSize;
        terrainLayer.Size = stageSize;
        var stride = currentTileSize + TileGap;
        if (snapshot.WrapsEastWest)
            cameraCenterTiles.X = PositiveMod(cameraCenterTiles.X, mapWidth);
        mapStage.Position = new Vector2(
            snapshot.WrapsEastWest
                ? mapCanvas.Size.X / 2 - cameraCenterTiles.X * stride
                : CameraAxis(cameraCenterTiles.X, stageSize.X, mapCanvas.Size.X, stride),
            CameraAxis(cameraCenterTiles.Y, stageSize.Y, mapCanvas.Size.Y, stride));
        cameraCenterTiles = new Vector2(
            snapshot.WrapsEastWest ? cameraCenterTiles.X : (mapCanvas.Size.X / 2 - mapStage.Position.X) / stride,
            (mapCanvas.Size.Y / 2 - mapStage.Position.Y) / stride);
        RepositionWrappedMapMarkers(mapWidth, stride, snapshot.WrapsEastWest);
        RefreshOverviewViewport(mapWidth, mapHeight, stride, snapshot.WrapsEastWest);
        RefreshTileHoverAtMouse();
        RenderWorldHud(snapshot);
        RenderWorldInfo(snapshot);
    }

    private static float CameraAxis(float centerTile, float stagePixels, float viewportPixels, float stride) =>
        stagePixels <= viewportPixels
            ? (viewportPixels - stagePixels) / 2
            : Math.Clamp((viewportPixels / 2) - (centerTile * stride), viewportPixels - stagePixels, 0);

    private static float PositiveMod(float value, int modulus) => (value % modulus + modulus) % modulus;

    private float WrappedMarkerX(float canonicalX, int mapWidth, float stride, bool wrapsEastWest) =>
        !wrapsEastWest ? canonicalX :
        canonicalX + MathF.Round((cameraCenterTiles.X - canonicalX / stride) / mapWidth) * mapWidth * stride;

    private void RepositionWrappedMapMarkers(int mapWidth, float stride, bool wrapsEastWest)
    {
        foreach (var (id, visual) in mapObjectVisuals)
            if (mapObjectCanonicalXs.TryGetValue(id, out var x))
                visual.Position = new Vector2(WrappedMarkerX(x, mapWidth, stride, wrapsEastWest), visual.Position.Y);
        foreach (var (id, visual) in inhabitantVisuals)
            if (inhabitantCanonicalXs.TryGetValue(id, out var x))
                visual.Position = new Vector2(WrappedMarkerX(x, mapWidth, stride, wrapsEastWest), visual.Position.Y);
    }

    private void RefreshOverviewViewport(int mapWidth, int mapHeight, float stride, bool wrapsEastWest)
    {
        var left = wrapsEastWest ? -mapStage.Position.X / stride :
            Math.Clamp(-mapStage.Position.X / stride, 0, mapWidth);
        var top = Math.Clamp(-mapStage.Position.Y / stride, 0, mapHeight);
        var right = wrapsEastWest ? (mapCanvas.Size.X - mapStage.Position.X) / stride :
            Math.Clamp((mapCanvas.Size.X - mapStage.Position.X) / stride, 0, mapWidth);
        var bottom = Math.Clamp((mapCanvas.Size.Y - mapStage.Position.Y) / stride, 0, mapHeight);
        var visible = new Rect2(left, top, right - left, bottom - top);
        worldOverview.SetVisibleTiles(visible);
        terrainLayer.SetCamera(visible, currentTileSize, TileGap, wrapsEastWest);
    }

    private void CenterCameraAt(Vector2 tileCenter)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot))
        {
            return;
        }

        cameraCenterTiles = tileCenter;
        UpdateMapGeometry(snapshot);
        PositionSelectedInhabitantCard(snapshot);
    }

    private void PanCamera(Vector2 deltaTiles)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot))
        {
            return;
        }

        CenterCameraAt(cameraCenterTiles + deltaTiles);
    }

    private void HandleMapInput(InputEvent @event)
    {
        if (gameMenuPanel.Visible ||
            renderedMapSnapshot is not { } snapshot || !HasMap(snapshot))
        {
            return;
        }

        if (@event is InputEventMouseButton mouse)
        {
            // Clicking the world hands the keyboard back to map controls.
            if (mouse.Pressed) GetViewport().GuiReleaseFocus();
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && movingFounderId is not null &&
                snapshot.FounderSetup is { Started: false })
            {
                _ = MoveFounderAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && choosingFirstTownSite &&
                snapshot.FounderSetup is { CanChooseTownSite: true })
            {
                _ = AcceptFirstTownSiteAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && founderSetupPanel.Visible &&
                snapshot.FounderSetup is { Started: false })
            {
                _ = PlaceFounderAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && founderSetupPanel.Visible &&
                placingAddedAgent && snapshot.FounderSetup is { Started: true })
            {
                _ = PlaceAgentAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.Middle)
            {
                draggingMap = mouse.Pressed;
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                // Zooming in moves toward what the player points at.
                ZoomAt(mouse.Position, mouse.ButtonIndex == MouseButton.WheelUp);
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                var tile = TileAtCanvas(mouse.Position, snapshot);
                if (MapContains(snapshot, tile.X, tile.Y))
                {
                    selectedTile = tile;
                    terrainLayer.SetSelectedTile(tile);
                    // Show first: hidden containers report no content size.
                    selectedTilePanel.Show();
                    RenderTileInspection(snapshot);
                    mapCanvas.AcceptEvent();
                }
            }
        }
        else if (@event is InputEventMouseMotion hoverMotion)
        {
            if (draggingMap)
            {
                PanCamera(-hoverMotion.Relative / (currentTileSize + TileGap));
                mapCanvas.AcceptEvent();
            }
            UpdateTileHover(hoverMotion.Position);
        }
    }

    private void RefreshTileHoverAtMouse() => UpdateTileHover(mapCanvas.GetLocalMousePosition());

    private Vector2I TileAtCanvas(Vector2 canvasPosition, OwnerWorldSnapshot snapshot)
    {
        var tile = (canvasPosition - mapStage.Position) / (currentTileSize + TileGap);
        var x = Mathf.FloorToInt(tile.X);
        if (snapshot.WrapsEastWest)
            x = ((x % terrainMap!.Width) + terrainMap.Width) % terrainMap.Width;
        return new Vector2I(x, Mathf.FloorToInt(tile.Y));
    }

    private void ClearTileSelection()
    {
        selectedTile = null;
        terrainLayer.SetSelectedTile(null);
        selectedTilePanel.Hide();
    }

    private void RenderTileInspection(OwnerWorldSnapshot snapshot)
    {
        if (selectedTile is not { } tile || terrainMap is null) return;
        if (!MapContains(snapshot, tile.X, tile.Y))
        {
            ClearTileSelection();
            return;
        }

        var regionSize = Math.Max(1, snapshot.WeatherRegionSize);
        var region = snapshot.WeatherRegions.FirstOrDefault(item =>
            item.X == tile.X / regionSize && item.Y == tile.Y / regionSize);
        var objects = snapshot.Objects.Where(item => item.Position.X == tile.X && item.Position.Y == tile.Y)
            .Select(item => Pretty(item.Kind))
            .Concat(snapshot.Resources.Where(item => item.Position.X == tile.X && item.Position.Y == tile.Y)
                .Select(item => item.TreeKind is { } tree
                    ? $"{Pretty(tree)} tree · {Pretty(item.TreeStage ?? item.State)}"
                    : $"{WorldTerrainMap.NaturalObjectName(item.NaturalObjectKind) ?? Pretty(item.Kind) + " site"}" +
                        (item.Quantity is { } quantity ? $" · {quantity} available" : string.Empty)))
            .Concat(snapshot.PlacedBuildings.Where(item =>
                    tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
                    tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height)
                .Select(item => item.DisplayName ?? Pretty(item.DefinitionId)))
            .ToArray();
        var climate = WorldTerrainMap.ClimateName(terrainMap.ClimateAt(tile.X, tile.Y));
        var elevation = terrainMap.ElevationAt(tile.X, tile.Y);
        var hydrology = WorldTerrainMap.HydrologyName(terrainMap.HydrologyAt(tile.X, tile.Y));
        var surface = WorldTerrainMap.SurfaceName(terrainMap.SurfaceAt(tile.X, tile.Y));
        var vegetation = WorldTerrainMap.VegetationName(terrainMap.VegetationAt(tile.X, tile.Y));
        var town = snapshot.Towns.FirstOrDefault(item => item.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y));
        var propertyOwnerId = snapshot.PlacedBuildings.FirstOrDefault(item => item.HouseholdId is not null &&
            tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
            tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height)?.HouseholdId;
        var lines = new List<string>
        {
            $"Tile {tile.X}, {tile.Y}",
            $"Terrain: {WorldTerrainMap.NameFor(terrainMap.At(tile.X, tile.Y))}",
        };
        if (climate is not null) lines.Add($"Climate: {climate}");
        if (surface is not null) lines.Add($"Surface: {surface}");
        if (hydrology is not null and not "Land") lines.Add($"Water: {hydrology}");
        if (vegetation is not null and not "None") lines.Add($"Vegetation: {vegetation}");
        if ((region?.Weather ?? snapshot.Authoring?.Weather) is { } weather)
            lines.Add($"Weather: {Pretty(weather)}");
        if (region?.SoilMoisture is { } moisture)
            lines.Add($"Soil moisture: {moisture}%");
        if (elevation is { } level) lines.Add($"Elevation: {level}/255");
        if (town is not null) lines.Add($"Town: {town.Name}");
        if (propertyOwnerId is not null)
            lines.Add($"Household property: {snapshot.Stockpiles.FirstOrDefault(item => item.OwnerId == propertyOwnerId)?.Name ?? propertyOwnerId}");
        if (snapshot.RoadTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) lines.Add("Road");
        if (objects.Length > 0) lines.Add($"Objects: {string.Join(", ", objects)}");
        SetPanelText(selectedTileText, string.Join('\n', lines));
        FitSelectedTileText();
    }

    /// <summary>
    /// Grows the tile card to its wrapped facts, so the last one is not hidden
    /// behind a scrollbar, and re-anchors it inside the bottom of the view.
    /// Before the card has been laid out its text width is unknown; the
    /// label's Resized signal repeats the fit once the width arrives.
    /// </summary>
    private void FitSelectedTileText()
    {
        if (selectedTileText.Size.X >= 64)
        {
            var height = Math.Min(Math.Max(64, mapCanvas.Size.Y - 96),
                Math.Max(64, selectedTileText.GetContentHeight() + 4));
            if (Math.Abs(selectedTileText.CustomMinimumSize.Y - height) >= 1)
                selectedTileText.CustomMinimumSize = new Vector2(0, height);
        }
        selectedTilePanel.Size = selectedTilePanel.GetCombinedMinimumSize();
        PositionSelectedTilePanel();
    }

    private void PositionSelectedTilePanel() =>
        selectedTilePanel.Position = new Vector2(14,
            Math.Max(14, mapCanvas.Size.Y - Math.Max(selectedTilePanel.Size.Y,
                selectedTilePanel.CustomMinimumSize.Y) - 14));

    private void UpdateTileHover(Vector2 canvasPosition)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot) ||
            gameMenuPanel.Visible ||
            canvasPosition.X < 0 || canvasPosition.Y < 0 ||
            canvasPosition.X >= mapCanvas.Size.X || canvasPosition.Y >= mapCanvas.Size.Y)
        {
            terrainLayer.SetHoveredTile(null);
            UpdateHoverReadout(null, null);
            return;
        }

        var stagePosition = canvasPosition - mapStage.Position;
        var tile = TileAtCanvas(canvasPosition, snapshot);
        UpdateHoverReadout(snapshot, tile);
        PreviewAddAgentPlacement(snapshot, tile);
        if (!MapContains(snapshot, tile.X, tile.Y) ||
            inhabitantVisuals.Values.Any(marker => marker.Visible &&
                new Rect2(marker.Position, marker.Size).HasPoint(stagePosition)))
        {
            terrainLayer.SetHoveredTile(null);
            return;
        }

        terrainLayer.SetHoveredTile(tile);
    }

}
