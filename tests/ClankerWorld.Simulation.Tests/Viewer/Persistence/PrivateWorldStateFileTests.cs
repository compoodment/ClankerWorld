using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldStateFileTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EquivalentTicksKeepEveryHotHistoryBoundedAcrossRepeatedCompaction()
    {
        using var evidence = new CheckpointIoTestEvidence("clankerworld-history-soak-", output.WriteLine);
        var file = new PrivateWorldStateFile(evidence.CheckpointPath);
        using var world = evidence.Observe("initial-create", null, () => file.LoadOrCreate("history-soak"));
        for (var tick = 0; tick < 2500; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            evidence.Observe("save", world.WorldTick, () => file.Save(world));
        }
        var state = world.ExportState();
        Assert.InRange(state.Events.Count, 1, PrivateWorldHistory.CompactionThreshold);
        Assert.InRange(state.Society.Society.Events.Count, 0, PrivateWorldHistory.CompactionThreshold);
        Assert.InRange(state.Society.Society.Inventory.Events.Count, 0, PrivateWorldHistory.CompactionThreshold);
        Assert.InRange(state.Society.Cognition.Events.Count, 0, PrivateWorldHistory.CompactionThreshold);
        Assert.All(state.Society.Cognition.Runtimes, runtime => Assert.InRange(runtime.Events.Count, 0, PrivateWorldHistory.CompactionThreshold));
        Assert.True(state.EventHistoryFloor > 0);
        var recent = new OwnerWorldObservationStore(world).GetReconnectBaseline(state.Events[^2].EventId);
        Assert.False(recent.Events.ResetRequired);
        Assert.Single(recent.Events.Events);
        using var restored = evidence.Observe("strict-reload", world.WorldTick, () => file.LoadOrCreate("history-soak"));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        evidence.Complete();
    }

    [WindowsSaveFact]
    public Task WindowsReaderWithoutDeleteSharingPreservesCheckpointUntilReleased() =>
        BlockedWindowsCheckpointPreservesBytesAndRecovers(readOnly: false);

    [WindowsSaveFact]
    public Task WindowsReaderAllowingDeleteSharingPreservesCheckpointUntilReleased() =>
        BlockedWindowsCheckpointPreservesBytesAndRecovers(readOnly: false, allowDeleteSharing: true);

    [WindowsSaveFact]
    public Task WindowsReadOnlyCheckpointPreservesBytesUntilAttributeIsRemoved() =>
        BlockedWindowsCheckpointPreservesBytesAndRecovers(readOnly: true);

    private async Task BlockedWindowsCheckpointPreservesBytesAndRecovers(bool readOnly, bool allowDeleteSharing = false)
    {
        using var evidence = new CheckpointIoTestEvidence("clankerworld-checkpoint-blocked-", output.WriteLine,
            expectedFailureControl: true);
        var file = new PrivateWorldStateFile(evidence.CheckpointPath);
        using var world = evidence.Observe("initial-create", null, () => file.LoadOrCreate("checkpoint-blocked"));
        var previousBytes = File.ReadAllBytes(file.Path);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var unsavedBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.NotEqual(previousBytes, unsavedBytes);
        var originalAttributes = File.GetAttributes(file.Path);
        var sharing = FileShare.Read | (allowDeleteSharing ? FileShare.Delete : FileShare.None);
        using (var reader = readOnly ? null : new FileStream(file.Path, FileMode.Open, FileAccess.Read, sharing))
        {
            try
            {
                if (readOnly) File.SetAttributes(file.Path, originalAttributes | FileAttributes.ReadOnly);
                var operation = readOnly ? "read-only-destination" : allowDeleteSharing ? "reader-allows-delete" : "reader-denies-delete";
                var failure = Record.Exception(() => evidence.Observe(operation, world.WorldTick, () => file.Save(world)));
                Assert.NotNull(failure);
                Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "Expected native replacement refusal.");
                Assert.Same(failure, evidence.FirstChanceFailure);
                Assert.Same(failure, evidence.EscapingFailure);
                // MoveFileEx replacement refuses an open destination even when the reader
                // shares deletion. Native Windows controls report access denied in all three cases.
                Assert.Equal(unchecked((int)0x80070005), failure!.HResult);
                Assert.Equal(previousBytes, File.ReadAllBytes(file.Path));
                Assert.Equal(unsavedBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                using var prior = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(previousBytes));
                prior.Validate();
                Assert.Empty(Directory.GetFiles(evidence.DirectoryPath, ".world.json.*.tmp"));
            }
            finally { if (readOnly) File.SetAttributes(file.Path, originalAttributes); }
        }
        evidence.Observe("save-after-release", world.WorldTick, () => file.Save(world));
        using var restored = evidence.Observe("strict-reload", world.WorldTick, () => file.LoadOrCreate("checkpoint-blocked"));
        Assert.Equal(unsavedBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        evidence.Complete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckpointFailureCapturePreservesTheOriginalExceptionEvenWhenArtifactsCannotBeWritten(bool blockArtifacts)
    {
        var artifacts = Directory.CreateTempSubdirectory("clankerworld-checkpoint-evidence-");
        try
        {
            var outputPath = Path.Combine(artifacts.FullName, "output");
            if (blockArtifacts) File.WriteAllText(outputPath, "A file deliberately blocks the diagnostic directory.");
            using var evidence = new CheckpointIoTestEvidence("clankerworld-checkpoint-obstructed-", output.WriteLine, outputPath,
                expectedFailureControl: true);
            var file = new PrivateWorldStateFile(evidence.CheckpointPath);
            using var world = evidence.Observe("initial-create", null, () => file.LoadOrCreate("checkpoint-obstructed"));
            var previousBytes = File.ReadAllBytes(file.Path);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var unsavedBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            // An earlier operation can handle an I/O exception successfully. Its first-chance
            // evidence must not suppress the subsequent checkpoint failure.
            evidence.Observe("handled-missing-file", world.WorldTick, () =>
            {
                try { using var missing = File.OpenRead(Path.Combine(evidence.DirectoryPath, "missing")); }
                catch (FileNotFoundException) { }
                return true;
            });
            var handled = Assert.IsType<FileNotFoundException>(evidence.FirstChanceFailure);
            Assert.Null(evidence.EscapingFailure);
            File.Move(file.Path, file.Path + ".previous");
            Directory.CreateDirectory(file.Path);

            var failure = Record.Exception(() => evidence.Observe("destination-is-directory", world.WorldTick, () =>
            {
                Exception? foreignFailure = null;
                var otherThread = new Thread(() => foreignFailure = Record.Exception(() =>
                {
                    using var missing = File.OpenRead(Path.Combine(evidence.DirectoryPath, "foreign-missing"));
                }));
                otherThread.Start();
                otherThread.Join();
                Assert.IsType<FileNotFoundException>(foreignFailure);
                Assert.Null(evidence.FirstChanceFailure);
                return file.Save(world);
            }));

            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "Expected the obstructed checkpoint to fail.");
            Assert.NotSame(handled, failure);
            Assert.Same(failure, evidence.FirstChanceFailure);
            Assert.Same(failure, evidence.EscapingFailure);
            Assert.Equal(previousBytes, File.ReadAllBytes(file.Path + ".previous"));
            Assert.Equal(unsavedBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Empty(Directory.GetFiles(evidence.DirectoryPath, ".world.json.*.tmp"));
            if (blockArtifacts) Assert.Null(evidence.EvidenceDirectory);
            else
            {
                var captured = Assert.Single(Directory.GetFiles(Path.Combine(evidence.EvidenceDirectory!, "files"), ".world.json.*.tmp"));
                Assert.Equal(unsavedBytes, File.ReadAllBytes(captured));
                using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence.EvidenceDirectory!, "escaping-exception.json")));
                Assert.True(metadata.RootElement.GetProperty("SameAsFirstChance").GetBoolean());
            }
            Directory.Delete(file.Path);
            File.Move(file.Path + ".previous", file.Path);
            evidence.Complete();
        }
        finally { artifacts.Delete(recursive: true); }
    }

    [Fact]
    public async Task CheckpointArchivesOldHistoryAndRestartsWithMonotonicEventIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-history-{Guid.NewGuid():N}");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory, "world.json"));
            using var setup = new PrivateWorldRuntime("history-test");
            var hungry = setup.ExportState();
            using var seed = PrivateWorldRuntime.Restore(hungry with
            {
                Inhabitants = hungry.Inhabitants.Select(person => person with { HungerBasisPoints = 3_000 }).ToArray(),
            });
            await seed.AdvanceOneTickAsync();
            var initial = seed.ExportState();
            const int count = 4096;
            var social = initial.Society.Society;
            var scheduler = initial.Society.Cognition;
            var expanded = initial with
            {
                Events = Enumerable.Range(1, count).Select(id => initial.Events[^1] with { EventId = id }).ToArray(),
                Society = initial.Society with
                {
                    Society = social with
                    {
                        Events = Enumerable.Range(1, count).Select(id => social.Events[^1] with { EventId = id }).ToArray(),
                        Inventory = social.Inventory with
                        {
                            Events = Enumerable.Range(1, count).Select(id => social.Inventory.Events[^1] with { EventId = id }).ToArray(),
                        },
                    },
                    Cognition = scheduler with
                    {
                        Events = Enumerable.Range(1, count).Select(id => scheduler.Events[^1] with { EventId = id }).ToArray(),
                        Runtimes = scheduler.Runtimes.Select(runtime => runtime with
                        {
                            Events = Enumerable.Range(1, count).Select(id => runtime.Events[^1] with { EventId = id }).ToArray(),
                        }).ToArray(),
                    },
                },
            };
            using var world = PrivateWorldRuntime.Restore(expanded);
            file.Save(world);
            var compact = world.ExportState();
            Assert.Equal(count - PrivateWorldHistory.RecentEventLimit, compact.EventHistoryFloor);
            Assert.Equal(PrivateWorldHistory.RecentEventLimit, compact.Events.Count);
            Assert.Equal(PrivateWorldHistory.RecentEventLimit, compact.Society.Society.Events.Count);
            Assert.Equal(PrivateWorldHistory.RecentEventLimit, compact.Society.Society.Inventory.Events.Count);
            Assert.Equal(PrivateWorldHistory.RecentEventLimit, compact.Society.Cognition.Events.Count);
            Assert.All(compact.Society.Cognition.Runtimes, runtime => Assert.Equal(PrivateWorldHistory.RecentEventLimit, runtime.Events.Count));
            Assert.NotNull(compact.HistoryArchiveHead);
            var archives = Directory.GetFiles(file.Path + ".history");
            Assert.Single(archives);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(archives[0]));
            }
            file.Save(world);
            Assert.Single(Directory.GetFiles(file.Path + ".history"));
            using var restored = file.LoadOrCreate("history-test");
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(compact), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            var baseline = new OwnerWorldObservationStore(restored).GetReconnectBaseline(0);
            Assert.True(baseline.Events.ResetRequired);
            Assert.Equal(compact.EventHistoryFloor, baseline.Events.EventHistoryFloor);
            Assert.Equal(count, baseline.Snapshot.LatestEventId);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.True(restored.ExportState().Events[^1].EventId > count);
            File.WriteAllText(archives[0], "corrupt");
            Assert.Throws<InvalidDataException>(() => file.LoadOrCreate("history-test"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrivateWorldStateFileRestoresTheIntegratedRuntimeWithoutProviderSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-private-state-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "private-world.json");
        try
        {
            var stateFile = new PrivateWorldStateFile(path);
            using (var runtime = stateFile.LoadOrCreate("playtest-alpha"))
            {
                _ = await runtime.AdvanceOneTickAsync();
                stateFile.Save(runtime);
            }

            using var restored = stateFile.LoadOrCreate("playtest-alpha");
            Assert.Equal(1, restored.WorldTick);
            Assert.Equal(4, restored.Society.Inhabitants.Count);
            Assert.Equal(
                PrivateWorldRuntimeCodec.Encode(restored.ExportState()),
                File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
