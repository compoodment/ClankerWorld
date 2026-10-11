using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class DeletionRecoveryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WholeWorldDeletionPreservesUnreadableSnapshotsOfAnotherWorld(bool overwritten, bool oldSchema)
    {
        var directory = Directory.CreateTempSubdirectory("delete-with-unreadable-foreign-save-");
        try
        {
            var path = Path.Combine(directory.FullName, "active.save");
            using var active = new PrivateWorldRuntime("delete-keep-unreadable-world");
            using var target = new PrivateWorldRuntime("delete-target-with-unreadable-neighbor");
            active.Pause();
            target.Pause();
            var settings = new WorldAutosaveSettings(active.Society.WorldId, true, 5, 5, DateTimeOffset.UtcNow, 0);
            var catalog = new WorldCatalogStore(path, active.ExportState(), [], settings);
            var entry = catalog.Add("Delete", target.ExportState());
            var saves = new ManualWorldSaveStore(path);
            var keep = saves.Create("Keep", active, []);
            var unreadable = saves.Create("Unreadable", active, []);
            if (overwritten) saves.Overwrite(unreadable.Id, active, []);
            var selected = saves.Create("Delete", target, []);
            saves.Overwrite(selected.Id, target, []);
            var manualDirectory = path + ".manual";
            var unreadablePath = overwritten
                ? Assert.Single(Directory.GetFiles(manualDirectory, unreadable.Id + ".*.save"))
                : Path.Combine(manualDirectory, unreadable.Id + ".save");
            if (oldSchema)
            {
                var document = JsonNode.Parse(File.ReadAllBytes(unreadablePath))!;
                document["state"]!["schemaVersion"] = 0;
                File.WriteAllText(unreadablePath, document.ToJsonString());
            }
            else File.WriteAllText(unreadablePath, "damaged checkpoint");
            var unreadableBytes = File.ReadAllBytes(unreadablePath);
            var unreadableMetadataPath = Path.Combine(manualDirectory, unreadable.Id + ".meta.json");
            var unreadableMetadata = File.ReadAllBytes(unreadableMetadataPath);
            var keepPath = Path.Combine(manualDirectory, keep.Id + ".save");
            var keepBytes = File.ReadAllBytes(keepPath);
            var activeSnapshotPath = Path.Combine(path + ".worlds", catalog.Active().Id + ".save");
            var activeBytes = File.ReadAllBytes(activeSnapshotPath);
            // An unpublished generation of the target still needs verified cleanup.
            var orphanPath = Path.Combine(manualDirectory,
                Guid.NewGuid().ToString("N") + "." + Guid.NewGuid().ToString("N") + ".save");
            File.WriteAllBytes(orphanPath, PrivateWorldRuntimeCodec.Encode(target.ExportState()));

            catalog.Delete(entry.Id, target.Society.WorldId, saves.DeleteWorldSnapshots);

            Assert.False(File.Exists(Path.Combine(path + ".worlds", entry.Id + ".save")));
            Assert.False(File.Exists(orphanPath));
            Assert.Empty(saves.List(target.Society.WorldId));
            Assert.Equal(unreadableBytes, File.ReadAllBytes(unreadablePath));
            Assert.Equal(unreadableMetadata, File.ReadAllBytes(unreadableMetadataPath));
            Assert.Equal(keepBytes, File.ReadAllBytes(keepPath));
            Assert.Equal(activeBytes, File.ReadAllBytes(activeSnapshotPath));
            Assert.Equal(active.Society.WorldId, saves.Read(keep.Id).Society.Society.WorldId);
            var reopened = new WorldCatalogStore(path, active.ExportState(), [], settings);
            Assert.Single(reopened.Capture().Worlds);
            Assert.Throws<FileNotFoundException>(() => reopened.Read(entry.Id));
            // No deletion intent remains to retry against the unreadable save.
            reopened.RecoverDeletions(new ManualWorldSaveStore(path).DeleteWorldSnapshots);
        }
        finally { directory.Delete(recursive: true); }
    }

    [WindowsSaveFact]
    public void LockedCheckpointRemainsHiddenUntilDeletionRecoveryCompletes()
    {
        var directory = Directory.CreateTempSubdirectory("deletion-lock-");
        try
        {
            var path = Path.Combine(directory.FullName, "active.save");
            using var runtime = new PrivateWorldRuntime("delete-locked");
            runtime.Pause();
            var saves = new ManualWorldSaveStore(path);
            var save = saves.Create("Locked", runtime, []);
            using (var locked = new FileStream(Path.Combine(path + ".manual", save.Id + ".save"),
                FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Record.Exception(() => saves.Delete(save.Id, runtime.Society.WorldId, save.CreatedUtc));
                Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Expected native sharing failure.");
                Assert.Empty(saves.List());
                Assert.Throws<FileNotFoundException>(() => saves.Read(save.Id));
            }
            var reopened = new ManualWorldSaveStore(path);
            reopened.RecoverDeletions();
            // Only the world's own branch record remains; the world itself still exists.
            var remaining = Assert.Single(Directory.GetFiles(path + ".manual"));
            Assert.StartsWith("timeline-", Path.GetFileName(remaining), StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void WholeWorldDeletionRemovesSnapshotsAndGenerationsButPreservesOtherWorld()
    {
        var directory = Directory.CreateTempSubdirectory("delete-world-files-");
        try
        {
            var path = Path.Combine(directory.FullName, "active.save");
            using var active = new PrivateWorldRuntime("delete-keep-world");
            using var target = new PrivateWorldRuntime("delete-target-world");
            active.Pause();
            target.Pause();
            var settings = new WorldAutosaveSettings(active.Society.WorldId, true, 5, 5, DateTimeOffset.UtcNow, 0);
            var catalog = new WorldCatalogStore(path, active.ExportState(), [], settings);
            var entry = catalog.Add("Delete", target.ExportState());
            var saves = new ManualWorldSaveStore(path);
            var keep = saves.Create("Keep", active, []);
            var manual = saves.Create("Delete", target, []);
            saves.Overwrite(manual.Id, target, []);
            saves.CreateAutosave(target, [], settings with { WorldId = target.Society.WorldId });
            var keepBytes = PrivateWorldRuntimeCodec.Encode(saves.Read(keep.Id));
            Assert.Throws<InvalidOperationException>(() => catalog.Delete(entry.Id, active.Society.WorldId, saves.DeleteWorldSnapshots));
            catalog.Delete(entry.Id, target.Society.WorldId, saves.DeleteWorldSnapshots);
            Assert.Single(catalog.Capture().Worlds);
            Assert.Empty(saves.List(target.Society.WorldId));
            Assert.Equal(keepBytes, PrivateWorldRuntimeCodec.Encode(saves.Read(keep.Id)));
            // The kept world's save, its metadata and its own branch record remain.
            Assert.Equal(3, Directory.GetFiles(path + ".manual").Length);
            Assert.Single(Directory.GetFiles(path + ".manual", "timeline-*.json"));
            var reopened = new WorldCatalogStore(path, active.ExportState(), [], settings);
            Assert.Single(reopened.Capture().Worlds);
            Assert.Throws<FileNotFoundException>(() => reopened.Read(entry.Id));
        }
        finally { directory.Delete(recursive: true); }
    }
}
