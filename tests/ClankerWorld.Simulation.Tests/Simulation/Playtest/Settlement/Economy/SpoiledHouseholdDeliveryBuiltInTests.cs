using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SpoiledHouseholdDeliveryTests
{
    [Fact]
    public async Task TheUnfilteredBuiltInChooserAdmitsRecoveryWhenAnActualCarriedPotSpoils()
    {
        var (pickedUp, actor, camp) = await PickedUpPot();
        Assert.True(pickedUp.Society.Society.Inventory.GetLot(PotGreens).FreshnessBasisPoints > 0);
        Assert.Equal("haul_household_stock", pickedUp.Society.Cognition.Runtimes
            .Single(runtime => runtime.InhabitantId == actor).CurrentIntention?.CandidateId);
        Assert.DoesNotContain(pickedUp.Events, item => item.Kind == "household_delivery_recovered");
        var family = pickedUp.Society.Society.Inventory.Lots.Where(lot => lot.Id == Pot || lot.ContainerLotId == Pot).ToArray();
        var bytes = PrivateWorldRuntimeCodec.Encode(pickedUp);
        using var world = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var admitted = false;
        var walked = false;
        var previous = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;

        // Keep the actual fresh pickup intention and context. Ordinary spoilage
        // must trigger a new unfiltered built-in decision, followed by a real
        // route to camp within the same bounded recovery trip as the directed tests.
        for (var tick = 0; tick < 96 && world.Society.Inventory.GetLot(Pot).OwnerId == actor; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            var current = world.ExportState();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(current), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var intention = current.Society.Cognition.Runtimes.Single(runtime => runtime.InhabitantId == actor).CurrentIntention;
            admitted |= intention is { CandidateId: "recover_household_delivery", Provider: DecisionProviderKind.Deterministic } &&
                intention.WorldTick > pickedUp.Society.Society.WorldTick;
            var position = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            if (position != previous)
            {
                Assert.True(pickedUp.Map.CanFootStep(previous, position));
                walked = true;
            }
            previous = position;
        }

        Assert.True(admitted, "Actual spoilage must admit a fresh recovery choice from the unfiltered built-in provider.");
        Assert.True(walked, "The already collected pot must be carried along actual foot steps to camp.");
        Assert.Equal((Household, 1, new InventoryGroundPosition(camp.X, camp.Y)),
            (world.Society.Inventory.GetLot(Pot).OwnerId, world.Society.Inventory.GetLot(Pot).Quantity,
                world.Society.Inventory.GetLot(Pot).GroundPosition));
        Assert.Equal((Household, 3, Pot, 0), (world.Society.Inventory.GetLot(PotGreens).OwnerId,
            world.Society.Inventory.GetLot(PotGreens).Quantity, world.Society.Inventory.GetLot(PotGreens).ContainerLotId,
            world.Society.Inventory.GetLot(PotGreens).FreshnessBasisPoints));
        Assert.Null(world.Society.Inventory.GetLot(PotGreens).GroundPosition);
        foreach (var original in family)
        {
            var actual = world.Society.Inventory.GetLot(original.Id);
            Assert.Equal((original.ItemKind, original.Quantity, original.ConditionBasisPoints, original.ProvenanceLotId),
                (actual.ItemKind, actual.Quantity, actual.ConditionBasisPoints, actual.ProvenanceLotId));
            Assert.Null(actual.DeliveryBuildingId);
            Assert.Null(actual.StorageBuildingId);
        }
        Assert.Contains(world.Society.Inventory.Events, item => item.Kind == "lot_spoiled" && item.Detail == PotGreens);
        Assert.Single(world.ExportState().Events, item => item.Kind == "household_delivery_recovered" &&
            item.Detail == $"{actor}:{Pot}:4:camp");
        var finalBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var finalReload = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(finalBytes), actor);
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
    }

    private static PrivateWorldRuntime RestoreWithBuiltIn(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state,
            id => id == actor ? (IDecisionProvider)new DeterministicDecisionProvider() : new DeliveryChoices());
}
