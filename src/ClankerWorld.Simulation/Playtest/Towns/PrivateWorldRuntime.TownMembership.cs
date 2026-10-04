using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Town admission (#602). A Town's resident list stays the one record of
/// membership; this applies each passed admission proposal from that Town's
/// current council exactly once. The newcomer must have asked, or accept an
/// approval someone else requested, and their dependent children move with
/// them. Household, House and position are separate and never change here.
/// </summary>
public sealed partial class PrivateWorldRuntime
{
    internal const string AdmissionApproved = "approved";
    internal const string AdmissionAdmitted = "admitted";
    internal const string AdmissionLapsed = "lapsed";
    internal static readonly IReadOnlyList<string> AdmissionLapseReasons =
        ["unavailable", "already_resident", "affiliation_changed", "joined_elsewhere"];

    private static TownProposal? PendingAdmission(TownRuntimeState town, string subject) =>
        town.Governance?.Proposals.FirstOrDefault(proposal => proposal.Kind == "admission" &&
            proposal.SubjectId == subject && proposal.Status == "pending");

    private static TownAdmissionRecord? ApprovedAdmission(TownRuntimeState town, string subject) =>
        town.Admissions?.LastOrDefault(record => record.SubjectId == subject && record.Status == AdmissionApproved);

    private bool HasPendingAdmission(string subject) => towns.Any(town => PendingAdmission(town, subject) is not null);

    /// <summary>A Town where this adult has an undecided request or an approval to accept; it grants nothing yet.</summary>
    private bool HasOpenAdmission(string subject, string? townId) =>
        towns.SingleOrDefault(town => town.Id == townId) is { } town &&
        (PendingAdmission(town, subject) is not null || ApprovedAdmission(town, subject) is not null);

    /// <summary>One open request of one's own at a time, in any Town; a resident of another Town may ask to move.</summary>
    private bool MayRequestOwnAdmission(string actor, TownRuntimeState town) =>
        AdultResident(actor) && TownForResident(actor) != town.Id && !HasPendingAdmission(actor) &&
        ApprovedAdmission(town, actor) is null && !AdmissionRetryWaits(town, actor);

    /// <summary>A resident may ask the council to admit an unaffiliated adult; the adult must still accept.</summary>
    private bool MayBeSponsoredForAdmission(string nominee, TownRuntimeState town) =>
        AdultResident(nominee) && TownForResident(nominee) is null && !HasPendingAdmission(nominee) &&
        ApprovedAdmission(town, nominee) is null && !AdmissionRetryWaits(town, nominee);

    /// <summary>
    /// A refused or withdrawn request for the same newcomer waits one unpaused
    /// world day unless the council changed, matching the proposal rules, so
    /// the choice is not offered while it would only be refused.
    /// </summary>
    private bool AdmissionRetryWaits(TownRuntimeState town, string subject) =>
        town.Governance is { } state &&
        state.Proposals.LastOrDefault(proposal => proposal.RequestKey == "admission:" + subject) is
        { Status: "rejected" or "withdrawn", SettledTick: { } settled } previous &&
        WorldTick < settled + CivicDay && previous.CouncilRevision == state.Revision &&
        previous.Circumstances == "council:" + state.Revision;

    /// <summary>
    /// An adult standing in a Town they do not belong to may walk to its notice
    /// place, and so may an adult with no Town from anywhere: Add Agent inside a
    /// border makes a resident, so they usually start outside. Either way the
    /// notice place must be reachable on foot. Walking registers nothing.
    /// </summary>
    private bool MayVisitAsNewcomer(string actor, TownRuntimeState town) =>
        AdultResident(actor) && TownForResident(actor) is var current && current != town.Id &&
        (current is null || town.BorderTiles.Contains(inhabitants[actor].Position)) && CanWalkToCivicBoard(actor, town);

    /// <summary>Whether a tile close enough to read the Town's notices is reachable on foot.</summary>
    private bool CanWalkToCivicBoard(string actor, TownRuntimeState town)
    {
        if (CivicBoard(town) is not { } board) return false;
        var position = inhabitants[actor].Position;
        for (var dy = -ResourceInteractionRange; dy <= ResourceInteractionRange; dy++)
            for (var dx = -ResourceInteractionRange; dx <= ResourceInteractionRange; dx++)
            {
                var tile = map.WrapColumn(new GridPoint(board.X + dx, board.Y + dy));
                if (map.Contains(tile) && IsWithinInteractionRange(tile, board, ResourceInteractionRange) &&
                    map.IsReachableOnFoot(position, tile))
                    return true;
            }
        return false;
    }

    private bool MayAcceptAdmission(string actor, TownRuntimeState town, TownAdmissionRecord record) =>
        record.Status == AdmissionApproved && record.SubjectId == actor && AdultResident(actor) &&
        TownForResident(actor) is var current && current == record.PreviousTownId && current != town.Id &&
        // Their own undecided request elsewhere is answered first, so one choice cannot move them twice.
        !towns.Any(other => other.Id != town.Id && PendingAdmission(other, actor) is { } pending && pending.AuthorId == actor) &&
        CivicHistory(town).Knows(actor, "result", record.ProposalId);

    private string AdmissionRequestText(string actor, TownRuntimeState town)
    {
        var dependents = TownCareGroup(actor, TownForResident(actor)).Length - 1;
        var children = dependents > 0 ? " Your dependent children would join with you." : string.Empty;
        return TownForResident(actor) is { } current
            ? $"Ask {town.Name}'s council to approve your move from {TownName(current)}. If approved, you leave {TownName(current)} and become a resident at once; until then the request grants no membership or Warehouse access. It never gives a House or household place.{children}"
            : $"Ask {town.Name}'s council to approve your admission. If approved, you become a resident at once; until then the request grants no membership or Warehouse access. It never gives a House or household place.{children}";
    }

    private void AddTownAdmissionCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (ApprovedAdmission(town, actor) is not { } record || !MayAcceptAdmission(actor, town, record)) return;
        var previous = record.PreviousTownId is { } from ? $" You would leave {TownName(from)}." : string.Empty;
        candidates.Add(new(CivicAction(town.Id, "accept_admission", record.ProposalId),
            $"Accept {town.Name}'s approved admission and become a resident.{previous} This gives no House or household place.", 169));
    }

    private void AcceptTownAdmission(string actor, string townId, string proposalId)
    {
        var town = towns.Single(item => item.Id == townId);
        if (ApprovedAdmission(town, actor) is not { } record || record.ProposalId != proposalId ||
            !MayAcceptAdmission(actor, town, record)) return;
        ApplyTownAdmission(townId, proposalId, actor, record);
        SettleTownAdmissions();
    }

    private bool settlingTownAdmissions;

    /// <summary>
    /// Records each newly passed admission once and lapses approvals that no
    /// longer fit. Runs after every saved council decision and roster change,
    /// rereading the Towns after every change, so a delayed or duplicate
    /// approval cannot add a dead or already admitted person or leave anyone in
    /// two Towns, and every saved passed admission has its one outcome record.
    /// </summary>
    private void SettleTownAdmissions()
    {
        // Applying an admission changes rosters; the running loop picks up anything that changes.
        if (settlingTownAdmissions) return;
        settlingTownAdmissions = true;
        try
        {
            while (SettleNextTownAdmission()) { }
        }
        finally
        {
            settlingTownAdmissions = false;
        }
    }

    private bool SettleNextTownAdmission()
    {
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            foreach (var record in town.Admissions ?? [])
            {
                if (record.Status != AdmissionApproved) continue;
                var current = TownForResident(record.SubjectId);
                var reason = !AdultResident(record.SubjectId) ? "unavailable"
                    : current == town.Id ? "already_resident"
                    : current != record.PreviousTownId ? "affiliation_changed" : null;
                if (reason is null) continue;
                LapseTownAdmission(town.Id, record, reason);
                return true;
            }
            if (town.Governance is not { } governance) continue;
            foreach (var proposal in governance.Proposals)
            {
                if (proposal is not { Kind: "admission", Status: "passed", SubjectId: { } subject } ||
                    (town.Admissions ?? []).Any(record => record.ProposalId == proposal.Id))
                    continue;
                // Asking for oneself is consent; an approval someone else asked for waits for the newcomer.
                if (proposal.AuthorId == subject)
                    ApplyTownAdmission(town.Id, proposal.Id, subject, approved: null);
                else
                {
                    var record = new TownAdmissionRecord(proposal.Id, subject, AdmissionApproved, WorldTick, TownForResident(subject));
                    // A newcomer who died or joined this Town while the vote was open has nothing to accept.
                    var lapse = !AdultResident(subject) ? "unavailable" : record.PreviousTownId == town.Id ? "already_resident" : null;
                    if (lapse is not null) LapseTownAdmission(town.Id, record, lapse);
                    else
                    {
                        SetTownAdmission(town.Id, record);
                        AppendEvent("town_admission_approved", $"{town.Id}|{subject}|{proposal.Id}");
                    }
                }
                return true;
            }
        }
        return false;
    }

    private void ApplyTownAdmission(string townId, string proposalId, string subject, TownAdmissionRecord? approved)
    {
        var previous = TownForResident(subject);
        var reason = !AdultResident(subject) ? "unavailable"
            : previous == townId ? "already_resident"
            : approved is not null && previous != approved.PreviousTownId ? "affiliation_changed" : null;
        if (reason is not null)
        {
            LapseTownAdmission(townId, approved ?? new TownAdmissionRecord(proposalId, subject, AdmissionApproved, WorldTick), reason);
            return;
        }
        var group = TownCareGroup(subject, previous);
        SetTownAdmission(townId, new TownAdmissionRecord(proposalId, subject, AdmissionAdmitted, WorldTick, previous, group));
        foreach (var other in towns.Where(item => item.Id != townId).ToArray())
            foreach (var open in (other.Admissions ?? []).Where(record => record.SubjectId == subject &&
                         record.Status == AdmissionApproved).ToArray())
                LapseTownAdmission(other.Id, open, "joined_elsewhere");
        var moving = group.ToHashSet(StringComparer.Ordinal);
        if (previous is not null)
        {
            var left = towns.Single(item => item.Id == previous);
            SetTown(left with { ResidentIds = left.ResidentIds.Where(id => !moving.Contains(id)).ToArray() });
        }
        var joined = towns.Single(item => item.Id == townId);
        SetTown(joined with
        {
            ResidentIds = joined.ResidentIds.Concat(group).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        });
        AppendEvent("town_admission_accepted", $"{townId}|{subject}|{previous ?? "none"}|{group.Length}");
        // Council rosters follow recorded adult residents in both Towns.
        AdvanceTownGovernance();
    }

    /// <summary>
    /// A dependent joining their accepted guardian follows the guardian's recorded
    /// Town membership. The caller has already checked arrival and House capacity.
    /// This changes only rosters; it is not a separate adult admission proposal.
    /// </summary>
    private bool MoveDependentToGuardianTown(string child, string guardian)
    {
        var person = society.Checkpoint.GetInhabitant(child);
        var caregiver = society.Checkpoint.GetInhabitant(guardian);
        if (!inhabitants.ContainsKey(child) || !inhabitants.ContainsKey(guardian) ||
            person.Status != SocietyInhabitantStatus.Active ||
            person.AgeBand is not (SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent) ||
            caregiver.Status != SocietyInhabitantStatus.Active ||
            caregiver.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
            person.PrimaryCaregiverId != guardian || !SocietyFixture.HasActivePrimaryCaregiver(society.Checkpoint, child) ||
            TownForResident(guardian) is not { } destination)
            return false;
        if (TownForResident(child) == destination) return true;

        // Finish both roster updates before governance or admission reconciliation
        // observes the move. House.TownId is deliberately not a membership source.
        foreach (var town in towns.ToArray())
        {
            if (town.Id == destination)
                SetTown(town with
                {
                    ResidentIds = town.ResidentIds.Append(child).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToArray(),
                });
            else if (town.ResidentIds.Contains(child, StringComparer.Ordinal))
                SetTown(town with { ResidentIds = town.ResidentIds.Where(id => id != child).ToArray() });
        }
        AdvanceTownGovernance();
        SettleTownAdmissions();
        return true;
    }

    private void LapseTownAdmission(string townId, TownAdmissionRecord record, string reason)
    {
        SetTownAdmission(townId, record with { Status = AdmissionLapsed, DecidedTick = WorldTick, PreviousTownId = null, MemberIds = null, Reason = reason });
        AppendEvent("town_admission_lapsed", $"{townId}|{record.SubjectId}|{record.ProposalId}|{reason}");
    }

    private void SetTownAdmission(string townId, TownAdmissionRecord record)
    {
        var town = towns.Single(item => item.Id == townId);
        var records = town.Admissions ?? [];
        SetTown(town with
        {
            Admissions = records.Any(item => item.ProposalId == record.ProposalId)
                ? records.Select(item => item.ProposalId == record.ProposalId ? record : item).ToArray()
                : records.Append(record).ToArray(),
        });
    }

    /// <summary>The adult and the living dependents they are primary caregiver for who share their current Town (or none).</summary>
    private string[] TownCareGroup(string adult, string? fromTownId) => society.Checkpoint.Inhabitants
        .Where(person => person.Status == SocietyInhabitantStatus.Active && (person.Id == adult ||
            person.AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
            person.PrimaryCaregiverId == adult && TownForResident(person.Id) == fromTownId))
        .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();

    private string TownName(string townId) => towns.SingleOrDefault(town => town.Id == townId)?.Name ?? "another Town";

    /// <summary>What this agent's own model is told about their Town: actual membership, rights and the admission they know of.</summary>
    private string? TownMembershipNote(string actor)
    {
        var known = towns.Where(town => town.Governance is not null).ToDictionary(town => town.Id, CivicHistory, StringComparer.Ordinal);
        return TownMembershipText.Describe(towns, society.Checkpoint, actor, CivicDay,
            TownMembershipText.TownsWithWarehouse(worldSimulation, worldContent),
            (town, kind, subject) => known.TryGetValue(town.Id, out var history) && history.Knows(actor, kind, subject),
            worldSystems.Config.CalendarOffsetTicks);
    }

    private static void ValidateTownAdmissions(IReadOnlyList<TownRuntimeState> savedTowns, SocietyCheckpoint society, int schemaVersion)
    {
        var people = society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var townIds = savedTowns.Select(town => town.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var town in savedTowns)
        {
            var records = town.Admissions ?? [];
            var proposals = (town.Governance?.Proposals ?? []).ToDictionary(proposal => proposal.Id, StringComparer.Ordinal);
            // Each passed admission is settled as soon as it passes, so it has exactly one outcome record.
            if ((town.Admissions is not null && schemaVersion < TownAdmissionSchemaVersion) ||
                records.Select(record => record?.ProposalId).Distinct(StringComparer.Ordinal).Count() != records.Count ||
                proposals.Values.Any(proposal => proposal is { Kind: "admission", Status: "passed" } &&
                    !records.Any(record => record?.ProposalId == proposal.Id)))
                throw new InvalidDataException("A Town's saved admission records are invalid.");
            foreach (var record in records)
            {
                var valid = record is not null && proposals.TryGetValue(record.ProposalId ?? "", out var proposal) &&
                    proposal is { Kind: "admission", Status: "passed" } && proposal.SubjectId == record.SubjectId &&
                    people.Contains(record.SubjectId) && record.DecidedTick >= proposal.SettledTick &&
                    record.DecidedTick <= society.WorldTick &&
                    (record.PreviousTownId is null || townIds.Contains(record.PreviousTownId) && record.PreviousTownId != town.Id) &&
                    record.Status switch
                    {
                        AdmissionApproved => proposal.AuthorId != record.SubjectId && record.MemberIds is null && record.Reason is null,
                        AdmissionAdmitted => record.Reason is null && record.MemberIds is { Count: > 0 } members &&
                            members.Contains(record.SubjectId, StringComparer.Ordinal) &&
                            members.All(people.Contains) && members.SequenceEqual(members.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                        AdmissionLapsed => record.PreviousTownId is null && record.MemberIds is null &&
                            AdmissionLapseReasons.Contains(record.Reason ?? "", StringComparer.Ordinal),
                        _ => false,
                    };
                if (!valid)
                    throw new InvalidDataException("A Town's saved admission record does not match a passed admission proposal.");
            }
        }
        // An approval waiting for acceptance belongs to someone who is not already a resident there.
        if (savedTowns.Any(town => (town.Admissions ?? []).Any(record => record.Status == AdmissionApproved &&
                town.ResidentIds.Contains(record.SubjectId, StringComparer.Ordinal))))
            throw new InvalidDataException("A Town's saved admission approval names an existing resident.");
    }
}

