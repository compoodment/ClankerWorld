using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class LandFertilityTests
{
    [Fact]
    public void FreshWaterImprovesNearbyLandAcrossEastWestSeamWithoutWrappingNorthSouth()
    {
        var plain = Map();
        var river = plain with
        {
            Tiles = plain.Tiles.Select(tile => tile.Position == new GridPoint(7, 3)
                ? tile with { Terrain = TerrainKind.River } : tile).ToArray(),
        };
        var before = new LandFertility(plain, "fertility-seam");
        var after = new LandFertility(river, "fertility-seam");
        Assert.Equal(before.At(new(0, 3)) + 15, after.At(new(0, 3)));
        Assert.Equal(before.At(new(0, 0)), after.At(new(0, 0)));
        Assert.False(after.CanFarm(new(7, 3)));
        var ocean = river with
        {
            Tiles = river.Tiles.Select(tile => tile.Terrain == TerrainKind.River
            ? tile with { Terrain = TerrainKind.Ocean } : tile).ToArray()
        };
        Assert.Equal(before.At(new(0, 3)), new LandFertility(ocean, "fertility-seam").At(new(0, 3)));
    }

    private static SeededMap Map() => new(8, 8, 0,
        Enumerable.Range(0, 64).Select(index => new TerrainTile(new(index % 8, index / 8), TerrainKind.Meadow)).ToArray(),
        [], [], "fertility-fixture")
    { WrapsEastWest = true };
}
