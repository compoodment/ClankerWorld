using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A move-out notice for an adult of an overcrowded House. The deadline is a
/// fixed world tick, so pausing stops it and nothing later restarts it; a
/// volunteer who takes another adult's place keeps that adult's deadline.
/// </summary>
public sealed record SettlementRelocation(string HouseholdId, long NoticeTick, long DeadlineTick, string Reason);

/// <summary>
/// Overcrowding relocation: choosing who must move out, the one-day notice and
/// revalidation before each departure. The exit itself is the ordinary
/// departure transition, so goods, the food allowance, care and paused work
/// follow the departure rules.
/// </summary>
public sealed partial class PrivateWorldRuntime
{
    public const int RelocationSchemaVersion = 53;
    private const string HouseholdVolunteerCandidate = "household_volunteer";

    /// <summary>One unpaused world day of notice, measured in world ticks.</summary>
    private long RelocationNoticeTicks => worldSystems.Config.TicksPerDay;

    /// <summary>When this person's current membership of the household was admitted; founders and births use their own start.</summary>
    private long AdmittedTick(string personId, string householdId) => society.Checkpoint.Relationships
        .Where(edge => edge.Type == SocietyRelationshipType.HouseholdMembership && edge.TargetId == personId &&
            edge.HouseholdId == householdId && edge.State == SocietyRelationshipState.Accepted)
        .Select(edge => Math.Max(edge.EffectiveTick, edge.ProposedTick)).DefaultIfEmpty(0).Max();

    private IEnumerable<SocietyInhabitant> ActiveResidents(string householdId) => society.Checkpoint.Inhabitants
        .Where(person => person.Status == SocietyInhabitantStatus.Active && person.HouseholdId == householdId);

    private HouseRelocationRules.Selection? RelocationSelection(string householdId, string? volunteerId = null)
    {
        if (HouseForHousehold(householdId) is not { } house) return null;
        var definition = BuildingStorageRules.EffectiveDefinition(
            worldContent.Buildings.Single(item => item.CanonicalId == house.DefinitionId), house);
        var residents = ActiveResidents(householdId).ToArray();
        var adults = residents.Where(person => person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder &&
                inhabitants.ContainsKey(person.Id))
            .Select(person =>
            {
                var notice = inhabitants[person.Id].Housing?.Relocation is { } relocation && relocation.HouseholdId == householdId
                    ? relocation : null;
                return person.Id == volunteerId && notice is null
                    ? new HouseRelocationRules.Adult(person.Id, AdmittedTick(person.Id, householdId),
                        MovingCareGroup(person.Id).Count == 1, HouseRelocationRules.Volunteer, WorldTick)
                    : new HouseRelocationRules.Adult(person.Id, AdmittedTick(person.Id, householdId),
                        MovingCareGroup(person.Id).Count == 1, notice?.Reason, notice?.NoticeTick);
            });
        return HouseRelocationRules.Select(residents, adults, definition.Width, definition.Height);
    }

    private IEnumerable<(string Id, SettlementRelocation Notice)> RelocationNotices(string? householdId = null) =>
        inhabitants.Values.Where(person => person.Housing?.Relocation is { } notice &&
                (householdId is null || notice.HouseholdId == householdId))
            .Select(person => (person.InhabitantId, person.Housing!.Relocation!))
            .OrderBy(item => item.Item2.DeadlineTick).ThenBy(item => item.Item2.NoticeTick)
            .ThenBy(item => item.InhabitantId, StringComparer.Ordinal);

    private bool HoldsRelocationNotice(string actor) => inhabitants.TryGetValue(actor, out var person) &&
        person.Housing?.Relocation is not null;

    /// <summary>
    /// Only a member with a move-out notice asks another household while still
    /// living in their House; other members leave first or start a household.
    /// </summary>
    private bool MayRelocateFromHousehold(string actor) =>
        inhabitants.TryGetValue(actor, out var person) && person.Housing?.Relocation is { } notice &&
        society.Checkpoint.GetInhabitant(actor).HouseholdId == notice.HouseholdId;

