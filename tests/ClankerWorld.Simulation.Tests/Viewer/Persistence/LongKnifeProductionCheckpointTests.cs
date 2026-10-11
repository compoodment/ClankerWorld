using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class LongInventoryActionCheckpointTests
{
    [Theory]
    [InlineData(1, 94)]
    [InlineData(3, 550)]
    [InlineData(4, 1158)]
    public async Task CookingOrderKeepsSplitKnifeIdentityThroughHostCheckpointAndCompletion(int splits, int length)
    {
        var (state, lotId) = await SplitInventoryState("iron_knife", splits);
        Assert.Equal(length, lotId.Length);
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == House);
        var inventory = state.Society.Society.Inventory;
        // Match the short control's carried count to the three-split case.
        // All identities still come from actual inventory split/move operations.
        if (splits == 1)
            inventory = InventoryFixture.Relocate(inventory, "equal-carried-count", lotId, Actor, 2,
                groundPosition: new(house.Position.X, house.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "cooking-potatoes", "potatoes", house.HouseholdId!, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "cooking-wood", "wood", house.HouseholdId!, 1, storageBuildingId: House);
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor ? person with
            { Position = house.Position, HungerBasisPoints = 10_000, Survival = new() } : person).ToArray(),
        };
        Assert.Equal(lotId, ToolProgressionRules.PlanWork(inventory, Actor, ToolFamily.Knife)!.ToolLotId);
        var choices = new CookingOrderChoices();
        IDecisionProvider Providers(string id) => id == Actor ? choices : new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, Providers);
        var initial = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), Providers))
            Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var potatoes = Quantity(world, house.HouseholdId!, "potatoes");
        var wood = Quantity(world, house.HouseholdId!, "wood");
        var knives = inventory.Lots.Where(lot => lot.OwnerId == Actor && lot.ItemKind == "iron_knife").Sum(lot => lot.Quantity);
        var receipt = world.SubmitInstruction(new("cook-lineage", "owner:test", Actor, OwnerInstructionKind.MustDo, "cook house potato meal"));
        Assert.Equal("produce_item", Order(world, receipt.InstructionId).Action);
        using var host = new CheckpointHost(world);
        for (var tick = 0; tick < 30 && world.WorldSimulation.ProductionJobs.Count == 0; tick++)
        {
            await host.AdvanceAndCheckSaved();
            using var strict = host.Reload(Providers);
        }
        var active = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Running, active.State);
        Assert.Equal(lotId, active.ToolLotId);
        Assert.Equal(0, Order(world, receipt.InstructionId).CompletedUnits);
        Assert.Contains("produce_item", choices.Selected);
        Assert.All(world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "iron_knife"),
            lot => Assert.Equal(10_000, lot.ConditionBasisPoints));

        var activeState = world.ExportState();
        foreach (var invalid in new[] { " ", lotId + "\0" })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(activeState with
            { WorldSimulation = activeState.WorldSimulation! with { ProductionJobs = [active with { ToolLotId = invalid }] } }));
        if (splits == 1)
        {
            // A syntactically valid reference can become unavailable. It must
            // cancel at completion without charging inputs or wearing another knife.
            using var unavailable = PrivateWorldRuntime.Restore(activeState with
            { WorldSimulation = activeState.WorldSimulation! with { ProductionJobs = [active with { ToolLotId = "missing-knife" }] } }, Providers);
            while (unavailable.WorldTick < active.CompletionTick)
                Assert.True((await unavailable.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(WorldProductionJobState.Cancelled, Assert.Single(unavailable.WorldSimulation.ProductionJobs).State);
            Assert.Equal(0, Order(unavailable, receipt.InstructionId).CompletedUnits);
            Assert.Equal(potatoes, Quantity(unavailable, house.HouseholdId!, "potatoes"));
            Assert.Equal(wood, Quantity(unavailable, house.HouseholdId!, "wood"));
            Assert.All(unavailable.Society.Inventory.Lots.Where(lot => lot.ItemKind == "iron_knife"),
                lot => Assert.Equal(10_000, lot.ConditionBasisPoints));
            Assert.DoesNotContain(unavailable.ExportState().Events, item => item.Kind == "recipe_completed");
            unavailable.Validate();
        }

        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = host.Reload(Providers);
        using var replayHost = new CheckpointHost(replay);
        for (var tick = 0; tick < 20 && Order(world, receipt.InstructionId).Status != "finished"; tick++)
        {
            await host.AdvanceAndCheckSaved();
            await replayHost.AdvanceAndCheckSaved();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            using var strict = host.Reload(Providers);
        }
        var finished = Order(world, receipt.InstructionId);
        Assert.Equal(("finished", 1), (finished.Status, finished.CompletedUnits));
        var completed = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal((WorldProductionJobState.Completed, lotId), (completed.State, completed.ToolLotId));
        Assert.Equal(potatoes - 2, Quantity(world, house.HouseholdId!, "potatoes"));
        Assert.Equal(wood - 1, Quantity(world, house.HouseholdId!, "wood"));
        var meal = world.Society.Inventory.GetLot(completed.JobId + ":output:00");
        Assert.Equal(("simple_meal", house.HouseholdId, House, 2), (meal.ItemKind, meal.OwnerId, meal.StorageBuildingId, meal.Quantity));
        var worn = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "iron_knife" && lot.ConditionBasisPoints < 10_000);
        Assert.Equal((Actor, 1, 9_000, lotId), (worn.OwnerId, worn.Quantity, worn.ConditionBasisPoints, worn.ProvenanceLotId));
        Assert.Equal(knives, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Actor && lot.ItemKind == "iron_knife").Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "recipe_completed" && item.Detail.StartsWith(completed.JobId + ":", StringComparison.Ordinal));
        for (var tick = 0; tick < 3; tick++)
        {
            await host.AdvanceAndCheckSaved();
            await replayHost.AdvanceAndCheckSaved();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(1, Order(replay, receipt.InstructionId).CompletedUnits);
        Assert.Equal(9_000, replay.Society.Inventory.GetLot(worn.Id).ConditionBasisPoints);
        Assert.Equal(potatoes - 2, Quantity(replay, house.HouseholdId!, "potatoes"));
        Assert.Equal(wood - 1, Quantity(replay, house.HouseholdId!, "wood"));
        Assert.Single(replay.ExportState().Events, item => item.Kind == "recipe_completed" && item.Detail.StartsWith(completed.JobId + ":", StringComparison.Ordinal));
        using var finalReload = host.Reload(Providers);
        Assert.Equal(lotId, Assert.Single(finalReload.WorldSimulation.ProductionJobs).ToolLotId);
    }

    private static int Quantity(PrivateWorldRuntime world, string owner, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == owner && lot.ItemKind == kind).Sum(lot => lot.Quantity);

    private sealed class CookingOrderChoices : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider chooser = new();
        public List<string> Selected { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var response = await chooser.DecideAsync(request, cancellationToken);
            Selected.Add(response.SelectedCandidateId);
            return response;
        }
    }
}
