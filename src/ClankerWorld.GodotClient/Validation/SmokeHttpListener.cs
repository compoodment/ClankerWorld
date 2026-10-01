using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClankerWorld.GodotClient;

/// <summary>Bounded HTTP/1.1 loopback transport for client smoke fixtures only.</summary>
internal sealed class SmokeHttpListener : IDisposable
{
    private const int MaximumHeaders = 16 * 1024;
    private const int MaximumBody = 1024 * 1024;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<TcpClient, byte> connections = new();
    private TcpListener? listener;
    private volatile bool listening;
    private bool disposed;
    private Uri? origin;

    public List<string> Prefixes { get; } = [];
    public bool IsListening => listening;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (listener is not null || Prefixes.Count != 1)
            throw new InvalidOperationException("A smoke listener starts once with one loopback prefix.");
        origin = new Uri(Prefixes[0], UriKind.Absolute);
        if (origin.Scheme != Uri.UriSchemeHttp || origin.Host != "127.0.0.1" || origin.AbsolutePath != "/")
            throw new InvalidOperationException("Smoke HTTP is restricted to an IPv4 loopback root.");
        listener = new TcpListener(IPAddress.Loopback, origin.Port);
        listener.Start(16);
        listening = true;
    }

    public async Task<SmokeHttpContext> GetContextAsync()
    {
        ObjectDisposedException.ThrowIf(!listening, this);
        TcpClient client;
        try
        {
            client = await listener!.AcceptTcpClientAsync(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!listening)
        {
            throw new ObjectDisposedException(nameof(SmokeHttpListener));
        }
        catch (SocketException) when (!listening)
        {
            throw new ObjectDisposedException(nameof(SmokeHttpListener));
        }
        if (connections.Count >= 16)
        {
            client.Dispose();
            throw new InvalidDataException("The smoke connection bound was exceeded.");
        }
        connections.TryAdd(client, 0);
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            readDeadline.CancelAfter(TimeSpan.FromSeconds(10));
            var reader = new ByteReader(stream, readDeadline.Token);
            var requestLine = await reader.LineAsync(8192).ConfigureAwait(false);
            var request = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (request.Length != 3 || !request[1].StartsWith('/') || request[2] is not ("HTTP/1.1" or "HTTP/1.0"))
                throw new InvalidDataException("The smoke request must use an HTTP/1.x origin-form target.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var headerBytes = requestLine.Length + 2;
            while (true)
            {
                var line = await reader.LineAsync(8192).ConfigureAwait(false);
                headerBytes += line.Length + 2;
                if (headerBytes > MaximumHeaders) throw new InvalidDataException("Smoke headers are too large.");
                if (line.Length == 0) break;
                var separator = line.IndexOf(':');
                if (separator < 1 || !headers.TryAdd(line[..separator], line[(separator + 1)..].Trim()))
                    throw new InvalidDataException("The smoke request has an invalid or duplicate header.");
            }
            if (headers.TryGetValue("Expect", out var expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), readDeadline.Token).ConfigureAwait(false);
            byte[] body;
            if (headers.TryGetValue("Transfer-Encoding", out var transfer))
            {
                if (!transfer.Equals("chunked", StringComparison.OrdinalIgnoreCase) || headers.ContainsKey("Content-Length"))
                    throw new InvalidDataException("The smoke transfer framing is unsupported or ambiguous.");
                using var chunks = new MemoryStream();
                while (true)
                {
                    var sizeLine = await reader.LineAsync(1024).ConfigureAwait(false);
                    var sizeText = sizeLine.Split(';', 2)[0];
                    if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) ||
                        size < 0 || size > MaximumBody - chunks.Length)
                        throw new InvalidDataException("The smoke chunk/body bound was exceeded.");
                    if (size == 0)
                    {
                        while (true)
                        {
                            var trailer = await reader.LineAsync(8192).ConfigureAwait(false);
                            headerBytes += trailer.Length + 2;
                            if (headerBytes > MaximumHeaders) throw new InvalidDataException("Smoke trailers are too large.");
                            if (trailer.Length == 0) break;
                        }
                        break;
                    }
                    chunks.Write(await reader.ExactAsync(size).ConfigureAwait(false));
                    if ((await reader.LineAsync(2).ConfigureAwait(false)).Length != 0)
                        throw new InvalidDataException("The smoke chunk has no terminating CRLF.");
                }
                body = chunks.ToArray();
            }
            else
            {
                var length = 0;
                if (headers.TryGetValue("Content-Length", out var lengthText) &&
                    (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length) ||
                     length < 0 || length > MaximumBody))
                    throw new InvalidDataException("The smoke body length is invalid.");
                body = await reader.ExactAsync(length).ConfigureAwait(false);
            }
            var input = new MemoryStream(body, writable: false);
            return new SmokeHttpContext(new SmokeHttpRequest(new Uri(origin!, request[1]), input),
                new SmokeHttpResponse(client, () =>
                {
                    input.Dispose();
                    connections.TryRemove(client, out _);
                }));
        }
        catch
        {
            connections.TryRemove(client, out _);
            client.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        if (!listening) return;
        listening = false;
        stopping.Cancel();
        listener!.Stop();
        foreach (var connection in connections.Keys) connection.Dispose();
        connections.Clear();
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        stopping.Dispose();
        disposed = true;
    }

    private sealed class ByteReader(NetworkStream stream, CancellationToken cancellation)
    {
        private readonly byte[] buffer = new byte[8192];
        private int next;
        private int end;

        private async ValueTask<byte> ByteAsync()
        {
            if (next == end)
            {
                end = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                next = 0;
                if (end == 0) throw new EndOfStreamException("The smoke request closed before its HTTP frame completed.");
            }
            return buffer[next++];
        }

        public async Task<string> LineAsync(int maximum)
        {
            var line = new List<byte>();
            while (true)
            {
                var value = await ByteAsync().ConfigureAwait(false);
                if (value == (byte)'\r')
                {
                    if (await ByteAsync().ConfigureAwait(false) != (byte)'\n')
                        throw new InvalidDataException("Smoke HTTP lines require CRLF.");
                    return Encoding.ASCII.GetString(line.ToArray());
                }
                if (value == (byte)'\n' || line.Count >= maximum)
                    throw new InvalidDataException("The smoke HTTP line is invalid or too large.");
                line.Add(value);
            }
        }

        public async Task<byte[]> ExactAsync(int count)
        {
            var result = new byte[count];
            for (var index = 0; index < count; index++)
                result[index] = await ByteAsync().ConfigureAwait(false);
            return result;
        }
    }
}

internal sealed record SmokeHttpRequest(Uri Url, Stream InputStream);
internal sealed record SmokeHttpContext(SmokeHttpRequest Request, SmokeHttpResponse Response);

internal sealed class SmokeHttpResponse(TcpClient client, Action completed) : IDisposable
{
    private readonly MemoryStream body = new();
    private int closed;
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public Stream OutputStream => body;

    public void Dispose() => Close();

    public void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        try
        {
            var bytes = body.ToArray();
            var reason = StatusCode switch { 200 => "OK", 409 => "Conflict", 503 => "Service Unavailable", _ => "Fixture Response" };
            var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {StatusCode} {reason}\r\nContent-Type: {ContentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            using var writeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();
            stream.WriteAsync(headers, writeDeadline.Token).AsTask().GetAwaiter().GetResult();
            stream.WriteAsync(bytes, writeDeadline.Token).AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            body.Dispose();
            client.Dispose();
            completed();
        }
    }
}
