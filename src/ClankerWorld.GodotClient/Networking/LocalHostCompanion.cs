using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ClankerWorld.GodotClient.Networking;

/// <summary>
/// Where the bundled host lives next to the game, and where it keeps the
/// player's data. Program files and user data never share a folder, so
/// replacing a game version cannot touch saves, keys or the model-call count.
/// </summary>
public sealed record LocalHostLayout(
    string FileName,
    IReadOnlyList<string> PrefixArguments,
    string HostDirectory,
    string DataDirectory)
{
    public const string HostFolderName = "host";
    public const string WindowsHostFileName = "ClankerWorld.Viewer.exe";

    public string SecretPath => Path.Combine(DataDirectory, "host", "companion.secret");
    public string LogPath => Path.Combine(DataDirectory, "logs", "host.log");
    public string SaveDirectory => Path.Combine(DataDirectory, "saves");
    public string SettingsDirectory => Path.Combine(DataDirectory, "settings");

    /// <summary>
    /// Finds the host shipped in the <c>host</c> folder beside the game. A
    /// development build or a server-only setup has none, and keeps
    /// connecting to whatever host the player chose.
    /// </summary>
    public static LocalHostLayout? FindBundled(string gameExecutablePath, string localApplicationData)
    {
        var gameDirectory = Path.GetDirectoryName(Path.GetFullPath(gameExecutablePath));
        if (gameDirectory is null || string.IsNullOrWhiteSpace(localApplicationData)) return null;
        var hostDirectory = Path.Combine(gameDirectory, HostFolderName);
        var hostExecutable = Path.Combine(hostDirectory, WindowsHostFileName);
        return File.Exists(hostExecutable)
            ? new LocalHostLayout(hostExecutable, [], hostDirectory, Path.Combine(localApplicationData, "ClankerWorld"))
            : null;
    }

    /// <summary>
    /// Host settings. Only paths and ports: the secret stays in its file and
    /// keys stay in the host's protected store, never on the command line.
    /// </summary>
    public IReadOnlyList<string> HostArguments(int port, int approvalPort) =>
    [
        .. PrefixArguments,
        $"--contentRoot={HostDirectory}",
        $"--ClankerWorld:Http:Port={port}",
        $"--ClankerWorld:Pairing:LocalApprovalPort={approvalPort}",
        $"--ClankerWorld:Pairing:CompanionSecretPath={SecretPath}",
        $"--ClankerWorld:Pairing:StatePath={Path.Combine(SettingsDirectory, "owner-authority.json")}",
        $"--ClankerWorld:Runtime:StatePath={Path.Combine(SaveDirectory, "private-world.json")}",
        $"--ClankerWorld:Runtime:ProviderStatePath={Path.Combine(SettingsDirectory, "provider-configuration.json")}",
        $"--ClankerWorld:Runtime:ProviderUsagePath={Path.Combine(SettingsDirectory, "provider-usage.json")}",
    ];
}

public enum LocalHostOutcome
{
    /// <summary>This game started the host and it answered.</summary>
    Started,
    /// <summary>A host of this exact build that an earlier launch started is still running.</summary>
    Reused,
    /// <summary>Something that is not this build's host holds the port.</summary>
    PortInUse,
    /// <summary>The host exited before it was ready.</summary>
    Exited,
    /// <summary>The host did not answer in time.</summary>
    TimedOut,
    /// <summary>The host program could not be started at all.</summary>
    CouldNotStart,
}

public sealed record LocalHostStart(LocalHostOutcome Outcome, string? Detail = null)
{
    public bool IsReady => Outcome is LocalHostOutcome.Started or LocalHostOutcome.Reused;

    /// <summary>A sentence for the player: what went wrong and what to do next.</summary>
    public string PlayerMessage => Outcome switch
    {
        LocalHostOutcome.Started or LocalHostOutcome.Reused => "Your world server is running on this PC.",
        LocalHostOutcome.PortInUse =>
            $"Another program is using the port the world server needs ({Detail}). Close it, or another copy of ClankerWorld, then try again. Your saves are safe.",
        LocalHostOutcome.Exited =>
            "The world server closed while starting. Your saves are safe. Try again, and if it keeps happening, send the host log from your ClankerWorld folder.",
        LocalHostOutcome.TimedOut =>
            "The world server is taking too long to start. Your saves are safe. Try again.",
        _ => "The world server could not be started. Check that the game folder is complete, then try again.",
    };
}

