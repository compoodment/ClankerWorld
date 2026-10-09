using ClankerWorld.Simulation.Kernel;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Society;

public enum SocietyInhabitantStatus
{
    Active,
    Dead,
}

public enum SocietyAgeBand
{
    Infant,
    Child,
    Adolescent,
    Adult,
    Elder,
}

public enum SocietyRelationshipType
{
    Partnership,
    Caregiver,
    HouseholdMembership,
    BiologicalParentage,
    LegalGuardian,
}

public enum SocietyRelationshipState
{
    Proposed,
    Accepted,
    Rejected,
    Revoked,
    Dissolved,
    EndedByDeath,
}

public enum SocietyConsentState
{
    Pending,
    Accepted,
    Refused,
    ProtectedLifecycle,
    Revoked,
}

public enum SocietyDeathCause
{
    NaturalAge,
    Hazard,
    Illness,
    Accident,
    Rule,
}

public enum SocietyWorkRole
{
    Unassigned,
    Farmer,
    Builder,
    Caregiver,
    Trader,
    Teacher,
    Organizer,
}

public enum SocietyOrganizationKind
{
    Farm,
    Workshop,
    Organization,
}

public enum NewbornProviderPolicy
{
    PerChild,
    ParentInheritance,
    WorldDefault,
    Hybrid,
}

/// <summary>Day-based lifecycle for newly created playtest worlds.</summary>
public sealed record SocietyDayLifecycle(
    int ChildStartDay = 3,
    int AdultStartDay = 15,
    int ElderStartDay = 45,
    int MaximumDay = 60)
{
    public void Validate()
    {
        if (ChildStartDay <= 0 || AdultStartDay <= ChildStartDay ||
            ElderStartDay <= AdultStartDay || MaximumDay <= ElderStartDay)
        {
            throw new ArgumentOutOfRangeException(nameof(SocietyDayLifecycle));
        }
    }
}

/// <summary>
/// Versioned lifecycle tuning. Legacy worlds count age in years; worlds with
/// DayLifecycle count age in days. The choice is saved with the world.
/// </summary>
public sealed record SocietyConfig(
    int TicksPerWorldDay = KernelClock.TicksPerDay,
    int DaysPerWorldYear = KernelClock.DaysPerYear,
    int InfantYears = 2,
    int ChildYears = 12,
    int AdultYears = 18,
    int ElderYears = 65,
    int EstateEscrowDays = 7,
    int BaseNaturalMortalityBasisPoints = 100,
    int NaturalMortalitySlopeBasisPoints = 25,
    int ContractVersion = 1,
    SocietyDayLifecycle? DayLifecycle = null)
{
    public long TicksPerWorldYear => checked((long)TicksPerWorldDay * DaysPerWorldYear);

    public long TicksPerLifecycleAge => DayLifecycle is null ? TicksPerWorldYear : TicksPerWorldDay;

    public int FounderStartingAge => DayLifecycle?.AdultStartDay ?? AdultYears;

    public int AgeAt(long birthTick, long worldTick)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(worldTick, birthTick);
        return checked((int)((worldTick - birthTick) / TicksPerLifecycleAge));
    }

    public SocietyAgeBand AgeBandAt(long birthTick, long worldTick) => AgeBandAt(AgeAt(birthTick, worldTick));

    public SocietyAgeBand AgeBandAt(int ageYears) => ageYears switch
    {
        _ when DayLifecycle is { } days && ageYears < days.ChildStartDay => SocietyAgeBand.Infant,
        _ when DayLifecycle is { } days && ageYears < days.AdultStartDay => SocietyAgeBand.Child,
        _ when DayLifecycle is { } days && ageYears < days.ElderStartDay => SocietyAgeBand.Adult,
        _ when DayLifecycle is not null => SocietyAgeBand.Elder,
        _ when ageYears < InfantYears => SocietyAgeBand.Infant,
        _ when ageYears < ChildYears => SocietyAgeBand.Child,
        _ when ageYears < AdultYears => SocietyAgeBand.Adolescent,
        _ when ageYears < ElderYears => SocietyAgeBand.Adult,
        _ => SocietyAgeBand.Elder,
    };

    public int NaturalMortalityRiskBasisPoints(int ageYears)
    {
        if (DayLifecycle is { } days && ageYears >= days.MaximumDay)
        {
            return 10_000;
        }

        if (ageYears < (DayLifecycle?.ElderStartDay ?? ElderYears))
        {
            return 0;
        }

        return Math.Min(
            9_999,
            checked(BaseNaturalMortalityBasisPoints +
                (ageYears - (DayLifecycle?.ElderStartDay ?? ElderYears)) * NaturalMortalitySlopeBasisPoints));
    }

    public void Validate()
    {
        if (TicksPerWorldDay <= 0 || DaysPerWorldYear <= 0 || InfantYears <= 0 ||
            ChildYears <= InfantYears || AdultYears <= ChildYears || ElderYears <= AdultYears ||
            EstateEscrowDays <= 0 || BaseNaturalMortalityBasisPoints < 0 ||
            NaturalMortalitySlopeBasisPoints < 0 || ContractVersion is < 1 or > 3 ||
            DayLifecycle is not null && ContractVersion < 3 ||
            DayLifecycle is null && ContractVersion == 3)
        {
            throw new ArgumentOutOfRangeException(nameof(SocietyConfig));
        }

        DayLifecycle?.Validate();
    }
}

