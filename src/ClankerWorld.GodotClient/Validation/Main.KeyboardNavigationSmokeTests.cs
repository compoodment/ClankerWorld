using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task KeyboardKeyAsync(Key key, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, ShiftPressed = shift });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task KeyboardActivateAsync(Control panel, Control target)
    {
        var limit = KeyboardControls(panel).Length + 2;
        for (var step = 0; GetViewport().GuiGetFocusOwner() != target && step < limit; step++)
            await KeyboardKeyAsync(Key.Tab);
        if (GetViewport().GuiGetFocusOwner() != target)
            throw new InvalidOperationException($"Keyboard could not reach {target.Name} ({(target as Button)?.Text}) within {panel.Name}: visible={target.IsVisibleInTree()}, mode={target.FocusMode}, disabled={(target as BaseButton)?.Disabled}, scope={CurrentKeyboardPanel().GetPath()}, focus={GetViewport().GuiGetFocusOwner()?.GetPath()}, controls={string.Join(", ", KeyboardControls(panel).Select(control => (control as Button)?.Text ?? control.GetClass()))}.");
        if (target is BaseButton && target.GetThemeStylebox("focus") is StyleBoxEmpty)
            throw new InvalidOperationException($"Focused action {target.Name} must have a visible focus style.");
        await KeyboardKeyAsync(Key.Enter);
    }

    private async Task VerifyTitleKeyboardAsync()
    {
        ShowMainMenu();
        GetViewport().GuiReleaseFocus();
        await KeyboardActivateAsync(mainMenuOverlay, mainMenuSettingsButton);
        if (!gameMenuPanel.Visible || !settingsPanel.Visible)
            throw new InvalidOperationException("Tab/Enter must open Main Menu Settings.");
        await KeyboardActivateAsync(gameMenuPanel, gameSettingsCategoryButton);
        var expected = GetViewport().GuiGetFocusOwner();
        await KeyboardKeyAsync(Key.Tab);
        var next = GetViewport().GuiGetFocusOwner();
        if (next is null || !gameMenuPanel.IsAncestorOf(next))
            throw new InvalidOperationException("Tab must stay in the open Settings screen.");
        await KeyboardKeyAsync(Key.Tab, shift: true);
        if (GetViewport().GuiGetFocusOwner() != expected)
            throw new InvalidOperationException("Shift+Tab must move back to the previous control.");
        await KeyboardActivateAsync(gameMenuPanel, menuCloseButton);
        if (gameMenuPanel.Visible || !mainMenuOverlay.Visible)
            throw new InvalidOperationException("Keyboard Back must return to Main Menu.");
        await KeyboardKeyAsync(Key.Tab);
        if (GetViewport().GuiGetFocusOwner() is not { } focus || !mainMenuOverlay.IsAncestorOf(focus))
            throw new InvalidOperationException("Returned Main Menu must keep its keyboard focus inside the menu.");
        GetViewport().GuiReleaseFocus();
        keyboardNavigation = false;
    }

    private async Task VerifyWorldKeyboardAsync()
    {
        var original = renderedMapSnapshot ?? throw new InvalidOperationException("Keyboard screen walk needs a rendered world.");
        var previousReconnect = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousProfile = agentProfileRequested;
        var previousCursor = keyboardMapTile;
        var previousKeyboard = keyboardNavigation;
        var previousInWorld = isInWorld;
        var people = new[]
        {
            PanelSmokeAgent("keyboard-first", "First", new(1, 1)),
            PanelSmokeAgent("keyboard-second", "Second", new(2, 1)),
        };
        var snapshot = original with { Inhabitants = people, LatestEventId = 0 };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        try
        {
            mainMenuOverlay.Hide();
            gameMenuPanel.Hide();
            isInWorld = true;
            foreach (var (panel, _) in keyboardPanels.ToArray()) panel.Hide();
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(snapshot, new(0, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Keyboard observation refused: " + failure);
            selectedInhabitantId = null;
            agentProfileRequested = false;
            Render(snapshot, []);
            GetViewport().GuiReleaseFocus();
            await KeyboardKeyAsync(Key.R);
            if (!rosterPanel.Visible || !rosterCards.HasFocus())
                throw new InvalidOperationException("R must enter the Agents list with visible keyboard focus.");
            await KeyboardKeyAsync(Key.Down);
            await KeyboardKeyAsync(Key.Down);
            if (!rosterPanel.Visible || !rosterCards.HasFocus() || selectedInhabitantId != people[1].Id)
                throw new InvalidOperationException("Arrow browsing must keep the Agents list focused.");
            await KeyboardKeyAsync(Key.Enter);
            if (!agentProfilePanel.Visible || selectedInhabitantId != people[1].Id)
                throw new InvalidOperationException("Enter must open the selected Profile.");
            await KeyboardActivateAsync(agentProfilePanel, readThoughtsButton);
            if (!thoughtsPanel.Visible) throw new InvalidOperationException("Keyboard must reach the thoughts reader.");
            await KeyboardKeyAsync(Key.Escape);
            await KeyboardActivateAsync(agentProfilePanel, memoriesButton);
            if (!memoriesPanel.Visible) throw new InvalidOperationException("Keyboard must reach Memories.");
            await KeyboardKeyAsync(Key.Escape);
            await KeyboardActivateAsync(agentProfilePanel, familyTreeButton);
            if (!familyTreePanel.Visible) throw new InvalidOperationException("Keyboard must reach Family.");
            var relative = familyTreeView.GetChildren().OfType<Button>().First();
            await KeyboardActivateAsync(familyTreePanel, relative);
            if (familyTreePanel.Visible) throw new InvalidOperationException("Keyboard must activate a family node.");
            await KeyboardKeyAsync(Key.Escape);
            await KeyboardKeyAsync(Key.Escape);
            foreach (var (key, panel) in new[] { (Key.I, worldInfoPanel), (Key.F, filtersPanel), (Key.E, eventsPanel), (Key.M, worldOverviewPanel), (Key.F1, controlsPanel), (Key.F12, developerPanel) })
            {
                GetViewport().GuiReleaseFocus();
                await KeyboardKeyAsync(key);
                if (!panel.Visible) throw new InvalidOperationException($"Keyboard must open {panel.Name}.");
                await KeyboardKeyAsync(Key.Tab);
                var focus = GetViewport().GuiGetFocusOwner();
                if (focus is null || !panel.IsAncestorOf(focus))
                    throw new InvalidOperationException($"Keyboard focus must stay inside {panel.Name}.");
                if (key == Key.M)
                {
                    await KeyboardActivateAsync(panel, worldOverview);
                    var camera = cameraCenterTiles;
                    await KeyboardKeyAsync(camera.Y < terrainMap!.Height - 1 ? Key.Down : Key.Up);
                    if (cameraCenterTiles == camera)
                        throw new InvalidOperationException("Focused World Map arrows must move the camera.");
                }
                if (key == Key.F12)
                {
                    var numericField = developerEditAmount.GetLineEdit();
                    for (var step = 0; GetViewport().GuiGetFocusOwner() != numericField && step <= KeyboardControls(panel).Length; step++)
                        await KeyboardKeyAsync(Key.Tab);
                    if (GetViewport().GuiGetFocusOwner() != numericField)
                        throw new InvalidOperationException("Tab must reach a numeric field's internal editor.");
                }
                await KeyboardKeyAsync(Key.Escape);
                if (key == Key.F12)
                {
                    if (GetViewport().GuiGetFocusOwner() is LineEdit)
                        throw new InvalidOperationException("First Escape must leave the numeric editor.");
                    await KeyboardKeyAsync(Key.Escape);
                }
                if (panel.Visible) throw new InvalidOperationException($"Escape must close {panel.Name}.");
            }
            GetViewport().GuiReleaseFocus();
            await KeyboardKeyAsync(Key.K);
            if (!mapCanvas.HasFocus() || keyboardMapTile is null)
                throw new InvalidOperationException("K must expose a focused map tile.");
            var before = keyboardMapTile.Value;
            var direction = before.X < terrainMap!.Width - 1 ? Key.Right : Key.Left;
            await KeyboardKeyAsync(direction);
            if (keyboardMapTile == before || !mapCanvas.HasFocus())
                throw new InvalidOperationException("Arrows must move the focused tile without leaving the map.");
            var chosen = keyboardMapTile;
            // A transient small canvas during resize cannot fit the inset.
            // Camera refresh must return even when recentering cannot put the
            // cursor inside it; ordinary observation must retain that cursor.
            var canvasSize = mapCanvas.Size;
            try
            {
                mapCanvas.Size = new Vector2(8, 8);
                RefreshKeyboardMapSelection();
                if (!mapCanvas.HasFocus() || keyboardMapTile != chosen)
                    throw new InvalidOperationException("Resize recentering must keep the focused tile and return.");
            }
            finally
            {
                mapCanvas.Size = canvasSize;
                UpdateMapGeometry(snapshot);
            }
            RefreshTileHoverAtMouse();
            if (keyboardMapTile != chosen) throw new InvalidOperationException("Observation hover must preserve the keyboard tile.");
            if (!new Rect2(Vector2.Zero, mapCanvas.Size).HasPoint(KeyboardMapCanvasPoint(chosen!.Value)))
                throw new InvalidOperationException("The camera must show the focused tile after resizing.");
            // Select an empty ground tile through the ordinary map action.
            var ground = snapshot.Tiles.First(tile =>
                !snapshot.Inhabitants.Any(person => person.Position.X == tile.X && person.Position.Y == tile.Y) &&
                BuildingAt(snapshot, new(tile.X, tile.Y), KeyboardMapCanvasPoint(new(tile.X, tile.Y))) is null);
            keyboardMapTile = new Vector2I(ground.X, ground.Y);
            RefreshKeyboardMapSelection();
            await KeyboardKeyAsync(Key.Enter);
            if (!selectedTilePanel.Visible || selectedTile != new Vector2I(ground.X, ground.Y))
                throw new InvalidOperationException("Enter must inspect the selected ground tile.");
            await KeyboardKeyAsync(Key.Escape);
            GD.Print("Keyboard screen walk passed: Settings/Back, Agents/Profile, Thoughts, Memories, Family, World Info, Filters, Event Log, World Map camera, Controls, Developer numeric field and map tile inspection.");
        }
        finally
        {
            foreach (var (panel, _) in keyboardPanels.ToArray()) panel.Hide();
            selectedInhabitantId = previousSelection;
            agentProfileRequested = previousProfile;
            keyboardMapTile = previousCursor;
            keyboardNavigation = previousKeyboard;
            isInWorld = previousInWorld;
            observationSession.ResetAfterLoad();
            if (previousReconnect is not null)
                observationSession.TryAccept(previousReconnect, previousReconnect.Baseline.Events.AfterEventId, out _);
            ClearTileSelection();
            Render(original, []);
            GetViewport().GuiReleaseFocus();
        }
    }
}
