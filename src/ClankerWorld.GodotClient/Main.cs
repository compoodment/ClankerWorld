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
    private const string AppIconPath = "res://icon.ico";

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
    // Panels over the map, and the full-screen menus, both drawn at UI Scale.
    private readonly ScaledLayer uiLayer = new() { Name = "Interface" };
    private readonly ScaledLayer menuLayer = new() { Name = "Menus" };
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
    private readonly Label connectionStatusLabel = new();
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
    private readonly Button inhabitantsButton = new();
    private readonly Button eventsButton = new();
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
    private readonly PanelContainer worldInfoPanel = new();
    private readonly PanelContainer selectedTilePanel = new();
    private readonly RichTextLabel selectedTileText = new();
    private Vector2I? selectedTile;
    private readonly PanelContainer gameMenuPanel = new();
    private readonly PanelContainer settingsPanel = new();
    private readonly ColorRect menuShade = new();
    private readonly ColorRect appBackdrop = new();
    private readonly ColorRect worldBackdrop = new();
    private readonly OptionButton themeChoice = new();
    private readonly CheckButton cloudHazeToggle = new();
    private readonly CheckButton lightningToggle = new();
    private readonly Label menuHeadingLabel = new();
    private readonly Button menuCloseButton = new();
    private readonly Button menuResumeButton = new();
    private readonly Button quitGameButton = new();
    private readonly ConfirmationDialog quitGameConfirmation = new();
    private readonly CheckButton fullscreenToggle = new();
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
    private OwnerDeviceRegistration? registration
    {
        get => observationSession.Registration;
        set
        {
            // Applies to load, forget, ordinary activation and recovered activation.
            refreshCancellation?.Cancel();
            observationSession.ReplaceRegistration(value);
            knownEvents.Clear();
        }
    }
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
    private CancellationTokenSource? refreshCancellation;
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
    private string? renderedEventLog;
    private StatusToastKind statusToastKind;
    private long statusToastShownAtMsec;

    public Main()
    {
        ownerApi = new OwnerWorldApi(httpClient);
    }

    public override void _Ready()
    {
        // Developer command: rewrite the Windows program icon from the logo art.
        if (OS.HasFeature("editor") && OS.GetCmdlineUserArgs().Contains("--write-app-icon", StringComparer.Ordinal))
        {
            File.WriteAllBytes(ProjectSettings.GlobalizePath(AppIconPath), MenuLogo.IconFile());
            GetTree().Quit();
            return;
        }
        displayPreferences = displayPreferencesStore.Load();
        ApplySavedDisplaySettings();
        // The logo's robot and planet are the window and taskbar icon.
        if (DisplayServer.GetName() != "headless")
            DisplayServer.SetIcon(MenuLogo.Icon(64));
        // Pixel frames, buttons and icons stay crisp when the picture is scaled.
        TextureFilter = TextureFilterEnum.Nearest;
        UiTheme.Apply(GetTree().Root, UiTheme.Resolve(UiTheme.Parse(displayPreferences.Theme)));
        if (OS.GetCmdlineUserArgs().Contains("--ui-smoke-test", StringComparer.Ordinal))
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        BuildLayout();
        UiTheme.Changed += ApplyThemeColors;
        ApplyThemeColors();
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

    public override void _ExitTree()
    {
        refreshCancellation?.Cancel();
        worldListRequest.Dispose();
        UiTheme.Changed -= ApplyThemeColors;
        deviceKey?.Dispose();
        httpClient.Dispose();
        base._ExitTree();
    }


}
