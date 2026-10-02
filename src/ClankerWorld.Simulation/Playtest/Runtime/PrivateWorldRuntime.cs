using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// The live private-world composition for the first complete single-player
/// alpha. Society owns identity, lifecycle, relationships, and inventory;
/// this composition owns the map, physical positions/needs, cognition cadence,
/// and the world-facing event stream. It is intentionally separate from the
/// legacy one-actor owner fixture while the owner protocol is migrated.
/// </summary>
public sealed partial class PrivateWorldRuntime : IDisposable
{
    public const int StateSchemaVersion = 44;
    public const int ObserverGuidanceSchemaVersion = 41;
    public const int ChildModelSelectionSchemaVersion = 33;
    public const int ConversationSchemaVersion = 35;
    public const int PersonalEquipmentSchemaVersion = 37;
    public const int ReusableContainerSchemaVersion = 39;
    public const int ToolProgressionSchemaVersion = 42;
    public const int LifeMomentIdentitySchemaVersion = 43;
    internal const int MinimumSupportedStateSchemaVersion = StateSchemaVersion;
    // Trees planted on new tiles are saved as map resources from this schema.
    private const int PlantedTreeSchemaVersion = 27;
    private const int MaximumRecentThoughts = 8;
    private const string HouseholdId = "household:camp-alpha";
    private const string SecondHouseholdId = "household:camp-beta";
    public const int RequiredFounders = 4;
    private const string FoodLotId = "food:camp-alpha";
    private const long CognitionReevaluationIntervalTicks = 30;
    private const int ResourceInteractionRange = 1;
    private const int HarvestFoodYield = 4;
    private static readonly string House1x1DefinitionId = HouseContent.House1x1().CanonicalId;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim tickGate = new(1, 1);
    private string worldSeed;
    private GeographyOptions? geographyOptions;
    private readonly Func<string, IDecisionProvider>? providerFactory;
    private readonly int maxCognitionDispatchPerCycle;
    private SeededMap map;
    private LandFertility fertility;
    private List<FarmFieldState> fields = [];
    private SocietyWorldRuntime society;
    private ContentPackageRegistry contentRegistry;
    private WorldSystemsState worldSystems;
    private DeclarativeWorldContentState worldContent;
    private WorldContentSimulationState worldSimulation;
    private WorldAssetReservationLedger assetReservations;
    private Dictionary<string, PlaytestInhabitantState> inhabitants = new(StringComparer.Ordinal);
    private Dictionary<string, PlaytestDeceasedInhabitantState> deceasedInhabitants = new(StringComparer.Ordinal);
    private Dictionary<string, ResourceState> resources = new(StringComparer.Ordinal);
    private PrivateWorldKnowledgeState knowledge = PrivateWorldKnowledgeState.Empty;
    private Dictionary<string, OwnerQueuedInstruction> instructionsByIdempotency =
        new(StringComparer.Ordinal);
    private Dictionary<string, OwnerInstructionReceipt> instructionReceipts =
        new(StringComparer.Ordinal);
    private HashSet<string> completedInstructionIds = new(StringComparer.Ordinal);
    private List<PlaytestWorldEvent> events = [];
    private long nextEventId = 1;
    private long eventHistoryFloor;
    private string? historyArchiveHead;
    private int checkpointSchemaVersion = StateSchemaVersion;
    private bool jevEnabled = true;
    private long jevPolicyRevision;
    private FounderSetupState? founderSetup;
    private List<TownRuntimeState> towns = [];
    private HashSet<GridPoint> roadTiles = [];
    private List<AgentConversation> conversations = [];
    private List<AgentConversationDailyBudget> conversationBudgets = [];
    private GridPoint SettlementStoragePosition =>
        map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
        worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == "first-town-warehouse")?.Position ??
        towns.FirstOrDefault(item => item.OriginSite is not null)?.OriginSite ??
        map.Resources.First(item => item.Id == "berry-patch").Position;
    private GridPoint WeatherAnchor =>
        map.CampObjects.FirstOrDefault(item => item.Kind == "cooking")?.Position ??
        towns.FirstOrDefault(item => item.OriginSite is not null)?.OriginSite ??
        SettlementStoragePosition;
    private long nextInstructionSequence = 1;
    private readonly Dictionary<string, PendingHostedDecision> pendingHosted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingConversationTurn> pendingConversationTurns = new(StringComparer.Ordinal);
    private readonly List<PrivateWorldMemoryCompactionTransition> memoryCompactionTransitions = [];

    private sealed record HostedDecisionOutcome(CognitionDecisionResponse? Response, string? Failure);
    private sealed record PendingHostedDecision(
        CognitionDecisionRequest Request,
        Task<HostedDecisionOutcome> Task,
        CancellationTokenSource Cancellation);
    private sealed record ConversationTurnOutcome(AgentConversationTurnResponse? Response, Exception? Failure);
    private sealed record PendingConversationTurn(
        AgentConversationTurnRequest Request,
        Task<ConversationTurnOutcome> Task,
        CancellationTokenSource Cancellation,
        long ProviderEpoch);

    public PrivateWorldRuntime(
        string worldSeed,
        Func<string, IDecisionProvider>? providerFactory = null,
        int maxCognitionQueueLength = 64,
        int maxCognitionDispatchPerCycle = 4,
        WorldStartPace startPace = WorldStartPace.Legacy,
        GeographyOptions? geographyOptions = null)
        : this(worldSeed, providerFactory, maxCognitionQueueLength, maxCognitionDispatchPerCycle, startPace, geographyOptions, preparedMap: null)
    {
    }

    private PrivateWorldRuntime(
        string worldSeed,
        Func<string, IDecisionProvider>? providerFactory,
        int maxCognitionQueueLength,
        int maxCognitionDispatchPerCycle,
        WorldStartPace startPace,
        GeographyOptions? geographyOptions,
        SeededMap? preparedMap,
        bool includeLegacyBedroll = false)
    {
        this.worldSeed = NormalizeRequiredText(worldSeed, nameof(worldSeed));
        if (geographyOptions is not null &&
            (startPace != WorldStartPace.FounderSetup ||
             !string.Equals(geographyOptions.Seed, this.worldSeed, StringComparison.Ordinal)))
            throw new ArgumentException("Generated geography requires founder setup and the world seed.", nameof(geographyOptions));
        this.geographyOptions = geographyOptions;
        this.providerFactory = providerFactory;
        if (maxCognitionQueueLength <= 0 || maxCognitionDispatchPerCycle <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCognitionQueueLength));
        }

        this.maxCognitionDispatchPerCycle = maxCognitionDispatchPerCycle;
        contentRegistry = new ContentPackageRegistry();
        worldContent = new DeclarativeWorldContentState([], []);
        worldSimulation = WorldContentSimulationState.Empty;
        assetReservations = new WorldAssetReservationLedger();
        map = preparedMap ?? (startPace == WorldStartPace.FounderSetup
            ? geographyOptions is null
                ? BaseCampMapGenerator.Generate(this.worldSeed, includeLegacyBedroll)
                : GeographyCandidateSelector.GenerateCandidate(geographyOptions, includeLegacyBedroll: includeLegacyBedroll)
            : SeededMapGenerator.Generate(this.worldSeed, includeLegacyBedroll));
        fertility = new LandFertility(map, this.worldSeed);
        worldSystems = CreateWorldSystems(this.worldSeed, map, startPace);
        society = CreateSociety(
            this.worldSeed,
            providerFactory,
            maxCognitionQueueLength,
            maxCognitionDispatchPerCycle,
            startPace);
        if (startPace is WorldStartPace.DecidedPlaytest or WorldStartPace.FounderSetup)
        {
            society.Pause();
        }
        if (startPace == WorldStartPace.FounderSetup)
        {
            founderSetup = new FounderSetupState([], false);
            if (geographyOptions is null)
                towns = [TownBorderRules.CreateFirstTown(map)];
        }
        else
            CreatePhysicalState();
        foreach (var resource in map.Resources)
        {
            resources.Add(resource.Id, ResourceState.Available);
        }
        // Out-of-season orchard trees start without fruit.
        SyncEcologyResourceStates();

        AppendEvent("world_created", $"{this.worldSeed}:inhabitants:{inhabitants.Count}");
        if (towns.Count > 0) AppendEvent("town_founding_started", TownBorderRules.FirstTownId);
    }

    /// <summary>Creates a world from the already previewed deterministic map.</summary>
    public static PrivateWorldRuntime CreateFromGeneratedGeography(string worldSeed,
        GeographyOptions geographyOptions, SeededMap preparedMap,
        Func<string, IDecisionProvider>? providerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(geographyOptions);
        ArgumentNullException.ThrowIfNull(preparedMap);
        if (!MatchesGeneratedCandidate(preparedMap, geographyOptions))
            throw new ArgumentException("The prepared map does not match the selected geography candidate.", nameof(preparedMap));

        return new PrivateWorldRuntime(worldSeed, providerFactory, 64, 4,
            WorldStartPace.FounderSetup, geographyOptions, preparedMap);
    }

    private static bool MatchesGeneratedCandidate(SeededMap map, GeographyOptions options)
    {
        var (width, height) = GeographyGenerator.Dimensions(options.Size);
        return map.Width == width && map.Height == height &&
            map.WrapsEastWest == options.WrapEastWest &&
            map.GenerationAttempt == options.CandidateAttempt &&
            string.Equals(MapManifestCodec.Digest(map), map.ManifestDigest, StringComparison.Ordinal);
    }

    public long WorldTick => society.Checkpoint.WorldTick;

    public SocietyCheckpoint Society => society.Checkpoint;

    public ContentRegistryState Content => contentRegistry.ExportState();

    public WorldSystemsState WorldSystems => worldSystems;

    public bool JevEnabled => jevEnabled;

    public long JevPolicyRevision => jevPolicyRevision;

    public FounderSetupState? FounderSetup => founderSetup;

    public IReadOnlyList<TownRuntimeState> Towns => towns
        .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<GridPoint> RoadTiles => roadTiles.OrderBy(item => item.Y).ThenBy(item => item.X).ToArray();

    public WorldContentSimulationState WorldSimulation => worldSimulation;

    public WorldAssetReservationLedgerState AssetReservations => assetReservations.ExportState();

    public PrivateWorldKnowledgeState Knowledge => knowledge;

    public IReadOnlyList<PlaytestInhabitantState> Inhabitants => inhabitants.Values
        .OrderBy(item => item.InhabitantId, StringComparer.Ordinal)
        .ToArray();

    public PrivateWorldRuntimeState ExportState()
    {
        gate.Wait();
        try
        {
            return CaptureState();
        }
        finally
        {
            gate.Release();
        }
    }

    public static PrivateWorldRuntime Restore(
        PrivateWorldRuntimeState state,
        Func<string, IDecisionProvider>? providerFactory = null,
        int maxCognitionDispatchPerCycle = 4)
        => RestoreCore(state, providerFactory, maxCognitionDispatchPerCycle, trustedPreparedState: false);

    private static PrivateWorldRuntime RestoreCore(
        PrivateWorldRuntimeState state,
        Func<string, IDecisionProvider>? providerFactory,
        int maxCognitionDispatchPerCycle,
        bool trustedPreparedState)
    {
        if (!trustedPreparedState) ValidateStateForCodec(state);
        var runtime = new PrivateWorldRuntime(
            state.WorldSeed,
            providerFactory,
            state.Society.Cognition.MaxQueueLength,
            maxCognitionDispatchPerCycle,
            state.FounderSetup is null ? WorldStartPace.Legacy : WorldStartPace.FounderSetup,
            state.Geography,
            trustedPreparedState ? state.Map : null,
            includeLegacyBedroll: state.Map.CampObjects.Any(item => item.Id == "bedroll" && item.Kind == "bedroll"));
        if (!trustedPreparedState && !IsCompatibleSavedMap(runtime.map, state))
        {
            runtime.Dispose();
            throw new InvalidDataException("The private-world map does not match deterministic regeneration.");
        }

        runtime.map = state.Map;
        runtime.eventHistoryFloor = state.EventHistoryFloor;
        runtime.historyArchiveHead = state.HistoryArchiveHead;
        runtime.checkpointSchemaVersion = StateSchemaVersion;
        runtime.jevEnabled = state.JevEnabled ?? true;
        runtime.jevPolicyRevision = state.JevPolicyRevision;
        runtime.founderSetup = state.FounderSetup;
        runtime.society.Dispose();
        runtime.society = SocietyWorldRuntime.Restore(
            state.Society,
            providerFactory);
        runtime.contentRegistry = ContentPackageRegistry.Restore(state.Content);
        runtime.worldContent = state.WorldContent!;
        runtime.worldSimulation = state.WorldSimulation! with { CropBuilds = state.WorldSimulation.CropBuilds ?? [] };
        runtime.fertility = new LandFertility(runtime.map, state.WorldSeed);
        runtime.fields = state.Fields!.OrderBy(field => field.Position.Y)
            .ThenBy(field => field.Position.X).ToList();
        runtime.towns = state.Towns!.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
        runtime.roadTiles = state.RoadTiles!.ToHashSet();
        runtime.bridges = state.Bridges!.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
        runtime.bridgeTraffic = state.BridgeTraffic!;
        runtime.conversations = state.Conversations!.ToList();
        runtime.conversationBudgets = state.ConversationBudgets!.ToList();
        runtime.businessTrades = state.BusinessTrades!.ToList();
        runtime.ApplyBridgeDecks();
        runtime.assetReservations = WorldAssetReservationLedger.Restore(state.AssetReservations);
        runtime.survivalState = state.Survival;
        runtime.council = state.Council;
        runtime.worldSystems = state.WorldSystems!;
        RegionalWeatherRules.ValidateMap(runtime.worldSystems, runtime.map);
        runtime.inhabitants.Clear();
        foreach (var inhabitant in state.Inhabitants)
        {
            runtime.inhabitants.Add(inhabitant.InhabitantId, inhabitant);
        }
        runtime.deceasedInhabitants.Clear();
        foreach (var inhabitant in state.DeceasedInhabitants ?? [])
        {
            runtime.deceasedInhabitants.Add(inhabitant.InhabitantId, inhabitant);
        }

        runtime.resources.Clear();
        foreach (var resource in state.Resources)
        {
            runtime.resources.Add(resource.ResourceId, resource.State);
        }
        runtime.knowledge = state.Knowledge!;

        runtime.instructionsByIdempotency.Clear();
        runtime.instructionReceipts.Clear();
        runtime.completedInstructionIds.Clear();
        foreach (var instruction in state.Instructions ?? [])
        {
            if (!runtime.instructionsByIdempotency.TryAdd(instruction.IdempotencyKey, instruction))
            {
                runtime.Dispose();
                throw new InvalidDataException("The private-world instruction idempotency keys are duplicated.");
            }

            runtime.instructionReceipts.Add(
                instruction.IdempotencyKey,
                new OwnerInstructionReceipt(
                    instruction.InstructionId,
                    instruction.IdempotencyKey,
                    instruction.SubmittedTick,
                    instruction.RunEpoch,
                    instruction.SubmissionSequence));
        }

        foreach (var completedInstructionId in state.CompletedInstructionIds ?? [])
        {
            runtime.completedInstructionIds.Add(completedInstructionId);
        }

        runtime.nextInstructionSequence = runtime.instructionsByIdempotency.Count == 0
            ? 1
            : checked(runtime.instructionsByIdempotency.Values.Max(item => item.SubmissionSequence) + 1);

        runtime.events.Clear();
        runtime.events.AddRange(state.Events);
        runtime.nextEventId = runtime.events.Count == 0 ? checked(runtime.eventHistoryFloor + 1) : checked(runtime.events[^1].EventId + 1);
        if (!trustedPreparedState)
        {
            runtime.SuspendRestoredConversations();
            runtime.RepairSavedRoadFootprints();
            runtime.Validate();
        }
        return runtime;
    }

    public void Dispose()
    {
        foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
        foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
        CancelIdentityMoments();
        foreach (var id in pendingConversationTurns.Keys.ToArray()) CancelPendingConversationTurn(id,
            AgentConversationInterruption.Disconnected);
        society.Dispose();
        gate.Dispose();
        tickGate.Dispose();
    }

    public void PersistCheckpoint(Func<PrivateWorldRuntimeState, PrivateWorldRuntimeState> persist, bool resumeOnSuccess = false)
    {
        ArgumentNullException.ThrowIfNull(persist);
        gate.Wait();
        try
        {
            if (resumeOnSuccess && society.Checkpoint.IsPaused)
            {
                // Keep the live world paused throughout the write. A failed write
                // discards this proposal, including its resume event and epoch.
                using var proposed = RestoreCore(CaptureState(), providerFactory, maxCognitionDispatchPerCycle, trustedPreparedState: true);
                proposed.Resume();
                var persisted = persist(proposed.CaptureState());
                if (persisted.HistoryArchiveHead != proposed.historyArchiveHead)
                {
                    using var compacted = Restore(persisted, providerFactory, maxCognitionDispatchPerCycle);
                    CommitPreparedTick(compacted);
                }
                else CommitPreparedTick(proposed);
                return;
            }
            var saved = persist(CaptureState());
            if (saved.HistoryArchiveHead != historyArchiveHead)
            {
                using var compacted = Restore(saved, providerFactory, maxCognitionDispatchPerCycle);
                CommitPreparedTick(compacted);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private PrivateWorldRuntimeState CaptureState() => new(
        checkpointSchemaVersion,
        worldSeed,
        map,
        society.ExportState(),
        inhabitants.Values.OrderBy(item => item.InhabitantId, StringComparer.Ordinal).ToArray(),
        resources.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PlaytestResourceState(item.Key, item.Value)).ToArray(),
        events.ToArray(),
        instructionsByIdempotency.Values
            .OrderBy(item => item.SubmissionSequence)
            .ToArray(),
        completedInstructionIds.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
        contentRegistry.ExportState(),
        worldSystems,
        worldContent,
        worldSimulation,
        assetReservations.ExportState(), eventHistoryFloor, historyArchiveHead, survivalState, council,
        deceasedInhabitants.Count == 0 ? null : deceasedInhabitants.Values.OrderBy(item => item.InhabitantId, StringComparer.Ordinal).ToArray(),
        jevPolicyRevision == 0 && jevEnabled ? null : jevEnabled, jevPolicyRevision, founderSetup,
        geographyOptions, towns.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), knowledge,
        RoadTiles, Bridges, bridgeTraffic, fields.ToArray(),
        conversations.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        conversationBudgets.OrderBy(item => item.AgentId, StringComparer.Ordinal).ToArray(), BusinessTrades);

    private void AppendEvent(string kind, string detail)
    {
        GridPoint? position = null;
        for (var length = detail.Length; length > 0; length = detail.LastIndexOf(':', length - 1))
        {
            var prefix = detail[..length];
            if (inhabitants.TryGetValue(prefix, out var living))
            {
                position = living.Position;
                break;
            }
            if (deceasedInhabitants.TryGetValue(prefix, out var deceased))
            {
                position = deceased.LastPhysical.Position;
                break;
            }
        }
        events.Add(new PlaytestWorldEvent(nextEventId++, WorldTick, kind, detail, position));
    }

}