/// <summary>
/// Plain words for an agent's recorded Town membership, its rights and any
/// admission in progress, shared by the agent's own model context and the
/// owner's agent card. Membership comes only from the Town resident lists.
/// </summary>
public static class TownMembershipText
{
    public const int MaximumLength = 256;

    public static IReadOnlySet<string> TownsWithWarehouse(WorldContentSimulationState simulation, DeclarativeWorldContentState content)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(content);
        var warehouses = content.Buildings.Where(definition => definition.Tags.Contains("warehouse", StringComparer.Ordinal))
            .Select(definition => definition.CanonicalId).ToHashSet(StringComparer.Ordinal);
        return simulation.Buildings.Where(building => building.TownId is not null && warehouses.Contains(building.DefinitionId))
            .Select(building => building.TownId!).ToHashSet(StringComparer.Ordinal);
    }

    /// <param name="knows">Whether this reader learned a Town notice of the given kind and subject; the owner sees all,
    /// an agent only what they read or were told.</param>
    /// <param name="calendarOffsetTicks">The world's saved clock offset, so day numbers match its calendar.</param>
    public static string? Describe(IReadOnlyList<TownRuntimeState> towns, SocietyCheckpoint society, string agentId,
        int ticksPerDay, IReadOnlySet<string> townsWithWarehouse, Func<TownRuntimeState, string, string, bool>? knows = null,
        int calendarOffsetTicks = 0)
    {
        ArgumentNullException.ThrowIfNull(towns);
        ArgumentNullException.ThrowIfNull(society);
        ArgumentNullException.ThrowIfNull(townsWithWarehouse);
        var person = society.Inhabitants.FirstOrDefault(item => item.Id == agentId);
        if (person is not { Status: SocietyInhabitantStatus.Active }) return null;
        knows ??= (_, _, _) => true;
        var adult = person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder;
        var home = towns.SingleOrDefault(town => town.ResidentIds.Contains(agentId, StringComparer.Ordinal));
        var council = home?.Governance?.Form switch
        {
            "representative" => "vote in its council elections",
            "leader" => "vote in its mayoral elections",
            _ => "sit and vote on its council",
        };
        var text = home is null
            ? adult ? "Town: none · no council vote or Warehouse access; a Town council must approve admission at its notice place"
                : "Town: none · follows their primary caregiver's Town"
            : !adult ? person.PrimaryCaregiverId is { } caregiver && home.ResidentIds.Contains(caregiver, StringComparer.Ordinal)
                ? $"Town: resident of {home.Name} with their primary caregiver · council rights begin at adulthood"
                : $"Town: resident of {home.Name} · council rights begin at adulthood"
            : townsWithWarehouse.Contains(home.Id)
                ? $"Town: resident of {home.Name} · may {council} and collect its Warehouse stock in person, housed or not"
                : $"Town: resident of {home.Name} · may {council}, housed or not; it has no Warehouse yet";
        if (adult && AdmissionStatus(towns, agentId, home?.Id, society.WorldTick, ticksPerDay, calendarOffsetTicks, knows) is { } status)
            text += " · " + status;
        return text.Length > MaximumLength ? text[..MaximumLength] : text;
    }

    private static string? AdmissionStatus(IReadOnlyList<TownRuntimeState> towns, string agentId, string? homeId,
        long worldTick, int ticksPerDay, int calendarOffsetTicks, Func<TownRuntimeState, string, string, bool> knows)
    {
        var day = Math.Max(1, ticksPerDay);
        // Calendar days, as WorldCalendarRules counts them: the offset moves midnight, not elapsed time.
        string Day(long tick) => "world day " +
            (tick / day + (tick % day + calendarOffsetTicks) / day + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal))
            if (town.Governance?.Proposals.FirstOrDefault(proposal => proposal.Kind == "admission" &&
                    proposal.SubjectId == agentId && proposal.Status == "pending") is { } pending && knows(town, "proposal", pending.Id))
                return $"admission to {town.Name} pending until {Day(pending.DeadlineTick)}; grants nothing yet";
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal))
            if (town.Admissions?.LastOrDefault(record => record.SubjectId == agentId &&
                    record.Status == PrivateWorldRuntime.AdmissionApproved) is { } approved && knows(town, "result", approved.ProposalId))
                return $"{town.Name}'s council approved admission; not accepted yet";
        var closed = towns.Where(town => town.Id != homeId).SelectMany(town => (town.Governance?.Proposals ?? [])
                .Where(proposal => proposal.Kind == "admission" && proposal.SubjectId == agentId &&
                    proposal.Status is "rejected" or "cancelled" && worldTick - proposal.SettledTick < day &&
                    // A refusal is posted as the proposal's result; a cancellation as the next council notice.
                    (proposal.Status == "rejected" ? knows(town, "result", proposal.Id)
                        : knows(town, "council", "council:" + (proposal.CouncilRevision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))))
                .Select(proposal => (Town: town, Proposal: proposal)))
            .OrderByDescending(item => item.Proposal.SettledTick).FirstOrDefault();
        return closed.Proposal switch
        {
            { Status: "rejected" } proposal => $"{closed.Town.Name}'s council did not approve admission on {Day(proposal.SettledTick ?? 0)}",
            { Status: "cancelled" } => $"admission request to {closed.Town.Name} closed when its council changed; ask again",
            _ => null,
        };
    }
}
