using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Persistence;
using System.Text.Json.Serialization;

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
    string Climate, string Elevation, string Hydrology, string Surface, string Vegetation)
{
    public string? Fertility { get; init; }
}
public sealed record ViewerFarmField(ViewerPosition Position, string HouseholdId, string Stage, string? Crop,
    int Fertility, string? WorkerId, int? WorkRemaining);
public sealed record ViewerGroundStock(ViewerPosition Position, string OwnerId, string Kind, int Quantity);
public sealed record ViewerWorldPreview(ViewerPackedTerrain Terrain, ViewerPosition Camp,
    string ManifestDigest, int ResourceSites = 0)
{
    public ViewerPackedMapLayers? PackedMapLayers { get; init; }
    public string? MapLayersDigest { get; init; }
    public GeographyCandidateReport? Coverage { get; init; }
    public IReadOnlyList<GeographyCandidateReport> Candidates { get; init; } = [];
    public IReadOnlyList<GeographyCandidateFailure> FailedCandidates { get; init; } = [];
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

/// <summary>
/// Developer tools: the route an agent is walking, as the server planned it on
/// its latest step. <see cref="Steps"/> holds at most the first
/// <see cref="StepLimit"/> tiles still ahead; <see cref="StepCount"/> counts all of them.
/// </summary>
public sealed record ViewerPlannedRoute(
    string Reason,
    ViewerPosition Destination,
    IReadOnlyList<ViewerPosition> Steps,
    int StepCount)
{
    public const int StepLimit = 256;
}

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
/// <summary>
/// The world's saved calendar, including its season lengths, so the game can
/// name the season and day of any tick the same way the world does.
/// </summary>
public sealed record ViewerCalendarPace(
    int TicksPerDay,
    int DaysPerYear,
    int SpringDays,
    int SummerDays,
    int AutumnDays,
    int WinterDays);

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

    /// <summary>Developer tools only; null when the agent is not walking anywhere.</summary>
    public ViewerPlannedRoute? PlannedRoute { get; init; }

    public ViewerProject? Project { get; init; }
    public ViewerSurvival? Survival { get; init; }
    public ViewerEquipment? Equipment { get; init; }
    public string? MedicalCareNote { get; init; }
    public ViewerLesson? Lesson { get; init; }
    public ViewerProficiency? Proficiency { get; init; }
    public IReadOnlyList<ViewerSkill> Skills { get; init; } = [];
    public IReadOnlyList<ViewerSocialStanding> SocialStanding { get; init; } = [];

    public IReadOnlyList<string> SocialNotes { get; init; } = [];

    public IReadOnlyList<ViewerInhabitantRelationship> Relationships { get; init; } = [];

    public IReadOnlyList<ViewerPrivateThought> RecentPrivateThoughts { get; init; } = [];

    public IReadOnlyList<ViewerAgentMemory> RecentMemories { get; init; } = [];

    public IReadOnlyList<ViewerAgentBelief> RecentBeliefs { get; init; } = [];

    public IReadOnlyList<ViewerAgentKnowledgeFact> RecentKnowledgeFacts { get; init; } = [];

    public IReadOnlyList<ViewerAgentKnowledgeArtifact> KnowledgeArtifacts { get; init; } = [];

    /// <summary>A dead agent's will on their historical profile; null for the living.</summary>
    public ViewerFinalWill? FinalWill { get; init; }
}

/// <param name="Status">pending, accepted or default (household inheritance).</param>
/// <param name="Split">equal or items for an accepted will; otherwise null.</param>
/// <param name="Heirs">Each named heir and the goods the will leaves them, in the will's order.</param>
/// <param name="FinalWords">Words the agent left for the people who inherit, if any.</param>
public sealed record ViewerFinalWill(string Status, string? Split, IReadOnlyList<ViewerWillHeir> Heirs, string? FinalWords);
public sealed record ViewerWillHeir(string Id, string Name, bool IsTown, IReadOnlyList<ViewerInventoryEntry> Items);

public sealed record ViewerProject(string Label, string Stage, int WorkDone, int WorkRequired, string? Blocker, long StartedTick);
public sealed record ViewerSurvival(int WarmthBasisPoints, int IllnessBasisPoints, bool HasClothing, bool HasTool,
    int NutritionBasisPoints, string? LastMealKind);
public sealed record ViewerEquipment(int CarriedQuantity, int Capacity, string? ClothingKind,
    int? ClothingConditionPercent, string? CarryAidKind, int? CarryAidConditionPercent,
    string? RepairItemKind, int RepairWorkDone, int RepairWorkRequired, string? OrnamentKind = null);

