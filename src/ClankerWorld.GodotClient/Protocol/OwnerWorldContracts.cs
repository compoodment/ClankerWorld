using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.GodotClient.UI;

// These client-owned DTOs mirror the public owner projection. Keeping them in
// the Godot project prevents the renderer from taking a project reference on
// the authoritative server or simulation assemblies.
public sealed record OwnerDeletionAction(string Kind, string Id, string WorldId, DateTimeOffset? ExpectedCreatedUtc = null);
public sealed record OwnerDeletionReceipt(string Id, bool CleanupComplete);

public sealed record OwnerWorldProtocolVersion(int Major, int Minor);

public sealed record OwnerWorldHandshake(
    OwnerWorldProtocolVersion Protocol,
    IReadOnlyList<string> ServerCapabilities,
    IReadOnlyList<string> ClientCapabilities);

public sealed record OwnerWorldPosition(int X, int Y);

public sealed record OwnerWorldTile(int X, int Y, string Terrain);
public sealed record OwnerWorldPackedTerrain(int Width, int Height, string Encoding, string Data);
public sealed record OwnerWorldPackedMapLayers(int Width, int Height, string Encoding,
    string Climate, string Elevation, string Hydrology, string Surface, string Vegetation)
{
    public string? Fertility { get; init; }
}
public sealed record OwnerWorldFarmField(OwnerWorldPosition Position, string HouseholdId, string Stage, string? Crop,
    int Fertility, string? WorkerId, int? WorkRemaining);
public sealed record OwnerWorldGroundStock(OwnerWorldPosition Position, string OwnerId, string Kind, int Quantity);

public sealed record OwnerWorldObject(string Id, string Kind, OwnerWorldPosition Position);

public sealed record OwnerWorldResource(
    string Id,
    string Kind,
    OwnerWorldPosition Position,
    bool IsRenewable,
    string State,
    int? Quantity = null,
    int? Capacity = null,
    int? RegenerationAmount = null,
    int? RegenerationIntervalDays = null,
    string? RegenerationSeason = null,
    string? TreeKind = null,
    bool IsPlanted = false,
    string? TreeStage = null,
    string? NaturalObjectKind = null);

public sealed record OwnerWorldInventoryEntry(string Kind, int Quantity);

public sealed record OwnerWorldDecisionFactor(string Key, string Detail);

public sealed record OwnerWorldRoute(
    string Status,
    string? DestinationId,
    OwnerWorldPosition? Destination,
    IReadOnlyList<OwnerWorldPosition> Steps,
    string TopologyManifestDigest);

/// <summary>Developer tools: the route an agent is walking, as the server planned it; at most the first 256 steps.</summary>
public sealed record OwnerWorldPlannedRoute(
    string Reason,
    OwnerWorldPosition Destination,
    IReadOnlyList<OwnerWorldPosition> Steps,
    int StepCount);

public sealed record OwnerWorldSpatialKnowledge(
    OwnerWorldPosition CurrentTile,
    IReadOnlyList<OwnerWorldPosition> PerceivedTiles,
    IReadOnlyList<OwnerWorldPosition> KnownTiles);

public sealed record OwnerWorldPublicIntention(
    string CandidateId,
    string Summary,
    string Provider,
    long WorldTick);

public sealed record OwnerWorldInhabitantRelationship(
    string RelationshipId,
    string OtherPartyId,
    string Type,
    string State,
    string PrivacyClass,
    long EffectiveTick,
    string? Direction = null);

