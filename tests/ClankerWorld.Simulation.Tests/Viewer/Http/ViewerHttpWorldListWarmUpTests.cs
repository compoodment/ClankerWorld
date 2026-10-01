using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public void WarmUpChecksInactiveWorldsSoTheFirstListReusesThem()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-warmup-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            host.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var log = RecordWorldListLog(host);
            using var healthy = new PrivateWorldRuntime("warmup-healthy-world");
            healthy.Pause();
            var healthyEntry = catalog.Add("Healthy", healthy.ExportState());
            using var damaged = new PrivateWorldRuntime("warmup-damaged-world");
            damaged.Pause();
            var damagedEntry = catalog.Add("Damaged", damaged.ExportState());
            var damagedPath = SnapshotPath(host, damagedEntry.Id);
            var bytes = File.ReadAllBytes(damagedPath);
            bytes[0] = (byte)'x';
            File.WriteAllBytes(damagedPath, bytes);

            selection.WarmUp(CancellationToken.None);

            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=finished checked=2 incompatible=1 skipped=0 failed=0", StringComparison.Ordinal));
            var listed = selection.List();
            Assert.Contains("world_list outcome=checked worlds=3 cache_hits=2 scans=0", LastList(log));
            Assert.Equal("compatible", listed.Worlds.Single(world => world.Id == healthyEntry.Id).Compatibility);
            Assert.Equal("incompatible", listed.Worlds.Single(world => world.Id == damagedEntry.Id).Compatibility);
            Assert.Equal(bytes, File.ReadAllBytes(damagedPath));
            selection.WarmUp(CancellationToken.None);
            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=finished checked=0 incompatible=0 skipped=2 failed=0", StringComparison.Ordinal));
            Assert.DoesNotContain(log.Messages, message =>
                message.Contains("warmup-healthy-world", StringComparison.Ordinal) ||
                message.Contains("warmup-damaged-world", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task WarmUpLeavesTheWorldMutationGateFreeWhileCheckingWorlds()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-warmup-gate-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            host.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var gate = host.Services.GetRequiredService<ProviderConfigurationStore>().WorldMutationGate;
            var log = RecordWorldListLog(host);
            // Medium worlds take about a second each to restore, far longer than a probe waits.
            for (var n = 0; n < 3; n++)
            {
                var seed = "warmup-gate-medium-" + n;
                using var other = new PrivateWorldRuntime(seed, startPace: WorldStartPace.FounderSetup,
                    geographyOptions: new GeographyOptions(seed, WorldSizePreset.Medium));
                other.InitializeFirstTownContent();
                other.Pause();
                catalog.Add("Medium " + n, other.ExportState());
            }

            var warmUp = Task.Run(() => selection.WarmUp(CancellationToken.None));
            var probes = 0;
            while (!warmUp.IsCompleted)
            {
                // Ticks, saves and owner actions take this gate; they must not wait for a restore.
                Assert.True(Monitor.TryEnter(gate, TimeSpan.FromMilliseconds(500)));
                Monitor.Exit(gate);
                probes++;
                await Task.Delay(20);
            }
            await warmUp;

            Assert.True(probes > 1);
            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=finished checked=3 incompatible=0 skipped=0 failed=0", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task WarmUpAndALoadWorldListCanRunTogether()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-warmup-concurrent-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            host.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var log = RecordWorldListLog(host);
            var ids = new List<string>();
            for (var n = 0; n < 4; n++)
            {
                using var other = new PrivateWorldRuntime("warmup-concurrent-world-" + n);
                other.Pause();
                ids.Add(catalog.Add("Other " + n, other.ExportState()).Id);
            }

            var warmUp = Task.Run(() => selection.WarmUp(CancellationToken.None));
            var during = selection.List();
            await warmUp;

            Assert.All(ids, id => Assert.Equal("compatible", during.Worlds.Single(world => world.Id == id).Compatibility));
            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=finished", StringComparison.Ordinal) && message.Contains("failed=0", StringComparison.Ordinal));
            var after = selection.List();
            Assert.Contains("world_list outcome=checked worlds=5 cache_hits=4 scans=0", LastList(log));
            Assert.All(ids, id => Assert.Equal("compatible", after.Worlds.Single(world => world.Id == id).Compatibility));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void WarmUpCarriesOnWhenASavedWorldCannotBeRead()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-warmup-unreadable-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            host.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var log = RecordWorldListLog(host);
            using var unreadable = new PrivateWorldRuntime("warmup-unreadable-world");
            unreadable.Pause();
            var unreadableEntry = catalog.Add("Unreadable", unreadable.ExportState());
            using var healthy = new PrivateWorldRuntime("warmup-readable-world");
            healthy.Pause();
            var healthyEntry = catalog.Add("Healthy", healthy.ExportState());
            var unreadablePath = SnapshotPath(host, unreadableEntry.Id);
            File.Delete(unreadablePath);
            Directory.CreateDirectory(unreadablePath);

            selection.WarmUp(CancellationToken.None);

            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=finished checked=1 incompatible=0 skipped=0 failed=1", StringComparison.Ordinal));
            Assert.Contains(log.Messages, message => message.Contains(
                $"world_list_warmup outcome=world_failed catalog_id={unreadableEntry.Id} error=UnauthorizedAccessException",
                StringComparison.Ordinal));
            var listed = selection.List();
            Assert.Equal("unknown", listed.Worlds.Single(world => world.Id == unreadableEntry.Id).Compatibility);
            Assert.Equal("compatible", listed.Worlds.Single(world => world.Id == healthyEntry.Id).Compatibility);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task StoppingTheHostStopsTheWarmUpBetweenWorlds()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-warmup-cancel-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            host.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var gate = host.Services.GetRequiredService<ProviderConfigurationStore>().WorldMutationGate;
            var log = RecordWorldListLog(host);
            for (var n = 0; n < 2; n++)
            {
                using var other = new PrivateWorldRuntime("warmup-cancel-world-" + n);
                other.Pause();
                catalog.Add("Other " + n, other.ExportState());
            }
            using var holding = new SemaphoreSlim(0);
            using var release = new SemaphoreSlim(0);
            // Hold the gate so the started warm-up waits to read the catalog
            // until the host has asked it to stop.
            var holder = Task.Run(() =>
            {
                lock (gate)
                {
                    holding.Release();
                    release.Wait();
                }
            });
            await holding.WaitAsync();
            var service = new WorldCatalogWarmUpService(selection);
            await service.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!log.Messages.Any(message => message.Contains("world_list_warmup outcome=started", StringComparison.Ordinal)) &&
                   DateTime.UtcNow < deadline)
                await Task.Delay(20);

            var stopping = service.StopAsync(CancellationToken.None);
            release.Release();
            await stopping;
            await holder;

            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=canceled checked=0 incompatible=0 skipped=0 failed=0", StringComparison.Ordinal));
            selection.List();
            Assert.Contains("cache_hits=0 scans=2", LastList(log));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void RunningPrivateHostWarmsUpItsSavedWorldsAtStartup()
    {
        var directory = Directory.CreateTempSubdirectory("world-list-warmup-startup-");
        try
        {
            using (var first = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = first.CreateClient())
            {
                first.Services.GetRequiredService<PrivateWorldRuntime>().Pause();
                using var other = new PrivateWorldRuntime("warmup-startup-world");
                other.Pause();
                first.Services.GetRequiredService<WorldCatalogStore>().Add("Other", other.ExportState());
            }

            var log = new RecordingLogger<WorldSelectionCoordinator>();
            using var factory = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var running = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ClankerWorld:Runtime:AdvanceScript", "true");
                builder.ConfigureLogging(logging =>
                    logging.AddProvider(new RecordingLoggerProvider<WorldSelectionCoordinator>(log)));
            });
            using var client2 = running.CreateClient();

            Assert.Contains(running.Services.GetServices<IHostedService>(), service => service is WorldCatalogWarmUpService);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!log.Messages.Any(message => message.Contains("world_list_warmup outcome=finished", StringComparison.Ordinal)) &&
                   DateTime.UtcNow < deadline)
                Thread.Sleep(50);
            Assert.Contains(log.Messages, message => message.Contains(
                "world_list_warmup outcome=finished checked=1 incompatible=0 skipped=0 failed=0", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static RecordingLogger<WorldSelectionCoordinator> RecordWorldListLog(ViewerWebApplicationFactory host)
    {
        var log = new RecordingLogger<WorldSelectionCoordinator>();
        host.Services.GetRequiredService<ILoggerFactory>().AddProvider(
            new RecordingLoggerProvider<WorldSelectionCoordinator>(log));
        return log;
    }

    private static string LastList(RecordingLogger<WorldSelectionCoordinator> log) =>
        log.Messages.Last(message => message.Contains("world_list outcome=checked", StringComparison.Ordinal));

    private static string SnapshotPath(ViewerWebApplicationFactory host, string id) =>
        Path.Combine(host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".worlds", id + ".save");
}