/// <summary>
/// Starts, pairs with and stops the host bundled with the game, on this PC
/// only. It never stops a process it did not start, and never looks for or
/// falls back to a remote server.
/// </summary>
public sealed class LocalHostCompanion : IAsyncDisposable
{
    public const int DefaultPort = 5188;
    public const int DefaultApprovalPort = 5189;
    public const string SecretHeader = "X-ClankerWorld-Companion-Secret";

    private readonly LocalHostLayout layout;
    private readonly HttpClient http;
    private readonly int port;
    private readonly int approvalPort;
    private readonly TimeSpan readyTimeout;
    private Process? process;
    private StreamWriter? log;
    private string? secret;
    private readonly object logGate = new();

    public LocalHostCompanion(LocalHostLayout layout, HttpClient http, int port = DefaultPort,
        int approvalPort = DefaultApprovalPort, TimeSpan? readyTimeout = null)
    {
        this.layout = layout;
        this.http = http;
        this.port = port;
        this.approvalPort = approvalPort;
        this.readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(90);
    }

    /// <summary>
    /// A fixed literal-loopback origin: the client pins the origin it paired
    /// with, so it must not change between launches.
    /// </summary>
    public Uri Origin => new($"http://127.0.0.1:{port}/");

    private Uri ApprovalOrigin => new($"http://127.0.0.1:{approvalPort}/");

    public bool StartedHost => process is not null;

