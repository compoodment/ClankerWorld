using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(0, "low")]
    [InlineData(SaveDiskSpaceMonitor.WarningBelowBytes - 1, "low")]
    [InlineData(SaveDiskSpaceMonitor.WarningBelowBytes, "ok")]
    [InlineData(-1, "unknown")]
    public async Task SaveSpaceAdvisoryUsesServerDiskAndNeverRefusesSaving(long available, string expected)
    {
        using var baseHost = new ViewerWebApplicationFactory(null, privateWorld: true);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ISaveDiskSpaceProbe>(new FixedSaveSpaceProbe(available)))));
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = await StartAndActivateAsync(host, client, key);
        var monitor = host.Services.GetRequiredService<SaveDiskSpaceMonitor>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (monitor.Capture().CheckedUtc is null) await Task.Delay(10, timeout.Token);
        var action = new OwnerControlAction("save-disk-status");
        using var response = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/saves/disk-status", action, OwnerHttpBinding.EmptyPayload(action.Operation));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<SaveDiskSpaceStatus>())!;
        Assert.Equal(expected, status.State);
        Assert.Equal(available < 0 ? null : (long?)available, status.AvailableBytes);
        Assert.DoesNotContain("runtime.json", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var signer = new ActionCompatibilitySigner(key);
        var identity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
        var authority = new ClankerWorld.GodotClient.Pairing.OwnerAuthorityIdentity(identity.ServerAuthorityId, identity.WorldId);
        var api = new ClankerWorld.GodotClient.UI.OwnerWorldApi(client);
        var uri = new UriBuilder(client.BaseAddress!) { Host = "127.0.0.1" }.Uri;
        var clientStatus = await api.GetSaveDiskSpaceAsync(uri, authority, device.DeviceId, signer, default);
        Assert.Equal(status.State, clientStatus.State);
        Assert.Equal(status.AvailableBytes, clientStatus.AvailableBytes);

        var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
        runtime.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
        var create = new OwnerManualSaveAction("create", "Low space still saves");
        using var created = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/saves/create", create, OwnerHttpBinding.ManualSavePayload(create));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var saved = (await created.Content.ReadFromJsonAsync<ManualWorldSave>())!;
        var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(saves.Read(saved.Id)));
        var overwrite = new OwnerManualSaveAction("overwrite", saved.Id);
        using var overwritten = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/saves/overwrite", overwrite, OwnerHttpBinding.ManualSavePayload(overwrite));
        Assert.Equal(HttpStatusCode.OK, overwritten.StatusCode);

        var autosave = host.Services.GetRequiredService<WorldAutosaveStore>();
        var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
        runtime.Resume();
        Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        var auto = Assert.IsType<ManualWorldSave>(autosave.MaybeSave(
            autosave.Capture().LastSavedUtc.AddMinutes(6), runtime, providers, saves));
        Assert.True(auto.IsAutosave);
        Assert.Equal(runtime.WorldTick, saves.Read(auto.Id).Society.Society.WorldTick);

        var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
        file.Save(runtime);
        using var restored = file.LoadOrCreate(runtime.ExportState().WorldSeed);
        Assert.Equal(runtime.WorldTick, restored.WorldTick);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task StalledDiskQueryCannotDelayHostStartupOrEmergencyCheckpoint()
    {
        using var release = new ManualResetEventSlim();
        var probe = new StalledSaveSpaceProbe(release);
        using var baseHost = new ViewerWebApplicationFactory(null, privateWorld: true);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ISaveDiskSpaceProbe>(probe))));
        try
        {
            using var client = host.CreateClient();
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var monitor = host.Services.GetRequiredService<SaveDiskSpaceMonitor>();
            Assert.Equal("unknown", monitor.Capture().State);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var presence = host.Services.GetRequiredService<OwnerClientPresenceLease>();
            presence.RecordAuthenticatedReconnect("disk-stall-test");
            using var service = new PrivateWorldRuntimeService(runtime, file, presence);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.False(release.IsSet);
            using var restored = file.LoadOrCreate(runtime.ExportState().WorldSeed);
            Assert.Equal(runtime.WorldTick, restored.WorldTick);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LowSnapshotVolumeStillWarnsWhenTheActiveVolumeHasSpaceOrCannotBeRead(bool unavailable)
    {
        using var monitor = new SaveDiskSpaceMonitor(Path.Combine(Path.GetTempPath(), "space-test.json"),
            new SnapshotVolumeProbe(unavailable));
        await monitor.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (monitor.Capture().CheckedUtc is null) await Task.Delay(10, timeout.Token);
            Assert.Equal("low", monitor.Capture().State);
            Assert.Equal(0, monitor.Capture().AvailableBytes);
        }
        finally { await monitor.StopAsync(default); }
    }

    [Fact]
    public async Task ADelayedLaterVolumeQueryCannotMakeAnEarlierSampleFreshAgain()
    {
        var clock = new SaveSpaceTestClock();
        using var monitor = new SaveDiskSpaceMonitor(Path.Combine(Path.GetTempPath(), "space-age-test.json"),
            new LaterDelayedSaveSpaceProbe(clock), timeProvider: clock);
        await monitor.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (monitor.Capture().CheckedUtc is null) await Task.Delay(10, timeout.Token);
            var status = monitor.Capture();
            Assert.Equal("unknown", status.State);
            Assert.Null(status.AvailableBytes);
            Assert.Equal(clock.StartedUtc, status.CheckedUtc);
        }
        finally { await monitor.StopAsync(default); }
    }

    private sealed class SaveSpaceTestClock : TimeProvider
    {
        public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
        private long elapsedTicks;
        public override DateTimeOffset GetUtcNow() => StartedUtc.AddTicks(Interlocked.Read(ref elapsedTicks));
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref elapsedTicks, elapsed.Ticks);
    }

    private sealed class LaterDelayedSaveSpaceProbe(SaveSpaceTestClock clock) : ISaveDiskSpaceProbe
    {
        private int reads;
        public long? AvailableBytes(string directory)
        {
            if (++reads == 2) clock.Advance(TimeSpan.FromSeconds(31));
            return 4 * SaveDiskSpaceMonitor.WarningBelowBytes;
        }
    }

    private sealed class SnapshotVolumeProbe(bool unavailable) : ISaveDiskSpaceProbe
    {
        public long? AvailableBytes(string directory) => directory.EndsWith(".manual", StringComparison.Ordinal)
            ? 0 : unavailable ? throw new IOException("Unavailable volume") : 4 * SaveDiskSpaceMonitor.WarningBelowBytes;
    }

    private sealed class FixedSaveSpaceProbe(long available) : ISaveDiskSpaceProbe
    {
        public long? AvailableBytes(string directory) => available >= 0 ? available : throw new IOException("Unavailable volume");
    }

    private sealed class StalledSaveSpaceProbe(ManualResetEventSlim release) : ISaveDiskSpaceProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long? AvailableBytes(string directory)
        {
            Entered.TrySetResult();
            release.Wait();
            return 0;
        }
    }
}
