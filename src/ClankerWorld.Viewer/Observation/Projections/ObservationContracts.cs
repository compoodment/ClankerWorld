using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Persistence;

namespace ClankerWorld.Viewer.Observation;

public sealed record ProtocolVersion(int Major, int Minor);

public sealed record ViewerHandshake(
    ProtocolVersion Protocol,
    IReadOnlyList<string> ServerCapabilities,
    IReadOnlyList<string> ClientCapabilities);

public sealed record ViewerPosition(int X, int Y);

public sealed record ViewerTile(int X, int Y, string Terrain);

/// <summary>Row-major terrain-kind bytes, base64-encoded for owner JSON.</summary>
public sealed record ViewerPackedTerrain(int Width, int Height, string Encoding, string Data);
/// <summary>Independent row-major byte layers; terrain remains a compatibility projection.</summary>
public sealed record ViewerPackedMapLayers(int Width, int Height, string Encoding,
    string Climate, string Elevation, string Hydrology, string Surface, string Vegetation);
public sealed record ViewerWorldPreview(ViewerPackedTerrain Terrain, ViewerPosition Camp,
    string ManifestDigest, int ResourceSites = 0)
{
    public ViewerPackedMapLayers? PackedMapLayers { get; init; }
    public string? MapLayersDigest { get; init; }
}

public sealed record ViewerMapObject(string Id, string Kind, ViewerPosition Position);

public sealed record ViewerResource(string Id, string Kind, ViewerPosition Position, bool IsRenewable, string State,
    int? Quantity = null, int? Capacity = null, int? RegenerationAmount = null,
    int? RegenerationIntervalDays = null, string? RegenerationSeason = null, string? TreeKind = null,
    bool IsPlanted = false, string? TreeStage = null, string? NaturalObjectKind = null);

public sealed record ViewerActor(
    string Id,
    ViewerPosition Position,
    int HungerBasisPoints,
    int FoodItems,
    int WoodItems);

public sealed record ViewerInventoryEntry(string Kind, int Quantity);

public sealed record ViewerDecisionFactor(string Key, string Detail);

public sealed record ViewerRoute(
    string Status,
    string? DestinationId,
    ViewerPosition? Destination,
    IReadOnlyList<ViewerPosition> Steps,
    string TopologyManifestDigest);

public sealed record ViewerSpatialKnowledge(
    ViewerPosition CurrentTile,
    IReadOnlyList<ViewerPosition> PerceivedTiles,
    IReadOnlyList<ViewerPosition> KnownTiles);

/// <summary>
/// A safe owner-facing summary of what an inhabitant is currently trying to
/// do. This is an intention label, never private model chain-of-thought.
/// </summary>
public sealed record ViewerPublicIntention(
    string CandidateId,
    string Summary,
    string Provider,
    long WorldTick);

public sealed record ViewerInhabitantRelationship(
    string RelationshipId,
    string OtherPartyId,
    string Type,
    string State,
    string PrivacyClass,
    long EffectiveTick,
    string? Direction = null);

public sealed record ViewerPrivateThought(long WorldTick, string Text);
public sealed record ViewerAgentMemory(long WorldTick, string SubjectId, string SubjectName, string Summary, string Visibility);
public sealed record ViewerAgentBelief(
    long WorldTick,
    string Statement,
    string Provenance,
    int ConfidenceBasisPoints,
    string? SourceAgentId,
    string? SourceAgentName,
    long? SourceEventId,
    string? AboutInhabitantId,
    bool IsCorrected,
    long? CorrectedTick);
public sealed record ViewerAgentKnowledgeFact(
    long WorldTick,
    int X,
    int Y,
    string Terrain,
    IReadOnlyList<string> ResourceKinds,
    string DiscovererName,
    string Acquisition,
    string? SourceAgentName);
public sealed record ViewerKnowledgeSite(int X, int Y, string Terrain, IReadOnlyList<string> ResourceKinds, string DiscovererName);
public sealed record ViewerAgentKnowledgeArtifact(
    string Id,
    string Kind,
    string Title,
    long CreatedTick,
    string CreatorName,
    IReadOnlyList<ViewerKnowledgeSite> Sites);
public sealed record ViewerCalendarPace(int TicksPerDay, int DaysPerYear);

