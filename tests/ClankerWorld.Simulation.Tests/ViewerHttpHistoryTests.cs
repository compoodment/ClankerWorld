using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(false, "missing-head")]
    [InlineData(false, "corrupt-head")]
    [InlineData(false, "missing-parent")]
    [InlineData(true, "missing-head")]
    [InlineData(true, "corrupt-head")]
    [InlineData(true, "missing-parent")]
    public async Task MissingHistoryCannotReplaceTheHealthyActiveWorld(bool named, string fault)
    {
        var directory = Directory.CreateTempSubdirectory("history-preflight-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
            stateFile.Save(runtime);
            var before = File.ReadAllBytes(stateFile.Path);
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var routing = JsonSerializer.Serialize(providers.CaptureRuntimeConfiguration());
            using var target = named ? PrivateWorldRuntime.Restore(runtime.ExportState()) : new PrivateWorldRuntime("archived-history-world");
            target.Pause();
            for (var segment = 0; segment < 2; segment++)
            {
                for (var n = 0; n < PrivateWorldHistory.CompactionThreshold + 2; n++) target.SetJevEnabled(!target.JevEnabled);
                stateFile.Save(target);
            }
            var head = target.ExportState().HistoryArchiveHead!;
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var id = named ? saves.Create("Archived", target, []).Id : catalog.Add("Archived", target.ExportState()).Id;
            stateFile.Save(runtime);
            var headPath = Path.Combine(stateFile.Path + ".history", head + ".json");
            var brokenPath = headPath;
            if (fault == "missing-parent")
            {
                var segment = JsonSerializer.Deserialize<PrivateWorldHistorySegment>(File.ReadAllBytes(headPath))!;
                Assert.NotNull(segment.Parent);
                brokenPath = Path.Combine(stateFile.Path + ".history", segment.Parent + ".json");
            }
            var archivedBytes = File.ReadAllBytes(brokenPath);
            if (fault == "corrupt-head") File.WriteAllText(brokenPath, "invalid-secret-history");
            else File.Delete(brokenPath);
            if (named)
            {
                var action = new OwnerManualSaveAction("load", id);
                using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/saves/load", action, OwnerHttpBinding.ManualSavePayload(action));
                Assert.False(response.IsSuccessStatusCode);
                Assert.DoesNotContain("invalid-secret", await response.Content.ReadAsStringAsync());
            }
            else
            {
                var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
                Assert.Equal("incompatible", selection.List().Worlds.Single(world => world.Id == id).Compatibility);
                Assert.Throws<InvalidDataException>(() => selection.Select(id));
            }
            Assert.Equal(before, File.ReadAllBytes(stateFile.Path));
            Assert.Equal(routing, JsonSerializer.Serialize(providers.CaptureRuntimeConfiguration()));
            using var restarted = stateFile.LoadOrCreate(runtime.ExportState().WorldSeed);
            Assert.Equal(runtime.Society.WorldId, restarted.Society.WorldId);
            File.WriteAllBytes(brokenPath, archivedBytes);
            stateFile.VerifyRequiredHistory(target.ExportState());
            if (!named) Assert.Equal("compatible", host.Services.GetRequiredService<WorldSelectionCoordinator>().List().Worlds.Single(world => world.Id == id).Compatibility);
        }
        finally { directory.Delete(recursive: true); }
    }
}