public sealed record OwnerWorldPrivateThought(long WorldTick, string Text);
public sealed record OwnerWorldAgentMemory(long WorldTick, string SubjectId, string SubjectName, string Summary, string Visibility);
public sealed record OwnerWorldAgentBelief(
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
public sealed record OwnerWorldKnowledgeFact(
    long WorldTick,
    int X,
    int Y,
    string Terrain,
    IReadOnlyList<string> ResourceKinds,
    string DiscovererName,
    string Acquisition,
    string? SourceAgentName);
public sealed record OwnerWorldKnowledgeSite(int X, int Y, string Terrain, IReadOnlyList<string> ResourceKinds, string DiscovererName);
public sealed record OwnerWorldKnowledgeArtifact(
    string Id,
    string Kind,
    string Title,
    long CreatedTick,
    string CreatorName,
    IReadOnlyList<OwnerWorldKnowledgeSite> Sites);
/// <summary>
/// The world's saved calendar. Season lengths come from the same saved values
/// the world uses for its seasons; an older host leaves them at zero.
/// </summary>
public sealed record OwnerWorldCalendarPace(
    int TicksPerDay,
    int DaysPerYear,
    int SpringDays = 0,
    int SummerDays = 0,
    int AutumnDays = 0,
    int WinterDays = 0);
public sealed record OwnerFounderSetup(int Required, int Placed, bool Started)
{
    public bool CanChooseTownSite { get; init; }
    public bool HasAcceptedTownSite { get; init; }
    public string? LastFounderId { get; init; }
}
public sealed record OwnerWorldTown(
    string Id,
    string Name,
    string FoundingState,
    long FoundedTick,
    IReadOnlyList<string> ResidentIds,
    IReadOnlyList<string> AssignedBuildingIds,
    IReadOnlyList<OwnerWorldPosition> BorderTiles)
{
    public OwnerTownGovernance? Governance { get; init; }
}

public sealed record OwnerCivicProposal(string Id, string Kind, string Text, string Status, int Yes, int No,
    int RequiredYes, long DeadlineTick);
public sealed record OwnerCivicCandidate(string Id, string Name, int Votes);
public sealed record OwnerTownElection(string Id, string Kind, string Stage, int Seats, long DeadlineTick,
    IReadOnlyList<OwnerCivicCandidate> Candidates, IReadOnlyList<string> SettledNames);
public sealed record OwnerTownGovernance(string Form, string Fallback, IReadOnlyList<string> MemberNames,
    long? TermEndTick, long RetryTick, IReadOnlyList<string> WillingCandidateNames,
    IReadOnlyList<OwnerCivicProposal> Proposals, OwnerTownElection? Election)
{
    public OwnerTownElection? LatestElection { get; init; }
}

public sealed record OwnerWorldLandTitle(string Id, string TownId, IReadOnlyList<OwnerWorldPosition> Tiles,
    long RecordedTick);

public sealed record OwnerWorldHouseholdLandUseRight(string Id, string TownId, string HouseholdId,
    IReadOnlyList<OwnerWorldPosition> Tiles, long GrantedTick, string GrantSource, long? AgreedEndTick);

public sealed record OwnerWorldHouseholdLandUseRequest(string Id, string TownId, string HouseholdId,
    string RequestedByAgentId, IReadOnlyList<OwnerWorldPosition> Tiles, long RequestedTick,
    long? AgreedEndTick, bool IsDisputed, IReadOnlyList<string> ClaimantHouseholdIds,
    IReadOnlyList<OwnerWorldPosition> DisputedTiles);

public sealed record OwnerWorldInhabitant(
    string Id,
    string DisplayName,
    string Lifecycle,
    OwnerWorldPosition Position,
    int HungerBasisPoints,
    IReadOnlyList<OwnerWorldInventoryEntry> Inventory,
    IReadOnlyList<OwnerWorldDecisionFactor> DecisionFactors,
    OwnerWorldRoute Route,
    OwnerWorldSpatialKnowledge SpatialKnowledge,
    bool IsDraft)
{
    public OwnerWorldPublicIntention? PublicIntention { get; init; }

    /// <summary>Developer tools only; null when the agent is not walking anywhere or the host does not report it.</summary>
    public OwnerWorldPlannedRoute? PlannedRoute { get; init; }

    public OwnerWorldProject? Project { get; init; }
    public OwnerWorldSurvival? Survival { get; init; }
    public OwnerWorldEquipment? Equipment { get; init; }
    public string? MedicalCareNote { get; init; }
    public OwnerWorldLesson? Lesson { get; init; }
    public OwnerWorldProficiency? Proficiency { get; init; }
    public IReadOnlyList<OwnerWorldSkill>? Skills { get; init; }
    public IReadOnlyList<OwnerWorldSocialStanding> SocialStanding { get; init; } = [];

    public IReadOnlyList<string> SocialNotes { get; init; } = [];

    public IReadOnlyList<OwnerWorldInhabitantRelationship> Relationships { get; init; } = [];

    public IReadOnlyList<OwnerWorldPrivateThought> RecentPrivateThoughts { get; init; } = [];

    public IReadOnlyList<OwnerWorldAgentMemory> RecentMemories { get; init; } = [];

    public IReadOnlyList<OwnerWorldAgentBelief> RecentBeliefs { get; init; } = [];

    public IReadOnlyList<OwnerWorldKnowledgeFact> RecentKnowledgeFacts { get; init; } = [];

    public IReadOnlyList<OwnerWorldKnowledgeArtifact> KnowledgeArtifacts { get; init; } = [];
}

public sealed record OwnerWorldProject(string Label, string Stage, int WorkDone, int WorkRequired, string? Blocker, long StartedTick);
public sealed record OwnerWorldSurvival(int WarmthBasisPoints, int IllnessBasisPoints, bool HasClothing, bool HasTool,
    int NutritionBasisPoints, string? LastMealKind);
public sealed record OwnerWorldEquipment(int CarriedQuantity, int Capacity, string? ClothingKind,
    int? ClothingConditionPercent, string? CarryAidKind, int? CarryAidConditionPercent,
    string? RepairItemKind, int RepairWorkDone, int RepairWorkRequired, string? OrnamentKind = null);

public sealed record OwnerWorldStockpile(string OwnerId, string Name, IReadOnlyList<OwnerWorldInventoryEntry> Items);
public sealed record OwnerWorldLesson(string TeacherName, [property: JsonPropertyName("role")] string Skill,
    string Stage, int Progress, int Required);
public sealed record OwnerWorldSkill(string Kind, long LearnedTick, string? TeacherId, string? TeacherName);
public sealed record OwnerWorldProficiency(int Building, int Farming, int Crafting);
public sealed record OwnerWorldSocialStanding(string SubjectId, string SubjectName, int Trust);

public sealed record OwnerWorldInstruction(
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
    OwnerWorldInstructionOrder? Order = null);

public sealed record OwnerWorldInstructionOrder(
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
    string? BlockedReason = null,
    string? TargetMaterialKind = null);

public sealed record OwnerWorldCognitionEvent(long EventId, long WorldTick, string Kind, string Detail);

public sealed record OwnerWorldInhabitantDecision(
    string InhabitantId, string Provider, string CandidateId, long WorldTick,
    double Confidence, string? Model, int? InputTokens, int? OutputTokens,
    string? Role = null, long? LatencyMilliseconds = null, bool FellBack = false);

public sealed record OwnerWorldCognition(
    string Provider,
    bool IsPaused,
    string? InFlightRequestId,
    string? CurrentCandidateId,
    string? CurrentDecisionProvider,
    IReadOnlyList<OwnerWorldCognitionEvent> Events,
    IReadOnlyList<OwnerWorldInhabitantDecision>? Decisions = null);

public sealed record OwnerWorldContentPackage(
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

public sealed record OwnerWorldContentGovernanceEvent(
    long EventId,
    long WorldTick,
    string PackageId,
    string Kind,
    string Detail);

public sealed record OwnerWorldSystemsSummary(
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

public sealed record OwnerWorldPlacedBuilding(
    string InstanceId,
    string DefinitionId,
    OwnerWorldPosition Position,
    long PlacedTick,
    string? DisplayName = null,
    IReadOnlyList<string>? Tags = null,
    int Width = 1,
    int Height = 1,
    string? TownId = null,
    string? HouseholdId = null,
    IReadOnlyList<OwnerWorldInventoryEntry>? StoredItems = null,
    OwnerWorldPosition? Entrance = null,
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
    public IReadOnlyList<OwnerWorldBusinessTrade> Trades { get; init; } = [];
    public bool AllowsHouseholdOwner { get; init; }
}

public sealed record OwnerWorldBusinessTrade(string OfferId, string BuyerName, string GoodsKind, int GoodsQuantity,
    string PaymentKind, int PaymentQuantity, string Status, string? CancellationReason);

public sealed record OwnerWorldProductionJob(
    string JobId,
    string RecipeId,
    string BuildingInstanceId,
    string WorkerId,
    long StartedTick,
    long CompletionTick,
    string State);

public sealed record OwnerWeatherRegion(int X, int Y, string Weather, int? SoilMoisture = null);

/// <summary>A saved bridge: the same crossing movement uses, drawn and inspected from these tiles.</summary>
public sealed record OwnerWorldBridge(
    string Id,
    string Design,
    string Trigger,
    string Axis,
    IReadOnlyList<OwnerWorldPosition> Entrances,
    IReadOnlyList<OwnerWorldPosition> Span,
    long BuiltTick);

public sealed record OwnerWorldConversationTurn(
    string Id,
    string SpeakerId,
    string SpeakerName,
    string Text,
    long WorldTick,
    IReadOnlyList<string> ListenerIds,
    bool IsWrapUp);

public sealed record OwnerWorldConversation(
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
    IReadOnlyList<OwnerWorldConversationTurn> Turns);

public sealed record OwnerWorldAuthoringState(
    bool IsPaused,
    long RunEpoch,
    long Revision,
    long TopologyRevision,
    string InitialMapManifestDigest,
    string CurrentMapManifestDigest,
    string Weather,
    string Season,
    IReadOnlyList<string> ApprovedAssetReferences);

public sealed record OwnerWorldActor(
    string Id,
    OwnerWorldPosition Position,
    int HungerBasisPoints,
    int FoodItems,
    int WoodItems);

public sealed record OwnerWorldSnapshot(
    string WorldId,
    long WorldTick,
    string MapManifestDigest,
    IReadOnlyList<OwnerWorldTile> Tiles,
    IReadOnlyList<OwnerWorldObject> Objects,
    IReadOnlyList<OwnerWorldResource> Resources,
    OwnerWorldActor? Actor,
    long LatestEventId)
{
    public OwnerWorldPackedTerrain? PackedTerrain { get; init; }
    public OwnerWorldPackedMapLayers? PackedMapLayers { get; init; }
    public string? MapLayersDigest { get; init; }
    public bool WrapsEastWest { get; init; }
    public IReadOnlyList<OwnerWorldFarmField> Fields { get; init; } = [];
    public IReadOnlyList<OwnerWorldGroundStock> GroundStocks { get; init; } = [];
    public IReadOnlyList<OwnerWorldStockpile> Stockpiles { get; init; } = [];
    public OwnerWorldCouncil? Council { get; init; }
    public int? LifePaceRate { get; init; }
    public OwnerWorldCalendarPace? CalendarPace { get; init; }
    public bool? JevEnabled { get; init; }
    public bool? ContinuityRuleActive { get; init; }
    public OwnerFounderSetup? FounderSetup { get; init; }
    public IReadOnlyList<OwnerWorldTown> Towns { get; init; } = [];
    public IReadOnlyList<OwnerWorldLandTitle> TownLandTitles { get; init; } = [];
    public IReadOnlyList<OwnerWorldHouseholdLandUseRight> HouseholdLandUseRights { get; init; } = [];
    public IReadOnlyList<OwnerWorldHouseholdLandUseRequest> HouseholdLandUseRequests { get; init; } = [];
    public IReadOnlyList<OwnerWorldPosition> RoadTiles { get; init; } = [];
    public IReadOnlyList<OwnerWorldBridge> Bridges { get; init; } = [];
    public int WeatherRegionSize { get; init; } = 32;
    public IReadOnlyList<OwnerWeatherRegion> WeatherRegions { get; init; } = [];
    /// <summary>Developer tools: how long the host took to work out the latest tick; null when not reported.</summary>
    public double? LastTickMilliseconds { get; init; }
    public IReadOnlyList<OwnerWorldInhabitant> Inhabitants { get; init; } = [];

    public IReadOnlyList<OwnerWorldConversation> Conversations { get; init; } = [];

    public OwnerWorldAuthoringState? Authoring { get; init; }

    public IReadOnlyList<OwnerWorldInstruction> Instructions { get; init; } = [];

    public OwnerWorldCognition? Cognition { get; init; }

    public IReadOnlyList<OwnerWorldContentPackage> ContentPackages { get; init; } = [];

    public IReadOnlyList<OwnerWorldContentGovernanceEvent> ContentEvents { get; init; } = [];

    public OwnerWorldSystemsSummary? WorldSystems { get; init; }

    public IReadOnlyList<OwnerWorldPlacedBuilding> PlacedBuildings { get; init; } = [];

    public IReadOnlyList<OwnerWorldProductionJob> ProductionJobs { get; init; } = [];
}

public sealed record OwnerWorldEvent(long EventId, long WorldTick, string Kind, string Detail, OwnerWorldPosition? Position = null);

public sealed record OwnerWorldEventSlice(
    long SnapshotTick,
    long AfterEventId,
    IReadOnlyList<OwnerWorldEvent> Events,
    long EventHistoryFloor = 0,
    bool ResetRequired = false);

public sealed record OwnerWorldCouncil(string? StewardName, string FoodPolicy, string? ProposedPolicy, int Approvals, int Rejections, int Voters);

public sealed record OwnerWorldReconnectBaseline(OwnerWorldSnapshot Snapshot, OwnerWorldEventSlice Events);

public sealed record OwnerWorldReconnect(OwnerWorldHandshake Handshake, OwnerWorldReconnectBaseline Baseline);

public sealed record OwnerReconnectAction(long AfterEventId,
    string? KnownTerrainWorldId = null, string? KnownTerrainDigest = null,
    string? KnownMapLayersDigest = null);

public sealed record OwnerControlAction(string Operation);
public sealed record OwnerManualSaveAction(string Operation, string Value);
public sealed record OwnerWorldCreationAction(string Name, string Seed, string Size,
    int WaterPercent, bool WrapEastWest, string ClimateMode = "Balanced",
    string SelectedClimate = "Temperate", bool LatitudeCooling = true,
    string ResourceAbundance = "Normal", string ForestCover = "Normal",
    string MountainRelief = "Normal", string RiverAbundance = "Normal",
    int? CandidateAttempt = null, string? ExpectedManifestDigest = null,
    string? ExpectedMapLayersDigest = null, bool AcceptUnmetTargets = false);
public sealed record CatalogWorld(string Id, string Name, string WorldId, string Seed,
    DateTimeOffset UpdatedUtc, IReadOnlyList<InhabitantProviderAssignment> Assignments,
    WorldAutosaveSettings? AutosaveSettings, string Compatibility = "unknown",
    string? CompatibilityReason = null, WorldThumbnail? Thumbnail = null);
/// <summary>A small picture of a world's terrain for the Load World list, packed like the world's own terrain.</summary>
public sealed record WorldThumbnail(int Width, int Height, string Encoding, string Data);
public sealed record WorldCatalogSnapshot(string ActiveId, IReadOnlyList<CatalogWorld> Worlds);
public sealed record OwnerWorldPreview(OwnerWorldPackedTerrain Terrain, OwnerWorldPosition Camp,
    string ManifestDigest, int ResourceSites = 0)
{
    public OwnerWorldPackedMapLayers? PackedMapLayers { get; init; }
    public string? MapLayersDigest { get; init; }
    public OwnerWorldCandidateReport? Coverage { get; init; }
    public IReadOnlyList<OwnerWorldCandidateReport> Candidates { get; init; } = [];
}
public sealed record OwnerWorldCandidateReport(int Attempt, int DryLandTiles, int ForestTiles,
    int MountainTiles, double ForestPercent, double MountainPercent, int ForestRegionCount,
    int LargestForestRegion, int MountainRegionCount, int LargestMountainRegion,
    bool ForestTargetApplicable, bool MountainTargetApplicable,
    bool ForestTargetMet, bool MountainTargetMet)
{
    public bool TargetsApplicable => ForestTargetApplicable || MountainTargetApplicable;
    public bool MeetsTargets => (!ForestTargetApplicable || ForestTargetMet) &&
        (!MountainTargetApplicable || MountainTargetMet);
    public IReadOnlyList<string> UnmetTargets
    {
        get
        {
            var unmet = new List<string>(2);
            if (ForestTargetApplicable && !ForestTargetMet)
                unmet.Add($"Forest {ForestPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% (target 20–40%)");
            if (MountainTargetApplicable && !MountainTargetMet)
                unmet.Add($"Mountains {MountainPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% (target 5–12%)");
            return unmet;
        }
    }
}
public sealed record ManualWorldSave(string Id, string Name, DateTimeOffset CreatedUtc, long WorldTick,
    bool IsAutosave = false, SaveBranch? Branch = null, string? ContinuedFromId = null,
    DateTimeOffset? ContinuedFromCreatedUtc = null, long BranchPosition = 0);
/// <summary>One version of a world's history; saves from before branches have none.</summary>
public sealed record SaveBranch(string Id, int Number, string? StartedFromId = null,
    string? StartedFromName = null, long? StartedFromTick = null);
public sealed record ManualSaveLoadReceipt(string LoadedId, string BackupId, long WorldTick);
public sealed record ManualSaveOverwriteReceipt(ManualWorldSave Saved, string BackupId);
public sealed record OwnerAutosaveConfigurationAction(bool Enabled, int IntervalMinutes, int RotationCount);
public sealed record WorldAutosaveSettings(string WorldId, bool Enabled, int IntervalMinutes,
    int RotationCount, DateTimeOffset LastSavedUtc, long LastWorldTick);
public sealed record OwnerLifePaceAction(int Rate);
public sealed record OwnerJevAssistanceAction(bool Enabled);

public sealed record OwnerPairingApprovalAction(string PairingId, string PairingCode);

public sealed record OwnerDeviceManagementAction(string DeviceId);

/// <summary>
/// Empty typed body for the challenge-bound paired-device registry query.
/// The fixed canonical payload prevents an unsigned or replayable registry
/// read from becoming an accidental bearer endpoint.
/// </summary>
public sealed record OwnerDeviceListAction;

public sealed record OwnerProviderStatusAction;

public sealed record OwnerUsageStatusAction;

public sealed record OwnerUsageLimitAction(long? AttemptLimit, long AdditionalCalls = 0);

public sealed record OwnerUsageRow(string Provider, string Model, string Role,
    long Attempts, long Completed, long Failed, long Abandoned,
    long InputTokens, long OutputTokens);

public sealed record OwnerUsageStatus(long Attempts, long Completed, long Failed,
    long Abandoned, long InputTokens, long OutputTokens, long? AttemptLimit,
    bool LimitReached, IReadOnlyList<OwnerUsageRow> Rows, string? AccountingError = null);

public sealed record OwnerCredentialSlotDeletionAction(string CredentialSlotId);

public sealed record OwnerCredentialSlotCreationAction(string CredentialSlotId, string Provider, string Label, string ApiKey);

/// <summary>
/// Asks the host for the game's model list for a provider. With
/// <see cref="CheckKey"/>, the host also asks the provider which of those
/// models a key can use. <see cref="ApiKey"/> is a key the owner has just
/// pasted and not saved yet; it is used for this check only. Without it, the
/// named key slot or the provider's saved key is used.
/// </summary>
public sealed record OwnerProviderModelListAction(
    string Provider, string? CredentialSlotId = null, string? ApiKey = null, bool CheckKey = true);

public sealed record OwnerProviderSetupCheckAction(
    string Provider, string Model, string? CredentialSlotId = null, string? ApiKey = null);

public sealed record OwnerProviderSetupCheckResult(string Outcome, string Message, bool IsReady);

/// <summary>One listed model, and whether the checked key can use it.</summary>
public sealed record OwnerProviderModelChoice(string Model, bool Available);

/// <summary>
/// The game's models for a provider, in display order. <see cref="DefaultModel"/>
/// is the game's default for this provider, chosen for a new agent when the
/// key can use it. <see cref="Error"/> explains, in plain words, a key that
/// couldn't be checked; the models are then all shown as usable.
/// </summary>
public sealed record OwnerProviderModelList(
    string Provider, IReadOnlyList<OwnerProviderModelChoice> Models, string DefaultModel, string? Error);

public sealed record OwnerProviderConfigurationAction(
    string Role,
    string Provider,
    string? Model,
    string? ApiKey,
    bool ForgetCredential,
    string? InhabitantId = null,
    string? CredentialSlotId = null,
    string? NewCredentialLabel = null);

public sealed record InhabitantProviderAssignment(
    string InhabitantId,
    string Role,
    string Provider,
    string? Model = null,
    string? CredentialSlotId = null,
    string? SelectionReason = null);

public sealed record OwnerProviderCredentialStatus(string Id, string Provider, string Label);

public sealed record OwnerFounderPlacementAction(
    string FounderId, int X, int Y, OwnerProviderConfigurationAction Cognition);

public sealed record OwnerFounderPlacementReceipt(string FounderId, string HouseholdId, int Placed, int Required);
public sealed record OwnerFounderMoveAction(string FounderId, int X, int Y);
public sealed record OwnerFounderMoveReceipt(string FounderId, int X, int Y, bool Changed);
public sealed record OwnerFounderUndoAction(string FounderId);
public sealed record OwnerFounderUndoReceipt(string FounderId, int Placed, int Required);
public sealed record OwnerFirstTownLayoutAction(int X, int Y);
public sealed record OwnerFirstTownLayoutReceipt(int X, int Y, int Buildings, int RoadTiles);

public sealed record OwnerAgentPlacementAction(
    string AgentId, int X, int Y, OwnerProviderConfigurationAction Cognition,
    string? ExpectedHouseholdId, string? ExpectedTownId);

public sealed record OwnerAgentPlacementReceipt(string AgentId, string? HouseholdId, string? TownId = null);

public sealed record OwnerAgentRenameAction(string AgentId, string Name);

public sealed record OwnerAgentRenameReceipt(string AgentId, string Name, bool Changed);

public sealed record OwnerProviderOptionStatus(
    string Provider,
    string Model,
    bool HasCredential);

public sealed record OwnerProviderConfigurationStatus(
    string RoutineProvider,
    string PlanningProvider,
    long Revision,
    IReadOnlyList<OwnerProviderOptionStatus> Providers,
    IReadOnlyList<InhabitantProviderAssignment>? Assignments = null,
    IReadOnlyList<OwnerProviderCredentialStatus>? CredentialSlots = null);

public sealed record OwnerInstructionAction(
    string IdempotencyKey,
    string TargetInhabitantId,
    string Kind,
    string Text,
    string WorldId,
    bool Queue = false);

public sealed record OwnerOrderCancelAction(
    string IdempotencyKey,
    string TargetInhabitantId,
    string OrderId,
    string WorldId);

public sealed record OwnerAuthoringOperationAction(
    string Kind,
    string? Id,
    string? Value,
    string? SecondaryValue,
    int? X,
    int? Y,
    bool? IsRenewable);

public sealed record OwnerAuthoringBatchAction(
    string BatchId,
    IReadOnlyList<OwnerAuthoringOperationAction> Operations);

public sealed record OwnerContentDependencyAction(
    string PackageId,
    string MinimumVersion,
    string MaximumExclusiveVersion,
    bool Optional);

public sealed record OwnerContentDefinitionAction(
    string Kind,
    string LocalId,
    string Version,
    string DisplayName,
    string PayloadDigest,
    string? PayloadJson = null);

public sealed record OwnerContentAssetReservationAction(
    string AssetId,
    string NormalizedDigest,
    string DecodeProfile,
    long DurableStorageBytes,
    long DecodedCacheBytes,
    long GpuBytes,
    int RenderUnits);

public sealed record OwnerContentPackageAction(
    string PackageId,
    string Version,
    string PackageDigest,
    IReadOnlyList<OwnerContentDependencyAction> Dependencies,
    IReadOnlyList<OwnerContentDefinitionAction> Definitions,
    IReadOnlyList<string> DeclaredCapabilities,
    IReadOnlyList<OwnerContentAssetReservationAction>? Assets = null);

public sealed record OwnerContentPackageIdAction(string PackageId);

public sealed record OwnerBuildingDesignAction(string Name, string Purpose, int WoodCost);

public sealed record OwnerBuildingDesignPreview(OwnerBuildingDesignAction Design, OwnerContentPackageAction Package, string ManifestDigest,
    string Summary, bool ConstructionPassed, int WoodConsumed);

public sealed record OwnerContentRollbackAction(string PackageId, string Reason);

public sealed record OwnerBuildingPlacementAction(
    string InstanceId,
    string DefinitionId,
    int X,
    int Y);

public sealed record OwnerBuildingRemovalAction(
    string InstanceId,
    string? ExpectedTownId,
    string? ExpectedHouseholdId,
    string WorldId);

public sealed record OwnerBuildingReassignmentAction(
    string InstanceId,
    string? ExpectedTownId,
    string? ExpectedHouseholdId,
    string? TargetTownId,
    string? TargetHouseholdId,
    string WorldId);

public sealed record OwnerProductionStartAction(
    string RecipeId,
    string BuildingInstanceId,
    string WorkerId);

public sealed record OwnerBuildingPlacementResult(
    bool Applied,
    string InstanceId,
    string DefinitionId,
    OwnerWorldPosition Position,
    string? Failure);

public sealed record OwnerBuildingManagementResult(
    bool Applied,
    string InstanceId,
    string? Failure,
    string? TownId = null,
    string? HouseholdId = null);

public sealed record OwnerProductionStartResult(
    bool Applied,
    string? JobId,
    string RecipeId,
    string? Failure);

public sealed record OwnerControlReceipt(
    string Operation,
    bool Changed,
    bool IsPaused,
    long RunEpoch,
    long Revision,
    long LatestEventId);

public sealed record OwnerInstructionReceipt(
    string InstructionId,
    string IdempotencyKey,
    long SubmittedTick,
    long RunEpoch,
    long Revision);

public sealed record OwnerOrderControlReceipt(
    string OrderId,
    string Status,
    bool Changed,
    long WorldTick,
    long LatestEventId);

public sealed record OwnerAuthoringBatchReceipt(
    string BatchId,
    bool Applied,
    string? Failure,
    long Revision,
    long TopologyRevision,
    string CurrentMapManifestDigest);

public sealed record OwnerContentPackageReceipt(
    string Operation,
    bool Applied,
    string PackageId,
    string Version,
    string PackageDigest,
    string Lifecycle,
    string? LockDigest,
    long? ValidationTick,
    long? StagedTick,
    long? ActivationTick,
    string? Failure,
    string? ManifestDigest = null);
