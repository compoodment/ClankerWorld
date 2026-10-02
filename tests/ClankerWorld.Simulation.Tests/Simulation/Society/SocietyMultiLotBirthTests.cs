using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class SocietyMultiLotBirthTests
{
    [Fact]
    public async Task TwoActualCookingOutputsPayOneBirthAndEveryContributionSurvivesIdempotentReload()
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: true);
        var checkpoint = prepared.State.Society.Society;
        var request = prepared.Request();
        var pot = checkpoint.Inventory.GetLot(CookedBirthFixture.PotId);
        var committed = SocietyFixture.CommitBirth(checkpoint, request);
        Assert.NotNull(committed.CreatedId);
        var birth = Assert.Single(committed.Checkpoint.Births);
        Assert.Equal(prepared.First, birth.PrimaryCaregiverId);
        Assert.Equal(prepared.Household, birth.HouseholdId);
        Assert.Equal(SocietyAgeBand.Infant, committed.Checkpoint.GetInhabitant(birth.ChildId).AgeBand);
        Assert.Equal(pot, committed.Checkpoint.Inventory.GetLot(pot.Id));
        Assert.DoesNotContain(committed.Checkpoint.Inventory.Lots, lot => prepared.OutputIds.Take(2).Contains(lot.Id));
        Assert.Equal(4, committed.Checkpoint.Inventory.Lots.Where(lot => prepared.OutputIds.Skip(2).Contains(lot.Id))
            .Sum(lot => lot.Quantity));
        for (var index = 0; index < 2; index++)
        {
            var receipt = committed.Checkpoint.Inventory.GetReservation($"birth:{request.Id}:food:{index:D2}");
            Assert.Equal(prepared.OutputIds[index], receipt.LotId);
            Assert.Equal(prepared.Household, receipt.OwnerId);
            Assert.Equal(2, receipt.Quantity);
            Assert.Equal("birth:" + request.Id, receipt.Purpose);
            Assert.Equal(InventoryReservationState.Completed, receipt.State);
        }
        var restored = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(committed.Checkpoint));
        Assert.Equal(SocietyCheckpointCodec.Encode(committed.Checkpoint), SocietyCheckpointCodec.Encode(restored));
        var retry = SocietyFixture.CommitBirth(restored, request);
        Assert.Equal(restored, retry.Checkpoint);
        Assert.Equal(birth.ChildId, retry.CreatedId);
        Assert.Single(retry.Checkpoint.Births);
        Assert.Equal(2, retry.Checkpoint.Inventory.Reservations.Count(item => item.Purpose == "birth:" + request.Id));
    }

    [Theory]
    [InlineData("later-lot")]
    [InlineData("later-quantity")]
    [InlineData("revision")]
    [InlineData("legacy-mode")]
    public async Task ACommittedBirthRefusesAConflictingRequestWithoutAnotherDebit(string change)
    {
        var prepared = await CookedBirthFixture.PrepareAsync();
        var request = prepared.Request();
        var committed = SocietyFixture.CommitBirth(prepared.State.Society.Society, request).Checkpoint;
        var altered = change switch
        {
            "later-lot" => request with { FoodContributions = [new(prepared.OutputIds[0], 2), new(prepared.OutputIds[2], 2)] },
            "later-quantity" => request with
            {
                FoodContributions =
                [new(prepared.OutputIds[0], 2), new(prepared.OutputIds[1], 1), new(prepared.OutputIds[2], 1)]
            },
            "revision" => request with { Revision = 2 },
            _ => request with { FoodContributions = null },
        };
        var result = SocietyFixture.CommitBirth(committed, altered);
        Assert.Null(result.CreatedId);
        Assert.Equal(committed.Inventory, result.Checkpoint.Inventory);
        Assert.Equal(committed.Inhabitants, result.Checkpoint.Inhabitants);
        Assert.Equal(committed.Households, result.Checkpoint.Households);
        Assert.Equal(committed.Relationships, result.Checkpoint.Relationships);
        Assert.Equal(committed.Births, result.Checkpoint.Births);
        Assert.Equal("birth_rejected", result.Checkpoint.Events[^1].Kind);
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("foreign")]
    [InlineData("spoiled")]
    [InlineData("missing")]
    [InlineData("broken-pot")]
    public async Task AFailedFinalContributionPublishesNeitherEarlierClaimsNorAnInfant(string fault)
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: fault == "broken-pot");
        var checkpoint = prepared.State.Society.Society;
        var inventory = checkpoint.Inventory;
        var last = prepared.OutputIds[1];
        if (fault == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "other-food-claim", prepared.Household, last, 1,
                "other_work", long.MaxValue);
        if (fault == "foreign")
            inventory = InventoryFixture.Transfer(inventory, "foreign-food", prepared.Household, prepared.Second,
                last, 2, "personal_food");
        if (fault == "foreign")
        {
            var outsider = checkpoint.Inhabitants.First(person => person.HouseholdId != prepared.Household).Id;
            inventory = InventoryFixture.Transfer(inventory, "outside-food", prepared.Second, outsider, last, 2, "gift");
        }
        if (fault == "spoiled")
        {
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == last
                ? lot with { FreshnessBasisPoints = 1 } : lot).ToArray()
            };
            checkpoint = SocietyFixture.AdvanceTo(checkpoint with { Inventory = inventory }, checkpoint.WorldTick + 1).Checkpoint;
            inventory = checkpoint.Inventory;
            Assert.Equal(0, inventory.GetLot(last).FreshnessBasisPoints);
            Assert.True(inventory.GetLot(prepared.OutputIds[0]).FreshnessBasisPoints > 0);
        }
        if (fault == "missing")
        {
            inventory = InventoryFixture.Reserve(inventory, "earlier-consumption", prepared.Household, last, 2,
                "already_eaten", inventory.WorldTick);
            inventory = InventoryFixture.ConsumeReservation(inventory, "earlier-consumption");
        }
        if (fault == "broken-pot")
            inventory = InventoryFixture.WearSingleUnit(inventory, CookedBirthFixture.PotId, 10_000);
        checkpoint = checkpoint with { Inventory = inventory };
        var request = prepared.Request() with { RequestedTick = checkpoint.WorldTick };
        var result = SocietyFixture.CommitBirth(checkpoint, request);
        Assert.Null(result.CreatedId);
        Assert.Equal(inventory, result.Checkpoint.Inventory);
        Assert.Equal(checkpoint.Inhabitants, result.Checkpoint.Inhabitants);
        Assert.Equal(checkpoint.Households, result.Checkpoint.Households);
        Assert.Equal(checkpoint.Relationships, result.Checkpoint.Relationships);
        Assert.Empty(result.Checkpoint.Births);
        Assert.DoesNotContain(result.Checkpoint.Inventory.Reservations, item => item.Purpose == "birth:" + request.Id);
        Assert.Equal(SocietyCheckpointCodec.Encode(result.Checkpoint),
            SocietyCheckpointCodec.Encode(SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(result.Checkpoint))));
    }

    [Fact]
    public async Task FourPartialOutputsConsumeOnlyTheirUnclaimedPortionsAndKeepThePotAndClaims()
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: true);
        var inventory = prepared.State.Society.Society.Inventory;
        foreach (var id in prepared.OutputIds)
            inventory = InventoryFixture.Reserve(inventory, "keep-one:" + id, prepared.Household,
                id, 1, "another_meal", long.MaxValue);
        var claims = inventory.Reservations.Where(item => item.Purpose == "another_meal").ToArray();
        Assert.Equal(4, claims.Length);
        var request = prepared.Request("four-input-child") with
        { FoodContributions = prepared.OutputIds.Select(id => new SocietyBirthFoodContribution(id, 1)).ToArray() };
        var result = SocietyFixture.CommitBirth(prepared.State.Society.Society with { Inventory = inventory }, request);
        Assert.NotNull(result.CreatedId);
        Assert.All(prepared.OutputIds, id =>
        {
            var food = result.Checkpoint.Inventory.GetLot(id);
            Assert.Equal(1, food.Quantity);
            Assert.Equal(CookedBirthFixture.PotId, food.ContainerLotId);
        });
        Assert.All(claims, claim => Assert.Equal(claim, result.Checkpoint.Inventory.GetReservation(claim.Id)));
        Assert.Equal(inventory.GetLot(CookedBirthFixture.PotId), result.Checkpoint.Inventory.GetLot(CookedBirthFixture.PotId));
        var receipts = result.Checkpoint.Inventory.Reservations.Where(item => item.Purpose == "birth:" + request.Id).ToArray();
        Assert.Equal(4, receipts.Length);
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(1, receipt.Quantity);
            Assert.Equal(InventoryReservationState.Completed, receipt.State);
        });
        Assert.Equal(result.Checkpoint, SocietyFixture.CommitBirth(result.Checkpoint, request).Checkpoint);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("wrong-total")]
    [InlineData("wrong-anchor")]
    [InlineData("unordered")]
    [InlineData("null-entry")]
    [InlineData("too-many")]
    public async Task MalformedContributionCommandsCannotChangeAnyInventoryOrBirthState(string fault)
    {
        var prepared = await CookedBirthFixture.PrepareAsync();
        var checkpoint = prepared.State.Society.Society;
        var request = prepared.Request();
        request = fault switch
        {
            "empty" => request with { FoodContributions = [] },
            "duplicate" => request with { FoodContributions = [new(prepared.OutputIds[0], 2), new(prepared.OutputIds[0], 2)] },
            "wrong-total" => request with { FoodContributions = [new(prepared.OutputIds[0], 2), new(prepared.OutputIds[1], 1)] },
            "wrong-anchor" => request with { FoodLotId = prepared.OutputIds[2] },
            "unordered" => request with
            {
                FoodLotId = prepared.OutputIds[1],
                FoodContributions =
                [new(prepared.OutputIds[1], 2), new(prepared.OutputIds[0], 2)]
            },
            "null-entry" => request with { FoodContributions = [new(prepared.OutputIds[0], 2), null!] },
            _ => request with
            {
                FoodQuantity = 5,
                FoodContributions =
                [new("a", 1), new("b", 1), new("c", 1), new("d", 1), new("e", 1)],
                FoodLotId = "a"
            },
        };
        var before = SocietyCheckpointCodec.Encode(checkpoint);
        Assert.Throws<ArgumentException>(() => SocietyFixture.CommitBirth(checkpoint, request));
        Assert.Equal(before, SocietyCheckpointCodec.Encode(checkpoint));
    }

    [Fact]
    public async Task LegacySingleLotBirthStillConsumesTwoAndUsesItsOriginalReceipt()
    {
        var prepared = await CookedBirthFixture.PrepareAsync();
        var request = prepared.Request("legacy-child") with { FoodContributions = null, FoodQuantity = 2 };
        var result = SocietyFixture.CommitBirth(prepared.State.Society.Society, request);
        Assert.NotNull(result.CreatedId);
        var receipt = result.Checkpoint.Inventory.GetReservation("birth:legacy-child:food");
        Assert.Equal(prepared.OutputIds[0], receipt.LotId);
        Assert.Equal(2, receipt.Quantity);
        Assert.Equal(InventoryReservationState.Completed, receipt.State);
        Assert.Equal(result.Checkpoint, SocietyFixture.CommitBirth(result.Checkpoint, request).Checkpoint);
    }
}