public sealed record SocietyInhabitant(
    string Id,
    string Name,
    long BirthTick,
    SocietyInhabitantStatus Status,
    SocietyAgeBand AgeBand,
    int HealthBasisPoints,
    string? HouseholdId,
    string? ProviderBindingId,
    SocietyWorkRole CurrentRole,
    long LastLifecycleYearChecked,
    long? DeathTick = null,
    SocietyDeathCause? DeathCause = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? BirthLifeTick = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NeedsName = false)
{
    /// <summary>Whether Name is a chosen identity rather than an unnamed placeholder.</summary>
    [JsonRequired]
    public bool HasChosenName { get; init; } = !NeedsName;

    /// <summary>The explicit domestic group used for House resident priority, separate from ancestry.</summary>
    public string? DomesticFamilyUnitId { get; init; }

    /// <summary>The current primary caregiver for a dependent; birth records preserve the original caregiver separately.</summary>
    public string? PrimaryCaregiverId { get; init; }
}

public sealed record SocietyHousehold(
    string Id,
    string Name,
    IReadOnlyList<string> MemberIds,
    IReadOnlyList<string> CaregiverIds);

public sealed record SocietyRelationship(
    string Id,
    int Revision,
    SocietyRelationshipType Type,
    string ProposerId,
    string TargetId,
    SocietyRelationshipState State,
    SocietyConsentState Consent,
    long ProposedTick,
    long EffectiveTick,
    string PrivacyClass,
    string? HouseholdId = null,
    IReadOnlyList<string>? AcceptedBy = null)
{
    public IReadOnlyList<string> AcceptedParties => AcceptedBy ?? [];
}

public sealed record SocietyRelationshipProposal(
    string Id,
    int Revision,
    SocietyRelationshipType Type,
    string ProposerId,
    string TargetId,
    long RequestedTick,
    string? HouseholdId = null,
    string PrivacyClass = "private");

public sealed record SocietyOrganization(
    string Id,
    SocietyOrganizationKind Kind,
    string OwnerId,
    IReadOnlyList<string> MemberIds,
    string InventoryOwnerId);

/// <summary>Why a private source is retained; permanent kinds never fade.</summary>
public enum SocietyMemoryKind
{
    Experience,
    LifeEvent,
    Relationship,
    Skill,
    Commitment,
}

public sealed record SocietySocialMemory(
    string Id,
    string OwnerId,
    string SubjectId,
    string Summary,
    string Visibility,
    long SourceTick,
    long? TombstonedTick = null)
{
    [JsonRequired]
    public SocietyMemoryKind Kind { get; init; } = SocietyMemoryKind.Experience;
}

