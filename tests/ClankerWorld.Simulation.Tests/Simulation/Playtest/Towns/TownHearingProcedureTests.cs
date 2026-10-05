using System.Text.Json;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownHearingProcedureTests
{
    [Fact]
    public void AnEmptyCaseElectionWaitsForNewConsentInsteadOfRecordingFailuresEveryDay()
    {
        string[] adults = ["subject", "judge", "witness"];
        var households = new Dictionary<string, string?>
        { ["subject"] = "subject-home", ["judge"] = "judge-home", ["witness"] = "witness-home" };
        var council = TownGovernanceState.Create(adults);
        var government = TownGovernmentState.Create() with
        {
            Arrangement = TownArrangementRules.Initial with { NonLand = TownArrangementRules.Mayor },
            Offices = [new("non_land", "subject", 0, 1_000, null, null, "election:subject")],
        };
        var state = TownNonviolentRulesTests.Pending();
        (state, council) = TownCaseJudgeRules.Advance(state, council, government, adults, households, 10, 10);
        Assert.Equal("failed", Assert.Single(state.Cases[0].ContestHistory).Stage);
        var failed = JsonSerializer.Serialize(state);
        for (var day = 2; day <= 5; day++)
            (state, council) = TownCaseJudgeRules.Advance(state, council, government, adults, households, day * 10, 10);
        Assert.Equal(failed, JsonSerializer.Serialize(state));
        state = TownCaseJudgeRules.Register(state, state.Cases[0].Id, "judge", adults, households, 51);
        (state, council) = TownCaseJudgeRules.Advance(state, council, government, adults, households, 51, 10);
        var contest = Assert.IsType<TownCaseJudgeContest>(state.Cases[0].Contest);
        Assert.Equal("voting", contest.Stage);
        Assert.Equal("judge", Assert.Single(contest.Candidates));
        Assert.Equal(61, contest.RoundDeadlineTick);
        Assert.Single(state.Cases[0].ContestHistory);
    }

    [Fact]
    public void TiedLeadersAndActualBallotsSurviveInterruptionBeforeAFullNewDay()
    {
        string[] adults = ["a", "b", "c"];
        string[] tiedLeaders = ["a", "b"];
        var contest = new TownCaseJudgeContest("case:judge:1", "waiting", 0, 0, null, null, [], [], [], [], 0, []);
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 0, 10);
        contest = contest with { Ballots = [new("a", "a"), new("b", "b")] };
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 10, 10);
        Assert.Equal(tiedLeaders, contest.TiedCandidates);
        var tie = Assert.Single(contest.Rounds);
        Assert.Equal("tie", tie.Result);
        Assert.Equal(tiedLeaders, tie.TiedCandidates);
        Assert.Equal(2, tie.Ballots.Count);
        Assert.Equal(tiedLeaders, contest.Candidates);
        contest = contest with { Ballots = [new("c", "b")] };
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, true, 13, 10);
        Assert.Equal("waiting", contest.Stage);
        Assert.Equal(1, contest.Interruptions);
        Assert.Equal("interrupted", contest.Rounds[^1].Result);
        Assert.Single(contest.Rounds[^1].Ballots);
        var replay = JsonSerializer.Deserialize<TownCaseJudgeContest>(JsonSerializer.Serialize(contest))!;
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 20, 10);
        replay = TownHearingProcedure.AdvanceContest(replay, adults, adults, false, 20, 10);
        Assert.Equal(30, contest.RoundDeadlineTick);
        Assert.Empty(contest.Ballots);
        Assert.DoesNotContain("c", contest.Candidates);
        contest = contest with { Ballots = [new("c", "b")] };
        replay = replay with { Ballots = [new("c", "b")] };
        Assert.Equal("voting", TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 29, 10).Stage);
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 30, 10);
        replay = TownHearingProcedure.AdvanceContest(replay, adults, adults, false, 30, 10);
        Assert.Equal("completed", contest.Stage);
        Assert.Equal("b", contest.WinnerId);
        Assert.Equal(JsonSerializer.Serialize(contest), JsonSerializer.Serialize(replay));
    }
}