    private void SetRelocation(string actor, SettlementRelocation? notice) =>
        SetHousing(actor, (inhabitants[actor].Housing ?? new()) with { Relocation = notice });

    /// <summary>
    /// Brings saved notices up to date with the actual residents, family units,
    /// dependent care and completed footprint; then any adult whose notice has
    /// run out leaves, one at a time, rechecking before each departure.
    /// </summary>
    private void MaintainRelocation()
    {
        var households = society.Checkpoint.Households.Select(household => household.Id)
            .Concat(RelocationNotices().Select(item => item.Notice.HouseholdId))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var householdId in households)
        {
            ReconcileRelocation(householdId);
            // A failed exit cancels its notice, so this loop always ends.
            while (RelocationNotices(householdId).FirstOrDefault(item => item.Notice.DeadlineTick <= WorldTick) is
                   { Id: not null } due)
            {
                if (!DepartHousehold(due.Id, "displaced"))
                    CancelRelocation(due.Id, due.Notice, "not_needed");
                ReconcileRelocation(householdId);
            }
        }
    }

    private void ReconcileRelocation(string householdId)
    {
        var selection = RelocationSelection(householdId);
        var chosen = (selection?.Chosen ?? []).ToDictionary(item => item.Id, item => item.Reason, StringComparer.Ordinal);
        foreach (var (id, notice) in RelocationNotices(householdId).ToArray())
        {
            if (chosen.ContainsKey(id) && society.Checkpoint.GetInhabitant(id).HouseholdId == householdId) continue;
            var reason = selection is null ? "no_house"
                : selection.Chosen.Count == 0 && !HouseResidentCapacity(householdId)!.IsOvercrowded ? "room"
                : MovingCareGroup(id).Count > 1 ? "care"
                : notice.Reason != HouseRelocationRules.Volunteer &&
                  HouseRelocationRules.DominantFamily(ActiveResidents(householdId).ToArray()) is { } dominant &&
                  society.Checkpoint.GetInhabitant(id).DomesticFamilyUnitId == dominant ? "family"
                : selection.Chosen.Any(item => item.Reason == HouseRelocationRules.Volunteer) ? "replaced"
                : HouseResidentCapacity(householdId) is { IsOvercrowded: false } ? "room" : "not_needed";
            CancelRelocation(id, notice, reason);
        }
        foreach (var item in selection?.Chosen ?? [])
        {
            if (inhabitants[item.Id].Housing?.Relocation is not null) continue;
            var deadline = checked(WorldTick + RelocationNoticeTicks);
            SetRelocation(item.Id, new(householdId, WorldTick, deadline, item.Reason));
            AppendEvent("relocation_notice", $"{item.Id}|{householdId}|{item.Reason}|" +
                deadline.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void CancelRelocation(string actor, SettlementRelocation notice, string reason)
    {
        SetRelocation(actor, null);
        AppendEvent("relocation_cancelled", $"{actor}|{notice.HouseholdId}|{reason}");
        // A member who no longer has to move stops asking elsewhere; someone who already left keeps asking.
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not null &&
            inhabitants[actor].Housing?.Request is { } request)
            EndHousingRequest(actor, request, "housing_request_cancelled", remember: false);
    }

    /// <summary>
    /// Volunteering is offered only when it would actually take a needed place:
    /// the adult moves alone and the House would be chosen to send them first.
    /// </summary>
    private bool MayVolunteerToRelocate(string actor) =>
        AdultResident(actor) && !HoldsRelocationNotice(actor) &&
        society.Checkpoint.GetInhabitant(actor).HouseholdId is { } householdId &&
        HouseResidentCapacity(householdId) is { IsOvercrowded: true } &&
        RelocationSelection(householdId, actor)?.Chosen.Any(item => item.Id == actor) == true;

    private void AddRelocationCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (MayVolunteerToRelocate(actor))
            candidates.Add(new(HouseholdVolunteerCandidate,
                "Volunteer to move out of your overcrowded House so another resident can stay; you keep the current notice time to find a home.",
                105));
    }

    private void ApplyVolunteerToRelocate(string actor)
    {
        if (!MayVolunteerToRelocate(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId) return;
        var selection = RelocationSelection(householdId, actor)!;
        var kept = selection.Chosen.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        // Taking someone's place keeps their deadline, so volunteering never delays the move.
        var inherited = RelocationNotices(householdId).Where(item => !kept.Contains(item.Id))
            .Select(item => (long?)item.Notice.DeadlineTick).Min();
        var deadline = inherited ?? checked(WorldTick + RelocationNoticeTicks);
        SetRelocation(actor, new(householdId, WorldTick, deadline, HouseRelocationRules.Volunteer));
        AppendEvent("relocation_notice", $"{actor}|{householdId}|{HouseRelocationRules.Volunteer}|" +
            deadline.ToString(CultureInfo.InvariantCulture));
        ReconcileRelocation(householdId);
    }

    /// <summary>What the next housing step is for a House that is over its limit, in the agent's own words.</summary>
    private string RelocationNote(string actor, string householdId, HouseResidentCapacityRules.Capacity capacity)
    {
        var house = HouseForHousehold(householdId)!;
        var expansion = (worldSimulation.BuildingExpansions ?? []).Any(job => job.BuildingInstanceId == house.InstanceId &&
            job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused)
            ? " Expansion under way; no places until it is finished." : string.Empty;
        var counts = $"House overcrowded: {capacity.ResidentCount}/{capacity.Limit} places.";
        if (inhabitants[actor].Housing?.Relocation is { } notice)
        {
            var why = notice.Reason switch
            {
                HouseRelocationRules.Volunteer => "you volunteered",
                HouseRelocationRules.LatestUnrelatedArrival => "latest arrival outside the main family",
                _ => "latest arrival; no family is a majority",
            };
            return $"{counts} You must move out in about {HoursLeft(notice.DeadlineTick)} hours ({why}). " +
                "Ask a household with room or start your own." + expansion;
        }
        var notices = RelocationNotices(householdId).Count();
        if (notices > 0)
            return $"{counts} {notices} adult{(notices == 1 ? " has" : "s have")} notice to move out." + expansion;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var next = HouseRelocationRules.FamilyPlan(definition, house.Footprint) == HouseRelocationRules.ExpandPlan
            ? "expand the House"
            : "an adult may start a separate household with their dependents and build a House";
        return $"{counts} Nobody can be required to leave. Next: {next}." + expansion;
    }

    private string HoursLeft(long deadlineTick)
    {
        var ticksPerDay = worldSystems.Config.TicksPerDay;
        var hours = (Math.Max(0, deadlineTick - WorldTick) * 24 + ticksPerDay - 1) / ticksPerDay;
        return hours.ToString(CultureInfo.InvariantCulture);
    }

    private static void ValidateRelocation(PlaytestInhabitantState person, SettlementRelocation notice,
        Dictionary<string, SocietyInhabitant> people, SocietyCheckpoint society, int schemaVersion)
    {
        if (schemaVersion < RelocationSchemaVersion)
            throw new InvalidDataException($"Move-out notices require private-world schema {RelocationSchemaVersion}.");
        var day = (long)society.Config.TicksPerWorldDay;
        if (!people.TryGetValue(person.InhabitantId, out var adult) || adult.Status != SocietyInhabitantStatus.Active ||
            adult.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
            string.IsNullOrWhiteSpace(notice.HouseholdId) || adult.HouseholdId != notice.HouseholdId ||
            !HouseRelocationRules.Reasons.Contains(notice.Reason, StringComparer.Ordinal) ||
            notice.NoticeTick < 0 || notice.NoticeTick > society.WorldTick ||
            notice.DeadlineTick <= notice.NoticeTick || notice.DeadlineTick > notice.NoticeTick + day ||
            notice.Reason != HouseRelocationRules.Volunteer && notice.DeadlineTick != notice.NoticeTick + day)
            throw new InvalidDataException("The saved move-out notice is invalid.");
    }
}
