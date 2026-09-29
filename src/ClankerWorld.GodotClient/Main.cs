using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The deliberately practical Phase 2 owner client. It renders only signed,
/// server-issued world projections; all control buttons submit a one-use
/// device-key proof to the server and never mutate a local simulation copy.
/// </summary>
public partial class Main : Control
{
    private const int DefaultTileSize = 96;
    private const int TileGap = 0;
    private const int RefreshSeconds = 1;
    private const long StatusToastMilliseconds = 6_000;
    private const int SettingCaptionWidth = 135;
    private const string UiScaleBaseFontSizeMetaPrefix = "clanker_ui_scale_base_font_size_";
    private static readonly string[] UiScaleFontSizeThemeItems = ["font_size"];
    private static readonly string[] UiScaleRichTextFontSizeThemeItems =
    [
        "normal_font_size",
        "bold_font_size",
        "italics_font_size",
        "bold_italics_font_size",
        "mono_font_size",
    ];

    private readonly System.Net.Http.HttpClient httpClient = new();
    private readonly OwnerWorldApi ownerApi;
    private readonly OwnerWorldObservationSession observationSession = new();
    private readonly OwnerDeviceRegistrationStore registrationStore = new();
    private readonly OwnerPendingSubmissionStore pendingSubmissionStore = new(
        ProjectSettings.GlobalizePath("user://owner-pending-submission.json"));
    private readonly GameDisplayPreferencesStore displayPreferencesStore = new(
        ProjectSettings.GlobalizePath("user://game-display-preferences.json"));
    private readonly Dictionary<long, OwnerWorldEvent> knownEvents = [];
    private readonly Dictionary<string, AgentMarker> inhabitantVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> mapObjectVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> inhabitantCanonicalXs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> mapObjectCanonicalXs = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> uiScaleWatchedNodes = [];
    private readonly List<Label> settingCaptionLabels = [];

    private readonly Label statusLabel = new();
    private readonly PanelContainer statusToast = new();
    private readonly PanelContainer connectionPanel = new();
    private readonly Button settingsButton = new();
    private readonly Button modLibraryButton = new();
    private readonly Button gameSettingsCategoryButton = new();
    private readonly Button worldSettingsCategoryButton = new();
    private readonly VBoxContainer gameSettingsContent = new();
    private readonly VBoxContainer worldSettingsContent = new();
    private readonly ScrollContainer settingsScroll = new();
    private readonly LineEdit worldUrlInput = new();
    private readonly Button connectButton = new();
    private readonly Button pairAgainButton = new();
    private readonly PanelContainer cognitionSettingsPanel = new();
    private readonly OptionButton cognitionRoleChoice = new();
    private readonly OptionButton cognitionTargetChoice = new();
    private readonly OptionButton cognitionProviderChoice = new();
    private readonly OptionButton cognitionCredentialChoice = new();
    private readonly LineEdit cognitionModelInput = new();
    private readonly LineEdit cognitionApiKeyInput = new();
    private readonly LineEdit cognitionCredentialLabelInput = new();
    private readonly Label cognitionConfigurationStatus = new();
    private readonly Label cognitionCredentialHint = new();
    private readonly Label usageMeterStatus = new();
    private readonly LineEdit usageAttemptLimitInput = new();
    private readonly Button applyUsageLimitButton = new();
    private readonly Button grantUsageCallsButton = new();
    private readonly Button refreshUsageButton = new();
    private readonly Button saveCognitionProviderButton = new();
    private readonly Button forgetCognitionCredentialButton = new();
    private readonly Button deleteCognitionCredentialSlotButton = new();
    private readonly Button refreshCognitionProviderButton = new();
    private readonly PanelContainer pairingPanel = new();
    private readonly Label pairingInstructionLabel = new();
    private readonly Label pairingCodeLabel = new();
    private readonly Label pairingIdLabel = new();
    private readonly Label pairingExpiryLabel = new();
    private readonly Button pairButton = new();
    private readonly Button forgetRegistrationButton = new();

