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

    [Fact]
    public void TemperateGeneratedWorldHasBroadFarmableLandAndReproducibleFertility()
    {
        var options = new GeographyOptions("fertile-temperate-world", WorldSizePreset.Small,
            ClimateMode: ClimateMode.Uniform, SelectedClimate: ClimateZone.Temperate, LatitudeCooling: false);
        var map = GeneratedCampMapGenerator.Generate(options);
        var fertility = new LandFertility(map, options.Seed);
        var grass = map.Tiles.Where(tile => map.SurfaceAt(tile.Position) is SurfaceKind.Grass or SurfaceKind.ForestFloor).ToArray();
        Assert.True(grass.Length > map.Tiles.Count / 5);
        Assert.All(grass, tile => Assert.True(fertility.CanFarm(tile.Position)));
        Assert.True(grass.Count(tile => fertility.At(tile.Position) >= 55) > grass.Length / 2);
        var restored = new LandFertility(GeneratedCampMapGenerator.Generate(options), options.Seed);
        foreach (var tile in grass.Where((_, index) => index % 97 == 0))
            Assert.Equal(fertility.At(tile.Position), restored.At(tile.Position));
        Assert.All(map.Tiles.Where(tile => !map.IsBuildable(tile.Position) ||
            map.SurfaceAt(tile.Position) is SurfaceKind.Sand or SurfaceKind.Rock or SurfaceKind.Snow),
            tile => Assert.False(fertility.CanFarm(tile.Position)));
    }

    private static SeededMap Map() => new(8, 8, 0,
        Enumerable.Range(0, 64).Select(index => new TerrainTile(new(index % 8, index / 8), TerrainKind.Meadow)).ToArray(),
        [], [], "fertility-fixture")
    { WrapsEastWest = true };
}
