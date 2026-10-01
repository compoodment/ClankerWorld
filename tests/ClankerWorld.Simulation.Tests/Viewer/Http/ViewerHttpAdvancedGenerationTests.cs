using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    private readonly ITestOutputHelper output;

    public ViewerHttpTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task BalancedPreviewRequiresAcceptanceForMissesAndCreateUsesItsExactCandidate()
    {
        using var host = new ViewerWebApplicationFactory(null, privateWorld: true, legacyPrivateWorld: false);
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = await StartAndActivateAsync(host, client, key);
        var action = new OwnerWorldCreationAction("Balanced", "issue-409-miss-1", "Small", 50, true);
        var payload = OwnerHttpBinding.WorldCreationPayload(action);
        var stopwatch = Stopwatch.StartNew();
        using var previewed = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/preview", action, payload);
        Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
        var responseBytes = await previewed.Content.ReadAsByteArrayAsync();
        var preview = JsonSerializer.Deserialize<ViewerWorldPreview>(responseBytes, WebJsonOptions)!;
        stopwatch.Stop();
        output.WriteLine($"small-balanced-owner-preview elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F0} " +
            $"response_bytes={responseBytes.Length} candidates={preview.Candidates.Count}");
        Assert.Equal(3, preview.Candidates.Count);
        var coverage = Assert.IsType<GeographyCandidateReport>(preview.Coverage);
        Assert.True(coverage.ForestTargetApplicable);
        Assert.True(coverage.MountainTargetApplicable);
        Assert.False(coverage.MeetsTargets);
        Assert.All(preview.Candidates, candidate => Assert.False(candidate.MeetsTargets));
        Assert.Equal(coverage.Attempt, Assert.Single(preview.Candidates,
            candidate => candidate.Attempt == coverage.Attempt).Attempt);

        var create = action with
        {
            CandidateAttempt = coverage.Attempt,
            ExpectedManifestDigest = preview.ManifestDigest,
            ExpectedMapLayersDigest = preview.MapLayersDigest,
        };
        using var unaccepted = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/create", create, OwnerHttpBinding.WorldCreationPayload(create));
        Assert.Equal(HttpStatusCode.Conflict, unaccepted.StatusCode);
        Assert.Single(host.Services.GetRequiredService<WorldCatalogStore>().Capture().Worlds);

        var stale = create with { ExpectedMapLayersDigest = "sha256:stale-preview" };
        using var changedPreview = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/create", stale, OwnerHttpBinding.WorldCreationPayload(stale));
        Assert.Equal(HttpStatusCode.Conflict, changedPreview.StatusCode);

        var accepted = create with { AcceptUnmetTargets = true };
        using var created = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/create", accepted, OwnerHttpBinding.WorldCreationPayload(accepted));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var state = host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState();
        Assert.Equal(preview.ManifestDigest, state.Map.ManifestDigest);
        Assert.Equal(preview.MapLayersDigest, MapLayerManifestCodec.Digest(state.Map));
        Assert.Equal(coverage.Attempt, state.Map.GenerationAttempt);
        Assert.Equal(coverage.Attempt, state.Geography!.CandidateAttempt);
    }

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
        Assert.NotNull(preview.Coverage);
        Assert.Single(preview.Candidates); // Dominant climate is outside the trial target.
        var createAction = action with
        {
            CandidateAttempt = preview.Coverage!.Attempt,
            ExpectedManifestDigest = preview.ManifestDigest,
            ExpectedMapLayersDigest = preview.MapLayersDigest,
        };
        using var created = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/create", createAction, OwnerHttpBinding.WorldCreationPayload(createAction));
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
