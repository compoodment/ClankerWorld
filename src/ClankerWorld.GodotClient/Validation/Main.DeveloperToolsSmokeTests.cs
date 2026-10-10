using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyAuthoringCoordinatesAsync(OwnerWorldSnapshot source)
    {
        var large = source with
        {
            WorldId = "authoring-coordinate-large",
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            PackedMapLayers = null,
            Tiles = [],
        };
        try
        {
            Render(large, []);
            authoringX.Value = 125;
            authoringY.Value = 65;
            if (authoringX.Value != 125 || authoringY.Value != 65)
                throw new InvalidOperationException($"Paused authoring must accept valid coordinates above 99: ({authoringX.Value}, {authoringY.Value}).");
            var input = authoringX.GetLineEdit();
            input.Text = "255";
            input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text);
            // SpinBox handles text submission through a deferred native callback.
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            authoringY.Value = 127;
            if (authoringX.Value != 255 || authoringY.Value != 127 || authoringX.MaxValue != 255 || authoringY.MaxValue != 127)
                throw new InvalidOperationException("Typed authoring coordinates must reach the actual map's last tile on each axis.");
            authoringX.Value = 256;
            authoringY.Value = 128;
            if (authoringX.Value != 255 || authoringY.Value != 127)
                throw new InvalidOperationException("Authoring coordinates must clamp at the map's actual edges.");
            Render(source, []);
            var (width, height) = MapDimensions(source);
            if (authoringX.MaxValue != width - 1 || authoringY.MaxValue != height - 1 ||
                authoringX.Value != width - 1 || authoringY.Value != height - 1)
                throw new InvalidOperationException("Switching to a smaller world must refresh authoring bounds and clamp old coordinates.");
            Render(large, []);
            if (authoringX.MaxValue != 255 || authoringY.MaxValue != 127)
                throw new InvalidOperationException("Returning to a larger world must restore its authoring bounds.");
            authoringX.Value = -1;
            authoringY.Value = -1;
            if (authoringX.Value != 0 || authoringY.Value != 0)
                throw new InvalidOperationException("Authoring coordinates must stay nonnegative.");
            Render(source with { PackedTerrain = null, PackedMapLayers = null, Tiles = [] }, []);
            if (authoringX.MaxValue != 0 || authoringY.MaxValue != 0)
                throw new InvalidOperationException("Without a map, authoring coordinates must not retain the previous world's bounds.");
        }
        finally { Render(source, []); }
    }

    /// <summary>
    /// F12 opens and closes Developer tools over a running world without the
    /// Pause Menu. The panel holds every older tool, fits the screen at 100%
    /// and 200%, reads out the tile, frame time, tick time and agents, jumps
    /// the camera to a chosen agent and draws the selected agent's route.
    /// </summary>
    private async Task VerifyDeveloperToolsAsync(OwnerWorldSnapshot occupied, OwnerWorldInhabitant founder)
    {
        // Rowan walks from (2, 0) down the east side toward food at (3, 3).
        var walker = founder with
        {
            PlannedRoute = new OwnerWorldPlannedRoute("food", new(3, 3), [new(3, 1), new(3, 2), new(3, 3)], 3),
        };
        var minePosition = new OwnerWorldPosition(0, 3);
        var other = founder with
        {
            Id = "developer-ui-test",
            DisplayName = "Mira",
            Position = minePosition,
            SpatialKnowledge = new OwnerWorldSpatialKnowledge(minePosition, [minePosition], [minePosition]),
        };
        var world = occupied with { Inhabitants = [walker, other], WorldTick = 77, LastTickMilliseconds = 4.2 };
        var window = GetWindow();
        var originalSize = window.Size;
        var originalRenderSize = window.ContentScaleSize;
        var originalZoom = cameraZoom;
        selectedInhabitantId = null;
        selectedInhabitantCard.Hide();
        ClearTileSelection();
        ClearBuildingSelection();
        foreach (var panel in new Control[] { rosterPanel, eventsPanel, worldInfoPanel, filtersPanel, worldOverviewPanel, controlsPanel, agentProfilePanel })
            panel.Hide();
        RenderMap(world);
        try
        {
            var controlLabels = controlsPanel.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text).ToArray();
            if (!ControlGroups.SelectMany(group => group.Rows).Any(row => row.Keys is ["F12"] && row.Action == "Developer tools") ||
                !controlLabels.Contains("F12") || !controlLabels.Contains("Developer tools"))
                throw new InvalidOperationException("The F1 controls list must name F12 for Developer tools.");

            // Behind the Pause Menu, F12 does nothing.
            gameMenuPanel.Show();
            _Input(new InputEventKey { Keycode = Key.F12, Pressed = true });
            gameMenuPanel.Hide();
            if (developerPanel.Visible)
                throw new InvalidOperationException("F12 must not open Developer tools behind the Pause Menu.");

            _Input(new InputEventKey { Keycode = Key.F12, Pressed = true });
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!developerPanel.Visible || gameMenuPanel.Visible || topBarShade.Visible || menuPausedWorld)
                throw new InvalidOperationException("F12 must open Developer tools over the world without opening the Pause Menu or pausing.");
            if (!developerBody.FindChildren("*", nameof(Label), recursive: true, owned: false)
                .OfType<Label>().Any(label => label.Text == BuildInformation.Display && label.IsVisibleInTree()))
                throw new InvalidOperationException("Developer tools must show the assembly version and source commit.");
            var tools = new Control[]
            {
                developerEditKind, developerEditValue, developerEditAmount, developerEditOther, developerEditApply,
                lifePaceChoice, applyLifePaceButton, retryPendingSubmissionButton, forgetPendingSubmissionButton,
                authoringKind, authoringId, submitAuthoringButton, pairingApprovalId, approvePairingButton,
                refreshDevicesButton, pairedDeviceList, revokeDeviceButton,
            };
            if (tools.Any(tool => !developerPanel.IsAncestorOf(tool)) || gameMenuPanel.IsAncestorOf(developerBody))
                throw new InvalidOperationException("Every older developer tool must live in the F12 panel, not the Pause Menu.");

            foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720) })
            {
                window.Size = size;
                window.ContentScaleSize = size;
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ApplyResponsiveLayout();
                var panel = developerPanel.GetGlobalRect();
                if (!GetViewportRect().Grow(1).Encloses(panel) || !panel.Encloses(developerScroll.GetGlobalRect()) ||
                    developerScroll.Size.Y < 100 || developerScroll.Size.X < 300)
                    throw new InvalidOperationException($"Developer tools must fit the screen and scroll the rest at {size}: panel={panel}, scroll={developerScroll.GetGlobalRect()}, screen={GetViewportRect()}.");
            }

            // Readouts: the tile under the pointer, frame time, tick time and agents.
            UpdateHoverReadout(world, new Vector2I(1, 2));
            UpdateHoverReadout(null, null);
            UpdateDeveloperFrameTime(0.3);
            RenderDeveloperTools(world);
            if (!developerTileLabel.Text.StartsWith($"1, 2\n{HoverSummary(world, terrainMap!, new Vector2I(1, 2))}", StringComparison.Ordinal) ||
                !developerTileLabel.Text.Contains("Elevation 123/255", StringComparison.Ordinal) ||
                !developerFrameLabel.Text.Contains(" ms", StringComparison.Ordinal) ||
                !developerTickLabel.Text.StartsWith("4.2 ms on the host · tick 77", StringComparison.Ordinal) ||
                developerAgentCountLabel.Text != "2 living")
                throw new InvalidOperationException($"Developer tools must read out the last tile, frame time, tick time and agents: {developerTileLabel.Text} | {developerFrameLabel.Text} | {developerTickLabel.Text} | {developerAgentCountLabel.Text}.");
            RenderDeveloperTools(world with { LastTickMilliseconds = null });
            if (!developerTickLabel.Text.StartsWith("Not reported", StringComparison.Ordinal))
                throw new InvalidOperationException("A host that reports no tick time must say so instead of showing a number.");

            // Choosing an agent selects them and moves the camera to them.
            cameraZoom = maximumCameraZoom;
            RenderMap(world);
            SetCameraAtImmediately(new Vector2(3.5f, 0.5f));
            var names = Enumerable.Range(0, developerAgentList.ItemCount).Select(developerAgentList.GetItemText).ToArray();
            if (!names.SequenceEqual(new[] { "Mira", "Rowan" }))
                throw new InvalidOperationException($"The agent list must name each living agent: {string.Join(", ", names)}.");
            var before = cameraCenterTiles.DistanceTo(new Vector2(0.5f, 3.5f));
            developerAgentList.EmitSignal(ItemList.SignalName.ItemSelected, 0);
            AdvanceCameraMotion(CameraEasing.MoveSeconds);
            var after = cameraCenterTiles.DistanceTo(new Vector2(0.5f, 3.5f));
            if (selectedInhabitantId != other.Id || after >= before || after > 1)
                throw new InvalidOperationException($"Choosing an agent must select them and move the camera to them: selected={selectedInhabitantId} distance {before} → {after}.");

            // Direct edits follow the selected living agent and require pause.
            var paused = world with { Authoring = new(true, 0, 0, 0, "initial", "current", "clear", "spring", []) };
            RenderDeveloperEdits(paused with { Authoring = paused.Authoring! with { IsPaused = false } }, actionDisabled: false);
            if (!developerEditApply.Disabled)
                throw new InvalidOperationException("Direct edits must be unavailable in a running world.");
            RenderDeveloperEdits(paused, actionDisabled: false);
            if (developerEditApply.Disabled || developerEditAgent.Text != "Selected: Mira" || developerEditKind.ItemCount != 8)
                throw new InvalidOperationException("Paused Developer tools must offer all eight edits for the selected agent.");
            developerEditKind.Select(5);
            ConfigureDeveloperEdit();
            RenderDeveloperEdits(paused, actionDisabled: false);
            if (!developerEditOther.Visible || developerEditValue.Visible || developerEditAmount.Visible ||
                developerEditOther.ItemCount != 1 || developerEditOther.GetSelectedMetadata().AsString() != walker.Id)
                throw new InvalidOperationException("Partnership edits must select another living agent.");
            developerEditKind.Select(3);
            ConfigureDeveloperEdit();
            if (developerEditValue.ItemCount != 4 || developerEditOther.Visible || developerEditAmount.Visible)
                throw new InvalidOperationException("Skill edits must offer the four supported skills without quantity or partner fields.");
            developerEditKind.Select(7);
            ConfigureDeveloperEdit();
            if (developerEditValue.ItemCount != 8 || !developerEditValue.Visible || developerEditOther.Visible || developerEditAmount.Visible ||
                !Enumerable.Range(0, 8).Select(index => developerEditValue.GetItemMetadata(index).AsString()).Contains("horse:male"))
                throw new InvalidOperationException("Animal placement must offer the species and sex without quantity or partner fields.");
            developerEditKind.Select(0);
            ConfigureDeveloperEdit();
            RenderDeveloperEdits(paused with { Inhabitants = [other with { Lifecycle = "dead" }] }, actionDisabled: false);
            if (!developerEditApply.Disabled)
                throw new InvalidOperationException("Historical profiles cannot be edited.");
            RenderDeveloperEdits(paused, actionDisabled: true);
            if (!developerEditApply.Disabled)
                throw new InvalidOperationException("Direct edits must be unavailable without owner action access.");

            // Show planned path draws the selected agent's reported route, and nothing else.
            developerPathToggle.ButtonPressed = true;
            if (plannedPathLayer.Points.Count != 0 || !developerRouteLabel.Text.Contains("not walking", StringComparison.Ordinal))
                throw new InvalidOperationException("An agent with no reported route must draw no path.");
            developerAgentList.EmitSignal(ItemList.SignalName.ItemSelected, 1);
            var stride = currentTileSize + TileGap;
            var expected = new[] { new Vector2(2.5f, 0.5f), new(3.5f, 1.5f), new(3.5f, 2.5f), new(3.5f, 3.5f) }
                .Select(point => point * stride).ToArray();
            if (selectedInhabitantId != walker.Id || !plannedPathLayer.Points.SequenceEqual(expected) ||
                plannedPathLayer.Destination != new Vector2(3.5f, 3.5f) * stride ||
                !developerRouteLabel.Text.Contains("3 steps left toward 3, 3", StringComparison.Ordinal))
                throw new InvalidOperationException($"Show planned path must draw the selected agent's route: {string.Join(' ', plannedPathLayer.Points)} | {developerRouteLabel.Text}.");
            developerPathToggle.ButtonPressed = false;
            if (plannedPathLayer.Points.Count != 0)
                throw new InvalidOperationException("Turning Show planned path off must remove the path.");
            developerPathToggle.ButtonPressed = true;

            // F12 also closes the panel while one of its fields is being typed in, and the path goes with it.
            var editable = revokeDeviceId.Editable;
            revokeDeviceId.Editable = true;
            revokeDeviceId.GrabFocus();
            var typing = revokeDeviceId.HasFocus();
            _Input(new InputEventKey { Keycode = Key.F12, Pressed = true });
            revokeDeviceId.Editable = editable;
            if (!typing || developerPanel.Visible || revokeDeviceId.HasFocus() || plannedPathLayer.Points.Count != 0 ||
                gameMenuPanel.Visible)
                throw new InvalidOperationException("F12 must close Developer tools, even from one of their fields, and clear the path.");

            // Escape closes the panel last, before it would open the Pause Menu.
            _Input(new InputEventKey { Keycode = Key.F12, Pressed = true });
            selectedInhabitantCard.Hide();
            agentProfilePanel.Hide();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (developerPanel.Visible || gameMenuPanel.Visible)
                throw new InvalidOperationException("Escape must close Developer tools before it opens the Pause Menu.");
        }
        finally
        {
            developerPathToggle.ButtonPressed = false;
            if (developerPanel.Visible) CloseDeveloperTools();
            window.Size = originalSize;
            window.ContentScaleSize = originalRenderSize;
            cameraZoom = originalZoom;
            selectedInhabitantId = null;
            selectedInhabitantCard.Hide();
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ApplyResponsiveLayout();
            RenderMap(occupied);
        }
    }
}
