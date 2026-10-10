using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldPreviewConcurrencyTests
{
    [Theory]
    [InlineData(WorldSizePreset.Small)]
    [InlineData(WorldSizePreset.Medium)]
    public void NativePreviewSelectionDoesNotOwnTheWorldMutationGate(WorldSizePreset size)
    {
        var directory = Directory.CreateTempSubdirectory("preview-mutation-gate-");
        try
        {
            using var world = NormalPathWorld.CreateGenerated("native-preview-readonly", _ => new DeterministicDecisionProvider());
            world.Resume();
            var path = Path.Combine(directory.FullName, "world.json");
            var file = new PrivateWorldStateFile(path);
            file.Save(world);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new("deterministic", null, null, null, null, null, null));
            var autosave = new WorldAutosaveStore(path, world.Society.WorldId, mutationGate: providers.WorldMutationGate);
            var catalog = new WorldCatalogStore(path, world.ExportState(), [], autosave.Capture(), providers.WorldMutationGate);
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            var metadata = JsonSerializer.SerializeToUtf8Bytes(catalog.Capture());
            var selectorCalls = 0;
            var coordinator = new WorldSelectionCoordinator(catalog, world, file, providers, autosave, new WorldJevPolicy(),
                NullLogger<WorldSelectionCoordinator>.Instance, _ => new DeterministicDecisionProvider(), options =>
                {
                    selectorCalls++;
                    Assert.False(Monitor.IsEntered(providers.WorldMutationGate));
                    return GeographyCandidateSelector.Select(options);
                });
            var options = new GeographyOptions("native-preview-gate", size);
            var preview = coordinator.Preview(options);
            var repeated = coordinator.Preview(options);
            Assert.Equal(2, selectorCalls);
            Assert.Equal(3, preview.Candidates.Count + preview.FailedCandidates.Count);
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(preview), JsonSerializer.SerializeToUtf8Bytes(repeated));
            Assert.NotNull(preview.PackedMapLayers);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(metadata, JsonSerializer.SerializeToUtf8Bytes(catalog.Capture()));
            Assert.False(world.Society.IsPaused);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ConfiguredHostCommitsWhilePreviewSelectionWaitsAndConcurrentPreviewsStayBounded()
    {
        var directory = Directory.CreateTempSubdirectory("preview-running-host-");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ViewerWorldPreview>? first = null;
        Task<ViewerWorldPreview>? second = null;
        Task<bool>? tick = null;
        PrivateWorldRuntime? world = null;
        PrivateWorldRuntimeService? service = null;
        try
        {
            world = NormalPathWorld.CreateGenerated("native-preview-running-host", _ => new DeterministicDecisionProvider());
            world.Resume();
            var path = Path.Combine(directory.FullName, "world.json");
            var file = new PrivateWorldStateFile(path);
            file.Save(world);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new("deterministic", null, null, null, null, null, null));
            var autosave = new WorldAutosaveStore(path, world.Society.WorldId, mutationGate: providers.WorldMutationGate);
            var catalog = new WorldCatalogStore(path, world.ExportState(), [], autosave.Capture(), providers.WorldMutationGate);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("preview-test-owner");
            service = new PrivateWorldRuntimeService(world, file, presence, autosave: autosave, providers: providers);
            var initialTick = world.WorldTick;
            var selectors = 0;
            var active = 0;
            var maximumActive = 0;
            var coordinator = new WorldSelectionCoordinator(catalog, world, file, providers, autosave, new WorldJevPolicy(),
                NullLogger<WorldSelectionCoordinator>.Instance, _ => new DeterministicDecisionProvider(), options =>
                {
                    var current = Interlocked.Increment(ref active);
                    lock (entered) maximumActive = Math.Max(current, maximumActive);
                    Interlocked.Increment(ref selectors);
                    entered.TrySetResult(true);
                    try
                    {
                        Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "Preview test release was not signaled.");
                        return GeographyCandidateSelector.Select(options);
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            var options = new GeographyOptions("native-preview-concurrent", WorldSizePreset.Small);
            first = Task.Factory.StartNew(() => coordinator.Preview(options), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var secondStarted = new ManualResetEventSlim();
            second = Task.Factory.StartNew(() =>
            {
                secondStarted.Set();
                return coordinator.Preview(options);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(30)));
            tick = Task.Factory.StartNew(() => service.TryAdvanceOnceAsync().AsTask(), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(20)), "The configured host did not advance.");
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref selectors));
            Assert.Equal(initialTick + 1, world.WorldTick);
            Assert.False(world.Society.IsPaused);
            var committed = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.Equal(committed, File.ReadAllBytes(path));
            var metadata = JsonSerializer.SerializeToUtf8Bytes(catalog.Capture());
            var assignments = JsonSerializer.SerializeToUtf8Bytes(providers.CaptureStatus().Assignments);
            var settings = JsonSerializer.SerializeToUtf8Bytes(autosave.Capture());
            release.Set();
            var previews = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, maximumActive);
            Assert.Equal(2, selectors);
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(previews[0]), JsonSerializer.SerializeToUtf8Bytes(previews[1]));
            Assert.Equal(committed, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Equal(committed, File.ReadAllBytes(path));
            Assert.Equal(metadata, JsonSerializer.SerializeToUtf8Bytes(catalog.Capture()));
            Assert.Equal(assignments, JsonSerializer.SerializeToUtf8Bytes(providers.CaptureStatus().Assignments));
            Assert.Equal(settings, JsonSerializer.SerializeToUtf8Bytes(autosave.Capture()));
            using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(committed), _ => new DeterministicDecisionProvider());
            Assert.Equal(committed, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        }
        finally
        {
            release.Set();
            // Never leave a worker touching a disposed runtime or temporary files.
            if (first is not null) try { await first; } catch { }
            if (second is not null) try { await second; } catch { }
            if (tick is not null) try { await tick; } catch { }
            service?.Dispose();
            world?.Dispose();
            directory.Delete(recursive: true);
        }
    }
}