/// <summary>
/// An inspection projection, never an editable actor record. A founder draft
/// is visible as such but is not a living simulation actor yet.
/// </summary>
public sealed record ViewerInhabitant(
    string Id,
    string DisplayName,
    string Lifecycle,
    ViewerPosition Position,
    int HungerBasisPoints,
    IReadOnlyList<ViewerInventoryEntry> Inventory,
    IReadOnlyList<ViewerDecisionFactor> DecisionFactors,
    ViewerRoute Route,
    ViewerSpatialKnowledge SpatialKnowledge,
    bool IsDraft)
{
    public ViewerPublicIntention? PublicIntention { get; init; }

    public ViewerProject? Project { get; init; }
    public ViewerSurvival? Survival { get; init; }
    public ViewerLesson? Lesson { get; init; }
    public ViewerProficiency? Proficiency { get; init; }
    public IReadOnlyList<ViewerSocialStanding> SocialStanding { get; init; } = [];

    public IReadOnlyList<string> SocialNotes { get; init; } = [];

    public IReadOnlyList<ViewerInhabitantRelationship> Relationships { get; init; } = [];

    public IReadOnlyList<ViewerPrivateThought> RecentPrivateThoughts { get; init; } = [];

    public IReadOnlyList<ViewerAgentMemory> RecentMemories { get; init; } = [];

    public IReadOnlyList<ViewerAgentBelief> RecentBeliefs { get; init; } = [];

    public IReadOnlyList<ViewerAgentKnowledgeFact> RecentKnowledgeFacts { get; init; } = [];

    public IReadOnlyList<ViewerAgentKnowledgeArtifact> KnowledgeArtifacts { get; init; } = [];
}

public sealed record ViewerProject(string Label, string Stage, int WorkDone, int WorkRequired, string? Blocker, long StartedTick);
public sealed record ViewerSurvival(int WarmthBasisPoints, int IllnessBasisPoints, bool HasClothing, bool HasTool,
    int NutritionBasisPoints, string? LastMealKind);

public sealed record ViewerStockpile(string OwnerId, string Name, IReadOnlyList<ViewerInventoryEntry> Items);
public sealed record ViewerLesson(string TeacherName, string Role, string Stage, int Progress, int Required);
public sealed record ViewerProficiency(int Building, int Farming, int Crafting);
public sealed record ViewerSocialStanding(string SubjectId, string SubjectName, int Trust);

public sealed record ViewerInstruction(
    string InstructionId,
    string TargetInhabitantId,
    string Kind,
    string Text,
    string State,
    long SubmittedTick,
    long RunEpoch,
    long SubmissionSequence);

public sealed record ViewerCognitionEvent(long EventId, long WorldTick, string Kind, string Detail);

public sealed record ViewerInhabitantDecision(
    string InhabitantId, string Provider, string CandidateId, long WorldTick,
    double Confidence, string? Model, int? InputTokens, int? OutputTokens,
    string? Role = null, long? LatencyMilliseconds = null, bool FellBack = false);

public sealed record ViewerCognition(
    string Provider,
    bool IsPaused,
    string? InFlightRequestId,
    string? CurrentCandidateId,
    string? CurrentDecisionProvider,
    IReadOnlyList<ViewerCognitionEvent> Events,
    IReadOnlyList<ViewerInhabitantDecision>? Decisions = null);

public sealed record ViewerContentPackage(
    string PackageId,
    string Version,
    string PackageDigest,
    string Lifecycle,
    string? LockDigest,
    long? ValidationTick,
    long? StagedTick,
    long? ActivationTick,
    string? ManifestDigest = null,
    string? DisplayName = null,
    string? ProposedByInhabitantId = null);

public sealed record ViewerContentGovernanceEvent(
    long EventId,
    long WorldTick,
    string PackageId,
    string Kind,
    string Detail);

public sealed record ViewerWorldSystemsSummary(
    string Season,
    string Weather,
    int EcologyResourceCount,
    int FactionCount,
    int CurrencyAccountCount,
    int CultureCount,
    int ChunkCount,
    int BuildingDefinitionCount = 0,
    int RecipeDefinitionCount = 0,
    int PlacedBuildingCount = 0,
    int ProductionJobCount = 0,
    int DistinctAssetReservationCount = 0,
    long DurableAssetReservationBytes = 0,
    long DecodedAssetCacheBytes = 0,
    long GpuAssetBytes = 0,
    int AssetRenderUnits = 0);

public sealed record ViewerPlacedBuilding(
    string InstanceId,
    string DefinitionId,
    ViewerPosition Position,
    long PlacedTick,
    string? DisplayName = null,
    IReadOnlyList<string>? Tags = null,
    int Width = 1,
    int Height = 1,
    string? TownId = null,
    string? HouseholdId = null,
    IReadOnlyList<ViewerInventoryEntry>? StoredItems = null);

public sealed record ViewerProductionJob(
    string JobId,
    string RecipeId,
    string BuildingInstanceId,
    string WorkerId,
    long StartedTick,
    long CompletionTick,
    string State);

