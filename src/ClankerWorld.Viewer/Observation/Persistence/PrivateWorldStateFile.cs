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
    private bool buildRecorded;

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
            VerifyHistory(state.HistoryArchiveHead, state.BoatTransport.RetiredRequestRanges);
            if (!allowDifferentSavedSeed && !string.Equals(state.WorldSeed, worldSeed, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The private-world save belongs to a different configured seed.");
            }

            var restored = PrivateWorldRuntime.Restore(state, providerFactory);
            SaveUnsafe(restored.ExportState());
            return restored;
        }
    }

    public bool Save(PrivateWorldRuntime runtime, bool resumeOnSuccess = false)
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
            }, resumeOnSuccess);
            return compacted;
        }
    }

    public PrivateWorldDeveloperEditResult ApplyDeveloperEdit(PrivateWorldRuntime runtime, PrivateWorldDeveloperEdit edit)
    {
        lock (gate)
            return runtime.ApplyDeveloperEdit(edit, state => SaveUnsafe(state));
    }

    private PrivateWorldRuntimeState SaveUnsafe(PrivateWorldRuntimeState state, bool compactHistory = false)
    {
        if (compactHistory)
        {
            var plan = PrivateWorldHistory.Prepare(state);
            if (plan.Segment.Streams.Count > 0 || plan.Segment.ClosedBoatRequests is { Count: > 0 })
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(plan.Segment);
                var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                WriteHistorySegment(digest, bytes);
                state = plan.State with { HistoryArchiveHead = digest };
            }
        }
        var checkpointBytes = PrivateWorldRuntimeCodec.Encode(state);
        // A valid in-memory checkpoint can still serialize into invalid source
        // evidence. Check the exact bytes before replacing the last good file.
        _ = PrivateWorldRuntimeCodec.Decode(checkpointBytes);
        WriteCheckpointBytes(Path, checkpointBytes);
        // Once per host run: every later checkpoint comes from the same build.
        if (!buildRecorded) buildRecorded = SavedBuild.TryWrite(Path);
        return state;
    }

    internal string PreserveDamagedCheckpoint(byte[] expectedBytes)
    {
        lock (gate)
        {
            if (!File.ReadAllBytes(Path).AsSpan().SequenceEqual(expectedBytes))
                throw new InvalidDataException("The latest checkpoint changed. Restart before recovering it.");
            var preserved = Path + ".damaged." + Guid.NewGuid().ToString("N") + ".json";
            WriteCheckpointBytes(preserved, expectedBytes, overwrite: false);
            return preserved;
        }
    }

    internal void RestorePreservedCheckpoint(byte[] bytes)
    {
        lock (gate) WriteCheckpointBytes(Path, bytes);
    }

    private static void WriteCheckpointBytes(string destination, byte[] checkpointBytes, bool overwrite = true)
    {
        var directory = System.IO.Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The private-world state path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(checkpointBytes);
                stream.Flush(flushToDisk: true);
            }
            RestrictPermissions(temporaryPath);
            File.Move(temporaryPath, destination, overwrite);
            RestrictPermissions(destination);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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

    public void VerifyRequiredHistory(PrivateWorldRuntimeState checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        VerifyRequiredHistory(checkpoint.HistoryArchiveHead, checkpoint.BoatTransport.RetiredRequestRanges);
    }

    internal void VerifyRequiredHistory(string? historyArchiveHead, IReadOnlyList<RetiredBoatRequestRange> retiredRequests)
    {
        // No archive means no disk work. Preserve the no-history path so
        // selection can prepare its transaction before checkpoint publication.
        if (historyArchiveHead is null && retiredRequests.Count == 0) return;
        lock (gate) VerifyHistory(historyArchiveHead, retiredRequests);
    }

    /// <summary>Call while holding the installation world mutation gate.</summary>
    public void ReclaimUnreferencedHistory()
    {
        lock (gate)
        {
            var directory = Path + ".history";
            if (!Directory.Exists(directory)) return;
            var roots = new List<string> { Path };
            roots.AddRange(Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)!,
                System.IO.Path.GetFileName(Path) + ".damaged.*.json"));
            foreach (var suffix in new[] { ".manual", ".worlds" })
                if (Directory.Exists(Path + suffix))
                    roots.AddRange(Directory.GetFiles(Path + suffix, "*.save"));
            var retained = new HashSet<string>(StringComparer.Ordinal);
            // Verify every root and chain before deleting anything. Unpublished
            // checkpoints are conservative roots too; corrupt roots fail closed.
            foreach (var root in roots)
            {
                var checkpoint = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(root));
                VerifyHistory(checkpoint.HistoryArchiveHead, checkpoint.BoatTransport.RetiredRequestRanges);
                var head = checkpoint.HistoryArchiveHead;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (head is not null)
                {
                    if (!visited.Add(head)) throw new InvalidDataException("The history archive contains a cycle.");
                    var bytes = File.ReadAllBytes(HistoryPath(head));
                    if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != head)
                        throw new InvalidDataException("The history archive is corrupt.");
                    retained.Add(head);
                    head = (JsonSerializer.Deserialize<PrivateWorldHistorySegment>(bytes)
                        ?? throw new InvalidDataException("The history archive is empty.")).Parent;
                }
            }
            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                var digest = System.IO.Path.GetFileNameWithoutExtension(file);
                if (digest.Length == 64 && digest.All(char.IsAsciiHexDigit) && !retained.Contains(digest))
                    File.Delete(file);
            }
        }
    }

    private void VerifyHistory(string? head, IReadOnlyList<RetiredBoatRequestRange> retiredRequests)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var sequences = new SortedSet<long>();
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
            var segment = JsonSerializer.Deserialize<PrivateWorldHistorySegment>(bytes)
                ?? throw new InvalidDataException("The world history archive is empty.");
            foreach (var request in segment.ClosedBoatRequests ?? [])
            {
                if (request is null || request.Sequence < 1 ||
                    request.Id != "boat-request:" + request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    request.Status is not ("arrived" or "returned" or "cancelled") ||
                    request.SettledTick is not { } settled || settled < request.RequestedTick ||
                    !sequences.Add(request.Sequence))
                    throw new InvalidDataException("The boat history archive contains an invalid or duplicate closed request.");
            }
            head = segment.Parent;
        }
        // Compare ranges without expanding a potentially damaged high-water
        // mark. Only records reachable from this checkpoint count as authority.
        using var archived = sequences.GetEnumerator();
        foreach (var range in retiredRequests)
        {
            var expected = range.FirstSequence;
            while (true)
            {
                if (!archived.MoveNext() || archived.Current != expected)
                    throw new InvalidDataException("Retired boat requests do not match their reachable history archive.");
                if (expected == range.LastSequence) break;
                expected++;
            }
        }
        if (archived.MoveNext())
            throw new InvalidDataException("The history archive contains boat requests that this checkpoint has not retired.");
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
