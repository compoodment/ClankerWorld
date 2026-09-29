using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task SignedNewWorldPreviewWorksWhileCurrentWorldIsRunningWithoutChangingIt()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-running-preview-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.False(runtime.Society.IsPaused);
            var before = runtime.ExportState();
            var options = new OwnerWorldCreationAction("New World", "running-preview-seed",
                "Medium", 45, true, "Balanced", "Temperate", true, "Normal");

            using var previewed = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/preview", options,
                OwnerHttpBinding.WorldCreationPayload(options));
            Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
            var preview = (await previewed.Content.ReadFromJsonAsync<ViewerWorldPreview>())!;
            Assert.Equal(512, preview.Terrain.Width);
            Assert.Equal(256, preview.Terrain.Height);
            Assert.Equal(before.Society.Society.WorldId, runtime.Society.WorldId);
            Assert.Equal(before.Society.Society.WorldTick, runtime.WorldTick);
            Assert.False(runtime.Society.IsPaused);
            Assert.Single(host.Services.GetRequiredService<WorldCatalogStore>().Capture().Worlds);

            using var refusedCreation = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/create", options,
                OwnerHttpBinding.WorldCreationPayload(options));
            Assert.Equal(HttpStatusCode.Conflict, refusedCreation.StatusCode);
            Assert.Single(host.Services.GetRequiredService<WorldCatalogStore>().Capture().Worlds);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task SignedWorldCreationAndSelectionKeepTwoIndependentPausedWorldsAcrossRestart()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-world-catalog-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string firstId;
            string generatedId;
            string deviceId;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null,
                       privateWorld: true, legacyPrivateWorld: false))
            using (var client = host.CreateClient())
            {
                var selectionLog = new RecordingLogger<WorldSelectionCoordinator>();
                host.Services.GetRequiredService<ILoggerFactory>().AddProvider(
                    new RecordingLoggerProvider<WorldSelectionCoordinator>(selectionLog));
                var townLog = new RecordingLogger<ViewerHttpTests>();
                host.Services.GetRequiredService<ILoggerFactory>().AddProvider(
                    new RecordingLoggerProvider<ViewerHttpTests>(townLog));
                var device = await StartAndActivateAsync(host, client, key);
                deviceId = device.DeviceId;
                var listAction = new OwnerControlAction("list-worlds");
                using var listed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/list", listAction,
                    OwnerHttpBinding.EmptyPayload("list-worlds"));
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                var initial = (await listed.Content.ReadFromJsonAsync<WorldCatalogSnapshot>())!;
                firstId = initial.ActiveId;
                Assert.Single(initial.Worlds);
                var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
                var credentialSlotId = Guid.NewGuid().ToString("N");
                providers.Configure(new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini",
                    "test-secret-key", false, "founder:checkpoint", credentialSlotId, "Test account"));
                host.Services.GetRequiredService<WorldAutosaveStore>().Configure(false, 1, 0);

                var create = new OwnerWorldCreationAction("Riverland", "riverland-test-seed",
                    "Small", 45, true, "Uniform", "Dry", false, "Abundant");
                const string createPath = "/api/v1/owner/worlds/create";
                var signed = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                    createPath, create, OwnerHttpBinding.WorldCreationPayload(create));
                using var tampered = await client.PostAsJsonAsync(createPath, signed with
                {
                    Action = create with { Seed = "different-seed" },
                });
                Assert.False(tampered.IsSuccessStatusCode);
                using var climateTampered = await client.PostAsJsonAsync(createPath, signed with
                {
                    Action = create with { SelectedClimate = "Tropical" },
                });
                Assert.False(climateTampered.IsSuccessStatusCode);
                using var abundanceTampered = await client.PostAsJsonAsync(createPath, signed with
                {
                    Action = create with { ResourceAbundance = "Sparse" },
                });
                Assert.False(abundanceTampered.IsSuccessStatusCode);
                using var previewed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/preview", create,
                    OwnerHttpBinding.WorldCreationPayload(create));
                Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
                var preview = (await previewed.Content.ReadFromJsonAsync<ViewerWorldPreview>())!;
                Assert.Equal(256, preview.Terrain.Width);
                Assert.True(preview.ResourceSites > 20);
                Assert.NotNull(preview.PackedMapLayers);
                Assert.NotNull(preview.MapLayersDigest);
                Assert.Contains(selectionLog.Messages, message => message.Contains(
                    "world_preview outcome=generated width=256", StringComparison.Ordinal));
                Assert.Null(host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState().Geography);
                Assert.Single(host.Services.GetRequiredService<WorldCatalogStore>().Capture().Worlds);
                using var created = await SendSignedAsync(host, client, key, device.DeviceId,
                    createPath, create, OwnerHttpBinding.WorldCreationPayload(create));
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                var entry = (await created.Content.ReadFromJsonAsync<CatalogWorld>())!;
                generatedId = entry.Id;
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                Assert.True(runtime.Society.IsPaused);
                Assert.Empty(runtime.Inhabitants);
                Assert.Equal(0, runtime.WorldTick);
                Assert.Equal(7, runtime.Content.Packages.Count);
                Assert.All(runtime.Content.Packages, package =>
                    Assert.Equal(ContentPackageLifecycle.Active, package.Lifecycle));
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "house-1x1");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "warehouse-2x2");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "farmhouse-1x1");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "blacksmith-1x2");
                Assert.Equal(WorldSizePreset.Small, runtime.ExportState().Geography?.Size);
                Assert.Equal(256, runtime.ExportState().Map.Width);
                Assert.Equal(preview.ManifestDigest, runtime.ExportState().Map.ManifestDigest);
                Assert.Equal(preview.MapLayersDigest, MapLayerManifestCodec.Digest(runtime.ExportState().Map));
                Assert.Equal(preview.ResourceSites, runtime.ExportState().Map.Resources.Count);
                Assert.Empty(runtime.ExportState().Map.CampObjects);
                Assert.Empty(runtime.Towns);
                Assert.Equal(preview.Camp.X, runtime.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position.X);
                Assert.Equal(preview.Camp.Y, runtime.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position.Y);
                Assert.Equal(ClimateMode.Uniform, runtime.ExportState().Geography?.ClimateMode);
                Assert.Equal(ResourceAbundance.Abundant, runtime.ExportState().Geography?.ResourceAbundance);
                Assert.Equal(ClimateZone.Dry, runtime.ExportState().Map.ClimateAt(
                    new GridPoint(preview.Camp.X, preview.Camp.Y)));
                var townSite = new OwnerFirstTownLayoutAction(preview.Camp.X, preview.Camp.Y);
                using var acceptedTown = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/town/first-layout", townSite,
                    OwnerHttpBinding.FirstTownLayoutPayload(townSite));
                Assert.Equal(HttpStatusCode.OK, acceptedTown.StatusCode);
                var layoutReceipt = (await acceptedTown.Content.ReadFromJsonAsync<OwnerFirstTownLayoutReceipt>())!;
                Assert.Equal(5, layoutReceipt.Buildings);
                Assert.Equal(5, runtime.WorldSimulation.Buildings.Count);
                Assert.NotEmpty(runtime.RoadTiles);
                Assert.Equal(new GridPoint(preview.Camp.X, preview.Camp.Y), Assert.Single(runtime.Towns).OriginSite);
                Assert.Contains(townLog.Messages, message => message.Contains(
                    "first_town_layout outcome=accepted world_tick=0", StringComparison.Ordinal));
                Assert.DoesNotContain(townLog.Messages, message => message.Contains(
                    "test-secret-key", StringComparison.Ordinal));
                var reconnect = new OwnerReconnectAction(0);
                using var observed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/reconnect", reconnect,
                    OwnerHttpBinding.ReconnectPayload(reconnect));
                Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
                var view = (await observed.Content.ReadFromJsonAsync<ViewerOwnerReconnect>())!;
                Assert.Equal(entry.WorldId, view.Baseline.Snapshot.WorldId);
                Assert.Equal(256, view.Baseline.Snapshot.PackedTerrain?.Width);
                Assert.Equal(preview.Terrain.Data, view.Baseline.Snapshot.PackedTerrain?.Data);
                Assert.Equal(preview.PackedMapLayers, view.Baseline.Snapshot.PackedMapLayers);
                Assert.Equal(preview.MapLayersDigest, view.Baseline.Snapshot.MapLayersDigest);
                Assert.Equal(create.WrapEastWest, view.Baseline.Snapshot.WrapsEastWest);
                Assert.True(view.Baseline.Snapshot.FounderSetup?.CanChooseTownSite == true);
                Assert.True(view.Baseline.Snapshot.FounderSetup?.HasAcceptedTownSite == true);
                Assert.Contains(view.Baseline.Snapshot.PlacedBuildings.Single(building =>
                    building.InstanceId == "first-town-house-a").StoredItems ?? [],
                    item => item.Kind == "food" && item.Quantity > 0);
                Assert.Contains(view.Baseline.Snapshot.PlacedBuildings.Single(building =>
                    building.InstanceId == "first-town-house-b").StoredItems ?? [],
                    item => item.Kind == "food" && item.Quantity > 0);
                var starterWarehouse = view.Baseline.Snapshot.PlacedBuildings.Single(building =>
                    building.InstanceId == "first-town-warehouse");
                Assert.Contains(starterWarehouse.StoredItems ?? [], item => item.Kind == "wooden_axe" && item.Quantity == 1);
                Assert.Contains(starterWarehouse.StoredItems ?? [], item => item.Kind == "wooden_pickaxe" && item.Quantity == 1);
                Assert.Empty(view.Baseline.Snapshot.Tiles);
                var cachedReconnect = new OwnerReconnectAction(0, entry.WorldId,
                    view.Baseline.Snapshot.MapManifestDigest, view.Baseline.Snapshot.MapLayersDigest);
                using var observedAgain = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/reconnect", cachedReconnect,
                    OwnerHttpBinding.ReconnectPayload(cachedReconnect));
                Assert.Equal(HttpStatusCode.OK, observedAgain.StatusCode);
                var cachedView = (await observedAgain.Content.ReadFromJsonAsync<ViewerOwnerReconnect>())!;
                Assert.Null(cachedView.Baseline.Snapshot.PackedTerrain);
                Assert.Null(cachedView.Baseline.Snapshot.PackedMapLayers);
                Assert.Empty(cachedView.Baseline.Snapshot.Tiles);
                Assert.Equal(view.Baseline.Snapshot.MapManifestDigest,
                    cachedView.Baseline.Snapshot.MapManifestDigest);
                Assert.Equal(view.Baseline.Snapshot.MapLayersDigest,
                    cachedView.Baseline.Snapshot.MapLayersDigest);
                var firstObservationBytes = (await observed.Content.ReadAsByteArrayAsync()).Length;
                var cachedObservationBytes = (await observedAgain.Content.ReadAsByteArrayAsync()).Length;
                Assert.True(firstObservationBytes - cachedObservationBytes >=
                    view.Baseline.Snapshot.PackedTerrain!.Data.Length +
                    view.Baseline.Snapshot.PackedMapLayers!.Elevation.Length);
                Assert.Empty(providers.CaptureRuntimeConfiguration().Assignments ?? []);
                Assert.Equal(5, host.Services.GetRequiredService<WorldAutosaveStore>().Capture().IntervalMinutes);
                Assert.DoesNotContain("test-secret-key", File.ReadAllText(Path.Combine(
                    host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".worlds", "catalog.json")));
                Assert.Contains(selectionLog.Messages, message => message.Contains(
                    "world_selection outcome=created", StringComparison.Ordinal));
                Assert.DoesNotContain(selectionLog.Messages, message => message.Contains(
                    "test-secret-key", StringComparison.Ordinal));

                var firstPath = Path.Combine(host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".worlds",
                    firstId + ".save");
                var originalBytes = File.ReadAllBytes(firstPath);
                var older = PrivateWorldRuntimeCodec.Decode(originalBytes) with { SchemaVersion = 16 };
                File.WriteAllBytes(firstPath, PrivateWorldRuntimeCodec.Encode(older));
                using var olderList = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/list", listAction, OwnerHttpBinding.EmptyPayload("list-worlds"));
                Assert.Equal("compatible", (await olderList.Content.ReadFromJsonAsync<WorldCatalogSnapshot>())!
                    .Worlds.Single(world => world.Id == firstId).Compatibility);
                File.WriteAllText(firstPath, "unsupported checkpoint");
                using var blockedList = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/list", listAction, OwnerHttpBinding.EmptyPayload("list-worlds"));
                Assert.Equal("incompatible", (await blockedList.Content.ReadFromJsonAsync<WorldCatalogSnapshot>())!
                    .Worlds.Single(world => world.Id == firstId).Compatibility);
                var blockedAction = new OwnerManualSaveAction("select-world", firstId);
                using var blocked = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", blockedAction,
                    OwnerHttpBinding.ManualSavePayload(blockedAction));
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                Assert.Equal("unsupported checkpoint", File.ReadAllText(firstPath));
                File.WriteAllBytes(firstPath, originalBytes);

                var select = new OwnerManualSaveAction("select-world", firstId);
                using var selected = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", select,
                    OwnerHttpBinding.ManualSavePayload(select));
                Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
                Assert.Null(runtime.ExportState().Geography);
                Assert.True(runtime.Society.IsPaused);
                Assert.Contains(providers.CaptureRuntimeConfiguration().Assignments ?? [],
                    assignment => assignment.CredentialSlotId == credentialSlotId);
                Assert.False(host.Services.GetRequiredService<WorldAutosaveStore>().Capture().Enabled);
                var returnToGenerated = new OwnerManualSaveAction("select-world", generatedId);
                using var returned = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", returnToGenerated,
                    OwnerHttpBinding.ManualSavePayload(returnToGenerated));
                Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
            }

            using var restarted = new ViewerWebApplicationFactory(directory.FullName, null,
                privateWorld: true, legacyPrivateWorld: false);
            using var restartedClient = restarted.CreateClient();
            var listAfterRestart = new OwnerControlAction("list-worlds");
            using var signedListAfterRestart = await SendSignedAsync(restarted, restartedClient,
                key, deviceId, "/api/v1/owner/worlds/list", listAfterRestart,
                OwnerHttpBinding.EmptyPayload("list-worlds"));
            Assert.Equal(HttpStatusCode.OK, signedListAfterRestart.StatusCode);
            var restoredCatalog = restarted.Services.GetRequiredService<WorldCatalogStore>().Capture();
            Assert.Equal(generatedId, restoredCatalog.ActiveId);
            Assert.Equal(2, restoredCatalog.Worlds.Count);
            Assert.Contains(restoredCatalog.Worlds, world => world.Id == generatedId);
            var restoredRuntime = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.Equal(WorldSizePreset.Small, restoredRuntime.ExportState().Geography?.Size);
            Assert.Equal(7, restoredRuntime.Content.Packages.Count);
            Assert.Equal(5, restoredRuntime.WorldSimulation.Buildings.Count);
            Assert.NotEmpty(restoredRuntime.RoadTiles);
            var selectedOld = restarted.Services.GetRequiredService<WorldSelectionCoordinator>()
                .Select(firstId);
            Assert.Equal(firstId, selectedOld.Id);
            Assert.Null(restoredRuntime.ExportState().Geography);
            Assert.False(restarted.Services.GetRequiredService<WorldAutosaveStore>().Capture().Enabled);
            var restoredEntry = restarted.Services.GetRequiredService<WorldSelectionCoordinator>()
                .Select(generatedId);
            Assert.Equal("Riverland", restoredEntry.Name);
            Assert.Equal(WorldSizePreset.Small, restoredRuntime.ExportState().Geography?.Size);
            Assert.True(restoredRuntime.Society.IsPaused);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task SignedManualSaveSurvivesRestartAndLoadPreservesThePreviousTimeline()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-manual-save-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string saveId;
            string backupId;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true,
                       legacyPrivateWorld: false))
            using (var client = host.CreateClient())
            {
                var saveLog = new RecordingLogger<ViewerHttpTests>();
                host.Services.GetRequiredService<ILoggerFactory>().AddProvider(
                    new RecordingLoggerProvider<ViewerHttpTests>(saveLog));
                var device = await StartAndActivateAsync(host, client, key);
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                Assert.True(runtime.Society.IsPaused);
                Assert.True(runtime.JevEnabled);
                var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
                var slotId = Guid.NewGuid().ToString("N");
                providers.Configure(new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini",
                    "test-secret-key", false, "founder:checkpoint", slotId, "Test account"));
                var create = new OwnerManualSaveAction("create", "Before changing Jev");
                const string createPath = "/api/v1/owner/saves/create";
                var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                    createPath, create, OwnerHttpBinding.ManualSavePayload(create));
                using var tampered = await client.PostAsJsonAsync(createPath, envelope with
                {
                    Action = create with { Value = "Different save" },
                });
                Assert.False(tampered.IsSuccessStatusCode);
                Assert.Empty(host.Services.GetRequiredService<ManualWorldSaveStore>().List());
                using var created = await SendSignedAsync(host, client, key, device.DeviceId,
                    createPath, create, OwnerHttpBinding.ManualSavePayload(create));
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                var saved = await created.Content.ReadFromJsonAsync<ManualWorldSave>();
                Assert.NotNull(saved);
                saveId = saved.Id;
                Assert.DoesNotContain("test-secret-key", File.ReadAllText(Path.Combine(
                    host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".manual", saveId + ".meta.json")));
                Assert.DoesNotContain("test-secret-key", File.ReadAllText(Path.Combine(
                    host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".manual", saveId + ".save")));

                var change = new OwnerJevAssistanceAction(false);
                using var changed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/jev-assistance", change,
                    OwnerHttpBinding.JevAssistancePayload(change));
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.False(runtime.JevEnabled);
                providers.Configure(new OwnerProviderConfigurationAction("personal", "inherit", null,
                    null, false, "founder:checkpoint"));
                Assert.Empty(providers.CaptureRuntimeConfiguration().Assignments ?? []);
                host.Services.GetRequiredService<WorldAutosaveStore>().Configure(false, 1, 0);

                var list = new OwnerControlAction("list-saves");
                using var listed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/saves/list", list, OwnerHttpBinding.EmptyPayload("list-saves"));
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                Assert.Contains((await listed.Content.ReadFromJsonAsync<ManualWorldSave[]>())!,
                    item => item.Id == saveId && item.Name == "Before changing Jev");

                var load = new OwnerManualSaveAction("load", saveId);
                using var loaded = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/saves/load", load, OwnerHttpBinding.ManualSavePayload(load));
                Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
                var receipt = await loaded.Content.ReadFromJsonAsync<ManualSaveLoadReceiptForTest>();
                Assert.NotNull(receipt);
                backupId = receipt.BackupId;
                Assert.True(runtime.JevEnabled);
                Assert.True(runtime.Society.IsPaused);
                Assert.True(host.Services.GetRequiredService<WorldAutosaveStore>().Capture().Enabled);
                Assert.Equal(5, host.Services.GetRequiredService<WorldAutosaveStore>().Capture().IntervalMinutes);
                Assert.False(host.Services.GetRequiredService<ManualWorldSaveStore>().Read(backupId).JevEnabled);
                Assert.Contains(providers.CaptureRuntimeConfiguration().Assignments ?? [],
                    item => item.InhabitantId == "founder:checkpoint" && item.CredentialSlotId == slotId);
                var statusAction = new OwnerControlAction("autosave-status");
                using var status = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/saves/autosave/status", statusAction,
                    OwnerHttpBinding.EmptyPayload("autosave-status"));
                Assert.Equal(HttpStatusCode.OK, status.StatusCode);
                Assert.Equal(5, (await status.Content.ReadFromJsonAsync<WorldAutosaveSettings>())!.IntervalMinutes);
                var autosaveAction = new OwnerAutosaveConfigurationAction(true, 1, 0);
                const string autosavePath = "/api/v1/owner/saves/autosave/configure";
                var autosaveEnvelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                    autosavePath, autosaveAction, OwnerHttpBinding.AutosaveConfigurationPayload(autosaveAction));
                using var tamperedAutosave = await client.PostAsJsonAsync(autosavePath, autosaveEnvelope with
                {
                    Action = autosaveAction with { Enabled = false },
                });
                Assert.False(tamperedAutosave.IsSuccessStatusCode);
                using var configured = await SendSignedAsync(host, client, key, device.DeviceId,
                    autosavePath, autosaveAction, OwnerHttpBinding.AutosaveConfigurationPayload(autosaveAction));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                Assert.Equal(0, host.Services.GetRequiredService<WorldAutosaveStore>().Capture().RotationCount);
                Assert.Contains(saveLog.Messages, message => message.Contains("manual_save outcome=created", StringComparison.Ordinal));
                Assert.Contains(saveLog.Messages, message => message.Contains("manual_save outcome=loaded", StringComparison.Ordinal));
                Assert.Contains(saveLog.Messages, message => message.Contains("autosave_settings outcome=changed", StringComparison.Ordinal));
                Assert.DoesNotContain(saveLog.Messages, message => message.Contains("test-secret-key", StringComparison.Ordinal));
            }

            using var restarted = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true,
                legacyPrivateWorld: false);
            using var restartedClient = restarted.CreateClient();
            Assert.True(restarted.Services.GetRequiredService<PrivateWorldRuntime>().JevEnabled);
            var saves = restarted.Services.GetRequiredService<ManualWorldSaveStore>().List();
            Assert.Contains(saves, item => item.Id == saveId);
            Assert.Contains(saves, item => item.Id == backupId);
            Assert.Equal(1, restarted.Services.GetRequiredService<WorldAutosaveStore>().Capture().IntervalMinutes);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed record ManualSaveLoadReceiptForTest(string LoadedId, string BackupId, long WorldTick);

    [Fact]
    public async Task SignedOverwriteTargetsOneSaveIdAndKeepsThePriorCheckpoint()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-save-overwrite-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null,
                privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var store = host.Services.GetRequiredService<ManualWorldSaveStore>();
            Assert.True(runtime.JevEnabled);
            var create = new OwnerManualSaveAction("create", "Same name");
            using var firstResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/create", create, OwnerHttpBinding.ManualSavePayload(create));
            var first = (await firstResponse.Content.ReadFromJsonAsync<ManualWorldSave>())!;
            using var secondResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/create", create, OwnerHttpBinding.ManualSavePayload(create));
            var second = (await secondResponse.Content.ReadFromJsonAsync<ManualWorldSave>())!;
            Assert.NotEqual(first.Id, second.Id);

            var change = new OwnerJevAssistanceAction(false);
            using var changed = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/jev-assistance", change,
                OwnerHttpBinding.JevAssistancePayload(change));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var overwrite = new OwnerManualSaveAction("overwrite", first.Id);
            const string path = "/api/v1/owner/saves/overwrite";
            var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                path, overwrite, OwnerHttpBinding.ManualSavePayload(overwrite));
            using var tampered = await client.PostAsJsonAsync(path, envelope with
            {
                Action = overwrite with { Value = second.Id },
            });
            Assert.False(tampered.IsSuccessStatusCode);
            Assert.Null(store.Read(first.Id).JevEnabled);
            using var overwritten = await SendSignedAsync(host, client, key, device.DeviceId,
                path, overwrite, OwnerHttpBinding.ManualSavePayload(overwrite));
            Assert.Equal(HttpStatusCode.OK, overwritten.StatusCode);
            var receipt = (await overwritten.Content.ReadFromJsonAsync<ManualSaveOverwriteReceipt>())!;
            Assert.Equal(first.Id, receipt.Saved.Id);
            Assert.False(store.Read(first.Id).JevEnabled);
            Assert.Null(store.Read(second.Id).JevEnabled);
            Assert.Null(store.Read(receipt.BackupId).JevEnabled);
            Assert.Equal(3, store.List().Count);
            Assert.Equal(2, store.List().Count(item => item.Name == "Same name"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void LoadingAnEarlierFounderCheckpointRestoresTheSetupGate()
    {
        using var runtime = new PrivateWorldRuntime("founder-save-rewind", startPace: WorldStartPace.FounderSetup);
        var emptyCamp = runtime.ExportState();
        runtime.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), new(0, 0));
        Assert.Single(runtime.FounderSetup!.FounderIds);

        runtime.LoadPausedCheckpoint(emptyCamp);

        Assert.Empty(runtime.FounderSetup!.FounderIds);
        Assert.Empty(runtime.Inhabitants);
        Assert.Throws<InvalidOperationException>(runtime.Resume);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("null")]
    [InlineData("missing-save")]
    [InlineData("aliased-id")]
    public void DamagedSaveMetadataDoesNotHideSoundSavesOrStopRotation(string damage)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-damaged-save-");
        try
        {
            using var runtime = new PrivateWorldRuntime("damaged-save-list");
            runtime.Pause();
            var path = Path.Combine(directory.FullName, "world.json");
            var log = new RecordingLogger<ManualWorldSaveStore>();
            var store = new ManualWorldSaveStore(path, log);
            var manual = store.Create("Keep me", runtime, []);
            var settings = new WorldAutosaveSettings(runtime.Society.WorldId, true, 5, 5, DateTimeOffset.MinValue, 0);
            var old = store.CreateAutosave(runtime, [], settings);
            var newest = store.CreateAutosave(runtime, [], settings);
            var damagedId = Guid.NewGuid().ToString("N");
            var metadataPath = Path.Combine(path + ".manual", damagedId + ".meta.json");
            var bytes = damage switch
            {
                "truncated" => "{private-provider-secret",
                "null" => "null",
                "missing-save" => "{}",
                _ => File.ReadAllText(Path.Combine(path + ".manual", manual.Id + ".meta.json"))
                    .Replace("\"IsAutosave\":false", "\"IsAutosave\":true", StringComparison.Ordinal)
            };
            File.WriteAllText(metadataPath, bytes);

            var listed = store.List(runtime.Society.WorldId);
            Assert.Equal(3, listed.Count);
            Assert.Single(listed, save => save.Id == manual.Id && !save.IsAutosave);
            store.KeepNewestAutosaves(1, newest.Id, runtime.Society.WorldId);
            Assert.DoesNotContain(store.List(), save => save.Id == old.Id);
            Assert.Contains(store.List(), save => save.Id == newest.Id);
            Assert.Equal(runtime.Society.WorldId, store.Read(manual.Id).Society.Society.WorldId);
            Assert.Equal(bytes, File.ReadAllText(metadataPath));
            var message = Assert.Single(log.Messages);
            Assert.Contains("outcome=excluded_from_list", message, StringComparison.Ordinal);
            Assert.Contains("save=" + damagedId, message, StringComparison.Ordinal);
            Assert.DoesNotContain("private-provider-secret", message, StringComparison.Ordinal);
            Assert.DoesNotContain(directory.FullName, message, StringComparison.Ordinal);
            // A repaired entry becomes reachable again without restarting the host.
            var repaired = File.ReadAllText(Path.Combine(path + ".manual", manual.Id + ".meta.json"))
                .Replace(manual.Id, damagedId, StringComparison.Ordinal);
            File.WriteAllText(metadataPath, repaired);
            File.Copy(Path.Combine(path + ".manual", manual.Id + ".save"),
                Path.Combine(path + ".manual", damagedId + ".save"));
            Assert.Contains(store.List(), save => save.Id == damagedId);
            File.WriteAllText(metadataPath, bytes);
            _ = store.List();
            Assert.Equal(2, log.Messages.Count);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task AutosaveScheduleRotatesOnlyAutomaticSnapshotsAndPersistsOwnerChoices()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-autosave-");
        try
        {
            using var runtime = new PrivateWorldRuntime("autosave-rotation");
            var path = Path.Combine(directory.FullName, "world.json");
            var saves = new ManualWorldSaveStore(path);
            var autosave = new WorldAutosaveStore(path, runtime.Society.WorldId);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            Assert.True(autosave.Capture().Enabled);
            Assert.Equal(5, autosave.Capture().IntervalMinutes);
            Assert.Equal(5, autosave.Capture().RotationCount);
            runtime.Pause();
            var manual = saves.Create("Keep me", runtime, []);
            runtime.Resume();

            var firstTick = await runtime.AdvanceOneTickAsync();
            Assert.True(firstTick.Advanced);
            var start = DateTimeOffset.UtcNow.AddMinutes(6);
            Assert.NotNull(autosave.MaybeSave(start, runtime, providers, saves));
            Assert.Null(autosave.MaybeSave(start.AddMinutes(6), runtime, providers, saves));
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.NotNull(autosave.MaybeSave(start.AddMinutes(12), runtime, providers, saves));
            Assert.Equal(2, saves.List().Count(item => item.IsAutosave));

            autosave.Configure(true, 1, 0);
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.NotNull(autosave.MaybeSave(start.AddMinutes(24), runtime, providers, saves));
            Assert.Single(saves.List(), item => item.IsAutosave);
            Assert.Contains(saves.List(), item => item.Id == manual.Id && !item.IsAutosave);

            autosave.Configure(false, 1, 0);
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.Null(autosave.MaybeSave(start.AddMinutes(36), runtime, providers, saves));
            var reloaded = new WorldAutosaveStore(path, runtime.Society.WorldId);
            Assert.False(reloaded.Capture().Enabled);
            Assert.Equal(0, reloaded.Capture().RotationCount);
        }
        finally { directory.Delete(recursive: true); }
    }
}
