using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.GodotClient.Networking;

namespace ClankerWorld.Simulation.Tests;

/// <summary>The game starting, pairing with and stopping its bundled host (#1564).</summary>
public sealed class LocalHostCompanionTests
{
    [Fact]
    public void BundledHostIsFoundBesideTheGameAndKeepsDataInTheUserFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"clankerworld-layout-{Guid.NewGuid():N}");
        try
        {
            var game = Path.Combine(root, "ClankerWorld-0.1.0-alpha.1", "ClankerWorld.exe");
            var localAppData = Path.Combine(root, "LocalAppData");
            Assert.Null(LocalHostLayout.FindBundled(game, localAppData));

            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(game)!, "host"));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(game)!, "host", LocalHostLayout.WindowsHostFileName), "");
            var layout = LocalHostLayout.FindBundled(game, localAppData);

            Assert.NotNull(layout);
            Assert.Equal(Path.Combine(localAppData, "ClankerWorld"), layout.DataDirectory);
            var arguments = layout.HostArguments(5188, 5189);
            Assert.Contains("--ClankerWorld:Pairing:LocalApprovalPort=5189", arguments);
            // Every saved path is in the player's folder, never in the versioned game folder.
            foreach (var path in arguments.Where(item => item.Contains("Path=", StringComparison.Ordinal)))
                Assert.StartsWith(layout.DataDirectory, path[(path.IndexOf('=', StringComparison.Ordinal) + 1)..],
                    StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GameStartsPairsWithAndStopsItsOwnHost()
    {
        var data = Path.Combine(Path.GetTempPath(), $"clankerworld-companion-{Guid.NewGuid():N}");
        var (port, approvalPort) = (FreePort(), FreePort());
        var viewer = typeof(Program).Assembly;
        var (version, revision) = BuildOf(viewer);
        var layout = new LocalHostLayout(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [viewer.Location], Path.GetDirectoryName(viewer.Location)!, data);
        using var http = new HttpClient();
        try
        {
            var game = new LocalHostCompanion(layout, http, port, approvalPort);
            Assert.Equal(LocalHostOutcome.Started, (await game.StartAsync(version, revision, CancellationToken.None)).Outcome);
            Assert.True(File.Exists(Path.Combine(data, "saves", "private-world.json")));
            Assert.DoesNotContain(File.ReadAllText(layout.SecretPath), await ReadSharedAsync(layout.LogPath));

            // A relaunch of the same build reuses the running host; a different build refuses it.
            var relaunch = new LocalHostCompanion(layout, http, port, approvalPort);
            Assert.Equal(LocalHostOutcome.Reused, (await relaunch.StartAsync(version, revision, CancellationToken.None)).Outcome);
            var otherBuild = new LocalHostCompanion(layout, http, port, approvalPort);
            Assert.Equal(LocalHostOutcome.PortInUse, (await otherBuild.StartAsync("9.9.9", revision, CancellationToken.None)).Outcome);

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var start = await http.PostAsJsonAsync(new Uri(game.Origin, "api/v1/pairings"),
                new { publicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) });
            using var pending = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
            var pairingId = pending.RootElement.GetProperty("pairingId").GetString()!;
            var code = pending.RootElement.GetProperty("pairingCode").GetString()!;
            Assert.False(await otherBuild.ApprovePairingAsync(pairingId, code, CancellationToken.None));
            Assert.True(await game.ApprovePairingAsync(pairingId, code, CancellationToken.None));
            using var status = JsonDocument.Parse(await http.GetStringAsync(new Uri(game.Origin, $"api/v1/pairings/{pairingId}")));
            Assert.Equal((int)ClankerWorld.GodotClient.Pairing.OwnerPairingState.Approved,
                status.RootElement.GetProperty("state").GetInt32());

            await game.StopAsync(CancellationToken.None);
            Assert.False(game.StartedHost);
            Assert.Contains("host_shutdown outcome=saved", await ReadSharedAsync(layout.LogPath), StringComparison.Ordinal);
            await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri(game.Origin, "api/v1/handshake")));
        }
        finally
        {
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public async Task AHostThatCannotStartIsReportedNotHidden()
    {
        var data = Path.Combine(Path.GetTempPath(), $"clankerworld-companion-missing-{Guid.NewGuid():N}");
        try
        {
            var layout = new LocalHostLayout(Path.Combine(data, "missing-host"), [], data, data);
            Directory.CreateDirectory(Path.GetDirectoryName(layout.LogPath)!);
            File.WriteAllText(layout.LogPath, "the launch that crashed");
            using var http = new HttpClient();
            var game = new LocalHostCompanion(layout, http, FreePort(), FreePort());
            var start = await game.StartAsync("0", "0", CancellationToken.None);
            Assert.Equal(LocalHostOutcome.CouldNotStart, start.Outcome);
            Assert.Contains("try again", start.PlayerMessage, StringComparison.OrdinalIgnoreCase);
            // A new launch keeps the last one's log, so a crash can still be reported.
            Assert.Equal("the launch that crashed", File.ReadAllText(layout.PreviousLogPath));
        }
        finally
        {
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task RefusedShutdownAndCallerCancellationPreserveTheChildForRetry(HttpStatusCode refusal)
    {
        var data = Path.Combine(Path.GetTempPath(), $"clankerworld-companion-refused-{Guid.NewGuid():N}");
        var viewer = typeof(Program).Assembly;
        var (version, revision) = BuildOf(viewer);
        var layout = new LocalHostLayout(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [viewer.Location], Path.GetDirectoryName(viewer.Location)!, data);
        using var handler = new ShutdownRefusalHandler();
        using var http = new HttpClient(handler);
        var game = new LocalHostCompanion(layout, http, FreePort(), FreePort());
        try
        {
            Assert.True((await game.StartAsync(version, revision, CancellationToken.None)).IsReady);
            handler.Refusal = refusal;
            Assert.False(await game.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(game.StartedHost);
            Assert.True((await http.GetAsync(new Uri(game.Origin, "api/v1/handshake"))).IsSuccessStatusCode);
            handler.Refusal = null;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => game.StopAsync(cancellation.Token));
            Assert.True(game.StartedHost);
            Assert.True((await http.GetAsync(new Uri(game.Origin, "api/v1/handshake"))).IsSuccessStatusCode);
            Assert.True(await game.StopAsync(CancellationToken.None));
            Assert.False(game.StartedHost);
        }
        finally
        {
            handler.Refusal = null;
            await game.StopAsync(CancellationToken.None);
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public async Task SameBuildRequiresTheRunningCompanionToRecognizeItsSavedSecret()
    {
        var data = Path.Combine(Path.GetTempPath(), $"clankerworld-companion-proof-{Guid.NewGuid():N}");
        var viewer = typeof(Program).Assembly;
        var (version, revision) = BuildOf(viewer);
        var layout = new LocalHostLayout(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [viewer.Location], Path.GetDirectoryName(viewer.Location)!, data);
        using var http = new HttpClient();
        var (port, approval) = (FreePort(), FreePort());
        var game = new LocalHostCompanion(layout, http, port, approval);
        string? original = null;
        try
        {
            Assert.True((await game.StartAsync(version, revision, CancellationToken.None)).IsReady);
            original = File.ReadAllText(layout.SecretPath);
            File.WriteAllText(layout.SecretPath, new string('x', 32));
            var relaunch = new LocalHostCompanion(layout, http, port, approval);
            Assert.Equal(LocalHostOutcome.PortInUse, (await relaunch.StartAsync(version, revision, CancellationToken.None)).Outcome);
            Assert.False(relaunch.StartedHost);
            await relaunch.StopAsync(CancellationToken.None);
            Assert.True((await http.GetAsync(new Uri(game.Origin, "api/v1/handshake"))).IsSuccessStatusCode);
            File.WriteAllText(layout.SecretPath, original);
            Assert.Equal(LocalHostOutcome.Reused, (await relaunch.StartAsync(version, revision, CancellationToken.None)).Outcome);
        }
        finally
        {
            if (original is not null) File.WriteAllText(layout.SecretPath, original);
            await game.StopAsync(CancellationToken.None);
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    private sealed class ShutdownRefusalHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker inner = new(new HttpClientHandler());
        public HttpStatusCode? Refusal { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return request.RequestUri!.AbsolutePath == "/api/v1/local/shutdown" && Refusal is { } status
                ? Task.FromResult(new HttpResponseMessage(status)) : inner.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private static (string Version, string Revision) BuildOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var separator = informational.IndexOf('+', StringComparison.Ordinal);
        return separator < 0 ? (informational, "unknown") : (informational[..separator], informational[(separator + 1)..]);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
