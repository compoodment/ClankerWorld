using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class DeletionRecoveryTests
{
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
            Assert.Empty(Directory.GetFiles(path + ".manual"));
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
            Assert.Equal(2, Directory.GetFiles(path + ".manual").Length);
            var reopened = new WorldCatalogStore(path, active.ExportState(), [], settings);
            Assert.Single(reopened.Capture().Worlds);
            Assert.Throws<FileNotFoundException>(() => reopened.Read(entry.Id));
        }
        finally { directory.Delete(recursive: true); }
    }
}
