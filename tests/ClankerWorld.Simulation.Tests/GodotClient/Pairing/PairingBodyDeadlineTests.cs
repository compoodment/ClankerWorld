using System.Net;
using System.Net.Http.Json;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.Simulation.Tests;

public sealed class PairingBodyDeadlineTests
{
    [Theory]
    [InlineData("start", false)]
    [InlineData("status", false)]
    [InlineData("activate", false)]
    [InlineData("start", true)]
    [InlineData("status", true)]
    [InlineData("activate", true)]
    public async Task StalledPairingBodyHonorsTimeoutAndCancellationAndAllowsRetry(string operation, bool callerCancels)
    {
        using var handler = new HeldBodyHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(500) };
        using var signer = new Signer();
        using var cancellation = new CancellationTokenSource();
        var client = new OwnerPairingClient(http);
        var origin = new Uri("http://127.0.0.1:5188");
        var first = operation switch
        {
            "start" => (Task)client.StartPairingAsync(origin, signer, cancellation.Token),
            "status" => client.GetPairingStatusAsync(origin, "pairing", cancellation.Token),
            "activate" => client.ActivatePairingAsync(origin, CreatePairing(), signer, cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        await handler.Body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels) cancellation.Cancel();
        try
        {
            // The 500 ms client timeout or the caller's cancellation must end the stalled read; the
            // generous outer limit only stops a hang, and leaves room for a busy parallel test run.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { cancellation.Cancel(); }
        Assert.True(handler.Body.Cancelled);
        var retry = await client.StartPairingAsync(origin, signer, CancellationToken.None);
        Assert.Equal("pairing", retry.PairingId);
    }

    private static OwnerPairingStart CreatePairing()
    {
        var authority = new OwnerAuthorityIdentity("server", "world");
        return new OwnerPairingStart(authority, "pairing", "device", "123456", "fingerprint",
            DateTimeOffset.UtcNow.AddMinutes(1),
            OwnerPairingProtocol.CreatePairingActivationCanonicalProof(authority, "pairing", "device", "fingerprint"));
    }

    private sealed class HeldBodyHandler : HttpMessageHandler
    {
        public HeldBody Body { get; } = new();
        private bool stalled;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content;
            if (!stalled)
            {
                stalled = true;
                content = new StreamContent(Body);
                content.Headers.ContentType = new("application/json");
            }
            else content = JsonContent.Create(CreatePairing());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class HeldBody : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        private bool began;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!began)
            {
                began = true;
                buffer.Span[0] = (byte)'{';
                return 1;
            }
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }

    private sealed class Signer : IOwnerDeviceSigner
    {
        public string PublicKeySpkiBase64 => "fixture-key";
        public string PublicKeyFingerprint => "fingerprint";
        public string SignCanonicalProof(string canonicalProof) => "fixture-signature";
        public void Dispose() { }
    }
}
