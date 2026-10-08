using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class RestitutionSourceLotTests
{
    [Theory]
    [InlineData(false, "available")]
    [InlineData(true, "available")]
    [InlineData(true, "pinned")]
    [InlineData(true, "reserved")]
    public async Task NativeRestitutionSelectsALotThatMeetsTheUnpinnedTermsAndProtectsPinnedOrReservedSources(bool smallFirst, string boundary)
    {
        const string actor = NonviolentRuntimeFixture.Subject;
        const string recipient = NonviolentRuntimeFixture.Witness;
        var state = await NonviolentRuntimeFixture.FindingAsync();
        // Initial personal stock is controlled; agreement, split lots and receipts are native.
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == NonviolentRuntimeFixture.ReturnLot
                ? lot with { Quantity = 3 } : lot).ToArray()
        };
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        state = await NonviolentRuntimeFixture.AcceptRemedyAsync(NonviolentRuntimeFixture.Strict(state),
            [new("return_goods", actor, recipient, "wood", 2, boundary == "pinned" ? NonviolentRuntimeFixture.ReturnLot : null)]);
        var agreement = Assert.Single(state.Towns![0].Nonviolent.Agreements);
        Assert.Equal("pending", agreement.Status);
        Assert.Empty(state.Towns[0].Nonviolent.Effects);
        var perform = false;
        NonviolentTestProvider Choices() => new()
        {
            Choose = observation => observation.InhabitantId != actor ? null :
                observation.OperativeOrderInstructionId is not null
                    ? observation.Candidates.FirstOrDefault(candidate => candidate.Id is "store_material" or "collect_goods" or "move_to")
                    : perform ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal)) : null
        };
        using var preparing = NonviolentRuntimeFixture.Create(state, Choices());
        var amount = smallFirst ? "two" : "one";
        await Order("restitution-store", "store " + amount + " wood in my House");
        await Order("restitution-collect", "collect " + amount + " wood");
        var carried = preparing.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood" &&
            PersonalEquipmentRules.IsCarried(lot, actor)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, carried.Length);
        Assert.Equal(NonviolentRuntimeFixture.ReturnLot, carried[0].Id);
        Assert.Equal(smallFirst ? 1 : 2, carried[0].Quantity);
        Assert.Equal(smallFirst ? 2 : 1, carried[1].Quantity);
        var board = preparing.Towns[0].OriginSite!.Value;
        var destination = state.Map.FootNeighbors(board).Where(point =>
            !preparing.Inhabitants.Any(person => person.Position == point) &&
            !preparing.WorldSimulation.Buildings.Any(building => building.Position == point))
            .OrderBy(point => state.Map.FootDistance(point, preparing.Inhabitants.Single(person => person.InhabitantId == actor).Position))
            .ThenBy(point => point.Y).ThenBy(point => point.X).First();
        await Order("restitution-walk", string.Create(CultureInfo.InvariantCulture, $"go to ({destination.X},{destination.Y})"));
        Assert.InRange(state.Map.FootDistance(preparing.Inhabitants.Single(person => person.InhabitantId == actor).Position,
            preparing.Inhabitants.Single(person => person.InhabitantId == recipient).Position), 0, 1);
        state = NonviolentRuntimeFixture.Strict(preparing.ExportState());
        inventory = state.Society.Society.Inventory;
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "restitution-held-wood", actor, carried[1].Id, 1, "other_work", long.MaxValue);
        state = NonviolentRuntimeFixture.Strict(state with
        { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } });
        var totalWood = inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        perform = true;
        using var world = NonviolentRuntimeFixture.Create(state, Choices());
        NonviolentRuntimeFixture.Wake(world, actor, "perform-restitution-source");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = NonviolentRuntimeFixture.Create(PrivateWorldRuntimeCodec.Decode(bytes), Choices());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var ledger = world.Towns[0].Nonviolent;
        Assert.Equal(totalWood, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        if (boundary == "available")
        {
            Assert.Equal("completed", ledger.Agreements.Single(item => item.Id == agreement.Id).Status);
            var effect = Assert.Single(ledger.Effects);
            var receipt = Assert.Single(ledger.NativeReceipts);
            Assert.Equal((2, actor, recipient, receipt.Id), (effect.Quantity, effect.ActorId, effect.BeneficiaryId, effect.NativeReceiptId));
            Assert.Equal((actor, recipient, 2), (receipt.PreviousOwnerId, receipt.ResultOwnerId, receipt.Quantity));
            Assert.Equal(carried[smallFirst ? 1 : 0].Id, receipt.SourceLotId);
            Assert.Equal(2, receipt.AvailableQuantityBefore);
            Assert.Equal(recipient, world.Society.Inventory.GetLot(receipt.ResultLotId).OwnerId);
            Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        }
        else
        {
            Assert.Equal("pending", ledger.Agreements.Single(item => item.Id == agreement.Id).Status);
            Assert.Empty(ledger.Effects);
            Assert.Empty(ledger.NativeReceipts);
            Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            if (boundary == "reserved") Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("restitution-held-wood").State);
        }
        NonviolentRuntimeFixture.Strict(world.ExportState());

        async Task Order(string key, string text)
        {
            preparing.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text));
            await NonviolentRuntimeFixture.UntilAsync(preparing, () => preparing.ExportState().Instructions!
                .Single(item => item.IdempotencyKey == key).Order?.Status == "finished");
            NonviolentRuntimeFixture.Strict(preparing.ExportState());
        }
    }
}
