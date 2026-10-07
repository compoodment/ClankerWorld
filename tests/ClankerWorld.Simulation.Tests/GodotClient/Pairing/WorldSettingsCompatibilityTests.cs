using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using HostBinding = ClankerWorld.Viewer.Control.OwnerHttpBinding;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldSettingsCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WorldSettingsRequireTheHostToAdvertiseWorldBoundActions(bool jev, bool supported)
    {
        using var handler = new SettingsHandler(jev, supported);
        using var client = new HttpClient(handler);
        using var signer = new Signer();
        var api = new OwnerWorldApi(client);
        var origin = new Uri("http://127.0.0.1:5188");
        var authority = new OwnerAuthorityIdentity("server", "authority-world");
        Task<OwnerControlReceipt> Submit() => jev
            ? api.SetJevAssistanceAsync(origin, authority, "device", false, "selected-world-é", signer, CancellationToken.None)
            : api.SetLifePaceAsync(origin, authority, "device", 365, "selected-world-é", signer, CancellationToken.None);
        if (!supported)
        {
            await Assert.ThrowsAsync<OwnerActionCompatibilityException>(Submit);
            Assert.Equal(0, handler.SettingsRequests);
        }
        else
        {
            Assert.True((await Submit()).Changed);
            Assert.Equal(1, handler.SettingsRequests);
            Assert.Equal("selected-world-é", handler.Action.GetProperty("worldId").GetString());
            if (jev) Assert.False(handler.Action.GetProperty("enabled").GetBoolean());
            else Assert.Equal(365, handler.Action.GetProperty("rate").GetInt32());
        }
    }

    private sealed class SettingsHandler(bool jev, bool supported) : HttpMessageHandler
    {
        public int SettingsRequests { get; private set; }
        public JsonElement Action { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == OwnerPairingEndpoints.ChallengeIssue)
            {
                var domain = jev ? HostBinding.JevAssistancePayloadDomain : HostBinding.LifePacePayloadDomain;
                var legacy = jev ? "clankerworld.owner-jev-assistance.v1" : "clankerworld.owner-life-pace.v1";
                return Reply(new OwnerChallenge(new("server", "authority-world"), "device", "challenge", "nonce",
                    DateTimeOffset.UtcNow.AddMinutes(1), supported ? [domain] : [legacy]));
            }
            Assert.Equal(jev ? OwnerPairingEndpoints.OwnerJevAssistance : OwnerPairingEndpoints.OwnerLifePace,
                request.RequestUri.AbsolutePath);
            SettingsRequests++;
            var envelope = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            Action = envelope.GetProperty("action");
            return Reply(new OwnerControlReceipt("settings", true, true, 0, 0, 0));
        }

        private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class Signer : IOwnerDeviceSigner
    {
        public string PublicKeySpkiBase64 => "fixture-key";
        public string PublicKeyFingerprint => "fixture-fingerprint";
        public string SignCanonicalProof(string canonicalProof) => "fixture-signature";
        public void Dispose() { }
    }
}
