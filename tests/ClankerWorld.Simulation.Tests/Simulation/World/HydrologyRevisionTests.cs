using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class HydrologyRevisionTests
{
    [Fact]
    public void UnknownHydrologyVersionIsRejectedInsteadOfSubstitutingAnotherMap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(
            new GeographyOptions("unsupported", WorldSizePreset.Small, HydrologyVersion: 2)));
    }
}
