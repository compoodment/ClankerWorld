using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SavedBuildTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"GameVersion":"","SourceRevision":"abc"}""")]
    public void AMissingOrUnusableBuildRecordReadsAsUnknown(string? contents)
    {
        var checkpoint = Path.Combine(Path.GetTempPath(), $"clankerworld-saved-build-{Guid.NewGuid():N}.json");
        try
        {
            if (contents is not null) File.WriteAllText(SavedBuild.PathFor(checkpoint), contents);
            Assert.Null(SavedBuild.TryRead(checkpoint));
            Assert.True(SavedBuild.TryWrite(checkpoint));
            Assert.False(string.IsNullOrEmpty(SavedBuild.TryRead(checkpoint)?.GameVersion));
        }
        finally
        {
            File.Delete(SavedBuild.PathFor(checkpoint));
        }
    }
}
