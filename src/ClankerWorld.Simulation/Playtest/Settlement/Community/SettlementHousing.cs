using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// An adult's request to live in another household's House. Every adult
/// current member of that household must agree; one refusal,
/// or no answer within the window, ends it.
/// </summary>
public sealed record SettlementHousingRequest(string HouseholdId, long RequestedTick, long ExpiryTick,
    IReadOnlyList<string> Members, IReadOnlyList<string> Approvals, IReadOnlyList<string> Rejections);

/// <summary>A household that refused or did not answer, so it is not asked again for a while.</summary>
public sealed record SettlementHousingRefusal(string HouseholdId, long Tick);

/// <summary>Housing state of an adult who has no House their household holds.</summary>
public sealed record SettlementHousing(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementHousingRequest? Request = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SettlementHousingRefusal>? Refusals = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Blocker = null);

/// <summary>
/// Why an adult has no home. These codes are saved and shown to the owner and
/// to the agent's own model; see <see cref="PrivateWorldRuntime.HousingBlocker"/>.
/// </summary>
public static class HousingBlockers
{
    public const string NoHousehold = "no_household";
    public const string NoAuthorizedHome = "no_authorized_home";
    public const string NoLegalSite = "no_legal_site";
    public const string MissingMaterials = "missing_materials";
    public const string AwaitingAnswer = "awaiting_answer";

    public static readonly IReadOnlyList<string> All =
        [NoHousehold, NoAuthorizedHome, NoLegalSite, MissingMaterials, AwaitingAnswer];
}

/// <summary>
/// Existing home first: an adult with no household asks a household that
/// holds a House before any construction is considered. Moving in needs the
/// agreement of every adult member; standing nearby grants nothing, and a
/// pending request grants no stock or shelter access. Add Agent placement on
/// household property keeps joining without consent (see AddAgent).
/// </summary>
public sealed partial class PrivateWorldRuntime
{
    private const int HousingSchemaVersion = 32;
    public const int HousingRequestTicks = 120;
    private const string HousingAskPrefix = "household_ask:";
    private const string HousingAdmitPrefix = "household_admit:";
    private const string HousingRefusePrefix = "household_refuse:";

    /// <summary>A refused or unanswered household is asked again only after two world days.</summary>
    private long HousingRefusalCooldownTicks => 2L * worldSystems.Config.TicksPerDay;

