using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void BuildLayout()
    {
        AddThemeFontSizeOverride("font_size", UiFonts.Body);

        appBackdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
        appBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(appBackdrop);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        BuildWorldColumn(root);
        BuildTopBar(uiLayer);
        BuildFounderSetupPanel(uiLayer);
        BuildInspectorColumn(uiLayer);
        BuildOwnerColumn(uiLayer);
        BuildDeveloperTools(uiLayer);
        FitFloatingPanelsToContents();
        BuildStatusToast(uiLayer);
        AddChild(menuLayer);
        BuildMainMenu();
        BuildManualSavesPanel();

        // A smaller window can lower the UI Scale that fits, as well as re-lay the panels.
        Resized += ApplyUiScale;
        foreach (var panel in HudPanels())
        {
            panel.VisibilityChanged += PlaceHudPanels;
            panel.Resized += PlaceHudPanels;
        }
        ApplyResponsiveLayout();
    }

    private void BuildConnectionPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        worldUrlInput.PlaceholderText = "https://your-tailnet-host:8443";
        worldUrlInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        controls.AddChild(worldUrlInput);
        connectButton.Text = "Connect";
        connectButton.Pressed += () => _ = ConnectUsingCurrentUrlAsync();
        controls.AddChild(connectButton);
        pairAgainButton.Text = "Pair again";
        pairAgainButton.TooltipText = "Forget this device's saved connection and connect again.";
        pairAgainButton.Visible = false;
        pairAgainButton.Pressed += () => _ = PairAgainAsync();
        controls.AddChild(pairAgainButton);
        body.AddChild(controls);
        connectionStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        connectionStatusLabel.Hide();
        body.AddChild(connectionStatusLabel);
        AddPanelContents(connectionPanel, "World connection", body);
        connectionPanel.ThemeTypeVariation = "InsetPanel";
    }

    private void ShowSettingsSection(bool worldSpecific)
    {
        if (worldSpecific && (!isInWorld || returnToMainMenu)) return;
        CloseAgentModelEditor();
        modLibraryPanel.Hide();
        settingsPanel.Show();
        // Each category opens at its top rather than where the other was scrolled.
        settingsScroll.ScrollVertical = 0;
        gameSettingsContent.Visible = !worldSpecific;
        worldSettingsContent.Visible = worldSpecific;
        SelectSettingsCategory(worldSpecific ? worldSettingsCategoryButton : gameSettingsCategoryButton);
        settingsScroll.Show();
        if (worldSpecific && registration is not null)
        {
            _ = RefreshWorldSettingsAsync();
        }
        if (!worldSpecific)
        {
            _ = RefreshApiKeysAsync();
            _ = RefreshUsageAsync();
        }

        ShowPauseMenuPage("Settings");
        ApplyResponsiveLayout();
    }

    private async Task RefreshWorldSettingsAsync()
    {
        await RefreshAutosaveSettingsAsync();
        await RefreshProviderConfigurationAsync();
    }

    private void BuildPairingPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        var heading = new Label { Text = "Pair this Windows device", ThemeTypeVariation = "HeadingLabel" };
        body.AddChild(heading);
        pairingInstructionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(pairingInstructionLabel);
        var codeRow = new HBoxContainer();
        codeRow.AddChild(new Label { Text = "comparison code:" });
        pairingCodeLabel.AddThemeFontSizeOverride("font_size", UiFonts.Body * 2);
        codeRow.AddChild(pairingCodeLabel);
        body.AddChild(codeRow);
        var idRow = new HBoxContainer();
        idRow.AddChild(new Label { Text = "pairing ID:" });
        pairingIdLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        idRow.AddChild(pairingIdLabel);
        body.AddChild(idRow);
        body.AddChild(pairingExpiryLabel);
        var buttons = new HBoxContainer();
        pairButton.Text = "Start pairing";
        pairButton.Pressed += () => _ = StartPairingAsync();
        buttons.AddChild(pairButton);
        forgetRegistrationButton.Text = "Forget local registration";
        forgetRegistrationButton.Pressed += ForgetLocalRegistration;
        buttons.AddChild(forgetRegistrationButton);
        body.AddChild(buttons);
        AddPanelContents(pairingPanel, body);
        pairingPanel.ThemeTypeVariation = "InsetPanel";
        pairingPanel.Hide();
    }

    private void BuildWorldColumn(Control content)
    {
        mapCanvas.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mapCanvas.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        mapCanvas.ClipContents = true;

        worldBackdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
        worldBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        mapCanvas.AddChild(worldBackdrop);

        mapStage.MouseFilter = Control.MouseFilterEnum.Ignore;
        mapCanvas.AddChild(mapStage);
        mapCanvas.AddChild(uiLayer);

        mapStage.AddChild(terrainLayer);

        objectLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        objectLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        mapStage.AddChild(objectLayer);

        // Developer tools draw an agent's planned path between objects and agents.
        plannedPathLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        mapStage.AddChild(plannedPathLayer);

        entityLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        entityLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        mapStage.AddChild(entityLayer);

        // Weather falls over buildings and agents alike.
        weatherLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        weatherLayer.CloudsEnabled = displayPreferences.CloudHaze;
        weatherLayer.LightningEnabled = displayPreferences.LightningFlashes;
        weatherLayer.Follow(terrainLayer);
        mapStage.AddChild(weatherLayer);

        BuildAgentCards();
        BuildBuildingCards();

        worldOverview.CenterRequested += CenterCameraAt;
        var overviewBody = new VBoxContainer();
        overviewBody.AddThemeConstantOverride("separation", 6);
        overviewBody.AddChild(worldOverview);
        overviewBody.AddChild(OverviewLegend());
        AddClosablePanelContents(worldOverviewPanel, "World Map", overviewBody);
        worldOverviewPanel.Position = new Vector2(14, 14);
        worldOverviewPanel.ZIndex = 80;
        worldOverviewPanel.Hide();
        uiLayer.AddChild(worldOverviewPanel);
        mapCanvas.GuiInput += HandleMapInput;
        mapCanvas.MouseExited += () =>
        {
            terrainLayer.SetHoveredTile(null);
            UpdateHoverReadout(null, null);
            if (placingAddedAgent) ResetAddAgentPlacementHint();
        };
        content.AddChild(mapCanvas);
    }

    private void BuildInspectorColumn(Control content)
    {
        BuildMapFiltersPanel(content);
        var rosterBody = new VBoxContainer();
        rosterBody.AddThemeConstantOverride("separation", 6);
        rosterSummaryLabel.Text = "Waiting for the world…";
        rosterSummaryLabel.ThemeTypeVariation = "DimLabel";
        rosterSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rosterBody.AddChild(rosterSummaryLabel);

        inhabitantList.CustomMinimumSize = new Vector2(380, 40);
        inhabitantList.ItemSelected += index => SelectInhabitantFromList(index);
        inhabitantList.TooltipText = "Choose someone to find them in the world.";
        rosterBody.AddChild(inhabitantList);
        BuildRosterCards(rosterBody);
        AddClosablePanelContents(rosterPanel, "Agents", rosterBody);
        rosterPanel.CustomMinimumSize = new Vector2(410, 0);
        rosterPanel.ZIndex = 80;
        rosterPanel.Hide();
        content.AddChild(rosterPanel);

        ConfigureTextPanel(eventLog, 300);
        eventLog.MetaClicked += meta => _ = HandleEventLogActionAsync(meta.AsString());
        eventLog.TooltipText = "Click a located event to jump to where it happened.";
        var eventsBody = new VBoxContainer();
        eventLog.Hide();
        eventsBody.AddChild(eventLog);
        BuildEventRows(eventsBody);
        AddClosablePanelContents(eventsPanel, "Event Log", eventsBody);
        eventsPanel.CustomMinimumSize = new Vector2(430, 0);
        eventsPanel.ZIndex = 80;
        eventsPanel.Hide();
        content.AddChild(eventsPanel);

        var familyBody = new VBoxContainer();
        var familyHeading = new HBoxContainer();
        var familyTitle = new Label { Text = "Family Tree", ThemeTypeVariation = "HeadingLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        familyHeading.AddChild(familyTitle);
        var closeFamily = CloseButton("Close family tree");
        closeFamily.Pressed += () => familyTreePanel.Hide();
        familyHeading.AddChild(closeFamily);
        familyBody.AddChild(familyHeading);
        familyTreeStatus.Text = "Green: parent–child   ·   Pink: partnership   ·   Click a person to inspect";
        familyBody.AddChild(familyTreeStatus);
        familyTreeScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        familyTreeScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        familyTreeScroll.AddChild(familyTreeView);
        familyBody.AddChild(familyTreeScroll);
        familyTreeView.PersonRequested += SelectFromFamilyTree;
        AddPanelContents(familyTreePanel, familyBody);
        familyTreePanel.ZIndex = 85;
        familyTreePanel.Hide();
        content.AddChild(familyTreePanel);

        var memoriesBody = new VBoxContainer();
        var memoriesHeading = new HBoxContainer();
        var memoriesTitle = new Label { Text = "Memories and maps", ThemeTypeVariation = "HeadingLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        memoriesHeading.AddChild(memoriesTitle);
        var closeMemories = CloseButton("Close memories");
        closeMemories.Pressed += () => memoriesPanel.Hide();
        memoriesHeading.AddChild(closeMemories);
        memoriesBody.AddChild(memoriesHeading);
        ConfigureTextPanel(memoryHistory, 360);
        memoryHistory.TooltipText = "What this agent remembers and believes, plus the maps they know. This is their view, not the full world log.";
        memoriesBody.AddChild(memoryHistory);
        AddPanelContents(memoriesPanel, memoriesBody);
        memoriesPanel.ZIndex = 85;
        memoriesPanel.Resized += () => PlaceReaderPanel(memoriesPanel);
        memoriesPanel.Hide();
        content.AddChild(memoriesPanel);

        BuildWorldInfoPanel(content);
        BuildControlsPanel(content);

        BuildTileCard(content);
        BuildMapHud(content);
    }

    private void BuildOwnerColumn(Control content)
    {
        menuShade.Color = UiTheme.Current.Shade;
        menuShade.MouseFilter = Control.MouseFilterEnum.Stop;
        menuShade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        menuShade.ZIndex = 90;
        menuShade.Hide();
        menuShade.VisibilityChanged += () => topBarShade.Visible = menuShade.Visible;
        content.AddChild(menuShade);

        var body = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(360, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 8);

        var menuHeading = new HBoxContainer();
        menuHeadingLabel.Text = "Paused";
        menuHeadingLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        menuHeadingLabel.ThemeTypeVariation = "TitleLabel";
        menuHeading.AddChild(menuHeadingLabel);
        StyleIconButton(menuCloseButton, PixelGlyph.Close);
        menuCloseButton.TooltipText = "Return to the world";
        menuCloseButton.Pressed += () =>
        {
            if (PauseMenuPageOpen) ShowPauseMenuButtons();
            else _ = CloseGameMenuAsync();
        };
        menuHeading.AddChild(menuCloseButton);
        body.AddChild(menuHeading);

        menuActions.AddThemeConstantOverride("separation", 6);
        menuResumeButton.Text = "Resume";
        StyleMenuChoice(menuResumeButton, primary: true);
        menuResumeButton.Pressed += () => _ = CloseGameMenuAsync();
        menuResumeButton.Hide();
        menuActions.AddChild(menuResumeButton);

        menuSaveWorldButton.Text = "Save World";
        StyleMenuChoice(menuSaveWorldButton);
        menuSaveWorldButton.Pressed += () => _ = OpenManualSavesAsync(loadMode: false);
        menuActions.AddChild(menuSaveWorldButton);

        settingsButton.Text = "Settings";
        StyleMenuChoice(settingsButton);
        settingsButton.Pressed += () => ShowSettingsSection(worldSpecific: false);
        menuActions.AddChild(settingsButton);

        modLibraryButton.Text = "Mod Library";
        StyleMenuChoice(modLibraryButton);
        modLibraryButton.Pressed += ShowModLibrary;
        menuActions.AddChild(modLibraryButton);

        menuActions.AddChild(menuQuitSeparator);
        menuQuitToMainButton.Text = "Quit to Menu";
        StyleMenuChoice(menuQuitToMainButton);
        menuQuitToMainButton.Pressed += () => PopupDialog(quitToMenuConfirmation);
        menuActions.AddChild(menuQuitToMainButton);

        StyleConfirmation(quitGameConfirmation, "Quit ClankerWorld?", "Quit Game");
        // Quit Game is only offered on the Main Menu, after leaving any world.
        // The title says it all, so the dialog needs no sentence beneath it.
        quitGameConfirmation.Confirmed += () => GetTree().Quit();
        AddChild(quitGameConfirmation);
        body.AddChild(menuActions);

        gameSettingsContent.AddThemeConstantOverride("separation", 8);
        worldSettingsContent.AddThemeConstantOverride("separation", 8);
        themeChoice.AddItem("Light", (int)UiThemeChoice.Light);
        themeChoice.AddItem("Dark", (int)UiThemeChoice.Dark);
        themeChoice.AddItem("Match system", (int)UiThemeChoice.System);
        themeChoice.Selected = (int)UiTheme.Parse(displayPreferences.Theme);
        themeChoice.TooltipText = "Light parchment or dark wood panels. Match system follows your computer's setting.";
        themeChoice.ItemSelected += SetUiTheme;

        gameSettingsContent.AddChild(SettingsBox("Interface", DisplaySettingRow("Theme", themeChoice)));

        fullscreenToggle.Text = string.Empty;
        fullscreenToggle.TooltipText = "Fill the whole screen.";
        fullscreenToggle.ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
        fullscreenToggle.Toggled += SetFullscreen;
        fullscreenToggle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;

        foreach (var preset in DisplaySizePresets)
        {
            var label = $"{preset.X} × {preset.Y}";
            windowSizeChoice.AddItem(label);
        }
        windowSizeChoice.Selected = DisplaySizeIndex(GetWindow().Size);
        windowSizeChoice.Disabled = fullscreenToggle.ButtonPressed;
        windowSizeChoice.TooltipText = "Size of the game window. Fullscreen uses your screen's size.";
        windowSizeChoice.ItemSelected += SetWindowSize;
        gameSettingsContent.AddChild(SettingsBox("Display",
            DisplaySettingRow("Fullscreen", fullscreenToggle), DisplaySettingRow("Window Size", windowSizeChoice)));

        cloudHazeToggle.TooltipText = "A faint haze of cloud that drifts over the land now and then.";
        cloudHazeToggle.ButtonPressed = displayPreferences.CloudHaze;
        cloudHazeToggle.Toggled += SetCloudHaze;
        lightningToggle.TooltipText = "A soft flash every several seconds during storms.";
        lightningToggle.ButtonPressed = displayPreferences.LightningFlashes;
        lightningToggle.Toggled += SetLightningFlashes;
        gameSettingsContent.AddChild(SettingsBox("Weather",
            DisplaySettingRow("Cloud haze", cloudHazeToggle), DisplaySettingRow("Lightning flashes", lightningToggle)));

        clockFormatChoice.AddItem("24-hour", 0);
        clockFormatChoice.AddItem("12-hour (AM/PM)", 1);
        clockFormatChoice.Selected = displayPreferences.UseTwelveHourClock ? 1 : 0;
        clockFormatChoice.ItemSelected += SetClockFormat;

        foreach (var (_, label) in DateStyles)
            dateFormatChoice.AddItem(label);
        dateFormatChoice.Selected = DateStyleIndex(displayPreferences.DateStyle);
        dateFormatChoice.TooltipText = "Show dates by season and day, or as numbers in the order you prefer.";
        dateFormatChoice.ItemSelected += SetDateStyle;
        gameSettingsContent.AddChild(SettingsBox("Date and time",
            DisplaySettingRow("Time display", clockFormatChoice), DisplaySettingRow("Date display", dateFormatChoice)));

        BuildAutosaveSettings();

        jevAssistanceToggle.Text = "Let Jev help in this world";
        jevAssistanceToggle.TooltipText = "Jev is an optional helper for small everyday choices, so your agents' own models are called less. Turn it off and nothing is lost. Memories and keys stay.";
        jevAssistanceToggle.Toggled += enabled => _ = SaveJevAssistanceAsync(enabled);
        worldSettingsContent.AddChild(SettingsBox("Jev", jevAssistanceToggle));

        BuildCognitionSettingsPanel();
        worldSettingsContent.AddChild(cognitionSettingsPanel);

        BuildApiKeysPanel();
        // One call count and limit covers every world, so it is a Game setting.
        BuildUsageLimitPanel();
        gameSettingsContent.AddChild(usageLimitPanel);
        BuildConnectionPanel();
        gameSettingsContent.AddChild(connectionPanel);
        BuildPairingPanel();
        gameSettingsContent.AddChild(pairingPanel);
        var settingsPages = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        settingsPages.AddChild(gameSettingsContent);
        settingsPages.AddChild(worldSettingsContent);
        worldSettingsContent.Hide();
        settingsScroll.CustomMinimumSize = new Vector2(0, 340);
        settingsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        settingsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        settingsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        // Keep the scrollbar clear of the boxes' edges and drop-downs.
        var scrollGap = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scrollGap.AddThemeConstantOverride("margin_right", SettingsScrollGap);
        scrollGap.AddChild(settingsPages);
        settingsScroll.AddChild(scrollGap);
        var settingsCategories = new VBoxContainer { CustomMinimumSize = new Vector2(130, 0) };
        gameSettingsCategoryButton.Text = "Game";
        StyleSettingsCategoryButton(gameSettingsCategoryButton);
        gameSettingsCategoryButton.Pressed += () => ShowSettingsSection(worldSpecific: false);
        settingsCategories.AddChild(gameSettingsCategoryButton);
        worldSettingsCategoryButton.Text = "World";
        StyleSettingsCategoryButton(worldSettingsCategoryButton);
        worldSettingsCategoryButton.Pressed += () => ShowSettingsSection(worldSpecific: true);
        settingsCategories.AddChild(worldSettingsCategoryButton);
        SelectSettingsCategory(gameSettingsCategoryButton);
        var settingsLayout = new HBoxContainer();
        settingsLayout.AddThemeConstantOverride("separation", 10);
        settingsLayout.AddChild(settingsCategories);
        settingsLayout.AddChild(settingsScroll);
        // The menu's own heading names the page, so the panel repeats no title.
        AddPanelContents(settingsPanel, settingsLayout);
        // Settings sits inside the menu panel, so it reads as a section of it.
        settingsPanel.ThemeTypeVariation = "InsetPanel";
        settingsPanel.Hide();
        body.AddChild(settingsPanel);
        BuildModLibrary(body);

        AddPanelContents(gameMenuPanel, body);
        gameMenuPanel.ZIndex = 100;
        gameMenuPanel.Hide();
        var menuCenter = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 100,
        };
        menuCenter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(menuCenter);
        menuCenter.AddChild(gameMenuPanel);
    }

    private void BuildStatusToast(Control content)
    {
        statusLabel.Text = "Connecting…";
        statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statusLabel.CustomMinimumSize = new Vector2(320, 0);
        AddPanelContents(statusToast, statusLabel);
        // Above the title backdrop and menus, so connection and pairing
        // results remain visible from Main Menu Settings.
        statusToast.ZIndex = 250;
        // Messages now stay up for several seconds; let clicks reach the map.
        statusToast.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in statusToast.FindChildren("*", nameof(Control), recursive: true, owned: false).OfType<Control>())
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
        statusToast.Hide();
        content.AddChild(statusToast);
    }

    private string SelectedAuthoringKind() => authoringKind.GetItemText(authoringKind.Selected);

    private static string? EmptyToNull(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string Positions(IReadOnlyList<OwnerWorldPosition> positions) => positions.Count == 0
        ? "none"
        : string.Join(", ", positions.Select(position => $"{position.X},{position.Y}"));

    // RichTextLabel ignores assigning its current non-empty text, even after
    // Clear() emptied the display, so text panels are only ever replaced.
    // Skipping unchanged text also keeps the reader's scroll position.
    private void SetPanelText(RichTextLabel label, string text)
    {
        if (label.Text == text) return;
        label.Text = text;
        FitTextPanel(label);
    }

    private const string TextPanelLimit = "text_panel_limit";

    /// <summary>A text panel as tall as its text, up to <paramref name="maximumHeight"/>; longer text scrolls.</summary>
    private void ConfigureTextPanel(RichTextLabel label, float maximumHeight)
    {
        label.BbcodeEnabled = false;
        label.FitContent = false;
        label.ScrollActive = true;
        label.SetMeta(TextPanelLimit, maximumHeight);
        // A new width rewraps the text, but only after the resized signal, so
        // measure it once this frame's layout is done.
        label.Resized += () => Callable.From(() => FitTextPanel(label)).CallDeferred();
    }

    /// <summary>
    /// Sizes a text panel to its text, so short text leaves no empty space. A
    /// floating panel around it stays between the top bar and the bottom of
    /// the screen; the text scrolls for the rest.
    /// </summary>
    private void FitTextPanel(RichTextLabel label)
    {
        if (label.Size.X < 1 || !label.HasMeta(TextPanelLimit)) return;
        var height = Math.Min((float)label.GetMeta(TextPanelLimit), label.GetContentHeight());
        var panel = FloatingPanel(label);
        if (panel is not null)
        {
            var rest = panel.GetCombinedMinimumSize().Y - label.CustomMinimumSize.Y;
            height = Math.Min(height, UiSize.Y - HudTop - 12 - rest);
        }
        // Text that has to scroll shows whole lines rather than a sliced last one.
        var line = label.GetThemeFont("normal_font").GetHeight(label.GetThemeFontSize("normal_font_size")) +
            label.GetThemeConstant("line_separation");
        var minimum = (float)UiFonts.Body * 2;
        if (height < label.GetContentHeight() && line > 0)
        {
            height = Math.Max(line, Mathf.Floor(height / line) * line);
            minimum = line;
        }
        height = Mathf.Ceil(Math.Max(minimum, height));
        if (Math.Abs(label.CustomMinimumSize.Y - height) >= 1)
            label.CustomMinimumSize = new Vector2(label.CustomMinimumSize.X, height);
    }

    /// <summary>
    /// Panels placed by hand grow with their contents but never shrink by
    /// themselves; these follow their contents both ways.
    /// </summary>
    private void FitFloatingPanelsToContents()
    {
        foreach (var panel in HudPanels().Append(memoriesPanel).Append(thoughtsPanel).Append(conversationPanel).Append(selectedTilePanel).Append(agentProfilePanel)
                     .Append(buildingQuickCard).Append(buildingDetailsPanel))
            panel.MinimumSizeChanged += () => panel.Size = panel.GetCombinedMinimumSize();
    }

    /// <summary>
    /// The panel a text panel floats in, placed by hand rather than by a
    /// container. Text inside a scrolling area is left to that area.
    /// </summary>
    private static PanelContainer? FloatingPanel(Control control)
    {
        for (var node = control.GetParent(); node is Control parent; node = parent.GetParent())
        {
            if (parent is ScrollContainer) return null;
            if (parent is PanelContainer panel && panel.GetParent() is not Container) return panel;
        }
        return null;
    }

    private static PanelContainer NewPanel(string title, Control content)
    {
        // Always nested inside another panel, so it reads as a section.
        var panel = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
        AddPanelContents(panel, title, content);
        return panel;
    }

    private static void AddPanelContents(PanelContainer panel, Control content) => AddPanelContents(panel, string.Empty, content);

    /// <summary>
    /// A titled world panel whose heading carries the close button. It hides
    /// the panel, like Escape does, unless the panel needs its own way to close.
    /// </summary>
    private static void AddClosablePanelContents(PanelContainer panel, string title, Control content, Action? close = null) =>
        AddPanelContents(panel, title, content, closable: true, close);

    private static void AddPanelContents(PanelContainer panel, string title, Control content, bool closable = false, Action? onClose = null)
    {
        // A titled box inside a menu already has its inset frame's padding.
        var pad = closable || string.IsNullOrWhiteSpace(title) ? 10 : 4;
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", pad);
        margin.AddThemeConstantOverride("margin_right", pad);
        margin.AddThemeConstantOverride("margin_top", pad);
        margin.AddThemeConstantOverride("margin_bottom", pad);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 7);
        if (!string.IsNullOrWhiteSpace(title) && closable)
        {
            var headingRow = new HBoxContainer();
            var heading = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            heading.ThemeTypeVariation = "HeadingLabel";
            headingRow.AddChild(heading);
            var close = CloseButton($"Close {title}");
            close.Pressed += () =>
            {
                close.ReleaseFocus();
                if (onClose is null) panel.Hide();
                else onClose();
            };
            headingRow.AddChild(close);
            body.AddChild(headingRow);
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            // A box inside a menu, such as API keys, takes a small section label
            // so several fit on screen together.
            var heading = new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" };
            body.AddChild(heading);
        }

        body.AddChild(content);
        margin.AddChild(body);
        panel.AddChild(margin);
    }

    private static HBoxContainer MetricRow(string caption, Label value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = new Label
        {
            Text = caption,
            CustomMinimumSize = new Vector2(82, 0),
        };
        label.ThemeTypeVariation = "DimLabel";
        row.AddChild(label);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(value);
        return row;
    }

    /// <summary>
    /// The one close button: a small square with the pixel × at the right end
    /// of a panel's heading. A screen reached from another shows the back
    /// chevron in the same place instead.
    /// </summary>
    private static Button CloseButton(string tooltip)
    {
        var button = new Button { TooltipText = tooltip };
        StyleIconButton(button, PixelGlyph.Close);
        return button;
    }

    /// <summary>A square button showing one pixel glyph, drawn in the theme's ink.</summary>
    private static void StyleIconButton(Button button, PixelGlyph glyph)
    {
        button.Text = string.Empty;
        button.ThemeTypeVariation = "IconButton";
        button.Icon = PixelIcons.Texture(glyph, Colors.White, Colors.White, 1);
        button.IconAlignment = HorizontalAlignment.Center;
        button.CustomMinimumSize = Vector2.Zero;
        // Stay square beside a tall title instead of stretching to its height.
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
    }

    /// <summary>
    /// A choice in the Main Menu or Pause Menu: full width, with an icon and
    /// the Timber heading lettering, so both menus read alike.
    /// </summary>
    private static void StyleMenuChoice(Button button, bool primary = false)
    {
        StyleButton(button, primary);
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeFontOverride("font", UiFonts.Headings);
        button.AddThemeFontSizeOverride("font_size", UiFonts.Heading);
    }

    /// <summary>Buttons take their look from the current theme; primary ones are the green action.</summary>
    private static void StyleButton(Button button, bool primary = false)
    {
        button.CustomMinimumSize = new Vector2(0, 34);
        button.ThemeTypeVariation = primary ? "PrimaryButton" : string.Empty;
    }

    // Settings categories are tabs: the open one reads as selected instead of
    // looking disabled, and pressing it again simply keeps it open.
    private static void StyleSettingsCategoryButton(Button button)
    {
        StyleButton(button);
        button.ToggleMode = true;
        button.ThemeTypeVariation = "TabButton";
    }

    private void SelectSettingsCategory(Button selected)
    {
        foreach (var button in new[] { gameSettingsCategoryButton, worldSettingsCategoryButton })
            button.SetPressedNoSignal(button == selected);
    }

}
