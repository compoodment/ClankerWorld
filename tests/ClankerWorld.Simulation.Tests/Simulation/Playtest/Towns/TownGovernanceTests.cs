using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernanceTests
{
    private const int Day = 10;
    private static readonly string[] Adults = ["a", "b", "c", "d", "e", "f", "g", "h"];
    private static TownGovernanceState Advance(TownGovernanceState state, long tick, string[]? adults = null) =>
        TownGovernanceRules.Advance(state, "town:test", "saved-council-seed", adults ?? Adults, tick, Day);
    private static TownGovernanceState Registered(int count = 3)
    {
        var state = TownGovernanceState.Create(Adults);
        foreach (var id in Adults.Take(count)) state = TownGovernanceRules.Register(state, id, true, null, Adults, 0);
        return state;
    }
    private static TownGovernanceState Elected()
    {
        var state = Advance(Registered(), 0);
        state = TownGovernanceRules.VoteElection(state, state.Election!.Id, "a", ["a", "b", "c"], 1);
        return Advance(state, Day);
    }
    private static TownGovernanceState Proposal(TownGovernanceState state, string[] adults, long tick = 0) =>
        TownGovernanceRules.SubmitProposal(state, "town:test", adults[0], "law", null, "Keep a public record of harvest dates.", "unchanged", adults, tick, Day);

    [Theory]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    [InlineData(2, 2)]
    public void OrdinaryProposalsNeedStrictMajorityAndSilenceNeverApproves(int count, int required)
    {
        var adults = Adults.Take(count).ToArray();
        var state = Proposal(TownGovernanceState.Create(adults), adults);
        Assert.Equal(required, state.Proposals[0].RequiredYes);
        foreach (var actor in adults.Take(required - 1))
            state = TownGovernanceRules.VoteProposal(state, state.Proposals[0].Id, actor, true, 1);
        Assert.Equal("pending", state.Proposals[0].Status);
        Assert.Equal("rejected", Advance(state, Day, adults).Proposals[0].Status);
    }

    [Fact]
    public void ProposalVotesAreFinalAndSettledResultsStaySettledAcrossCouncilChanges()
    {
        var adults = Adults.Take(4).ToArray();
        var state = Proposal(TownGovernanceState.Create(adults), adults);
        var id = state.Proposals[0].Id;
        state = TownGovernanceRules.VoteProposal(state, id, "a", true, 1);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteProposal(state, id, "a", false, 2));
        state = TownGovernanceRules.VoteProposal(state, id, "b", true, 2);
        state = TownGovernanceRules.VoteProposal(state, id, "c", true, 3);
        Assert.Equal("passed", state.Proposals[0].Status);
        state = Advance(state, 4, ["a", "b", "c", "d", "e"]);
        Assert.Equal("passed", state.Proposals[0].Status);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteProposal(state, id, "d", false, 4));
    }

    [Fact]
    public void CouncilRosterChangesCancelPendingProposalsAndAllowFreshVotes()
    {
        var adults = Adults.Take(4).ToArray();
        var state = Proposal(TownGovernanceState.Create(adults), adults);
        state = TownGovernanceRules.VoteProposal(state, state.Proposals[0].Id, "a", true, 1);
        var newer = adults.Append("e").ToArray();
        state = Advance(state, 2, newer);
        Assert.Equal("cancelled", state.Proposals[0].Status);
        state = Proposal(state, newer, 2);
        Assert.Equal(2, state.Proposals.Count);
        Assert.Empty(state.Proposals[1].Votes);
        Assert.Equal(12, state.Proposals[1].DeadlineTick);
    }

    [Fact]
    public void EquivalentAdmissionRequestsShareDeadlineAndFailedRequestsHaveCooldown()
    {
        var adults = Adults.Take(4).ToArray();
        var state = TownGovernanceState.Create(adults);
        state = TownGovernanceRules.SubmitProposal(state, "town:test", "newcomer", "admission", "newcomer", "Please admit me.", "same", adults, 0, Day);
        state = TownGovernanceRules.SubmitProposal(state, "town:test", "newcomer", "admission", "newcomer", "I would like to join.", "same", adults, 2, Day);
        Assert.Single(state.Proposals);
        Assert.Equal(Day, state.Proposals[0].DeadlineTick);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteProposal(state, state.Proposals[0].Id, "newcomer", true, 2));
        Assert.Null(TownGovernanceRules.AdmissionApproval(state, "newcomer"));
        state = TownGovernanceRules.WithdrawProposal(state, state.Proposals[0].Id, "newcomer", 3);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.SubmitProposal(state, "town:test", "newcomer", "admission", "newcomer", "Let me in.", "same", adults, 4, Day));
        state = TownGovernanceRules.SubmitProposal(state, "town:test", "newcomer", "admission", "newcomer", "Let me in.", "new material", adults, 4, Day);
        Assert.Equal(2, state.Proposals.Count);
    }

    [Fact]
    public void AdmissionResultIsRecordedOnlyAfterActualCouncilVotes()
    {
        var adults = Adults.Take(3).ToArray();
        var state = TownGovernanceRules.SubmitProposal(TownGovernanceState.Create(adults), "town:test", "visitor", "admission", "visitor", "Admit visitor.", "same", adults, 0, Day);
        state = TownGovernanceRules.VoteProposal(state, state.Proposals[0].Id, "a", true, 1);
        Assert.Null(TownGovernanceRules.AdmissionApproval(state, "visitor"));
        state = TownGovernanceRules.VoteProposal(state, state.Proposals[0].Id, "b", true, 1);
        Assert.Equal("passed", TownGovernanceRules.AdmissionApproval(state, "visitor")!.Status);
        Assert.DoesNotContain("visitor", state.Members);
    }

    [Fact]
    public void CandidateConsentAndOneVoterIncludingSelfCanElectThreeSupportedRepresentatives()
    {
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.Register(Registered(), "visitor", true, null, Adults, 0));
        var state = Elected();
        Assert.Equal("representative", state.Form);
        Assert.Equal(["a", "b", "c"], state.Members);
        Assert.Equal(110, state.TermEndTick);
        Assert.Single(state.ElectionHistory);
        Assert.Single(state.ElectionHistory[0].Ballots);
    }

    [Fact]
    public void SilentElectionFailsAndZeroVoteCandidatesCannotEnterDraw()
    {
        var state = Advance(Registered(4), 0);
        state = TownGovernanceRules.VoteElection(state, state.Election!.Id, "a", ["a", "b"], 1);
        state = Advance(state, Day);
        Assert.Equal("all_adult", state.Form);
        Assert.Equal("candidates", state.Fallback);
        Assert.Equal("failed", state.ElectionHistory[0].Stage);
        Assert.Empty(state.ElectionHistory[0].DrawOrder);
        Assert.Equal(["a", "b"], state.ElectionHistory[0].Supported);
        state = Advance(Registered(), 0);
        Assert.Equal("failed", Advance(state, Day).ElectionHistory[0].Stage);
    }

    [Fact]
    public void BallotsAreRevisableAndWithdrawalsPreserveOtherChoicesWithoutResettingDeadline()
    {
        var state = Advance(Registered(4), 0);
        var contest = state.Election!.Id;
        state = TownGovernanceRules.VoteElection(state, contest, "a", ["a", "b", "c"], 1);
        state = TownGovernanceRules.VoteElection(state, contest, "a", ["b", "c", "d"], 2);
        Assert.Single(state.Election!.Ballots);
        state = TownGovernanceRules.WithdrawCandidate(state, "c", 3);
        Assert.Equal(["b", "d"], state.Election!.Ballots[0].Choices);
        Assert.Equal(Day, state.Election.DeadlineTick);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteElection(state, contest, "a", ["c"], 3));
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteElection(state, contest, "a", ["b", "b"], 3));
    }

    [Fact]
    public void EligibilityIsFixedAtOpeningAndDeathOrDepartureRemovesBallotAndCandidacy()
    {
        var state = Advance(Registered(), 0);
        var contest = state.Election!.Id;
        state = TownGovernanceRules.VoteElection(state, contest, "a", ["a", "b", "c"], 1);
        state = TownGovernanceRules.Register(state, "d", true, null, Adults, 2);
        Assert.DoesNotContain("d", state.Election!.Candidates);
        var after = Adults.Where(id => id != "a").Append("newadult").ToArray();
        state = Advance(state, 3, after);
        Assert.DoesNotContain("a", state.Election!.Voters);
        Assert.DoesNotContain("a", state.Election.Candidates);
        Assert.Empty(state.Election.Ballots);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteElection(state, contest, "newadult", ["b"], 3));
    }

    [Fact]
    public void SilentCutoffRunoffUsesSavedFairDrawAmongSupportedTiedCandidatesOnly()
    {
        var state = Advance(Registered(5), 0);
        var id = state.Election!.Id;
        state = TownGovernanceRules.VoteElection(state, id, "a", ["a", "b", "c"], 1);
        state = TownGovernanceRules.VoteElection(state, id, "b", ["a", "d"], 2);
        state = Advance(state, Day);
        Assert.Equal("runoff", state.Election!.Stage);
        Assert.Equal(["a"], state.Election.SettledSeats);
        Assert.Equal(2, state.Election.Seats);
        Assert.Equal(["b", "c", "d"], state.Election.Candidates);
        Assert.DoesNotContain("e", state.Election.Candidates);
        state = Advance(state, 2 * Day);
        Assert.Equal("representative", state.Form);
        Assert.Contains("a", state.Members);
        var finished = state.ElectionHistory[0];
        Assert.Equal(3, finished.DrawOrder.Count);
        Assert.Equal(["b", "c", "d"], finished.DrawOrder.Order(StringComparer.Ordinal));
        Assert.Equal(120, state.TermEndTick);
        Assert.Equal(finished.DrawOrder, Advance(state, 2 * Day).ElectionHistory[0].DrawOrder);
    }

    [Fact]
    public void RunoffRefreshesVotersButNotCandidatesAndPreservesSettledSeats()
    {
        var state = Advance(Registered(4), 0);
        var id = state.Election!.Id;
        state = TownGovernanceRules.VoteElection(state, id, "a", ["a", "b", "c"], 1);
        state = TownGovernanceRules.VoteElection(state, id, "b", ["a", "d"], 2);
        state = Advance(state, Day, Adults.Append("newadult").ToArray());
        Assert.Contains("newadult", state.Election!.Voters);
        state = TownGovernanceRules.VoteElection(state, id, "newadult", ["b", "d"], 11);
        state = Advance(state, 2 * Day, Adults.Append("newadult").ToArray());
        Assert.Equal(["a", "b", "d"], state.Members);
    }

    [Fact]
    public void SevenAdultsRetainRepresentationThreeAdultsTriggerDemographicFallbackUntilEightAgain()
    {
        var state = Advance(Elected(), 11, Adults.Take(7).ToArray());
        Assert.Equal("representative", state.Form);
        state = Advance(state, 12, Adults.Take(3).ToArray());
        Assert.Equal("demographic", state.Fallback);
        Assert.Equal("all_adult", state.Form);
        Assert.Null(Advance(state, 13, Adults.Take(7).ToArray()).Election);
        Assert.NotNull(Advance(state, 14, Adults).Election);
    }

    [Fact]
    public void RepresentativeVacanciesStillNeedTwoYesVotesAndReplacementKeepsTerm()
    {
        var state = Elected();
        var adults = Adults.Where(id => id != "c").ToArray();
        state = Advance(state, 11, adults);
        Assert.Equal("replacement", state.Election!.Kind);
        Assert.Equal(1, state.Election.Seats);
        state = TownGovernanceRules.Register(state, "d", false, state.TermEndTick, adults, 11);
        // Registration after opening cannot enter this fixed contest; retry picks it up after failure.
        state = Advance(state, 21, adults);
        Assert.Equal("all_adult", state.Form);
        var vacant = Elected() with { Members = ["a"] };
        vacant = Proposal(vacant, Adults, 11);
        Assert.Equal(2, vacant.Proposals[0].RequiredYes);
        vacant = TownGovernanceRules.VoteProposal(vacant, vacant.Proposals[0].Id, "a", true, 12);
        Assert.Equal("rejected", vacant.Proposals[0].Status);
    }

    [Fact]
    public void ReadyRegularCouncilLosingAWinnerFailsWithoutShorteningIncumbentTerm()
    {
        var state = Advance(Elected(), 100);
        var id = state.Election!.Id;
        state = TownGovernanceRules.VoteElection(state, id, "a", ["a", "b", "c"], 101);
        // The normal regular window ends at term end; a runoff/ready result can be revalidated before seating too.
        var ready = state.Election! with { Stage = "ready", Supported = ["a", "b", "c"], SettledSeats = ["a", "b", "c"] };
        state = state with { Election = ready };
        state = TownGovernanceRules.WithdrawCandidate(state, "c", 102);
        state = Advance(state, 103);
        Assert.Equal("representative", state.Form);
        Assert.Equal(110, state.TermEndTick);
        Assert.NotEmpty(state.ElectionHistory);
        Assert.Equal("failed", state.ElectionHistory[^1].Stage);
        Assert.Equal(3, state.Members.Count);
    }

    [Fact]
    public void FailedElectionRetriesOnlyAfterDayOrPositiveImprovement()
    {
        var state = Advance(Advance(Registered(), 0), Day);
        Assert.Null(Advance(state, Day + 1, Adults.Take(7).ToArray()).Election);
        state = TownGovernanceRules.WithdrawCandidate(state, "a", Day + 1);
        Assert.Null(Advance(state, Day + 2).Election);
        state = TownGovernanceRules.Register(state, "d", true, null, Adults, Day + 2);
        state = TownGovernanceRules.Register(state, "e", true, null, Adults, Day + 2);
        Assert.NotNull(Advance(state, Day + 3).Election);
        Assert.NotNull(Advance(Advance(Advance(Registered(), 0), Day), 2 * Day).Election);
    }

    [Fact]
    public void RegularElectionSupersedesReplacementWithoutCopyingBallotsOrShortConsent()
    {
        var state = Elected();
        state = TownGovernanceRules.Register(state, "d", false, state.TermEndTick, Adults, 90);
        state = Advance(state, 95, Adults.Where(id => id != "c").ToArray());
        Assert.Equal("replacement", state.Election!.Kind);
        state = TownGovernanceRules.VoteElection(state, state.Election.Id, "a", ["d"], 96);
        state = Advance(state, 100, Adults.Where(id => id != "c").ToArray());
        Assert.Equal("regular", state.Election!.Kind);
        Assert.Empty(state.Election.Ballots);
        Assert.DoesNotContain("d", state.Election.Candidates);
        Assert.Equal("cancelled", state.ElectionHistory[^1].Stage);
        Assert.Equal(["a", "b"], state.Members);
    }

    [Fact]
    public void CivicKnowledgeNeedsAnActualNoticeOrAnInformedRelay()
    {
        var state = Registered();
        Assert.Empty(state.Knowledge);
        var notice = state.Notices[0].Id;
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.LearnNotice(state, "b", notice, 1, "a"));
        state = TownGovernanceRules.LearnNotice(state, "a", notice, 1);
        state = TownGovernanceRules.LearnNotice(state, "b", notice, 2, "a");
        Assert.Equal("a", state.Knowledge.Single(k => k.AgentId == "b").SourceAgentId);
        Assert.Equal(2, state.Knowledge.Count);
        Assert.Equal(2, TownGovernanceRules.LearnNotice(state, "b", notice, 3).Knowledge.Count);
    }
}
