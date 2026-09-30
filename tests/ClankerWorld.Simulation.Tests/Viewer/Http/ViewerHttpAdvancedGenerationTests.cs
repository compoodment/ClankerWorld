using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task AdvancedPreviewAndCreationUseTheSameSignedSavedOptions()
    {
        using var host = new ViewerWebApplicationFactory(null, privateWorld: true, legacyPrivateWorld: false);
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = await StartAndActivateAsync(host, client, key);
        var action = new OwnerWorldCreationAction("Advanced", "advanced-http", "Small", 50, false,
            "Dominant", "Temperate", false, "Abundant", "High", "Low", "High");
        var payload = OwnerHttpBinding.WorldCreationPayload(action);
        Assert.Equal(payload, ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.WorldCreation(
            new ClankerWorld.GodotClient.UI.OwnerWorldCreationAction(action.Name, action.Seed, action.Size,
                action.WaterPercent, action.WrapEastWest, action.ClimateMode, action.SelectedClimate, action.LatitudeCooling,
                action.ResourceAbundance, action.ForestCover, action.MountainRelief, action.RiverAbundance)));
        var signed = await CreateSignedRequestAsync(host, client, key, device.DeviceId, "/api/v1/owner/worlds/preview", action, payload);
        using var tampered = await client.PostAsJsonAsync("/api/v1/owner/worlds/preview", signed with { Action = action with { ForestCover = "Low" } });
        Assert.False(tampered.IsSuccessStatusCode);
        foreach (var invalid in new[] { action with { ForestCover = "Unknown" }, action with { MountainRelief = "99" },
                     action with { RiverAbundance = "99" }, action with { WaterPercent = 19 }, action with { WaterPercent = 81 }, action with { Size = "Large" } })
        {
            using var refused = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/worlds/preview", invalid, OwnerHttpBinding.WorldCreationPayload(invalid));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        using var previewed = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/worlds/preview", action, payload);
        Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
        var preview = (await previewed.Content.ReadFromJsonAsync<ViewerWorldPreview>())!;
        using var created = await SendSignedAsync(host, client, key, device.DeviceId, "/api/v1/owner/worlds/create", action, payload);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var state = host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState();
        Assert.Equal(preview.ManifestDigest, state.Map.ManifestDigest);
        Assert.Equal(preview.MapLayersDigest, MapLayerManifestCodec.Digest(state.Map));
        Assert.Equal(GenerationAmount.High, state.Geography!.ForestCover);
        Assert.Equal(GenerationAmount.Low, state.Geography.MountainRelief);
        Assert.Equal(GenerationAmount.High, state.Geography.RiverAbundance);
        Assert.False(state.Geography.LatitudeCooling);
    }
}
