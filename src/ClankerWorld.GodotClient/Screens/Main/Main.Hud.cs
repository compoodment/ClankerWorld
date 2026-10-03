using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The in-world HUD: three framed groups floating over the map (map tools;
/// time, season and weather; world actions), pixel icons on every button,
/// an unread count on the Event Log, a warning dot when an agent is hungry,
/// and World Info with Towns and World pages. A world can hold several
/// Towns, so there is no single-Town button.
/// </summary>
public partial class Main
{
    private readonly MarginContainer hudBar = new();
    private readonly VBoxContainer hudRows = new();
    private readonly HBoxContainer hudLeft = new();
    private readonly HBoxContainer hudTime = new();
    private readonly HBoxContainer hudRight = new();
    private readonly HBoxContainer founderHudRow = new();
    private readonly HBoxContainer hudFounders = new();
    private readonly HBoxContainer climateBox = new();
    private readonly TextureRect seasonIcon = new();
    private readonly Label seasonLabel = new();
    private readonly TextureRect weatherIcon = new();
    private readonly Label weatherLabel = new();
    private readonly PixelBadge eventsBadge = new();
    private readonly PixelBadge agentsWarning = new() { Diameter = 7, Warning = true };
    private readonly Button worldInfoTownsTab = new();
    private readonly Button worldInfoWorldTab = new();
    private readonly ScrollContainer townsScroll = new()
    {
        HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
    };
    private readonly VBoxContainer townsPage = new();
    private readonly VBoxContainer townList = new();
    private string? renderedTownList;
    private string? eventsWorldId;
    private long lastSeenEventId = long.MinValue;
    private long newEventsAfter = long.MaxValue;
    private int unreadEvents;
    private readonly Dictionary<Button, string> hudButtonLabels = [];

    /// <summary>Top of the map area left free by the floating HUD.</summary>
    private float HudTop => hudBar.Visible && hudBar.Size.Y > 0 ? hudBar.Position.Y + hudBar.Size.Y + 8 : 14;

    private const int HudIconScale = 2;

    private void BuildTopBar(Control canvas)
    {
        hudBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        hudBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        hudBar.ZIndex = 55;
        hudBar.AddThemeConstantOverride("margin_left", 12);
        hudBar.AddThemeConstantOverride("margin_right", 12);
        hudBar.AddThemeConstantOverride("margin_top", 10);
        hudBar.AddThemeConstantOverride("margin_bottom", 0);
        topBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        topBar.AddThemeConstantOverride("separation", 10);

        mapButton.Text = "Map";
        mapButton.TooltipText = "World map (M). Scroll to zoom, and use WASD or middle-drag to move around. F1 lists all controls.";
        StyleButton(mapButton);
        mapButton.Pressed += () =>
        {
            var show = !worldOverviewPanel.Visible;
            rosterPanel.Hide();
            eventsPanel.Hide();
            familyTreePanel.Hide();
            worldInfoPanel.Hide();
            filtersPanel.Hide();
            worldOverviewPanel.Visible = show;
        };
        hudLeft.AddChild(mapButton);
        BuildFiltersButton();
        topBar.AddChild(HudGroup(hudLeft));

        pauseButton.Caption = "Pause";
        StyleButton(pauseButton);
        pauseButton.Pressed += () => _ = TogglePauseAsync();
        hudTime.AddChild(pauseButton);
        clockLabel.Text = "Connecting...";
        clockLabel.ThemeTypeVariation = "HeadingLabel";
        clockLabel.VerticalAlignment = VerticalAlignment.Center;
        hudTime.AddChild(clockLabel);
        climateBox.AddThemeConstantOverride("separation", 6);
        climateBox.AddChild(new VSeparator());
        foreach (var (icon, label) in new[] { (seasonIcon, seasonLabel), (weatherIcon, weatherLabel) })
        {
            icon.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.VerticalAlignment = VerticalAlignment.Center;
            climateBox.AddChild(icon);
            climateBox.AddChild(label);
        }
        climateBox.Hide();
        hudTime.AddChild(climateBox);
        topBar.AddChild(HudGroup(hudTime, padRight: true));

        topBar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        inhabitantsButton.Text = "0";
        StyleButton(inhabitantsButton);
        inhabitantsButton.Pressed += ToggleInhabitants;
        AttachCorner(inhabitantsButton, agentsWarning, new Vector2(-12, 4));
        hudRight.AddChild(inhabitantsButton);

        worldInfoButton.Text = "Info";
        worldInfoButton.TooltipText = "World Info: Towns and the world (I). T opens Towns.";
        StyleButton(worldInfoButton);
        worldInfoButton.Pressed += ToggleWorldInfo;
        hudRight.AddChild(worldInfoButton);

        eventsButton.Text = "Events";
        eventsButton.TooltipText = "Event Log (E)";
        StyleButton(eventsButton);
        eventsButton.Pressed += ToggleEvents;
        AttachCorner(eventsButton, eventsBadge, new Vector2(-12, -6));
        hudRight.AddChild(eventsButton);

        townSiteButton.Text = "Choose Town site";
        StyleButton(townSiteButton);
        townSiteButton.Pressed += ToggleFirstTownSite;
        townSiteButton.Hide();
        hudFounders.AddChild(townSiteButton);

        moveFounderButton.Text = "Move founder";
        moveFounderButton.TooltipText = "Pick a founder you have placed, then click a new spot. Only works before time starts.";
        StyleButton(moveFounderButton);
        moveFounderButton.Pressed += ToggleMoveFounder;
        moveFounderButton.Hide();
        hudFounders.AddChild(moveFounderButton);

        undoFounderButton.Text = "Undo last founder";
        undoFounderButton.TooltipText = "Take back the last founder you placed. Their model choice is cleared. Your saved keys stay.";
        StyleButton(undoFounderButton);
        undoFounderButton.Pressed += () => _ = UndoLastFounderAsync();
        undoFounderButton.Hide();
        hudFounders.AddChild(undoFounderButton);

        founderSetupButton.Text = "Add founders";
        StyleButton(founderSetupButton);
        founderSetupButton.Pressed += () => _ = ToggleFounderSetupAsync();
        hudFounders.AddChild(founderSetupButton);

        startWorldButton.Text = "Start World";
        StyleButton(startWorldButton, primary: true);
        startWorldButton.Pressed += () => _ = StartFounderWorldAsync();
        hudFounders.AddChild(startWorldButton);

        addAgentButton.Text = "Add Agent";
        addAgentButton.TooltipText = "Add an agent to the world.";
        StyleButton(addAgentButton);
        addAgentButton.Pressed += () => _ = ToggleAddAgentAsync();
        hudRight.AddChild(addAgentButton);

        menuButton.Text = "Menu";
        menuButton.TooltipText = "Pause Menu (Esc)";
        StyleButton(menuButton);
        menuButton.Pressed += () => _ = ToggleGameMenuAsync();
        hudRight.AddChild(menuButton);
        topBar.AddChild(HudGroup(hudRight));

        // Activated buttons return focus to the map. They remain reachable by
        // Tab/Enter, including actions without a one-key shortcut.
        foreach (var button in HudButtons())
            button.Pressed += button.ReleaseFocus;
        hudRows.AddChild(topBar);
        founderHudRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        founderHudRow.AddChild(HudGroup(hudFounders));
        founderHudRow.Hide();
        hudRows.AddChild(founderHudRow);
        hudBar.AddChild(hudRows);
        // Mirrors the world-view menu shade so HUD actions such as Start World
        // or Play cannot run behind a modal menu. The menu shade already dims
        // the HUD, which now floats inside the world view, so this one only
        // blocks clicks.
        topBarShade.Color = new Color(0, 0, 0, 0);
        topBarShade.MouseFilter = Control.MouseFilterEnum.Stop;
        topBarShade.ZIndex = 190;
        topBarShade.Hide();
        hudBar.AddChild(topBarShade);
        hudBar.Resized += ApplyResponsiveLayout;
        canvas.AddChild(hudBar);
        RefreshHudIcons();
    }

    /// <summary>A small wooden frame around a row of HUD controls.</summary>
    private static PanelContainer HudGroup(HBoxContainer row, bool padRight = false)
    {
        row.AddThemeConstantOverride("separation", 5);
        var group = new PanelContainer { ThemeTypeVariation = "HudPanel" };
        if (!padRight)
        {
            group.AddChild(row);
            return group;
        }
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddChild(row);
        group.AddChild(margin);
        return group;
    }

    /// <summary>Pins a small click-through marker to a button's top-right corner.</summary>
    private static void AttachCorner(Button button, Control marker, Vector2 offset)
    {
        marker.MouseFilter = Control.MouseFilterEnum.Ignore;
        marker.Position = new Vector2(offset.X, offset.Y);
        // A marker can reach past the button's edge; draw it over the next button.
        marker.ZIndex = 1;
        marker.Hide();
        button.AddChild(marker);
        button.Resized += () => marker.Position = new Vector2(button.Size.X + offset.X, offset.Y);
    }

    /// <summary>
    /// On a narrow interface the top bar keeps its icons and drops the words
    /// that would push it off screen; each tooltip still names its button.
    /// </summary>
    private void CompactHud(bool compact)
    {
        if (hudButtonLabels.Count == 0)
            foreach (var button in new[] { mapButton, filtersButton, worldInfoButton, eventsButton, addAgentButton, menuButton })
                hudButtonLabels[button] = button.Text;
        foreach (var (button, label) in hudButtonLabels)
            button.Text = compact ? string.Empty : label;
    }

    /// <summary>
    /// Opens a top-bar panel just under the button that opened it, lined up
    /// with the button's outer edge, so it appears where the player is looking.
    /// </summary>
    private void PlaceUnderButton(Control panel, Control button)
    {
        if (!panel.Visible) return;
        if (!button.IsVisibleInTree())
        {
            panel.Position = new Vector2(panel.Position.X, Math.Max(panel.Position.Y, HudTop));
            return;
        }
        var ui = UiSize;
        var width = Math.Max(panel.Size.X, panel.CustomMinimumSize.X);
        var origin = (button.GlobalPosition - uiLayer.GlobalPosition) / uiLayer.Factor;
        var x = origin.X + button.Size.X / 2 < ui.X / 2 ? origin.X : origin.X + button.Size.X - width;
        panel.Position = new Vector2(Math.Clamp(x, 14, Math.Max(14, ui.X - width - 14)),
            Math.Max(HudTop, origin.Y + button.Size.Y + 8));
    }

    private void PlaceHudPanels()
    {
        PlaceUnderButton(worldOverviewPanel, mapButton);
        PlaceUnderButton(filtersPanel, filtersButton);
        PlaceUnderButton(rosterPanel, inhabitantsButton);
        PlaceUnderButton(worldInfoPanel, worldInfoButton);
        // The Event Log often stays open while playing, so it sits flush with the right edge.
        if (eventsPanel.Visible)
            eventsPanel.Position = new Vector2(
                Math.Max(14, UiSize.X - Math.Max(eventsPanel.Size.X, eventsPanel.CustomMinimumSize.X) - 14), HudTop);
        PlaceUnderButton(founderSetupPanel, placingAddedAgent && addAgentButton.IsVisibleInTree() ? addAgentButton : founderSetupButton);
    }

    /// <summary>Panels opened from the top bar; the agent card keeps clear of them.</summary>
    private Control[] HudPanels() => [worldOverviewPanel, filtersPanel, rosterPanel, worldInfoPanel, eventsPanel, founderSetupPanel];

    private IEnumerable<Button> HudButtons() =>
        new[] { hudLeft, hudTime, hudRight, hudFounders }.SelectMany(row => row.GetChildren().OfType<Button>());

    /// <summary>Icons follow the theme's ink and the current UI scale.</summary>
    private void RefreshHudIcons()
    {
        var palette = UiTheme.Current;
        var scale = HudIconScale;
        var accent = palette.Name == "dark" ? new Color("C99A62") : new Color("9C6C42");
        var green = palette.Name == "dark" ? new Color("8DBA6A") : palette.Primary;
        mapButton.Icon = PixelIcons.Themed(PixelGlyph.Map, green, scale);
        filtersButton.Icon = PixelIcons.Themed(PixelGlyph.Filter, accent, scale);
        inhabitantsButton.Icon = PixelIcons.Themed(PixelGlyph.Person, accent, scale);
        worldInfoButton.Icon = PixelIcons.Themed(PixelGlyph.Info, green, scale);
        eventsButton.Icon = PixelIcons.Themed(PixelGlyph.Scroll, accent, scale);
        addAgentButton.Icon = PixelIcons.Themed(PixelGlyph.PersonPlus, green, scale);
        menuButton.Icon = PixelIcons.Themed(PixelGlyph.Menu, palette.Ink, scale);
        var paused = renderedMapSnapshot?.Authoring?.IsPaused == true;
        pauseButton.Glyph = paused
            ? PixelIcons.Texture(PixelGlyph.Play, palette.EmberInk, palette.EmberInk, scale)
            : PixelIcons.Themed(PixelGlyph.Pause, palette.Ink, scale);
        if (renderedMapSnapshot?.Authoring is { } authoring)
        {
            seasonIcon.Texture = PixelIcons.Season(authoring.Season, scale);
            weatherIcon.Texture = PixelIcons.Weather(WeatherAtCamera(renderedMapSnapshot), scale);
        }
        RefreshMenuIcons();
        RefreshAgentCardIcons();
        FillControlsGroups();
    }

    /// <summary>Pause control, agent count and warning, climate and the unread count.</summary>
    private void RenderHudState(OwnerWorldSnapshot snapshot)
    {
        var paused = snapshot.Authoring?.IsPaused == true;
        pauseButton.Caption = paused ? "Paused" : "Pause";
        pauseButton.ThemeTypeVariation = paused ? "EmberButton" : string.Empty;
        pauseButton.TooltipText = paused ? "Time is stopped. Resume the world (Space)" : "Pause the world (Space)";

        var living = snapshot.Inhabitants.Where(person => !person.IsDraft && IsLiving(person)).ToArray();
        var hungry = living.Where(person => GameUiText.FullnessState(person.HungerBasisPoints) is "hungry" or "very hungry")
            .Select(person => person.DisplayName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        inhabitantsButton.Text = living.Length.ToString(CultureInfo.InvariantCulture);
        inhabitantsButton.TooltipText = $"Agents (R) · {living.Length} living" +
            (hungry.Length == 0 ? string.Empty : $" · hungry: {string.Join(", ", hungry)}") +
            ". N selects the next agent.";
        agentsWarning.Visible = hungry.Length > 0;

        if (snapshot.Authoring is { } authoring)
        {
            seasonLabel.Text = Pretty(authoring.Season);
            // A season date already names the season, so only the weather follows it.
            seasonIcon.Visible = seasonLabel.Visible = !DatesShowSeason;
            weatherLabel.Text = Pretty(WeatherAtCamera(snapshot));
            climateBox.Visible = Size.X >= 1100;
        }
        else
        {
            seasonLabel.Text = string.Empty;
            weatherLabel.Text = string.Empty;
            climateBox.Hide();
        }
        RefreshHudIcons();
        UpdateUnreadEvents(snapshot.WorldId);
    }

    /// <summary>
    /// Counts player-facing events that arrived since the Event Log was last
    /// open. Events already in the world when it is first opened count as read.
    /// </summary>
    private void UpdateUnreadEvents(string? worldId)
    {
        var ids = knownEvents.Values.Where(worldEvent => GameUiText.IsPlayerFacingEvent(worldEvent.Kind))
            .Select(worldEvent => worldEvent.EventId).ToArray();
        var newest = ids.Length == 0 ? long.MinValue : ids.Max();
        if (!string.Equals(eventsWorldId, worldId, StringComparison.Ordinal))
        {
            eventsWorldId = worldId;
            lastSeenEventId = newest;
            newEventsAfter = long.MaxValue;
        }
        if (eventsPanel.Visible) lastSeenEventId = Math.Max(lastSeenEventId, newest);
        unreadEvents = ids.Count(id => id > lastSeenEventId);
        eventsBadge.Text = unreadEvents > 9 ? "9+" : unreadEvents.ToString(CultureInfo.InvariantCulture);
        eventsBadge.Visible = unreadEvents > 0;
        eventsButton.TooltipText = unreadEvents == 0 ? "Event Log (E)" : $"Event Log (E) · {unreadEvents} new";
    }

    /// <summary>Opening the log marks everything read; the rows that were new keep a dot while it stays open.</summary>
    private void MarkEventsSeen()
    {
        newEventsAfter = lastSeenEventId;
        UpdateUnreadEvents(eventsWorldId);
        renderedEventLog = null;
        RenderEventLog();
    }

    private void BuildWorldInfoPanel(Control content)
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        foreach (var (tab, text, towns) in new[] { (worldInfoTownsTab, "Towns", true), (worldInfoWorldTab, "World", false) })
        {
            tab.Text = text;
            StyleSettingsCategoryButton(tab);
            tab.Pressed += () => ShowWorldInfoPage(towns);
            tabs.AddChild(tab);
        }
        body.AddChild(tabs);

        townList.AddThemeConstantOverride("separation", 6);
        townsPage.AddThemeConstantOverride("separation", 8);
        townsPage.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        townsPage.MinimumSizeChanged += QueueHudListsFit;
        townsPage.AddChild(townList);
        BuildWorldInfoPages(body);
        AddClosablePanelContents(worldInfoPanel, "World Info", body);
        worldInfoPanel.CustomMinimumSize = new Vector2(420, 0);
        worldInfoPanel.ZIndex = 80;
        worldInfoPanel.Hide();
        content.AddChild(worldInfoPanel);
        ShowWorldInfoPage(towns: true);
    }

    private bool WorldInfoShowsTowns => townsScroll.Visible;

    private void ShowWorldInfoPage(bool towns)
    {
        townsScroll.Visible = towns;
        worldStatsPage.Visible = !towns;
        worldInfoTownsTab.SetPressedNoSignal(towns);
        worldInfoWorldTab.SetPressedNoSignal(!towns);
        worldInfoPanel.ResetSize();
        FitHudLists();
        QueueHudListsFit();
    }

    /// <summary>T opens World Info on its Towns page; pressing it again there closes it.</summary>
    private void ToggleTowns()
    {
        if (!worldInfoButton.IsVisibleInTree() || worldInfoButton.Disabled) return;
        if (worldInfoPanel.Visible && WorldInfoShowsTowns)
        {
            worldInfoPanel.Hide();
            return;
        }
        if (!worldInfoPanel.Visible) ToggleWorldInfo();
        ShowWorldInfoPage(towns: true);
    }

    /// <summary>One row per Town: its name, residents and when it was founded, with a way to find it.</summary>
    private void RenderTownList(OwnerWorldSnapshot snapshot)
    {
        var signature = string.Join("\n", snapshot.Towns.Select(town =>
            $"{town.Id}|{town.Name}|{town.FoundingState}|{town.FoundedTick}|{town.ResidentIds.Count}|{town.BorderTiles.Count}|{ResidentPortraitsKey(snapshot, town)}|{TownCivicText(town, snapshot.WorldTick)}|{TownProjectText(town)}")) +
            "|" + displayPreferences.DateStyle + "|" + observedCalendarPace + "|" + UiTheme.Current.Name;
        if (renderedTownList == signature) return;
        renderedTownList = signature;
        foreach (var child in townList.GetChildren())
        {
            townList.RemoveChild(child);
            child.QueueFree();
        }
        if (snapshot.Towns.Count == 0)
        {
            townList.AddChild(new Label
            {
                Text = "No Town yet. The first Town appears once its site is chosen.",
                ThemeTypeVariation = "DimLabel",
            });
            worldInfoPanel.ResetSize();
            QueueHudListsFit();
            return;
        }
        foreach (var town in snapshot.Towns)
        {
            var row = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 10);
            line.AddChild(new TextureRect
            {
                Texture = PixelIcons.Themed(PixelGlyph.House, UiTheme.Current.Name == "dark" ? new Color("D89A5A") : new Color("B8733A"), 2),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            });
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var name = new Label { Text = town.Name, ThemeTypeVariation = "HeadingLabel" };
            text.AddChild(name);
            var founded = string.Equals(town.FoundingState, "founded", StringComparison.OrdinalIgnoreCase)
                ? "founded " + SplitClock(DisplayWorldClock(town.FoundedTick)).Date
                : Pretty(town.FoundingState).ToLowerInvariant();
            var facts = new Label
            {
                Text = $"{town.ResidentIds.Count} {(town.ResidentIds.Count == 1 ? "resident" : "residents")} · {founded}",
                ThemeTypeVariation = "DimLabel",
            };
            text.AddChild(facts);
            text.AddChild(ResidentPortraits(snapshot, town));
            if (town.Governance is not null)
                text.AddChild(new Label
                {
                    Text = TownCivicText(town, snapshot.WorldTick),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(300, 0),
                });
            if (town.Projects.Count > 0)
                text.AddChild(new Label
                {
                    Text = TownProjectText(town),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(300, 0),
                });
            line.AddChild(text);
            var show = new Button
            {
                Text = "Show",
                TooltipText = $"Move the map to {town.Name}.",
                Icon = PixelIcons.Themed(PixelGlyph.Find, UiTheme.Current.Primary, 1),
                Disabled = town.BorderTiles.Count == 0,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            StyleButton(show);
            var townId = town.Id;
            show.Pressed += () => CenterOnTown(townId);
            line.AddChild(show);
            row.AddChild(line);
            townList.AddChild(row);
        }
        // Shrink back to fit when the list gets shorter.
        worldInfoPanel.ResetSize();
        FitHudLists();
        QueueHudListsFit();
    }

    private string TownCivicText(OwnerWorldTown town, long tick)
    {
        if (town.Governance is not { } council) return "";
        var lines = new List<string>
        {
            council.Form == "representative" ? "Council: elected representatives" : "Council: all adult residents",
            council.MemberNames.Count == 0 ? "No adult councillors." : string.Join(", ", council.MemberNames),
        };
        if (council.TermEndTick is { } termEnd) lines.Add("Term ends " + DisplayWorldClock(termEnd));
        if (council.Fallback == "candidates") lines.Add("All adults govern while the Town seeks a supported council.");
        if (council.Fallback == "demographic") lines.Add("Representation resumes at eight adult residents.");
        if (council.Election is { } election)
        {
            var stage = election.Stage switch
            {
                "main" => "Voting",
                "runoff" => "Runoff voting",
                "ready" => "Representatives chosen",
                _ => "Voting has ended",
            };
            lines.Add($"{CivicElectionName(election.Kind)} · {stage} · {election.Seats} {(election.Seats == 1 ? "seat" : "seats")}");
            if (election.Stage is "main" or "runoff") lines.Add("Voting closes " + DisplayWorldClock(election.DeadlineTick));
            lines.Add(election.Candidates.Count == 0 ? "No willing eligible candidates in this contest." :
                string.Join(" · ", election.Candidates.Select(c => $"{c.Name}: {c.Votes} {(c.Votes == 1 ? "vote" : "votes")}")));
            if (election.SettledNames.Count > 0) lines.Add("Chosen: " + string.Join(", ", election.SettledNames));
            if (election.Stage == "ready" && election.Kind == "regular" && council.TermEndTick is { } currentTermEnd && currentTermEnd > tick)
                lines.Add("The chosen representatives take office when the current term ends.");
        }
        else if (council.RetryTick > tick) lines.Add("Election retry after " + DisplayWorldClock(council.RetryTick));
        if (council.LatestElection is { } latest)
        {
            lines.Add(latest.Stage switch
            {
                "completed" => "Last election completed: " + string.Join(", ", latest.SettledNames),
                "failed" => "The last election did not elect a supported council.",
                "cancelled" => "The last election was cancelled.",
                _ => "The last election has ended.",
            });
        }
        if (council.WillingCandidateNames.Count > 0) lines.Add("Willing candidates: " + string.Join(", ", council.WillingCandidateNames));
        foreach (var proposal in council.Proposals.TakeLast(8))
        {
            lines.Add($"{Pretty(proposal.Status)} {Pretty(proposal.Kind).ToLowerInvariant()} proposal: {proposal.Text}");
            lines.Add($"{proposal.Yes} yes / {proposal.No} no · {proposal.RequiredYes} yes needed" +
                (proposal.Status == "pending" ? " · closes " + DisplayWorldClock(proposal.DeadlineTick) : ""));
            if (proposal.Project is { } plan && proposal.Status == "pending")
            {
                lines.Add($"{plan.ProposerName} proposes {plan.Name} · {plan.DisplayName} · {plan.Width} × {plan.Height} tiles at ({plan.Site.X}, {plan.Site.Y})");
                lines.Add($"Entrance: ({plan.Entrance.X}, {plan.Entrance.Y}) · Provisional budget: " +
                    string.Join(" · ", plan.Budget.Select(q => $"{q.Quantity} {GameUiText.ItemName(q.Kind)}")));
            }
        }
        // Proposals are written by agents' models, which may use the font's mid-height ellipsis.
        return GameUiText.PlainEllipses(string.Join("\n", lines));
    }

    private static string TownProjectText(OwnerWorldTown town)
    {
        var lines = new List<string>();
        foreach (var project in town.Projects)
        {
            lines.Add($"{project.Name} · {project.DisplayName} · {Pretty(project.Stage)}");
            lines.Add($"Proposed by {project.ProposerName} · {project.Width} × {project.Height} tiles at ({project.Site.X}, {project.Site.Y})");
            lines.Add($"Entrance: ({project.Entrance.X}, {project.Entrance.Y})");
            lines.Add("Provisional budget · supplied: " + string.Join(" · ", project.Materials.Select(q =>
                $"{q.Supplied} / {q.Budget} {GameUiText.ItemName(q.Kind)}")));
            lines.Add($"Provisional work: {project.WorkDone} / {project.WorkRequired} units");
            lines.Add($"Council approval: {project.Approval.Yes} yes / {project.Approval.No} no · {project.Approval.RequiredYes} yes needed");
            if (project.Blocker is { } blocker) lines.Add("Waiting: " + blocker);
            if (project.CompletedBuildingId is not null) lines.Add("Built · select the Town Hall on the map for details.");
        }
        return GameUiText.PlainEllipses(string.Join("\n", lines));
    }

    private static string CivicElectionName(string kind) => kind switch
    {
        "regular" => "Scheduled council election",
        "replacement" => "Council vacancy election",
        _ => "Council election",
    };

    /// <summary>Every label in the Towns list, for checks and assistive reading.</summary>
    private string TownListText() => string.Join("\n",
        townList.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text));

    /// <summary>Centers the map on a Town's border, measured across the seam on wrapped worlds.</summary>
    private void CenterOnTown(string townId)
    {
        if (renderedMapSnapshot is not { } snapshot || terrainMap is null ||
            snapshot.Towns.FirstOrDefault(town => town.Id == townId) is not { BorderTiles.Count: > 0 } town)
            return;
        var anchor = town.BorderTiles[0];
        var width = terrainMap.Width;
        var sum = Vector2.Zero;
        foreach (var tile in town.BorderTiles)
        {
            var dx = tile.X - anchor.X;
            if (snapshot.WrapsEastWest && Math.Abs(dx) > width / 2) dx -= Math.Sign(dx) * width;
            sum += new Vector2(anchor.X + dx, tile.Y);
        }
        CenterCameraAt(sum / town.BorderTiles.Count + new Vector2(0.5f, 0.5f));
    }
}
