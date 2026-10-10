using ClankerWorld.GodotClient.Pairing;
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

    private async Task VerifyKeyboardScrollAsync(ScrollContainer scroll, Control? panel = null, Window? window = null)
    {
        for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var bar = scroll.GetVScrollBar();
        if (!bar.IsVisibleInTree() || bar.MaxValue <= bar.Page)
            throw new InvalidOperationException("Keyboard scrolling requires an overflowing actual reader.");
        Control? FocusOwner() => window?.GuiGetFocusOwner() ?? GetViewport().GuiGetFocusOwner();
        var limit = panel is null ? 32 : KeyboardControls(panel).Length + 2;
        for (var step = 0; FocusOwner() != bar && step < limit; step++) await KeyboardKeyAsync(Key.Tab);
        if (FocusOwner() != bar)
            throw new InvalidOperationException("Tab must reach a plain reader's scrollbar, including native dialogs.");
        if (bar.GetThemeStylebox("scroll_focus") is not StyleBoxFlat { BorderWidthLeft: > 0 })
            throw new InvalidOperationException("A plain reader's keyboard focus must have a visible outline.");
        await KeyboardKeyAsync(Key.Home);
        var afterHomeFocus = FocusOwner()?.GetPath().ToString();
        var before = scroll.ScrollVertical;
        await KeyboardKeyAsync(Key.Down);
        var afterDownFocus = FocusOwner()?.GetPath().ToString();
        if (FocusOwner() != bar)
            throw new InvalidOperationException($"Scrolling Down must keep focus on the scrollbar: bar={bar.GetPath()}, focus={afterDownFocus}.");
        if (scroll.ScrollVertical <= before)
            throw new InvalidOperationException($"Down must scroll the plain reader even when its continuous pointer step is zero: before={before}, after={scroll.ScrollVertical}, focus={FocusOwner()?.GetPath()}, max={bar.MaxValue}, page={bar.Page}, window={window?.Name}.");
        var afterDown = scroll.ScrollVertical;
        await KeyboardKeyAsync(Key.Up);
        if (FocusOwner() != bar)
            throw new InvalidOperationException("Scrolling Up must keep focus on the scrollbar.");
        if (scroll.ScrollVertical >= afterDown)
            throw new InvalidOperationException($"Up must scroll the plain reader back: down={afterDown}, up={scroll.ScrollVertical}, focus={FocusOwner()?.GetPath()}, homeFocus={afterHomeFocus}, downFocus={afterDownFocus}, bar={bar.GetPath()}, step={bar.Step}, customStep={bar.CustomStep}, max={bar.MaxValue}, page={bar.Page}, window={window?.Name}.");
        await KeyboardKeyAsync(Key.Tab);
        if (FocusOwner() == bar) throw new InvalidOperationException("Tab must leave the scrollbar after arrow scrolling.");
    }

    private async Task WithKeyboardOwnerAsync(Func<Task> check)
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            await check();
        }
        finally
        {
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshMainMenuAvailability();
            RefreshControlAvailability();
        }
    }

    private Task VerifyTitleKeyboardAsync() => WithKeyboardOwnerAsync(VerifyTitleKeyboardCoreAsync);

    private Task VerifyWorldKeyboardAsync() => WithKeyboardOwnerAsync(VerifyWorldKeyboardCoreAsync);

    private async Task VerifyTitleKeyboardCoreAsync()
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
        if (GetViewport().GuiGetFocusOwner() != mainMenuSettingsButton)
            throw new InvalidOperationException("Closing title Settings must restore focus to its Settings opener.");
        await KeyboardKeyAsync(Key.Tab);
        if (GetViewport().GuiGetFocusOwner() is not { } focus || !mainMenuOverlay.IsAncestorOf(focus))
            throw new InvalidOperationException("Returned Main Menu must keep its keyboard focus inside the menu.");
        GetViewport().GuiReleaseFocus();
        keyboardNavigation = false;
    }

    private async Task VerifyWorldKeyboardCoreAsync()
    {
        var original = renderedMapSnapshot ?? throw new InvalidOperationException("Keyboard screen walk needs a rendered world.");
        var previousReconnect = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousProfile = agentProfileRequested;
        var previousCursor = keyboardMapTile;
        var previousKeyboard = keyboardNavigation;
        var previousInWorld = isInWorld;
        var previousZoom = cameraZoom;
        var people = new[]
        {
            PanelSmokeAgent("keyboard-first", "First", new(1, 1)),
            PanelSmokeAgent("keyboard-second", "Second", new(2, 1)),
        };
        var lanterns = new[]
        {
            new OwnerWorldPlacedBuilding("keyboard-stone", "test/stone_lantern", new(3, 1), 0,
                "Stone street lantern", ["street_lantern", "stone_lantern"], 1, 1, Entrance: new(3, 2)),
            new OwnerWorldPlacedBuilding("keyboard-hanging", "test/hanging_lantern", new(3, 2), 0,
                "Hanging street lantern", ["street_lantern", "hanging_lantern"], 1, 1, Entrance: new(3, 3)),
        };
        var snapshot = original with { Inhabitants = people, PlacedBuildings = lanterns, LatestEventId = 0 };
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
            if (lanterns.Any(lantern => !MapContains(snapshot, lantern.Position.X, lantern.Position.Y)))
                throw new InvalidOperationException("Keyboard lantern fixtures must lie inside the rendered map.");
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
            await KeyboardActivateAsync(agentProfilePanel, renameToggleButton);
            if (!renameRow.Visible || !renameAgentInput.HasFocus())
                throw new InvalidOperationException("Keyboard Rename must open and focus its editor.");
            await KeyboardKeyAsync(Key.Escape);
            await KeyboardActivateAsync(agentProfilePanel, renameToggleButton);
            if (renameRow.Visible) throw new InvalidOperationException("Keyboard must close Rename without submitting it.");
            await KeyboardActivateAsync(agentProfilePanel, readThoughtsButton);
            if (!thoughtsPanel.Visible) throw new InvalidOperationException("Keyboard must reach the thoughts reader.");
            // Fill the actual reader past its viewport, then use real Tab and
            // arrow input. Opening a short reader alone cannot prove scrolling.
            thoughtsReaderText.Text = string.Join("\n", Enumerable.Range(0, 120).Select(index => $"Recorded thought {index}: a long reader must remain usable."));
            FitTextPanel(thoughtsReaderText);
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var readerScroll = thoughtsReaderText.GetVScrollBar();
            for (var step = 0; GetViewport().GuiGetFocusOwner() != readerScroll && step <= KeyboardControls(thoughtsPanel).Length; step++)
                await KeyboardKeyAsync(Key.Tab);
            if (!readerScroll.HasFocus()) throw new InvalidOperationException("Tab must reach an overflowing reader's scrollbar.");
            if (readerScroll.GetThemeStylebox("scroll_focus") is not StyleBoxFlat { BorderWidthLeft: > 0 })
                throw new InvalidOperationException("A reader's keyboard focus must have a visible outline.");
            var scrollBefore = readerScroll.Value;
            await KeyboardKeyAsync(Key.Down);
            if (!readerScroll.HasFocus()) throw new InvalidOperationException("Scrolling a text reader must retain its keyboard focus.");
            if (readerScroll.Value <= scrollBefore)
                throw new InvalidOperationException("Arrow input must scroll the actual overflowing reader.");
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
                if (key == Key.E)
                {
                    var previousEvents = knownEvents.ToArray();
                    var eventZoom = cameraZoom;
                    try
                    {
                        cameraZoom = 4;
                        CenterCameraAt(new(0.5f, 0.5f));
                        var expectedCamera = cameraCenterTiles;
                        CenterCameraAt(new(3.5f, 3.5f));
                        knownEvents.Clear();
                        // Find actions require visible events; routine meals are intentionally filtered out.
                        knownEvents[9001] = new OwnerWorldEvent(9001, 1, "skill_learned", $"{people[0].Id}|building|work", new(0, 0));
                        RenderEventLog();
                        var find = KeyboardControls(eventRows).OfType<Button>().Single();
                        for (var step = 0; !find.HasFocus() && step <= KeyboardControls(panel).Length; step++)
                            await KeyboardKeyAsync(Key.Tab);
                        if (!find.HasFocus()) throw new InvalidOperationException("Tab must reach the event's Find action.");
                        knownEvents[9002] = new OwnerWorldEvent(9002, 2, "skill_learned", $"{people[1].Id}|building|work", new(3, 3));
                        RenderEventLog();
                        var restoredFind = GetViewport().GuiGetFocusOwner();
                        if (restoredFind is not Button || !restoredFind.HasMeta("keyboard_event_action") ||
                            restoredFind.GetMeta("keyboard_event_action").AsString() != "9001")
                            throw new InvalidOperationException("An arriving event must retain its focused Find action before activation.");
                        await KeyboardKeyAsync(Key.Enter);
                        if (eventsPanel.Visible || cameraCenterTiles != expectedCamera)
                            throw new InvalidOperationException($"An arriving event must preserve the focused Find action and its destination: panel={eventsPanel.Visible}, expected={expectedCamera}, actual={cameraCenterTiles}, focus={GetViewport().GuiGetFocusOwner()?.GetPath()}, cursor={keyboardMapTile}.");
                    }
                    finally
                    {
                        knownEvents.Clear();
                        foreach (var pair in previousEvents) knownEvents[pair.Key] = pair.Value;
                        cameraZoom = eventZoom;
                        UpdateMapGeometry(snapshot);
                        eventsPanel.Show();
                        RenderEventLog();
                    }
                    var longHistory = new Label { Text = string.Join('\n', Enumerable.Range(0, 120).Select(index => $"Recorded event {index}")) };
                    eventRows.AddChild(longHistory);
                    try { await VerifyKeyboardScrollAsync(eventScroll, eventsPanel); }
                    finally
                    {
                        eventRows.RemoveChild(longHistory);
                        longHistory.QueueFree();
                        eventScroll.ScrollVertical = 0;
                    }
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
            // A zoomed map puts the old cursor outside both shortcut destinations.
            cameraZoom = 4;
            keyboardMapTile = new Vector2I(terrainMap.Width - 1, terrainMap.Height - 1);
            UpdateMapGeometry(snapshot);
            selectedInhabitantId = people[0].Id;
            await KeyboardKeyAsync(Key.C);
            if (keyboardMapTile != new Vector2I(people[0].Position.X, people[0].Position.Y))
                throw new InvalidOperationException("C must move the focused cursor to the selected agent instead of undoing the camera jump.");
            keyboardMapTile = new Vector2I(terrainMap.Width - 1, terrainMap.Height - 1);
            RefreshKeyboardMapSelection();
            var home = InitialCameraCenter(snapshot, terrainMap);
            await KeyboardKeyAsync(Key.H);
            var homeTile = BoundKeyboardMapTile(snapshot, new Vector2I((int)home.X, (int)home.Y));
            UpdateMapGeometry(snapshot);
            if (keyboardMapTile != homeTile || !mapCanvas.HasFocus() ||
                !new Rect2(Vector2.Zero, mapCanvas.Size).HasPoint(KeyboardMapCanvasPoint(homeTile)))
                throw new InvalidOperationException("H must keep the opening view and focused tile through geometry refresh.");
            cameraZoom = previousZoom;
            UpdateMapGeometry(snapshot);
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
                BuildingAt(snapshot, new(tile.X, tile.Y)) is null);
            keyboardMapTile = new Vector2I(ground.X, ground.Y);
            RefreshKeyboardMapSelection();
            await KeyboardKeyAsync(Key.Enter);
            if (!selectedTilePanel.Visible || selectedTile != new Vector2I(ground.X, ground.Y))
                throw new InvalidOperationException("Enter must inspect the selected ground tile.");
            await KeyboardKeyAsync(Key.Escape);
            foreach (var lantern in lanterns)
            {
                await KeyboardKeyAsync(Key.K);
                keyboardMapTile = new Vector2I(lantern.Position.X - 1, lantern.Position.Y);
                RefreshKeyboardMapSelection();
                await KeyboardKeyAsync(Key.Right);
                await KeyboardKeyAsync(Key.Enter);
                if (!buildingQuickCard.Visible || selectedBuildingId != lantern.InstanceId)
                    throw new InvalidOperationException($"Keyboard Enter must select both street-lantern styles by their tile: expected={lantern.InstanceId}, selected={selectedBuildingId}, cursor={keyboardMapTile}, quickCard={buildingQuickCard.Visible}.");
                await KeyboardActivateAsync(buildingQuickCard, buildingDetailsButton);
                var detailsFocus = GetViewport().GuiGetFocusOwner();
                if (!buildingDetailsPanel.Visible || detailsFocus is null || !buildingDetailsPanel.IsAncestorOf(detailsFocus))
                    throw new InvalidOperationException("Opening Details must retain focus in the new panel after the quick card closes.");
                await KeyboardKeyAsync(Key.Tab);
                if (GetViewport().GuiGetFocusOwner() is not { } detailControl || !buildingDetailsPanel.IsAncestorOf(detailControl))
                    throw new InvalidOperationException("Tab must remain in building Details.");
                await KeyboardKeyAsync(Key.Escape);
                if (!buildingQuickCard.Visible || GetViewport().GuiGetFocusOwner() is not { } quickControl ||
                    !buildingQuickCard.IsAncestorOf(quickControl))
                    throw new InvalidOperationException("Back from Details must focus the restored quick card.");
                await KeyboardKeyAsync(Key.Escape);
            }
            GD.Print("Keyboard screen walk passed: Settings opener/Back, Agents/Profile/Rename, overflowing Thoughts scrolling, Memories, Family, World Info, Filters, Event Log, World Map camera, Controls, Developer numeric field, ground and both street lanterns.");
        }
        finally
        {
            foreach (var (panel, _) in keyboardPanels.ToArray()) panel.Hide();
            selectedInhabitantId = previousSelection;
            agentProfileRequested = previousProfile;
            keyboardMapTile = previousCursor;
            keyboardNavigation = previousKeyboard;
            cameraZoom = previousZoom;
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
