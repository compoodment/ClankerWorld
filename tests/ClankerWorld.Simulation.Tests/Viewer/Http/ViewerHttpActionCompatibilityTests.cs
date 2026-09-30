using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Client = ClankerWorld.GodotClient.UI;
using OwnerAuthorityIdentity = ClankerWorld.GodotClient.Pairing.OwnerAuthorityIdentity;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("clankerworld.owner-world-creation.v1")]
    [InlineData("clankerworld.owner-world-creation.v3")]
    public async Task OlderActionHostAllowsReconnectButClientExplainsNewWorldUpdate(string? advertisedDomain)
    {
        using var baseHost = new ViewerWebApplicationFactory(null, privateWorld: true, legacyPrivateWorld: false);
        var legacy = new LegacyWorldActionFilter(advertisedDomain);
        using var host = baseHost.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(legacy)));
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var signer = new ActionCompatibilitySigner(key);
        var device = await StartAndActivateAsync(host, client, key);
        var identity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
        var authority = new OwnerAuthorityIdentity(identity.ServerAuthorityId, identity.WorldId);
        var api = new Client.OwnerWorldApi(client);
        var uri = client.BaseAddress!;
        // Literal loopback is the production client's explicit test/development exception.
        uri = new UriBuilder(uri) { Host = "127.0.0.1" }.Uri;
        var before = PrivateWorldRuntimeCodec.Encode(host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState());
        var reconnect = await api.ReconnectAsync(uri, authority, device.DeviceId, 0, null, null, null, signer, default);
        Assert.NotNull(reconnect.Baseline.Snapshot);

        var action = new OwnerWorldCreationAction("Disposable", "compatibility", "Small", 50, false);
        // Reproduce the old host's actual strict v1 verification with a new v2 proof.
        using var failedAction = await SendSignedAsync(host, client, key, device.DeviceId,
            OwnerPairingEndpoints.OwnerWorldPreview, action, OwnerHttpBinding.WorldCreationPayload(action));
        Assert.Equal(HttpStatusCode.Unauthorized, failedAction.StatusCode);
        Assert.Equal(1, legacy.WorldActionRequests);

        var clientAction = new Client.OwnerWorldCreationAction(action.Name, action.Seed, action.Size, 50, false);
        var previewFailure = await Assert.ThrowsAsync<OwnerActionCompatibilityException>(() =>
            api.PreviewWorldAsync(uri, authority, device.DeviceId, clientAction, signer, default));
        await Assert.ThrowsAsync<OwnerActionCompatibilityException>(() =>
            api.CreateWorldAsync(uri, authority, device.DeviceId, clientAction, signer, default));
        var text = Client.GameUiText.FriendlyFailure(previewFailure);
        Assert.Contains("matching updates", text, StringComparison.Ordinal);
        Assert.Contains("pairing can stay", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not allowed", text, StringComparison.Ordinal);
        Assert.Equal(1, legacy.WorldActionRequests); // Neither client action was posted or downgraded.
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState()));
        Assert.NotNull(await api.ReconnectAsync(uri, authority, device.DeviceId, 0, null, null, null, signer, default));
    }

    [Fact]
    public async Task MatchingClientPreviewsAndCreatesWithAdvancedOptionsAndStrictProofs()
    {
        using var host = new ViewerWebApplicationFactory(null, privateWorld: true, legacyPrivateWorld: false);
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var signer = new ActionCompatibilitySigner(key);
        var device = await StartAndActivateAsync(host, client, key);
        var identity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
        var authority = new OwnerAuthorityIdentity(identity.ServerAuthorityId, identity.WorldId);
        var api = new Client.OwnerWorldApi(client);
        var uri = new UriBuilder(client.BaseAddress!) { Host = "127.0.0.1" }.Uri;
        var action = new Client.OwnerWorldCreationAction("Disposable", "compatible-advanced", "Small", 50, false,
            "Dominant", "Temperate", false, "Abundant", "High", "Low", "High");
        var preview = await api.PreviewWorldAsync(uri, authority, device.DeviceId, action, signer, default);
        var created = await api.CreateWorldAsync(uri, authority, device.DeviceId, action, signer, default);
        var state = host.Services.GetRequiredService<PrivateWorldRuntime>().ExportState();
        Assert.Equal(created.WorldId, state.Society.Society.WorldId);
        Assert.Equal(preview.ManifestDigest, state.Map.ManifestDigest);
        Assert.Equal(preview.MapLayersDigest, MapLayerManifestCodec.Digest(state.Map));
        Assert.Equal(GenerationAmount.High, state.Geography!.ForestCover);
        Assert.Equal(GenerationAmount.Low, state.Geography.MountainRelief);
        Assert.Equal(GenerationAmount.High, state.Geography.RiverAbundance);

        var serverAction = new OwnerWorldCreationAction(action.Name, action.Seed, action.Size, 50, false);
        var downgrade = LegacyWorldActionFilter.V1Payload(serverAction);
        using var refused = await SendSignedAsync(host, client, key, device.DeviceId,
            OwnerPairingEndpoints.OwnerWorldPreview, serverAction, downgrade);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(OwnerHttpBinding.WorldCreationPayloadDomain, Client.OwnerWorldActionPayload.WorldCreationPayloadDomain);
    }

    [Fact]
    public async Task RevokedDeviceStillGetsAnAccessFailureBeforeCompatibilityIsConsidered()
    {
        using var host = new ViewerWebApplicationFactory(null, privateWorld: true, legacyPrivateWorld: false);
        using var client = host.CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var signer = new ActionCompatibilitySigner(key);
        var device = await StartAndActivateAsync(host, client, key);
        var authorityStore = host.Services.GetRequiredService<OwnerAuthorityStore>();
        Assert.True(authorityStore.RevokeDeviceLocally(device.DeviceId).IsSuccess);
        var identity = authorityStore.Identity;
        var authority = new OwnerAuthorityIdentity(identity.ServerAuthorityId, identity.WorldId);
        var api = new Client.OwnerWorldApi(client);
        var uri = new UriBuilder(client.BaseAddress!) { Host = "127.0.0.1" }.Uri;
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => api.PreviewWorldAsync(uri,
            authority, device.DeviceId, new("Disposable", "seed", "Small", 50, false), signer, default));
        Assert.Equal(HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Contains("not allowed", Client.GameUiText.FriendlyFailure(failure), StringComparison.Ordinal);
    }

    // Emulate the pre-Advanced host at its signed HTTP boundary. Challenge issuance
    // and action verification use the real authority store/authorizer; only the
    // historical advertised metadata and exact v1 payload differ from the host.
    private sealed class LegacyWorldActionFilter(string? advertisedDomain) : IStartupFilter
    {
        public int WorldActionRequests { get; private set; }

        public static string V1Payload(OwnerWorldCreationAction action) => string.Join('\n',
            OwnerHttpBinding.WorldCreationPayload(action).Split('\n').Take(10))
            .Replace("clankerworld.owner-world-creation.v2", "clankerworld.owner-world-creation.v1", StringComparison.Ordinal);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path == OwnerPairingEndpoints.ChallengeIssue)
                {
                    var request = (await context.Request.ReadFromJsonAsync<IssueOwnerChallengeHttpRequest>())!;
                    var authority = context.RequestServices.GetRequiredService<OwnerAuthorityStore>();
                    var result = authority.IssueChallenge(new(request.DeviceId, request.RequestId,
                        request.CanonicalProof, request.SignatureBase64));
                    context.RequestServices.GetRequiredService<OwnerAuthorityStateFile>().Save(authority);
                    await (result.IsSuccess ? Results.Ok(result.Value! with
                    {
                        SupportedActionPayloads = advertisedDomain is null ? null : [advertisedDomain],
                    }) : OwnerFailures.ToHttpResult(result.Failure)).ExecuteAsync(context);
                    return;
                }
                if (context.Request.Path == OwnerPairingEndpoints.OwnerWorldPreview ||
                    context.Request.Path == OwnerPairingEndpoints.OwnerWorldCreate)
                {
                    WorldActionRequests++;
                    var request = (await context.Request.ReadFromJsonAsync<OwnerSignedHttpRequest<OwnerWorldCreationAction>>())!;
                    var result = context.RequestServices.GetRequiredService<OwnerRequestAuthorizer>()
                        .Authorize(request, "POST", context.Request.Path, V1Payload(request.Action));
                    await (result.IsSuccess ? Results.Ok(new { accepted = true }) :
                        OwnerFailures.ToHttpResult(result.Failure)).ExecuteAsync(context);
                    return;
                }
                await continuation(context);
            });
            next(app);
        };
    }

    private sealed class ActionCompatibilitySigner(ECDsa key) : IOwnerDeviceSigner
    {
        public string PublicKeySpkiBase64 => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        public string PublicKeyFingerprint => OwnerPairingProtocol.CreatePublicKeyFingerprint(key.ExportSubjectPublicKeyInfo());
        public string SignCanonicalProof(string proof) => Sign(key, proof);
        public void Dispose() { } // The surrounding test owns the key.
    }
}
