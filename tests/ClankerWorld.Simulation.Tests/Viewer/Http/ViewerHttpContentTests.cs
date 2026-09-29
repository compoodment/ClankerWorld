using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task PairedOwnerCanAttachOnlyAnExactReferenceFromTheHostConfiguredCatalog()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-approved-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = System.IO.Path.Combine(directory, "approved-assets.json");
            File.WriteAllText(
                catalogPath,
                $$"""{"schemaVersion":1,"references":[{"assetId":"portrait-alice","assetDigest":"{{ApprovedAssetDigest}}"}]}""");

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory, catalogPath);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);

            using var pause = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/control/pause",
                new OwnerControlAction("pause"),
                OwnerHttpBinding.EmptyPayload("pause"));
            Assert.Equal(HttpStatusCode.OK, pause.StatusCode);

            var allowedAction = new OwnerAuthoringBatchAction(
                "approved-asset-http",
                [new OwnerAuthoringOperationAction(
                    "add_approved_asset_reference",
                    "portrait-alice",
                    ApprovedAssetDigest,
                    null,
                    null,
                    null,
                    null)]);
            using var allowed = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/authoring",
                allowedAction,
                OwnerHttpBinding.AuthoringPayload(allowedAction));
            var allowedReceipt = await allowed.Content.ReadFromJsonAsync<OwnerAuthoringBatchReceipt>();

            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.NotNull(allowedReceipt);
            Assert.True(allowedReceipt.Applied, allowedReceipt.Failure);

            var rejectedAction = allowedAction with
            {
                BatchId = "unapproved-asset-http",
                Operations = [new OwnerAuthoringOperationAction(
                    "add_approved_asset_reference",
                    "portrait-bob",
                    ApprovedAssetDigest,
                    null,
                    null,
                    null,
                    null)],
            };
            using var rejected = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/authoring",
                rejectedAction,
                OwnerHttpBinding.AuthoringPayload(rejectedAction));
            var rejectedReceipt = await rejected.Content.ReadFromJsonAsync<OwnerAuthoringBatchReceipt>();

            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.NotNull(rejectedReceipt);
            Assert.False(rejectedReceipt.Applied);
            Assert.Contains("server-owned asset catalog", rejectedReceipt.Failure, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PairedOwnerCanGovernPrivateContentThroughTheSignedLifecycle()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-private-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory, null, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var packageDigest =
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var packageVersion = ContentVersion.Parse("1.0.0");
            var building = new BuildingDefinition(
                packageDigest,
                "camp-kitchen",
                packageVersion,
                "Camp kitchen",
                1,
                1,
                2,
                [new ContentQuantity("wood", 2)],
                ["camp"]);
            var package = new OwnerContentPackageAction(
                "camp-recipes",
                "1.0.0",
                packageDigest,
                [],
                [new OwnerContentDefinitionAction(
                    BuildingDefinition.SchemaKind,
                    building.LocalId,
                    building.Version.ToString(),
                    building.DisplayName,
                    building.PayloadDigest,
                    """{"schema":"building/v1","width":1,"height":1,"capacity":2,"buildCosts":[{"resourceId":"wood","amount":2}],"tags":["camp"]}""")],
                []);

            using var proposed = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/propose",
                package,
                OwnerContentBinding.ProposePayload(package));
            var proposedReceipt = await proposed.Content.ReadFromJsonAsync<OwnerContentPackageReceipt>();
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            Assert.Equal("proposed", proposedReceipt!.Lifecycle);
            Assert.Matches("^sha256:[0-9a-f]{64}$", proposedReceipt.ManifestDigest);

            var packageId = new OwnerContentPackageIdAction(package.PackageId);
            using var validated = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/validate",
                packageId,
                OwnerContentBinding.PackageIdPayload("validate", packageId));
            var validatedReceipt = await validated.Content.ReadFromJsonAsync<OwnerContentPackageReceipt>();
            Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
            Assert.Equal("validated", validatedReceipt!.Lifecycle);
            Assert.NotNull(validatedReceipt.LockDigest);

            using var approved = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/approve",
                packageId,
                OwnerContentBinding.PackageIdPayload("approve", packageId));
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

            using var staged = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/stage",
                packageId,
                OwnerContentBinding.PackageIdPayload("stage", packageId));
            var stagedReceipt = await staged.Content.ReadFromJsonAsync<OwnerContentPackageReceipt>();
            Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
            Assert.Equal("staged", stagedReceipt!.Lifecycle);
            Assert.Equal(0, stagedReceipt.StagedTick);

            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            using var resume = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
            Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
            _ = await runtime.AdvanceOneTickAsync();
            host.Services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            Assert.Equal("active", runtime.Content.Packages.Single().Lifecycle.ToString().ToLowerInvariant());
            Assert.Equal(building.CanonicalId, Assert.Single(runtime.WorldContent.Buildings).CanonicalId);

            var occupiedMapPositions = runtime.ExportState().Map.CampObjects.Select(item => item.Position)
                .Concat(runtime.ExportState().Map.Resources.Select(item => item.Position))
                .ToHashSet();
            var worker = runtime.Inhabitants.First(item => !occupiedMapPositions.Contains(item.Position));
            var placementAction = new OwnerBuildingPlacementAction(
                "camp-kitchen-one",
                building.CanonicalId,
                worker.Position.X,
                worker.Position.Y);
            using var placed = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/buildings/place",
                placementAction,
                OwnerContentBinding.BuildingPlacementPayload(placementAction));
            var placementReceipt = await placed.Content.ReadFromJsonAsync<BuildingPlacementResult>();
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            Assert.True(placementReceipt!.Applied, placementReceipt.Failure);
            Assert.Single(runtime.WorldSimulation.Buildings);

            var rollbackLogger = new RecordingLogger<PrivateWorldRuntimeService>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(new RecordingLoggerProvider<PrivateWorldRuntimeService>(rollbackLogger));
            var rollback = new OwnerContentRollbackAction(package.PackageId, "sk-private-rollback-reason");
            var beforeRollback = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using var rolledBack = await SendSignedAsync(
                host,
                client,
                key,
                device.DeviceId,
                "/api/v1/owner/content/rollback",
                rollback,
                OwnerContentBinding.RollbackPayload(rollback));
            Assert.Equal(HttpStatusCode.Conflict, rolledBack.StatusCode);
            var rollbackFailure = await rolledBack.Content.ReadFromJsonAsync<OwnerControlFailure>();
            Assert.Equal("content_rejected", rollbackFailure!.Code);
            Assert.Equal(beforeRollback, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Single(runtime.WorldContent.Buildings);
            Assert.Single(runtime.WorldSimulation.Buildings);
            Assert.Contains(rollbackLogger.Messages, message => message.Contains("content_rollback", StringComparison.Ordinal) &&
                message.Contains("package=camp-recipes outcome=rejected", StringComparison.Ordinal));
            Assert.DoesNotContain(rollbackLogger.Messages, message => message.Contains(rollback.Reason, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