    private readonly HBoxContainer topBar = new();
    private readonly Button mapButton = new();
    private readonly Button worldInfoButton = new();
    private readonly Label clockLabel = new();
    private readonly Label climateLabel = new();
    private readonly Button inhabitantsButton = new();
    private readonly Button eventsButton = new();
    private readonly Button settlementButton = new();
    private readonly Button menuButton = new();
    private readonly ColorRect topBarShade = new();
    private readonly WorldTerrainLayer terrainLayer = new();
    private readonly WeatherLayer weatherLayer = new();
    private WorldTerrainMap? terrainMap;
    private string? terrainWorldId;
    private string? terrainManifestDigest;
    private string? terrainLayersDigest;
    private readonly Control mapCanvas = new();
    private readonly Control mapStage = new();
    private readonly PanelContainer worldOverviewPanel = new();
    private readonly WorldOverview worldOverview = new();
    private readonly Control objectLayer = new();
    private readonly Control entityLayer = new();
    private readonly Label rosterSummaryLabel = new();
    private readonly PanelContainer selectedInhabitantCard = new();
    private readonly VBoxContainer selectedAgentOverview = new();
    private readonly ScrollContainer selectedAgentOverviewScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly ScrollContainer selectedAgentModelScroll = new();
    private readonly VBoxContainer selectedAgentModelContent = new();
    private readonly Button modelSettingsButton = new();
    private readonly Label selectedActorNameLabel = new();
    private readonly LineEdit renameAgentInput = new();
    private readonly Button renameAgentButton = new();
    private readonly Label selectedActorSummaryLabel = new();
    private readonly Label selectedActorConditionLabel = new();
    private readonly Button clearSelectionButton = new();
    private readonly Button findAgentButton = new();
    private readonly Button familyTreeButton = new();
    private readonly PanelContainer familyTreePanel = new();
    private readonly FamilyTreeView familyTreeView = new();
    private readonly Label familyTreeStatus = new();
    private readonly ItemList inhabitantList = new();
    private readonly RichTextLabel inhabitantDetails = new();
    private readonly RichTextLabel inhabitantSocialDetails = new();
    private readonly RichTextLabel privateThoughtHistory = new();
    private readonly Button memoriesButton = new();
    private readonly PanelContainer memoriesPanel = new();
    private readonly RichTextLabel memoryHistory = new();
    private readonly RichTextLabel worldDetails = new();
    private readonly RichTextLabel worldInfoText = new();
    private readonly RichTextLabel eventLog = new();
    private readonly PanelContainer rosterPanel = new();
    private readonly PanelContainer eventsPanel = new();
    private readonly PanelContainer settlementPanel = new();
    private readonly PanelContainer worldInfoPanel = new();
    private readonly PanelContainer selectedTilePanel = new();
    private readonly RichTextLabel selectedTileText = new();
    private Vector2I? selectedTile;
    private readonly PanelContainer gameMenuPanel = new();
    private readonly PanelContainer settingsPanel = new();
    private readonly ColorRect menuShade = new();
    private readonly Label menuHeadingLabel = new();
    private readonly Button menuCloseButton = new();
    private readonly Button menuResumeButton = new();
    private readonly Button quitGameButton = new();
    private readonly ConfirmationDialog quitGameConfirmation = new();
    private readonly CheckBox fullscreenToggle = new();
    private readonly OptionButton windowSizeChoice = new();
    private readonly OptionButton renderResolutionChoice = new();
    private readonly OptionButton uiScaleChoice = new();
    private static readonly Vector2I[] DisplaySizePresets =
    [
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080),
    ];
    private readonly List<Vector2I> renderSizeOptions = [];
    private readonly OptionButton clockFormatChoice = new();
    private readonly OptionButton dateFormatChoice = new();
    private readonly OptionButton lifePaceChoice = new();
    private readonly CheckBox jevAssistanceToggle = new();
    private readonly Button applyLifePaceButton = new();
    private int? lastObservedLifePace;
    private string? lastLifePaceWorldId;

    private readonly Button pauseButton = new();
    private readonly OptionButton instructionKind = new();
    private readonly LineEdit instructionText = new();
    private readonly Button submitInstructionButton = new();
    private readonly Label pendingSubmissionLabel = new();
    private readonly Button retryPendingSubmissionButton = new();
    private readonly Button forgetPendingSubmissionButton = new();
    private readonly OptionButton authoringKind = new();
    private readonly LineEdit authoringId = new();
    private readonly LineEdit authoringValue = new();
    private readonly LineEdit authoringSecondaryValue = new();
    private readonly SpinBox authoringX = new();
    private readonly SpinBox authoringY = new();
    private readonly CheckBox authoringRenewable = new();
    private readonly Button submitAuthoringButton = new();
    private readonly Label authoringHintLabel = new();
    private readonly LineEdit pairingApprovalId = new();
    private readonly LineEdit pairingApprovalCode = new();
    private readonly Button approvePairingButton = new();
    private readonly Button refreshDevicesButton = new();
    private readonly ItemList pairedDeviceList = new();
    private readonly LineEdit revokeDeviceId = new();
    private readonly Button revokeDeviceButton = new();
    private readonly Button developerToggleButton = new();
    private readonly ScrollContainer developerScroll = new();
    private readonly VBoxContainer developerBody = new();

    private OwnerDeviceKey? deviceKey;
    private OwnerDeviceRegistration? registration;
    private OwnerPairingStart? pendingPairing;
    private Uri? pendingPairingOrigin;
    private OwnerDevice[] pairedDevices = [];
    private OwnerProviderConfigurationStatus? providerConfiguration;
    private OwnerUsageStatus? usageStatus;
    private string? usagePauseWorldId;
    private bool wasObservedPaused;
    private OwnerPendingSubmission? pendingSubmission;
    private string? selectedInhabitantId;
    private string? renamingAgentId;
    private bool isRefreshing;
    private int successfulRefreshCount;
    private bool isPairingOperation;
    private bool isOwnerAction;
    private bool registeredEndpointInvalid;
    private bool menuPausedWorld;
    private bool menuPauseConfirmed;
    private OwnerWorldSnapshot? renderedMapSnapshot;
    private int currentTileSize = DefaultTileSize;
    private float cameraZoom = 1;
    private float minimumCameraZoom = 0.65f;
    private float maximumCameraZoom = 4f;
    private Vector2 cameraCenterTiles;
    private string? cameraWorldId;
    private bool draggingMap;
    private GameDisplayPreferences displayPreferences = new();
    private OwnerWorldCalendarPace? observedCalendarPace;
    private bool uiScaleTreeReady;
    private string? renderedEventLog;
    private StatusToastKind statusToastKind;
    private long statusToastShownAtMsec;

    public Main()
    {
        ownerApi = new OwnerWorldApi(httpClient);
    }

    public override void _Ready()
    {
        displayPreferences = displayPreferencesStore.Load();
        ApplySavedDisplaySettings();
        if (OS.GetCmdlineUserArgs().Contains("--ui-smoke-test", StringComparer.Ordinal))
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        BuildLayout();
        uiScaleTreeReady = true;
        WatchUiScaleTree(this);
        ApplyUiScale(displayPreferences.UiScalePercent);
        GetWindow().SizeChanged += RefreshAutomaticRenderResolution;
        ShowMainMenu();
        if (OS.GetCmdlineUserArgs().Contains("--ui-smoke-test", StringComparer.Ordinal))
        {
            _ = VerifyMenuLayoutAsync();
            return;
        }
        _ = TryGetCommandLineWorldUrl(out var commandLineUrl);
        worldUrlInput.Text = commandLineUrl ?? ConfiguredWorldUrl();
        _ = InitializeAsync();

        var timer = new Godot.Timer
        {
            WaitTime = RefreshSeconds,
            Autostart = true,
        };
        timer.Timeout += () => _ = PulseAsync();
        AddChild(timer);
    }

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

    private async Task VerifyMenuLayoutAsync()
    {
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1024, 768) })
            {
                GetWindow().Size = size;
                for (var frame = 0; frame < 3; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!mainMenuOverlay.GetGlobalRect().Encloses(mainMenuCard.GetGlobalRect()) ||
                    mainMenuOverlay.GetGlobalRect().GetCenter().DistanceTo(mainMenuCard.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"Main Menu escaped its centered bounds at {size}.");
                manualSaveOverlay.Show();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!manualSaveOverlay.GetGlobalRect().Encloses(manualSaveCard.GetGlobalRect()) ||
                    manualSaveOverlay.GetGlobalRect().GetCenter().DistanceTo(manualSaveCard.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"Save/load panel escaped its centered bounds at {size}.");
                manualSaveOverlay.Hide();
                worldMenuHeading.Text = "New World";
                worldMenuStatus.Text = "Pick a seed and size. After creating the world, choose where your Town goes and add four founders, then start time.";
                worldPreviewStatus.Text = "Map preview · you will choose where your Town goes after creating the world.";
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
                menuCloseButton.Text != "<")
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
                manualSaveOverwriteConfirmation.OkButtonText != "Overwrite" || quitToMenuConfirmation.Theme is null)
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
                            settlementPanel.Show();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            if (!mapCanvas.GetGlobalRect().Encloses(settlementPanel.GetGlobalRect()))
                            {
                                throw new InvalidOperationException($"Settlement panel escaped the world viewport: window={size}");
                            }
                            settlementPanel.Hide();
                            worldInfoPanel.Show();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            if (!mapCanvas.GetGlobalRect().Encloses(worldInfoPanel.GetGlobalRect()))
                                throw new InvalidOperationException($"World Info escaped the world viewport: window={size}");
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
                !worldInfoText.Text.Contains("First Town · Founding · 4 residents", StringComparison.Ordinal))
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
            foreach (var panel in new PanelContainer[] { rosterPanel, eventsPanel, settlementPanel, worldInfoPanel, filtersPanel, worldOverviewPanel })
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
                !undoFounderButton.Visible ||
                !GetViewport().GetVisibleRect().Encloses(undoFounderButton.GetGlobalRect()) ||
                !mapCanvas.GetGlobalRect().Encloses(founderSetupPanel.GetGlobalRect()))
                throw new InvalidOperationException("Founder setup must show progress and keep Start World gated inside the world view.");
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
            if (!worldInfoText.Text.Contains("Town borders are hidden", StringComparison.Ordinal))
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
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.S, Pressed = true });
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
            if (climateLabel.Text != "Spring · Rain" ||
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
            if (!pausedBadge.Visible || !mapCanvas.GetGlobalRect().Encloses(pausedBadge.GetGlobalRect()))
                throw new InvalidOperationException($"A paused, started world must show a paused badge inside the world view: visible={pausedBadge.Visible} badge={pausedBadge.GetGlobalRect()}.");
            RenderWorldHud(startedMap with { Authoring = startedMap.Authoring! with { IsPaused = false } });
            if (pausedBadge.Visible)
                throw new InvalidOperationException("The paused badge must disappear while time runs.");
            RenderWorldHud(largeMap);
            UpdateTileHover(mapCanvas.Size / 2);
            var hoveredCenter = TileAtCanvas(mapCanvas.Size / 2, largeMap);
            if (!hoverReadout.Visible || !hoverReadoutLabel.Text.EndsWith($"{hoveredCenter.X}, {hoveredCenter.Y}", StringComparison.Ordinal) ||
                hoverReadout.MouseFilter != Control.MouseFilterEnum.Ignore || pausedBadge.MouseFilter != Control.MouseFilterEnum.Ignore)
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
            if (climateLabel.Text != "Spring · Snow" ||
                !worldInfoText.Text.Contains("here: Spring · Snow", StringComparison.Ordinal) ||
                !worldInfoText.Text.Contains("Soil moisture here: 12%", StringComparison.Ordinal))
                throw new InvalidOperationException($"Panning must update HUD and World Info to local weather: camera={cameraCenterTiles}, HUD={climateLabel.Text}, info={worldInfoText.Text}.");
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

            var repeatedKeyStart = cameraCenterTiles.X;
            var previousKeyCenter = repeatedKeyStart;
            for (var step = 0; step < 512; step++)
            {
                _UnhandledKeyInput(new InputEventKey { Keycode = Key.Right, Pressed = true });
                if (Math.Abs(PositiveMod(cameraCenterTiles.X - previousKeyCenter, 256) - 1.5f) > 0.01f)
                    throw new InvalidOperationException("Repeated right-key input must keep moving through multiple wrapped laps.");
                previousKeyCenter = cameraCenterTiles.X;
            }
            for (var step = 0; step < 512; step++)
            {
                _UnhandledKeyInput(new InputEventKey { Keycode = Key.Left, Pressed = true });
                if (Math.Abs(PositiveMod(previousKeyCenter - cameraCenterTiles.X, 256) - 1.5f) > 0.01f)
                    throw new InvalidOperationException("Repeated left-key input must keep moving through multiple wrapped laps.");
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
            if (currentTileSize != 8 || worldOverview.VisibleTiles.Size.X < oldVisibleWidth * 1.4f ||
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
            if (topBar.GetChildren().OfType<Button>().Any(button => button.FocusMode == Control.FocusModeEnum.None))
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
            if (!quitGameConfirmation.Visible)
                throw new InvalidOperationException("Quit Game must ask for confirmation before exiting.");
            quitGameConfirmation.Hide();
            GD.Print("UI checks passed: startup Main Menu and settings, compact in-world pause menu and read-only Mod Library, confirmed quit, settlement panel, resource hover, square tile hover and agent priority, bounded marker hitboxes at zoom, building footprints, camera-bounded large terrain and regional weather, zoom, middle-drag, WASD, overview navigation, Event Log jumps without pop-ups, keyboard shortcuts and the F1 controls list, private thoughts, memories, deceased inspection and family tree.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(1);
        }
    }

    public override void _ExitTree()
    {
        deviceKey?.Dispose();
        httpClient.Dispose();
        base._ExitTree();
    }

    private async Task InitializeAsync()
    {
        try
        {
            deviceKey = OwnerDeviceKey.OpenOrCreate();
            registration = registrationStore.TryLoad(deviceKey.PublicKeyFingerprint);
            if (registration is null)
            {
                RefreshMainMenuAvailability();
                return;
            }

            // Once paired, this device is pinned to the server origin that
            // issued the registration. A command-line URL is useful only for
            // a first pairing; it must never silently retarget an owner key.
            if (!WorldServerOrigin.TryResolve(registration.WorldUrl, out var storedWorldUri))
            {
                registeredEndpointInvalid = true;
                pairingPanel.Show();
                OpenMenuForSetup();
                pairingInstructionLabel.Text = "This saved device registration has no valid pinned server endpoint. Forget the local registration, then pair this Windows key again at the intended HTTPS host.";
                SetStatus("saved owner endpoint is invalid · re-pair required", good: false);
                RefreshControlAvailability();
                return;
            }

            worldUrlInput.Text = storedWorldUri.AbsoluteUri;
            LoadPendingSubmission();

            pairingPanel.Hide();
            settingsPanel.Hide();
            CloseGameMenu();
            RefreshMainMenuAvailability();
            SetStatus("This device is paired. Choose Continue to enter your world.", good: true);
        }
        catch (Exception exception)
        {
            pairingPanel.Show();
            OpenMenuForSetup();
            SetStatus($"owner key unavailable · {FriendlyFailure(exception)}", good: false);
            pairingInstructionLabel.Text = "This client needs the Windows current-user key store. It does not create a portable private-key file.";
            pairButton.Disabled = true;
        }
    }

    private async Task PulseAsync()
    {
        ExpireStatusToast(refreshSucceeded: false);
        if (pendingPairing is not null)
        {
            await PollPairingAsync();
            return;
        }

        if (isInWorld && registration is not null && !registeredEndpointInvalid)
        {
            await RefreshAsync();
        }
    }

    private async Task StartPairingAsync()
    {
        if (isPairingOperation || pendingPairing is not null || deviceKey is null)
        {
            return;
        }

        isPairingOperation = true;
        RefreshControlAvailability();
        try
        {
            var origin = ResolveWorldUri();
            pendingPairing = await ownerApi.StartPairingAsync(
                origin,
                deviceKey,
                CancellationToken.None);
            pendingPairingOrigin = origin;
            pairingPanel.Show();
            pairingInstructionLabel.Text = "Give the host the pairing ID and short comparison code below. The host approves it on its private loopback listener; this code is not a password.";
            pairingCodeLabel.Text = pendingPairing.PairingCode;
            pairingIdLabel.Text = pendingPairing.PairingId;
            pairingExpiryLabel.Text = $"expires {pendingPairing.ExpiresAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
            pairButton.Text = "Start fresh pairing";
            SetStatus("Waiting for the host to approve this device", good: true);
            _ = RevealPairingPanelAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"could not start device pairing · {FriendlyFailure(exception)}", good: false);
        }
        finally
        {
            isPairingOperation = false;
            RefreshControlAvailability();
        }
    }

    // The comparison code sits below the display settings; scroll it into
    // view once the settings page has laid out the newly shown panel.
    private async Task RevealPairingPanelAsync()
    {
        for (var frame = 0; frame < 2; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (pairingPanel.IsVisibleInTree()) settingsScroll.EnsureControlVisible(pairingPanel);
    }

    private async Task PollPairingAsync()
    {
        if (isPairingOperation || pendingPairing is null || deviceKey is null)
        {
            return;
        }

        isPairingOperation = true;
        try
        {
            var status = await ownerApi.GetPairingStatusAsync(
                ResolveWorldUri(),
                pendingPairing.PairingId,
                CancellationToken.None);
            if (!Equals(status.Authority, pendingPairing.Authority) ||
                !string.Equals(status.DeviceId, pendingPairing.DeviceId, StringComparison.Ordinal) ||
                !string.Equals(status.PublicKeyFingerprint, deviceKey.PublicKeyFingerprint, StringComparison.Ordinal))
            {
                pendingPairingOrigin = null;
                pendingPairing = null;
                SetStatus("pairing status does not match this device and server · start a fresh pairing", good: false);
                return;
            }

            switch (status.State)
            {
                case OwnerPairingState.Pending:
                    pairingInstructionLabel.Text = "Waiting for host approval. Give the host the pairing ID and comparison code exactly as shown.";
                    break;
                case OwnerPairingState.Approved:
                    pairingInstructionLabel.Text = "Host approval received. Proving possession of this Windows device key…";
                    await ActivatePendingPairingAsync();
                    break;
                case OwnerPairingState.Active:
                    // The process can be interrupted after a successful
                    // server activation but before its non-secret local
                    // registration is flushed. Recover only when the active
                    // record remains bound to this exact Windows key.
                    if (string.Equals(status.DeviceId, pendingPairing.DeviceId, StringComparison.Ordinal) &&
                        string.Equals(status.PublicKeyFingerprint, deviceKey.PublicKeyFingerprint, StringComparison.Ordinal) &&
                        Equals(status.Authority, pendingPairing.Authority))
                    {
                        registration = new OwnerDeviceRegistration(
                            status.Authority,
                            status.DeviceId,
                            deviceKey.PublicKeyFingerprint,
                            ResolveWorldUri().AbsoluteUri);
                        registrationStore.Save(registration);
                        LoadPendingSubmission();
                        pendingPairing = null;
                        pendingPairingOrigin = null;
                        pairingPanel.Hide();
                        settingsPanel.Hide();
                        CloseGameMenu();
                        SetStatus("recovered the active device registration · requesting signed owner observation", good: true);
                        await RefreshAsync();
                    }
                    else
                    {
                        pairingInstructionLabel.Text = "This pairing is active but is not bound to this device key. Revoke it at the host before attempting another pairing.";
                        SetStatus("active pairing does not match this device key", good: false);
                    }

                    break;
                case OwnerPairingState.Expired:
                    pairingInstructionLabel.Text = "The comparison code expired. Start a fresh pairing to get a new short code.";
                    pendingPairing = null;
                    pendingPairingOrigin = null;
                    SetStatus("pairing expired", good: false);
                    break;
                default:
                    SetStatus("unknown pairing state returned by server", good: false);
                    break;
            }
        }
        catch (Exception exception)
        {
            SetStatus($"pairing status unavailable · {FriendlyFailure(exception)}", good: false);
        }
        finally
        {
            isPairingOperation = false;
            RefreshControlAvailability();
        }
    }

    private async Task ActivatePendingPairingAsync()
    {
        if (pendingPairing is null || deviceKey is null)
        {
            return;
        }

        var pairing = pendingPairing;
        try
        {
            var device = await ownerApi.ActivatePairingAsync(
                ResolveWorldUri(),
                pairing,
                deviceKey,
                CancellationToken.None);
            registration = new OwnerDeviceRegistration(
                pairing.Authority,
                device.DeviceId,
                deviceKey.PublicKeyFingerprint,
                ResolveWorldUri().AbsoluteUri);
            registrationStore.Save(registration);
            LoadPendingSubmission();
            pendingPairing = null;
            pendingPairingOrigin = null;
            pairingPanel.Hide();
            settingsPanel.Hide();
            CloseGameMenu();
            ShowMainMenu();
            SetStatus("Device paired. Choose Continue to enter your world.", good: true);
        }
        catch (Exception exception)
        {
            SetStatus($"host approved pairing, but key activation failed · {FriendlyFailure(exception)}", good: false);
        }
    }

    private void ForgetLocalRegistration()
    {
        registrationStore.Forget();
        registration = null;
        registeredEndpointInvalid = false;
        pendingPairing = null;
        pendingPairingOrigin = null;
        pairedDevices = [];
        providerConfiguration = null;
        pairedDeviceList.Clear();
        pendingSubmission = null;
        _ = pendingSubmissionStore.TryForget();
        RenderPendingSubmission();
        knownEvents.Clear();
        pairingPanel.Show();
        connectionPanel.Show();
        OpenMenuForSetup();
        pairingCodeLabel.Text = "—";
        pairingIdLabel.Text = "—";
        pairingExpiryLabel.Text = string.Empty;
        pairingInstructionLabel.Text = "Local public registration forgotten. The Windows private key remains in the current-user key store; start a new pairing only if the host allows that key to be paired.";
        SetStatus("local registration forgotten", good: false);
        RefreshControlAvailability();
    }

    private async Task RefreshAsync()
    {
        if (isRefreshing || registeredEndpointInvalid || registration is null || deviceKey is null)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            var requestedCursor = observationSession.EventCursor;
            var cachedTerrain = observationSession.Current is { } held &&
                held.Handshake.ServerCapabilities.Contains("owner-terrain-delta.v1", StringComparer.Ordinal) &&
                held.Baseline.Snapshot.PackedTerrain is not null &&
                (held.Baseline.Snapshot.MapLayersDigest is null ||
                 held.Baseline.Snapshot.PackedMapLayers is not null)
                ? held.Baseline.Snapshot : null;
            var cachedMapLayersDigest = cachedTerrain is not null &&
                observationSession.Current!.Handshake.ServerCapabilities.Contains(
                    "owner-map-layer-delta.v1", StringComparer.Ordinal)
                ? cachedTerrain.MapLayersDigest : null;
            var reconnect = await ownerApi.ReconnectAsync(
                ResolveWorldUri(),
                registration.Authority,
                registration.DeviceId,
                requestedCursor,
                cachedTerrain?.WorldId,
                cachedTerrain?.MapManifestDigest,
                cachedMapLayersDigest,
                deviceKey,
                CancellationToken.None);
            if (!observationSession.TryAccept(reconnect, requestedCursor, out var failure))
            {
                ShowHeldState(failure);
                return;
            }

            if (reconnect.Baseline.Events.ResetRequired)
            {
                knownEvents.Clear();
            }
            Render(observationSession.Current!.Baseline.Snapshot, reconnect.Baseline.Events.Events);
            successfulRefreshCount++;
            if (!isOwnerAction)
            {
                if (usageStatus?.LimitReached == true && reconnect.Baseline.Snapshot.Authoring?.IsPaused == true)
                {
                    SetStatus("Paid-call limit reached. The world is paused; open World Settings to allow more calls.",
                        good: false, StatusToastKind.UsageLimit);
                }
                else
                {
                    ExpireStatusToast(refreshSucceeded: true);
                }
            }
        }
        catch (Exception exception)
        {
            ShowHeldState(FriendlyFailure(exception));
        }
        finally
        {
            isRefreshing = false;
            RefreshControlAvailability();
        }
    }

    private async Task<bool> SetPausedAsync(bool paused)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return false;
        }

        var accepted = false;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await ownerApi.SetPausedAsync(
                ResolveWorldUri(), authority, deviceId, paused, signer, CancellationToken.None);
            accepted = true;
            return receipt.Changed
                ? paused ? "World paused" : "World resumed"
                : $"The world was already {(paused ? "paused" : "running")}";
        });
        return accepted;
    }

    private async Task SubmitInstructionAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            observationSession.Current is not { } current)
        {
            SetStatus("Wait for the world to load before giving an instruction.", good: false);
            return;
        }

        var selected = current.Baseline.Snapshot.Inhabitants
            .FirstOrDefault(inhabitant => string.Equals(inhabitant.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (selected is null || selected.IsDraft)
        {
            SetStatus("Pick an agent first.", good: false);
            return;
        }
        if (selected.DecisionFactors.Any(factor => factor.Key == "age-band" && factor.Detail == "infant"))
        {
            SetStatus("Infants need an adult to look after them. They can't take work instructions.", good: false);
            return;
        }

        var text = instructionText.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Write something to say first.", good: false);
            return;
        }

        var action = new OwnerInstructionAction(
            $"instruction_{OwnerPairingProtocol.CreateRequestId()}",
            selected.Id,
            instructionKind.GetSelectedId() == 1 ? "must_do" : "suggestive",
            text);
        if (!TryBeginPendingInstruction(action, out var pending))
        {
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await ownerApi.SubmitInstructionAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
            completed = true;
            instructionText.Text = string.Empty;
            return $"queued {action.Kind} instruction {receipt.InstructionId}";
        });
        if (completed)
        {
            CompletePendingSubmission(pending);
        }
    }

    private async Task SubmitAuthoringAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            observationSession.Current?.Baseline.Snapshot.Authoring is not { IsPaused: true })
        {
            SetStatus("paused authoring is disabled until the server reports an atomic paused boundary", good: false);
            return;
        }

        var operation = new OwnerAuthoringOperationAction(
            SelectedAuthoringKind(),
            EmptyToNull(authoringId.Text),
            EmptyToNull(authoringValue.Text),
            EmptyToNull(authoringSecondaryValue.Text),
            checked((int)authoringX.Value),
            checked((int)authoringY.Value),
            authoringRenewable.ButtonPressed);
        var batch = new OwnerAuthoringBatchAction(
            $"authoring_{OwnerPairingProtocol.CreateRequestId()}",
            [operation]);
        if (!TryBeginPendingAuthoring(batch, out var pending))
        {
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await ownerApi.SubmitAuthoringAsync(
                ResolveWorldUri(), authority, deviceId, batch, signer, CancellationToken.None);
            completed = true;
            return receipt.Applied
                ? $"applied {operation.Kind} at revision {receipt.Revision}"
                : $"authoring rejected · {receipt.Failure ?? "unknown validation failure"}";
        });
        if (completed)
        {
            CompletePendingSubmission(pending);
        }
    }

    private async Task ApprovePairingAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return;
        }

        var pairingId = pairingApprovalId.Text.Trim();
        var pairingCode = pairingApprovalCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(pairingId) || string.IsNullOrWhiteSpace(pairingCode))
        {
            SetStatus("enter the pending pairing ID and comparison code", good: false);
            return;
        }

        var action = new OwnerPairingApprovalAction(pairingId, pairingCode);
        await RunOwnerActionAsync(async () =>
        {
            var approval = await ownerApi.ApprovePairingAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
            pairingApprovalCode.Text = string.Empty;
            return $"approved pending device {approval.DeviceId}; it must still activate its own Windows key";
        });
    }

    private async Task RevokeDeviceAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return;
        }

        var targetDeviceId = revokeDeviceId.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDeviceId))
        {
            SetStatus("enter a paired device ID to revoke it", good: false);
            return;
        }

        if (string.Equals(targetDeviceId, deviceId, StringComparison.Ordinal))
        {
            SetStatus("this client will not revoke its own active key; use host-local recovery if that is intentional", good: false);
            return;
        }

        var action = new OwnerDeviceManagementAction(targetDeviceId);
        await RunOwnerActionAsync(async () =>
        {
            var revoked = await ownerApi.RevokeDeviceAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
            revokeDeviceId.Text = string.Empty;
            return $"revoked device {revoked.DeviceId}";
        });
        await RefreshDeviceRegistryAsync();
    }

    private async Task RefreshDeviceRegistryAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return;
        }

        await RunOwnerActionAsync(async () =>
        {
            pairedDevices = await ownerApi.ListDevicesAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            RenderPairedDevices();
            return $"loaded {pairedDevices.Length} paired device record(s)";
        });
    }

    private void RenderPairedDevices()
    {
        pairedDeviceList.Clear();
        foreach (var device in pairedDevices
            .OrderBy(device => device.State == OwnerDeviceState.Active ? 0 : 1)
            .ThenBy(device => device.DeviceId, StringComparer.Ordinal))
        {
            var self = string.Equals(device.DeviceId, registration?.DeviceId, StringComparison.Ordinal)
                ? " · this Windows device"
                : string.Empty;
            pairedDeviceList.AddItem($"{device.State.ToString().ToLowerInvariant()} · {device.DeviceId}{self}");
            pairedDeviceList.SetItemMetadata(pairedDeviceList.ItemCount - 1, device.DeviceId);
        }
    }

    private async Task SaveLifePaceAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var rate = lifePaceChoice.GetSelectedId();
        await RunOwnerActionAsync(async () =>
        {
            _ = await ownerApi.SetLifePaceAsync(ResolveWorldUri(), authority, deviceId, rate, signer, CancellationToken.None);
            return "life pace saved; current ages preserved, future aging changed";
        });
    }

    private async Task SaveJevAssistanceAsync(bool enabled)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            _ = await ownerApi.SetJevAssistanceAsync(ResolveWorldUri(), authority, deviceId, enabled, signer, CancellationToken.None);
            return enabled ? "Jev assistance enabled for this world" : "Jev assistance disabled for this world";
        });
    }

    private async Task RefreshProviderConfigurationAsync()
    {
        cognitionApiKeyInput.Text = string.Empty;
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            cognitionConfigurationStatus.Text = "Connect this device before setting up agent models.";
            RenderProviderConfiguration();
            return;
        }

        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            PopulateCognitionTargets();
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            return "loaded inhabitant cognition settings";
        });
    }

    private async Task RefreshUsageAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            usageMeterStatus.Text = "Connect this device to see model-call usage.";
            return;
        }
        await RunOwnerActionAsync(async () =>
        {
            usageStatus = await ownerApi.GetUsageStatusAsync(ResolveWorldUri(), authority, deviceId,
                signer, CancellationToken.None);
            RenderUsageStatus();
            return "loaded paid-call usage";
        });
    }

    private async Task ObserveUsagePauseAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        try
        {
            usageStatus = await ownerApi.GetUsageStatusAsync(ResolveWorldUri(), authority, deviceId,
                signer, CancellationToken.None);
            RenderUsageStatus();
            if (usageStatus.LimitReached)
                SetStatus("Paid-call limit reached. The world is paused; open World Settings to allow more calls.", good: false);
        }
        catch (Exception)
        {
            // A pause is authoritative even when the optional meter read is unavailable.
        }
    }

    private void RenderUsageStatus()
    {
        if (usageStatus is null)
        {
            usageMeterStatus.Text = "Loading model calls…";
            return;
        }
        if (!usageAttemptLimitInput.HasFocus())
            usageAttemptLimitInput.Text = usageStatus.AttemptLimit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var rows = usageStatus.Rows.OrderByDescending(row => row.Attempts)
            .Select(row => $"{row.Provider} / {row.Model}: {row.Attempts} calls");
        usageMeterStatus.Text = usageStatus.AttemptLimit is { } limit
            ? $"{usageStatus.Attempts} of {limit} model calls used on this installation."
            : $"{usageStatus.Attempts} model calls used on this installation. No limit set.";
        if (usageStatus.LimitReached)
            usageMeterStatus.Text += " Time is paused. Raise the limit to allow more calls, then resume.";
        if (usageStatus.Rows.Count > 0)
            usageMeterStatus.Text += "\n" + string.Join("\n", rows);
        usageMeterStatus.TooltipText = $"Calls started: {usageStatus.Attempts}; completed: {usageStatus.Completed}; " +
            $"failed: {usageStatus.Failed}; interrupted: {usageStatus.Abandoned}. " +
            $"Known input/output tokens: {usageStatus.InputTokens}/{usageStatus.OutputTokens}. " +
            "Counts since this installation began; each retry counts as another call.";
        grantUsageCallsButton.Visible = usageStatus.LimitReached;
        RefreshControlAvailability();
    }

    private async Task ConfigureUsageAsync(bool grant)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        long? cap = null;
        if (!grant && !string.IsNullOrWhiteSpace(usageAttemptLimitInput.Text))
        {
            if (!long.TryParse(usageAttemptLimitInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 1 or > 1_000_000)
            {
                SetStatus("Enter a number of calls from 1 to 1,000,000, or leave it blank for no cap.", good: false);
                return;
            }
            cap = parsed;
        }
        var action = grant ? new OwnerUsageLimitAction(null, AdditionalCalls: 100) :
            new OwnerUsageLimitAction(cap);
        await RunOwnerActionAsync(async () =>
        {
            usageStatus = await ownerApi.ConfigureUsageLimitAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None);
            RenderUsageStatus();
            if (grant)
                return "Allowed 100 more paid calls. Resume the world when ready";
            return cap is null ? "paid-call limit turned off" : $"paid-call limit set to {cap} attempts";
        });
    }

    private async Task SaveProviderConfigurationAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before setting up agent models.", good: false);
            return;
        }

        var role = SelectedRoleId();
        var provider = SelectedProviderId();
        var target = SelectedCognitionTarget();
        var hostedAgent = target is not null && provider is ("openai" or "ollama-cloud");
        var credentialChoice = hostedAgent ? SelectedCredentialChoice() : null;
        var creatingSlot = credentialChoice == "new";
        if (creatingSlot && (string.IsNullOrWhiteSpace(cognitionCredentialLabelInput.Text) ||
            string.IsNullOrWhiteSpace(cognitionApiKeyInput.Text)))
        {
            SetStatus("Give the new key a name and paste the key.", good: false);
            return;
        }
        var action = new OwnerProviderConfigurationAction(
            role,
            provider,
            provider is "deterministic" or "inherit" ? null : EmptyToNull(cognitionModelInput.Text),
            provider is "deterministic" or "inherit" || hostedAgent && !creatingSlot
                ? null : EmptyToNull(cognitionApiKeyInput.Text),
            ForgetCredential: false,
            InhabitantId: target,
            CredentialSlotId: creatingSlot ? Guid.NewGuid().ToString("N") : credentialChoice is null or "default" ? null : credentialChoice,
            NewCredentialLabel: creatingSlot ? EmptyToNull(cognitionCredentialLabelInput.Text) : null);
        try
        {
            await RunOwnerActionAsync(async () =>
            {
                providerConfiguration = await ownerApi.ConfigureProviderAsync(
                    ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
                PopulateCredentialChoices();
                return $"{ProviderDisplayName(provider)} will handle {RoleDisplayName(role).ToLowerInvariant()} at the next cognition boundary";
            });
        }
        finally
        {
            cognitionApiKeyInput.Text = string.Empty;
            cognitionCredentialLabelInput.Text = string.Empty;
            RenderProviderConfiguration();
        }
    }

    private async Task ForgetProviderCredentialAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before changing keys.", good: false);
            return;
        }

        var role = SelectedRoleId();
        var provider = SelectedProviderId();
        if (provider == "deterministic")
        {
            SetStatus("Built-in rules use no API key", good: false);
            return;
        }

        var action = new OwnerProviderConfigurationAction(
            role,
            provider,
            EmptyToNull(cognitionModelInput.Text),
            null,
            ForgetCredential: true);
        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await ownerApi.ConfigureProviderAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
            return $"forgot the saved {ProviderDisplayName(provider)} key";
        });
        cognitionApiKeyInput.Text = string.Empty;
        RenderProviderConfiguration();
    }

    private async Task DeleteCredentialSlotAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before deleting a saved key.", good: false);
            return;
        }

        var slotId = SelectedCredentialChoice();
        var slot = providerConfiguration?.CredentialSlots?.FirstOrDefault(item => item.Id == slotId);
        if (slot is null)
        {
            SetStatus("Choose a saved key to delete.", good: false);
            return;
        }
        if (providerConfiguration?.Assignments?.Any(item => item.CredentialSlotId == slotId) == true)
        {
            SetStatus("An agent is still using this key. Give that agent another key first.", good: false);
            return;
        }

        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await ownerApi.DeleteCredentialSlotAsync(
                ResolveWorldUri(), authority, deviceId, slotId, signer, CancellationToken.None);
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            return $"deleted saved key {slot.Label}";
        });
    }

    private string SelectedRoleId() => SelectedCognitionTarget() is not null
        ? "personal" : cognitionRoleChoice.Selected == 1 ? "planning" : "routine";

    private string? SelectedCognitionTarget() => cognitionTargetChoice.Selected <= 0
        ? null : cognitionTargetChoice.GetItemMetadata(cognitionTargetChoice.Selected).AsString();

    private bool SelectedTargetWasBornHere() => observationSession.Current?.Baseline.Snapshot.Inhabitants
        .FirstOrDefault(item => item.Id == SelectedCognitionTarget())?.Relationships
        .Any(item => item.Type == "biological_parentage" && item.Direction == "child") == true;

    private void PopulateCognitionTargets()
    {
        var target = SelectedCognitionTarget();
        cognitionTargetChoice.Clear();
        cognitionTargetChoice.AddItem("World defaults");
        foreach (var inhabitant in observationSession.Current?.Baseline.Snapshot.Inhabitants ?? [])
        {
            cognitionTargetChoice.AddItem(inhabitant.DisplayName);
            var index = cognitionTargetChoice.ItemCount - 1;
            cognitionTargetChoice.SetItemMetadata(index, inhabitant.Id);
            if (inhabitant.Id == target)
            {
                cognitionTargetChoice.Select(index);
            }
        }
    }

    private InhabitantProviderAssignment? SelectedAssignment() => providerConfiguration?.Assignments?
        .FirstOrDefault(item => item.InhabitantId == SelectedCognitionTarget() &&
            item.Role == (SelectedRoleId() == "personal" ? "planning" : SelectedRoleId()));

    private string ActiveProviderForSelectedRole() => SelectedCognitionTarget() is not null
        ? SelectedAssignment()?.Provider ?? "inherit"
        : providerConfiguration is null
        ? "deterministic"
        : SelectedRoleId() == "planning"
            ? providerConfiguration.PlanningProvider
            : providerConfiguration.RoutineProvider;

    private void PopulateProviderChoices(string selectedProvider)
    {
        cognitionProviderChoice.Clear();
        if (SelectedCognitionTarget() is not null)
        {
            AddProviderChoice(SelectedTargetWasBornHere()
                ? "No personal model (safe local)" : "Use world default", "inherit");
        }
        AddProviderChoice("Built-in rules (no model)", "deterministic");
        if (SelectedRoleId() == "routine" && SelectedCognitionTarget() is null)
        {
            AddProviderChoice("Jev", "jev");
        }
        if (SelectedRoleId() == "planning" || SelectedCognitionTarget() is not null)
        {
            AddProviderChoice("OpenAI", "openai");
            AddProviderChoice("Ollama Cloud", "ollama-cloud");
        }
        if (SelectedCognitionTarget() is not null && selectedProvider == "jev")
            AddProviderChoice("Jev (legacy assignment)", "jev");

        SelectProviderChoice(selectedProvider);
    }

    private void SelectProviderChoice(string provider)
    {
        for (var index = 0; index < cognitionProviderChoice.ItemCount; index++)
        {
            if (cognitionProviderChoice.GetItemMetadata(index).AsString() == provider)
            {
                cognitionProviderChoice.Select(index);
                return;
            }
        }
        cognitionProviderChoice.Select(0);
    }

    private void AddProviderChoice(string label, string id)
    {
        cognitionProviderChoice.AddItem(label);
        cognitionProviderChoice.SetItemMetadata(cognitionProviderChoice.ItemCount - 1, id);
    }

    private string SelectedProviderId() => cognitionProviderChoice.Selected < 0
        ? "deterministic" : cognitionProviderChoice.GetItemMetadata(cognitionProviderChoice.Selected).AsString();

    private string SelectedCredentialChoice() => cognitionCredentialChoice.Selected < 0
        ? "default" : cognitionCredentialChoice.GetItemMetadata(cognitionCredentialChoice.Selected).AsString();

    private void PopulateCredentialChoices()
    {
        cognitionCredentialChoice.Clear();
        cognitionCredentialChoice.AddItem("Provider default key");
        cognitionCredentialChoice.SetItemMetadata(0, "default");
        var provider = SelectedProviderId();
        foreach (var slot in providerConfiguration?.CredentialSlots ?? [])
        {
            if (slot.Provider != provider) continue;
            cognitionCredentialChoice.AddItem(slot.Label);
            cognitionCredentialChoice.SetItemMetadata(cognitionCredentialChoice.ItemCount - 1, slot.Id);
        }
        cognitionCredentialChoice.AddItem("Add another API key…");
        cognitionCredentialChoice.SetItemMetadata(cognitionCredentialChoice.ItemCount - 1, "new");
        var assignedSlot = SelectedAssignment()?.Provider == provider ? SelectedAssignment()?.CredentialSlotId : null;
        for (var index = 0; index < cognitionCredentialChoice.ItemCount; index++)
        {
            if (cognitionCredentialChoice.GetItemMetadata(index).AsString() != assignedSlot) continue;
            cognitionCredentialChoice.Select(index);
            return;
        }
        cognitionCredentialChoice.Select(0);
    }

    private void RenderProviderConfiguration()
    {
        var provider = SelectedProviderId();
        var option = providerConfiguration?.Providers.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.Ordinal));
        var hosted = provider is not ("deterministic" or "inherit");
        cognitionRoleChoice.Visible = SelectedCognitionTarget() is null;
        var agentCredential = hosted && SelectedCognitionTarget() is not null && provider is ("openai" or "ollama-cloud");
        var newCredential = agentCredential && SelectedCredentialChoice() == "new";
        cognitionModelInput.Visible = hosted;
        cognitionCredentialChoice.Visible = agentCredential;
        cognitionCredentialLabelInput.Visible = newCredential;
        cognitionApiKeyInput.Visible = hosted && (!agentCredential || newCredential);
        cognitionCredentialHint.Visible = hosted;
        forgetCognitionCredentialButton.Visible = hosted && SelectedCognitionTarget() is null;
        deleteCognitionCredentialSlotButton.Visible = agentCredential &&
            SelectedCredentialChoice() is not ("default" or "new");
        if (hosted && option is not null && !cognitionModelInput.HasFocus())
        {
            cognitionModelInput.Text = SelectedAssignment() is { } assignment && assignment.Provider == provider
                ? assignment.Model ?? option.Model : option.Model;
        }

        cognitionApiKeyInput.PlaceholderText = newCredential ? "New API key" : option?.HasCredential == true
            ? "Leave blank to keep saved key"
            : "API key";
        cognitionCredentialHint.Text = newCredential
            ? "A new key is stored privately on the host and can be reused for other agents."
            : agentCredential && SelectedCredentialChoice() != "default"
            ? "Named key saved on host"
            : agentCredential && option?.HasCredential != true
            ? "No provider default key. Select Add another API key to give this agent one."
            : option?.HasCredential == true
            ? "Key saved on host"
            : "No saved key";
        cognitionConfigurationStatus.Text = providerConfiguration is null
            ? "Loading…"
            : SelectedTargetWasBornHere() && SelectedAssignment() is null
            ? "No personal model selected for this child. After infancy, safe local decisions continue until a model is assigned; world defaults are not used."
            : $"Routine: {ProviderDisplayName(providerConfiguration.RoutineProvider)} · Planning: {ProviderDisplayName(providerConfiguration.PlanningProvider)}";
        RefreshControlAvailability();
    }

    private static string RoleDisplayName(string role) => role == "planning"
        ? "Planning and work decisions"
        : role == "personal" ? "this agent's decisions"
        : "Routine survival decisions";

    private static string ProviderDisplayName(string provider) => provider switch
    {
        "jev" => "Jev",
        "openai" => "OpenAI",
        "ollama-cloud" => "Ollama Cloud",
        "inherit" => "World default",
        _ => "Built-in rules",
    };

    private static string DefaultProviderModel(string provider) => provider switch
    {
        "jev" => "jev-1.13.0",
        "openai" => "gpt-5-mini",
        "ollama-cloud" => "gpt-oss:120b-cloud",
        _ => string.Empty,
    };

    private bool TryBeginPendingInstruction(
        OwnerInstructionAction action,
        out OwnerPendingSubmission pending)
    {
        pending = null!;
        if (!TryCreatePendingSubmissionBinding(out var binding))
        {
            SetStatus("cannot retain an instruction until this paired device has a valid pinned server origin", good: false);
            return false;
        }

        pending = OwnerPendingSubmission.ForInstruction(binding, action);
        return TryRetainPendingSubmission(pending);
    }

    private bool TryBeginPendingAuthoring(
        OwnerAuthoringBatchAction action,
        out OwnerPendingSubmission pending)
    {
        pending = null!;
        if (!TryCreatePendingSubmissionBinding(out var binding))
        {
            SetStatus("cannot retain authoring until this paired device has a valid pinned server origin", good: false);
            return false;
        }

        pending = OwnerPendingSubmission.ForAuthoring(binding, action);
        return TryRetainPendingSubmission(pending);
    }

    private bool TryRetainPendingSubmission(OwnerPendingSubmission candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (pendingSubmission is not null)
        {
            SetStatus("a prior owner request is awaiting confirmation; retry it or explicitly forget it first", good: false);
            return false;
        }

        if (!pendingSubmissionStore.TrySave(candidate))
        {
            SetStatus("could not retain the owner request locally; retry or forget the existing local retry record first", good: false);
            return false;
        }

        pendingSubmission = candidate;
        RenderPendingSubmission();
        RefreshControlAvailability();
        return true;
    }

    private void LoadPendingSubmission()
    {
        pendingSubmission = TryCreatePendingSubmissionBinding(out var binding)
            ? pendingSubmissionStore.TryLoad(binding)
            : null;
        RenderPendingSubmission();
        RefreshControlAvailability();
    }

    private bool TryCreatePendingSubmissionBinding(out OwnerPendingSubmissionBinding binding)
    {
        binding = null!;
        if (registeredEndpointInvalid || registration is null || deviceKey is null)
        {
            return false;
        }

        try
        {
            binding = OwnerPendingSubmissionBinding.Create(
                registration.Authority,
                registration.DeviceId,
                deviceKey.PublicKeyFingerprint,
                ResolveWorldUri());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task RetryPendingSubmissionAsync()
    {
        var pending = pendingSubmission;
        if (pending is null)
        {
            SetStatus("there is no loaded owner request to retry", good: false);
            return;
        }

        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            !TryCreatePendingSubmissionBinding(out var binding) ||
            !pending.Binding.Matches(binding))
        {
            SetStatus("this retained request is not bound to the current paired device and pinned server; forget it explicitly before making a new request", good: false);
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            if (pending.Instruction is { } instruction)
            {
                var receipt = await ownerApi.SubmitInstructionAsync(
                    ResolveWorldUri(), authority, deviceId, instruction.ToAction(), signer, CancellationToken.None);
                completed = true;
                return $"confirmed {instruction.Kind} instruction {receipt.InstructionId}";
            }

            if (pending.Authoring is { } authoring)
            {
                var receipt = await ownerApi.SubmitAuthoringAsync(
                    ResolveWorldUri(), authority, deviceId, authoring.ToAction(), signer, CancellationToken.None);
                completed = true;
                return receipt.Applied
                    ? $"confirmed authoring batch {receipt.BatchId} at revision {receipt.Revision}"
                    : $"authoring batch rejected · {receipt.Failure ?? "unknown validation failure"}";
            }

            throw new InvalidOperationException("The retained owner request has no supported payload.");
        });

        if (completed)
        {
            CompletePendingSubmission(pending);
        }
    }

    private void CompletePendingSubmission(OwnerPendingSubmission completed)
    {
        if (!pendingSubmissionStore.TryClear(completed))
        {
            SetStatus("server confirmed the request, but its local retry record could not be cleared; retry remains safe or forget it after checking the world", good: false);
            return;
        }

        if (ReferenceEquals(pendingSubmission, completed))
        {
            pendingSubmission = null;
        }

        RenderPendingSubmission();
        RefreshControlAvailability();
    }

    private void ForgetPendingSubmission()
    {
        if (!pendingSubmissionStore.TryForget())
        {
            SetStatus("could not discard the local retry record", good: false);
            return;
        }

        pendingSubmission = null;
        RenderPendingSubmission();
        RefreshControlAvailability();
        SetStatus("discarded the local retry record; no server state was changed", good: false);
    }

    private void RenderPendingSubmission()
    {
        pendingSubmissionLabel.Text = pendingSubmission switch
        {
            { Instruction: { } instruction } =>
                $"Retained instruction retry · {instruction.Kind} for {instruction.TargetInhabitantId} · ID {instruction.IdempotencyKey}",
            { Authoring: { } authoring } =>
                $"Retained paused-authoring retry · batch {authoring.BatchId}",
            _ => "No retained owner request. A network failure keeps one instruction or authoring batch here for an exact retry.",
        };
    }

    private async Task RunOwnerActionAsync(Func<Task<string>> action)
    {
        if (isOwnerAction)
        {
            return;
        }

        isOwnerAction = true;
        RefreshControlAvailability();
        try
        {
            SetStatus("Sending…", good: true);
            var detail = await action();
            SetStatus(detail, good: true);
            await RefreshAsync();
        }
        catch (System.Net.Http.HttpRequestException exception) when (exception.StatusCode is not null)
        {
            // The host answered and refused this one action; the connection
            // and the displayed world remain current.
            SetStatus($"The world host did not accept that request · {FriendlyFailure(exception)}", good: false);
        }
        catch (System.Net.Http.HttpRequestException exception)
        {
            ShowHeldState($"could not reach the world host · {FriendlyFailure(exception)}");
        }
        catch (TaskCanceledException exception)
        {
            ShowHeldState($"the world host did not respond · {FriendlyFailure(exception)}");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not complete that action · {FriendlyFailure(exception)}", good: false);
        }
        finally
        {
            isOwnerAction = false;
            RefreshControlAvailability();
        }
    }

    private async Task RenameSelectedAgentAsync()
    {
        if (selectedInhabitantId is not { } agentId ||
            observationSession.Current?.Baseline.Snapshot.Inhabitants.All(person => person.Id != agentId) != false)
            return;
        var name = renameAgentInput.Text.Trim();
        if (name.Length is < 1 or > 48 || name.Any(char.IsControl))
        {
            SetStatus("Pick a name of 48 characters or fewer.", good: false);
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var result = await ownerApi.RenameAgentAsync(ResolveWorldUri(), authority, deviceId,
                new OwnerAgentRenameAction(agentId, name), signer, CancellationToken.None);
            renamingAgentId = null;
            return result.Changed ? $"Renamed to {result.Name}" : "Name unchanged";
        });
    }

    private bool TryGetOwner(
        out OwnerAuthorityIdentity authority,
        out string deviceId,
        out IOwnerDeviceSigner signer)
    {
        if (!registeredEndpointInvalid && registration is not null && deviceKey is not null)
        {
            authority = registration.Authority;
            deviceId = registration.DeviceId;
            signer = deviceKey;
            return true;
        }

        authority = null!;
        deviceId = string.Empty;
        signer = null!;
        return false;
    }

    private void BuildLayout()
    {
        AddThemeColorOverride("font_color", new Color("E5EFEA"));
        AddThemeFontSizeOverride("font_size", 14);

        var backdrop = new ColorRect
        {
            Color = new Color("0D151C"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        BuildTopBar(root);
        BuildWorldColumn(root);
        BuildFounderSetupPanel(mapCanvas);
        BuildInspectorColumn(mapCanvas);
        BuildOwnerColumn(mapCanvas);
        BuildStatusToast(mapCanvas);
        BuildMainMenu();
        BuildManualSavesPanel();

        Resized += ApplyResponsiveLayout;
        ApplyResponsiveLayout();
    }

    private void BuildTopBar(Control content)
    {
        var chrome = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 60),
        };
        chrome.AddThemeStyleboxOverride("panel", TopBarStyle());
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 9);
        margin.AddThemeConstantOverride("margin_bottom", 9);

        topBar.AddThemeConstantOverride("separation", 8);
        mapButton.Text = "Map";
        mapButton.TooltipText = "World map (M). Scroll to zoom, and use WASD or middle-drag to move around. F1 lists all controls.";
        StyleButton(mapButton);
        mapButton.Pressed += () =>
        {
            var show = !worldOverviewPanel.Visible;
            rosterPanel.Hide();
            settlementPanel.Hide();
            eventsPanel.Hide();
            familyTreePanel.Hide();
            worldInfoPanel.Hide();
            filtersPanel.Hide();
            worldOverviewPanel.Visible = show;
        };
        topBar.AddChild(mapButton);
        BuildFiltersButton();

        clockLabel.Text = "Connecting…";
        clockLabel.AddThemeFontSizeOverride("font_size", 20);
        clockLabel.AddThemeColorOverride("font_color", new Color("F4F0E3"));
        topBar.AddChild(clockLabel);

        climateLabel.Text = string.Empty;
        climateLabel.Modulate = new Color("AFC4BA");
        climateLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        topBar.AddChild(climateLabel);

        worldInfoButton.Text = "Info";
        worldInfoButton.TooltipText = "World Info (I)";
        StyleButton(worldInfoButton);
        worldInfoButton.Pressed += ToggleWorldInfo;
        topBar.AddChild(worldInfoButton);

        inhabitantsButton.Text = "Inhabitants";
        StyleButton(inhabitantsButton);
        inhabitantsButton.Pressed += ToggleInhabitants;
        topBar.AddChild(inhabitantsButton);

        settlementButton.Text = "Town";
        settlementButton.TooltipText = "Town stores and projects (T)";
        StyleButton(settlementButton);
        settlementButton.Pressed += () =>
        {
            filtersPanel.Hide();
            rosterPanel.Hide();
            eventsPanel.Hide();
            worldOverviewPanel.Hide();
            worldInfoPanel.Hide();
            settlementPanel.Visible = !settlementPanel.Visible;
        };
        topBar.AddChild(settlementButton);

        eventsButton.Text = "Events";
        eventsButton.TooltipText = "Event Log (E)";
        StyleButton(eventsButton);
        eventsButton.Pressed += ToggleEvents;
        topBar.AddChild(eventsButton);

        townSiteButton.Text = "Choose Town site";
        StyleButton(townSiteButton);
        townSiteButton.Pressed += ToggleFirstTownSite;
        townSiteButton.Hide();
        topBar.AddChild(townSiteButton);

        moveFounderButton.Text = "Move founder";
        moveFounderButton.TooltipText = "Pick a founder you have placed, then click a new spot. Only works before time starts.";
        StyleButton(moveFounderButton);
        moveFounderButton.Pressed += ToggleMoveFounder;
        moveFounderButton.Hide();
        topBar.AddChild(moveFounderButton);

        undoFounderButton.Text = "Undo last founder";
        undoFounderButton.TooltipText = "Take back the last founder you placed. Their model choice is cleared. Your saved keys stay.";
        StyleButton(undoFounderButton);
        undoFounderButton.Pressed += () => _ = UndoLastFounderAsync();
        undoFounderButton.Hide();
        topBar.AddChild(undoFounderButton);

        founderSetupButton.Text = "Add founders";
        StyleButton(founderSetupButton);
        founderSetupButton.Pressed += () => _ = ToggleFounderSetupAsync();
        topBar.AddChild(founderSetupButton);

        startWorldButton.Text = "Start World";
        StyleButton(startWorldButton, primary: true);
        startWorldButton.Pressed += () => _ = StartFounderWorldAsync();
        topBar.AddChild(startWorldButton);

        pauseButton.Text = "Pause";
        StyleButton(pauseButton, primary: true);
        pauseButton.Pressed += () => _ = TogglePauseAsync();
        topBar.AddChild(pauseButton);

        addAgentButton.Text = "Add Agent";
        StyleButton(addAgentButton);
        addAgentButton.Pressed += () => _ = ToggleAddAgentAsync();
        topBar.AddChild(addAgentButton);

        menuButton.Text = "Menu";
        StyleButton(menuButton);
        menuButton.Pressed += () => _ = ToggleGameMenuAsync();
        topBar.AddChild(menuButton);

        // Activated buttons return focus to the map. They remain reachable by
        // Tab/Enter, including actions without a one-key shortcut.
        foreach (var button in topBar.GetChildren().OfType<Button>())
            button.Pressed += button.ReleaseFocus;
        margin.AddChild(topBar);
        chrome.AddChild(margin);
        // Mirrors the world-view menu shade so top-bar actions such as Start
        // World or Play cannot run behind a modal menu. It draws above the
        // title backdrop so Main Menu Settings dims the whole screen evenly.
        topBarShade.Color = new Color(0, 0, 0, 0.46f);
        topBarShade.MouseFilter = Control.MouseFilterEnum.Stop;
        topBarShade.ZIndex = 190;
        topBarShade.Hide();
        chrome.AddChild(topBarShade);
        content.AddChild(chrome);
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
    }

    private void BuildCognitionSettingsPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);

        cognitionTargetChoice.AddItem("World defaults");
        cognitionTargetChoice.ItemSelected += _ =>
        {
            cognitionApiKeyInput.Text = string.Empty;
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        body.AddChild(cognitionTargetChoice);

        cognitionRoleChoice.AddItem("Routine survival");
        cognitionRoleChoice.AddItem("Planning and work");
        cognitionRoleChoice.TooltipText = "Routine covers everyday choices. Planning covers bigger projects. Jev, the optional helper, is turned on or off for the whole world in Settings.";
        cognitionRoleChoice.ItemSelected += _ =>
        {
            cognitionApiKeyInput.Text = string.Empty;
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            var selected = SelectedProviderId();
            var option = providerConfiguration?.Providers.FirstOrDefault(item => item.Provider == selected);
            cognitionModelInput.Text = option?.Model ?? DefaultProviderModel(selected);
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        var providerRow = new HBoxContainer();
        cognitionRoleChoice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cognitionProviderChoice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        providerRow.AddChild(cognitionRoleChoice);

        PopulateProviderChoices("deterministic");
        cognitionProviderChoice.ItemSelected += _ =>
        {
            cognitionApiKeyInput.Text = string.Empty;
            var selected = SelectedProviderId();
            var option = providerConfiguration?.Providers.FirstOrDefault(item => item.Provider == selected);
            cognitionModelInput.Text = option?.Model ?? DefaultProviderModel(selected);
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        providerRow.AddChild(cognitionProviderChoice);
        body.AddChild(providerRow);

        cognitionCredentialChoice.TooltipText = "Pick a saved key for this agent, or add another key for the same provider.";
        cognitionCredentialChoice.ItemSelected += _ =>
        {
            cognitionApiKeyInput.Text = string.Empty;
            RenderProviderConfiguration();
        };
        body.AddChild(cognitionCredentialChoice);

        cognitionCredentialLabelInput.PlaceholderText = "Name this key (for example, Personal account)";
        body.AddChild(cognitionCredentialLabelInput);

        cognitionModelInput.PlaceholderText = "Model ID";
        body.AddChild(cognitionModelInput);

        cognitionApiKeyInput.Secret = true;
        cognitionApiKeyInput.PlaceholderText = "Paste API key";
        body.AddChild(cognitionApiKeyInput);

        cognitionCredentialHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cognitionCredentialHint.Modulate = new Color("8FA5A7");
        body.AddChild(cognitionCredentialHint);

        cognitionCredentialHint.TooltipText = "Keys are sent securely and stay on the game server. They are never shown again, logged, or saved in world files.";

        cognitionConfigurationStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(cognitionConfigurationStatus);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        saveCognitionProviderButton.Text = "Apply";
        StyleButton(saveCognitionProviderButton, primary: true);
        saveCognitionProviderButton.Pressed += () => _ = SaveProviderConfigurationAsync();
        buttons.AddChild(saveCognitionProviderButton);
        forgetCognitionCredentialButton.Text = "Remove key";
        StyleButton(forgetCognitionCredentialButton);
        forgetCognitionCredentialButton.Pressed += () => _ = ForgetProviderCredentialAsync();
        buttons.AddChild(forgetCognitionCredentialButton);
        deleteCognitionCredentialSlotButton.Text = "Delete named key";
        deleteCognitionCredentialSlotButton.TooltipText = "Delete a saved key you no longer use. Move any agents using it to another key first.";
        StyleButton(deleteCognitionCredentialSlotButton);
        deleteCognitionCredentialSlotButton.Pressed += () => _ = DeleteCredentialSlotAsync();
        buttons.AddChild(deleteCognitionCredentialSlotButton);
        refreshCognitionProviderButton.Text = "Refresh";
        StyleButton(refreshCognitionProviderButton);
        refreshCognitionProviderButton.Pressed += () => _ = RefreshProviderConfigurationAsync();
        buttons.AddChild(refreshCognitionProviderButton);
        body.AddChild(buttons);

        body.AddChild(new Label { Text = "Model calls" });
        usageMeterStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(usageMeterStatus);
        body.AddChild(new Label
        {
            Text = "Optional cap on paid model calls. The game pauses when you reach it. Leave blank for no cap.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        usageAttemptLimitInput.PlaceholderText = "Maximum model calls (blank = no limit)";
        usageAttemptLimitInput.TooltipText = "Every call counts, even ones that fail or are retried. This counts calls, not money.";
        body.AddChild(usageAttemptLimitInput);
        var usageButtons = new HBoxContainer();
        applyUsageLimitButton.Text = "Apply limit";
        StyleButton(applyUsageLimitButton);
        applyUsageLimitButton.Pressed += () => _ = ConfigureUsageAsync(grant: false);
        usageButtons.AddChild(applyUsageLimitButton);
        grantUsageCallsButton.Text = "Allow 100 more calls";
        grantUsageCallsButton.TooltipText = "Allow 100 more paid model calls. The world stays paused until you resume it.";
        StyleButton(grantUsageCallsButton, primary: true);
        grantUsageCallsButton.Pressed += () => _ = ConfigureUsageAsync(grant: true);
        grantUsageCallsButton.Visible = false;
        usageButtons.AddChild(grantUsageCallsButton);
        refreshUsageButton.Text = "Refresh usage";
        StyleButton(refreshUsageButton);
        refreshUsageButton.Pressed += () => _ = RefreshUsageAsync();
        usageButtons.AddChild(refreshUsageButton);
        body.AddChild(usageButtons);

        AddPanelContents(cognitionSettingsPanel, "Agent model", body);
        RenderProviderConfiguration();
        RenderUsageStatus();
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

    private async Task ConnectUsingCurrentUrlAsync()
    {
        if (registeredEndpointInvalid)
        {
            SetStatus("saved paired endpoint is invalid · forget this local registration before pairing again", good: false);
            return;
        }

        try
        {
            _ = ResolveWorldUri();
        }
        catch (Exception exception)
        {
            SetStatus($"world URL is invalid · {FriendlyFailure(exception)}", good: false);
            return;
        }

        if (registration is not null)
        {
            await RefreshAsync();
            return;
        }

        await StartPairingAsync();
    }

    private async Task PairAgainAsync()
    {
        if (isPairingOperation || isOwnerAction || isRefreshing)
        {
            return;
        }

        ForgetLocalRegistration();
        await StartPairingAsync();
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
        pairingPanel.Hide();
    }

    private void BuildWorldColumn(Control content)
    {
        mapCanvas.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mapCanvas.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        mapCanvas.ClipContents = true;

        var worldBackdrop = new ColorRect
        {
            Color = new Color("101A1E"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
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
        rosterSummaryLabel.Modulate = new Color("A7B9B7");
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

        ConfigureTextPanel(worldDetails, 320);
        AddClosablePanelContents(settlementPanel, "Town", worldDetails);
        settlementPanel.CustomMinimumSize = new Vector2(420, 380);
        settlementPanel.ZIndex = 80;
        settlementPanel.Hide();
        content.AddChild(settlementPanel);

        ConfigureTextPanel(worldInfoText, 220);
        AddClosablePanelContents(worldInfoPanel, "World Info", worldInfoText);
        worldInfoPanel.CustomMinimumSize = new Vector2(365, 280);
        worldInfoPanel.ZIndex = 80;
        worldInfoPanel.Hide();
        content.AddChild(worldInfoPanel);
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
        menuShade.Color = new Color(0, 0, 0, 0.46f);
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
        menuHeadingLabel.AddThemeColorOverride("font_color", new Color("F4F0E3"));
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
        fullscreenToggle.Text = "Fullscreen";
        fullscreenToggle.ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
        fullscreenToggle.Toggled += SetFullscreen;
        gameSettingsContent.AddChild(fullscreenToggle);

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

        foreach (var percentage in DisplayUiScalePolicy.SupportedPercentages)
            uiScaleChoice.AddItem($"{percentage}%");
        uiScaleChoice.Selected = DisplayUiScalePolicy.IndexOfPercent(displayPreferences.UiScalePercent);
        uiScaleChoice.TooltipText = "Makes menus and text bigger or smaller.";
        uiScaleChoice.ItemSelected += SetUiScale;
        gameSettingsContent.AddChild(DisplaySettingRow("UI Scale", uiScaleChoice));

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
        selectedActorNameLabel.AddThemeColorOverride("font_color", new Color("F0F4EC"));
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
        selectedActorSummaryLabel.Modulate = new Color("A7B9B7");
        selectedAgentOverview.AddChild(selectedActorSummaryLabel);
        selectedActorConditionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        selectedActorConditionLabel.Modulate = new Color("C9DFCF");
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
        instructionHeading.AddThemeColorOverride("font_color", new Color("D8C6A5"));
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

    private void OpenAgentModelEditor()
    {
        if (selectedInhabitantId is null || registration is null || observationSession.Current is not { } current) return;
        PopulateCognitionTargets();
        for (var index = 1; index < cognitionTargetChoice.ItemCount; index++)
        {
            if (cognitionTargetChoice.GetItemMetadata(index).AsString() != selectedInhabitantId) continue;
            cognitionTargetChoice.Select(index);
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            cognitionTargetChoice.Hide();
            cognitionSettingsPanel.Reparent(selectedAgentModelContent, keepGlobalTransform: false);
            selectedAgentOverviewScroll.Hide();
            selectedAgentModelScroll.Show();
            RenderProviderConfiguration();
            PositionSelectedInhabitantCard(current.Baseline.Snapshot);
            _ = RefreshProviderConfigurationAsync();
            return;
        }
        SetStatus("You can't change this agent's model right now.", good: false);
    }

    private void CloseAgentModelEditor()
    {
        if (!selectedAgentModelScroll.Visible) return;
        selectedAgentModelScroll.Hide();
        cognitionSettingsPanel.Reparent(worldSettingsContent, keepGlobalTransform: false);
        cognitionTargetChoice.Show();
        selectedAgentOverviewScroll.Show();
        cognitionApiKeyInput.Text = string.Empty;
        cognitionCredentialLabelInput.Text = string.Empty;
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

    private void ToggleInhabitants()
    {
        var show = !rosterPanel.Visible;
        filtersPanel.Hide();
        familyTreePanel.Hide();
        settlementPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        rosterPanel.Visible = show;
    }

    private void ToggleEvents()
    {
        var show = !eventsPanel.Visible;
        filtersPanel.Hide();
        familyTreePanel.Hide();
        settlementPanel.Hide();
        rosterPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        eventsPanel.Visible = show;
    }

    private void ToggleWorldInfo()
    {
        var show = !worldInfoPanel.Visible;
        filtersPanel.Hide();
        familyTreePanel.Hide();
        settlementPanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Visible = show;
    }

    private void OpenFamilyTree()
    {
        if (selectedInhabitantId is not { } id || observationSession.Current is not { } current)
            return;
        ShowFamilyTree(current.Baseline.Snapshot, id);
    }

    private void ShowFamilyTree(OwnerWorldSnapshot snapshot, string id)
    {
        memoriesPanel.Hide();
        familyTreeView.SetPeople(snapshot.WorldId, snapshot.Inhabitants, id);
        UpdateFamilyTreeStatus();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        settlementPanel.Hide();
        filtersPanel.Hide();
        familyTreePanel.Show();
        ApplyResponsiveLayout();
    }

    private void UpdateFamilyTreeStatus()
    {
        familyTreeStatus.Text = familyTreeView.ParentEdgeCount + familyTreeView.PartnerEdgeCount == 0
            ? "No family links recorded yet. Housemates are not automatically relatives."
            : "Green: parent–child   ·   Pink: partnership   ·   Click a person to inspect";
    }

    private void SelectFromFamilyTree(string id)
    {
        familyTreePanel.Hide();
        selectedInhabitantId = id;
        if (observationSession.Current is not { } current) return;
        var snapshot = current.Baseline.Snapshot;
        RenderInhabitantList(snapshot);
        RenderInhabitantDetails(snapshot);
        RenderSelectedInhabitantCard(snapshot);
        RenderMap(snapshot);
    }

    private void OpenMemories()
    {
        if (selectedInhabitantId is null) return;
        familyTreePanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        settlementPanel.Hide();
        memoriesPanel.Show();
        ApplyResponsiveLayout();
    }

    private async Task TogglePauseAsync()
    {
        var paused = observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused == true;
        await SetPausedAsync(!paused);
    }

    private async Task ToggleGameMenuAsync()
    {
        if (gameMenuPanel.Visible)
        {
            await CloseGameMenuAsync();
            return;
        }

        rosterPanel.Hide();
        eventsPanel.Hide();
        familyTreePanel.Hide();
        memoriesPanel.Hide();
        returnToMainMenu = false;
        menuResumeButton.Text = "Resume";
        SetWorldMenuActionsVisible(true);
        menuHeadingLabel.Text = "Paused";
        settlementPanel.Hide();
        gameMenuPanel.Show();
        menuShade.Show();
        ApplyResponsiveLayout();

        var paused = observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused == true;
        menuPausedWorld = observationSession.Current is not null && !paused;
        menuPauseConfirmed = paused;
        if (menuPausedWorld)
        {
            menuPauseConfirmed = await SetPausedAsync(paused: true);
        }
    }

    private async Task CloseGameMenuAsync()
    {
        if (returnToMainMenu)
        {
            CloseGameMenu();
            ShowMainMenu();
            return;
        }
        var resumeWorld = menuPausedWorld;
        CloseGameMenu();
        if (resumeWorld)
        {
            await SetPausedAsync(paused: false);
        }
    }

    private void CloseGameMenu()
    {
        cognitionApiKeyInput.Text = string.Empty;
        gameMenuPanel.Hide();
        menuShade.Hide();
        settingsPanel.Hide();
        modLibraryPanel.Hide();
        developerScroll.Hide();
        menuPausedWorld = false;
        menuPauseConfirmed = false;
        menuCloseButton.Text = "×";
        menuCloseButton.TooltipText = "Return to the world";
    }

    private void OpenMenuForSetup()
    {
        // Connection and pairing share Main Menu Settings' presentation: the
        // title backdrop stays up and one compact header button goes back.
        OpenMainMenuSettings();
        menuPausedWorld = false;
        menuHeadingLabel.Text = "Connect this device";
    }

    private void SetFullscreen(bool enabled)
    {
        SaveDisplayPreferences(displayPreferences with { Fullscreen = enabled });
        DisplayServer.WindowSetMode(enabled
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        windowSizeChoice.Disabled = enabled;
        if (!enabled)
            GetWindow().Size = DisplaySizePresets[windowSizeChoice.Selected];
        RefreshAutomaticRenderResolution();
        RefreshRenderResolutionOptions();
    }

    private void ApplySavedDisplaySettings()
    {
        var window = GetWindow();
        DisplayServer.WindowSetMode(displayPreferences.UsesFullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        var windowSize = new Vector2I(displayPreferences.WindowWidth, displayPreferences.WindowHeight);
        var renderSize = new Vector2I(displayPreferences.RenderWidth, displayPreferences.RenderHeight);
        window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        if (!displayPreferences.UsesFullscreen)
            window.Size = DisplaySizePresets[DisplaySizeIndex(windowSize)];
        window.ContentScaleSize = displayPreferences.UsesAutomaticRenderResolution
            ? AutomaticRenderSize()
            : new DisplayDimensions(renderSize.X, renderSize.Y).IsReasonable
                ? renderSize : AutomaticRenderSize();
        ApplyUiScale(displayPreferences.UiScalePercent);
    }

    private void ApplyUiScale(int percent)
    {
        var factor = DisplayUiScalePolicy.ScaleFactor(percent);
        // The fallback base scale helps theme-aware controls, while this
        // client's explicit font-size overrides also need direct scaling.
        ThemeDB.FallbackBaseScale = factor;
        if (uiScaleTreeReady)
        {
            ApplyUiScaleFontOverrides(this, factor);
            ApplyResponsiveLayout();
        }
    }

    private void WatchUiScaleTree(Node node)
    {
        if (IsMapRenderNode(node) || !uiScaleWatchedNodes.Add(node.GetInstanceId())) return;
        node.ChildEnteredTree += OnUiScaleChildEnteredTree;
        foreach (var child in node.GetChildren())
            WatchUiScaleTree(child);
    }

    private void OnUiScaleChildEnteredTree(Node child)
    {
        if (IsMapRenderNode(child)) return;
        WatchUiScaleTree(child);
        ApplyUiScaleFontOverrides(child, DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent));
    }

    private bool IsMapRenderNode(Node node) =>
        node.GetInstanceId() == mapStage.GetInstanceId() || mapStage.IsAncestorOf(node);

    private void ApplyUiScaleFontOverrides(Node node, float factor)
    {
        // Map terrain, object labels, and fixed-size agent hit targets stay in
        // their native map-space geometry; only the surrounding GUI is scaled.
        if (IsMapRenderNode(node)) return;
        if (node is Control control)
        {
            // RichTextLabel has separate sizes for each style; ordinary controls use font_size.
            var themeFontSizeItems = control is RichTextLabel
                ? UiScaleRichTextFontSizeThemeItems
                : UiScaleFontSizeThemeItems;
            foreach (var themeFontSizeItem in themeFontSizeItems)
            {
                // Cache each original resolved size so changes never compound.
                var metadataKey = UiScaleBaseFontSizeMetaPrefix + themeFontSizeItem;
                var baseFontSize = control.HasMeta(metadataKey)
                    ? (int)control.GetMeta(metadataKey)
                    : control.GetThemeFontSize(themeFontSizeItem);
                if (!control.HasMeta(metadataKey))
                    control.SetMeta(metadataKey, baseFontSize);
                var scaledFontSize = Math.Max(1, (int)Math.Round(baseFontSize * factor, MidpointRounding.AwayFromZero));
                control.AddThemeFontSizeOverride(themeFontSizeItem, scaledFontSize);
            }
        }

        foreach (var child in node.GetChildren())
            ApplyUiScaleFontOverrides(child, factor);
    }

    private static Vector2I CurrentMonitorSize() =>
        DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());

    private Vector2I AutomaticRenderSize()
    {
        var monitor = CurrentMonitorSize();
        var window = GetWindow().Size;
        var mode = DisplayServer.WindowGetMode();
        var target = DisplayResolutionPolicy.AutomaticRenderSize(
            new DisplayDimensions(monitor.X, monitor.Y),
            new DisplayDimensions(window.X, window.Y),
            mode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
        return new Vector2I(target.Width, target.Height);
    }

    private void RefreshAutomaticRenderResolution()
    {
        if (!displayPreferences.UsesAutomaticRenderResolution) return;
        var target = AutomaticRenderSize();
        if (GetWindow().ContentScaleSize != target)
            GetWindow().ContentScaleSize = target;
        if (renderResolutionChoice.ItemCount > 0)
            renderResolutionChoice.SetItemText(0, $"Automatic ({target.X} × {target.Y})");
    }

    private void RefreshRenderResolutionOptions()
    {
        var monitor = CurrentMonitorSize();
        var saved = new DisplayDimensions(displayPreferences.RenderWidth, displayPreferences.RenderHeight);
        renderSizeOptions.Clear();
        renderSizeOptions.AddRange(DisplayResolutionPolicy.FixedRenderSizes(
            new DisplayDimensions(monitor.X, monitor.Y), saved)
            .Select(size => new Vector2I(size.Width, size.Height)));
        renderResolutionChoice.Clear();
        var automatic = AutomaticRenderSize();
        renderResolutionChoice.AddItem($"Automatic ({automatic.X} × {automatic.Y})");
        foreach (var size in renderSizeOptions)
            renderResolutionChoice.AddItem($"{size.X} × {size.Y}");
        renderResolutionChoice.Select(displayPreferences.UsesAutomaticRenderResolution ? 0 :
            Math.Max(0, renderSizeOptions.IndexOf(saved.IsReasonable
                ? new Vector2I(saved.Width, saved.Height) : automatic) + 1));
    }

    private static int DisplaySizeIndex(Vector2I size)
    {
        for (var index = 0; index < DisplaySizePresets.Length; index++)
            if (DisplaySizePresets[index] == size) return index;
        return 0;
    }

    // Every labelled settings row shares one caption column so the choices
    // line up; ApplyResponsiveLayout widens it with the caption text.
    private HBoxContainer DisplaySettingRow(string label, OptionButton choice)
    {
        var row = new HBoxContainer();
        var caption = new Label { Text = label, CustomMinimumSize = new Vector2(SettingCaptionWidth, 0) };
        settingCaptionLabels.Add(caption);
        row.AddChild(caption);
        choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(choice);
        return row;
    }

    private void SetWindowSize(long index)
    {
        var size = DisplaySizePresets[(int)index];
        SaveDisplayPreferences(displayPreferences with { WindowWidth = size.X, WindowHeight = size.Y });
        if (!fullscreenToggle.ButtonPressed)
            GetWindow().Size = size;
        RefreshAutomaticRenderResolution();
    }

    private void SetRenderResolution(long index)
    {
        if (index == 0)
        {
            SaveDisplayPreferences(displayPreferences with { AutoRenderResolution = true });
            RefreshAutomaticRenderResolution();
            return;
        }
        var size = renderSizeOptions[(int)index - 1];
        SaveDisplayPreferences(displayPreferences with
        {
            RenderWidth = size.X,
            RenderHeight = size.Y,
            AutoRenderResolution = false,
        });
        GetWindow().ContentScaleSize = size;
    }

    private void SetUiScale(long index)
    {
        if (index < 0 || index >= DisplayUiScalePolicy.SupportedPercentages.Count) return;
        var percent = DisplayUiScalePolicy.SupportedPercentages[(int)index];
        SaveDisplayPreferences(displayPreferences with { UiScalePercent = percent });
        ApplyUiScale(percent);
    }

    private void SetClockFormat(long index)
    {
        SaveDisplayPreferences(displayPreferences with { UseTwelveHourClock = index == 1 });
        if (observationSession.Current is { } current)
            Render(current.Baseline.Snapshot, []);
    }

    private void SetDateFormat(long index)
    {
        var format = index switch { 1 => "mdy", 2 => "ymd", _ => "dmy" };
        SaveDisplayPreferences(displayPreferences with { DateFormat = format });
        if (observationSession.Current is { } current)
            Render(current.Baseline.Snapshot, []);
    }

    private void SaveDisplayPreferences(GameDisplayPreferences updated)
    {
        displayPreferences = updated with
        {
            UiScalePercent = DisplayUiScalePolicy.NormalizePercent(updated.UiScalePercent),
        };
        try
        {
            displayPreferencesStore.Save(displayPreferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save your display settings.", good: false);
        }
    }

    private string DisplayWorldClock(long worldTick) =>
        GameUiText.FormatWorldClock(worldTick, displayPreferences.UseTwelveHourClock,
            observedCalendarPace, displayPreferences.DateFormat);

    private void AddAuthoringKinds()
    {
        AddAuthoringKind("set_terrain", "Set terrain — value: meadow, water, or mountain; x/y required");
        AddAuthoringKind("place_resource", "Place resource — ID, value=kind, x/y, renewable required");
        AddAuthoringKind("remove_resource", "Remove resource — ID required");
        AddAuthoringKind("place_object", "Place object — ID, value=kind, x/y required");
        AddAuthoringKind("remove_object", "Remove object — ID required");
        AddAuthoringKind("place_building", "Place building — ID, value=building kind, x/y required");
        AddAuthoringKind("remove_building", "Remove building — ID required");
        AddAuthoringKind("place_plant", "Place plant — ID, value=plant kind, x/y required");
        AddAuthoringKind("remove_plant", "Remove plant — ID required");
        AddAuthoringKind("create_founder_draft", "Create founder draft — ID, value=display name, x/y required");
        AddAuthoringKind("remove_founder_draft", "Remove founder draft — ID required");
        AddAuthoringKind("set_weather", "Set weather — value required");
        AddAuthoringKind("set_season", "Set season — value: spring, summer, autumn, or winter");
        AddAuthoringKind("set_weather_season", "Set weather + season — value=weather, secondary value=season");
        AddAuthoringKind("add_approved_asset_reference", "Add approved asset reference — ID and exact lowercase sha256 digest must already exist in the host catalog");
        AddAuthoringKind("remove_approved_asset_reference", "Remove approved asset reference — ID required");
    }

    private void AddAuthoringKind(string kind, string description)
    {
        authoringKind.AddItem(kind);
        authoringKind.SetItemMetadata(authoringKind.ItemCount - 1, description);
    }

    private void UpdateAuthoringHint()
    {
        if (authoringKind.ItemCount == 0)
        {
            return;
        }

        authoringHintLabel.Text = authoringKind.GetItemMetadata(authoringKind.Selected).AsString();
    }

    private static void ConfigureCoordinate(SpinBox box, string placeholder)
    {
        box.MinValue = 0;
        box.MaxValue = 99;
        box.Step = 1;
        box.CustomMinimumSize = new Vector2(72, 0);
        box.TooltipText = placeholder;
    }

    private void Render(OwnerWorldSnapshot snapshot, IReadOnlyList<OwnerWorldEvent> appendedEvents)
    {
        if (usagePauseWorldId != snapshot.WorldId)
        {
            usagePauseWorldId = snapshot.WorldId;
            wasObservedPaused = false;
        }
        var isPaused = snapshot.Authoring?.IsPaused == true;
        var checkUsagePause = isPaused && !wasObservedPaused;
        wasObservedPaused = isPaused;
        observedCalendarPace = snapshot.CalendarPace;
        jevAssistanceToggle.SetPressedNoSignal(snapshot.JevEnabled == true);
        if (cameraWorldId is not null && cameraWorldId != snapshot.WorldId)
        {
            knownEvents.Clear();
            familyTreePanel.Hide();
            memoriesPanel.Hide();
            ClearTileSelection();
        }
        foreach (var worldEvent in appendedEvents)
        {
            knownEvents[worldEvent.EventId] = worldEvent;
        }
        foreach (var expiredId in knownEvents.Keys.OrderByDescending(id => id).Skip(2048).ToArray())
        {
            knownEvents.Remove(expiredId);
        }

        RenderInhabitantList(snapshot);
        RenderMap(snapshot);
        RenderWorldHud(snapshot);
        if (checkUsagePause && registration is not null) _ = ObserveUsagePauseAsync();
        RenderFounderSetup(snapshot);
        RenderWorldInfo(snapshot);
        RenderInhabitantDetails(snapshot);
        RenderSelectedInhabitantCard(snapshot);
        if (familyTreePanel.Visible && selectedInhabitantId is { } center)
        {
            familyTreeView.SetPeople(snapshot.WorldId, snapshot.Inhabitants, center);
            UpdateFamilyTreeStatus();
        }
        RenderWorldDetails(snapshot);
        RenderModLibrary(snapshot);
        RenderEventLog();
        RefreshControlAvailability();
    }

    private static bool HasMap(OwnerWorldSnapshot snapshot) =>
        snapshot.PackedTerrain is not null || snapshot.Tiles.Count > 0;

    private static (int Width, int Height) MapDimensions(OwnerWorldSnapshot snapshot) =>
        snapshot.PackedTerrain is { } packed ? (packed.Width, packed.Height) :
        snapshot.Tiles.Count == 0 ? (0, 0) :
        (snapshot.Tiles.Max(tile => tile.X) + 1, snapshot.Tiles.Max(tile => tile.Y) + 1);

    private static bool MapContains(OwnerWorldSnapshot snapshot, int x, int y)
    {
        var (width, height) = MapDimensions(snapshot);
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    private void RenderMap(OwnerWorldSnapshot snapshot)
    {
        renderedMapSnapshot = snapshot;
        var objectIds = snapshot.Resources.Where(resource => resource.TreeKind is null)
            .Select(resource => "resource:" + resource.Id)
            .Concat(snapshot.Objects.Select(item => "object:" + item.Id))
            .Concat(snapshot.PlacedBuildings.Select(item => "building:" + item.InstanceId)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in mapObjectVisuals.Keys.Where(id => !objectIds.Contains(id)).ToArray())
        {
            mapObjectVisuals[id].QueueFree();
            mapObjectVisuals.Remove(id);
            mapObjectCanonicalXs.Remove(id);
        }

        if (!HasMap(snapshot))
        {
            foreach (var visual in inhabitantVisuals.Values) visual.QueueFree();
            inhabitantVisuals.Clear();
            inhabitantCanonicalXs.Clear();
            terrainLayer.SetHoveredTile(null);
            return;
        }

        var manifest = snapshot.Authoring?.CurrentMapManifestDigest ?? snapshot.MapManifestDigest;
        if (terrainMap is null || !string.Equals(terrainWorldId, snapshot.WorldId, StringComparison.Ordinal) ||
            !string.Equals(terrainManifestDigest, manifest, StringComparison.Ordinal) ||
            !string.Equals(terrainLayersDigest, snapshot.MapLayersDigest, StringComparison.Ordinal) ||
            (!terrainMap.HasMapLayers && snapshot.PackedMapLayers is not null))
        {
            var (width, height) = MapDimensions(snapshot);
            terrainMap = snapshot.PackedTerrain is { } packed
                ? WorldTerrainMap.FromPacked(packed, snapshot.PackedMapLayers)
                : WorldTerrainMap.FromTiles(snapshot.Tiles, width, height, snapshot.PackedMapLayers);
            terrainWorldId = snapshot.WorldId;
            terrainManifestDigest = manifest;
            terrainLayersDigest = snapshot.MapLayersDigest;
            terrainLayer.SetWorld(terrainMap);
            worldOverview.SetWorld(terrainMap);
        }
        terrainLayer.SetTrees(snapshot.Resources);
        terrainLayer.SetNaturalObjects(snapshot.Resources);
        terrainLayer.SetWeatherRegions(snapshot.WeatherRegionSize, snapshot.WeatherRegions);
        terrainLayer.SetRoads(snapshot.RoadTiles);
        terrainLayer.SetBuildings(snapshot.PlacedBuildings, snapshot.Objects);
        worldOverview.SetRoads(snapshot.RoadTiles);
        ApplyMapFilters(snapshot);
        var mapWidth = terrainMap.Width;
        var mapHeight = terrainMap.Height;
        worldOverview.WrapsEastWest = snapshot.WrapsEastWest;
        if (!string.Equals(cameraWorldId, snapshot.WorldId, StringComparison.Ordinal))
        {
            cameraWorldId = snapshot.WorldId;
            cameraZoom = 1;
            cameraCenterTiles = InitialCameraCenter(snapshot, terrainMap);
        }
        UpdateMapGeometry(snapshot);

        foreach (var resource in snapshot.Resources)
        {
            if (resource.TreeKind is not null) continue;
            AddMapObjectVisual(
                "resource:" + resource.Id,
                resource.Position,
                // Natural sites are drawn as terrain sprites; their marker only
                // adds hover help and a caption, not a second symbol.
                WorldTerrainMap.NaturalObjectName(resource.NaturalObjectKind) is null &&
                    (resource.NaturalObjectKind is not null || NatureSprites.ForCampResource(resource.Kind) is null)
                    ? ResourceGlyph(resource.Kind, resource.NaturalObjectKind) : string.Empty,
                ResourceMarker(resource.Kind, resource.NaturalObjectKind) + (resource.Quantity is null ? "" : " " + GameUiText.ResourceQuantity(resource.Kind, resource.Quantity, resource.Capacity)),
                GameUiText.ResourceTooltip(resource));
        }

        foreach (var mapObject in snapshot.Objects)
        {
            AddMapObjectVisual(
                "object:" + mapObject.Id,
                mapObject.Position,
                BuildingSprites.KindForObject(mapObject.Kind) is null ? ObjectGlyph(mapObject.Kind) : string.Empty,
                ObjectMarker(mapObject.Kind),
                Pretty(mapObject.Kind));
        }

        foreach (var building in snapshot.PlacedBuildings)
        {
            var name = building.DisplayName ?? "Building";
            var assignedTown = snapshot.Towns.FirstOrDefault(item => item.Id == building.TownId)?.Name;
            var household = snapshot.Stockpiles.FirstOrDefault(item => item.OwnerId == building.HouseholdId);
            var stored = building.StoredItems is { Count: > 0 }
                ? string.Join(" · ", building.StoredItems.Select(item => $"{Pretty(item.Kind)} {item.Quantity}"))
                : "none recorded";
            // The terrain layer draws the roof; the marker keeps the name and hover help.
            AddMapObjectVisual("building:" + building.InstanceId, building.Position, string.Empty, name,
                $"{name}\nBuilt · {building.Width} × {building.Height} tiles" +
                (assignedTown is null ? "\nNo Town assignment" : $"\nTown · {assignedTown}") +
                (household is null ? "" : $"\nHousehold · {household.Name}\nStored here · {stored}"),
                building.Width, building.Height);
        }

        foreach (var group in snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft && string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase))
            .GroupBy(inhabitant => PositionKey(inhabitant.Position)))
        {
            var occupants = group.ToArray();
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(occupants.Length)));
            var rows = (int)Math.Ceiling((double)occupants.Length / columns);
            var cellWidth = (float)currentTileSize / columns;
            var cellHeight = (float)currentTileSize / rows;
            for (var index = 0; index < occupants.Length; index++)
            {
                var inhabitant = occupants[index];
                var stride = currentTileSize + TileGap;
                var inset = Math.Min(3f, Math.Min(cellWidth, cellHeight) / 8f);
                var visualLimit = Math.Min(78f, currentTileSize * 0.55f);
                var markerSize = new Vector2(Math.Min(cellWidth - 2 * inset, visualLimit),
                    Math.Min(cellHeight - 2 * inset, visualLimit));
                var offsetX = (index % columns) * cellWidth + (cellWidth - markerSize.X) / 2;
                var offsetY = (index / columns) * cellHeight + (cellHeight - markerSize.Y) / 2;
                var targetPosition = new Vector2(
                    inhabitant.Position.X * stride + offsetX,
                    inhabitant.Position.Y * stride + offsetY);
                if (!inhabitantVisuals.TryGetValue(inhabitant.Id, out var actorMarker))
                {
                    actorMarker = new AgentMarker { Position = targetPosition };
                    actorMarker.Activated += () =>
                    {
                        if (placingAddedAgent && founderSetupPanel.Visible)
                        {
                            var currentPosition = renderedMapSnapshot?.Inhabitants
                                .FirstOrDefault(item => item.Id == inhabitant.Id)?.Position;
                            if (currentPosition is { } position)
                                _ = PlaceAgentAtAsync(new Vector2I(position.X, position.Y));
                        }
                        else
                            SelectInhabitant(inhabitant.Id);
                    };
                    actorMarker.MouseEntered += RefreshTileHoverAtMouse;
                    actorMarker.MouseExited += RefreshTileHoverAtMouse;
                    entityLayer.AddChild(actorMarker);
                    inhabitantVisuals.Add(inhabitant.Id, actorMarker);
                }
                actorMarker.Caption = $"{ActivityGlyph(inhabitant.PublicIntention?.CandidateId)} {ActorLabel(inhabitant.DisplayName)}";
                actorMarker.Variant = AgentSprites.VariantFor(inhabitant.Id);
                actorMarker.Stage = AgentSprites.StageIndex(
                    inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail);
                actorMarker.ShowNameTag = occupants.Length == 1;
                var actorTooltip = $"{inhabitant.DisplayName} · {Pretty(inhabitant.Lifecycle)} · " +
                    (inhabitant.PublicIntention?.Summary ?? "taking in the world");
                if (actorMarker.TooltipText != actorTooltip) actorMarker.TooltipText = actorTooltip;
                actorMarker.Selected = string.Equals(inhabitant.Id, selectedInhabitantId, StringComparison.Ordinal);
                inhabitantCanonicalXs[inhabitant.Id] = targetPosition.X;
                actorMarker.Position = new Vector2(
                    WrappedMarkerX(targetPosition.X, mapWidth, stride, snapshot.WrapsEastWest),
                    targetPosition.Y);
                actorMarker.Size = markerSize;

            }
        }

        var visibleInhabitantIds = snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft && string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase))
            .Select(inhabitant => inhabitant.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var removedId in inhabitantVisuals.Keys.Where(id => !visibleInhabitantIds.Contains(id)).ToArray())
        {
            inhabitantVisuals[removedId].QueueFree();
            inhabitantVisuals.Remove(removedId);
            inhabitantCanonicalXs.Remove(removedId);
        }

        RenderTileInspection(snapshot);
        PositionSelectedInhabitantCard(snapshot);
        RefreshTileHoverAtMouse();
    }

    // Clipped captions degrade into unreadable fragments such as "rehou", so a
    // marker shows its name only when the whole caption fits; the glyph and
    // tooltip still identify it when zoomed out.
    private static bool MapObjectLabelFits(Label visual, string label)
    {
        var font = visual.GetThemeFont("font");
        var fontSize = visual.GetThemeFontSize("font_size");
        return visual.Size.Y >= font.GetHeight(fontSize) * 2 &&
            font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize).X <= visual.Size.X;
    }

    /// <summary>
    /// Opens a world on its settlement rather than the geometric map center,
    /// which on generated maps is often open water: the first Town, then the
    /// living agents, then camp objects. A new world has none of these yet,
    /// so it opens near dry land instead of possibly over open water.
    /// </summary>
    private static Vector2 InitialCameraCenter(OwnerWorldSnapshot snapshot, WorldTerrainMap terrain)
    {
        var mapWidth = terrain.Width;
        var mapHeight = terrain.Height;
        IReadOnlyList<OwnerWorldPosition> focus =
            snapshot.Towns.FirstOrDefault(town => town.BorderTiles.Count > 0)?.BorderTiles ?? [];
        if (focus.Count == 0)
        {
            focus = snapshot.Inhabitants
                .Where(person => !person.IsDraft &&
                    string.Equals(person.Lifecycle, "active", StringComparison.OrdinalIgnoreCase))
                .Select(person => person.Position).ToArray();
        }
        if (focus.Count == 0) focus = snapshot.Objects.Select(item => item.Position).ToArray();
        if (focus.Count == 0)
        {
            // Prefer a little room around the cursor for Town-site selection.
            // This is a camera hint, not a claim that the host will accept a
            // five-building layout at that tile.
            static bool Dry(byte kind) => kind is 1 or 7 or 8 or 9;
            Vector2? nearestDry = null;
            Vector2? nearestWithRoom = null;
            var dryDistance = float.MaxValue;
            var roomDistance = float.MaxValue;
            for (var y = 1; y < mapHeight - 1; y++)
            {
                for (var x = 1; x < mapWidth - 1; x++)
                {
                    if (!Dry(terrain.At(x, y))) continue;
                    var deltaX = x - mapWidth / 2f;
                    var deltaY = y - mapHeight / 2f;
                    var distance = deltaX * deltaX + deltaY * deltaY;
                    if (distance < dryDistance)
                    {
                        dryDistance = distance;
                        nearestDry = new Vector2(x + 0.5f, y + 0.5f);
                    }
                    var hasRoom = true;
                    for (var dy = -1; dy <= 1 && hasRoom; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                            if (!Dry(terrain.At(x + dx, y + dy))) hasRoom = false;
                    }
                    if (!hasRoom) continue;
                    if (distance >= roomDistance) continue;
                    roomDistance = distance;
                    nearestWithRoom = new Vector2(x + 0.5f, y + 0.5f);
                }
            }
            return nearestWithRoom ?? nearestDry ?? new Vector2(mapWidth / 2f, mapHeight / 2f);
        }
        // Measure east/west offsets from one member so a group straddling a
        // wrapped seam is framed together instead of averaging to the far side.
        var reference = focus[0].X;
        var offset = focus.Average(position =>
        {
            var dx = position.X - reference;
            return snapshot.WrapsEastWest ? dx - MathF.Round(dx / (float)mapWidth) * mapWidth : dx;
        });
        return new Vector2(reference + (float)offset + 0.5f, (float)focus.Average(position => position.Y) + 0.5f);
    }

    private void AddMapObjectVisual(
        string id,
        OwnerWorldPosition position,
        string glyph,
        string label,
        string tooltip,
        int width = 1,
        int height = 1)
    {
        var stride = currentTileSize + TileGap;
        if (!mapObjectVisuals.TryGetValue(id, out var visual))
        {
            visual = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Pass,
                ZIndex = 5,
                ClipText = true,
            };
            visual.AddThemeFontSizeOverride("font_size", 12);
            visual.AddThemeColorOverride("font_color", new Color("E8F0D8"));
            visual.AddThemeColorOverride("font_shadow_color", new Color("18211D"));
            visual.AddThemeConstantOverride("shadow_offset_x", 1);
            visual.AddThemeConstantOverride("shadow_offset_y", 1);
            objectLayer.AddChild(visual);
            mapObjectVisuals.Add(id, visual);
        }
        var canonicalX = position.X * stride + 4;
        mapObjectCanonicalXs[id] = canonicalX;
        visual.Position = new Vector2(WrappedMarkerX(canonicalX, terrainMap!.Width, stride,
            renderedMapSnapshot?.WrapsEastWest == true), position.Y * stride + 4);
        visual.Size = new Vector2(stride * Math.Clamp(width, 1, 32) - TileGap - 8,
            stride * Math.Clamp(height, 1, 32) - TileGap - 8);
        visual.Text = !MapObjectLabelFits(visual, label) ? glyph :
            glyph.Length == 0 ? label : $"{glyph}\n{label}";
        visual.TooltipText = tooltip;
    }

    private void RenderWorldHud(OwnerWorldSnapshot snapshot)
    {
        var paused = snapshot.Authoring?.IsPaused == true;
        clockLabel.Text = DisplayWorldClock(snapshot.WorldTick);
        inhabitantsButton.Text = $"Agents {LivingPopulation(snapshot)}";
        inhabitantsButton.TooltipText = "Living agents · open the agent list (R). N selects the next agent.";
        climateLabel.Text = snapshot.Authoring is { } authoring
            ? $"{Pretty(authoring.Season)} · {Pretty(WeatherAtCamera(snapshot))}"
            : string.Empty;
        pauseButton.Text = paused ? "Play" : "Pause";
        pauseButton.TooltipText = paused ? "Resume the world (Space)" : "Pause the world (Space)";
        UpdatePausedBadge(snapshot);
        menuResumeButton.Text = menuPausedWorld ? "Resume" : "Close menu";
    }

    private static int LivingPopulation(OwnerWorldSnapshot snapshot) => snapshot.Inhabitants.Count(inhabitant =>
        !inhabitant.IsDraft && string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase));

    private OwnerWeatherRegion? WeatherRegionAtCamera(OwnerWorldSnapshot snapshot)
    {
        var size = Math.Max(1, snapshot.WeatherRegionSize);
        var x = Math.Max(0, (int)MathF.Floor(cameraCenterTiles.X / size));
        var y = Math.Max(0, (int)MathF.Floor(cameraCenterTiles.Y / size));
        return snapshot.WeatherRegions.FirstOrDefault(region => region.X == x && region.Y == y);
    }

    private string WeatherAtCamera(OwnerWorldSnapshot snapshot) =>
        WeatherRegionAtCamera(snapshot)?.Weather ?? snapshot.Authoring?.Weather ?? "unknown";

    private void RenderWorldInfo(OwnerWorldSnapshot snapshot)
    {
        var (width, height) = MapDimensions(snapshot);
        var localWeather = snapshot.Authoring is { } authoring
            ? $"{Pretty(authoring.Season)} · {Pretty(WeatherAtCamera(snapshot))}"
            : "Not reported";
        var townInfo = snapshot.Towns.Count == 0 ? string.Empty :
            (townBorderFilter.ButtonPressed ? "\nTown borders are outlined in amber on the map.\n"
                : "\nTown borders are hidden. Turn them on in Filters.\n") + string.Join("\n",
            snapshot.Towns.Select(town =>
                $"{town.Name} · {Pretty(town.FoundingState)} · {town.ResidentIds.Count} residents\n" +
                "Residents: " + string.Join(", ", town.ResidentIds.Select(id =>
                    snapshot.Inhabitants.FirstOrDefault(item => item.Id == id)?.DisplayName).Where(name => name is not null))));
        worldInfoText.Text =
            $"Date and time: {DisplayWorldClock(snapshot.WorldTick)}\n" +
            (snapshot.CalendarPace is { } pace ? $"Year length: {pace.DaysPerYear} days\n" : "") +
            $"Living agents: {LivingPopulation(snapshot)}\n" +
            $"Map size: {width} × {height}\n" +
            $"Buildings: {snapshot.PlacedBuildings.Count}\n" +
            $"Roads: {snapshot.RoadTiles.Count} tiles\n" +
            townInfo + "\n" +
            $"Resource locations: {snapshot.Resources.Count}\n" +
            $"Season and weather here: {localWeather}" +
            (WeatherRegionAtCamera(snapshot)?.SoilMoisture is { } moisture
                ? $"\nSoil moisture here: {moisture}%"
                : "") +
            "\n\nPress F1 for keyboard and mouse controls.";
    }

    private void RenderInhabitantList(OwnerWorldSnapshot snapshot)
    {
        var previousSelection = selectedInhabitantId;
        var selectionFound = false;
        inhabitantList.Clear();
        // The living come first, each with what they are doing; the deceased
        // follow under their own heading so history stays inspectable.
        var inhabitants = snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft)
            .OrderBy(inhabitant => IsLiving(inhabitant) ? 0 : 1)
            .ThenBy(inhabitant => inhabitant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var living = inhabitants.Count(IsLiving);
        var deceased = inhabitants.Length - living;
        rosterSummaryLabel.Text = inhabitants.Length == 0
            ? "No one lives here yet."
            : deceased == 0 ? $"{living} living" : $"{living} living · {deceased} deceased";

        foreach (var inhabitant in inhabitants)
        {
            if (!IsLiving(inhabitant) && living > 0 && inhabitantList.ItemCount == living)
            {
                var header = inhabitantList.AddItem("Deceased", selectable: false);
                inhabitantList.SetItemCustomFgColor(header, new Color("8FA5A7"));
            }
            var rowText = RosterRow(inhabitant);
            var row = inhabitantList.AddItem(rowText);
            inhabitantList.SetItemMetadata(row, inhabitant.Id);
            // The full row, in case a long activity is clipped at this width.
            inhabitantList.SetItemTooltip(row, rowText + "\n" + (IsLiving(inhabitant)
                ? "Select to find this agent on the map and open their card."
                : "Select to open this historical profile."));
            if (!IsLiving(inhabitant)) inhabitantList.SetItemCustomFgColor(row, new Color("A7B9B7"));
            if (string.Equals(inhabitant.Id, previousSelection, StringComparison.Ordinal))
            {
                selectionFound = true;
                inhabitantList.Select(row);
            }
        }
        // Fit the list to its rows instead of reserving a tall empty box.
        inhabitantList.Visible = inhabitantList.ItemCount > 0;
        var rowHeight = inhabitantList.GetThemeFont("font").GetHeight(inhabitantList.GetThemeFontSize("font_size")) +
            inhabitantList.GetThemeConstant("v_separation") + 4;
        var uiScale = DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent);
        inhabitantList.CustomMinimumSize = new Vector2(inhabitantList.CustomMinimumSize.X,
            Math.Clamp(inhabitantList.ItemCount * rowHeight + 12, 40, 360 * uiScale));
        rosterPanel.Size = rosterPanel.GetCombinedMinimumSize();

        if (!selectionFound)
        {
            selectedInhabitantId = null;
            inhabitantList.DeselectAll();
        }
    }

    private static bool IsLiving(OwnerWorldInhabitant inhabitant) =>
        string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase);

    private static string RosterRow(OwnerWorldInhabitant inhabitant)
    {
        if (!IsLiving(inhabitant)) return $"{inhabitant.DisplayName}  ·  died";
        var activity = inhabitant.DecisionFactors.Any(factor => factor.Key == "decision-pending")
            ? "deciding what to do"
            : GameUiText.ActivityPhrase(inhabitant.PublicIntention?.CandidateId, inhabitant.PublicIntention?.Summary);
        var fullness = GameUiText.FullnessState(inhabitant.HungerBasisPoints);
        return $"{inhabitant.DisplayName}  ·  {activity}" +
            (fullness is "hungry" or "very hungry" ? $"  ·  {fullness}" : string.Empty);
    }

    /// <summary>Moves the camera to a living agent; historical profiles have no map position to show.</summary>
    private void CenterOnInhabitant(string inhabitantId)
    {
        if (renderedMapSnapshot?.Inhabitants.FirstOrDefault(person => person.Id == inhabitantId) is not { } person ||
            person.IsDraft || !IsLiving(person))
            return;
        CenterCameraAt(new Vector2(person.Position.X + 0.5f, person.Position.Y + 0.5f));
    }

    private void RenderInhabitantDetails(OwnerWorldSnapshot snapshot)
    {
        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            SetPanelText(inhabitantDetails, string.Empty);
            return;
        }

        if (string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase))
        {
            SetPanelText(inhabitantDetails, "Deceased · historical record; no current activity or carried inventory.");
            return;
        }

        var inventory = inhabitant.Inventory.Count == 0
            ? "nothing"
            : string.Join(", ", inhabitant.Inventory.Select(item => $"{Pretty(item.Kind)}: {item.Quantity}"));
        var currentActivity = string.IsNullOrWhiteSpace(inhabitant.Route.Status)
            ? "wandering"
            : Pretty(inhabitant.Route.Status);
        var destination = inhabitant.Route.Destination is { } routeDestination
            ? $" toward {routeDestination.X}, {routeDestination.Y}"
            : string.Empty;
        SetPanelText(inhabitantDetails,
            $"{currentActivity}{destination}\n" +
            $"Fullness {NeedPercent(inhabitant.HungerBasisPoints)}%\n" +
            $"Carrying {inventory}");
    }

    private void RenderSelectedInhabitantCard(OwnerWorldSnapshot snapshot)
    {
        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            CloseAgentModelEditor();
            selectedActorNameLabel.Text = string.Empty;
            renamingAgentId = null;
            selectedActorSummaryLabel.Text = string.Empty;
            selectedActorConditionLabel.Text = string.Empty;
            SetPanelText(inhabitantSocialDetails, string.Empty);
            SetPanelText(privateThoughtHistory, string.Empty);
            SetPanelText(memoryHistory, string.Empty);
            memoriesPanel.Hide();
            selectedInhabitantCard.Hide();
            return;
        }

        selectedActorNameLabel.Text = inhabitant.DisplayName;
        if (renamingAgentId != inhabitant.Id || !renameAgentInput.HasFocus())
        {
            renameAgentInput.Text = inhabitant.DisplayName;
            renamingAgentId = inhabitant.Id;
        }
        var ageBand = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail;
        var ageYears = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-years")?.Detail;
        var ageDays = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-days")?.Detail;
        var deathTick = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "death-tick")?.Detail;
        var deathCause = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "death-cause")?.Detail;
        var willStatus = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "will-status")?.Detail;
        var willHeir = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "will-heir")?.Detail;
        var isDeceased = string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase);
        modelSettingsButton.Disabled = isDeceased || registration is null;
        findAgentButton.Visible = !isDeceased;
        if (selectedAgentModelScroll.Visible && SelectedCognitionTarget() != inhabitant.Id)
            CloseAgentModelEditor();
        var waitingForDecision = inhabitant.DecisionFactors.Any(factor => factor.Key == "decision-pending");
        selectedActorSummaryLabel.Text = Pretty(inhabitant.Lifecycle) + (ageBand is null ? "" : " · " + Pretty(ageBand)) +
            (ageYears is null ? "" : " · " + ageYears + " years") +
            (ageDays is null ? "" : " · " + ageDays + " days") +
            (deathTick is not null && long.TryParse(deathTick, CultureInfo.InvariantCulture, out var finalTick)
                ? $" · {DisplayWorldClock(finalTick)}" : "");
        var intention = isDeceased
            ? $"Life ended{(deathCause is null ? "" : " · " + Pretty(deathCause))}. No current thoughts or activity." +
              (willStatus == "accepted" ? $" Final will: personal estate to {willHeir}." :
                  willStatus == "pending" ? " Final will pending." :
                  willStatus == "default" ? " Personal estate follows household inheritance." : "")
            : waitingForDecision
            ? "Decision pending."
            : inhabitant.PublicIntention is { } publicIntention
            // The summary is a gerund phrase ("keeping a safe routine"); the
            // candidate reads as a verb phrase that fits "Wants to".
            ? $"Wants to {GameUiText.HumanizeIdentifier(publicIntention.CandidateId).ToLowerInvariant()}."
            : "Taking in their surroundings.";
        var relationships = inhabitant.Relationships.Count == 0
            ? "No close relationships yet."
            : string.Join("; ", inhabitant.Relationships.Select(relationship => GameUiText.RelationshipSummary(
                relationship.Type, relationship.State, GameUiText.PartyName(snapshot, relationship.OtherPartyId),
                relationship.Direction)));
        var decision = snapshot.Cognition?.Decisions?.FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        var activity = waitingForDecision ? "Decision pending" : decision is null
            ? "No decision yet"
            : decision.FellBack
            ? $"Model did not provide a usable choice · built-in rules chose to {GameUiText.HumanizeIdentifier(decision.CandidateId).ToLowerInvariant()}"
            : $"{ProviderDisplayName(decision.Provider)} chose to {GameUiText.HumanizeIdentifier(decision.CandidateId).ToLowerInvariant()}";
        var projectText = inhabitant.Project is { } project
            ? $"{project.Label} · {Pretty(project.Stage)} · {project.WorkDone}/{project.WorkRequired}" +
                (project.Blocker is null ? "" : $"\n{project.Blocker}")
            : "No active building project";
        var socialNotes = inhabitant.SocialNotes.Count == 0 ? "" : "\n" + string.Join("\n", inhabitant.SocialNotes);
        var standing = inhabitant.SocialStanding.Count == 0 ? "" : "\n" + string.Join(" · ",
            inhabitant.SocialStanding.Select(item => $"Trust in {item.SubjectName} {item.Trust}/10"));
        var condition = inhabitant.Survival is { } survival
            ? $"{(isDeceased ? "At death · " : "")}Warmth {survival.WarmthBasisPoints / 100}% · Illness {survival.IllnessBasisPoints / 100}%" +
                $" · Diet {survival.NutritionBasisPoints / 100}%\n" +
                $"{(survival.HasClothing ? "Clothed" : "No warm clothing")} · {(survival.HasTool ? "Tool equipped" : "Working by hand")}" :
                string.Empty;
        // Omit the condition line until the host reports it, rather than
        // filling the card with an "unavailable" placeholder.
        selectedActorConditionLabel.Text = condition;
        selectedActorConditionLabel.Visible = condition.Length > 0;
        var role = inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "role")?.Detail;
        var learning = inhabitant.Lesson is { } lesson
            ? $"\nLearning {Pretty(lesson.Role)} with {lesson.TeacherName} · {Pretty(lesson.Stage)} · {lesson.Progress}/{lesson.Required}" : "";
        if (inhabitant.Proficiency is { } practice)
            learning += $"\nPractice · Building {practice.Building}/30 · Farming {practice.Farming}/30 · Crafting {practice.Crafting}/30";
        var socialText = $"{(role is null or "unassigned" ? "" : $"Role: {Pretty(role)}\n")}{(inhabitant.Project is null ? intention : projectText)}{learning}\n{relationships}{standing}{socialNotes}\n{activity}";
        SetPanelText(inhabitantSocialDetails, socialText);
        var thoughtHeading = isDeceased ? "Private thoughts · historical" : "Private thoughts";
        SetPanelText(privateThoughtHistory, inhabitant.RecentPrivateThoughts.Count == 0
            ? thoughtHeading + "\nNone recorded yet."
            : thoughtHeading + "\n" + string.Join("\n", inhabitant.RecentPrivateThoughts
                .Reverse().Select(thought => $"{DisplayWorldClock(thought.WorldTick)}  {thought.Text}")));
        var memoryRows = new List<(long WorldTick, int Kind, string Text)>();
        memoryRows.AddRange(inhabitant.RecentBeliefs.Select(belief =>
        {
            var evidence = belief.Provenance switch
            {
                "firsthand" => "witnessed",
                "hearsay" when belief.SourceAgentName is { } source => $"heard from {source}",
                "hearsay" => "heard from someone",
                _ => "inferred",
            };
            var subject = belief.AboutInhabitantId is { } subjectId
                ? snapshot.Inhabitants.FirstOrDefault(person => person.Id == subjectId)?.DisplayName
                : null;
            var context = $"Belief · {evidence} · {belief.ConfidenceBasisPoints / 100}% sure" +
                (subject is null ? "" : $" · about {subject}") +
                (belief.IsCorrected
                    ? $" · corrected{(belief.CorrectedTick is { } correctedTick ? $" at {DisplayWorldClock(correctedTick)}" : "")}" : "");
            return (belief.WorldTick, 0,
                $"{DisplayWorldClock(belief.WorldTick)} · {context}\n{belief.Statement}");
        }));
        memoryRows.AddRange(inhabitant.RecentMemories.Select(memory =>
            (memory.WorldTick, 1,
                $"{DisplayWorldClock(memory.WorldTick)} · {Pretty(memory.Visibility)} · about {memory.SubjectName}\n{memory.Summary}")));
        memoryRows.AddRange(inhabitant.RecentKnowledgeFacts.Select(fact =>
        {
            var acquisition = fact.Acquisition == "firsthand"
                ? $"discovered by {fact.DiscovererName}"
                : $"{Pretty(fact.Acquisition)} from {fact.SourceAgentName ?? "another agent"}; discovered by {fact.DiscovererName}";
            var resources = fact.ResourceKinds.Count == 0 ? "no recorded resource site" :
                "resources · " + string.Join(", ", fact.ResourceKinds.Select(Pretty));
            return (fact.WorldTick, 2,
                $"{DisplayWorldClock(fact.WorldTick)} · Map fact · {acquisition}\n" +
                $"{Pretty(fact.Terrain)} at ({fact.X}, {fact.Y}) · {resources}");
        }));
        memoryRows.AddRange(inhabitant.KnowledgeArtifacts.Select(artifact =>
        {
            var sites = string.Join("\n", artifact.Sites.Select(site =>
                $"  {Pretty(site.Terrain)} at ({site.X}, {site.Y})" +
                (site.ResourceKinds.Count == 0 ? "" : " · " + string.Join(", ", site.ResourceKinds.Select(Pretty)))));
            return (artifact.CreatedTick, 3,
                $"{DisplayWorldClock(artifact.CreatedTick)} · {Pretty(artifact.Kind)} · {artifact.Title} · by {artifact.CreatorName}\n{sites}");
        }));
        SetPanelText(memoryHistory, memoryRows.Count == 0
            ? "No saved memories, beliefs, or map records for this agent yet."
            : string.Join("\n\n", memoryRows.OrderByDescending(item => item.WorldTick)
                .ThenBy(item => item.Kind).Select(item => item.Text)));
        inhabitantSocialDetails.TooltipText = decision is null ? "" :
            $"Last accepted decision\nRole: {decision.Role ?? "not reported"}\nModel: {decision.Model ?? "not reported"}\nConfidence: {decision.Confidence:P0}\n" +
            $"Latency: {decision.LatencyMilliseconds?.ToString(CultureInfo.CurrentCulture) ?? "—"} ms\n" +
            $"Tokens in/out: {decision.InputTokens?.ToString(CultureInfo.CurrentCulture) ?? "—"}/{decision.OutputTokens?.ToString(CultureInfo.CurrentCulture) ?? "—"}";
        if (inhabitant.Proficiency is not null)
            inhabitantSocialDetails.TooltipText += "\nPractice: each completed project earns one point in its domain, up to 30. Every 10 points adds one work per preparation step. Materials, permissions and crop growth time are unchanged.";
        selectedInhabitantCard.Show();
        PositionSelectedInhabitantCard(snapshot);
    }

    private void RenderWorldDetails(OwnerWorldSnapshot snapshot)
    {
        var authoring = snapshot.Authoring;
        var lines = new List<TownLine>();
        if (authoring is not null)
            lines.Add(new(TownStyle.Note, $"{(authoring.IsPaused ? "Paused" : "Playing")} · {DisplayWorldClock(snapshot.WorldTick)} · " +
                $"{Pretty(authoring.Season)} · {Pretty(WeatherAtCamera(snapshot))} here"));
        lines.Add(new(TownStyle.Heading, "Shared stores"));
        if (snapshot.Stockpiles.Count == 0) lines.Add(new(TownStyle.Note, "No shared stores yet."));
        foreach (var stockpile in snapshot.Stockpiles)
        {
            lines.Add(new(TownStyle.Name, stockpile.Name));
            lines.Add(new(TownStyle.Detail, stockpile.Items.Count == 0 ? "empty" :
                string.Join(" · ", stockpile.Items.Select(item => $"{Pretty(item.Kind)} {item.Quantity}"))));
        }
        lines.Add(new(TownStyle.Heading, "Projects"));
        var workers = snapshot.Inhabitants.Where(person => person.Project is not null).ToArray();
        if (workers.Length == 0) lines.Add(new(TownStyle.Note, "No one is working on a project right now."));
        foreach (var person in workers)
        {
            lines.Add(new(TownStyle.Body, $"{person.DisplayName}: {person.Project!.Label} · {Pretty(person.Project.Stage)}"));
            if (person.Project.Blocker is { } blocker) lines.Add(new(TownStyle.Warning, blocker));
        }
        if (snapshot.Council is { } council)
        {
            lines.Add(new(TownStyle.Heading, "Household council"));
            lines.Add(new(TownStyle.Body, $"Steward: {council.StewardName ?? "awaiting a contributor"}"));
            lines.Add(new(TownStyle.Body, council.FoodPolicy == "essential_first" ? "Food reserve: hungry members first" : "Shared food: open access"));
            if (council.ProposedPolicy is not null)
                lines.Add(new(TownStyle.Body, $"Vote: {Pretty(council.ProposedPolicy)} · {council.Approvals} yes / {council.Rejections} no / {council.Voters} voters"));
        }
        lines.Add(new(TownStyle.Heading, "Social activity"));
        var notes = snapshot.Inhabitants.SelectMany(person => person.SocialNotes.Take(2).Select(note => $"{person.DisplayName}: {note}")).ToArray();
        if (notes.Length == 0) lines.Add(new(TownStyle.Note, "Nothing to report yet."));
        lines.AddRange(notes.Select(note => new TownLine(TownStyle.Body, note)));
        WriteTownPanel(lines);
    }

    private void RenderEventLog()
    {
        var snapshot = observationSession.Current?.Baseline.Snapshot;
        var entries = knownEvents.Values
            .Where(worldEvent => GameUiText.IsPlayerFacingEvent(worldEvent.Kind))
            .OrderByDescending(worldEvent => worldEvent.EventId)
            .Take(30)
            .Select(worldEvent => (worldEvent.EventId, Located: worldEvent.Position is not null,
                Clock: DisplayWorldClock(worldEvent.WorldTick), Text: DescribeWorldEvent(worldEvent, snapshot)))
            .ToArray();
        // Rebuilding identical rows every refresh would reset the reader's
        // scroll position, so only a changed list is redrawn.
        var content = string.Join("\n", entries.Select(entry => $"{entry.EventId}|{entry.Located}|{entry.Clock}|{entry.Text}"));
        if (renderedEventLog == content) return;
        renderedEventLog = content;
        eventLog.Clear();
        if (entries.Length == 0)
        {
            eventLog.PushColor(DimText);
            eventLog.AddText("Nothing notable has happened yet.");
            eventLog.Pop();
            return;
        }

        // Newest first, grouped under each day so a time is enough per row.
        string? day = null;
        foreach (var entry in entries)
        {
            var (date, time) = SplitClock(entry.Clock);
            if (date != day)
            {
                if (day is not null) eventLog.Newline();
                eventLog.PushFontSize(13);
                eventLog.PushColor(HeadingText);
                eventLog.AddText(date);
                eventLog.Pop();
                eventLog.Pop();
                eventLog.Newline();
                day = date;
            }
            eventLog.PushColor(DimText);
            eventLog.AddText(time + "  ");
            eventLog.Pop();
            if (entry.Located)
            {
                eventLog.PushMeta(entry.EventId.ToString(CultureInfo.InvariantCulture));
                eventLog.PushColor(LinkText);
                eventLog.AddText(entry.Text + " ↗");
                eventLog.Pop();
                eventLog.Pop();
            }
            else eventLog.AddText(entry.Text);
            eventLog.Newline();
        }
    }

    private void JumpToEvent(string eventId)
    {
        if (!long.TryParse(eventId, CultureInfo.InvariantCulture, out var id) ||
            !knownEvents.TryGetValue(id, out var worldEvent) ||
            worldEvent.Position is not { } position)
            return;
        CenterCameraAt(new Vector2(position.X + 0.5f, position.Y + 0.5f));
        eventsPanel.Hide();
    }

    private void SelectInhabitantFromList(long index)
    {
        if (index < 0 || index >= inhabitantList.ItemCount)
        {
            return;
        }

        var inhabitantId = inhabitantList.GetItemMetadata((int)index).AsString();
        if (string.Equals(inhabitantId, selectedInhabitantId, StringComparison.Ordinal))
        {
            ClearInhabitantSelection();
            return;
        }

        selectedInhabitantId = inhabitantId;
        rosterPanel.Hide();
        // The roster promises to find the agent, so bring them into view.
        CenterOnInhabitant(inhabitantId);
        if (observationSession.Current is { } current)
        {
            RenderInhabitantDetails(current.Baseline.Snapshot);
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

    private void SelectInhabitant(string inhabitantId)
    {
        if (string.Equals(inhabitantId, selectedInhabitantId, StringComparison.Ordinal))
        {
            ClearInhabitantSelection();
            return;
        }

        selectedInhabitantId = inhabitantId;
        for (var index = 0; index < inhabitantList.ItemCount; index++)
        {
            if (string.Equals(inhabitantList.GetItemMetadata(index).AsString(), inhabitantId, StringComparison.Ordinal))
            {
                inhabitantList.Select(index);
                break;
            }
        }

        if (observationSession.Current is { } current)
        {
            RenderInhabitantDetails(current.Baseline.Snapshot);
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

    private void ClearInhabitantSelection()
    {
        CloseAgentModelEditor();
        familyTreePanel.Hide();
        memoriesPanel.Hide();
        selectedInhabitantId = null;
        inhabitantList.DeselectAll();
        if (observationSession.Current is { } current)
        {
            RenderInhabitantDetails(current.Baseline.Snapshot);
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

    private void RefreshControlAvailability()
    {
        var paired = !registeredEndpointInvalid && registration is not null && deviceKey is not null;
        worldSettingsCategoryButton.Disabled = !paired || !isInWorld || returnToMainMenu;
        var snapshot = observationSession.Current?.Baseline.Snapshot;
        var paused = snapshot?.Authoring?.IsPaused == true;
        var selected = snapshot?.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        var actionDisabled = !paired || isOwnerAction || pendingSubmission is not null;
        autosaveApplyButton.Disabled = actionDisabled || !paused || !autosaveSettingsLoaded;
        var supportsLifePace = snapshot?.LifePaceRate is not null;
        var supportsJevAssistance = snapshot?.JevEnabled is not null &&
            observationSession.Current?.Handshake.ServerCapabilities.Contains("owner-jev-assistance.v1", StringComparer.Ordinal) == true;
        jevAssistanceToggle.Disabled = actionDisabled || !paused || !supportsJevAssistance;
        applyLifePaceButton.Disabled = actionDisabled || !paused || !supportsLifePace;
        lifePaceChoice.Disabled = actionDisabled || !paused || !supportsLifePace;
        applyLifePaceButton.TooltipText = !supportsLifePace ? "This host does not support life pacing." :
            !paused ? "Pause the world before changing life pace." : "Apply future aging speed; existing ages are preserved.";
        if (snapshot?.LifePaceRate is { } rate && (lastObservedLifePace != rate || lastLifePaceWorldId != snapshot.WorldId))
        {
            lifePaceChoice.Select(lifePaceChoice.GetItemIndex(rate));
            lastObservedLifePace = rate;
            lastLifePaceWorldId = snapshot.WorldId;
        }
        worldUrlInput.Editable = registration is null && pendingPairing is null && !isPairingOperation && !isOwnerAction && !isRefreshing;
        connectButton.Disabled = registeredEndpointInvalid || pendingPairing is not null || isPairingOperation || isOwnerAction || isRefreshing;
        pairAgainButton.Visible = registration is not null;
        pairAgainButton.Disabled = isPairingOperation || isOwnerAction || isRefreshing;
        pauseButton.Disabled = actionDisabled || snapshot is null;
        pauseButton.Visible = snapshot?.FounderSetup is not { Started: false };
        founderSetupButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { Started: false };
        townSiteButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { CanChooseTownSite: true };
        moveFounderButton.Disabled = actionDisabled || movingFounderId is null &&
            (snapshot?.FounderSetup is not { Started: false, Placed: > 0 } ||
             selected is null || !selected.Id.StartsWith("founder:", StringComparison.Ordinal));
        undoFounderButton.Disabled = actionDisabled || snapshot?.FounderSetup is not
        { Started: false, Placed: > 0, LastFounderId: not null };
        if (snapshot?.FounderSetup is { CanChooseTownSite: true, HasAcceptedTownSite: false })
            founderSetupButton.Disabled = true;
        addAgentButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { Started: true };
        renameAgentButton.Disabled = actionDisabled || selected is null || selected.IsDraft;
        renameAgentInput.Editable = !actionDisabled && selected is { IsDraft: false };
        startWorldButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { Started: false, Placed: 4 };
        founderProviderChoice.Disabled = actionDisabled;
        founderCredentialChoice.Disabled = actionDisabled;
        founderModelInput.Editable = !actionDisabled;
        founderApiKeyInput.Editable = !actionDisabled;
        founderKeyLabelInput.Editable = !actionDisabled;
        var infantSelected = selected?.DecisionFactors.Any(factor => factor.Key == "age-band" && factor.Detail == "infant") == true;
        var deceasedSelected = selected?.Lifecycle == "dead";
        submitInstructionButton.Disabled = actionDisabled || selected is null || selected.IsDraft || infantSelected || deceasedSelected;
        submitInstructionButton.TooltipText = deceasedSelected ? "Historical profiles cannot receive instructions." :
            infantSelected ? "Direct care through an adult caregiver." : "Send an instruction to this inhabitant.";
        submitAuthoringButton.Disabled = actionDisabled || !paused;
        authoringKind.Disabled = actionDisabled || !paused;
        authoringId.Editable = !actionDisabled && paused;
        authoringValue.Editable = !actionDisabled && paused;
        authoringSecondaryValue.Editable = !actionDisabled && paused;
        authoringX.Editable = !actionDisabled && paused;
        authoringY.Editable = !actionDisabled && paused;
        authoringRenewable.Disabled = actionDisabled || !paused;
        instructionKind.Disabled = actionDisabled || deceasedSelected;
        instructionText.Editable = !actionDisabled && !deceasedSelected;
        retryPendingSubmissionButton.Disabled = !paired || isOwnerAction || pendingSubmission is null;
        forgetPendingSubmissionButton.Disabled = isPairingOperation || isOwnerAction || isRefreshing;
        pairingApprovalId.Editable = !actionDisabled;
        pairingApprovalCode.Editable = !actionDisabled;
        approvePairingButton.Disabled = actionDisabled;
        refreshDevicesButton.Disabled = actionDisabled;
        revokeDeviceId.Editable = !actionDisabled;
        revokeDeviceButton.Disabled = actionDisabled;
        cognitionRoleChoice.Disabled = actionDisabled;
        cognitionProviderChoice.Disabled = actionDisabled;
        cognitionCredentialChoice.Disabled = actionDisabled;
        cognitionModelInput.Editable = !actionDisabled && SelectedProviderId() != "deterministic";
        cognitionApiKeyInput.Editable = !actionDisabled && SelectedProviderId() != "deterministic";
        cognitionCredentialLabelInput.Editable = !actionDisabled;
        refreshCognitionProviderButton.Disabled = actionDisabled;
        applyUsageLimitButton.Disabled = actionDisabled;
        grantUsageCallsButton.Disabled = actionDisabled || usageStatus?.LimitReached != true;
        refreshUsageButton.Disabled = actionDisabled;
        usageAttemptLimitInput.Editable = !actionDisabled;
        var selectedProvider = SelectedProviderId();
        var selectedProviderStatus = providerConfiguration?.Providers.FirstOrDefault(item =>
            string.Equals(item.Provider, selectedProvider, StringComparison.Ordinal));
        saveCognitionProviderButton.Disabled = actionDisabled ||
            SelectedCognitionTarget() is not null && selectedProvider == "jev" ||
            SelectedCognitionTarget() is not null && selectedProviderStatus?.HasCredential != true &&
                selectedProvider is ("openai" or "ollama-cloud") && SelectedCredentialChoice() == "default";
        forgetCognitionCredentialButton.Disabled = actionDisabled || selectedProvider == "deterministic" ||
            selectedProviderStatus?.HasCredential != true;
        deleteCognitionCredentialSlotButton.Disabled = actionDisabled ||
            providerConfiguration?.Assignments?.Any(item => item.CredentialSlotId == SelectedCredentialChoice()) == true;
        // A public key can have only one pending server pairing. Keep the
        // visible comparison value stable until it expires or activates.
        pairButton.Disabled = isPairingOperation || deviceKey is null || pendingPairing is not null || registration is not null;
        forgetRegistrationButton.Disabled = isPairingOperation || registration is null;
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

    private void ApplyResponsiveLayout()
    {
        var viewport = mapCanvas.Size;
        if (viewport.X <= 0 || viewport.Y <= 0)
        {
            return;
        }

        climateLabel.Visible = Size.X >= 1100;
        var uiScale = DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent);
        float panelWidth(int width) => Math.Min(width * uiScale, Math.Max(1, viewport.X - 28));
        rosterPanel.CustomMinimumSize = new Vector2(panelWidth(410), 0);
        eventsPanel.CustomMinimumSize = new Vector2(panelWidth(390), 360);
        settlementPanel.CustomMinimumSize = new Vector2(panelWidth(420), 380);
        worldInfoPanel.CustomMinimumSize = new Vector2(panelWidth(365), 280);
        selectedTilePanel.CustomMinimumSize = new Vector2(panelWidth(315), 0);
        filtersPanel.CustomMinimumSize = new Vector2(panelWidth(305), 0);
        // Size the shared caption column from its widest caption at the
        // current font size, so no single long caption pushes its choice out.
        var captionWidth = settingCaptionLabels.Aggregate(SettingCaptionWidth * uiScale,
            (widest, caption) => Math.Max(widest, caption.GetMinimumSize().X));
        foreach (var caption in settingCaptionLabels)
            caption.CustomMinimumSize = new Vector2(captionWidth, 0);
        mainMenuCard.CustomMinimumSize = new Vector2(panelWidth(440), 0);
        LayoutWorldMenu();
        manualSaveCard.CustomMinimumSize = new Vector2(panelWidth(470), 0);

        if (observationSession.Current?.Baseline.Snapshot is { } snapshot && HasMap(snapshot))
        {
            var previousTileSize = currentTileSize;
            UpdateMapGeometry(snapshot);
            if (previousTileSize != currentTileSize)
            {
                RenderMap(snapshot);
            }
            else
            {
                PositionSelectedInhabitantCard(snapshot);
            }
        }

        if (controlsPanel.Visible) PositionControlsPanel();
        PositionMapHud();
        rosterPanel.Position = new Vector2(14, 14);
        settlementPanel.Position = new Vector2(14, 14);
        worldInfoPanel.Position = new Vector2(14, 14);
        filtersPanel.Position = new Vector2(
            Math.Max(14, viewport.X - Math.Max(filtersPanel.Size.X, filtersPanel.CustomMinimumSize.X) - 14),
            14);
        PositionSelectedTilePanel();
        eventsPanel.Position = new Vector2(
            Math.Max(14, viewport.X - Math.Max(eventsPanel.Size.X, eventsPanel.CustomMinimumSize.X) - 14),
            14);
        var familySize = new Vector2(Math.Clamp(viewport.X - 28, 320, 840 * uiScale),
            Math.Clamp(viewport.Y - 28, 280, 600));
        familyTreePanel.Size = familySize;
        familyTreePanel.Position = new Vector2(
            Math.Max(14, (viewport.X - familySize.X) / 2),
            Math.Max(14, (viewport.Y - familySize.Y) / 2));
        var memoriesSize = new Vector2(Math.Clamp(viewport.X - 28, 320, 600 * uiScale),
            Math.Clamp(viewport.Y - 28, 280, 430));
        memoriesPanel.Size = memoriesSize;
        memoriesPanel.Position = new Vector2(
            Math.Max(14, (viewport.X - memoriesSize.X) / 2),
            Math.Max(14, (viewport.Y - memoriesSize.Y) / 2));

        var menuWidth = panelWidth(560);
        gameMenuPanel.CustomMinimumSize = new Vector2(menuWidth, 0);

        var toastSize = statusToast.GetCombinedMinimumSize();
        statusToast.Position = new Vector2(
            Math.Max(14, (viewport.X - toastSize.X) / 2),
            Math.Max(14, viewport.Y - toastSize.Y - 18));
    }

    private void UpdateMapGeometry(OwnerWorldSnapshot snapshot)
    {
        if (!HasMap(snapshot) || mapCanvas.Size.X <= 0 || mapCanvas.Size.Y <= 0)
        {
            return;
        }

        var (mapWidth, mapHeight) = MapDimensions(snapshot);
        var availableWidth = Math.Max(1, mapCanvas.Size.X - 36 - ((mapWidth - 1) * TileGap));
        var availableHeight = Math.Max(1, mapCanvas.Size.Y - 36 - ((mapHeight - 1) * TileGap));
        var fittedTileSize = (int)Math.Floor(Math.Min(availableWidth / mapWidth, availableHeight / mapHeight));
        var baseTileSize = Math.Clamp(fittedTileSize, 12, 220);
        if (mapWidth >= 256 && mapHeight >= 128)
        {
            // Small and Medium maps stop before their north/south edges become
            // black letterbox space. Larger maps share the same 8 px overview
            // floor, independent of their total geographic area.
            var minimumTileSize = mapWidth <= 512 && mapHeight <= 256
                ? Math.Max(8, (int)MathF.Ceiling(MathF.Max(
                    mapCanvas.Size.X / (mapWidth * 0.7f),
                    mapCanvas.Size.Y / (mapHeight * 0.7f))))
                : 8;
            // Frame a similar number of world rows at maximum zoom-in on a
            // 720p or 1440p display instead of fixing the maximum to 48 px.
            var maximumTileSize = Math.Max(minimumTileSize,
                Math.Clamp((int)MathF.Ceiling(mapCanvas.Size.Y / 14f), 48, 256));
            minimumCameraZoom = Math.Max(0.65f, minimumTileSize / (float)baseTileSize);
            maximumCameraZoom = Math.Max(minimumCameraZoom, maximumTileSize / (float)baseTileSize);
        }
        else
        {
            minimumCameraZoom = 0.65f;
            maximumCameraZoom = 4f;
        }
        cameraZoom = Math.Clamp(cameraZoom, minimumCameraZoom, maximumCameraZoom);
        currentTileSize = Math.Clamp((int)MathF.Round(baseTileSize * cameraZoom), 8, 880);

        var stageSize = new Vector2(
            (mapWidth * currentTileSize) + ((mapWidth - 1) * TileGap),
            (mapHeight * currentTileSize) + ((mapHeight - 1) * TileGap));
        mapStage.Size = stageSize;
        terrainLayer.Size = stageSize;
        var stride = currentTileSize + TileGap;
        if (snapshot.WrapsEastWest)
            cameraCenterTiles.X = PositiveMod(cameraCenterTiles.X, mapWidth);
        mapStage.Position = new Vector2(
            snapshot.WrapsEastWest
                ? mapCanvas.Size.X / 2 - cameraCenterTiles.X * stride
                : CameraAxis(cameraCenterTiles.X, stageSize.X, mapCanvas.Size.X, stride),
            CameraAxis(cameraCenterTiles.Y, stageSize.Y, mapCanvas.Size.Y, stride));
        cameraCenterTiles = new Vector2(
            snapshot.WrapsEastWest ? cameraCenterTiles.X : (mapCanvas.Size.X / 2 - mapStage.Position.X) / stride,
            (mapCanvas.Size.Y / 2 - mapStage.Position.Y) / stride);
        RepositionWrappedMapMarkers(mapWidth, stride, snapshot.WrapsEastWest);
        RefreshOverviewViewport(mapWidth, mapHeight, stride, snapshot.WrapsEastWest);
        RefreshTileHoverAtMouse();
        RenderWorldHud(snapshot);
        RenderWorldInfo(snapshot);
    }

    private static float CameraAxis(float centerTile, float stagePixels, float viewportPixels, float stride) =>
        stagePixels <= viewportPixels
            ? (viewportPixels - stagePixels) / 2
            : Math.Clamp((viewportPixels / 2) - (centerTile * stride), viewportPixels - stagePixels, 0);

    private static float PositiveMod(float value, int modulus) => (value % modulus + modulus) % modulus;

    private float WrappedMarkerX(float canonicalX, int mapWidth, float stride, bool wrapsEastWest) =>
        !wrapsEastWest ? canonicalX :
        canonicalX + MathF.Round((cameraCenterTiles.X - canonicalX / stride) / mapWidth) * mapWidth * stride;

    private void RepositionWrappedMapMarkers(int mapWidth, float stride, bool wrapsEastWest)
    {
        foreach (var (id, visual) in mapObjectVisuals)
            if (mapObjectCanonicalXs.TryGetValue(id, out var x))
                visual.Position = new Vector2(WrappedMarkerX(x, mapWidth, stride, wrapsEastWest), visual.Position.Y);
        foreach (var (id, visual) in inhabitantVisuals)
            if (inhabitantCanonicalXs.TryGetValue(id, out var x))
                visual.Position = new Vector2(WrappedMarkerX(x, mapWidth, stride, wrapsEastWest), visual.Position.Y);
    }

    private void RefreshOverviewViewport(int mapWidth, int mapHeight, float stride, bool wrapsEastWest)
    {
        var left = wrapsEastWest ? -mapStage.Position.X / stride :
            Math.Clamp(-mapStage.Position.X / stride, 0, mapWidth);
        var top = Math.Clamp(-mapStage.Position.Y / stride, 0, mapHeight);
        var right = wrapsEastWest ? (mapCanvas.Size.X - mapStage.Position.X) / stride :
            Math.Clamp((mapCanvas.Size.X - mapStage.Position.X) / stride, 0, mapWidth);
        var bottom = Math.Clamp((mapCanvas.Size.Y - mapStage.Position.Y) / stride, 0, mapHeight);
        var visible = new Rect2(left, top, right - left, bottom - top);
        worldOverview.SetVisibleTiles(visible);
        terrainLayer.SetCamera(visible, currentTileSize, TileGap, wrapsEastWest);
    }

    private void CenterCameraAt(Vector2 tileCenter)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot))
        {
            return;
        }

        cameraCenterTiles = tileCenter;
        UpdateMapGeometry(snapshot);
        PositionSelectedInhabitantCard(snapshot);
    }

    private void PanCamera(Vector2 deltaTiles)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot))
        {
            return;
        }

        CenterCameraAt(cameraCenterTiles + deltaTiles);
    }

    private void HandleMapInput(InputEvent @event)
    {
        if (gameMenuPanel.Visible ||
            renderedMapSnapshot is not { } snapshot || !HasMap(snapshot))
        {
            return;
        }

        if (@event is InputEventMouseButton mouse)
        {
            // Clicking the world hands the keyboard back to map controls.
            if (mouse.Pressed) GetViewport().GuiReleaseFocus();
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && movingFounderId is not null &&
                snapshot.FounderSetup is { Started: false })
            {
                _ = MoveFounderAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && choosingFirstTownSite &&
                snapshot.FounderSetup is { CanChooseTownSite: true })
            {
                _ = AcceptFirstTownSiteAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && founderSetupPanel.Visible &&
                snapshot.FounderSetup is { Started: false })
            {
                _ = PlaceFounderAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && founderSetupPanel.Visible &&
                placingAddedAgent && snapshot.FounderSetup is { Started: true })
            {
                _ = PlaceAgentAtAsync(TileAtCanvas(mouse.Position, snapshot));
                mapCanvas.AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.Middle)
            {
                draggingMap = mouse.Pressed;
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                // Zooming in moves toward what the player points at.
                ZoomAt(mouse.Position, mouse.ButtonIndex == MouseButton.WheelUp);
                mapCanvas.AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                var tile = TileAtCanvas(mouse.Position, snapshot);
                if (MapContains(snapshot, tile.X, tile.Y))
                {
                    selectedTile = tile;
                    terrainLayer.SetSelectedTile(tile);
                    // Show first: hidden containers report no content size.
                    selectedTilePanel.Show();
                    RenderTileInspection(snapshot);
                    mapCanvas.AcceptEvent();
                }
            }
        }
        else if (@event is InputEventMouseMotion hoverMotion)
        {
            if (draggingMap)
            {
                PanCamera(-hoverMotion.Relative / (currentTileSize + TileGap));
                mapCanvas.AcceptEvent();
            }
            UpdateTileHover(hoverMotion.Position);
        }
    }

    private void RefreshTileHoverAtMouse() => UpdateTileHover(mapCanvas.GetLocalMousePosition());

    private Vector2I TileAtCanvas(Vector2 canvasPosition, OwnerWorldSnapshot snapshot)
    {
        var tile = (canvasPosition - mapStage.Position) / (currentTileSize + TileGap);
        var x = Mathf.FloorToInt(tile.X);
        if (snapshot.WrapsEastWest)
            x = ((x % terrainMap!.Width) + terrainMap.Width) % terrainMap.Width;
        return new Vector2I(x, Mathf.FloorToInt(tile.Y));
    }

    private void ClearTileSelection()
    {
        selectedTile = null;
        terrainLayer.SetSelectedTile(null);
        selectedTilePanel.Hide();
    }

    private void RenderTileInspection(OwnerWorldSnapshot snapshot)
    {
        if (selectedTile is not { } tile || terrainMap is null) return;
        if (!MapContains(snapshot, tile.X, tile.Y))
        {
            ClearTileSelection();
            return;
        }

        var regionSize = Math.Max(1, snapshot.WeatherRegionSize);
        var region = snapshot.WeatherRegions.FirstOrDefault(item =>
            item.X == tile.X / regionSize && item.Y == tile.Y / regionSize);
        var objects = snapshot.Objects.Where(item => item.Position.X == tile.X && item.Position.Y == tile.Y)
            .Select(item => Pretty(item.Kind))
            .Concat(snapshot.Resources.Where(item => item.Position.X == tile.X && item.Position.Y == tile.Y)
                .Select(item => item.TreeKind is { } tree
                    ? $"{Pretty(tree)} tree · {Pretty(item.TreeStage ?? item.State)}"
                    : $"{WorldTerrainMap.NaturalObjectName(item.NaturalObjectKind) ?? Pretty(item.Kind) + " site"}" +
                        (item.Quantity is { } quantity ? $" · {quantity} available" : string.Empty)))
            .Concat(snapshot.PlacedBuildings.Where(item =>
                    tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
                    tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height)
                .Select(item => item.DisplayName ?? Pretty(item.DefinitionId)))
            .ToArray();
        var climate = WorldTerrainMap.ClimateName(terrainMap.ClimateAt(tile.X, tile.Y));
        var elevation = terrainMap.ElevationAt(tile.X, tile.Y);
        var hydrology = WorldTerrainMap.HydrologyName(terrainMap.HydrologyAt(tile.X, tile.Y));
        var surface = WorldTerrainMap.SurfaceName(terrainMap.SurfaceAt(tile.X, tile.Y));
        var vegetation = WorldTerrainMap.VegetationName(terrainMap.VegetationAt(tile.X, tile.Y));
        var town = snapshot.Towns.FirstOrDefault(item => item.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y));
        var propertyOwnerId = snapshot.PlacedBuildings.FirstOrDefault(item => item.HouseholdId is not null &&
            tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
            tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height)?.HouseholdId;
        var lines = new List<string>
        {
            $"Tile {tile.X}, {tile.Y}",
            $"Terrain: {WorldTerrainMap.NameFor(terrainMap.At(tile.X, tile.Y))}",
        };
        if (climate is not null) lines.Add($"Climate: {climate}");
        if (surface is not null) lines.Add($"Surface: {surface}");
        if (hydrology is not null and not "Land") lines.Add($"Water: {hydrology}");
        if (vegetation is not null and not "None") lines.Add($"Vegetation: {vegetation}");
        if ((region?.Weather ?? snapshot.Authoring?.Weather) is { } weather)
            lines.Add($"Weather: {Pretty(weather)}");
        if (region?.SoilMoisture is { } moisture)
            lines.Add($"Soil moisture: {moisture}%");
        if (elevation is { } level) lines.Add($"Elevation: {level}/255");
        if (town is not null) lines.Add($"Town: {town.Name}");
        if (propertyOwnerId is not null)
            lines.Add($"Household property: {snapshot.Stockpiles.FirstOrDefault(item => item.OwnerId == propertyOwnerId)?.Name ?? propertyOwnerId}");
        if (snapshot.RoadTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) lines.Add("Road");
        if (objects.Length > 0) lines.Add($"Objects: {string.Join(", ", objects)}");
        SetPanelText(selectedTileText, string.Join('\n', lines));
        FitSelectedTileText();
    }

    /// <summary>
    /// Grows the tile card to its wrapped facts, so the last one is not hidden
    /// behind a scrollbar, and re-anchors it inside the bottom of the view.
    /// Before the card has been laid out its text width is unknown; the
    /// label's Resized signal repeats the fit once the width arrives.
    /// </summary>
    private void FitSelectedTileText()
    {
        if (selectedTileText.Size.X >= 64)
        {
            var height = Math.Min(Math.Max(64, mapCanvas.Size.Y - 96),
                Math.Max(64, selectedTileText.GetContentHeight() + 4));
            if (Math.Abs(selectedTileText.CustomMinimumSize.Y - height) >= 1)
                selectedTileText.CustomMinimumSize = new Vector2(0, height);
        }
        selectedTilePanel.Size = selectedTilePanel.GetCombinedMinimumSize();
        PositionSelectedTilePanel();
    }

    private void PositionSelectedTilePanel() =>
        selectedTilePanel.Position = new Vector2(14,
            Math.Max(14, mapCanvas.Size.Y - Math.Max(selectedTilePanel.Size.Y,
                selectedTilePanel.CustomMinimumSize.Y) - 14));

    private void UpdateTileHover(Vector2 canvasPosition)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot) ||
            gameMenuPanel.Visible ||
            canvasPosition.X < 0 || canvasPosition.Y < 0 ||
            canvasPosition.X >= mapCanvas.Size.X || canvasPosition.Y >= mapCanvas.Size.Y)
        {
            terrainLayer.SetHoveredTile(null);
            UpdateHoverReadout(null, null);
            return;
        }

        var stagePosition = canvasPosition - mapStage.Position;
        var tile = TileAtCanvas(canvasPosition, snapshot);
        UpdateHoverReadout(snapshot, tile);
        PreviewAddAgentPlacement(snapshot, tile);
        if (!MapContains(snapshot, tile.X, tile.Y) ||
            inhabitantVisuals.Values.Any(marker => marker.Visible &&
                new Rect2(marker.Position, marker.Size).HasPoint(stagePosition)))
        {
            terrainLayer.SetHoveredTile(null);
            return;
        }

        terrainLayer.SetHoveredTile(tile);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (HandleEscape()) GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventKey { Pressed: true } key || mainMenuOverlay.Visible || gameMenuPanel.Visible ||
            GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
        {
            return;
        }
        if (HandleShortcutKey(key))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        var direction = key.Keycode switch
        {
            Key.W or Key.Up => new Vector2(0, -1),
            Key.A or Key.Left => new Vector2(-1, 0),
            Key.S or Key.Down => new Vector2(0, 1),
            Key.D or Key.Right => new Vector2(1, 0),
            _ => Vector2.Zero,
        };
        if (direction != Vector2.Zero)
        {
            PanCamera(direction * 1.5f);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Escape backs out one step at a time: a focused text field, a modal
    /// menu, a map-click mode, the newest open panel, the selected agent, and
    /// finally opens the Pause Menu. It never commits a world change itself.
    /// </summary>
    private bool HandleEscape()
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
        {
            GetViewport().GuiGetFocusOwner()!.ReleaseFocus();
            return true;
        }
        if (worldMenuOverlay.Visible)
        {
            if (!worldMenuBusy) worldMenuOverlay.Hide();
            return true;
        }
        if (manualSaveOverlay.Visible)
        {
            manualSaveOverlay.Hide();
            return true;
        }
        if (gameMenuPanel.Visible)
        {
            _ = CloseGameMenuAsync();
            return true;
        }
        if (mainMenuOverlay.Visible || !isInWorld) return false;
        if (controlsPanel.Visible)
        {
            controlsPanel.Hide();
            return true;
        }
        if (choosingFirstTownSite)
        {
            CancelFirstTownSiteSelection();
            return true;
        }
        if (movingFounderId is not null)
        {
            ToggleMoveFounder();
            return true;
        }
        if (founderSetupPanel.Visible)
        {
            founderApiKeyInput.Text = string.Empty;
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            return true;
        }
        foreach (var panel in new Control[] { familyTreePanel, memoriesPanel })
        {
            if (!panel.Visible) continue;
            panel.Hide();
            return true;
        }
        if (selectedTilePanel.Visible)
        {
            ClearTileSelection();
            return true;
        }
        var overlays = new Control[] { rosterPanel, eventsPanel, settlementPanel, worldInfoPanel, filtersPanel, worldOverviewPanel };
        if (overlays.Any(panel => panel.Visible))
        {
            foreach (var panel in overlays) panel.Hide();
            return true;
        }
        if (selectedAgentModelScroll.Visible)
        {
            CloseAgentModelEditor();
            return true;
        }
        if (selectedInhabitantCard.Visible)
        {
            ClearInhabitantSelection();
            return true;
        }
        _ = ToggleGameMenuAsync();
        return true;
    }

    /// <summary>
    /// Shows the whole agent profile when it fits; on a short view the
    /// profile scrolls inside the card so Speak and Send stay reachable.
    /// </summary>
    private void FitSelectedCardHeight()
    {
        var profileHeight = selectedAgentOverview.GetCombinedMinimumSize().Y;
        selectedAgentOverviewScroll.CustomMinimumSize = new Vector2(0, profileHeight);
        var excess = selectedInhabitantCard.GetCombinedMinimumSize().Y - (mapCanvas.Size.Y - 24);
        if (excess > 0)
            selectedAgentOverviewScroll.CustomMinimumSize = new Vector2(0, Math.Max(120, profileHeight - excess));
    }

    private void PositionSelectedInhabitantCard(OwnerWorldSnapshot snapshot)
    {
        if (!selectedInhabitantCard.Visible || mapCanvas.Size.X <= 0 || mapCanvas.Size.Y <= 0)
        {
            return;
        }

        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            return;
        }

        var cardWidth = Math.Min(370 * DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent),
            Math.Max(300, mapCanvas.Size.X - 24));
        selectedInhabitantCard.CustomMinimumSize = new Vector2(cardWidth, 0);
        FitSelectedCardHeight();
        var cardSize = selectedInhabitantCard.GetCombinedMinimumSize();
        selectedInhabitantCard.Size = cardSize;
        if (string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase))
        {
            selectedInhabitantCard.Position = new Vector2(Math.Max(12, mapCanvas.Size.X - cardWidth - 12), 12);
            return;
        }
        var stride = currentTileSize + TileGap;
        var actorCenter = mapStage.Position + new Vector2(
            (inhabitant.Position.X * stride) + (currentTileSize / 2f),
            (inhabitant.Position.Y * stride) + (currentTileSize / 2f));
        var x = Math.Clamp(actorCenter.X - (cardWidth / 2), 12, Math.Max(12, mapCanvas.Size.X - cardWidth - 12));
        var y = actorCenter.Y - (currentTileSize / 2f) - cardSize.Y - 12;
        if (y < 12)
        {
            y = actorCenter.Y + (currentTileSize / 2f) + 12;
        }

        y = Math.Clamp(y, 12, Math.Max(12, mapCanvas.Size.Y - cardSize.Y - 12));
        // A tall card on a short screen cannot fit above or below the agent,
        // so it moves beside them rather than covering the person it describes.
        var actorRect = new Rect2(actorCenter - new Vector2(currentTileSize, currentTileSize) / 2,
            new Vector2(currentTileSize, currentTileSize));
        if (new Rect2(x, y, cardSize).Intersects(actorRect))
        {
            var right = actorRect.End.X + 12;
            var left = actorRect.Position.X - cardWidth - 12;
            if (right + cardWidth <= mapCanvas.Size.X - 12) x = right;
            else if (left >= 12) x = left;
            y = Math.Clamp(actorCenter.Y - cardSize.Y / 2, 12, Math.Max(12, mapCanvas.Size.Y - cardSize.Y - 12));
        }
        selectedInhabitantCard.Position = new Vector2(x, y);
    }

    private static PanelContainer NewPanel(string title, Control content)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        AddPanelContents(panel, title, content);
        return panel;
    }

    private static void AddPanelContents(PanelContainer panel, Control content) => AddPanelContents(panel, string.Empty, content);

    /// <summary>A titled world panel whose heading carries a × that hides it, like Escape does.</summary>
    private static void AddClosablePanelContents(PanelContainer panel, string title, Control content) =>
        AddPanelContents(panel, title, content, closable: true);

    private static void AddPanelContents(PanelContainer panel, string title, Control content, bool closable = false)
    {
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
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
            heading.AddThemeColorOverride("font_color", new Color("F4F0E3"));
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
            var heading = new Label { Text = title };
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
        label.Modulate = new Color("8FA5A7");
        row.AddChild(label);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(value);
        return row;
    }

    private static void StyleButton(Button button, bool primary = false)
    {
        button.CustomMinimumSize = new Vector2(0, 34);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(
            primary ? new Color("2C706B") : new Color("20343B"),
            primary ? new Color("80CDBA") : new Color("49656A")));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(
            primary ? new Color("38877E") : new Color("2B464D"),
            new Color("B0DFCE")));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(
            primary ? new Color("225A58") : new Color("182A31"),
            new Color("D8C6A5")));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(
            new Color("17232A"),
            new Color("2A3A40")));
        button.AddThemeColorOverride("font_color", new Color("E5EFEA"));
        button.AddThemeColorOverride("font_hover_color", new Color("FFFFFF"));
        button.AddThemeColorOverride("font_pressed_color", new Color("FFFFFF"));
        button.AddThemeColorOverride("font_disabled_color", new Color("718486"));
    }

    // Settings categories are tabs: the open one reads as selected instead of
    // looking disabled, and pressing it again simply keeps it open.
    private static void StyleSettingsCategoryButton(Button button)
    {
        StyleButton(button);
        button.ToggleMode = true;
        var selected = ButtonStyle(new Color("2C706B"), new Color("80CDBA"));
        button.AddThemeStyleboxOverride("pressed", selected);
        button.AddThemeStyleboxOverride("hover_pressed", selected);
    }

    private void SelectSettingsCategory(Button selected)
    {
        foreach (var button in new[] { gameSettingsCategoryButton, worldSettingsCategoryButton, developerToggleButton })
            button.SetPressedNoSignal(button == selected);
    }

    private static StyleBoxFlat ButtonStyle(Color background, Color border) => new()
    {
        BgColor = background,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        BorderColor = border,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 7,
        ContentMarginBottom = 7,
    };

    private static StyleBoxFlat InnerPanelStyle() => new()
    {
        BgColor = new Color("111D24"),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        BorderColor = new Color("263D44"),
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
    };

    private static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color("192631"),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        BorderColor = new Color("345363"),
        CornerRadiusTopLeft = 10,
        CornerRadiusTopRight = 10,
        CornerRadiusBottomLeft = 10,
        CornerRadiusBottomRight = 10,
    };

    private static StyleBoxFlat TopBarStyle() => new()
    {
        BgColor = new Color("162127"),
        BorderWidthBottom = 1,
        BorderColor = new Color("314A4A"),
        ContentMarginLeft = 0,
        ContentMarginRight = 0,
        ContentMarginTop = 0,
        ContentMarginBottom = 0,
    };

    private Uri ResolveWorldUri()
    {
        if (!WorldServerOrigin.TryResolve(worldUrlInput.Text, out var configuredWorldUri))
        {
            throw new InvalidOperationException("World URL must be an absolute HTTPS origin (or loopback HTTP for local development).");
        }

        if (registration is null)
        {
            if (pendingPairingOrigin is not null)
            {
                if (!WorldServerOrigin.Same(configuredWorldUri, pendingPairingOrigin))
                {
                    throw new InvalidOperationException("This pending pairing is pinned to the server that created it. Wait for it to expire or forget the local registration before changing servers.");
                }

                return pendingPairingOrigin;
            }

            return configuredWorldUri;
        }

        if (!WorldServerOrigin.TryResolve(registration.WorldUrl, out var pinnedWorldUri) ||
            !WorldServerOrigin.Same(configuredWorldUri, pinnedWorldUri))
        {
            throw new InvalidOperationException("This paired device is pinned to its original server origin. Forget the local registration before pairing it with a different server.");
        }

        return pinnedWorldUri;
    }

    private static string ConfiguredWorldUrl() => ProjectSettings
        .GetSetting("clankerworld/world_url", "http://127.0.0.1:5188")
        .AsString();

    private static bool TryGetCommandLineWorldUrl(out string? worldUrl)
    {
        var argument = OS.GetCmdlineUserArgs()
            .FirstOrDefault(value => value.StartsWith("--world-url=", StringComparison.Ordinal));
        worldUrl = argument is null ? null : argument["--world-url=".Length..];
        return !string.IsNullOrWhiteSpace(worldUrl);
    }

    private enum StatusToastKind
    {
        // Action results and hints stay readable for a few refreshes.
        Message,
        // Instructions for an active map-click mode stay until replaced.
        Sticky,
        // Connection trouble clears on the next successful refresh.
        Connection,
        UsageLimit,
    }

    private void SetStatus(string text, bool good, StatusToastKind kind = StatusToastKind.Message)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            statusToast.Hide();
            return;
        }

        statusLabel.Text = text;
        statusLabel.Modulate = new Color(good ? "B9E8C5" : "F0B6A6");
        statusToastKind = kind;
        statusToastShownAtMsec = (long)Time.GetTicksMsec();
        statusToast.Show();
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// Runs on every one-second pulse and after each successful observation
    /// refresh, which must not erase an action result before the player can
    /// read it. Connection and usage-limit notices clear once a refresh
    /// succeeds without them.
    /// </summary>
    private void ExpireStatusToast(bool refreshSucceeded)
    {
        if (!statusToast.Visible) return;
        var expired = statusToastKind switch
        {
            StatusToastKind.Connection or StatusToastKind.UsageLimit => refreshSucceeded,
            // A mode instruction outlives its mode only as an ordinary message.
            StatusToastKind.Sticky when choosingFirstTownSite || movingFounderId is not null => false,
            _ => (long)Time.GetTicksMsec() - statusToastShownAtMsec >= StatusToastMilliseconds,
        };
        if (expired) statusToast.Hide();
    }

    private void ShowHeldState(string reason)
    {
        var heldTick = observationSession.Current?.Baseline.Snapshot.WorldTick;
        SetStatus(
            heldTick is not { } tick
                ? $"Connection lost · {reason}"
                : $"Connection lost · showing the world as of {DisplayWorldClock(tick)} · {reason}",
            good: false, StatusToastKind.Connection);
    }

    private static string FriendlyFailure(Exception exception) => exception switch
    {
        System.Net.Http.HttpRequestException { StatusCode: { } code } => code switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                "this device is not allowed in. Try connecting it again",
            System.Net.HttpStatusCode.NotFound => "the server does not know about that",
            System.Net.HttpStatusCode.Conflict => "the server's state changed. Try again",
            System.Net.HttpStatusCode.TooManyRequests => "the server cannot handle another connection request right now. Try again later",
            >= System.Net.HttpStatusCode.InternalServerError => "the server had a problem",
            _ => $"the server said no ({(int)code})",
        },
        System.Net.Http.HttpRequestException => "cannot reach the world server",
        OperationCanceledException => "the server took too long to answer",
        System.Text.Json.JsonException => "the server sent something unexpected",
        _ => exception.Message,
    };

    private static string DescribeWorldEvent(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var parts = worldEvent.Detail.Split(':', StringSplitOptions.RemoveEmptyEntries);
        string NameAt(int index)
        {
            if (index >= parts.Length)
            {
                return "Someone";
            }

            return snapshot?.Inhabitants.FirstOrDefault(inhabitant => inhabitant.Id == parts[index])?.DisplayName
                ?? GameUiText.HumanizeIdentifier(parts[index]);
        }

        string ThingAt(int index) => index < parts.Length
            ? GameUiText.HumanizeIdentifier(parts[index])
            : "something new";

        return worldEvent.Kind switch
        {
            "world_created" => "A new world has begun.",
            "weather_changed" when parts.Length >= 2 => $"The weather changed to {ThingAt(1)}.",
            "building_placed" => $"{ThingAt(1)} was built.",
            "build_started" => $"Work began on {ThingAt(1)}.",
            "build_completed" => $"{ThingAt(1)} is ready.",
            "recipe_started" => $"Work began on {ThingAt(1)}.",
            "recipe_completed" => $"{ThingAt(1)} was finished.",
            "crop_moisture_effect" when parts.Length >= 3 => parts[1] == "wet"
                ? "Moist soil improved a crop harvest."
                : "Dry soil reduced a crop harvest.",
            "food_harvested" => $"{NameAt(0)} gathered food.",
            "food_consumed" => $"{NameAt(0)} ate.",
            "inhabitant_slept" => $"{NameAt(0)} slept.",
            "child_born" => $"{NameAt(0)} was born.",
            "inhabitant_removed" => $"{NameAt(0)} died.",
            "estate_will_accepted" => "A final will decided who gets their belongings.",
            "estate_will_default" => "Their belongings went to their household.",
            "inhabitant_building_proposed" => $"{NameAt(0)} suggested a new building design.",
            "settlement_founded" => "A new Town was founded.",
            "town_founding_started" => "Your first Town is being set up.",
            "town_resident_joined" when parts.Length >= 2 => $"{NameAt(1)} joined the first Town.",
            "town_resident_left" when parts.Length >= 2 => $"{NameAt(1)} left the first Town.",
            "town_membership_evaluated" => "The new adult is not part of a Town yet.",
            "town_building_assigned" => "A building joined the first Town.",
            "town_border_expanded" => "The first Town border expanded.",
            "town_founded" => "Your first Town is founded.",
            "paused" => "The world was paused.",
            "resumed" => "The world resumed.",
            _ => $"{GameUiText.HumanizeIdentifier(worldEvent.Kind)}.",
        };
    }

    private static string PositionKey(OwnerWorldPosition position) => $"{position.X},{position.Y}";

    private static string ActorLabel(string displayName)
    {
        var trimmed = displayName.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "?";
        }

        return trimmed.Length <= 8 ? trimmed : $"{trimmed[..7]}…";
    }

    private static string ActivityGlyph(string? candidateId) => candidateId switch
    {
        "seek_food" => "→",
        "harvest_food" => "✦",
        "consume_food" => "♥",
        not null when candidateId.StartsWith("build:", StringComparison.Ordinal) => "◆",
        "safe_idle" => "·",
        _ => "○",
    };

    private static string ResourceMarker(string kind, string? naturalObjectKind) => naturalObjectKind switch
    {
        "berry_bush" => "BERRIES",
        "wild_greens" => "GREENS",
        "fiber_plant" => "FIBER",
        "reeds" => "REEDS",
        "stone_outcrop" => "STONE",
        "iron_outcrop" => "IRON",
        "gold_outcrop" => "GOLD",
        "diamond_outcrop" => "DIAMOND",
        "clay_bank" => "CLAY",
        "wild_seed_patch" => "SEEDS",
        "fertile_soil" => "SOIL",
        _ => ResourceMarker(kind),
    };

    private static string ResourceMarker(string kind) => kind switch
    {
        "food" => "FOOD",
        "construction" => "WOOD",
        "stone" => "STONE",
        "fiber" => "FIBER",
        "seed" => "SEEDS",
        _ => ShortMarker(kind),
    };

    private static string ResourceGlyph(string kind, string? naturalObjectKind) => naturalObjectKind switch
    {
        "berry_bush" => "●",
        "wild_greens" => "❧",
        "fiber_plant" => "♧",
        "reeds" => "≋",
        "stone_outcrop" => "⬟",
        "iron_outcrop" => "⬣",
        "gold_outcrop" => "◆",
        "diamond_outcrop" => "◇",
        "clay_bank" => "▰",
        "wild_seed_patch" => "✦",
        "fertile_soil" => "▤",
        _ => ResourceGlyph(kind),
    };

    private static string ResourceGlyph(string kind) => kind switch
    {
        "food" => "●",
        "construction" => "▰",
        "stone" => "⬟",
        "fiber" => "♧",
        "seed" => "✦",
        _ => "◆",
    };

    // Objects drawn as camp art are named like buildings; the rest keep a
    // short uppercase marker beside their symbol.
    private static string ObjectMarker(string kind) => kind switch
    {
        "campfire" or "cooking" => "Campfire",
        "bedroll" => "Bedroll",
        "shelter" => "Shelter",
        "storage" => "Storage",
        "workshop" => "Workshop",
        "path" => "Path",
        "tree" => "TREE",
        _ => ShortMarker(kind),
    };

    private static string ObjectGlyph(string kind) => kind switch
    {
        "campfire" => "✦",
        "shelter" => "⌂",
        "tree" => "♣",
        _ => "■",
    };

    private static string ShortMarker(string value)
    {
        var compact = value.Trim().Replace('_', ' ');
        return compact.Length <= 6 ? compact.ToUpperInvariant() : $"{compact[..5].ToUpperInvariant()}…";
    }

    private static string Pretty(string value) => string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 1
                ? part.ToUpperInvariant()
                : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    private static int NeedPercent(int basisPoints) => Math.Clamp(basisPoints / 100, 0, 100);

}
