using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private (TownGovernanceState Council, TownLandHearingState LandHearings) AdvanceTownLandHearings(
        TownRuntimeState town, TownGovernanceState council, TownGovernmentState government)
    {
        var hearings = town.LandHearings ?? TownLandHearingState.Create();
        foreach (var right in householdLandUseRights.Where(right => right.TownId == town.Id && right.AgreedEndTick <= WorldTick).ToArray())
        {
            if (hearings.Cases.Any(item => item.Filings.Any(filing => filing.Kind == "expiry" && filing.AuthorityId == right.Id))) continue;
            // A different pending case over these tiles must rule on the lapsed permission first; only a
            // ruling made at or after the agreed end, with the terms unchanged since, has already reviewed it.
            if (TownLandHearingRules.FilingRefusal(hearings, town.Id, right.Tiles, householdLandUseRights, right.Id) is not null) continue;
            var filing = new TownLandCaseFiling(null, "expiry", "Review an agreed permission expiry; existing permission remains provisional.",
                new("confirm"), WorldTick, right.Id);
            (council, hearings) = OpenLandHearing(town, council, hearings, filing, right.Tiles);
        }
        foreach (var proposal in council.Proposals.Where(proposal => proposal.Kind == "land_hearing" && proposal.Status == "passed"))
        {
            var unopened = proposal.Id + ":unopened";
            if (proposal.LandHearingRequest is not { } request ||
                hearings.Cases.Any(item => item.Filings.Any(filing => filing.AuthorityId == proposal.Id)) ||
                council.Notices.Any(notice => notice.Kind == "result" && notice.SubjectId == unopened) ||
                !LandHearingTownFilingAuthority(town with { Governance = council, Government = government }, proposal.AuthorId, proposal.Id)) continue;
            var filing = new TownLandCaseFiling(proposal.AuthorId, "town", request.Statement, request.RequestedOutcome, WorldTick, proposal.Id);
            // The Council's vote stands when its filing can no longer open: this runs inside the deciding
            // vote, so a refusal is published once as a result instead of undoing that vote.
            var refusal = TownLandHearingRules.IsValidOutcome(request.RequestedOutcome, WorldTick) ? null : "Its requested end date has passed.";
            try
            {
                if (refusal is null)
                    (council, hearings) = OpenLandHearing(town, council, hearings, filing, request.Tiles, townRepresentative: proposal.AuthorId,
                        propertyRequest: request.Property);
            }
            catch (InvalidOperationException exception) { refusal = exception.Message; }
            if (refusal is not null)
                council = TownGovernanceRules.PostNotice(council, "result", unopened,
                    "The Town land hearing that the Council authorized could not open. " + refusal, WorldTick);
        }
        foreach (var item in hearings.Cases.Where(item => item.Status == "pending").ToArray())
        {
            var revision = TownLandHearingRules.CurrentRevision(item);
            var noticeId = "notice:" + (council.Notices.Count + 1);
            TownPropertySnapshot? snapshot = null;
            var partiesItem = item;
            if (item.Property is { Transfer: null } property)
            {
                try
                {
                    snapshot = CaptureProperty(town, property.Request, revision.Number + 1);
                    partiesItem = item with { Property = property with { Snapshots = property.Snapshots.Append(snapshot).ToArray() } };
                }
                catch (InvalidOperationException) { /* A stale asset can be rejected, never transferred. */ }
            }
            var revised = TownLandHearingRules.Revise(hearings, item.Id, householdLandUseRights,
                LandHearingParties(town with { Governance = council, Government = government }, revision.Tiles, partiesItem),
                revision.Tiles, WorldTick, CivicDay, noticeId, assetsChanged: snapshot is not null &&
                    TownPropertyRules.MaterialVersion(snapshot) != TownPropertyRules.MaterialVersion(item.Property!.Snapshots[^1]));
            if (TownLandHearingRules.CurrentRevision(revised.Cases.Single(c => c.Id == item.Id)).Number != revision.Number)
            {
                hearings = revised;
                var current = hearings.Cases.Single(c => c.Id == item.Id);
                if (snapshot is not null)
                {
                    current = current with { Property = current.Property! with { Snapshots = current.Property.Snapshots.Append(snapshot).ToArray() } };
                    hearings = TownPropertyRules.Replace(hearings, current);
                }
                council = PostLandHearingNotice(council, current);
                LandHearingEvent("notice", town, current, null, "revised");
            }
        }
        return AdvanceLandHearingJudges(town with { Governance = council, Government = government, LandHearings = hearings }, council, government, hearings);
    }

    private (TownGovernanceState Council, TownLandHearingState LandHearings) OpenLandHearing(TownRuntimeState town,
        TownGovernanceState council, TownLandHearingState hearings, TownLandCaseFiling filing, IReadOnlyList<GridPoint> tiles,
        string? filingHousehold = null, string? townRepresentative = null, TownPropertyRequest? propertyRequest = null)
    {
        if (!TownLandRightsRules.IsValidPlot(map, tiles, WorldTick, WorldTick) ||
            tiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, townLandTitles)))
            throw new InvalidOperationException("A land case needs one connected plot under this Town's existing title.");
        var before = hearings.Cases.Count;
        var property = propertyRequest is null ? null : new TownPropertyCase(propertyRequest, [CaptureProperty(town, propertyRequest, 1)], []);
        var parties = LandHearingParties(town, tiles, filingHousehold: filingHousehold, townRepresentative: townRepresentative);
        if (property is not null) parties = TownPropertyRules.Parties(parties, property, town.Id, society.Checkpoint.Inhabitants);
        hearings = TownLandHearingRules.File(hearings, town.Id, filing, tiles, householdLandUseRights,
            parties, WorldTick, CivicDay, "notice:" + (council.Notices.Count + 1), property);
        var item = hearings.Cases.Single(item => item.Status == "pending" && item.Filings.Contains(filing) &&
            TownLandHearingRules.CurrentRevision(item).Tiles.SequenceEqual(tiles));
        if (hearings.Cases.Count > before)
        {
            council = PostLandHearingNotice(council, item);
            LandHearingEvent("opened", town, item, filing.AgentId, filing.Kind);
        }
        return (council, hearings);
    }

    private static TownGovernanceState PostLandHearingNotice(TownGovernanceState council, TownLandCase item)
    {
        var revision = TownLandHearingRules.CurrentRevision(item);
        return TownGovernanceRules.PostNotice(council, "land_hearing", TownLandHearingRules.RevisionToken(item),
            "Land hearing opened for " + TownLandClaimRules.DescribeTiles(revision.Tiles) +
            (item.Property is { } property ? ". Property requested: " + property.Request.BuildingId +
                " and its recorded shared goods. Living former members keep ownership unless each personally agrees. Waiving a response gives no property consent." : "") +
            ". Affected adults may answer or explicitly waive their own response. Existing permissions and property remain while the case is pending.", revision.PublishedTick);
    }

    private void LandHearingEvent(string kind, TownRuntimeState town, TownLandCase item, string? actor, string status) =>
        AppendEvent("land_case_" + kind,
            $"{town.Id}|{item.Id}|{TownLandHearingRules.CurrentRevision(item).Number}|{actor ?? ""}|{status}", CivicBoard(town));
}
