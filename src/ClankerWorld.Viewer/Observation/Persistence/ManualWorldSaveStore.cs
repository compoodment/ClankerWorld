using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

public sealed record ManualWorldSave(string Id, string Name, DateTimeOffset CreatedUtc, long WorldTick,
    bool IsAutosave = false);
public sealed record ManualSaveOverwriteReceipt(ManualWorldSave Saved, string BackupId);

/// <summary>
/// Owner-only named checkpoints for the currently active world. Opaque IDs,
/// atomic writes, and private files keep names out of paths and credentials
/// out of world saves. History segments remain alongside the active save.
/// </summary>
public sealed class ManualWorldSaveStore
{
    private sealed record Metadata(ManualWorldSave Save, IReadOnlyList<InhabitantProviderAssignment> Assignments,
        WorldAutosaveSettings? AutosaveSettings, string? WorldId = null, string? Generation = null);
    private readonly object gate;
    private readonly string directory;
    private readonly ILogger<ManualWorldSaveStore>? logger;
    private readonly HashSet<string> reportedInvalidMetadata = new(StringComparer.Ordinal);

    public ManualWorldSaveStore(string activeSavePath, ILogger<ManualWorldSaveStore>? logger = null, object? mutationGate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeSavePath);
        directory = Path.GetFullPath(activeSavePath) + ".manual";
        this.logger = logger;
        gate = mutationGate ?? new object();
    }

    public static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80 || normalized.Any(char.IsControl))
            throw new ArgumentException("Save name must be 1–80 printable characters.", nameof(name));
        return normalized;
    }

    public ManualWorldSave Create(string name, PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings? autosaveSettings = null)
        => CreateCore(name, runtime, assignments, autosaveSettings, isAutosave: false);

    public ManualWorldSave CreateAutosave(PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings autosaveSettings)
        => CreateCore("Autosave", runtime, assignments, autosaveSettings, isAutosave: true);

    /// <summary>Replace one selected named checkpoint, preserving its previous bytes in a new recovery checkpoint.</summary>
    public ManualSaveOverwriteReceipt Overwrite(string id, PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings? autosaveSettings = null)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(assignments);
        lock (gate)
        {
            var state = runtime.ExportState();
            if (!state.Society.Society.IsPaused)
                throw new InvalidOperationException("Pause the world before overwriting a manual save.");
            if (!File.Exists(MetadataPath(id)))
                throw new FileNotFoundException("The selected manual save no longer exists.");
            var previousMetadata = ReadMetadata(id);
            if (previousMetadata.Save.Id != id || previousMetadata.Save.IsAutosave ||
                previousMetadata.WorldId != state.Society.Society.WorldId)
                throw new InvalidOperationException("Only a named save from this world can be overwritten.");
            var previousBytes = File.ReadAllBytes(CommittedStatePath(id, previousMetadata));
            var oldCheckpoint = PrivateWorldRuntimeCodec.Decode(previousBytes);
            if (oldCheckpoint.Society.Society.WorldId != previousMetadata.WorldId)
                throw new InvalidDataException("The selected checkpoint does not match its metadata.");

            var backupName = "Before overwriting: " + previousMetadata.Save.Name;
            var backup = previousMetadata.Save with
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = backupName[..Math.Min(80, backupName.Length)],
                CreatedUtc = DateTimeOffset.UtcNow
            };
            WriteAtomic(StatePath(backup.Id), previousBytes);
            WriteAtomic(MetadataPath(backup.Id), JsonSerializer.SerializeToUtf8Bytes(
                previousMetadata with { Save = backup, Generation = null }));

            var saved = previousMetadata.Save with
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                WorldTick = state.Society.Society.WorldTick
            };
            var generation = Guid.NewGuid().ToString("N");
            // Only metadata publishes the new immutable generation. A failed metadata
            // replacement leaves the prior checkpoint and routing/settings selected.
            WriteAtomic(GenerationPath(id, generation), PrivateWorldRuntimeCodec.Encode(state));
            WriteAtomic(MetadataPath(id), JsonSerializer.SerializeToUtf8Bytes(new Metadata(
                saved, assignments, autosaveSettings, state.Society.Society.WorldId, generation)), overwrite: true);
            return new ManualSaveOverwriteReceipt(saved, backup.Id);
        }
    }

    private ManualWorldSave CreateCore(string name, PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments,
        WorldAutosaveSettings? autosaveSettings, bool isAutosave)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(assignments);
        name = NormalizeName(name);
        lock (gate)
        {
            var state = runtime.ExportState();
            if (!isAutosave && !state.Society.Society.IsPaused)
                throw new InvalidOperationException("Pause the world before making a manual save.");
            var entry = new ManualWorldSave(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow,
                state.Society.Society.WorldTick, isAutosave);
            Directory.CreateDirectory(directory);
            RestrictDirectory();
            WriteAtomic(StatePath(entry.Id), PrivateWorldRuntimeCodec.Encode(state));
            WriteAtomic(MetadataPath(entry.Id), JsonSerializer.SerializeToUtf8Bytes(
                new Metadata(entry, assignments, autosaveSettings, state.Society.Society.WorldId)));
            return entry;
        }
    }

    // The metadata rename is the durable point of deletion. A partial cleanup
    // stays hidden from List/Read and can be resumed without reviving the save.
    public void Delete(string id, string worldId, DateTimeOffset expectedCreatedUtc)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        lock (gate)
        {
            var intent = Path.Combine(directory, id + ".deleting.json");
            var source = File.Exists(intent) ? intent : MetadataPath(id);
            var metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(source))
                ?? throw new InvalidDataException("The save metadata is invalid.");
            if (metadata.Save.Id != id || metadata.WorldId != worldId ||
                metadata.Save.CreatedUtc != expectedCreatedUtc)
                throw new InvalidOperationException("The selected save changed. Refresh the list before deleting.");
            if (source != intent) File.Move(source, intent);
            DeleteCheckpointFiles(id);
            File.Delete(intent);
        }
    }

    public void DeleteWorldSnapshots(string worldId)
    {
        lock (gate)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                var name = Path.GetFileName(path);
                if (!name.EndsWith(".meta.json", StringComparison.Ordinal) &&
                    !name.EndsWith(".deleting.json", StringComparison.Ordinal)) continue;
                var id = name.Split('.')[0];
                if (!IsId(id)) continue;
                var metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(path))
                    ?? throw new InvalidDataException("Save ownership cannot be established.");
                if (metadata.WorldId == worldId)
                    Delete(id, worldId, metadata.Save.CreatedUtc);
            }
            // A failed create/overwrite may leave unpublished generations. Decode
            // ownership before removal; never guess from a filename or seed.
            foreach (var path in Directory.GetFiles(directory, "*.save"))
            {
                var parts = Path.GetFileName(path).Split('.');
                if (parts.Length is not (2 or 3) || !IsId(parts[0]) ||
                    parts.Length == 3 && !IsId(parts[1])) continue;
                var state = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path));
                if (state.Society.Society.WorldId == worldId) File.Delete(path);
            }
        }
    }

    public void RecoverDeletions()
    {
        lock (gate)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, "*.deleting.json"))
            {
                var metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(path))
                    ?? throw new InvalidDataException("The pending save deletion is invalid.");
                if (Path.GetFileName(path) != metadata.Save.Id + ".deleting.json" || metadata.WorldId is null)
                    throw new InvalidDataException("The pending save deletion identity is invalid.");
                Delete(metadata.Save.Id, metadata.WorldId, metadata.Save.CreatedUtc);
            }
        }
    }

    private void DeleteCheckpointFiles(string id)
    {
        File.Delete(StatePath(id));
        foreach (var path in Directory.GetFiles(directory, id + ".*.save"))
        {
            var parts = Path.GetFileName(path).Split('.');
            if (parts.Length == 3 && IsId(parts[1])) File.Delete(path);
        }
    }

    public IReadOnlyList<ManualWorldSave> List(string? worldId = null)
    {
        lock (gate)
        {
            if (!Directory.Exists(directory)) return [];
            var entries = new List<ManualWorldSave>();
            var invalidPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(directory, "*.meta.json"))
            {
                // Metadata cannot redirect rotation onto a different checkpoint.
                var fileId = Path.GetFileName(path)[..^".meta.json".Length];
                try
                {
                    var item = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(path));
                    if (!IsValidMetadata(item, fileId))
                    {
                        ReportInvalid(path, fileId, "invalid_metadata", invalidPaths);
                        continue;
                    }
                    if (!File.Exists(CommittedStatePath(fileId, item)))
                    {
                        ReportInvalid(path, fileId, "missing_checkpoint", invalidPaths);
                        continue;
                    }
                    reportedInvalidMetadata.Remove(path);
                    if (worldId is null || item.WorldId == worldId) entries.Add(item.Save);
                }
                catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
                {
                    ReportInvalid(path, fileId, exception is JsonException ? "invalid_json" : "unreadable_metadata", invalidPaths);
                }
            }
            reportedInvalidMetadata.IntersectWith(invalidPaths);
            return entries
                .OrderByDescending(item => item.CreatedUtc)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private void ReportInvalid(string path, string fileId, string reason, HashSet<string> invalidPaths)
    {
        invalidPaths.Add(path);
        if (reportedInvalidMetadata.Add(path) && logger is not null)
            ManualWorldSaveTelemetry.InvalidMetadata(logger, IsId(fileId) ? fileId : "invalid", reason);
    }

    public void KeepNewestAutosaves(int count, string? preserveId = null, string? worldId = null)
    {
        if (count is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(count));
        lock (gate)
        {
            var candidates = List(worldId).Where(item => item.IsAutosave && item.Id != preserveId)
                .Skip(preserveId is null ? count : count - 1);
            foreach (var old in candidates)
            {
                // The new snapshot is already durable before older rotations
                // are retired. Named manual saves are never included here.
                File.Delete(MetadataPath(old.Id));
                File.Delete(StatePath(old.Id));
            }
        }
    }

    public PrivateWorldRuntimeState Read(string id)
    {
        return ReadCommitted(id).Checkpoint;
    }

    /// <summary>Read the checkpoint and its routing/settings from one published generation.</summary>
    public (PrivateWorldRuntimeState Checkpoint, IReadOnlyList<InhabitantProviderAssignment> Assignments,
        WorldAutosaveSettings? AutosaveSettings) ReadCommitted(string id)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        lock (gate)
        {
            if (!File.Exists(MetadataPath(id)))
                throw new FileNotFoundException("The manual save does not exist.");
            var metadata = ReadMetadata(id);
            var checkpoint = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(CommittedStatePath(id, metadata)));
            return (checkpoint, metadata.Assignments, metadata.AutosaveSettings);
        }
    }

    public IReadOnlyList<InhabitantProviderAssignment> ReadAssignments(string id)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        lock (gate)
        {
            if (!File.Exists(MetadataPath(id)))
                throw new FileNotFoundException("The manual save does not exist.");
            return ReadMetadata(id).Assignments;
        }
    }

    public WorldAutosaveSettings? ReadAutosaveSettings(string id)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        lock (gate)
        {
            if (!File.Exists(MetadataPath(id)))
                throw new FileNotFoundException("The manual save does not exist.");
            return ReadMetadata(id).AutosaveSettings;
        }
    }

    private Metadata ReadMetadata(string id)
    {
        Metadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(MetadataPath(id)));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The manual save metadata is invalid.", exception);
        }
        if (!IsValidMetadata(metadata, id))
            throw new InvalidDataException("The manual save metadata is invalid.");
        return metadata;
    }

    private static bool IsValidMetadata([NotNullWhen(true)] Metadata? metadata, string id) =>
        metadata?.Save is { } save && IsId(id) && save.Id == id &&
        !string.IsNullOrWhiteSpace(save.Name) && save.Name.Length <= 80 &&
        !save.Name.Any(char.IsControl) && save.WorldTick >= 0 &&
        metadata.Assignments is not null && metadata.Assignments.All(item => item is not null) &&
        (metadata.Generation is null || IsId(metadata.Generation));

    private string CommittedStatePath(string id, Metadata metadata) => metadata.Generation is { } generation
        ? GenerationPath(id, generation) : StatePath(id);

    private string GenerationPath(string id, string generation)
    {
        if (!IsId(generation)) throw new InvalidDataException("The manual save generation is invalid.");
        return Path.Combine(directory, id + "." + generation + ".save");
    }

    private string StatePath(string id) => Path.Combine(directory, id + ".save");
    private string MetadataPath(string id) => Path.Combine(directory, id + ".meta.json");
    private static bool IsId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);

    private void RestrictDirectory()
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WriteAtomic(string destination, byte[] bytes, bool overwrite = false)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, destination, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
