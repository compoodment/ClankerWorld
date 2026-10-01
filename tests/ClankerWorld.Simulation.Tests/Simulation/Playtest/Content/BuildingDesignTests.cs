using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingDesignTests
{
    [Theory]
    [InlineData("", "shelter", 4)]
    [InlineData("name", "filesystem", 4)]
    [InlineData("name", "shelter", 49)]
    public void InvalidBuildingDesignsFailBeforeCreatingContent(string name, string purpose, int woodCost) =>
        Assert.ThrowsAny<ArgumentException>(() => BuildingDesign.Create(name, purpose, woodCost));

    [Fact]
    public void ReviewRejectsUnrecognizedManifestAndOversizedName()
    {
        var original = BuildingDesign.Create("Home", "shelter", 8);
        Assert.Equal(("Home", "shelter", 8), BuildingDesign.Read(original));
        Assert.ThrowsAny<ArgumentException>(() => BuildingDesign.Create(new string('x', 97), "shelter", 8));
        Assert.ThrowsAny<ArgumentException>(() => BuildingDesign.Read(original with { PackageId = "other-package" }));
    }

}