public sealed record ViewerStockpile(string OwnerId, string Name, IReadOnlyList<ViewerInventoryEntry> Items);
// Keep the existing observation field name so older owner clients can still display a lesson.
public sealed record ViewerLesson(string TeacherName, [property: JsonPropertyName("role")] string Skill,
    string Stage, int Progress, int Required);
public sealed record ViewerSkill(string Kind, long LearnedTick, string? TeacherId, string? TeacherName);
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
    long SubmissionSequence,
    long? ObservedTick = null,
    string? ObserverReply = null,
    ViewerInstructionOrder? Order = null);

public sealed record ViewerInstructionOrder(
    string Action,
    string Status,
    int RequestedUnits,
    int CompletedUnits,
    string ProgressUnit,
    bool RepeatUntilCancelled,
    string? TargetFoodKind = null,
    string? TargetResourceId = null,
    int? TargetX = null,
    int? TargetY = null,
    string? BlockedReason = null);

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
    IReadOnlyList<ViewerInventoryEntry>? StoredItems = null,
    ViewerPosition? Entrance = null,
    int? StorageCapacity = null,
    int StoredQuantity = 0,
    int FootprintRevision = 0,
    IReadOnlyList<string>? InvitedGuests = null,
    string? ExpansionState = null,
    string? ExpansionFailure = null,
    int? ResidentLimit = null,
    int PermanentResidentCount = 0,
    bool HasDominantFamily = false,
    bool IsOvercrowded = false)
{
    public IReadOnlyList<ViewerBusinessTrade> Trades { get; init; } = [];
    public bool AllowsHouseholdOwner { get; init; }
}

public sealed record ViewerBusinessTrade(string OfferId, string BuyerName, string GoodsKind, int GoodsQuantity,
    string PaymentKind, int PaymentQuantity, string Status, string? CancellationReason);

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
    IReadOnlyList<ViewerPosition> BorderTiles)
{
    public ViewerTownGovernance? Governance { get; init; }
    public IReadOnlyList<ViewerTownProject> Projects { get; init; } = [];
    public IReadOnlyList<ViewerMarket> Markets { get; init; } = [];
}

public sealed record ViewerMarket(string Id, string ProjectId, string HallBuildingId,
    ViewerPosition Site, ViewerPosition PlazaPosition, int PlazaWidth, int PlazaHeight,
    IReadOnlyList<ViewerMarketStall> Stalls, long? RemovedTick = null);
public sealed record ViewerMarketStall(string BuildingInstanceId, int SlotIndex, ViewerPosition Position,
    string? SellerId, string? SellerName, long? OccupiedTick,
    IReadOnlyList<ViewerMarketStock> Stock, IReadOnlyList<ViewerMarketTrade> Trades);
public sealed record ViewerMarketStock(string LotId, string? ParentLotId, string OwnerId, string OwnerName,
    string Kind, int Quantity, int AvailableQuantity);
public sealed record ViewerMarketTrade(string OfferId, string SellerId, string SellerName,
    string GoodsOwnerId, string GoodsOwnerName, string PaymentOwnerId, string PaymentOwnerName,
    string BuyerId, string BuyerName, string GoodsKind, int GoodsQuantity, string PaymentKind,
    int PaymentQuantity, string Status, string? CancellationReason,
    bool SellerAccepted = false, bool BuyerAccepted = false);

public sealed record ViewerCivicProposal(string Id, string Kind, string Text, string Status, int Yes, int No,
    int RequiredYes, long DeadlineTick)
{
    public ViewerTownProjectPlan? Project { get; init; }
}
public sealed record ViewerTownProjectBudget(string Kind, int Quantity);
public sealed record ViewerTownProjectPlan(string Name, string ProposerId, string ProposerName,
    string DefinitionId, string DisplayName, ViewerPosition Site, ViewerPosition Entrance,
    int Width, int Height, IReadOnlyList<ViewerTownProjectBudget> Budget);
public sealed record ViewerTownProjectMaterial(string Kind, int Budget, int Supplied);
public sealed record ViewerTownProject(string Id, string ProposalId, string Name,
    string ProposerId, string ProposerName, string DefinitionId, string DisplayName,
    ViewerPosition Site, ViewerPosition Entrance, int Width, int Height,
    IReadOnlyList<ViewerTownProjectMaterial> Materials, int WorkDone, int WorkRequired,
    string Stage, string? Blocker, string? CompletedBuildingId, ViewerCivicProposal Approval);
public sealed record ViewerCivicCandidate(string Id, string Name, int Votes);
public sealed record ViewerTownElection(string Id, string Kind, string Stage, int Seats, long DeadlineTick,
    IReadOnlyList<ViewerCivicCandidate> Candidates, IReadOnlyList<string> SettledNames);
