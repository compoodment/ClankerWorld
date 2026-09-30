using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldStateFileTests
{
    [Fact]
    public async Task EquivalentTicksKeepEveryHotHistoryBoundedAcrossRepeatedCompaction()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-history-soak-{Guid.NewGuid():N}");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory, "world.json"));
            using var world = file.LoadOrCreate("history-soak");
            for (var tick = 0; tick < 2500; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                file.Save(world);
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
            using var restored = file.LoadOrCreate("history-soak");
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
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
    public void LoadingSchemaThreeAtomicallyMigratesItsCheckpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-history-legacy-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var file = new PrivateWorldStateFile(Path.Combine(directory, "world.json"));
            using var world = new PrivateWorldRuntime("legacy-history");
            var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState() with { SchemaVersion = 3 });
            File.WriteAllBytes(file.Path, bytes);
            using var restored = file.LoadOrCreate("legacy-history");
            Assert.NotEqual(bytes, File.ReadAllBytes(file.Path));
            Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, restored.ExportState().SchemaVersion);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), File.ReadAllBytes(file.Path));
            Assert.False(Directory.Exists(file.Path + ".history"));
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
