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

public sealed record PlaytestInhabitantState(
    string InhabitantId,
    GridPoint Position,
    int HungerBasisPoints,
    int MoveWaitTicks,
    string Personality,
    string Aspiration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastDecisionContext = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementProject? Project = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SurvivalCondition? Survival = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementLesson? Lesson = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementParenthood? Parenthood = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementProficiency? Proficiency = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SettlementSocialStanding>? SocialStanding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PlaytestPrivateThought>? RecentThoughts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int TravelCooldownTicks = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementExploration? Exploration = null);

public sealed record PlaytestPrivateThought(long WorldTick, string Text);

public sealed record PlaytestResourceState(string ResourceId, ResourceState State);

public sealed record PlaytestDeceasedInhabitantState(
    string InhabitantId,
    long DeathTick,
    int AgeAtDeath,
    PlaytestInhabitantState LastPhysical);

public sealed record PlaytestWorldEvent(
    long EventId,
    long WorldTick,
    string Kind,
    string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GridPoint? Position = null);

public sealed record FounderSetupState(IReadOnlyList<string> FounderIds, bool Started);

public sealed record PrivateWorldRuntimeState(
    int SchemaVersion,
    string WorldSeed,
    SeededMap Map,
    SocietyWorldRuntimeState Society,
    IReadOnlyList<PlaytestInhabitantState> Inhabitants,
    IReadOnlyList<PlaytestResourceState> Resources,
    IReadOnlyList<PlaytestWorldEvent> Events,
    IReadOnlyList<OwnerQueuedInstruction>? Instructions = null,
    IReadOnlyList<string>? CompletedInstructionIds = null,
    ContentRegistryState? Content = null,
    WorldSystemsState? WorldSystems = null,
    DeclarativeWorldContentState? WorldContent = null,
    WorldContentSimulationState? WorldSimulation = null,
    WorldAssetReservationLedgerState? AssetReservations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long EventHistoryFloor = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HistoryArchiveHead = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementSurvivalState? Survival = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementCouncil? Council = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PlaytestDeceasedInhabitantState>? DeceasedInhabitants = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? JevEnabled = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long JevPolicyRevision = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FounderSetupState? FounderSetup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GeographyOptions? Geography = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TownRuntimeState>? Towns = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PrivateWorldKnowledgeState? Knowledge = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<GridPoint>? RoadTiles = null);

public sealed record PrivateWorldStepResult(
    bool Advanced,
    string Outcome,
    long WorldTick,
    IReadOnlyList<SocietyCognitionDispatchResult> Decisions,
    IReadOnlyList<PlaytestWorldEvent> Events)
{
    public IReadOnlyList<PrivateWorldMemoryCompactionTransition> MemoryCompactionTransitions { get; init; } = [];
}

/// <summary>
/// The live private-world composition for the first complete single-player
/// alpha. Society owns identity, lifecycle, relationships, and inventory;
/// this composition owns the map, physical positions/needs, cognition cadence,
/// and the world-facing event stream. It is intentionally separate from the
/// legacy one-actor owner fixture while the owner protocol is migrated.
/// </summary>
public sealed partial class PrivateWorldRuntime : IDisposable
{
    public const int StateSchemaVersion = 25;
    private const int MaximumRecentThoughts = 8;
    private const string HouseholdId = "household:camp-alpha";
    private const string SecondHouseholdId = "household:camp-beta";
    public const int RequiredFounders = 4;
    private const string FoodLotId = "food:camp-alpha";
    private const long CognitionReevaluationIntervalTicks = 30;
    private const int ResourceInteractionRange = 1;
    private const int HarvestFoodYield = 4;
    private static readonly string LegacyStarterDigest = StarterContent.Create().PackageDigest;
    private static readonly string House1x1DefinitionId = HouseContent.House1x1().CanonicalId;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim tickGate = new(1, 1);
    private string worldSeed;
    private GeographyOptions? geographyOptions;
    private readonly Func<string, IDecisionProvider>? providerFactory;
    private readonly double minimumCognitionConfidence;
    private readonly int maxCognitionDispatchPerCycle;
    private SeededMap map;
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
    private readonly List<PrivateWorldMemoryCompactionTransition> memoryCompactionTransitions = [];

    private sealed record HostedDecisionOutcome(CognitionDecisionResponse? Response, string? Failure);
    private sealed record PendingHostedDecision(
        CognitionDecisionRequest Request,
        Task<HostedDecisionOutcome> Task,
        CancellationTokenSource Cancellation);

    public PrivateWorldRuntime(
        string worldSeed,
        Func<string, IDecisionProvider>? providerFactory = null,
        int maxCognitionQueueLength = 64,
        int maxCognitionDispatchPerCycle = 4,
        double minimumCognitionConfidence = 0.5,
        WorldStartPace startPace = WorldStartPace.Legacy,
        GeographyOptions? geographyOptions = null)
        : this(worldSeed, providerFactory, maxCognitionQueueLength, maxCognitionDispatchPerCycle,
            minimumCognitionConfidence, startPace, geographyOptions, preparedMap: null)
    {
    }

