using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class RecoveryCleanupDeletionTests
{
    [Theory]
    [InlineData("history")]
    [InlineData("schema")]
    [InlineData("identity")]
    public void CleanupDeletionPreservesUnverifiableRecoveryAndItsLatestVerifiedPredecessor(string damage)
    {
        var directory = Directory.CreateTempSubdirectory("recovery-verify-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            using var runtime = new PrivateWorldRuntime("recovery-verify");
            runtime.Pause();
            var store = new ManualWorldSaveStore(path);
            var named = store.Create("Named", runtime, []);
            var old = store.Overwrite(named.Id, runtime, []).BackupId;
            var valid = store.Overwrite(named.Id, runtime, []).BackupId;
            var unverified = store.Overwrite(named.Id, runtime, []).BackupId;
            var target = Path.Combine(path + ".manual", unverified + ".save");
            if (damage == "history")
                File.WriteAllBytes(target, PrivateWorldRuntimeCodec.Encode(store.Read(unverified) with
                { HistoryArchiveHead = new string('c', 64) }));
            else if (damage == "schema")
            {
                var document = JsonNode.Parse(File.ReadAllBytes(target))!;
                document["state"]!["schemaVersion"] = 0;
                File.WriteAllText(target, document.ToJsonString());
            }
            else
            {
                using var other = new PrivateWorldRuntime("another-recovery-world");
                File.WriteAllBytes(target, PrivateWorldRuntimeCodec.Encode(other.ExportState()));
            }
            var evidence = File.ReadAllBytes(target);
            var stateFile = new PrivateWorldStateFile(path);
            var preview = store.PreviewRecoveryCleanup(runtime.Society.WorldId, 1, stateFile);
            Assert.Equal(old, Assert.Single(preview.Remove).Id);
            Assert.Contains(preview.Keep, save => save.Id == valid);
            Assert.Contains(preview.Keep, save => save.Id == unverified);
            store.CleanRecoveryHistory(preview.WorldId, 1, preview.Digest, stateFile);
            Assert.Equal(evidence, File.ReadAllBytes(target));
            Assert.NotNull(store.Read(valid));
            Assert.NotNull(store.Read(named.Id));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void CleanupDeletionKeepsVerifiedLatestPerSourceAndUnclassifiedOrDamagedCopies()
    {
        var directory = Directory.CreateTempSubdirectory("recovery-preserve-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            using var runtime = new PrivateWorldRuntime("recovery-preserve");
            runtime.Pause();
            var stateFile = new PrivateWorldStateFile(path);
            var store = new ManualWorldSaveStore(path);
            var first = store.Create("First", runtime, []);
            var second = store.Create("Second", runtime, []);
            var firstOld = store.Overwrite(first.Id, runtime, []).BackupId;
            var firstLatest = store.Overwrite(first.Id, runtime, []).BackupId;
            var damaged = store.Overwrite(first.Id, runtime, []).BackupId;
            var legacy = store.Overwrite(first.Id, runtime, []).BackupId;
            var secondOld = store.Overwrite(second.Id, runtime, []).BackupId;
            var secondLatest = store.Overwrite(second.Id, runtime, []).BackupId;
            var metadataPath = Path.Combine(path + ".manual", legacy + ".meta.json");
            var metadata = JsonNode.Parse(File.ReadAllBytes(metadataPath))!;
            metadata.AsObject().Remove("Recovery");
            File.WriteAllText(metadataPath, metadata.ToJsonString());
            File.WriteAllText(Path.Combine(path + ".manual", damaged + ".save"), "damaged checkpoint");
            // Dates can move backwards; sequence determines the latest recovery.
            var latestPath = Path.Combine(path + ".manual", firstLatest + ".meta.json");
            var latestMetadata = JsonNode.Parse(File.ReadAllBytes(latestPath))!;
            latestMetadata["Save"]!["CreatedUtc"] = DateTimeOffset.UnixEpoch;
            File.WriteAllText(latestPath, latestMetadata.ToJsonString());
            var reopened = new ManualWorldSaveStore(path);
            var preview = reopened.PreviewRecoveryCleanup(runtime.Society.WorldId, 1, stateFile);
            Assert.Equal(new[] { firstOld, secondOld }.Order(StringComparer.Ordinal), preview.Remove.Select(save => save.Id));
            foreach (var id in new[] { first.Id, second.Id, firstLatest, secondLatest, damaged, legacy })
                Assert.Contains(preview.Keep, save => save.Id == id);
            // Loading an old recovery protects it even if it exceeds the count.
            reopened.ContinueFrom(firstOld);
            var active = reopened.PreviewRecoveryCleanup(runtime.Society.WorldId, 1, stateFile);
            Assert.DoesNotContain(active.Remove, save => save.Id == firstOld);
            Assert.Contains(active.Keep, save => save.Id == firstLatest);
            Assert.Throws<InvalidOperationException>(() => reopened.CleanRecoveryHistory(preview.WorldId, 1, preview.Digest, stateFile));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void BeforeLoadCopiesHaveNamedProvenanceWhileAnExplicitOverwritePinsARecovery()
    {
        var directory = Directory.CreateTempSubdirectory("recovery-load-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            using var runtime = new PrivateWorldRuntime("recovery-load");
            runtime.Pause();
            var store = new ManualWorldSaveStore(path);
            var named = store.Create("Named", runtime, []);
            var copies = new List<ManualWorldSave>();
            for (var index = 0; index < 3; index++)
            {
                store.ContinueFrom(named.Id);
                copies.Add(store.CreateLoadRecovery(runtime, [], null));
            }
            var stateFile = new PrivateWorldStateFile(path);
            var preview = store.PreviewRecoveryCleanup(runtime.Society.WorldId, 1, stateFile);
            Assert.Equal(2, preview.Remove.Count);
            Assert.Contains(preview.Keep, save => save.Id == copies[2].Id);
            store.Overwrite(copies[0].Id, runtime, []);
            var pinned = store.PreviewRecoveryCleanup(runtime.Society.WorldId, 1, stateFile);
            Assert.DoesNotContain(pinned.Remove, save => save.Id == copies[0].Id);
            Assert.Contains(pinned.Keep, save => save.Id == copies[0].Id);
        }
        finally { directory.Delete(recursive: true); }
    }
}
