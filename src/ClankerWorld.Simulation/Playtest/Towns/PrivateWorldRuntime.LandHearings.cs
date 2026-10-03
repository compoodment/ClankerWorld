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
            if (hearings.Cases.Any(item => item.Status == "settled" &&
                    TownLandHearingRules.CurrentRevision(item).Tiles.SequenceEqual(right.Tiles) &&
                    LandHearingSamePermissionTerms(TownLandHearingRules.CurrentRevision(item).RightVersions.Select(version => version.Right),
                        householdLandUseRights, right.Tiles))) continue;
            var filing = new TownLandCaseFiling(null, "expiry", "Review an agreed permission expiry; existing permission remains provisional.",
                new("confirm"), WorldTick, right.Id);
            (council, hearings) = OpenLandHearing(town, council, hearings, filing, right.Tiles);
        }
        foreach (var proposal in council.Proposals.Where(proposal => proposal.Kind == "land_hearing" && proposal.Status == "passed"))
        {
            if (proposal.LandHearingRequest is not { } request ||
                hearings.Cases.Any(item => item.Filings.Any(filing => filing.AuthorityId == proposal.Id)) ||
                !LandHearingTownFilingAuthority(town with { Governance = council, Government = government }, proposal.AuthorId, proposal.Id)) continue;
            var filing = new TownLandCaseFiling(proposal.AuthorId, "town", request.Statement, request.RequestedOutcome, WorldTick, proposal.Id);
            (council, hearings) = OpenLandHearing(town, council, hearings, filing, request.Tiles, townRepresentative: proposal.AuthorId);
        }
        foreach (var item in hearings.Cases.Where(item => item.Status == "pending").ToArray())
        {
            var revision = TownLandHearingRules.CurrentRevision(item);
            var noticeId = "notice:" + (council.Notices.Count + 1);
            var revised = TownLandHearingRules.Revise(hearings, item.Id, householdLandUseRights,
                LandHearingParties(town with { Governance = council, Government = government }, revision.Tiles, item),
                revision.Tiles, WorldTick, CivicDay, noticeId);
            if (TownLandHearingRules.CurrentRevision(revised.Cases.Single(c => c.Id == item.Id)).Number != revision.Number)
            {
                hearings = revised;
                var current = hearings.Cases.Single(c => c.Id == item.Id);
                council = PostLandHearingNotice(council, current);
                LandHearingEvent("notice", town, current, null, "revised");
            }
        }
        return AdvanceLandHearingJudges(town with { Governance = council, Government = government, LandHearings = hearings }, council, government, hearings);
    }

    private (TownGovernanceState Council, TownLandHearingState LandHearings) OpenLandHearing(TownRuntimeState town,
        TownGovernanceState council, TownLandHearingState hearings, TownLandCaseFiling filing, IReadOnlyList<GridPoint> tiles,
        string? filingHousehold = null, string? townRepresentative = null)
    {
        if (!TownLandRightsRules.IsValidPlot(map, tiles, WorldTick, WorldTick) ||
            tiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, townLandTitles)))
            throw new InvalidOperationException("A land case needs one connected plot under this Town's existing title.");
        var before = hearings.Cases.Count;
        hearings = TownLandHearingRules.File(hearings, town.Id, filing, tiles, householdLandUseRights,
            LandHearingParties(town, tiles, filingHousehold: filingHousehold, townRepresentative: townRepresentative),
            WorldTick, CivicDay, "notice:" + (council.Notices.Count + 1));
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
            ". Affected adults may answer or explicitly waive their own response. Existing permissions remain while the case is pending.", revision.PublishedTick);
    }

    private void LandHearingEvent(string kind, TownRuntimeState town, TownLandCase item, string? actor, string status) =>
        AppendEvent("land_case_" + kind,
            $"{town.Id}|{item.Id}|{TownLandHearingRules.CurrentRevision(item).Number}|{actor ?? ""}|{status}", CivicBoard(town));

    private static bool LandHearingSamePermissionTerms(IEnumerable<HouseholdLandUseRight> left,
        IEnumerable<HouseholdLandUseRight> right, IReadOnlyList<GridPoint> plot)
    {
        static IEnumerable<string> Terms(IEnumerable<HouseholdLandUseRight> rights, IReadOnlyList<GridPoint> tiles) => rights
            .SelectMany(right => right.Tiles.Where(tiles.Contains).Select(tile =>
                FormattableString.Invariant($"{tile.X}|{tile.Y}|{right.TownId}|{right.HouseholdId}|{right.GrantedTick}|{right.GrantSource}|{right.AgreedEndTick}")))
            .Order(StringComparer.Ordinal);
        return Terms(left, plot).SequenceEqual(Terms(right, plot), StringComparer.Ordinal);
    }
}