public sealed record ViewerAuthoringState(
    bool IsPaused,
    long RunEpoch,
    long Revision,
    long TopologyRevision,
    string InitialMapManifestDigest,
    string CurrentMapManifestDigest,
    string Weather,
    string Season,
    IReadOnlyList<string> ApprovedAssetReferences);

public sealed record ViewerEvent(long EventId, long WorldTick, string Kind, string Detail, ViewerPosition? Position = null);

public sealed record ViewerFounderSetup(int Required, int Placed, bool Started)
{
    public bool CanChooseTownSite { get; init; }
    public bool HasAcceptedTownSite { get; init; }
    public string? LastFounderId { get; init; }
}

public sealed record ViewerTown(
    string Id,
    string Name,
    string FoundingState,
    long FoundedTick,
    IReadOnlyList<string> ResidentIds,
    IReadOnlyList<string> AssignedBuildingIds,
    IReadOnlyList<ViewerPosition> BorderTiles);

public sealed record ViewerWeatherRegion(int X, int Y, string Weather, int? SoilMoisture = null);

public sealed record ViewerWorldSnapshot(
    string WorldId,
    long WorldTick,
    string MapManifestDigest,
    IReadOnlyList<ViewerTile> Tiles,
    IReadOnlyList<ViewerMapObject> Objects,
    IReadOnlyList<ViewerResource> Resources,
    ViewerActor? Actor,
    long LatestEventId)
{
    public ViewerPackedTerrain? PackedTerrain { get; init; }
    public ViewerPackedMapLayers? PackedMapLayers { get; init; }
    public string? MapLayersDigest { get; init; }
    public bool WrapsEastWest { get; init; }
    public IReadOnlyList<ViewerStockpile> Stockpiles { get; init; } = [];
    public ViewerCouncil? Council { get; init; }
    public int? LifePaceRate { get; init; }
    public ViewerCalendarPace? CalendarPace { get; init; }
    public bool? JevEnabled { get; init; }
    public ViewerFounderSetup? FounderSetup { get; init; }
    public IReadOnlyList<ViewerTown> Towns { get; init; } = [];
    public IReadOnlyList<ViewerPosition> RoadTiles { get; init; } = [];
    public int WeatherRegionSize { get; init; } = 32;
    public IReadOnlyList<ViewerWeatherRegion> WeatherRegions { get; init; } = [];
    /// <summary>
    /// The inspectable population projection. <see cref="Actor"/> remains for
    /// backwards-compatible Phase 2 diagnostic clients.
    /// </summary>
    public IReadOnlyList<ViewerInhabitant> Inhabitants { get; init; } = [];

    /// <summary>
    /// Present for the Phase 2 composite host. Its separate topology revision
    /// makes it clear when paused authoring differs from the protected fixture.
    /// </summary>
    public ViewerAuthoringState? Authoring { get; init; }

    public IReadOnlyList<ViewerInstruction> Instructions { get; init; } = [];

    public ViewerCognition? Cognition { get; init; }

    public IReadOnlyList<ViewerContentPackage> ContentPackages { get; init; } = [];

    public IReadOnlyList<ViewerContentGovernanceEvent> ContentEvents { get; init; } = [];

    public ViewerWorldSystemsSummary? WorldSystems { get; init; }

    public IReadOnlyList<ViewerPlacedBuilding> PlacedBuildings { get; init; } = [];

    public IReadOnlyList<ViewerProductionJob> ProductionJobs { get; init; } = [];
}

public sealed record ViewerCouncil(string? StewardName, string FoodPolicy, string? ProposedPolicy, int Approvals, int Rejections, int Voters);

public sealed record ViewerEventSlice(long SnapshotTick, long AfterEventId, IReadOnlyList<ViewerEvent> Events,
    long EventHistoryFloor = 0, bool ResetRequired = false);

/// <summary>
/// A reconnect response is one server-side capture, not a race between a
/// client's separate snapshot and event-history requests.
/// </summary>
public sealed record ViewerReconnectBaseline(ViewerWorldSnapshot Snapshot, ViewerEventSlice Events);

/// <summary>
/// Owns the static deterministic sample exposed by the first browser slice.
/// It creates protocol DTOs from the core's immutable harness state rather than
/// exposing simulation records to clients.
/// </summary>
public sealed class SeededWorldObservationStore
{
    public const string SampleSeed = "camp-alpha";

    private static readonly string[] ServerCapabilities =
    [
        "snapshot.read.v1",
        "event-replay.read.v1",
        "reconnect-baseline.read.v1",
        "seeded-map.read.v1",
    ];

