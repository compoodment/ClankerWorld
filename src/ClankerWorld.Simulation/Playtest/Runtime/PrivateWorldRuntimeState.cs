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
