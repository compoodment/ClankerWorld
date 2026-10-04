using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernmentTests
{
    private const int Day = 10;
    private static readonly TownArrangement LandMayor = new(TownArrangementRules.Council, TownArrangementRules.Mayor);
    private static readonly TownArrangement BothMandates = new(TownArrangementRules.Mayor, TownArrangementRules.Mayor);

    private sealed class Town(params string[] adults)
    {
        private readonly HashSet<string> known = new(adults.Length == 0 ? ["a", "b", "c", "d"] : adults, StringComparer.Ordinal);
        public string Id { get; init; } = "town:test";
        public string[] Adults { get; set; } = adults.Length == 0 ? ["a", "b", "c", "d"] : adults;
        public TownGovernanceState Council { get; set; } = TownGovernanceState.Create(adults.Length == 0 ? ["a", "b", "c", "d"] : adults);
        public TownGovernmentState Government { get; set; } = TownGovernmentState.Create();
        public long Tick { get; private set; }
        public void Advance(long tick)
        {
            Tick = tick;
            (Council, Government) = TownGovernmentRules.Advance(Council, Government, Id, "Test Town", "seed", Adults, tick, Day);
            known.UnionWith(Adults);
            Validate();
        }
        public void Validate()
        {
            var society = SocietyFixture.CreateGenesis("government-validation", known.Select(id => SocietyFixture.CreateFounder(id, id))) with { WorldTick = Tick };
            var town = new TownRuntimeState(Id, "Test Town", "founded", 0, Adults, [], [], Governance: Council, Government: Government);
            TownGovernanceValidation.Validate(town, society, Day);
            TownGovernmentValidation.Validate(town, society, [], Day);
        }
        public Town Reload()
        {
            var restored = new Town(Adults)
            {
                Id = Id,
                Tick = Tick,
                Government = JsonSerializer.Deserialize<TownGovernmentState>(JsonSerializer.Serialize(Government))!,
                Council = JsonSerializer.Deserialize<TownGovernanceState>(JsonSerializer.Serialize(Council))!,
            };
            restored.known.UnionWith(known);
            restored.Validate();
            return restored;
        }
        public string Propose(TownArrangement target, string actor = "a", bool replace = false)
        {
            (Council, Government) = TownGovernmentRules.Propose(Council, Government, Id, actor, target, replace, Adults, Tick, Day);
            return Government.Changes[^1].Id;
        }
        public void Yes(string id, params string[] voters)
        {
            foreach (var voter in voters) Government = TownGovernmentRules.Vote(Government, id, voter, true, Tick);
            Advance(Tick);
        }
        public void Register(string actor, string mandates = "land") =>
            (Council, Government) = TownGovernmentRules.RegisterMayor(Council, Government, actor, mandates, Adults, Tick);
        public void Ballot(string actor, string candidate) =>
            Government = TownGovernmentRules.VoteMayor(Government, TownGovernmentRules.RoundToken(Government.Contest!), actor, candidate, Tick);
        public void Elect(TownArrangement target, string mandates = "land")
        {
            Register("a", mandates);
            Yes(Propose(target), "a", "b", "c");
            Ballot("a", "a");
            Advance(Tick + Day);
        }
    }

    [Fact]
    public void RetainedVacantLandMandateDoesNotBlockOrdinaryAuthorityChange()
    {
        var town = new Town();
        town.Elect(LandMayor);
        Assert.Equal(Day, town.Tick);
        (town.Council, town.Government) = TownGovernmentRules.Resign(
            town.Council, town.Government, "a", "land", town.Tick);
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(
            town.Council, town.Government, "a", "land", town.Tick);
        var vacant = Assert.Single(town.Government.Offices);
        Assert.Null(vacant.HolderId);
        Assert.Empty(town.Government.Consents);
        var officeHistory = town.Government.OfficeHistory.ToArray();

        // The vote changes ordinary authority, not the retained vacant land mandate.
        var target = new TownArrangement(TownArrangementRules.AllAdultCouncil, TownArrangementRules.Mayor);
        var changeId = town.Propose(target);
        town.Yes(changeId, "a", "b", "c");

        Assert.Equal(vacant, Assert.Single(town.Government.Offices));
        Assert.Equal(officeHistory, town.Government.OfficeHistory);
        var completed = town.Government.Changes.Single(change => change.Id == changeId);
        Assert.Equal(("completed", town.Tick), (completed.Status, completed.SettledTick));
        Assert.Equal(target, town.Government.Arrangement);
        Assert.Null(town.Government.Contest);
    }

    [Fact]
    public void VacantOfficeWaitsQuietlyForPersonalConsentThenElectsARealSuccessor()
    {
        var town = new Town();
        town.Elect(LandMayor);
        (town.Council, town.Government) = TownGovernmentRules.Resign(
            town.Council, town.Government, "a", "land", town.Tick);
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(
            town.Council, town.Government, "a", "land", town.Tick);
        var before = town.Government;
        var noticesBefore = town.Council.Notices.ToArray();
        Assert.Empty(before.Consents);
        Assert.Null(Assert.Single(before.Offices).HolderId);

        // Advancing days must not announce or archive elections nobody agreed to enter.
        for (var day = 2; day <= 11; day++)
        {
            town.Advance(day * Day);
            Assert.Null(town.Government.Contest);
        }
        var afterQuietDays = town.Government;
        var noticesAfterQuietDays = town.Council.Notices.ToArray();

        // Personal consent later still starts a fresh term backed by an actual ballot.
        town.Register("b");
        town.Advance(111);
        var contest = Assert.IsType<TownMayoralContest>(town.Government.Contest);
        Assert.Equal(("vacancy", "land", "voting"), (contest.Purpose, contest.Mandates, contest.Stage));
        Assert.Equal("b", Assert.Single(contest.Candidates));
        town.Ballot("b", "b");
        town.Advance(121);
        var successor = Assert.Single(town.Government.Offices);
        Assert.Equal("b", successor.HolderId);
        Assert.Equal(121, successor.TermStartTick);
        Assert.Equal(321, successor.TermEndTick);
        Assert.Equal("completed", town.Government.ContestHistory[^1].Stage);

        Assert.Equal(before.Sequence, afterQuietDays.Sequence);
        Assert.Equal(before.ContestHistory, afterQuietDays.ContestHistory);
        Assert.Equal(noticesBefore, noticesAfterQuietDays);
        Assert.Equal(before.MayoralRetryTick, afterQuietDays.MayoralRetryTick);
        Assert.Equal(before.MayoralRetryCircumstances, afterQuietDays.MayoralRetryCircumstances);
    }

    [Fact]
    public void NewOrdinaryMandateUsesItsOwnConsentAndReplayWithoutFillingRetainedVacantLand()
    {
        var town = new Town();
        town.Elect(LandMayor);
        (town.Council, town.Government) = TownGovernmentRules.Resign(
            town.Council, town.Government, "a", "land", town.Tick);
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(
            town.Council, town.Government, "a", "land", town.Tick);
        var vacantLand = Assert.Single(town.Government.Offices);
        var history = town.Government.OfficeHistory.ToArray();
        town.Register("b", "land+ordinary");
        var changeId = town.Propose(BothMandates);
        town.Yes(changeId, "a", "b", "c");
        Assert.Equal("handover", town.Government.Changes.Single(change => change.Id == changeId).Status);
        Assert.Null(town.Government.Contest); // Consent to a different bundle is not consent to ordinary alone.
        Assert.Equal(LandMayor, town.Government.Arrangement);
        Assert.Equal(vacantLand, Assert.Single(town.Government.Offices));

        town.Register("c", "ordinary");
        town.Advance(11);
        var contest = Assert.IsType<TownMayoralContest>(town.Government.Contest);
        Assert.Equal(("handover", "ordinary", changeId), (contest.Purpose, contest.Mandates, contest.ChangeId));
        Assert.Equal("c", Assert.Single(contest.Candidates));
        Assert.Null(TownGovernmentRules.GoverningOffice(town.Government));
        var governmentJson = JsonSerializer.Serialize(town.Government);
        var councilJson = JsonSerializer.Serialize(town.Council);
        var replay = new Town
        {
            Government = JsonSerializer.Deserialize<TownGovernmentState>(governmentJson)!,
            Council = JsonSerializer.Deserialize<TownGovernanceState>(councilJson)!,
        };
        replay.Advance(town.Tick); // Both saved components pass their native validators on continuation.
        Assert.Equal(governmentJson, JsonSerializer.Serialize(replay.Government));
        Assert.Equal(councilJson, JsonSerializer.Serialize(replay.Council));
        town.Ballot("d", "c");
        replay.Ballot("d", "c");
        town.Advance(20);
        replay.Advance(20);
        Assert.Null(TownGovernmentRules.GoverningOffice(town.Government));
        town.Advance(21);
        replay.Advance(21);

        Assert.Equal(BothMandates, town.Government.Arrangement);
        Assert.Equal("completed", town.Government.Changes.Single(change => change.Id == changeId).Status);
        Assert.Equal(("c", 21L, 221L), (TownGovernmentRules.GoverningOffice(town.Government)!.HolderId,
            TownGovernmentRules.GoverningOffice(town.Government)!.TermStartTick,
            TownGovernmentRules.GoverningOffice(town.Government)!.TermEndTick));
        Assert.Equal(("leader", "c"), (town.Council.Form, Assert.Single(town.Council.Members)));
        Assert.Equal(vacantLand, town.Government.Offices.Single(office => office.Mandates == "land"));
        Assert.Equal(history, town.Government.OfficeHistory);
        Assert.Equal(JsonSerializer.Serialize(town.Government), JsonSerializer.Serialize(replay.Government));
        Assert.Equal(JsonSerializer.Serialize(town.Council), JsonSerializer.Serialize(replay.Council));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitReplacementStillNeedsEveryTargetMandateOrLapsesWithoutChangingAuthority(bool successor)
    {
        var town = new Town();
        town.Elect(BothMandates, "land+ordinary");
        (town.Council, town.Government) = TownGovernmentRules.Resign(
            town.Council, town.Government, "a", "land", town.Tick);
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(
            town.Council, town.Government, "a", "land+ordinary", town.Tick);
        var offices = town.Government.Offices.ToArray();
        var history = town.Government.OfficeHistory.ToArray();
        town.Register("b", "ordinary");
        var changeId = town.Propose(BothMandates, replace: true);
        town.Yes(changeId, "a", "b", "c");
        var approved = town.Government.Changes.Single(change => change.Id == changeId);
        Assert.Equal(("replace_mayor", "handover", 40L), (approved.Kind, approved.Status, approved.HandoverDeadlineTick));
        Assert.Null(town.Government.Contest);
        Assert.Equal(offices, town.Government.Offices);

        if (successor)
        {
            town.Register("c", "land+ordinary");
            town.Advance(11);
            var contest = Assert.IsType<TownMayoralContest>(town.Government.Contest);
            Assert.Equal("land+ordinary", contest.Mandates);
            Assert.Equal("c", Assert.Single(contest.Candidates));
            town.Ballot("d", "c");
            town.Advance(21);
            Assert.Equal("completed", town.Government.Changes.Single(change => change.Id == changeId).Status);
            Assert.Equal(2, town.Government.Offices.Count);
            Assert.All(town.Government.Offices, office => Assert.Equal(("c", 21L, 221L),
                (office.HolderId, office.TermStartTick, office.TermEndTick)));
            Assert.Contains(town.Government.OfficeHistory, term => term.HolderId == "a" && term.Mandates == "ordinary" && term.EndTick == 21);
        }
        else
        {
            town.Advance(39);
            Assert.Equal("handover", town.Government.Changes.Single(change => change.Id == changeId).Status);
            town.Advance(40);
            var cancelled = town.Government.Changes.Single(change => change.Id == changeId);
            Assert.Equal(("cancelled", 40L), (cancelled.Status, cancelled.SettledTick));
            Assert.Equal(offices, town.Government.Offices);
            Assert.Equal(history, town.Government.OfficeHistory);
            Assert.Equal(BothMandates, town.Government.Arrangement);
            Assert.Equal(("leader", "a"), (town.Council.Form, Assert.Single(town.Council.Members)));
            Assert.Null(town.Government.Contest);
        }
    }

    [Fact]
    public void SavedRetainedMandateContestIsCancelledWhenItsHandoverCompletesAndCannotReuseItsBallot()
    {
        var town = new Town();
        town.Elect(LandMayor);
        (town.Council, town.Government) = TownGovernmentRules.Resign(
            town.Council, town.Government, "a", "land", town.Tick);
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(
            town.Council, town.Government, "a", "land", town.Tick);
        town.Register("b", "land");
        var target = new TownArrangement(TownArrangementRules.AllAdultCouncil, TownArrangementRules.Mayor);
        var changeId = town.Propose(target);
        foreach (var voter in new[] { "a", "b", "c" })
            town.Government = TownGovernmentRules.Vote(town.Government, changeId, voter, true, town.Tick);

        // Recorded native main b5909c33 output after approving this proposal at tick 10.
        // It is a valid current-format save, before the retained-mandate handover fix.
        const string contestId = "town:test:mayor:4";
        town.Government = town.Government with
        {
            Sequence = 4,
            Changes = town.Government.Changes.Select(change => change.Id == changeId
                ? change with { Status = "handover", ApprovedTick = 10, HandoverDeadlineTick = 40 } : change).ToArray(),
            Contest = new(contestId, "handover", "land", changeId, "voting", 1, 10, 10, 20,
                town.Adults, ["b"], [], [], 0),
        };
        town.Council = TownGovernanceRules.PostNotice(town.Council, "government", changeId,
            "Residents approved the government proposal. Incumbent authority continues until a valid handover, due by tick 40.", 10);
        town.Council = TownGovernanceRules.PostNotice(town.Council, "mayor", contestId,
            "An election is due for land disputes and permission expiries. Adult residents must personally agree to stand for these mandates; Council candidacy is not mayoral consent.", 10);
        town.Council = TownGovernanceRules.PostNotice(town.Council, "mayor", contestId + ":1:10",
            "Mayoral round 1: choose one of b for land disputes and permission expiries. Ballots may change until tick 20. Self-voting is allowed; a winner needs an actual vote in this round.", 10);
        town.Ballot("b", "b");
        town.Validate();
        var savedOffice = Assert.Single(town.Government.Offices);
        var savedBallot = Assert.Single(town.Government.Contest!.Ballots);
        var replay = town.Reload();

        town.Advance(11);
        replay.Advance(11);
        Assert.Equal("completed", town.Government.Changes.Single(change => change.Id == changeId).Status);
        Assert.Equal(target, town.Government.Arrangement);
        Assert.Equal(savedOffice, Assert.Single(town.Government.Offices));
        Assert.Null(town.Government.Contest);
        var cancelled = Assert.Single(town.Government.ContestHistory, contest => contest.Id == contestId);
        Assert.Equal(("cancelled", changeId, 11L), (cancelled.Stage, cancelled.ChangeId, cancelled.SettledTick));
        var round = Assert.Single(cancelled.Rounds);
        Assert.Equal(("cancelled", 10L, 11L), (round.Result, round.OpenedTick, round.ClosedTick));
        Assert.Equal(savedBallot, Assert.Single(round.Ballots));
        Assert.Null(cancelled.WinnerId);
        Assert.Equal(JsonSerializer.Serialize(town.Government), JsonSerializer.Serialize(replay.Government));
        Assert.Equal(JsonSerializer.Serialize(town.Council), JsonSerializer.Serialize(replay.Council));
        var continued = town.Reload();

        // The ordinary vacancy route remains available, with a fresh ballot requirement.
        continued.Advance(12);
        var fresh = Assert.IsType<TownMayoralContest>(continued.Government.Contest);
        Assert.Equal(("vacancy", "land", "voting"), (fresh.Purpose, fresh.Mandates, fresh.Stage));
        Assert.NotEqual(contestId, fresh.Id);
        Assert.Null(fresh.ChangeId);
        Assert.Empty(fresh.Ballots);
        continued.Advance(22);
        Assert.Equal(savedOffice, Assert.Single(continued.Government.Offices));
        Assert.Equal("failed", continued.Government.ContestHistory.Single(contest => contest.Id == fresh.Id).Stage);
        Assert.Equal("cancelled", continued.Government.ContestHistory.Single(contest => contest.Id == contestId).Stage);
        continued.Reload();
    }

    [Fact]
    public void FourResidentsCanCreateALandMayorWithoutChangingCouncilOrPropertyAuthority()
    {
        var town = new Town();
        town.Register("a");
        var id = town.Propose(LandMayor, "d"); // any resident, with no incumbent permission
        town.Yes(id, "a", "b");
        Assert.Equal("voting", town.Government.Changes[0].Status);
        Assert.Empty(town.Government.Offices);
        town.Yes(id, "c");
        Assert.Equal("handover", town.Government.Changes[0].Status);
        Assert.Equal(TownArrangementRules.Initial, town.Government.Arrangement);
        town.Ballot("a", "a");
        town.Advance(Day);
        var office = Assert.Single(town.Government.Offices);
        Assert.Equal("a", office.HolderId);
        Assert.Equal("land", office.Mandates);
        Assert.Equal(Day * 21, office.TermEndTick);
        Assert.Equal("all_adult", town.Council.Form);
        Assert.Equal(town.Adults, town.Council.Members);
        Assert.Equal("completed", town.Government.Changes[0].Status);
    }

    [Fact]
    public void OpeningResidentRosterIsFixedAndFinalVotesExcludeDeparturesButNotTravel()
    {
        var town = new Town();
        var id = town.Propose(LandMayor);
        town.Government = TownGovernmentRules.Vote(town.Government, id, "a", true, 0);
        Assert.Throws<InvalidOperationException>(() => TownGovernmentRules.Vote(town.Government, id, "a", false, 0));
        town.Adults = ["a", "b", "c", "d", "new-adult"];
        town.Advance(1);
        Assert.Throws<InvalidOperationException>(() => TownGovernmentRules.Vote(town.Government, id, "new-adult", true, 1));
        Assert.Equal(4, town.Government.Changes[0].Voters.Count);
        town.Adults = ["b", "c", "d", "new-adult"];
        town.Advance(2);
        Assert.Empty(town.Government.Changes[0].Votes);
        town.Yes(id, "b", "c");
        Assert.Equal("handover", town.Government.Changes[0].Status);
        Assert.Equal(["a", "b", "c", "d"], town.Government.Changes[0].OpeningVoters);
    }

    [Fact]
    public void EquivalentRequestsShareClockAndDifferentRequestsOpenFreshAfterHandover()
    {
        var town = new Town();
        town.Register("a");
        var first = town.Propose(LandMayor);
        town.Advance(2);
        town.Propose(LandMayor, "b");
        Assert.Single(town.Government.Changes);
        Assert.Equal(Day, town.Government.Changes[0].DeadlineTick);
        var second = town.Propose(new(TownArrangementRules.AllAdultCouncil, TownArrangementRules.NoOffice), "d");
        Assert.Equal("queued", town.Government.Changes[1].Status);
        town.Yes(first, "a", "b", "c");
        town.Ballot("a", "a");
        town.Adults = ["a", "b", "c", "d", "e"];
        town.Advance(12);
        var next = town.Government.Changes.Single(c => c.Id == second);
        Assert.Equal("voting", next.Status);
        Assert.Equal(12, next.OpenedTick);
        Assert.Equal(22, next.DeadlineTick);
        Assert.Equal(town.Adults, next.Voters);
        Assert.Empty(next.Votes);
    }

    [Fact]
    public void SilenceAndWithdrawalsRequireACooldownUnlessResidentsChange()
    {
        var town = new Town();
        town.Propose(LandMayor);
        town.Advance(Day);
        Assert.Equal("rejected", town.Government.Changes[0].Status);
        Assert.Throws<InvalidOperationException>(() => town.Propose(LandMayor));
        town.Adults = ["a", "b", "c", "d", "e"];
        var retried = town.Propose(LandMayor);
        (town.Council, town.Government) = TownGovernmentRules.Withdraw(town.Council, town.Government, retried, "a", Day);
        Assert.Throws<InvalidOperationException>(() => town.Propose(LandMayor));
        town.Advance(Day * 2);
        town.Propose(LandMayor);
        Assert.Equal(3, town.Government.Changes.Count);
    }

    [Fact]
    public void CouncilRosterChangesCancelOrdinaryProposalsButCannotCancelResidentVoting()
    {
        var town = new Town();
        town.Propose(LandMayor);
        town.Council = TownGovernanceRules.SubmitProposal(town.Council, "town:test", "a", "law", null, "Share work.", "same", town.Adults, 0, Day);
        town.Adults = ["a", "b", "c", "d", "e"];
        town.Advance(1);
        Assert.Equal("cancelled", town.Council.Proposals[0].Status);
        Assert.Equal("voting", town.Government.Changes[0].Status);
    }

    [Fact]
    public void NoWillingSuccessorCancelsTransitionAndItsContestsAtThreeDays()
    {
        var town = new Town();
        town.Yes(town.Propose(LandMayor), "a", "b", "c");
        town.Advance(Day * 3);
        Assert.Equal("cancelled", town.Government.Changes[0].Status);
        Assert.Null(town.Government.Contest);
        Assert.Empty(town.Government.Offices);
        Assert.Equal(TownArrangementRules.Initial, town.Government.Arrangement);
        town.Register("a");
        town.Advance(Day * 4);
        Assert.Null(town.Government.Contest);
    }

    [Fact]
    public void MayoralTiesRepeatWithFreshVotersAndNeedAnActualVoteInEveryDecidingRound()
    {
        var town = new Town();
        town.Register("a"); town.Register("b");
        town.Yes(town.Propose(LandMayor), "a", "b", "c");
        var oldToken = TownGovernmentRules.RoundToken(town.Government.Contest!);
        town.Ballot("a", "a"); town.Ballot("b", "b");
        town.Adults = ["a", "b", "c", "d", "e"];
        town.Advance(Day);
        Assert.Equal(2, town.Government.Contest!.Round);
        Assert.Contains("e", town.Government.Contest.Voters);
        Assert.Equal(["a", "b"], town.Government.Contest.TiedCandidates);
        Assert.Throws<InvalidOperationException>(() => TownGovernmentRules.VoteMayor(town.Government, oldToken, "a", "a", Day));
        town.Ballot("a", "a"); town.Ballot("b", "b");
        town.Advance(Day * 2);
        Assert.Equal(3, town.Government.Contest!.Round);
        Assert.Empty(town.Government.Offices);
        town.Ballot("e", "b");
        town.Advance(Day * 3);
        Assert.Equal("b", Assert.Single(town.Government.Offices).HolderId);
        Assert.Equal(2, town.Government.ContestHistory[^1].Rounds.Count(r => r.Result == "tie"));
    }

    [Fact]
    public void BallotsAreRevisableAndCouncilConsentDoesNotSupplyMayoralConsent()
    {
        var town = new Town();
        town.Council = TownGovernanceRules.Register(town.Council, "a", true, null, town.Adults, 0);
        town.Yes(town.Propose(LandMayor), "a", "b", "c");
        Assert.Null(town.Government.Contest);
        town.Register("a"); town.Register("b");
        town.Advance(1); // willingness is a material change, so retry is immediate
        town.Ballot("c", "a"); town.Ballot("c", "b");
        Assert.Equal("b", Assert.Single(town.Government.Contest!.Ballots).CandidateId);
        town.Advance(11);
        Assert.Equal("b", Assert.Single(town.Government.Offices).HolderId);
    }

    [Fact]
    public void EndingTheLandMandateKeepsTheSeparateGoverningLeaderAndItsTerm()
    {
        var town = new Town();
        town.Elect(BothMandates, "land+ordinary");
        var ordinary = town.Government.Offices.Single(o => o.Mandates == "ordinary");
        (town.Council, town.Government) = TownGovernmentRules.Resign(town.Council, town.Government, "a", "land", town.Tick);
        town.Advance(town.Tick);
        Assert.Equal(ordinary, TownGovernmentRules.GoverningOffice(town.Government));
        Assert.Equal("leader", town.Council.Form);
        Assert.Equal(["a"], town.Council.Members);
        Assert.Null(town.Government.Offices.Single(o => o.Mandates == "land").HolderId);
        town.Register("b", "land"); town.Advance(town.Tick);
        town.Ballot("b", "b"); town.Advance(town.Tick + Day);
        Assert.Equal("b", town.Government.Offices.Single(o => o.Mandates == "land").HolderId);
        Assert.Equal(ordinary, TownGovernmentRules.GoverningOffice(town.Government));
    }

    [Fact]
    public void GoverningVacancyRestoresAdultsAndLawfulSuccessionResumesTheApprovedForm()
    {
        var town = new Town();
        town.Elect(BothMandates, "land+ordinary");
        town.Adults = ["b", "c", "d"];
        town.Register("b", "land+ordinary");
        town.Advance(11);
        Assert.Equal("all_adult", town.Council.Form);
        Assert.Equal(town.Adults, town.Council.Members);
        Assert.Equal(BothMandates, town.Government.Arrangement);
        town.Ballot("b", "b"); town.Advance(21);
        Assert.Equal("leader", town.Council.Form);
        Assert.Equal(["b"], town.Council.Members);
        Assert.Single(town.Government.Changes);
        Assert.All(town.Government.Offices, o => Assert.Equal(221, o.TermEndTick));
    }

    [Fact]
    public void RenewalOpensOneDayBeforeExpiryAndSeatsAFreshTwentyDayTerm()
    {
        var town = new Town();
        town.Elect(LandMayor);
        town.Advance(199);
        Assert.Null(town.Government.Contest);
        town.Advance(200);
        Assert.Equal("renewal", town.Government.Contest!.Purpose);
        town.Ballot("a", "a");
        town.Advance(210);
        Assert.Equal(410, Assert.Single(town.Government.Offices).TermEndTick);
        Assert.Single(town.Government.OfficeHistory);
    }

    [Fact]
    public void ScheduledCouncilElectionInterruptsMayorAndResumesWithFreshBallots()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        foreach (var id in town.Adults.Take(3)) town.Council = TownGovernanceRules.Register(town.Council, id, true, null, town.Adults, 0);
        town.Advance(0);
        town.Council = TownGovernanceRules.VoteElection(town.Council, town.Council.Election!.Id, "a", ["a", "b", "c"], 0);
        town.Advance(10);
        Assert.Equal(110, town.Council.TermEndTick);
        town.Advance(95); town.Register("d");
        town.Yes(town.Propose(LandMayor), "a", "b", "c", "d", "e");
        town.Ballot("d", "d");
        town.Advance(100);
        Assert.Equal("waiting", town.Government.Contest!.Stage);
        Assert.Empty(town.Government.Contest.Ballots);
        town.Council = TownGovernanceRules.VoteElection(town.Council, town.Council.Election!.Id, "a", ["a", "b", "c"], 100);
        town.Advance(110);
        Assert.Equal("voting", town.Government.Contest!.Stage);
        Assert.Equal(1, town.Government.Contest.Interruptions);
        Assert.Empty(town.Government.Contest.Ballots);
        town.Ballot("d", "d"); town.Advance(120);
        Assert.Equal("d", Assert.Single(town.Government.Offices).HolderId);
    }

    [Fact]
    public void AddingALandMayorPreservesAnExistingSevenResidentRepresentativeCouncil()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        foreach (var id in town.Adults.Take(3)) town.Council = TownGovernanceRules.Register(town.Council, id, true, null, town.Adults, 0);
        town.Advance(0);
        town.Council = TownGovernanceRules.VoteElection(town.Council, town.Council.Election!.Id, "a", ["a", "b", "c"], 0);
        town.Advance(10);
        var end = town.Council.TermEndTick;
        town.Adults = ["a", "b", "c", "d", "e", "f", "g"];
        town.Advance(11);
        town.Register("d");
        town.Yes(town.Propose(LandMayor), "a", "b", "c", "d");
        town.Ballot("d", "d"); town.Advance(21);
        Assert.Equal("d", Assert.Single(town.Government.Offices).HolderId);
        Assert.Equal("representative", town.Council.Form);
        Assert.Equal(["a", "b", "c"], town.Council.Members);
        Assert.Equal(end, town.Council.TermEndTick);
    }

    [Fact]
    public void AReadyMayorDoesNotHoldBackTheCouncilElectionItsOwnHandoverNeeds()
    {
        var town = new Town("a", "b", "c", "d", "e");
        var target = new TownArrangement(TownArrangementRules.ElectedCouncil, TownArrangementRules.Mayor);
        town.Register("a");
        var id = town.Propose(target);
        town.Yes(id, "a", "b", "c");
        town.Ballot("a", "a");
        town.Advance(1);
        foreach (var actor in new[] { "a", "b", "c" })
            town.Council = TownGovernanceRules.Register(town.Council, actor, true, null, town.Adults, 1);
        for (var tick = 2L; tick < Day * 3 && town.Government.Changes[0].Status == "handover"; tick++)
        {
            if (town.Council.Election is { Stage: "main" } election && election.Ballots.Count == 0)
                town.Council = TownGovernanceRules.VoteElection(town.Council, election.Id, "a", ["a", "b", "c"], tick);
            town.Advance(tick);
        }

        Assert.Equal("completed", town.Government.Changes[0].Status);
        Assert.Equal(target, town.Government.Arrangement);
        Assert.Equal("representative", town.Council.Form);
        Assert.Equal("a", Assert.Single(town.Government.Offices).HolderId);
    }

    [Fact]
    public void ACancelledChangeLeavesNoElectedCouncilBehind()
    {
        var town = new Town("a", "b", "c", "d", "e");
        var elected = new TownArrangement(TownArrangementRules.ElectedCouncil, TownArrangementRules.NoOffice);
        town.Yes(town.Propose(elected), "a", "b", "c");
        // Nobody agrees to stand, so the forced first election fails and the change lapses.
        for (var tick = 1L; tick <= Day * 3; tick++) town.Advance(tick);
        Assert.Equal("cancelled", town.Government.Changes[0].Status);
        Assert.Equal(TownArrangementRules.Initial, town.Government.Arrangement);

        // Willing candidates later must not be seated by a council election nobody approved.
        foreach (var actor in new[] { "a", "b", "c" })
            town.Council = TownGovernanceRules.Register(town.Council, actor, true, null, town.Adults, town.Tick);
        for (var tick = town.Tick + 1; tick <= Day * 8; tick++)
        {
            if (town.Council.Election is { Stage: "main" } election && election.Ballots.Count == 0)
                town.Council = TownGovernanceRules.VoteElection(town.Council, election.Id, "a", ["a", "b", "c"], tick);
            town.Advance(tick);
        }
        Assert.Null(town.Council.Election);
        Assert.Equal("all_adult", town.Council.Form);
        Assert.Equal(town.Adults, town.Council.Members);
    }

    [Fact]
    public void AFailedSuccessorElectionKeepsTheLeadersCouncilAndPendingBusiness()
    {
        var town = new Town("a", "b", "c", "d", "e");
        town.Elect(BothMandates, "land+ordinary");
        Assert.Equal(("leader", "a"), (town.Council.Form, Assert.Single(town.Council.Members)));
        town.Council = TownGovernanceRules.SubmitProposal(town.Council, town.Id, "a", "law", null, "Keep the well covered.",
            $"council:{town.Council.Revision}", town.Adults, town.Tick, Day);
        var revision = town.Council.Revision;
        var elected = new TownArrangement(TownArrangementRules.ElectedCouncil, TownArrangementRules.NoOffice);
        town.Yes(town.Propose(elected), "a", "b", "c");
        // Nobody agrees to stand, so the successor council election fails.
        var approved = town.Tick;
        for (var tick = approved + 1; tick < approved + Day * 2; tick++) town.Advance(tick);

        Assert.Contains(town.Council.ElectionHistory, election => election.Stage == "failed");
        Assert.Equal(("leader", revision), (town.Council.Form, town.Council.Revision));
        // Its own deadline settles the law proposal; a council change would have cancelled it.
        Assert.NotEqual("cancelled", town.Council.Proposals.Single(proposal => proposal.Kind == "law").Status);
    }

    [Fact]
    public void ExplicitAllAdultAndElectedArrangementsUseProtectedHandover()
    {
        var town = new Town();
        var allAdult = new TownArrangement(TownArrangementRules.AllAdultCouncil, TownArrangementRules.NoOffice);
        town.Yes(town.Propose(allAdult), "a", "b", "c");
        town.Adults = ["a", "b", "c", "d", "e", "f", "g", "h"];
        foreach (var id in town.Adults.Take(3)) town.Council = TownGovernanceRules.Register(town.Council, id, true, null, town.Adults, 0);
        town.Advance(1);
        Assert.Null(town.Council.Election);
        var elected = new TownArrangement(TownArrangementRules.ElectedCouncil, TownArrangementRules.NoOffice);
        town.Yes(town.Propose(elected), "a", "b", "c", "d", "e");
        Assert.Equal("all_adult", town.Council.Form);
        town.Council = TownGovernanceRules.VoteElection(town.Council, town.Council.Election!.Id, "a", ["a", "b", "c"], 1);
        town.Advance(11);
        Assert.Equal("representative", town.Council.Form);
        Assert.Equal(elected, town.Government.Arrangement);
        town.Advance(12);
        Assert.Null(town.Council.Election);
    }

    [Fact]
    public void ASecondTownCannotConsumeTheFirstTownsResidentVotesOrElection()
    {
        var first = new Town();
        var second = new Town { Id = "town:other" };
        var firstVote = first.Propose(LandMayor);
        var secondVote = second.Propose(LandMayor);
        Assert.NotEqual(firstVote, secondVote);
        Assert.Throws<InvalidOperationException>(() => TownGovernmentRules.Vote(second.Government, firstVote, "a", true, 0));
        first.Register("a"); first.Yes(firstVote, "a", "b", "c");
        first.Ballot("a", "a"); first.Advance(Day);
        Assert.Single(first.Government.Offices);
        Assert.Empty(second.Government.Offices);
        Assert.Empty(second.Government.Changes[0].Votes);
    }

    [Fact]
    public void AnUnvotedRunoffCannotUseSupportFromTheEarlierTie()
    {
        var town = new Town();
        town.Register("a"); town.Register("b");
        town.Yes(town.Propose(LandMayor), "a", "b", "c");
        town.Ballot("a", "a"); town.Ballot("b", "b"); town.Advance(Day);
        town.Advance(Day * 2);
        Assert.Empty(town.Government.Offices);
        Assert.Equal("failed", town.Government.ContestHistory[^1].Stage);
        Assert.Null(town.Government.ContestHistory[^1].WinnerId);
    }

    [Fact]
    public void EndingAMandateCancelsItsPendingRenewalAndCannotRecreateTheOffice()
    {
        var town = new Town();
        town.Elect(LandMayor);
        town.Advance(200);
        town.Ballot("a", "a");
        town.Yes(town.Propose(new(TownArrangementRules.AllAdultCouncil, TownArrangementRules.NoOffice)), "a", "b", "c");
        Assert.Empty(town.Government.Offices);
        Assert.Null(town.Government.Contest);
        Assert.Equal("cancelled", town.Government.ContestHistory[^1].Stage);
        town.Advance(210);
        Assert.Empty(town.Government.Offices);
    }

    [Fact]
    public void ReloadedTieContinuesWithTheSameResultAndSeparateTownsDoNotShareVotes()
    {
        var town = new Town();
        town.Register("a"); town.Register("b");
        town.Yes(town.Propose(LandMayor), "a", "b", "c");
        town.Ballot("a", "a"); town.Ballot("b", "b"); town.Advance(Day);
        var replay = new Town
        {
            Government = JsonSerializer.Deserialize<TownGovernmentState>(JsonSerializer.Serialize(town.Government))!,
            Council = JsonSerializer.Deserialize<TownGovernanceState>(JsonSerializer.Serialize(town.Council))!
        };
        replay.Advance(Day);
        town.Ballot("c", "a"); replay.Ballot("c", "a");
        town.Advance(20); replay.Advance(20);
        Assert.Equal(JsonSerializer.Serialize(town.Government), JsonSerializer.Serialize(replay.Government));
        Assert.Empty(new Town().Government.Changes);
    }
}
