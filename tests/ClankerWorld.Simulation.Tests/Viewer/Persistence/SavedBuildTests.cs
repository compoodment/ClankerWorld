using ClankerWorld.Viewer.Observation;
using ClankerWorld.Simulation.Playtest;
using System.Text.Json.Nodes;

namespace ClankerWorld.Simulation.Tests;

public sealed class SavedBuildTests
{
    [Theory]
    [InlineData("GameVersion", "42")]
    [InlineData("GameVersion", "[]")]
    [InlineData("GameVersion", "{}")]
    [InlineData("SourceRevision", "42")]
    [InlineData("SourceRevision", "[]")]
    [InlineData("SourceRevision", "{}")]
    public void MalformedOptionalBuildValuesKeepNamedAndAutosavedCheckpointsReadable(string field, string malformedJson)
    {
        var directory = Directory.CreateTempSubdirectory("saved-build-metadata-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            using var runtime = new PrivateWorldRuntime("saved-build-metadata");
            runtime.Pause();
            var store = new ManualWorldSaveStore(path);
            foreach (var autosave in new[] { false, true })
            {
                var settings = new WorldAutosaveSettings(runtime.Society.WorldId, true, 5, 3, DateTimeOffset.MinValue, -1);
                var save = autosave ? store.CreateAutosave(runtime, [], settings) : store.Create("Named", runtime, []);
                var checkpointBytes = PrivateWorldRuntimeCodec.Encode(store.Read(save.Id));
                var metadataPath = Path.Combine(path + ".manual", save.Id + ".meta.json");
                var metadata = JsonNode.Parse(File.ReadAllBytes(metadataPath))!;
                metadata["Save"]![field] = JsonNode.Parse(malformedJson);
                var original = metadata.ToJsonString();
                File.WriteAllText(metadataPath, original);
                var reopened = new ManualWorldSaveStore(path);
                var listed = Assert.Single(reopened.List(runtime.Society.WorldId), item => item.Id == save.Id);
                Assert.Equal(save.Name, listed.Name);
                Assert.Null(field == "GameVersion" ? listed.GameVersion : listed.SourceRevision);
                Assert.Equal(checkpointBytes, PrivateWorldRuntimeCodec.Encode(reopened.Read(save.Id)));
                reopened.ContinueFrom(save.Id);
                using var restored = PrivateWorldRuntime.Restore(reopened.Read(save.Id));
                restored.Validate();
                Assert.Equal(original, File.ReadAllText(metadataPath));
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{not valid build metadata")]
    [InlineData("""{"GameVersion":"0.0.9-alpha.1","SourceRevision":"old"}""")]
    public void RecoveryRollbackRestoresTheBuildRecordAndSuccessfulRetryRecordsThisBuild(string? oldBuild)
    {
        var directory = Directory.CreateTempSubdirectory("saved-build-rollback-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            var damaged = "{refused checkpoint"u8.ToArray();
            File.WriteAllBytes(path, damaged);
            if (oldBuild is not null) File.WriteAllText(SavedBuild.PathFor(path), oldBuild);
            var file = new PrivateWorldStateFile(path);
            var preserved = file.PreserveDamagedCheckpoint(damaged);
            using var recovered = new PrivateWorldRuntime("saved-build-rollback");
            recovered.Pause();
            file.Save(recovered);
            Assert.Equal(BuildInformation.Version, SavedBuild.TryRead(path)?.GameVersion);
            file.RestorePreservedCheckpoint(preserved);
            Assert.Equal(damaged, File.ReadAllBytes(path));
            if (oldBuild is null) Assert.False(File.Exists(SavedBuild.PathFor(path)));
            else Assert.Equal(oldBuild, File.ReadAllText(SavedBuild.PathFor(path)));
            Assert.Equal(damaged, File.ReadAllBytes(preserved.PreservedPath));
            file.Save(recovered);
            Assert.Equal(BuildInformation.Version, SavedBuild.TryRead(path)?.GameVersion);
            using var reloaded = file.LoadOrCreate("saved-build-rollback");
            reloaded.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

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
