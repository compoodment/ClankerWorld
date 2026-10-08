using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class CampReachabilityReuseTests
{
    [Fact]
    public void RepeatedMapAcceptanceAvoidsAllocatingAnotherCampTraversal()
    {
        var map = OpenMap(64, 16);
        for (var warmup = 0; warmup < 3; warmup++)
            Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
        var beforeTraversal = GC.GetAllocatedBytesForCurrentThread();
        var reachable = MapAcceptance.ReachableFrom(map, new(1, 1));
        var traversalBytes = GC.GetAllocatedBytesForCurrentThread() - beforeTraversal;
        Assert.Equal(map.Tiles.Count, reachable.Count);
        long warmBytes = 0;
        long coldBytes = 0;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var warm = MapAcceptance.Validate(map, allowEmptyCamp: true);
            warmBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(warm.IsValid, warm.Failure);
            var fresh = map with { };
            before = GC.GetAllocatedBytesForCurrentThread();
            var cold = MapAcceptance.Validate(fresh, allowEmptyCamp: true);
            coldBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(cold.IsValid, cold.Failure);
        }
        Assert.True(coldBytes - warmBytes >= 5 * traversalBytes * 3 / 4,
            $"Five unchanged validations allocated {warmBytes}; fresh maps allocated {coldBytes}; one traversal allocated {traversalBytes}.");
    }

    [Theory]
    [InlineData("hydrology")]
    [InlineData("elevation")]
    [InlineData("surface")]
    public void BorrowedLayerEditsCannotReuseAnOutdatedCampRoute(string layer)
    {
        var original = OpenMap(7, 3);
        var map = original with
        {
            HydrologyKinds = layer == "surface" ? null : new byte[21],
            ElevationLevels = new byte[21],
            SurfaceKinds = new byte[21],
        };
        Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
        for (var row = 0; row < map.Height; row++)
        {
            var index = row * map.Width + 3;
            if (layer == "hydrology") map.HydrologyKinds![index] = (byte)WaterKind.Ocean;
            else if (layer == "elevation") map.ElevationLevels[index] = 255;
            else map.SurfaceKinds[index] = (byte)SurfaceKind.Water;
        }
        var result = MapAcceptance.Validate(map, allowEmptyCamp: true);
        Assert.False(result.IsValid);
        Assert.Contains("route", result.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public void BorrowedBridgeAxisEditsCannotReuseAnOutdatedCampRoute()
    {
        var original = OpenMap(7, 3);
        var tiles = original.Tiles.Select(tile => tile.Position.X == 3
            ? tile with { Terrain = TerrainKind.River } : tile).ToArray();
        var decks = Enumerable.Range(0, 3).ToDictionary(row => new GridPoint(3, row), _ => BridgeAxis.EastWest);
        var map = original with { Tiles = tiles, BridgeDecks = decks };
        map = map with { ManifestDigest = MapManifestCodec.Digest(map) };
        Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
        foreach (var point in decks.Keys.ToArray()) decks[point] = BridgeAxis.NorthSouth;
        var result = MapAcceptance.Validate(map, allowEmptyCamp: true);
        Assert.False(result.IsValid);
        Assert.Contains("route", result.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingEastWestWrappingRechecksRoutesAcrossTheSeam()
    {
        var original = OpenMap(7, 3);
        var map = original with
        {
            Tiles = original.Tiles.Select(tile => tile.Position.X == 3
                ? tile with { Terrain = TerrainKind.Ocean } : tile).ToArray(),
            WrapsEastWest = true,
        };
        map = map with { ManifestDigest = MapManifestCodec.Digest(map) };
        Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
        var result = MapAcceptance.Validate(map with { WrapsEastWest = false }, allowEmptyCamp: true);
        Assert.False(result.IsValid);
        Assert.Contains("route", result.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public void BorrowedTileEditsAreCheckedBeforeReusingCampRoutes()
    {
        var map = OpenMap(7, 3);
        var tiles = Assert.IsType<TerrainTile[]>(map.Tiles);
        Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
        for (var row = 0; row < map.Height; row++)
            tiles[row * map.Width + 3] = new(new(3, row), TerrainKind.Ocean);
        var result = MapAcceptance.Validate(map, allowEmptyCamp: true);
        Assert.False(result.IsValid);
        Assert.Contains("route", result.Failure!, StringComparison.Ordinal);
    }

    private static SeededMap OpenMap(int width, int height)
    {
        var tiles = Enumerable.Range(0, width * height)
            .Select(index => new TerrainTile(new(index % width, index / width), TerrainKind.Meadow)).ToArray();
        var map = new SeededMap(width, height, 0, tiles, [],
            [new("berry-patch", "food", new(1, 1), true),
             new("timber-tree", "construction", new(width - 2, height - 2), true)], "");
        return map with { ManifestDigest = MapManifestCodec.Digest(map) };
    }
}
