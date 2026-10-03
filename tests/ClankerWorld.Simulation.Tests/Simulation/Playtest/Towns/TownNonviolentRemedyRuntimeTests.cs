using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentRemedyRuntimeTests
{
    [Fact]
    public async Task PersonalConsentReservesNothingAndOnlyTheActualTransferCompletesTheAgreementOnceAcrossReplay()
    {
        var state = await NonviolentRuntimeFixture.AcceptedRemedyAsync();
        var ledger = state.Towns![0].Nonviolent;
        var agreement = Assert.Single(ledger.Agreements);
        Assert.Equal(agreement.AcceptedTick + 3L * NonviolentRuntimeFixture.Day, agreement.DeadlineTick);
        Assert.Equal(NonviolentRuntimeFixture.Subject, Assert.Single(agreement.Consents).AgentId);
        Assert.Equal(agreement.TermsHash, agreement.Consents[0].TermsHash);
        Assert.Empty(ledger.Effects);
        Assert.Empty(ledger.NativeReceipts);
        Assert.DoesNotContain(state.Society.Society.Inventory.Reservations, reservation => reservation.LotId == NonviolentRuntimeFixture.ReturnLot);
        var total = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var provider = CompletionProvider();
        using var world = NonviolentRuntimeFixture.Create(state, provider);
        NonviolentRuntimeFixture.Wake(world, NonviolentRuntimeFixture.Subject, "perform-voluntary-return");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Contains(provider.Selected, selected => selected.Contains("nonviolent_remedy:", StringComparison.Ordinal));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = NonviolentRuntimeFixture.Create(PrivateWorldRuntimeCodec.Decode(before), CompletionProvider());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }

        var completed = world.Towns[0].Nonviolent;
        Assert.Equal("completed", Assert.Single(completed.Agreements).Status);
        var effect = Assert.Single(completed.Effects);
        var receipt = Assert.Single(completed.NativeReceipts);
        Assert.Equal(receipt.Id, effect.NativeReceiptId);
        Assert.Equal(receipt.Version, effect.NativeReceiptVersion);
        Assert.Equal(1, effect.Quantity);
        Assert.Equal(NonviolentRuntimeFixture.Subject, effect.ActorId);
        Assert.Equal(NonviolentRuntimeFixture.Witness, effect.BeneficiaryId);
        Assert.Equal("return_goods", receipt.Kind);
        Assert.Equal(NonviolentRuntimeFixture.Subject, receipt.PreviousOwnerId);
        Assert.Equal(NonviolentRuntimeFixture.Witness, receipt.ResultOwnerId);
        Assert.Equal(1, world.Society.Inventory.GetLot(NonviolentRuntimeFixture.ReturnLot).Quantity);
        Assert.Equal(NonviolentRuntimeFixture.Witness, world.Society.Inventory.GetLot(receipt.ResultLotId).OwnerId);
        Assert.Equal(total, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "voluntary_goods_returned");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        NonviolentRuntimeFixture.Strict(world.ExportState());

        // Duplicating a real receipt is not another completed contribution.
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(world.ExportState()))!;
        var effects = document["state"]!["towns"]![0]!["nonviolent"]!["effects"]!.AsArray();
        effects.Add(effects[0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public async Task ReservedGoodsDoNotBecomeAvailableThroughConsentAndTheSameAgreementResumesAfterNativeRelease()
    {
        var state = await NonviolentRuntimeFixture.AcceptedRemedyAsync();
        var inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "independent-wood-work",
            NonviolentRuntimeFixture.Subject, NonviolentRuntimeFixture.ReturnLot, 2, "independent-work", long.MaxValue);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var provider = CompletionProvider();
        using (var blocked = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(state), provider))
        {
            NonviolentRuntimeFixture.Wake(blocked, NonviolentRuntimeFixture.Subject, "blocked-voluntary-return");
            Assert.True((await blocked.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(provider.Observations.Where(observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject)
                .SelectMany(observation => observation.Candidates), candidate => candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal));
            Assert.Empty(blocked.Towns[0].Nonviolent.Effects);
            Assert.Equal(2, blocked.Society.Inventory.GetLot(NonviolentRuntimeFixture.ReturnLot).Quantity);
            state = NonviolentRuntimeFixture.Strict(blocked.ExportState());
        }
        inventory = InventoryFixture.ReleaseReservation(state.Society.Society.Inventory, "independent-wood-work");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var released = NonviolentRuntimeFixture.Create(state, CompletionProvider());
        NonviolentRuntimeFixture.Wake(released, NonviolentRuntimeFixture.Subject, "released-voluntary-return");
        await NonviolentRuntimeFixture.UntilAsync(released, () => released.Towns[0].Nonviolent.Effects.Count > 0, 5);
        Assert.Single(released.Towns[0].Nonviolent.Agreements);
        Assert.Equal("completed", released.Towns[0].Nonviolent.Agreements[0].Status);
        NonviolentRuntimeFixture.Strict(released.ExportState());
    }

    private static NonviolentTestProvider CompletionProvider() => new()
    {
        Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject
            ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal)) : null
    };
}
