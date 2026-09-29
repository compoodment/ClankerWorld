using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task BuildingWorkbenchPreviewsWithoutMutationThenUsesExplicitSignedLifecycle()
    {
        var directory = Directory.CreateTempSubdirectory("building-workbench-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            host.Services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var saveBefore = File.ReadAllBytes(Path.Combine(directory.FullName, "runtime.json"));
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(new RecordingLoggerProvider<PrivateWorldRuntimeService>(logger));
            var action = new OwnerBuildingDesignAction("sk-private-design-name", "shelter", 9);
            const string previewPath = "/api/v1/owner/content/building-preview";
            using var tampered = await SendSignedAsync(host, client, key, device.DeviceId, previewPath,
                action with { WoodCost = 1 }, OwnerBuildingDesign.Payload(action));
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
            using var response = await SendSignedAsync(host, client, key, device.DeviceId, previewPath, action, OwnerBuildingDesign.Payload(action));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var preview = await response.Content.ReadFromJsonAsync<OwnerBuildingDesignPreview>();
            Assert.NotNull(preview);
            Assert.True(preview.ConstructionPassed);
            Assert.Equal(9, preview.WoodConsumed);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(saveBefore, File.ReadAllBytes(Path.Combine(directory.FullName, "runtime.json")));
            Assert.Contains(logger.Messages, message => message.Contains("content_building_preview", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains(action.Name, StringComparison.Ordinal));
            var observations = host.Services.GetRequiredService<OwnerWorldObservationStore>();
            Assert.Contains("owner-building-design.v1", observations.GetOwnerHandshake().ServerCapabilities);

            using var proposed = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/content/propose",
                preview.Package, OwnerContentBinding.ProposePayload(preview.Package));
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            var packageId = new OwnerContentPackageIdAction(preview.Package.PackageId);
            // The saved proposal can be reviewed by a fresh client with no local draft.
            var proposedBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using var review = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/content/building-review",
                packageId, OwnerContentBinding.PackageIdPayload("building-review", packageId));
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            var reviewed = await review.Content.ReadFromJsonAsync<ClankerWorld.GodotClient.UI.OwnerBuildingDesignPreview>();
            Assert.Equal(preview.ManifestDigest, reviewed!.ManifestDigest);
            Assert.Equal(action.Name, reviewed.Design.Name);
            Assert.Equal(proposedBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

            foreach (var operation in new[] { "validate", "approve", "stage" })
            {
                using var result = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/content/" + operation,
                    packageId, OwnerContentBinding.PackageIdPayload(operation, packageId));
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            }
            Assert.False((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.Empty(runtime.WorldContent.Buildings);
            runtime.Resume();
            await runtime.AdvanceOneTickAsync();
            var definition = Assert.Single(runtime.WorldContent.Buildings);
            Assert.Equal(action.Name, definition.DisplayName);
            var state = runtime.ExportState();
            var worker = state.Inhabitants.First(person => !state.Map.CampObjects.Any(item => item.Position == person.Position) &&
                !state.Map.Resources.Any(item => item.Position == person.Position));
            var placement = runtime.PlaceBuilding("workbench-home", definition.CanonicalId, worker.Position);
            Assert.True(placement.Applied, placement.Failure);
            var snapshot = observations.GetSnapshot();
            var building = snapshot.PlacedBuildings.Single(item => item.InstanceId == placement.InstanceId);
            Assert.Equal(action.Name, building.DisplayName);
            Assert.Contains("shelter", building.Tags!);
            Assert.Equal(1, building.Width);
            Assert.Equal(1, building.Height);
            var package = Assert.Single(snapshot.ContentPackages);
            Assert.Equal(action.Name, package.DisplayName);
            Assert.Equal(preview.ManifestDigest, package.ManifestDigest);
            using var restored = PrivateWorldRuntime.Restore(runtime.ExportState());
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { directory.Delete(recursive: true); }
    }
}
