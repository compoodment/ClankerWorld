using System.Text.Json;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownHearingProcedureTests
{
    [Fact]
    public void TiedLeadersAndActualBallotsSurviveInterruptionBeforeAFullNewDay()
    {
        string[] adults = ["a", "b", "c"];
        var contest = new TownCaseJudgeContest("case:judge:1", "waiting", 0, 0, null, null, [], [], [], [], 0, []);
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 0, 10);
        contest = contest with { Ballots = [new("a", "a"), new("b", "b")] };
        contest = TownHearingProcedure.AdvanceContest(contest, adults, adults, false, 10, 10);
        Assert.Equal(new[] { "a", "b" }, contest.TiedCandidates);
        var tie = Assert.Single(contest.Rounds);
        Assert.Equal("tie", tie.Result);
        Assert.Equal(new[] { "a", "b" }, tie.TiedCandidates);
        Assert.Equal(2, tie.Ballots.Count);
        Assert.Equal(new[] { "a", "b" }, contest.Candidates);
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
