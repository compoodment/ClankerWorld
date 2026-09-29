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
    private readonly OptionButton worldSizeChoice = new();
    private readonly OptionButton worldWaterChoice = new();
    private readonly OptionButton worldResourceChoice = new();
    private readonly OptionButton worldClimateModeChoice = new();
    private readonly OptionButton worldClimateFamilyChoice = new();
    private readonly CheckBox worldWrapChoice = new();
    private readonly CheckBox worldLatitudeChoice = new();
    private readonly WorldOverview worldPreview = new();
    private readonly Label worldPreviewStatus = new();
    private readonly Button worldPreviewButton = new();
    private readonly ItemList worldSelectionList = new();
    private readonly Button worldCreateButton = new();
    private readonly Button worldSelectButton = new();
    private CatalogWorld[] listedWorlds = [];
    private OwnerWorldCreationAction? previewedWorldOptions;
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
        AddChild(mainMenuOverlay);

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
        StyleButton(mainMenuContinueButton, primary: true);
        mainMenuContinueButton.Pressed += () => _ = EnterWorldAsync();
        body.AddChild(mainMenuContinueButton);

        mainMenuNewButton.Text = "New World";
        StyleButton(mainMenuNewButton);
        mainMenuNewButton.Pressed += () => OpenWorldMenu(create: true);
        body.AddChild(mainMenuNewButton);

        mainMenuLoadButton.Text = "Load World";
        mainMenuLoadButton.TooltipText = "Choose a world to play. Named saves are in each world's Pause Menu.";
        StyleButton(mainMenuLoadButton);
        mainMenuLoadButton.Pressed += () => OpenWorldMenu(create: false);
        body.AddChild(mainMenuLoadButton);

        mainMenuSettingsButton.Text = "Settings";
        StyleButton(mainMenuSettingsButton);
        mainMenuSettingsButton.Pressed += OpenMainMenuSettings;
        body.AddChild(mainMenuSettingsButton);

        mainMenuConnectButton.Text = "Connect / Pair development host";
        StyleButton(mainMenuConnectButton);
        mainMenuConnectButton.Pressed += () => _ = OpenMainMenuConnectionAsync();
        body.AddChild(mainMenuConnectButton);

        quitGameButton.Text = "Quit Game";
        StyleButton(quitGameButton);
        quitGameButton.Pressed += () => quitGameConfirmation.PopupCentered(new Vector2I(440, 170));
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
        if (area.X <= 0 || area.Y <= 0) area = GetViewportRect().Size;
        var scale = Math.Clamp((int)Math.Min(area.X * 0.62f / MenuLogo.Width, area.Y * 0.24f / MenuLogo.Height), 1, 8);
        mainMenuLogo.CustomMinimumSize = new Vector2(MenuLogo.Width, MenuLogo.Height) * scale;
    }

    private async Task EnterWorldAsync()
    {
        if (registration is null || deviceKey is null || registeredEndpointInvalid) return;
        mainMenuContinueButton.Disabled = true;
        var previousRefreshCount = successfulRefreshCount;
        await RefreshAsync();
        if (successfulRefreshCount == previousRefreshCount)
        {
            RefreshMainMenuAvailability();
            SetMainMenuStatus("Could not reach your world. Check your connection and try Continue again.");
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
        menuHeadingLabel.Text = "Game Settings";
        menuCloseButton.Text = "<";
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

    private void QuitToMainMenu()
    {
        // Opening the pause menu already committed a pause on the host. Stop
        // owner polling while the title screen is open, so no model work runs.
        if (observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != true &&
            !menuPauseConfirmed)
        {
            SetStatus("Wait a moment for the world to pause before leaving.", good: false);
            return;
        }
        resumeWorldOnContinue = menuPausedWorld;
        CloseGameMenu();
        ShowMainMenu();
    }

    private void SetWorldMenuActionsVisible(bool visible)
    {
        menuResumeButton.Visible = !visible;
        menuSaveWorldButton.Visible = visible;
        settingsButton.Visible = visible;
        modLibraryButton.Visible = visible;
        worldSettingsCategoryButton.Visible = visible;
        developerToggleButton.Visible = visible;
        menuQuitToMainButton.Visible = visible;
        menuQuitSeparator.Visible = visible;
        modLibraryPanel.Hide();
    }

    private void BuildWorldMenu()
    {
        worldMenuOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.MouseFilter = MouseFilterEnum.Stop;
        worldMenuOverlay.ZIndex = 210;
        AddChild(worldMenuOverlay);
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
        worldMenuHeading.AddThemeFontSizeOverride("font_size", 24);
        worldMenuBody.AddChild(worldMenuHeading);
        worldMenuStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        worldMenuBody.AddChild(worldMenuStatus);

        var options = new VBoxContainer { CustomMinimumSize = new Vector2(WorldOptionsWidth, 0) };
        options.AddThemeConstantOverride("separation", 8);
        // Both fields open pre-filled, which hides their placeholders, so each
        // keeps a visible caption.
        worldNameInput.PlaceholderText = "World name";
        worldNameInput.MaxLength = 80;
        options.AddChild(WorldOptionRow("Name", worldNameInput));
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
        options.AddChild(seedRow);
        worldSizeChoice.AddItem("Small · 256 × 128", 0);
        worldSizeChoice.AddItem("Medium · 512 × 256", 1);
        worldSizeChoice.ItemSelected += _ => InvalidateWorldPreview();
        options.AddChild(WorldOptionRow("Size", worldSizeChoice));
        worldWaterChoice.AddItem("Less · 35%", 35);
        worldWaterChoice.AddItem("Balanced · 45%", 45);
        worldWaterChoice.AddItem("More · 55%", 55);
        worldWaterChoice.Select(1);
        worldWaterChoice.ItemSelected += _ => InvalidateWorldPreview();
        options.AddChild(WorldOptionRow("Water", worldWaterChoice));
        worldResourceChoice.AddItem("Sparse", 0);
        worldResourceChoice.AddItem("Normal", 1);
        worldResourceChoice.AddItem("Abundant", 2);
        worldResourceChoice.Select(1);
        worldResourceChoice.ItemSelected += _ => InvalidateWorldPreview();
        options.AddChild(WorldOptionRow("Resources", worldResourceChoice));
        worldClimateModeChoice.AddItem("Balanced", 0);
        worldClimateModeChoice.AddItem("Uniform", 1);
        worldClimateModeChoice.AddItem("Dominant", 2);
        worldClimateModeChoice.ItemSelected += _ =>
        {
            worldClimateFamilyChoice.GetParent<Control>().Visible = worldClimateModeChoice.GetSelectedId() != 0;
            InvalidateWorldPreview();
        };
        options.AddChild(WorldOptionRow("Climates", worldClimateModeChoice));
        worldClimateFamilyChoice.AddItem("Tropical", 0);
        worldClimateFamilyChoice.AddItem("Dry", 1);
        worldClimateFamilyChoice.AddItem("Temperate", 2);
        worldClimateFamilyChoice.AddItem("Cold", 3);
        worldClimateFamilyChoice.AddItem("Polar", 4);
        worldClimateFamilyChoice.Select(2);
        worldClimateFamilyChoice.ItemSelected += _ => InvalidateWorldPreview();
        var familyRow = WorldOptionRow("Main climate", worldClimateFamilyChoice);
        familyRow.Visible = false;
        options.AddChild(familyRow);
        worldLatitudeChoice.Text = "Colder toward the poles";
        worldLatitudeChoice.ButtonPressed = true;
        worldLatitudeChoice.Toggled += _ => InvalidateWorldPreview();
        options.AddChild(worldLatitudeChoice);
        worldWrapChoice.Text = "Wrap east/west";
        worldWrapChoice.ButtonPressed = true;
        worldWrapChoice.Toggled += _ => InvalidateWorldPreview();
        options.AddChild(worldWrapChoice);

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
        worldPreviewStatus.CustomMinimumSize = new Vector2(0, 44);
        previewColumn.AddChild(worldPreviewStatus);

        worldMenuColumns.AddThemeConstantOverride("h_separation", 20);
        worldMenuColumns.AddThemeConstantOverride("v_separation", 12);
        worldMenuColumns.AddChild(options);
        worldMenuColumns.AddChild(previewColumn);
        worldMenuBody.AddChild(worldMenuColumns);

        worldSelectionList.CustomMinimumSize = new Vector2(0, 300);
        worldSelectionList.ItemSelected += index =>
        {
            var world = listedWorlds[(int)index];
            worldSelectButton.Disabled = world.Compatibility == "incompatible";
            worldMenuStatus.Text = world.Compatibility == "incompatible"
                ? "Cannot open this world: " + (world.CompatibilityReason ?? "It was made with a different version.") + " Your save is safe."
                : world.Compatibility == "unknown"
                    ? "Could not check this world. Opening it will try the saved copy and will not delete anything."
                    : "This world is ready to open.";
        };
        worldSelectionList.ItemActivated += index => _ = SelectListedWorldAsync();
        worldSelectionList.Hide();
        worldMenuBody.AddChild(worldSelectionList);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        worldBackButton.Text = "Back";
        StyleButton(worldBackButton);
        worldBackButton.CustomMinimumSize = new Vector2(110, 38);
        worldBackButton.Pressed += () => { if (!worldMenuBusy) worldMenuOverlay.Hide(); };
        actions.AddChild(worldBackButton);
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        worldPreviewButton.Text = "Preview again";
        worldPreviewButton.TooltipText = "The preview updates by itself when you change an option. Use this if it failed.";
        StyleButton(worldPreviewButton);
        worldPreviewButton.CustomMinimumSize = new Vector2(0, 38);
        worldPreviewButton.Pressed += () => _ = PreviewWorldAsync();
        actions.AddChild(worldPreviewButton);
        worldCreateButton.Text = "Create World";
        StyleButton(worldCreateButton, primary: true);
        worldCreateButton.CustomMinimumSize = new Vector2(170, 38);
        worldCreateButton.Pressed += () => _ = CreateSelectedWorldAsync();
        worldCreateButton.Disabled = true;
        actions.AddChild(worldCreateButton);
        worldSelectButton.Text = "Open World";
        StyleButton(worldSelectButton, primary: true);
        worldSelectButton.CustomMinimumSize = new Vector2(170, 38);
        worldSelectButton.Pressed += () => _ = SelectListedWorldAsync();
        worldSelectButton.Hide();
        actions.AddChild(worldSelectButton);
        worldMenuBody.AddChild(actions);

        worldMenuBody.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        worldMenuScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        worldMenuScroll.AddChild(worldMenuBody);
        AddPanelContents(worldMenuCard, worldMenuScroll);
        worldMenuOverlay.VisibilityChanged += () => Callable.From(LayoutWorldMenu).CallDeferred();
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
        var viewport = GetViewportRect().Size;
        var uiScale = DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent);
        const float margins = 20;
        var optionsWidth = WorldOptionsWidth * uiScale;
        var previewWidth = WorldPreviewWidth * uiScale;
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
        worldMenuHeading.Text = create ? "New World" : "Load World";
        worldMenuStatus.Text = create
            ? "Choose a seed and size. Then choose your first Town's site and add four founders before starting time."
            : "Choose a world. The current world is saved before switching.";
        worldMenuColumns.Visible = create;
        worldPreviewButton.Visible = create;
        worldPreview.Visible = create && previewedWorldOptions is not null;
        worldCreateButton.Visible = create;
        worldSelectionList.Visible = !create;
        worldSelectButton.Visible = !create;
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
        worldMenuStatus.Text = "Loading worlds…";
        try
        {
            var catalog = await ownerApi.ListWorldsAsync(ResolveWorldUri(), authority,
                deviceId, signer, CancellationToken.None);
            listedWorlds = catalog.Worlds.OrderByDescending(world => world.Id == catalog.ActiveId)
                .ThenByDescending(world => world.UpdatedUtc).ToArray();
            worldSelectionList.Clear();
            foreach (var world in listedWorlds)
            {
                // A compatible world needs no label; the others say what that means.
                var state = world.Compatibility switch
                {
                    "incompatible" => "  ·  can't open in this version",
                    "unknown" => "  ·  not checked yet",
                    _ => string.Empty,
                };
                var row = worldSelectionList.AddItem(world.Name +
                    (world.Id == catalog.ActiveId ? "  ·  current" : string.Empty) +
                    "  ·  saved " + world.UpdatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + state);
                if (world.Compatibility == "incompatible")
                    worldSelectionList.SetItemCustomFgColor(row, UiTheme.Current.InkMuted);
            }
            worldMenuStatus.Text = listedWorlds.Length == 0 ? "No worlds yet." : "Choose a world. Double-click to open it.";
        }
        catch (Exception exception)
        {
            worldMenuStatus.Text = "Could not list worlds: " + FriendlyFailure(exception);
        }
    }

    private OwnerWorldCreationAction CurrentWorldOptions() => new(
        worldNameInput.Text.Trim(), worldSeedInput.Text.Trim(),
        worldSizeChoice.GetSelectedId() == 1 ? "Medium" : "Small",
        worldWaterChoice.GetSelectedId(), worldWrapChoice.ButtonPressed,
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
        worldResourceChoice.GetSelectedId() switch { 0 => "Sparse", 2 => "Abundant", _ => "Normal" });

    private static bool SameGeneration(OwnerWorldCreationAction? first, OwnerWorldCreationAction second) =>
        first is not null && first.Seed == second.Seed && first.Size == second.Size &&
        first.WaterPercent == second.WaterPercent && first.WrapEastWest == second.WrapEastWest &&
        first.ClimateMode == second.ClimateMode && first.SelectedClimate == second.SelectedClimate &&
        first.LatitudeCooling == second.LatitudeCooling &&
        first.ResourceAbundance == second.ResourceAbundance;

    private void InvalidateWorldPreview(bool refresh = true)
    {
        var revision = ++worldPreviewRevision;
        previewedWorldOptions = null;
        worldCreateButton.Disabled = true;
        worldPreview.Hide();
        worldPreviewStatus.Text = "Updating the preview…";
        if (refresh && worldMenuOverlay.Visible)
            _ = RefreshWorldPreviewAfterChangeAsync(revision);
    }

    private async Task RefreshWorldPreviewAfterChangeAsync(int revision)
    {
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        while (worldMenuBusy && IsInsideTree() && worldMenuOverlay.Visible && revision == worldPreviewRevision)
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree() || !worldMenuOverlay.Visible || revision != worldPreviewRevision ||
            SameGeneration(previewedWorldOptions, CurrentWorldOptions()))
            return;
        await PreviewWorldAsync();
    }

    private async Task PreviewWorldAsync()
    {
        if (worldMenuBusy || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
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
        worldPreviewStatus.Text = "Generating map preview…";
        try
        {
            var result = await ownerApi.PreviewWorldAsync(ResolveWorldUri(), authority,
                deviceId, action, signer, CancellationToken.None);
            if (!SameGeneration(action, CurrentWorldOptions()))
            {
                return;
            }
            worldPreview.MarkerTile = null;
            worldPreview.SetWorld(WorldTerrainMap.FromPacked(result.Terrain, result.PackedMapLayers));
            worldPreview.Show();
            previewedWorldOptions = action;
            worldCreateButton.Disabled = false;
            worldPreviewStatus.Text = $"Map preview · {result.ResourceSites} resource sites. " +
                "You will choose where your first Town goes after creating the world.";
        }
        catch (Exception exception)
        {
            if (SameGeneration(action, CurrentWorldOptions()))
            {
                InvalidateWorldPreview(refresh: false);
                worldPreviewStatus.Text = "Could not preview map: " + FriendlyFailure(exception);
            }
        }
        finally
        {
            worldMenuBusy = false;
            worldPreviewButton.Disabled = false;
        }
    }

    private async Task CreateSelectedWorldAsync()
    {
        if (worldMenuBusy || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var name = worldNameInput.Text.Trim();
        var seed = worldSeedInput.Text.Trim();
        if (name.Length is < 1 or > 80 || seed.Length is < 1 or > 100 ||
            name.Any(char.IsControl) || seed.Any(char.IsControl))
        {
            worldMenuStatus.Text = "Enter a world name (1–80 characters) and seed (1–100 characters).";
            return;
        }
        var action = CurrentWorldOptions();
        if (!SameGeneration(previewedWorldOptions, action))
        {
            worldPreviewStatus.Text = "Preview the map before creating the world.";
            worldCreateButton.Disabled = true;
            return;
        }
        worldMenuBusy = true;
        worldCreateButton.Disabled = true;
        worldMenuStatus.Text = "Generating world…";
        try
        {
            await ownerApi.SetPausedAsync(ResolveWorldUri(), authority, deviceId, true,
                signer, CancellationToken.None);
            resumeWorldOnContinue = false;
            await observationSession.ChangeTimelineAsync(() => ownerApi.CreateWorldAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None));
            worldMenuOverlay.Hide();
            await EnterWorldAsync();
        }
        catch (Exception exception)
        {
            worldMenuStatus.Text = "Could not create world: " + FriendlyFailure(exception);
        }
        finally
        {
            worldMenuBusy = false;
            worldCreateButton.Disabled = !SameGeneration(previewedWorldOptions, CurrentWorldOptions());
        }
    }

    private async Task SelectListedWorldAsync()
    {
        if (worldMenuBusy || worldSelectionList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedWorlds.Length ||
            listedWorlds[selected[0]].Compatibility == "incompatible" ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        worldMenuBusy = true;
        worldSelectButton.Disabled = true;
        worldMenuStatus.Text = "Opening world…";
        try
        {
            await ownerApi.SetPausedAsync(ResolveWorldUri(), authority, deviceId, true,
                signer, CancellationToken.None);
            resumeWorldOnContinue = false;
            await observationSession.ChangeTimelineAsync(() => ownerApi.SelectWorldAsync(ResolveWorldUri(), authority, deviceId,
                listedWorlds[selected[0]].Id, signer, CancellationToken.None));
            worldMenuOverlay.Hide();
            await EnterWorldAsync();
        }
        catch (Exception exception)
        {
            worldMenuStatus.Text = "Could not open world: " + FriendlyFailure(exception);
        }
        finally
        {
            worldMenuBusy = false;
            worldSelectButton.Disabled = listedWorlds[selected[0]].Compatibility == "incompatible";
        }
    }

    /// <summary>Menu buttons carry the same pixel icons as the HUD, redrawn for the current theme.</summary>
    private void RefreshMenuIcons()
    {
        foreach (var button in new[] { mainMenuContinueButton, mainMenuNewButton, mainMenuLoadButton, mainMenuSettingsButton,
                     mainMenuConnectButton, quitGameButton, menuResumeButton, menuSaveWorldButton, settingsButton,
                     modLibraryButton, menuQuitToMainButton })
            button.Alignment = HorizontalAlignment.Left;
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
        worldBackButton.Icon = PixelIcons.Themed(PixelGlyph.Back, palette.Ink, scale);
    }
}
