using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandHearingRulesTests
{
    private static readonly GridPoint[] Plot = [new(1, 0), new(2, 0)];
    private static readonly string[] Adults = ["a", "b", "judge", "other"];
    private static readonly IReadOnlyDictionary<string, string?> Households = new Dictionary<string, string?>
    { ["a"] = "alpha", ["b"] = "beta", ["judge"] = "neutral", ["other"] = "other-household" };
    private static readonly TownLandCaseParty[] Parties =
        [new("alpha", "household", "alpha", "town", ["a"]), new("beta", "household", "beta", "town", ["b"])];

    [Fact]
    public void AHouseholdFilingJoinsAnExpiryReviewWithoutGivingConsentOrRestartingItsClock()
    {
        var rights = new[] { Right() };
        var state = TownLandHearingRules.File(TownLandHearingState.Create(), "town",
            new(null, "expiry", "Review the agreed end", new("confirm"), 10, rights[0].Id), Plot, rights, Parties, 10, 20, "notice:1");
        var original = Assert.Single(state.Cases);
        state = TownLandHearingRules.File(state, "town", new("b", "dispute", "I request this plot", new("amend", "beta"), 15),
            Plot, rights, Parties, 15, 20, "notice:unused");

        var joined = Assert.Single(state.Cases);
        Assert.Equal(original.Id, joined.Id);
        Assert.Equal("expiry", joined.Kind);
        Assert.Equal(10, Assert.Single(joined.Revisions).PublishedTick);
        Assert.Equal(30, joined.Revisions[0].DeadlineTick);
        Assert.Equal(2, joined.Filings.Count);
        Assert.Empty(joined.Responses);
        Assert.Empty(state.Adjustments);
        Assert.Equal(("alpha", (long?)10), (rights[0].HouseholdId, rights[0].AgreedEndTick));
    }

    [Fact]
    public void ANoticeIsNotReceiptAndOneAdultMustAnswerHouseholdAndTownPartiesSeparately()
    {
        var parties = Parties.Append(new TownLandCaseParty("town-party", "town", null, "town", [], "a")).ToArray();
        var state = File(parties: parties);
        var item = state.Cases[0];
        Assert.False(TownLandHearingRules.CanCloseResponses(item, parties, 5));
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Respond(state, item.Id, 1, "a", "answer", "I answer", 5, [], parties, "alpha"));
        TownCivicReceipt[] receipts = [new("a", "notice:1", 2), new("b", "notice:1", 3)];
        state = TownLandHearingRules.Respond(state, item.Id, 1, "a", "answer", "My household answers", 5, receipts, parties, "alpha");
        state = TownLandHearingRules.Respond(state, item.Id, 1, "b", "waive", "I waive my response", 5, receipts, parties, "beta");
        Assert.False(TownLandHearingRules.CanCloseResponses(state.Cases[0], parties, 5));
        state = TownLandHearingRules.Respond(state, item.Id, 1, "a", "answer", "The Town answers", 6, receipts, parties, "town-party");
        Assert.True(TownLandHearingRules.CanCloseResponses(state.Cases[0], parties, 6));
        Assert.Equal(3, state.Cases[0].Responses.Count);
        Assert.True(TownLandHearingRules.CanCloseResponses(item, parties, 10));
    }

    [Fact]
    public void DepartingAdultsPreserveTheNoticeButANewAdultOrChangedRightGetsAFullFreshDay()
    {
        var initialParties = new[] { Parties[0] with { AdultIds = ["a", "departing"] }, Parties[1] };
        var state = File(parties: initialParties);
        var item = state.Cases[0];
        var unchanged = TownLandHearingRules.Revise(state, item.Id, [Right()], Parties, Plot, 6, 10, "notice:unused");
        Assert.Same(state, unchanged);
        var added = new[] { Parties[0] with { AdultIds = ["a", "new-adult"] }, Parties[1] };
        state = TownLandHearingRules.Revise(state, item.Id, [Right()], added, Plot, 6, 10, "notice:2");
        Assert.Equal((2, 6L, 16L), (state.Cases[0].Revisions[^1].Number, state.Cases[0].Revisions[^1].PublishedTick, state.Cases[0].Revisions[^1].DeadlineTick));
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Respond(state, item.Id, 1, "a", "answer", "Stale answer", 7,
            [new("a", "notice:1", 2)], added, "alpha"));
        var changed = TownLandHearingRules.Revise(state, item.Id, [Right() with { AgreedEndTick = 40 }], added, Plot, 8, 10, "notice:3");
        Assert.Equal(18, changed.Cases[0].Revisions[^1].DeadlineTick);
    }

    [Fact]
    public void CaseOnlyElectionPreservesTiesAcrossInterruptionsAndSurvivesOrdinaryMayorSuccession()
    {
        var state = File();
        var id = state.Cases[0].Id;
        var council = TownGovernanceState.Create(Adults);
        var government = Government();
        state = TownLandCaseJudgeRules.Register(state, id, "judge", Adults, Households, 0);
        state = TownLandCaseJudgeRules.Register(state, id, "other", Adults, Households, 0);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 0, 10);
        var round = state.Cases[0].Contest!;
        // Recused parties keep their ordinary resident ballots.
        state = TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(round), "a", "judge", 1);
        state = TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(round), "b", "other", 1);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 10, 10);
        Assert.Collection(state.Cases[0].Contest!.TiedCandidates,
            id => Assert.Equal("judge", id), id => Assert.Equal("other", id));
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 12, 10, ordinaryContestBusy: true);
        Assert.Equal("waiting", state.Cases[0].Contest!.Stage);
        Assert.Empty(state.Cases[0].Contest!.Ballots);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 13, 10);
        var resumed = state.Cases[0].Contest!;
        Assert.Equal(23, resumed.RoundDeadlineTick);
        Assert.Throws<InvalidOperationException>(() => TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(round), "a", "judge", 14));
        state = TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(resumed), "a", "judge", 14);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, government, Adults, Households, 23, 10);
        var judge = state.Cases[0].Judge!;
        Assert.Equal(("judge", "case_elected"), (judge.AgentId, judge.Kind));
        Assert.Collection(state.Cases[0].ContestHistory[0].Rounds,
            roundResult => Assert.Equal("tie", roundResult.Result),
            roundResult => Assert.Equal("interrupted", roundResult.Result),
            roundResult => Assert.Equal("winner", roundResult.Result));
        var successor = government with { Offices = [new("land", "b", 24, 124, null, null, "successor-election")] };
        (state, _) = TownLandCaseJudgeRules.Advance(state, council, successor, Adults, Households, 24, 10);
        Assert.Equal(judge, state.Cases[0].Judge);
        Assert.Equal(0, state.Cases[0].Revisions[0].PublishedTick);
        Assert.Equal(government.Offices, Government().Offices);
        state = TownLandCaseJudgeRules.Withdraw(state, id, "judge", 25);
        Assert.Null(state.Cases[0].Judge);
        Assert.Equal(25, state.Cases[0].JudgeConsents.Single(c => c.AgentId == "judge").WithdrawnTick);
        Assert.Equal(10, state.Cases[0].Revisions[0].DeadlineTick);
    }

    [Fact]
    public void TwoPartyHouseholdsWithoutAnIndependentCandidateStayPending()
    {
        var state = File();
        var council = TownGovernanceState.Create(["a", "b"]);
        (state, _) = TownLandCaseJudgeRules.Advance(state, council, Government(), ["a", "b"], Households, 0, 10);
        Assert.Equal("pending", state.Cases[0].Status);
        Assert.Null(state.Cases[0].Judge);
        Assert.Equal("failed", Assert.Single(state.Cases[0].ContestHistory).Stage);
        Assert.Empty(state.Adjustments);
    }

    [Fact]
    public void APartialRulingAndLaterAuthorizedBuildingTransferReplayWithoutErasingOriginalGrantTerms()
    {
        var fixture = Ready();
        var original = fixture.Rights[0];
        var (state, rights) = TownLandHearingRules.Rule(fixture.State, Map(), fixture.Id, 1, fixture.State.Cases[0].Judge!, 11,
            new("renew", "alpha", 50), ["record:right"], [], "Renew only the noticed plot", fixture.Rights, Parties, true);
        Assert.Equal(original, Assert.Single(state.OriginalRights).Right);
        Assert.Equal(50, Assert.Single(rights, r => r.Tiles.Contains(new GridPoint(1, 0))).AgreedEndTick);
        Assert.Equal(10, Assert.Single(rights, r => r.Tiles.Contains(new GridPoint(0, 0))).AgreedEndTick);
        Assert.Equal("alpha", Assert.Single(rights, r => r.Tiles.Contains(new GridPoint(3, 0))).HouseholdId);
        var moved = TownLandRightsRules.ReassignFootprintRights(Map(), rights, new HashSet<GridPoint> { new(1, 0) }, "beta", 12);
        state = TownLandHearingRules.RecordBuildingTransfer(state, Map(), "house", [new(1, 0)], "beta", rights, moved, 12);
        var baseline = TownLandHearingRules.OriginalGrantRights(state, moved);
        Assert.Equal(new[] { original }, baseline);
        Assert.Equal(JsonSerializer.Serialize(moved), JsonSerializer.Serialize(TownLandHearingRules.ApplyAdjustments(state, baseline)));
        Validate(state, moved, fixture.Council, 12);
        var damaged = state with
        {
            Adjustments = state.Adjustments.Select(a => a.Kind == "ruling" ?
            a with { PriorRights = a.PriorRights.Select(r => r with { Version = new string('0', 64) }).ToArray() } : a).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => Validate(damaged, moved, fixture.Council, 12));
        var fabricated = state with
        {
            Adjustments = state.Adjustments.Select(a => a.Kind == "building_transfer" ?
            a with { ResultRights = a.ResultRights.Select(r => r with { AgreedEndTick = 90 }).ToArray() } : a).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => Validate(fabricated, moved, fixture.Council, 12));
    }

    [Fact]
    public void UnsupportedAllegationsStaleRightsAndUnrepresentedHouseholdsCannotLosePermissions()
    {
        var fixture = Ready();
        var noAdult = Parties.Select(p => p.Id == "alpha" ? p with { AdultIds = [] } : p).ToArray();
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Rule(fixture.State, Map(), fixture.Id, 1, fixture.State.Cases[0].Judge!, 11,
            new("end", "alpha"), ["record:right"], [], "End this permission", fixture.Rights, noAdult, true));
        var changed = fixture.Rights.Select(r => r with { HouseholdId = "beta" }).ToArray();
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Rule(fixture.State, Map(), fixture.Id, 1, fixture.State.Cases[0].Judge!, 11,
            new("end", "beta"), ["record:right"], [], "Stale plot", changed, Parties, true));
        var allegation = new TownLandEvidence("claim", 1, "allegation", "statement", "b", null, null, 1, "b", 2, "I claim the other party is wrong");
        var claimed = TownLandHearingRules.AddEvidence(fixture.State, fixture.Id, 1, allegation, fixture.Council.Knowledge);
        claimed = TownLandHearingRules.Inspect(claimed, fixture.Id, 1, "judge", 11);
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Rule(claimed, Map(), fixture.Id, 1, claimed.Cases[0].Judge!, 11,
            new("amend", "beta"), ["claim"], [], "The allegation alone supports transfer", fixture.Rights, Parties, true));
        Assert.Empty(fixture.State.Adjustments);
        Assert.Equal(JsonSerializer.Serialize(new[] { Right() }), JsonSerializer.Serialize(fixture.Rights));
    }

    [Fact]
    public void FileRelayCannotTeachEvidenceThatTheSourceHasNotActuallyRead()
    {
        var fixture = Ready();
        var later = new TownLandEvidence("later", 1, "allegation", "statement", "b", null, null, 11, "b", 11, "A later statement");
        var state = TownLandHearingRules.AddEvidence(fixture.State, fixture.Id, 1, later, fixture.Council.Knowledge);
        state = TownLandHearingRules.RelayRead(state, fixture.Id, 1, "judge", "other", 11);
        var receipt = state.Cases[0].Reads[^1];
        Assert.Equal("judge", receipt.SourceAgentId);
        Assert.Equal("record:right", Assert.Single(receipt.EvidenceIds));
        Assert.DoesNotContain("later", receipt.EvidenceIds);
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.RelayRead(state, fixture.Id, 1, "unread", "a", 11));
        Validate(state, fixture.Rights, fixture.Council, 11);
    }

    [Fact]
    public void ASettledPlotResolvesOnlyItsRequestIntersectionWithoutInventingGrantConsent()
    {
        var fixture = Ready();
        var (state, rights) = TownLandHearingRules.Rule(fixture.State, Map(), fixture.Id, 1, fixture.State.Cases[0].Judge!, 11,
            new("confirm"), ["record:right"], [], "Keep the current permission", fixture.Rights, Parties, true);
        var ruling = state.Cases[0].Rulings[0];
        var partial = new HouseholdLandUseRequest("partial", "town", "beta", "b", [new(1, 0), new(2, 0), new(3, 0)], 0);
        var full = new HouseholdLandUseRequest("full", "town", "beta", "b", Plot, 0);
        var receipts = TownLandHearingRules.RequestResolutions(state, fixture.Id, ruling.Id, [partial, full]);
        partial = partial with { HearingResolutions = [receipts.Single(r => r.RequestId == partial.Id)] };
        full = full with { HearingResolutions = [receipts.Single(r => r.RequestId == full.Id)], Status = "hearing_resolved", SettledTick = 11 };
        TownLandHearingValidation.ValidateRequestResolutions(11, [state], [partial, full]);
        Assert.Equal(Plot, partial.HearingResolutions[0].Tiles);
        Assert.DoesNotContain(new GridPoint(3, 0), partial.HearingResolutions[0].Tiles);
        Assert.Equal("alpha", Assert.Single(TownLandRightsRules.ClaimantsAt(new(1, 0), rights, [partial, full])));
        Assert.Collection(TownLandRightsRules.ClaimantsAt(new(3, 0), rights, [partial, full]),
            id => Assert.Equal("alpha", id), id => Assert.Equal("beta", id));
        Assert.Empty(full.GrantAdults);
        Assert.Empty(full.Consents);
        Assert.Throws<InvalidDataException>(() => TownLandHearingValidation.ValidateRequestResolutions(11, [state],
            [partial with { Status = "hearing_resolved", SettledTick = 11 }]));
        Assert.Throws<InvalidDataException>(() => TownLandHearingValidation.ValidateRequestResolutions(11, [state],
            [partial with { HearingResolutions = [partial.HearingResolutions[0] with { Tiles = [new(3, 0)] }] }]));
    }

    [Fact]
    public void ASecondCaseCannotStartConcurrentVotingAndAnIndependentMayorClosesTheOldContest()
    {
        var state = File();
        var firstId = state.Cases[0].Id;
        state = TownLandHearingRules.File(state, "town", new("b", "dispute", "A separate plot", new("amend", "beta"), 1),
            [new(5, 0)], [Right()], Parties, 1, 10, "notice:second");
        var secondId = state.Cases[1].Id;
        state = TownLandCaseJudgeRules.Register(state, firstId, "judge", Adults, Households, 1);
        state = TownLandCaseJudgeRules.Register(state, secondId, "judge", Adults, Households, 1);
        var council = TownGovernanceState.Create(Adults);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, Government(), Adults, Households, 1, 10);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, Government(), Adults, Households, 2, 10);
        Assert.Single(state.Cases, c => c.Contest is { Stage: "voting" });
        var independent = Government() with { Offices = [new("land", "other", 3, 103, null, null, "independent-election")] };
        (state, _) = TownLandCaseJudgeRules.Advance(state, council, independent, Adults, Households, 3, 10);
        Assert.False(TownLandCaseJudgeRules.IsBusy(state));
        Assert.All(state.Cases, c => Assert.Equal("other", c.Judge!.AgentId));
        Assert.Equal("cancelled", state.Cases[0].ContestHistory[^1].Stage);
        Assert.Equal("cancelled", state.Cases[0].ContestHistory[^1].Rounds[^1].Result);
        Assert.Equal(0, state.Cases[0].Revisions[0].PublishedTick);
    }

    [Fact]
    public void ASettledCaseNeedsGroundedReopeningAndKeepsItsRulingAndCurrentPermissionDuringTheFreshWindow()
    {
        var fixture = Ready();
        var (state, rights) = TownLandHearingRules.Rule(fixture.State, Map(), fixture.Id, 1, fixture.State.Cases[0].Judge!, 11,
            new("confirm"), ["record:right"], [], "Preserve the existing permission", fixture.Rights, Parties, true);
        var oldRuling = state.Cases[0].Rulings[0];
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.File(state, "town",
            new("b", "dispute", "I disagree again", new("amend", "beta"), 12), Plot, rights, Parties, 12, 10, "unused"));
        var newEvidence = new TownLandEvidence("new-observation", 1, "observation", "firsthand", "a", null, null, 12, "a", 12, "I actually observed new plot conditions");
        state = TownLandHearingRules.AddEvidence(state, fixture.Id, 1, newEvidence, fixture.Council.Knowledge);
        state = TownLandHearingRules.RequestReopen(state, fixture.Id, "a", "material_evidence", [newEvidence.Id], "New plot evidence", 13);
        var request = state.Cases[0].ReopenRequests[0];
        Assert.True(TownLandHearingRules.MaterialNewEvidence(state.Cases[0], request, state));
        state = TownLandHearingRules.AssignJudge(state, fixture.Id, oldRuling.Judge with { AssignedTick = 14 });
        state = TownLandHearingRules.Inspect(state, fixture.Id, 1, "judge", 14);
        state = TownLandHearingRules.Reopen(state, fixture.Id, request.Id, state.Cases[0].Judge!, true, "The new observation warrants a hearing", rights, Parties,
            14, 10, "notice:reopen", true);
        Assert.Equal(oldRuling, state.Cases[0].Rulings[0]);
        Assert.Equal("pending", state.Cases[0].Status);
        Assert.Equal((14L, 24L), (state.Cases[0].Revisions[^1].PublishedTick, state.Cases[0].Revisions[^1].DeadlineTick));
        Assert.Empty(state.Adjustments);
        Assert.Equal(fixture.Rights, rights);
    }

    [Fact]
    public void RereadingRecordsAndRepeatingFirsthandFactsCannotEstablishReopeningGrounds()
    {
        var fixture = Ready();
        var priorObservation = new TownLandEvidence("old-observation", 1, "observation", "firsthand", "a", null, null,
            2, "a", 2, "Two marked boundary posts stand on this plot.");
        var state = TownLandHearingRules.AddEvidence(fixture.State, fixture.Id, 1, priorObservation, fixture.Council.Knowledge);
        state = TownLandHearingRules.Inspect(state, fixture.Id, 1, "judge", 10);
        var rulingResult = TownLandHearingRules.Rule(state, Map(), fixture.Id, 1, state.Cases[0].Judge!, 11,
            new("confirm"), ["record:right", priorObservation.Id], [], "Preserve the recorded permission", fixture.Rights, Parties, true);
        state = rulingResult.State;
        var right = fixture.Rights[0];
        TownLandEvidence[] repeated =
        [
            new("reread-right", 1, "record", "record_inspection", "b", right.Id, TownLandHearingRules.Version(right),
                12, "b", 12, "A different inspector describes this unchanged permission differently"),
            new("reread-title", 1, "record", "record_inspection", "b", Title().Id, TownLandHearingRules.RecordVersion(Title()),
                12, "b", 12, "The recorded permission and end date"),
            priorObservation with { Id = "later-same-witness", ObservedTick = 12, SubmittedTick = 12 },
            priorObservation with { Id = "later-other-witness", SourceAgentId = "b", SubmittedByAgentId = "b", ObservedTick = 12,
                SubmittedTick = 12, Text = "  TWO marked boundary posts stand on this plot.  " },
            new("new-facts-claim", 1, "allegation", "statement", "b", null, null, 12, "b", 12, "I claim that new facts require a correction")
        ];
        foreach (var evidence in repeated)
        {
            state = TownLandHearingRules.AddEvidence(state, fixture.Id, 1, evidence, fixture.Council.Knowledge);
            state = TownLandHearingRules.RequestReopen(state, fixture.Id, "b", "material_evidence", [evidence.Id], "Consider my claimed new grounds", 13);
            Assert.False(TownLandHearingRules.MaterialNewEvidence(state.Cases[0], state.Cases[0].ReopenRequests[^1], state));
        }
        var request = state.Cases[0].ReopenRequests[0];
        state = TownLandHearingRules.AssignJudge(state, fixture.Id, state.Cases[0].Rulings[0].Judge with { AssignedTick = 14 });
        state = TownLandHearingRules.Inspect(state, fixture.Id, 1, "judge", 14);
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Reopen(state, fixture.Id, request.Id, state.Cases[0].Judge!, true,
            "A reread alone warrants reopening", rulingResult.Rights, Parties, 14, 10, "notice:unused", true));
        Assert.Equal("settled", state.Cases[0].Status);
        Assert.Single(state.Cases[0].Revisions);
        Assert.Equal(fixture.Rights, rulingResult.Rights);
        Validate(state, rulingResult.Rights, fixture.Council, 14);
    }

    [Fact]
    public void TheCourtsOwnRulingAndResultingPermissionCannotBecomeNewMaterialFacts()
    {
        var fixture = Ready();
        var (state, rights) = TownLandHearingRules.Rule(fixture.State, Map(), fixture.Id, 1, fixture.State.Cases[0].Judge!, 11,
            new("renew", "alpha", 50), ["record:right"], [], "Renew the permission for this plot", fixture.Rights, Parties, true);
        var ruling = Assert.Single(state.Cases[0].Rulings);
        var permission = Assert.Single(rights, right => right.Tiles.Contains(new GridPoint(1, 0)));
        TownLandEvidence[] courtEffects =
        [
            new("ruling-receipt", 1, "record", "record_inspection", "b", ruling.Id, TownLandHearingRules.RecordVersion(ruling),
                12, "b", 12, "The court renewed the noticed permission"),
            new("result-permission", 1, "record", "record_inspection", "b", permission.Id, TownLandHearingRules.Version(permission),
                12, "b", 12, "The resulting permission now has an agreed end at fifty")
        ];
        foreach (var evidence in courtEffects)
        {
            state = TownLandHearingRules.AddEvidence(state, fixture.Id, 1, evidence, fixture.Council.Knowledge);
            state = TownLandHearingRules.RequestReopen(state, fixture.Id, "b", "material_evidence", [evidence.Id], "I disagree with the court result", 13);
            var request = state.Cases[0].ReopenRequests[^1];
            Assert.False(TownLandHearingRules.MaterialNewEvidence(state.Cases[0], request, state));
            Assert.False(TownLandHearingRules.MaterialNewEvidence(state.Cases[0], request));
        }
        Assert.Equal(50, permission.AgreedEndTick);
        Assert.Equal("settled", state.Cases[0].Status);
        Validate(state, rights, fixture.Council, 13);
    }

    [Fact]
    public void ANewlyRevealedRecordOrChangedFirsthandFactCanEstablishMaterialGrounds()
    {
        var fixture = Ready();
        var priorObservation = new TownLandEvidence("old-observation", 1, "observation", "firsthand", "a", null, null,
            2, "a", 2, "Two marked boundary posts stand on this plot.");
        var state = TownLandHearingRules.AddEvidence(fixture.State, fixture.Id, 1, priorObservation, fixture.Council.Knowledge);
        state = TownLandHearingRules.Inspect(state, fixture.Id, 1, "judge", 10);
        var rulingResult = TownLandHearingRules.Rule(state, Map(), fixture.Id, 1, state.Cases[0].Judge!, 11,
            new("confirm"), ["record:right", priorObservation.Id], [], "Preserve the recorded permission", fixture.Rights, Parties, true);
        state = rulingResult.State;
        var title = Title();
        TownLandEvidence[] newFacts =
        [
            new("new-title", 1, "record", "record_inspection", "b", title.Id, TownLandHearingRules.RecordVersion(title),
                12, "b", 12, "The Town formally holds title to the disputed plot"),
            priorObservation with { Id = "changed-observation", ObservedTick = 12, SubmittedTick = 12,
                Text = "One marked boundary post stands on this plot." }
        ];
        foreach (var evidence in newFacts)
        {
            state = TownLandHearingRules.AddEvidence(state, fixture.Id, 1, evidence, fixture.Council.Knowledge);
            state = TownLandHearingRules.RequestReopen(state, fixture.Id, "b", "material_evidence", [evidence.Id], "Assess the actual new factual material", 13);
            Assert.True(TownLandHearingRules.MaterialNewEvidence(state.Cases[0], state.Cases[0].ReopenRequests[^1], state));
        }
        Validate(state, rulingResult.Rights, fixture.Council, 13);
        Assert.Equal("settled", state.Cases[0].Status);
        Assert.Equal("pending", state.Cases[0].ReopenRequests[0].Status);
        Assert.Single(state.Cases[0].Revisions);
        Assert.Equal(fixture.Rights, rulingResult.Rights);
    }

    [Fact]
    public void InspectedLawWordingAndTitleEvidenceSurviveLaterLawAmendmentAndAdditionalTownTitle()
    {
        var state = File();
        var id = state.Cases[0].Id;
        var council = Council(id);
        var version = new TownLawVersion(1, "Use", "Review permission at its agreed end", "jurisdiction", [], "proposal:law", 0);
        var evidence = new TownLandEvidence("law-proof", 1, "record", "record_inspection", "a", "law@1",
            TownLandHearingRules.LawVersion(version), 1, "a", 1, "The adopted wording");
        state = TownLandHearingRules.AddEvidence(state, id, 1, evidence, council.Knowledge);
        var title = Title();
        state = TownLandHearingRules.AddEvidence(state, id, 1,
            new("title-proof", 1, "record", "record_inspection", "a", title.Id, TownLandHearingRules.RecordVersion(title), 1, "a", 1, "Recorded Town title"), council.Knowledge);
        var ended = version with { EndedTick = 5, EndedByProposalId = "proposal:amend" };
        Assert.Equal(evidence.SourceVersion, TownLandHearingRules.LawVersion(ended));
        var government = Government() with
        {
            Laws = [new("law", [ended,
            new(2, "Use", "Amended wording", "jurisdiction", [], "proposal:amend", 5)])]
        };
        TownLandHearingValidation.Validate(Map(), 6, "town", state, [Right()],
            [title, new("claim-title", "town", [new(6, 0)], 5)], Adults.ToHashSet(), Households.Values.OfType<string>().ToHashSet(), council, 10, government);
        var damaged = state with { Cases = [state.Cases[0] with { Evidence = state.Cases[0].Evidence.Select(e => e.Id == "law-proof" ? e with { SourceVersion = "invented" } : e).ToArray() }] };
        Assert.Throws<InvalidDataException>(() => TownLandHearingValidation.Validate(Map(), 6, "town", damaged, [Right()], [title],
            Adults.ToHashSet(), Households.Values.OfType<string>().ToHashSet(), council, 10, government));
    }

    private static TownLandHearingState File(IReadOnlyList<TownLandCaseParty>? parties = null) =>
        TownLandHearingRules.File(TownLandHearingState.Create(), "town", new("b", "dispute", "Consider the competing permission", new("amend", "beta"), 0),
            Plot, [Right()], parties ?? Parties, 0, 10, "notice:1");
    private static HouseholdLandUseRight Right() => new("right", "town", "alpha", [new(0, 0), new(1, 0), new(2, 0), new(3, 0)], 0, "council:original", 10);
    private static SeededMap Map() => new(8, 2, 0,
        (from y in Enumerable.Range(0, 2) from x in Enumerable.Range(0, 8) select new TerrainTile(new(x, y), TerrainKind.Meadow)).ToArray(), [], [], "hearing-fixture");
    private static TownLandTitleRecord Title() => new("title", "town", [new(0, 0), new(1, 0), new(2, 0), new(3, 0)], 0);
    private static TownGovernmentState Government() => TownGovernmentState.Create() with
    { Arrangement = new("council", "mayor"), Offices = [new("land", "a", 0, 100, null, null, "mayor-election")] };
    private static TownGovernanceState Council(string caseId)
    {
        var council = TownGovernanceRules.PostNotice(TownGovernanceState.Create(Adults), "land_hearing", caseId + ":1", "Formal plot hearing", 0);
        foreach (var actor in Adults) council = TownGovernanceRules.LearnNotice(council, actor, "notice:1", 0);
        return council;
    }
    private static (TownLandHearingState State, IReadOnlyList<HouseholdLandUseRight> Rights, TownGovernanceState Council, string Id) Ready()
    {
        var state = File();
        var id = state.Cases[0].Id;
        var council = Council(id);
        state = TownLandCaseJudgeRules.Register(state, id, "judge", Adults, Households, 0);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, Government(), Adults, Households, 0, 10);
        state = TownLandCaseJudgeRules.Vote(state, id, TownLandCaseJudgeRules.RoundToken(state.Cases[0].Contest!), "a", "judge", 1);
        (state, council) = TownLandCaseJudgeRules.Advance(state, council, Government(), Adults, Households, 10, 10);
        var right = Right();
        state = TownLandHearingRules.AddEvidence(state, id, 1,
            new("record:right", 1, "record", "record_inspection", "a", right.Id, TownLandHearingRules.Version(right), 1, "a", 1, "The recorded permission and end date"), council.Knowledge);
        state = TownLandHearingRules.Inspect(state, id, 1, "judge", 10);
        return (state, new[] { right }, council, id);
    }
    private static void Validate(TownLandHearingState state, IReadOnlyList<HouseholdLandUseRight> rights, TownGovernanceState council, long tick) =>
        TownLandHearingValidation.Validate(Map(), tick, "town", state, rights, [Title()], Adults.ToHashSet(),
            Households.Values.OfType<string>().ToHashSet(), council, 10, Government());
}
