using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLawTests
{
    private const int Day = 10;
    private const string Town = "town:test";
    private const string Grove = "Grove: Do not cut trees in the north grove.";
    private static readonly string[] Adults = ["a", "b", "c"];
    private static readonly TownLandTitleRecord[] Titles =
    [
        new("title:a", Town, [new(0, 0), new(1, 0), new(2, 0)], 0),
        new("title:b", "town:other", [new(8, 8)], 0),
    ];

    private static (TownGovernanceState Council, TownGovernmentState Government) Fresh() =>
        (TownGovernanceState.Create(Adults), TownGovernmentState.Create());

    private static (TownGovernanceState, TownGovernmentState) Pass((TownGovernanceState Council, TownGovernmentState Government) civic,
        long tick, params string[] voters)
    {
        var (council, government) = civic;
        var id = council.Proposals[^1].Id;
        foreach (var voter in voters) council = TownGovernanceRules.VoteProposal(council, id, voter, true, tick);
        return TownLawRules.Enact(council, government, Town, "Test Town", tick);
    }

    private static (TownGovernanceState, TownGovernmentState) Adopt(string text, string scope = TownLawRules.Jurisdiction,
        GridPoint[]? site = null, long tick = 0)
    {
        var (council, government) = Fresh();
        var civic = TownLawRules.ProposeAdoption(council, government, Town, "a", text, scope, site ?? [], Adults, tick, Day);
        return Pass(civic, tick + 1, "a", "b");
    }

    [Fact]
    public void PassedLawRecordsItsSubjectScopeAndAppliesOnlyFromAdoption()
    {
        var (council, government) = Adopt(Grove, tick: 4);
        var law = Assert.Single(government.Laws);
        var version = Assert.Single(law.Versions);
        Assert.Equal("Grove", version.Subject);
        Assert.Equal("Do not cut trees in the north grove.", version.Rule);
        Assert.Equal(TownLawRules.Jurisdiction, version.Scope);
        Assert.Equal(5, version.AdoptedTick);
        Assert.Equal(council.Proposals[0].Id, version.ProposalId);
        Assert.Null(TownLawRules.InForceAt(law, 4));
        Assert.Same(version, TownLawRules.InForceAt(law, 5));
        Assert.Equal("enacted", Assert.Single(government.LawDrafts).Status);
        var notice = Assert.Single(council.Notices, n => n.Kind == "law");
        Assert.Equal(law.Id, notice.SubjectId);
        Assert.Contains("does not block actions or change ownership", notice.Text, StringComparison.Ordinal);
        // Conduct before adoption is assessed under the laws then in force: none.
        Assert.Empty(TownLawRules.Applicable(government, Town, Titles, false, new(0, 0), 4));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("visitor:a")]
    public void OnlyAnAdoptedTypedGrantPermitsBoatUseAndRepealEndsIt(string? visitor)
    {
        var grant = new TownBoatAccessGrant(visitor);
        var ordinary = Adopt(TownBoatAccessRules.Text(grant));
        var town = new TownRuntimeState(Town, "Test Town", "founded", 0, Adults, [], [], Government: ordinary.Item2);
        Assert.False(TownBoatAccessRules.Allows(town, "visitor:a", 2));
        var (council, government) = Fresh();
        var pending = TownLawRules.ProposeBoatAccess(council, government, Town, "a", visitor, Adults, 4, Day);
        Assert.False(TownBoatAccessRules.Allows(town with { Government = pending.Government }, "visitor:a", 5));
        var passed = Pass(pending, 5, "a", "b");
        town = town with { Government = passed.Item2 };
        Assert.False(TownBoatAccessRules.Allows(town, "visitor:a", 4));
        Assert.True(TownBoatAccessRules.Allows(town, "visitor:a", 5));
        Assert.Equal(visitor is null, TownBoatAccessRules.Allows(town, "visitor:b", 5));
        var law = Assert.Single(passed.Item2.Laws);
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeAmendment(passed.Item1, passed.Item2,
            Town, "a", law.Id, "Boat access: Allow every visitor.", Adults, 6, Day));
        var repealed = Pass(TownLawRules.ProposeRepeal(passed.Item1, passed.Item2, Town, "a", law.Id, Adults, 6, Day), 7, "a", "b");
        town = town with { Government = repealed.Item2 };
        Assert.True(TownBoatAccessRules.Allows(town, "visitor:a", 6));
        Assert.False(TownBoatAccessRules.Allows(town, "visitor:a", 7));
    }

    [Theory]
    [InlineData("Do not cut trees in the north grove.")]
    [InlineData(": Do not cut trees.")]
    [InlineData("Grove:")]
    public void LawTextWithoutAClearSubjectAndRuleIsRefused(string text)
    {
        var (council, government) = Fresh();
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeAdoption(council, government, Town, "a", text,
            TownLawRules.Jurisdiction, [], Adults, 0, Day));
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeAdoption(council, government, Town, "a", Grove,
            "everywhere", [], Adults, 0, Day));
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeAdoption(council, government, Town, "a", Grove,
            TownLawRules.Site, [], Adults, 0, Day));
    }

    [Fact]
    public void LocalConductLawsCoverVisitorsOnClaimedLandButNotAnUnclaimedBorderOrAnotherTownsClaim()
    {
        var (_, government) = Adopt(Grove);
        var visitorOnClaim = TownLawRules.Applicable(government, Town, Titles, isResident: false, new(1, 0), 5);
        Assert.Equal("Grove", Assert.Single(visitorOnClaim).Version.Subject);
        // A drawn border without title, or another Town's title, does not carry this Town's territorial law.
        Assert.Empty(TownLawRules.Applicable(government, Town, Titles, isResident: true, new(3, 0), 5));
        Assert.Empty(TownLawRules.Applicable(government, Town, Titles, isResident: true, new(8, 8), 5));
    }

    [Fact]
    public void ResidentDutiesFollowResidentsWhileTerritorialRulesStayOnTheLand()
    {
        var (_, duty) = Adopt("Watch rota: Take a turn watching the Warehouse each season.", TownLawRules.ResidentDuty);
        Assert.Single(TownLawRules.Applicable(duty, Town, Titles, isResident: true, new(40, 40), 5));
        Assert.Empty(TownLawRules.Applicable(duty, Town, Titles, isResident: false, new(0, 0), 5));
        var (_, territorial) = Adopt(Grove);
        Assert.Empty(TownLawRules.Applicable(territorial, Town, Titles, isResident: true, new(40, 40), 5));
    }

    [Fact]
    public void SiteLawsApplyOnlyOnTheirRecordedClaimedTiles()
    {
        var (_, government) = Adopt(Grove, TownLawRules.Site, [new(1, 0), new(0, 0)]);
        var version = government.Laws[0].Versions[0];
        Assert.Equal([new GridPoint(0, 0), new GridPoint(1, 0)], version.SiteTiles);
        var (pendingCouncil, pendingGovernment) = Fresh();
        (pendingCouncil, pendingGovernment) = TownLawRules.ProposeAdoption(pendingCouncil, pendingGovernment, Town, "a", Grove,
            TownLawRules.Site, [new(1, 0), new(0, 0)], Adults, 0, Day);
        var notice = pendingCouncil.Notices.Single(n => n.Kind == "proposal");
        Assert.Contains("visitors included", notice.Text, StringComparison.Ordinal);
        Assert.Contains("Site tiles: 0,0;1,0", notice.Text, StringComparison.Ordinal);
        Assert.Contains(Grove, TownLawRules.VoteText(pendingGovernment.LawDrafts[0]), StringComparison.Ordinal);
        Assert.Single(TownLawRules.Applicable(government, Town, Titles, false, new(0, 0), 5));
        Assert.Empty(TownLawRules.Applicable(government, Town, Titles, false, new(2, 0), 5));
    }

    [Fact]
    public void TwoYesVotesAmendAndThenRepealWhilePreservingEveryEarlierWording()
    {
        var civic = Adopt(Grove);
        var lawId = civic.Item2.Laws[0].Id;
        civic = TownLawRules.ProposeAmendment(civic.Item1, civic.Item2, Town, "b", lawId,
            "Grove: Cut no more than one tree a season in the north grove.", Adults, 7, Day);
        Assert.StartsWith("Amend law 1 to read:", civic.Item1.Proposals[^1].Text, StringComparison.Ordinal);
        civic = Pass(civic, 8, "b", "c");
        var amended = civic.Item2.Laws[0];
        Assert.Equal(2, amended.Versions.Count);
        Assert.Equal(8, amended.Versions[0].EndedTick);
        Assert.Equal(TownLawRules.Jurisdiction, amended.Versions[1].Scope);
        Assert.Equal(1, TownLawRules.InForceAt(amended, 7)!.Version);
        Assert.Equal(2, TownLawRules.InForceAt(amended, 8)!.Version);

        civic = TownLawRules.ProposeRepeal(civic.Item1, civic.Item2, Town, "c", lawId, Adults, 12, Day);
        civic = Pass(civic, 13, "a", "c");
        var repealed = civic.Item2.Laws[0];
        Assert.False(TownLawRules.IsInForce(repealed));
        Assert.Equal(2, repealed.Versions.Count);
        Assert.Equal(13, repealed.Versions[1].EndedTick);
        Assert.Equal(2, TownLawRules.InForceAt(repealed, 12)!.Version);
        Assert.Null(TownLawRules.InForceAt(repealed, 13));
        Assert.Throws<InvalidOperationException>(() => TownLawRules.ProposeRepeal(civic.Item1, civic.Item2, Town, "a", lawId, Adults, 14, Day));
    }

    [Fact]
    public void EquivalentLawRequestsShareAWindowButADifferentScopeIsADifferentRequest()
    {
        var (council, government) = Fresh();
        (council, government) = TownLawRules.ProposeAdoption(council, government, Town, "a", Grove, TownLawRules.Jurisdiction, [], Adults, 0, Day);
        (council, government) = TownLawRules.ProposeAdoption(council, government, Town, "b", "grove:   do not cut trees in the north grove.",
            TownLawRules.Jurisdiction, [], Adults, 3, Day);
        Assert.Single(council.Proposals);
        Assert.Equal(Day, council.Proposals[0].DeadlineTick);
        (council, government) = TownLawRules.ProposeAdoption(council, government, Town, "b", Grove, TownLawRules.ResidentDuty, [], Adults, 3, Day);
        Assert.Equal(2, council.Proposals.Count);
        Assert.Equal(2, government.LawDrafts.Count);
    }

    [Fact]
    public void RejectedAndCancelledLawProposalsRecordNothingAndSilenceNeverPasses()
    {
        var (council, government) = Fresh();
        (council, government) = TownLawRules.ProposeAdoption(council, government, Town, "a", Grove, TownLawRules.Jurisdiction, [], Adults, 0, Day);
        council = TownGovernanceRules.VoteProposal(council, council.Proposals[0].Id, "a", true, 1);
        council = TownGovernanceRules.Advance(council, Town, "seed", Adults, Day, Day);
        (council, government) = TownLawRules.Enact(council, government, Town, "Test Town", Day);
        Assert.Equal("rejected", council.Proposals[0].Status);
        Assert.Empty(government.Laws);
        Assert.Equal("not_passed", government.LawDrafts[0].Status);

        (council, government) = TownLawRules.ProposeAdoption(council, government, Town, "a", "Wells: Keep the well covered.",
            TownLawRules.Jurisdiction, [], Adults, Day + 1, Day);
        council = TownGovernanceRules.Advance(council, Town, "seed", Adults.Append("d").ToArray(), Day + 2, Day);
        (_, government) = TownLawRules.Enact(council, government, Town, "Test Town", Day + 2);
        Assert.Empty(government.Laws);
        Assert.Equal("not_passed", government.LawDrafts[1].Status);
    }

    [Fact]
    public void AChangePassedAfterTheLawAlreadyChangedIsKeptStaleRatherThanRewritingANewerWording()
    {
        var civic = Adopt(Grove);
        var lawId = civic.Item2.Laws[0].Id;
        civic = TownLawRules.ProposeAmendment(civic.Item1, civic.Item2, Town, "a", lawId, "Grove: Cut only fallen trees.", Adults, 6, Day);
        var amendment = civic.Item1.Proposals[^1].Id;
        civic = TownLawRules.ProposeRepeal(civic.Item1, civic.Item2, Town, "b", lawId, Adults, 6, Day);
        var repeal = civic.Item1.Proposals[^1].Id;
        var council = TownGovernanceRules.VoteProposal(civic.Item1, amendment, "a", true, 7);
        council = TownGovernanceRules.VoteProposal(council, amendment, "b", true, 7);
        council = TownGovernanceRules.VoteProposal(council, repeal, "b", true, 8);
        council = TownGovernanceRules.VoteProposal(council, repeal, "c", true, 8);
        var (enactedCouncil, government) = TownLawRules.Enact(council, civic.Item2, Town, "Test Town", 8);
        Assert.True(TownLawRules.IsInForce(government.Laws[0]));
        Assert.Equal("Cut only fallen trees.", TownLawRules.Current(government.Laws[0]).Rule);
        Assert.Equal("stale", government.LawDrafts.Single(d => d.ProposalId == repeal).Status);
        Assert.Contains(enactedCouncil.Notices, n => n.Kind == "law" && n.Text.Contains("fresh proposal", StringComparison.Ordinal));
    }
}