public sealed record ViewerTownGovernance(string Form, string Fallback, IReadOnlyList<string> MemberNames,
    long? TermEndTick, long RetryTick, IReadOnlyList<string> WillingCandidateNames,
    IReadOnlyList<ViewerCivicProposal> Proposals, ViewerTownElection? Election)
{
    public ViewerTownElection? LatestElection { get; init; }
}

public sealed record ViewerTownLandTitle(string Id, string TownId, IReadOnlyList<ViewerPosition> Tiles,
    long RecordedTick);

public sealed record ViewerHouseholdLandUseRight(string Id, string TownId, string HouseholdId,
    IReadOnlyList<ViewerPosition> Tiles, long GrantedTick, string GrantSource, long? AgreedEndTick);

public sealed record ViewerHouseholdLandUseRequest(string Id, string TownId, string HouseholdId,
    string RequestedByAgentId, IReadOnlyList<ViewerPosition> Tiles, long RequestedTick,
    long? AgreedEndTick, bool IsDisputed, IReadOnlyList<string> ClaimantHouseholdIds,
    IReadOnlyList<ViewerPosition> DisputedTiles);

public sealed record ViewerWeatherRegion(int X, int Y, string Weather, int? SoilMoisture = null);

/// <summary>
/// A saved bridge exactly as movement uses it: its deck tiles are walkable
/// only along <see cref="Axis"/> between the two entrances.
/// </summary>
public sealed record ViewerBridge(
    string Id,
    string Design,
    string Trigger,
    string Axis,
    IReadOnlyList<ViewerPosition> Entrances,
    IReadOnlyList<ViewerPosition> Span,
    long BuiltTick);

public sealed record ViewerConversationTurn(
    string Id,
    string SpeakerId,
    string SpeakerName,
    string Text,
    long WorldTick,
    IReadOnlyList<string> ListenerIds,
    bool IsWrapUp);

public sealed record ViewerConversation(
    string Id,
    string InitiatorId,
    string InitiatorName,
    string InviteeId,
    string InviteeName,
    string Status,
    string? Interruption,
    string? Outcome,
    long CreatedTick,
    long LastUpdatedTick,
    IReadOnlyList<ViewerConversationTurn> Turns);

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
    public IReadOnlyList<ViewerFarmField> Fields { get; init; } = [];
    public IReadOnlyList<ViewerGroundStock> GroundStocks { get; init; } = [];
    public IReadOnlyList<ViewerStockpile> Stockpiles { get; init; } = [];
    public ViewerCouncil? Council { get; init; }
    public int? LifePaceRate { get; init; }
    public ViewerCalendarPace? CalendarPace { get; init; }
    /// <summary>
    /// How dark the world is now, decided by the host from the world clock:
    /// 0 in daylight, 10,000 at full night, between them at dusk and dawn.
    /// Absent for a world without a calendar.
    /// </summary>
    public int? DarknessBasisPoints { get; init; }
    public bool? JevEnabled { get; init; }
    /// <summary>The current saved rule state, independent of retained event history.</summary>
    public bool? ContinuityRuleActive { get; init; }
    public ViewerFounderSetup? FounderSetup { get; init; }
    public IReadOnlyList<ViewerTown> Towns { get; init; } = [];
    public IReadOnlyList<ViewerTownLandTitle> TownLandTitles { get; init; } = [];
    public IReadOnlyList<ViewerHouseholdLandUseRight> HouseholdLandUseRights { get; init; } = [];
    public IReadOnlyList<ViewerHouseholdLandUseRequest> HouseholdLandUseRequests { get; init; } = [];
    public IReadOnlyList<ViewerPosition> RoadTiles { get; init; } = [];
    public IReadOnlyList<ViewerBridge> Bridges { get; init; } = [];
    public int WeatherRegionSize { get; init; } = 32;
    public IReadOnlyList<ViewerWeatherRegion> WeatherRegions { get; init; } = [];
    /// <summary>Developer tools: how long the host took to work out the latest tick; null before one runs.</summary>
    public double? LastTickMilliseconds { get; init; }
    /// <summary>
    /// The inspectable population projection. <see cref="Actor"/> remains for
    /// backwards-compatible Phase 2 diagnostic clients.
    /// </summary>
    public IReadOnlyList<ViewerInhabitant> Inhabitants { get; init; } = [];

    /// <summary>Recent public dialogue only; private thoughts never enter this projection.</summary>
    public IReadOnlyList<ViewerConversation> Conversations { get; init; } = [];

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
