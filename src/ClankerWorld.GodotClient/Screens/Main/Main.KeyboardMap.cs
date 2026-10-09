using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private Vector2I? keyboardMapTile;
    private bool refreshingKeyboardMapSelection;

    private void BeginKeyboardMapSelection()
    {
        if (!keyboardNavigation)
        {
            mapCanvas.ReleaseFocus();
            return;
        }
        if (renderedMapSnapshot is not { } snapshot || terrainMap is null || !HasMap(snapshot)) return;
        var tile = keyboardMapTile ?? selectedTile ?? new Vector2I((int)cameraCenterTiles.X, (int)cameraCenterTiles.Y);
        keyboardMapTile = BoundKeyboardMapTile(snapshot, tile);
        RefreshKeyboardMapSelection();
    }

    private Vector2I BoundKeyboardMapTile(OwnerWorldSnapshot snapshot, Vector2I tile) => new(
        snapshot.WrapsEastWest ? ((tile.X % terrainMap!.Width) + terrainMap.Width) % terrainMap.Width
            : Math.Clamp(tile.X, 0, terrainMap!.Width - 1),
        Math.Clamp(tile.Y, 0, terrainMap!.Height - 1));

    private Vector2 KeyboardMapCanvasPoint(Vector2I tile)
    {
        var stride = currentTileSize + TileGap;
        var x = WrappedMarkerX((tile.X + 0.5f) * stride, terrainMap!.Width, stride, renderedMapSnapshot!.WrapsEastWest);
        return mapStage.Position + new Vector2(x, (tile.Y + 0.5f) * stride);
    }

    private void RefreshKeyboardMapSelection()
    {
        if (refreshingKeyboardMapSelection || !mapCanvas.HasFocus() || keyboardMapTile is not { } tile ||
            renderedMapSnapshot is not { } snapshot || terrainMap is null)
            return;
        // Recentring refreshes geometry, which asks for hover again. During
        // resize or at an edge, the inset can still exclude the chosen tile.
        refreshingKeyboardMapSelection = true;
        try
        {
            keyboardMapTile = tile = BoundKeyboardMapTile(snapshot, tile);
            var point = KeyboardMapCanvasPoint(tile);
            if (!new Rect2(Vector2.Zero, mapCanvas.Size).Grow(-8).HasPoint(point))
            {
                CenterCameraAt(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
                point = KeyboardMapCanvasPoint(tile);
            }
            UpdateTileHover(point);
            terrainLayer.SetHoveredTile(tile);
        }
        finally { refreshingKeyboardMapSelection = false; }
    }

    private bool HandleKeyboardMapInput(InputEvent input, OwnerWorldSnapshot snapshot)
    {
        if (!mapCanvas.HasFocus() || input is not InputEventKey { Pressed: true } key ||
            key.CtrlPressed || key.AltPressed || key.MetaPressed || terrainMap is null) return false;
        if (keyboardMapTile is null) BeginKeyboardMapSelection();
        if (keyboardMapTile is not { } tile) return false;
        var step = key.ShiftPressed ? 10 : 1;
        var direction = key.Keycode switch
        {
            Key.Left => new Vector2I(-step, 0),
            Key.Right => new Vector2I(step, 0),
            Key.Up => new Vector2I(0, -step),
            Key.Down => new Vector2I(0, step),
            _ => Vector2I.Zero,
        };
        if (direction != Vector2I.Zero)
        {
            keyboardMapTile = BoundKeyboardMapTile(snapshot, tile + direction);
            RefreshKeyboardMapSelection();
            return true;
        }
        if (key.Keycode is Key.Enter or Key.KpEnter && !key.Echo)
        {
            ActivateMapTile(snapshot, tile, KeyboardMapCanvasPoint(tile), selectAgent: true);
            return true;
        }
        return false;
    }

    private void ActivateMapTile(OwnerWorldSnapshot snapshot, Vector2I tile, Vector2 canvasPoint, bool selectAgent = false)
    {
        if (!MapContains(snapshot, tile.X, tile.Y)) return;
        if (movingFounderId is not null && snapshot.FounderSetup is { Started: false })
            _ = MoveFounderAtAsync(tile);
        else if (choosingFirstTownSite && snapshot.FounderSetup is { CanChooseTownSite: true })
            _ = AcceptFirstTownSiteAtAsync(tile);
        else if (founderSetupPanel.Visible && snapshot.FounderSetup is { Started: false })
            _ = PlaceFounderAtAsync(tile);
        else if (founderSetupPanel.Visible && placingAddedAgent && snapshot.FounderSetup is { Started: true })
            _ = PlaceAgentAtAsync(tile);
        else if (selectAgent && snapshot.Inhabitants.FirstOrDefault(person => !person.IsDraft && IsLiving(person) &&
            person.Position.X == tile.X && person.Position.Y == tile.Y) is { } agent)
            SelectInhabitant(agent.Id);
        else if (BuildingAt(snapshot, tile, canvasPoint) is { } building)
            SelectBuilding(building.InstanceId);
        else
        {
            ClearBuildingSelection();
            selectedTile = tile;
            terrainLayer.SetSelectedTile(tile);
            selectedTilePanel.Show();
            RenderTileInspection(snapshot);
        }
    }
}
