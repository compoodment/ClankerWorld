using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class NearbyConstructionMaterialTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MaterialReasonsKeepExactDistanceBonusesAndFiveTileCutoff(bool wrap, bool dense)
    {
        var map = Meadow(40, 40, wrap);
        var anchor = new GridPoint(wrap ? 39 : 10, 10);
        var costs = new Dictionary<GridPoint, int> { [anchor] = 100 };
        var definition = Definition();
        var empty = new TownLayoutContext(map, null, [], costs, [], []);
        Assert.True(TownLayoutService.TryEvaluateConstructionSite(empty, definition, anchor, out var plain));

        for (var distance = 0; distance <= 6; distance++)
        {
            var source = map.WrapColumn(new GridPoint(anchor.X + distance, anchor.Y));
            var resources = new List<TownLayoutResource> { new(new("closest", "construction", source, false), true) };
            if (dense)
                resources.AddRange(Enumerable.Range(0, 30).Select(index =>
                    new TownLayoutResource(new("far-" + index, "wood", new(index, 30), false), true)));
            // An unavailable closer tree and a different material never add a wood bonus.
            resources.Add(new(new("depleted", "wood", anchor, false), false));
            resources.Add(new(new("stone", "stone", anchor, false), true));
            var context = new TownLayoutContext(map, null, [], costs, resources, []);
            var candidate = Assert.Single(TownLayoutService.RankConstructionSites(context, definition));
            Assert.Equal(anchor, candidate.Position);
            Assert.Equal(plain!.Score + (distance <= 1 ? 12 : distance <= 3 ? 8 : distance <= 5 ? 4 : 0), candidate.Score);
            var reasons = candidate.Reasons.Where(reason => reason.Code == TownConstructionSiteReasonCodes.NearbyMaterial).ToArray();
            if (distance <= 5)
                Assert.Equal($"Available wood is {distance} map tile(s) away.", Assert.Single(reasons).Description);
            else
                Assert.Empty(reasons);
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void NarrowWrappedMapsUseShortestDistanceAndFreshResourceAvailability(int width, int distance)
    {
        var map = Meadow(width, 40, true);
        var anchor = new GridPoint(0, 1);
        var costs = new Dictionary<GridPoint, int> { [anchor] = 100 };
        var nearby = new TownLayoutResource(new("nearby", "wood", new(width - 1, 1), false), true);
        var far = Enumerable.Range(10, 30).Select(row =>
            new TownLayoutResource(new("far-" + row, "wood", new(0, row), false), true)).ToArray();
        var current = new TownLayoutContext(map, null, [], costs, far.Append(nearby), []);
        var candidate = Assert.Single(TownLayoutService.RankConstructionSites(current, Definition()));
        Assert.Equal($"Available wood is {distance} map tile(s) away.", Assert.Single(candidate.Reasons,
            reason => reason.Code == TownConstructionSiteReasonCodes.NearbyMaterial).Description);

        var next = new TownLayoutContext(map, null, [], costs, far.Append(nearby with { Available = false }), []);
        Assert.DoesNotContain(Assert.Single(TownLayoutService.RankConstructionSites(next, Definition())).Reasons,
            reason => reason.Code == TownConstructionSiteReasonCodes.NearbyMaterial);
    }

    private static SeededMap Meadow(int width, int height, bool wrap) => new(width, height, 0,
        (from y in Enumerable.Range(0, height)
         from x in Enumerable.Range(0, width)
         select new TerrainTile(new(x, y), TerrainKind.Meadow)).ToArray(), [], [], "fixture")
    { WrapsEastWest = wrap };

    private static BuildingDefinition Definition() => new("sha256:" + new string('a', 64), "test-house",
        ContentVersion.Parse("1.0.0"), "Test house", 1, 1, 1,
        buildCosts: [new ContentQuantity("wood", 1)]);
}
