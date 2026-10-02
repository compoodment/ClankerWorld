using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildingManagementRejectsAnActionFromAnotherWorldWithoutChangingLiveOrSavedState(bool remove)
    {
        var directory = Directory.CreateTempSubdirectory("building-world-review-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.json");
            File.WriteAllBytes(path, PrivateWorldRuntimeCodec.Encode(BuildingWorldReviewState("building-action-world-a")));
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            client.BaseAddress = new Uri("http://127.0.0.1/");
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var worldA = runtime.Society.WorldId;
            var farmhouseA = runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
            var removalA = new OwnerBuildingRemovalAction(farmhouseA.InstanceId, farmhouseA.TownId,
                farmhouseA.HouseholdId, worldA);
            var reassignmentA = new OwnerBuildingReassignmentAction(farmhouseA.InstanceId, farmhouseA.TownId,
                farmhouseA.HouseholdId, null, "household:camp-beta", worldA);
            const string removalEndpoint = "/api/v1/owner/buildings/remove";
            const string reassignmentEndpoint = "/api/v1/owner/buildings/reassign";
            // Retain a signed, unused action, as if another paired device switches worlds before it arrives.
            var removalEnvelope = remove ? await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                removalEndpoint, removalA, OwnerContentBinding.BuildingRemovalPayload(removalA)) : null;
            var reassignmentEnvelope = remove ? null : await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                reassignmentEndpoint, reassignmentA, OwnerContentBinding.BuildingReassignmentPayload(reassignmentA));

            using var other = PrivateWorldRuntime.Restore(BuildingWorldReviewState("building-action-world-b"));
            other.Pause();
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            var entryB = catalog.Add("Other building world", other.ExportState());
            Assert.NotEqual(worldA, other.Society.WorldId);
            var switchToB = new OwnerManualSaveAction("select-world", entryB.Id);
            using var switched = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/select", switchToB, OwnerHttpBinding.ManualSavePayload(switchToB));
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            var worldB = runtime.Society.WorldId;
            Assert.Equal(other.Society.WorldId, worldB);
            var farmhouseB = runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == farmhouseA.InstanceId);
            Assert.Equal(farmhouseA.TownId, farmhouseB.TownId);
            Assert.Equal(farmhouseA.HouseholdId, farmhouseB.HouseholdId);
            var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var diskBefore = File.ReadAllBytes(path);

            // Changing the world in transit cannot change what the owner signed.
            using var tampered = remove
                ? await client.PostAsJsonAsync(removalEndpoint,
                    removalEnvelope! with { Action = removalA with { WorldId = worldB } })
                : await client.PostAsJsonAsync(reassignmentEndpoint,
                    reassignmentEnvelope! with { Action = reassignmentA with { WorldId = worldB } });
            Assert.False(tampered.IsSuccessStatusCode);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(diskBefore, File.ReadAllBytes(path));

            // Missing required world identity is refused before authorization or mutation.
            using var missing = remove
                ? await client.PostAsJsonAsync(removalEndpoint,
                    removalEnvelope! with { Action = removalA with { WorldId = null! } })
                : await client.PostAsJsonAsync(reassignmentEndpoint,
                    reassignmentEnvelope! with { Action = reassignmentA with { WorldId = null! } });
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(diskBefore, File.ReadAllBytes(path));

            // The intact action remains authentic, but its actual world has changed.
            using var stale = remove
                ? await client.PostAsJsonAsync(removalEndpoint, removalEnvelope)
                : await client.PostAsJsonAsync(reassignmentEndpoint, reassignmentEnvelope);
            var staleResult = await stale.Content.ReadFromJsonAsync<BuildingManagementResult>();
            Assert.NotNull(staleResult);
            Assert.False(staleResult.Applied);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(diskBefore, File.ReadAllBytes(path));

            // The same operation explicitly formed and signed for B is allowed and durable.
            using var fresh = remove
                ? await SendSignedAsync(host, client, key, device.DeviceId, removalEndpoint,
                    removalA with { WorldId = worldB },
                    OwnerContentBinding.BuildingRemovalPayload(removalA with { WorldId = worldB }))
                : await SendSignedAsync(host, client, key, device.DeviceId, reassignmentEndpoint,
                    reassignmentA with { WorldId = worldB },
                    OwnerContentBinding.BuildingReassignmentPayload(reassignmentA with { WorldId = worldB }));
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            var freshResult = await fresh.Content.ReadFromJsonAsync<BuildingManagementResult>();
            Assert.NotNull(freshResult);
            Assert.True(freshResult.Applied, freshResult.Failure);
            var saved = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()), PrivateWorldRuntimeCodec.Encode(saved));
            if (remove)
            {
                Assert.DoesNotContain(runtime.WorldSimulation.Buildings, item => item.InstanceId == farmhouseB.InstanceId);
                Assert.DoesNotContain(saved.WorldSimulation!.Buildings, item => item.InstanceId == farmhouseB.InstanceId);
            }
            else
            {
                Assert.Equal("household:camp-beta", runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == farmhouseB.InstanceId).HouseholdId);
                Assert.Equal("household:camp-beta", saved.WorldSimulation!.Buildings.Single(item => item.InstanceId == farmhouseB.InstanceId).HouseholdId);
            }
            runtime.Validate();
            using var restored = PrivateWorldRuntime.Restore(saved);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(saved), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static PrivateWorldRuntimeState BuildingWorldReviewState(string seed)
    {
        using var setup = PrivateWorldRuntime.Restore(
            GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions(seed, WorldSizePreset.Small)));
        setup.StageStarterContent();
        return setup.ExportState();
    }
}
