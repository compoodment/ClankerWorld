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
    public async Task DelayedAutosaveConfigurationPreservesTheOtherWorldAndItsCheckpointCopies()
    {
        var directory = Directory.CreateTempSubdirectory("delayed-autosave-world-");
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
            var autosave = host.Services.GetRequiredService<WorldAutosaveStore>();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var entryA = catalog.Active();
            var settingsA = autosave.Capture();
            var idsA = Enumerable.Range(0, 3).Select(_ => saves.CreateAutosave(runtime, [], settingsA).Id).ToArray();
            using var other = new PrivateWorldRuntime("delayed-autosave-world-B");
            other.Pause();
            var entryB = catalog.Add("Other", other.ExportState());
            var initialB = new WorldAutosaveSettings(other.Society.WorldId, true, 5, 5, DateTimeOffset.UtcNow, -1);
            var idsB = Enumerable.Range(0, 3).Select(_ => saves.CreateAutosave(other, [], initialB).Id).ToArray();
            var root = file.Path + ".manual";
            var copies = idsA.Concat(idsB).SelectMany(id => new[] { id + ".save", id + ".meta.json" })
                .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(root, name)));

            // Real authorization is prepared for the settings opened in A.
            // Delay only delivery, then select B through the real coordinator.
            const string path = "/api/v1/owner/saves/autosave/configure";
            var action = new OwnerAutosaveConfigurationAction(true, 1, 0, settingsA.WorldId);
            var held = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                path, action, OwnerHttpBinding.AutosaveConfigurationPayload(action));
            selection.Select(entryB.Id);
            var settingsB = autosave.Capture();
            Assert.Equal((other.Society.WorldId, 5, 5),
                (settingsB.WorldId, settingsB.IntervalMinutes, settingsB.RotationCount));
            var activeBytes = File.ReadAllBytes(file.Path);
            var settingsBytes = File.ReadAllBytes(file.Path + ".autosave.json");
            var runtimeBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using var refused = await client.PostAsJsonAsync(path, held);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(settingsB, autosave.Capture());
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(settingsBytes, File.ReadAllBytes(file.Path + ".autosave.json"));
            Assert.Equal(runtimeBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(idsA.Order(), saves.List(settingsA.WorldId).Select(item => item.Id).Order());
            Assert.Equal(idsB.Order(), saves.List(settingsB.WorldId).Select(item => item.Id).Order());
            foreach (var (name, bytes) in copies) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, name)));

            selection.Select(entryA.Id);
            Assert.Equal(settingsA, autosave.Capture());
            selection.Select(entryB.Id);
            Assert.Equal(settingsB, autosave.Capture());

            // Adding B's ID to A's signed body cannot retarget its proof.
            var signedA = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                path, action, OwnerHttpBinding.AutosaveConfigurationPayload(action));
            using var tampered = await client.PostAsJsonAsync(path,
                signedA with { Action = action with { WorldId = settingsB.WorldId } });
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);

            // An older client cannot submit settings without a target world.
            var unscoped = action with { WorldId = null! };
            var legacyPayload = "clankerworld.owner-autosave-configuration.v1\nenabled=true\ninterval-minutes=1\nrotation-count=0";
            var oldRequest = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, unscoped, legacyPayload);
            using var missingWorld = await client.PostAsJsonAsync(path, oldRequest);
            Assert.Equal(HttpStatusCode.BadRequest, missingWorld.StatusCode);
            Assert.Equal(settingsB, autosave.Capture());
            foreach (var (name, bytes) in copies) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, name)));

            // A fresh request for B still applies its settings and rotates only B.
            var current = new OwnerAutosaveConfigurationAction(true, 1, 0, settingsB.WorldId);
            using var accepted = await SendSignedAsync(host, client, key, device.DeviceId,
                path, current, OwnerHttpBinding.AutosaveConfigurationPayload(current));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var updated = (await accepted.Content.ReadFromJsonAsync<WorldAutosaveSettings>())!;
            Assert.Equal((settingsB.WorldId, 1, 0), (updated.WorldId, updated.IntervalMinutes, updated.RotationCount));
            var retained = Assert.Single(saves.List(settingsB.WorldId));
            Assert.Contains(retained.Id, idsB);
            Assert.Equal(idsA.Order(), saves.List(settingsA.WorldId).Select(item => item.Id).Order());
            foreach (var id in idsA)
                foreach (var suffix in new[] { ".save", ".meta.json" })
                    Assert.Equal(copies[id + suffix], File.ReadAllBytes(Path.Combine(root, id + suffix)));
            selection.Select(entryA.Id);
            Assert.Equal(settingsA, autosave.Capture());
            selection.Select(entryB.Id);
            Assert.Equal(updated, autosave.Capture());
            Assert.Equal(updated, new WorldAutosaveStore(file.Path, settingsB.WorldId).Capture());
        }
        finally { directory.Delete(recursive: true); }
    }
}
