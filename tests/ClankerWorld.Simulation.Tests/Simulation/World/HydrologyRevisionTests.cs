using System.Text;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class HydrologyRevisionTests
{
    [Theory]
    [InlineData(0)]
    public void SavedMapRetainsItsHydrologyLineage(int version)
    {
        var geography = new GeographyOptions("hydrology-save", WorldSizePreset.Small,
            HydrologyVersion: version);
        using var world = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: geography);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        if (version == 0)
            Assert.DoesNotContain("hydrologyVersion", Encoding.UTF8.GetString(before), StringComparison.OrdinalIgnoreCase);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before));
        Assert.Equal(version, restored.ExportState().Geography!.HydrologyVersion);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void UnknownHydrologyVersionIsRejectedInsteadOfSubstitutingAnotherMap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(
            new GeographyOptions("unsupported", WorldSizePreset.Small, HydrologyVersion: 2)));
    }
}
