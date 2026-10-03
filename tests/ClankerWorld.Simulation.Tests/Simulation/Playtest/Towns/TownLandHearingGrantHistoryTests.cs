using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandHearingGrantHistoryTests
{
    private static readonly string[] Adults = ["alpha-adult", "beta-adult", "judge", "other"];
    private static readonly GridPoint[] GrantPlot = [new(0, 0), new(1, 0), new(2, 0), new(3, 0)];
    private static readonly GridPoint[] HearingPlot = [new(1, 0), new(2, 0)];
    private static readonly IReadOnlyDictionary<string, string?> Households = new Dictionary<string, string?>
    {
        ["alpha-adult"] = "alpha",
        ["beta-adult"] = "beta",
        ["judge"] = "neutral",
        ["other"] = "other-household",
    };
    private static readonly TownLandCaseParty[] Parties =
    [new("alpha", "household", "alpha", "town", ["alpha-adult"]), new("beta", "household", "beta", "town", ["beta-adult"])];

    [Fact]
    public void CourtChangesAndAnAuthorizedBuildingTransferKeepActualCouncilGrantApprovalAndConsent()
    {
        var request = new HouseholdLandUseRequest("original-grant", "town", "alpha", "alpha-adult", GrantPlot, 0, 30);
        var council = TownGovernanceState.Create(Adults);
        council = TownGovernanceRules.PostNotice(council, "land_use", request.Id, "The exact household land request", 0);
        council = TownGovernanceRules.LearnNotice(council, "alpha-adult", council.Notices[^1].Id, 0);
        council = TownGovernanceRules.SubmitProposal(council, "town", "alpha-adult", "land_use", request.Id,
            "Grant this plot subject to personal acceptance", "council:0", Adults, 0, 10, landUseRequest: request);
        var proposalId = council.Proposals[0].Id;
        foreach (var voter in Adults.Take(3)) council = TownGovernanceRules.VoteProposal(council, proposalId, voter, true, 0);
        request = request with
        {
            CouncilProposalId = proposalId,
            Status = "granted",
            SettledTick = 0,
            Consents = [new("alpha-adult", true, 0)],
            GrantAdults = ["alpha-adult"],
        };
        var original = new HouseholdLandUseRight(HouseholdLandGrantRules.RightId(request.Id), "town", "alpha",
            GrantPlot, 0, HouseholdLandGrantRules.GrantSource(request.Id), 30);
        var title = new TownLandTitleRecord("title", "town", GrantPlot, 0);
        var map = new SeededMap(8, 2, 0,
            (from y in Enumerable.Range(0, 2)
             from x in Enumerable.Range(0, 8)
             select new TerrainTile(new(x, y), TerrainKind.Meadow)).ToArray(), [], [], "grant-history-fixture");
        var government = TownGovernmentState.Create() with
        {
            Arrangement = new("council", "mayor"),
            Offices = [new("land", "alpha-adult", 0, 100, null, null, "mayor-election")],
        };
        var hearings = TownLandHearingRules.File(TownLandHearingState.Create(), "town",
            new("beta-adult", "dispute", "Review the plot permission", new("end", "alpha"), 1),
            HearingPlot, [original], Parties, 1, 10, "notice:" + (council.Notices.Count + 1));
        var caseId = hearings.Cases[0].Id;
        council = TownGovernanceRules.PostNotice(council, "land_hearing", caseId + ":1", "The exact hearing plot", 1);
        foreach (var actor in Adults) council = TownGovernanceRules.LearnNotice(council, actor, council.Notices[^1].Id, 1);
        hearings = TownLandCaseJudgeRules.Register(hearings, caseId, "judge", Adults, Households, 1);
        (hearings, council) = TownLandCaseJudgeRules.Advance(hearings, council, government, Adults, Households, 1, 10);
        hearings = TownLandCaseJudgeRules.Vote(hearings, caseId,
            TownLandCaseJudgeRules.RoundToken(hearings.Cases[0].Contest!), "alpha-adult", "judge", 2);
        (hearings, council) = TownLandCaseJudgeRules.Advance(hearings, council, government, Adults, Households, 11, 10);
        hearings = TownLandHearingRules.AddEvidence(hearings, caseId, 1,
            new("permission-record", 1, "record", "record_inspection", "alpha-adult", original.Id,
                TownLandHearingRules.Version(original), 1, "alpha-adult", 1, "The existing granted permission"), council.Knowledge);
        hearings = TownLandHearingRules.Inspect(hearings, caseId, 1, "judge", 11);
        (hearings, var endedRights) = TownLandHearingRules.Rule(hearings, map, caseId, 1, hearings.Cases[0].Judge!, 11,
            new("end", "alpha"), ["permission-record"], [], "End permission only on the noticed plot", [original], Parties, true);
        Assert.DoesNotContain(endedRights, right => right.Tiles.Any(HearingPlot.Contains));
        var moved = TownLandRightsRules.ReassignFootprintRights(map, endedRights, new HashSet<GridPoint> { new(0, 0) }, "beta", 12);
        hearings = TownLandHearingRules.RecordBuildingTransfer(hearings, map, "house", [new(0, 0)], "beta", endedRights, moved, 12);
        var town = new TownRuntimeState("town", "Town", "founded", 0, Adults, [], GrantPlot,
            Governance: council, Government: government)
        { LandHearings = hearings };

        // These are the production validators, composed across the grant and court ledgers.
        HouseholdLandGrantRules.Validate(12, [town], moved, [request], Adults.ToHashSet(StringComparer.Ordinal));
        TownLandHearingValidation.Validate(map, 12, "town", hearings, moved, [title], Adults.ToHashSet(StringComparer.Ordinal),
            Households.Values.OfType<string>().ToHashSet(StringComparer.Ordinal), council, 10, government);
        var roundTrip = JsonSerializer.Deserialize<TownRuntimeState>(JsonSerializer.Serialize(town))!;
        HouseholdLandGrantRules.Validate(12, [roundTrip], moved, [request], Adults.ToHashSet(StringComparer.Ordinal));
        Assert.Throws<InvalidDataException>(() => HouseholdLandGrantRules.Validate(12, [town], moved,
            [request with { Consents = [] }], Adults.ToHashSet(StringComparer.Ordinal)));
        Assert.Throws<InvalidDataException>(() => HouseholdLandGrantRules.Validate(12,
            [town with { LandHearings = TownLandHearingState.Create() }], moved, [request], Adults.ToHashSet(StringComparer.Ordinal)));
        Assert.Equal("alpha", Assert.Single(moved, right => right.Tiles.Contains(new GridPoint(3, 0))).HouseholdId);
        Assert.Equal("beta", Assert.Single(moved, right => right.Tiles.Contains(new GridPoint(0, 0))).HouseholdId);
        Assert.Equal(30, Assert.Single(moved, right => right.Tiles.Contains(new GridPoint(0, 0))).AgreedEndTick);
    }
}
