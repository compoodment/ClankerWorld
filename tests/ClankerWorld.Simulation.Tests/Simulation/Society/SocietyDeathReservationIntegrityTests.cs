using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class SocietyDeathReservationIntegrityTests
{
    [Fact]
    public void DeathPreservesConsumedReceiptWhileReleasingUnusedStockToEstate()
    {
        var state = SocietyFixture.CreateGenesis(
            "death-consumed-receipt",
            [SocietyFixture.CreateFounder("caregiver", "Caregiver")]);
        state = state with
        {
            Inventory = InventoryFixture.AddLot(
                state.Inventory, "medicine-stock", "medicine", "caregiver", 3,
                conditionBasisPoints: 8_750, freshnessBasisPoints: 9_500),
        };
        state = state with
        {
            Inventory = InventoryFixture.Reserve(
                state.Inventory, "consumed-dose", "caregiver", "medicine-stock", 1,
                "treatment dose", 120),
        };
        state = state with
        {
            Inventory = InventoryFixture.ConsumeReservation(state.Inventory, "consumed-dose"),
        };
        state = state with
        {
            Inventory = InventoryFixture.Reserve(
                state.Inventory, "unused-dose", "caregiver", "medicine-stock", 1,
                "future treatment", 120),
        };
        state = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(state));
        var consumedReceipt = state.Inventory.GetReservation("consumed-dose");
        var liveReservation = state.Inventory.GetReservation("unused-dose");
        var remainingStock = state.Inventory.GetLot("medicine-stock");
        Assert.Equal(InventoryReservationState.Completed, consumedReceipt.State);
        Assert.Equal(InventoryReservationState.Reserved, liveReservation.State);
        Assert.Equal(2, remainingStock.Quantity);

        var afterDeath = SocietyFixture.Kill(state, "caregiver", SocietyDeathCause.NaturalAge).Checkpoint;

        Assert.Equal(consumedReceipt, afterDeath.Inventory.GetReservation("consumed-dose"));
        Assert.Equal(
            liveReservation with { State = InventoryReservationState.Released },
            afterDeath.Inventory.GetReservation("unused-dose"));
        Assert.Equal(
            remainingStock with { OwnerId = "estate:caregiver:0" },
            Assert.Single(afterDeath.Inventory.Lots));
        Assert.Equal(2, afterDeath.Inventory.Reservations.Count);
        var saved = SocietyCheckpointCodec.Encode(afterDeath);
        Assert.Equal(saved, SocietyCheckpointCodec.Encode(SocietyCheckpointCodec.Decode(saved)));
    }
}
