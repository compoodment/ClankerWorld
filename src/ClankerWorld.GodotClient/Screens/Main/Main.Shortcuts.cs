using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// In-world keyboard shortcuts and the F1 controls list. Shortcuts press the
/// same top-bar buttons the mouse does, so they obey the same availability:
/// nothing here works behind a modal menu or while typing in a text field,
/// and none of them commits a world change the buttons would not.
/// </summary>
public partial class Main
{
    private readonly PanelContainer controlsPanel = new();

    private static readonly (string Keys, string Action)[] ControlsList =
    [
        ("Left click", "Select an agent or inspect a tile"),
        ("Scroll wheel", "Zoom toward the pointer"),
        ("Middle-drag", "Move the map"),
        ("W A S D / arrows", "Move the map"),
        ("+ / −", "Zoom in or out"),
        ("Space or P", "Pause or resume"),
        ("N / Shift+N", "Next or previous agent"),
        ("C", "Center on the selected agent"),
        ("H", "Back to the first Town"),
        ("M", "Map overview"),
        ("F", "Filters"),
        ("I", "World Info"),
        ("R", "Agents"),
        ("T", "Towns"),
        ("E", "Event Log"),
        ("F1 or ?", "Show or hide this list"),
        ("Esc", "Close the newest panel, or open the Pause Menu"),
    ];

    private void BuildControlsPanel(Control content)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 5);
        foreach (var (keys, action) in ControlsList)
        {
            var keyLabel = new Label { Text = keys };
            keyLabel.ThemeTypeVariation = "KeyLabel";
            grid.AddChild(keyLabel);
            grid.AddChild(new Label { Text = action });
        }
        AddClosablePanelContents(controlsPanel, "Controls", grid);
        controlsPanel.ZIndex = 85;
        controlsPanel.Resized += PositionControlsPanel;
        controlsPanel.Hide();
        content.AddChild(controlsPanel);
    }

    private void PositionControlsPanel()
    {
        controlsPanel.Size = controlsPanel.GetCombinedMinimumSize();
        controlsPanel.Position = new Vector2(
            Math.Max(14, (mapCanvas.Size.X - controlsPanel.Size.X) / 2),
            Math.Max(14, (mapCanvas.Size.Y - controlsPanel.Size.Y) / 2));
    }

    private void ToggleControlsPanel()
    {
        controlsPanel.Visible = !controlsPanel.Visible;
        if (controlsPanel.Visible) PositionControlsPanel();
    }

    /// <summary>World keys apply only in a world with no modal menu open and no text field focused.</summary>
    private bool WorldKeysAvailable() =>
        isInWorld && !mainMenuOverlay.Visible && !gameMenuPanel.Visible && !topBarShade.Visible &&
        !worldMenuOverlay.Visible && !manualSaveOverlay.Visible &&
        GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit);

    /// <summary>
    /// Space pauses even when a clicked button still holds keyboard focus;
    /// otherwise Godot would re-press that button instead. Enter still
    /// activates a focused button.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space } key &&
            !key.ShiftPressed && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed &&
            WorldKeysAvailable())
        {
            PressTopBarAction(pauseButton);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Handles one-key shortcuts; returns false for keys it does not own.</summary>
    private bool HandleShortcutKey(InputEventKey key)
    {
        if (key.Echo || key.CtrlPressed || key.AltPressed || key.MetaPressed || !WorldKeysAvailable())
            return false;
        if (key.Keycode == Key.F1 || key.Unicode == '?')
        {
            ToggleControlsPanel();
            return true;
        }
        switch (key.Keycode)
        {
            case Key.P:
                PressTopBarAction(pauseButton);
                return true;
            case Key.M:
                PressTopBarAction(mapButton);
                return true;
            case Key.F:
                PressTopBarAction(filtersButton);
                return true;
            case Key.I:
                PressTopBarAction(worldInfoButton);
                return true;
            case Key.R:
                PressTopBarAction(inhabitantsButton);
                return true;
            case Key.T:
                ToggleTowns();
                return true;
            case Key.E:
                PressTopBarAction(eventsButton);
                return true;
            case Key.N:
                CycleSelectedAgent(key.ShiftPressed ? -1 : 1);
                return true;
            case Key.C:
                if (selectedInhabitantId is { } selected) CenterOnInhabitant(selected);
                return true;
            case Key.H:
                CenterOnHome();
                return true;
            case Key.Equal or Key.Plus or Key.KpAdd:
                ZoomAt(mapCanvas.Size / 2, zoomIn: true);
                return true;
            case Key.Minus or Key.KpSubtract:
                ZoomAt(mapCanvas.Size / 2, zoomIn: false);
                return true;
        }
        return false;
    }

    /// <summary>A shortcut does exactly what clicking its visible, enabled top-bar button does.</summary>
    private static void PressTopBarAction(Button button)
    {
        if (button.IsVisibleInTree() && !button.Disabled)
            button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>Selects the next living agent in Agents-list order and brings them into view.</summary>
    private void CycleSelectedAgent(int step)
    {
        if (renderedMapSnapshot is not { } snapshot) return;
        var living = snapshot.Inhabitants
            .Where(person => !person.IsDraft && IsLiving(person))
            .OrderBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(person => person.Id)
            .ToArray();
        if (living.Length == 0) return;
        var current = Array.IndexOf(living, selectedInhabitantId);
        var next = current < 0
            ? (step > 0 ? 0 : living.Length - 1)
            : ((current + step) % living.Length + living.Length) % living.Length;
        if (living[next] != selectedInhabitantId) SelectInhabitant(living[next]);
        CenterOnInhabitant(living[next]);
    }

    /// <summary>Returns the camera to where the world opens: its first Town, else its people or land.</summary>
    private void CenterOnHome()
    {
        if (renderedMapSnapshot is not { } snapshot || terrainMap is null || !HasMap(snapshot)) return;
        CenterCameraAt(InitialCameraCenter(snapshot, terrainMap));
    }

    /// <summary>Zooms one step while keeping the world point under <paramref name="canvasPoint"/> fixed.</summary>
    private void ZoomAt(Vector2 canvasPoint, bool zoomIn)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot)) return;
        var nextZoom = Math.Clamp(cameraZoom * (zoomIn ? 1.25f : 0.8f), minimumCameraZoom, maximumCameraZoom);
        if (Math.Abs(nextZoom - cameraZoom) <= 0.001f) return;
        var anchor = (canvasPoint - mapStage.Position) / (currentTileSize + TileGap);
        cameraZoom = nextZoom;
        RenderMap(snapshot);
        CenterCameraAt(anchor - (canvasPoint - mapCanvas.Size / 2) / (currentTileSize + TileGap));
    }
}
