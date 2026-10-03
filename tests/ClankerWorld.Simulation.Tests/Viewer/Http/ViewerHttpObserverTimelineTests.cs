using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObserverTimelineIsAdvertisedOnlyToAnAuthenticatedPrivateWorldDevice(bool privateWorld)
    {
        using var host = new ViewerWebApplicationFactory(null, privateWorld: privateWorld);
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var discovery = await client.GetFromJsonAsync<ViewerHandshake>("/api/v1/handshake");
        Assert.NotNull(discovery);
        Assert.DoesNotContain("owner-observation-timeline.v1", discovery.ServerCapabilities);
        var device = await StartAndActivateAsync(host, client, key);
        var reconnect = await ReadObserverTimelineAsync(host, client, key, device.DeviceId, new(0));

        if (privateWorld)
        {
            Assert.Contains("owner-observation-timeline.v1", reconnect.Handshake.ServerCapabilities);
            Assert.NotNull(reconnect.Baseline.Timeline);
            Assert.False(string.IsNullOrWhiteSpace(reconnect.Baseline.Timeline.InstanceId));
            Assert.True(reconnect.Baseline.Timeline.Generation >= 0);
        }
        else
        {
            Assert.DoesNotContain("owner-observation-timeline.v1", reconnect.Handshake.ServerCapabilities);
            Assert.Null(reconnect.Baseline.Timeline);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PassivePairedDeviceSeesSignedTimelineChangesWithAnOlderEventHistory(bool selectWorld)
    {
        var directory = Directory.CreateTempSubdirectory("observer-timeline-http-");
        try
        {
            const string seed = "observer-timeline-http-target";
            using var target = new PrivateWorldRuntime(seed, startPace: WorldStartPace.FounderSetup,
                geographyOptions: new GeographyOptions(seed, WorldSizePreset.Small));
            target.Pause();
            if (!selectWorld)
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "runtime.json")).Save(target);

            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var initiatingClient = host.CreateClient();
            using var passiveClient = host.CreateClient();
            using var initiatingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var passiveKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var initiatingDevice = await StartAndActivateAsync(host, initiatingClient, initiatingKey);
            var passiveDevice = await StartAndActivateAsync(host, passiveClient, passiveKey);
            Assert.NotEqual(initiatingDevice.DeviceId, passiveDevice.DeviceId);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var targetId = selectWorld
                ? host.Services.GetRequiredService<WorldCatalogStore>().Add("Other world", target.ExportState()).Id
                : host.Services.GetRequiredService<ManualWorldSaveStore>().Create("Earlier world", runtime, []).Id;
            // Give the observing device a real cursor beyond the checkpoint's
            // history, without running background simulation or model calls.
            for (var change = 0; change < 10; change++)
                Assert.True(runtime.SetJevEnabled(!runtime.JevEnabled));
            var initial = await ReadObserverTimelineAsync(host, initiatingClient, initiatingKey,
                initiatingDevice.DeviceId, new(0));
            var passiveInitial = await ReadObserverTimelineAsync(host, passiveClient, passiveKey,
                passiveDevice.DeviceId, new(0));
            Assert.Contains("owner-observation-timeline.v1", passiveInitial.Handshake.ServerCapabilities);
            var initialTimeline = Assert.IsType<ViewerObserverTimeline>(initial.Baseline.Timeline);
            Assert.Equal(initialTimeline, passiveInitial.Baseline.Timeline);
            var held = passiveInitial.Baseline.Snapshot;
            var cachedRequest = new OwnerReconnectAction(held.LatestEventId, held.WorldId,
                held.MapManifestDigest, held.MapLayersDigest);
            var unchanged = await ReadObserverTimelineAsync(host, passiveClient, passiveKey,
                passiveDevice.DeviceId, cachedRequest);
            Assert.Equal(initialTimeline, unchanged.Baseline.Timeline);
            if (!selectWorld)
            {
                Assert.NotNull(held.PackedTerrain);
                Assert.NotNull(held.PackedMapLayers);
                Assert.Null(unchanged.Baseline.Snapshot.PackedTerrain);
                Assert.Null(unchanged.Baseline.Snapshot.PackedMapLayers);
            }

            var operation = selectWorld ? "select-world" : "load";
            var path = selectWorld ? "/api/v1/owner/worlds/select" : "/api/v1/owner/saves/load";
            var missing = new OwnerManualSaveAction(operation, Guid.NewGuid().ToString("N"));
            using (var refused = await SendSignedAsync(host, initiatingClient, initiatingKey,
                       initiatingDevice.DeviceId, path, missing, OwnerHttpBinding.ManualSavePayload(missing)))
                Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            var afterFailure = await ReadObserverTimelineAsync(host, passiveClient, passiveKey,
                passiveDevice.DeviceId, cachedRequest);
            Assert.Equal(initialTimeline, afterFailure.Baseline.Timeline);
            Assert.Equal(held.WorldId, afterFailure.Baseline.Snapshot.WorldId);
            Assert.Equal(held.LatestEventId, afterFailure.Baseline.Snapshot.LatestEventId);

            var action = new OwnerManualSaveAction(operation, targetId);
            using (var changed = await SendSignedAsync(host, initiatingClient, initiatingKey,
                       initiatingDevice.DeviceId, path, action, OwnerHttpBinding.ManualSavePayload(action)))
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

            // This device did not send the load/switch. Its old event and map
            // hints must still receive the identity of the replacement timeline.
            var passiveChanged = await ReadObserverTimelineAsync(host, passiveClient, passiveKey,
                passiveDevice.DeviceId, cachedRequest);
            var changedTimeline = Assert.IsType<ViewerObserverTimeline>(passiveChanged.Baseline.Timeline);
            Assert.Equal(initialTimeline.InstanceId, changedTimeline.InstanceId);
            Assert.Equal(initialTimeline.Generation + 1, changedTimeline.Generation);
            Assert.Equal(target.Society.WorldId, passiveChanged.Baseline.Snapshot.WorldId);
            Assert.True(passiveChanged.Baseline.Snapshot.LatestEventId < held.LatestEventId);
            Assert.Equal(held.LatestEventId, passiveChanged.Baseline.Events.AfterEventId);
            Assert.Empty(passiveChanged.Baseline.Events.Events);
            Assert.False(passiveChanged.Baseline.Events.ResetRequired);

            var refreshed = await ReadObserverTimelineAsync(host, passiveClient, passiveKey,
                passiveDevice.DeviceId, new(0));
            var initiatingRefreshed = await ReadObserverTimelineAsync(host, initiatingClient, initiatingKey,
                initiatingDevice.DeviceId, new(0));
            Assert.Equal(changedTimeline, refreshed.Baseline.Timeline);
            Assert.Equal(changedTimeline, initiatingRefreshed.Baseline.Timeline);
            Assert.Equal(initiatingRefreshed.Baseline.Snapshot.WorldId, refreshed.Baseline.Snapshot.WorldId);
            Assert.Equal(initiatingRefreshed.Baseline.Snapshot.LatestEventId, refreshed.Baseline.Snapshot.LatestEventId);
            Assert.Equal(0, refreshed.Baseline.Events.AfterEventId);
            Assert.NotEmpty(refreshed.Baseline.Events.Events);
            Assert.Equal(refreshed.Baseline.Snapshot.LatestEventId, refreshed.Baseline.Events.Events[^1].EventId);
            Assert.NotNull(refreshed.Baseline.Snapshot.PackedTerrain);
            Assert.NotNull(refreshed.Baseline.Snapshot.PackedMapLayers);
            Assert.Equal(target.ExportState().Map.ManifestDigest, refreshed.Baseline.Snapshot.MapManifestDigest);
            Assert.Equal(initiatingRefreshed.Baseline.Snapshot.PackedTerrain, refreshed.Baseline.Snapshot.PackedTerrain);
            Assert.Equal(initiatingRefreshed.Baseline.Snapshot.PackedMapLayers, refreshed.Baseline.Snapshot.PackedMapLayers);
            runtime.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<ViewerOwnerReconnect> ReadObserverTimelineAsync(
        ViewerWebApplicationFactory host, HttpClient client, ECDsa key, string deviceId,
        OwnerReconnectAction action)
    {
        using var response = await SendSignedAsync(host, client, key, deviceId,
            "/api/v1/owner/reconnect", action, OwnerHttpBinding.ReconnectPayload(action));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<ViewerOwnerReconnect>(await response.Content.ReadFromJsonAsync<ViewerOwnerReconnect>());
    }
}
