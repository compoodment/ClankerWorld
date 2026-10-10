using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
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

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void FertilityUsesTheSelectedCandidatesActualRainfallAndSurvivesReload(int attempt, bool wrapped)
    {
        var options = new GeographyOptions("fertility-candidate-rainfall", WorldSizePreset.Small,
            WrapEastWest: wrapped, ClimateMode: ClimateMode.Uniform,
            SelectedClimate: ClimateZone.Temperate, LatitudeCooling: false,
            HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion, CandidateAttempt: attempt);
        var geography = GeographyGenerator.Generate(options);
        var map = GeographyCandidateSelector.GenerateCandidate(options);
        using var world = PrivateWorldRuntime.CreateFromGeneratedGeography(options.Seed, options, map);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var fertility = new LandFertility(map, options.Seed);
        var restoredFertility = new LandFertility(restored.ExportState().Map, options.Seed);
        var checkedTiles = 0;
        foreach (var tile in map.Tiles)
        {
            var point = tile.Position;
            if (!map.IsBuildable(point) || map.SurfaceAt(point) is not (SurfaceKind.Grass or SurfaceKind.ForestFloor) ||
                map.ClimateAt(point) != ClimateZone.Temperate)
                continue;
            // Away from fresh water the agreed score is the base 40 plus
            // one sixth of the generator's actual rainfall byte.
            var nearWater = false;
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    var neighbor = new GridPoint(point.X + dx, point.Y + dy);
                    if (wrapped) neighbor = map.WrapColumn(neighbor);
                    if (map.Contains(neighbor) && map.HydrologyAt(neighbor) is WaterKind.River or WaterKind.Lake)
                        nearWater = true;
                }
            if (nearWater) continue;
            var expected = 40 + geography.At(point.X, point.Y).Rainfall / 6;
            Assert.Equal(expected, fertility.At(point));
            Assert.Equal(expected, restoredFertility.At(point));
            checkedTiles++;
        }
        Assert.True(checkedTiles > 100, $"Only {checkedTiles} suitable land tiles were checked.");
        Assert.Equal(attempt, restored.ExportState().Map.GenerationAttempt);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void LegacyFixtureAttemptsKeepTheirOriginalRainfallSeed()
    {
        var original = Map();
        var retry = original with { GenerationAttempt = 2 };
        var before = new LandFertility(original, "fertility-seam");
        var after = new LandFertility(retry, "fertility-seam");
        foreach (var tile in original.Tiles) Assert.Equal(before.At(tile.Position), after.At(tile.Position));
    }

    private static SeededMap Map() => new(8, 8, 0,
        Enumerable.Range(0, 64).Select(index => new TerrainTile(new(index % 8, index / 8), TerrainKind.Meadow)).ToArray(),
        [], [], "fertility-fixture")
    { WrapsEastWest = true };
}
