using System.Text.Json;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownRemedyRulesTests
{
    [Fact]
    public void EveryContributorMustLearnAndPersonallyAcceptExactFeasibleTerms()
    {
        var state = Offered([Goods("subject", 2), Goods("helper", 1) with { Id = "help" }]);
        var offer = Assert.Single(state.Offers);
        Assert.Throws<InvalidOperationException>(() => Respond(state, "subject", receipts: []));
        Assert.Throws<InvalidOperationException>(() => Respond(state, "witness"));
        Assert.Throws<InvalidOperationException>(() => Respond(state, "subject", feasible: false));
        state = Respond(state, "subject");
        Assert.Empty(state.Agreements);
        Assert.Single(state.Offers[0].Responses);
        Assert.Throws<InvalidOperationException>(() => Respond(state, "subject"));
        state = Respond(state, "helper", tick: 23);
        var agreement = Assert.Single(state.Agreements);
        Assert.Equal(offer.TermsHash, agreement.TermsHash);
        Assert.Equal((23L, 43L), (agreement.AcceptedTick, agreement.DeadlineTick));
        string[] contributors = ["subject", "helper"];
        Assert.Equal(contributors, agreement.Consents.Select(consent => consent.AgentId));
        Assert.Empty(state.Effects);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("counter")]
    [InlineData("unanswered")]
    public void RefusalSilenceAndCounterofferDoNotCreateAnAgreementOrAPenalty(string response)
    {
        var state = Offered([Goods("subject", 2)]);
        var originalFinding = state.Cases[0].Findings[0];
        state = response == "unanswered" ? TownRemedyRules.Advance(state, 31) : Respond(state, "subject", response);
        Assert.Equal(response == "decline" ? "declined" : response == "counter" ? "countered" : "unanswered", state.Offers[0].Status);
        Assert.Empty(state.Agreements);
        Assert.Empty(state.Effects);
        Assert.Equal(originalFinding, Assert.Single(state.Cases[0].Findings));
        Assert.Throws<InvalidOperationException>(() => Respond(state, "subject", tick: 32));
    }

    [Fact]
    public void CounterofferInvalidatesOldConsentAndKeepsTheUnacceptedHistory()
    {
        var state = Respond(Offered([Goods("subject", 3)]), "subject", "counter");
        var old = state.Offers[0];
        state = TownRemedyRules.Offer(state, old.CaseId, old.FindingId, "subject", [Goods("subject", 1)], 20,
            "I can contribute one piece.", 23, 10, "notice:counter", true, replacesOfferId: old.Id);
        var current = state.Offers[^1];
        Assert.Equal(2, current.Revision);
        Assert.NotEqual(old.TermsHash, current.TermsHash);
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.Respond(state, old.Id, old.Revision, "subject", "accept", null,
            24, 10, [new("subject", old.NoticeId, 22)], true));
        state = TownRemedyRules.Respond(state, current.Id, current.Revision, "subject", "accept", null,
            24, 10, [new("subject", current.NoticeId, 24)], true);
        Assert.Equal("countered", state.Offers[0].Status);
        Assert.Equal(1, Assert.Single(state.Agreements).Terms[0].Quantity);
        Assert.Null(state.Agreements[0].ReplacesAgreementId);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(TownNonviolentRulesTests.RoundTrip(state)));
    }

    [Fact]
    public void ActualPartialEffectsSurviveReloadAndCountOnceWithoutInventingWork()
    {
        var state = Respond(Offered([Goods("subject", 3)]), "subject");
        var agreement = state.Agreements[0];
        var first = Effect(agreement, 1, 23, "receipt:one");
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, first with { ActorId = "helper" }));
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, first with { Tick = 21 }));
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, first with { Quantity = 4 }));
        state = TownRemedyRules.RecordEffect(state, first);
        Assert.Equal(1, TownRemedyRules.CompletedQuantity(state, agreement.Id, "goods"));
        Assert.Equal("pending", state.Agreements[0].Status);
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, first with { Id = "different-effect" }));
        var replay = TownNonviolentRulesTests.RoundTrip(state);
        state = TownRemedyRules.Advance(state, 43);
        replay = TownRemedyRules.Advance(replay, 43);
        Assert.Equal("overdue", state.Agreements[0].Status);
        var final = Effect(agreement, 2, 44, "receipt:two");
        state = TownRemedyRules.RecordEffect(state, final);
        replay = TownRemedyRules.RecordEffect(replay, final);
        Assert.Equal("completed", state.Agreements[0].Status);
        Assert.Equal(3, TownRemedyRules.CompletedQuantity(state, agreement.Id, "goods"));
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(replay));
        Assert.Single(state.Cases[0].Findings);
    }

    [Fact]
    public void RenegotiationPreservesOldActualPerformanceWithoutCreditingItTwice()
    {
        var state = Respond(Offered([Goods("subject", 3)]), "subject");
        var original = state.Agreements[0];
        state = TownRemedyRules.RecordEffect(state, Effect(original, 1, 23, "receipt:one"));
        state = TownRemedyRules.Offer(state, state.Cases[0].Id, state.Cases[0].Findings[0].Id, "subject", [Goods("subject", 1)], 10,
            "Agree a smaller remaining contribution.", 24, 10, "notice:revised", true, replacesAgreementId: original.Id);
        var revised = state.Offers[^1];
        state = TownRemedyRules.Respond(state, revised.Id, revised.Revision, "subject", "accept", null, 25, 10,
            [new("subject", revised.NoticeId, 25)], true);
        var replacement = state.Agreements[^1];
        Assert.Equal(original.Id, replacement.ReplacesAgreementId);
        Assert.True(TownRemedyRules.IsSuperseded(state, original.Id));
        Assert.Equal(1, TownRemedyRules.CompletedQuantity(state, original.Id, "goods"));
        Assert.Equal(0, TownRemedyRules.CompletedQuantity(state, replacement.Id, "goods"));
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, Effect(original, 1, 26, "receipt:later-old")));
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, Effect(replacement, 1, 26, "receipt:one")));
        state = TownRemedyRules.RecordEffect(state, Effect(replacement, 1, 26, "receipt:new"));
        Assert.Equal("completed", state.Agreements[^1].Status);
    }

    [Theory]
    [InlineData("repair_equipment", "wooden_axe", "tool:one", "home")]
    [InlineData("public_service_goods", "wood", "warehouse:one", "town")]
    public void PhysicalWorkTermsKeepExactItemTargetAndBeneficiary(string kind, string item, string target, string beneficiary)
    {
        var state = Respond(Offered([new("work", kind, "subject", beneficiary, item, 1, target)]), "subject");
        var agreement = state.Agreements[0];
        var receipt = new TownRemedyEffect("effect:work", agreement.Id, "work", "subject", 23, kind,
            beneficiary, item, 1, target, "native:work", "version");
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, receipt with { TargetId = "other" }));
        Assert.Throws<InvalidOperationException>(() => TownRemedyRules.RecordEffect(state, receipt with { ItemKind = "other" }));
        state = TownRemedyRules.RecordEffect(state, receipt);
        Assert.Equal("completed", state.Agreements[0].Status);
        Assert.Equal(receipt, Assert.Single(state.Effects));
    }

    private static TownRemedyTerm Goods(string actor, int quantity) => new("goods", "return_goods", actor, "witness", "wood", quantity, null);
    private static TownNonviolentState Offered(IReadOnlyList<TownRemedyTerm> terms)
    {
        var state = TownNonviolentRulesTests.Settled();
        return TownRemedyRules.Offer(state, state.Cases[0].Id, state.Cases[0].Findings[0].Id, "judge", terms, 20,
            "A voluntary contribution to make things right.", 21, 10, "notice:offer", true);
    }
    private static TownNonviolentState Respond(TownNonviolentState state, string actor, string kind = "accept", long tick = 22,
        IReadOnlyList<TownCivicReceipt>? receipts = null, bool feasible = true) => TownRemedyRules.Respond(state, state.Offers[0].Id,
            state.Offers[0].Revision, actor, kind, null, tick, 10, receipts ?? [new(actor, "notice:offer", 22)], feasible);
    private static TownRemedyEffect Effect(TownRestorativeAgreement agreement, int quantity, long tick, string receipt) =>
        new("effect:" + receipt, agreement.Id, "goods", "subject", tick, "return_goods", "witness", "wood", quantity, null, receipt, "version");
}
