using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task FailedStartStaysPausedAndCanBeRetriedDurably()
    {
        var directory = Directory.CreateTempSubdirectory("durable-start-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var positions = new[] { new ClankerWorld.Simulation.Harness.GridPoint(0, 0), new(1, 2), new(2, 2), new(3, 2) };
            foreach (var position in positions)
            {
                var id = "founder:" + Guid.NewGuid().ToString("N");
                runtime.PlaceFounder(id, position);
                providers.Configure(new("personal", "openai", "test-model", "test-only-start-key", false, id));
            }
            file.Save(runtime);
            var original = File.ReadAllBytes(file.Path);
            File.Move(file.Path, file.Path + ".prior");
            Directory.CreateDirectory(file.Path);
            try
            {
                using var failed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/start-world", new OwnerControlAction("start-world"), OwnerHttpBinding.EmptyPayload("start-world"));
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Assert.True(runtime.Society.IsPaused);
            Assert.False(runtime.FounderSetup!.Started);
            Assert.False((await runtime.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(0, runtime.WorldTick);
            Directory.Delete(file.Path);
            File.Move(file.Path + ".prior", file.Path);
            Assert.Equal(original, File.ReadAllBytes(file.Path));
            using var retry = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/start-world", new OwnerControlAction("start-world"), OwnerHttpBinding.EmptyPayload("start-world"));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.True(runtime.FounderSetup.Started);
            Assert.False(runtime.Society.IsPaused);
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var restartedClient = restarted.CreateClient();
            var restored = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.True(restored.FounderSetup!.Started);
            Assert.Equal(4, restored.FounderSetup.FounderIds.Count);
            Assert.True(restored.Society.IsPaused);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulMutationRetryPersistsAnAlreadyChangedRuntime(bool rename)
    {
        var directory = Directory.CreateTempSubdirectory("durable-owner-retry-");
        try
        {
            // The legacy fixture has founders but no FounderSetup, so the rename
            // endpoint correctly rejects it. Use a placed founder for that case;
            // keep the running legacy world for the pause transition case.
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true,
                legacyPrivateWorld: !rename);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var founderId = "founder:" + Guid.NewGuid().ToString("N");
            if (rename) runtime.PlaceFounder(founderId, new(0, 0));
            file.Save(runtime);
            var savedPath = file.Path + ".prior";
            File.Move(file.Path, savedPath);
            Directory.CreateDirectory(file.Path);
            async Task<HttpResponseMessage> SendMutation()
            {
                if (rename)
                {
                    var action = new OwnerAgentRenameAction(founderId, "Durable Name");
                    return await SendSignedAsync(host, client, key, device.DeviceId,
                        "/api/v1/owner/agents/rename", action, OwnerHttpBinding.AgentRenamePayload(action));
                }
                return await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/pause", new OwnerControlAction("pause"), OwnerHttpBinding.EmptyPayload("pause"));
            }
            try
            {
                using var failed = await SendMutation();
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Assert.True(rename ? runtime.Society.GetInhabitant(founderId).Name == "Durable Name" : runtime.Society.IsPaused);
            if (!rename)
            {
                var action = new OwnerReconnectAction(0);
                using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/reconnect", action, OwnerHttpBinding.ReconnectPayload(action));
                response.EnsureSuccessStatusCode();
                var observed = await response.Content.ReadFromJsonAsync<ViewerOwnerReconnect>();
                Assert.True(observed!.Baseline.Snapshot.Authoring!.IsPaused);
                using var prior = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(savedPath)));
                Assert.False(prior.Society.IsPaused);
            }
            Directory.Delete(file.Path);
            File.Move(savedPath, file.Path);
            using var retry = await SendMutation();
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            using var restored = file.LoadOrCreate(runtime.ExportState().WorldSeed);
            Assert.True(rename ? restored.Society.GetInhabitant(founderId).Name == "Durable Name" : restored.Society.IsPaused);
        }
        finally { directory.Delete(recursive: true); }
    }
}
