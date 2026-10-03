using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class CookedMealBirthTests
{
    [Fact]
    public async Task ParentsCompleteBirthFromDistinctTwoServingMealsAndRetainTheirRealAdultReserveAcrossReplay()
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: true);
        Assert.All(prepared.OutputIds, id => Assert.Equal(2, prepared.State.Society.Society.Inventory.GetLot(id).Quantity));
        Assert.DoesNotContain(prepared.State.Society.Society.Inventory.Lots, lot =>
            lot.OwnerId == prepared.Household && PersonalEquipmentRules.AvailableQuantity(
                prepared.State.Society.Society.Inventory, lot) >= 4 && InventoryContainerRules.IsFood(lot.ItemKind));
        using var world = CookedBirthFixture.Restore(prepared);
        for (var tick = 0; tick < 40 && world.Inhabitants.Single(person => person.InhabitantId == prepared.First)
                 .Parenthood?.Stage != "preparing"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var plan = world.Inhabitants.Single(person => person.InhabitantId == prepared.First).Parenthood;
        Assert.NotNull(plan);
        Assert.Equal("preparing", plan.Stage);
        Assert.Equal(prepared.First, plan.PrimaryCaregiverId);
        var due = plan.LastTransitionTick + 600;
        while (world.WorldTick < due - 1) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Society.Births);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before),
            id => new CookedBirthFixture.BirthChooser(id, prepared.First, prepared.Second));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var pot = world.Society.Inventory.GetLot(CookedBirthFixture.PotId);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var birth = Assert.Single(world.Society.Births);
        var infant = world.Society.GetInhabitant(birth.ChildId);
        Assert.Equal(SocietyAgeBand.Infant, infant.AgeBand);
        Assert.Equal(prepared.First, infant.PrimaryCaregiverId);
        Assert.Equal(prepared.Household, infant.HouseholdId);
        Assert.Equal(world.Society.GetInhabitant(prepared.First).DomesticFamilyUnitId, infant.DomesticFamilyUnitId);
        var receipts = world.Society.Inventory.Reservations.Where(receipt => receipt.Purpose == "birth:" + birth.RequestId)
            .OrderBy(receipt => receipt.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, receipts.Length);
        Assert.Equal(prepared.OutputIds.Take(2), receipts.Select(receipt => receipt.LotId));
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(prepared.Household, receipt.OwnerId);
            Assert.Equal(2, receipt.Quantity);
            Assert.Equal(InventoryReservationState.Completed, receipt.State);
        });
        Assert.Equal(4, prepared.OutputIds.Skip(2).Sum(id => world.Society.Inventory.GetLot(id).Quantity));
        var retainedPot = world.Society.Inventory.GetLot(CookedBirthFixture.PotId);
        Assert.Equal(pot with { LastProcessedTick = retainedPot.LastProcessedTick }, retainedPot);
        Assert.All(prepared.State.Society.Society.Inventory.Reservations.Where(receipt =>
            receipt.Purpose == "unrelated_household_food"), receipt =>
            Assert.Equal(receipt, world.Society.Inventory.GetReservation(receipt.Id)));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => new CookedBirthFixture.BirthChooser(id, prepared.First, prepared.Second));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Single(world.Society.Births);
    }

    [Fact]
    public async Task AClaimedServingBlocksBirthWithoutSpendingTheTwoAdultsReserve()
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: true);
        var inventory = prepared.State.Society.Society.Inventory;
        var last = prepared.OutputIds[^1];
        inventory = InventoryFixture.Reserve(inventory, "reserved-adult-serving", prepared.Household,
            last, 1, "another_meal", long.MaxValue);
        prepared = prepared with { State = FarmFieldTests.WithInventory(prepared.State, inventory) };
        using var world = CookedBirthFixture.Restore(prepared);
        for (var tick = 0; tick < 650; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Society.Births);
        Assert.Equal(8, prepared.OutputIds.Sum(id => world.Society.Inventory.GetLot(id).Quantity));
        Assert.Equal(inventory.GetReservation("reserved-adult-serving"),
            world.Society.Inventory.GetReservation("reserved-adult-serving"));
    }
}
