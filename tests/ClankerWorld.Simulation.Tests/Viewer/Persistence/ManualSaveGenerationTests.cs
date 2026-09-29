using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ManualSaveGenerationTests
{
    [Fact]
    public void RepeatedOverwriteSelectsWholeGenerationsAndPreservesRecoveryPairs()
    {
        var directory = Directory.CreateTempSubdirectory("save-generation-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            using var runtime = new PrivateWorldRuntime("save-generations");
            runtime.Pause();
            var store = new ManualWorldSaveStore(path);
            var oldSettings = new WorldAutosaveSettings(runtime.Society.WorldId, true, 5, 5, DateTimeOffset.UtcNow, 0);
            var oldAssignments = new InhabitantProviderAssignment[] { new("founder-scout", "planning", "openai", "old-model") };
            var save = store.Create("Selected", runtime, oldAssignments, oldSettings);
            var original = PrivateWorldRuntimeCodec.Encode(store.Read(save.Id));
            runtime.SetJevEnabled(false);
            var newSettings = oldSettings with { Enabled = false, IntervalMinutes = 1 };
            var newAssignments = new InhabitantProviderAssignment[] { oldAssignments[0] with { Model = "new-model" } };
            var first = store.Overwrite(save.Id, runtime, newAssignments, newSettings);
            var reopened = new ManualWorldSaveStore(path);
            var committed = reopened.ReadCommitted(save.Id);
            Assert.False(committed.Checkpoint.JevEnabled);
            Assert.Equal(newAssignments, committed.Assignments);
            Assert.Equal(newSettings, committed.AutosaveSettings);
            Assert.Equal(original, PrivateWorldRuntimeCodec.Encode(reopened.Read(first.BackupId)));
            Assert.Equal(oldAssignments, reopened.ReadAssignments(first.BackupId));
            runtime.SetJevEnabled(true);
            var second = reopened.Overwrite(save.Id, runtime, oldAssignments, oldSettings);
            Assert.False(reopened.Read(second.BackupId).JevEnabled);
            Assert.Equal(newAssignments, reopened.ReadAssignments(second.BackupId));
            Assert.Equal(newSettings, reopened.ReadAutosaveSettings(second.BackupId));
            Assert.True(reopened.Read(save.Id).JevEnabled);
            Assert.Equal(3, reopened.List().Count);
        }
        finally { directory.Delete(recursive: true); }
    }

    [WindowsSaveFact]
    public void LockedMetadataCannotPublishHalfAnOverwrite()
    {
        var directory = Directory.CreateTempSubdirectory("save-generation-lock-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            using var runtime = new PrivateWorldRuntime("save-generation-lock");
            runtime.Pause();
            var store = new ManualWorldSaveStore(path);
            var settings = new WorldAutosaveSettings(runtime.Society.WorldId, true, 5, 5, DateTimeOffset.UtcNow, 0);
            var assignments = new InhabitantProviderAssignment[] { new("founder-scout", "planning", "openai", "old-model") };
            var save = store.Create("Selected", runtime, assignments, settings);
            var original = PrivateWorldRuntimeCodec.Encode(store.Read(save.Id));
            runtime.SetJevEnabled(false);
            using (var locked = new FileStream(Path.Combine(path + ".manual", save.Id + ".meta.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = Record.Exception(() => store.Overwrite(save.Id, runtime,
                    [assignments[0] with { Model = "new-model" }], settings with { Enabled = false }));
                Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "Expected metadata replacement to fail.");
            }
            var reopened = new ManualWorldSaveStore(path);
            Assert.Equal(original, PrivateWorldRuntimeCodec.Encode(reopened.Read(save.Id)));
            Assert.Equal(assignments, reopened.ReadAssignments(save.Id));
            Assert.Equal(settings, reopened.ReadAutosaveSettings(save.Id));
            Assert.Equal(2, reopened.List().Count);
            using var restored = PrivateWorldRuntime.Restore(reopened.Read(save.Id));
            Assert.True(restored.JevEnabled);
            var retry = reopened.Overwrite(save.Id, runtime, [assignments[0] with { Model = "new-model" }], settings with { Enabled = false });
            Assert.False(reopened.Read(retry.Saved.Id).JevEnabled);
        }
        finally { directory.Delete(recursive: true); }
    }
}

public sealed class WindowsSaveFactAttribute : FactAttribute
{
    public WindowsSaveFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires native Windows file-sharing semantics.";
    }
}