    private static readonly string[] ClientCapabilities =
    [
        "snapshot.read.v1",
        "event-replay.read.v1",
        "reconnect-baseline.read.v1",
    ];

    private readonly HarnessWorld? staticWorld;
    private readonly LiveSeededWorldRuntime? runtime;

    public SeededWorldObservationStore()
        : this(ScriptedHarness.RunEntireSequence(SampleSeed))
    {
    }

    public SeededWorldObservationStore(HarnessWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        staticWorld = world;
    }

    public SeededWorldObservationStore(LiveSeededWorldRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ViewerHandshake GetHandshake() => new(
        new ProtocolVersion(Major: 1, Minor: 0),
        ServerCapabilities.ToArray(),
        ClientCapabilities.ToArray());

    public ViewerWorldSnapshot GetSnapshot() => ToSnapshot(Capture(0).World);

    public ViewerEventSlice GetEventsAfter(long afterEventId)
    {
        var capture = Capture(afterEventId);
        return new ViewerEventSlice(
            capture.World.Identity.WorldTick,
            capture.AfterEventId,
            capture.Events.Select(ToEvent).ToArray());
    }

    public ViewerReconnectBaseline GetReconnectBaseline(long afterEventId)
    {
        var capture = Capture(afterEventId);
        var snapshot = ToSnapshot(capture.World);
        var events = new ViewerEventSlice(
            snapshot.WorldTick,
            capture.AfterEventId,
            capture.Events.Select(ToEvent).ToArray());
        return new ViewerReconnectBaseline(snapshot, events);
    }

    private LiveWorldCapture Capture(long afterEventId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterEventId);
        if (runtime is not null)
        {
            return runtime.Capture(afterEventId);
        }

        var world = staticWorld ?? throw new InvalidOperationException("An observation store needs a world source.");
        return new LiveWorldCapture(
            world,
            afterEventId,
            world.Events
                .Where(worldEvent => worldEvent.EventId > afterEventId)
                .OrderBy(worldEvent => worldEvent.EventId)
                .ToArray());
    }

    private static ViewerWorldSnapshot ToSnapshot(HarnessWorld world) => new(
        world.Identity.WorldId,
        world.Identity.WorldTick,
        world.Map.ManifestDigest,
        world.Map.Tiles
            .OrderBy(tile => tile.Position.Y)
            .ThenBy(tile => tile.Position.X)
            .Select(tile => new ViewerTile(tile.Position.X, tile.Position.Y, ToWireValue(tile.Terrain)))
            .ToArray(),
        world.Map.CampObjects
            .OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal)
            .Select(mapObject => new ViewerMapObject(mapObject.Id, mapObject.Kind, ToPosition(mapObject.Position)))
            .ToArray(),
        world.Map.Resources
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => new ViewerResource(
                resource.Id,
                resource.Kind,
                ToPosition(resource.Position),
                resource.IsRenewable,
                ToWireValue(world.GetResource(resource.Id).State)))
            .ToArray(),
        new ViewerActor(
            world.Actor.Id,
            ToPosition(world.Actor.Position),
            world.Actor.HungerBasisPoints,
            world.Actor.FoodItems,
            world.Actor.WoodItems),
        world.Events.Count == 0 ? 0 : world.Events.Max(worldEvent => worldEvent.EventId));

    private static ViewerEvent ToEvent(PersistenceEvent worldEvent) => new(
        worldEvent.EventId,
        worldEvent.WorldTick,
        ToWireValue(worldEvent.Kind),
        worldEvent.Detail ?? string.Empty);

    private static ViewerPosition ToPosition(GridPoint point) => new(point.X, point.Y);

    private static string ToWireValue(TerrainKind terrain) => terrain switch
    {
        TerrainKind.Meadow => "meadow",
        TerrainKind.Water => "water",
        TerrainKind.Mountain => "mountain",
        TerrainKind.River => "river",
        TerrainKind.Lake => "lake",
        TerrainKind.Ocean => "ocean",
        TerrainKind.Peak => "peak",
        TerrainKind.Sand => "sand",
        TerrainKind.Forest => "forest",
        TerrainKind.Snow => "snow",
        _ => throw new ArgumentOutOfRangeException(nameof(terrain)),
    };

    private static string ToWireValue(ResourceState state) => state switch
    {
        ResourceState.Available => "available",
        ResourceState.Depleted => "depleted",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string ToWireValue(PersistenceEventKind kind) => kind switch
    {
        PersistenceEventKind.CounterAdjusted => "counter_adjusted",
        PersistenceEventKind.MigrationApplied => "migration_applied",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
