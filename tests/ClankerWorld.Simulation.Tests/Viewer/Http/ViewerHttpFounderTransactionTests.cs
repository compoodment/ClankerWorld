using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task FailedFounderCheckpointRestoresWorldAndKeysThenAllowsRetry()
    {
        var directory = Directory.CreateTempSubdirectory("founder-transaction-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var world = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            file.Save(world);
            var original = File.ReadAllBytes(file.Path);
            var routing = JsonSerializer.Serialize(providers.CaptureRuntimeConfiguration());
            var id = "founder:" + Guid.NewGuid().ToString("N");
            var action = new OwnerFounderPlacementAction(id, 0, 0,
                new("personal", "openai", "test-model", "test-secret", false, id, Guid.NewGuid().ToString("N"), "Test"));
            File.Move(file.Path, file.Path + ".prior");
            Directory.CreateDirectory(file.Path);
            try
            {
                using var failed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/founders/place", action, OwnerHttpBinding.FounderPlacementPayload(action));
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Assert.Empty(world.Inhabitants);
            Assert.Equal(routing, JsonSerializer.Serialize(providers.CaptureRuntimeConfiguration()));
            var reopened = new ProviderConfigurationStore(providers.Path,
                new("deterministic", null, null, null, null, null, null));
            Assert.Equal(routing, JsonSerializer.Serialize(reopened.CaptureRuntimeConfiguration()));
            Directory.Delete(file.Path);
            File.Move(file.Path + ".prior", file.Path);
            Assert.Equal(original, File.ReadAllBytes(file.Path));
            using var retry = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/founders/place", action, OwnerHttpBinding.FounderPlacementPayload(action));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            using var restored = file.LoadOrCreate(world.ExportState().WorldSeed);
            Assert.Contains(restored.Inhabitants, person => person.InhabitantId == id);
            Assert.Equal(2, providers.CaptureRuntimeConfiguration().Assignments!.Count(item => item.InhabitantId == id));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task SelectionAndFounderPlacementCannotInterleaveRoutingCommits()
    {
        var directory = Directory.CreateTempSubdirectory("founder-selection-gate-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var world = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            using var target = new PrivateWorldRuntime("selection-gate-target", startPace: WorldStartPace.FounderSetup);
            var entry = host.Services.GetRequiredService<WorldCatalogStore>().Add("Target", target.ExportState());
            var id = "founder:" + Guid.NewGuid().ToString("N");
            var action = new OwnerFounderPlacementAction(id, 0, 0,
                new("personal", "openai", "test-model", "test-secret", false, id, Guid.NewGuid().ToString("N"), "Test"));
            var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/founders/place", action, OwnerHttpBinding.FounderPlacementPayload(action));
            // Test-only checkpoint-latency injection reproduces the reported interleaving.
            var fileGate = typeof(PrivateWorldStateFile).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(file)!;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var held = Task.Run(() => { lock (fileGate) { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(15)); } });
            await entered.Task;
            var selection = Task.Run(() => host.Services.GetRequiredService<WorldSelectionCoordinator>().Select(entry.Id));
            Task<HttpResponseMessage>? placement = null;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (world.Society.WorldId != target.Society.WorldId) await Task.Delay(10, deadline.Token);
                placement = Task.Run(() => client.PostAsJsonAsync("/api/v1/owner/founders/place", envelope));
                await Task.Delay(200, deadline.Token);
                Assert.Empty(world.Inhabitants);
            }
            finally { release.Set(); }
            await held;
            await selection;
            using var response = await placement!;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(world.Inhabitants, person => person.InhabitantId == id);
            Assert.Equal(2, providers.CaptureRuntimeConfiguration().Assignments!.Count(item => item.InhabitantId == id));
        }
        finally { directory.Delete(recursive: true); }
    }
}
