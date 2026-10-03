using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Protected handovers that need both an elected council and a mayor, whichever
/// successor is ready first, and lapsed handovers that must leave the current
/// arrangement's own council elections and retries alone.
/// </summary>
public sealed class TownGovernmentHandoverTests
{
    private const int Day = 10;
    private static readonly TownArrangement ElectedMayor = new(TownArrangementRules.ElectedCouncil, TownArrangementRules.Mayor);
    private static readonly TownArrangement AllAdult = new(TownArrangementRules.AllAdultCouncil, TownArrangementRules.NoOffice);

    private sealed class Town(params string[] adults)
    {
        private readonly HashSet<string> known = new(adults, StringComparer.Ordinal);
        public string Id { get; init; } = "town:test";
        public string[] Adults { get; set; } = adults;
        public TownGovernanceState Council { get; set; } = TownGovernanceState.Create(adults);
        public TownGovernmentState Government { get; set; } = TownGovernmentState.Create();
        public long Tick { get; private set; }
        public void Advance(long tick)
        {
            Tick = tick;
            (Council, Government) = TownGovernmentRules.Advance(Council, Government, Id, "Test Town", "seed", Adults, tick, Day);
            known.UnionWith(Adults);
            var society = SocietyFixture.CreateGenesis("government-validation", known.Select(id => SocietyFixture.CreateFounder(id, id))) with { WorldTick = tick };
            var town = new TownRuntimeState(Id, "Test Town", "founded", 0, Adults, [], [], Governance: Council, Government: Government);
            TownGovernanceValidation.Validate(town, society, Day);
            TownGovernmentValidation.Validate(town, society, [], Day);
        }
        public string Propose(TownArrangement target, string actor = "a")
        {
            (Council, Government) = TownGovernmentRules.Propose(Council, Government, Id, actor, target, false, Adults, Tick, Day);
            return Government.Changes[^1].Id;
        }
        public void Yes(string id, params string[] voters)
        {
            foreach (var voter in voters) Government = TownGovernmentRules.Vote(Government, id, voter, true, Tick);
            Advance(Tick);
        }
        public void RegisterMayor(string actor, string mandates) =>
            (Council, Government) = TownGovernmentRules.RegisterMayor(Council, Government, actor, mandates, Adults, Tick);
        public void RegisterCouncil(params string[] actors)
        {
            foreach (var actor in actors) Council = TownGovernanceRules.Register(Council, actor, true, null, Adults, Tick);
        }
        // Everyone votes: council ballot for a,b,c and mayoral ballot for the first candidate.
        public void VoteEverything(long tick)
        {
            if (Council.Election is { Stage: "main" } election && election.Ballots.Count == 0)
                Council = TownGovernanceRules.VoteElection(Council, election.Id, "a", election.Candidates.Take(3).ToArray(), tick);
            if (Government.Contest is { Stage: "voting" } contest && contest.Ballots.Count == 0 && contest.Candidates.Count > 0)
                Government = TownGovernmentRules.VoteMayor(Government, TownGovernmentRules.RoundToken(contest), "a", contest.Candidates[0], tick);
        }
    }


