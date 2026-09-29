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

public sealed partial class ViewerHttpTests : IDisposable
{
    // The public pairing budget is host-wide; independent scenarios own independent hosts.
    private readonly ViewerWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private const string ApprovedAssetDigest =
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private async Task<OwnerDevice> StartAndActivateAsync(HttpClient client, ECDsa key, string publicKey)
    {
        using var start = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest(publicKey));
        var pairing = await start.Content.ReadFromJsonAsync<OwnerPairingStart>();
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(pairing);

        var authority = factory.Services.GetRequiredService<OwnerAuthorityStore>();
        var stateFile = factory.Services.GetRequiredService<OwnerAuthorityStateFile>();
        Assert.True(authority.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode).IsSuccess);
        stateFile.Save(authority);

        var activation = new ActivateOwnerPairingHttpRequest(
            pairing.PairingId,
            pairing.ActivationCanonicalProof,
            Sign(key, pairing.ActivationCanonicalProof));
        using var activated = await client.PostAsJsonAsync("/api/v1/pairings/activate", activation);
        var device = await activated.Content.ReadFromJsonAsync<OwnerDevice>();
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.NotNull(device);
        return device;
    }

    private static async Task<OwnerDevice> StartAndActivateAsync(
        WebApplicationFactory<Program> host,
        HttpClient client,
        ECDsa key)
    {
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        using var start = await client.PostAsJsonAsync("/api/v1/pairings", new StartOwnerPairingHttpRequest(publicKey));
        var pairing = await start.Content.ReadFromJsonAsync<OwnerPairingStart>();
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(pairing);

        var authority = host.Services.GetRequiredService<OwnerAuthorityStore>();
        var stateFile = host.Services.GetRequiredService<OwnerAuthorityStateFile>();
        Assert.True(authority.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode).IsSuccess);
        stateFile.Save(authority);

        var activation = new ActivateOwnerPairingHttpRequest(
            pairing.PairingId,
            pairing.ActivationCanonicalProof,
            Sign(key, pairing.ActivationCanonicalProof));
        using var activated = await client.PostAsJsonAsync("/api/v1/pairings/activate", activation);
        var device = await activated.Content.ReadFromJsonAsync<OwnerDevice>();
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.NotNull(device);
        return device;
    }

    private async Task<HttpResponseMessage> SendSignedAsync<TAction>(
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var envelope = await CreateSignedRequestAsync(client, key, deviceId, path, action, canonicalPayload);
        return await client.PostAsJsonAsync(path, envelope);
    }

    private static async Task<HttpResponseMessage> SendSignedAsync<TAction>(
        WebApplicationFactory<Program> host,
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var envelope = await CreateSignedRequestAsync(host, client, key, deviceId, path, action, canonicalPayload);
        return await client.PostAsJsonAsync(path, envelope);
    }

    private async Task<OwnerSignedHttpRequest<TAction>> CreateSignedRequestAsync<TAction>(
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var authority = factory.Services.GetRequiredService<OwnerAuthorityStore>();
        var requestId = $"request-{Guid.NewGuid():N}";
        var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(authority.Identity, deviceId, requestId);
        var challengeRequest = new IssueOwnerChallengeHttpRequest(
            deviceId,
            requestId,
            issueProof,
            Sign(key, issueProof));
        using var issued = await client.PostAsJsonAsync("/api/v1/owner/challenges", challengeRequest);
        var challenge = await issued.Content.ReadFromJsonAsync<OwnerChallenge>();
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.NotNull(challenge);

        var binding = OwnerHttpBinding.Create("POST", path, requestId, canonicalPayload);
        var consumeProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
            authority.Identity,
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding);
        return new OwnerSignedHttpRequest<TAction>(
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding,
            consumeProof,
            Sign(key, consumeProof),
            requestId,
            action);
    }

    private static async Task<OwnerSignedHttpRequest<TAction>> CreateSignedRequestAsync<TAction>(
        WebApplicationFactory<Program> host,
        HttpClient client,
        ECDsa key,
        string deviceId,
        string path,
        TAction action,
        string canonicalPayload)
        where TAction : class
    {
        var authority = host.Services.GetRequiredService<OwnerAuthorityStore>();
        var requestId = $"request-{Guid.NewGuid():N}";
        var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(authority.Identity, deviceId, requestId);
        var challengeRequest = new IssueOwnerChallengeHttpRequest(
            deviceId,
            requestId,
            issueProof,
            Sign(key, issueProof));
        using var issued = await client.PostAsJsonAsync("/api/v1/owner/challenges", challengeRequest);
        var challenge = await issued.Content.ReadFromJsonAsync<OwnerChallenge>();
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.NotNull(challenge);

        var binding = OwnerHttpBinding.Create("POST", path, requestId, canonicalPayload);
        var consumeProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
            authority.Identity,
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding);
        return new OwnerSignedHttpRequest<TAction>(
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding,
            consumeProof,
            Sign(key, consumeProof),
            requestId,
            action);
    }

    private static string Sign(ECDsa key, string proof) => Convert.ToBase64String(key.SignData(
        Encoding.UTF8.GetBytes(proof),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}
