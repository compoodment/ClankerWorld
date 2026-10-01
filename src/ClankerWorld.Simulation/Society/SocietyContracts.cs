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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NeedsName = false);

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

public sealed record SocietySocialMemory(
    string Id,
    string OwnerId,
    string SubjectId,
    string Summary,
    string Visibility,
    long SourceTick,
    long? TombstonedTick = null);

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
    string? SourceTurnId = null);

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
    string? ChildName = null);

public sealed record SocietyEstate(
    string Id,
    string DeceasedId,
    long CreatedTick,
    long ExpiryTick,
    IReadOnlyList<string> BeneficiaryIds,
    bool Settled = false,
    IReadOnlyList<SocietyEstateLot>? FrozenLots = null,
    string? WillStatus = null,
    string? WillBeneficiaryId = null);

public sealed record SocietyEstateLot(string LotId, string ItemKind, int Quantity);

public sealed record SocietyBirthRecord(
    string RequestId,
    string ChildId,
    int Revision,
    long CommittedTick);

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
