using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentRulesTests
{
    [Fact]
    public void ReportsJoinTheSameActAndLawWithoutRestartingNoticeOrErasingDisagreement()
    {
        var state = Pending();
        var original = Assert.Single(state.Cases);
        state = TownNonviolentRules.File(state, "town", original.Allegation with { Statement = "Another account of this same act." },
            new("other", "witness", 12, "I disagree about what happened.", ["source:other"]), Parties, 12, 10, "unused-notice");
        var joined = Assert.Single(state.Cases);
        Assert.Equal(original.Id, joined.Id);
        Assert.Equal(original.Revisions, joined.Revisions);
        Assert.Equal(20, TownNonviolentRules.CurrentRevision(joined).DeadlineTick);
        Assert.Equal(2, joined.Filings.Count);
        Assert.Equal("I disagree about what happened.", joined.Filings[1].Statement);
        Assert.Empty(joined.Findings);
        var replay = RoundTrip(state);
        var later = original.Allegation with { IncidentId = "act:later", ConductTick = 13 };
        var filing = new TownViolationFiling("witness", "witness", 14, "A later distinct act.", ["source:later"]);
        state = TownNonviolentRules.File(state, "town", later, filing, Parties, 14, 10, "notice:later");
        replay = TownNonviolentRules.File(replay, "town", later, filing, Parties, 14, 10, "notice:later");
        Assert.Equal(2, state.Cases.Count);
        Assert.Equal(24, state.Cases[1].Revisions[0].DeadlineTick);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(replay));
    }

    [Fact]
    public void PublicationDoesNotAuthorizeAnAnswerAndChangedCaregiverGetsANewFullWindow()
    {
        var parties = new[] { new TownCaseParty("subject", "subject", "child", "old-carer", "care:old", 2, "home") };
        var state = Pending(parties, "child");
        var item = Assert.Single(state.Cases);
        Assert.Throws<InvalidOperationException>(() => TownNonviolentRules.Respond(state, item.Id, 1, "old-carer", "child", "subject",
            "answer", "My account.", 11, [], parties));
        state = TownNonviolentRules.Respond(state, item.Id, 1, "old-carer", "child", "subject", "answer", "My account.", 11,
            [new("old-carer", "notice:case", 11)], parties);
        Assert.True(TownNonviolentRules.CanCloseResponses(state.Cases[0], parties, 11));
        var replacement = new[] { parties[0] with { RespondingAdultId = "new-carer", CareRelationshipId = "care:new", CareRevision = 1 } };
        state = TownNonviolentRules.ReviseParties(state, item.Id, replacement, 12, 10, "notice:replacement");
        Assert.Equal(22, state.Cases[0].Revisions[^1].DeadlineTick);
        Assert.False(TownNonviolentRules.CanCloseResponses(state.Cases[0], replacement, 21));
        Assert.Throws<InvalidOperationException>(() => TownNonviolentRules.Respond(state, item.Id, 1, "old-carer", "child", "subject",
            "waive", "Old delayed reply.", 13, [new("old-carer", "notice:case", 11)], replacement));
        Assert.Single(state.Cases[0].Responses);
    }

    [Fact]
    public void ARelayedAccusationAndLawTextAloneCannotSupportAnAdverseFinding()
    {
        var state = Pending();
        var id = state.Cases[0].Id;
        state = Add(state, Evidence("rumor", "allegation", "relay", "original-witness", "act:one", "act-version", "Someone told me about it."));
        state = Add(state, Evidence("law", "record", "record_inspection", "witness", "law:one:1", "law-version", "The rule says to take care."));
        state = Ready(state);
        Assert.Throws<InvalidOperationException>(() => Rule(state, "supported", "warning", ["rumor", "law"]));
        Assert.Empty(state.Cases[0].Findings);
        var dismissed = Rule(state, "unsupported", "none", ["rumor", "law"]);
        var finding = Assert.Single(dismissed.Cases[0].Findings);
        Assert.Equal("unsupported", finding.Result);
        Assert.Equal("none", finding.Consequence);
        Assert.Equal(state.Cases[0].Allegation, dismissed.Cases[0].Allegation);
        Assert.Throws<InvalidOperationException>(() => TownNonviolentRules.File(dismissed, "town", state.Cases[0].Allegation,
            new("witness", "witness", 21, "Repeat the same report.", ["source:one"]), Parties, 21, 10, "notice:duplicate"));
        Assert.Equal(id, dismissed.Cases[0].Id);
    }

    [Fact]
    public void ANewPartyAnswerMustBeReadAndFirstNoticeAllowsWarningButNotCensure()
    {
        var state = Ready(Add(Pending(), ConductEvidence()));
        state = TownNonviolentRules.Respond(state, state.Cases[0].Id, 1, "subject", "subject", "subject", "answer",
            "There is another explanation.", 12, [new("subject", "notice:case", 11)], Parties);
        Assert.Equal(state.Cases[0].Reads[^1].ReadTick, state.Cases[0].Responses[^1].Tick);
        Assert.False(TownNonviolentRules.ReadCurrent(state.Cases[0], "judge", 20));
        Assert.Throws<InvalidOperationException>(() => Rule(state, "supported", "warning", ["conduct"]));
        state = TownNonviolentRules.Inspect(state, state.Cases[0].Id, 1, "judge", 20);
        Assert.Throws<InvalidOperationException>(() => Rule(state, "supported", "censure", ["conduct"]));
        var warned = Rule(state, "supported", "warning", ["conduct"]);
        Assert.Equal("warning", Assert.Single(warned.Cases[0].Findings).Consequence);
        Assert.Empty(warned.Agreements);
        Assert.Empty(warned.Effects);
        var censured = Rule(state, "supported", "censure", ["conduct"], priorNotice: true);
        Assert.Equal("censure", Assert.Single(censured.Cases[0].Findings).Consequence);
    }

    [Fact]
    public void AnUnrepresentedChildCanBeHeardButCannotReceiveAnAdverseFinding()
    {
        var child = new[] { new TownCaseParty("subject", "subject", "child", null, null, null, "home") };
        var state = Ready(Add(Pending(child, "child"), ConductEvidence()));
        Assert.Throws<InvalidOperationException>(() => Rule(state, "supported", "warning", ["conduct"], parties: child));
        var dismissed = Rule(state, "unsupported", "none", ["conduct"], parties: child);
        Assert.Equal("unsupported", Assert.Single(dismissed.Cases[0].Findings).Result);
    }

    [Fact]
    public void ReopeningNeedsNewGroundsAndStartsANewNoticeWithoutErasingTheOldFinding()
    {
        var state = Settled();
        var id = state.Cases[0].Id;
        var original = state.Cases[0].Findings[0];
        state = Add(state, Evidence("reworded", "observation", "firsthand", "witness", "act:one", "act-version", "SAW the act!", 22));
        state = TownNonviolentRules.RequestReopen(state, id, "subject", "material_evidence", ["reworded"], "The same point again.", 23);
        Assert.False(TownNonviolentRules.MaterialNewEvidence(state.Cases[0], state.Cases[0].ReopenRequests[^1]));
        state = Add(state, Evidence("new-fact", "observation", "firsthand", "other", "act:one", "act-version", "I saw the damaged tool before the act.", 24));
        state = TownNonviolentRules.RequestReopen(state, id, "subject", "material_evidence", ["new-fact"], "A distinct firsthand fact affects the assessment.", 25);
        var request = state.Cases[0].ReopenRequests[^1];
        Assert.True(TownNonviolentRules.MaterialNewEvidence(state.Cases[0], request));
        var judge = Judge with { AssignedTick = 25 };
        state = TownNonviolentRules.AssignJudge(state, id, judge);
        state = TownNonviolentRules.Inspect(state, id, 1, "judge", 26);
        state = TownNonviolentRules.Reopen(state, id, request.Id, judge, true, "This is material new evidence.", Parties, 27, 10, "notice:reopen", true);
        var reopened = state.Cases[0];
        Assert.Equal("pending", reopened.Status);
        Assert.Null(reopened.SettledTick);
        Assert.Equal(original, Assert.Single(reopened.Findings));
        Assert.Equal((2, 37L), (reopened.Revisions[^1].Number, reopened.Revisions[^1].DeadlineTick));
        Assert.False(TownNonviolentRules.CanCloseResponses(reopened, Parties, 36));
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(RoundTrip(state)));
    }

    internal static readonly TownCaseParty[] Parties = [new("subject", "subject", "subject", "subject", null, null, "subject-home")];
    internal static readonly TownCaseJudge Judge = new("judge", "non_land_mayor", "authority:judge", 11);
    internal static TownNonviolentState Pending(IReadOnlyList<TownCaseParty>? parties = null, string subject = "subject") =>
        TownNonviolentRules.File(TownNonviolentState.Create(), "town", new("act:one", subject, "travel", new GridPoint(1, 1), 9,
            "law:one", 1, "A specific reported act.", ["source:one"]),
            new("witness", "witness", 10, "Here is my account.", ["source:one"]), parties ?? Parties, 10, 10, "notice:case");
    internal static TownCaseEvidence Evidence(string id, string kind, string acquisition, string source, string record, string version,
        string text, long submitted = 11) => new(id, 1, kind, acquisition, source, record, version, 9,
            acquisition == "relay" ? "witness" : source, submitted, text);
    internal static TownCaseEvidence ConductEvidence() => Evidence("conduct", "observation", "firsthand", "witness", "act:one", "act-version", "Saw the act.");
    internal static TownNonviolentState Add(TownNonviolentState state, TownCaseEvidence evidence) => TownNonviolentRules.AddEvidence(state,
        state.Cases[0].Id, 1, evidence, [new(evidence.SubmittedByAgentId, "notice:case", 10)]);
    internal static TownNonviolentState Ready(TownNonviolentState state)
    {
        state = TownNonviolentRules.AssignJudge(state, state.Cases[0].Id, Judge);
        return TownNonviolentRules.Inspect(state, state.Cases[0].Id, 1, "judge", 12);
    }
    internal static TownNonviolentState Rule(TownNonviolentState state, string result, string consequence, IReadOnlyList<string> evidence,
        bool priorNotice = false, IReadOnlyList<TownCaseParty>? parties = null) => TownNonviolentRules.Rule(state, state.Cases[0].Id, 1,
            Judge, 20, result, consequence, evidence, "Assessment of the cited account.", "This is a civil assessment, with uncertainty.", parties ?? Parties, true, priorNotice);
    internal static TownNonviolentState Settled()
    {
        var state = Rule(Ready(Add(Pending(), ConductEvidence())), "supported", "warning", ["conduct"]);
        state = TownNonviolentRules.Inspect(state, state.Cases[0].Id, 1, "judge", 21);
        return TownNonviolentRules.Inspect(state, state.Cases[0].Id, 1, "subject", 21);
    }
    internal static TownNonviolentState RoundTrip(TownNonviolentState state) => JsonSerializer.Deserialize<TownNonviolentState>(JsonSerializer.Serialize(state))!;
}
