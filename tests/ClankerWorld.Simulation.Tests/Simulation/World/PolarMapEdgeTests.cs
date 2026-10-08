using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PolarMapEdgeTests
{
    [Theory]
    [InlineData("polar-edge-balanced", WorldSizePreset.Small, true, ClimateMode.Balanced, true, ClimateZone.Temperate, 45)]
    [InlineData("polar-edge-uniform-dry", WorldSizePreset.Small, false, ClimateMode.Uniform, false, ClimateZone.Dry, 45)]
    [InlineData("polar-edge-dominant-tropical", WorldSizePreset.Small, true, ClimateMode.Dominant, false, ClimateZone.Tropical, 30)]
    [InlineData("polar-edge-little-water", WorldSizePreset.Small, false, ClimateMode.Balanced, true, ClimateZone.Temperate, 10)]
    [InlineData("polar-edge-medium", WorldSizePreset.Medium, true, ClimateMode.Balanced, true, ClimateZone.Temperate, 45)]
    public void NorthAndSouthEdgesAreUncrossablePolarSeaForEveryClimateAndSize(string seed, WorldSizePreset size,
        bool wrap, ClimateMode mode, bool latitudeCooling, ClimateZone climate, int waterPercent)
    {
        var options = new GeographyOptions(seed, size, wrap, waterPercent, mode, climate, latitudeCooling);
        var geography = GeographyGenerator.Generate(options);
        var edgeRows = Enumerable.Range(0, geography.Height)
            .Where(y => GeographyGenerator.IsPolarEdgeRow(y, geography.Height)).ToArray();
        Assert.Equal(GeographyGenerator.PolarEdgeRows * 2, edgeRows.Length);
        foreach (var y in edgeRows)
            for (var x = 0; x < geography.Width; x++)
                Assert.Equal((WaterKind.Ocean, ClimateZone.Polar), (geography.At(x, y).Water, geography.At(x, y).Climate));
        // The rows inside the edge are generated as before, so the world is not all sea.
        Assert.Contains(Enumerable.Range(0, geography.Width), x => geography.At(x, GeographyGenerator.PolarEdgeRows).Water != WaterKind.Ocean ||
            geography.At(x, geography.Height / 2).Water == WaterKind.Land);

        var map = GeneratedCampMapGenerator.Generate(options);
        foreach (var y in edgeRows)
            for (var x = 0; x < map.Width; x++)
                Assert.False(map.IsPassable(new GridPoint(x, y)), $"Edge tile ({x}, {y}) must not be walkable.");
        Assert.DoesNotContain(map.Resources, resource => GeographyGenerator.IsPolarEdgeRow(resource.Position.Y, map.Height));
        Assert.DoesNotContain(map.CampObjects, item => GeographyGenerator.IsPolarEdgeRow(item.Position.Y, map.Height));
    }
}
