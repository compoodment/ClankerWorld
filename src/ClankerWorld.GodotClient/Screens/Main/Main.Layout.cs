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
        AddThemeFontSizeOverride("font_size", 14);

        appBackdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
        appBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(appBackdrop);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        BuildWorldColumn(root);
        BuildTopBar(mapCanvas);
        BuildFounderSetupPanel(mapCanvas);
        BuildInspectorColumn(mapCanvas);
        BuildOwnerColumn(mapCanvas);
        BuildStatusToast(mapCanvas);
        BuildMainMenu();
        BuildManualSavesPanel();

        Resized += ApplyResponsiveLayout;
        ApplyResponsiveLayout();
    }

    private void BuildConnectionPanel()
    {
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        worldUrlInput.PlaceholderText = "https://your-tailnet-host:8443";
        worldUrlInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.AddChild(worldUrlInput);
        connectButton.Text = "Connect";
        connectButton.Pressed += () => _ = ConnectUsingCurrentUrlAsync();
        body.AddChild(connectButton);
        pairAgainButton.Text = "Pair again";
        pairAgainButton.TooltipText = "Forget this device's saved connection and connect again.";
        pairAgainButton.Visible = false;
        pairAgainButton.Pressed += () => _ = PairAgainAsync();
        body.AddChild(pairAgainButton);
        AddPanelContents(connectionPanel, "World connection", body);
        connectionPanel.ThemeTypeVariation = "InsetPanel";
    }

    private void ShowSettingsSection(bool worldSpecific)
    {
        if (worldSpecific && (!isInWorld || returnToMainMenu)) return;
        CloseAgentModelEditor();
        modLibraryPanel.Hide();
        settingsPanel.Show();
        gameSettingsContent.Visible = !worldSpecific;
        worldSettingsContent.Visible = worldSpecific;
        if (!worldSpecific && renderResolutionChoice.ItemCount > 0)
            RefreshRenderResolutionOptions();
        SelectSettingsCategory(worldSpecific ? worldSettingsCategoryButton : gameSettingsCategoryButton);
        settingsScroll.Show();
        developerScroll.Hide();
        developerToggleButton.Text = "Developer tools";
        if (worldSpecific && registration is not null)
        {
            _ = RefreshWorldSettingsAsync();
        }

        ApplyResponsiveLayout();
    }

    private async Task RefreshWorldSettingsAsync()
    {
        await RefreshProviderConfigurationAsync();
        await RefreshUsageAsync();
        await RefreshAutosaveSettingsAsync();
    }

    private void BuildPairingPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        var heading = new Label { Text = "PAIR THIS WINDOWS DEVICE" };
        heading.AddThemeFontSizeOverride("font_size", 16);
        body.AddChild(heading);
        pairingInstructionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(pairingInstructionLabel);
        var codeRow = new HBoxContainer();
        codeRow.AddChild(new Label { Text = "comparison code:" });
        pairingCodeLabel.AddThemeFontSizeOverride("font_size", 22);
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

        mapStage.AddChild(terrainLayer);

        objectLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        objectLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        mapStage.AddChild(objectLayer);

        entityLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        entityLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        mapStage.AddChild(entityLayer);

        // Weather falls over buildings and agents alike.
        weatherLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        weatherLayer.CloudsEnabled = displayPreferences.CloudHaze;
        weatherLayer.LightningEnabled = displayPreferences.LightningFlashes;
        weatherLayer.Follow(terrainLayer);
        mapStage.AddChild(weatherLayer);

        BuildSelectedInhabitantCard();
        mapCanvas.AddChild(selectedInhabitantCard);

        worldOverview.CenterRequested += CenterCameraAt;
        AddClosablePanelContents(worldOverviewPanel, "World Map", worldOverview);
        worldOverviewPanel.Position = new Vector2(14, 14);
        worldOverviewPanel.ZIndex = 80;
        worldOverviewPanel.Hide();
        mapCanvas.AddChild(worldOverviewPanel);
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
        AddClosablePanelContents(rosterPanel, "Agents", rosterBody);
        rosterPanel.CustomMinimumSize = new Vector2(410, 0);
        rosterPanel.ZIndex = 80;
        rosterPanel.Hide();
        content.AddChild(rosterPanel);

        ConfigureTextPanel(eventLog, 300);
        eventLog.MetaClicked += meta => JumpToEvent(meta.AsString());
        eventLog.TooltipText = "Click a located event to jump to where it happened.";
        AddClosablePanelContents(eventsPanel, "Event Log", eventLog);
        eventsPanel.CustomMinimumSize = new Vector2(390, 360);
        eventsPanel.ZIndex = 80;
        eventsPanel.Hide();
        content.AddChild(eventsPanel);

        var familyBody = new VBoxContainer();
        var familyHeading = new HBoxContainer();
        var familyTitle = new Label { Text = "Family Tree", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        familyTitle.AddThemeFontSizeOverride("font_size", 18);
        familyHeading.AddChild(familyTitle);
        var closeFamily = new Button { Text = "×", TooltipText = "Close family tree" };
        StyleButton(closeFamily);
        closeFamily.Pressed += () => familyTreePanel.Hide();
        familyHeading.AddChild(closeFamily);
        familyBody.AddChild(familyHeading);
        familyTreeStatus.Text = "Green: parent–child   ·   Pink: partnership   ·   Click a person to inspect";
        familyBody.AddChild(familyTreeStatus);
        var familyScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        familyScroll.AddChild(familyTreeView);
        familyBody.AddChild(familyScroll);
        familyTreeView.PersonRequested += SelectFromFamilyTree;
        AddPanelContents(familyTreePanel, familyBody);
        familyTreePanel.ZIndex = 85;
        familyTreePanel.Hide();
        content.AddChild(familyTreePanel);

        var memoriesBody = new VBoxContainer();
        var memoriesHeading = new HBoxContainer();
        var memoriesTitle = new Label { Text = "Memories and maps", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        memoriesTitle.AddThemeFontSizeOverride("font_size", 18);
        memoriesHeading.AddChild(memoriesTitle);
        var closeMemories = new Button { Text = "×", TooltipText = "Close memories" };
        StyleButton(closeMemories);
        closeMemories.Pressed += () => memoriesPanel.Hide();
        memoriesHeading.AddChild(closeMemories);
        memoriesBody.AddChild(memoriesHeading);
        ConfigureTextPanel(memoryHistory, 300);
        memoryHistory.TooltipText = "What this agent remembers and believes, plus the maps they know. This is their view, not the full world log.";
        memoriesBody.AddChild(memoryHistory);
        AddPanelContents(memoriesPanel, memoriesBody);
        memoriesPanel.ZIndex = 85;
        memoriesPanel.Hide();
        content.AddChild(memoriesPanel);

        BuildWorldInfoPanel(content);
        BuildControlsPanel(content);

        var tileBody = new VBoxContainer();
        var tileHeading = new HBoxContainer();
        tileHeading.AddChild(new Label { Text = "Selected tile", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var closeTile = new Button { Text = "×", TooltipText = "Close tile inspection" };
        StyleButton(closeTile);
        closeTile.Pressed += ClearTileSelection;
        tileHeading.AddChild(closeTile);
        tileBody.AddChild(tileHeading);
        ConfigureTextPanel(selectedTileText, 64);
        selectedTileText.Resized += FitSelectedTileText;
        tileBody.AddChild(selectedTileText);
        AddPanelContents(selectedTilePanel, tileBody);
        selectedTilePanel.CustomMinimumSize = new Vector2(315, 0);
        selectedTilePanel.Resized += PositionSelectedTilePanel;
        selectedTilePanel.ZIndex = 80;
        selectedTilePanel.Hide();
        content.AddChild(selectedTilePanel);
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
        menuHeadingLabel.AddThemeFontSizeOverride("font_size", 24);
        menuHeadingLabel.ThemeTypeVariation = "HeadingLabel";
        menuHeading.AddChild(menuHeadingLabel);
        menuCloseButton.Text = "×";
        menuCloseButton.TooltipText = "Return to the world";
        StyleButton(menuCloseButton);
        menuCloseButton.Pressed += () => _ = CloseGameMenuAsync();
        menuHeading.AddChild(menuCloseButton);
        body.AddChild(menuHeading);

        var menuActions = new VBoxContainer();
        menuActions.AddThemeConstantOverride("separation", 6);
        menuResumeButton.Text = "Resume";
        StyleButton(menuResumeButton, primary: true);
        menuResumeButton.Pressed += () => _ = CloseGameMenuAsync();
        menuResumeButton.Hide();
        menuActions.AddChild(menuResumeButton);

        menuSaveWorldButton.Text = "Save World";
        StyleButton(menuSaveWorldButton);
        menuSaveWorldButton.Pressed += () => _ = OpenManualSavesAsync(loadMode: false);
        menuActions.AddChild(menuSaveWorldButton);

        settingsButton.Text = "Settings";
        StyleButton(settingsButton);
        settingsButton.Pressed += () => ShowSettingsSection(worldSpecific: false);
        menuActions.AddChild(settingsButton);

        modLibraryButton.Text = "Mod Library";
        StyleButton(modLibraryButton);
        modLibraryButton.Pressed += ShowModLibrary;
        menuActions.AddChild(modLibraryButton);

        developerToggleButton.Text = "Developer tools";
        StyleSettingsCategoryButton(developerToggleButton);
        developerToggleButton.Pressed += () =>
        {
            settingsScroll.Hide();
            developerScroll.Show();
            SelectSettingsCategory(developerToggleButton);
            ApplyResponsiveLayout();
        };

        menuActions.AddChild(menuQuitSeparator);
        menuQuitToMainButton.Text = "Quit to Menu";
        StyleButton(menuQuitToMainButton);
        menuQuitToMainButton.Pressed += () => quitToMenuConfirmation.PopupCentered(new Vector2I(470, 180));
        menuActions.AddChild(menuQuitToMainButton);

        StyleConfirmation(quitGameConfirmation, "Quit ClankerWorld?", "Quit Game");
        quitGameConfirmation.DialogText = "Quit the game? Your progress is saved.";
        quitGameConfirmation.Confirmed += () => GetTree().Quit();
        AddChild(quitGameConfirmation);
        body.AddChild(menuActions);

        gameSettingsContent.AddThemeConstantOverride("separation", 8);
        worldSettingsContent.AddThemeConstantOverride("separation", 8);
        gameSettingsContent.AddChild(SettingsSection("Interface"));
        themeChoice.AddItem("Light", (int)UiThemeChoice.Light);
        themeChoice.AddItem("Dark", (int)UiThemeChoice.Dark);
        themeChoice.AddItem("Match system", (int)UiThemeChoice.System);
        themeChoice.Selected = (int)UiTheme.Parse(displayPreferences.Theme);
        themeChoice.TooltipText = "Light parchment or dark wood panels. Match system follows your computer's setting.";
        themeChoice.ItemSelected += SetUiTheme;
        gameSettingsContent.AddChild(DisplaySettingRow("Theme", themeChoice));

        foreach (var percentage in DisplayUiScalePolicy.SupportedPercentages)
            uiScaleChoice.AddItem($"{percentage}%");
        uiScaleChoice.Selected = DisplayUiScalePolicy.IndexOfPercent(displayPreferences.UiScalePercent);
        uiScaleChoice.TooltipText = "Makes menus and text bigger or smaller.";
        uiScaleChoice.ItemSelected += SetUiScale;
        gameSettingsContent.AddChild(DisplaySettingRow("UI Scale", uiScaleChoice));

        gameSettingsContent.AddChild(SettingsSection("Display"));
        fullscreenToggle.Text = string.Empty;
        fullscreenToggle.TooltipText = "Fill the whole screen.";
        fullscreenToggle.ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
        fullscreenToggle.Toggled += SetFullscreen;
        fullscreenToggle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        gameSettingsContent.AddChild(DisplaySettingRow("Fullscreen", fullscreenToggle));

        foreach (var preset in DisplaySizePresets)
        {
            var label = $"{preset.X} × {preset.Y}";
            windowSizeChoice.AddItem(label);
        }
        windowSizeChoice.Selected = DisplaySizeIndex(GetWindow().Size);
        windowSizeChoice.Disabled = fullscreenToggle.ButtonPressed;
        windowSizeChoice.TooltipText = "Size of the game window. Fullscreen uses your screen's size.";
        windowSizeChoice.ItemSelected += SetWindowSize;
        gameSettingsContent.AddChild(DisplaySettingRow("Window Size", windowSizeChoice));
        RefreshRenderResolutionOptions();
        renderResolutionChoice.TooltipText = "How sharp the picture is. Automatic matches your window or screen. Fixed sizes are scaled to fit.";
        renderResolutionChoice.ItemSelected += SetRenderResolution;
        gameSettingsContent.AddChild(DisplaySettingRow("Render Resolution", renderResolutionChoice));


        gameSettingsContent.AddChild(SettingsSection("Weather"));
        cloudHazeToggle.TooltipText = "A faint haze of cloud that drifts over the land now and then.";
        cloudHazeToggle.ButtonPressed = displayPreferences.CloudHaze;
        cloudHazeToggle.Toggled += SetCloudHaze;
        gameSettingsContent.AddChild(DisplaySettingRow("Cloud haze", cloudHazeToggle));
        lightningToggle.TooltipText = "A soft flash every several seconds during storms.";
        lightningToggle.ButtonPressed = displayPreferences.LightningFlashes;
        lightningToggle.Toggled += SetLightningFlashes;
        gameSettingsContent.AddChild(DisplaySettingRow("Lightning flashes", lightningToggle));

        gameSettingsContent.AddChild(SettingsSection("Date and time"));
        clockFormatChoice.AddItem("24-hour", 0);
        clockFormatChoice.AddItem("12-hour (AM/PM)", 1);
        clockFormatChoice.Selected = displayPreferences.UseTwelveHourClock ? 1 : 0;
        clockFormatChoice.ItemSelected += SetClockFormat;
        gameSettingsContent.AddChild(DisplaySettingRow("Time display", clockFormatChoice));

        dateFormatChoice.AddItem("DD-MM-YYYY");
        dateFormatChoice.AddItem("MM-DD-YYYY");
        dateFormatChoice.AddItem("YYYY-MM-DD");
        dateFormatChoice.Selected = displayPreferences.DateFormat switch { "mdy" => 1, "ymd" => 2, _ => 0 };
        dateFormatChoice.ItemSelected += SetDateFormat;
        gameSettingsContent.AddChild(DisplaySettingRow("Date display", dateFormatChoice));

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

        BuildAutosaveSettings();

        jevAssistanceToggle.Text = "Let Jev help in this world";
        jevAssistanceToggle.TooltipText = "Jev is an optional helper for small everyday choices, so your agents' own models are called less. Turn it off and nothing is lost. Memories and keys stay.";
        jevAssistanceToggle.Toggled += enabled => _ = SaveJevAssistanceAsync(enabled);
        worldSettingsContent.AddChild(jevAssistanceToggle);

        BuildCognitionSettingsPanel();
        worldSettingsContent.AddChild(cognitionSettingsPanel);

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
        settingsScroll.AddChild(settingsPages);
        var settingsCategories = new VBoxContainer { CustomMinimumSize = new Vector2(130, 0) };
        gameSettingsCategoryButton.Text = "Game";
        StyleSettingsCategoryButton(gameSettingsCategoryButton);
        gameSettingsCategoryButton.Pressed += () => ShowSettingsSection(worldSpecific: false);
        settingsCategories.AddChild(gameSettingsCategoryButton);
        worldSettingsCategoryButton.Text = "World";
        StyleSettingsCategoryButton(worldSettingsCategoryButton);
        worldSettingsCategoryButton.Pressed += () => ShowSettingsSection(worldSpecific: true);
        settingsCategories.AddChild(worldSettingsCategoryButton);
        settingsCategories.AddChild(developerToggleButton);
        SelectSettingsCategory(gameSettingsCategoryButton);
        var settingsLayout = new HBoxContainer();
        settingsLayout.AddThemeConstantOverride("separation", 10);
        settingsLayout.AddChild(settingsCategories);
        settingsLayout.AddChild(settingsScroll);
        AddPanelContents(settingsPanel, "Settings", settingsLayout);
        // Settings sits inside the menu panel, so it reads as a section of it.
        settingsPanel.ThemeTypeVariation = "InsetPanel";
        settingsPanel.Hide();
        body.AddChild(settingsPanel);
        BuildModLibrary(body);

        developerScroll.CustomMinimumSize = new Vector2(0, 440);
        developerScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        developerScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        developerScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        developerBody.AddThemeConstantOverride("separation", 8);
        developerScroll.AddChild(developerBody);

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

        var authoringBody = new VBoxContainer();
        authoringKind.ItemSelected += _ => UpdateAuthoringHint();
        AddAuthoringKinds();
        authoringBody.AddChild(authoringKind);
        authoringId.PlaceholderText = "ID (resource/object/draft/asset as required)";
        authoringBody.AddChild(authoringId);
        authoringValue.PlaceholderText = "Value (terrain, kind, name, weather, digest…)";
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

        developerScroll.Hide();
        settingsLayout.AddChild(developerScroll);
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
        UpdateAuthoringHint();
    }

    private void BuildSelectedInhabitantCard()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);

        var heading = new HBoxContainer();
        selectedActorNameLabel.Text = string.Empty;
        selectedActorNameLabel.AddThemeFontSizeOverride("font_size", 18);
        selectedActorNameLabel.ThemeTypeVariation = "HeadingLabel";
        selectedActorNameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(selectedActorNameLabel);
        clearSelectionButton.Text = "×";
        clearSelectionButton.TooltipText = "Close";
        StyleButton(clearSelectionButton);
        clearSelectionButton.Pressed += ClearInhabitantSelection;
        findAgentButton.Text = "Find";
        findAgentButton.TooltipText = "Center the map on this agent.";
        StyleButton(findAgentButton);
        findAgentButton.Pressed += () =>
        {
            if (selectedInhabitantId is { } id) CenterOnInhabitant(id);
        };
        heading.AddChild(findAgentButton);
        heading.AddChild(clearSelectionButton);
        body.AddChild(heading);

        selectedAgentOverview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        selectedAgentOverviewScroll.AddChild(selectedAgentOverview);
        body.AddChild(selectedAgentOverviewScroll);
        selectedAgentModelScroll.CustomMinimumSize = new Vector2(0, 300);
        selectedAgentModelScroll.AddChild(selectedAgentModelContent);
        var backToProfile = new Button { Text = "← Agent profile" };
        StyleButton(backToProfile);
        backToProfile.Pressed += CloseAgentModelEditor;
        selectedAgentModelContent.AddChild(backToProfile);
        body.AddChild(selectedAgentModelScroll);
        selectedAgentModelScroll.Hide();

        selectedActorSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        selectedActorSummaryLabel.ThemeTypeVariation = "DimLabel";
        selectedAgentOverview.AddChild(selectedActorSummaryLabel);
        selectedActorConditionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        selectedActorConditionLabel.ThemeTypeVariation = "SoftLabel";
        selectedAgentOverview.AddChild(selectedActorConditionLabel);

        var renameRow = new HBoxContainer();
        renameAgentInput.PlaceholderText = "Agent name";
        renameAgentInput.MaxLength = 48;
        renameAgentInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        renameRow.AddChild(renameAgentInput);
        renameAgentButton.Text = "Rename";
        StyleButton(renameAgentButton);
        renameAgentButton.Pressed += () => _ = RenameSelectedAgentAsync();
        renameRow.AddChild(renameAgentButton);
        selectedAgentOverview.AddChild(renameRow);

        ConfigureTextPanel(inhabitantDetails, 96);
        selectedAgentOverview.AddChild(inhabitantDetails);

        ConfigureTextPanel(inhabitantSocialDetails, 104);
        selectedAgentOverview.AddChild(inhabitantSocialDetails);

        ConfigureTextPanel(privateThoughtHistory, 86);
        privateThoughtHistory.TooltipText = "Only you can see these thoughts. Other agents don't know them unless they are told.";
        selectedAgentOverview.AddChild(privateThoughtHistory);

        memoriesButton.Text = "Memories + maps";
        memoriesButton.TooltipText = "See what this agent remembers, including private memories.";
        StyleButton(memoriesButton);
        memoriesButton.Pressed += OpenMemories;
        var historyActions = new HBoxContainer();
        historyActions.AddChild(memoriesButton);

        familyTreeButton.Text = "Family Tree";
        familyTreeButton.TooltipText = "See their family, including those who have passed.";
        StyleButton(familyTreeButton);
        familyTreeButton.Pressed += OpenFamilyTree;
        historyActions.AddChild(familyTreeButton);
        modelSettingsButton.Text = "Model and key";
        modelSettingsButton.TooltipText = "Choose this agent's model and API key.";
        StyleButton(modelSettingsButton);
        modelSettingsButton.Pressed += OpenAgentModelEditor;
        historyActions.AddChild(modelSettingsButton);
        selectedAgentOverview.AddChild(historyActions);

        var instructionHeading = new Label { Text = "Speak to them" };
        instructionHeading.AddThemeFontSizeOverride("font_size", 13);
        instructionHeading.ThemeTypeVariation = "SectionLabel";
        selectedAgentOverview.AddChild(instructionHeading);
        instructionKind.AddItem("Suggestion", 0);
        instructionKind.AddItem("Direct order", 1);
        instructionKind.CustomMinimumSize = new Vector2(0, 32);
        selectedAgentOverview.AddChild(instructionKind);
        instructionText.PlaceholderText = "Say something…";
        instructionText.CustomMinimumSize = new Vector2(0, 34);
        selectedAgentOverview.AddChild(instructionText);
        submitInstructionButton.Text = "Send";
        StyleButton(submitInstructionButton, primary: true);
        submitInstructionButton.Pressed += () => _ = SubmitInstructionAsync();
        selectedAgentOverview.AddChild(submitInstructionButton);

        AddPanelContents(selectedInhabitantCard, body);
        selectedInhabitantCard.CustomMinimumSize = new Vector2(350, 0);
        selectedInhabitantCard.ZIndex = 70;
        selectedInhabitantCard.Hide();
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
    private static void SetPanelText(RichTextLabel label, string text)
    {
        if (label.Text != text) label.Text = text;
    }

    private static void ConfigureTextPanel(RichTextLabel label, float minimumHeight)
    {
        label.BbcodeEnabled = false;
        label.FitContent = false;
        label.CustomMinimumSize = new Vector2(0, minimumHeight);
        label.ScrollActive = true;
    }

    private static PanelContainer NewPanel(string title, Control content)
    {
        // Always nested inside another panel, so it reads as a section.
        var panel = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
        AddPanelContents(panel, title, content);
        return panel;
    }

    private static void AddPanelContents(PanelContainer panel, Control content) => AddPanelContents(panel, string.Empty, content);

    /// <summary>A titled world panel whose heading carries a × that hides it, like Escape does.</summary>
    private static void AddClosablePanelContents(PanelContainer panel, string title, Control content) =>
        AddPanelContents(panel, title, content, closable: true);

    private static void AddPanelContents(PanelContainer panel, string title, Control content, bool closable = false)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 7);
        if (!string.IsNullOrWhiteSpace(title) && closable)
        {
            var headingRow = new HBoxContainer();
            var heading = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            heading.AddThemeFontSizeOverride("font_size", 17);
            heading.ThemeTypeVariation = "HeadingLabel";
            headingRow.AddChild(heading);
            var close = new Button
            {
                Text = "×",
                TooltipText = $"Close {title}",
                CustomMinimumSize = new Vector2(34, 0),
            };
            StyleButton(close);
            close.Pressed += () =>
            {
                close.ReleaseFocus();
                panel.Hide();
            };
            headingRow.AddChild(close);
            body.AddChild(headingRow);
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            var heading = new Label { Text = title, ThemeTypeVariation = "HeadingLabel" };
            heading.AddThemeFontSizeOverride("font_size", 15);
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
        foreach (var button in new[] { gameSettingsCategoryButton, worldSettingsCategoryButton, developerToggleButton })
            button.SetPressedNoSignal(button == selected);
    }

}
