using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private (TownGovernanceState Council, TownLandHearingState LandHearings) ApplyTownLandTransferAction(
        TownRuntimeState town, string actor, string action, string subject, string choice,
        IReadOnlyList<CognitionLandTile>? landTiles, CognitionLandHearingChoice? hearingChoice, TownGovernanceState council)
    {
        (council, var hearings) = AdvanceTownLandTransfers(town with { Governance = council }, council);
        var currentTown = town with { Governance = council, LandHearings = hearings };
        // Maintenance is independent of the requested act. Its rights, receipts and notices
        // must stay together even when current-action validation rejects the stale request below.
        SetTown(currentTown);
        var candidates = new List<CognitionCandidate>();
        AddTownLandTransferCandidates(candidates, actor, currentTown);
        if (!candidates.Any(candidate => candidate.Id == $"civic|{town.Id}|{action}|{subject}|{choice}"))
            throw new InvalidOperationException("The transfer choice no longer has current source terms or informed household eligibility.");
        if (action == "land_transfer_propose")
        {
            var target = hearingChoice?.HouseholdId;
            var source = society.Checkpoint.GetInhabitant(actor).HouseholdId;
            var tiles = TownLandRightsRules.OrderTiles(landTiles?.Select(tile => new GridPoint(tile.X, tile.Y)) ?? []);
            if (source is null || target is null || target == source ||
                !LandTransferKnownHouseholds(actor, currentTown).Any(household => household.Id == target) ||
                hearingChoice?.AgreedEndTick is not null || tiles.Length == 0 ||
                tiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, townLandTitles) ||
                    !householdLandUseRights.Any(right => right.TownId == town.Id && right.HouseholdId == source && right.Tiles.Contains(tile))))
                throw new InvalidOperationException("A transfer names a known household and exact existing titled permission, preserving its agreed term.");
            var before = hearings.Transfers.Count;
            hearings = TownLandTransferRules.Propose(hearings, map, town.Id, actor, tiles, target, householdLandUseRights,
                TownLandTransferRules.PartiesFor(householdLandUseRights, tiles, target, LandTransferAdultHouseholds()),
                householdLandUseRequests, WorldTick, "notice:" + (council.Notices.Count + 1));
            if (hearings.Transfers.Count > before)
            {
                var proposed = hearings.Transfers[^1];
                council = TownGovernanceRules.PostNotice(council, "land_transfer", TownLandTransferRules.TermsToken(proposed),
                    "Proposed voluntary household permission transfer. " + LandTransferTerms(proposed) +
                    " Every current adult in each affected household must personally agree; filing supplies no consent.", WorldTick);
                LandTransferEvent("proposed", currentTown, proposed, actor, "pending");
            }
            return (council, hearings);
        }
        var request = hearings.Transfers.Single(item => LandTransferActionToken(item) == subject && item.Status == "pending");
        switch (action)
        {
            case "land_transfer_read":
                council = TownGovernanceRules.LearnNotice(council, actor, request.NoticeId, WorldTick);
                LandTransferEvent("read", currentTown, request, actor, "learned");
                break;
            case "land_transfer_accept": case "land_transfer_decline":
                var parties = LandTransferParties(request);
                var party = parties.Single(item => CivicAgentToken(item.HouseholdId) == choice && item.AdultIds.Contains(actor, StringComparer.Ordinal));
                var response = action == "land_transfer_accept" ? "accept" : "decline";
                hearings = TownLandTransferRules.Respond(hearings, request.Id, TownLandTransferRules.TermsToken(request),
                    actor, party.HouseholdId, response, council.Knowledge, householdLandUseRights, parties, householdLandUseRequests, WorldTick);
                LandTransferEvent("consent", currentTown, request, actor, response);
                if (response == "decline")
                    council = TownGovernanceRules.PostNotice(council, "result", request.Id,
                        "Voluntary household permission transfer declined. Existing permissions remain. " + LandTransferTerms(request), WorldTick);
                break;
            case "land_transfer_withdraw":
                hearings = TownLandTransferRules.Withdraw(hearings, request.Id, actor, WorldTick);
                council = TownGovernanceRules.PostNotice(council, "result", request.Id,
                    "Voluntary household permission transfer withdrawn by its proposer. Existing permissions remain. " + LandTransferTerms(request), WorldTick);
                LandTransferEvent("withdrawn", currentTown, request, actor, "withdrawn");
                break;
            default: throw new InvalidOperationException("Unsupported household permission transfer action.");
        }
        return AdvanceTownLandTransfers(currentTown with { Governance = council, LandHearings = hearings }, council);
    }
}
