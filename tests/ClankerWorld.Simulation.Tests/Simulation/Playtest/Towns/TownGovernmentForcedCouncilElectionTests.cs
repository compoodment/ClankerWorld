using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownGovernmentHandoverTests
{
    private static readonly string[] FirstCouncil = ["a", "b", "c"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryInitialElectionKeepsItsTermAfterPopulationDropAndUnrelatedHandover(bool withHandover)
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        var initial = Assert.IsType<TownElection>(town.Council.Election);
        Assert.Equal("initial", initial.Kind);
        Assert.Equal(8, initial.Voters.Count);
        Assert.Equal(Day, initial.DeadlineTick);
        if (withHandover)
        {
            town.Yes(town.Propose(ElectedMayor), "a", "b", "c", "d", "e");
            Assert.Equal("handover", town.Government.Changes[^1].Status);
            Assert.Null(town.Government.Changes[^1].ForcedCouncilElectionId);
            Assert.Equal(initial.Id, town.Council.Election!.Id);
        }
        town.Adults = ["a", "b", "c", "d", "e", "f", "g"];
        town.Advance(1);
        town.Council = TownGovernanceRules.VoteElection(town.Council, initial.Id, "a", FirstCouncil, 1);
        var restored = town.RoundTrip();

        foreach (var run in new[] { town, restored })
        {
            run.Advance(Day);
            Assert.Equal("representative", run.Council.Form);
            Assert.Equal(FirstCouncil, run.Council.Members);
            Assert.Equal("completed", Assert.Single(run.Council.ElectionHistory, election => election.Id == initial.Id).Stage);
            var electedTerm = run.Council.TermEndTick;
            Assert.Equal(11 * Day, electedTerm);
            run.Advance(3 * Day);
            if (withHandover)
            {
                Assert.Equal("cancelled", run.Government.Changes[^1].Status);
                Assert.Null(run.Government.Changes[^1].ForcedCouncilElectionId);
            }
            Assert.Equal("representative", run.Council.Form);
            Assert.Equal(FirstCouncil, run.Council.Members);
            Assert.Equal(electedTerm, run.Council.TermEndTick);
            Assert.DoesNotContain(run.Council.ElectionHistory, election => election.Id == initial.Id && election.Stage == "cancelled");
        }
        Assert.Equal(town.Save(), restored.Save());
    }

    [Fact]
    public void FailedOrdinaryInitialElectionRetainsFallbackAndActuallyRetriesDuringTheHandover()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        var initial = Assert.IsType<TownElection>(town.Council.Election);
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c", "d", "e");
        town.Adults = ["a", "b", "c", "d", "e", "f", "g"];
        town.Advance(1);
        town.Advance(Day);
        Assert.Equal("failed", Assert.Single(town.Council.ElectionHistory, election => election.Id == initial.Id).Stage);
        Assert.Equal("candidates", town.Council.Fallback);
        Assert.Null(town.Government.Changes[^1].ForcedCouncilElectionId);
        var retryTick = town.Council.RetryTick;
        Assert.Equal(2 * Day, retryTick);
        var restored = town.RoundTrip();

        foreach (var run in new[] { town, restored })
        {
            run.Advance(retryTick);
            var retry = Assert.IsType<TownElection>(run.Council.Election);
            Assert.NotEqual(initial.Id, retry.Id);
            Assert.Equal("initial", retry.Kind);
            Assert.Empty(retry.Ballots);
            Assert.Null(run.Government.Changes[^1].ForcedCouncilElectionId);
            run.Council = TownGovernanceRules.VoteElection(run.Council, retry.Id, "a", FirstCouncil, retryTick);
            run.Advance(retry.DeadlineTick);
            Assert.Equal("representative", run.Council.Form);
            Assert.Equal(FirstCouncil, run.Council.Members);
            Assert.Equal("completed", Assert.Single(run.Council.ElectionHistory, election => election.Id == retry.Id).Stage);
            Assert.Equal("cancelled", run.Government.Changes[^1].Status);
        }
        Assert.Equal(town.Save(), restored.Save());
    }

    [Fact]
    public void ForcedCouncilWaitsAcrossReloadAndOnlyItsCreatingHandoverCancelsIt()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        Assert.Null(town.Council.Election);
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        var forced = Assert.IsType<TownElection>(town.Council.Election);
        Assert.Equal(forced.Id, town.Government.Changes[^1].ForcedCouncilElectionId);
        town.Council = TownGovernanceRules.VoteElection(town.Council, forced.Id, "a", FirstCouncil, 0);
        town.Advance(Day);
        Assert.Equal("all_adult", town.Council.Form);
        Assert.Null(town.Council.TermEndTick);
        Assert.Equal("ready", town.Council.Election!.Stage);
        Assert.Equal(FirstCouncil, town.Council.Election.SettledSeats);
        var restored = town.RoundTrip();

        foreach (var run in new[] { town, restored })
        {
            run.Advance(3 * Day);
            var change = Assert.Single(run.Government.Changes);
            Assert.Equal("cancelled", change.Status);
            Assert.Equal(forced.Id, change.ForcedCouncilElectionId);
            Assert.Equal(TownArrangementRules.Initial, run.Government.Arrangement);
            Assert.Null(run.Council.Election);
            Assert.Equal("cancelled", Assert.Single(run.Council.ElectionHistory, election => election.Id == forced.Id).Stage);
            run.Advance(3 * Day + 1);
            Assert.Null(run.Council.Election);
            Assert.Equal("all_adult", run.Council.Form);
            Assert.Null(run.Council.TermEndTick);
        }
        Assert.Equal(town.Save(), restored.Save());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediatelyFailedForcedAttemptTracksOnlyANewActuallyForcedRetry(bool ordinaryPopulationNow)
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        var failed = Assert.Single(town.Council.ElectionHistory);
        Assert.Equal("failed", failed.Stage);
        Assert.Equal("initial", failed.Kind);
        Assert.Equal(failed.Id, town.Government.Changes[^1].ForcedCouncilElectionId);
        Assert.Null(town.Council.Election);
        Assert.Equal("initial", town.Council.Fallback);
        Assert.Equal(Day, town.Council.RetryTick);
        Assert.NotEmpty(town.Council.RetryCircumstances);
        var retryCircumstances = town.Council.RetryCircumstances;
        town.Advance(1);
        Assert.Single(town.Council.ElectionHistory);
        Assert.Equal(retryCircumstances, town.Council.RetryCircumstances);
        var restored = town.RoundTrip();

        foreach (var run in new[] { town, restored })
        {
            run.RegisterCouncil("a", "b", "c");
            if (ordinaryPopulationNow) run.Adults = ["a", "b", "c", "d", "e", "f", "g", "h"];
            run.Advance(2);
            var retry = Assert.IsType<TownElection>(run.Council.Election);
            Assert.NotEqual(failed.Id, retry.Id);
            Assert.Equal(2, retry.OpenedTick);
            Assert.Empty(retry.Ballots);
            Assert.Equal(ordinaryPopulationNow ? failed.Id : retry.Id, run.Government.Changes[^1].ForcedCouncilElectionId);
            // Consent improved before the retry deadline; the recorded old attempt stays failed.
            Assert.True(run.Tick < run.Council.RetryTick);
            Assert.Equal("failed", Assert.Single(run.Council.ElectionHistory, election => election.Id == failed.Id).Stage);
            run.Council = TownGovernanceRules.VoteElection(run.Council, retry.Id, "a", FirstCouncil, run.Tick);
            run.Advance(retry.DeadlineTick);
            Assert.Equal(ordinaryPopulationNow ? "representative" : "all_adult", run.Council.Form);
            run.Advance(3 * Day);
            Assert.Equal("cancelled", run.Government.Changes[^1].Status);
            Assert.Equal(ordinaryPopulationNow ? "completed" : "cancelled",
                Assert.Single(run.Council.ElectionHistory, election => election.Id == retry.Id).Stage);
            run.RoundTrip();
        }
        Assert.Equal(town.Save(), restored.Save());
    }

    [Fact]
    public void CompletedHandoverDoesNotLeaveItsUnneededForcedCouncilElectionAlive()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.Yes(town.Propose(AllAdult), "a", "b", "c");
        town.RegisterCouncil("a", "b", "c");
        town.Yes(town.Propose(new(TownArrangementRules.ElectedCouncil, TownArrangementRules.NoOffice)), "a", "b", "c");
        var forced = Assert.IsType<TownElection>(town.Council.Election);
        Assert.Equal(forced.Id, town.Government.Changes[^1].ForcedCouncilElectionId);
        Assert.Equal("handover", town.Government.Changes[^1].Status);
        var restored = town.RoundTrip();

        foreach (var run in new[] { town, restored })
        {
            run.Adults = ["a", "b", "c"];
            run.Advance(1);
            Assert.Equal("completed", run.Government.Changes[^1].Status);
            Assert.Equal(forced.Id, run.Government.Changes[^1].ForcedCouncilElectionId);
            Assert.Equal(TownArrangementRules.ElectedCouncil, run.Government.Arrangement.Ordinary);
            Assert.Null(run.Council.Election);
            Assert.Equal("all_adult", run.Council.Form);
            Assert.Equal("cancelled", Assert.Single(run.Council.ElectionHistory, election => election.Id == forced.Id).Stage);
            run.RoundTrip();
        }
        Assert.Equal(town.Save(), restored.Save());
    }

    [Fact]
    public void ForcedElectionKeepsItsIdentityThroughALaterRunoffAndSuccessfulHandover()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.RegisterCouncil("a", "b", "c", "d", "e");
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        town.RegisterMayor("a", "land");
        var electionId = town.Council.Election!.Id;
        town.Council = TownGovernanceRules.VoteElection(town.Council, electionId, "a", FirstCouncil, 0);
        town.Council = TownGovernanceRules.VoteElection(town.Council, electionId, "b", ["a", "d"], 0);
        town.Advance(Day);
        Assert.Equal("runoff", town.Council.Election!.Stage);
        Assert.Equal(Day, town.Council.Election.OpenedTick);
        Assert.Equal(electionId, town.Government.Changes[^1].ForcedCouncilElectionId);
        Assert.True(town.Council.Election.OpenedTick > town.Government.Changes[^1].ApprovedTick);
        var restored = town.RoundTrip();

        foreach (var run in new[] { town, restored })
        {
            run.Advance(2 * Day);
            Assert.Equal("ready", run.Council.Election!.Stage);
            Assert.Equal("all_adult", run.Council.Form);
            Assert.NotEmpty(run.Council.Election.DrawOrder);
            run.VoteEverything(run.Tick);
            run.Advance(3 * Day);
            Assert.Equal("completed", run.Government.Changes[^1].Status);
            Assert.Equal(electionId, run.Government.Changes[^1].ForcedCouncilElectionId);
            Assert.Equal(ElectedMayor, run.Government.Arrangement);
            Assert.Equal("representative", run.Council.Form);
            Assert.Equal(3, run.Council.Members.Count);
            Assert.Equal("completed", Assert.Single(run.Council.ElectionHistory, election => election.Id == electionId).Stage);
            Assert.Equal("a", Assert.Single(run.Government.Offices).HolderId);
            run.RoundTrip();
        }
        Assert.Equal(town.Save(), restored.Save());
    }
}
