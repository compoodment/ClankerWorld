using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
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
    [Theory]
    [InlineData(false, "\"32\"")]
    [InlineData(false, "2147483648")]
    [InlineData(true, "[]")]
    public async Task MalformedSaveJsonIsReportedAsInvalidWhilePausedAndPreservesTheWorld(
        bool malformedEnvelope, string malformedJson)
    {
        var directory = Directory.CreateTempSubdirectory("malformed-save-schema-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            file.Save(runtime);
            var activeBytes = File.ReadAllBytes(file.Path);
            var runtimeBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var activeId = catalog.Capture().ActiveId;
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var saved = saves.Create("Recoverable", runtime, []);
            var path = Path.Combine(file.Path + ".manual", saved.Id + ".save");
            var healthy = File.ReadAllBytes(path);
            byte[] damaged;
            if (malformedEnvelope)
                damaged = Encoding.UTF8.GetBytes(malformedJson);
            else
            {
                var document = JsonNode.Parse(healthy)!.AsObject();
                document["state"]!["schemaVersion"] = JsonNode.Parse(malformedJson);
                damaged = Encoding.UTF8.GetBytes(document.ToJsonString());
            }
            File.WriteAllBytes(path, damaged);

            var action = new OwnerManualSaveAction("load", saved.Id);
            using var refused = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/load", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("The save is invalid", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(runtimeBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(activeId, catalog.Capture().ActiveId);
            Assert.Equal(damaged, File.ReadAllBytes(path));
            Assert.Single(saves.List(runtime.Society.WorldId));

            File.WriteAllBytes(path, healthy);
            using var accepted = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/load", action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            runtime.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    public async Task AutosaveConfigurationNeverTrimsAnotherWorld(int rotation)
    {
        var directory = Directory.CreateTempSubdirectory("autosave-world-boundary-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            using var other = new PrivateWorldRuntime("other-autosave-world");
            other.Pause();
            var settings = new WorldAutosaveSettings(other.Society.WorldId, true, 1, 3, DateTimeOffset.MinValue, 0);
            var otherIds = Enumerable.Range(0, 3).Select(_ => saves.CreateAutosave(other, [], settings).Id).ToArray();
            var root = host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".manual";
            var before = otherIds.SelectMany(id => new[] { id + ".save", id + ".meta.json" })
                .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(root, name)));
            var activeSettings = settings with { WorldId = runtime.Society.WorldId };
            for (var n = 0; n < 12; n++) saves.CreateAutosave(runtime, [], activeSettings);
            var action = new OwnerAutosaveConfigurationAction(true, 1, rotation);
            using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/autosave/configure", action, OwnerHttpBinding.AutosaveConfigurationPayload(action));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Math.Max(1, rotation), saves.List(runtime.Society.WorldId).Count(item => item.IsAutosave));
            Assert.Equal(otherIds.Order(), saves.List(other.Society.WorldId).Select(item => item.Id).Order());
            foreach (var (name, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, name)));
        }
        finally { directory.Delete(recursive: true); }
    }

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

            var createAction = options with
            {
                CandidateAttempt = preview.Coverage!.Attempt,
                ExpectedManifestDigest = preview.ManifestDigest,
                ExpectedMapLayersDigest = preview.MapLayersDigest,
            };
            using var refusedCreation = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/create", createAction,
                OwnerHttpBinding.WorldCreationPayload(createAction));
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
            PrivateWorldRuntimeState generatedLandState;
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
                Assert.NotNull(preview.Coverage);
                Assert.Contains(selectionLog.Messages, message => message.Contains(
                    "world_preview outcome=generated width=256", StringComparison.Ordinal));
                Assert.Null(host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState().Geography);
                Assert.Single(host.Services.GetRequiredService<WorldCatalogStore>().Capture().Worlds);
                var createAction = create with
                {
                    CandidateAttempt = preview.Coverage!.Attempt,
                    ExpectedManifestDigest = preview.ManifestDigest,
                    ExpectedMapLayersDigest = preview.MapLayersDigest,
                };
                using var created = await SendSignedAsync(host, client, key, device.DeviceId,
                    createPath, createAction, OwnerHttpBinding.WorldCreationPayload(createAction));
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                var entry = (await created.Content.ReadFromJsonAsync<CatalogWorld>())!;
                generatedId = entry.Id;
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                Assert.True(runtime.Society.IsPaused);
                Assert.Empty(runtime.Inhabitants);
                Assert.Equal(0, runtime.WorldTick);
                Assert.Equal(21, runtime.Content.Packages.Count);
                AssertBuildingVariantPackagesActive(runtime);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == PotteryContent.PackageId);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == RestaurantContent.PackageId);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == BusinessContent.PackageId);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == CareContent.PackageId);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == OrnamentContent.PackageId);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == TownHallContent.PackageId);
                Assert.Contains(runtime.Content.Packages, package => package.Manifest.PackageId == KnowledgeContent.PackageId);
                Assert.All(runtime.Content.Packages, package =>
                    Assert.Equal(ContentPackageLifecycle.Active, package.Lifecycle));
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "house-1x1");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "warehouse-2x2");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "silo-1x1");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "farmhouse-1x1");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "blacksmith-1x2");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "restaurant-1x2");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "store-1x1");
                Assert.Contains(runtime.WorldContent.Buildings, building => building.LocalId == "town-hall-3x4");
                Assert.Equal(WorldSizePreset.Small, runtime.ExportState().Geography?.Size);
                Assert.Equal(256, runtime.ExportState().Map.Width);
                Assert.Equal(GeographyGenerator.CurrentHydrologyVersion, runtime.ExportState().Geography!.HydrologyVersion);
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
                generatedLandState = runtime.ExportState();
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
                var olderDocument = JsonNode.Parse(originalBytes)!.AsObject();
                var olderState = olderDocument["state"]!.AsObject();
                olderState["schemaVersion"] = PrivateWorldRuntime.StateSchemaVersion - 1;
                olderState.Remove("bridges");
                olderState.Remove("bridgeTraffic");
                olderState["map"]!.AsObject().Remove("bridgeDecks");
                var olderBytes = Encoding.UTF8.GetBytes(olderDocument.ToJsonString());
                File.WriteAllBytes(firstPath, olderBytes);
                using var olderList = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/list", listAction, OwnerHttpBinding.EmptyPayload("list-worlds"));
                var blockedSnapshot = (await olderList.Content.ReadFromJsonAsync<WorldCatalogSnapshot>())!;
                var blockedWorld = blockedSnapshot.Worlds.Single(world => world.Id == firstId);
                Assert.Equal("incompatible", blockedWorld.Compatibility);
                Assert.Equal("The saved checkpoint or required content cannot be restored.",
                    blockedWorld.CompatibilityReason);
                var activeIdBeforeRefusal = host.Services.GetRequiredService<WorldCatalogStore>().Capture().ActiveId;
                var activeWorldIdBeforeRefusal = runtime.Society.WorldId;
                var activeBytesBeforeRefusal = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
                Assert.Equal(generatedId, activeIdBeforeRefusal);
                var blockedAction = new OwnerManualSaveAction("select-world", firstId);
                using var olderSelect = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", blockedAction,
                    OwnerHttpBinding.ManualSavePayload(blockedAction));
                Assert.Equal(HttpStatusCode.Conflict, olderSelect.StatusCode);
                Assert.Equal(olderBytes, File.ReadAllBytes(firstPath));
                Assert.Equal(activeIdBeforeRefusal,
                    host.Services.GetRequiredService<WorldCatalogStore>().Capture().ActiveId);
                Assert.Equal(activeWorldIdBeforeRefusal, runtime.Society.WorldId);
                Assert.Equal(activeBytesBeforeRefusal, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

                File.WriteAllText(firstPath, "unsupported checkpoint");
                using var blockedList = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/list", listAction, OwnerHttpBinding.EmptyPayload("list-worlds"));
                Assert.Equal("incompatible", (await blockedList.Content.ReadFromJsonAsync<WorldCatalogSnapshot>())!
                    .Worlds.Single(world => world.Id == firstId).Compatibility);
                using var blocked = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", blockedAction,
                    OwnerHttpBinding.ManualSavePayload(blockedAction));
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                Assert.Equal("unsupported checkpoint", File.ReadAllText(firstPath));
                Assert.Equal(activeIdBeforeRefusal,
                    host.Services.GetRequiredService<WorldCatalogStore>().Capture().ActiveId);
                Assert.Equal(activeWorldIdBeforeRefusal, runtime.Society.WorldId);
                Assert.Equal(activeBytesBeforeRefusal, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
                File.WriteAllBytes(firstPath, originalBytes);

                var originalCheckpoint = PrivateWorldRuntimeCodec.Decode(originalBytes);
                Assert.NotEqual(JsonSerializer.Serialize(originalCheckpoint.TownLandTitles),
                    JsonSerializer.Serialize(generatedLandState.TownLandTitles));

                var select = new OwnerManualSaveAction("select-world", firstId);
                using var selected = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/worlds/select", select,
                    OwnerHttpBinding.ManualSavePayload(select));
                Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
                AssertSavedTownLandRecordsMatch(originalCheckpoint, runtime.ExportState());
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
                AssertSavedTownLandRecordsMatch(generatedLandState, runtime.ExportState());
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
            AssertSavedTownLandRecordsMatch(generatedLandState,
                restarted.Services.GetRequiredService<PrivateWorldRuntime>().ExportState());
            Assert.Equal(2, restoredCatalog.Worlds.Count);
            Assert.Contains(restoredCatalog.Worlds, world => world.Id == generatedId);
            var restoredRuntime = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.Equal(WorldSizePreset.Small, restoredRuntime.ExportState().Geography?.Size);
            Assert.Equal(21, restoredRuntime.Content.Packages.Count);
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == TownHallContent.PackageId);
            AssertBuildingVariantPackagesActive(restoredRuntime);
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == PotteryContent.PackageId);
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == RestaurantContent.PackageId);
            Assert.Contains(restoredRuntime.WorldContent.Buildings, building => building.LocalId == "restaurant-1x2");
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == BusinessContent.PackageId);
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == CareContent.PackageId);
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == OrnamentContent.PackageId);
            Assert.Contains(restoredRuntime.Content.Packages, package => package.Manifest.PackageId == KnowledgeContent.PackageId);
            Assert.Contains(restoredRuntime.WorldContent.Buildings, building => building.LocalId == "store-1x1");
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
                providers.Configure(new OwnerProviderConfigurationAction("routine", "deterministic", null,
                    null, false));
                providers.Configure(new OwnerProviderConfigurationAction("planning", "ollama-cloud", "world-model",
                    "test-world-key", false));
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
                Assert.DoesNotContain("test-world-key", File.ReadAllText(Path.Combine(
                    host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".manual", saveId + ".meta.json")));
                Assert.DoesNotContain("test-world-key", File.ReadAllText(Path.Combine(
                    host.Services.GetRequiredService<PrivateWorldStateFile>().Path + ".manual", saveId + ".save")));

                var change = new OwnerJevAssistanceAction(false);
                using var changed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/jev-assistance", change,
                    OwnerHttpBinding.JevAssistancePayload(change));
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.False(runtime.JevEnabled);
                providers.Configure(new OwnerProviderConfigurationAction("personal", "inherit", null,
                    null, false, "founder:checkpoint"));
                var inheritedConfiguration = providers.CaptureStatus();
                Assert.Equal(PlayerDecisionProviders.Deterministic, inheritedConfiguration.RoutineProvider);
                Assert.Equal(PlayerDecisionProviders.OllamaCloud, inheritedConfiguration.PlanningProvider);
                var inheritedRoutes = inheritedConfiguration.Assignments!
                    .Where(item => item.InhabitantId == "founder:checkpoint").ToArray();
                Assert.Equal(2, inheritedRoutes.Length);
                Assert.Equal(new[] { PlayerDecisionProviders.PlanningRole, PlayerDecisionProviders.RoutineRole },
                    inheritedRoutes.Select(item => item.Role).Order(StringComparer.Ordinal));
                Assert.All(inheritedRoutes, assignment =>
                {
                    Assert.Equal(PlayerDecisionProviders.Inherit, assignment.Provider);
                    Assert.Null(assignment.Model);
                    Assert.Null(assignment.CredentialSlotId);
                    Assert.Null(assignment.SelectionReason);
                });
                var decisionProvider = host.Services.GetRequiredService<ConfigurableDecisionProvider>();
                var routineObservation = new InhabitantObservation("founder:checkpoint", runtime.WorldTick, 0, 0,
                    "sha256:inheritance-fallback", 5_000,
                    [new CognitionCandidate("safe_idle", "Wait safely.")]);
                var planningObservation = routineObservation with
                {
                    Candidates = [new CognitionCandidate("build:building:shelter", "Build a shelter.")],
                };
                Assert.Equal(DecisionProviderKind.Deterministic, decisionProvider.KindFor(routineObservation));
                Assert.Equal(DecisionProviderKind.LargeLanguageModel, decisionProvider.KindFor(planningObservation));
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
                // The world left behind stays on the save's branch; playing on from
                // the loaded save starts a second branch.
                var branchStore = host.Services.GetRequiredService<ManualWorldSaveStore>();
                var original = Assert.Single(branchStore.List(), item => item.Id == saveId);
                Assert.Equal(original.Branch, Assert.Single(branchStore.List(), item => item.Id == backupId).Branch);
                var afterLoad = new OwnerManualSaveAction("create", "After loading");
                using var savedAfterLoad = await SendSignedAsync(host, client, key, device.DeviceId,
                    createPath, afterLoad, OwnerHttpBinding.ManualSavePayload(afterLoad));
                Assert.Equal(HttpStatusCode.OK, savedAfterLoad.StatusCode);
                var branched = await savedAfterLoad.Content.ReadFromJsonAsync<ManualWorldSave>();
                Assert.Equal(2, branched?.Branch?.Number);
                Assert.Equal(saveId, branched?.Branch?.StartedFromId);
                var timelineAction = new OwnerControlAction("save-timeline");
                using var timeline = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/saves/timeline", timelineAction, OwnerHttpBinding.EmptyPayload("save-timeline"));
                Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
                Assert.Equal(new SaveTimelinePosition(branched?.Id, branched?.Branch?.Id, false,
                    branched?.Branch?.Number, branched?.WorldTick),
                    await timeline.Content.ReadFromJsonAsync<SaveTimelinePosition>());
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
                Assert.DoesNotContain(saveLog.Messages, message => message.Contains("test-world-key", StringComparison.Ordinal));
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

    [Fact]
    public async Task BrowsingRunningAutosavesWithoutPlayingCreatesNoExtraBackupOrBranch()
    {
        var directory = Directory.CreateTempSubdirectory("browse-running-autosaves-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var autosave = host.Services.GetRequiredService<WorldAutosaveStore>();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            runtime.Resume();
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            var first = saves.CreateAutosave(runtime, providers.CaptureRuntimeConfiguration().Assignments ?? [],
                autosave.Capture());
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            var second = saves.CreateAutosave(runtime, providers.CaptureRuntimeConfiguration().Assignments ?? [],
                autosave.Capture());
            Assert.False(saves.Read(first.Id).Society.Society.IsPaused);
            Assert.False(saves.Read(second.Id).Society.Society.IsPaused);
            var firstBytes = PrivateWorldRuntimeCodec.Encode(saves.Read(first.Id));
            var secondBytes = PrivateWorldRuntimeCodec.Encode(saves.Read(second.Id));
            runtime.Pause();

            const string loadPath = "/api/v1/owner/saves/load";
            var loadFirst = new OwnerManualSaveAction("load", first.Id);
            using var firstResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                loadPath, loadFirst, OwnerHttpBinding.ManualSavePayload(loadFirst));
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.True(runtime.Society.IsPaused);
            var beforeBrowsing = saves.List(runtime.Society.WorldId);

            var loadSecond = new OwnerManualSaveAction("load", second.Id);
            using var secondResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                loadPath, loadSecond, OwnerHttpBinding.ManualSavePayload(loadSecond));
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var receipt = await secondResponse.Content.ReadFromJsonAsync<ManualSaveLoadReceiptForTest>();
            Assert.NotNull(receipt);
            Assert.Equal(first.Id, receipt.BackupId);
            Assert.Equal(second.WorldTick, runtime.WorldTick);
            Assert.True(runtime.Society.IsPaused);
            var afterBrowsing = saves.List(runtime.Society.WorldId);
            Assert.Equal(beforeBrowsing.Select(save => save.Id).Order(), afterBrowsing.Select(save => save.Id).Order());
            Assert.All(afterBrowsing, save => Assert.Equal(first.Branch, save.Branch));
            Assert.Equal(firstBytes, PrivateWorldRuntimeCodec.Encode(saves.Read(first.Id)));
            Assert.Equal(secondBytes, PrivateWorldRuntimeCodec.Encode(saves.Read(second.Id)));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadingAnotherSavePreservesTheCurrentWorldWhenItsSourceCheckpointIsLost(bool damagedBody)
    {
        var directory = Directory.CreateTempSubdirectory("loaded-save-source-lost-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var providers = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var autosave = host.Services.GetRequiredService<WorldAutosaveStore>();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
            runtime.Pause();
            var first = saves.Create("Jev on", runtime,
                providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
            runtime.SetJevEnabled(false);
            var second = saves.Create("Jev off", runtime,
                providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
            var secondBytes = PrivateWorldRuntimeCodec.Encode(saves.Read(second.Id));

            const string loadPath = "/api/v1/owner/saves/load";
            var loadFirst = new OwnerManualSaveAction("load", first.Id);
            using var firstResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                loadPath, loadFirst, OwnerHttpBinding.ManualSavePayload(loadFirst));
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.True(runtime.JevEnabled);
            var currentBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var sourcePath = Path.Combine(stateFile.Path + ".manual", first.Id + ".save");
            var damagedBytes = Encoding.UTF8.GetBytes("{truncated checkpoint");
            if (damagedBody) File.WriteAllBytes(sourcePath, damagedBytes);
            else File.Delete(sourcePath);
            Assert.True(File.Exists(Path.Combine(stateFile.Path + ".manual", first.Id + ".meta.json")));

            var loadSecond = new OwnerManualSaveAction("load", second.Id);
            using var secondResponse = await SendSignedAsync(host, client, key, device.DeviceId,
                loadPath, loadSecond, OwnerHttpBinding.ManualSavePayload(loadSecond));
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var receipt = await secondResponse.Content.ReadFromJsonAsync<ManualSaveLoadReceiptForTest>();
            Assert.NotNull(receipt);
            Assert.Equal(second.Id, receipt.LoadedId);
            Assert.NotEqual(first.Id, receipt.BackupId);
            var backup = Assert.Single(saves.List(runtime.Society.WorldId), save => save.Id == receipt.BackupId);
            Assert.Equal("Before loading", backup.Name);
            Assert.Equal(currentBytes, PrivateWorldRuntimeCodec.Encode(saves.Read(backup.Id)));
            Assert.False(runtime.JevEnabled);
            Assert.True(runtime.Society.IsPaused);
            Assert.Equal(secondBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(secondBytes, PrivateWorldRuntimeCodec.Encode(saves.Read(second.Id)));
            if (damagedBody) Assert.Equal(damagedBytes, File.ReadAllBytes(sourcePath));
            else Assert.False(File.Exists(sourcePath));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AssertBuildingVariantPackagesActive(PrivateWorldRuntime runtime)
    {
        foreach (var id in new[]
                 {
                     FarmhouseVariantContent.PackageId, BlacksmithVariantContent.PackageId,
                     TailorVariantContent.PackageId, ClinicVariantContent.PackageId,
                     RestaurantVariantContent.PackageId,
                 })
        {
            var package = Assert.Single(runtime.Content.Packages, item => item.Manifest.PackageId == id);
            Assert.Equal(ContentPackageLifecycle.Active, package.Lifecycle);
            Assert.Equal(0, package.ActivationTick);
            Assert.Contains(runtime.WorldContent.Buildings,
                building => building.PackageDigest == package.Manifest.PackageDigest);
        }
    }

    private static void AssertSavedTownLandRecordsMatch(PrivateWorldRuntimeState expected,
        PrivateWorldRuntimeState actual)
    {
        Assert.Equal(JsonSerializer.Serialize(expected.TownLandTitles),
            JsonSerializer.Serialize(actual.TownLandTitles));
        Assert.Equal(JsonSerializer.Serialize(expected.HouseholdLandUseRights),
            JsonSerializer.Serialize(actual.HouseholdLandUseRights));
        Assert.Equal(JsonSerializer.Serialize(expected.HouseholdLandUseRequests),
            JsonSerializer.Serialize(actual.HouseholdLandUseRequests));
    }
}
