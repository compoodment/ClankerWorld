using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task JevAssistanceIsSignedPauseOnlyAndSurvivesReloadWithoutChangingProviderCredentials()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-jev-world-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            long savedProviderEpoch;
            string pairedDeviceId;
            byte[] providerBytes;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                var device = await StartAndActivateAsync(host, client, key);
                using var resume = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
                pairedDeviceId = device.DeviceId;
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                var initialProviderEpoch = host.Services.GetRequiredService<ConfigurableDecisionProvider>().ProviderEpoch;
                var providerPath = host.Services.GetRequiredService<ProviderConfigurationStore>().Path;
                providerBytes = File.ReadAllBytes(providerPath);
                var action = new OwnerJevAssistanceAction(false);
                const string path = "/api/v1/owner/control/jev-assistance";
                using var running = await SendSignedAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.JevAssistancePayload(action));
                Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
                Assert.True(runtime.JevEnabled);

                using var pause = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/pause", new OwnerControlAction("pause"), OwnerHttpBinding.EmptyPayload("pause"));
                Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
                var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.JevAssistancePayload(action));
                using var tampered = await client.PostAsJsonAsync(path, envelope with { Action = new OwnerJevAssistanceAction(true) });
                Assert.False(tampered.IsSuccessStatusCode);
                Assert.True(runtime.JevEnabled);

                using var configured = await SendSignedAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.JevAssistancePayload(action));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                Assert.True((await configured.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
                Assert.False(runtime.JevEnabled);
                Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, runtime.ExportState().SchemaVersion);
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(
                    runtime.ExportState() with { SchemaVersion = 14 }));
                Assert.Equal(1, runtime.JevPolicyRevision);
                savedProviderEpoch = host.Services.GetRequiredService<ConfigurableDecisionProvider>().ProviderEpoch;
                Assert.Equal(initialProviderEpoch + 1, savedProviderEpoch);
                Assert.False(host.Services.GetRequiredService<WorldJevPolicy>().Capture().Enabled);
                Assert.False(host.Services.GetRequiredService<OwnerWorldObservationStore>().GetSnapshot().JevEnabled);
                Assert.Equal(providerBytes, File.ReadAllBytes(providerPath));
                Assert.Contains("owner-jev-assistance.v1",
                    host.Services.GetRequiredService<OwnerWorldObservationStore>().GetOwnerHandshake().ServerCapabilities);
                using var repeated = await SendSignedAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.JevAssistancePayload(action));
                Assert.False((await repeated.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
            }

            using var restarted = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var restartedClient = restarted.CreateClient();
            var restored = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.False(restored.JevEnabled);
            Assert.Equal(1, restored.JevPolicyRevision);
            Assert.False(restarted.Services.GetRequiredService<WorldJevPolicy>().Capture().Enabled);
            Assert.Equal(savedProviderEpoch, restarted.Services.GetRequiredService<ConfigurableDecisionProvider>().ProviderEpoch);
            Assert.True(restored.Society.IsPaused);
            Assert.Equal(0, restored.WorldTick);
            var reenable = new OwnerJevAssistanceAction(true);
            using var restoredRequest = await SendSignedAsync(restarted, restartedClient, key, pairedDeviceId,
                "/api/v1/owner/control/jev-assistance", reenable, OwnerHttpBinding.JevAssistancePayload(reenable));
            Assert.Equal(HttpStatusCode.OK, restoredRequest.StatusCode);
            Assert.True(restored.JevEnabled);
            Assert.Equal(2, restored.JevPolicyRevision);
            Assert.True(restarted.Services.GetRequiredService<WorldJevPolicy>().Capture().Enabled);
            Assert.Equal(savedProviderEpoch + 1, restarted.Services.GetRequiredService<ConfigurableDecisionProvider>().ProviderEpoch);
            Assert.Equal(providerBytes, File.ReadAllBytes(restarted.Services.GetRequiredService<ProviderConfigurationStore>().Path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LifePaceRequiresBoundSignedRequestAndPauseAndPersistsWithoutAgingAnyone()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-life-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                var device = await StartAndActivateAsync(host, client, key);
                using var resume = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                var births = runtime.Society.Inhabitants.Select(person => person.BirthTick).ToArray();
                var action = new OwnerLifePaceAction(1_460);
                const string path = "/api/v1/owner/control/life-pace";
                using var running = await SendSignedAsync(host, client, key, device.DeviceId, path, action, OwnerHttpBinding.LifePacePayload(action));
                Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
                Assert.Null(runtime.Society.LifeClock);
                using var pause = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/control/pause",
                    new OwnerControlAction("pause"), OwnerHttpBinding.EmptyPayload("pause"));
                Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
                var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, action, OwnerHttpBinding.LifePacePayload(action));
                using var tampered = await client.PostAsJsonAsync(path, envelope with { Action = new OwnerLifePaceAction(365) });
                Assert.False(tampered.IsSuccessStatusCode);
                Assert.Null(runtime.Society.LifeClock);
                using var configured = await SendSignedAsync(host, client, key, device.DeviceId, path, action, OwnerHttpBinding.LifePacePayload(action));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                Assert.True((await configured.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
                using var repeated = await SendSignedAsync(host, client, key, device.DeviceId, path, action, OwnerHttpBinding.LifePacePayload(action));
                Assert.False((await repeated.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
                Assert.Equal(births, runtime.Society.Inhabitants.Select(person => person.BirthTick));
                Assert.Equal(0, runtime.WorldTick);
                Assert.True(runtime.Society.IsPaused);
                var observation = host.Services.GetRequiredService<OwnerWorldObservationStore>();
                Assert.Equal(1_460, observation.GetSnapshot().LifePaceRate);
                Assert.Contains("owner-life-pace.v1", observation.GetOwnerHandshake().ServerCapabilities);
            }
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var restartedClient = restarted.CreateClient();
            var restored = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.Equal(1_460, restored.Society.LifeClock!.Rate);
            Assert.Equal(0, restored.WorldTick);
            Assert.True(restored.Society.IsPaused);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PairedDeviceCanObserveThenIssueServerValidatedControlAndPausedAuthoringRequests()
    {
        using var client = factory.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var pairing = await StartAndActivateAsync(client, key, publicKey);

        using var observation = await SendSignedAsync(
            client,
            key,
            pairing.DeviceId,
            "/api/v1/owner/reconnect",
            new OwnerReconnectAction(0),
            OwnerHttpBinding.ReconnectPayload(new OwnerReconnectAction(0)));
        var reconnect = await observation.Content.ReadFromJsonAsync<ViewerOwnerReconnect>();

        Assert.Equal(HttpStatusCode.OK, observation.StatusCode);
        Assert.NotNull(reconnect);
        Assert.Contains("owner-observation.read.v1", reconnect.Handshake.ServerCapabilities);
        Assert.Contains("owner-control.request.v1", reconnect.Handshake.ServerCapabilities);
        Assert.Contains("owner-provider-configuration.v1", reconnect.Handshake.ServerCapabilities);
        Assert.Contains("owner-inhabitant-provider-configuration.v1", reconnect.Handshake.ServerCapabilities);
        Assert.True(factory.Services.GetRequiredService<OwnerClientPresenceLease>().HasActiveClient);
        Assert.Single(reconnect.Baseline.Snapshot.Inhabitants);
        Assert.NotNull(reconnect.Baseline.Snapshot.Authoring);

        using var pause = await SendSignedAsync(
            client,
            key,
            pairing.DeviceId,
            "/api/v1/owner/control/pause",
            new OwnerControlAction("pause"),
            OwnerHttpBinding.EmptyPayload("pause"));
        var pauseReceipt = await pause.Content.ReadFromJsonAsync<OwnerControlReceipt>();
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        Assert.True(pauseReceipt!.Changed);
        Assert.True(pauseReceipt.IsPaused);

        var instructionAction = new OwnerInstructionAction(
            "instruction-http-1",
            "actor-scout",
            "must_do",
            "Gather food.");
        using var instruction = await SendSignedAsync(
            client,
            key,
            pairing.DeviceId,
            "/api/v1/owner/instructions",
            instructionAction,
            OwnerHttpBinding.InstructionPayload(instructionAction));
        var instructionReceipt = await instruction.Content.ReadFromJsonAsync<OwnerInstructionReceipt>();
        Assert.Equal(HttpStatusCode.OK, instruction.StatusCode);
        Assert.NotNull(instructionReceipt);
        Assert.Equal("instruction-0000000001", instructionReceipt.InstructionId);

        var runtime = factory.Services.GetRequiredService<OwnerWorldRuntime>();
        var beforeAuthoring = runtime.Capture();
        var water = beforeAuthoring.Snapshot.CurrentMap.Tiles.Single(tile => tile.Terrain == TerrainKind.Water).Position;
        var authoringAction = new OwnerAuthoringBatchAction(
            "authoring-http-1",
            [new OwnerAuthoringOperationAction(
                "set_terrain",
                null,
                "mountain",
                null,
                water.X,
                water.Y,
                null)]);
        using var authoring = await SendSignedAsync(
            client,
            key,
            pairing.DeviceId,
            "/api/v1/owner/authoring",
            authoringAction,
            OwnerHttpBinding.AuthoringPayload(authoringAction));
        var authoringReceipt = await authoring.Content.ReadFromJsonAsync<OwnerAuthoringBatchReceipt>();
        var afterAuthoring = runtime.Capture();

        Assert.Equal(HttpStatusCode.OK, authoring.StatusCode);
        Assert.True(authoringReceipt!.Applied, authoringReceipt.Failure);
        Assert.NotEqual(beforeAuthoring.Snapshot.CurrentMapManifestDigest, afterAuthoring.Snapshot.CurrentMapManifestDigest);
        Assert.Equal(beforeAuthoring.Snapshot.InitialMapManifestDigest, afterAuthoring.Snapshot.InitialMapManifestDigest);
        var queued = Assert.Single(afterAuthoring.Snapshot.Instructions);
        Assert.Equal($"owner-device:{pairing.DeviceId}", queued.IssuerId);
        Assert.Contains(
            $"issuer:owner-device:{pairing.DeviceId}",
            afterAuthoring.Events.Single(worldEvent => worldEvent.Kind == "paused").Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            $"issuer=owner-device:{pairing.DeviceId}",
            afterAuthoring.Events[^1].Detail,
            StringComparison.Ordinal);
    }
}
