using System.Runtime.InteropServices;

namespace ClankerWorld.Viewer.Observation;

public sealed record SaveDiskSpaceStatus(string State, long? AvailableBytes, long WarningBelowBytes,
    DateTimeOffset? CheckedUtc);

/// <summary>Reads space available to this host on the volume containing a save directory.</summary>
public interface ISaveDiskSpaceProbe
{
    long? AvailableBytes(string directory);
}

public sealed class SaveDiskSpaceProbe : ISaveDiskSpaceProbe
{
    public long? AvailableBytes(string directory)
    {
        var existing = new DirectoryInfo(Path.GetFullPath(directory));
        while (!existing.Exists && existing.Parent is { } parent) existing = parent;
        var resolved = Path.GetPathRoot(existing.FullName)!;
        foreach (var part in Path.GetRelativePath(resolved, existing.FullName).Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new DirectoryInfo(Path.Combine(resolved, part));
            resolved = next.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next.FullName;
        }
        static string WithSeparator(string path) => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
        // Windows volume mount folders and network shares need the actual
        // directory query; a drive-letter lookup can report a different disk.
        if (OperatingSystem.IsWindows())
            return GetDiskFreeSpaceEx(WithSeparator(resolved), out var available, out _, out _)
                ? (long)Math.Min(available, (ulong)long.MaxValue) : null;
        var location = WithSeparator(resolved);
        // A save folder can be on a mounted volume rather than the filesystem root.
        var drive = DriveInfo.GetDrives().Where(item => location.StartsWith(
                WithSeparator(item.Name), StringComparison.Ordinal))
            .OrderByDescending(item => item.Name.Length).FirstOrDefault();
        return drive?.AvailableFreeSpace;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong availableBytes,
        out ulong totalBytes, out ulong totalFreeBytes);
}

/// <summary>
/// Samples outside world locks. Clients read this cached advisory; even a
/// stalled volume query cannot hold up emergency checkpoints or host startup.
/// </summary>
public sealed partial class SaveDiskSpaceMonitor(string activeSavePath, ISaveDiskSpaceProbe probe,
    ILogger<SaveDiskSpaceMonitor>? logger = null, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    // Provisional alpha level, to be tuned by playtesting.
    public const long WarningBelowBytes = 1024L * 1024 * 1024;
    private SaveDiskSpaceStatus status = new("unknown", null, WarningBelowBytes, null);

    public SaveDiskSpaceStatus Capture()
    {
        var current = Volatile.Read(ref status);
        return current.CheckedUtc is { } checkedUtc && clock.GetUtcNow() - checkedUtc is { } age &&
            age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(30)
            ? current : new("unknown", null, WarningBelowBytes, current.CheckedUtc);
    }

    [LoggerMessage(EventId = 2292, Level = LogLevel.Warning,
        Message = "save_disk_space state={State} available_bytes={AvailableBytes} warning_below_bytes={WarningBelowBytes} recovery_unblocked=true")]
    private static partial void LogStatus(ILogger logger, string state, long? availableBytes, long warningBelowBytes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Never run a filesystem query on StartAsync's calling thread.
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            var sampledUtc = clock.GetUtcNow();
            long? Read(string directory)
            {
                try { return probe.AvailableBytes(directory) is >= 0 and var bytes ? bytes : null; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                    or System.Security.SecurityException or NotSupportedException)
                { return null; }
            }
            long?[] samples = [Read(Path.GetDirectoryName(Path.GetFullPath(activeSavePath))!),
                Read(Path.GetFullPath(activeSavePath) + ".manual"), Read(Path.GetFullPath(activeSavePath) + ".worlds"),
                Read(Path.GetFullPath(activeSavePath) + ".history")];
            var minimum = samples.Min();
            var available = samples.All(item => item is not null) || minimum is < WarningBelowBytes ? minimum : null;
            var next = new SaveDiskSpaceStatus(available is null ? "unknown" : available < WarningBelowBytes ? "low" : "ok",
                available, WarningBelowBytes, sampledUtc);
            var previous = Interlocked.Exchange(ref status, next);
            if (logger is not null && previous.State != next.State && next.State != "ok")
                LogStatus(logger, next.State, next.AvailableBytes, next.WarningBelowBytes);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
