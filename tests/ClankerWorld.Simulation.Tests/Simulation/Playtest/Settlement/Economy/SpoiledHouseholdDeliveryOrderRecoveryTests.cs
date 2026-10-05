using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SpoiledHouseholdDeliveryTests
{
    private const string UrgentRecoveryMeal = "urgent-recovery-household-berries";

    [Fact]
    public async Task AnUrgentEatOrderRecoversAFullSpoiledPotDeliveryBeforeCollectingAndCreditingFood()
    {
        var (state, actor, camp) = await UrgentFullSpoiledDelivery();
        using var submitted = RestoreWithBuiltIn(state, actor);
        var receipt = submitted.SubmitInstruction(new OwnerInstructionRequest("urgent-spoiled-delivery-eat", "owner:test",
            actor, OwnerInstructionKind.MustDo, "eat berries"));
        var order = RecoveryFoodOrder(submitted.ExportState(), receipt.InstructionId);
        Assert.Equal(("consume_food", "berries", 0), (order.Action, order.TargetFoodKind, order.CompletedUnits));
        Assert.Null(order.LastEffectId);

        // Reload the real active order with the complete in-flight load. The
        // hunger control above changed no pickup, intention or delivery state.
        var bytes = PrivateWorldRuntimeCodec.Encode(submitted.ExportState());
        using var world = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        using var replay = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var admittedRecovery = false;
        var walked = false;
        var previous = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        for (var tick = 0; tick < 96 && world.Society.Inventory.GetLot(Pot).OwnerId == actor; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            var current = world.ExportState();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(current), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var intention = current.Society.Cognition.Runtimes.Single(item => item.InhabitantId == actor).CurrentIntention;
            admittedRecovery |= intention is { CandidateId: "recover_household_delivery", Provider: DecisionProviderKind.Deterministic } &&
                intention.OperativeOrderInstructionId == receipt.InstructionId;
            var position = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            if (position != previous)
            {
                Assert.True(state.Map.CanFootStep(previous, position));
                walked = true;
            }
            previous = position;
            Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null), 4, 8);
            Assert.Equal(0, RecoveryFoodOrder(current, receipt.InstructionId).CompletedUnits);
            Assert.Null(RecoveryFoodOrder(current, receipt.InstructionId).LastEffectId);
            Assert.DoesNotContain(current.CompletedInstructionIds ?? [], id => id == receipt.InstructionId);
            Assert.DoesNotContain(current.Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        }

        Assert.True(admittedRecovery, "The active food order must admit actual spoiled-delivery recovery for the urgent full carrier.");
        Assert.True(walked, "Recovery must carry the pot along legal foot steps to the household camp.");
        AssertUrgentRecoveredPot(world, state, actor, camp);
        Assert.Equal(4, PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor, null));
        AssertRecoveryStockConserved(world, state, mealConsumed: false);
        Assert.Single(world.ExportState().Events, item => item.Kind == "household_delivery_recovered" &&
            item.Detail == $"{actor}:{Pot}:4:camp");

        for (var tick = 0; tick < 12 && RecoveryFoodOrder(world.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }

        var final = world.ExportState();
        var finished = RecoveryFoodOrder(final, receipt.InstructionId);
        Assert.Equal(("finished", 1), (finished.Status, finished.CompletedUnits));
        Assert.StartsWith("consume:", finished.LastEffectId);
        Assert.InRange(finished.LastEffectId!.Length, 1, 512);
        Assert.Contains(receipt.InstructionId, final.CompletedInstructionIds ?? []);
        Assert.Single(final.Events, item => item.Kind == "household_food_collected" &&
            item.Detail == $"{actor}:{UrgentRecoveryMeal}:1");
        Assert.Single(final.Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        Assert.Single(final.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == receipt.InstructionId + ":consume_food");
        AssertUrgentRecoveredPot(world, state, actor, camp);
        AssertRecoveryStockConserved(world, state, mealConsumed: true);
        var finalBytes = PrivateWorldRuntimeCodec.Encode(final);
        using var reloaded = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(finalBytes), actor);
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        reloaded.Validate();
    }

    [Fact]
    public async Task AHeldPersonalResponseDoesNotBlockUrgentRecoveryOfAFullSpoiledDeliveryLoad()
    {
        var (state, actor, camp) = await UrgentFullSpoiledDelivery();
        var provider = new HeldSpoiledDeliveryProvider();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? (IDecisionProvider)provider : new DeliveryChoices());
        var previous = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var walked = false;
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var request = Assert.Single(provider.Requests);
            Assert.Null(request.OperativeOrderInstructionId);
            Assert.Contains(request.Candidates, candidate => candidate.Id == "recover_household_delivery");
            Assert.DoesNotContain(request.Candidates, candidate => candidate.Id == "make_room_for_food");
            for (var tick = 0; tick < 96 && world.Society.Inventory.GetLot(Pot).OwnerId == actor; tick++)
            {
                var position = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
                if (position != previous)
                {
                    Assert.True(state.Map.CanFootStep(previous, position));
                    walked = true;
                }
                previous = position;
                Assert.Single(provider.Requests);
                Assert.False(provider.Release.Task.IsCompleted);
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null), 4, 8);
            }

            Assert.True(walked, "Urgent local recovery must walk to camp while the single personal response remains held.");
            AssertUrgentRecoveredPot(world, state, actor, camp);
            Assert.Equal(4, PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor, null));
            AssertRecoveryStockConserved(world, state, mealConsumed: false);
            Assert.Single(provider.Requests);
            Assert.False(provider.Release.Task.IsCompleted);
            var current = world.ExportState();
            Assert.DoesNotContain(current.Events, item => item.Kind == "hosted_decision_completed" &&
                item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
            Assert.DoesNotContain(current.Society.Cognition.Runtimes, item => item.InhabitantId == actor &&
                item.CurrentIntention is { CandidateId: "recover_household_delivery", Provider: DecisionProviderKind.LargeLanguageModel });
            Assert.Single(current.Events, item => item.Kind == "household_delivery_recovered" &&
                item.Detail == $"{actor}:{Pot}:4:camp");
            var bytes = PrivateWorldRuntimeCodec.Encode(current);
            using var reloaded = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(bytes), actor);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            AssertUrgentRecoveredPot(reloaded, state, actor, camp);
            reloaded.Validate();
        }
        finally
        {
            provider.Release.TrySetResult(true);
        }
    }

    private static async Task<(PrivateWorldRuntimeState State, string Actor, GridPoint Camp)> UrgentFullSpoiledDelivery()
    {
        var (pickedUp, actor, camp) = await PickedUpPot();
        // Admit hauling while it is legal, then choose actual safe_idle until
        // ordinary spoilage finishes. This keeps both real pickups aboard,
        // without fabricating delivery pointers or prematurely recovering them.
        using var world = Restore(pickedUp, actor, new DeliveryChoices("haul_household_stock"));
        for (var tick = 0; tick < 12 && (world.Society.Inventory.GetLot(SecondGreens).OwnerId != actor ||
            world.Society.Inventory.GetLot(SecondGreens).FreshnessBasisPoints > 0 ||
            world.Society.Inventory.GetLot(PotGreens).FreshnessBasisPoints > 0); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        AssertSpoiledCarriedFamily(world, actor);
        Assert.Equal((actor, 4, House, 0), (world.Society.Inventory.GetLot(SecondGreens).OwnerId,
            world.Society.Inventory.GetLot(SecondGreens).Quantity, world.Society.Inventory.GetLot(SecondGreens).DeliveryBuildingId,
            world.Society.Inventory.GetLot(SecondGreens).FreshnessBasisPoints));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_picked_up" &&
            item.Detail == $"{actor}:{SecondGreens}:4:{House}");
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor, null));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "household_delivery_recovered");
        var state = world.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            UrgentRecoveryMeal, "berries", Household, 1, groundPosition: new(camp.X, camp.Y));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var reloaded = RestoreWithBuiltIn(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        reloaded.Validate();
        return (reloaded.ExportState(), actor, camp);
    }

    private static OwnerInstructionOrder RecoveryFoodOrder(PrivateWorldRuntimeState state, string instructionId) =>
        Assert.Single(state.Instructions!, instruction => instruction.InstructionId == instructionId).Order!;

    private static void AssertUrgentRecoveredPot(PrivateWorldRuntime world, PrivateWorldRuntimeState original,
        string actor, GridPoint camp)
    {
        Assert.Equal((Household, 1, new InventoryGroundPosition(camp.X, camp.Y)),
            (world.Society.Inventory.GetLot(Pot).OwnerId, world.Society.Inventory.GetLot(Pot).Quantity,
                world.Society.Inventory.GetLot(Pot).GroundPosition));
        Assert.Equal((Household, 3, Pot, 0), (world.Society.Inventory.GetLot(PotGreens).OwnerId,
            world.Society.Inventory.GetLot(PotGreens).Quantity, world.Society.Inventory.GetLot(PotGreens).ContainerLotId,
            world.Society.Inventory.GetLot(PotGreens).FreshnessBasisPoints));
        Assert.Null(world.Society.Inventory.GetLot(PotGreens).GroundPosition);
        foreach (var id in new[] { Pot, PotGreens, SecondGreens })
        {
            var before = original.Society.Society.Inventory.GetLot(id);
            var after = world.Society.Inventory.GetLot(id);
            Assert.Equal((before.Id, before.ItemKind, before.Quantity, before.ConditionBasisPoints, before.ProvenanceLotId),
                (after.Id, after.ItemKind, after.Quantity, after.ConditionBasisPoints, after.ProvenanceLotId));
            Assert.Null(after.StorageBuildingId);
        }
        Assert.Null(world.Society.Inventory.GetLot(Pot).DeliveryBuildingId);
        Assert.Null(world.Society.Inventory.GetLot(PotGreens).DeliveryBuildingId);
        Assert.Equal((actor, 4, House, 0), (world.Society.Inventory.GetLot(SecondGreens).OwnerId,
            world.Society.Inventory.GetLot(SecondGreens).Quantity, world.Society.Inventory.GetLot(SecondGreens).DeliveryBuildingId,
            world.Society.Inventory.GetLot(SecondGreens).FreshnessBasisPoints));
    }

    private static void AssertRecoveryStockConserved(PrivateWorldRuntime world, PrivateWorldRuntimeState original, bool mealConsumed)
    {
        var originalReservations = original.Society.Society.Inventory.Reservations;
        var originalIds = originalReservations.Select(reservation => reservation.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(originalReservations, world.Society.Inventory.Reservations.Where(reservation => originalIds.Contains(reservation.Id)));
        var addedReservations = world.Society.Inventory.Reservations.Where(reservation => !originalIds.Contains(reservation.Id));
        if (mealConsumed)
        {
            var consumption = Assert.Single(addedReservations);
            Assert.Equal((UrgentRecoveryMeal, 1, "direct_consumption", InventoryReservationState.Completed),
                (consumption.LotId, consumption.Quantity, consumption.Purpose, consumption.State));
            Assert.Equal(original.Society.Society.Inventory.GetLot(Pot).OwnerId, consumption.OwnerId);
        }
        else
            Assert.Empty(addedReservations);
        Assert.Equal(original.Society.Society.Inventory.Lots.Where(lot => !mealConsumed || lot.Id != UrgentRecoveryMeal)
                .Select(lot => (lot.Id, lot.Quantity)),
            world.Society.Inventory.Lots.Select(lot => (lot.Id, lot.Quantity)));
    }

    private sealed class HeldSpoiledDeliveryProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            Requests.Enqueue(request.Observation);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            var selected = request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1d,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal));
        }
    }
}
