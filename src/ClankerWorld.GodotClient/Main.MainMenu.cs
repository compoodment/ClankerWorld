using Godot;
using ClankerWorld.GodotClient.UI;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Control mainMenuOverlay = new();
    private readonly ColorRect mainMenuBackground = new();
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

    private void BuildMainMenu()
    {
        mainMenuOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.MouseFilter = MouseFilterEnum.Stop;
        mainMenuOverlay.ZIndex = 180;
        AddChild(mainMenuOverlay);

        mainMenuBackground.Color = new Color("0D151C");
        mainMenuBackground.MouseFilter = MouseFilterEnum.Stop;
        mainMenuBackground.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.AddChild(mainMenuBackground);

        mainMenuCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mainMenuOverlay.AddChild(mainMenuCenter);
        mainMenuCenter.AddChild(mainMenuCard);

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        body.AddThemeConstantOverride("separation", 12);
        var title = new Label { Text = "CLANKERWORLD", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 35);
        title.AddThemeColorOverride("font_color", new Color("F4F0E3"));
        body.AddChild(title);
        body.AddChild(new Label
        {
            Text = "A world shaped by the people who live in it",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("AFC4BA"),
        });

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
        mainMenuLoadButton.TooltipText = "Choose a world. Named checkpoints remain inside each world's pause menu.";
        StyleButton(mainMenuLoadButton);
        mainMenuLoadButton.Pressed += () => OpenWorldMenu(create: false);
        body.AddChild(mainMenuLoadButton);

        var settings = new Button { Text = "Settings" };
        StyleButton(settings);
        settings.Pressed += OpenMainMenuSettings;
        body.AddChild(settings);

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

        quitToMenuConfirmation.Title = "Quit to Main Menu?";
        quitToMenuConfirmation.DialogText = "Leave this world and return to the Main Menu? The simulation will remain paused until you continue it.";
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
        mainMenuStatus.Text = paired
            ? "Continue your current world, create another, or load a different world."
            : "Connect or pair this device to the private development world. No model key is needed to open the game.";
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
            mainMenuStatus.Text = "Could not reach the development world. Check its connection and try Continue again.";
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
            SetStatus("Wait for the host to confirm the pause before leaving this world.", good: false);
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
        modLibraryPanel.Hide();
    }

    private void BuildWorldMenu()
    {
        worldMenuOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.MouseFilter = MouseFilterEnum.Stop;
        worldMenuOverlay.ZIndex = 210;
        AddChild(worldMenuOverlay);
        var shade = new ColorRect { Color = new Color("071015E0"), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMenuOverlay.AddChild(center);
        center.AddChild(worldMenuCard);

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        body.AddThemeConstantOverride("separation", 8);
        worldMenuHeading.AddThemeFontSizeOverride("font_size", 24);
        body.AddChild(worldMenuHeading);
        worldMenuStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(worldMenuStatus);
        worldNameInput.PlaceholderText = "World name";
        worldNameInput.MaxLength = 80;
        body.AddChild(worldNameInput);
        worldSeedInput.PlaceholderText = "Generation seed";
        worldSeedInput.MaxLength = 100;
        worldSeedInput.TextChanged += _ => InvalidateWorldPreview();
        var seedRow = new HBoxContainer();
        worldSeedInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        seedRow.AddChild(worldSeedInput);
        var reroll = new Button { Text = "Reroll seed" };
        StyleButton(reroll);
        reroll.Pressed += () =>
        {
            worldSeedInput.Text = Guid.NewGuid().ToString("N")[..12];
            if (!worldMenuBusy) _ = PreviewWorldAsync();
        };
        seedRow.AddChild(reroll);
        body.AddChild(seedRow);
        worldSizeChoice.AddItem("Small · 256 × 128", 0);
        worldSizeChoice.AddItem("Medium · 512 × 256", 1);
        worldSizeChoice.ItemSelected += _ => InvalidateWorldPreview();
        body.AddChild(worldSizeChoice);
        worldWaterChoice.AddItem("Less water · 35%", 35);
        worldWaterChoice.AddItem("Balanced water · 45%", 45);
        worldWaterChoice.AddItem("More water · 55%", 55);
        worldWaterChoice.Select(1);
        worldWaterChoice.ItemSelected += _ => InvalidateWorldPreview();
        body.AddChild(worldWaterChoice);
        worldResourceChoice.AddItem("Sparse resources", 0);
        worldResourceChoice.AddItem("Normal resources", 1);
        worldResourceChoice.AddItem("Abundant resources", 2);
        worldResourceChoice.Select(1);
        worldResourceChoice.ItemSelected += _ => InvalidateWorldPreview();
        body.AddChild(worldResourceChoice);
        worldClimateModeChoice.AddItem("Balanced climates", 0);
        worldClimateModeChoice.AddItem("Uniform climate", 1);
        worldClimateModeChoice.AddItem("Dominant climate", 2);
        worldClimateModeChoice.ItemSelected += _ =>
        {
            worldClimateFamilyChoice.Visible = worldClimateModeChoice.GetSelectedId() != 0;
            InvalidateWorldPreview();
        };
        body.AddChild(worldClimateModeChoice);
        worldClimateFamilyChoice.AddItem("Tropical", 0);
        worldClimateFamilyChoice.AddItem("Dry", 1);
        worldClimateFamilyChoice.AddItem("Temperate", 2);
        worldClimateFamilyChoice.AddItem("Cold", 3);
        worldClimateFamilyChoice.AddItem("Polar", 4);
        worldClimateFamilyChoice.Select(2);
        worldClimateFamilyChoice.ItemSelected += _ => InvalidateWorldPreview();
        worldClimateFamilyChoice.Visible = false;
        body.AddChild(worldClimateFamilyChoice);
        worldLatitudeChoice.Text = "Colder toward the poles";
        worldLatitudeChoice.ButtonPressed = true;
        worldLatitudeChoice.Toggled += _ => InvalidateWorldPreview();
        body.AddChild(worldLatitudeChoice);
        worldWrapChoice.Text = "Wrap east/west";
        worldWrapChoice.ButtonPressed = true;
        worldWrapChoice.Toggled += _ => InvalidateWorldPreview();
        body.AddChild(worldWrapChoice);
        worldPreviewButton.Text = "Preview map";
        StyleButton(worldPreviewButton);
        worldPreviewButton.Pressed += () => _ = PreviewWorldAsync();
        body.AddChild(worldPreviewButton);
        worldPreview.ShowCameraBounds = false;
        worldPreview.MouseFilter = MouseFilterEnum.Ignore;
        worldPreview.TooltipText = "Generated map preview. Choose a Town site after creating the world.";
        worldPreview.CustomMinimumSize = new Vector2(400, 170);
        worldPreview.Hide();
        body.AddChild(worldPreview);
        worldPreviewStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(worldPreviewStatus);
        worldSelectionList.CustomMinimumSize = new Vector2(0, 240);
        worldSelectionList.ItemSelected += index =>
        {
            var world = listedWorlds[(int)index];
            worldSelectButton.Disabled = world.Compatibility == "incompatible";
            worldMenuStatus.Text = world.Compatibility == "incompatible"
                ? "Cannot open this world: " + (world.CompatibilityReason ?? "Its save is incompatible.") + " The save was preserved."
                : world.Compatibility == "unknown"
                    ? "Compatibility could not be assessed; opening will try the saved checkpoint without deleting it."
                    : "This world passed save and required-content checks.";
        };
        worldSelectionList.Hide();
        body.AddChild(worldSelectionList);
        worldCreateButton.Text = "Create World";
        StyleButton(worldCreateButton, primary: true);
        worldCreateButton.Pressed += () => _ = CreateSelectedWorldAsync();
        worldCreateButton.Disabled = true;
        body.AddChild(worldCreateButton);
        worldSelectButton.Text = "Open World";
        StyleButton(worldSelectButton, primary: true);
        worldSelectButton.Pressed += () => _ = SelectListedWorldAsync();
        worldSelectButton.Hide();
        body.AddChild(worldSelectButton);
        var back = new Button { Text = "Back" };
        StyleButton(back);
        back.Pressed += () => { if (!worldMenuBusy) worldMenuOverlay.Hide(); };
        body.AddChild(back);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(440, 570) };
        scroll.AddChild(body);
        AddPanelContents(worldMenuCard, scroll);
        worldMenuCard.CustomMinimumSize = new Vector2(480, 0);
        worldMenuOverlay.Hide();
    }

    private void OpenWorldMenu(bool create)
    {
        if (registration is null || deviceKey is null || registeredEndpointInvalid) return;
        worldMenuHeading.Text = create ? "New World" : "Load World";
        worldMenuStatus.Text = create
            ? "Choose a seed and size. Then choose your Town site and add four founders before starting time."
            : "Choose a world. The current world is saved before switching.";
        worldNameInput.Visible = create;
        worldSeedInput.GetParent<Control>().Visible = create;
        worldSizeChoice.Visible = create;
        worldWaterChoice.Visible = create;
        worldResourceChoice.Visible = create;
        worldClimateModeChoice.Visible = create;
        worldClimateFamilyChoice.Visible = create && worldClimateModeChoice.GetSelectedId() != 0;
        worldLatitudeChoice.Visible = create;
        worldWrapChoice.Visible = create;
        worldPreviewButton.Visible = create;
        worldPreviewStatus.Visible = create;
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
                worldSelectionList.AddItem(world.Name +
                    (world.Id == catalog.ActiveId ? " · current" : "") +
                    " · " + world.UpdatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) +
                    " · " + world.Compatibility);
            worldMenuStatus.Text = listedWorlds.Length == 0 ? "No worlds yet." : "Choose a world.";
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
        worldPreviewStatus.Text = "Updating map preview for this seed and its options…";
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
            worldPreviewStatus.Text = "Enter a world name and generation seed first.";
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
                "Choose your Town site after creating the world.";
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
            worldPreviewStatus.Text = "Preview the current seed and options before creating the world.";
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
            await ownerApi.CreateWorldAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None);
            observationSession.ResetAfterLoad();
            resumeWorldOnContinue = false;
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
            await ownerApi.SelectWorldAsync(ResolveWorldUri(), authority, deviceId,
                listedWorlds[selected[0]].Id, signer, CancellationToken.None);
            observationSession.ResetAfterLoad();
            resumeWorldOnContinue = false;
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
}
