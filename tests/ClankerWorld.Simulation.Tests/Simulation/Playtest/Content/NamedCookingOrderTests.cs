using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ConcreteMealTests
{
    [Theory]
    [InlineData("cook two potato meals", "house-meal", false)]
    [InlineData("cook two wild green meals", "wild-green-meal", false)]
    [InlineData("cook two cultivated green meals", "cultivated-green-meal", false)]
    [InlineData("cook two house porridge", "porridge", false)]
    [InlineData("cook two house berry porridge", "berry-porridge", false)]
    [InlineData("cook two house fruit porridge", "fruit-porridge", false)]
    [InlineData("cook two house bread", "bread", false)]
    [InlineData("cook two house vegetable stew", "vegetable-stew", false)]
    [InlineData("cook two restaurant porridge", "porridge", true)]
    [InlineData("cook two restaurant berry porridge", "berry-porridge", true)]
    [InlineData("cook two restaurant fruit porridge", "fruit-porridge", true)]
    [InlineData("cook two restaurant bread", "bread", true)]
    [InlineData("cook two restaurant vegetable stew", "vegetable-stew", true)]
    [InlineData("prepare two restaurant meals", "restaurant-meal", true)]
    [InlineData("cook two restaurant 2x2 porridge", "restaurant-2x2-porridge", true)]
    [InlineData("cook two restaurant 2x2 berry porridge", "restaurant-2x2-berry-porridge", true)]
    [InlineData("cook two restaurant 2x2 fruit porridge", "restaurant-2x2-fruit-porridge", true)]
    [InlineData("cook two restaurant 2x2 bread", "restaurant-2x2-bread", true)]
    [InlineData("cook two restaurant 2x2 vegetable stew", "restaurant-2x2-vegetable-stew", true)]
    [InlineData("prepare two restaurant 2x2 meals", "restaurant-2x2-restaurant-meal", true)]
    public async Task NamedCookingOrdersBindTheExactRecipeAndPayForTwoRealServingsAcrossJobReplay(
        string text, string localId, bool restaurant)
    {
        var (state, actor, household, site, recipe) = Prepared(localId, restaurant,
            localId.StartsWith("restaurant-2x2-", StringComparison.Ordinal) ? "restaurant-2x2" : "restaurant-1x2");
        using var world = CookingOrderWorld(state, actor);
        var receipt = world.SubmitInstruction(new("named-cooking", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        Assert.Equal(("produce_item", recipe.CanonicalId, recipe.Outputs[0].ResourceId, "output_items", 2),
            (CookingOrder(world, receipt).Action, CookingOrder(world, receipt).TargetRecipeId,
                CookingOrder(world, receipt).TargetOutputKind, CookingOrder(world, receipt).ProgressUnit,
                CookingOrder(world, receipt).RequestedUnits));
        Assert.Null(CookingOrder(world, receipt).TargetFoodKind);
        for (var tick = 0; tick < 60 && world.WorldSimulation.ProductionJobs.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var started = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal((WorldProductionJobState.Running, recipe.CanonicalId, site.InstanceId, receipt.InstructionId),
            (started.State, started.RecipeId, started.BuildingInstanceId, started.OrderInstructionId));
        Assert.Equal(0, CookingOrder(world, receipt).CompletedUnits);
        Assert.All(started.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = CookingOrderWorld(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 60 && CookingOrder(world, receipt).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(("finished", 2), (CookingOrder(world, receipt).Status, CookingOrder(world, receipt).CompletedUnits));
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Completed, job.State);
        Assert.Equal((actor, household, site.InstanceId, recipe.CanonicalId),
            (job.WorkerId, job.OwnerId, job.BuildingInstanceId, job.RecipeId));
        var output = world.Society.Inventory.GetLot(job.JobId + ":output:00");
        Assert.Equal((recipe.Outputs[0].ResourceId, 2, household, site.InstanceId),
            (output.ItemKind, output.Quantity, output.OwnerId, output.StorageBuildingId));
        foreach (var input in recipe.Inputs)
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "meal-test-input-" + input.ResourceId);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(id).State));
        if (recipe.Inputs.Any(input => input.ResourceId == InventoryContainerRules.FreshWater))
        {
            Assert.Equal(site.InstanceId, world.Society.Inventory.GetLot("meal-test-jug").StorageBuildingId);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ContainerLotId == "meal-test-jug");
        }
        Assert.Single(world.ExportState().Events, item => item.Kind == "recipe_completed");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(2, world.Society.Inventory.GetLot(output.Id).Quantity);
        world.Validate();
    }

    private static PrivateWorldRuntime CookingOrderWorld(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(state, id => new Chooser(id == actor ? ["produce_item"] : []));

    private static OwnerInstructionOrder CookingOrder(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
}
