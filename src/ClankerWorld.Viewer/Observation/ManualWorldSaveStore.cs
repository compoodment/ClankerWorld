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
    private readonly object gate = new();
    private readonly string directory;
    private readonly ILogger<ManualWorldSaveStore>? logger;
    private readonly HashSet<string> reportedInvalidMetadata = new(StringComparer.Ordinal);

    public ManualWorldSaveStore(string activeSavePath, ILogger<ManualWorldSaveStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeSavePath);
        directory = Path.GetFullPath(activeSavePath) + ".manual";
        this.logger = logger;
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
        var state = runtime.ExportState();
        if (!state.Society.Society.IsPaused)
            throw new InvalidOperationException("Pause the world before overwriting a manual save.");
        lock (gate)
        {
            if (!File.Exists(MetadataPath(id)))
                throw new FileNotFoundException("The selected manual save no longer exists.");
            var previousMetadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(MetadataPath(id)))
                ?? throw new InvalidDataException("The selected manual save metadata is invalid.");
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
        var state = runtime.ExportState();
        if (!isAutosave && !state.Society.Society.IsPaused)
            throw new InvalidOperationException("Pause the world before making a manual save.");
        var entry = new ManualWorldSave(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow,
            state.Society.Society.WorldTick, isAutosave);
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            RestrictDirectory();
            WriteAtomic(StatePath(entry.Id), PrivateWorldRuntimeCodec.Encode(state));
            WriteAtomic(MetadataPath(entry.Id), JsonSerializer.SerializeToUtf8Bytes(
                new Metadata(entry, assignments, autosaveSettings, state.Society.Society.WorldId)));
        }
        return entry;
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
                    if (item?.Save is null || !IsId(fileId) || item.Save.Id != fileId || item.Generation is not null && !IsId(item.Generation))
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
        var metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(MetadataPath(id)))
            ?? throw new InvalidDataException("The manual save metadata is invalid.");
        if (metadata.Save.Id != id || metadata.Generation is not null && !IsId(metadata.Generation))
            throw new InvalidDataException("The manual save generation is invalid.");
        return metadata;
    }

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
