using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class AdvancedGenerationTests
{
    [Fact]
    public void LowAndHighControlsChangeTheirOwnGeneratedLayers()
    {
        var options = new GeographyOptions("advanced-layer-controls", WorldSizePreset.Small,
            WaterPercent: 50, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
        var lowRiver = GeographyGenerator.Generate(options with { RiverAbundance = GenerationAmount.Low });
        var highRiver = GeographyGenerator.Generate(options with { RiverAbundance = GenerationAmount.High });
        Assert.True(highRiver.Count(WaterKind.River) > lowRiver.Count(WaterKind.River));
        Assert.Equal(lowRiver.Count(WaterKind.Lake) + lowRiver.Count(WaterKind.Ocean), highRiver.Count(WaterKind.Lake) + highRiver.Count(WaterKind.Ocean));
        var lowMountain = GeographyGenerator.Generate(options with { MountainRelief = GenerationAmount.Low });
        var highMountain = GeographyGenerator.Generate(options with { MountainRelief = GenerationAmount.High });
        var lowPeaks = 0;
        var highPeaks = 0;
        for (var y = 0; y < lowMountain.Height; y++)
            for (var x = 0; x < lowMountain.Width; x++)
            {
                if (lowMountain.At(x, y).Elevation >= SeededMap.MountainElevationThreshold) lowPeaks++;
                if (highMountain.At(x, y).Elevation >= SeededMap.MountainElevationThreshold) highPeaks++;
            }
        Assert.True(highPeaks > lowPeaks);
        var lowForest = GeneratedCampMapGenerator.Generate(options with { ForestCover = GenerationAmount.Low });
        var highForest = GeneratedCampMapGenerator.Generate(options with { ForestCover = GenerationAmount.High });
        Assert.True(highForest.VegetationKinds!.Count(value => value == (byte)VegetationCover.Forest) >
            lowForest.VegetationKinds!.Count(value => value == (byte)VegetationCover.Forest));
        Assert.Equal(lowForest.HydrologyKinds, highForest.HydrologyKinds);
    }

    [Theory]
    [InlineData(WorldSizePreset.Small, GenerationAmount.Low, 20)]
    [InlineData(WorldSizePreset.Medium, GenerationAmount.High, 80)]
    public void AdvancedOptionsSurviveExactSaveRestore(WorldSizePreset size, GenerationAmount amount, int water)
    {
        var options = new GeographyOptions("advanced-save", size, WaterPercent: water, LatitudeCooling: false,
            HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion, ForestCover: amount, MountainRelief: amount, RiverAbundance: amount);
        using var original = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var bytes = PrivateWorldRuntimeCodec.Encode(original.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(options, restored.ExportState().Geography);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
