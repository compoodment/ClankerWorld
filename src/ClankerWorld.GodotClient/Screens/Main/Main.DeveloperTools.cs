using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// Developer tools: F12 opens a panel over a world without pausing it. It
/// shows the tile under the pointer, frame time, the host's tick time and
/// the agent count; jumps the camera to any agent and draws their planned
/// path as the server reports it; and holds the older testing tools (life
/// speed, lost-reply recovery, paused world editing and paired devices).
/// </summary>
public partial class Main
{
    private const float DeveloperToolsWidth = 460;
    private const double DeveloperReadoutSeconds = 0.25;

    private readonly PanelContainer developerPanel = new();
    private readonly ScrollContainer developerScroll = new();
    private readonly VBoxContainer developerBody = new();
    private readonly Label developerTileLabel = new();
    private readonly Label developerFrameLabel = new();
    private readonly Label developerTickLabel = new();
    private readonly Label developerAgentCountLabel = new();
    private readonly ItemList developerAgentList = new();
    private readonly CheckBox developerPathToggle = new();
    private readonly Label developerRouteLabel = new();
    private readonly PlannedPathLayer plannedPathLayer = new();
    private Vector2I? developerTile;
    private double developerFrameMilliseconds;
    private double developerReadoutAge;
    private string? developerAgentListKey;

