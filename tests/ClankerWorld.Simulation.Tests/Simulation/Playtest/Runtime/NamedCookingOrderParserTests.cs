using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldProductionOrderParserTests
{
    [Theory]
    [InlineData("cook house bread", 1, "production_batches", false, false)]
    [InlineData("cook four house bread", 4, "output_items", true, false)]
    [InlineData("cook two batches of house bread", 2, "production_batches", true, false)]
    [InlineData("keep cooking house bread", 1, "production_batches", false, true)]
    public void NamedCookingKeepsDefaultBatchesServingCountsAndRepetitionDistinct(
        string text, int count, string unit, bool explicitCount, bool repeat)
    {
        using var world = CreateWorld();
        var order = Submit(world, "cooking-count", text);
        var recipe = Assert.Single(world.WorldContent.Recipes, item => item.LocalId == "bread" &&
            item.WorkstationBuildingId == HouseContent.House1x1().CanonicalId);
        Assert.Equal(("produce_item", recipe.CanonicalId, "bread", count, unit, explicitCount, repeat),
            (order.Action, order.TargetRecipeId, order.TargetOutputKind, order.RequestedUnits,
                order.ProgressUnit, order.QuantityIsExplicit, order.RepeatUntilCancelled));
        Assert.Equal(2, recipe.Outputs[0].Amount);
        Assert.Null(order.TargetFoodKind);
        world.Validate();
    }
}
