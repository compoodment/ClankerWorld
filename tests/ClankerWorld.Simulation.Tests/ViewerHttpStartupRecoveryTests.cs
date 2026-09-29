using System.Net;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task InterruptedSelectionIsRecoveredBeforeResumeAndCannotUndoLaterConfiguration()
    {
        var directory = Directory.CreateTempSubdirectory("startup-selection-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.json");
            using var first = new PrivateWorldRuntime("startup-world-a");
            using var second = new PrivateWorldRuntime("startup-world-b");
            first.Pause();
            second.Pause();
            var initialProviders = new ProviderConfigurationStore(Path.Combine(directory.FullName, "provider-configuration.json"),
                new("deterministic", null, null, null, null, null, null));
            initialProviders.Configure(new("planning", "openai", "model-before-crash", "test-only-startup-key", false));
            var settings = new WorldAutosaveStore(path, first.Society.WorldId);
            var catalog = new WorldCatalogStore(path, first.ExportState(), [], settings.Capture());
            var firstId = catalog.Active().Id;
            var destination = catalog.Add("Second", second.ExportState());
            catalog.Select(destination.Id);
            settings.SelectWorld(second.Society.WorldId, null);
            var savedSettings = settings.Configure(false, 1, 3);
            catalog.ArchiveActive(second.ExportState(), [new("founder-scout", "planning", "openai", "model-before-crash")], savedSettings);
            catalog.Select(firstId);
            settings.SelectWorld(first.Society.WorldId, null);
            // Genuine interruption boundary: active checkpoint replaced, catalog/routing not committed.
            new PrivateWorldStateFile(path).Save(second);
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            Assert.Equal("model-before-crash", Assert.Single(providers.CaptureRuntimeConfiguration().Assignments!).Model);
            Assert.False(host.Services.GetRequiredService<WorldAutosaveStore>().Capture().Enabled);
            Assert.Equal(1, host.Services.GetRequiredService<WorldAutosaveStore>().Capture().IntervalMinutes);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            using var resumed = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
            Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
            var action = new OwnerProviderConfigurationAction("planning", "openai", "chosen-after-restart", null, false, "founder-scout");
            using var changed = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/providers/configure", action, OwnerHttpBinding.ProviderConfigurationPayload(action));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            using var listed = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/list", new OwnerControlAction("list-worlds"), OwnerHttpBinding.EmptyPayload("list-worlds"));
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            Assert.Equal("chosen-after-restart", Assert.Single(providers.CaptureRuntimeConfiguration().Assignments!).Model);
        }
        finally { directory.Delete(recursive: true); }
    }
}