/// <summary>The evidence basis for an agent-owned account, not a world fact.</summary>
public enum SocietyBeliefProvenance
{
    Firsthand,
    Hearsay,
    Inference,
}

/// <summary>
/// A bounded, owner-private claim about what happened. Correction links retain
/// the earlier belief for inspection; neither record is an authoritative event.
/// </summary>
/// <param name="SourceAgentId">The reporter for hearsay; firsthand evidence is owned by the witness.</param>
/// <param name="SourceEventId">An optional reference to an authoritative world-event ID behind the account.</param>
public sealed record SocietyAgentBelief(
    string Id,
    string OwnerId,
    string Statement,
    SocietyBeliefProvenance Provenance,
    int ConfidenceBasisPoints,
    long FormedTick,
    string? SourceAgentId = null,
    long? SourceEventId = null,
    string? AboutInhabitantId = null,
    string? SupersedesBeliefId = null,
    string? SupersededByBeliefId = null,
    long? SupersededTick = null,
    string? SourceTurnId = null)
{
    [JsonRequired]
    public SocietyMemoryKind Kind { get; init; } = SocietyMemoryKind.Experience;
}

public sealed record SocietyArchivedMemory(SocietySocialMemory Memory, long ArchivedTick);
public sealed record SocietyArchivedBelief(SocietyAgentBelief Belief, long ArchivedTick);

/// <summary>Identifies an agent-private source without turning it into a world fact.</summary>
public enum SocietyMemorySourceKind
{
    Experience,
    Belief,
}

/// <summary>A bounded Jev importance estimate linked to its unchanged source record.</summary>
public sealed record SocietyAgentMemoryImportance(
    string SourceId,
    SocietyMemorySourceKind Kind,
    long SourceTick,
    int ImportanceBasisPoints,
    int ImportanceConfidenceBasisPoints,
    long AssessedTick);

/// <summary>
/// Per-agent salience index. Source experiences and beliefs remain separately
/// stored so provenance, uncertainty, and correction history are preserved.
/// </summary>
public sealed record SocietyAgentMemoryCompaction(
    string OwnerId,
    IReadOnlyList<SocietyAgentMemoryImportance> Sources);

public sealed record SocietyBirthFoodContribution(string LotId, int Quantity);

public sealed record SocietyBirthRequest(
    string Id,
    int Revision,
    string FirstParentId,
    string SecondParentId,
    string HouseholdId,
    IReadOnlyList<string> CaregiverIds,
    IReadOnlyList<string> ConsentingParentIds,
    string FoodLotId,
    int FoodQuantity,
    long RequestedTick,
    NewbornProviderPolicy ProviderPolicy = NewbornProviderPolicy.Hybrid,
    string? RequestedProviderBindingId = null,
    string? ChildName = null,
    string? PrimaryCaregiverId = null,
    IReadOnlyList<SocietyBirthFoodContribution>? FoodContributions = null);

/// <summary>
/// A dead agent's frozen estate. <paramref name="BeneficiaryIds"/> is always the
/// household default. An accepted will adds its named heirs (people or a Town),
/// how it divides the estate and the exact quantity of each lot each heir gets.
/// Final words, when left, are heard by the people who inherit.
/// </summary>
public sealed record SocietyEstate(
    string Id,
    string DeceasedId,
    long CreatedTick,
    long ExpiryTick,
    IReadOnlyList<string> BeneficiaryIds,
    bool Settled = false,
    IReadOnlyList<SocietyEstateLot>? FrozenLots = null,
    string? WillStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? WillHeirIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WillSplit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SocietyWillBequest>? WillBequests = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FinalWords = null);

public sealed record SocietyEstateLot(
    string LotId,
    string ItemKind,
    int Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StorageBuildingId = null);