    public async Task<LocalHostStart> StartAsync(string expectedVersion, string expectedRevision,
        CancellationToken cancellationToken)
    {
        if (process is { HasExited: false }) return new LocalHostStart(LocalHostOutcome.Started);
        var existing = await ProbeAsync(expectedVersion, expectedRevision, cancellationToken);
        if (existing == Probe.SameBuild && TryReadSecret(out var running) &&
            await IsCompanionAsync(running, cancellationToken))
        {
            secret = running;
            return new LocalHostStart(LocalHostOutcome.Reused);
        }
        if (existing != Probe.Nothing) return new LocalHostStart(LocalHostOutcome.PortInUse, port.ToString(CultureInfo.InvariantCulture));

        try
        {
            secret = WriteFreshSecret();
            process = Launch();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await StopLoggingAsync();
            process = null;
            return new LocalHostStart(LocalHostOutcome.CouldNotStart, exception.GetType().Name);
        }

        var deadline = DateTimeOffset.UtcNow + readyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                var code = process.ExitCode;
                await ForgetProcessAsync();
                return new LocalHostStart(LocalHostOutcome.Exited, code.ToString(CultureInfo.InvariantCulture));
            }
            switch (await ProbeAsync(expectedVersion, expectedRevision, cancellationToken))
            {
                case Probe.SameBuild:
                    if (await IsCompanionAsync(secret!, cancellationToken))
                        return new LocalHostStart(LocalHostOutcome.Started);
                    break;
                case Probe.OtherBuild or Probe.NotClankerWorld:
                    // Something else claimed the port first; leave it, stop ours.
                    await StopAsync(cancellationToken);
                    return new LocalHostStart(LocalHostOutcome.PortInUse, port.ToString(CultureInfo.InvariantCulture));
            }
            await Task.Delay(250, cancellationToken);
        }
        await StopAsync(cancellationToken);
        return new LocalHostStart(LocalHostOutcome.TimedOut);
    }

    /// <summary>
    /// Approves this game's own pending pairing on the host's approval
    /// listener, using the comparison code the host returned to it.
    /// </summary>
    public async Task<bool> ApprovePairingAsync(string pairingId, string pairingCode, CancellationToken cancellationToken)
    {
        if (secret is null) return false;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(ApprovalOrigin, $"api/v1/local/pairings/{Uri.EscapeDataString(pairingId)}/approve"))
        {
            Content = JsonContent.Create(new { pairingCode }),
        };
        request.Headers.Add(SecretHeader, secret);
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Asks the host to save and exit, and waits for it. Only a host this
    /// launch started is ever forced to stop, and only if it ignores the request.
    /// </summary>
    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        if (secret is not null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ApprovalOrigin, "api/v1/local/shutdown"));
                request.Headers.Add(SecretHeader, secret);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await http.SendAsync(request, deadline.Token);
                // The host explicitly refused to exit: preserve its paused world
                // and our process handle so the player can repair and retry.
                if (!response.IsSuccessStatusCode) return false;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Already gone, or not answering: handled below for a host we started.
            }
        }
        if (process is { HasExited: false } running)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await running.WaitForExitAsync(wait.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The host saves every tick, so forcing our own child is the last resort.
                running.Kill(entireProcessTree: true);
            }
        }
        await ForgetProcessAsync();
        return true;
    }

    public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None); }

    private async Task<bool> IsCompanionAsync(string value, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApprovalOrigin, "api/v1/local/companion"));
            request.Headers.Add(SecretHeader, value);
            using var response = await http.SendAsync(request, deadline.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException &&
            !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private enum Probe { Nothing, SameBuild, OtherBuild, NotClankerWorld }

    private async Task<Probe> ProbeAsync(string expectedVersion, string expectedRevision, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            using var handshake = await http.GetAsync(new Uri(Origin, "api/v1/handshake"), deadline.Token);
            if (!handshake.IsSuccessStatusCode) return Probe.NotClankerWorld;
            var status = await http.GetFromJsonAsync<HostStatus>(new Uri(Origin, "api/v1/status"), deadline.Token);
            return status is not null &&
                string.Equals(status.Version, expectedVersion, StringComparison.Ordinal) &&
                string.Equals(status.SourceRevision, expectedRevision, StringComparison.Ordinal)
                    ? Probe.SameBuild
                    : Probe.OtherBuild;
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return Probe.Nothing;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or
            NotSupportedException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Probe.NotClankerWorld;
        }
    }

    private bool TryReadSecret(out string value)
    {
        try
        {
            value = File.ReadAllText(layout.SecretPath).Trim();
            return value.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            value = string.Empty;
            return false;
        }
    }

    private string WriteFreshSecret()
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Directory.CreateDirectory(Path.GetDirectoryName(layout.SecretPath)!);
        // The player's own data folder is private to their Windows account.
        // Elsewhere, restrict the file to this user before writing the value.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(layout.SecretPath, options))
        using (var writer = new StreamWriter(file))
        {
            writer.Write(value);
        }
        return value;
    }

    private Process Launch()
    {
        Directory.CreateDirectory(layout.SaveDirectory);
        Directory.CreateDirectory(layout.SettingsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.LogPath)!);
        log = new StreamWriter(new FileStream(layout.LogPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
        var start = new ProcessStartInfo(layout.FileName)
        {
            WorkingDirectory = layout.HostDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in layout.HostArguments(port, approvalPort)) start.ArgumentList.Add(argument);
        var started = new Process { StartInfo = start, EnableRaisingEvents = true };
        started.OutputDataReceived += (_, line) => WriteLog(line.Data);
        started.ErrorDataReceived += (_, line) => WriteLog(line.Data);
        if (!started.Start()) throw new InvalidOperationException("The host process did not start.");
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();
        return started;
    }

    private void WriteLog(string? line)
    {
        if (line is null) return;
        try
        {
            lock (logGate) log?.WriteLine(line);
        }
        catch (ObjectDisposedException)
        {
            // The game is closing the log after the host exited.
        }
    }

    private async Task ForgetProcessAsync()
    {
        process?.Dispose();
        process = null;
        await StopLoggingAsync();
    }

    private async Task StopLoggingAsync()
    {
        StreamWriter? closing;
        lock (logGate)
        {
            closing = log;
            log = null;
        }
        if (closing is not null) await closing.DisposeAsync();
    }

    private sealed record HostStatus(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("sourceRevision")] string? SourceRevision);
}
