using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using ViewerBuildingReassignmentAction = ClankerWorld.Viewer.Control.OwnerBuildingReassignmentAction;
using ViewerBuildingRemovalAction = ClankerWorld.Viewer.Control.OwnerBuildingRemovalAction;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task PairedOwnerCanReassignAndRemoveBuildingsThroughSignedSavedEndpoints()
    {
        var directory = Directory.CreateTempSubdirectory("owner-building-management-");
        try
        {
            var (state, tailorId) = TailorTestWorld.Create(SeededWorldObservationStore.SampleSeed, 0);
            File.WriteAllBytes(Path.Combine(directory.FullName, "runtime.json"), PrivateWorldRuntimeCodec.Encode(state));
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var tailor = runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == tailorId);

            const string reassignPath = "/api/v1/owner/buildings/reassign";
            var reassignment = new ViewerBuildingReassignmentAction(tailorId, tailor.TownId, tailor.HouseholdId,
                TargetTownId: null, TargetHouseholdId: "household:camp-beta");
            var beforeTampering = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var signed = await CreateSignedRequestAsync(host, client, key, device.DeviceId, reassignPath,
                reassignment, OwnerContentBinding.BuildingReassignmentPayload(reassignment));
            using (var tampered = await client.PostAsJsonAsync(reassignPath, signed with
            {
                Action = reassignment with { TargetHouseholdId = "household:camp-alpha" },
            }))
            {
                Assert.False(tampered.IsSuccessStatusCode);
            }
            Assert.Equal(beforeTampering, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

            using var reassignedResponse = await SendSignedAsync(host, client, key, device.DeviceId, reassignPath,
                reassignment, OwnerContentBinding.BuildingReassignmentPayload(reassignment));
            Assert.Equal(HttpStatusCode.OK, reassignedResponse.StatusCode);
            var reassignedResult = await reassignedResponse.Content.ReadFromJsonAsync<BuildingManagementResult>();
            Assert.NotNull(reassignedResult);
            Assert.True(reassignedResult.Applied, reassignedResult.Failure);
            var current = runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == tailorId);
            Assert.Equal("household:camp-beta", current.HouseholdId);
            var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
            Assert.Equal("household:camp-beta", PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(stateFile.Path))
                .WorldSimulation!.Buildings.Single(item => item.InstanceId == tailorId).HouseholdId);

            const string removePath = "/api/v1/owner/buildings/remove";
            var removal = new ViewerBuildingRemovalAction(tailorId, current.TownId, current.HouseholdId);
            using var removedResponse = await SendSignedAsync(host, client, key, device.DeviceId, removePath,
                removal, OwnerContentBinding.BuildingRemovalPayload(removal));
            Assert.Equal(HttpStatusCode.OK, removedResponse.StatusCode);
            var removedResult = await removedResponse.Content.ReadFromJsonAsync<BuildingManagementResult>();
            Assert.NotNull(removedResult);
            Assert.True(removedResult.Applied, removedResult.Failure);
            Assert.DoesNotContain(runtime.WorldSimulation.Buildings, item => item.InstanceId == tailorId);
            Assert.DoesNotContain(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(stateFile.Path)).WorldSimulation!.Buildings,
                item => item.InstanceId == tailorId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
