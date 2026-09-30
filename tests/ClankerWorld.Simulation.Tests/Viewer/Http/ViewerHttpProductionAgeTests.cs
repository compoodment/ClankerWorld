using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(SocietyAgeBand.Infant)]
    [InlineData(SocietyAgeBand.Child)]
    [InlineData(SocietyAgeBand.Adolescent)]
    [InlineData(SocietyAgeBand.Adult)]
    [InlineData(SocietyAgeBand.Elder)]
    public async Task SignedProductionRequestEnforcesWorkerAgeWithoutChangingRejectedWorlds(SocietyAgeBand age)
    {
        var prepared = await ProductionAgeTests.PrepareAsync(age);
        var directory = Directory.CreateTempSubdirectory("production-age-http-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.json");
            File.WriteAllBytes(path, PrivateWorldRuntimeCodec.Encode(prepared.State));
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var world = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            var savedBefore = File.ReadAllBytes(path);
            var action = new OwnerProductionStartAction(prepared.RecipeId, prepared.WorkstationId, prepared.WorkerId);
            using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/production/start", action, OwnerContentBinding.ProductionStartPayload(action));
            if (age is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var receipt = await response.Content.ReadFromJsonAsync<ProductionStartResult>();
                Assert.True(receipt!.Applied, receipt.Failure);
                Assert.NotNull(receipt.JobId);
                var saved = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path));
                Assert.Equal(before, savedBefore);
                await ProductionAgeTests.CompleteAfterReloadAsync(saved, receipt.JobId);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                var failure = await response.Content.ReadFromJsonAsync<OwnerControlFailure>();
                Assert.Equal("production_rejected", failure!.Code);
                Assert.Contains("too young", failure.Detail, StringComparison.Ordinal);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                Assert.Equal(savedBefore, File.ReadAllBytes(path));
                using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(savedBefore));
                Assert.False(reloaded.StartProduction(action.RecipeId, action.BuildingInstanceId, action.WorkerId).Applied);
                Assert.Equal(savedBefore, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
