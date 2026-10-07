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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedWorldSettingsPreserveTheOtherSelectedWorldAndItsCheckpoint(bool jev)
    {
        var directory = Directory.CreateTempSubdirectory("delayed-world-settings-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var entryA = catalog.Active();
            using var other = new PrivateWorldRuntime("delayed-world-settings-B");
            other.Pause();
            var entryB = catalog.Add("Other", other.ExportState());
            var path = jev ? "/api/v1/owner/control/jev-assistance" : "/api/v1/owner/control/life-pace";
            object Action(string worldId) => jev ? new OwnerJevAssistanceAction(false, worldId) : new OwnerLifePaceAction(365, worldId);
            string Payload(object action) => action is OwnerJevAssistanceAction helper
                ? OwnerHttpBinding.JevAssistancePayload(helper) : OwnerHttpBinding.LifePacePayload((OwnerLifePaceAction)action);
            var actionA = Action(runtime.Society.WorldId);
            // Obtain real authorization while A is active; delay only delivery.
            var held = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, actionA, Payload(actionA));
            selection.Select(entryB.Id);
            var activeBytes = File.ReadAllBytes(file.Path);
            var runtimeBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using var refused = await client.PostAsJsonAsync(path, held);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(runtimeBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            selection.Select(entryA.Id);
            Assert.Equal((1, true), (runtime.Society.LifeClock?.Rate ?? 1, runtime.JevEnabled));
            selection.Select(entryB.Id);
            Assert.Equal((1, true), (runtime.Society.LifeClock?.Rate ?? 1, runtime.JevEnabled));

            // Changing just the target world cannot retarget an already signed proof.
            activeBytes = File.ReadAllBytes(file.Path);
            runtimeBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var signedA = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, actionA, Payload(actionA));
            using var tampered = await client.PostAsJsonAsync(path, signedA with { Action = Action(runtime.Society.WorldId) });
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
            var legacyPayload = jev ? "clankerworld.owner-jev-assistance.v1\nenabled=false" : "clankerworld.owner-life-pace.v1\nrate=365";
            var unscoped = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, Action(null!), legacyPayload);
            using var missingWorld = await client.PostAsJsonAsync(path, unscoped);
            Assert.Equal(HttpStatusCode.BadRequest, missingWorld.StatusCode);
            Assert.Equal((1, true), (runtime.Society.LifeClock?.Rate ?? 1, runtime.JevEnabled));
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(runtimeBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

            // Fresh settings still apply to B and survive selecting away and back.
            var current = Action(runtime.Society.WorldId);
            using var accepted = await SendSignedAsync(host, client, key, device.DeviceId, path, current, Payload(current));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            selection.Select(entryA.Id);
            Assert.Equal((1, true), (runtime.Society.LifeClock?.Rate ?? 1, runtime.JevEnabled));
            selection.Select(entryB.Id);
            Assert.Equal(jev ? (1, false) : (365, true), (runtime.Society.LifeClock?.Rate ?? 1, runtime.JevEnabled));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task RoutineHelperSignsWorldProviderAndModelAndPersistsAcrossHostRestart()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-helper-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string deviceId;
            byte[] checkpoint;
            byte[] credentials;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                deviceId = (await StartAndActivateAsync(host, client, key)).DeviceId;
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                var action = new OwnerRoutineHelperAction(runtime.ExportState().Society.Society.WorldId, "decisions", "gpt-6-luna");
                const string path = "/api/v1/owner/control/routine-helper";
                using var resume = await SendSignedAsync(host, client, key, deviceId, "/api/v1/owner/control/resume",
                    new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
                using var running = await SendSignedAsync(host, client, key, deviceId, path, action, OwnerHttpBinding.RoutineHelperPayload(action));
                Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
                using var pause = await SendSignedAsync(host, client, key, deviceId, "/api/v1/owner/control/pause",
                    new OwnerControlAction("pause"), OwnerHttpBinding.EmptyPayload("pause"));
                foreach (var changed in new[] { action with { WorldId = "other-world" }, action with { Provider = "jev" }, action with { Model = "other-model" } })
                {
                    var envelope = await CreateSignedRequestAsync(host, client, key, deviceId, path, action, OwnerHttpBinding.RoutineHelperPayload(action));
                    using var tampered = await client.PostAsJsonAsync(path, envelope with { Action = changed });
                    Assert.False(tampered.IsSuccessStatusCode);
                    Assert.Equal(RoutineHelperSettings.Jev, runtime.RoutineHelper);
                }
                var staleWorld = action with { WorldId = "other-world" };
                using var wrongWorld = await SendSignedAsync(host, client, key, deviceId, path, staleWorld, OwnerHttpBinding.RoutineHelperPayload(staleWorld));
                Assert.Equal(HttpStatusCode.Conflict, wrongWorld.StatusCode);
                var defaultJev = action with { Provider = "jev", Model = RoutineHelperSettings.Jev.Model };
                Assert.Equal(0, runtime.JevPolicyRevision);
                using var activated = await SendSignedAsync(host, client, key, deviceId, path, defaultJev, OwnerHttpBinding.RoutineHelperPayload(defaultJev));
                Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
                Assert.True((await activated.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
                Assert.Equal(1, runtime.JevPolicyRevision);
                Assert.Equal(1, host.Services.GetRequiredService<WorldJevPolicy>().Capture().Revision);
                credentials = File.ReadAllBytes(host.Services.GetRequiredService<ProviderConfigurationStore>().Path);
                using var configured = await SendSignedAsync(host, client, key, deviceId, path, action, OwnerHttpBinding.RoutineHelperPayload(action));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                Assert.Equal(new RoutineHelperSettings("decisions", "gpt-6-luna"), runtime.RoutineHelper);
                var snapshot = host.Services.GetRequiredService<OwnerWorldObservationStore>().GetSnapshot();
                Assert.Equal("decisions", snapshot.RoutineHelperProvider);
                Assert.Equal("gpt-6-luna", snapshot.RoutineHelperModel);
                Assert.Contains("owner-routine-helper.v1", host.Services.GetRequiredService<OwnerWorldObservationStore>().GetOwnerHandshake().ServerCapabilities);
                using var repeated = await SendSignedAsync(host, client, key, deviceId, path, action, OwnerHttpBinding.RoutineHelperPayload(action));
                Assert.False((await repeated.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
                checkpoint = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
                using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(checkpoint));
                Assert.Equal(checkpoint, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
                Assert.Equal(credentials, File.ReadAllBytes(host.Services.GetRequiredService<ProviderConfigurationStore>().Path));
            }
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var restartedClient = restarted.CreateClient();
            var loaded = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.Equal(checkpoint, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            Assert.Equal(loaded.RoutineHelper, restarted.Services.GetRequiredService<WorldJevPolicy>().Capture().Helper);
            Assert.Equal(credentials, File.ReadAllBytes(restarted.Services.GetRequiredService<ProviderConfigurationStore>().Path));
        }
        finally { directory.Delete(recursive: true); }
    }

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
                var action = new OwnerJevAssistanceAction(false, runtime.Society.WorldId);
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
                using var tampered = await client.PostAsJsonAsync(path, envelope with { Action = action with { Enabled = true } });
                Assert.False(tampered.IsSuccessStatusCode);
                Assert.True(runtime.JevEnabled);

                using var configured = await SendSignedAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.JevAssistancePayload(action));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                Assert.True((await configured.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
                Assert.False(runtime.JevEnabled);
                Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, runtime.ExportState().SchemaVersion);
                Assert.Equal(1, runtime.JevPolicyRevision);
                savedProviderEpoch = host.Services.GetRequiredService<ConfigurableDecisionProvider>().ProviderEpoch;
                Assert.Equal(initialProviderEpoch + 1, savedProviderEpoch);
                Assert.False(host.Services.GetRequiredService<WorldJevPolicy>().Capture().Enabled);
                Assert.False(host.Services.GetRequiredService<OwnerWorldObservationStore>().GetSnapshot().JevEnabled);
                Assert.Equal(providerBytes, File.ReadAllBytes(providerPath));
                Assert.Contains("owner-jev-assistance.v2",
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
            var reenable = new OwnerJevAssistanceAction(true, restored.Society.WorldId);
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
                var action = new OwnerLifePaceAction(1_460, runtime.Society.WorldId);
                const string path = "/api/v1/owner/control/life-pace";
                using var running = await SendSignedAsync(host, client, key, device.DeviceId, path, action, OwnerHttpBinding.LifePacePayload(action));
                Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
                Assert.Null(runtime.Society.LifeClock);
                using var pause = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/control/pause",
                    new OwnerControlAction("pause"), OwnerHttpBinding.EmptyPayload("pause"));
                Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
                var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, action, OwnerHttpBinding.LifePacePayload(action));
                using var tampered = await client.PostAsJsonAsync(path, envelope with { Action = action with { Rate = 365 } });
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
                Assert.Contains("owner-life-pace.v2", observation.GetOwnerHandshake().ServerCapabilities);
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
            "Gather food.",
            reconnect.Baseline.Snapshot.WorldId);
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
        Assert.Equal(beforeAuthoring.Snapshot.World.Identity.InitialMapManifestDigest, afterAuthoring.Snapshot.InitialMapManifestDigest);
        Assert.Equal(beforeAuthoring.Snapshot.TopologyRevision + 1, afterAuthoring.Snapshot.TopologyRevision);
        Assert.Equal(beforeAuthoring.Snapshot.World.Map.ManifestDigest, afterAuthoring.Snapshot.World.Map.ManifestDigest);
        Assert.Equal(TerrainKind.Mountain, afterAuthoring.Snapshot.CurrentMap.Tiles.Single(tile => tile.Position == water).Terrain);
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