/// <summary>An exact part of one frozen lot left to one named heir.</summary>
public sealed record SocietyWillBequest(string LotId, string HeirId, int Quantity);

/// <summary>
/// A will's validated choice in society IDs: one to three distinct heirs in the
/// order named, "equal" or "items", and for "items" the heir of each listed lot.
/// </summary>
public sealed record SocietyWillDirective(
    IReadOnlyList<string> HeirIds,
    string Split,
    IReadOnlyDictionary<string, string>? LotHeirs = null);

/// <summary>
/// Where a Town keeps inherited goods: its Warehouse, the room left there, and
/// the kinds it does not store. Supplied by the world when an estate settles.
/// </summary>
public sealed record SocietyTownStore(
    string TownId,
    string WarehouseId,
    int FreeRoom,
    IReadOnlySet<string> RefusedItemKinds);

public sealed record SocietyBirthRecord(
    string RequestId,
    string ChildId,
    int Revision,
    long CommittedTick,
    string PrimaryCaregiverId,
    string HouseholdId,
    string DomesticFamilyUnitId);

public sealed record SocietyEvent(
    long EventId,
    long WorldTick,
    string Kind,
    string Detail);

public sealed record SocietyCheckpoint(
    string WorldId,
    long WorldTick,
    long RunEpoch,
    bool IsPaused,
    SocietyConfig Config,
    string? WorldDefaultProviderBindingId,
    IReadOnlyList<SocietyInhabitant> Inhabitants,
    IReadOnlyList<SocietyHousehold> Households,
    IReadOnlyList<SocietyRelationship> Relationships,
    IReadOnlyList<SocietyOrganization> Organizations,
    IReadOnlyList<SocietySocialMemory> Memories,
    IReadOnlyList<SocietyEstate> Estates,
    IReadOnlyList<SocietyBirthRecord> Births,
    InventoryCheckpoint Inventory,
    IReadOnlyList<SocietyEvent> Events,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long EventHistoryFloor = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SocietyLifeClock? LifeClock = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SocietyAgentBelief>? Beliefs { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SocietyAgentMemoryCompaction>? MemoryCompactions { get; init; }

    [JsonRequired]
    public IReadOnlyList<SocietyArchivedMemory> ArchivedMemories { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<SocietyArchivedBelief> ArchivedBeliefs { get; init; } = [];

    public IEnumerable<SocietySocialMemory> AllMemories() => Memories.Concat(ArchivedMemories.Select(item => item.Memory));
    public IEnumerable<SocietyAgentBelief> AllBeliefs() => (Beliefs ?? []).Concat(ArchivedBeliefs.Select(item => item.Belief));

    public long LifeTickAt(long worldTick) => LifeClock?.At(worldTick) ?? worldTick;

    public int AgeAt(SocietyInhabitant inhabitant, long worldTick) =>
        Config.AgeAt(inhabitant.BirthLifeTick ?? inhabitant.BirthTick, LifeTickAt(worldTick));

    public SocietyInhabitant GetInhabitant(string id) =>
        Inhabitants.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public SocietyHousehold GetHousehold(string id) =>
        Households.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public SocietyRelationship GetRelationship(string id) =>
        Relationships.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public SocietyEstate GetEstate(string id) =>
        Estates.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));
}

public sealed record SocietyOperationResult(
    SocietyCheckpoint Checkpoint,
    string? CreatedId = null,
    IReadOnlyList<SocietyEvent>? NewEvents = null);

/// <summary>A prospective biological clock; world dates and seasons are unchanged.</summary>
public sealed record SocietyLifeClock(int Rate, long WorldAnchorTick, long LifeAnchorTick)
{
    public long At(long worldTick) => checked(LifeAnchorTick + (worldTick - WorldAnchorTick) * Rate);

    public long WorldTickFor(long lifeTick)
    {
        var difference = checked(lifeTick - LifeAnchorTick);
        return checked(WorldAnchorTick + (difference >= 0 ? checked(difference + Rate - 1) / Rate : difference / Rate));
    }
}
