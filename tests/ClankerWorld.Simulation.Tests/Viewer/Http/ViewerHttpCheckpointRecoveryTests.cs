using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("truncated", true)]
    [InlineData("history", false)]
    [InlineData("schema", false)]
    public async Task DamagedStartupOffersAVerifiedAutosaveWithoutChangingTheDamagedCheckpoint(string damage, bool advanceScript)
    {
        var directory = Directory.CreateTempSubdirectory("checkpoint-recovery-");
        try
        {
            string saveId;
            using (var original = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (original.CreateClient())
            {
                var runtime = original.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                var settings = original.Services.GetRequiredService<WorldAutosaveStore>().Capture();
                saveId = original.Services.GetRequiredService<ManualWorldSaveStore>().CreateAutosave(runtime, [], settings).Id;
            }
            var path = Path.Combine(directory.FullName, "runtime.json");
            var damaged = Encoding.UTF8.GetBytes("{interrupted checkpoint");
            if (damage == "history")
                damaged = PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path))
                    with { HistoryArchiveHead = new string('a', 64) });
            else if (damage == "schema")
            {
                var old = JsonNode.Parse(File.ReadAllBytes(path))!;
                old["state"]!["schemaVersion"] = 1;
                damaged = Encoding.UTF8.GetBytes(old.ToJsonString());
            }
            File.WriteAllBytes(path, damaged);

            using var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, advanceScript: advanceScript);
            using var client = restarted.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(restarted, client, key);
            using var status = await SendSignedAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/recovery/status", new OwnerControlAction("recovery-status"),
                OwnerHttpBinding.EmptyPayload("recovery-status"));
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("pending").GetBoolean());
            Assert.Equal(damage == "schema" ? "different_save_format" : "checkpoint_refused",
                json.RootElement.GetProperty("reason").GetString());
            Assert.Equal(saveId, json.RootElement.GetProperty("autosave").GetProperty("id").GetString());
            Assert.Equal(damaged, File.ReadAllBytes(path));

            var savedFiles = Directory.GetFiles(path + ".manual").ToDictionary(file => file, File.ReadAllBytes);
            var blockedRequest = await CreateSignedRequestAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
            using var blocked = await client.PostAsJsonAsync("/api/v1/owner/control/resume", blockedRequest);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(damaged, File.ReadAllBytes(path));
            var action = new OwnerManualSaveAction("recover", saveId);
            using var recovered = await SendSignedAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/recovery/restore", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            var loaded = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.True(loaded.Society.IsPaused);
            using var stale = await client.PostAsJsonAsync("/api/v1/owner/control/resume", blockedRequest);
            Assert.True(stale.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict);
            Assert.True(loaded.Society.IsPaused);
            loaded.Validate();
            Assert.Equal(loaded.Society.WorldId, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path)).Society.Society.WorldId);
            Assert.Equal(damaged, File.ReadAllBytes(Assert.Single(Directory.GetFiles(directory.FullName, "runtime.json.damaged.*.json"))));
            foreach (var file in savedFiles.Where(file => file.Key.EndsWith(".save", StringComparison.Ordinal) ||
                file.Key.EndsWith(".meta.json", StringComparison.Ordinal)))
                Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            Assert.Equal(saveId, restarted.Services.GetRequiredService<ManualWorldSaveStore>().CurrentPosition(loaded.Society.WorldId).ContinuedFromId);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task StartupRecoverySkipsABrokenNewerAutosaveAndKeepsOtherWorldsAndNamedSaves()
    {
        var directory = Directory.CreateTempSubdirectory("checkpoint-recovery-candidates-");
        try
        {
            string goodId;
            string otherId;
            var path = Path.Combine(directory.FullName, "runtime.json");
            using (var original = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (original.CreateClient())
            {
                var runtime = original.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                var saves = original.Services.GetRequiredService<ManualWorldSaveStore>();
                var settings = original.Services.GetRequiredService<WorldAutosaveStore>().Capture();
                goodId = saves.CreateAutosave(runtime, [], settings).Id;
                var broken = saves.CreateAutosave(runtime, [], settings);
                File.WriteAllText(Path.Combine(path + ".manual", broken.Id + ".save"), "{broken autosave");
                saves.Create("Named save", runtime, [], settings);
                using var other = new PrivateWorldRuntime("different-recovery-world");
                other.Pause();
                otherId = saves.CreateAutosave(other, [], settings with { WorldId = other.Society.WorldId }).Id;
            }
            var damaged = Encoding.UTF8.GetBytes("{damaged latest");
            File.WriteAllBytes(path, damaged);
            var files = Directory.GetFiles(path + ".manual").ToDictionary(file => file, File.ReadAllBytes);
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = restarted.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(restarted, client, key);
            var recovery = restarted.Services.GetRequiredService<PrivateWorldStartupRecovery>();
            Assert.Equal(goodId, recovery.Capture().Autosave!.Id);
            var action = new OwnerManualSaveAction("recover", otherId);
            using var refused = await SendSignedAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/recovery/restore", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(damaged, File.ReadAllBytes(path));
            foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            Assert.Empty(Directory.GetFiles(directory.FullName, "runtime.json.damaged.*.json"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task StartupRecoveryWithoutAnAutosaveStillStartsAndPreservesTheRefusedWorld()
    {
        var directory = Directory.CreateTempSubdirectory("checkpoint-recovery-empty-");
        try
        {
            string namedId;
            using (var original = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (original.CreateClient())
            {
                var runtime = original.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                namedId = original.Services.GetRequiredService<ManualWorldSaveStore>().Create("Named only", runtime, []).Id;
            }
            var path = Path.Combine(directory.FullName, "runtime.json");
            var damaged = Encoding.UTF8.GetBytes("{damaged latest");
            File.WriteAllBytes(path, damaged);
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, advanceScript: true);
            using var client = restarted.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(restarted, client, key);
            Assert.True(restarted.Services.GetRequiredService<PrivateWorldStartupRecovery>().Pending);
            Assert.Null(restarted.Services.GetRequiredService<PrivateWorldStartupRecovery>().Capture().Autosave);
            var action = new OwnerManualSaveAction("recover", namedId);
            using var refused = await SendSignedAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/recovery/restore", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Throws<InvalidOperationException>(() => restarted.Services.GetRequiredService<PrivateWorldRuntime>());
            Assert.Equal(damaged, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory.FullName, "runtime.json.damaged.*.json"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task FailedRecoveryKeepsTheDamagedFileAndCanBeRetriedAfterStorageIsRepaired()
    {
        var directory = Directory.CreateTempSubdirectory("checkpoint-recovery-storage-");
        try
        {
            string saveId;
            var path = Path.Combine(directory.FullName, "runtime.json");
            using (var original = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (original.CreateClient())
            {
                var runtime = original.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                saveId = original.Services.GetRequiredService<ManualWorldSaveStore>().CreateAutosave(runtime, [],
                    original.Services.GetRequiredService<WorldAutosaveStore>().Capture()).Id;
            }
            var damaged = Encoding.UTF8.GetBytes("{damaged latest");
            File.WriteAllBytes(path, damaged);
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = restarted.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(restarted, client, key);
            Assert.Equal(saveId, restarted.Services.GetRequiredService<PrivateWorldStartupRecovery>().Capture().Autosave!.Id);
            var timeline = Assert.Single(Directory.GetFiles(path + ".manual", "timeline-*.json"));
            File.Move(timeline, timeline + ".kept");
            Directory.CreateDirectory(timeline);
            var action = new OwnerManualSaveAction("recover", saveId);
            using var refused = await SendSignedAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/recovery/restore", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(damaged, File.ReadAllBytes(path));
            Assert.True(restarted.Services.GetRequiredService<PrivateWorldStartupRecovery>().Pending);
            Directory.Delete(timeline);
            File.Move(timeline + ".kept", timeline);
            using var retried = await SendSignedAsync(restarted, client, key, device.DeviceId,
                "/api/v1/owner/recovery/restore", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            var retained = Directory.GetFiles(directory.FullName, "runtime.json.damaged.*.json");
            Assert.Equal(2, retained.Length);
            foreach (var file in retained) Assert.Equal(damaged, File.ReadAllBytes(file));
            restarted.Services.GetRequiredService<PrivateWorldRuntime>().Validate();
            var history = path + ".history";
            Directory.CreateDirectory(history);
            var unreachable = Path.Combine(history, new string('b', 64) + ".json");
            File.WriteAllText(unreachable, "history kept while damaged roots remain");
            Assert.Throws<InvalidDataException>(() => restarted.Services.GetRequiredService<PrivateWorldStateFile>().ReclaimUnreferencedHistory());
            Assert.True(File.Exists(unreachable));
        }
        finally { directory.Delete(recursive: true); }
    }
}
