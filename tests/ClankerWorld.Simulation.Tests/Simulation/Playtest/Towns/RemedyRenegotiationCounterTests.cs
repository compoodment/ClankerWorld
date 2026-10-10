using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class RemedyRenegotiationCounterTests
{
    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 1)]
    public async Task AcceptedCounterReplacesOnlyItsOriginalUnfinishedAgreement(bool renegotiate, bool counter, int counterRounds)
    {
        const string subject = NonviolentRuntimeFixture.Subject;
        const string witness = NonviolentRuntimeFixture.Witness;
        var state = renegotiate ? await NonviolentRuntimeFixture.AcceptedRemedyAsync() : await NonviolentRuntimeFixture.FindingAsync();
        state = ShelterOrderTestFixture.WithClearWeather(state);
        var original = renegotiate ? Assert.Single(state.Towns![0].Nonviolent.Agreements) : null;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "counter-witness-wood", "wood", witness, 2);
        var board = state.Towns![0].OriginSite!.Value;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId is subject or witness ? board : person.Position,
                HungerBasisPoints = 9_000,
                Survival = person.Survival is { } survival ? survival with { WarmthBasisPoints = 9_000, IllnessBasisPoints = 0 } : null,
                LastDecisionContext = null,
            }).ToArray(),
        };
        if (!renegotiate) state = await NonviolentRuntimeFixture.OfferRemedyAsync(NonviolentRuntimeFixture.Strict(state), Terms(1));
        var phase = renegotiate ? "renegotiate" : "counter";
        var counterActor = witness;
        var provider = Choices();
        using (var negotiating = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(state), provider))
        {
            if (renegotiate)
            {
                var count = negotiating.Towns[0].Nonviolent.Offers.Count;
                NonviolentRuntimeFixture.Wake(negotiating, subject, "request-remaining-remedy");
                await NonviolentRuntimeFixture.UntilAsync(negotiating, () => negotiating.Towns[0].Nonviolent.Offers.Count > count, 8);
                state = NonviolentRuntimeFixture.Strict(negotiating.ExportState());
            }
            for (var round = 0; round < counterRounds; round++)
            {
                phase = "counter";
                counterActor = round % 2 == 0 ? witness : subject;
                var count = negotiating.Towns[0].Nonviolent.Offers.Count;
                NonviolentRuntimeFixture.Wake(negotiating, counterActor, "counter-remaining-remedy-" + round);
                await NonviolentRuntimeFixture.UntilAsync(negotiating, () => negotiating.Towns[0].Nonviolent.Offers.Count > count, 8);
                state = NonviolentRuntimeFixture.Strict(negotiating.ExportState());
            }
            phase = "accept";
            NonviolentRuntimeFixture.Wake(negotiating, subject, "consent-subject-counter");
            NonviolentRuntimeFixture.Wake(negotiating, witness, "consent-witness-counter");
            await NonviolentRuntimeFixture.UntilAsync(negotiating, () => negotiating.Towns[0].Nonviolent.Agreements.Count == (renegotiate ? 2 : 1), 8);
            state = NonviolentRuntimeFixture.Strict(negotiating.ExportState());
            Assert.Empty(state.Towns![0].Nonviolent.Effects);
        }
        var replacement = state.Towns![0].Nonviolent.Agreements[^1];
        var totalWood = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        Assert.Equal(1, Assert.Single(replacement.Terms, term => term.ContributorId == subject).Quantity);
        Assert.Equal(counter ? 2 : 1, Assert.Single(replacement.Terms, term => term.ContributorId == witness).Quantity);
        Assert.All(new[] { subject, witness }, actor => Assert.Contains(replacement.Consents,
            consent => consent.AgentId == actor && consent.Kind == "accept" && consent.TermsHash == replacement.TermsHash));
        if (original is not null)
            foreach (var invalid in new string?[] { null, "nonviolent-agreement:unrelated" })
            {
                var corrupt = state with
                {
                    Towns = state.Towns.Select(town => town.Id == state.Towns[0].Id ? town with
                    {
                        Nonviolent = town.Nonviolent with
                        {
                            Agreements = town.Nonviolent.Agreements.Select(agreement => agreement.Id == replacement.Id
                                ? agreement with { ReplacesAgreementId = invalid } : agreement).ToArray(),
                        },
                    } : town).ToArray(),
                };
                Assert.Throws<InvalidDataException>(() => NonviolentRuntimeFixture.Strict(corrupt));
            }
        phase = "perform";
        using var world = NonviolentRuntimeFixture.Create(state, Choices());
        NonviolentRuntimeFixture.Wake(world, subject, "perform-counter-subject");
        NonviolentRuntimeFixture.Wake(world, witness, "perform-counter-witness");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = NonviolentRuntimeFixture.Create(PrivateWorldRuntimeCodec.Decode(bytes), Choices());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 6; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var ledger = world.Towns[0].Nonviolent;
        Assert.Equal(1, ledger.Effects.Where(effect => effect.ActorId == subject).Sum(effect => effect.Quantity));
        Assert.Equal(counter ? 2 : 1, ledger.Effects.Where(effect => effect.ActorId == witness).Sum(effect => effect.Quantity));
        Assert.All(ledger.Effects, effect =>
        {
            Assert.Equal(replacement.Id, effect.AgreementId);
            var receipt = Assert.Single(ledger.NativeReceipts, item => item.Id == effect.NativeReceiptId);
            Assert.Equal((effect.ActorId, effect.BeneficiaryId, effect.Quantity),
                (receipt.PreviousOwnerId, receipt.ResultOwnerId, receipt.Quantity));
            Assert.Equal(effect.BeneficiaryId, world.Society.Inventory.GetLot(receipt.ResultLotId).OwnerId);
        });
        Assert.Equal("completed", ledger.Agreements.Single(agreement => agreement.Id == replacement.Id).Status);
        Assert.Equal(original?.Id, ledger.Agreements.Single(agreement => agreement.Id == replacement.Id).ReplacesAgreementId);
        if (original is not null)
        {
            Assert.True(TownRemedyRules.IsSuperseded(ledger, original.Id));
            Assert.DoesNotContain(ledger.Effects, effect => effect.AgreementId == original.Id);
            Assert.Equal("superseded", ledger.Offers.Single(offer => offer.Id == original.OfferId).Status);
        }
        Assert.All(ledger.Offers.Where(offer => offer.AgreementId is null && offer.Responses.Any(response => response.Kind == "counter")),
            offer => Assert.Equal("countered", offer.Status));
        Assert.Equal(totalWood, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        NonviolentRuntimeFixture.Strict(world.ExportState());

        CognitionRemedyTerm[] Terms(int witnessQuantity) =>
        [new("return_goods", subject, witness, "wood", 1, null), new("return_goods", witness, subject, "wood", witnessQuantity, null)];

        NonviolentTestProvider Choices() => new()
        {
            Choose = observation =>
            {
                if (observation.InhabitantId is not (subject or witness)) return null;
                if (phase == "perform") return observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal));
                var informed = observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_inspect|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_read|", StringComparison.Ordinal));
                if (informed is not null) return informed;
                var action = phase == "accept" ? "remedy_accept" : phase == "counter" && observation.InhabitantId == counterActor ? "remedy_counter" :
                    phase == "renegotiate" && observation.InhabitantId == subject ? "remedy_renegotiate" : null;
                return action is null ? null : observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            },
            Payload = (_, candidate) => candidate.Id.Contains("|remedy_renegotiate|", StringComparison.Ordinal) ||
                candidate.Id.Contains("|remedy_counter|", StringComparison.Ordinal)
                ? new(Statement: "Replace the remaining commitments with these voluntary named returns.",
                    Terms: Terms(phase == "counter" ? 2 : 1), CompletionTicks: 60) : null,
        };
    }
}
