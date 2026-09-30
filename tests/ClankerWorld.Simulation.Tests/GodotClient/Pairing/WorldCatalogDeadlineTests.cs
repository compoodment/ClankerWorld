using System.Net;
using System.Net.Http.Json;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldCatalogDeadlineTests
{
    [Fact]
    public async Task FirstCatalogReplyBeyondOrdinaryActionDeadlinePopulatesVisibleRequest()
    {
        using var handler = new CatalogHandler(TimeSpan.FromSeconds(16));
        using var client = new HttpClient(handler);
        using var signer = new Signer();
        using var request = new WorldListRequest();
        var api = new OwnerWorldApi(client);
        var loading = request.RefreshAsync(token => api.ListWorldsAsync(
            new("http://127.0.0.1:5188"), new("server", "world"), "device", signer, token));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(request.IsLoading);
        await loading.WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Null(request.Failure);
        Assert.False(request.IsLoading);
        Assert.Equal("active", request.Catalog!.ActiveId);
        Assert.Equal("incompatible", Assert.Single(request.Catalog.Worlds).Compatibility);
        Assert.Equal("Cannot restore this checkpoint.", request.Catalog.Worlds[0].CompatibilityReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogStillHonorsShorterHttpTimeoutAndScreenCancellation(bool screenCancels)
    {
        using var handler = new CatalogHandler(Timeout.InfiniteTimeSpan);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(500) };
        using var signer = new Signer();
        using var request = new WorldListRequest();
        var api = new OwnerWorldApi(client);
        var loading = request.RefreshAsync(token => api.ListWorldsAsync(
            new("http://127.0.0.1:5188"), new("server", "world"), "device", signer, token));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (screenCancels) request.Cancel();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.Cancelled);
        Assert.False(request.IsLoading);
        Assert.Null(request.Catalog);
        if (screenCancels) Assert.Null(request.Failure);
        else Assert.IsAssignableFrom<OperationCanceledException>(request.Failure);
        var receipt = await api.SetPausedAsync(new("http://127.0.0.1:5188"),
            new("server", "world"), "device", true, signer, CancellationToken.None);
        Assert.True(receipt.IsPaused);
    }

    private sealed class CatalogHandler(TimeSpan delay) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == OwnerPairingEndpoints.ChallengeIssue)
                return Reply(new OwnerChallenge(new("server", "world"), "device", "challenge", "nonce", DateTimeOffset.UtcNow.AddMinutes(1)));
            if (request.RequestUri.AbsolutePath != OwnerPairingEndpoints.OwnerWorldList)
                return Reply(new OwnerControlReceipt("pause", true, true, 1, 1, 1));
            Started.TrySetResult();
            try { await Task.Delay(delay, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return Reply(new WorldCatalogSnapshot("active",
                [new CatalogWorld("blocked", "Blocked world", "world", "seed", DateTimeOffset.UnixEpoch, [], null,
                    "incompatible", "Cannot restore this checkpoint.")]));
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
