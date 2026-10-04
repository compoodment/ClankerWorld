using ClankerWorld.GodotClient.ClientState;
using Godot;
using ClankerWorld.GodotClient.UI;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Control mainMenuOverlay = new();
    private readonly ColorRect mainMenuBackground = new();
    private readonly MenuBackdrop mainMenuBackdrop = new();
    private readonly VBoxContainer mainMenuStack = new() { Alignment = BoxContainer.AlignmentMode.Center };
    private readonly TextureRect mainMenuLogo = new()
    {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        TextureFilter = TextureFilterEnum.Nearest,
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        MouseFilter = MouseFilterEnum.Ignore,
    };
    private readonly CenterContainer mainMenuCenter = new();
    private readonly PanelContainer mainMenuCard = new();
    private readonly Label mainMenuStatus = new();
    private readonly Button mainMenuContinueButton = new();
    private readonly Button mainMenuNewButton = new();
    private readonly Button mainMenuConnectButton = new();
    private readonly Button mainMenuLoadButton = new();
    private readonly Button menuQuitToMainButton = new();
    private readonly Button menuSaveWorldButton = new();
    private readonly Control worldMenuOverlay = new();
    private readonly PanelContainer worldMenuCard = new();
    private readonly ScrollContainer worldMenuScroll = new();
    private readonly VBoxContainer worldMenuBody = new();
    private readonly HFlowContainer worldMenuColumns = new();
    private readonly PanelContainer worldPreviewFrame = new();
    private readonly Label worldMenuHeading = new();
    private readonly Label worldMenuStatus = new();
    private readonly LineEdit worldNameInput = new();
    private readonly LineEdit worldSeedInput = new();
    private readonly SegmentedChoice worldSizeChoice = new();
    private readonly HSlider worldWaterSlider = new() { MinValue = 20, MaxValue = 80, Step = 1, Value = 50 };
    private readonly Label worldWaterValue = new() { CustomMinimumSize = new Vector2(36, 0), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly SegmentedChoice worldForestChoice = new();
    private readonly SegmentedChoice worldMountainChoice = new();
    private readonly SegmentedChoice worldRiverChoice = new();
    private readonly VBoxContainer worldAdvancedOptions = new();
    private readonly Button worldAdvancedToggle = new() { Text = "+ More options", ToggleMode = true };
    private readonly SegmentedChoice worldResourceChoice = new();
    private readonly SegmentedChoice worldClimateModeChoice = new();
    private readonly OptionButton worldClimateFamilyChoice = new();
    private readonly CheckBox worldWrapChoice = new();
    private readonly CheckBox worldLatitudeChoice = new();
    private readonly WorldOverview worldPreview = new();
    private readonly Label worldPreviewStatus = new();
    private readonly CheckBox worldAcceptUnmetTargets = new();
    private readonly Button worldPreviewButton = new();
    private readonly SlotList worldSelectionList = new();
    private readonly Button worldCreateButton = new();
    private readonly Button worldSelectButton = new();
    private readonly Button worldSavesButton = new();
    private CatalogWorld[] listedWorlds = [];
    private readonly Button worldDeleteButton = new();
    private string? listedActiveWorldId;
    private readonly WorldListRequest worldListRequest = new();
    private OwnerWorldCreationAction? previewedWorldOptions;
    private OwnerWorldPreview? previewedWorldResult;
    private bool worldMenuBusy;
    private int worldPreviewRevision;
    private readonly ConfirmationDialog quitToMenuConfirmation = new();
    private bool isInWorld;
    private bool returnToMainMenu;
    private bool resumeWorldOnContinue;
    private readonly Button mainMenuSettingsButton = new();
    private readonly Button worldBackButton = new();
    private readonly HSeparator menuQuitSeparator = new();

    private void BuildMainMenu()
    {
        mainMenuOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.MouseFilter = MouseFilterEnum.Stop;
        mainMenuOverlay.ZIndex = 180;
        menuLayer.AddChild(mainMenuOverlay);

        mainMenuBackground.Color = UiTheme.Current.Backdrop;
        mainMenuBackground.MouseFilter = MouseFilterEnum.Stop;
        mainMenuBackground.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.AddChild(mainMenuBackground);
        mainMenuBackdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuBackdrop.Night = ReferenceEquals(UiTheme.Current, UiTheme.Dark);
        mainMenuOverlay.AddChild(mainMenuBackdrop);

        mainMenuCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.AddChild(mainMenuCenter);
        // The logo floats over the valley above a card that holds only the menu.
        mainMenuStack.AddThemeConstantOverride("separation", 18);
        mainMenuCenter.AddChild(mainMenuStack);
        mainMenuLogo.Texture = ImageTexture.CreateFromImage(MenuLogo.Create());
        mainMenuStack.AddChild(mainMenuLogo);
        mainMenuCard.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        mainMenuStack.AddChild(mainMenuCard);
        mainMenuOverlay.Resized += FitMainMenuLogo;
        FitMainMenuLogo();

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        body.AddThemeConstantOverride("separation", 12);

        mainMenuStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        mainMenuStatus.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(mainMenuStatus);

        mainMenuContinueButton.Text = "Continue";
        StyleMenuChoice(mainMenuContinueButton, primary: true);
        mainMenuContinueButton.Pressed += () => _ = EnterWorldAsync();
        body.AddChild(mainMenuContinueButton);

        mainMenuNewButton.Text = "New World";
        StyleMenuChoice(mainMenuNewButton);
        mainMenuNewButton.Pressed += () => OpenWorldMenu(create: true);
        body.AddChild(mainMenuNewButton);

        mainMenuLoadButton.Text = "Load World";
        StyleMenuChoice(mainMenuLoadButton);
        mainMenuLoadButton.Pressed += () => OpenWorldMenu(create: false);
        body.AddChild(mainMenuLoadButton);

        mainMenuSettingsButton.Text = "Settings";
        StyleMenuChoice(mainMenuSettingsButton);
        mainMenuSettingsButton.Pressed += OpenMainMenuSettings;
        body.AddChild(mainMenuSettingsButton);

        mainMenuConnectButton.Text = "Connect / Pair development host";
        StyleMenuChoice(mainMenuConnectButton);
        mainMenuConnectButton.Pressed += () => _ = OpenMainMenuConnectionAsync();
        body.AddChild(mainMenuConnectButton);

        quitGameButton.Text = "Quit Game";
        StyleMenuChoice(quitGameButton);
        quitGameButton.Pressed += () => PopupDialog(quitGameConfirmation);
        body.AddChild(quitGameButton);

        AddPanelContents(mainMenuCard, body);
        mainMenuCard.CustomMinimumSize = new Vector2(440, 0);

        StyleConfirmation(quitToMenuConfirmation, "Quit to Main Menu?", "Quit to Menu");
        quitToMenuConfirmation.DialogText = "Leave this world and return to the Main Menu? Time stays paused until you continue.";
        quitToMenuConfirmation.Confirmed += QuitToMainMenu;
        AddChild(quitToMenuConfirmation);
        BuildWorldMenu();
        RefreshMainMenuAvailability();
    }

    private void ShowMainMenu()
    {
        isInWorld = false;
        if (developerPanel.Visible) CloseDeveloperTools();
        mainMenuOverlay.MouseFilter = MouseFilterEnum.Stop;
        mainMenuBackground.MouseFilter = MouseFilterEnum.Stop;
        mainMenuCenter.MouseFilter = MouseFilterEnum.Pass;
        mainMenuCard.Show();
        mainMenuLogo.Show();
        menuShade.ZIndex = 90;
        gameMenuPanel.ZIndex = 100;
        mainMenuOverlay.Show();
        RefreshMainMenuAvailability();
    }

    private void RefreshMainMenuAvailability()
    {
        var paired = !registeredEndpointInvalid && registration is not null && deviceKey is not null;
        mainMenuContinueButton.Disabled = !paired;
        mainMenuNewButton.Disabled = !paired;
        mainMenuLoadButton.Disabled = !paired;
        mainMenuConnectButton.Visible = !paired;
        SetMainMenuStatus(paired ? null : "Connect this device to your world to play.");
    }

    /// <summary>The Main Menu only shows a line when something needs the player's attention.</summary>
    private void SetMainMenuStatus(string? text)
    {
        mainMenuStatus.Text = text ?? string.Empty;
        mainMenuStatus.Visible = !string.IsNullOrEmpty(text);
    }

    /// <summary>Whole-number scale so the logo's pixels match the valley's and stay crisp.</summary>
    private void FitMainMenuLogo()
    {
        var area = mainMenuOverlay.Size;
        if (area.X <= 0 || area.Y <= 0) area = GetViewportRect().Size / menuLayer.Factor;
        var scale = Math.Clamp((int)Math.Min(area.X * 0.62f / MenuLogo.Width, area.Y * 0.24f / MenuLogo.Height), 1, 8);
        mainMenuLogo.CustomMinimumSize = new Vector2(MenuLogo.Width, MenuLogo.Height) * scale;
    }

    private async Task EnterWorldAsync()
    {
        if (registration is null || deviceKey is null || registeredEndpointInvalid) return;
        mainMenuContinueButton.Disabled = true;
        var previousRefreshCount = successfulRefreshCount;
        await RefreshAsync();
        if (successfulRefreshCount == previousRefreshCount || observationSession.AwaitingFreshBaseline)
        {
            RefreshMainMenuAvailability();
            SetMainMenuStatus("Could not reach your world. Check your connection and try Continue again.");
            return;
        }

        if (observationSession.Current?.Baseline.Snapshot.FounderSetup?.RequiresWorldCreation == true)
        {
            ShowMainMenu();
            resumeWorldOnContinue = false;
            OpenWorldMenu(create: true);
            // Load World still holds its action guard until this entry call returns.
            if (isOwnerAction) _ = RefreshWorldPreviewAfterChangeAsync(worldPreviewRevision);
            return;
        }

        mainMenuOverlay.Hide();
        // Title-screen notices such as "continue from Main Menu" are stale here.
        statusToast.Hide();
        isInWorld = true;
        if (resumeWorldOnContinue)
        {
            resumeWorldOnContinue = false;
            await SetPausedAsync(paused: false);
        }
    }

    private void OpenMainMenuSettings()
    {
        returnToMainMenu = true;
        mainMenuOverlay.Show();
        mainMenuCard.Hide();
        mainMenuLogo.Hide();
        // Keep the title backdrop visible, but let the Settings panel behind
        // this later-added overlay receive pointer input.
        mainMenuOverlay.MouseFilter = MouseFilterEnum.Ignore;
        mainMenuBackground.MouseFilter = MouseFilterEnum.Ignore;
        mainMenuCenter.MouseFilter = MouseFilterEnum.Ignore;
        menuShade.ZIndex = 190;
        gameMenuPanel.ZIndex = 200;
        menuHeadingLabel.Text = "Settings";
        StyleIconButton(menuCloseButton, PixelGlyph.Back);
        menuCloseButton.TooltipText = "Back to Main Menu";
        SetWorldMenuActionsVisible(false);
        menuResumeButton.Hide();
        gameMenuPanel.Show();
        menuShade.Show();
        ShowSettingsSection(worldSpecific: false);
        ApplyResponsiveLayout();
    }

    private async Task OpenMainMenuConnectionAsync()
    {
        OpenMenuForSetup();
        if (registration is null && pendingPairing is null)
            await StartPairingAsync();
    }

    private bool isQuittingToMenu;

    private async void QuitToMainMenu()
    {
        var generation = observationSession.RequestGeneration;
        if (isQuittingToMenu || !IsCurrentWorldRequest(generation)) return;
        isQuittingToMenu = true;
        try
        {
            // Require a confirmed pause before stopping owner polling on the title screen.
            if (!menuPauseConfirmed)
            {
                var confirmed = await SetPausedAsync(paused: true);
                if (!IsCurrentWorldRequest(generation)) return;
                menuPauseConfirmed = confirmed;
                if (!menuPauseConfirmed)
                {
                    SetStatus("Could not confirm the pause. Try Quit to Menu again when the host is reachable.", good: false);
                    return;
                }
                // The owner may have closed the menu while this request waited.
                if (!gameMenuPanel.Visible) return;
            }
            resumeWorldOnContinue = menuPausedWorld;
            CloseGameMenu();
            ShowMainMenu();
        }
        finally { isQuittingToMenu = false; }
    }

    private void SetWorldMenuActionsVisible(bool visible)
    {
        menuResumeButton.Visible = !visible;
        menuSaveWorldButton.Visible = visible;
        settingsButton.Visible = visible;
        modLibraryButton.Visible = visible;
        worldSettingsCategoryButton.Visible = visible;
        menuQuitToMainButton.Visible = visible;
        menuQuitSeparator.Visible = visible;
        modLibraryPanel.Hide();
    }

    private void BuildWorldMenu()
    {
        worldMenuOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.MouseFilter = MouseFilterEnum.Stop;
        worldMenuOverlay.ZIndex = 210;
        menuLayer.AddChild(worldMenuOverlay);
        var shade = new ColorRect { Color = UiTheme.Current.Shade with { A = 0.88f }, MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.AddChild(center);
        center.AddChild(worldMenuCard);

        // New World reads left to right: options, then the large preview and
        // the actions that act on it. Narrow or scaled-up screens wrap the
        // preview under the options and the card scrolls instead of clipping.
        worldMenuBody.AddThemeConstantOverride("separation", 10);
        // Like Main Menu Settings, the one way back sits where a close button would.
        var headingRow = new HBoxContainer();
        worldMenuHeading.ThemeTypeVariation = "TitleLabel";
        worldMenuHeading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headingRow.AddChild(worldMenuHeading);
        StyleIconButton(worldBackButton, PixelGlyph.Back);
        worldBackButton.TooltipText = "Back to Main Menu";
        worldBackButton.Pressed += () => { if (!worldMenuBusy) worldMenuOverlay.Hide(); };
        headingRow.AddChild(worldBackButton);
        worldMenuBody.AddChild(headingRow);
        worldMenuStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        worldMenuBody.AddChild(worldMenuStatus);

        var options = new VBoxContainer { CustomMinimumSize = new Vector2(WorldOptionsWidth, 0) };
        options.AddThemeConstantOverride("separation", 8);
        // Both fields open pre-filled, which hides their placeholders, so each
        // keeps a visible caption.
        worldNameInput.PlaceholderText = "World name";
        worldNameInput.MaxLength = 80;
        worldSeedInput.PlaceholderText = "Generation seed";
        worldSeedInput.MaxLength = 100;
        worldSeedInput.TextChanged += _ => InvalidateWorldPreview();
        var seedRow = WorldOptionRow("Seed", worldSeedInput);
        var reroll = new Button { Text = "Reroll", TooltipText = "Pick a new random seed" };
        StyleButton(reroll);
        reroll.Pressed += () =>
        {
            worldSeedInput.Text = Guid.NewGuid().ToString("N")[..12];
            if (!worldMenuBusy) _ = PreviewWorldAsync();
        };
        seedRow.AddChild(reroll);
        worldSizeChoice.AddItem("Small", 0);
        worldSizeChoice.AddItem("Medium", 1);
        worldSizeChoice.SetItemTooltip(0, "256 × 128 tiles");
        worldSizeChoice.SetItemTooltip(1, "512 × 256 tiles. Takes longer to make and to load.");
        worldSizeChoice.Select(0);
        worldSizeChoice.ItemSelected += _ => InvalidateWorldPreview();
        options.AddChild(SettingsBox("World", WorldOptionRow("Name", worldNameInput), seedRow,
            WorldOptionRow("Size", worldSizeChoice)));

        // The rarer choices stay folded away until asked for. The toggle reads
        // as a link, not as a pressed button, whichever way it is set.
        StyleButton(worldAdvancedToggle);
        worldAdvancedToggle.ThemeTypeVariation = "TabButton";
        worldAdvancedToggle.Alignment = HorizontalAlignment.Left;
        worldAdvancedToggle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var unpressed = new StyleBoxEmpty { ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 7, ContentMarginBottom = 7 };
        worldAdvancedToggle.AddThemeStyleboxOverride("pressed", unpressed);
        worldAdvancedToggle.AddThemeStyleboxOverride("hover_pressed", unpressed);
        worldAdvancedToggle.Toggled += visible =>
        {
            worldAdvancedOptions.Visible = visible;
            worldAdvancedToggle.Text = visible ? "− Fewer options" : "+ More options";
        };
        options.AddChild(worldAdvancedToggle);
        options.AddChild(worldAdvancedOptions);
        worldAdvancedOptions.AddThemeConstantOverride("separation", 8);
        worldAdvancedOptions.Visible = false;

        worldWaterSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        worldWaterSlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        worldWaterSlider.TooltipText = "How much of the world is sea and lakes.";
        worldWaterSlider.ValueChanged += value =>
        {
            worldWaterValue.Text = $"{value:0}%";
            InvalidateWorldPreview();
        };
        worldWaterValue.Text = $"{worldWaterSlider.Value:0}%";
        var waterRow = WorldOptionRow("Water", worldWaterSlider);
        waterRow.AddChild(worldWaterValue);
        foreach (var choice in new[] { worldForestChoice, worldMountainChoice, worldRiverChoice })
        {
            choice.AddItem("Low", 1);
            choice.AddItem("Normal", 0);
            choice.AddItem("High", 2);
            choice.Select(1);
            choice.ItemSelected += _ => InvalidateWorldPreview();
        }
        worldResourceChoice.AddItem("Low", 0);
        worldResourceChoice.AddItem("Normal", 1);
        worldResourceChoice.AddItem("High", 2);
        worldResourceChoice.Select(1);
        worldResourceChoice.ItemSelected += _ => InvalidateWorldPreview();
        worldAdvancedOptions.AddChild(SettingsBox("Land", waterRow,
            WorldOptionRow("Forest", worldForestChoice), WorldOptionRow("Mountains", worldMountainChoice),
            WorldOptionRow("Rivers", worldRiverChoice), WorldOptionRow("Resources", worldResourceChoice)));

        worldClimateModeChoice.AddItem("Balanced", 0);
        worldClimateModeChoice.AddItem("Uniform", 1);
        worldClimateModeChoice.AddItem("Dominant", 2);
        worldClimateModeChoice.SetItemTooltip(0, "A mix of climates, colder toward the poles.");
        worldClimateModeChoice.SetItemTooltip(1, "One climate everywhere.");
        worldClimateModeChoice.SetItemTooltip(2, "Mostly one climate, with others at the edges.");
        worldClimateModeChoice.Select(0);
        worldClimateModeChoice.ItemSelected += _ =>
        {
            worldClimateFamilyChoice.GetParent<Control>().Visible = worldClimateModeChoice.GetSelectedId() != 0;
            InvalidateWorldPreview();
        };
        worldClimateFamilyChoice.AddItem("Tropical", 0);
        worldClimateFamilyChoice.AddItem("Dry", 1);
        worldClimateFamilyChoice.AddItem("Temperate", 2);
        worldClimateFamilyChoice.AddItem("Cold", 3);
        worldClimateFamilyChoice.AddItem("Polar", 4);
        worldClimateFamilyChoice.Select(2);
        worldClimateFamilyChoice.ItemSelected += _ => InvalidateWorldPreview();
        var familyRow = WorldOptionRow("Main climate", worldClimateFamilyChoice);
        familyRow.Visible = false;
        worldLatitudeChoice.Text = "Colder toward the poles";
        worldLatitudeChoice.ButtonPressed = true;
        worldLatitudeChoice.Toggled += _ => InvalidateWorldPreview();
        worldWrapChoice.Text = "Wrap east/west";
        worldWrapChoice.TooltipText = "Walking off the east edge comes back on the west.";
        worldWrapChoice.ButtonPressed = true;
        worldWrapChoice.Toggled += _ => InvalidateWorldPreview();
        worldAdvancedOptions.AddChild(SettingsBox("Climate", WorldOptionRow("Climates", worldClimateModeChoice),
            familyRow, worldLatitudeChoice, worldWrapChoice));

        var reset = new Button { Text = "Reset these options" };
        StyleButton(reset);
        reset.Pressed += ResetWorldGenerationOptions;
        worldAdvancedOptions.AddChild(reset);

        var previewColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(WorldPreviewWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        previewColumn.AddThemeConstantOverride("separation", 8);
        // The frame keeps its size while a new preview generates, so the
        // layout does not jump each time an option changes.
        worldPreviewFrame.CustomMinimumSize = new Vector2(WorldPreviewWidth, WorldPreviewWidth / 2f);
        worldPreviewFrame.ThemeTypeVariation = "InsetPanel";
        worldPreview.ShowCameraBounds = false;
        worldPreview.MouseFilter = MouseFilterEnum.Ignore;
        worldPreview.TooltipText = "Map preview. You will choose where your first Town goes after creating the world.";
        worldPreview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldPreview.Hide();
        worldPreviewFrame.AddChild(worldPreview);
        previewColumn.AddChild(worldPreviewFrame);
        worldPreviewStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        worldPreviewStatus.CustomMinimumSize = new Vector2(0, 64);
        // Hovering the description shows the map's exact measurements.
        worldPreviewStatus.MouseFilter = MouseFilterEnum.Pass;
        previewColumn.AddChild(worldPreviewStatus);
        worldAcceptUnmetTargets.Text = "Keep this map anyway";
        worldAcceptUnmetTargets.TooltipText = "Create this map even though it is less balanced than the game aims for.";
        worldAcceptUnmetTargets.Hide();
        worldAcceptUnmetTargets.Toggled += _ => RefreshWorldMenuAvailability();
        previewColumn.AddChild(worldAcceptUnmetTargets);

        worldMenuColumns.AddThemeConstantOverride("h_separation", 20);
        worldMenuColumns.AddThemeConstantOverride("v_separation", 12);
        worldMenuColumns.AddChild(options);
        worldMenuColumns.AddChild(previewColumn);
        worldMenuBody.AddChild(worldMenuColumns);

        worldSelectionList.CustomMinimumSize = new Vector2(0, 300);
        worldSelectionList.ItemSelected += index =>
        {
            RefreshWorldMenuAvailability();
            if (worldMenuBusy || isOwnerAction) return;
            if (worldListRequest.IsLoading || index < 0 || index >= listedWorlds.Length) return;
            var world = listedWorlds[(int)index];
            worldMenuStatus.Text = world.Compatibility == "incompatible"
                ? "Cannot open this world: " + (world.CompatibilityReason ?? "It was made with a different version.") + " Your save is safe."
                : world.Compatibility == "unknown"
                    ? "Could not check this world. Opening it will try the saved copy and will not delete anything."
                    : world.Id == listedActiveWorldId
                        ? "This is your current world. Open it, or load one of its saves."
                        : "This world is ready to open. Open it first to load one of its saves.";
        };
        worldSelectionList.ItemActivated += index => _ = SelectListedWorldAsync();
        worldSelectionList.Hide();
        worldMenuBody.AddChild(worldSelectionList);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        // Deleting sits apart on the left, away from the main action on the right.
        worldDeleteButton.Text = "Delete World";
        StyleButton(worldDeleteButton);
        worldDeleteButton.Pressed += ConfirmWorldDeletion;
        actions.AddChild(worldDeleteButton);
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        worldPreviewButton.Text = "Preview again";
        worldPreviewButton.TooltipText = "The preview updates by itself when you change an option. Use this if it failed.";
        StyleButton(worldPreviewButton);
        worldPreviewButton.Pressed += () => _ = PreviewWorldAsync();
        actions.AddChild(worldPreviewButton);
        worldCreateButton.Text = "Create World";
        StyleButton(worldCreateButton, primary: true);
        worldCreateButton.CustomMinimumSize = new Vector2(170, 34);
        worldCreateButton.Pressed += () => _ = CreateSelectedWorldAsync();
        worldCreateButton.Disabled = true;
        actions.AddChild(worldCreateButton);
        worldSavesButton.Text = "Load a save...";
        worldSavesButton.TooltipText = "Go back to one of the current world's saves. Playing on from an older save starts a new branch.";
        StyleButton(worldSavesButton);
        worldSavesButton.Pressed += () => _ = OpenManualSavesAsync(loadMode: true);
        worldSavesButton.Hide();
        actions.AddChild(worldSavesButton);
        worldSelectButton.Text = "Open World";
        StyleButton(worldSelectButton, primary: true);
        worldSelectButton.CustomMinimumSize = new Vector2(170, 34);
        worldSelectButton.Pressed += () => _ = SelectListedWorldAsync();
        worldSelectButton.Hide();
        actions.AddChild(worldSelectButton);
        worldMenuBody.AddChild(actions);

        worldMenuBody.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        worldMenuScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        worldMenuScroll.AddChild(worldMenuBody);
        AddPanelContents(worldMenuCard, worldMenuScroll);
        worldListRequest.Changed += RenderWorldList;
        worldMenuOverlay.VisibilityChanged += () =>
        {
            if (!worldMenuOverlay.Visible) worldListRequest.Cancel();
            Callable.From(LayoutWorldMenu).CallDeferred();
        };
        worldMenuBody.MinimumSizeChanged += () => Callable.From(LayoutWorldMenu).CallDeferred();
        worldMenuOverlay.Hide();
    }

    private const float WorldOptionsWidth = 380;
    private const float WorldPreviewWidth = 540;

    private static HBoxContainer WorldOptionRow(string caption, Control field)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(104, 0) };
        label.ThemeTypeVariation = "DimLabel";
        row.AddChild(label);
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(field);
        return row;
    }

    /// <summary>
    /// Sizes the New World / Load World card to the screen: both columns side
    /// by side when they fit, and a scroll height that never exceeds the view.
    /// </summary>
    private void LayoutWorldMenu()
    {
        // Hidden containers report no minimum size, so this runs once shown.
        if (!worldMenuOverlay.Visible) return;
        var viewport = menuLayer.Size;
        const float margins = 20;
        float optionsWidth = WorldOptionsWidth;
        float previewWidth = WorldPreviewWidth;
        // The slack keeps both columns side by side even if a scrollbar shows.
        var width = Math.Min(optionsWidth + previewWidth + 20 + 16, Math.Max(280, viewport.X - 40 - margins));
        previewWidth = Math.Min(previewWidth, width);
        worldMenuColumns.GetChild<Control>(0).CustomMinimumSize = new Vector2(Math.Min(optionsWidth, width), 0);
        worldMenuColumns.GetChild<Control>(1).CustomMinimumSize = new Vector2(previewWidth, 0);
        worldPreviewFrame.CustomMinimumSize = new Vector2(previewWidth, previewWidth / 2);
        worldMenuScroll.CustomMinimumSize = new Vector2(width, 0);
        var contentHeight = worldMenuBody.GetCombinedMinimumSize().Y;
        worldMenuScroll.CustomMinimumSize = new Vector2(width,
            Math.Min(contentHeight, Math.Max(200, viewport.Y - 40 - margins)));
        worldMenuCard.Size = worldMenuCard.GetCombinedMinimumSize();
    }

    private void OpenWorldMenu(bool create)
    {
        if (registration is null || deviceKey is null || registeredEndpointInvalid) return;
        worldListRequest.Cancel();
        worldMenuHeading.Text = create ? "New World" : "Load World";
        worldMenuStatus.Text = create
            ? "Choose a name, seed and size. You'll pick your first Town's site next."
            : "Choose a world to open. The current world is saved first.";
        worldMenuColumns.Visible = create;
        worldPreviewButton.Visible = create;
        worldPreview.Visible = create && previewedWorldOptions is not null;
        worldCreateButton.Visible = create;
        worldSelectionList.Visible = !create;
        worldSelectButton.Visible = !create;
        worldSavesButton.Visible = !create;
        worldSavesButton.Disabled = true;
        worldDeleteButton.Visible = !create;
        worldDeleteButton.Disabled = true;
        pendingDeletion = null;
        worldSelectButton.Disabled = true;
        worldNameInput.Text = "New World";
        worldSeedInput.Text = Guid.NewGuid().ToString("N")[..12];
        InvalidateWorldPreview();
        worldMenuOverlay.Show();
        if (create) _ = PreviewWorldAsync();
        else _ = RefreshWorldListAsync();
    }

    private async Task RefreshWorldListAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var server = ResolveWorldUri();
        await worldListRequest.RefreshAsync(token => ownerApi.ListWorldsAsync(server, authority,
            deviceId, signer, token));
    }

    private void RenderWorldList()
    {
        if (!IsInsideTree() || !worldMenuOverlay.Visible || worldMenuColumns.Visible) return;
        listedWorlds = [];
        listedActiveWorldId = null;
        worldSelectionList.Clear();
        worldSelectButton.Disabled = true;
        worldSavesButton.Disabled = true;
        worldDeleteButton.Disabled = true;
        if (worldListRequest.IsLoading)
        {
            worldMenuStatus.Text = "Checking saved worlds... This can take a moment. You can go back while you wait.";
            worldSelectionList.Placeholder = "Checking saved worlds...";
        }
        else if (worldListRequest.Failure is { } failure)
        {
            worldMenuStatus.Text = "Could not list worlds: " + FriendlyFailure(failure);
            worldSelectionList.Placeholder = "No worlds to show.";
        }
        else if (worldListRequest.Catalog is { } catalog)
        {
            listedActiveWorldId = catalog.ActiveId;
            listedWorlds = catalog.Worlds.OrderByDescending(world => world.Id == catalog.ActiveId)
                .ThenByDescending(world => world.UpdatedUtc).ToArray();
            var globe = SlotIcon(PixelGlyph.Globe);
            foreach (var world in listedWorlds)
            {
                var icon = WorldThumbnailTexture(world.Thumbnail) ?? globe;
                List<SlotTag> tags = [];
                if (world.Id == catalog.ActiveId) tags.Add(new SlotTag("Current"));
                if (world.Compatibility == "incompatible") tags.Add(new SlotTag("Can't open", Note: true));
                else if (world.Compatibility == "unknown") tags.Add(new SlotTag("Not checked", Note: true));
                worldSelectionList.AddItem(world.Name,
                    $"Saved {GameUiText.SavedAgo(world.UpdatedUtc, DateTimeOffset.Now)} · Seed {world.Seed}",
                    icon, tags, muted: world.Compatibility == "incompatible");
            }
            worldSelectionList.Placeholder = "No worlds yet. Make one with New World.";
            worldMenuStatus.Text = listedWorlds.Length == 0 ? "No worlds yet." : "Choose a world. Double-click to open it.";
        }
        // The list fills after its containers update; size the first opening once it has.
        Callable.From(LayoutWorldMenu).CallDeferred();
    }

    /// <summary>
    /// A world's map thumbnail from the host, or nothing when the host sent none
    /// or it can't be read, in which case the card shows a globe.
    /// </summary>
    private static ImageTexture? WorldThumbnailTexture(WorldThumbnail? thumbnail)
    {
        // The host limits thumbnails to 96 columns and checkpoint maps to
        // 2048 rows. Check these before decoding or creating a texture; this
        // optional catalog copy must never prevent healthy cards from drawing.
        if (thumbnail is null || thumbnail.Width is < 1 or > 96 || thumbnail.Height is < 1 or > 2048 ||
            thumbnail.Encoding != "terrain-kind-v1" || thumbnail.Data is null ||
            thumbnail.Data.Length != ((thumbnail.Width * thumbnail.Height + 2) / 3) * 4)
            return null;
        try
        {
            return WorldOverview.Thumbnail(WorldTerrainMap.FromPacked(
                new OwnerWorldPackedTerrain(thumbnail.Width, thumbnail.Height, thumbnail.Encoding, thumbnail.Data)));
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>A world or save card's icon, in the same colors as the menu choice that opens it.</summary>
    private static ImageTexture SlotIcon(PixelGlyph glyph)
    {
        var dark = UiTheme.Current.Name == "dark";
        var color = glyph == PixelGlyph.Globe
            ? dark ? new Color("8DBA6A") : UiTheme.Current.Primary
            : dark ? new Color("C99A62") : new Color("9C6C42");
        return PixelIcons.Themed(glyph, color, HudIconScale);
    }

    private CatalogWorld? SelectedListedWorld()
    {
        var selected = worldSelectionList.GetSelectedItems();
        return selected.Length == 1 && selected[0] >= 0 && selected[0] < listedWorlds.Length
            ? listedWorlds[selected[0]] : null;
    }

    private void RefreshWorldMenuAvailability()
    {
        var disabled = worldMenuBusy || isOwnerAction || worldListRequest.IsLoading ||
            registeredEndpointInvalid || registration is null || deviceKey is null || observationSession.AwaitingFreshBaseline;
        var world = SelectedListedWorld();
        worldSelectButton.Disabled = disabled || world is null || world.Compatibility == "incompatible";
        // Saves load into the open world, so another world must be opened first.
        worldSavesButton.Disabled = disabled || world is null || world.Id != listedActiveWorldId;
        worldDeleteButton.Disabled = disabled || world is null;
        worldPreviewButton.Disabled = disabled;
        worldCreateButton.Disabled = disabled || !CanCreatePreview(CurrentWorldOptions());
    }

    private bool CanCreatePreview(OwnerWorldCreationAction options)
    {
        if (!SameGeneration(previewedWorldOptions, options) || previewedWorldResult is not { } preview ||
            preview.Coverage is not { } coverage || string.IsNullOrWhiteSpace(preview.MapLayersDigest))
            return false;
        return coverage.MeetsTargets || !coverage.TargetsApplicable || worldAcceptUnmetTargets.ButtonPressed;
    }

    private async Task RunWorldMenuActionAsync(Func<Task> action)
    {
        var generation = observationSession.RequestGeneration;
        if (!IsCurrentWorldRequest(generation)) return;
        await ownerActionGate.RunAsync(async () =>
        {
            if (!IsCurrentWorldRequest(generation)) return;
            worldMenuBusy = true;
            isOwnerAction = true;
            refreshCancellation?.Cancel();
            RefreshControlAvailability();
            try { await action(); }
            finally
            {
                worldMenuBusy = false;
                isOwnerAction = false;
                RefreshControlAvailability();
            }
        });
    }

    private OwnerWorldCreationAction CurrentWorldOptions() => new(
        worldNameInput.Text.Trim(), worldSeedInput.Text.Trim(),
        worldSizeChoice.GetSelectedId() == 1 ? "Medium" : "Small",
        (int)worldWaterSlider.Value, worldWrapChoice.ButtonPressed,
        worldClimateModeChoice.GetSelectedId() switch { 1 => "Uniform", 2 => "Dominant", _ => "Balanced" },
        worldClimateFamilyChoice.GetSelectedId() switch
        {
            0 => "Tropical",
            1 => "Dry",
            3 => "Cold",
            4 => "Polar",
            _ => "Temperate",
        },
        worldLatitudeChoice.ButtonPressed,
        worldResourceChoice.GetSelectedId() switch { 0 => "Sparse", 2 => "Abundant", _ => "Normal" },
        GenerationAmountText(worldForestChoice), GenerationAmountText(worldMountainChoice), GenerationAmountText(worldRiverChoice));

    private static string GenerationAmountText(SegmentedChoice choice) => choice.GetSelectedId() switch
    {
        1 => "Low",
        2 => "High",
        _ => "Normal",
    };

    private void ResetWorldGenerationOptions()
    {
        worldSizeChoice.Select(0);
        worldWaterSlider.Value = 50;
        worldResourceChoice.Select(1);
        worldForestChoice.Select(1);
        worldMountainChoice.Select(1);
        worldRiverChoice.Select(1);
        worldClimateModeChoice.Select(0);
        worldClimateFamilyChoice.Select(2);
        worldClimateFamilyChoice.GetParent<Control>().Hide();
        worldWrapChoice.ButtonPressed = true;
        worldLatitudeChoice.ButtonPressed = true;
        InvalidateWorldPreview();
    }

    private static bool SameGeneration(OwnerWorldCreationAction? first, OwnerWorldCreationAction second) =>
        first is not null && first.Seed == second.Seed && first.Size == second.Size &&
        first.WaterPercent == second.WaterPercent && first.WrapEastWest == second.WrapEastWest &&
        first.ClimateMode == second.ClimateMode && first.SelectedClimate == second.SelectedClimate &&
        first.LatitudeCooling == second.LatitudeCooling &&
        first.ResourceAbundance == second.ResourceAbundance && first.ForestCover == second.ForestCover &&
        first.MountainRelief == second.MountainRelief && first.RiverAbundance == second.RiverAbundance;

    private void InvalidateWorldPreview(bool refresh = true)
    {
        var revision = ++worldPreviewRevision;
        previewedWorldOptions = null;
        previewedWorldResult = null;
        worldAcceptUnmetTargets.ButtonPressed = false;
        worldAcceptUnmetTargets.Hide();
        worldCreateButton.Disabled = true;
        worldPreview.Hide();
        worldPreviewStatus.Text = "Updating the preview...";
        worldPreviewStatus.TooltipText = string.Empty;
        if (refresh && worldMenuOverlay.Visible && worldMenuColumns.Visible)
            _ = RefreshWorldPreviewAfterChangeAsync(revision);
    }

    private async Task RefreshWorldPreviewAfterChangeAsync(int revision)
    {
        var generation = observationSession.RequestGeneration;
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        while (worldMenuBusy && IsInsideTree() && worldMenuOverlay.Visible && revision == worldPreviewRevision)
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        if (!IsCurrentWorldRequest(generation) || !IsInsideTree() || !worldMenuOverlay.Visible ||
            !worldMenuColumns.Visible || revision != worldPreviewRevision ||
            SameGeneration(previewedWorldOptions, CurrentWorldOptions()))
            return;
        await PreviewWorldAsync();
    }

    private async Task PreviewWorldAsync()
    {
        var generation = observationSession.RequestGeneration;
        if (worldMenuBusy || isOwnerAction || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var action = CurrentWorldOptions();
        if (action.Name.Length is < 1 or > 80 || action.Seed.Length is < 1 or > 100 ||
            action.Name.Any(char.IsControl) || action.Seed.Any(char.IsControl))
        {
            worldPreviewStatus.Text = "Enter a world name and a seed first.";
            return;
        }
        worldMenuBusy = true;
        worldPreviewButton.Disabled = true;
        worldCreateButton.Disabled = true;
        worldAcceptUnmetTargets.ButtonPressed = false;
        worldAcceptUnmetTargets.Hide();
        worldPreviewStatus.Text = "Generating map preview...";
        try
        {
            var result = await ownerApi.PreviewWorldAsync(ResolveWorldUri(), authority,
                deviceId, action, signer, CancellationToken.None);
            if (!IsCurrentWorldRequest(generation) || !SameGeneration(action, CurrentWorldOptions()))
            {
                return;
            }
            worldPreview.MarkerTile = null;
            worldPreview.SetWorld(WorldTerrainMap.FromPacked(result.Terrain, result.PackedMapLayers, action.WrapEastWest));
            worldPreview.Show();
            previewedWorldOptions = action;
            previewedWorldResult = result;
            SetWorldPreviewStatus(result);
            worldCreateButton.Disabled = !CanCreatePreview(CurrentWorldOptions());
        }
        catch (Exception exception)
        {
            if (IsCurrentWorldRequest(generation) && SameGeneration(action, CurrentWorldOptions()))
            {
                InvalidateWorldPreview(refresh: false);
                worldPreviewStatus.Text = "Could not preview map: " + FriendlyFailure(exception);
            }
        }
        finally
        {
            worldMenuBusy = false;
            RefreshWorldMenuAvailability();
        }
    }

    private void SetWorldPreviewStatus(OwnerWorldPreview result)
    {
        var (text, details) = WorldPreviewSummary(result);
        worldPreviewStatus.Text = text;
        worldPreviewStatus.TooltipText = details;
        if (result.Coverage is { TargetsApplicable: true, MeetsTargets: false }) worldAcceptUnmetTargets.Show();
        else
        {
            worldAcceptUnmetTargets.ButtonPressed = false;
            worldAcceptUnmetTargets.Hide();
        }
    }

    /// <summary>
    /// The preview described in words, such as "Plenty of forest and some mountain
    /// ranges, on 15,568 tiles of land", with the exact measurements in its tooltip.
    /// A map that misses the balance the default settings aim for says so plainly.
    /// </summary>
    internal static (string Text, string Details) WorldPreviewSummary(OwnerWorldPreview result)
    {
        var gather = $"{result.ResourceSites.ToString("N0", CultureInfo.InvariantCulture)} places to gather food and materials.";
        if (result.Coverage is not { } coverage) return (gather, string.Empty);
        var land = coverage.DryLandTiles.ToString("N0", CultureInfo.InvariantCulture);
        var text = $"{ForestAmount(coverage.ForestPercent)} and {MountainAmount(coverage.MountainPercent)}, on {land} tiles of land. ";
        List<string> misses = [];
        if (coverage.ForestTargetApplicable && !coverage.ForestTargetMet)
            misses.Add(coverage.ForestPercent < ForestBand.Min ? "less forest" : "more forest");
        if (coverage.MountainTargetApplicable && !coverage.MountainTargetMet)
            misses.Add(coverage.MountainPercent < MountainBand.Min ? "fewer mountains" : "more mountains");
        if (misses.Count > 0)
            text += $"It has {string.Join(" and ", misses)} than a balanced world. Try another seed, or keep this map below. ";
        var details = $"Forest {Percent(coverage.ForestPercent)} and mountains {Percent(coverage.MountainPercent)} of the land.";
        if (coverage.TargetsApplicable)
            details += $" A balanced world has {ForestBand.Min}–{ForestBand.Max}% forest and {MountainBand.Min}–{MountainBand.Max}% mountains.";
        // Every map tried, when there was a choice, so a miss can be checked against the others.
        // An attempt that could not be used keeps its place in the order tried, without invented figures.
        var tried = result.Candidates
            .Select(candidate => (candidate.Attempt,
                Text: $"{Percent(candidate.ForestPercent)} forest, {Percent(candidate.MountainPercent)} mountains"))
            .Concat(result.FailedCandidates.Select(candidate => (candidate.Attempt,
                Text: candidate.Reason == "no-clearing" ? "no room for a first Town" : "unavailable")))
            .OrderBy(candidate => candidate.Attempt).Select(candidate => candidate.Text).ToList();
        if (tried.Count > 1)
            details += $" Closest of {tried.Count.ToString(CultureInfo.InvariantCulture)} maps tried: " +
                string.Join("; ", tried) + ".";
        return (text + gather, details);
    }

    // The default Balanced settings aim for these shares of dry land; the host chooses the closest map.
    private static readonly (int Min, int Max) ForestBand = (20, 40);
    private static readonly (int Min, int Max) MountainBand = (5, 12);

    private static string Percent(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string ForestAmount(double percent) => percent switch
    {
        < 5 => "Almost no forest",
        < 20 => "Some forest",
        <= 40 => "Plenty of forest",
        _ => "Thick forest",
    };

    private static string MountainAmount(double percent) => percent switch
    {
        < 1 => "no mountains to speak of",
        < 5 => "a few mountains",
        <= 12 => "some mountain ranges",
        _ => "many mountains",
    };

    private async Task CreateSelectedWorldAsync()
    {
        if (worldMenuBusy || isOwnerAction || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var name = worldNameInput.Text.Trim();
        var seed = worldSeedInput.Text.Trim();
        if (name.Length is < 1 or > 80 || seed.Length is < 1 or > 100 ||
            name.Any(char.IsControl) || seed.Any(char.IsControl))
        {
            worldMenuStatus.Text = "Enter a world name (1–80 characters) and seed (1–100 characters).";
            return;
        }
        var action = CurrentWorldOptions();
        if (!CanCreatePreview(action))
        {
            worldPreviewStatus.Text = previewedWorldResult?.Coverage is { TargetsApplicable: true, MeetsTargets: false }
                ? "Tick Keep this map anyway, or choose a new seed, before creating the world."
                : "Preview the map before creating the world.";
            RefreshWorldMenuAvailability();
            return;
        }
        var preview = previewedWorldResult!;
        var createAction = action with
        {
            CandidateAttempt = preview.Coverage!.Attempt,
            ExpectedManifestDigest = preview.ManifestDigest,
            ExpectedMapLayersDigest = preview.MapLayersDigest,
            AcceptUnmetTargets = !preview.Coverage.MeetsTargets && preview.Coverage.TargetsApplicable &&
                worldAcceptUnmetTargets.ButtonPressed,
        };
        await RunWorldMenuActionAsync(async () =>
        {
            worldMenuStatus.Text = "Generating world...";
            try
            {
                await AwaitCurrentWorldResultAsync(ownerApi.SetPausedAsync(ResolveWorldUri(), authority, deviceId, true,
                    signer, CancellationToken.None));
                resumeWorldOnContinue = false;
                await observationSession.ChangeTimelineAsync(() => ownerApi.CreateWorldAsync(ResolveWorldUri(), authority, deviceId,
                    createAction, signer, CancellationToken.None));
                worldMenuOverlay.Hide();
                await EnterWorldAsync();
            }
            catch (ObsoleteWorldRequestException) { }
            catch (Exception exception)
            {
                worldMenuStatus.Text = "Could not create world: " + FriendlyFailure(exception);
            }
        });
    }

    private async Task SelectListedWorldAsync()
    {
        if (worldMenuBusy || isOwnerAction || SelectedListedWorld() is not { } world ||
            world.Compatibility == "incompatible" ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        // The catalog and selection can change while the pause response is pending.
        // Keep the player's chosen identity, never its position in the list.
        var worldId = world.Id;
        var server = ResolveWorldUri();
        await RunWorldMenuActionAsync(async () =>
        {
            worldMenuStatus.Text = "Opening world...";
            try
            {
                await AwaitCurrentWorldResultAsync(ownerApi.SetPausedAsync(server, authority, deviceId, true,
                    signer, CancellationToken.None));
                resumeWorldOnContinue = false;
                await observationSession.ChangeTimelineAsync(() => ownerApi.SelectWorldAsync(server, authority, deviceId,
                    worldId, signer, CancellationToken.None));
                worldMenuOverlay.Hide();
                await EnterWorldAsync();
            }
            catch (ObsoleteWorldRequestException) { }
            catch (Exception exception)
            {
                worldMenuStatus.Text = "Could not open world: " + FriendlyFailure(exception);
            }
        });
    }

    /// <summary>Menu buttons carry the same pixel icons as the HUD, redrawn for the current theme.</summary>
    private void RefreshMenuIcons()
    {
        var palette = UiTheme.Current;
        var scale = HudIconScale;
        var dark = palette.Name == "dark";
        var wood = dark ? new Color("C99A62") : new Color("9C6C42");
        var green = dark ? new Color("8DBA6A") : palette.Primary;
        var warm = dark ? new Color("D89A5A") : new Color("B8733A");
        var play = PixelIcons.Texture(PixelGlyph.Play, palette.PrimaryInk, palette.PrimaryInk, scale);
        mainMenuContinueButton.Icon = play;
        menuResumeButton.Icon = play;
        mainMenuNewButton.Icon = PixelIcons.Themed(PixelGlyph.Globe, green, scale);
        mainMenuLoadButton.Icon = PixelIcons.Themed(PixelGlyph.Folder, warm, scale);
        mainMenuSettingsButton.Icon = PixelIcons.Themed(PixelGlyph.Gear, wood, scale);
        mainMenuConnectButton.Icon = PixelIcons.Themed(PixelGlyph.Link, green, scale);
        settingsButton.Icon = PixelIcons.Themed(PixelGlyph.Gear, wood, scale);
        menuSaveWorldButton.Icon = PixelIcons.Themed(PixelGlyph.Book, wood, scale);
        modLibraryButton.Icon = PixelIcons.Themed(PixelGlyph.Box, wood, scale);
        var door = PixelIcons.Themed(PixelGlyph.Door, palette.Bad, scale);
        menuQuitToMainButton.Icon = door;
        quitGameButton.Icon = door;
    }
}
