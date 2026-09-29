using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProviderResponseBodyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedBodiesAreRejectedBeforeUnboundedBuffering(bool declared)
    {
        using var stream = new NonSeekableStream(new byte[ProviderResponseBody.MaximumBytes + 100]);
        using var content = new StreamContent(stream);
        if (declared)
        {
            content.Headers.ContentLength = ProviderResponseBody.MaximumBytes + 100;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => ProviderResponseBody.ReadAsync(content));
        Assert.True(stream.BytesRead <= (declared ? 0 : ProviderResponseBody.MaximumBytes + 1));
    }

    [Fact]
    public async Task MisreportedLengthDoesNotBypassStreamingCap()
    {
        using var content = new StreamContent(new NonSeekableStream(new byte[ProviderResponseBody.MaximumBytes + 1]));
        content.Headers.ContentLength = 10;
        await Assert.ThrowsAsync<InvalidDataException>(() => ProviderResponseBody.ReadAsync(content));
    }

    [Fact]
    public async Task ExactLimitIsAcceptedAndCallerCancellationIsPreserved()
    {
        using var content = new ByteArrayContent(new byte[ProviderResponseBody.MaximumBytes]);
        Assert.Equal(ProviderResponseBody.MaximumBytes, (await ProviderResponseBody.ReadAsync(content)).Length);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProviderResponseBody.ReadAsync(content, cancelled.Token));
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }
}
