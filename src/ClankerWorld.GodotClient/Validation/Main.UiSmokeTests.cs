using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyUiScaleAt1440pAsync(Window displayWindow)
    {
        var originalWindowSize = displayWindow.Size;
        var originalRenderSize = displayWindow.ContentScaleSize;
        var originalScaleMode = displayWindow.ContentScaleMode;
        var originalScaleAspect = displayWindow.ContentScaleAspect;
        var originalPreferences = displayPreferences;
        OpenMainMenuSettings();
        try
        {
            displayPreferences = originalPreferences with
            {
                UiScalePercent = 100,
                RenderWidth = 2560,
                RenderHeight = 1440,
                AutoRenderResolution = false,
            };
            ApplyUiScale(100);
            displayWindow.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            displayWindow.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            displayWindow.Size = new Vector2I(2560, 1440);
            displayWindow.ContentScaleSize = new Vector2I(2560, 1440);
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var smokeMap = new OwnerWorldSnapshot("ui-scale-smoke", 0, "ui-scale-map",
                Enumerable.Range(0, 16).Select(index => new OwnerWorldTile(index % 4, index / 4, "meadow")).ToArray(),
                [], [], null, 0)
            {
                PackedTerrain = new OwnerWorldPackedTerrain(4, 4, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[16])),
            };
            RenderMap(smokeMap);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var nativeRenderSize = displayWindow.ContentScaleSize;
            var baseSettingsFontSize = uiScaleChoice.GetThemeFontSize("font_size");
            var baseHudFontSize = clockLabel.GetThemeFontSize("font_size");
            var baseTextPanelFontSize = eventLog.GetThemeFontSize("normal_font_size");
            var baseResumeButtonHeight = menuResumeButton.GetCombinedMinimumSize().Y;
            var baseHudMinimumWidth = topBar.GetCombinedMinimumSize().X;
            var baseSettingsPanelWidth = gameMenuPanel.CustomMinimumSize.X;
            var baseTilePanelWidth = selectedTilePanel.CustomMinimumSize.X;
            var mapStageScale = mapStage.Scale;
            if (baseSettingsFontSize < 1 || baseHudFontSize < 1 || baseTextPanelFontSize < 1 || baseResumeButtonHeight < 1)
                throw new InvalidOperationException("UI Scale smoke check could not read the settings font size.");
            if (TileAtCanvas(mapStage.Position + new Vector2(currentTileSize * 1.5f, currentTileSize * 1.5f), smokeMap) != new Vector2I(1, 1))
                throw new InvalidOperationException("1440p UI Scale map input smoke check could not resolve its reference tile.");

            var scaleIndex = 1;
            foreach (var percent in DisplayUiScalePolicy.SupportedPercentages.Skip(1))
            {
                uiScaleChoice.Select(scaleIndex);
                SetUiScale(scaleIndex);
                RenderMap(smokeMap);
                for (var frame = 0; frame < 2; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                var expectedScale = DisplayUiScalePolicy.ScaleFactor(percent);
                if (!Mathf.IsEqualApprox(ThemeDB.FallbackBaseScale, expectedScale))
                    throw new InvalidOperationException($"UI Scale did not update Godot's fallback base scale to {percent}%.");
                if (uiScaleChoice.GetThemeFontSize("font_size") <= baseSettingsFontSize ||
                    clockLabel.GetThemeFontSize("font_size") <= baseHudFontSize ||
                    eventLog.GetThemeFontSize("normal_font_size") <= baseTextPanelFontSize)
                    throw new InvalidOperationException($"UI Scale {percent}% did not enlarge settings, HUD, and text-panel text.");
                if (displayWindow.Size != new Vector2I(2560, 1440) ||
                    displayWindow.ContentScaleSize != nativeRenderSize)
                    throw new InvalidOperationException($"UI Scale {percent}% changed the 1440p window or native render size.");
                if (mapStage.Scale != mapStageScale ||
                    TileAtCanvas(mapStage.Position + new Vector2(currentTileSize * 1.5f, currentTileSize * 1.5f), smokeMap) != new Vector2I(1, 1))
                    throw new InvalidOperationException($"UI Scale {percent}% changed the terrain transform or map interaction coordinates.");

                var settingsViewport = settingsScroll.GetGlobalRect();
                var scaleChoiceBounds = uiScaleChoice.GetGlobalRect();
                if (!mainMenuOverlay.GetGlobalRect().Encloses(gameMenuPanel.GetGlobalRect()) ||
                    scaleChoiceBounds.Position.X < settingsViewport.Position.X - 1 ||
                    scaleChoiceBounds.End.X > settingsViewport.End.X + 1)
                    throw new InvalidOperationException($"Game Settings escaped its usable bounds at 1440p and {percent}% UI Scale.");
                scaleIndex++;
            }

            if (menuResumeButton.GetCombinedMinimumSize().Y <= baseResumeButtonHeight ||
                topBar.GetCombinedMinimumSize().X <= baseHudMinimumWidth ||
                gameMenuPanel.CustomMinimumSize.X <= baseSettingsPanelWidth ||
                selectedTilePanel.CustomMinimumSize.X <= baseTilePanelWidth)
                throw new InvalidOperationException("UI Scale did not enlarge controls and panel geometry.");

            uiScaleChoice.Select(0);
            SetUiScale(0);
            var generatedMap = smokeMap with
            {
                WorldId = "zoom-bounds-smoke",
                PackedTerrain = new OwnerWorldPackedTerrain(256, 128, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[256 * 128])),
            };
            cameraZoom = 0.65f;
            RenderMap(generatedMap);
            if (mapStage.Size.Y * 0.7f < mapCanvas.Size.Y - 1)
                throw new InvalidOperationException("Small-map zoom-out exposed the north/south map edge at 1440p.");
            cameraZoom = maximumCameraZoom;
            RenderMap(generatedMap);
            var highResolutionVisibleRows = mapCanvas.Size.Y / currentTileSize;
            displayWindow.Size = new Vector2I(1280, 720);
            displayWindow.ContentScaleSize = new Vector2I(1280, 720);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            cameraZoom = maximumCameraZoom;
            RenderMap(generatedMap);
            var lowResolutionVisibleRows = mapCanvas.Size.Y / currentTileSize;
            if (Math.Abs(highResolutionVisibleRows - lowResolutionVisibleRows) > 2)
                throw new InvalidOperationException($"Maximum zoom-in showed different world heights at 1440p and 720p: {highResolutionVisibleRows:0.0} versus {lowResolutionVisibleRows:0.0} rows.");
        }
        finally
        {
            displayWindow.Size = originalWindowSize;
            displayWindow.ContentScaleMode = originalScaleMode;
            displayWindow.ContentScaleAspect = originalScaleAspect;
            displayWindow.ContentScaleSize = originalRenderSize;
            SaveDisplayPreferences(originalPreferences);
            ApplyUiScale(originalPreferences.UiScalePercent);
            RefreshRenderResolutionOptions();
        }
    }

    private async Task VerifyMenuBackdropAsync()
    {
        var originalPalette = UiTheme.Current;
        try
        {
            if (!mainMenuBackdrop.IsVisibleInTree() || mainMenuBackdrop.MouseFilter != MouseFilterEnum.Ignore ||
                !mainMenuBackdrop.GetGlobalRect().Grow(1).Encloses(mainMenuOverlay.GetGlobalRect()))
                throw new InvalidOperationException("The Main Menu backdrop must fill the title screen without taking clicks.");
            UiTheme.Apply(GetTree().Root, UiTheme.Light);
            var day = mainMenuBackdrop.Scene;
            if (mainMenuBackdrop.Night || day.Night || day.Stars.Count != 0 || day.Glows.Count != 0 ||
                day.Land.GetWidth() != MenuScene.Width || day.Land.GetHeight() != MenuScene.Height ||
                day.Land.GetPixel(0, 0).A != 0 || day.Land.GetPixel(0, MenuScene.Height - 1).A < 1 ||
                day.Clouds.Count == 0 || day.Chimneys.Count == 0 || day.Sparkles.Count == 0)
                throw new InvalidOperationException("The Light theme must show the daytime valley with clouds, smoke and river sparkles.");
            if (!MenuScene.Create(night: false).Land.GetData().AsSpan().SequenceEqual(day.Land.GetData()))
                throw new InvalidOperationException("The Main Menu backdrop must draw the same picture every time.");
            UiTheme.Apply(GetTree().Root, UiTheme.Dark);
            var dusk = mainMenuBackdrop.Scene;
            if (!mainMenuBackdrop.Night || !dusk.Night || dusk.Stars.Count == 0 || dusk.Glows.Count == 0 ||
                dusk.FireflyHomes.Count == 0 || dusk.MoonReflection.Count == 0)
                throw new InvalidOperationException("The Dark theme must switch the backdrop to dusk with stars, lights and fireflies.");
            var before = mainMenuBackdrop.AnimationTime;
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (mainMenuBackdrop.AnimationTime <= before)
                throw new InvalidOperationException("The Main Menu backdrop must animate while the title screen is open.");
            mainMenuOverlay.Hide();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hidden = mainMenuBackdrop.AnimationTime;
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (mainMenuBackdrop.AnimationTime != hidden)
                throw new InvalidOperationException("The Main Menu backdrop must hold still while it is hidden.");
        }
        finally
        {
            mainMenuOverlay.Show();
            UiTheme.Apply(GetTree().Root, originalPalette);
        }
    }

    /// <summary>The logo replaces the old title and slogan, sits above the card and stays crisp.</summary>
    private void VerifyMainMenuLogo(Vector2I size)
    {
        var logo = mainMenuLogo.GetGlobalRect();
        var scale = logo.Size.X / MenuLogo.Width;
        if (!mainMenuLogo.IsVisibleInTree() || mainMenuLogo.Texture.GetWidth() != MenuLogo.Width ||
            mainMenuLogo.Texture.GetHeight() != MenuLogo.Height || scale < 2 || scale != Mathf.Floor(scale) ||
            logo.Size.Y != MenuLogo.Height * scale || logo.End.Y > mainMenuCard.GetGlobalRect().Position.Y ||
            !mainMenuOverlay.GetGlobalRect().Encloses(logo))
            throw new InvalidOperationException($"At {size} the logo must sit above the menu card at a whole-number scale: logo={logo} card={mainMenuCard.GetGlobalRect()}.");
        if (mainMenuCard.FindChildren("*", nameof(Label), true, false).OfType<Label>().Any(label =>
                label.Visible && (label.Text == "CLANKERWORLD" || label.Text.StartsWith("A world shaped", StringComparison.Ordinal))))
            throw new InvalidOperationException("The Main Menu must show the logo instead of a text title or slogan.");
        if (!mainMenuStatus.Visible || mainMenuStatus.Text != "Connect this device to your world to play.")
            throw new InvalidOperationException("An unpaired Main Menu must say how to start playing.");
        SetMainMenuStatus(null);
        if (mainMenuStatus.Visible)
            throw new InvalidOperationException("The Main Menu status line must disappear when nothing needs attention.");
        RefreshMainMenuAvailability();
        if (!MenuLogo.Create().GetData().AsSpan().SequenceEqual(((ImageTexture)mainMenuLogo.Texture).GetImage().GetData()))
            throw new InvalidOperationException("The logo must be drawn the same way every time.");
        foreach (var iconSize in MenuLogo.IconSizes)
        {
            var icon = MenuLogo.Icon(iconSize);
            if (icon.GetWidth() != iconSize || icon.GetHeight() != iconSize || icon.GetPixel(iconSize / 2, iconSize / 2).A < 1)
                throw new InvalidOperationException($"The {iconSize} px window icon must be a filled square image.");
        }
        VerifyAppIconFile();
    }

    /// <summary>The committed Windows program icon must still match the logo art.</summary>
    private static void VerifyAppIconFile()
    {
        const string stale = "The Windows program icon is out of date. Run: godot --headless --path src/ClankerWorld.GodotClient -- --write-app-icon";
        var file = Godot.FileAccess.GetFileAsBytes(AppIconPath);
        if (file.Length < 6 || BitConverter.ToUInt16(file, 2) != 1 || BitConverter.ToUInt16(file, 4) != MenuLogo.IconSizes.Length)
            throw new InvalidOperationException(stale);
        for (var i = 0; i < MenuLogo.IconSizes.Length; i++)
        {
            var entry = 6 + 16 * i;
            var size = file[entry] == 0 ? 256 : file[entry];
            var length = (int)BitConverter.ToUInt32(file, entry + 8);
            var offset = (int)BitConverter.ToUInt32(file, entry + 12);
            var image = new Image();
            if (size != MenuLogo.IconSizes[i] || offset + length > file.Length ||
                image.LoadPngFromBuffer(file[offset..(offset + length)]) != Error.Ok)
                throw new InvalidOperationException(stale);
            image.Convert(Image.Format.Rgba8);
            if (!image.GetData().AsSpan().SequenceEqual(MenuLogo.Icon(size).GetData()))
                throw new InvalidOperationException(stale);
        }
    }

    private async Task VerifyMenuLayoutAsync()
    {
        try
        {
            await VerifyMenuBackdropAsync();
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1024, 768) })
            {
                GetWindow().Size = size;
                for (var frame = 0; frame < 3; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!mainMenuOverlay.GetGlobalRect().Encloses(mainMenuStack.GetGlobalRect()) ||
                    mainMenuOverlay.GetGlobalRect().GetCenter().DistanceTo(mainMenuStack.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"Main Menu escaped its centered bounds at {size}.");
                VerifyMainMenuLogo(size);
                manualSaveOverlay.Show();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!manualSaveOverlay.GetGlobalRect().Encloses(manualSaveCard.GetGlobalRect()) ||
                    manualSaveOverlay.GetGlobalRect().GetCenter().DistanceTo(manualSaveCard.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"Save/load panel escaped its centered bounds at {size}.");
                manualSaveOverlay.Hide();
                worldMenuHeading.Text = "New World";
                worldMenuStatus.Text = "Pick a seed and size. After creating the world, choose where your first Town goes and add four founders, then start time.";
                worldPreviewStatus.Text = "Map preview · you will choose where your first Town goes after creating the world.";
                worldPreview.Show();
                worldMenuOverlay.Show();
                if (!worldNameInput.GetParent().GetChildren().OfType<Label>().Any(label => label.Text == "Name") ||
                    !worldSeedInput.GetParent().GetChildren().OfType<Label>().Any(label => label.Text == "Seed"))
                    throw new InvalidOperationException("New World name and seed fields must keep visible captions once filled.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!worldMenuOverlay.GetGlobalRect().Encloses(worldMenuCard.GetGlobalRect()) ||
                    worldMenuOverlay.GetGlobalRect().GetCenter().DistanceTo(worldMenuCard.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"World creation/selection panel escaped its centered bounds at {size}.");
                if (!worldMenuScroll.GetGlobalRect().Grow(1).Encloses(worldCreateButton.GetGlobalRect()) ||
                    worldPreviewFrame.Size.X < 480 || worldPreviewFrame.GetGlobalRect().Position.X <= worldNameInput.GetGlobalRect().Position.X)
                    throw new InvalidOperationException($"At {size} New World must show a large preview beside its options and Create World without scrolling: preview={worldPreviewFrame.GetGlobalRect()} create={worldCreateButton.GetGlobalRect()} scroll={worldMenuScroll.GetGlobalRect()}.");
                worldMenuOverlay.Hide();
                worldPreview.Hide();
            }
            OpenMainMenuSettings();
            if (!mainMenuOverlay.Visible || mainMenuCard.Visible || !gameMenuPanel.Visible || !gameSettingsContent.Visible ||
                worldSettingsCategoryButton.Visible || worldSettingsContent.Visible || menuResumeButton.Visible ||
                menuCloseButton.Text != "<" || !mainMenuBackdrop.IsVisibleInTree() || mainMenuLogo.Visible)
                throw new InvalidOperationException("Main Menu Settings must keep the title background and show only Game Settings.");
            if (!gameSettingsCategoryButton.ButtonPressed || gameSettingsCategoryButton.Disabled)
                throw new InvalidOperationException("The open Settings category must read as selected, not disabled.");
            settingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            settingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            gameSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!settingsPanel.Visible || !gameSettingsContent.Visible)
                throw new InvalidOperationException("The selected Settings category must remain open.");
            worldSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!gameMenuPanel.Visible || !gameSettingsContent.Visible || worldSettingsContent.Visible ||
                !returnToMainMenu)
                throw new InvalidOperationException("World Settings cannot be opened from the Main Menu.");
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Math.Abs(clockFormatChoice.GetGlobalRect().Position.X - uiScaleChoice.GetGlobalRect().Position.X) > 1 ||
                Math.Abs(dateFormatChoice.GetGlobalRect().Position.X - windowSizeChoice.GetGlobalRect().Position.X) > 1 ||
                Math.Abs(renderResolutionChoice.GetGlobalRect().Position.X - windowSizeChoice.GetGlobalRect().Position.X) > 1)
                throw new InvalidOperationException("Game Settings choices must share one aligned caption column.");
            // Both themes keep text readable on every surface it sits on.
            foreach (var palette in new[] { UiTheme.Light, UiTheme.Dark })
            {
                (string Pair, Color Text, Color Surface, float Minimum)[] readable =
                [
                    ("ink on parchment", palette.Ink, palette.Paper, 7f),
                    ("muted ink on parchment", palette.InkMuted, palette.Paper, 4.5f),
                    ("section headings", palette.Section, palette.Paper, 4.5f),
                    ("links", palette.Link, palette.Paper, 4.5f),
                    ("warnings", palette.Warning, palette.Paper, 4.5f),
                    ("good status", palette.Good, palette.Paper, 4.5f),
                    ("bad status", palette.Bad, palette.Paper, 4.5f),
                    ("button text", palette.Ink, palette.Button, 4.5f),
                    ("primary button text", palette.PrimaryInk, palette.Primary, 4.5f),
                    ("paused button text", palette.EmberInk, palette.Ember, 4.5f),
                    ("text on the wooden bar", palette.OnWood, palette.Wood, 4.5f),
                    ("soft text on the wooden bar", palette.OnWoodSoft, palette.Wood, 4.5f),
                    ("field text", palette.Ink, palette.Field, 4.5f),
                    ("disabled text", palette.InkFaint, palette.FieldDisabled, 3f),
                ];
                foreach (var (pair, text, surface, minimum) in readable)
                    if (UiTheme.Contrast(text, surface) < minimum)
                        throw new InvalidOperationException($"{palette.Name} theme {pair} is too faint: {UiTheme.Contrast(text, surface):0.00}.");
            }
            var themeBefore = displayPreferences.Theme;
            var frameBefore = settingsPanel.GetThemeStylebox("panel");
            var (switchTo, expected) = UiTheme.Current == UiTheme.Dark
                ? (UiThemeChoice.Light, UiTheme.Light)
                : (UiThemeChoice.Dark, UiTheme.Dark);
            themeChoice.Select((int)switchTo);
            SetUiTheme((int)switchTo);
            if (UiTheme.Current != expected || GetTree().Root.Theme != UiTheme.Theme ||
                displayPreferences.Theme != UiTheme.Key(switchTo) || settingsPanel.GetThemeStylebox("panel") == frameBefore ||
                appBackdrop.Color != expected.Backdrop)
                throw new InvalidOperationException("Choosing a theme must restyle the open window at once and be remembered.");
            themeChoice.Select((int)UiTheme.Parse(themeBefore));
            SetUiTheme((int)UiTheme.Parse(themeBefore));
            var (hazeBefore, flashesBefore) = (displayPreferences.CloudHaze, displayPreferences.LightningFlashes);
            cloudHazeToggle.ButtonPressed = !hazeBefore;
            lightningToggle.ButtonPressed = !flashesBefore;
            if (weatherLayer.CloudsEnabled == hazeBefore || weatherLayer.LightningEnabled == flashesBefore ||
                displayPreferences.CloudHaze == hazeBefore || displayPreferences.LightningFlashes == flashesBefore)
                throw new InvalidOperationException("The cloud haze and lightning switches must take effect at once and be remembered.");
            cloudHazeToggle.ButtonPressed = hazeBefore;
            lightningToggle.ButtonPressed = flashesBefore;
            if (!topBarShade.Visible || topBarShade.ZIndex <= mainMenuOverlay.ZIndex)
                throw new InvalidOperationException("Main Menu Settings must shade the top bar like the rest of the title backdrop.");
            SetStatus("Settings status check", good: true);
            if (!statusToast.Visible || statusToast.ZIndex <= gameMenuPanel.ZIndex || statusToast.ZIndex <= mainMenuOverlay.ZIndex)
                throw new InvalidOperationException("Status messages must remain visible above Main Menu Settings.");
            statusToast.Hide();
            var backPoint = menuCloseButton.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = backPoint }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hoveredBackControl = GetViewport().GuiGetHoveredControl();
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = backPoint,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = backPoint,
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!mainMenuOverlay.Visible || !mainMenuCard.Visible || gameMenuPanel.Visible)
                throw new InvalidOperationException($"Clicking Back in Main Menu Settings must return to the Main Menu. Back={menuCloseButton.GetGlobalRect()}, pointer={backPoint}, hovered={hoveredBackControl?.GetPath()}");
            OpenMenuForSetup();
            if (!mainMenuOverlay.Visible || mainMenuCard.Visible || !gameMenuPanel.Visible || menuResumeButton.Visible ||
                menuCloseButton.Text != "<" || menuHeadingLabel.Text != "Connect this device" || !topBarShade.Visible)
                throw new InvalidOperationException("Connect/pair setup must keep the title backdrop with a single compact back button.");
            menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!mainMenuCard.Visible || gameMenuPanel.Visible || topBarShade.Visible)
                throw new InvalidOperationException("Back from connect/pair setup must return to the Main Menu.");
            var displayWindow = GetWindow();
            var originalWindowSize = displayWindow.Size;
            var originalRenderSize = displayWindow.ContentScaleSize;
            var originalScaleMode = displayWindow.ContentScaleMode;
            var originalDisplayPreferences = displayPreferences;
            var originalWindowChoice = windowSizeChoice.Selected;
            var originalRenderChoice = renderResolutionChoice.Selected;
            var originalUiScaleChoice = uiScaleChoice.Selected;
            try
            {
                await VerifyUiScaleAt1440pAsync(displayWindow);
                windowSizeChoice.Select(1);
                SetWindowSize(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (displayWindow.Size != DisplaySizePresets[1])
                    throw new InvalidOperationException("Window Size must change the physical window size.");
                if (renderSizeOptions.Count == 0)
                    throw new InvalidOperationException("At least one fixed render choice must be available.");
                var fixedChoice = renderSizeOptions.Count;
                var fixedRenderSize = renderSizeOptions[^1];
                renderResolutionChoice.Select(fixedChoice);
                SetRenderResolution(fixedChoice);
                if (displayWindow.Size != DisplaySizePresets[1] ||
                    displayWindow.ContentScaleSize != fixedRenderSize)
                    throw new InvalidOperationException($"A fixed render choice must leave the window size alone: window={displayWindow.Size}, expected={DisplaySizePresets[1]}, render={displayWindow.ContentScaleSize}, expected render={fixedRenderSize}.");
                windowSizeChoice.Select(0);
                SetWindowSize(0);
                if (displayWindow.Size != DisplaySizePresets[0] ||
                    displayWindow.ContentScaleSize != fixedRenderSize ||
                    displayWindow.ContentScaleMode != Window.ContentScaleModeEnum.Viewport)
                    throw new InvalidOperationException("Window Size must not change a fixed render resolution.");
                renderResolutionChoice.Select(0);
                SetRenderResolution(0);
                if (displayWindow.ContentScaleSize != AutomaticRenderSize())
                    throw new InvalidOperationException("Automatic render resolution must follow the current display or window.");
            }
            finally
            {
                displayWindow.Size = originalWindowSize;
                displayWindow.ContentScaleSize = originalRenderSize;
                displayWindow.ContentScaleMode = originalScaleMode;
                windowSizeChoice.Select(originalWindowChoice);
                uiScaleChoice.Select(originalUiScaleChoice);
                SaveDisplayPreferences(originalDisplayPreferences);
                ApplyUiScale(originalDisplayPreferences.UiScalePercent);
                RefreshRenderResolutionOptions();
                renderResolutionChoice.Select(originalRenderChoice);
            }
            mainMenuOverlay.Hide();
            isInWorld = true;
            returnToMainMenu = false;
            SetWorldMenuActionsVisible(true);
            pairingPanel.Hide();
            developerScroll.Hide();
            gameMenuPanel.Show();
            var pauseActions = menuQuitToMainButton.GetParent<VBoxContainer>().GetChildren()
                .OfType<Button>().Where(button => button.Visible).Select(button => button.Text).ToArray();
            if (!pauseActions.SequenceEqual(new[] { "Save World", "Settings", "Mod Library", "Quit to Menu" }) ||
                gameMenuPanel.FindChildren("*", nameof(Button), recursive: true, owned: false)
                    .OfType<Button>().Any(button => button.Visible && button.Text == "Create"))
                throw new InvalidOperationException("Pause Menu must have only the four ordered actions and no player Create workbench.");
            modLibraryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!modLibraryPanel.Visible || settingsPanel.Visible)
                throw new InvalidOperationException("Mod Library action must open the in-world package view.");
            settingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (modLibraryPanel.Visible || !settingsPanel.Visible || !worldSettingsCategoryButton.Visible)
                throw new InvalidOperationException("Settings action must open the in-world Game/World category view.");
            developerToggleButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!developerScroll.Visible || settingsScroll.Visible || !settingsPanel.Visible)
                throw new InvalidOperationException("Developer controls must stay inside Settings without adding a Pause Menu action.");
            if (!developerToggleButton.ButtonPressed || gameSettingsCategoryButton.ButtonPressed ||
                worldSettingsCategoryButton.ButtonPressed)
                throw new InvalidOperationException("Developer tools must be the only selected Settings category.");
            if (developerScroll.Size.X < 200 || !settingsPanel.GetGlobalRect().Encloses(developerScroll.GetGlobalRect()))
                throw new InvalidOperationException("Developer controls must have usable width inside Settings at a narrow window.");
            gameSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (developerScroll.Visible || !settingsScroll.Visible || !gameSettingsContent.Visible)
                throw new InvalidOperationException("Game Settings must replace Developer tools in the same panel.");
            settingsPanel.Hide();
            menuQuitToMainButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!quitToMenuConfirmation.Visible)
                throw new InvalidOperationException("Quit to Menu must request confirmation.");
            if (quitToMenuConfirmation.OkButtonText != "Quit to Menu" || quitGameConfirmation.OkButtonText != "Quit Game" ||
                manualSaveOverwriteConfirmation.OkButtonText != "Overwrite" ||
                quitToMenuConfirmation.GetThemeStylebox("embedded_border", "Window") != UiTheme.Theme.GetStylebox("embedded_border", "Window"))
                throw new InvalidOperationException("Confirmations must use the game's panel style and name their action instead of OK.");
            quitToMenuConfirmation.Hide();
            // The pause receipt can succeed even when the following reconnect
            // fails. Leaving must not require a newer snapshot in that case.
            menuPauseConfirmed = true;
            menuPausedWorld = true;
            QuitToMainMenu();
            if (!mainMenuOverlay.Visible || gameMenuPanel.Visible || !resumeWorldOnContinue)
                throw new InvalidOperationException("An accepted pause must allow Quit to Menu despite a held snapshot.");
            mainMenuOverlay.Hide();
            isInWorld = true;
            resumeWorldOnContinue = false;
            gameMenuPanel.Show();
            menuShade.Show();
            if (!topBarShade.Visible)
                throw new InvalidOperationException("Top-bar actions must be blocked while the Pause Menu is open.");
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1024, 768) })
            {
                GetWindow().Size = size;
                foreach (var settingsVisible in new[] { false, true })
                {
                    foreach (var worldSpecific in settingsVisible ? new[] { false, true } : new[] { false })
                    {
                        if (settingsVisible)
                        {
                            ShowSettingsSection(worldSpecific);
                            if (gameSettingsContent.Visible == worldSpecific || worldSettingsContent.Visible != worldSpecific)
                                throw new InvalidOperationException("Game and World Settings must show different controls.");
                            (worldSpecific ? worldSettingsCategoryButton : gameSettingsCategoryButton)
                                .EmitSignal(BaseButton.SignalName.Pressed);
                            if (!settingsPanel.Visible || gameSettingsContent.Visible == worldSpecific ||
                                worldSettingsContent.Visible != worldSpecific)
                                throw new InvalidOperationException("Re-selecting a Settings category must leave its page open.");
                        }
                        else settingsPanel.Hide();
                        foreach (var selected in new[] { false, true, false })
                        {
                            selectedInhabitantCard.Visible = selected;
                            for (var frame = 0; frame < 5; frame++)
                            {
                                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            }
                            ApplyResponsiveLayout();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            var menu = gameMenuPanel.GetGlobalRect();
                            var bounds = gameMenuPanel.GetParent<Control>().GetGlobalRect();
                            if (menu.GetCenter().DistanceTo(bounds.GetCenter()) > 2 || !bounds.Encloses(menu))
                            {
                                throw new InvalidOperationException($"Menu escaped its centered bounds: window={size}, settings={settingsVisible}, world={worldSpecific}, selected={selected}, menu={menu}, bounds={bounds}");
                            }
                            ShowWorldInfoPage(towns: true);
                            worldInfoPanel.Show();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            if (!mapCanvas.GetGlobalRect().Encloses(worldInfoPanel.GetGlobalRect()))
                                throw new InvalidOperationException($"World Info escaped the world viewport: window={size} map={mapCanvas.GetGlobalRect()} info={worldInfoPanel.GetGlobalRect()} hud={hudBar.GetGlobalRect()}");
                            worldInfoPanel.Hide();
                        }
                    }
                }
            }
            gameMenuPanel.Hide();
            menuShade.Hide();
            if (topBarShade.Visible)
                throw new InvalidOperationException("Closing the Pause Menu must restore top-bar actions.");
            selectedInhabitantCard.Hide();
            var sampleResource = new OwnerWorldResource("wood", "construction", new(1, 1), false, "available", 8, 12, 0, 0, "spring");
            var sample = new OwnerWorldSnapshot("ui-test", 0, "ui-map", Enumerable.Range(0, 16)
                .Select(index => new OwnerWorldTile(index % 4, index / 4, "meadow")).ToArray(), [], [sampleResource], null, 0)
            {
                PackedTerrain = new OwnerWorldPackedTerrain(4, 4, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[16])),
                PackedMapLayers = new OwnerWorldPackedMapLayers(4, 4, "map-layers-v1",
                    Convert.ToBase64String(Enumerable.Repeat((byte)2, 16).ToArray()),
                    Convert.ToBase64String(Enumerable.Repeat((byte)123, 16).ToArray()),
                    Convert.ToBase64String(new byte[16]),
                    Convert.ToBase64String(Enumerable.Repeat((byte)1, 16).ToArray()),
                    Convert.ToBase64String(Enumerable.Repeat((byte)3, 16).ToArray())),
                Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0,
                    ["founder:1", "founder:2", "founder:3", "founder:4"], [],
                    [new(0, 0), new(1, 0), new(0, 1), new(1, 1)])],
                PlacedBuildings = [new("test-hall", "test-definition", new(0, 2), 0, "Test hall", ["shelter"], 2, 1)],
                ContentPackages = [new("owner-building-ui-test", "1.0.0", "sha256:test", "proposed", null, null, null, null,
                    "sha256:manifest", "Mira's shelter study", "builder-test")],
            };
            Render(sample with { WorldTick = 3_600, CalendarPace = new OwnerWorldCalendarPace(360, 40) }, []);
            if (clockLabel.Text != "01-02-0001 · 00:00" ||
                !worldInfoText.Text.Contains("40 days", StringComparison.Ordinal) ||
                !TownListText().Contains("First Town", StringComparison.Ordinal) ||
                !TownListText().Contains("4 residents · founding", StringComparison.Ordinal))
                throw new InvalidOperationException("World Info must show the saved calendar and only the first Town's established founding, membership and border facts.");
            Render(sample with { JevEnabled = true }, []);
            if (!jevAssistanceToggle.ButtonPressed)
                throw new InvalidOperationException("World Settings must reflect this world's saved Jev assistance choice.");
            Render(sample with { JevEnabled = false }, []);
            if (jevAssistanceToggle.ButtonPressed)
                throw new InvalidOperationException("World Settings must show when Jev assistance is off.");
            usageStatus = new OwnerUsageStatus(2, 1, 0, 1, 10, 3, 2, true,
                [new OwnerUsageRow("openai", "test-model", "planning", 2, 1, 0, 1, 10, 3)]);
            RenderUsageStatus();
            if (!usageMeterStatus.Text.Contains("2 of 2 model calls used", StringComparison.Ordinal) ||
                !usageMeterStatus.Text.Contains("Time is paused", StringComparison.Ordinal) ||
                !usageMeterStatus.Text.Contains("openai / test-model", StringComparison.Ordinal) ||
                !grantUsageCallsButton.Visible || usageAttemptLimitInput.Text != "2")
                throw new InvalidOperationException("World Settings must present paid attempts, scope, provider/model and explicit consent at the cap.");
            usageStatus = usageStatus with { AccountingError = "Accounting unavailable. Restore a trusted backup and restart." };
            RenderUsageStatus();
            if (!usageMeterStatus.Text.Contains("Restore a trusted backup", StringComparison.Ordinal) ||
                usageMeterStatus.Text.Contains("calls used", StringComparison.Ordinal) ||
                grantUsageCallsButton.Visible || !applyUsageLimitButton.Disabled || usageAttemptLimitInput.Editable)
                throw new InvalidOperationException("Unavailable accounting must not display zero usage or offer a cap bypass.");
            usageStatus = null;
            RenderUsageStatus();
            Render(sample, []);
            Render(sample, []);
            if (worldDetails.GetParsedText().Length == 0)
                throw new InvalidOperationException("An unchanged refresh must keep the Town panel's stores and projects visible.");
            if (!worldDetails.GetParsedText().Contains("Shared stores", StringComparison.Ordinal) ||
                !worldDetails.GetParsedText().Contains("No one is working on a project right now.", StringComparison.Ordinal))
                throw new InvalidOperationException($"The Town panel must head its sections and say when nothing is under way instead of leaving gaps: {worldDetails.GetParsedText()}");
            RenderWorldDetails(sample with
            {
                Authoring = new OwnerWorldAuthoringState(false, 3, 7, 1, "initial-digest", "current-digest", "clear", "spring", []),
            });
            if (worldDetails.TooltipText.Length != 0 ||
                worldDetails.GetParsedText().Contains("digest", StringComparison.OrdinalIgnoreCase) ||
                worldDetails.GetParsedText().Contains("revision", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Town panel must not show operator diagnostics such as revisions or digests.");
            RenderWorldDetails(sample);
            foreach (var panel in new PanelContainer[] { rosterPanel, eventsPanel, worldInfoPanel, filtersPanel, worldOverviewPanel })
            {
                panel.Show();
                var close = panel.FindChildren("*", nameof(Button), recursive: true, owned: false)
                    .OfType<Button>().FirstOrDefault(button => button.Text == "×");
                if (close is null || close.FocusMode == Control.FocusModeEnum.None)
                    throw new InvalidOperationException($"{panel.Name} must have a keyboard-reachable close button in its heading.");
                close.GrabFocus();
                close.EmitSignal(BaseButton.SignalName.Pressed);
                if (panel.Visible || close.HasFocus())
                    throw new InvalidOperationException($"{panel.Name} must close and release focus when its close button is pressed.");
            }
            SetStatus("Action result check", good: true);
            ExpireStatusToast(refreshSucceeded: true);
            if (!statusToast.Visible)
                throw new InvalidOperationException("A new action result must survive the next observation refresh.");
            statusToastShownAtMsec -= StatusToastMilliseconds;
            ExpireStatusToast(refreshSucceeded: false);
            if (statusToast.Visible)
                throw new InvalidOperationException("An action result must clear after its reading time.");
            ShowHeldState("connection check");
            ExpireStatusToast(refreshSucceeded: false);
            if (!statusLabel.Text.StartsWith("Connection lost", StringComparison.Ordinal) ||
                statusLabel.Text.Contains("tick", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Connection problems must be described without internal tick numbers.");
            if (!statusToast.Visible)
                throw new InvalidOperationException("A connection problem must stay visible until a refresh succeeds.");
            ExpireStatusToast(refreshSucceeded: true);
            if (statusToast.Visible)
                throw new InvalidOperationException("A successful refresh must clear a connection problem.");
            choosingFirstTownSite = true;
            SetStatus("Map mode check", good: true, StatusToastKind.Sticky);
            statusToastShownAtMsec -= StatusToastMilliseconds;
            ExpireStatusToast(refreshSucceeded: true);
            if (!statusToast.Visible)
                throw new InvalidOperationException("Map-click instructions must stay visible while their mode is active.");
            RenderFounderSetup(sample with { FounderSetup = new OwnerFounderSetup(4, 0, false) { CanChooseTownSite = true } });
            if (townSiteButton.Text != "Cancel Town site")
                throw new InvalidOperationException("An active Town-site selection must show how to cancel it.");
            choosingFirstTownSite = false;
            ExpireStatusToast(refreshSucceeded: true);
            if (statusToast.Visible)
                throw new InvalidOperationException("Map-click instructions must clear after their mode ends.");
            Render(sample with
            {
                FounderSetup = new OwnerFounderSetup(4, 0, false)
                {
                    CanChooseTownSite = true,
                }
            }, []);
            if (!townSiteButton.Visible || townSiteButton.Text != "Choose Town site" ||
                !founderSetupButton.Disabled)
                throw new InvalidOperationException("Paused New World must offer Town-site selection before founders.");
            Render(sample with
            {
                FounderSetup = new OwnerFounderSetup(4, 0, false)
                {
                    CanChooseTownSite = true,
                    HasAcceptedTownSite = true,
                }
            }, []);
            if (!townSiteButton.Visible || townSiteButton.Text != "Redo Town site")
                throw new InvalidOperationException("Accepted first Town must offer a redo before founders.");
            Render(sample with
            {
                FounderSetup = new OwnerFounderSetup(4, 2, false)
                {
                    LastFounderId = "founder:00000000000000000000000000000002",
                },
            }, []);
            founderSetupPanel.Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!founderSetupButton.Visible || !founderSetupButton.Text.Contains("2/4", StringComparison.Ordinal) ||
                !startWorldButton.Visible || !startWorldButton.Disabled ||
                !undoFounderButton.Visible || !founderHudRow.Visible ||
                HudButtons().Where(button => button.IsVisibleInTree())
                    .Any(button => !mapCanvas.GetGlobalRect().Encloses(button.GetGlobalRect())) ||
                !mapCanvas.GetGlobalRect().Encloses(founderSetupPanel.GetGlobalRect()))
                throw new InvalidOperationException("Founder setup must keep every HUD action inside the world view and Start World gated.");
            founderSetupPanel.Hide();
            Render(sample with { FounderSetup = new OwnerFounderSetup(4, 4, true) }, []);
            if (!addAgentButton.Visible || founderSetupButton.Visible || startWorldButton.Visible ||
                undoFounderButton.Visible)
                throw new InvalidOperationException("Started worlds must offer Add Agent instead of founder setup controls.");
            Render(sample, []);
            RenderModLibrary(sample);
            if (!modLibraryContents.Text.Contains("proposed by builder-test", StringComparison.Ordinal))
                throw new InvalidOperationException("Mod Library must show existing agent proposal provenance.");
            RenderMap(sample);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!terrainLayer.DrawsGroundTextures)
                throw new InvalidOperationException("Zoomed-in terrain must draw pixel-art ground textures.");
            var inspectClick = mapStage.Position + new Vector2(currentTileSize * 1.5f,
                currentTileSize * 1.5f);
            HandleMapInput(new InputEventMouseButton
            {
                Position = inspectClick,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
            if (!selectedTilePanel.Visible || terrainLayer.SelectedTile != new Vector2I(1, 1) ||
                !selectedTileText.Text.Contains("8 available", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Climate: Temperate", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Elevation: 123/255", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Terrain: Meadow", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Surface: Sand", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Vegetation: Scrub", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Town: First Town", StringComparison.Ordinal) ||
                selectedTileText.Text.Contains("Fertility", StringComparison.Ordinal) ||
                selectedTileText.Text.Contains("unavailable", StringComparison.Ordinal) ||
                selectedTileText.Text.Contains("none", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selected-tile inspection must show available map facts without empty placeholders.");
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!mapCanvas.GetGlobalRect().Encloses(selectedTilePanel.GetGlobalRect()))
                throw new InvalidOperationException($"Selected-tile inspection must open inside the world view: map={mapCanvas.GetGlobalRect()} card={selectedTilePanel.GetGlobalRect()}.");
            if (selectedTileText.GetContentHeight() > selectedTileText.Size.Y + 1)
                throw new InvalidOperationException($"Selected-tile facts must fit without an inner scrollbar: content={selectedTileText.GetContentHeight()} visible={selectedTileText.Size.Y}.");
            var ownedMap = sample with
            {
                PlacedBuildings = [.. sample.PlacedBuildings,
                    new("test-house", "house", new(2, 2), 0, "House", ["shelter"], 2, 1,
                        HouseholdId: "household:one")],
                Stockpiles = [new("household:one", "Founder's household", [])],
            };
            RenderMap(ownedMap);
            filtersButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!filtersPanel.Visible || !mapCanvas.GetGlobalRect().Encloses(filtersPanel.GetGlobalRect()))
                throw new InvalidOperationException($"Map Filters must open inside the world view: map={mapCanvas.GetGlobalRect()} filters={filtersPanel.GetGlobalRect()} site_visible={townSiteButton.Visible}.");
            townBorderFilter.ButtonPressed = false;
            householdPropertyFilter.ButtonPressed = true;
            if (!townBorderHint.Text.Contains("Town borders are hidden", StringComparison.Ordinal))
                throw new InvalidOperationException("The Town border filter must update the visible map explanation.");
            HandleMapInput(new InputEventMouseButton
            {
                Position = mapStage.Position + new Vector2(currentTileSize * 2.5f, currentTileSize * 2.5f),
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
            if (!selectedTileText.Text.Contains("Household property: Founder's household", StringComparison.Ordinal))
                throw new InvalidOperationException("Owned building footprints must expose their recorded household in tile inspection.");
            placingAddedAgent = true;
            founderSetupPanel.Show();
            UpdateTileHover(mapStage.Position + new Vector2(currentTileSize * 2.5f, currentTileSize * 2.5f));
            if (!founderSetupHint.Text.Contains("Household: Founder's household · Town: no Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Add Agent must preview recorded household property without inferring Town membership.");
            UpdateTileHover(mapStage.Position + new Vector2(currentTileSize * 0.5f, currentTileSize * 0.5f));
            if (!founderSetupHint.Text.Contains("Household: none · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Unclaimed Town land must preview Town residency without invented household membership.");
            UpdateTileHover(mapStage.Position + new Vector2(currentTileSize * 3.5f, currentTileSize * 3.5f));
            if (!founderSetupHint.Text.Contains("Household: new independent household · Town: no Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Unclaimed land must preview a new independent household.");
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            householdPropertyFilter.ButtonPressed = false;
            townBorderFilter.ButtonPressed = true;
            filtersButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderMap(sample);
            selectedTile = new Vector2I(1, 1);
            terrainLayer.SetSelectedTile(selectedTile);
            RenderTileInspection(sample);
            var renderedSurface = terrainMap?.DisplayColorAt(1, 1) ?? Colors.Transparent;
            var expectedSurface = new Color("AA985F");
            if (terrainMap?.SurfaceAt(1, 1) != 1 ||
                Math.Abs(renderedSurface.R - expectedSurface.R) > 0.001f ||
                Math.Abs(renderedSurface.G - expectedSurface.G) > 0.001f ||
                Math.Abs(renderedSurface.B - expectedSurface.B) > 0.001f)
                throw new InvalidOperationException("The rendered map must use its separate surface and vegetation layers.");
            var testHydrology = new byte[16];
            var testSurfaces = Enumerable.Repeat((byte)0, 16).ToArray();
            testHydrology[6] = 3;
            testSurfaces[6] = 4;
            var testLayers = new OwnerWorldPackedMapLayers(4, 4, "map-layers-v2",
                Convert.ToBase64String(Enumerable.Repeat((byte)2, 16).ToArray()),
                Convert.ToBase64String(Enumerable.Repeat((byte)123, 16).ToArray()),
                Convert.ToBase64String(testHydrology), Convert.ToBase64String(testSurfaces),
                Convert.ToBase64String(Enumerable.Repeat((byte)1, 16).ToArray()));
            var transitionMap = WorldTerrainMap.FromTiles(sample.Tiles, 4, 4, testLayers);
            if ((transitionMap.WaterEdgeMaskAt(1, 1, false) & 2) == 0 ||
                (transitionMap.SurfaceBoundaryMaskAt(1, 1, false) & 2) == 0)
                throw new InvalidOperationException("Generated water and ground changes must expose functional tile-edge transitions.");
            var coastPieces = new List<(TerrainStyle Style, int Piece)>();
            var edgeProbe = new List<(TerrainStyle Style, int Piece)>();
            TerrainTransitions.CollectCoast(transitionMap, 2, 1, false, coastPieces);
            var riverSides = coastPieces.Count(item => item.Piece < TerrainTransitions.OuterCornerPiece(0, 0));
            TerrainTransitions.CollectCoast(transitionMap, 1, 1, false, edgeProbe);
            if (transitionMap.StyleAt(2, 1) != TerrainStyle.River || riverSides != 4 || coastPieces.Count != 8 || edgeProbe.Count != 0)
                throw new InvalidOperationException($"A river tile surrounded by land must take a rounded bank on every side and corner, and land tiles none: {coastPieces.Count} pieces.");
            foreach (var atlasSize in new[] { 16, 32 })
                for (var start = 0; start < TerrainTransitions.Levels; start++)
                    for (var end = 0; end < TerrainTransitions.Levels; end++)
                    {
                        var piece = TerrainTransitions.EdgePiece(0, start, end, 0);
                        var land = CoastEdges.Piece(CoastEdges.LandRow, piece, atlasSize);
                        var shallow = CoastEdges.Piece(CoastEdges.ShallowRow, piece, atlasSize);
                        var foam = CoastEdges.Piece(CoastEdges.FoamRow, piece, atlasSize);
                        var last = atlasSize - 1;
                        var band = CoastEdges.Band(false, atlasSize);
                        int Depth(Image image, int column)
                        {
                            var depth = 0;
                            while (depth < atlasSize && image.GetPixel(column, depth).A > 0) depth++;
                            return depth;
                        }
                        if (Depth(land, 0) != TerrainTransitions.Reach(start, atlasSize) ||
                            Depth(land, last) != TerrainTransitions.Reach(end, atlasSize) ||
                            Depth(shallow, 0) != TerrainTransitions.Reach(start, atlasSize) + band ||
                            Depth(shallow, last) != TerrainTransitions.Reach(end, atlasSize) + band)
                            throw new InvalidOperationException($"{atlasSize}px shores must meet each tile corner at its shared reach, with the shallow band just beyond.");
                        for (var column = 0; column < atlasSize; column++)
                            for (var row = 0; row < atlasSize; row++)
                                if (foam.GetPixel(column, row).A > 0 && land.GetPixel(column, row).A > 0)
                                    throw new InvalidOperationException($"{atlasSize}px foam must lie along the land's edge, not on it.");
                    }
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var style in Enum.GetValues<TerrainStyle>())
                {
                    var baseColor = TerrainTextures.BaseColor(style);
                    var first = TerrainTextures.Tile(style, 0, atlasSize);
                    var second = TerrainTextures.Tile(style, 1, atlasSize);
                    foreach (var texture in new[] { first, second })
                    {
                        var detail = 0;
                        for (var ty = 0; ty < atlasSize; ty++)
                            for (var tx = 0; tx < atlasSize; tx++)
                                if (!texture.GetPixel(tx, ty).IsEqualApprox(baseColor)) detail++;
                        // Calm ground: a few pixel clusters, never per-pixel grain.
                        // Mountains and peaks are drawn as relief shapes instead.
                        var detailLimit = style is TerrainStyle.Mountain or TerrainStyle.Peak ? 0.4f : 0.12f;
                        if (detail > atlasSize * atlasSize * detailLimit)
                            throw new InvalidOperationException($"{style} {atlasSize}px texture is too busy: {detail} detail pixels.");
                        for (var edge = 0; edge < atlasSize; edge++)
                            if (!texture.GetPixel(edge, 0).IsEqualApprox(baseColor) || !texture.GetPixel(0, edge).IsEqualApprox(baseColor))
                                throw new InvalidOperationException($"{style} {atlasSize}px details must stay off tile edges so neighbors join without seams.");
                    }
                    if (style != TerrainStyle.Unknown && first.GetData().SequenceEqual(second.GetData()))
                        throw new InvalidOperationException($"{style} needs two distinct texture variants.");
                }
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var style in new[] { TerrainStyle.Ocean, TerrainStyle.Lake, TerrainStyle.River, TerrainStyle.ShallowWater })
                {
                    // Water repeats as one larger block: it must average near
                    // the flat color that the overview and shore bands use, and
                    // its tiles must differ so no tile grid shows.
                    var waterBlock = WaterTextures.Block(style, atlasSize);
                    var waterBase = TerrainTextures.BaseColor(style);
                    var pixels = waterBlock.GetWidth() * waterBlock.GetHeight();
                    var (red, green, blue) = (0f, 0f, 0f);
                    for (var py = 0; py < waterBlock.GetHeight(); py++)
                        for (var px = 0; px < waterBlock.GetWidth(); px++)
                        {
                            var pixel = waterBlock.GetPixel(px, py);
                            red += pixel.R;
                            green += pixel.G;
                            blue += pixel.B;
                        }
                    if (Math.Abs(red / pixels - waterBase.R) > 0.03f || Math.Abs(green / pixels - waterBase.G) > 0.03f ||
                        Math.Abs(blue / pixels - waterBase.B) > 0.03f)
                        throw new InvalidOperationException($"{style} {atlasSize}px water must average close to its base color.");
                    var distinctTiles = new HashSet<string>();
                    for (var ty = 0; ty < WaterTextures.BlockTiles; ty++)
                        for (var tx = 0; tx < WaterTextures.BlockTiles; tx++)
                            distinctTiles.Add(Convert.ToBase64String(waterBlock.GetRegion(
                                new Rect2I(tx * atlasSize, ty * atlasSize, atlasSize, atlasSize)).GetData()));
                    if (distinctTiles.Count < WaterTextures.BlockTiles ||
                        WaterTextures.Region(style, WaterTextures.BlockTiles, -WaterTextures.BlockTiles, atlasSize) !=
                        WaterTextures.Region(style, 0, 0, atlasSize))
                        throw new InvalidOperationException($"{style} water must vary between tiles and repeat only as a whole block.");
                }
            if (!TerrainTransitions.Overlaps(TerrainStyle.Grass, TerrainStyle.Sand) ||
                TerrainTransitions.Overlaps(TerrainStyle.Sand, TerrainStyle.Grass) ||
                !TerrainTransitions.Overlaps(TerrainStyle.Snow, TerrainStyle.Rock) ||
                TerrainTransitions.Overlaps(TerrainStyle.Peak, TerrainStyle.Mountain) ||
                TerrainTransitions.Overlaps(TerrainStyle.Grass, TerrainStyle.Ocean) ||
                TerrainTransitions.Overlaps(TerrainStyle.Lake, TerrainStyle.Sand))
                throw new InvalidOperationException("Land edges must follow the surface order and leave water to its shoreline pieces.");
            // Pixels covered from one edge inward along a line, stopping at the first gap.
            static int Reached(Image piece, int startX, int startY, int stepX, int stepY)
            {
                var count = 0;
                for (int px = startX, py = startY; px >= 0 && py >= 0 && px < piece.GetWidth() && py < piece.GetHeight(); px += stepX, py += stepY, count++)
                    if (piece.GetPixel(px, py).A <= 0) break;
                return count;
            }
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var over in new[] { TerrainStyle.Grass, TerrainStyle.Snow })
                {
                    var last = atlasSize - 1;
                    var depths = new HashSet<int>();
                    for (var start = 0; start < TerrainTransitions.Levels; start++)
                        for (var end = 0; end < TerrainTransitions.Levels; end++)
                            for (var variant = 0; variant < TerrainTransitions.EdgeVariants; variant++)
                            {
                                var north = TerrainTransitions.Piece(over, TerrainTransitions.EdgePiece(0, start, end, variant), atlasSize);
                                var west = TerrainTransitions.Piece(over, TerrainTransitions.EdgePiece(3, start, end, variant), atlasSize);
                                if (Reached(north, 0, 0, 0, 1) != TerrainTransitions.Reach(start, atlasSize) ||
                                    Reached(north, last, 0, 0, 1) != TerrainTransitions.Reach(end, atlasSize) ||
                                    Reached(west, 0, 0, 1, 0) != TerrainTransitions.Reach(start, atlasSize) ||
                                    Reached(west, 0, last, 1, 0) != TerrainTransitions.Reach(end, atlasSize))
                                    throw new InvalidOperationException($"{over} {atlasSize}px edges must meet each tile corner at that corner's shared reach.");
                                for (var column = 0; column < atlasSize; column++)
                                {
                                    for (var row = TerrainTransitions.MaximumReach(atlasSize); row < atlasSize; row++)
                                        if (north.GetPixel(column, row).A > 0)
                                            throw new InvalidOperationException($"{over} {atlasSize}px edges must stay within a quarter of the tile so it still reads as a square.");
                                    depths.Add(Reached(north, column, 0, 0, 1));
                                }
                            }
                    if (depths.Count < 4)
                        throw new InvalidOperationException($"{over} {atlasSize}px edges must wander rather than run in straight lines.");
                    for (var level = 0; level < TerrainTransitions.Levels; level++)
                    {
                        var corner = TerrainTransitions.Piece(over, TerrainTransitions.OuterCornerPiece(3, level), atlasSize);
                        var reach = TerrainTransitions.Reach(level, atlasSize);
                        if (Reached(corner, 0, 0, 1, 0) != reach || Reached(corner, 0, 0, 0, 1) != reach || corner.GetPixel(last, last).A > 0)
                            throw new InvalidOperationException($"{over} {atlasSize}px outer corners must meet both neighboring edges at the corner's reach.");
                    }
                }
            var edgeMap = WorldTerrainMap.FromTiles(
                Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, index == 4 ? "meadow" : "sand")).ToArray(), 3, 3);
            var edgePieces = new List<(TerrainStyle Style, int Piece)>();
            TerrainTransitions.Collect(edgeMap, 1, 0, false, edgePieces);
            var southLevels = (TerrainTransitions.CornerLevel(1, 1, 3, false), TerrainTransitions.CornerLevel(2, 1, 3, false));
            if (edgePieces.Count != 1 || edgePieces[0].Style != TerrainStyle.Grass ||
                edgePieces[0].Piece / (TerrainTransitions.Levels * TerrainTransitions.Levels * TerrainTransitions.EdgeVariants) != 2 ||
                edgePieces[0].Piece % (TerrainTransitions.Levels * TerrainTransitions.Levels * TerrainTransitions.EdgeVariants) / TerrainTransitions.EdgeVariants !=
                    southLevels.Item1 * TerrainTransitions.Levels + southLevels.Item2)
                throw new InvalidOperationException("Sand beside grass must take one south grass edge between its south corners' reaches.");
            TerrainTransitions.Collect(edgeMap, 0, 0, false, edgePieces);
            if (edgePieces.Count != 1 || edgePieces[0].Piece != TerrainTransitions.OuterCornerPiece(1, TerrainTransitions.CornerLevel(1, 1, 3, false)))
                throw new InvalidOperationException("Sand diagonal to grass must take one rounded outer corner.");
            TerrainTransitions.Collect(edgeMap, 1, 1, false, edgePieces);
            if (edgePieces.Count != 0)
                throw new InvalidOperationException("Grass must not take an edge from lower sand.");
            if (TerrainTransitions.CornerLevel(3, 5, 3, true) != TerrainTransitions.CornerLevel(0, 5, 3, true))
                throw new InvalidOperationException("Edge corners must continue across a wrapped world seam.");
            if (!TerrainTransitions.WaterOverlaps(TerrainStyle.River, TerrainStyle.Ocean) ||
                TerrainTransitions.WaterOverlaps(TerrainStyle.Ocean, TerrainStyle.River) ||
                !TerrainTransitions.WaterOverlaps(TerrainStyle.Lake, TerrainStyle.River) ||
                TerrainTransitions.WaterOverlaps(TerrainStyle.Grass, TerrainStyle.Ocean))
                throw new InvalidOperationException("Lighter water must fan into darker water, and land never into water this way.");
            var mouthMap = WorldTerrainMap.FromTiles(
                Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, index == 3 ? "river" : "ocean")).ToArray(), 3, 3);
            TerrainTransitions.CollectWater(mouthMap, 1, 1, false, edgePieces);
            var mouthLevels = (TerrainTransitions.CornerLevel(1, 1, 3, false), TerrainTransitions.CornerLevel(1, 2, 3, false));
            if (edgePieces.Count != 1 || edgePieces[0].Style != TerrainStyle.River ||
                edgePieces[0].Piece != TerrainTransitions.EdgePiece(3, mouthLevels.Item1, mouthLevels.Item2, (int)(PixelArt.Hash(1, 1, 94) % TerrainTransitions.EdgeVariants)))
                throw new InvalidOperationException("Sea beside a river mouth must take one soft west edge of river water.");
            TerrainTransitions.CollectWater(mouthMap, 0, 1, false, edgePieces);
            if (edgePieces.Count != 0)
                throw new InvalidOperationException("A river must not take an edge from the darker sea.");
            var spriteData = new HashSet<string>(StringComparer.Ordinal);
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var nature in Enum.GetValues<NatureSprite>())
                {
                    var sprite = NatureSprites.Sprite(nature, atlasSize);
                    var covered = 0;
                    for (var sy = 0; sy < atlasSize; sy++)
                        for (var sx = 0; sx < atlasSize; sx++)
                            if (sprite.GetPixel(sx, sy).A > 0.05f) covered++;
                    if (sprite.GetPixel(0, 0).A > 0 || sprite.GetPixel(atlasSize - 1, 0).A > 0 ||
                        covered < atlasSize * atlasSize * 0.03f || covered > atlasSize * atlasSize * 0.9f)
                        throw new InvalidOperationException($"{nature} {atlasSize}px sprite must sit on a transparent tile with visible art: {covered} pixels.");
                    if (atlasSize == 32 && !spriteData.Add(Convert.ToBase64String(sprite.GetData())))
                        throw new InvalidOperationException($"{nature} must look different from every other nature sprite.");
                }
            for (byte code = 1; code <= 9; code++)
                if (NatureSprites.ForTree(code) is null)
                    throw new InvalidOperationException($"Tree state {code} has no sprite.");
            for (byte kind = 1; kind <= 11; kind++)
                if (NatureSprites.ForNaturalObject(kind, 0) is null)
                    throw new InvalidOperationException($"Natural object {kind} has no sprite.");
            var buildingData = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tilePixels in new[] { 16, 32 })
                foreach (var kind in Enum.GetValues<BuildingKind>())
                    foreach (var (footprintWidth, footprintHeight) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
                    {
                        var roof = BuildingSprites.Render(kind, footprintWidth, footprintHeight, tilePixels);
                        var covered = 0;
                        for (var by = 0; by < roof.GetHeight(); by++)
                            for (var bx = 0; bx < roof.GetWidth(); bx++)
                                if (roof.GetPixel(bx, by).A > 0.05f) covered++;
                        var area = roof.GetWidth() * roof.GetHeight();
                        // Hearths and bedrolls keep a fixed size; roofs and paths cover most of their footprint.
                        var minimum = kind is BuildingKind.Hearth or BuildingKind.Bedroll ? 0.05f : 0.2f;
                        if (roof.GetWidth() != footprintWidth * tilePixels || roof.GetHeight() != footprintHeight * tilePixels ||
                            roof.GetPixel(0, 0).A > 0 || covered < area * minimum || covered > area * 0.95f)
                            throw new InvalidOperationException($"{kind} {footprintWidth}x{footprintHeight} {tilePixels}px building art must fill its footprint inside a clear margin: {covered} of {area} pixels.");
                        if (tilePixels == 32 && footprintWidth == 2 && footprintHeight == 1 &&
                            !buildingData.Add(Convert.ToBase64String(roof.GetData())))
                            throw new InvalidOperationException($"{kind} buildings must look different from every other building family.");
                    }
            if (BuildingSprites.KindFor(["shelter"]) != BuildingKind.Shelter ||
                BuildingSprites.KindFor(["house", "shelter"]) != BuildingKind.House ||
                BuildingSprites.KindFor(["cooking", "warmth"]) != BuildingKind.Hearth ||
                BuildingSprites.KindFor(null) != BuildingKind.Generic ||
                BuildingSprites.KindForObject("campfire") != BuildingKind.Hearth ||
                BuildingSprites.KindForObject("cooking") != BuildingKind.Hearth ||
                BuildingSprites.KindForObject("path") != BuildingKind.Path ||
                NatureSprites.ForCampResource("construction") != NatureSprite.WoodPile ||
                NatureSprites.ForCampResource("iron_ore") is not null ||
                BuildingSprites.KindForObject("resource") is not null)
                throw new InvalidOperationException("Buildings and camp objects must pick their art family from their recorded tags and kinds.");
            var agentData = new HashSet<string>(StringComparer.Ordinal);
            foreach (var agentSize in new[] { 16, 32 })
                for (var stage = 0; stage < 4; stage++)
                    for (var variant = 0; variant < AgentSprites.VariantCount; variant++)
                    {
                        var figure = AgentSprites.Sprite(variant, stage, agentSize);
                        if (figure.GetPixel(0, 0).A > 0 || figure.GetPixel(agentSize / 2, agentSize / 2).A < 0.9f)
                            throw new InvalidOperationException($"Agent variant {variant} stage {stage} {agentSize}px must be a solid figure on a clear tile.");
                        if (agentSize == 32 && !agentData.Add(Convert.ToBase64String(figure.GetData())))
                            throw new InvalidOperationException($"Agent variant {variant} stage {stage} must look different from every other agent sprite.");
                    }
            if (AgentSprites.VariantFor("founder:1") != AgentSprites.VariantFor("founder:1") ||
                Enumerable.Range(1, 12).Select(index => AgentSprites.VariantFor($"founder:{index}")).Distinct().Count() < 3 ||
                AgentSprites.StageIndex("elder") != 3 || AgentSprites.StageIndex(null) != 2)
                throw new InvalidOperationException("Agent appearance must be stable per agent, varied across agents, and follow their life stage.");
            testHydrology[3] = 1;
            testSurfaces[3] = 4;
            var seamLayers = testLayers with
            {
                Hydrology = Convert.ToBase64String(testHydrology),
                Surface = Convert.ToBase64String(testSurfaces),
            };
            var seamMap = WorldTerrainMap.FromTiles(sample.Tiles, 4, 4, seamLayers);
            if ((seamMap.WaterEdgeMaskAt(0, 0, true) & 8) == 0)
                throw new InvalidOperationException("Water-edge transitions must continue across an enabled world seam.");
            var marker = mapObjectVisuals["resource:wood"];
            var identity = marker.GetInstanceId();
            var entered = false;
            marker.MouseEntered += () => entered = true;
            GetViewport().PushInput(new InputEventMouseMotion { Position = marker.GetGlobalRect().GetCenter(), GlobalPosition = marker.GetGlobalRect().GetCenter() }, inLocalCoords: true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RenderMap(sample with { Resources = [sampleResource with { Quantity = 7 }] });
            if (!selectedTileText.Text.Contains("7 available", StringComparison.Ordinal))
                throw new InvalidOperationException("Selected-tile resource stock must refresh with observations.");
            ClearTileSelection();
            if (!entered || marker.MouseFilter == MouseFilterEnum.Ignore || marker.GetInstanceId() != identity ||
                !marker.TooltipText.Contains("7/12", StringComparison.Ordinal) || !marker.Text.Contains("7/12", StringComparison.Ordinal))
                throw new InvalidOperationException($"Resource hover/update failed: entered={entered}, filter={marker.MouseFilter}, stable={marker.GetInstanceId() == identity}, text={marker.Text}, rect={marker.GetGlobalRect()}, hovered={GetViewport().GuiGetHoveredControl()?.GetPath()}.");
            var sampleTree = new OwnerWorldResource("sample-tree", "construction", new(3, 1), true,
                "available", 1, 1, 1, 6, "spring", "broadleaf");
            RenderMap(sample with { Resources = [sampleResource, sampleTree] });
            if (terrainLayer.TreeStageAt(3, 1) != "mature" || mapObjectVisuals.ContainsKey("resource:sample-tree"))
                throw new InvalidOperationException("A live tree must render as a terrain object, not a resource text label.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree with { Quantity = 0, State = "depleted" }] });
            if (terrainLayer.TreeStageAt(3, 1) != "stump")
                throw new InvalidOperationException("Harvested trees must become visible stumps.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree with { Quantity = 0, State = "depleted", IsPlanted = true }] });
            if (terrainLayer.TreeStageAt(3, 1) != "sapling")
                throw new InvalidOperationException("Replanted trees must become visible saplings.");
            var sampleNaturalObject = new OwnerWorldResource("sample-berry-bush", "food", new(0, 2), true,
                "available", 4, 8, NaturalObjectKind: "berry_bush");
            RenderMap(sample with { Resources = [sampleResource, sampleNaturalObject] });
            if (terrainLayer.NaturalObjectNameAt(0, 2) != "Berry bush" ||
                terrainLayer.NaturalObjectStageAt(0, 2) != "available" || mapObjectVisuals.ContainsKey("resource:sample-berry-bush") == false)
                throw new InvalidOperationException("Natural food patches must draw and remain inspectable as distinct objects.");
            RenderMap(sample with { Resources = [sampleResource, sampleNaturalObject with { Quantity = 0, State = "depleted" }] });
            if (terrainLayer.NaturalObjectStageAt(0, 2) != "regrowing")
                throw new InvalidOperationException("Renewable natural patches must render their depleted/regrowing transition.");
            var naturalRoster = new[]
            {
                sampleNaturalObject,
                new OwnerWorldResource("sample-wild-greens", "food", new(1, 0), true, "available", 4, 8,
                    NaturalObjectKind: "wild_greens"),
                new OwnerWorldResource("sample-fiber", "fiber", new(2, 0), true, "available", 4, 8,
                    NaturalObjectKind: "fiber_plant"),
                new OwnerWorldResource("sample-reeds", "fiber", new(3, 0), true, "available", 4, 8,
                    NaturalObjectKind: "reeds"),
                new OwnerWorldResource("sample-stone", "stone", new(0, 1), false, "available", 3, 3,
                    NaturalObjectKind: "stone_outcrop"),
                new OwnerWorldResource("sample-iron", "iron_ore", new(1, 2), false, "available", 3, 3,
                    NaturalObjectKind: "iron_outcrop"),
                new OwnerWorldResource("sample-gold", "gold_ore", new(2, 2), false, "available", 3, 3,
                    NaturalObjectKind: "gold_outcrop"),
                new OwnerWorldResource("sample-diamond", "diamond", new(3, 2), false, "available", 3, 3,
                    NaturalObjectKind: "diamond_outcrop"),
                new OwnerWorldResource("sample-clay", "clay", new(0, 3), false, "available", 3, 3,
                    NaturalObjectKind: "clay_bank"),
                new OwnerWorldResource("sample-seed-patch", "seed", new(1, 3), true, "available", 4, 8,
                    NaturalObjectKind: "wild_seed_patch"),
                new OwnerWorldResource("sample-fertile-soil", "fertile_land", new(2, 3), false, "available", 1, 1,
                    NaturalObjectKind: "fertile_soil"),
            };
            var expectedNaturalNames = new[]
            {
                "Berry bush", "Wild greens", "Fiber plant", "Reeds", "Stone outcrop", "Iron outcrop",
                "Gold outcrop", "Diamond outcrop", "Clay bank", "Wild seed patch", "Fertile soil",
            };
            RenderMap(sample with { Resources = [sampleResource, .. naturalRoster] });
            foreach (var (resource, expectedName) in naturalRoster.Zip(expectedNaturalNames))
            {
                if (terrainLayer.NaturalObjectNameAt(resource.Position.X, resource.Position.Y) != expectedName ||
                    terrainLayer.NaturalObjectStageAt(resource.Position.X, resource.Position.Y) != "available" ||
                    !mapObjectVisuals.TryGetValue("resource:" + resource.Id, out var resourceVisual) ||
                    !resourceVisual.TooltipText.Contains(expectedName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The {expectedName} natural object must draw as a distinct inspectable map site.");
            }
            var sampleOrchard = new OwnerWorldResource("sample-orchard", "fruit", new(2, 1), true,
                "available", 1, 1, 1, 3, "spring", "orchard", TreeStage: "fruiting");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, sampleOrchard] });
            if (terrainLayer.TreeStageAt(2, 1) != "fruiting" || mapObjectVisuals.ContainsKey("resource:sample-orchard"))
                throw new InvalidOperationException("Orchard fruit must render on one tree tile instead of a resource label.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, sampleOrchard with { Quantity = 0, TreeStage = "picked" }] });
            if (terrainLayer.TreeStageAt(2, 1) != "picked")
                throw new InvalidOperationException("Picked orchard trees must lose their visible fruit.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, sampleOrchard with { Quantity = 0, TreeStage = "growing" }] });
            if (terrainLayer.TreeStageAt(2, 1) != "growing")
                throw new InvalidOperationException("Regrowing orchard trees must show their growing stage.");
            var builtMarker = mapObjectVisuals["building:test-hall"];
            if (!builtMarker.Text.Contains("Test hall", StringComparison.Ordinal) || builtMarker.Size.X <= builtMarker.Size.Y)
                throw new InvalidOperationException("Built structures must render their name and multi-tile footprint.");
            var founderPosition = new OwnerWorldPosition(2, 0);
            var founder = new OwnerWorldInhabitant("founder-ui-test", "Rowan", "active", founderPosition,
                8_000, [], [], new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(founderPosition, [founderPosition], [founderPosition]), false)
            {
                Survival = new OwnerWorldSurvival(8_200, 300, true, false, 7_400, null),
            };
            var occupied = sample with { Inhabitants = [founder] };
            RenderMap(occupied);
            var founderButton = inhabitantVisuals[founder.Id];
            var founderButtonIdentity = founderButton.GetInstanceId();
            if (founderButton.Variant != AgentSprites.VariantFor(founder.Id) || !founderButton.ShowNameTag ||
                founderButton.Caption.Length == 0)
                throw new InvalidOperationException("A lone agent on the map must use their stable sprite and show a name tag.");
            if (terrainLayer.CampResourceSpriteCount == 0 || mapObjectVisuals["resource:wood"].Text.Contains('▰'))
                throw new InvalidOperationException("Older camp resources such as the wood store must draw as sprites instead of glyphs.");
            if (terrainLayer.BuildingSpriteCount != occupied.PlacedBuildings.Count ||
                mapObjectVisuals.TryGetValue("building:test-hall", out var hallMarker) && hallMarker.Text.Contains('⌂'))
                throw new InvalidOperationException("Placed buildings must be drawn as roof art rather than text glyphs.");
            selectedInhabitantId = founder.Id;
            RenderSelectedInhabitantCard(occupied);
            RenderMap(occupied with { WorldTick = 1 });
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (inhabitantVisuals[founder.Id].GetInstanceId() != founderButtonIdentity ||
                !selectedActorConditionLabel.Text.Contains("Warmth 82%", StringComparison.Ordinal) ||
                !selectedActorConditionLabel.IsVisibleInTree() ||
                !selectedInhabitantCard.GetGlobalRect().Encloses(selectedActorConditionLabel.GetGlobalRect()))
                throw new InvalidOperationException("Agent hover targets and condition stats must survive observation refreshes.");
            if (!mapCanvas.GetGlobalRect().Grow(1).Encloses(selectedInhabitantCard.GetGlobalRect()))
                throw new InvalidOperationException($"The agent card must fit inside the world view: map={mapCanvas.GetGlobalRect()} card={selectedInhabitantCard.GetGlobalRect()}.");
            if (selectedInhabitantCard.GetGlobalRect().Intersects(inhabitantVisuals[founder.Id].GetGlobalRect()))
                throw new InvalidOperationException($"The agent card must not cover the agent it describes: card={selectedInhabitantCard.GetGlobalRect()} agent={inhabitantVisuals[founder.Id].GetGlobalRect()}.");
            selectedInhabitantId = null;
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            selectedInhabitantId = founder.Id;
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            if (inhabitantSocialDetails.GetParsedText().Length == 0 || privateThoughtHistory.GetParsedText().Length == 0)
                throw new InvalidOperationException("Reselecting an unchanged agent must restore their social details and private thoughts.");
            RenderSelectedInhabitantCard(occupied with
            {
                Inhabitants = [founder with
                {
                    Survival = null,
                    PublicIntention = new OwnerWorldPublicIntention("safe_idle", "keeping a safe routine", "deterministic", 1),
                    Relationships = [new OwnerWorldInhabitantRelationship("home:test", "household:one",
                        "household_membership", "accepted", "household", 1)],
                }],
                Stockpiles = [new("household:one", "Founder's household", [])],
            });
            if (selectedActorConditionLabel.Visible ||
                !inhabitantSocialDetails.Text.Contains("Member of Founder's household", StringComparison.Ordinal) ||
                inhabitantSocialDetails.Text.Contains("household:one", StringComparison.OrdinalIgnoreCase) ||
                inhabitantSocialDetails.Text.Contains("Unassigned", StringComparison.Ordinal) ||
                !inhabitantSocialDetails.Text.Contains("Wants to take it easy.", StringComparison.Ordinal))
                throw new InvalidOperationException($"The agent card must read naturally, name households and omit unavailable condition or unassigned-role placeholders: {inhabitantSocialDetails.Text}");
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            if (!selectedActorConditionLabel.Visible)
                throw new InvalidOperationException("Reported agent condition must be shown again.");
            UpdateTileHover(founderButton.Position + mapStage.Position + founderButton.Size / 2);
            if (terrainLayer.HoveredTile is not null)
                throw new InvalidOperationException("An agent marker must take hover priority over its ground tile.");
            UpdateTileHover(new Vector2(currentTileSize * 1.5f, currentTileSize * 0.5f) + mapStage.Position);
            if (terrainLayer.HoveredTile != new Vector2I(1, 0))
                throw new InvalidOperationException("The hovered ground tile must receive a square outline.");
            selectedInhabitantId = null;
            selectedInhabitantCard.Hide();
            var agentClick = founderButton.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = agentClick,
                GlobalPosition = agentClick,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }, inLocalCoords: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (selectedInhabitantId != founder.Id)
                throw new InvalidOperationException("Clicking an agent marker must select the agent before the ground tile.");
            selectedInhabitantId = null;
            RenderMap(sample with { Resources = [], PlacedBuildings = [] });
            if (inhabitantVisuals.ContainsKey(founder.Id))
                throw new InvalidOperationException("Removed agent marker was retained.");
            if (mapObjectVisuals.ContainsKey("resource:wood")) throw new InvalidOperationException("Removed resource marker was retained.");
            if (mapObjectVisuals.ContainsKey("building:test-hall")) throw new InvalidOperationException("Removed building marker was retained.");
            var crowded = sample with
            {
                WorldId = "ui-marker-bounds",
                PackedTerrain = null,
                PackedMapLayers = null,
                MapLayersDigest = null,
                Tiles = Enumerable.Range(0, 64 * 64)
                    .Select(index => new OwnerWorldTile(index % 64, index / 64, "meadow")).ToArray(),
                Inhabitants = Enumerable.Range(0, 4)
                    .Select(index => founder with { Id = $"crowded-{index}", Position = new OwnerWorldPosition(2, 2) })
                    .ToArray(),
                Resources = [],
                PlacedBuildings = [],
            };
            foreach (var zoom in new[] { 1f, 2f, 4f })
            {
                cameraZoom = zoom;
                RenderMap(crowded);
                CenterCameraAt(new Vector2(2.5f, 2.5f));
                var tileRect = new Rect2(new Vector2(2 * currentTileSize, 2 * currentTileSize),
                    new Vector2(currentTileSize, currentTileSize));
                var markers = crowded.Inhabitants.Select(person => inhabitantVisuals[person.Id]).ToArray();
                if (markers.Any(marker => !tileRect.Encloses(new Rect2(marker.Position, marker.Size))) ||
                    markers.Where((marker, i) => markers.Skip(i + 1)
                        .Any(other => new Rect2(marker.Position, marker.Size).Intersects(
                            new Rect2(other.Position, other.Size)))).Any())
                    throw new InvalidOperationException($"Crowded marker hitboxes overflow or overlap at {currentTileSize}px tiles.");
                UpdateTileHover(new Vector2(3.5f * currentTileSize, 2.5f * currentTileSize) + mapStage.Position);
                if (terrainLayer.HoveredTile != new Vector2I(3, 2))
                    throw new InvalidOperationException("An adjacent tile must not hit a crowded agent marker.");
            }
            RenderMap(sample with { Resources = [], PlacedBuildings = [] });
            var smallMapTileSize = currentTileSize;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = mapCanvas.Size / 2 });
            if (currentTileSize <= smallMapTileSize)
                throw new InvalidOperationException("Mouse-wheel zoom must work even when the small starter map reaches its fitted tile-size cap.");
            RenderMap(sample with
            {
                WorldId = "ui-navigation",
                PackedTerrain = null,
                PackedMapLayers = null,
                MapLayersDigest = null,
                Tiles = Enumerable.Range(0, 192)
                    .Select(index => new OwnerWorldTile(index % 16, index / 16, "meadow")).ToArray(),
                Resources = [],
                PlacedBuildings = [],
            });
            mapButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!worldOverviewPanel.Visible || worldOverview.VisibleTiles.Size.Y <= 0)
                throw new InvalidOperationException("The top-left map button did not open a camera-aware world overview.");
            var fittedTileSize = currentTileSize;
            var fittedViewHeight = worldOverview.VisibleTiles.Size.Y;
            for (var index = 0; index < 2; index++)
                HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = mapCanvas.Size / 2 });
            if (currentTileSize <= fittedTileSize || worldOverview.VisibleTiles.Size.Y >= fittedViewHeight)
                throw new InvalidOperationException($"Mouse-wheel zoom did not narrow the visible world area: tile={fittedTileSize}->{currentTileSize}, view={fittedViewHeight}->{worldOverview.VisibleTiles.Size.Y}.");
            var beforeOverviewClick = mapStage.Position;
            worldOverview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(worldOverview.Size.X / 2, 12),
            });
            if (mapStage.Position.DistanceTo(beforeOverviewClick) < 1)
                throw new InvalidOperationException("Clicking the overview did not move the world camera.");
            var beforeOverviewDrag = mapStage.Position;
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(worldOverview.Size.X / 2, worldOverview.Size.Y - 12),
            });
            worldOverview._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            if (mapStage.Position.DistanceTo(beforeOverviewDrag) < 1)
                throw new InvalidOperationException("Dragging the overview did not move the world camera.");
            var beforeMiddleDrag = mapStage.Position;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            HandleMapInput(new InputEventMouseMotion { Relative = new Vector2(0, 60) });
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
            if (mapStage.Position.DistanceTo(beforeMiddleDrag) < 1)
                throw new InvalidOperationException("Middle-drag did not pan the world camera.");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            var beforeKeyboardPan = mapStage.Position;
            PanCameraForFrame(new Vector2(0, 1), 0.1);
            if (mapStage.Position.DistanceTo(beforeKeyboardPan) < 1)
                throw new InvalidOperationException("Keyboard panning did not move the world camera.");
            var eventDestination = cameraCenterTiles.X < 8 ? new OwnerWorldPosition(15, 11) : new OwnerWorldPosition(0, 0);
            knownEvents[100] = new OwnerWorldEvent(100, 1, "food_consumed", "founder-scout", eventDestination);
            RenderEventLog();
            eventLog.AddText("\nscroll-sentinel");
            RenderEventLog();
            if (!eventLog.GetParsedText().Contains("scroll-sentinel", StringComparison.Ordinal))
                throw new InvalidOperationException("An unchanged Event Log must not be rebuilt, which would reset its scroll position.");
            var beforeEventJump = cameraCenterTiles;
            eventLog.EmitSignal(RichTextLabel.SignalName.MetaClicked, "100");
            if (cameraCenterTiles.DistanceTo(beforeEventJump) < 0.5f)
                throw new InvalidOperationException("Clicking a located event did not move the world camera.");
            knownEvents[101] = new OwnerWorldEvent(101, 2, "food_consumed", "founder-scout", null);
            RenderEventLog();
            var loggedDay = SplitClock(DisplayWorldClock(1)).Date;
            if (eventLog.GetParsedText().Split(loggedDay).Length != 2)
                throw new InvalidOperationException($"Same-day events must share one date heading: {eventLog.GetParsedText()}");
            knownEvents.Remove(101);
            RenderEventLog();
            // Events that arrive while the log is closed are counted on its
            // button, then marked read once it is opened.
            eventsPanel.Hide();
            UpdateUnreadEvents(eventsWorldId);
            var readBefore = unreadEvents;
            knownEvents[102] = new OwnerWorldEvent(102, 3, "food_consumed", "founder-scout", null);
            RenderEventLog();
            if (unreadEvents != readBefore + 1 || !eventsBadge.Visible ||
                eventsBadgeLabel.Text != (readBefore + 1).ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException($"A new event must show an unread count on the Event Log button: {unreadEvents} after {readBefore}.");
            if (eventsBadge.ZIndex < 1 || !eventsBadge.ZAsRelative)
                throw new InvalidOperationException("The unread count must draw over the HUD button next to Events instead of being covered by it.");
            ToggleEvents();
            if (unreadEvents != 0 || eventsBadge.Visible || !eventLog.GetParsedText().Contains('●'))
                throw new InvalidOperationException("Opening the Event Log must mark events read and dot the rows that were new.");
            ToggleEvents();
            knownEvents.Remove(102);
            RenderEventLog();
            var largeTerrain = Enumerable.Range(0, 256 * 128)
                .Select(index => (byte)(index % 37 == 0 ? 3 : 0)).ToArray();
            var largeMap = sample with
            {
                WorldId = "ui-large-map",
                MapManifestDigest = "ui-large-map-v1",
                Tiles = [],
                PackedTerrain = new OwnerWorldPackedTerrain(256, 128, "terrain-kind-v1",
                    Convert.ToBase64String(largeTerrain)),
                PackedMapLayers = null,
                MapLayersDigest = null,
                Authoring = new OwnerWorldAuthoringState(true, 0, 0, 0, "ui-large-map-v1",
                    "ui-large-map-v1", "clear", "spring", []),
                WeatherRegionSize = 32,
                WeatherRegions = Enumerable.Range(0, 4)
                    .SelectMany(x => Enumerable.Range(0, 2).Select(y => new OwnerWeatherRegion(x, y, "snow", 12)))
                    .Append(new OwnerWeatherRegion(4, 2, "rain", 78)).ToArray(),
                Resources = [],
                PlacedBuildings = [],
                Towns = [],
            };
            RenderMap(largeMap);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (terrainLayer.DrawsGroundTextures)
                throw new InvalidOperationException("Overview zoom must keep the flat one-pixel-per-tile palette instead of textures.");
            if (terrainLayer.GetChildCount() != 0 || terrainLayer.VisibleTileCount >= largeTerrain.Length / 2 ||
                worldOverview.VisibleTiles.Size.X >= 256)
                throw new InvalidOperationException($"A regional map must draw only the visible terrain without per-tile nodes: children={terrainLayer.GetChildCount()}, visible={terrainLayer.VisibleTileCount}, overview={worldOverview.VisibleTiles.Size}.");
            if (seasonLabel.Text != "Spring" || weatherLabel.Text != "Rain" ||
                !worldInfoText.Text.Contains("Soil moisture here: 78%", StringComparison.Ordinal) ||
                terrainLayer.WeatherAt(150, 80) != "rain" || terrainLayer.WeatherAt(20, 20) != "snow")
                throw new InvalidOperationException("The world HUD and info must show weather and moisture at the camera.");
            // Regions are squares on the host; on the map their weather must
            // reach the middle fully but end in a wandering, soft edge.
            var edgeColumns = new List<int>();
            for (var edgeRow = 68; edgeRow <= 92; edgeRow += 2)
                for (var column = 100; column < 160; column++)
                    if (weatherLayer.CoverageAt(column, edgeRow, "rain") > 0.5f)
                    {
                        edgeColumns.Add(column);
                        break;
                    }
            if (weatherLayer.GetChildCount() != 0 || weatherLayer.CoverageAt(144, 80, "rain") < 0.9f ||
                weatherLayer.CoverageAt(144, 80, "snow") > 0.1f || edgeColumns.Count < 10 ||
                edgeColumns.Max() - edgeColumns.Min() < 3)
                throw new InvalidOperationException($"Rain must fill its region and end in a wandering edge, not a square: edge={string.Join(',', edgeColumns)}.");
            // Lightning brightens softly and rarely: never a strobe.
            var (brightest, lit, flashes, wasLit) = (0f, 0, 0, false);
            for (var step = 0; step < 9000; step++)
            {
                var flash = WeatherLayer.LightningFlash(step * 0.01);
                brightest = Math.Max(brightest, flash);
                if (flash > 0.02f) lit++;
                if (flash > 0.02f && !wasLit) flashes++;
                wasLit = flash > 0.02f;
            }
            if (brightest > 0.31f || lit > 900 || flashes > 25)
                throw new InvalidOperationException($"Lightning must stay soft and rare: peak={brightest}, lit samples={lit}, flashes={flashes}.");
            var startedMap = largeMap with { FounderSetup = null };
            RenderWorldHud(startedMap);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The HUD's pause control shows stopped time on its own, and the
            // weather holds still until time runs again.
            var frozenAt = weatherLayer.AnimationTime;
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (pauseButton.ThemeTypeVariation != "EmberButton" || pauseButton.Text != "Paused" ||
                !weatherLayer.Paused || weatherLayer.AnimationTime != frozenAt)
                throw new InvalidOperationException($"A paused world must show Paused on its pause control and freeze its weather: {pauseButton.Text}, {weatherLayer.AnimationTime - frozenAt}s.");
            RenderWorldHud(startedMap with { Authoring = startedMap.Authoring! with { IsPaused = false } });
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (pauseButton.ThemeTypeVariation == "EmberButton" || weatherLayer.Paused || weatherLayer.AnimationTime <= frozenAt)
                throw new InvalidOperationException("Weather must move again, and the pause control return to normal, once time runs.");
            RenderWorldHud(largeMap);
            UpdateTileHover(mapCanvas.Size / 2);
            var hoveredCenter = TileAtCanvas(mapCanvas.Size / 2, largeMap);
            if (!hoverReadout.Visible || !hoverReadoutLabel.Text.EndsWith($"{hoveredCenter.X}, {hoveredCenter.Y}", StringComparison.Ordinal) ||
                hoverReadout.MouseFilter != Control.MouseFilterEnum.Ignore)
                throw new InvalidOperationException($"Hovering ground must show a click-through readout ending in the tile position: {hoverReadoutLabel.Text}");
            var beforeRoad = largeMap with { RoadTiles = [] };
            UpdateHoverReadout(beforeRoad, hoveredCenter);
            var afterRoad = beforeRoad with { RoadTiles = [new OwnerWorldPosition(hoveredCenter.X, hoveredCenter.Y)] };
            UpdateHoverReadout(afterRoad, hoveredCenter);
            if (!hoverReadoutLabel.Text.Contains(" · Road", StringComparison.Ordinal))
                throw new InvalidOperationException("The hovered tile must reflect a newly observed road without moving the pointer.");
            UpdateTileHover(new Vector2(-5, -5));
            if (hoverReadout.Visible)
                throw new InvalidOperationException("Leaving the map must hide the hover readout.");
            var beforeLargePan = worldOverview.VisibleTiles.Position;
            CenterCameraAt(new Vector2(20, 20));
            if (worldOverview.VisibleTiles.Position.DistanceTo(beforeLargePan) < 1 ||
                terrainLayer.VisibleTileCount >= largeTerrain.Length / 2)
                throw new InvalidOperationException("Panning a large map must update the camera-bounded terrain view.");
            if (seasonLabel.Text != "Spring" || weatherLabel.Text != "Snow" ||
                !worldInfoText.Text.Contains("here: Spring · Snow", StringComparison.Ordinal) ||
                !worldInfoText.Text.Contains("Soil moisture here: 12%", StringComparison.Ordinal))
                throw new InvalidOperationException($"Panning must update HUD and World Info to local weather: camera={cameraCenterTiles}, HUD={seasonLabel.Text} · {weatherLabel.Text}, info={worldInfoText.Text}.");
            var wrappedMap = largeMap with
            {
                WorldId = "ui-wrapped-map",
                WrapsEastWest = true,
                WeatherRegions = [.. largeMap.WeatherRegions, new OwnerWeatherRegion(7, 2, "storm", 40)],
                Resources = [new OwnerWorldResource("seam-wood", "construction", new(255, 64),
                    true, "available", 5, 10, 0, 0, "spring")],
            };
            RenderMap(wrappedMap);
            CenterCameraAt(new Vector2(0.5f, 64));
            if (worldOverview.VisibleTiles.Position.X >= 0 ||
                terrainLayer.VisibleTileCount >= largeTerrain.Length / 2 ||
                !worldOverview.WrapsEastWest ||
                terrainLayer.WeatherAt(-1, 70) != "storm" ||
                TileAtCanvas(mapCanvas.Size / 2 - new Vector2(2 * currentTileSize, 0), wrappedMap).X != 254)
                throw new InvalidOperationException("Wrapped camera must render and target the western seam without an empty edge.");
            var seamMarker = mapObjectVisuals["resource:seam-wood"];
            if (!mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()))
                throw new InvalidOperationException("Resources across the wrapped seam must remain visible at the camera.");
            var seamEntered = false;
            seamMarker.MouseEntered += () => seamEntered = true;
            GetViewport().PushInput(new InputEventMouseMotion
            {
                Position = seamMarker.GetGlobalRect().GetCenter(),
                GlobalPosition = seamMarker.GetGlobalRect().GetCenter(),
            }, inLocalCoords: true);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!seamEntered)
                throw new InvalidOperationException("A resource across the wrapped seam must remain hoverable.");
            PanCamera(new Vector2(-5, 0));
            if (cameraCenterTiles.X < 250 || worldOverview.VisibleTiles.End.X <= 256 ||
                !mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()))
                throw new InvalidOperationException("Panning west across a wrapped seam must not clamp the camera.");
            PanCamera(new Vector2(10, 0));
            if (cameraCenterTiles.X > 10 || worldOverview.VisibleTiles.Position.X >= 0 ||
                !mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()))
                throw new InvalidOperationException("Panning east across a wrapped seam must remain continuous.");
            var overviewAvailable = worldOverview.Size - new Vector2(12, 12);
            var overviewScale = Math.Min(overviewAvailable.X / 256, overviewAvailable.Y / 128);
            var overviewAtlasSize = new Vector2(256 * overviewScale, 128 * overviewScale);
            var overviewAtlasPosition = (worldOverview.Size - overviewAtlasSize) / 2;
            var overviewAtlasY = overviewAtlasPosition.Y + overviewAtlasSize.Y / 2;
            var overviewRightEdge = overviewAtlasPosition.X + overviewAtlasSize.X;
            CenterCameraAt(new Vector2(254, 64));
            var eastDragStart = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(overviewRightEdge - 1, overviewAtlasY),
            });
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewRightEdge + 48, overviewAtlasY),
            });
            var eastAfterFirstDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewRightEdge + 96, overviewAtlasY),
            });
            var eastAfterSecondDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            if (PositiveMod(eastAfterFirstDrag - eastDragStart, 256) <= 2 ||
                PositiveMod(eastAfterSecondDrag - eastAfterFirstDrag, 256) <= 2)
                throw new InvalidOperationException("Dragging past the wrapped overview's eastern edge must continue panning east.");

            var overviewLeftEdge = overviewAtlasPosition.X;
            CenterCameraAt(new Vector2(2, 64));
            var westDragStart = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(overviewLeftEdge + 1, overviewAtlasY),
            });
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewLeftEdge - 48, overviewAtlasY),
            });
            var westAfterFirstDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewLeftEdge - 96, overviewAtlasY),
            });
            var westAfterSecondDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            if (PositiveMod(westDragStart - westAfterFirstDrag, 256) <= 2 ||
                PositiveMod(westAfterFirstDrag - westAfterSecondDrag, 256) <= 2)
                throw new InvalidOperationException("Dragging past the wrapped overview's western edge must continue panning west.");

            CenterCameraAt(new Vector2(0.5f, 64));
            var wrappedStride = currentTileSize + TileGap;
            var seamMarkerCellSample = mapStage.Position + new Vector2(
                seamMarker.Position.X + Math.Min(seamMarker.Size.X / 2, wrappedStride / 2f),
                seamMarker.Position.Y + wrappedStride / 2f);
            var seamTileUnderMarker = TileAtCanvas(seamMarkerCellSample, wrappedMap);
            if (seamTileUnderMarker != new Vector2I(255, 64))
                throw new InvalidOperationException($"A seam marker must select its canonical tile before repeated wrapping: " +
                    $"tile={seamTileUnderMarker}, camera={cameraCenterTiles}, stage={mapStage.Position}, " +
                    $"marker={seamMarker.Position}+{seamMarker.Size}, sample={seamMarkerCellSample}.");
            var nearestSeamTileX = 255 + MathF.Round((cameraCenterTiles.X - 255) / 256) * 256;
            var seamSelectionPosition = mapStage.Position + new Vector2(
                (nearestSeamTileX + 0.5f) * wrappedStride, 64.5f * wrappedStride);
            HandleMapInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = seamSelectionPosition,
            });
            if (terrainLayer.SelectedTile != new Vector2I(255, 64) ||
                !selectedTileText.Text.Contains("Tile 255, 64", StringComparison.Ordinal))
                throw new InvalidOperationException("Selecting a wrapped seam marker must inspect its canonical tile.");

            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            var beforeFramePan = cameraCenterTiles;
            cameraCenterTiles = new Vector2(128, 64);
            PanCameraForFrame(new Vector2(1, 0), 0.1);
            var cardinalDistance = cameraCenterTiles.DistanceTo(new Vector2(128, 64));
            cameraCenterTiles = new Vector2(128, 64);
            PanCameraForFrame(new Vector2(1, 1), 0.05);
            PanCameraForFrame(new Vector2(1, 1), 0.05);
            if (Math.Abs(cameraCenterTiles.DistanceTo(new Vector2(128, 64)) - cardinalDistance) > 0.01f)
                throw new InvalidOperationException("Diagonal pan must match cardinal speed and depend on elapsed time, not repeat count.");
            var beforeEcho = cameraCenterTiles;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Right, Pressed = true, Echo = true });
            if (cameraCenterTiles != beforeEcho)
                throw new InvalidOperationException("Keyboard repeat events must not add camera movement.");
            cameraCenterTiles = beforeFramePan;
            var repeatedKeyStart = cameraCenterTiles.X;
            var previousKeyCenter = repeatedKeyStart;
            for (var step = 0; step < 512; step++)
            {
                PanCameraForFrame(new Vector2(1, 0), 0.1);
                if (Math.Abs(PositiveMod(cameraCenterTiles.X - previousKeyCenter, 256) - 1.5f) > 0.01f)
                    throw new InvalidOperationException("Held right movement must keep moving through multiple wrapped laps.");
                previousKeyCenter = cameraCenterTiles.X;
            }
            for (var step = 0; step < 512; step++)
            {
                PanCameraForFrame(new Vector2(-1, 0), 0.1);
                if (Math.Abs(PositiveMod(previousKeyCenter - cameraCenterTiles.X, 256) - 1.5f) > 0.01f)
                    throw new InvalidOperationException("Held left movement must keep moving through multiple wrapped laps.");
                previousKeyCenter = cameraCenterTiles.X;
            }
            if (Math.Abs(cameraCenterTiles.X - repeatedKeyStart) > 0.01f)
                throw new InvalidOperationException("Equal repeated horizontal key travel must return to the same wrapped position.");

            var dragStride = currentTileSize + TileGap;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            for (var lap = 0; lap < 4; lap++)
            {
                var beforeEastDrag = cameraCenterTiles.X;
                HandleMapInput(new InputEventMouseMotion
                {
                    Relative = new Vector2(-dragStride * (256 + 37), 0),
                });
                if (Math.Abs(PositiveMod(cameraCenterTiles.X - beforeEastDrag, 256) - 37) > 0.01f)
                    throw new InvalidOperationException("Repeated eastward main-map drags must cross full wrapped laps.");
            }
            for (var lap = 0; lap < 4; lap++)
            {
                var beforeWestDrag = cameraCenterTiles.X;
                HandleMapInput(new InputEventMouseMotion
                {
                    Relative = new Vector2(dragStride * (256 + 37), 0),
                });
                if (Math.Abs(PositiveMod(beforeWestDrag - cameraCenterTiles.X, 256) - 37) > 0.01f)
                    throw new InvalidOperationException("Repeated westward main-map drags must cross full wrapped laps.");
            }
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
            nearestSeamTileX = 255 + MathF.Round((cameraCenterTiles.X - 255) / 256) * 256;
            seamSelectionPosition = mapStage.Position + new Vector2(
                (nearestSeamTileX + 0.5f) * wrappedStride, 64.5f * wrappedStride);
            if (Math.Abs(cameraCenterTiles.X - repeatedKeyStart) > 0.01f ||
                terrainLayer.SelectedTile != new Vector2I(255, 64) ||
                TileAtCanvas(seamSelectionPosition, wrappedMap) != new Vector2I(255, 64) ||
                !mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()) ||
                !selectedTileText.Text.Contains("Tile 255, 64", StringComparison.Ordinal))
                throw new InvalidOperationException("Repeated wrapped travel must preserve marker visibility and canonical selection alignment.");

            if (mapObjectVisuals["resource:seam-wood"].Text.Contains('\n', StringComparison.Ordinal))
                throw new InvalidOperationException("A map marker too small for its name must show its glyph instead of a clipped fragment.");
            RenderMap(largeMap);
            CenterCameraAt(new Vector2(-5, 64));
            if (worldOverview.VisibleTiles.Position.X < 0 || worldOverview.WrapsEastWest)
                throw new InvalidOperationException("Non-wrapped worlds must retain bounded horizontal camera edges.");

            cameraZoom = 1;
            RenderMap(largeMap);
            var oldVisibleWidth = worldOverview.VisibleTiles.Size.X;
            var oldTileCount = terrainLayer.VisibleTileCount;
            var baselinePan = System.Diagnostics.Stopwatch.StartNew();
            for (var step = 0; step < 8; step++)
            {
                CenterCameraAt(new Vector2(80 + step, 64));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            baselinePan.Stop();
            cameraZoom = 0.65f;
            RenderMap(largeMap);
            // The HUD floats over the map, so the full-height view needs a
            // 9 px floor to keep the Small map's poles out of sight.
            if (currentTileSize > 9 || worldOverview.VisibleTiles.Size.X < oldVisibleWidth * 1.3f ||
                terrainLayer.VisibleTileCount > 40_000)
                throw new InvalidOperationException($"Overview zoom must widen bounded terrain coverage: tile={currentTileSize}, width={oldVisibleWidth}->{worldOverview.VisibleTiles.Size.X}, tiles={terrainLayer.VisibleTileCount}.");
            var wideVisibleWidth = worldOverview.VisibleTiles.Size.X;
            var wideTileCount = terrainLayer.VisibleTileCount;
            var widePan = System.Diagnostics.Stopwatch.StartNew();
            for (var step = 0; step < 8; step++)
            {
                CenterCameraAt(new Vector2(80 + step, 64));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            widePan.Stop();
            GD.Print($"Zoom comparison at {mapCanvas.Size}: 12 px={oldVisibleWidth:0} columns/{oldTileCount} tiles, {baselinePan.Elapsed.TotalMilliseconds:0} ms/8 pan frames; 8 px={wideVisibleWidth:0} columns/{wideTileCount} tiles, {widePan.Elapsed.TotalMilliseconds:0} ms/8 pan frames (headless sample).");
            cameraZoom = 1;
            RenderMap(largeMap);
            var waterCenter = WorldTerrainMap.FromTiles(
                (from y in Enumerable.Range(0, 9)
                 from x in Enumerable.Range(0, 9)
                 select new OwnerWorldTile(x, y, x is >= 5 and <= 7 && y is >= 5 and <= 7
                     ? "meadow" : "ocean")).ToArray(), 9, 9);
            var emptyWorldFocus = InitialCameraCenter(largeMap with
            {
                Towns = [],
                Inhabitants = [],
                Objects = [],
            }, waterCenter);
            if (emptyWorldFocus.DistanceTo(new Vector2(6.5f, 6.5f)) > 0.01f)
                throw new InvalidOperationException($"A new world without a Town or agents must open over dry land: camera={emptyWorldFocus}.");
            CenterCameraAt(new Vector2(80, 64));
            var zoomPointer = mapCanvas.Size * new Vector2(0.25f, 0.3f);
            var tileUnderPointer = (zoomPointer - mapStage.Position) / (currentTileSize + TileGap);
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = zoomPointer });
            var tileUnderPointerAfterZoom = (zoomPointer - mapStage.Position) / (currentTileSize + TileGap);
            if (tileUnderPointerAfterZoom.DistanceTo(tileUnderPointer) > 0.1f)
                throw new InvalidOperationException($"Mouse-wheel zoom must keep the pointed-at tile under the cursor: {tileUnderPointer} -> {tileUnderPointerAfterZoom}.");
            cameraZoom = 1;
            RenderMap(largeMap);
            var focusedMap = largeMap with
            {
                WorldId = "ui-camera-focus",
                Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0, [], [],
                    [new(180, 80), new(181, 80), new(180, 81), new(181, 81)])],
            };
            RenderMap(focusedMap);
            if (cameraCenterTiles.DistanceTo(new Vector2(181, 81)) > 1.5f)
                throw new InvalidOperationException($"A newly opened world must frame its first Town, not the map center: camera={cameraCenterTiles}.");
            RenderMap(focusedMap with
            {
                WorldId = "ui-camera-seam",
                WrapsEastWest = true,
                Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0, [], [], [new(255, 64), new(0, 64)])],
            });
            if (PositiveMod(cameraCenterTiles.X + 1, 256) > 2 || Math.Abs(cameraCenterTiles.Y - 64.5f) > 1.5f)
                throw new InvalidOperationException($"A Town across the wrapped seam must be framed as one place: camera={cameraCenterTiles}.");
            RenderMap(largeMap);
            var rosterPosition = new OwnerWorldPosition(180, 90);
            OwnerWorldInhabitant RosterAgent(string id, string name, string lifecycle, int fullness) =>
                new(id, name, lifecycle, rosterPosition, fullness, [], [],
                    new OwnerWorldRoute("idle", null, null, [], string.Empty),
                    new OwnerWorldSpatialKnowledge(rosterPosition, [rosterPosition], [rosterPosition]), false);
            var rosterMap = largeMap with
            {
                WorldId = "ui-roster",
                Inhabitants =
                [
                    RosterAgent("roster-rowan", "Rowan", "active", 2_000) with
                    {
                        PublicIntention = new OwnerWorldPublicIntention("seek_food", "looking for food", "deterministic", 1),
                    },
                    RosterAgent("roster-ilya", "Ilya", "active", 9_000),
                    RosterAgent("roster-mira", "Mira", "dead", 5_000),
                ],
            };
            RenderMap(rosterMap);
            RenderInhabitantList(rosterMap);
            if (inhabitantList.ItemCount != 4 || !inhabitantList.Visible ||
                !inhabitantList.GetItemText(0).StartsWith("Ilya", StringComparison.Ordinal) ||
                !inhabitantList.GetItemText(1).Contains("looking for food", StringComparison.Ordinal) ||
                !inhabitantList.GetItemText(1).Contains("very hungry", StringComparison.Ordinal) ||
                inhabitantList.GetItemText(0).Contains("hungry", StringComparison.Ordinal) ||
                inhabitantList.GetItemText(2) != "Deceased" || inhabitantList.IsItemSelectable(2) ||
                !inhabitantList.GetItemText(3).StartsWith("Mira", StringComparison.Ordinal))
                throw new InvalidOperationException("The roster must list the living with their activity and hunger before the deceased.");
            RenderWorldHud(rosterMap);
            if (!agentsWarning.Visible || inhabitantsButton.Text != "2" ||
                !inhabitantsButton.TooltipText.Contains("hungry: Rowan", StringComparison.Ordinal))
                throw new InvalidOperationException("The Agents button must count the living and flag anyone hungry.");
            CenterCameraAt(new Vector2(40, 30));
            SelectInhabitantFromList(1);
            if (selectedInhabitantId != "roster-rowan" || cameraCenterTiles.DistanceTo(new Vector2(180.5f, 90.5f)) > 1.5f)
                throw new InvalidOperationException($"Choosing a living agent in the roster must bring them into view: camera={cameraCenterTiles}.");
            selectedInhabitantId = null;
            RenderInhabitantList(rosterMap with { Inhabitants = [] });
            if (inhabitantList.Visible || !rosterSummaryLabel.Text.Contains("No one lives here yet", StringComparison.Ordinal))
                throw new InvalidOperationException("An empty roster must show its summary without an empty list box.");
            var formerPosition = new OwnerWorldPosition(2, 2);
            var deceased = new OwnerWorldInhabitant("archived-mira", "Mira", "dead", formerPosition,
                5_000, [], [new("age-band", "elder"), new("death-tick", "1")],
                new OwnerWorldRoute("deceased", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(formerPosition, [formerPosition], [formerPosition]), false)
            {
                RecentPrivateThoughts = [new OwnerWorldPrivateThought(1, "I hope Rowan remembers our garden.")],
                RecentMemories = [new OwnerWorldAgentMemory(1, "living-parent", "Rowan",
                    "I hid the garden tools where Rowan cannot see them.", "private")],
                RecentKnowledgeFacts = [new OwnerWorldKnowledgeFact(2, 7, 9, "Forest", ["wood"],
                    "Mira", "firsthand", null)],
                KnowledgeArtifacts = [new OwnerWorldKnowledgeArtifact("knowledge-artifact-000001", "field_map",
                    "Field map · 2 sites", 2, "Mira",
                    [new OwnerWorldKnowledgeSite(7, 9, "Forest", ["wood"], "Mira"),
                     new OwnerWorldKnowledgeSite(8, 9, "River", [], "Mira")])],
            };
            var historicalSnapshot = sample with
            {
                WorldId = "ui-deceased",
                Inhabitants = [deceased],
                Resources = [],
                PlacedBuildings = [],
            };
            RenderMap(historicalSnapshot);
            RenderInhabitantList(historicalSnapshot);
            selectedInhabitantId = deceased.Id;
            RenderSelectedInhabitantCard(historicalSnapshot);
            if (entityLayer.GetChildren().Any(child => !child.IsQueuedForDeletion()) ||
                inhabitantList.ItemCount != 1 || !rosterSummaryLabel.Text.Contains("1 deceased", StringComparison.Ordinal) ||
                !selectedInhabitantCard.Visible || !selectedActorSummaryLabel.Text.Contains("Dead", StringComparison.Ordinal) ||
                renameAgentInput.Text != "Mira")
                throw new InvalidOperationException("A deceased inhabitant must remain inspectable without appearing as a living map actor.");
            if (!privateThoughtHistory.Text.Contains("I hope Rowan remembers our garden.", StringComparison.Ordinal) ||
                !privateThoughtHistory.Text.Contains("historical", StringComparison.Ordinal))
                throw new InvalidOperationException("Deceased profiles must retain their saved private thoughts without generating new ones.");
            memoriesButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!memoriesPanel.Visible ||
                !memoryHistory.Text.Contains("I hid the garden tools", StringComparison.Ordinal) ||
                !memoryHistory.Text.Contains("Field map", StringComparison.Ordinal) ||
                !memoryHistory.Text.Contains("Forest at (7, 9)", StringComparison.Ordinal) ||
                inhabitantSocialDetails.Text.Contains("I hid the garden tools", StringComparison.Ordinal))
                throw new InvalidOperationException("Historical memories and bounded agent-owned map records must be inspectable separately from public social notes.");
            memoriesPanel.Hide();
            cameraZoom = 4;
            RenderMap(historicalSnapshot);
            var deathDestination = cameraCenterTiles.X < 8
                ? new OwnerWorldPosition(15, 11) : new OwnerWorldPosition(0, 0);
            knownEvents[101] = new OwnerWorldEvent(101, 2, "inhabitant_removed", deceased.Id,
                deathDestination);
            RenderEventLog();
            if (!eventLog.GetParsedText().Contains("died.", StringComparison.Ordinal) ||
                eventLog.GetParsedText().Contains("scroll-sentinel", StringComparison.Ordinal) ||
                DescribeWorldEvent(knownEvents[101], historicalSnapshot) != "Mira died." ||
                gameSettingsContent.GetChildren().OfType<Label>()
                    .Any(label => label.Text.Contains("event pop-ups", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Deaths must remain in the Event Log without an event pop-up setting.");
            ToggleEvents();
            if (!eventsPanel.Visible || !selectedInhabitantCard.Visible)
                throw new InvalidOperationException("The Event Log and agent info panel must remain available.");
            var beforeDeathJump = cameraCenterTiles;
            eventLog.EmitSignal(RichTextLabel.SignalName.MetaClicked, "101");
            if (eventsPanel.Visible || cameraCenterTiles.DistanceTo(beforeDeathJump) < 0.5f)
                throw new InvalidOperationException("A death in the Event Log must jump to its location.");
            var parentPosition = new OwnerWorldPosition(1, 1);
            var parent = new OwnerWorldInhabitant("living-parent", "Rowan", "active", parentPosition,
                7_000, [], [new("age-band", "adult")],
                new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(parentPosition, [parentPosition], [parentPosition]), false)
            {
                Relationships =
                [
                    new OwnerWorldInhabitantRelationship("birth:test", deceased.Id,
                        "biological_parentage", "accepted", "family", 1, "parent"),
                    new OwnerWorldInhabitantRelationship("partner:test", "living-partner",
                        "partnership", "accepted", "family", 1, "partner"),
                ],
            };
            var partner = new OwnerWorldInhabitant("living-partner", "Ilya", "active", parentPosition,
                7_000, [], [new("age-band", "adult")],
                new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(parentPosition, [parentPosition], [parentPosition]), false)
            {
                Relationships = [new OwnerWorldInhabitantRelationship("partner:test", parent.Id,
                    "partnership", "accepted", "family", 1, "partner")],
            };
            var child = deceased with
            {
                Relationships = [new OwnerWorldInhabitantRelationship("birth:test", parent.Id,
                    "biological_parentage", "accepted", "family", 1, "child")],
            };
            ShowFamilyTree(historicalSnapshot with { Inhabitants = [parent, child, partner] }, child.Id);
            if (!familyTreePanel.Visible || familyTreeView.ParentEdgeCount != 1 || familyTreeView.PartnerEdgeCount != 1 ||
                !familyTreeView.VisiblePersonIds.Contains(parent.Id) ||
                !familyTreeView.VisiblePersonIds.Contains(child.Id) ||
                !familyTreeView.VisiblePersonIds.Contains(partner.Id))
                throw new InvalidOperationException("Family tree must show ancestry, partnerships and deceased profiles.");
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ApplyResponsiveLayout();
            if (!mapCanvas.GetGlobalRect().Encloses(familyTreePanel.GetGlobalRect()))
                throw new InvalidOperationException("Family tree panel must fit within the world view.");
            familyTreeView.GetChildren().OfType<Button>().Single(button => button.Text.StartsWith(parent.DisplayName, StringComparison.Ordinal))
                .EmitSignal(BaseButton.SignalName.Pressed);
            if (selectedInhabitantId != parent.Id || familyTreePanel.Visible)
                throw new InvalidOperationException("Selecting a relative must open that person's agent profile.");
            familyTreeView.SetPeople("roommates-only", [parent with { Relationships = [] }, child with { Relationships = [] }, partner with { Relationships = [] }], child.Id);
            if (familyTreeView.VisiblePersonIds.Count != 1 || familyTreeView.ParentEdgeCount != 0)
                throw new InvalidOperationException("Household membership must not create a family link.");
            familyTreePanel.Hide();
            eventsPanel.Show();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (eventsPanel.Visible)
                throw new InvalidOperationException("Escape must close the open world panel.");
            choosingFirstTownSite = true;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (choosingFirstTownSite || townSiteButton.Text == "Cancel Town site")
                throw new InvalidOperationException("Escape must cancel an active Town-site selection.");
            if (HudButtons().Any(button => button.FocusMode == Control.FocusModeEnum.None))
                throw new InvalidOperationException("Top-bar actions must remain reachable by keyboard focus.");
            eventsButton.GrabFocus();
            eventsButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (eventsButton.HasFocus())
                throw new InvalidOperationException("A pressed top-bar button must return keyboard focus to map controls.");
            eventsButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!pauseButton.TooltipText.Contains("(Space)", StringComparison.Ordinal) ||
                !inhabitantsButton.TooltipText.Contains("(R)", StringComparison.Ordinal) ||
                !eventsButton.TooltipText.Contains("(E)", StringComparison.Ordinal))
                throw new InvalidOperationException("Top-bar tooltips must keep naming their keyboard shortcuts after refreshes.");
            RenderMap(occupied);
            ClearInhabitantSelection();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.I, Pressed = true });
            if (!worldInfoPanel.Visible || !worldInfoText.Text.Contains("F1", StringComparison.Ordinal))
                throw new InvalidOperationException("I must open World Info, which points to the F1 controls list.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.I, Pressed = true });
            if (worldInfoPanel.Visible)
                throw new InvalidOperationException("Pressing a panel shortcut again must close that panel.");
            ShowWorldInfoPage(towns: false);
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.T, Pressed = true });
            if (!worldInfoPanel.Visible || !WorldInfoShowsTowns)
                throw new InvalidOperationException("T must open World Info on its Towns page; a world can hold several Towns.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.T, Pressed = true });
            if (worldInfoPanel.Visible)
                throw new InvalidOperationException("Pressing T again must close the Towns page.");
            // The three HUD groups float over the map side by side, never on top of each other.
            var hudGroups = new[] { hudLeft, hudTime, hudRight }.Select(row => row.GetParent<Control>().GetGlobalRect()).ToArray();
            if (hudGroups.Any(rect => !mapCanvas.GetGlobalRect().Encloses(rect)) ||
                hudGroups[0].Intersects(hudGroups[1]) || hudGroups[1].Intersects(hudGroups[2]) || hudGroups[0].Intersects(hudGroups[2]))
                throw new InvalidOperationException($"HUD groups must sit inside the world view without overlapping: {string.Join(' ', hudGroups)}.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.F1, Pressed = true });
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!controlsPanel.Visible || !mapCanvas.GetGlobalRect().Encloses(controlsPanel.GetGlobalRect()))
                throw new InvalidOperationException($"F1 must open the controls list inside the world view: map={mapCanvas.GetGlobalRect()} controls={controlsPanel.GetGlobalRect()}.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (controlsPanel.Visible)
                throw new InvalidOperationException("Escape must close the controls list.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Minus, Pressed = true });
            var zoomedOutTile = currentTileSize;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Equal, Pressed = true });
            if (currentTileSize <= zoomedOutTile)
                throw new InvalidOperationException("The + key must zoom in after - zoomed out.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.N, Pressed = true });
            if (selectedInhabitantId != founder.Id ||
                !mapCanvas.GetGlobalRect().Encloses(inhabitantVisuals[founder.Id].GetGlobalRect()))
                throw new InvalidOperationException($"N must select the next living agent and bring them into view: selected={selectedInhabitantId} marker={inhabitantVisuals[founder.Id].GetGlobalRect()}.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.N, Pressed = true });
            if (selectedInhabitantId != founder.Id)
                throw new InvalidOperationException("N with a single living agent must keep them selected.");
            selectedInhabitantCard.Hide();
            selectedInhabitantId = null;
            ClearTileSelection();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (!gameMenuPanel.Visible || !topBarShade.Visible)
                throw new InvalidOperationException("Escape with nothing open must open the Pause Menu.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.I, Pressed = true });
            if (worldInfoPanel.Visible)
                throw new InvalidOperationException("Map shortcuts must not act behind the Pause Menu.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (gameMenuPanel.Visible)
                throw new InvalidOperationException("Escape must close the Pause Menu.");
            quitGameButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (quitGameConfirmation.DialogText.Contains("saved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Quit Game is offered only on the Main Menu, so it must not talk about saving progress.");
            if (!quitGameConfirmation.Visible)
                throw new InvalidOperationException("Quit Game must ask for confirmation before exiting.");
            quitGameConfirmation.Hide();
            GD.Print("UI checks passed: startup Main Menu and settings, compact in-world pause menu and read-only Mod Library, confirmed quit, World Info Towns page, resource hover, square tile hover and agent priority, bounded marker hitboxes at zoom, building footprints, camera-bounded large terrain and regional weather, zoom, middle-drag, WASD, overview navigation, Event Log jumps without pop-ups, keyboard shortcuts and the F1 controls list, private thoughts, memories, deceased inspection and family tree.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(1);
        }
    }

}
