using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private Dictionary<string, string?> LandTransferAdultHouseholds() => society.Checkpoint.Inhabitants
        .Where(person => person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
        .ToDictionary(person => person.Id, person => person.HouseholdId, StringComparer.Ordinal);

    private IReadOnlyList<TownLandTransferParty> LandTransferParties(TownLandTransferRequest request) =>
        TownLandTransferRules.PartiesFor(request.RightVersions.Select(version => version.Right), request.Tiles,
            request.TargetHouseholdId, LandTransferAdultHouseholds());

    private SocietyHousehold[] LandTransferKnownHouseholds(string actor, TownRuntimeState town)
    {
        var people = TownAdults(town).Concat(inhabitants.Values.Where(person =>
            IsWithinInteractionRange(person.Position, inhabitants[actor].Position, ResourceInteractionRange))
            .Select(person => person.InhabitantId)).ToHashSet(StringComparer.Ordinal);
        var households = LandTransferAdultHouseholds().Where(pair => people.Contains(pair.Key) && pair.Value is not null)
            .Select(pair => pair.Value!).ToHashSet(StringComparer.Ordinal);
        return society.Checkpoint.Households.Where(household => households.Contains(household.Id))
            .OrderBy(household => household.Id, StringComparer.Ordinal).ToArray();
    }

    private string LandTransferSourceToken(TownRuntimeState town, string household) => "rights:" +
        TownLandHearingRules.RecordVersion(householdLandUseRights.Where(right => right.TownId == town.Id && right.HouseholdId == household)
            .OrderBy(right => right.Id, StringComparer.Ordinal).ToArray());

    private static string LandTransferActionToken(TownLandTransferRequest request) => CivicAgentToken(TownLandTransferRules.TermsToken(request));

    private string LandTransferTerms(TownLandTransferRequest request) =>
        "Exact plot: " + TownLandClaimRules.DescribeTiles(request.Tiles) + ". Beneficiary household: " +
        society.Checkpoint.GetHousehold(request.TargetHouseholdId).Name + " (" + request.TargetHouseholdId + "). Source permissions: " +
        string.Join("; ", request.RightVersions.Select(version => society.Checkpoint.GetHousehold(version.Right.HouseholdId).Name +
            " (" + version.Right.HouseholdId + "), " + (version.Right.AgreedEndTick is { } end
                ? "same agreed end on world day " + CivicDayNumber(end) : "no agreed end date"))) +
        ". Title, household membership, private buildings, crops and goods keep their current records and owners.";

    private void LandTransferEvent(string kind, TownRuntimeState town, TownLandTransferRequest request, string? actor, string status) =>
        AppendEvent("land_transfer_" + kind, $"{town.Id}|{request.Id}|1|{actor ?? ""}|{status}", CivicBoard(town));

    private (TownGovernanceState Council, TownLandHearingState LandHearings) AdvanceTownLandTransfers(
        TownRuntimeState town, TownGovernanceState council)
    {
        var before = town.LandHearings;
        var parties = before.Transfers.Where(request => request.Status == "pending")
            .ToDictionary(request => request.Id, LandTransferParties, StringComparer.Ordinal);
        var (hearings, rights) = TownLandTransferRules.Advance(before, map, householdLandUseRights,
            parties, householdLandUseRequests, council.Knowledge, WorldTick);
        householdLandUseRights = rights.ToList();
        foreach (var request in hearings.Transfers.Where(request => request.Status != "pending" &&
                     before.Transfers.Any(old => old.Id == request.Id && old.Status == "pending")))
        {
            var text = request.Status == "transferred" ? "Voluntary household permission transfer completed. " :
                "Voluntary household permission transfer stopped: " + LandTransferFailureText(request.Reason) + ". ";
            council = TownGovernanceRules.PostNotice(council, "result", request.Id, text + LandTransferTerms(request), WorldTick);
            LandTransferEvent(request.Status == "transferred" ? "settled" : "blocked", town, request, null, request.Status);
        }
        return (council, hearings);
    }

    private static string LandTransferFailureText(string? reason) => reason switch
    {
        "rights_changed" => "the source permission changed",
        "permission_expired" => "the agreed permission term ended",
        "plot_disputed" => "the plot is disputed",
        "hearing_open" => "the plot has an open hearing or reopening request",
        "adult_declined" => "an adult household member declined",
        "filer_withdrew" => "the proposer withdrew",
        _ => "the recorded agreement is no longer valid"
    };

    private bool MayVisitLandTransfer(string actor, TownRuntimeState town) => LandHearingAdult(actor) &&
        town.Governance is { } council && CanWalkToCivicBoard(actor, town) &&
        town.LandHearings.Transfers.Any(request => request.Status == "pending" &&
            LandTransferParties(request).Any(party => party.AdultIds.Contains(actor, StringComparer.Ordinal)) &&
            TownLandTransferRules.HasNotice(request, actor, WorldTick, council.Knowledge));

    private void AddTownLandTransferCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (!LandHearingAdult(actor) || town.Governance is not { } council) return;
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        var atBoard = NearCivicBoard(actor, town);
        if (!atBoard && MayVisitLandTransfer(actor, town) && !TownAdults(town).Contains(actor, StringComparer.Ordinal) &&
            !MayVisitAsNewcomer(actor, town) && !MayVisitLandHearing(actor, town))
            candidates.Add(new(CivicAction(town.Id, "visit"), "Walk to the public notice place to read the household permission transfer you actually learned concerns you. Visiting changes no property or membership.", 175));
        if (atBoard && household is not null)
        {
            var known = LandTransferKnownHouseholds(actor, town).Where(target => target.Id != household).ToArray();
            var own = householdLandUseRights.Where(right => right.TownId == town.Id && right.HouseholdId == household &&
                (right.AgreedEndTick is null || right.AgreedEndTick > WorldTick)).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray();
            var adultHouseholds = LandTransferAdultHouseholds();
            var eligible = known.Select(target => (Target: target, Tiles: TownLandRightsRules.OrderTiles(own.SelectMany(right => right.Tiles).Distinct()
                .Where(tile => TownLandTransferRules.CanPropose(town.LandHearings, map, town.Id, actor, [tile], target.Id,
                    householdLandUseRights, TownLandTransferRules.PartiesFor(householdLandUseRights, [tile], target.Id, adultHouseholds),
                    householdLandUseRequests, WorldTick))))).Where(option => option.Tiles.Length > 0).ToArray();
            if (eligible.Length > 0)
                candidates.Add(new(CivicAction(town.Id, "land_transfer_propose", household, LandTransferSourceToken(town, household)),
                    "Propose transferring only an exact connected plot of existing household permission. Supply exact civic_land_tiles and civic_land_hearing.household_id from these known beneficiary households: " +
                    string.Join("; ", eligible.Select(option => option.Target.Name + "=" + option.Target.Id)) +
                    ". Eligible source coordinates by beneficiary: " + string.Join("; ", eligible.Select(option => option.Target.Id + ": " + TownLandClaimRules.DescribeTiles(option.Tiles))) +
                    ". Existing terms remain; every current adult in each source and beneficiary household must separately learn and accept. Proposing gives no consent, title, building, goods or membership.", 185));
        }
        foreach (var request in town.LandHearings.Transfers.Where(request => request.Status == "pending"))
        {
            var parties = LandTransferParties(request);
            var party = parties.SingleOrDefault(item => item.AdultIds.Contains(actor, StringComparer.Ordinal));
            if (party is null) continue;
            var token = LandTransferActionToken(request);
            var knows = TownLandTransferRules.HasNotice(request, actor, WorldTick, council.Knowledge);
            if (atBoard && !knows)
                candidates.Add(new(CivicAction(town.Id, "land_transfer_read", token), "Read the actual published household transfer notice. " + LandTransferTerms(request), 155));
            if (!knows) continue;
            if (request.FilerId == actor)
                candidates.Add(new(CivicAction(town.Id, "land_transfer_withdraw", token), "Withdraw your pending permission transfer proposal. Existing rights remain. " + LandTransferTerms(request), 190));
            if (TownLandTransferRules.PendingFailure(town.LandHearings, request, householdLandUseRights, householdLandUseRequests, WorldTick) is not null) continue;
            var accepted = TownLandTransferRules.AcceptedAdults(request, party, council.Knowledge, WorldTick).Contains(actor, StringComparer.Ordinal);
            if (!accepted)
                candidates.Add(new(CivicAction(town.Id, "land_transfer_accept", token, party.HouseholdId), "Personally accept these exact permission terms for your current household; no other adult's consent is supplied. " + LandTransferTerms(request), 165));
            candidates.Add(new(CivicAction(town.Id, "land_transfer_decline", token, party.HouseholdId),
                (accepted ? "Revoke your acceptance and decline" : "Personally decline") + " this pending permission transfer. Silence and another member's choice give no agreement. " + LandTransferTerms(request), 166));
        }
    }
}
