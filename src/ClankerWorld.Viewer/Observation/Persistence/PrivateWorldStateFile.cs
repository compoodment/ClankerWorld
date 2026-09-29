using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Atomic persistence for the integrated private-world alpha runtime. The
/// provider factory is supplied by the host and credentials never enter the
/// checkpoint bytes.
/// </summary>
public sealed class PrivateWorldStateFile
{
    private readonly object gate = new();
    private readonly Func<string, IDecisionProvider>? providerFactory;
    private readonly WorldStartPace newWorldPace;
    private readonly GeographyOptions? newWorldGeography;
    private readonly bool allowDifferentSavedSeed;

    public PrivateWorldStateFile(string path, Func<string, IDecisionProvider>? providerFactory = null,
        WorldStartPace newWorldPace = WorldStartPace.Legacy, GeographyOptions? newWorldGeography = null,
        bool allowDifferentSavedSeed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        this.providerFactory = providerFactory;
        this.newWorldPace = newWorldPace;
        this.newWorldGeography = newWorldGeography;
        this.allowDifferentSavedSeed = allowDifferentSavedSeed;
    }

    public string Path { get; }

    public PrivateWorldRuntime LoadOrCreate(string worldSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        lock (gate)
        {
            if (!File.Exists(Path))
            {
                var created = new PrivateWorldRuntime(worldSeed, providerFactory, startPace: newWorldPace,
                    geographyOptions: newWorldGeography);
                SaveUnsafe(created.ExportState());
                return created;
            }

            var state = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(Path));
            VerifyHistory(state.HistoryArchiveHead);
            if (!allowDifferentSavedSeed && !string.Equals(state.WorldSeed, worldSeed, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The private-world save belongs to a different configured seed.");
            }

            var restored = PrivateWorldRuntime.Restore(state, providerFactory);
            SaveUnsafe(restored.ExportState());
            return restored;
        }
    }

    public bool Save(PrivateWorldRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (gate)
        {
            var compacted = false;
            runtime.PersistCheckpoint(state =>
            {
                var saved = SaveUnsafe(state, compactHistory: true);
                compacted = saved.HistoryArchiveHead != state.HistoryArchiveHead;
                return saved;
            });
            return compacted;
        }
    }

    private PrivateWorldRuntimeState SaveUnsafe(PrivateWorldRuntimeState state, bool compactHistory = false)
    {
        if (compactHistory)
        {
            var plan = PrivateWorldHistory.Prepare(state);
            if (plan.Segment.Streams.Count > 0)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(plan.Segment);
                var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                WriteHistorySegment(digest, bytes);
                state = plan.State with { HistoryArchiveHead = digest };
            }
        }
        var directory = System.IO.Path.GetDirectoryName(Path) ??
            throw new InvalidOperationException("The private-world state path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(PrivateWorldRuntimeCodec.Encode(state));
                stream.Flush(flushToDisk: true);
            }
            RestrictPermissions(temporaryPath);
            File.Move(temporaryPath, Path, overwrite: true);
            RestrictPermissions(Path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        return state;
    }

    private string HistoryPath(string digest)
    {
        if (digest.Length != 64 || digest.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException("The world history archive reference is invalid.");
        }
        return System.IO.Path.Combine(Path + ".history", digest + ".json");
    }

    private void WriteHistorySegment(string digest, byte[] bytes)
    {
        var destination = HistoryPath(digest);
        Directory.CreateDirectory(Path + ".history");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path + ".history", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        if (File.Exists(destination))
        {
            if (!File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("A world history segment failed content verification.");
            }
            return;
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            RestrictPermissions(temporary);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void VerifyHistory(string? head)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (head is not null)
        {
            if (!visited.Add(head))
            {
                throw new InvalidDataException("The world history archive contains a cycle.");
            }
            var bytes = File.ReadAllBytes(HistoryPath(head));
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != head)
            {
                throw new InvalidDataException("The world history archive is corrupt.");
            }
            head = (JsonSerializer.Deserialize<PrivateWorldHistorySegment>(bytes)
                ?? throw new InvalidDataException("The world history archive is empty.")).Parent;
        }
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
