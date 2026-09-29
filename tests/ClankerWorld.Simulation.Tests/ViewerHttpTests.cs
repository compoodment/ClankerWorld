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

public sealed partial class ViewerHttpTests : IDisposable
{
    // The public pairing budget is host-wide; independent scenarios own independent hosts.
    private readonly ViewerWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private const string ApprovedAssetDigest =
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task PairingVolumeIsBoundedWithoutBlockingAnExistingOwnersSignedRequests()
    {
        using var host = new ViewerWebApplicationFactory();
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = await StartAndActivateAsync(host, client, key);
        for (var index = 0; index < 7; index++)
        {
            using var rejected = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest("invalid"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest("invalid"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var owner = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/reconnect",
            new OwnerReconnectAction(0), OwnerHttpBinding.ReconnectPayload(new OwnerReconnectAction(0)));
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        using var hidden = await client.PostAsJsonAsync("/api/v1/local/pairings", new StartOwnerPairingHttpRequest("invalid"));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var oversized = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest(new string('x', 17_000)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task DamagedUsageMeterKeepsHostReachableAndReportsBlockedAccountingToOwner()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-meter-startup-");
        try
        {
            var path = Path.Combine(directory.FullName, "provider-usage.json");
            const string damaged = "{private sk-secret-accounting";
            File.WriteAllText(path, damaged);
            using var host = new ViewerWebApplicationFactory(directory.FullName,
                configureProviderUsagePath: false);
            var log = new RecordingLogger<ViewerHttpTests>();
            using var configured = host.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
                logging.AddProvider(new RecordingLoggerProvider<ViewerHttpTests>(log))));
            using var client = configured.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(configured, client, key);
            using var response = await SendSignedAsync(configured, client, key, device.DeviceId,
                "/api/v1/owner/usage/status", new OwnerUsageStatusAction(), OwnerHttpBinding.UsageStatusPayload());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var status = await response.Content.ReadFromJsonAsync<ProviderUsageStatus>();
            Assert.NotNull(status);
            Assert.True(status.LimitReached);
            Assert.Equal(ProviderUsageStore.RecoveryMessage, status.AccountingError);
            Assert.DoesNotContain("sk-secret", await response.Content.ReadAsStringAsync());
            Assert.Equal(damaged, File.ReadAllText(path));
            Assert.Contains(log.Messages, message => message.Contains("provider_usage_unavailable outcome=blocked", StringComparison.Ordinal));
            Assert.DoesNotContain(log.Messages, message => message.Contains("sk-secret", StringComparison.Ordinal) ||
                message.Contains(path, StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void DefaultProviderUsageMeterUsesTheConfiguredPrivateStateDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-provider-usage-path-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName,
                configureProviderUsagePath: false);
            using var client = host.CreateClient();
            var usage = host.Services.GetRequiredService<ProviderUsageStore>();
            var ticket = usage.Begin("ollama-cloud", "test-model", "planning");
            usage.Finish(ticket, "failed");
            Assert.True(File.Exists(Path.Combine(directory.FullName, "provider-usage.json")));
            Assert.Equal(1, usage.Capture().Failed);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task NewPrivateWorldRequiresFourConfiguredFoundersAndAnExplicitSignedStart()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-founder-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true,
                legacyPrivateWorld: false);
            using var client = host.CreateClient();
            var placementLog = new RecordingLogger<ViewerHttpTests>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(new RecordingLoggerProvider<ViewerHttpTests>(placementLog));
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.Empty(runtime.Inhabitants);
            using var premature = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
            Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);

            var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
            var slotId = Guid.NewGuid().ToString("N");
            OwnerFounderMoveAction? firstFounderMove = null;
            for (var index = 0; index < positions.Length; index++)
            {
                var id = "founder:" + Guid.NewGuid().ToString("N");
                var cognition = new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini",
                    index == 0 ? "test-secret-key" : null, false, id, slotId,
                    index == 0 ? "Test account" : null);
                var action = new OwnerFounderPlacementAction(id, positions[index].X, positions[index].Y, cognition);
                const string path = "/api/v1/owner/founders/place";
                if (index == 0)
                {
                    var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, action,
                        OwnerHttpBinding.FounderPlacementPayload(action));
                    using var tampered = await client.PostAsJsonAsync(path, envelope with
                    {
                        Action = action with { X = positions[index].X + 1 },
                    });
                    Assert.False(tampered.IsSuccessStatusCode);
                    Assert.Empty(runtime.Inhabitants);
                }
                using var placed = await SendSignedAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.FounderPlacementPayload(action));
                Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
                var receipt = await placed.Content.ReadFromJsonAsync<OwnerFounderPlacementReceipt>();
                Assert.Equal(index + 1, receipt!.Placed);
                if (index == 0)
                {
                    var map = runtime.ExportState().Map;
                    var destination = map.Tiles.Select(tile => tile.Position).First(point =>
                        map.IsBuildable(point) && !positions.Contains(point) &&
                        !map.CampObjects.Any(item => item.Position == point) &&
                        !map.Resources.Any(item => item.Position == point));
                    firstFounderMove = new OwnerFounderMoveAction(id, destination.X, destination.Y);
                    const string movePath = "/api/v1/owner/founders/move";
                    var signedMove = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                        movePath, firstFounderMove, OwnerHttpBinding.FounderMovePayload(firstFounderMove));
                    using var tamperedMove = await client.PostAsJsonAsync(movePath, signedMove with
                    {
                        Action = firstFounderMove with { X = destination.X + 1 },
                    });
                    Assert.False(tamperedMove.IsSuccessStatusCode);
                    Assert.Equal(positions[0], runtime.Inhabitants.Single(person => person.InhabitantId == id).Position);
                    using var moved = await SendSignedAsync(host, client, key, device.DeviceId,
                        movePath, firstFounderMove, OwnerHttpBinding.FounderMovePayload(firstFounderMove));
                    Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
                    Assert.True((await moved.Content.ReadFromJsonAsync<OwnerFounderMoveReceipt>())!.Changed);
                    Assert.Equal(destination, runtime.Inhabitants.Single(person => person.InhabitantId == id).Position);
                    Assert.Contains(placementLog.Messages, message => message.Contains(
                        "founder_setup outcome=moved world_tick=0", StringComparison.Ordinal));
                }
            }

            Assert.True(runtime.Society.IsPaused);
            Assert.Equal(2, runtime.Society.Households.Single(item => item.Id == "household:camp-beta").MemberIds.Count);
            var lastFounder = runtime.FounderSetup!.FounderIds[^1];
            var undo = new OwnerFounderUndoAction(lastFounder);
            const string undoPath = "/api/v1/owner/founders/undo";
            var signedUndo = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                undoPath, undo, OwnerHttpBinding.FounderUndoPayload(undo));
            using var tamperedUndo = await client.PostAsJsonAsync(undoPath, signedUndo with
            {
                Action = undo with { FounderId = runtime.FounderSetup.FounderIds[0] },
            });
            Assert.False(tamperedUndo.IsSuccessStatusCode);
            Assert.Equal(4, runtime.FounderSetup.FounderIds.Count);
            using var undone = await SendSignedAsync(host, client, key, device.DeviceId,
                undoPath, undo, OwnerHttpBinding.FounderUndoPayload(undo));
            Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
            Assert.Equal(3, (await undone.Content.ReadFromJsonAsync<OwnerFounderUndoReceipt>())!.Placed);
            Assert.DoesNotContain(lastFounder, runtime.FounderSetup.FounderIds);
            Assert.Equal(runtime.FounderSetup.FounderIds[^1], host.Services
                .GetRequiredService<OwnerWorldObservationStore>().GetSnapshot().FounderSetup!.LastFounderId);
            var providerStatus = host.Services.GetRequiredService<ProviderConfigurationStore>().CaptureStatus();
            Assert.DoesNotContain(providerStatus.Assignments ?? [], item => item.InhabitantId == lastFounder);
            Assert.Contains(providerStatus.CredentialSlots ?? [], item => item.Id == slotId);
            Assert.Contains(placementLog.Messages, message => message.Contains(
                "founder_setup outcome=undone world_tick=0", StringComparison.Ordinal));
            var replacementId = "founder:" + Guid.NewGuid().ToString("N");
            var replacement = new OwnerFounderPlacementAction(replacementId, positions[3].X, positions[3].Y,
                new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini", null,
                    false, replacementId, slotId));
            using var replaced = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/founders/place", replacement,
                OwnerHttpBinding.FounderPlacementPayload(replacement));
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.Equal(4, runtime.FounderSetup.FounderIds.Count);
            var start = new OwnerControlAction("start-world");
            using var started = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/start-world", start, OwnerHttpBinding.EmptyPayload("start-world"));
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            Assert.False(runtime.Society.IsPaused);
            Assert.True(runtime.FounderSetup!.Started);
            using var lateMove = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/founders/move", firstFounderMove!,
                OwnerHttpBinding.FounderMovePayload(firstFounderMove!));
            Assert.Equal(HttpStatusCode.Conflict, lateMove.StatusCode);
            using var lateUndo = await SendSignedAsync(host, client, key, device.DeviceId,
                undoPath, new OwnerFounderUndoAction(replacementId),
                OwnerHttpBinding.FounderUndoPayload(new OwnerFounderUndoAction(replacementId)));
            Assert.Equal(HttpStatusCode.Conflict, lateUndo.StatusCode);
            var agentId = "agent:" + Guid.NewGuid().ToString("N");
            var adult = new OwnerAgentPlacementAction(agentId, 4, 2,
                new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini", null,
                    false, agentId, slotId));
            const string agentPath = "/api/v1/owner/agents/place";
            var signedAdult = await CreateSignedRequestAsync(host, client, key, device.DeviceId, agentPath,
                adult, OwnerHttpBinding.AgentPlacementPayload(adult));
            using var tamperedAdult = await client.PostAsJsonAsync(agentPath, signedAdult with
            {
                Action = adult with { X = 5 },
            });
            Assert.False(tamperedAdult.IsSuccessStatusCode);
            Assert.Equal(4, runtime.Inhabitants.Count);
            using var added = await SendSignedAsync(host, client, key, device.DeviceId, agentPath,
                adult, OwnerHttpBinding.AgentPlacementPayload(adult));
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var addedReceipt = await added.Content.ReadFromJsonAsync<OwnerAgentPlacementReceipt>();
            Assert.Null(addedReceipt!.HouseholdId);
            Assert.Equal(5, runtime.Inhabitants.Count);
            Assert.Null(runtime.Society.GetInhabitant(agentId).HouseholdId);
            Assert.Contains(agentId, runtime.Towns.Single().ResidentIds);
            var observedAdult = host.Services.GetRequiredService<OwnerWorldObservationStore>()
                .GetSnapshot().Inhabitants.Single(person => person.Id == agentId);
            Assert.Equal("active", observedAdult.Lifecycle);
            Assert.Equal("unhoused", observedAdult.DecisionFactors.Single(factor => factor.Key == "household").Detail);
            using var duplicate = await SendSignedAsync(host, client, key, device.DeviceId, agentPath,
                adult, OwnerHttpBinding.AgentPlacementPayload(adult));
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            Assert.Contains(placementLog.Messages, message => message.Contains("AgentPlaced AgentId=" + agentId, StringComparison.Ordinal));
            var rename = new OwnerAgentRenameAction(agentId, "Nova");
            const string renamePath = "/api/v1/owner/agents/rename";
            var signedRename = await CreateSignedRequestAsync(host, client, key, device.DeviceId, renamePath,
                rename, OwnerHttpBinding.AgentRenamePayload(rename));
            using var tamperedRename = await client.PostAsJsonAsync(renamePath, signedRename with
            {
                Action = rename with { Name = "Someone else" },
            });
            Assert.False(tamperedRename.IsSuccessStatusCode);
            Assert.Equal("New agent", runtime.Society.GetInhabitant(agentId).Name);
            using var renamed = await SendSignedAsync(host, client, key, device.DeviceId, renamePath,
                rename, OwnerHttpBinding.AgentRenamePayload(rename));
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.Equal("Nova", host.Services.GetRequiredService<OwnerWorldObservationStore>()
                .GetSnapshot().Inhabitants.Single(person => person.Id == agentId).DisplayName);
            Assert.Contains(placementLog.Messages, message => message.Contains("AgentRenamed AgentId=" + agentId, StringComparison.Ordinal));
            Assert.DoesNotContain(placementLog.Messages, message => message.Contains("test-secret-key", StringComparison.Ordinal));
            Assert.DoesNotContain("test-secret-key", File.ReadAllText(host.Services.GetRequiredService<PrivateWorldStateFile>().Path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
    public async Task UnpairedClientsCanDiscoverPairingButCannotReadTheWorld()
    {
        using var client = factory.CreateClient();

        var handshake = await client.GetFromJsonAsync<ViewerHandshake>("/api/v1/handshake");
        using var world = await client.GetAsync("/api/v1/world");
        using var events = await client.GetAsync("/api/v1/events?afterEventId=0");
        using var reconnect = await client.GetAsync("/api/v1/reconnect?afterEventId=0");
        using var attemptedWrite = await client.PostAsync("/api/v1/world", content: null);
        using var remotePairingApproval = await client.PostAsJsonAsync(
            "/api/v1/local/pairings/pairing_not_real/approve",
            new LocalPairingApprovalHttpRequest("000000"));
        using var remoteRecoveryRevoke = await client.PostAsJsonAsync(
            "/api/v1/local/devices/device_not_real/revoke",
            new LocalDeviceRevokeHttpRequest(null));
        using var unsignedDeviceList = await client.PostAsJsonAsync(
            "/api/v1/owner/devices/list",
            new { });
        var page = await client.GetStringAsync("/");

        Assert.NotNull(handshake);
        Assert.Equal(new ProtocolVersion(1, 1), handshake.Protocol);
        Assert.Equal(["owner-device-pairing.v1"], handshake.ServerCapabilities);
        Assert.Equal(HttpStatusCode.Unauthorized, world.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, events.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reconnect.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, attemptedWrite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remotePairingApproval.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remoteRecoveryRevoke.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsignedDeviceList.StatusCode);
        Assert.Contains("read-only deterministic inspection", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PairingBackpressureUsesTooManyRequestsInsteadOfCreatingUnboundedState()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-pairing-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var host = new ViewerWebApplicationFactory(directory);
            using var client = host.CreateClient();
            for (var index = 0; index < OwnerAuthorityStore.MaximumPendingPairings; index++)
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                using var started = await client.PostAsJsonAsync(
                    "/api/v1/pairings",
                    new StartOwnerPairingHttpRequest(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
                Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            }

            using var overflowKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var overflow = await client.PostAsJsonAsync(
                "/api/v1/pairings",
                new StartOwnerPairingHttpRequest(Convert.ToBase64String(overflowKey.ExportSubjectPublicKeyInfo())));

            Assert.Equal((HttpStatusCode)429, overflow.StatusCode);
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
    public async Task RejectedReconnectDoesNotCreateAClientPresenceLease()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-presence-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var host = new ViewerWebApplicationFactory(directory);
            using var client = host.CreateClient();

            using var rejected = await client.PostAsJsonAsync(
                "/api/v1/owner/reconnect",
                new { });

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.False(host.Services.GetRequiredService<OwnerClientPresenceLease>().HasActiveClient);
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

    [Fact]
    public async Task PairedOwnerCanAttachOnlyAnExactReferenceFromTheHostConfiguredCatalog()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-approved-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = System.IO.Path.Combine(directory, "approved-assets.json");
            File.WriteAllText(
                catalogPath,
                $$"""{"schemaVersion":1,"references":[{"assetId":"portrait-alice","assetDigest":"{{ApprovedAssetDigest}}"}]}""");

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory, catalogPath);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);

            using var pause = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/control/pause",
                new OwnerControlAction("pause"),
                OwnerHttpBinding.EmptyPayload("pause"));
            Assert.Equal(HttpStatusCode.OK, pause.StatusCode);

            var allowedAction = new OwnerAuthoringBatchAction(
                "approved-asset-http",
                [new OwnerAuthoringOperationAction(
                    "add_approved_asset_reference",
                    "portrait-alice",
                    ApprovedAssetDigest,
                    null,
                    null,
                    null,
                    null)]);
            using var allowed = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/authoring",
                allowedAction,
                OwnerHttpBinding.AuthoringPayload(allowedAction));
            var allowedReceipt = await allowed.Content.ReadFromJsonAsync<OwnerAuthoringBatchReceipt>();

            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.NotNull(allowedReceipt);
            Assert.True(allowedReceipt.Applied, allowedReceipt.Failure);

            var rejectedAction = allowedAction with
            {
                BatchId = "unapproved-asset-http",
                Operations = [new OwnerAuthoringOperationAction(
                    "add_approved_asset_reference",
                    "portrait-bob",
                    ApprovedAssetDigest,
                    null,
                    null,
                    null,
                    null)],
            };
            using var rejected = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/authoring",
                rejectedAction,
                OwnerHttpBinding.AuthoringPayload(rejectedAction));
            var rejectedReceipt = await rejected.Content.ReadFromJsonAsync<OwnerAuthoringBatchReceipt>();

            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.NotNull(rejectedReceipt);
            Assert.False(rejectedReceipt.Applied);
            Assert.Contains("server-owned asset catalog", rejectedReceipt.Failure, StringComparison.Ordinal);
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
    public async Task PairedOwnerCanGovernPrivateContentThroughTheSignedLifecycle()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-private-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory, null, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var packageDigest =
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var packageVersion = ContentVersion.Parse("1.0.0");
            var building = new BuildingDefinition(
                packageDigest,
                "camp-kitchen",
                packageVersion,
                "Camp kitchen",
                1,
                1,
                2,
                [new ContentQuantity("wood", 2)],
                ["camp"]);
            var package = new OwnerContentPackageAction(
                "camp-recipes",
                "1.0.0",
                packageDigest,
                [],
                [new OwnerContentDefinitionAction(
                    BuildingDefinition.SchemaKind,
                    building.LocalId,
                    building.Version.ToString(),
                    building.DisplayName,
                    building.PayloadDigest,
                    """{"schema":"building/v1","width":1,"height":1,"capacity":2,"buildCosts":[{"resourceId":"wood","amount":2}],"tags":["camp"]}""")],
                []);

            using var proposed = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/propose",
                package,
                OwnerContentBinding.ProposePayload(package));
            var proposedReceipt = await proposed.Content.ReadFromJsonAsync<OwnerContentPackageReceipt>();
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            Assert.Equal("proposed", proposedReceipt!.Lifecycle);
            Assert.Matches("^sha256:[0-9a-f]{64}$", proposedReceipt.ManifestDigest);

            var packageId = new OwnerContentPackageIdAction(package.PackageId);
            using var validated = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/validate",
                packageId,
                OwnerContentBinding.PackageIdPayload("validate", packageId));
            var validatedReceipt = await validated.Content.ReadFromJsonAsync<OwnerContentPackageReceipt>();
            Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
            Assert.Equal("validated", validatedReceipt!.Lifecycle);
            Assert.NotNull(validatedReceipt.LockDigest);

            using var approved = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/approve",
                packageId,
                OwnerContentBinding.PackageIdPayload("approve", packageId));
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

            using var staged = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/stage",
                packageId,
                OwnerContentBinding.PackageIdPayload("stage", packageId));
            var stagedReceipt = await staged.Content.ReadFromJsonAsync<OwnerContentPackageReceipt>();
            Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
            Assert.Equal("staged", stagedReceipt!.Lifecycle);
            Assert.Equal(0, stagedReceipt.StagedTick);

            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            using var resume = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
            Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
            _ = await runtime.AdvanceOneTickAsync();
            host.Services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            Assert.Equal("active", runtime.Content.Packages.Single().Lifecycle.ToString().ToLowerInvariant());
            Assert.Equal(building.CanonicalId, Assert.Single(runtime.WorldContent.Buildings).CanonicalId);

            var occupiedMapPositions = runtime.ExportState().Map.CampObjects.Select(item => item.Position)
                .Concat(runtime.ExportState().Map.Resources.Select(item => item.Position))
                .ToHashSet();
            var worker = runtime.Inhabitants.First(item => !occupiedMapPositions.Contains(item.Position));
            var placementAction = new OwnerBuildingPlacementAction(
                "camp-kitchen-one",
                building.CanonicalId,
                worker.Position.X,
                worker.Position.Y);
            using var placed = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/buildings/place",
                placementAction,
                OwnerContentBinding.BuildingPlacementPayload(placementAction));
            var placementReceipt = await placed.Content.ReadFromJsonAsync<BuildingPlacementResult>();
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            Assert.True(placementReceipt!.Applied, placementReceipt.Failure);
            Assert.Single(runtime.WorldSimulation.Buildings);

            var rollbackLogger = new RecordingLogger<PrivateWorldRuntimeService>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(new RecordingLoggerProvider<PrivateWorldRuntimeService>(rollbackLogger));
            var rollback = new OwnerContentRollbackAction(package.PackageId, "sk-private-rollback-reason");
            var beforeRollback = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using var rolledBack = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/rollback",
                rollback,
                OwnerContentBinding.RollbackPayload(rollback));
            Assert.Equal(HttpStatusCode.Conflict, rolledBack.StatusCode);
            var rollbackFailure = await rolledBack.Content.ReadFromJsonAsync<OwnerControlFailure>();
            Assert.Equal("content_rejected", rollbackFailure!.Code);
            Assert.Equal(beforeRollback, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Single(runtime.WorldContent.Buildings);
            Assert.Single(runtime.WorldSimulation.Buildings);
            Assert.Contains(rollbackLogger.Messages, message => message.Contains("content_rollback", StringComparison.Ordinal) &&
                message.Contains("package=camp-recipes outcome=rejected", StringComparison.Ordinal));
            Assert.DoesNotContain(rollbackLogger.Messages, message => message.Contains(rollback.Reason, StringComparison.Ordinal));
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
    public async Task ReplayedChallengeIsRejectedBeforeASecondControlCanReachTheRuntime()
    {
        using var client = factory.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pairing = await StartAndActivateAsync(client, key, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        var envelope = await CreateSignedRequestAsync(
            client,
            key,
            pairing.DeviceId,
            "/api/v1/owner/control/pause",
            new OwnerControlAction("pause"),
            OwnerHttpBinding.EmptyPayload("pause"));

        using var first = await client.PostAsJsonAsync("/api/v1/owner/control/pause", envelope);
        using var replay = await client.PostAsJsonAsync("/api/v1/owner/control/pause", envelope);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        var runtime = factory.Services.GetRequiredService<OwnerWorldRuntime>();
        Assert.True(runtime.Capture().Snapshot.IsPaused);
        Assert.Single(runtime.Capture().Events, worldEvent => worldEvent.Kind == "paused");
    }

    [Fact]
    public async Task PairedOwnerCanApproveAndRevokeAnotherDeviceThroughSignedRequests()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-device-management-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory);
            using var client = host.CreateClient();
            var firstDevice = await StartAndActivateAsync(host, client, firstKey);

            using var start = await client.PostAsJsonAsync(
                "/api/v1/pairings",
                new StartOwnerPairingHttpRequest(Convert.ToBase64String(secondKey.ExportSubjectPublicKeyInfo())));
            var pending = await start.Content.ReadFromJsonAsync<OwnerPairingStart>();
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            Assert.NotNull(pending);

            var approveAction = new OwnerPairingApprovalAction(pending.PairingId, pending.PairingCode);
            using var approve = await SendSignedAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/pairings/approve",
                approveAction,
                OwnerHttpBinding.PairingApprovalPayload(approveAction));
            var approval = await approve.Content.ReadFromJsonAsync<OwnerPairingApproval>();
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.NotNull(approval);
            Assert.Equal(pending.DeviceId, approval.DeviceId);

            var activation = new ActivateOwnerPairingHttpRequest(
                pending.PairingId,
                pending.ActivationCanonicalProof,
                Sign(secondKey, pending.ActivationCanonicalProof));
            using var activate = await client.PostAsJsonAsync("/api/v1/pairings/activate", activation);
            var secondDevice = await activate.Content.ReadFromJsonAsync<OwnerDevice>();
            Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
            Assert.NotNull(secondDevice);
            Assert.Equal(OwnerDeviceState.Active, secondDevice.State);

            var listAction = new OwnerDeviceListAction();
            var activeListEnvelope = await CreateSignedRequestAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/devices/list",
                listAction,
                OwnerHttpBinding.DeviceListPayload());
            using var activeList = await client.PostAsJsonAsync(
                "/api/v1/owner/devices/list",
                activeListEnvelope);
            using var activeListReplay = await client.PostAsJsonAsync(
                "/api/v1/owner/devices/list",
                activeListEnvelope);
            var activeDevices = await activeList.Content.ReadFromJsonAsync<OwnerDevice[]>();

            Assert.Equal(HttpStatusCode.OK, activeList.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, activeListReplay.StatusCode);
            Assert.NotNull(activeDevices);
            Assert.Equal(2, activeDevices.Length);
            Assert.All(activeDevices, device => Assert.Equal(OwnerDeviceState.Active, device.State));
            var listedFirstDevice = Assert.Single(activeDevices, device => device.DeviceId == firstDevice.DeviceId);
            var listedSecondDevice = Assert.Single(activeDevices, device => device.DeviceId == secondDevice.DeviceId);
            Assert.Equal(firstDevice.PublicKeyFingerprint, listedFirstDevice.PublicKeyFingerprint);
            Assert.Equal(secondDevice.PublicKeyFingerprint, listedSecondDevice.PublicKeyFingerprint);
            Assert.Equal(firstDevice.PublicKeySpkiBase64, listedFirstDevice.PublicKeySpkiBase64);
            Assert.Equal(secondDevice.PublicKeySpkiBase64, listedSecondDevice.PublicKeySpkiBase64);
            Assert.Null(listedFirstDevice.RevokedAtUtc);
            Assert.Null(listedSecondDevice.RevokedAtUtc);

            var revokeAction = new OwnerDeviceManagementAction(secondDevice.DeviceId);
            using var revoke = await SendSignedAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/devices/revoke",
                revokeAction,
                OwnerHttpBinding.DeviceManagementPayload(revokeAction));
            var revoked = await revoke.Content.ReadFromJsonAsync<OwnerDevice>();
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            Assert.NotNull(revoked);
            Assert.Equal(OwnerDeviceState.Revoked, revoked.State);

            using var revokedList = await SendSignedAsync(
                host,
                client,
                firstKey,
                firstDevice.DeviceId,
                "/api/v1/owner/devices/list",
                listAction,
                OwnerHttpBinding.DeviceListPayload());
            var devicesAfterRevoke = await revokedList.Content.ReadFromJsonAsync<OwnerDevice[]>();

            Assert.Equal(HttpStatusCode.OK, revokedList.StatusCode);
            Assert.NotNull(devicesAfterRevoke);
            Assert.Equal(2, devicesAfterRevoke.Length);
            Assert.Equal(
                OwnerDeviceState.Active,
                Assert.Single(devicesAfterRevoke, device => device.DeviceId == firstDevice.DeviceId).State);
            var listedRevokedDevice = Assert.Single(devicesAfterRevoke, device => device.DeviceId == secondDevice.DeviceId);
            Assert.Equal(OwnerDeviceState.Revoked, listedRevokedDevice.State);
            Assert.NotNull(listedRevokedDevice.RevokedAtUtc);

            var authority = host.Services.GetRequiredService<OwnerAuthorityStore>();
            const string rejectedRequestId = "revoked-device-challenge";
            var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(
                authority.Identity,
                secondDevice.DeviceId,
                rejectedRequestId);
            using var rejectedChallenge = await client.PostAsJsonAsync(
                "/api/v1/owner/challenges",
                new IssueOwnerChallengeHttpRequest(
                    secondDevice.DeviceId,
                    rejectedRequestId,
                    issueProof,
                    Sign(secondKey, issueProof)));

            Assert.Equal(HttpStatusCode.Forbidden, rejectedChallenge.StatusCode);
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
    public async Task SignedUsageCapPausesWorldPersistsAndRequiresOwnerAllowanceBeforeResume()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            OwnerDevice device;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                device = await StartAndActivateAsync(host, client, key);
                var cap = new ProviderUsageLimitAction(1);
                using var configured = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/usage/limit", cap, OwnerHttpBinding.UsageLimitPayload(cap));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                var usage = host.Services.GetRequiredService<ProviderUsageStore>();
                var ticket = usage.Begin("openai", "test-model", "planning");
                usage.Finish(ticket, "completed", 4, 2);
                Assert.True(host.Services.GetRequiredService<PrivateWorldRuntime>().Society.IsPaused);
                var statusAction = new OwnerUsageStatusAction();
                using var observed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/usage/status", statusAction, OwnerHttpBinding.UsageStatusPayload());
                Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
                var meter = await observed.Content.ReadFromJsonAsync<ProviderUsageStatus>();
                Assert.True(meter!.LimitReached);
                Assert.Equal(1, meter.Attempts);
                Assert.Equal(4, meter.InputTokens);
                using var refused = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"),
                    OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            }
            using (var restarted = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = restarted.CreateClient())
            {
                Assert.True(restarted.Services.GetRequiredService<PrivateWorldRuntime>().Society.IsPaused);
                var usage = restarted.Services.GetRequiredService<ProviderUsageStore>();
                var statusAction = new OwnerUsageStatusAction();
                using var response = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/usage/status", statusAction, OwnerHttpBinding.UsageStatusPayload());
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var meter = await response.Content.ReadFromJsonAsync<ProviderUsageStatus>();
                Assert.Equal(1, meter!.Attempts);
                Assert.Equal(1, meter.AttemptLimit);
                using var refused = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"),
                    OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                var grant = new ProviderUsageLimitAction(null, AdditionalCalls: 2);
                var signed = await CreateSignedRequestAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/usage/limit", grant, OwnerHttpBinding.UsageLimitPayload(grant));
                using var tampered = await client.PostAsJsonAsync("/api/v1/owner/usage/limit",
                    signed with { Action = grant with { AdditionalCalls = 100 } });
                Assert.False(tampered.IsSuccessStatusCode);
                Assert.Equal(1, usage.Capture().AttemptLimit);
                using var consent = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/usage/limit", grant, OwnerHttpBinding.UsageLimitPayload(grant));
                Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
                using var resumed = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"),
                    OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
                Assert.False(restarted.Services.GetRequiredService<PrivateWorldRuntime>().Society.IsPaused);
                Assert.Equal(3, usage.Capture().AttemptLimit);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task PairedOwnerCanConfigureHostedCognitionWithoutEchoingOrSavingTheKeyInTheWorld()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        const string secret = "openai-player-secret-that-must-not-echo";
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            OwnerDevice pairedDevice;
            using (var host = new ViewerWebApplicationFactory(directory, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                pairedDevice = await StartAndActivateAsync(host, client, key);
                var configureAction = new OwnerProviderConfigurationAction(
                    "planning",
                    "openai",
                    "test-openai-model",
                    secret,
                    ForgetCredential: false);
                using var configure = await SendSignedAsync(
                    host,
                    client,
                    key,
                    pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure",
                    configureAction,
                    OwnerHttpBinding.ProviderConfigurationPayload(configureAction));
                var responseText = await configure.Content.ReadAsStringAsync();
                var status = System.Text.Json.JsonSerializer.Deserialize<OwnerProviderConfigurationStatus>(
                    responseText,
                    WebJsonOptions);

                Assert.Equal(HttpStatusCode.OK, configure.StatusCode);
                Assert.NotNull(status);
                Assert.Equal("deterministic", status.RoutineProvider);
                Assert.Equal("openai", status.PlanningProvider);
                Assert.True(status.Providers.Single(item => item.Provider == "openai").HasCredential);
                Assert.DoesNotContain(secret, responseText, StringComparison.Ordinal);

                var personal = new OwnerProviderConfigurationAction("planning", "deterministic", null, null, false, "founder-scout");
                using var personalResponse = await SendSignedAsync(host, client, key, pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure", personal, OwnerHttpBinding.ProviderConfigurationPayload(personal));
                Assert.Equal(HttpStatusCode.OK, personalResponse.StatusCode);
                var personalStatus = await personalResponse.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
                Assert.Equal("founder-scout", Assert.Single(personalStatus!.Assignments!).InhabitantId);
                Assert.Equal("openai", personalStatus.PlanningProvider);

                var tampered = personal with { InhabitantId = "founder-rowan" };
                using var tamperedResponse = await SendSignedAsync(host, client, key, pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure", tampered, OwnerHttpBinding.ProviderConfigurationPayload(personal));
                Assert.False(tamperedResponse.IsSuccessStatusCode);
                Assert.Equal(personalStatus.Revision, host.Services.GetRequiredService<ProviderConfigurationStore>().CaptureStatus().Revision);

                var missing = personal with { InhabitantId = "not-an-inhabitant" };
                using var missingResponse = await SendSignedAsync(host, client, key, pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure", missing, OwnerHttpBinding.ProviderConfigurationPayload(missing));
                Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
                Assert.Equal(personalStatus.Revision, host.Services.GetRequiredService<ProviderConfigurationStore>().CaptureStatus().Revision);

                var providerPath = host.Services.GetRequiredService<ProviderConfigurationStore>().Path;
                Assert.Equal(secret, new ProviderConfigurationStore(providerPath,
                    new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null)).CaptureRuntimeConfiguration().OpenAi.ApiKey);
                if (OperatingSystem.IsWindows())
                    Assert.DoesNotContain(secret, File.ReadAllText(providerPath), StringComparison.Ordinal);
                foreach (var file in Directory.EnumerateFiles(directory).Where(path => path != providerPath))
                {
                    Assert.DoesNotContain(secret, File.ReadAllText(file), StringComparison.Ordinal);
                }

                if (!OperatingSystem.IsWindows())
                {
                    var mode = File.GetUnixFileMode(providerPath);
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
                }
            }

            using (var restartedHost = new ViewerWebApplicationFactory(directory, null, privateWorld: true))
            using (var restartedClient = restartedHost.CreateClient())
            {
                var statusAction = new OwnerProviderStatusAction();
                using var statusResponse = await SendSignedAsync(
                    restartedHost,
                    restartedClient,
                    key,
                    pairedDevice.DeviceId,
                    "/api/v1/owner/providers/status",
                    statusAction,
                    OwnerHttpBinding.ProviderStatusPayload());
                var restored = await statusResponse.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
                Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
                Assert.Equal("openai", restored!.PlanningProvider);

                var forgetAction = new OwnerProviderConfigurationAction(
                    "planning",
                    "openai",
                    "test-openai-model",
                    null,
                    ForgetCredential: true);
                using var forgottenResponse = await SendSignedAsync(
                    restartedHost,
                    restartedClient,
                    key,
                    pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure",
                    forgetAction,
                    OwnerHttpBinding.ProviderConfigurationPayload(forgetAction));
                var forgotten = await forgottenResponse.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
                Assert.Equal(HttpStatusCode.OK, forgottenResponse.StatusCode);
                Assert.Equal("deterministic", forgotten!.PlanningProvider);
                Assert.False(forgotten.Providers.Single(item => item.Provider == "openai").HasCredential);
                Assert.DoesNotContain(
                    secret,
                    File.ReadAllText(restartedHost.Services.GetRequiredService<ProviderConfigurationStore>().Path),
                    StringComparison.Ordinal);
            }
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
    public async Task OnlySignedOwnerCanDeleteUnusedNamedCredentialSlot()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-slot-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var store = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var slotId = Guid.NewGuid().ToString("N");
            const string secret = "delete-me-secret";
            _ = store.Configure(new("personal", "openai", "model", secret, false,
                "inhabitant-test", slotId, "Discard"));
            var action = new OwnerCredentialSlotDeletionAction(slotId);
            const string endpoint = "/api/v1/owner/providers/slots/delete";
            using var tampered = await SendSignedAsync(host, client, key, device.DeviceId,
                endpoint, action, OwnerHttpBinding.CredentialSlotDeletionPayload(
                    action with { CredentialSlotId = Guid.NewGuid().ToString("N") }));
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
            Assert.Equal(secret, Assert.Single(new ProviderConfigurationStore(store.Path,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null)).CaptureRuntimeConfiguration().CredentialSlots!).ApiKey);
            if (OperatingSystem.IsWindows())
                Assert.DoesNotContain(secret, File.ReadAllText(store.Path), StringComparison.Ordinal);

            using var assigned = await SendSignedAsync(host, client, key, device.DeviceId,
                endpoint, action, OwnerHttpBinding.CredentialSlotDeletionPayload(action));
            Assert.Equal(HttpStatusCode.Conflict, assigned.StatusCode);
            Assert.DoesNotContain(secret, await assigned.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            _ = store.Configure(new("personal", "inherit", null, null, false, "inhabitant-test"));
            using var removed = await SendSignedAsync(host, client, key, device.DeviceId,
                endpoint, action, OwnerHttpBinding.CredentialSlotDeletionPayload(action));
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            var status = await removed.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
            Assert.Empty(status!.CredentialSlots!);
            Assert.Empty(new ProviderConfigurationStore(store.Path,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null)).CaptureRuntimeConfiguration().CredentialSlots!);
            Assert.DoesNotContain(secret, File.ReadAllText(store.Path), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, await removed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PairedOwnerReconnectAndPausedWorldSurviveAHostRestart()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            OwnerDevice pairedDevice;
            using (var firstHost = new ViewerWebApplicationFactory(directory))
            using (var firstClient = firstHost.CreateClient())
            {
                pairedDevice = await StartAndActivateAsync(firstHost, firstClient, key);
                var firstRuntime = firstHost.Services.GetRequiredService<OwnerWorldRuntime>();
                Assert.True(firstRuntime.Pause($"owner-device:{pairedDevice.DeviceId}"));
                firstHost.Services.GetRequiredService<OwnerWorldStateFile>().Save(firstRuntime);
            }

            using var restartedHost = new ViewerWebApplicationFactory(directory);
            using var restartedClient = restartedHost.CreateClient();
            using var reconnect = await SendSignedAsync(
                restartedHost,
                restartedClient,
                key,
                pairedDevice.DeviceId,
                "/api/v1/owner/reconnect",
                new OwnerReconnectAction(0),
                OwnerHttpBinding.ReconnectPayload(new OwnerReconnectAction(0)));
            var baseline = await reconnect.Content.ReadFromJsonAsync<ViewerOwnerReconnect>();

            Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
            Assert.NotNull(baseline);
            Assert.True(baseline.Baseline.Snapshot.Authoring!.IsPaused);
            Assert.Contains(
                baseline.Baseline.Events.Events,
                worldEvent => worldEvent.Kind == "paused" &&
                    worldEvent.Detail.Contains($"issuer:owner-device:{pairedDevice.DeviceId}", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private async Task<OwnerDevice> StartAndActivateAsync(HttpClient client, ECDsa key, string publicKey)
    {
        using var start = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest(publicKey));
        var pairing = await start.Content.ReadFromJsonAsync<OwnerPairingStart>();
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(pairing);

        var authority = factory.Services.GetRequiredService<OwnerAuthorityStore>();
        var stateFile = factory.Services.GetRequiredService<OwnerAuthorityStateFile>();
        Assert.True(authority.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode).IsSuccess);
        stateFile.Save(authority);

        var activation = new ActivateOwnerPairingHttpRequest(
            pairing.PairingId,
            pairing.ActivationCanonicalProof,
            Sign(key, pairing.ActivationCanonicalProof));
        using var activated = await client.PostAsJsonAsync("/api/v1/pairings/activate", activation);
        var device = await activated.Content.ReadFromJsonAsync<OwnerDevice>();
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.NotNull(device);
        return device;
    }

    private static async Task<OwnerDevice> StartAndActivateAsync(
        WebApplicationFactory<Program> host,
        HttpClient client,
        ECDsa key)
    {
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        using var start = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest(publicKey));
        var pairing = await start.Content.ReadFromJsonAsync<OwnerPairingStart>();
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(pairing);

        var authority = host.Services.GetRequiredService<OwnerAuthorityStore>();
        var stateFile = host.Services.GetRequiredService<OwnerAuthorityStateFile>();
        Assert.True(authority.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode).IsSuccess);
        stateFile.Save(authority);

        var activation = new ActivateOwnerPairingHttpRequest(
            pairing.PairingId,
            pairing.ActivationCanonicalProof,
            Sign(key, pairing.ActivationCanonicalProof));
        using var activated = await client.PostAsJsonAsync("/api/v1/pairings/activate", activation);
        var device = await activated.Content.ReadFromJsonAsync<OwnerDevice>();
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.NotNull(device);
        return device;
    }

    private async Task<HttpResponseMessage> SendSignedAsync<TAction>(
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var envelope = await CreateSignedRequestAsync(client, key, deviceId, path, action, canonicalPayload);
        return await client.PostAsJsonAsync(path, envelope);
    }

    private static async Task<HttpResponseMessage> SendSignedAsync<TAction>(
        WebApplicationFactory<Program> host,
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var envelope = await CreateSignedRequestAsync(host, client, key, deviceId, path, action, canonicalPayload);
        return await client.PostAsJsonAsync(path, envelope);
    }

    private async Task<OwnerSignedHttpRequest<TAction>> CreateSignedRequestAsync<TAction>(
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var authority = factory.Services.GetRequiredService<OwnerAuthorityStore>();
        var requestId = $"request-{Guid.NewGuid():N}";
        var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(authority.Identity, deviceId, requestId);
        var challengeRequest = new IssueOwnerChallengeHttpRequest(
            deviceId,
            requestId,
            issueProof,
            Sign(key, issueProof));
        using var issued = await client.PostAsJsonAsync("/api/v1/owner/challenges", challengeRequest);
        var challenge = await issued.Content.ReadFromJsonAsync<OwnerChallenge>();
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.NotNull(challenge);

        var binding = OwnerHttpBinding.Create("POST", path, requestId, canonicalPayload);
        var consumeProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
            authority.Identity,
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding);
        return new OwnerSignedHttpRequest<TAction>(
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding,
            consumeProof,
            Sign(key, consumeProof),
            requestId,
            action);
    }

    private static async Task<OwnerSignedHttpRequest<TAction>> CreateSignedRequestAsync<TAction>(
        WebApplicationFactory<Program> host,
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var authority = host.Services.GetRequiredService<OwnerAuthorityStore>();
        var requestId = $"request-{Guid.NewGuid():N}";
        var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(authority.Identity, deviceId, requestId);
        var challengeRequest = new IssueOwnerChallengeHttpRequest(
            deviceId,
            requestId,
            issueProof,
            Sign(key, issueProof));
        using var issued = await client.PostAsJsonAsync("/api/v1/owner/challenges", challengeRequest);
        var challenge = await issued.Content.ReadFromJsonAsync<OwnerChallenge>();
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.NotNull(challenge);

        var binding = OwnerHttpBinding.Create("POST", path, requestId, canonicalPayload);
        var consumeProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
            authority.Identity,
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding);
        return new OwnerSignedHttpRequest<TAction>(
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding,
            consumeProof,
            Sign(key, consumeProof),
            requestId,
            action);
    }

    private static string Sign(ECDsa key, string proof) => Convert.ToBase64String(key.SignData(
        Encoding.UTF8.GetBytes(proof),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}

public sealed class ViewerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string stateDirectory;
    private readonly bool ownsStateDirectory;
    private readonly string? approvedAssetCatalogPath;
    private readonly bool privateWorld;
    private readonly bool legacyPrivateWorld;
    private readonly bool configureProviderUsagePath;

    public ViewerWebApplicationFactory()
        : this(null)
    {
    }

    internal ViewerWebApplicationFactory(
        string? persistedStateDirectory,
        string? approvedAssetCatalogPath = null,
        bool privateWorld = false,
        bool legacyPrivateWorld = true,
        bool configureProviderUsagePath = true)
    {
        ownsStateDirectory = persistedStateDirectory is null;
        stateDirectory = persistedStateDirectory ?? System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-http-{Guid.NewGuid():N}");
        this.approvedAssetCatalogPath = approvedAssetCatalogPath;
        this.privateWorld = privateWorld;
        this.legacyPrivateWorld = legacyPrivateWorld;
        this.configureProviderUsagePath = configureProviderUsagePath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(stateDirectory);
        // Existing protocol tests exercise a populated world. New-world setup has its
        // own integration test; seed the older fixture explicitly instead of quietly
        // depending on the production host's default genesis.
        if (privateWorld && legacyPrivateWorld && !File.Exists(System.IO.Path.Combine(stateDirectory, "runtime.json")))
        {
            using var seeded = new PrivateWorldStateFile(System.IO.Path.Combine(stateDirectory, "runtime.json"),
                newWorldPace: WorldStartPace.Legacy).LoadOrCreate(SeededWorldObservationStore.SampleSeed);
        }
        // The compatibility suite uses fixture mode; selected tests opt into
        // the integrated private runtime through the same real host boundary.
        builder.UseSetting("ClankerWorld:Runtime:WorldMode", privateWorld ? "private" : "fixture");
        builder.UseSetting("ClankerWorld:Runtime:AdvanceScript", "false");
        builder.UseSetting("ClankerWorld:Pairing:StatePath", System.IO.Path.Combine(stateDirectory, "authority.json"));
        builder.UseSetting("ClankerWorld:Runtime:StatePath", System.IO.Path.Combine(stateDirectory, "runtime.json"));
        builder.UseSetting(
            "ClankerWorld:Runtime:ProviderStatePath",
            System.IO.Path.Combine(stateDirectory, "provider-configuration.json"));
        if (configureProviderUsagePath)
            builder.UseSetting("ClankerWorld:Runtime:ProviderUsagePath",
                System.IO.Path.Combine(stateDirectory, "provider-usage.json"));
        builder.UseSetting("ClankerWorld:Pairing:ServerAuthorityId", "authority-http-tests");
        if (!string.IsNullOrWhiteSpace(approvedAssetCatalogPath))
        {
            builder.UseSetting("ClankerWorld:Assets:CatalogPath", approvedAssetCatalogPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && ownsStateDirectory && Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
