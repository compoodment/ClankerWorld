using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ConstructionBorderGrowthTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    public void RankedGrowthMatchesActualRoundedBorderAtMapEdgeAndWater(int width, int height)
    {
        var map = new SeededMap(12, 12, 0,
            (from y in Enumerable.Range(0, 12)
             from x in Enumerable.Range(0, 12)
             select new TerrainTile(new(x, y), x == 5 && y == 4 ? TerrainKind.Water : TerrainKind.Meadow))
            .ToArray(), [], [], "fixture")
        { WrapsEastWest = true };
        var anchor = new GridPoint(0, 4);
        var border = (from y in Enumerable.Range(anchor.Y, height)
                      from x in Enumerable.Range(anchor.X, width)
                      select new GridPoint(x, y)).ToArray();
        var town = new TownRuntimeState("town:test", "Test Town", "founded", 0, [], [], border);
        var definition = new BuildingDefinition("sha256:" + new string('a', 64), "test-building",
            ContentVersion.Parse("1.0.0"), "Test building", width, height, 1);
        var context = new TownLayoutContext(map, town, [],
            new Dictionary<GridPoint, int> { [anchor] = 100 }, [], []);

        Assert.True(TownLayoutService.TryEvaluateConstructionSite(context, definition, anchor, out var candidate));
        var expanded = TownBorderRules.ExpandForBuilding(map, town, anchor, width, height).ToHashSet();
        Assert.Equal(expanded.Except(border).Count(), candidate!.TownBorderGrowthTiles);
        Assert.DoesNotContain(new GridPoint(5, 4), expanded);
        Assert.DoesNotContain(expanded, point => point.X == 11);
        Assert.Contains(candidate.Reasons, reason => reason.Code == TownConstructionSiteReasonCodes.TownGrowth);
    }
}