    private string[] HouseholdAdults(string householdId) => society.Checkpoint.Inhabitants.Where(person =>
            person.Status == SocietyInhabitantStatus.Active && person.HouseholdId == householdId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
        .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();

    private bool HasHome(string actor) =>
        society.Checkpoint.GetInhabitant(actor).HouseholdId is { } householdId && HouseForHousehold(householdId) is not null;

    /// <summary>
    /// Households this adult may ask now: they hold a House in the adult's
    /// Town, have an adult who can answer, and did not refuse recently. Only
    /// an adult with no household asks; how an adult leaves or changes a
    /// household is still an open design question.
    /// </summary>
    private IEnumerable<SocietyHousehold> AskableHouseholds(string actor)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not null)
            yield break;
        var housing = inhabitants[actor].Housing;
        if (housing?.Request is not null)
            yield break;
        var town = TownForResident(actor);
        foreach (var household in society.Checkpoint.Households.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (HouseForHousehold(household.Id) is not { } house || house.TownId != town ||
                HouseholdAdults(household.Id).Length == 0 ||
                housing?.Refusals?.Any(refusal => refusal.HouseholdId == household.Id &&
                    WorldTick - refusal.Tick < HousingRefusalCooldownTicks) == true)
                continue;
            yield return household;
        }
    }

    /// <summary>Applicants whose request this member has not answered yet, in a stable order.</summary>
    private IEnumerable<string> PendingHousingRequestsFor(string member)
    {
        var householdId = society.Checkpoint.GetInhabitant(member).HouseholdId;
        if (householdId is null)
            yield break;
        foreach (var applicant in inhabitants.Values.OrderBy(person => person.InhabitantId, StringComparer.Ordinal))
        {
            if (applicant.Housing?.Request is { } request && request.HouseholdId == householdId &&
                request.Members.Contains(member, StringComparer.Ordinal) &&
                !request.Approvals.Contains(member, StringComparer.Ordinal) &&
                !request.Rejections.Contains(member, StringComparer.Ordinal))
                yield return applicant.InhabitantId;
        }
    }

    private bool HasHousingDecision(string actor) => ReadyForBriefInteraction(actor) && PendingHousingRequestsFor(actor).Any();

    private void AddHousingCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor))
            return;
        foreach (var household in AskableHouseholds(actor))
        {
            candidates.Add(new(HousingAskPrefix + household.Id,
                $"Ask the {household.Name} household to let you live in their House; every adult member must agree.",
                18, household.Id));
        }
        foreach (var applicant in PendingHousingRequestsFor(actor))
        {
            var name = society.Checkpoint.GetInhabitant(applicant).Name;
            candidates.Add(new(HousingAdmitPrefix + applicant,
                $"Agree to let {name} live in your household's House as a member; every adult member must agree.", 16));
            candidates.Add(new(HousingRefusePrefix + applicant,
                $"Refuse {name}'s request to live in your household's House.", 70));
        }
    }

    private void ApplyHousingCandidate(string actor, string candidate)
    {
        if (!AdultResident(actor))
            return;
        if (candidate.StartsWith(HousingAskPrefix, StringComparison.Ordinal))
        {
            var householdId = candidate[HousingAskPrefix.Length..];
            if (AskableHouseholds(actor).All(household => household.Id != householdId))
                return;
            SetHousing(actor, (inhabitants[actor].Housing ?? new()) with
            {
                Request = new(householdId, WorldTick, WorldTick + HousingRequestTicks, HouseholdAdults(householdId), [], []),
            });
            AppendEvent("housing_request_made", $"{actor}:{householdId}");
            return;
        }

        var admit = candidate.StartsWith(HousingAdmitPrefix, StringComparison.Ordinal);
        if (!admit && !candidate.StartsWith(HousingRefusePrefix, StringComparison.Ordinal))
            return;
        var applicant = candidate[(admit ? HousingAdmitPrefix : HousingRefusePrefix).Length..];
        if (!PendingHousingRequestsFor(actor).Contains(applicant, StringComparer.Ordinal) ||
            inhabitants[applicant].Housing is not { Request: { } request } housing)
            return;
        SetHousing(applicant, housing with
        {
            Request = admit
                ? request with { Approvals = request.Approvals.Append(actor).Order(StringComparer.Ordinal).ToArray() }
                : request with { Rejections = request.Rejections.Append(actor).Order(StringComparer.Ordinal).ToArray() },
        });
        AppendEvent("housing_answer_recorded", $"{actor}:{applicant}");
        ResolveHousingRequest(applicant);
    }

    private void MaintainHousing()
    {
        foreach (var actor in inhabitants.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            ResolveHousingRequest(actor);
            var housing = inhabitants[actor].Housing;
            var refusals = housing?.Refusals?.Where(refusal => WorldTick - refusal.Tick < HousingRefusalCooldownTicks).ToArray();
            var blocker = HousingBlocker(actor);
            if (housing?.Blocker == blocker && (refusals?.Length ?? 0) == (housing?.Refusals?.Count ?? 0))
                continue;
            SetHousing(actor, (housing ?? new()) with { Blocker = blocker, Refusals = refusals });
            if (blocker is not null && blocker != HousingBlockers.AwaitingAnswer && housing?.Blocker != blocker)
                AppendEvent("housing_blocked", $"{actor}:{blocker}");
        }
    }

    private void ResolveHousingRequest(string actor)
    {
        if (!inhabitants.TryGetValue(actor, out var state) || state.Housing is not { Request: { } request } housing)
            return;
        var living = HouseholdAdults(request.HouseholdId);
        var members = request.Members.Union(living, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!request.Members.SequenceEqual(members, StringComparer.Ordinal))
        {
            // Existing answers stay recorded, while adults added or grown into
            // the household must answer before this request can be accepted.
            request = request with { Members = members };
            housing = housing with { Request = request };
            SetHousing(actor, housing);
        }
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not null ||
            HouseForHousehold(request.HouseholdId) is null || living.Length == 0)
        {
            EndHousingRequest(actor, request, "housing_request_cancelled", remember: false);
        }
        else if (WorldTick > request.ExpiryTick)
        {
            // Restored answers, or the loss of the last unanswered adult,
            // cannot grant membership after the request's world-tick deadline.
            EndHousingRequest(actor, request, "housing_request_expired", remember: true);
        }
        else if (request.Rejections.Any(id => living.Contains(id, StringComparer.Ordinal)))
        {
            EndHousingRequest(actor, request, "housing_request_refused", remember: true);
        }
        else if (living.All(id => request.Approvals.Contains(id, StringComparer.Ordinal)))
        {
            society.Apply(checkpoint => SocietyFixture.JoinHousehold(checkpoint, actor, request.HouseholdId));
            if (society.Checkpoint.GetInhabitant(actor).HouseholdId != request.HouseholdId)
            {
                EndHousingRequest(actor, request, "housing_request_cancelled", remember: false);
                return;
            }
            SetHousing(actor, housing with { Request = null, Refusals = null, Blocker = null });
            AppendEvent("household_joined", $"{actor}:{request.HouseholdId}");
        }
    }

    private void EndHousingRequest(string actor, SettlementHousingRequest request, string eventKind, bool remember)
    {
        var housing = inhabitants[actor].Housing!;
        var refusals = (housing.Refusals ?? []).Where(refusal => refusal.HouseholdId != request.HouseholdId);
        if (remember)
            refusals = refusals.Append(new SettlementHousingRefusal(request.HouseholdId, WorldTick));
        SetHousing(actor, housing with
        {
            Request = null,
            Refusals = refusals.OrderBy(refusal => refusal.HouseholdId, StringComparer.Ordinal).ToArray(),
            Blocker = null,
        });
        // The blocker is recomputed now, so the owner never sees a stale "waiting" line.
        SetHousing(actor, (inhabitants[actor].Housing ?? new()) with { Blocker = HousingBlocker(actor) });
        AppendEvent(eventKind, $"{actor}:{request.HouseholdId}");
    }

    private void SetHousing(string actor, SettlementHousing housing)
    {
        var empty = housing.Request is null && housing.Refusals is not { Count: > 0 } && housing.Blocker is null;
        inhabitants[actor] = inhabitants[actor] with
        {
            Housing = empty ? null : housing with { Refusals = housing.Refusals is { Count: > 0 } ? housing.Refusals : null },
        };
        if (!empty)
            checkpointSchemaVersion = StateSchemaVersion;
    }

    /// <summary>
    /// The real reason an adult has no home, or null when their household
    /// holds a House. The agreement path comes first: an adult with no
    /// household is told so and may ask a household; only a household can
    /// plan a House, and then materials and a legal site are checked.
    /// </summary>
    private string? HousingBlocker(string actor)
    {
        if (!AdultResident(actor) || HasHome(actor))
            return null;
        if (inhabitants[actor].Housing?.Request is not null)
            return HousingBlockers.AwaitingAnswer;
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            return HousingBlockers.NoHousehold;
        if (HouseholdBuildingProjectInProgress(householdId, "house", actor))
            return HousingBlockers.NoAuthorizedHome;
        var house = PlannableHouseholdBuildings(householdId, actor)
            .FirstOrDefault(definition => HouseholdBuildingKind(definition) == "house");
        if (house is null)
            return HousingBlockers.NoAuthorizedHome;
        if (!HouseholdHasMaterialsInHand(householdId, house.BuildCosts, house))
            return HousingBlockers.MissingMaterials;
        if (TownLayoutService.RankConstructionSites(CreateTownLayoutContext(actor), house).Count == 0)
            return HousingBlockers.NoLegalSite;
        return HousingBlockers.NoAuthorizedHome;
    }

    /// <summary>What the agent's own model is told about its housing; null when it has a home.</summary>
    private string? HousingNote(string actor)
    {
        if (inhabitants[actor].Housing is not { Blocker: { } blocker } housing)
            return null;
        return blocker switch
        {
            HousingBlockers.AwaitingAnswer when housing.Request is { } request =>
                $"You have asked the {HouseholdNameFor(request.HouseholdId)} household to let you live in their House. Every adult member must agree.",
            HousingBlockers.NoHousehold =>
                "You have no home. You belong to no household, so no House can be planned for you. A household with a House may agree to take you in.",
            HousingBlockers.NoAuthorizedHome =>
                "You have no home. Your household holds no House yet, so plan one or help build it.",
            HousingBlockers.MissingMaterials =>
                "You have no home. Your household holds no House and lacks the materials to build one.",
            HousingBlockers.NoLegalSite =>
                "You have no home. Your household has the materials for a House but no legal site to build it.",
            _ => null,
        };
    }

    private string HouseholdNameFor(string householdId) =>
        society.Checkpoint.Households.FirstOrDefault(item => item.Id == householdId)?.Name ?? "other";

    private static void ValidateHousing(IEnumerable<PlaytestInhabitantState> physical, SocietyCheckpoint society, int schemaVersion)
    {
        var people = society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var households = society.Households.Select(household => household.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var person in physical)
        {
            if (person.Housing is not { } housing)
                continue;
            if (schemaVersion < HousingSchemaVersion)
                throw new InvalidDataException($"Housing requests require private-world schema {HousingSchemaVersion}.");
            if (housing.Request is null && housing.Refusals is not { Count: > 0 } && housing.Blocker is null ||
                housing.Blocker is not null && !HousingBlockers.All.Contains(housing.Blocker, StringComparer.Ordinal) ||
                housing.Refusals is { } refusals && (refusals.Any(refusal => refusal is null ||
                    !households.Contains(refusal.HouseholdId) || refusal.Tick < 0 || refusal.Tick > society.WorldTick) ||
                    refusals.Select(refusal => refusal.HouseholdId).Distinct(StringComparer.Ordinal).Count() != refusals.Count))
                throw new InvalidDataException("The saved housing state is invalid.");
            if (housing.Request is not { } request)
                continue;
            var answers = request.Approvals is null || request.Rejections is null ? null : request.Approvals.Concat(request.Rejections).ToArray();
            if (!people.TryGetValue(person.InhabitantId, out var applicant) || applicant.HouseholdId is not null ||
                !households.Contains(request.HouseholdId) || request.RequestedTick < 0 || request.RequestedTick > society.WorldTick ||
                request.ExpiryTick - request.RequestedTick != HousingRequestTicks ||
                request.Members is null || answers is null || request.Members.Count == 0 ||
                request.Members.Distinct(StringComparer.Ordinal).Count() != request.Members.Count ||
                request.Members.Any(id => string.IsNullOrWhiteSpace(id) || !people.ContainsKey(id) || id == person.InhabitantId) ||
                answers.Distinct(StringComparer.Ordinal).Count() != answers.Length ||
                answers.Any(id => !request.Members.Contains(id, StringComparer.Ordinal)))
                throw new InvalidDataException("The saved housing request is invalid.");
        }
    }
}