    // Council candidates are willing before approval, so the council is ready before the mayor.
    [Fact]
    public void ACouncilReadyBeforeTheMayorStillCompletesFromAnAllAdultCouncil()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.Yes(town.Propose(AllAdult), "a", "b", "c");
        Assert.Equal(AllAdult, town.Government.Arrangement);
        town.Advance(1);
        town.RegisterCouncil("a", "b", "c");
        town.RegisterMayor("a", "land");
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        for (var tick = town.Tick + 1; tick <= town.Tick + Day * 3 + 1 && town.Government.Changes[^1].Status == "handover"; tick++)
        {
            town.VoteEverything(tick);
            town.Advance(tick);
        }
        Assert.Equal("completed", town.Government.Changes[^1].Status);
    }

    // Five adults under the starting council, willing council candidates but no mayor: nothing is seated.
    [Fact]
    public void RepresentativesTheStartingCouncilWouldNotElectWaitForTheHandover()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.RegisterCouncil("a", "b", "c");
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        for (var tick = 1L; tick <= Day * 3; tick++)
        {
            town.VoteEverything(tick);
            town.Advance(tick);
        }
        Assert.Equal("cancelled", town.Government.Changes[^1].Status);
        Assert.Equal(TownArrangementRules.Initial, town.Government.Arrangement);
        // The lapse cancels the waiting election too, so it is never seated afterwards.
        for (var tick = Day * 3 + 1; tick <= Day * 6; tick++) town.Advance(tick);
        Assert.Null(town.Council.Election);
        Assert.Equal("all_adult", town.Council.Form);
    }

    // A legitimately elected representative council (council arrangement, population fell to five) is renewing
    // its term when an unrelated handover lapses; the cancellation must not cancel that regular election.
    [Fact]
    public void ALapsedHandoverKeepsTheCurrentCouncilsRegularElection()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        town.VoteEverything(0);
        town.Advance(10);
        Assert.Equal("representative", town.Council.Form);
        var termEnd = town.Council.TermEndTick!.Value;
        town.Adults = ["a", "b", "c", "d", "e"];
        town.Advance(11);
        Assert.Equal("representative", town.Council.Form);
        // Approve so the three-day deadline falls inside the regular election window.
        var approve = termEnd - Day / 2 - Day * 3;
        town.Advance(approve);
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        for (var tick = approve + 1; tick <= termEnd + Day; tick++)
        {
            if (town.Council.Election is { Stage: "main" } election && election.Ballots.Count == 0)
                town.Council = TownGovernanceRules.VoteElection(town.Council, election.Id, "d", ["a", "b", "c"], tick);
            town.Advance(tick);
        }
        Assert.Equal("cancelled", town.Government.Changes[^1].Status);
        Assert.DoesNotContain(town.Council.ElectionHistory, e => e.Kind == "regular" && e.Stage == "cancelled");
    }

    // A council arrangement whose representative term lapsed (candidates fallback, five adults) may retry its
    // election per the design; an unrelated cancelled handover must not erase that right.
    [Fact]
    public void ALapsedHandoverKeepsTheCouncilsOwnRetryForCandidates()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        town.VoteEverything(0);
        town.Advance(10);
        town.Adults = ["a", "b", "c", "d", "e"];
        foreach (var actor in new[] { "a", "b", "c" }) town.Council = TownGovernanceRules.WithdrawCandidate(town.Council, actor, 11);
        for (var tick = 11L; tick <= 115; tick++) town.Advance(tick);
        Assert.Equal(("all_adult", "candidates"), (town.Council.Form, town.Council.Fallback));
        town.Advance(120);
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        for (var tick = 121L; tick <= 151; tick++) town.Advance(tick);
        Assert.Equal("cancelled", town.Government.Changes[^1].Status);
        town.RegisterCouncil("a", "b", "c");
        for (var tick = 152L; tick <= 200; tick++) { town.VoteEverything(tick); town.Advance(tick); }
        Assert.Equal("representative", town.Council.Form);
    }

    // As above, from an elected governing leader to an elected council with a land mayor.
    [Fact]
    public void ACouncilReadyBeforeTheMayorStillCompletesFromAGoverningLeader()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.RegisterMayor("a", "ordinary");
        town.Yes(town.Propose(new(TownArrangementRules.Mayor, TownArrangementRules.NoOffice)), "a", "b", "c");
        town.VoteEverything(town.Tick);
        town.Advance(Day);
        Assert.Equal("leader", town.Council.Form);
        town.RegisterCouncil("a", "b", "c");
        town.RegisterMayor("b", "land");
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        for (var tick = town.Tick + 1; tick <= town.Tick + Day * 3 + 1 && town.Government.Changes[^1].Status == "handover"; tick++)
        {
            town.VoteEverything(tick);
            town.Advance(tick);
        }
        Assert.Equal("completed", town.Government.Changes[^1].Status);
    }

    // The mayor is ready first and council candidates agree later.
    [Fact]
    public void AMayorReadyBeforeTheCouncilCompletesFromAnAllAdultCouncil()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.Yes(town.Propose(AllAdult), "a", "b", "c");
        town.Advance(1);
        town.RegisterMayor("a", "land");
        town.Yes(town.Propose(ElectedMayor), "a", "b", "c");
        town.VoteEverything(town.Tick);
        for (var tick = town.Tick + 1; tick <= town.Tick + Day * 3 + 1 && town.Government.Changes[^1].Status == "handover"; tick++)
        {
            if (tick == 12) town.RegisterCouncil("a", "b", "c");
            town.VoteEverything(tick);
            town.Advance(tick);
        }
        Assert.Equal("completed", town.Government.Changes[^1].Status);
    }
}
