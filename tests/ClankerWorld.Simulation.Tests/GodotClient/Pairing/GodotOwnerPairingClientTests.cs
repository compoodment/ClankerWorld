using System.Net;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.Simulation.Tests;

public sealed class GodotOwnerPairingClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconnectBoundsStalledTransportAndHonorsOwnerCancellation(bool ownerCancels)
    {
        using var handler = new StalledHandler();
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var api = new OwnerWorldApi(client);
        using var signer = new StubSigner();
        using var cancellation = new CancellationTokenSource();
        var first = api.ReconnectAsync(new Uri("http://127.0.0.1:5188"), new("server", "world"),
            "device", 0, null, null, null, signer, cancellation.Token);
        var started = await Task.WhenAny(first, handler.Started.Task).WaitAsync(TimeSpan.FromSeconds(10));
        if (started == first) await first;
        if (ownerCancels) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(handler.WasCancelled);
        // A cancelled poll does not poison the shared client used by pause and later polls.
        using var response = await client.GetAsync("http://127.0.0.1:5188/next");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StalledHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/next") return new(HttpStatusCode.OK);
            Started.TrySetResult(true);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { WasCancelled = true; throw; }
            throw new InvalidOperationException("A stalled request must be cancelled.");
        }
    }

    [Fact]
    public async Task PairingRefusesPlaintextNonLoopbackTransportBeforeItCanSendAKey()
    {
        using var client = new HttpClient(new FailingHandler());
        var pairingClient = new OwnerPairingClient(client);
        using var signer = new StubSigner();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => pairingClient.StartPairingAsync(
            new Uri("http://example.test:5188"),
            signer,
            CancellationToken.None));

        Assert.Contains("requires HTTPS", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StubSigner : IOwnerDeviceSigner
    {
        public string PublicKeySpkiBase64 => "not-used-before-transport-validation";

        public string PublicKeyFingerprint => "not-used-before-transport-validation";

        public string SignCanonicalProof(string canonicalProof) => "test-signature";

        public void Dispose()
        {
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Transport must not be reached for an insecure remote URI.");
    }
}
