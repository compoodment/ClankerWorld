using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentCouncilFilingTests
{
    private static readonly string[] Adults = ["reporter", "voter", "third"];

    [Fact]
    public void CouncilApprovalRetainsTheExactReportButDoesNotDecideGuiltOrCreateASecondWindow()
    {
        var request = Request();
        var council = Submit(TownGovernanceState.Create(Adults), request);
        var original = Assert.Single(council.Proposals);
        council = Submit(council, request with { Statement = "A differently worded report of the same act." }, tick: 12);
        var joined = Assert.Single(council.Proposals);
        Assert.Equal(original.Id, joined.Id);
        Assert.Equal((10L, 30L), (joined.OpenedTick, joined.DeadlineTick));
        Assert.Equal(request, joined.NonviolentRequest);
        Assert.Empty(joined.Votes);

        council = TownGovernanceRules.VoteProposal(council, joined.Id, "reporter", true, 12);
        Assert.Equal("pending", council.Proposals[0].Status);
        council = TownGovernanceRules.VoteProposal(council, joined.Id, "voter", true, 13);
        Assert.Equal("passed", council.Proposals[0].Status);
        Assert.Equal(request.Allegation, council.Proposals[0].NonviolentRequest!.Allegation);
        Assert.Equal(2, council.Proposals[0].Votes.Count);

        var later = request with { Allegation = request.Allegation with { IncidentId = "act:later", ConductTick = 14 } };
        council = Submit(council, later, tick: 15);
        Assert.Equal(2, council.Proposals.Count);
        Assert.Equal("pending", council.Proposals[1].Status);
        Assert.Equal(35, council.Proposals[1].DeadlineTick);
    }

    [Fact]
    public void AnotherLawOnTheSameActNeedsItsOwnCouncilAuthorization()
    {
        var request = Request();
        var council = Submit(TownGovernanceState.Create(Adults), request);
        council = Submit(council, request with { Allegation = request.Allegation with { LawId = "law:other" } });
        Assert.Equal(2, council.Proposals.Count);
        Assert.NotEqual(council.Proposals[0].RequestKey, council.Proposals[1].RequestKey);
        Assert.All(council.Proposals, proposal => Assert.Empty(proposal.Votes));
    }

    [Fact]
    public void AReportPayloadCannotRideAnOrdinaryLawVoteOrBeFiledByAVisitorForTheTown()
    {
        var council = TownGovernanceState.Create(Adults);
        Assert.Throws<InvalidOperationException>(() => Submit(council, Request(), actor: "visitor"));
        Assert.Throws<InvalidOperationException>(() => Submit(council, Request(), kind: "law"));
        Assert.Throws<InvalidOperationException>(() => Submit(council, null));
        Assert.Throws<InvalidOperationException>(() => Submit(council,
            Request() with { Allegation = Request().Allegation with { ConductTick = 11 } }));
        Assert.Throws<InvalidOperationException>(() => Submit(council,
            Request() with { Allegation = Request().Allegation with { SourceEvidenceIds = ["source:1", "source:1"] } }));
        Assert.Empty(council.Proposals);
        Assert.Empty(council.Notices);
    }

    private static TownViolationFilingRequest Request() => new(
        new("act:1", "subject", "gather_material", new GridPoint(2, 3), 9, "law:1", 1,
            "I saw this person gather material here.", ["source:1"]), "Ask the Town to report this conduct.");

    private static TownGovernanceState Submit(TownGovernanceState council, TownViolationFilingRequest? request,
        long tick = 10, string actor = "reporter", string kind = "law_case") =>
        TownGovernanceRules.SubmitProposal(council, "town", actor, kind, null, "Authorize this report.", "unchanged",
            Adults, tick, 20, nonviolentRequest: request);
}
