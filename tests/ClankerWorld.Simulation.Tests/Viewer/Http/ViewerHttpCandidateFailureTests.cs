using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using ClankerWorld.GodotClient.Pairing;
using Client = ClankerWorld.GodotClient.UI;
using OwnerAuthorityIdentity = ClankerWorld.GodotClient.Pairing.OwnerAuthorityIdentity;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task PreviewAndCreateKeepThePlayableMapAndShowItsUnavailableAttempt()
    {
        using var host = new ViewerWebApplicationFactory(null, privateWorld: true, legacyPrivateWorld: false);
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = await StartAndActivateAsync(host, client, key);
        var oldWorld = host.Services.GetRequiredService<PrivateWorldRuntime>();
        var oldBytes = PrivateWorldRuntimeCodec.Encode(oldWorld.ExportState());
        var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
        var oldCatalog = JsonSerializer.SerializeToUtf8Bytes(catalog.Capture(), WebJsonOptions);
        var authorityIdentity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
        var authority = new OwnerAuthorityIdentity(authorityIdentity.ServerAuthorityId, authorityIdentity.WorldId);
        using var signer = new ActionCompatibilitySigner(key);
        var api = new Client.OwnerWorldApi(client);
        var uri = new UriBuilder(client.BaseAddress!) { Host = "127.0.0.1" }.Uri;
        var unavailable = new OwnerWorldCreationAction("No clearing", "audit-town-3", "Small", 80, true,
            LatitudeCooling: false, ForestCover: "High", MountainRelief: "High");
        var unavailableCreate = unavailable with
        {
            CandidateAttempt = 0,
            ExpectedManifestDigest = "valid-shaped-manifest",
            ExpectedMapLayersDigest = "valid-shaped-layers",
        };
        foreach (var (path, request) in new[]
        {
            ("/api/v1/owner/worlds/preview", unavailable),
            ("/api/v1/owner/worlds/create", unavailableCreate),
        })
        {
            using var refusedGeneration = await SendSignedAsync(host, client, key, device.DeviceId,
                path, request, OwnerHttpBinding.WorldCreationPayload(request));
            Assert.Equal(HttpStatusCode.Conflict, refusedGeneration.StatusCode);
            var failureBody = JsonSerializer.Deserialize<OwnerControlFailure>(
                await refusedGeneration.Content.ReadAsByteArrayAsync(), WebJsonOptions)!;
            Assert.Equal("no_playable_candidate", failureBody.Code);
            Assert.Equal("There is no room for a first Town with these settings.", failureBody.Detail);
        }
        var unavailableClientAction = new Client.OwnerWorldCreationAction("No clearing", "audit-town-3", "Small", 80, true,
            LatitudeCooling: false, ForestCover: "High", MountainRelief: "High");
        var previewFailure = await Assert.ThrowsAsync<OwnerWorldGenerationException>(() =>
            api.PreviewWorldAsync(uri, authority, device.DeviceId, unavailableClientAction, signer, default));
        var createFailure = await Assert.ThrowsAsync<OwnerWorldGenerationException>(() =>
            api.CreateWorldAsync(uri, authority, device.DeviceId, unavailableClientAction with
            {
                CandidateAttempt = 0,
                ExpectedManifestDigest = "valid-shaped-manifest",
                ExpectedMapLayersDigest = "valid-shaped-layers",
            }, signer, default));
        Assert.Equal(HttpStatusCode.Conflict, previewFailure.StatusCode);
        Assert.Equal(Client.GameUiText.FriendlyFailure(previewFailure), Client.GameUiText.FriendlyFailure(createFailure));
        Assert.Contains("Choose another seed", Client.GameUiText.FriendlyFailure(previewFailure), StringComparison.Ordinal);
        Assert.DoesNotContain("base-camp", Client.GameUiText.FriendlyFailure(previewFailure), StringComparison.Ordinal);
        Assert.Equal(oldCatalog, JsonSerializer.SerializeToUtf8Bytes(catalog.Capture(), WebJsonOptions));
        Assert.Equal(oldBytes, PrivateWorldRuntimeCodec.Encode(oldWorld.ExportState()));
        var action = new OwnerWorldCreationAction("Playable survivors", "audit-town-2", "Small", 80, true,
            LatitudeCooling: false, MountainRelief: "High");

        using var previewed = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/preview", action, OwnerHttpBinding.WorldCreationPayload(action));
        Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
        var response = await previewed.Content.ReadAsByteArrayAsync();
        var preview = JsonSerializer.Deserialize<ViewerWorldPreview>(response, WebJsonOptions)!;
        Assert.Collection(preview.Candidates, candidate => Assert.Equal(0, candidate.Attempt),
            candidate => Assert.Equal(1, candidate.Attempt));
        var failure = Assert.Single(preview.FailedCandidates);
        Assert.Equal(2, failure.Attempt);
        Assert.Equal("no-clearing", failure.Reason);
        var selected = Assert.IsType<GeographyCandidateReport>(preview.Coverage);
        Assert.Equal(0, selected.Attempt);
        Assert.True(selected.MeetsTargets);
        Assert.Equal(oldBytes, PrivateWorldRuntimeCodec.Encode(oldWorld.ExportState()));

        // The real owner payload reaches the independent client DTO; older
        // previews without failure metadata remain readable with an empty list.
        var ownerPreview = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldPreview>(response,
            WebJsonOptions)!;
        Assert.Equal(2, Assert.Single(ownerPreview.FailedCandidates).Attempt);
        Assert.Equal(preview.ManifestDigest, ownerPreview.ManifestDigest);
        var legacyPayload = JsonNode.Parse(response)!.AsObject();
        Assert.True(legacyPayload.Remove("failedCandidates"));
        var olderPreview = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldPreview>(
            legacyPayload.ToJsonString(), WebJsonOptions)!;
        Assert.Empty(olderPreview.FailedCandidates);
        Assert.Equal(ownerPreview.Coverage, olderPreview.Coverage);
        Assert.Equal(ownerPreview.ManifestDigest, olderPreview.ManifestDigest);

        var create = action with
        {
            CandidateAttempt = selected.Attempt,
            ExpectedManifestDigest = preview.ManifestDigest,
            ExpectedMapLayersDigest = preview.MapLayersDigest,
        };
        var wrongAttempt = create with { CandidateAttempt = failure.Attempt };
        using var refused = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/create", wrongAttempt, OwnerHttpBinding.WorldCreationPayload(wrongAttempt));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var wrongClientAttempt = new Client.OwnerWorldCreationAction(action.Name, action.Seed, action.Size, 80, true,
            LatitudeCooling: false, MountainRelief: "High", CandidateAttempt: failure.Attempt,
            ExpectedManifestDigest: preview.ManifestDigest, ExpectedMapLayersDigest: preview.MapLayersDigest);
        var identityFailure = await Assert.ThrowsAsync<HttpRequestException>(() => api.CreateWorldAsync(uri,
            authority, device.DeviceId, wrongClientAttempt, signer, default));
        Assert.Equal("the server's state changed. Try again", Client.GameUiText.FriendlyFailure(identityFailure));
        Assert.Equal(oldCatalog, JsonSerializer.SerializeToUtf8Bytes(catalog.Capture(), WebJsonOptions));
        Assert.Equal(oldBytes, PrivateWorldRuntimeCodec.Encode(oldWorld.ExportState()));

        using var created = await SendSignedAsync(host, client, key, device.DeviceId,
            "/api/v1/owner/worlds/create", create, OwnerHttpBinding.WorldCreationPayload(create));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var state = host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState();
        Assert.Equal(preview.ManifestDigest, state.Map.ManifestDigest);
        Assert.Equal(preview.MapLayersDigest, MapLayerManifestCodec.Digest(state.Map));
        Assert.Equal(0, state.Map.GenerationAttempt);
        Assert.Equal(0, state.Geography!.CandidateAttempt);
        Assert.False(state.Geography.LatitudeCooling);
        Assert.Equal(ClankerWorld.Simulation.World.GenerationAmount.High, state.Geography.MountainRelief);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
