using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("Medium", 50, 512)]
    [InlineData("Small", 65, 256)]
    public async Task SameSeedDifferentMapsKeepDistinctWorlds(string nextSize, int nextWater, int nextWidth)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-generated-identity-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null,
                privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var selection = host.Services.GetRequiredService<WorldSelectionCoordinator>();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var autosave = host.Services.GetRequiredService<WorldAutosaveStore>();
            async Task<(OwnerWorldCreationAction Action, ViewerWorldPreview Preview)> Preview(string size, string seed, int water = 50)
            {
                var action = new OwnerWorldCreationAction(size + " identity audit", seed, size,
                    water, true, "Dominant", "Temperate", false, "Normal");
                using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/preview", action, OwnerHttpBinding.WorldCreationPayload(action));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var preview = (await response.Content.ReadFromJsonAsync<ViewerWorldPreview>())!;
                return (action with
                {
                    CandidateAttempt = preview.Coverage!.Attempt,
                    ExpectedManifestDigest = preview.ManifestDigest,
                    ExpectedMapLayersDigest = preview.MapLayersDigest,
                    AcceptUnmetTargets = true,
                }, preview);
            }
            async Task<HttpResponseMessage> Create(OwnerWorldCreationAction action) =>
                await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/create", action, OwnerHttpBinding.WorldCreationPayload(action));

            const string seed = "native-map-size-identity-audit";
            var small = await Preview("Small", seed);
            using var first = await Create(small.Action);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var smallEntry = (await first.Content.ReadFromJsonAsync<CatalogWorld>())!;
            runtime.Validate();
            autosave.Configure(true, 5, 3);
            var smallManual = saves.Create("Small named checkpoint", runtime, [], autosave.Capture());
            var smallAuto = saves.CreateAutosave(runtime, [], autosave.Capture());
            var smallBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using (var duplicate = await Create(small.Action))
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(smallBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(2, catalog.Capture().Worlds.Count);

            var heldAction = new OwnerAutosaveConfigurationAction(false, 1, 0, smallEntry.WorldId);
            const string autosavePath = "/api/v1/owner/saves/autosave/configure";
            var held = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                autosavePath, heldAction, OwnerHttpBinding.AutosaveConfigurationPayload(heldAction));
            const string loadPath = "/api/v1/owner/saves/load";
            var loadAction = new OwnerManualSaveAction("load", smallManual.Id);
            var heldLoad = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                loadPath, loadAction, OwnerHttpBinding.ManualSavePayload(loadAction));
            var medium = await Preview(nextSize, seed, nextWater);
            Assert.Equal((256, nextWidth), (small.Preview.Terrain.Width, medium.Preview.Terrain.Width));
            Assert.NotEqual(small.Preview.ManifestDigest, medium.Preview.ManifestDigest);
            using var second = await Create(medium.Action);
            var status = second.StatusCode;
            var responseText = await second.Content.ReadAsStringAsync();
            if (status != HttpStatusCode.OK)
            {
                Assert.Equal(smallBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
                Assert.Equal(2, catalog.Capture().Worlds.Count);
            }
            var control = await Preview("Medium", "native-map-size-identity-control");
            using var freshSeed = await Create(control.Action);
            Assert.Equal(HttpStatusCode.OK, freshSeed.StatusCode);
            Assert.True(status == HttpStatusCode.OK, $"Different preview with the same seed: {status} {responseText}");
            var mediumEntry = System.Text.Json.JsonSerializer.Deserialize<CatalogWorld>(responseText, WebJsonOptions)!;
            Assert.NotEqual(smallEntry.WorldId, mediumEntry.WorldId);
            Assert.NotEqual(smallEntry.Id, mediumEntry.Id);
            Assert.Equal(smallEntry.Seed, mediumEntry.Seed);
            Assert.Matches("^generated:[0-9a-f]{64}$", mediumEntry.WorldId);

            // Return from the fresh-seed control to the second same-seed world.
            selection.Select(mediumEntry.Id);
            runtime.Validate();
            Assert.Equal(medium.Preview.ManifestDigest, runtime.ExportState().Map.ManifestDigest);
            Assert.Equal(medium.Preview.MapLayersDigest, MapLayerManifestCodec.Digest(runtime.ExportState().Map));
            autosave.Configure(true, 10, 5);
            var mediumManual = saves.Create("Second named checkpoint", runtime, [], autosave.Capture());
            var mediumAuto = saves.CreateAutosave(runtime, [], autosave.Capture());
            var mediumBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var settingsBytes = File.ReadAllBytes(host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".autosave.json");
            using (var stale = await client.PostAsJsonAsync(autosavePath, held))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(mediumBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(settingsBytes, File.ReadAllBytes(host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".autosave.json"));
            Assert.Equal((mediumEntry.WorldId, 10, 5),
                (autosave.Capture().WorldId, autosave.Capture().IntervalMinutes, autosave.Capture().RotationCount));

            // Refusal must precede backup creation, timeline continuation and any
            // runtime, catalog, provider or autosave mutation.
            var statePath = host.Services.GetRequiredService<PrivateWorldStateFile>().Path;
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            Dictionary<string, byte[]> SaveFiles() => Directory.GetFiles(directory.FullName, "*", SearchOption.AllDirectories)
                .Where(path => path == statePath || path == statePath + ".autosave.json" || path == providers.Path ||
                    path.StartsWith(statePath + ".manual" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    path.StartsWith(statePath + ".worlds" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
            var beforeLoadFiles = SaveFiles();
            var catalogBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalog.Capture());
            var providerBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(providers.CaptureRuntimeConfiguration());
            var autosaveBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(autosave.Capture());
            using (var staleLoad = await client.PostAsJsonAsync(loadPath, heldLoad))
                Assert.Equal(HttpStatusCode.Conflict, staleLoad.StatusCode);
            Assert.Equal(mediumBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(catalogBytes, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalog.Capture()));
            Assert.Equal(providerBytes, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(providers.CaptureRuntimeConfiguration()));
            Assert.Equal(autosaveBytes, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(autosave.Capture()));
            var afterLoadFiles = SaveFiles();
            Assert.Equal(beforeLoadFiles.Keys.Order(), afterLoadFiles.Keys.Order());
            foreach (var (path, bytes) in beforeLoadFiles) Assert.Equal(bytes, afterLoadFiles[path]);

            var count = catalog.Capture().Worlds.Count;
            using (var renamedDuplicate = await Create(medium.Action with { Name = "A different name" }))
                Assert.Equal(HttpStatusCode.Conflict, renamedDuplicate.StatusCode);
            using (var mismatchedPreview = await Create(medium.Action with { ExpectedManifestDigest = small.Preview.ManifestDigest }))
                Assert.Equal(HttpStatusCode.Conflict, mismatchedPreview.StatusCode);
            Assert.Equal(count, catalog.Capture().Worlds.Count);
            Assert.Equal(mediumBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(smallBytes, PrivateWorldRuntimeCodec.Encode(catalog.Read(smallEntry.Id)));

            foreach (var (entry, bytes, manual, automatic, interval, rotation, width, water, manifest) in new[]
            {
                (smallEntry, smallBytes, smallManual, smallAuto, 5, 3, 256, 50, small.Preview.ManifestDigest),
                (mediumEntry, mediumBytes, mediumManual, mediumAuto, 10, 5, nextWidth, nextWater, medium.Preview.ManifestDigest),
            })
            {
                var selectAction = new OwnerManualSaveAction("select-world", entry.Id);
                using var selected = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", selectAction, OwnerHttpBinding.ManualSavePayload(selectAction));
                Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
                var state = runtime.ExportState();
                Assert.Equal(entry.WorldId, state.Society.Society.WorldId);
                Assert.Equal(seed, state.WorldSeed);
                Assert.Equal(width, state.Map.Width);
                Assert.Equal(water, state.Geography!.WaterPercent);
                Assert.Equal(manifest, state.Map.ManifestDigest);
                Assert.Equal(entry.Name, catalog.Active().Name);
                Assert.Equal((entry.WorldId, interval, rotation),
                    (autosave.Capture().WorldId, autosave.Capture().IntervalMinutes, autosave.Capture().RotationCount));
                Assert.Equal(new[] { manual.Id, automatic.Id }.Order(), saves.List(entry.WorldId).Select(save => save.Id).Order());
                var committed = saves.ReadCommitted(manual.Id);
                Assert.Equal(entry.WorldId, committed.AutosaveSettings!.WorldId);
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(committed.Checkpoint));
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(saves.Read(automatic.Id)));
                using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
                reloaded.Validate();
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
                var ownLoad = new OwnerManualSaveAction("load", manual.Id);
                using var loaded = await SendSignedAsync(host, client, key, device.DeviceId,
                    loadPath, ownLoad, OwnerHttpBinding.ManualSavePayload(ownLoad));
                Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            }

            using var restarted = new ViewerWebApplicationFactory(directory.FullName, null,
                privateWorld: true, legacyPrivateWorld: false);
            var reopened = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            reopened.Validate();
            Assert.Equal(mediumEntry.WorldId, reopened.Society.WorldId);
            Assert.Equal(mediumBytes, PrivateWorldRuntimeCodec.Encode(reopened.ExportState()));
            var reopenedCatalog = restarted.Services.GetRequiredService<WorldCatalogStore>();
            Assert.Equal(smallBytes, PrivateWorldRuntimeCodec.Encode(reopenedCatalog.Read(smallEntry.Id)));
            Assert.Equal(mediumBytes, PrivateWorldRuntimeCodec.Encode(reopenedCatalog.Read(mediumEntry.Id)));
        }
        finally { directory.Delete(recursive: true); }
    }
}