    private PrivateWorldRuntime(
        string worldSeed,
        Func<string, IDecisionProvider>? providerFactory,
        int maxCognitionQueueLength,
        int maxCognitionDispatchPerCycle,
        double minimumCognitionConfidence,
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

        if (double.IsNaN(minimumCognitionConfidence) ||
            double.IsInfinity(minimumCognitionConfidence) ||
            minimumCognitionConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCognitionConfidence));
        }

        this.minimumCognitionConfidence = minimumCognitionConfidence;
        this.maxCognitionDispatchPerCycle = maxCognitionDispatchPerCycle;
        contentRegistry = new ContentPackageRegistry();
        worldContent = new DeclarativeWorldContentState([], []);
        worldSimulation = WorldContentSimulationState.Empty;
        assetReservations = new WorldAssetReservationLedger();
        map = preparedMap ?? (startPace == WorldStartPace.FounderSetup
            ? geographyOptions is null
                ? BaseCampMapGenerator.Generate(this.worldSeed, includeLegacyBedroll)
                : GeneratedCampMapGenerator.Generate(geographyOptions, includeLegacyBedroll)
            : SeededMapGenerator.Generate(this.worldSeed, includeLegacyBedroll));
        worldSystems = CreateWorldSystems(this.worldSeed, map, startPace);
        society = CreateSociety(
            this.worldSeed,
            providerFactory,
            maxCognitionQueueLength,
            maxCognitionDispatchPerCycle,
            minimumCognitionConfidence,
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

        AppendEvent("world_created", $"{this.worldSeed}:inhabitants:{inhabitants.Count}");
        if (towns.Count > 0) AppendEvent("town_founding_started", TownBorderRules.FirstTownId);
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
        int maxCognitionDispatchPerCycle = 4,
        double minimumCognitionConfidence = 0.5)
        => RestoreCore(state, providerFactory, maxCognitionDispatchPerCycle,
            minimumCognitionConfidence, trustedPreparedState: false);

    private static PrivateWorldRuntime RestoreCore(
        PrivateWorldRuntimeState state,
        Func<string, IDecisionProvider>? providerFactory,
        int maxCognitionDispatchPerCycle,
        double minimumCognitionConfidence,
        bool trustedPreparedState)
    {
        if (!trustedPreparedState) ValidateStateForCodec(state);
        var runtime = new PrivateWorldRuntime(
            state.WorldSeed,
            providerFactory,
            state.Society.Cognition.MaxQueueLength,
            maxCognitionDispatchPerCycle,
            minimumCognitionConfidence,
            state.FounderSetup is null ? WorldStartPace.Legacy : WorldStartPace.FounderSetup,
            state.Geography,
            trustedPreparedState ? state.Map : null,
            includeLegacyBedroll: state.Map.CampObjects.Any(item => item.Id == "bedroll" && item.Kind == "bedroll"));
        if (!trustedPreparedState && !IsCompatibleSavedMap(runtime.map, state))
        {
            runtime.Dispose();
            throw new InvalidDataException("The private-world map does not match deterministic regeneration.");
        }

        // Older generated checkpoints did not serialize the route-topology flag.
        // Geography already binds that choice, while the v1 terrain manifest
        // remains byte-compatible with existing saves.
        runtime.map = state.Geography is null ? state.Map :
            state.Map with
            {
                WrapsEastWest = state.Geography.WrapEastWest,
                // Schema 19 saved only climate and the flattened terrain. Its
                // deterministic generator still recovers the original layers
                // without changing the v1 manifest or resource topology.
                ElevationLevels = state.Map.ElevationLevels ?? runtime.map.ElevationLevels,
                HydrologyKinds = state.Map.HydrologyKinds ?? runtime.map.HydrologyKinds,
                SurfaceKinds = state.Map.SurfaceKinds ?? runtime.map.SurfaceKinds,
                VegetationKinds = state.Map.VegetationKinds ?? runtime.map.VegetationKinds,
            };
        runtime.eventHistoryFloor = state.EventHistoryFloor;
        runtime.historyArchiveHead = state.HistoryArchiveHead;
        runtime.checkpointSchemaVersion = StateSchemaVersion;
        runtime.jevEnabled = state.JevEnabled ?? true;
        runtime.jevPolicyRevision = state.JevPolicyRevision;
        runtime.founderSetup = state.FounderSetup;
        runtime.society.Dispose();
        runtime.society = SocietyWorldRuntime.Restore(
            state.Society,
            providerFactory,
            minimumCognitionConfidence);
        runtime.contentRegistry = ContentPackageRegistry.Restore(state.Content);
        runtime.worldContent = state.WorldContent ?? RebuildWorldContent(runtime.contentRegistry.ExportState());
        runtime.worldSimulation = state.WorldSimulation is null
            ? WorldContentSimulationState.Empty
            : state.WorldSimulation with { CropBuilds = state.WorldSimulation.CropBuilds ?? [] };
        runtime.towns = (state.Towns ?? MigrateTowns(state)).OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
        runtime.roadTiles = (state.RoadTiles ?? []).ToHashSet();
        runtime.assetReservations = WorldAssetReservationLedger.Restore(state.AssetReservations);
        runtime.survivalState = state.Survival;
        runtime.council = state.Council;
        runtime.worldSystems = state.WorldSystems is null
            ? AdvanceWorldSystemsTo(
                CreateWorldSystems(state.WorldSeed, state.Map),
                state.Society.Society.WorldTick)
            : state.WorldSystems;
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
        runtime.knowledge = state.Knowledge ?? PrivateWorldKnowledgeState.Empty;

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
        if (!trustedPreparedState) runtime.Validate();
        return runtime;
    }

    public ValueTask<PrivateWorldStepResult> AdvanceOneTickAsync(CancellationToken cancellationToken = default) =>
        AdvanceOneTickAsync(null, cancellationToken);

    public async ValueTask<PrivateWorldStepResult> AdvanceOneTickAsync(
        Func<bool>? commitPermitted, CancellationToken cancellationToken = default) =>
        await AdvanceOneTickCoreAsync(false, commitPermitted, cancellationToken).ConfigureAwait(false);

    /// <summary>Playable-host path: hosted decisions run between ticks, never inside a tick transaction.</summary>
    public ValueTask<PrivateWorldStepResult> AdvanceOneTickNonBlockingAsync(
        Func<bool>? commitPermitted = null, CancellationToken cancellationToken = default) =>
        AdvanceOneTickCoreAsync(true, commitPermitted, cancellationToken);

    private async ValueTask<PrivateWorldStepResult> AdvanceOneTickCoreAsync(
        bool deferHosted, Func<bool>? commitPermitted, CancellationToken cancellationToken)
    {
        await tickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PrivateWorldRuntimeState baseline;
            long baselineEventId;
            PendingHostedDecision[] completed = [];
            PendingWillDecision[] completedWills = [];
            string[] activeWillIds = [];
            IReadOnlyDictionary<string, string> inactiveWillReasons = new Dictionary<string, string>();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (society.Checkpoint.IsPaused)
                {
                    return new PrivateWorldStepResult(false, "paused", WorldTick, [], []);
                }
                if (deferHosted)
                {
                    foreach (var (id, pending) in pendingHosted.ToArray())
                    {
                        if (!society.Checkpoint.Inhabitants.Any(person => person.Id == id && person.Status == SocietyInhabitantStatus.Active) ||
                            society.CurrentProviderEpoch(id) != pending.Request.ProviderEpoch ||
                            society.Checkpoint.RunEpoch != pending.Request.Observation.RunEpoch)
                        {
                            CancelPendingHosted(id);
                        }
                    }
                    foreach (var (id, pending) in pendingWills.ToArray())
                    {
                        var estate = society.Checkpoint.Estates.FirstOrDefault(item => item.Id == id);
                        if (estate is null || estate.WillStatus != "pending")
                        {
                            CancelPendingWill(id);
                            continue;
                        }
                        if (society.Checkpoint.RunEpoch != pending.Request.Observation.RunEpoch)
                        {
                            CancelPendingWill(id, "run_epoch_changed");
                            continue;
                        }
                        try
                        {
                            var currentProvider = providerFactory?.Invoke(estate.DeceasedId) ?? new DeterministicDecisionProvider();
                            if (currentProvider.KindFor(pending.Request.Observation) != DecisionProviderKind.LargeLanguageModel ||
                                currentProvider.ProviderEpoch != pending.Request.ProviderEpoch)
                                CancelPendingWill(id, "provider_changed");
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            CancelPendingWill(id, "provider_unavailable");
                        }
                    }
                    completed = pendingHosted.Values.Where(item => item.Task.IsCompleted).ToArray();
                    completedWills = pendingWills.Values.Where(item => item.Task.IsCompleted).ToArray();
                    activeWillIds = pendingWills.Keys.ToArray();
                    inactiveWillReasons = new Dictionary<string, string>(pendingWillCancellationReasons, StringComparer.Ordinal);
                }
                baseline = CaptureState();
                baselineEventId = nextEventId;
            }
            finally
            {
                gate.Release();
            }

            // World mutations operate on an isolated proposed tick. In the
            // playable path, external cognition itself runs between ticks.
            // Readers and owner controls use the last committed world.
            // The baseline was captured from this committed runtime under the
            // gate. Clone its mutable systems without regenerating or
            // revalidating millions of immutable terrain tiles each tick.
            using var proposed = RestoreCore(baseline, providerFactory,
                maxCognitionDispatchPerCycle, minimumCognitionConfidence,
                trustedPreparedState: true);
            var result = await proposed.AdvancePreparedTickAsync(deferHosted, completed, completedWills,
                activeWillIds, inactiveWillReasons, cancellationToken).ConfigureAwait(false);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (commitPermitted is not null && !commitPermitted())
                {
                    return new PrivateWorldStepResult(false, "waiting_for_client", WorldTick, [], []);
                }
                if (nextEventId != baselineEventId || WorldTick != baseline.Society.Society.WorldTick || historyArchiveHead != baseline.HistoryArchiveHead)
                {
                    return new PrivateWorldStepResult(false, "tick_superseded_by_owner_change", WorldTick, [], []);
                }
                if (!result.Advanced)
                {
                    return result;
                }
                CommitPreparedTick(proposed);
                if (deferHosted)
                {
                    foreach (var id in pendingWills.Keys.Where(id =>
                                 society.Checkpoint.Estates.All(estate => estate.Id != id || estate.WillStatus != "pending"))
                             .ToArray())
                        CancelPendingWill(id);
                    foreach (var item in completed)
                    {
                        pendingHosted.Remove(item.Request.Observation.InhabitantId);
                        item.Cancellation.Dispose();
                    }
                    foreach (var item in completedWills)
                    {
                        pendingWills.Remove(item.EstateId);
                        item.Cancellation.Dispose();
                    }
                    foreach (var id in inactiveWillReasons.Keys)
                        pendingWillCancellationReasons.Remove(id);
                    if (commitPermitted is null || commitPermitted()) StartWillDecisions();
                    if (commitPermitted is null || commitPermitted()) StartHostedDecisions();
                }
                return result with { Events = events.Where(item => item.EventId >= baselineEventId).ToArray() };
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            tickGate.Release();
        }
    }

    private void StartHostedDecisions()
    {
        var capacity = Math.Max(0, maxCognitionDispatchPerCycle - pendingHosted.Count);
        if (capacity == 0) return;
        foreach (var preview in society.PreviewHostedRequests(pendingHosted.Keys.ToHashSet(StringComparer.Ordinal)).Take(capacity))
        {
            var cancellation = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        return new HostedDecisionOutcome(
                            await preview.DecideAsync(cancellation.Token).ConfigureAwait(false), null);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        return new HostedDecisionOutcome(null, "provider_cancelled");
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        if (attempt == 1)
                            return new HostedDecisionOutcome(null, $"provider_failure:{exception.GetType().Name}");
                    }
                }
                return new HostedDecisionOutcome(null, "provider_failure:retry_exhausted");
            });
            pendingHosted.Add(preview.InhabitantId, new PendingHostedDecision(preview.Request, task, cancellation));
            AppendEvent("hosted_decision_started", preview.InhabitantId);
        }
    }

    private void CancelPendingHosted(string inhabitantId)
    {
        if (!pendingHosted.Remove(inhabitantId, out var pending)) return;
        pending.Cancellation.Cancel();
        _ = pending.Task.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
    }

    public void CancelPendingHostedDecisions()
    {
        gate.Wait();
        try
        {
            foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
            foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
        }
        finally { gate.Release(); }
    }

    private void CommitPreparedTick(PrivateWorldRuntime proposed)
    {
        // Transfer the committed society; disposing the proposal retires the old one.
        (society, proposed.society) = (proposed.society, society);
        worldSeed = proposed.worldSeed;
        map = proposed.map;
        geographyOptions = proposed.geographyOptions;
        contentRegistry = proposed.contentRegistry;
        worldSystems = proposed.worldSystems;
        survivalState = proposed.survivalState;
        council = proposed.council;
        worldContent = proposed.worldContent;
        worldSimulation = proposed.worldSimulation;
        assetReservations = proposed.assetReservations;
        inhabitants = proposed.inhabitants;
        deceasedInhabitants = proposed.deceasedInhabitants;
        resources = proposed.resources;
        knowledge = proposed.knowledge;
        instructionsByIdempotency = proposed.instructionsByIdempotency;
        instructionReceipts = proposed.instructionReceipts;
        completedInstructionIds = proposed.completedInstructionIds;
        events = proposed.events;
        nextEventId = proposed.nextEventId;
        eventHistoryFloor = proposed.eventHistoryFloor;
        historyArchiveHead = proposed.historyArchiveHead;
        checkpointSchemaVersion = proposed.checkpointSchemaVersion;
        jevEnabled = proposed.jevEnabled;
        jevPolicyRevision = proposed.jevPolicyRevision;
        founderSetup = proposed.founderSetup;
        towns = proposed.towns;
        roadTiles = proposed.roadTiles;
        nextInstructionSequence = proposed.nextInstructionSequence;
    }

    /// <summary>Rewind this paused world to a validated checkpoint of the same world.</summary>
    public void LoadPausedCheckpoint(PrivateWorldRuntimeState checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(checkpoint.WorldSeed, worldSeed, StringComparison.Ordinal))
            throw new InvalidDataException("A checkpoint belongs to a different world.");
        tickGate.Wait();
        try
        {
            gate.Wait();
            try
            {
                if (!society.Checkpoint.IsPaused)
                    throw new InvalidOperationException("Pause the world before loading a checkpoint.");
                using var restored = Restore(checkpoint, providerFactory,
                    maxCognitionDispatchPerCycle, minimumCognitionConfidence);
                // Loading never resumes a world implicitly, even if the saved
                // checkpoint was taken while it was running.
                restored.Pause();
                foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
                foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
                CommitPreparedTick(restored);
            }
            finally { gate.Release(); }
        }
        finally { tickGate.Release(); }
    }

    /// <summary>Replace the selected world while the current world is paused.</summary>
    public void SwitchPausedWorld(PrivateWorldRuntimeState checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        tickGate.Wait();
        try
        {
            gate.Wait();
            try
            {
                if (!society.Checkpoint.IsPaused)
                    throw new InvalidOperationException("Pause the world before selecting another world.");
                using var restored = Restore(checkpoint, providerFactory,
                    maxCognitionDispatchPerCycle, minimumCognitionConfidence);
                restored.Pause();
                foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
                foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
                CommitPreparedTick(restored);
            }
            finally { gate.Release(); }
        }
        finally { tickGate.Release(); }
    }

    private async ValueTask<PrivateWorldStepResult> AdvancePreparedTickAsync(
        bool deferHosted, IReadOnlyList<PendingHostedDecision> completed,
        IReadOnlyList<PendingWillDecision> completedWills, IReadOnlyList<string> activeWillIds,
        IReadOnlyDictionary<string, string> inactiveWillReasons,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (society.Checkpoint.IsPaused)
            {
                return new PrivateWorldStepResult(false, "paused", WorldTick, [], []);
            }

            var startingEvent = events.Count;
            var targetTick = checked(WorldTick + 1);
            StageSettlementContent();
            StageHouseContent();
            StageWarehouseContent();
            StageFarmContent();
            StageBlacksmithContent();
            StageHouseCookingContent();
            StageForestryContent();
            var readyPackages = contentRegistry.GetActivationCandidates(targetTick);
            var reservationPreview = WorldAssetReservationLedger.Restore(
                assetReservations.ExportState(),
                assetReservations.Policy);
            foreach (var package in readyPackages)
            {
                var reservation = reservationPreview.TryReservePackage(
                    package.Manifest.PackageId,
                    package.Manifest.AssetReservations ?? [],
                    targetTick);
                if (!reservation.IsSuccess)
                {
                    return new PrivateWorldStepResult(
                        false,
                        $"asset_reservation_rejected:{reservation.FailureCode ?? "invalid"}",
                        WorldTick,
                        [],
                        []);
                }
            }

            assetReservations = reservationPreview;
            society.AdvanceTo(targetTick);
            if (deferHosted) await ProcessWillDecisionsAsync(completedWills, activeWillIds, inactiveWillReasons);
            var previousClimate = worldSystems.Climate;
            worldSystems = WorldSystemsRules.AdvanceOneTick(worldSystems);
            SyncEcologyResourceStates();
            var campPosition = WeatherAnchor;
            var previousCampWeather = WeatherRules.At(worldSystems with
            { WorldTick = previousClimate.WorldTick, Climate = previousClimate },
                campPosition, map.Height, WeatherRules.RegionClimate(map, campPosition));
            var campWeather = WeatherAt(campPosition);
            if (previousClimate.Season != worldSystems.Climate.Season || previousCampWeather != campWeather)
            {
                AppendEvent(
                    "weather_changed",
                    $"{worldSystems.Climate.Season.ToString().ToLowerInvariant()}:{campWeather.ToString().ToLowerInvariant()}");
            }

            var activatedWorldContent = worldContent;
            foreach (var package in readyPackages)
            {
                activatedWorldContent = ContentDefinitionPayloadCodec.ApplyPackage(
                    activatedWorldContent,
                    package.Manifest);
            }

            foreach (var activated in contentRegistry.ActivateReady(targetTick))
            {
                AppendEvent("content_activated", activated.Manifest.PackageId);
                if (activated.Manifest.PackageId == SettlementContent.PackageId)
                {
                    AddSettlementResources();
                }
                if (activated.Manifest.Definitions.Any(definition => !string.IsNullOrWhiteSpace(definition.PayloadJson)))
                {
                    AppendEvent(
                        "content_definitions_activated",
                        $"{activated.Manifest.PackageId}:buildings={activatedWorldContent.Buildings.Count}:recipes={activatedWorldContent.Recipes.Count}");
                }
            }
            worldContent = activatedWorldContent;
            if (targetTick == 1 && contentRegistry.ExportState().Packages.Any(package =>
                    package.Manifest.PackageId == SettlementContent.PackageId &&
                    package.Lifecycle == ContentPackageLifecycle.Active && package.ActivationTick == 0))
                AddSettlementResources();
            CancelUnavailableWorkers();
            ProcessProduction(targetTick);
            ProcessCropBuilds(targetTick);

            AdvanceSettlementSurvival();
            MaintainSettlementTrades();
            DrainNeeds();
            RemoveDeadPhysicalState();
            AdvanceSettlementCouncil();
            MaintainLessons();
            MaintainPartnerships();
            MaintainParenthood();
            MaintainDependentCare();
            EnqueueDueCognition();
            var deferredDecisions = new List<SocietyCognitionDispatchResult>();
            if (deferHosted)
            {
                foreach (var item in completed)
                {
                    var id = item.Request.Observation.InhabitantId;
                    if (!inhabitants.TryGetValue(id, out var physical)) continue;
                    var outcome = await item.Task.ConfigureAwait(false);
                    var legal = CreateCandidates(id, physical).Select(candidate => candidate.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    var decision = society.CompleteDeferredCognition(item.Request, outcome.Response,
                        outcome.Failure, legal);
                    if (decision is not null)
                    {
                        if (decision.Admission.Accepted && !decision.Admission.FellBack &&
                            outcome.Response is
                            {
                                Provider: DecisionProviderKind.LargeLanguageModel,
                                PrivateThought: { } thought
                            } && inhabitants.TryGetValue(id, out var thinking))
                        {
                            inhabitants[id] = thinking with
                            {
                                RecentThoughts = (thinking.RecentThoughts ?? [])
                                    .Append(new PlaytestPrivateThought(targetTick, thought))
                                    .TakeLast(MaximumRecentThoughts).ToArray(),
                            };
                            checkpointSchemaVersion = StateSchemaVersion;
                        }
                        if (decision.Admission.Accepted && !decision.Admission.FellBack &&
                            outcome.Response is
                            {
                                Provider: DecisionProviderKind.LargeLanguageModel,
                                ChosenName: { } chosenName
                            } && society.Checkpoint.GetInhabitant(id).NeedsName)
                        {
                            society.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, id, chosenName));
                            AppendEvent("agent_named", id);
                        }
                        deferredDecisions.Add(decision);
                        AppendEvent("hosted_decision_completed", $"{id}:{decision.Admission.Outcome}");
                    }
                    else AppendEvent("hosted_decision_discarded", id);
                }
            }
            var dispatch = deferHosted
                ? await society.DispatchDeterministicCognitionAsync(cancellationToken).ConfigureAwait(false)
                : await society.DispatchCognitionAsync(cancellationToken).ConfigureAwait(false);
            var decisions = deferredDecisions.Concat(dispatch.Decisions)
                .OrderBy(item => item.InhabitantId, StringComparer.Ordinal).ToArray();
            foreach (var decision in decisions)
            {
                ApplyDecision(decision);
            }
            var waiting = deferHosted ? society.PendingHostedInhabitantIds() : new HashSet<string>(StringComparer.Ordinal);
            ApplyContinuingIntentions(decisions.Select(item => item.InhabitantId).Concat(waiting));
            if (deferHosted) ApplySafeRoutinesWhileWaiting(waiting);

            AppendEvent("tick_advanced", targetTick.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var newEvents = events.Skip(startingEvent).ToArray();
            return new PrivateWorldStepResult(true, "advanced", targetTick, decisions, newEvents)
            {
                MemoryCompactionTransitions = memoryCompactionTransitions.ToArray(),
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public OwnerInstructionReceipt SubmitInstruction(OwnerInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInstructionRequest(request);
        gate.Wait();
        try
        {
            var targetId = request.TargetInhabitantId.Trim();
            var target = society.Checkpoint.Inhabitants.SingleOrDefault(item => item.Id == targetId);
            if (target is null || target.Status != SocietyInhabitantStatus.Active)
            {
                throw new ArgumentException($"No active inhabitant with ID '{targetId}' exists.", nameof(request));
            }
            if (target.AgeBand == SocietyAgeBand.Infant)
            {
                throw new ArgumentException("Infants cannot carry out owner instructions; direct care through an adult caregiver.", nameof(request));
            }

            if (instructionsByIdempotency.TryGetValue(request.IdempotencyKey, out var existing))
            {
                if (!Matches(existing, request))
                {
                    throw new InvalidOperationException(
                        "An idempotency key cannot be reused for a different instruction request.");
                }

                return instructionReceipts[request.IdempotencyKey];
            }

            var sequence = nextInstructionSequence++;
            var instruction = new OwnerQueuedInstruction(
                $"private-instruction-{sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)}",
                request.IdempotencyKey.Trim(),
                request.IssuerId.Trim(),
                targetId,
                request.Kind,
                request.Text.Trim(),
                WorldTick,
                society.Checkpoint.RunEpoch,
                sequence,
                OwnerInstructionState.Queued);
            instructionsByIdempotency.Add(instruction.IdempotencyKey, instruction);
            var receipt = new OwnerInstructionReceipt(
                instruction.InstructionId,
                instruction.IdempotencyKey,
                instruction.SubmittedTick,
                instruction.RunEpoch,
                sequence);
            instructionReceipts.Add(instruction.IdempotencyKey, receipt);
            AppendEvent("instruction_queued", $"{instruction.InstructionId}:{ToWireValue(instruction.Kind)}");
            return receipt;
        }
        finally
        {
            gate.Release();
        }
    }

    public static ContentResolutionResult PreviewContent(
        IEnumerable<ContentPackageManifest> availablePackages,
        IEnumerable<string> rootPackageIds) =>
        ContentPackageResolver.Resolve(availablePackages, rootPackageIds);

    public static ContentPreviewResult PreviewWorldContent(
        IEnumerable<ContentPackageManifest> availablePackages,
        IEnumerable<string> rootPackageIds,
        DeclarativeWorldContentState? baseWorldContent = null) =>
        ContentPackagePreview.Run(availablePackages, rootPackageIds, baseWorldContent);

    public ContentResolutionResult ResolveContent(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        gate.Wait();
        try
        {
            var available = contentRegistry.ExportState().Packages
                .Select(package => package.Manifest)
                .ToArray();
            return ContentPackageResolver.Resolve(available, [packageId]);
        }
        finally
        {
            gate.Release();
        }
    }

    public ContentPackageRecord ProposeContent(ContentPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        gate.Wait();
        try
        {
            var record = contentRegistry.Propose(manifest, WorldTick);
            AppendEvent("content_proposed", manifest.PackageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool StageStarterContent()
    {
        gate.Wait();
        try
        {
            if (society.Checkpoint.IsPaused || contentRegistry.ExportState().Packages
                .Any(package => package.Manifest.PackageId == StarterContent.PackageId))
            {
                // Never undo an owner's rollback or quarantine, or mutate a paused save.
                return false;
            }

            var manifest = StarterContent.Create();
            _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
            var resolution = ContentPackageResolver.Resolve([manifest], [manifest.PackageId]);
            contentRegistry.Propose(manifest, WorldTick);
            contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
            contentRegistry.Approve(manifest.PackageId, WorldTick);
            contentRegistry.Stage(manifest.PackageId, WorldTick);
            AppendEvent("starter_content_staged", manifest.PackageId);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Install trusted shipped content before the first paused Town layout is accepted.</summary>
    public void InitializeFirstTownContent()
    {
        gate.Wait();
        try
        {
            if (geographyOptions is null || founderSetup is not { Started: false } ||
                !society.Checkpoint.IsPaused || WorldTick != 0 ||
                contentRegistry.ExportState().Packages.Count != 0)
                throw new InvalidOperationException("Initial content is available only to a fresh paused generated world.");
            ContentPackageManifest[] manifests =
            [
                StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(),
                WarehouseContent.Create(), FarmContent.Create(), BlacksmithContent.Create(),
                HouseCookingContent.Create(),
            ];
            foreach (var manifest in manifests)
            {
                var packages = contentRegistry.ExportState().Packages;
                var resolution = ContentPackageResolver.Resolve(
                    packages.Select(package => package.Manifest).Append(manifest), [manifest.PackageId]);
                var definitions = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
                contentRegistry.Propose(manifest, 0);
                contentRegistry.Validate(manifest.PackageId, resolution, 0);
                contentRegistry.Approve(manifest.PackageId, 0);
                contentRegistry.Stage(manifest.PackageId, 0);
                var reservation = assetReservations.TryReservePackage(manifest.PackageId,
                    manifest.AssetReservations ?? [], 0);
                if (!reservation.IsSuccess)
                    throw new InvalidOperationException($"Initial content assets were rejected: {reservation.FailureCode}");
                contentRegistry.ActivateAtCreation(manifest.PackageId);
                worldContent = definitions;
                AppendEvent("initial_content_activated", manifest.PackageId);
            }
        }
        finally { gate.Release(); }
    }

    public ContentPackageRecord ValidateContent(
        string packageId,
        ContentResolutionResult resolution)
    {
        gate.Wait();
        try
        {
            var manifest = GetContentManifest(packageId);
            _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
            var record = contentRegistry.Validate(packageId, resolution, WorldTick);
            AppendEvent("content_validated", packageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public ContentPackageRecord ApproveContent(string packageId)
    {
        gate.Wait();
        try
        {
            var record = contentRegistry.Approve(packageId, WorldTick);
            AppendEvent("content_approved", packageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public ContentPackageRecord StageContent(string packageId)
    {
        gate.Wait();
        try
        {
            var manifest = GetContentManifest(packageId);
            _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
            var record = contentRegistry.Stage(packageId, WorldTick);
            AppendEvent("content_staged", packageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public BuildingPlacementResult PlaceBuilding(
        string instanceId,
        string definitionId,
        GridPoint position,
        string? householdId = null)
    {
        gate.Wait();
        try
        {
            var definition = worldContent.Buildings.SingleOrDefault(item => item.CanonicalId == definitionId);
            var assignedTown = definition is null ? null : TownForOwnerPlacement(position, definition);
            return PlaceBuildingCore(instanceId, definitionId, position, "building_placed", assignedTown?.Id, householdId);
        }
        finally
        {
            gate.Release();
        }
    }

    private BuildingPlacementResult PlaceBuildingCore(
        string instanceId,
        string definitionId,
        GridPoint position,
        string eventKind,
        string? assignedTownId = null,
        string? householdId = null,
        string? constructionOwnerId = null)
    {
        try
        {
            var normalizedInstanceId = NormalizeRequiredText(instanceId, nameof(instanceId));
            var normalizedDefinitionId = NormalizeRequiredText(definitionId, nameof(definitionId));
            ContentPackageRules.ValidateLocalId(normalizedInstanceId);
            var definition = worldContent.Buildings.SingleOrDefault(item => item.CanonicalId == normalizedDefinitionId);
            if (definition is null)
            {
                return BuildingPlacementResult.Rejected(
                    normalizedInstanceId,
                    normalizedDefinitionId,
                    position,
                    $"Building definition '{normalizedDefinitionId}' is not active.");
            }

            if (worldSimulation.Buildings.Any(item => item.InstanceId == normalizedInstanceId))
            {
                return BuildingPlacementResult.Rejected(
                    normalizedInstanceId,
                    normalizedDefinitionId,
                    position,
                    $"Building instance '{normalizedInstanceId}' already exists.");
            }

            if (assignedTownId is not null && !towns.Any(item => item.Id == assignedTownId))
                return BuildingPlacementResult.Rejected(normalizedInstanceId, normalizedDefinitionId, position,
                    "The assigned Town does not exist.");

            var isHouse = definition.Tags.Contains("house", StringComparer.Ordinal);
            var acceptsHouseholdOwner = definition.Tags.Any(IsHouseholdBuildingTag);
            if (isHouse && householdId is null || householdId is not null && !acceptsHouseholdOwner ||
                householdId is not null && !society.Checkpoint.Households.Any(item => item.Id == householdId))
                return BuildingPlacementResult.Rejected(normalizedInstanceId, normalizedDefinitionId, position,
                    "A House requires an existing household; only household work buildings may take household ownership.");

            if (definition.Tags.Contains("warehouse", StringComparer.Ordinal) &&
                (assignedTownId is null || worldSimulation.Buildings.Any(building =>
                    building.TownId == assignedTownId && worldContent.Buildings.Any(existing =>
                        existing.CanonicalId == building.DefinitionId &&
                        existing.Tags.Contains("warehouse", StringComparer.Ordinal)))))
                return BuildingPlacementResult.Rejected(normalizedInstanceId, normalizedDefinitionId, position,
                    "A Warehouse must join a Town that does not already have one.");

            if (!CanPlaceBuilding(definition, position, out var placementFailure))
            {
                return BuildingPlacementResult.Rejected(
                    normalizedInstanceId,
                    normalizedDefinitionId,
                    position,
                    placementFailure);
            }

            ApplyInventoryTransition(inventory => ConsumeQuantities(
                inventory,
                definition.BuildCosts,
                $"building:{normalizedInstanceId}",
                householdId ?? constructionOwnerId ?? HouseholdId));
            var placed = new PlacedBuilding(
                normalizedInstanceId,
                definition.CanonicalId,
                position,
                WorldTick,
                assignedTownId,
                householdId);
            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings
                    .Append(placed)
                    .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
                    .ToArray(),
                worldSimulation.ProductionJobs,
                worldSimulation.NextProductionJobSequence,
                worldSimulation.CropBuilds);
            if (assignedTownId is not null)
                AssignBuildingToTown(placed, definition);
            AppendEvent(eventKind, $"{placed.InstanceId}:{placed.DefinitionId}:{position.X},{position.Y}" +
                (householdId is null ? string.Empty : $":household={householdId}"));
            return BuildingPlacementResult.Success(placed);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return BuildingPlacementResult.Rejected(
                instanceId?.Trim() ?? string.Empty,
                definitionId?.Trim() ?? string.Empty,
                position,
                exception.Message);
        }
    }

    public ProductionStartResult StartProduction(
        string recipeId,
        string buildingInstanceId,
        string workerId)
    {
        gate.Wait();
        try
        {
            return StartProductionCore(recipeId, buildingInstanceId, workerId, "recipe_started");
        }
        finally
        {
            gate.Release();
        }
    }

    private ProductionStartResult StartProductionCore(
        string recipeId,
        string buildingInstanceId,
        string workerId,
        string eventKind)
    {
        try
        {
            var normalizedRecipeId = NormalizeRequiredText(recipeId, nameof(recipeId));
            var normalizedBuildingId = NormalizeRequiredText(buildingInstanceId, nameof(buildingInstanceId));
            var normalizedWorkerId = NormalizeRequiredText(workerId, nameof(workerId));
            var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == normalizedRecipeId);
            if (recipe is null)
            {
                return ProductionStartResult.Rejected(normalizedRecipeId, "The recipe is not active.");
            }
            if (recipe.Outputs.Any(output => output.ResourceId == "bedding"))
                return ProductionStartResult.Rejected(normalizedRecipeId, "Bedding production was retired with sleep.");

            GridPoint workPosition;
            var isFertileLandBuild = recipe.IsCrop && recipe.WorkstationBuildingId is null;
            PlacedBuilding? placed = null;
            if (isFertileLandBuild)
            {
                if (!WorldBuildSiteRules.TryGetFertileLandPosition(normalizedBuildingId, out workPosition) ||
                    !WorldContentSimulationRules.IsFertileLandPosition(map, workPosition))
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The crop must use a generated fertile-land site.");
                }

                if ((worldSimulation.CropBuilds ?? []).Any(job =>
                        job.State == WorldProductionJobState.Running &&
                        job.BuildingInstanceId == normalizedBuildingId))
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The fertile-land site is already being used.");
                }
            }
            else
            {
                placed = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == normalizedBuildingId);
                if (placed is null)
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The workstation building is not placed.");
                }

                if (recipe.WorkstationBuildingId is not null && recipe.WorkstationBuildingId != placed.DefinitionId)
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The placed building is not a valid workstation for this recipe.");
                }

                var buildingDefinition = worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId);
                var activeJobs = worldSimulation.ProductionJobs.Count(item =>
                    item.BuildingInstanceId == placed.InstanceId && item.State == WorldProductionJobState.Running);
                if (activeJobs >= buildingDefinition.Capacity)
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The workstation has no free production capacity.");
                }

                workPosition = placed.Position;
            }

            var worker = society.Checkpoint.Inhabitants.SingleOrDefault(item => item.Id == normalizedWorkerId);
            if (worker is null || worker.Status != SocietyInhabitantStatus.Active)
            {
                return ProductionStartResult.Rejected(normalizedRecipeId, "The production worker is not active.");
            }

            var workstation = placed is null ? null : worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId);
            if (placed?.HouseholdId is { } workOwner && worker.HouseholdId != workOwner)
                return ProductionStartResult.Rejected(normalizedRecipeId,
                    "Only a member of the building's household can work there.");

            if (workstation?.Tags.Any(tag => tag is "farmhouse" or "blacksmith") == true && placed?.HouseholdId is null)
                return ProductionStartResult.Rejected(normalizedRecipeId,
                    "The household workshop must be claimed before production.");

            var onSiteHouseholdRecipe = placed?.HouseholdId is not null && workstation?.Tags.Any(IsHouseholdBuildingTag) == true;
            if (onSiteHouseholdRecipe && !HasIngredientsAtBuilding(recipe.Inputs, worker.HouseholdId!, placed!.InstanceId))
                return ProductionStartResult.Rejected(normalizedRecipeId,
                    "The household building lacks the required ingredients in its on-site stock.");

            if (!inhabitants.TryGetValue(normalizedWorkerId, out var physical) || physical.Position != workPosition)
            {
                return ProductionStartResult.Rejected(normalizedRecipeId, "The worker must be standing at the build site.");
            }

            var jobId = $"production-{worldSimulation.NextProductionJobSequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)}";
            var completionTick = checked(WorldTick + recipe.DurationTicks);
            IReadOnlyList<string> reservationIds = [];
            ApplyInventoryTransition(inventory =>
            {
                var reserved = ReserveQuantities(
                    inventory,
                    recipe.Inputs,
                    $"{jobId}:input",
                    completionTick,
                    ProductionOwnerFor(placed, normalizedWorkerId),
                    out reservationIds,
                    onSiteHouseholdRecipe ? placed!.InstanceId : null);
                return reserved;
            });

            var job = new WorldProductionJob(
                jobId,
                recipe.CanonicalId,
                normalizedBuildingId,
                normalizedWorkerId,
                WorldTick,
                completionTick,
                WorldProductionJobState.Running,
                reservationIds.ToArray());
            var productionJobs = isFertileLandBuild
                ? worldSimulation.ProductionJobs
                : worldSimulation.ProductionJobs.Append(job).OrderBy(item => item.JobId, StringComparer.Ordinal).ToArray();
            var cropBuilds = isFertileLandBuild
                ? (worldSimulation.CropBuilds ?? []).Append(job).OrderBy(item => item.JobId, StringComparer.Ordinal).ToArray()
                : worldSimulation.CropBuilds;
            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings,
                productionJobs,
                checked(worldSimulation.NextProductionJobSequence + 1),
                cropBuilds);
            AppendEvent(eventKind == "recipe_started" && isFertileLandBuild ? "build_started" : eventKind,
                $"{job.JobId}:{job.RecipeId}:{job.BuildingInstanceId}");
            return ProductionStartResult.Success(job);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return ProductionStartResult.Rejected(recipeId?.Trim() ?? string.Empty, exception.Message);
        }
    }

    public ContentPackageRecord RollbackContent(string packageId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        gate.Wait();
        try
        {
            var manifest = GetContentManifest(packageId);
            var remainingSimulation = WorldContentSimulationRules.RemovePackage(worldSimulation, manifest.PackageDigest);
            if (inhabitants.Values.Any(person => person.Project is { } project &&
                (project.CandidateId.StartsWith($"build:building:{manifest.PackageDigest}/", StringComparison.Ordinal) ||
                 project.CandidateId.StartsWith($"build:recipe:{manifest.PackageDigest}/", StringComparison.Ordinal))))
            {
                throw new InvalidOperationException("Content referenced by settlement projects requires an explicit migration before removal.");
            }
            var record = contentRegistry.Rollback(packageId, WorldTick, reason);
            worldContent = ContentDefinitionApplicator.RemovePackage(worldContent, record.Manifest.PackageDigest);
            worldSimulation = remainingSimulation;
            if (survivalState is not null)
            {
                survivalState = survivalState with
                {
                    Fires = survivalState.Fires.Where(fire =>
                    worldSimulation.Buildings.Any(building => building.InstanceId == fire.BuildingId)).ToArray()
                };
            }
            assetReservations.ReleasePackage(packageId, WorldTick);
            AppendEvent("content_rolled_back", $"{packageId}:{reason.Trim()}");
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool SetLifePace(int rate)
    {
        gate.Wait();
        try
        {
            var before = society.Checkpoint.LifeClock;
            society.Apply(checkpoint => SocietyFixture.SetLifePace(checkpoint, rate));
            if (before == society.Checkpoint.LifeClock) return false;
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("life_pace_changed", rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool SetJevEnabled(bool enabled)
    {
        gate.Wait();
        try
        {
            if (!society.Checkpoint.IsPaused)
                throw new InvalidOperationException("Pause the world before changing Jev assistance.");
            if (jevEnabled == enabled) return false;
            foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
            foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
            jevEnabled = enabled;
            jevPolicyRevision = checked(jevPolicyRevision + 1);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("jev_assistance_changed", enabled ? "enabled" : "disabled");
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Pause()
    {
        gate.Wait();
        try
        {
            var wasPaused = society.Checkpoint.IsPaused;
            var result = society.Pause();
            if (!wasPaused && result.Checkpoint.IsPaused)
            {
                foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
                foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
                AppendEvent("paused", "owner_request");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Resume()
    {
        gate.Wait();
        try
        {
            if (founderSetup is { Started: false })
                throw new InvalidOperationException("Place four founders and explicitly start the world before time can run.");
            var wasPaused = society.Checkpoint.IsPaused;
            var result = society.Resume();
            if (wasPaused && !result.Checkpoint.IsPaused)
            {
                AppendEvent("resumed", $"epoch:{result.Checkpoint.RunEpoch}");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public string PlaceFounder(string founderId, GridPoint position)
    {
        gate.Wait();
        try
        {
            ValidateFounderPlacementUnsafe(founderId, position);
            var setup = founderSetup!;

            var ordinal = setup.FounderIds.Count + 1;
            var householdId = ordinal <= 2 ? HouseholdId : SecondHouseholdId;
            var name = $"Founder {ordinal}";
            var founder = SocietyFixture.CreateFounder(founderId, name, config: society.Checkpoint.Config);
            society.Apply(checkpoint => SocietyFixture.PlaceFounder(checkpoint, founder, householdId));
            inhabitants.Add(founderId, new PlaytestInhabitantState(founderId, position, 6_500, 0,
                "undecided", "find a purpose"));
            founderSetup = setup with { FounderIds = [.. setup.FounderIds, founderId] };
            AddTownResident(TownBorderRules.FirstTownId, founderId, "founder_joined");
            AppendEvent("founder_placed", $"{founderId}:{householdId}");
            return householdId;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool MoveFounder(string founderId, GridPoint position)
    {
        gate.Wait();
        try
        {
            if (founderSetup is not { Started: false } setup || !society.Checkpoint.IsPaused ||
                WorldTick != 0)
                throw new InvalidOperationException("Founders can only be moved during initial paused setup.");
            if (string.IsNullOrWhiteSpace(founderId) || !setup.FounderIds.Contains(founderId, StringComparer.Ordinal) ||
                !inhabitants.TryGetValue(founderId, out var founder))
                throw new ArgumentException("Choose a placed founder to move.", nameof(founderId));
            if (!map.IsBuildable(position) || map.CampObjects.Any(item => item.Position == position) ||
                map.Resources.Any(item => item.Position == position) ||
                inhabitants.Values.Any(person => person.InhabitantId != founderId && person.Position == position))
                throw new ArgumentException("Choose an empty passable tile for this founder.", nameof(position));
            if (founder.Position == position) return false;
            inhabitants[founderId] = founder with { Position = position };
            AppendEvent("founder_moved", $"{founderId}:{founder.Position.X},{founder.Position.Y}->{position.X},{position.Y}");
            return true;
        }
        finally { gate.Release(); }
    }

    public int UndoLastFounder(string founderId)
    {
        gate.Wait();
        try
        {
            if (founderSetup is not { Started: false } setup || !society.Checkpoint.IsPaused ||
                WorldTick != 0 || setup.FounderIds.Count == 0)
                throw new InvalidOperationException("Founder placement can only be undone during initial paused setup.");
            if (setup.FounderIds[^1] != founderId)
                throw new ArgumentException("Only the most recently placed founder can be undone.", nameof(founderId));
            society.Apply(checkpoint => SocietyFixture.UndoFounderPlacement(checkpoint, founderId));
            inhabitants.Remove(founderId);
            founderSetup = setup with { FounderIds = setup.FounderIds.Take(setup.FounderIds.Count - 1).ToArray() };
            RemoveTownResident(founderId);
            AppendEvent("founder_placement_undone", founderId);
            return founderSetup.FounderIds.Count;
        }
        finally { gate.Release(); }
    }

    public void ValidateFounderPlacement(string founderId, GridPoint position)
    {
        gate.Wait();
        try { ValidateFounderPlacementUnsafe(founderId, position); }
        finally { gate.Release(); }
    }

    private void ValidateFounderPlacementUnsafe(string founderId, GridPoint position)
    {
        if (founderSetup is not { Started: false } setup || !society.Checkpoint.IsPaused ||
            WorldTick != 0 || setup.FounderIds.Count >= RequiredFounders)
            throw new InvalidOperationException("Founders can only be placed during the initial paused setup.");
        if (geographyOptions is not null && setup.FounderIds.Count == 0 &&
            contentRegistry.ExportState().Packages.Any(package =>
                package.Manifest.PackageId == StarterContent.PackageId && package.ActivationTick == 0) &&
            !towns.Any(item => item.Id == TownBorderRules.FirstTownId && item.OriginSite is not null))
            throw new InvalidOperationException("Choose the first Town site before placing founders.");
        if (founderId is null || !founderId.StartsWith("founder:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(founderId["founder:".Length..], "N", out _) ||
            inhabitants.ContainsKey(founderId))
            throw new ArgumentException("The founder ID is invalid or already used.", nameof(founderId));
        if (!map.IsBuildable(position) || map.CampObjects.Any(item => item.Position == position) ||
            map.Resources.Any(item => item.Position == position) ||
            inhabitants.Values.Any(person => person.Position == position))
            throw new ArgumentException("Choose an empty passable tile for this founder.", nameof(position));
    }

    public string? AddAgent(string agentId, GridPoint position)
    {
        gate.Wait();
        try
        {
            ValidateAgentPlacementUnsafe(agentId, position);
            var town = towns.SingleOrDefault(item => item.BorderTiles.Contains(position));
            // The Town border gives residency, not household membership.
            // Only recorded household property forces an existing household;
            // outside every Town, the adult begins an independent one.
            var householdId = HouseholdPropertyAt(position) ??
                (town is null ? "household:" + agentId : null);
            society.Apply(checkpoint => SocietyFixture.AddAdult(checkpoint, agentId, householdId));
            inhabitants.Add(agentId, new PlaytestInhabitantState(agentId, position, 6_500, 0,
                "undecided", "find a purpose"));
            if (town is not null) AddTownResident(town.Id, agentId, "agent_joined");
            else AppendEvent("town_membership_evaluated", $"{agentId}:unaffiliated");
            AppendEvent("agent_added", agentId);
            return householdId;
        }
        finally { gate.Release(); }
    }

    public void ValidateAgentPlacement(string agentId, GridPoint position)
    {
        gate.Wait();
        try { ValidateAgentPlacementUnsafe(agentId, position); }
        finally { gate.Release(); }
    }

    private void ValidateAgentPlacementUnsafe(string agentId, GridPoint position)
    {
        if (founderSetup is not { Started: true })
            throw new InvalidOperationException("Start the world with four founders before adding more agents.");
        if (agentId is null || !agentId.StartsWith("agent:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(agentId["agent:".Length..], "N", out _) ||
            society.Checkpoint.Inhabitants.Any(person => person.Id == agentId))
            throw new ArgumentException("The agent ID is invalid or already used.", nameof(agentId));
        if (!map.IsBuildable(position) || map.CampObjects.Any(item => item.Position == position) ||
            map.Resources.Any(item => item.Position == position) ||
            (inhabitants.Values.Any(person => person.Position == position) && !IsHouseAt(position)))
            throw new ArgumentException("Choose an empty passable tile for this agent.", nameof(position));
        if (towns.Count(item => item.BorderTiles.Contains(position)) > 1)
            throw new InvalidOperationException("Overlapping Town borders cannot determine starting membership.");
        _ = HouseholdPropertyAt(position);
    }

    private string? HouseholdPropertyAt(GridPoint position)
    {
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var owners = worldSimulation.Buildings.Where(building => building.HouseholdId is not null &&
                definitions.TryGetValue(building.DefinitionId, out var definition) &&
                WorldContentSimulationRules.Footprint(definition, building.Position).Contains(position))
            .Select(building => building.HouseholdId!)
            .Distinct(StringComparer.Ordinal)
            .Take(2).ToArray();
        if (owners.Length > 1)
            throw new InvalidOperationException("Overlapping household property cannot determine starting membership.");
        return owners.FirstOrDefault();
    }

    private bool IsHouseAt(GridPoint position)
    {
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        return worldSimulation.Buildings.Any(building =>
            definitions.TryGetValue(building.DefinitionId, out var definition) &&
            definition.Tags.Contains("house", StringComparer.Ordinal) &&
            WorldContentSimulationRules.Footprint(definition, building.Position).Contains(position));
    }

    public bool RenameAgent(string agentId, string name)
    {
        gate.Wait();
        try
        {
            if (founderSetup is null || !society.Checkpoint.Inhabitants.Any(person => person.Id == agentId))
                throw new ArgumentException("Choose an agent in this world.", nameof(agentId));
            var result = society.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, agentId, name));
            var changed = result.NewEvents is { Count: > 0 };
            if (changed) AppendEvent("agent_renamed", agentId);
            return changed;
        }
        finally { gate.Release(); }
    }

    public void StartWorld()
    {
        gate.Wait();
        try
        {
            if (founderSetup is not { Started: false } setup || setup.FounderIds.Count != RequiredFounders ||
                !society.Checkpoint.IsPaused || WorldTick != 0)
                throw new InvalidOperationException("Place and configure all four founders before starting time.");
            founderSetup = setup with { Started = true };
            if (towns.SingleOrDefault(item => item.Id == TownBorderRules.FirstTownId) is { } firstTown)
            {
                SetTown(firstTown with { FoundingState = "founded" });
                AppendEvent("town_founded", $"{firstTown.Id}:residents:{firstTown.ResidentIds.Count}");
            }
            society.Resume();
            AppendEvent("world_started", "four_founders_ready");
        }
        finally
        {
            gate.Release();
        }
    }

    public void Validate()
    {
        SocietyFixture.Validate(society.Checkpoint);
        ValidateBeliefEventSources(society.Checkpoint.Beliefs ?? [], events, eventHistoryFloor);
        society.Validate();
        contentRegistry.Validate();
        worldContent.Validate();
        var expectedWorldContent = RebuildWorldContent(contentRegistry.ExportState());
        if (!string.Equals(worldContent.StateDigest, expectedWorldContent.StateDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The private-world typed content does not match active package records.");
        }
        assetReservations.Validate();
        ValidateAssetReservationsAgainstActivePackages();
        WorldContentSimulationRules.Validate(worldSimulation, worldContent, map, WorldTick);
        ValidatePhysicalInventoryLocations(society.Checkpoint.Inventory, worldSimulation, worldContent,
            society.Checkpoint.Inhabitants);
        if (worldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
            !society.Checkpoint.Households.Any(household => household.Id == householdId)))
            throw new InvalidDataException("A House references a missing household.");
        var inventoryReservationIds = society.Checkpoint.Inventory.Reservations
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var job in worldSimulation.ProductionJobs
                     .Concat(worldSimulation.CropBuilds ?? [])
                     .Where(item => item.State == WorldProductionJobState.Running))
        {
            if (job.InputReservationIds.Any(id => !inventoryReservationIds.Contains(id)))
            {
                throw new InvalidDataException($"Production job '{job.JobId}' has a missing inventory reservation.");
            }
        }
        WorldSystemsRules.Validate(worldSystems);
        if (worldSystems.WorldTick != WorldTick ||
            !string.Equals(worldSystems.WorldSeed, worldSeed, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The private-world richer-systems state does not match the authoritative clock or seed.");
        }
        var mapValidation = MapAcceptance.Validate(map, allowEmptyCamp: founderSetup is not null);
        if (!mapValidation.IsValid)
        {
            throw new InvalidDataException($"The private-world map is invalid: {mapValidation.Failure}");
        }
        var activeIds = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var physicalIds = inhabitants.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!activeIds.SequenceEqual(physicalIds))
        {
            throw new InvalidDataException("The private-world physical and society populations disagree.");
        }
        ValidateFounderSetup(founderSetup, society.Checkpoint);
        ValidateTowns(towns, map, founderSetup, society.Checkpoint, worldSimulation, worldContent);
        ValidateRoads(RoadTiles, map, founderSetup);
        ValidateDeceasedArchive(deceasedInhabitants.Values, society.Checkpoint, map, checkpointSchemaVersion);
        AgentKnowledgeRules.Validate(knowledge, map, society.Checkpoint, WorldTick, checkpointSchemaVersion);

        foreach (var inhabitant in inhabitants.Values)
        {
            ValidateProficiency(inhabitant, checkpointSchemaVersion);
            ValidateSocialStanding(inhabitant, society.Checkpoint.Inhabitants.Select(item => item.Id), checkpointSchemaVersion, WorldTick);
            ValidatePrivateThoughts(inhabitant.RecentThoughts, checkpointSchemaVersion, WorldTick);
            if (inhabitant.Project is { } project)
            {
                ValidateProject(project, WorldTick);
                if (checkpointSchemaVersion < 5)
                {
                    throw new InvalidDataException("Persistent projects require private-world schema 5.");
                }
            }
            if (!map.IsPassable(inhabitant.Position) ||
                inhabitant.HungerBasisPoints is < 0 or > 10_000 ||
                inhabitant.MoveWaitTicks < 0 || inhabitant.TravelCooldownTicks < 0)
            {
                throw new InvalidDataException($"Physical state for '{inhabitant.InhabitantId}' is invalid.");
            }
        }

        var expectedEventId = checked(eventHistoryFloor + 1);
        var previousTick = 0L;
        foreach (var worldEvent in events)
        {
            if (worldEvent.EventId != expectedEventId ||
                worldEvent.WorldTick < previousTick ||
                worldEvent.WorldTick > WorldTick ||
                worldEvent.Position is { } eventPosition && !map.Contains(eventPosition))
            {
                throw new InvalidDataException("Private-world events are not a committed ordered sequence.");
            }

            expectedEventId++;
            previousTick = worldEvent.WorldTick;
        }
    }

    private void AddTownResident(string townId, string residentId, string reason)
    {
        var town = towns.SingleOrDefault(item => item.Id == townId)
            ?? throw new InvalidOperationException("The resident's Town does not exist.");
        if (town.ResidentIds.Contains(residentId, StringComparer.Ordinal)) return;
        SetTown(town with
        {
            ResidentIds = town.ResidentIds.Append(residentId).Order(StringComparer.Ordinal).ToArray(),
        });
        var updated = towns.Single(item => item.Id == townId);
        AppendEvent("town_resident_joined", $"{updated.Id}:{residentId}:{reason}:residents:{updated.ResidentIds.Count}");
    }

    private void RemoveTownResident(string residentId)
    {
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(residentId, StringComparer.Ordinal));
        if (town is null) return;
        var residents = town.ResidentIds.Where(id => id != residentId).ToArray();
        SetTown(town with { ResidentIds = residents });
        AppendEvent("town_resident_left", $"{town.Id}:{residentId}:residents:{residents.Length}");
    }

    private string? TownForResident(string residentId) => towns
        .SingleOrDefault(item => item.ResidentIds.Contains(residentId, StringComparer.Ordinal))?.Id;

    private TownRuntimeState? TownForOwnerPlacement(GridPoint position, BuildingDefinition definition) => towns
        .SingleOrDefault(item => TownBorderRules.IsWithinOrAdjacent(item, position, definition.Width, definition.Height));

    private void AssignBuildingToTown(PlacedBuilding building, BuildingDefinition definition)
    {
        var town = towns.SingleOrDefault(item => item.Id == building.TownId)
            ?? throw new InvalidOperationException("The assigned Town does not exist.");
        if (town.AssignedBuildingIds.Contains(building.InstanceId, StringComparer.Ordinal))
            throw new InvalidDataException("A placed building is already assigned to its Town.");
        var border = TownBorderRules.ExpandForBuilding(map, town, building.Position, definition.Width, definition.Height);
        SetTown(town with
        {
            AssignedBuildingIds = town.AssignedBuildingIds.Append(building.InstanceId)
                .Order(StringComparer.Ordinal).ToArray(),
            BorderTiles = border,
        });
        var updated = towns.Single(item => item.Id == town.Id);
        AppendEvent("town_building_assigned", $"{town.Id}:{building.InstanceId}:buildings:{updated.AssignedBuildingIds.Count}");
        if (!town.BorderTiles.SequenceEqual(border))
            AppendEvent("town_border_expanded", $"{town.Id}:{building.InstanceId}:tiles:{border.Count}");
        GenerateRoadToBuilding(building);
    }

    private void SetTown(TownRuntimeState updated)
    {
        var index = towns.FindIndex(item => item.Id == updated.Id);
        if (index < 0) throw new InvalidOperationException("The Town identity does not exist.");
        towns[index] = updated;
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private static IReadOnlyList<TownRuntimeState> MigrateTowns(PrivateWorldRuntimeState state)
    {
        if (state.FounderSetup is not { } setup) return [];
        if (!setup.Started && setup.FounderIds.Count == 0 && state.Map.CampObjects.Count == 0)
            return [];
        var active = state.Society.Society.Inhabitants
            .Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var residents = setup.FounderIds.Where(active.Contains).ToArray();
        return [TownBorderRules.CreateFirstTown(state.Map, residents, founded: setup.Started)];
    }

    private static void ValidateTowns(
        IReadOnlyList<TownRuntimeState>? savedTowns,
        SeededMap map,
        FounderSetupState? setup,
        SocietyCheckpoint society,
        WorldContentSimulationState simulation,
        DeclarativeWorldContentState content)
    {
        ArgumentNullException.ThrowIfNull(savedTowns);
        if (setup is null)
        {
            if (savedTowns.Count != 0 || simulation.Buildings.Any(item => item.TownId is not null))
                throw new InvalidDataException("A legacy world cannot claim an unrecorded Town or Town-assigned building.");
            return;
        }

        if (savedTowns.Count == 0 && !setup.Started && setup.FounderIds.Count == 0 &&
            map.CampObjects.Count == 0 && simulation.Buildings.Count == 0)
            return;
        if (savedTowns.Count != 1)
            throw new InvalidDataException("A founder-setup world must have exactly one first Town.");
        var town = savedTowns[0];
        if (town.Id != TownBorderRules.FirstTownId || town.Name != TownBorderRules.FirstTownName ||
            town.FoundingState != (setup.Started ? "founded" : "founding") || town.FoundedTick != 0 ||
            town.OriginSite is { } origin && !map.IsBuildable(origin) ||
            town.ResidentIds is null || town.AssignedBuildingIds is null || town.BorderTiles is null ||
            town.ResidentIds.Distinct(StringComparer.Ordinal).Count() != town.ResidentIds.Count ||
            town.AssignedBuildingIds.Distinct(StringComparer.Ordinal).Count() != town.AssignedBuildingIds.Count ||
            town.BorderTiles.Distinct().Count() != town.BorderTiles.Count || town.BorderTiles.Count == 0)
            throw new InvalidDataException("The first Town identity, founding state, or membership is invalid.");

        var active = society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        if (town.ResidentIds.Any(id => !active.Contains(id)) ||
            setup.FounderIds.Any(id => active.Contains(id) && !town.ResidentIds.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("Town residents must be active inhabitants and active founders retain their founding membership.");

        var byInstance = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var assignedIds = simulation.Buildings.Where(item => item.TownId == town.Id)
            .Select(item => item.InstanceId).Order(StringComparer.Ordinal).ToArray();
        if (!town.AssignedBuildingIds.Order(StringComparer.Ordinal).SequenceEqual(assignedIds) ||
            simulation.Buildings.Any(item => item.TownId is not null && item.TownId != town.Id))
            throw new InvalidDataException("Town building assignments disagree with the placed-building state.");

        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var buildingId in town.AssignedBuildingIds)
            if (!byInstance.ContainsKey(buildingId))
                throw new InvalidDataException("A Town references a building that is not placed.");
        var expected = TownBorderRules.ExpectedBorder(map, town, simulation.Buildings, definitions);
        if (!town.BorderTiles.OrderBy(point => point.Y).ThenBy(point => point.X)
            .SequenceEqual(expected.OrderBy(point => point.Y).ThenBy(point => point.X)) ||
            town.BorderTiles.Any(point => !map.Contains(point)))
            throw new InvalidDataException("The saved Town border does not match its founding area and assigned buildings.");
    }

    private static void ValidateFounderSetup(FounderSetupState? setup, SocietyCheckpoint society)
    {
        if (setup is null) return;
        if (setup.FounderIds is null || setup.FounderIds.Count > RequiredFounders ||
            setup.FounderIds.Distinct(StringComparer.Ordinal).Count() != setup.FounderIds.Count ||
            setup.FounderIds.Any(id => string.IsNullOrWhiteSpace(id) ||
                !society.Inhabitants.Any(person => person.Id == id)))
            throw new InvalidDataException("Founder setup references invalid or duplicate agents.");
        if (setup.Started)
        {
            if (setup.FounderIds.Count != RequiredFounders)
                throw new InvalidDataException("A started world requires four configured founders.");
        }
        else if (!society.IsPaused || society.WorldTick != 0 ||
                 society.Inhabitants.Count != setup.FounderIds.Count ||
                 society.Inhabitants.Any(person => person.Status != SocietyInhabitantStatus.Active))
            throw new InvalidDataException("An incomplete founder setup must remain paused at creation time.");
    }

    public void Dispose()
    {
        foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
        foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
        society.Dispose();
        gate.Dispose();
        tickGate.Dispose();
    }

    public void PersistCheckpoint(Func<PrivateWorldRuntimeState, PrivateWorldRuntimeState> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        gate.Wait();
        try
        {
            var saved = persist(CaptureState());
            if (saved.HistoryArchiveHead != historyArchiveHead)
            {
                using var compacted = Restore(saved, providerFactory, maxCognitionDispatchPerCycle, minimumCognitionConfidence);
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
        RoadTiles);

    public DeclarativeWorldContentState WorldContent => worldContent;

    private ContentPackageManifest GetContentManifest(string packageId)
    {
        ContentPackageRules.ValidatePackageId(packageId);
        return contentRegistry.ExportState().Packages
            .SingleOrDefault(package => package.Manifest.PackageId == packageId)?.Manifest
            ?? throw new KeyNotFoundException($"Package '{packageId}' has no lifecycle record.");
    }

    private static DeclarativeWorldContentState RebuildWorldContent(ContentRegistryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new DeclarativeWorldContentState([], []);
        var active = state.Packages.Where(package => package.Lifecycle == ContentPackageLifecycle.Active)
            .ToDictionary(package => package.Manifest.PackageId, StringComparer.Ordinal);
        var resolution = ContentPackageResolver.Resolve(active.Values.Select(package => package.Manifest), active.Keys);
        if (!resolution.IsSuccess)
        {
            throw new InvalidDataException("Active world content has an invalid dependency graph.");
        }
        foreach (var entry in resolution.Lock)
        {
            result = ContentDefinitionPayloadCodec.ApplyPackage(result, active[entry.PackageId].Manifest);
        }

        return result;
    }

    private void ValidateAssetReservationsAgainstActivePackages()
    {
        var expected = new WorldAssetReservationLedger(assetReservations.Policy);
        foreach (var package in contentRegistry.ExportState().Packages
                     .Where(item => item.Lifecycle == ContentPackageLifecycle.Active)
                     .OrderBy(item => item.ActivationTick ?? long.MaxValue)
                     .ThenBy(item => item.Manifest.PackageId, StringComparer.Ordinal))
        {
            var result = expected.TryReservePackage(
                package.Manifest.PackageId,
                package.Manifest.AssetReservations ?? [],
                package.ActivationTick ?? WorldTick);
            if (!result.IsSuccess)
            {
                throw new InvalidDataException(
                    $"Active package '{package.Manifest.PackageId}' cannot be reconstructed in the world asset reservation ledger: {result.Diagnostic}");
            }
        }

        var expectedReservations = expected.ExportState().Reservations;
        var actualReservations = assetReservations.ExportState().Reservations;
        if (!expectedReservations.SequenceEqual(actualReservations))
        {
            throw new InvalidDataException("The world asset reservation ledger does not match active package reservations.");
        }
    }

    private TownLayoutContext CreateTownLayoutContext(string actor, GridPoint? selectedSite = null)
    {
        var origin = inhabitants[actor].Position;
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(actor, StringComparer.Ordinal));
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var occupied = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(roadTiles)
            .Concat(worldSimulation.Buildings.SelectMany(building =>
            {
                if (!definitions.TryGetValue(building.DefinitionId, out var definition))
                    throw new InvalidDataException("A placed building has no active definition.");
                return WorldContentSimulationRules.Footprint(definition, building.Position);
            }))
            .Concat(inhabitants.Values.Where(person => person.InhabitantId != actor)
                .Select(person => person.Position))
            .ToHashSet();
        var resourcesForLayout = map.Resources.Select(resource => new TownLayoutResource(
            resource,
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available));
        var buildingsForLayout = worldSimulation.Buildings
            .Where(building => definitions.ContainsKey(building.DefinitionId))
            .Select(building => new TownLayoutBuilding(building, definitions[building.DefinitionId]));
        return new TownLayoutContext(
            map,
            town,
            occupied,
            FindUnoccupiedFootCosts(actor, origin, town, occupied, selectedSite),
            resourcesForLayout,
            buildingsForLayout);
    }

    private Dictionary<GridPoint, int> FindUnoccupiedFootCosts(
        string inhabitantId, GridPoint origin, TownRuntimeState? town,
        HashSet<GridPoint> occupiedSites, GridPoint? selectedSite)
    {
        if (selectedSite is { } invalidSite &&
            (!map.IsBuildable(invalidSite) || occupiedSites.Contains(invalidSite)))
            return new Dictionary<GridPoint, int> { [origin] = 0 };
        var occupied = inhabitants.Values
            .Where(item => item.InhabitantId != inhabitantId)
            .Select(item => item.Position)
            .ToHashSet();
        // A Town has bounded construction anchors. Legacy worlds without a
        // Town consider the nearest 32 viable anchors; an already chosen site
        // remains a mandatory route target, however far away it was saved.
        IReadOnlyList<GridPoint>? anchors = town is null ? null : TownLayoutContext.CandidateBounds(map, town);
        if (selectedSite is { } chosenSite && anchors is not null)
            anchors = anchors.Contains(chosenSite) ? [chosenSite] : [];
        var pendingAnchors = anchors?.Where(point => map.IsBuildable(point) && !occupiedSites.Contains(point))
            .ToHashSet();
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int> { [origin] = 0 };
        var settled = new Dictionary<GridPoint, int>();
        var viableLegacyAnchors = 0;
        var selectedSiteReached = selectedSite is null ||
            pendingAnchors is not null && pendingAnchors.Count == 0;
        var order = 0;
        open.Enqueue(origin, (0, origin.Y, origin.X, order++));
        while (open.TryDequeue(out var current, out var priority))
        {
            if (priority.Cost != best[current])
                continue;
            settled[current] = priority.Cost;
            if (current == selectedSite)
                selectedSiteReached = true;
            if (pendingAnchors is null)
            {
                if (map.IsBuildable(current) && !occupiedSites.Contains(current))
                    viableLegacyAnchors++;
            }
            else
            {
                pendingAnchors.Remove(current);
            }
            if (selectedSite is not null ? selectedSiteReached :
                pendingAnchors?.Count == 0 || pendingAnchors is null && viableLegacyAnchors >= 32)
                break;
            foreach (var next in map.FootNeighbors(current))
            {
                if (occupied.Contains(next) ||
                    map.IsDiagonalFootStep(current, next) &&
                    (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                     occupied.Contains(new GridPoint(current.X, next.Y))))
                    continue;

                var cost = checked(priority.Cost + RoadStepCost(current, next));
                if (best.TryGetValue(next, out var previous) && previous <= cost)
                    continue;
                best[next] = cost;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }

        return settled;
    }

    private bool TryFindRecipeSite(
        RecipeDefinition recipe,
        out string siteId,
        out GridPoint position,
        string? actorId = null)
    {
        if (recipe.IsCrop)
        {
            foreach (var resource in map.Resources
                         .Where(item => item.Id == SeededMapGenerator.FertileLandResourceId &&
                             item.Kind == "fertile_land")
                         .OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                if (resources.TryGetValue(resource.Id, out var resourceState) &&
                    resourceState == ResourceState.Available &&
                    !(worldSimulation.CropBuilds ?? []).Any(job =>
                        job.State == WorldProductionJobState.Running &&
                        job.BuildingInstanceId == WorldBuildSiteRules.FertileLandSiteId(resource.Position)))
                {
                    siteId = WorldBuildSiteRules.FertileLandSiteId(resource.Position);
                    position = resource.Position;
                    return true;
                }
            }

            siteId = string.Empty;
            position = default;
            return false;
        }

        if (recipe.WorkstationBuildingId is null)
        {
            siteId = string.Empty;
            position = default;
            return false;
        }

        foreach (var placed in worldSimulation.Buildings.OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            if (placed.DefinitionId != recipe.WorkstationBuildingId ||
                placed.HouseholdId is not null && (actorId is null ||
                    placed.HouseholdId != society.Checkpoint.GetInhabitant(actorId).HouseholdId))
            {
                continue;
            }

            var definition = worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId);
            var activeJobs = worldSimulation.ProductionJobs.Count(item =>
                item.BuildingInstanceId == placed.InstanceId && item.State == WorldProductionJobState.Running);
            if (activeJobs < definition.Capacity)
            {
                siteId = placed.InstanceId;
                position = placed.Position;
                return true;
            }
        }

        siteId = string.Empty;
        position = default;
        return false;
    }

    private bool HasAvailableQuantities(IReadOnlyList<ContentQuantity> quantities, string? ownerId = null)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var requested in quantities)
        {
            var available = inventory.Lots
                .Where(lot => lot.OwnerId == (ownerId ?? HouseholdId) && lot.ItemKind == requested.ResourceId)
                .Sum(AvailableLotQuantity);
            if (available < requested.Amount)
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildInstanceId(string inhabitantId, BuildingDefinition definition)
    {
        // Preserve valid legacy IDs; descendant identities contain separators
        // that are legal society IDs but invalid content instance IDs.
        if (inhabitantId.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-' or '_'))
            return $"build-{inhabitantId}-{definition.PackageDigest[7..15]}-{definition.LocalId}";
        return "build-v2-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhabitantId + "\n" + definition.CanonicalId)));
    }

    private bool CanPlaceBuilding(
        BuildingDefinition definition,
        GridPoint position,
        out string failure)
    {
        var footprint = WorldContentSimulationRules.Footprint(definition, position).ToArray();
        if (footprint.Any(point => !map.IsBuildable(point)))
        {
            failure = "Every building footprint tile must be on buildable ground; mountains and peaks cannot hold buildings.";
            return false;
        }

        var occupied = map.CampObjects
            .Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(roadTiles)
            .ToHashSet();
        var buildingDefinitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var placed in worldSimulation.Buildings)
        {
            if (!buildingDefinitions.TryGetValue(placed.DefinitionId, out var existingDefinition))
            {
                failure = $"Placed building '{placed.InstanceId}' references an unavailable definition.";
                return false;
            }

            foreach (var existingPoint in WorldContentSimulationRules.Footprint(existingDefinition, placed.Position))
            {
                occupied.Add(existingPoint);
            }
        }

        if (footprint.Any(occupied.Contains))
        {
            failure = "The building footprint overlaps an existing object, resource, Road, or building.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private void ApplyInventoryTransition(Func<InventoryCheckpoint, InventoryCheckpoint> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        society.Apply(checkpoint => new SocietyOperationResult(
            checkpoint with { Inventory = transition(checkpoint.Inventory) },
            null,
            []));
    }

    private static InventoryCheckpoint ConsumeQuantities(
        InventoryCheckpoint inventory,
        IReadOnlyList<ContentQuantity> quantities,
        string purpose,
        string ownerId)
    {
        var current = inventory;
        for (var quantityIndex = 0; quantityIndex < quantities.Count; quantityIndex++)
        {
            var requested = quantities[quantityIndex];
            var remaining = requested.Amount;
            var lots = current.Lots
                .Where(lot => lot.OwnerId == ownerId && lot.ItemKind == requested.ResourceId && lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal)
                .ToArray();
            foreach (var lot in lots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var reserved = current.Reservations
                    .Where(reservation => reservation.LotId == lot.Id &&
                        reservation.State is InventoryReservationState.Reserved or
                            InventoryReservationState.PartiallyConsumed or
                            InventoryReservationState.Committed)
                    .Sum(reservation => reservation.Quantity);
                var available = lot.Quantity - reserved;
                if (available <= 0)
                {
                    continue;
                }

                var amount = Math.Min(remaining, available);
                var reservationId = $"{purpose}:quantity:{quantityIndex}:lot:{lot.Id}";
                current = InventoryFixture.Reserve(
                    current,
                    reservationId,
                    ownerId,
                    lot.Id,
                    amount,
                    purpose,
                    current.WorldTick);
                current = InventoryFixture.ConsumeReservation(current, reservationId);
                remaining -= amount;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException(
                    $"Insufficient '{requested.ResourceId}' for {purpose}; missing {remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }
        }

        return current;
    }

    private bool HasIngredientsAtBuilding(IReadOnlyList<ContentQuantity> inputs,
        string ownerId, string buildingId) => inputs.All(input =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == ownerId &&
                lot.StorageBuildingId == buildingId && lot.ItemKind == input.ResourceId &&
                lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0)
            .Sum(lot => (long)AvailableLotQuantity(lot)) >= input.Amount);

    private static InventoryCheckpoint ReserveQuantities(
        InventoryCheckpoint inventory,
        IReadOnlyList<ContentQuantity> quantities,
        string purpose,
        long expiryTick,
        string ownerId,
        out IReadOnlyList<string> reservationIds,
        string? requiredStorageBuildingId = null)
    {
        var current = inventory;
        var created = new List<string>();
        for (var quantityIndex = 0; quantityIndex < quantities.Count; quantityIndex++)
        {
            var requested = quantities[quantityIndex];
            var remaining = requested.Amount;
            var lots = current.Lots
                .Where(lot => lot.OwnerId == ownerId && lot.ItemKind == requested.ResourceId &&
                    lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0 &&
                    (requiredStorageBuildingId is null || lot.StorageBuildingId == requiredStorageBuildingId))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal)
                .ToArray();
            foreach (var lot in lots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var reserved = current.Reservations
                    .Where(reservation => reservation.LotId == lot.Id &&
                        reservation.State is InventoryReservationState.Reserved or
                            InventoryReservationState.PartiallyConsumed or
                            InventoryReservationState.Committed)
                    .Sum(reservation => reservation.Quantity);
                var available = lot.Quantity - reserved;
                if (available <= 0)
                {
                    continue;
                }

                var amount = Math.Min(remaining, available);
                var reservationId = $"{purpose}:quantity:{quantityIndex}:lot:{lot.Id}";
                current = InventoryFixture.Reserve(
                    current,
                    reservationId,
                    ownerId,
                    lot.Id,
                    amount,
                    purpose,
                    expiryTick);
                created.Add(reservationId);
                remaining -= amount;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException(
                    $"Insufficient '{requested.ResourceId}' for production; missing {remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }
        }

        reservationIds = created.ToArray();
        return current;
    }

    private void ProcessProduction(long targetTick)
    {
        var due = worldSimulation.ProductionJobs
            .Where(job => job.State == WorldProductionJobState.Running && job.CompletionTick <= targetTick)
            .OrderBy(job => job.CompletionTick)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .ToArray();
        foreach (var job in due)
        {
            var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == job.RecipeId);
            if (recipe is null)
            {
                throw new InvalidDataException($"Production job '{job.JobId}' references a recipe that is no longer active.");
            }

            var completed = CompleteProductionJob(job, recipe, targetTick);

            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings,
                worldSimulation.ProductionJobs
                    .Select(candidate => candidate.JobId == job.JobId
                        ? candidate with { State = completed ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled }
                        : candidate)
                    .OrderBy(candidate => candidate.JobId, StringComparer.Ordinal)
                    .ToArray(),
                worldSimulation.NextProductionJobSequence,
                worldSimulation.CropBuilds);
            AppendEvent(completed ? "recipe_completed" : "recipe_cancelled", $"{job.JobId}:{recipe.CanonicalId}");
        }
    }

    private void ProcessCropBuilds(long targetTick)
    {
        var due = (worldSimulation.CropBuilds ?? [])
            .Where(job => job.State == WorldProductionJobState.Running && job.CompletionTick <= targetTick)
            .OrderBy(job => job.CompletionTick)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .ToArray();
        foreach (var job in due)
        {
            var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == job.RecipeId);
            if (recipe is null || !recipe.IsCrop)
            {
                throw new InvalidDataException($"Crop build '{job.JobId}' references a recipe that is no longer active.");
            }

            var completed = CompleteProductionJob(job, recipe, targetTick);
            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings,
                worldSimulation.ProductionJobs,
                worldSimulation.NextProductionJobSequence,
                (worldSimulation.CropBuilds ?? [])
                    .Select(candidate => candidate.JobId == job.JobId
                        ? candidate with { State = completed ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled }
                        : candidate)
                    .OrderBy(candidate => candidate.JobId, StringComparer.Ordinal)
                    .ToArray());
            AppendEvent(completed ? "build_completed" : "build_cancelled", $"{job.JobId}:{recipe.CanonicalId}");
        }
    }

    private GridPoint CropSite(WorldProductionJob job) =>
        WorldBuildSiteRules.TryGetFertileLandPosition(job.BuildingInstanceId, out var position)
            ? position
            : worldSimulation.Buildings.Single(building => building.InstanceId == job.BuildingInstanceId).Position;

    private bool CompleteProductionJob(
        WorldProductionJob job,
        RecipeDefinition recipe,
        long targetTick)
    {
        var inventoryState = society.Checkpoint.Inventory;
        var inputs = job.InputReservationIds.Select(inventoryState.GetReservation).ToArray();
        if (inputs.Any(reservation => reservation.State is not (InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed) ||
            reservation.ExpiryTick < targetTick || inventoryState.Lots.FirstOrDefault(lot => lot.Id == reservation.LotId) is not { FreshnessBasisPoints: > 0, ConditionBasisPoints: > 0 }))
        {
            ApplyInventoryTransition(inventory =>
            {
                foreach (var reservation in inputs.Where(reservation => reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed))
                {
                    inventory = InventoryFixture.ReleaseReservation(inventory, reservation.Id, "production_input_unusable");
                }
                return inventory;
            });
            AppendEvent("production_input_unusable", job.JobId);
            return false;
        }
        var cropSite = recipe.IsCrop && survivalState is not null ? CropSite(job) : default;
        var cropWeather = recipe.IsCrop && survivalState is not null ? WeatherAt(cropSite) : WeatherKind.Clear;
        var soilMoisture = recipe.IsCrop && survivalState is not null
            ? WeatherRules.SoilMoistureAt(worldSystems, cropSite, map.Height,
                WeatherRules.RegionClimate(map, cropSite))
            : 35;
        var productionBuilding = worldSimulation.Buildings
            .FirstOrDefault(building => building.InstanceId == job.BuildingInstanceId);
        var productionOwner = ProductionOwnerFor(productionBuilding, job.WorkerId);
        ApplyInventoryTransition(inventory =>
        {
            var current = inventory;
            foreach (var reservationId in job.InputReservationIds.Order(StringComparer.Ordinal))
            {
                current = InventoryFixture.ConsumeReservation(current, reservationId);
            }

            for (var outputIndex = 0; outputIndex < recipe.Outputs.Count; outputIndex++)
            {
                var output = recipe.Outputs[outputIndex];
                current = InventoryFixture.AddLot(
                    current,
                    $"{job.JobId}:output:{outputIndex.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}",
                    output.ResourceId,
                    productionOwner,
                    CropOutputQuantity(recipe, output, cropWeather, soilMoisture),
                    targetTick,
                    storageBuildingId: productionBuilding?.HouseholdId is null ? null : productionBuilding.InstanceId);
            }

            return current;
        });
        if (recipe.IsCrop && survivalState is not null && cropWeather is WeatherKind.Snow or WeatherKind.Storm)
        {
            AppendEvent("crop_weather_loss", $"{job.JobId}:{cropWeather.ToString().ToLowerInvariant()}");
        }
        if (recipe.IsCrop && survivalState is not null &&
            recipe.Outputs.Any(output => output.ResourceId == "food") &&
            cropWeather is not (WeatherKind.Snow or WeatherKind.Storm) &&
            (soilMoisture < 15 || soilMoisture >= 50))
        {
            AppendEvent("crop_moisture_effect", $"{job.JobId}:{(soilMoisture < 15 ? "dry" : "wet")}:{soilMoisture}");
        }
        CreditCompletedWork(job.WorkerId, recipe.IsCrop ? "farming" : "crafting");
        return true;
    }

    private static WorldSystemsState CreateWorldSystems(string worldSeed, SeededMap map,
        WorldStartPace startPace = WorldStartPace.Legacy)
    {
        var config = WorldStartPaceRules.WorldSystems(startPace);
        var resources = map.Resources
            .Select(resource => resource.TreeKind is not null
                ? new EcologyResource(
                    resource.Id,
                    resource.Kind,
                    resource.Position,
                    resource.IsRenewable,
                    1,
                    1,
                    resource.IsRenewable ? 1 : 0,
                    resource.IsRenewable ? resource.TreeKind == "orchard" ? 3 : 6 : 0,
                    SeasonKind.Spring,
                    resource.TreeKind == "orchard" ? 3 : 6,
                    EcologyResourceState.Available)
                : resource.IsRenewable
                ? new EcologyResource(
                    resource.Id,
                    resource.Kind,
                    resource.Position,
                    true,
                    8,
                    12,
                    2,
                    1,
                    SeasonKind.Spring,
                    1,
                    EcologyResourceState.Available)
                : new EcologyResource(
                    resource.Id,
                    resource.Kind,
                    resource.Position,
                    false,
                    3,
                    3,
                    0,
                    0,
                    SeasonKind.Spring,
                    0,
                    EcologyResourceState.Available))
            .ToArray();
        var culture = new CultureState(
            [new CultureDefinition("camp", "Camp", ["cooperation", "survival"])],
            [
                new CultureAssignment("founder-scout", "camp", ["mapping"]),
                new CultureAssignment("founder-mira", "camp", ["harvest"]),
                new CultureAssignment("founder-rowan", "camp", ["building"]),
                new CultureAssignment("founder-ilya", "camp", ["memory"]),
            ]);
        var factions = new FactionState(
            [new FactionDefinition("camp-alpha", "Camp Alpha", ["camp"])],
            [
                new FactionStanding("founder-scout", "camp-alpha", 0),
                new FactionStanding("founder-mira", "camp-alpha", 0),
                new FactionStanding("founder-rowan", "camp-alpha", 0),
                new FactionStanding("founder-ilya", "camp-alpha", 0),
            ],
            [new LawRule("camp-no-theft", "camp-alpha", LawActionKind.Theft, LawSeverity.Major, 500, 25)],
            []);
        var currency = new CurrencyState(
            [new CurrencyDefinition("copper", "Copper", "cp")],
            [new CurrencyAccount("camp-wallet", HouseholdId, "copper", 100)],
            []);
        var chunkSize = map.Width > ChunkRules.DefaultChunkSize || map.Height > ChunkRules.DefaultChunkSize
            ? GeographyGenerator.ChunkSize : ChunkRules.DefaultChunkSize;
        var chunks = new List<ChunkManifest>();
        for (var top = 0; top < map.Height; top += chunkSize)
            for (var left = 0; left < map.Width; left += chunkSize)
            {
                var coordinate = new ChunkCoordinate(left / chunkSize, top / chunkSize);
                var chunkWidth = Math.Min(chunkSize, map.Width - left);
                var chunkHeight = Math.Min(chunkSize, map.Height - top);
                chunks.Add(ChunkManifestCodec.WithDigest(new ChunkManifest(
                    coordinate,
                    chunkSize,
                    chunkWidth,
                    chunkHeight,
                    map.Width > ChunkRules.DefaultChunkSize || map.Height > ChunkRules.DefaultChunkSize
                        ? "noise-drainage-camp" : SeededMapGenerator.GeneratorId,
                    map.Width > ChunkRules.DefaultChunkSize || map.Height > ChunkRules.DefaultChunkSize
                        ? "v2" : SeededMapGenerator.GeneratorVersion,
                    map.Resources.Where(resource => resource.Position.X >= left &&
                            resource.Position.X < left + chunkWidth && resource.Position.Y >= top &&
                            resource.Position.Y < top + chunkHeight)
                        .Select(resource => new ChunkResourceMetadata(
                            resource.Id, resource.Kind,
                            new GridPoint(resource.Position.X - left, resource.Position.Y - top),
                            resource.IsRenewable)).ToArray())));
            }
        return WorldSystemsRules.CreateGenesis(
            worldSeed,
            config,
            resources,
            factions,
            currency,
            culture,
            chunks);
    }

    private static WorldSystemsState AdvanceWorldSystemsTo(WorldSystemsState state, long targetTick)
    {
        while (state.WorldTick < targetTick)
        {
            state = WorldSystemsRules.AdvanceOneTick(state);
        }

        return state;
    }

    private void SyncEcologyResourceStates()
    {
        foreach (var resource in worldSystems.Ecology.Resources)
        {
            resources[resource.Id] = resource.State == EcologyResourceState.Available && resource.Quantity > 0
                ? ResourceState.Available
                : ResourceState.Depleted;
        }
    }

    private static SocietyWorldRuntime CreateSociety(
        string worldSeed,
        Func<string, IDecisionProvider>? providerFactory,
        int maxCognitionQueueLength,
        int maxCognitionDispatchPerCycle,
        double minimumCognitionConfidence,
        WorldStartPace startPace)
    {
        var config = WorldStartPaceRules.Society(startPace);
        var founders = new[]
        {
            SocietyFixture.CreateFounder("founder-scout", "Scout", "model:scout", config: config),
            SocietyFixture.CreateFounder("founder-mira", "Mira", "model:mira", config: config),
            SocietyFixture.CreateFounder("founder-rowan", "Rowan", "model:rowan", config: config),
            SocietyFixture.CreateFounder("founder-ilya", "Ilya", "model:ilya", config: config),
        };
        var initialFounders = startPace == WorldStartPace.FounderSetup ? [] : founders;
        var checkpoint = SocietyFixture.CreateGenesis(
            worldSeed,
            initialFounders,
            [
                new InventoryLot(FoodLotId, "food", HouseholdId, 32, 10_000, 10_000, 0),
                new InventoryLot("wood:camp-alpha", "wood", HouseholdId, 48, 10_000, 10_000, 0),
                new InventoryLot("tools:camp-alpha", "tool", HouseholdId, 4, 10_000, 10_000, 0),
                ..(startPace == WorldStartPace.FounderSetup ? new InventoryLot[]
                {
                    new("food:camp-beta", "food", SecondHouseholdId, 16, 10_000, 10_000, 0),
                    new("wood:camp-beta", "wood", SecondHouseholdId, 24, 10_000, 10_000, 0),
                    new("tools:camp-beta", "tool", SecondHouseholdId, 2, 10_000, 10_000, 0),
                    new("seeds:camp-alpha", "seed", HouseholdId, 8, 10_000, 10_000, 0),
                    new("clothing:camp-alpha", "clothing", HouseholdId, 2, 10_000, 10_000, 0),
                    new("seeds:camp-beta", "seed", SecondHouseholdId, 8, 10_000, 10_000, 0),
                    new("clothing:camp-beta", "clothing", SecondHouseholdId, 2, 10_000, 10_000, 0),
                } : []),
            ],
            config,
            "model:world-default");
        checkpoint = SocietyFixture.CreateHousehold(
            checkpoint,
            HouseholdId,
            "Camp Alpha",
            initialFounders.Select(item => item.Id)).Checkpoint;
        if (startPace == WorldStartPace.FounderSetup)
            checkpoint = SocietyFixture.CreateHousehold(checkpoint, SecondHouseholdId, "Camp Beta", []).Checkpoint;
        if (startPace != WorldStartPace.FounderSetup)
        {
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-scout", SocietyWorkRole.Trader).Checkpoint;
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-mira", SocietyWorkRole.Farmer).Checkpoint;
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-rowan", SocietyWorkRole.Builder).Checkpoint;
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-ilya", SocietyWorkRole.Teacher).Checkpoint;
        }
        return new SocietyWorldRuntime(
            checkpoint,
            providerFactory,
            maxCognitionQueueLength,
            maxCognitionDispatchPerCycle,
            minimumCognitionConfidence);
    }

    private void CreatePhysicalState()
    {
        var startingPositions = new[]
        {
            new GridPoint(0, 0),
            new GridPoint(1, 1),
            new GridPoint(2, 1),
            new GridPoint(3, 1),
        };
        var profiles = new (string Id, string Personality, string Aspiration)[]
        {
            ("founder-scout", "curious", "map the nearby world"),
            ("founder-mira", "practical", "make the camp self-sufficient"),
            ("founder-rowan", "patient", "build something lasting"),
            ("founder-ilya", "observant", "teach and preserve memory"),
        };
        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            inhabitants.Add(
                profile.Id,
                new PlaytestInhabitantState(
                    profile.Id,
                    startingPositions[index],
                    6_500,
                    0,
                    profile.Personality,
                    profile.Aspiration));
        }
    }

    private void DrainNeeds()
    {
        foreach (var state in inhabitants.Values.ToArray())
        {
            inhabitants[state.InhabitantId] = state with
            {
                HungerBasisPoints = Math.Max(0, state.HungerBasisPoints - 4),
            };
        }
    }

    private void RemoveDeadPhysicalState()
    {
        var activeIds = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in inhabitants.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            var deceased = society.Checkpoint.GetInhabitant(id);
            var deathTick = deceased.DeathTick ?? throw new InvalidDataException("A removed inhabitant has no committed death.");
            deceasedInhabitants.Add(id, new PlaytestDeceasedInhabitantState(
                id, deathTick, society.Checkpoint.AgeAt(deceased, deathTick), inhabitants[id]));
            inhabitants.Remove(id);
            RemoveTownResident(id);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("inhabitant_removed", id);
        }
    }

    private void EnqueueDueCognition()
    {
        var runtimes = society.Capture().Cognition.Runtimes
            .ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        foreach (var inhabitant in society.Checkpoint.Inhabitants
                     .Where(item => item.Status == SocietyInhabitantStatus.Active)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
                continue;
            }
            var physical = inhabitants[inhabitant.Id];
            if (physical.Project is { Stage: not ("completed" or "cancelled") } project &&
                (physical.HungerBasisPoints < 3_500 || NeedsUrgentWarmth(physical) && !IsProtectiveProject(project)))
            {
                SetProject(inhabitant.Id, project with { Stage = "paused", Blocker = NeedsUrgentWarmth(physical) ? "Seeking warmth" : "Meeting food needs" });
                physical = inhabitants[inhabitant.Id];
            }
            var candidates = CreateCandidates(inhabitant.Id, physical);
            var current = runtimes[inhabitant.Id].CurrentIntention;
            if (!NeedsCognition(inhabitant.Id, current, candidates))
            {
                continue;
            }

            var generation = checked((int)(WorldTick + 1));
            var checkpoint = society.Checkpoint;
            var compaction = (checkpoint.MemoryCompactions ?? [])
                .SingleOrDefault(item => item.OwnerId == inhabitant.Id);
            var retrievedMemories = PrivateWorldMemoryRetrieval.Retrieve(
                checkpoint.Memories,
                checkpoint.Beliefs ?? [],
                compaction,
                inhabitant.Id,
                WorldTick,
                candidates);
            var requiresPersonalProvider = checkpoint.Births.Any(birth => birth.ChildId == inhabitant.Id);
            var knownMapFacts = KnownMapFactsForCognition(inhabitant.Id);
            var observation = new InhabitantObservation(
                inhabitant.Id,
                WorldTick,
                society.Checkpoint.RunEpoch,
                generation,
                ObservationDigest(inhabitant.Id, physical, candidates, retrievedMemories, [], knownMapFacts),
                physical.HungerBasisPoints,
                candidates,
                NeedsName: inhabitant.NeedsName,
                RequiresPersonalProvider: requiresPersonalProvider,
                RetrievedMemories: retrievedMemories,
                KnownMapFacts: knownMapFacts);
            if (jevEnabled && !requiresPersonalProvider && providerFactory is not null)
            {
                try
                {
                    var provider = providerFactory(inhabitant.Id);
                    if (provider.KindFor(observation) == DecisionProviderKind.Jev)
                    {
                        var memoryCandidates = PrivateWorldMemoryRetrieval.Unassessed(
                            checkpoint.Memories,
                            checkpoint.Beliefs ?? [],
                            compaction,
                            inhabitant.Id,
                            WorldTick);
                        if (memoryCandidates.Count > 0)
                        {
                            observation = observation with
                            {
                                MemoryCompactionCandidates = memoryCandidates,
                                ObservationDigest = ObservationDigest(
                                    inhabitant.Id, physical, candidates, retrievedMemories, memoryCandidates, knownMapFacts),
                            };
                        }
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Optional memory assistance cannot prevent the local
                    // retrieval path from supplying safe existing records.
                }
            }
            var accepted = society.EnqueueCognition(new SocietyCognitionScheduleEntry(
                $"tick:{WorldTick}:{inhabitant.Id}",
                inhabitant.Id,
                PriorityFor(physical),
                WorldTick,
                ["routine_tick"],
                observation));
            if (!accepted)
            {
                AppendEvent("cognition_backpressure", inhabitant.Id);
            }
            else
            {
                inhabitants[inhabitant.Id] = inhabitants[inhabitant.Id] with
                {
                    LastDecisionContext = DecisionContext(physical, candidates),
                };
            }
        }
    }

    private bool NeedsCognition(
        string inhabitantId,
        CognitionIntention? current,
        List<CognitionCandidate> candidates)
    {
        if (PendingInstructionFor(inhabitantId) is not null)
        {
            return true;
        }

        if (CanContinueLesson(inhabitantId) || CanContinueProject(inhabitants[inhabitantId]))
        {
            return false;
        }

        if (candidates.Count == 1 && candidates[0].Id == "safe_idle")
        {
            return false;
        }

        if (current is null)
        {
            return true;
        }

        if (!candidates.Any(candidate => candidate.Id == current.CandidateId))
        {
            return true;
        }

        if (current.CandidateId is "harvest_food" or "consume_food")
        {
            return true;
        }

        if (current.CandidateId == "safe_idle")
        {
            var physical = inhabitants[inhabitantId];
            return DecisionContext(physical, candidates) != physical.LastDecisionContext ||
                checked(WorldTick - current.WorldTick) >= 300;
        }

        return checked(WorldTick - current.WorldTick) >= CognitionReevaluationIntervalTicks;
    }

    private static string DecisionContext(PlaytestInhabitantState state, List<CognitionCandidate> candidates) =>
        $"{state.HungerBasisPoints < 2_500}:{NeedsUrgentWarmth(state)}:" +
        string.Join('|', candidates.Select(candidate => candidate.Id).Order(StringComparer.Ordinal));

    private void ApplyContinuingIntentions(IEnumerable<string> dispatchedInhabitantIds)
    {
        var dispatched = dispatchedInhabitantIds.ToHashSet(StringComparer.Ordinal);
        var runtimes = society.Capture().Cognition.Runtimes
            .ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        foreach (var inhabitant in society.Checkpoint.Inhabitants
                     .Where(item => item.Status == SocietyInhabitantStatus.Active)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (dispatched.Contains(inhabitant.Id) || !inhabitants.TryGetValue(inhabitant.Id, out var state))
            {
                continue;
            }
            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
                continue;
            }
            if (CanContinueLesson(inhabitant.Id))
            {
                ContinueLesson(inhabitant.Id);
                continue;
            }
            if (CanContinueProject(state))
            {
                ContinueProject(inhabitant.Id, state);
                continue;
            }
            if (runtimes[inhabitant.Id].CurrentIntention is not { } intention ||
                !CreateCandidates(inhabitant.Id, state).Any(candidate => candidate.Id == intention.CandidateId))
            {
                continue;
            }

            ApplyCandidate(inhabitant.Id, state, intention.CandidateId, reportIdle: false);
        }
    }

    private void ApplySafeRoutinesWhileWaiting(IEnumerable<string> waitingIds)
    {
        var safe = new HashSet<string>(StringComparer.Ordinal)
        {
            "consume_food", "collect_shared_food", "harvest_food", "seek_food",
            "wear_clothing", "tend_fire", "seek_warmth",
        };
        foreach (var id in waitingIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!inhabitants.TryGetValue(id, out var state)) continue;
            if (state.HungerBasisPoints >= 3_500 && !NeedsUrgentWarmth(state))
                continue;
            var candidate = CreateCandidates(id, state)
                .Where(item => safe.Contains(item.Id))
                .OrderBy(item => item.DeterministicPriority)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is not null) ApplyCandidate(id, state, candidate.Id, reportIdle: false);
        }
    }

    private void ApplyDecision(SocietyCognitionDispatchResult decision)
    {
        if (decision.Admission.Accepted && !decision.Admission.FellBack &&
            decision.Admission.Intention?.Provider == DecisionProviderKind.Jev &&
            decision.Admission.MemoryCompactionScores is { Count: > 0 } memoryScores)
        {
            ApplyMemoryCompaction(decision.InhabitantId, memoryScores);
        }

        if (!decision.Admission.Accepted || decision.Admission.Intention is null ||
            !inhabitants.TryGetValue(decision.InhabitantId, out var state))
        {
            AppendEvent("cognition_rejected", $"{decision.InhabitantId}:{decision.Admission.Outcome}");
            return;
        }

        var pendingInstruction = PendingInstructionFor(decision.InhabitantId);
        var candidateId = decision.Admission.Intention.CandidateId;
        var forcedCandidate = !decision.Admission.FellBack && pendingInstruction?.Kind == OwnerInstructionKind.MustDo
            ? InstructionCandidate(pendingInstruction.Text)
            : null;
        if (forcedCandidate is not null && CreateCandidates(decision.InhabitantId, state)
            .Any(candidate => candidate.Id == forcedCandidate))
        {
            candidateId = forcedCandidate;
        }

        ApplyCandidate(decision.InhabitantId, state, candidateId, reportIdle: true);

        if (!decision.Admission.FellBack && pendingInstruction is not null &&
            (pendingInstruction.Kind == OwnerInstructionKind.Suggestive || forcedCandidate is not null))
        {
            completedInstructionIds.Add(pendingInstruction.InstructionId);
            AppendEvent("instruction_applied", $"{pendingInstruction.InstructionId}:{candidateId}");
        }
    }

    private void ApplyCandidate(
        string inhabitantId,
        PlaytestInhabitantState state,
        string candidateId,
        bool reportIdle)
    {
        if (!AgePermitsCandidate(inhabitantId, candidateId))
        {
            AppendEvent("age_action_rejected", $"{inhabitantId}:{candidateId}");
            return;
        }
        if (candidateId.StartsWith("child_", StringComparison.Ordinal))
        {
            ApplyChildCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("guardian_", StringComparison.Ordinal))
        {
            ApplyDependentCareCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("parent_", StringComparison.Ordinal) || candidateId.StartsWith("care:", StringComparison.Ordinal))
        {
            ApplyParenthoodCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("partner_", StringComparison.Ordinal))
        {
            ApplyFamilyCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("learn:", StringComparison.Ordinal) || candidateId.StartsWith("lesson_", StringComparison.Ordinal))
        {
            ApplyLearningCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("council_", StringComparison.Ordinal))
        {
            ApplyCouncilCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("trade_", StringComparison.Ordinal))
        {
            ApplyTradeCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId == "haul_household_stock")
        {
            HaulHouseholdStock(inhabitantId, state);
            return;
        }
        if (candidateId == "store_household_food")
        {
            StoreHouseholdFood(inhabitantId, state);
            return;
        }
        if (candidateId == "store_town_resources")
        {
            StoreTownResources(inhabitantId, state);
            return;
        }
        if (candidateId == "haul_farm_grain")
        {
            HaulFarmGrain(inhabitantId, state);
            return;
        }
        if (candidateId == "haul_farm_flour")
        {
            HaulFarmFlour(inhabitantId, state);
            return;
        }
        if (candidateId == "haul_smith_input")
        {
            HaulBlacksmithInput(inhabitantId, state);
            return;
        }
        if (candidateId == "gather_smith_ore")
        {
            GatherBlacksmithOre(inhabitantId, state);
            return;
        }
        if (candidateId == "deliver_smith_ore")
        {
            DeliverBlacksmithOre(inhabitantId, state);
            return;
        }
        if (candidateId == "collect_wooden_axe" || candidateId == "collect_wooden_pickaxe")
        {
            CollectEquipment(inhabitantId, state,
                candidateId == "collect_wooden_axe" ? "wooden_axe" : "wooden_pickaxe");
            return;
        }
        if (candidateId.StartsWith(KnowledgeSharePrefix, StringComparison.Ordinal))
        {
            ApplyKnowledgeShare(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("build:", StringComparison.Ordinal))
        {
            BeginProject(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("invent:building:", StringComparison.Ordinal))
        {
            ApplyInhabitantBuildingDesignCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("assist:", StringComparison.Ordinal))
        {
            AssistProject(inhabitantId, state, candidateId[7..]);
            return;
        }
        if (candidateId == "replant_tree")
        {
            ReplantTree(inhabitantId, state);
            return;
        }

        switch (candidateId)
        {
            case "explore":
                Explore(inhabitantId, state);
                break;
            case "wear_clothing":
                CollectEquipment(inhabitantId, state, "clothing");
                break;
            case "tend_fire":
                TendFire(inhabitantId, state);
                break;
            case "seek_warmth":
                SeekWarmth(inhabitantId, state);
                break;
            case "seek_food":
                if (AvailableFoodSource(state.Position) is { } foodSource)
                    MoveToward(inhabitantId, state, foodSource.Position, "food", ResourceInteractionRange);
                break;
            case "harvest_food":
                HarvestFood(inhabitantId, state);
                break;
            case "consume_food":
                ConsumeFood(inhabitantId, state);
                break;
            case "collect_shared_food":
                CollectSharedFood(inhabitantId, state);
                break;
            default:
                if (reportIdle)
                {
                    AppendEvent("inhabitant_idle", inhabitantId);
                }
                break;
        }
    }

    private void ApplyBuildDecision(
        string inhabitantId,
        PlaytestInhabitantState state,
        string candidateId)
    {
        if (!TownConstructionCandidateIds.TryParse(candidateId, out var selection))
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:unknown_target");
            return;
        }

        if (selection.IsBuilding)
        {
            var definition = worldContent.Buildings.SingleOrDefault(item => item.CanonicalId == selection.DefinitionId);
            var layout = CreateTownLayoutContext(inhabitantId, selection.SitePosition);
            TownConstructionSiteCandidate? rankedSite = null;
            if (definition is not null)
            {
                if (selection.SitePosition is { } selectedSite)
                    TownLayoutService.TryEvaluateConstructionSite(layout, definition, selectedSite, out rankedSite);
                else
                {
                    var sites = TownLayoutService.RankConstructionSites(layout, definition);
                    rankedSite = sites.Count == 0 ? null : sites[0];
                }
            }

            if (definition is null || rankedSite is null)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:selected_site_no_longer_legal");
                if (selection.SitePosition is { } rejectedSite && definition is not null)
                    AppendEvent("town_layout_site_rejected",
                        $"{TownForResident(inhabitantId) ?? "none"}|{inhabitantId}|{definition.CanonicalId}|{rejectedSite.X}|{rejectedSite.Y}|site_unavailable");
                return;
            }
            var householdProperty = definition.Tags.Any(IsHouseholdBuildingTag);
            var houseOwner = householdProperty
                ? society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId : null;
            if (householdProperty && houseOwner is null)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:household_required");
                return;
            }
            if (definition.Tags.Contains("house", StringComparer.Ordinal) && houseOwner is not null &&
                HouseForHousehold(houseOwner) is not null)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:existing_house");
                return;
            }

            var position = rankedSite.Position;
            if (state.Position != position)
            {
                MoveToward(inhabitantId, state, position, "build");
                return;
            }

            var placement = PlaceBuildingCore(
                BuildInstanceId(inhabitantId, definition),
                definition.CanonicalId,
                position,
                "build_completed",
                TownForResident(inhabitantId),
                houseOwner,
                society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId is null ? inhabitantId : null);
            if (!placement.Applied)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:{placement.Failure}");
            }
            else
            {
                CreditCompletedWork(inhabitantId, "building");
            }

            return;
        }

        var recipeId = selection.DefinitionId;
        var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == recipeId);
        if (recipe is null || !TryFindRecipeSite(recipe, out var siteId, out var sitePosition, inhabitantId))
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:no_valid_site");
            return;
        }

        if (state.Position != sitePosition)
        {
            MoveToward(inhabitantId, state, sitePosition, "build");
            return;
        }

        var started = StartProductionCore(recipe.CanonicalId, siteId, inhabitantId, "build_started");
        if (!started.Applied)
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:{started.Failure}");
        }
    }

    private void MoveToward(
        string inhabitantId,
        PlaytestInhabitantState state,
        GridPoint destination,
        string reason,
        int interactionRange = 0)
    {
        if (IsWithinInteractionRange(state.Position, destination, interactionRange))
        {
            AppendEvent("destination_reached", $"{inhabitantId}:{reason}");
            return;
        }

        if (state.TravelCooldownTicks > 0)
        {
            inhabitants[inhabitantId] = state with { TravelCooldownTicks = state.TravelCooldownTicks - 1 };
            return;
        }

        var route = FindUnoccupiedRoute(inhabitantId, state.Position, destination, interactionRange);
        if (route.Count < 2)
        {
            RecordMovementBlocked(inhabitantId, state, "no_route");
            return;
        }

        var next = route[1];
        inhabitants[inhabitantId] = state with
        {
            Position = next,
            MoveWaitTicks = 0,
            TravelCooldownTicks = (RoadStepCost(state.Position, next) + 99) / 100 - 1 +
                SettlementIllnessRules.TravelDelayTicks(state.Survival?.IllnessBasisPoints ?? 0),
        };
        AppendEvent("inhabitant_moved", $"{inhabitantId}:{state.Position.X},{state.Position.Y}->{next.X},{next.Y}:{reason}");
    }

    private List<GridPoint> FindUnoccupiedRoute(
        string inhabitantId,
        GridPoint origin,
        GridPoint destination,
        int interactionRange)
    {
        var occupied = inhabitants.Values
            .Where(item => item.InhabitantId != inhabitantId)
            .Select(item => item.Position)
            .ToHashSet();
        if (interactionRange == 0 && worldSimulation.Buildings.Any(building =>
                building.Position == destination &&
                building.HouseholdId is not null &&
                building.HouseholdId == society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId))
            occupied.Remove(destination);
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int> { [origin] = 0 };
        var predecessor = new Dictionary<GridPoint, GridPoint>();
        var order = 0;
        open.Enqueue(origin, (0, origin.Y, origin.X, order++));

        while (open.TryDequeue(out var current, out var priority))
        {
            if (priority.Cost != best[current])
                continue;
            if (IsWithinInteractionRange(current, destination, interactionRange))
            {
                var route = new List<GridPoint> { current };
                while (current != origin)
                {
                    current = predecessor[current];
                    route.Add(current);
                }

                route.Reverse();
                return route;
            }

            foreach (var next in map.FootNeighbors(current))
            {
                if (occupied.Contains(next) ||
                    map.IsDiagonalFootStep(current, next) &&
                    (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                     occupied.Contains(new GridPoint(current.X, next.Y))))
                {
                    continue;
                }

                var cost = checked(priority.Cost + RoadStepCost(current, next));
                if (best.TryGetValue(next, out var previous) && previous <= cost)
                    continue;
                best[next] = cost;
                predecessor[next] = current;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }

        return [];
    }

    private void RecordMovementBlocked(
        string inhabitantId,
        PlaytestInhabitantState state,
        string reason)
    {
        var waitTicks = checked(state.MoveWaitTicks + 1);
        inhabitants[inhabitantId] = state with { MoveWaitTicks = waitTicks };
        if (waitTicks == 1 || waitTicks % 30 == 0)
        {
            AppendEvent("movement_blocked", $"{inhabitantId}:{reason}:wait={waitTicks}");
        }
    }

    private bool IsWithinInteractionRange(GridPoint origin, GridPoint destination, int interactionRange) =>
        map.FootDistance(origin, destination) <= interactionRange;

    private MapResource? AvailableFoodSource(GridPoint position) => map.Resources
        .Where(resource => resource.Kind is "food" or "fruit" &&
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available &&
            map.IsReachableFromCampOnFoot(resource.Position))
        .OrderBy(resource => map.FootDistance(resource.Position, position))
        .FirstOrDefault();

    private void HarvestFood(string inhabitantId, PlaytestInhabitantState state)
    {
        var source = AvailableFoodSource(state.Position);
        if (source is null || !IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange))
        {
            AppendEvent("harvest_failed", $"{inhabitantId}:not_at_available_food");
            return;
        }

        var ecologyResource = worldSystems.Ecology.GetResource(source.Id);
        var harvest = EcologyRules.Harvest(ecologyResource, 1);
        if (!harvest.IsValid || harvest.Resource is null)
        {
            SyncEcologyResourceStates();
            AppendEvent("harvest_failed", $"{inhabitantId}:{harvest.Failure ?? "food_depleted"}");
            return;
        }

        var harvested = source.TreeKind == "orchard" && harvest.Resource.Quantity == 0
            ? harvest.Resource with
            {
                NextRegenerationDay = WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex +
                    harvest.Resource.RegenerationIntervalDays,
            }
            : harvest.Resource;
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources
                    .Select(resource => resource.Id == source.Id ? harvested : resource)
                    .ToArray(),
            },
        };
        SyncEcologyResourceStates();
        ApplyInventoryTransition(inventory => InventoryFixture.AddLot(
            inventory,
            $"food:harvest:{WorldTick:D10}:{inhabitantId}",
            source.Kind == "fruit" ? "fruit" : "food",
            inhabitantId,
            HarvestFoodYield,
            WorldTick));

        AppendEvent("food_harvested", $"{inhabitantId}:{HarvestFoodYield}");
        if (source.TreeKind == "orchard")
            AppendEvent("fruit_harvested", $"{inhabitantId}:{source.Id}:{HarvestFoodYield}:picked");
    }

    private string HouseholdFor(string actor) => society.Checkpoint.GetInhabitant(actor).HouseholdId ?? actor;

    private string ProductionOwnerFor(PlacedBuilding? building, string workerId) =>
        building?.HouseholdId ?? society.Checkpoint.GetInhabitant(workerId).HouseholdId ?? workerId;

    private static bool IsHouseholdBuildingTag(string tag) => tag is "house" or "farmhouse" or "blacksmith";

    private PlacedBuilding? HouseForHousehold(string householdId) => worldSimulation.Buildings
        .Where(building => building.HouseholdId == householdId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("house", StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();

    private GridPoint HouseholdStockPosition(InventoryLot lot) => lot.StorageBuildingId is { } buildingId
        ? worldSimulation.Buildings.Single(building => building.InstanceId == buildingId).Position
        : SettlementStoragePosition;

    private static int HouseholdStockInteractionRange(InventoryLot lot) =>
        lot.StorageBuildingId is null ? ResourceInteractionRange : 0;

    private InventoryLot? AvailableSharedFood(string actor) =>
        society.Checkpoint.GetInhabitant(actor).HouseholdId is not null && MayCollectSharedFood(actor)
        ? PreferredFood(HouseholdFor(actor), actor).FirstOrDefault(lot =>
            (lot.StorageBuildingId is null ||
             society.Checkpoint.GetInhabitant(actor).HouseholdId == lot.OwnerId) &&
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, HouseholdStockPosition(lot),
                HouseholdStockInteractionRange(lot)).Count > 0)
        : null;

    private void CollectSharedFood(string inhabitantId, PlaytestInhabitantState state)
    {
        if (AvailableSharedFood(inhabitantId) is not { } lot)
            return;
        var supplyPoint = HouseholdStockPosition(lot);
        var interactionRange = HouseholdStockInteractionRange(lot);
        if (!IsWithinInteractionRange(state.Position, supplyPoint, interactionRange))
        {
            MoveToward(inhabitantId, state, supplyPoint, "household_food", interactionRange);
            return;
        }

        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(
            inventory, $"household-food:{WorldTick}:{inhabitantId}", HouseholdFor(inhabitantId), inhabitantId,
            lot.Id, 1, "household_food_share"));
        AppendEvent("household_food_collected", $"{inhabitantId}:{lot.Id}:1");
    }

    private void ConsumeFood(string inhabitantId, PlaytestInhabitantState state)
    {
        var lot = PreferredFood(inhabitantId, inhabitantId).FirstOrDefault();
        if (lot is null)
        {
            AppendEvent("consumption_failed", $"{inhabitantId}:no_food");
            return;
        }

        society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint, inhabitantId, lot.Id, 1));
        inhabitants[inhabitantId] = state with
        {
            HungerBasisPoints = Math.Min(10_000, state.HungerBasisPoints + 3_000),
            Survival = AfterMeal(state, lot)
        };
        AppendEvent("food_consumed", inhabitantId);
    }

    private List<CognitionCandidate> CreateCandidates(
        string inhabitantId,
        PlaytestInhabitantState state)
    {
        var candidates = new List<CognitionCandidate>();
        var instruction = PendingInstructionFor(inhabitantId);
        var instructionCandidate = instruction is null ? null : InstructionCandidate(instruction.Text);

        var hasFood = society.Checkpoint.Inventory.Lots.Any(item =>
            item.OwnerId == inhabitantId && IsEdibleFood(item.ItemKind) && AvailableLotQuantity(item) > 0);
        if (hasFood && state.HungerBasisPoints < 8_500)
        {
            candidates.Add(new CognitionCandidate("consume_food", "Eat one carried food item.", 0));
        }

        if (instructionCandidate == "consume_food" && hasFood && !candidates.Any(item => item.Id == "consume_food"))
        {
            candidates.Add(new CognitionCandidate("consume_food", "Follow the owner's food instruction.", 0));
        }

        var foodSource = AvailableFoodSource(state.Position);
        var foodPriority = state.HungerBasisPoints < 2_500 ? 2 : 5;
        var shouldGatherFood = !hasFood && state.HungerBasisPoints < 7_000;
        var sharedFood = shouldGatherFood ? AvailableSharedFood(inhabitantId) : null;
        if (sharedFood is not null && contentRegistry.ExportState().Packages.Any(package =>
                package.Manifest.PackageId == StarterContent.PackageId && package.Lifecycle == ContentPackageLifecycle.Active))
        {
            candidates.Add(new CognitionCandidate("collect_shared_food",
                "Collect one available household food serving from its store, then eat it.",
                foodPriority - 1, HouseholdId));
        }
        if (shouldGatherFood && foodSource is not null &&
            IsWithinInteractionRange(state.Position, foodSource.Position, ResourceInteractionRange))
        {
            candidates.Add(new CognitionCandidate(
                "harvest_food",
                "Gather several food servings from the nearby food source.",
                foodPriority,
                foodSource.Id));
        }
        else if (shouldGatherFood && foodSource is not null)
        {
            candidates.Add(new CognitionCandidate(
                "seek_food",
                "Travel within gathering range of an available food source.",
                foodPriority,
                foodSource.Id));
        }

        if (instructionCandidate == "seek_food" && foodSource is not null &&
            !candidates.Any(item => item.Id == "seek_food"))
        {
            candidates.Add(new CognitionCandidate("seek_food", "Follow the owner's travel instruction.", 0, foodSource.Id));
        }

        if (instructionCandidate == "harvest_food" &&
            foodSource is not null &&
            IsWithinInteractionRange(state.Position, foodSource.Position, ResourceInteractionRange) &&
            !candidates.Any(item => item.Id == "harvest_food"))
        {
            candidates.Add(new CognitionCandidate("harvest_food", "Follow the owner's harvest instruction.", 0, foodSource.Id));
        }

        AddSurvivalCandidates(candidates, inhabitantId, state);
        AddDependentCareCandidates(candidates, inhabitantId);
        if (state.HungerBasisPoints >= 3_500 && !NeedsUrgentWarmth(state) && ChildResident(inhabitantId))
        {
            AddChildCandidates(candidates, inhabitantId, state);
        }
        if (state.HungerBasisPoints >= 2_500 && AdultResident(inhabitantId))
        {
            var inhabitant = society.Checkpoint.GetInhabitant(inhabitantId);
            AddBuildCandidates(candidates, inhabitant, state);
            AddHouseHaulCandidate(candidates, inhabitantId, state);
            AddWarehouseStockCandidate(candidates, inhabitantId, state);
            AddFarmGrainCandidate(candidates, inhabitantId, state);
            AddFarmFlourCandidate(candidates, inhabitantId, state);
            AddBlacksmithStockCandidate(candidates, inhabitantId, state);
            AddBlacksmithOreCandidates(candidates, inhabitantId, state);
            AddCraftToolCandidates(candidates, inhabitantId);
            AddInhabitantBuildingDesignCandidates(candidates, inhabitant, state);
            AddProjectAssistanceCandidates(candidates, inhabitantId);
            AddForestryCandidates(candidates, inhabitantId, state);
            AddTradeCandidates(candidates, inhabitantId);
            AddKnowledgeCandidates(candidates, inhabitantId, state);
            AddCouncilCandidates(candidates, inhabitantId);
            AddLearningCandidates(candidates, inhabitantId);
            AddFamilyCandidates(candidates, inhabitantId);
            AddParenthoodCandidates(candidates, inhabitantId);
            AddExplorationCandidate(candidates, inhabitantId, state);
        }

        candidates.Add(new CognitionCandidate("safe_idle", "Continue safely without starting a new task.", 100));
        return candidates;
    }

    private void AddBuildCandidates(
        List<CognitionCandidate> candidates,
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState state)
    {
        var canBuildStructures = inhabitant.CurrentRole == SocietyWorkRole.Builder ||
            state.Aspiration.Contains("build", StringComparison.OrdinalIgnoreCase);
        if (canBuildStructures)
        {
            var layout = CreateTownLayoutContext(inhabitant.Id);
            foreach (var definition in worldContent.Buildings)
            {
                if (definition.PackageDigest == LegacyStarterDigest && definition.LocalId == "storage" &&
                    worldContent.Buildings.Any(building => building.Tags.Contains("warehouse", StringComparer.Ordinal)))
                    continue;
                if (definition.Tags.Contains("warehouse", StringComparer.Ordinal) &&
                    (TownForResident(inhabitant.Id) is not { } townId ||
                     worldSimulation.Buildings.Any(building => building.TownId == townId &&
                         worldContent.Buildings.Any(existing => existing.CanonicalId == building.DefinitionId &&
                             existing.Tags.Contains("warehouse", StringComparer.Ordinal)))))
                    continue;
                if (definition.Tags.Contains("house", StringComparer.Ordinal) &&
                    (inhabitant.HouseholdId is null || HouseForHousehold(inhabitant.HouseholdId) is not null))
                    continue;
                if (definition.Tags.Contains("farmhouse", StringComparer.Ordinal) && inhabitant.HouseholdId is null)
                    continue;
                if (definition.Tags.Contains("blacksmith", StringComparer.Ordinal) &&
                    (inhabitant.HouseholdId is null || TownForResident(inhabitant.Id) is null))
                    continue;
                if (definition.PackageDigest == LegacyStarterDigest && definition.LocalId == "shelter" &&
                    worldContent.Buildings.Any(building => building.Tags.Contains("house", StringComparer.Ordinal)))
                    continue;
                if (NeedsUrgentWarmth(state) && !definition.Tags.Any(tag => tag is "shelter" or "warmth" or "cooking"))
                {
                    continue;
                }
                var instanceId = BuildInstanceId(inhabitant.Id, definition);
                var constructionOwner = definition.Tags.Any(IsHouseholdBuildingTag)
                    ? inhabitant.HouseholdId : inhabitant.HouseholdId is null ? inhabitant.Id : null;
                if (worldSimulation.Buildings.Any(item => item.InstanceId == instanceId) ||
                    !CanAcquireProjectInputs(definition.BuildCosts, constructionOwner, inhabitant.Id))
                {
                    continue;
                }

                var sites = TownLayoutService.RankConstructionSites(layout, definition);
                for (var rank = 0; rank < sites.Count; rank++)
                {
                    var site = sites[rank];
                    var description = string.Join(" ", site.Reasons.Select(reason => reason.Description));
                    candidates.Add(new CognitionCandidate(
                        TownConstructionCandidateIds.Building(definition.CanonicalId, site.Position),
                        $"Plan {definition.DisplayName} at ({site.Position.X}, {site.Position.Y}): {description}",
                        20 + rank,
                        $"build-site:{site.Position.X},{site.Position.Y}"));
                }
            }
        }

        var canGrow = inhabitant.CurrentRole == SocietyWorkRole.Farmer ||
            state.Aspiration.Contains("self-sufficient", StringComparison.OrdinalIgnoreCase);
        var canProduce = inhabitant.CurrentRole is SocietyWorkRole.Farmer or
            SocietyWorkRole.Builder or SocietyWorkRole.Trader or SocietyWorkRole.Organizer;
        foreach (var recipe in worldContent.Recipes.Where(item =>
                     !item.Outputs.Any(output => output.ResourceId == "bedding") &&
                     (item.IsCrop ? canGrow : canProduce)))
        {
            if (NeedsUrgentWarmth(state) && !recipe.Outputs.Any(output => output.ResourceId == "clothing"))
            {
                continue;
            }
            var householdWorkstation = recipe.WorkstationBuildingId is { } workstationId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == workstationId &&
                    definition.Tags.Any(IsHouseholdBuildingTag));
            var recipeOwner = householdWorkstation || recipe.Tags.Contains("grain", StringComparer.Ordinal)
                ? inhabitant.HouseholdId : inhabitant.HouseholdId is null ? inhabitant.Id : null;
            if (!NeedsRecipeOutput(recipe, recipeOwner) || !CanAcquireProjectInputs(recipe.Inputs, recipeOwner, inhabitant.Id) ||
                !TryFindRecipeSite(recipe, out var siteId, out var position, inhabitant.Id) ||
                householdWorkstation &&
                (recipeOwner is null || !HasIngredientsAtBuilding(recipe.Inputs, recipeOwner, siteId)))
            {
                continue;
            }

            candidates.Add(new CognitionCandidate(
                $"build:recipe:{recipe.CanonicalId}",
                $"Build {recipe.DisplayName} at a valid site.",
                recipe.IsCrop ? 20 : WeatherExposure(state.Position) > 0 && recipe.Outputs.Any(output => output.ResourceId == "clothing") ? 25 : 30,
                $"build-site:{position.X},{position.Y}"));
        }
    }

    private static int PriorityFor(PlaytestInhabitantState state) =>
        state.HungerBasisPoints < 2_500 || NeedsUrgentWarmth(state) ? 20 : 0;

    private OwnerQueuedInstruction? PendingInstructionFor(string inhabitantId) =>
        instructionsByIdempotency.Values
            .Where(item => item.TargetInhabitantId == inhabitantId &&
                !completedInstructionIds.Contains(item.InstructionId))
            .OrderBy(item => item.SubmissionSequence)
            .FirstOrDefault();

    private static string? InstructionCandidate(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Contains("harvest") || normalized.Contains("gather") || normalized.Contains("berry"))
        {
            return normalized.Contains("harvest") || normalized.Contains("gather")
                ? "harvest_food"
                : "seek_food";
        }

        if (normalized.Contains("eat") || normalized.Contains("food") || normalized.Contains("hungry"))
        {
            return "consume_food";
        }

        if (normalized.Contains("go") || normalized.Contains("travel") || normalized.Contains("move"))
        {
            return "seek_food";
        }

        return null;
    }

    private static bool Matches(OwnerQueuedInstruction existing, OwnerInstructionRequest request) =>
        existing.IssuerId == request.IssuerId.Trim() &&
        existing.TargetInhabitantId == request.TargetInhabitantId.Trim() &&
        existing.Kind == request.Kind &&
        existing.Text == request.Text.Trim();

    private static void ValidateInstructionRequest(OwnerInstructionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IssuerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetInhabitantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
    }

    private static string ToWireValue(OwnerInstructionKind kind) => kind switch
    {
        OwnerInstructionKind.Suggestive => "suggestive",
        OwnerInstructionKind.MustDo => "must_do",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ObservationDigest(
        string inhabitantId,
        PlaytestInhabitantState state,
        IReadOnlyList<CognitionCandidate> candidates,
        IReadOnlyList<CognitionMemoryExcerpt> memories,
        IReadOnlyList<CognitionMemoryCompactionCandidate> compactionCandidates,
        IReadOnlyList<CognitionKnowledgeFact> knownMapFacts)
    {
        var text = new StringBuilder()
            .Append("clankerworld.private-world-observation/v1|")
            .Append(inhabitantId).Append('|')
            .Append(state.Position.X).Append(',').Append(state.Position.Y).Append('|')
            .Append(state.HungerBasisPoints).Append('|')
            .Append(string.Join(',', candidates.Select(candidate => candidate.Id)));
        foreach (var memory in memories)
            text.Append('|').Append(memory.Id.Length).Append(':').Append(memory.Id)
                .Append('|').Append(memory.SourceTick)
                .Append('|').Append(memory.SubjectId.Length).Append(':').Append(memory.SubjectId)
                .Append('|').Append(memory.Summary.Length).Append(':').Append(memory.Summary)
                .Append('|').Append(memory.Kind)
                .Append('|').Append(memory.Visibility ?? "none")
                .Append('|').Append(memory.Provenance ?? "none")
                .Append('|').Append(memory.ConfidenceBasisPoints ?? -1)
                .Append('|').Append(memory.SourceAgentId ?? "none")
                .Append('|').Append(memory.SourceEventId ?? -1)
                .Append('|').Append(memory.IsCorrected)
                .Append('|').Append(memory.ImportanceBasisPoints)
                .Append('|').Append(memory.ImportanceConfidenceBasisPoints);
        foreach (var memory in compactionCandidates)
            text.Append('|').Append(memory.Kind)
                .Append('|').Append(memory.Id.Length).Append(':').Append(memory.Id)
                .Append('|').Append(memory.SourceTick)
                .Append('|').Append(memory.SubjectId.Length).Append(':').Append(memory.SubjectId)
                .Append('|').Append(memory.Summary.Length).Append(':').Append(memory.Summary)
                .Append('|').Append(memory.Visibility ?? "none")
                .Append('|').Append(memory.Provenance ?? "none")
                .Append('|').Append(memory.ConfidenceBasisPoints ?? -1)
                .Append('|').Append(memory.SourceAgentId ?? "none")
                .Append('|').Append(memory.SourceEventId ?? -1)
                .Append('|').Append(memory.IsCorrected);
        foreach (var fact in knownMapFacts)
            text.Append("|map=").Append(fact.X).Append(',').Append(fact.Y).Append('|')
                .Append(fact.Terrain).Append('|').Append(string.Join(',', fact.ResourceKinds))
                .Append('|').Append(fact.DiscovererId).Append('|').Append(fact.Acquisition)
                .Append('|').Append(fact.LearnedTick);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))}";
    }

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

    private static void ValidatePhysicalInventoryLocations(InventoryCheckpoint inventory,
        WorldContentSimulationState simulation, DeclarativeWorldContentState content,
        IReadOnlyList<SocietyInhabitant> inhabitants)
    {
        var buildings = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var people = inhabitants.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var lot in inventory.Lots)
        {
            if (lot.StorageBuildingId is { } storageId)
            {
                if (!buildings.TryGetValue(storageId, out var storage) ||
                    !definitions.TryGetValue(storage.DefinitionId, out var definition) ||
                    !(storage.HouseholdId == lot.OwnerId && definition.Tags.Any(IsHouseholdBuildingTag) ||
                      storage.TownId == lot.OwnerId && storage.HouseholdId is null && !WarehouseFoodKinds.Contains(lot.ItemKind) &&
                      definition.Tags.Contains("warehouse", StringComparer.Ordinal)))
                    throw new InvalidDataException($"Inventory lot '{lot.Id}' has an invalid building storage location.");
            }
            if (lot.DeliveryBuildingId is { } deliveryId &&
                (!buildings.TryGetValue(deliveryId, out var destination) ||
                 destination.HouseholdId is null ||
                 !people.TryGetValue(lot.OwnerId, out var carrier) ||
                 carrier.Status != SocietyInhabitantStatus.Active ||
                 carrier.HouseholdId != destination.HouseholdId))
                throw new InvalidDataException($"Inventory lot '{lot.Id}' has an invalid House delivery destination.");
        }
    }

    internal static void ValidateStateForCodec(PrivateWorldRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion < 10 && (state.Society.Society.LifeClock is not null ||
            state.Society.Society.Inhabitants.Any(person => person.BirthLifeTick is not null)))
        {
            throw new InvalidDataException("Biological life pacing requires private-world schema 10.");
        }
        if (state.SchemaVersion is < 1 or > StateSchemaVersion || string.IsNullOrWhiteSpace(state.WorldSeed) || state.EventHistoryFloor < 0)
        {
            throw new InvalidDataException("The private-world runtime state schema or seed is invalid.");
        }
        if (state.SchemaVersion < 20 && state.Society.Society.Beliefs is { Count: > 0 })
            throw new InvalidDataException("Agent belief history requires private-world schema 20.");
        if (state.SchemaVersion < 22 && state.Society.Society.MemoryCompactions is { Count: > 0 })
            throw new InvalidDataException("Agent memory compaction indexes require private-world schema 22.");
        if (state.SchemaVersion < 24 && state.RoadTiles is { Count: > 0 })
            throw new InvalidDataException("Generated Roads require private-world schema 24.");
        if (state.JevPolicyRevision < 0 || state.JevEnabled is null && state.JevPolicyRevision != 0 ||
            state.SchemaVersion < 15 && (state.JevEnabled is not null || state.JevPolicyRevision != 0))
            throw new InvalidDataException("The saved Jev routing policy is invalid.");
        if (state.SchemaVersion < 16 && state.FounderSetup is not null)
            throw new InvalidDataException("Founder setup requires private-world schema 16.");
        if (state.Geography is not null &&
            (state.SchemaVersion < 17 || state.FounderSetup is null ||
             !string.Equals(state.Geography.Seed, state.WorldSeed, StringComparison.Ordinal)))
            throw new InvalidDataException("Generated geography does not match the saved world setup.");
        ValidateFounderSetup(state.FounderSetup, state.Society.Society);
        if (state.SchemaVersion >= 21 && state.Towns is null)
            throw new InvalidDataException("Private-world schema 21 requires authoritative Town state.");
        if (state.SchemaVersion >= 23 && state.Knowledge is null)
            throw new InvalidDataException("Private-world schema 23 requires agent map-knowledge state.");
        if (state.SchemaVersion >= 24 && state.RoadTiles is null)
            throw new InvalidDataException("Private-world schema 24 requires authoritative Road state.");
        var hasArchivedEvents = state.EventHistoryFloor > 0 || state.Society.Society.EventHistoryFloor > 0 ||
            state.Society.Society.Inventory.EventHistoryFloor > 0 || state.Society.Cognition.EventHistoryFloor > 0 ||
            state.Society.Cognition.Runtimes.Any(runtime => runtime.EventHistoryFloor > 0);
        if ((hasArchivedEvents && state.HistoryArchiveHead is null) ||
            (state.SchemaVersion < 4 && (hasArchivedEvents || state.HistoryArchiveHead is not null)) ||
            (state.HistoryArchiveHead is { } head && (head.Length != 64 || head.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))))
        {
            throw new InvalidDataException("The private-world history reference or schema is invalid.");
        }

        if (!MapAcceptance.Validate(state.Map, allowEmptyCamp: state.FounderSetup is not null).IsValid)
        {
            throw new InvalidDataException("The private-world runtime contains an invalid map.");
        }

        using var society = SocietyWorldRuntime.Restore(state.Society);
        ValidateBeliefEventSources(state.Society.Society.Beliefs ?? [], state.Events, state.EventHistoryFloor);
        AgentKnowledgeRules.Validate(state.Knowledge, state.Map, society.Checkpoint,
            society.Checkpoint.WorldTick, state.SchemaVersion);
        ValidateSurvival(state);
        ValidateCouncil(state);
        ValidateLessons(state);
        foreach (var person in state.Inhabitants)
        {
            ValidateProficiency(person, state.SchemaVersion);
            ValidateSocialStanding(person, state.Society.Society.Inhabitants.Select(item => item.Id),
                state.SchemaVersion, state.Society.Society.WorldTick);
            ValidatePrivateThoughts(person.RecentThoughts, state.SchemaVersion, state.Society.Society.WorldTick);
            ValidateExploration(person.Exploration, state.Map, state.Society.Society.WorldTick);
        }
        ValidateParenthood(state);
        ContentPackageRegistry.Restore(state.Content);
        if (state.SchemaVersion >= 3 && state.Content is null)
        {
            throw new InvalidDataException("The current private-world schema requires content governance state.");
        }

        if (state.WorldSystems is not null)
        {
            WorldSystemsRules.Validate(state.WorldSystems);
            if (state.WorldSystems.WorldTick != state.Society.Society.WorldTick ||
                !string.Equals(state.WorldSystems.WorldSeed, state.WorldSeed, StringComparison.Ordinal) ||
                state.WorldSystems.Config.TicksPerDay != state.Society.Society.Config.TicksPerWorldDay ||
                state.WorldSystems.Config.DaysPerYear != state.Society.Society.Config.DaysPerWorldYear)
            {
                throw new InvalidDataException("The saved world systems do not match the society clock, calendar, or seed.");
            }
        }
        else if (state.SchemaVersion >= 3)
        {
            throw new InvalidDataException("The current private-world schema requires richer-systems state.");
        }

        state.WorldContent?.Validate();
        if (state.SchemaVersion >= 3 &&
            (state.WorldContent is null || state.WorldSimulation is null || state.AssetReservations is null))
        {
            throw new InvalidDataException(
                "The current private-world schema requires content simulation and world asset reservation state.");
        }

        if (state.WorldContent is not null && state.WorldSimulation is not null)
        {
            WorldContentSimulationRules.Validate(
                state.WorldSimulation,
                state.WorldContent,
                state.Map,
                state.Society.Society.WorldTick);
            ValidatePhysicalInventoryLocations(state.Society.Society.Inventory, state.WorldSimulation,
                state.WorldContent, state.Society.Society.Inhabitants);
            if (state.WorldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
                !state.Society.Society.Households.Any(household => household.Id == householdId)))
                throw new InvalidDataException("A House references a missing household.");
            ValidateTowns(state.Towns ?? MigrateTowns(state), state.Map, state.FounderSetup,
                state.Society.Society, state.WorldSimulation, state.WorldContent);
            ValidateRoads(state.RoadTiles ?? [], state.Map, state.FounderSetup);
        }

        if (state.AssetReservations is not null)
        {
            WorldAssetReservationLedger.Restore(state.AssetReservations);
        }
        var activeIds = state.Society.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .OrderBy(item => item, StringComparer.Ordinal);
        var physicalIds = state.Inhabitants
            .Select(item => item.InhabitantId)
            .OrderBy(item => item, StringComparer.Ordinal);
        if (!activeIds.SequenceEqual(physicalIds))
        {
            throw new InvalidDataException("The saved private-world populations disagree.");
        }
        ValidateDeceasedArchive(state.DeceasedInhabitants ?? [], state.Society.Society, state.Map, state.SchemaVersion);
        foreach (var inhabitant in state.Inhabitants)
        {
            if (inhabitant.Project is { } project)
            {
                if (state.SchemaVersion < 5)
                {
                    throw new InvalidDataException("Settlement projects require save schema 5.");
                }
                ValidateProject(project, state.Society.Society.WorldTick);
            }
        }
    }

    private static void ValidateDeceasedArchive(
        IEnumerable<PlaytestDeceasedInhabitantState> archive,
        SocietyCheckpoint society,
        SeededMap map,
        int schemaVersion)
    {
        var archived = archive.ToArray();
        if (archived.Length > 0 && schemaVersion < 13)
            throw new InvalidDataException("Deceased inhabitant archives require private-world schema 13.");
        if (archived.Select(item => item.InhabitantId).Distinct(StringComparer.Ordinal).Count() != archived.Length)
            throw new InvalidDataException("The deceased inhabitant archive contains duplicate identities.");
        var deceasedById = society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Dead)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var person in archived)
        {
            if (!deceasedById.TryGetValue(person.InhabitantId, out var deceased) ||
                deceased.DeathTick != person.DeathTick || person.DeathTick < 0 || person.DeathTick > society.WorldTick ||
                person.AgeAtDeath < 0 || person.LastPhysical.InhabitantId != person.InhabitantId ||
                !map.IsPassable(person.LastPhysical.Position) ||
                person.LastPhysical.HungerBasisPoints is < 0 or > 10_000)
                throw new InvalidDataException("The deceased inhabitant archive contains an invalid final state.");
            ValidatePrivateThoughts(person.LastPhysical.RecentThoughts, schemaVersion, person.DeathTick);
            ValidateExploration(person.LastPhysical.Exploration, map, person.DeathTick);
        }
    }

    private static void ValidatePrivateThoughts(
        IReadOnlyList<PlaytestPrivateThought>? thoughts, int schemaVersion, long latestTick)
    {
        if (thoughts is null) return;
        if (schemaVersion < 14 || thoughts.Count > MaximumRecentThoughts)
            throw new InvalidDataException("The private-thought history version or size is invalid.");
        long previousTick = -1;
        foreach (var thought in thoughts)
        {
            if (thought is null || thought.Text is null ||
                thought.WorldTick < 0 || thought.WorldTick < previousTick || thought.WorldTick > latestTick ||
                CognitionDecisionResponse.NormalizePrivateThought(thought.Text) != thought.Text)
                throw new InvalidDataException("The private-thought history contains an invalid entry.");
            previousTick = thought.WorldTick;
        }
    }

    private static string NormalizeRequiredText(string? value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}

