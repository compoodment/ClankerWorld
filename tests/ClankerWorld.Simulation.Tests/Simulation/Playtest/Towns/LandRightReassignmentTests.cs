using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class LandRightReassignmentTests
{
    [Fact]
    public void WholePlotTransferRetainsItsIdentityWithoutGrantingUnrecordedFootprintTiles()
    {
        var map = new SeededMap(4, 1, 0, Enumerable.Range(0, 4)
            .Select(x => new TerrainTile(new GridPoint(x, 0), TerrainKind.Meadow)).ToArray(), [], [], "fixture");
        var right = new HouseholdLandUseRight("original", "town", "old", [new(1, 0)], 2, "starter_allocation", 50);
        var moved = TownLandRightsRules.ReassignFootprintRights(map, [right], new HashSet<GridPoint> { new(1, 0), new(2, 0) }, "new", 10);

        var transferred = Assert.Single(moved);
        Assert.Equal(right with { HouseholdId = "new", Tiles = transferred.Tiles }, transferred);
        Assert.Equal(right.Tiles, transferred.Tiles);
        Assert.DoesNotContain(new GridPoint(2, 0), transferred.Tiles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingPartOfAPlotKeepsEveryTileAndGrantTermIncludingAcrossTheWorldSeam(bool atSeam)
    {
        var map = new SeededMap(8, 3, 0,
            (from y in Enumerable.Range(0, 3)
             from x in Enumerable.Range(0, 8)
             select new TerrainTile(new GridPoint(x, y), TerrainKind.Meadow)).ToArray(), [], [], "fixture");
        var middle = atSeam ? 0 : 3;
        var left = new GridPoint((middle + 7) % 8, 1);
        var center = new GridPoint(middle, 1);
        var right = new GridPoint(middle + 1, 1);
        var shared = new HouseholdLandUseRight("shared", "town", "old", TownLandRightsRules.OrderTiles([left, center, right]),
            2, "starter_allocation", 50);
        // A collision with the first generated ID must not replace an unrelated right.
        var untouched = new HouseholdLandUseRight("household-use:reassigned:10:0", "town", "other", [new(4, 2)], 0, "starter_allocation");
        var original = new[] { shared, untouched };

        var moved = TownLandRightsRules.ReassignFootprintRights(map, original, new HashSet<GridPoint> { center }, "new", 10);

        Assert.Equal(4, moved.Count);
        Assert.Equal(moved.Count, moved.Select(item => item.Id).Distinct().Count());
        Assert.Contains(untouched, moved);
        Assert.Equal(new[] { center }, Assert.Single(moved, item => item.HouseholdId == "new").Tiles);
        Assert.Equal(TownLandRightsRules.OrderTiles([left, right]),
            TownLandRightsRules.OrderTiles(moved.Where(item => item.HouseholdId == "old").SelectMany(item => item.Tiles)));
        Assert.All(moved.Where(item => item != untouched), item =>
        {
            Assert.Equal((shared.TownId, shared.GrantedTick, shared.GrantSource, shared.AgreedEndTick),
                (item.TownId, item.GrantedTick, item.GrantSource, item.AgreedEndTick));
            Assert.True(TownLandRightsRules.IsValidPlot(map, item.Tiles, 10, item.GrantedTick, item.AgreedEndTick));
        });
        var repeated = TownLandRightsRules.ReassignFootprintRights(map, original.Reverse().ToArray(), new HashSet<GridPoint> { center }, "new", 10);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(moved), System.Text.Json.JsonSerializer.Serialize(repeated));
        Assert.Equal("old", shared.HouseholdId);
        Assert.Equal(3, shared.Tiles.Count);
    }
}
