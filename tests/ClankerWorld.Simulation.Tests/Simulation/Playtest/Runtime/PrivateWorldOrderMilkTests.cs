using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData("ordered")]
    [InlineData("unordered")]
    [InlineData("bread")]
    [InlineData("reserved")]
    [InlineData("spoiled")]
    [InlineData("broken-jug")]
    public async Task UrgentCarriedMilkInterruptsAnOrderWithoutBypassingAvailability(string food)
    {
        var (state, source) = MaterialOrderState("stone", "wooden_pickaxe");
        var inventory = WithoutFoodLots(state.Society.Society.Inventory);
        inventory = InventoryFixture.AddLot(inventory, "order-milk-jug", "water_jug", HarvestInstructionActor, 1);
        inventory = InventoryFixture.AddLot(inventory, "order-milk", "milk", HarvestInstructionActor, 2,
            containerLotId: "order-milk-jug");
        if (food == "bread")
            inventory = InventoryFixture.AddLot(inventory, "order-bread", "bread", HarvestInstructionActor, 1);
        if (food == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "order-held-milk", HarvestInstructionActor, "order-milk", 2,
                "other-personal-work", long.MaxValue);
        if (food is "spoiled" or "broken-jug")
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => food == "spoiled" && lot.Id == "order-milk"
                    ? lot with { FreshnessBasisPoints = 0 }
                    : food == "broken-jug" && lot.Id == "order-milk-jug"
                        ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
            };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { HungerBasisPoints = 1_500 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new MilkOrderProvider());
        var receipt = food == "unordered" ? null
            : SubmitMaterialOrder(world, "milk-interruption", $"gather stone from {source.Id}");
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new MilkOrderProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var usableMilk = food is "ordered" or "unordered";
        Assert.Equal(usableMilk ? 1 : 2, world.Society.Inventory.GetLot("order-milk").Quantity);
        Assert.Equal(usableMilk ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "milk_drunk"));
        Assert.Equal(food == "bread" ? 5_496 : usableMilk ? 4_496 : 1_496,
            world.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor).HungerBasisPoints);
        var jug = world.Society.Inventory.GetLot("order-milk-jug");
        Assert.Equal((HarvestInstructionActor, 1, food == "broken-jug" ? 0 : 10_000),
            (jug.OwnerId, jug.Quantity, jug.ConditionBasisPoints));
        if (food == "reserved") Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation("order-held-milk").State);
        if (food == "spoiled") Assert.Equal(0, world.Society.Inventory.GetLot("order-milk").FreshnessBasisPoints);
        if (food == "bread") Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "order-bread" && lot.Quantity > 0);
        if (receipt is not null && food is "ordered" or "bread")
        {
            Assert.Equal(("interrupted", 0), (CancellationOrder(world.ExportState(), receipt.InstructionId).Status,
                CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits));
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var order = CancellationOrder(world.ExportState(), receipt.InstructionId);
            Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
            Assert.Equal(6, Assert.Single(world.Society.Inventory.Lots,
                lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "stone").Quantity);
            Assert.Equal(0, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
            Assert.Equal(HarvestInstructionActor, world.Society.Inventory.GetLot("order-milk-jug").OwnerId);
        }
        world.Validate();
        replay.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new MilkOrderProvider());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private sealed class MilkOrderProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choice = observation.InhabitantId == HarvestInstructionActor
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id == "consume_food")
                    ?? observation.Candidates.FirstOrDefault(candidate => candidate.Id == "drink_milk") : null;
            choice ??= observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}