public static class PrivateWorldRuntimeCodec
{
    private const string LegacyHeader = "clankerworld.private-world-runtime/v1";
    private const string ChunkedHeader = "clankerworld.private-world-runtime/v2";
    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
    private static readonly JsonSerializerOptions ChunkedOptions = CreateChunkedOptions();

    private static JsonSerializerOptions CreateChunkedOptions()
    {
        var options = new JsonSerializerOptions(LegacyOptions);
        options.Converters.Add(new PrivateWorldTerrainChunkCodec());
        return options;
    }

    public static byte[] Encode(PrivateWorldRuntimeState state)
    {
        PrivateWorldRuntime.ValidateStateForCodec(state);
        var chunked = state.SchemaVersion >= 19;
        return JsonSerializer.SerializeToUtf8Bytes(
            new RuntimeDocument(chunked ? ChunkedHeader : LegacyHeader, state),
            chunked ? ChunkedOptions : LegacyOptions);
    }

    public static PrivateWorldRuntimeState Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var header = JsonDocument.Parse(bytes);
            if (!header.RootElement.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The private-world runtime checkpoint format is missing.");
            var version = format.GetString();
            if (version is not (LegacyHeader or ChunkedHeader))
                throw new InvalidDataException("The private-world runtime checkpoint format is unsupported.");
            var document = JsonSerializer.Deserialize<RuntimeDocument>(bytes.Span,
                version == ChunkedHeader ? ChunkedOptions : LegacyOptions)
                ?? throw new InvalidDataException("The private-world runtime checkpoint is empty.");
            if (document.State is null ||
                (version == ChunkedHeader && document.State.SchemaVersion < 19) ||
                (version == LegacyHeader && document.State.SchemaVersion >= 19))
                throw new InvalidDataException("The private-world runtime checkpoint schema and terrain format disagree.");
            PrivateWorldRuntime.ValidateStateForCodec(document.State);
            return document.State;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The private-world runtime checkpoint JSON is damaged.", exception);
        }
    }

    private sealed record RuntimeDocument(string Format, PrivateWorldRuntimeState State);
}
