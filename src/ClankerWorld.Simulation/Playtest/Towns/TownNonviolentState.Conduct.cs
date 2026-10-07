using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>The applicable wording and actual prior notice at the moment of a physical act.</summary>
public sealed record TownConductLawSnapshot(string LawId, int Version, string WordingDigest,
    string Scope, IReadOnlyList<string> TitleIds, IReadOnlyList<string> PriorNoticeIds);

/// <summary>A physical fact is not a finding that a law was broken.</summary>
public sealed record TownConductRecord(string Id, string ActorId, string ConductKind, GridPoint Position,
    long Tick, string? ActorTownId, string? TargetId, string? ItemKind, int Quantity,
    IReadOnlyList<TownConductLawSnapshot> Laws, string Version);

/// <summary>Who actually observed or was told about an act, retaining the communication chain.</summary>
public sealed record TownConductAcquisition(string Id, string ConductId, string AgentId, string Kind,
    long Tick, GridPoint Position, string Text, string? SourceAgentId = null, string? ParentId = null,
    [property: JsonRequired] GridPoint? SourcePosition = null,
    [property: JsonRequired] string? ObserverHouseholdId = null,
    [property: JsonRequired] string? PrivateSiteHouseholdId = null,
    [property: JsonRequired] string? PublicCaseId = null);

/// <summary>A native completed effect, retained independently of mutable stock and compacted events.</summary>
public sealed record TownNativeRemedyReceipt(string Id, string Kind, string ActorId, long Tick,
    string? BeneficiaryId, string ItemKind, int Quantity, string? TargetId, string SourceLotId,
    string ResultLotId, string PreviousOwnerId, string ResultOwnerId, GridPoint Position,
    int BeforeCondition, int AfterCondition, string Version,
    [property: JsonRequired] string? ActorHouseholdId, [property: JsonRequired] string? ActorTownId,
    int SourceQuantityBefore, int AvailableQuantityBefore, IReadOnlyList<TownNativeRepairInput> RepairInputs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CarriedAvailableQuantityBefore = null);

public sealed record TownNativeRepairInput(string ItemKind, InventoryReservation Reservation);

public sealed partial record TownNonviolentState
{
    [JsonRequired]
    public IReadOnlyList<TownConductRecord> ConductRecords { get; init; } = [];
    [JsonRequired]
    public IReadOnlyList<TownConductAcquisition> Acquisitions { get; init; } = [];
    [JsonRequired]
    public IReadOnlyList<TownNativeRemedyReceipt> NativeReceipts { get; init; } = [];
}