    private void BuildDeveloperTools(Control content)
    {
        developerBody.AddThemeConstantOverride("separation", 8);

        var readouts = new VBoxContainer();
        readouts.AddThemeConstantOverride("separation", 4);
        developerTileLabel.Text = "Point at the map.";
        readouts.AddChild(MetricRow("Tile", developerTileLabel));
        readouts.AddChild(MetricRow("Frame time", developerFrameLabel));
        developerTickLabel.TooltipText = "How long the host took to work out the latest world tick, not counting saving it.";
        readouts.AddChild(MetricRow("Tick time", developerTickLabel));
        readouts.AddChild(MetricRow("Agents", developerAgentCountLabel));
        developerBody.AddChild(NewPanel("Readouts", readouts));

        var agents = new VBoxContainer();
        agents.AddThemeConstantOverride("separation", 6);
        developerAgentList.CustomMinimumSize = new Vector2(0, 96);
        developerAgentList.AllowReselect = true;
        developerAgentList.TooltipText = "Choose an agent to select them and move the camera to them.";
        developerAgentList.ItemSelected += JumpToDeveloperAgent;
        agents.AddChild(developerAgentList);
        developerPathToggle.Text = "Show planned path";
        developerPathToggle.TooltipText = "Draw the route the selected agent is walking, as the host planned it.";
        developerPathToggle.Toggled += _ => RenderDeveloperRoute();
        agents.AddChild(developerPathToggle);
        developerRouteLabel.ThemeTypeVariation = "DimLabel";
        developerRouteLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        agents.AddChild(developerRouteLabel);
        developerBody.AddChild(NewPanel("Jump to an agent", agents));

        developerBody.AddChild(new Label
        {
            Text = "The tools below act on the running world. The aging override and world editing need it paused: press Space.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        BuildDeveloperEdits();
        BuildDeveloperLifePace();
        BuildDeveloperRecovery();
        BuildDeveloperAuthoring();
        BuildDeveloperDevices();

        developerScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        developerScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        // Keep the scrollbar clear of the boxes' edges, as in Settings.
        var scrollGap = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scrollGap.AddThemeConstantOverride("margin_right", SettingsScrollGap);
        scrollGap.AddChild(developerBody);
        developerScroll.AddChild(scrollGap);
        AddClosablePanelContents(developerPanel, "Developer tools", developerScroll, CloseDeveloperTools);
        developerPanel.ZIndex = 86;
        developerPanel.Hide();
        content.AddChild(developerPanel);
        // Readouts change length as they update; refit once the new text is laid out.
        developerBody.MinimumSizeChanged += () => Callable.From(PositionDeveloperTools).CallDeferred();
        UpdateAuthoringHint();
    }

    private void BuildDeveloperLifePace()
    {
        var lifePaceRow = new HBoxContainer();
        lifePaceRow.AddChild(new Label { Text = "Aging multiplier" });
        lifePaceChoice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lifePaceChoice.AddItem("Calendar", 1);
        lifePaceChoice.AddItem("Generations", 365);
        lifePaceChoice.AddItem("Fast generations", 1_460);
        lifePaceChoice.SetItemTooltip(0, "Original aging: one biological year per 365 world days.");
        lifePaceChoice.SetItemTooltip(1, "One biological year per world day (about 24 active minutes).");
        lifePaceChoice.SetItemTooltip(2, "One biological year per quarter-day (about 6 active minutes).");
        lifePaceChoice.TooltipText = "Prototype override only: changes future biological aging without changing the calendar, seasons or model-call speed. This is not the decided 40-day year or six-hour lifespan.";
        lifePaceRow.AddChild(lifePaceChoice);
        applyLifePaceButton.Text = "Apply";
        StyleButton(applyLifePaceButton);
        applyLifePaceButton.Pressed += () => _ = SaveLifePaceAsync();
        lifePaceRow.AddChild(applyLifePaceButton);
        var prototypePaceBody = new VBoxContainer();
        prototypePaceBody.AddChild(new Label
        {
            Text = "Experimental prototype control. The decided world calendar and lifespan are not implemented by this setting.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        prototypePaceBody.AddChild(lifePaceRow);
        developerBody.AddChild(NewPanel("Prototype aging override", prototypePaceBody));
    }

    private void BuildDeveloperRecovery()
    {
        var retryBody = new VBoxContainer();
        pendingSubmissionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        retryBody.AddChild(pendingSubmissionLabel);
        var retryButtons = new HBoxContainer();
        retryPendingSubmissionButton.Text = "Retry retained request";
        retryPendingSubmissionButton.Pressed += () => _ = RetryPendingSubmissionAsync();
        retryButtons.AddChild(retryPendingSubmissionButton);
        forgetPendingSubmissionButton.Text = "Forget retained request";
        forgetPendingSubmissionButton.Pressed += ForgetPendingSubmission;
        retryButtons.AddChild(forgetPendingSubmissionButton);
        retryBody.AddChild(retryButtons);
        developerBody.AddChild(NewPanel("Response-loss recovery · exact server retry", retryBody));
        RenderPendingSubmission();
    }

    private void BuildDeveloperAuthoring()
    {
        var authoringBody = new VBoxContainer();
        authoringKind.ItemSelected += _ => UpdateAuthoringHint();
        AddAuthoringKinds();
        authoringBody.AddChild(authoringKind);
        authoringId.PlaceholderText = "ID (resource/object/draft/asset as required)";
        authoringBody.AddChild(authoringId);
        authoringValue.PlaceholderText = "Value (terrain, kind, name, weather, digest...)";
        authoringBody.AddChild(authoringValue);
        authoringSecondaryValue.PlaceholderText = "Secondary value (season for set_weather_season)";
        authoringBody.AddChild(authoringSecondaryValue);
        var coordinateRow = new HBoxContainer();
        ConfigureCoordinate(authoringX, "x");
        ConfigureCoordinate(authoringY, "y");
        coordinateRow.AddChild(authoringX);
        coordinateRow.AddChild(authoringY);
        authoringRenewable.Text = "renewable resource";
        coordinateRow.AddChild(authoringRenewable);
        authoringBody.AddChild(coordinateRow);
        authoringHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        authoringBody.AddChild(authoringHintLabel);
        submitAuthoringButton.Text = "Apply one paused authoring operation";
        submitAuthoringButton.Pressed += () => _ = SubmitAuthoringAsync();
        authoringBody.AddChild(submitAuthoringButton);
        developerBody.AddChild(NewPanel("Paused authoring · server validates atomically", authoringBody));
    }

    private void BuildDeveloperDevices()
    {
        var deviceManagementBody = new VBoxContainer();
        pairingApprovalId.PlaceholderText = "Pending pairing ID from the new device";
        deviceManagementBody.AddChild(pairingApprovalId);
        pairingApprovalCode.PlaceholderText = "Six-digit comparison code";
        pairingApprovalCode.Secret = true;
        deviceManagementBody.AddChild(pairingApprovalCode);
        approvePairingButton.Text = "Approve paired device";
        approvePairingButton.Pressed += () => _ = ApprovePairingAsync();
        deviceManagementBody.AddChild(approvePairingButton);
        refreshDevicesButton.Text = "Refresh signed device list";
        refreshDevicesButton.Pressed += () => _ = RefreshDeviceRegistryAsync();
        deviceManagementBody.AddChild(refreshDevicesButton);
        pairedDeviceList.CustomMinimumSize = new Vector2(0, 104);
        pairedDeviceList.ItemSelected += index =>
        {
            var deviceId = pairedDeviceList.GetItemMetadata(checked((int)index)).AsString();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                revokeDeviceId.Text = deviceId;
            }
        };
        deviceManagementBody.AddChild(pairedDeviceList);
        revokeDeviceId.PlaceholderText = "Device ID to revoke";
        deviceManagementBody.AddChild(revokeDeviceId);
        revokeDeviceButton.Text = "Revoke other device";
        revokeDeviceButton.Pressed += () => _ = RevokeDeviceAsync();
        deviceManagementBody.AddChild(revokeDeviceButton);
        developerBody.AddChild(NewPanel("Paired-device management · signed server requests", deviceManagementBody));
    }

    /// <summary>F12: opens or closes the panel. Time keeps running either way.</summary>
    private void ToggleDeveloperTools()
    {
        if (developerPanel.Visible)
        {
            CloseDeveloperTools();
            return;
        }
        developerPanel.Show();
        developerReadoutAge = DeveloperReadoutSeconds;
        if (renderedMapSnapshot is { } snapshot) RenderDeveloperTools(snapshot);
        PositionDeveloperTools();
    }

    private void CloseDeveloperTools()
    {
        // A field left focused in the closed panel would keep blocking map keys.
        if (GetViewport().GuiGetFocusOwner() is { } focus && developerPanel.IsAncestorOf(focus))
            focus.ReleaseFocus();
        developerPanel.Hide();
        RenderPlannedPath();
    }

    /// <summary>
    /// The panel sits at the right edge below the HUD, as wide as its tools
    /// need, and scrolls when the screen is too short for all of them.
    /// </summary>
    private void PositionDeveloperTools()
    {
        if (!developerPanel.Visible) return;
        var ui = UiSize;
        if (ui.X <= 0 || ui.Y <= 0) return;
        developerPanel.CustomMinimumSize = new Vector2(Math.Min(DeveloperToolsWidth, Math.Max(1, ui.X - 28)), 0);
        developerScroll.CustomMinimumSize = Vector2.Zero;
        var around = developerPanel.GetCombinedMinimumSize().Y;
        var contents = developerBody.GetCombinedMinimumSize().Y;
        var top = Math.Min(HudTop, Math.Max(14, ui.Y - 134 - around));
        developerScroll.CustomMinimumSize = new Vector2(0, Math.Clamp(contents, 120, Math.Max(120, ui.Y - top - 14 - around)));
        developerPanel.Size = developerPanel.GetCombinedMinimumSize();
        developerPanel.Position = new Vector2(Math.Max(14, ui.X - developerPanel.Size.X - 14), top);
    }

    /// <summary>Refreshes the readouts and agent list from a new observation.</summary>
    private void RenderDeveloperTools(OwnerWorldSnapshot snapshot)
    {
        if (!developerPanel.Visible) return;
        var living = snapshot.Inhabitants.Count(person => !person.IsDraft && IsLiving(person));
        var died = snapshot.Inhabitants.Count(person => !person.IsDraft && !IsLiving(person));
        developerAgentCountLabel.Text = died == 0 ? $"{living} living" : $"{living} living · {died} died";
        developerTickLabel.Text = snapshot.LastTickMilliseconds is { } milliseconds
            ? $"{milliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms on the host · tick {snapshot.WorldTick.ToString("N0", CultureInfo.InvariantCulture)}"
            : $"Not reported by this host yet · tick {snapshot.WorldTick.ToString("N0", CultureInfo.InvariantCulture)}";
        RenderDeveloperAgentList(snapshot);
        RenderDeveloperTile();
        RenderDeveloperRoute();
    }

    private void RenderDeveloperAgentList(OwnerWorldSnapshot snapshot)
    {
        var people = snapshot.Inhabitants.Where(person => !person.IsDraft && IsLiving(person))
            .OrderBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id, StringComparer.Ordinal)
            .ToArray();
        // Rebuild only when the people change, so the list keeps its scroll position.
        var key = string.Join('\n', people.Select(person => person.Id + "\t" + person.DisplayName));
        if (key != developerAgentListKey)
        {
            developerAgentListKey = key;
            developerAgentList.Clear();
            foreach (var person in people)
            {
                var row = developerAgentList.AddItem(person.DisplayName);
                developerAgentList.SetItemMetadata(row, person.Id);
            }
            if (people.Length == 0)
                developerAgentList.SetItemMetadata(developerAgentList.AddItem("No one lives here yet.", selectable: false), string.Empty);
        }
        developerAgentList.DeselectAll();
        for (var index = 0; index < developerAgentList.ItemCount; index++)
        {
            if (string.Equals(developerAgentList.GetItemMetadata(index).AsString(), selectedInhabitantId, StringComparison.Ordinal))
                developerAgentList.Select(index);
        }
    }

    /// <summary>Selects the chosen agent and brings them into view.</summary>
    private void JumpToDeveloperAgent(long index)
    {
        if (index < 0 || index >= developerAgentList.ItemCount) return;
        var id = developerAgentList.GetItemMetadata((int)index).AsString();
        if (string.IsNullOrEmpty(id)) return;
        if (!string.Equals(id, selectedInhabitantId, StringComparison.Ordinal)) SelectInhabitant(id);
        CenterOnInhabitant(id);
        RenderDeveloperRoute();
    }

    /// <summary>Keeps the last tile the pointer was over, so moving onto the panel does not lose it.</summary>
    private void NoteDeveloperTile(Vector2I? tile)
    {
        if (tile is null || tile == developerTile) return;
        developerTile = tile;
        if (developerPanel.Visible) RenderDeveloperTile();
    }

    private void RenderDeveloperTile()
    {
        if (developerTile is not { } tile || renderedMapSnapshot is not { } snapshot || terrainMap is null ||
            !MapContains(snapshot, tile.X, tile.Y))
        {
            developerTileLabel.Text = "Point at the map.";
            return;
        }
        var extra = new List<string>();
        if (terrainMap.ElevationAt(tile.X, tile.Y) is { } elevation) extra.Add($"Elevation {elevation}/255");
        if (WorldTerrainMap.ClimateName(terrainMap.ClimateAt(tile.X, tile.Y)) is { } climate) extra.Add(climate);
        developerTileLabel.Text = $"{tile.X}, {tile.Y}\n{HoverSummary(snapshot, terrainMap, tile)}" +
            (extra.Count == 0 ? string.Empty : "\n" + string.Join(" · ", extra));
    }

    /// <summary>Frame time is smoothed and shown a few times a second, so it can be read.</summary>
    private void UpdateDeveloperFrameTime(double delta)
    {
        if (!developerPanel.Visible) return;
        developerFrameMilliseconds = developerFrameMilliseconds <= 0
            ? delta * 1000
            : developerFrameMilliseconds + (delta * 1000 - developerFrameMilliseconds) * 0.1;
        developerReadoutAge += delta;
        if (developerReadoutAge < DeveloperReadoutSeconds) return;
        developerReadoutAge = 0;
        developerFrameLabel.Text = $"{developerFrameMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms · " +
            $"{Engine.GetFramesPerSecond().ToString("0", CultureInfo.InvariantCulture)} frames a second";
    }

    private void RenderDeveloperRoute()
    {
        var person = renderedMapSnapshot?.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        developerRouteLabel.Text = person is null ? "Select an agent to see their route." :
            person.PlannedRoute is not { } route ? $"{person.DisplayName} is not walking anywhere right now." :
            $"{person.DisplayName}: {route.StepCount} {(route.StepCount == 1 ? "step" : "steps")} left toward " +
            $"{route.Destination.X}, {route.Destination.Y} ({Pretty(route.Reason).ToLowerInvariant()}).";
        RenderPlannedPath();
    }

    /// <summary>
    /// Draws the selected agent's reported route while the panel is open and
    /// Show planned path is on. On a wrapped map each step follows the short
    /// way round from the agent as drawn.
    /// </summary>
    private void RenderPlannedPath()
    {
        var snapshot = renderedMapSnapshot;
        var person = snapshot?.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (!developerPanel.Visible || !developerPathToggle.ButtonPressed || snapshot is null || terrainMap is null ||
            person is null || person.IsDraft || !IsLiving(person) || person.PlannedRoute is not { } route)
        {
            plannedPathLayer.Clear();
            return;
        }
        var stride = currentTileSize + TileGap;
        var width = terrainMap.Width;
        float Nearer(int fromX, int toX)
        {
            var step = toX - fromX;
            return snapshot.WrapsEastWest && Math.Abs(step) > width / 2 ? step - Math.Sign(step) * width : step;
        }
        var x = WrappedMarkerX(person.Position.X * stride, width, stride, snapshot.WrapsEastWest) / stride;
        var previous = person.Position.X;
        var points = new List<Vector2> { new(x + 0.5f, person.Position.Y + 0.5f) };
        foreach (var step in route.Steps)
        {
            x += Nearer(previous, step.X);
            previous = step.X;
            points.Add(new Vector2(x + 0.5f, step.Y + 0.5f));
        }
        var goal = new Vector2(x + Nearer(previous, route.Destination.X) + 0.5f, route.Destination.Y + 0.5f);
        plannedPathLayer.SetPath(points.Select(point => point * stride).ToArray(), goal * stride, currentTileSize);
    }
}
