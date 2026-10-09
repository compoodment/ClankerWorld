using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

public sealed record ManualWorldSave(string Id, string Name, DateTimeOffset CreatedUtc, long WorldTick,
    bool IsAutosave = false, SaveBranch? Branch = null, string? ContinuedFromId = null,
    DateTimeOffset? ContinuedFromCreatedUtc = null, long BranchPosition = 0);
public sealed record ManualSaveOverwriteReceipt(ManualWorldSave Saved, string BackupId);

/// <summary>
/// One version of a world's history. Playing on from an older save starts a new
/// branch, so later saves of the original branch stay where they were. Saves
/// made before branches existed have no branch.
/// </summary>
public sealed record SaveBranch(string Id, int Number, string? StartedFromId = null,
    string? StartedFromName = null, long? StartedFromTick = null);

/// <summary>The branch record to put back if loading a save fails part-way.</summary>
public sealed record SaveTimelineRestorePoint(string WorldId, byte[]? Bytes);

/// <summary>
/// Where the running world's history continues: the save it was last loaded from
/// or saved as, that save's branch, and whether the next save starts a new branch.
/// </summary>
public sealed record SaveTimelinePosition(string? ContinuedFromId, string? BranchId, bool StartsNewBranch,
    int? NextBranchNumber = null, long? ContinuedFromTick = null);

/// <summary>
/// Owner-only named checkpoints for the currently active world. Opaque IDs,
/// atomic writes, and private files keep names out of paths and credentials
/// out of world saves. History segments remain alongside the active save.
/// </summary>
public sealed partial class ManualWorldSaveStore
{
    private sealed record Metadata(ManualWorldSave Save, IReadOnlyList<InhabitantProviderAssignment> Assignments,
        WorldAutosaveSettings? AutosaveSettings, string? WorldId = null, string? Generation = null,
        [property: JsonIgnore] bool HasInvalidBranchMetadata = false, RecoveryProvenance? Recovery = null);
    private sealed record BranchMetadata(SaveBranch? Branch = null, string? ContinuedFromId = null,
        DateTimeOffset? ContinuedFromCreatedUtc = null, long BranchPosition = 0);
    // Where the running world's history continues from: its branch (null when the
    // next save must start one) and the last save it was loaded from or saved as.
    private sealed record Timeline(string WorldId, SaveBranch? Branch, string? ContinuedFromId,
        string? ContinuedFromName, long ContinuedFromTick, int LastBranchNumber,
        DateTimeOffset? ContinuedFromCreatedUtc = null, long ContinuedFromBranchPosition = 0,
        string? LoadedStateFingerprint = null, string? SavedCheckpointFingerprint = null);
    private const string LegacyBranchKey = "";
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
                previousMetadata with { Save = backup, Generation = null, Recovery = NextRecovery(id) }));

            // The backup keeps the old version in its original branch. The chosen
            // slot now holds the running world, so it joins the running world's branch.
            var worldId = state.Society.Society.WorldId;
            var timeline = ReadTimeline(worldId);
            // Decide against the actual loaded version before its ID is rebound
            // to the recovery copy. Positions survive deleted intermediate saves.
            var branch = ResolveBranch(timeline, List(worldId));
            if (timeline?.ContinuedFromId == id)
            {
                timeline = timeline with
                {
                    ContinuedFromId = backup.Id,
                    ContinuedFromName = backup.Name,
                    ContinuedFromCreatedUtc = backup.CreatedUtc
                };
                if (branch.Id != timeline.Branch?.Id)
                    branch = branch with { StartedFromId = backup.Id, StartedFromName = backup.Name };
            }
            var saved = previousMetadata.Save with
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                WorldTick = state.Society.Society.WorldTick,
                Branch = branch,
                ContinuedFromId = timeline?.ContinuedFromId,
                ContinuedFromCreatedUtc = timeline?.ContinuedFromCreatedUtc,
                BranchPosition = NextBranchPosition(branch, timeline)
            };
            var generation = Guid.NewGuid().ToString("N");
            // Only metadata publishes the new immutable generation. A failed metadata
            // replacement leaves the prior checkpoint and routing/settings selected.
            var bytes = PrivateWorldRuntimeCodec.Encode(state);
            WriteAtomic(GenerationPath(id, generation), bytes);
            AdvanceTimeline(worldId, timeline, saved, bytes);
            WriteAtomic(MetadataPath(id), JsonSerializer.SerializeToUtf8Bytes(new Metadata(
                saved, assignments, autosaveSettings, worldId, generation)), overwrite: true);
            return new ManualSaveOverwriteReceipt(saved, backup.Id);
        }
    }

    private ManualWorldSave CreateCore(string name, PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments,
        WorldAutosaveSettings? autosaveSettings, bool isAutosave, RecoveryProvenance? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(assignments);
        name = NormalizeName(name);
        lock (gate)
        {
            var state = runtime.ExportState();
            if (!isAutosave && !state.Society.Society.IsPaused)
                throw new InvalidOperationException("Pause the world before making a manual save.");
            var worldId = state.Society.Society.WorldId;
            var timeline = ReadTimeline(worldId);
            var branch = ResolveBranch(timeline, List(worldId));
            var entry = new ManualWorldSave(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow,
                state.Society.Society.WorldTick, isAutosave, branch, timeline?.ContinuedFromId,
                timeline?.ContinuedFromCreatedUtc, NextBranchPosition(branch, timeline));
            Directory.CreateDirectory(directory);
            RestrictDirectory();
            var bytes = PrivateWorldRuntimeCodec.Encode(state);
            WriteAtomic(StatePath(entry.Id), bytes);
            // The branch record moves before the save is published. A failure in
            // between leaves a record pointing at an unlisted save, whose
            // position still keeps the next save on this branch.
            AdvanceTimeline(worldId, timeline, entry, bytes);
            WriteAtomic(MetadataPath(entry.Id), JsonSerializer.SerializeToUtf8Bytes(
                new Metadata(entry, assignments, autosaveSettings, worldId, Recovery: recovery)));
            return entry;
        }
    }

    /// <summary>
    /// Record that the running world now continues from one of its saves, as
    /// after loading it. The next save continues that save's branch only if the
    /// branch has nothing later; otherwise it starts a new branch.
    /// </summary>
    public SaveTimelineRestorePoint ContinueFrom(string id)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        lock (gate)
        {
            if (!File.Exists(MetadataPath(id)))
                throw new FileNotFoundException("The manual save does not exist.");
            var metadata = ReadMetadata(id);
            var worldId = metadata.WorldId ?? throw new InvalidDataException("The manual save has no world.");
            var path = TimelinePath(worldId);
            var restore = new SaveTimelineRestorePoint(worldId, File.Exists(path) ? File.ReadAllBytes(path) : null);
            var previous = ReadTimeline(worldId);
            var save = WithValidBranch(metadata.Save);
            var checkpointFingerprint = Fingerprint(File.ReadAllBytes(CommittedStatePath(id, metadata)));
            WriteTimeline(new Timeline(worldId, save.Branch, save.Id, save.Name, save.WorldTick,
                Math.Max(previous?.LastBranchNumber ?? 0, save.Branch?.Number ?? 0), save.CreatedUtc,
                save.BranchPosition, SavedCheckpointFingerprint: checkpointFingerprint));
            return restore;
        }
    }

    public void RestoreTimeline(SaveTimelineRestorePoint restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        lock (gate)
        {
            var path = TimelinePath(restore.WorldId);
            if (restore.Bytes is null) File.Delete(path);
            else WriteAtomic(path, restore.Bytes, overwrite: true);
        }
    }

    /// <summary>Remember the loaded state after the host has applied its required pause.</summary>
    public void RecordLoadedState(PrivateWorldRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (gate)
        {
            var state = runtime.ExportState();
            var timeline = ReadTimeline(state.Society.Society.WorldId)
                ?? throw new InvalidDataException("The loaded save has no branch record.");
            WriteTimeline(timeline with { LoadedStateFingerprint = Fingerprint(PrivateWorldRuntimeCodec.Encode(state)) });
        }
    }

    /// <summary>
    /// The save the running world continues from, when nothing has changed since:
    /// the same world state, model routing and autosave choices. Loading another
    /// save then needs no extra copy of the world being left.
    /// </summary>
    public ManualWorldSave? FindUnchangedSave(PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings? autosaveSettings)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(assignments);
        lock (gate)
        {
            var state = runtime.ExportState();
            var worldId = state.Society.Society.WorldId;
            var timeline = ReadTimeline(worldId);
            if (timeline?.ContinuedFromId is not { } id || !File.Exists(MetadataPath(id)))
                return null;
            try
            {
                var metadata = ReadMetadata(id);
                if (metadata.WorldId != worldId || metadata.Save.CreatedUtc != timeline.ContinuedFromCreatedUtc ||
                    !metadata.Assignments.SequenceEqual(assignments) ||
                    !SameAutosaveChoices(metadata.AutosaveSettings, autosaveSettings))
                    return null;
                var bytes = PrivateWorldRuntimeCodec.Encode(state);
                // A remembered runtime baseline cannot stand in for a missing
                // or damaged recovery checkpoint. Verify the stored bytes too.
                var checkpointBytes = File.ReadAllBytes(CommittedStatePath(id, metadata));
                var unchanged = timeline.SavedCheckpointFingerprint is { } checkpointFingerprint &&
                    timeline.LoadedStateFingerprint is { } fingerprint
                    ? checkpointFingerprint == Fingerprint(checkpointBytes) && fingerprint == Fingerprint(bytes)
                    : checkpointBytes.AsSpan().SequenceEqual(bytes);
                return unchanged ? metadata.Save : null;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Where the running world continues, for the Load Save timeline. Reading it
    /// changes nothing; the next save makes the same branch choice it reports.
    /// </summary>
    public SaveTimelinePosition CurrentPosition(string worldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        lock (gate)
        {
            var timeline = ReadTimeline(worldId);
            var worldSaves = List(worldId);
            var continuing = ContinuingBranch(timeline, worldSaves);
            return new SaveTimelinePosition(timeline?.ContinuedFromId, timeline?.Branch?.Id,
                continuing is null, continuing?.Number ?? NextBranchNumber(timeline, worldSaves),
                timeline?.ContinuedFromTick);
        }
    }

    private static bool SameAutosaveChoices(WorldAutosaveSettings? left, WorldAutosaveSettings? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.Enabled == right.Enabled && left.IntervalMinutes == right.IntervalMinutes &&
              left.RotationCount == right.RotationCount;

    private SaveBranch ResolveBranch(Timeline? timeline, IReadOnlyList<ManualWorldSave> worldSaves)
    {
        if (ContinuingBranch(timeline, worldSaves) is { } current)
            return current;
        var number = NextBranchNumber(timeline, worldSaves);
        var started = timeline?.ContinuedFromId is { } fromId
            ? new SaveBranch(Guid.NewGuid().ToString("N"), number, fromId, timeline.ContinuedFromName,
                timeline.ContinuedFromTick)
            : new SaveBranch(Guid.NewGuid().ToString("N"), number);
        if (logger is not null && started.StartedFromId is { } from)
            ManualWorldSaveTelemetry.BranchStarted(logger, number, from);
        return started;
    }

    // The running world's branch, when nothing later has been saved on it.
    private static SaveBranch? ContinuingBranch(Timeline? timeline, IReadOnlyList<ManualWorldSave> worldSaves) =>
        // Positions order snapshots even when the world tick stays unchanged or
        // an intermediate checkpoint has been deleted. Recovery copies retain
        // the original position and do not themselves advance the history.
        timeline?.Branch is { } current && !worldSaves.Any(save => save.Branch?.Id == current.Id &&
            save.BranchPosition > timeline.ContinuedFromBranchPosition)
            ? current
            : null;

    private static int NextBranchNumber(Timeline? timeline, IReadOnlyList<ManualWorldSave> worldSaves) =>
        Math.Max(timeline?.LastBranchNumber ?? 0,
            worldSaves.Select(save => save.Branch?.Number ?? 0).DefaultIfEmpty(0).Max()) + 1;

    private static long NextBranchPosition(SaveBranch branch, Timeline? timeline) =>
        branch.Id == timeline?.Branch?.Id ? checked(timeline.ContinuedFromBranchPosition + 1) : 1;

    private static string Fingerprint(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private void AdvanceTimeline(string worldId, Timeline? previous, ManualWorldSave saved, byte[] bytes)
    {
        var fingerprint = Fingerprint(bytes);
        WriteTimeline(new Timeline(worldId, saved.Branch, saved.Id, saved.Name, saved.WorldTick,
            Math.Max(previous?.LastBranchNumber ?? 0, saved.Branch?.Number ?? 0), saved.CreatedUtc,
            saved.BranchPosition, fingerprint, fingerprint));
    }

    private Timeline? ReadTimeline(string worldId)
    {
        var path = TimelinePath(worldId);
        if (!File.Exists(path)) return null;
        try
        {
            var timeline = JsonSerializer.Deserialize<Timeline>(File.ReadAllBytes(path));
            if (timeline is not null && timeline.WorldId == worldId && IsValidBranch(timeline.Branch) &&
                (timeline.ContinuedFromId is null || IsId(timeline.ContinuedFromId)) &&
                timeline.LastBranchNumber is >= 0 and < int.MaxValue &&
                (timeline.Branch is null ? timeline.ContinuedFromBranchPosition == 0
                    : timeline.ContinuedFromBranchPosition is > 0 and < long.MaxValue) &&
                IsValidFingerprint(timeline.LoadedStateFingerprint) && IsValidFingerprint(timeline.SavedCheckpointFingerprint))
                return timeline;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { }
        // A damaged record never blocks saving: the next save starts a new branch.
        if (logger is not null) ManualWorldSaveTelemetry.InvalidTimeline(logger);
        return null;
    }

    private void WriteTimeline(Timeline timeline)
    {
        Directory.CreateDirectory(directory);
        RestrictDirectory();
        WriteAtomic(TimelinePath(timeline.WorldId), JsonSerializer.SerializeToUtf8Bytes(timeline), overwrite: true);
    }

    private static bool IsValidFingerprint(string? fingerprint) => fingerprint is null ||
        fingerprint is { Length: 64 } && fingerprint.All(char.IsAsciiHexDigit);

    private string TimelinePath(string worldId) => Path.Combine(directory, "timeline-" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(worldId)))[..32] + ".json");

    private static bool IsValidBranch(SaveBranch? branch) => branch is null ||
        IsId(branch.Id) && branch.Number is >= 1 and < int.MaxValue &&
        (branch.StartedFromId is null || IsId(branch.StartedFromId)) &&
        (branch.StartedFromName is null || branch.StartedFromName.Length <= 80 &&
            !branch.StartedFromName.Any(char.IsControl)) &&
        branch.StartedFromTick is null or >= 0;

    // A damaged branch record only loses the save's branch; the save itself stays listed.
    private static ManualWorldSave WithValidBranch(ManualWorldSave save) =>
        IsValidBranch(save.Branch) && (save.Branch is null ? save.BranchPosition == 0
                : save.BranchPosition is > 0 and < long.MaxValue) &&
            (save.ContinuedFromId is null || IsId(save.ContinuedFromId))
            ? save : save with { Branch = null, ContinuedFromId = null, ContinuedFromCreatedUtc = null, BranchPosition = 0 };

    // The metadata rename is the durable point of deletion. A partial cleanup
    // stays hidden from List/Read and can be resumed without reviving the save.
    public void Delete(string id, string worldId, DateTimeOffset expectedCreatedUtc)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid save ID.", nameof(id));
        lock (gate)
        {
            var intent = Path.Combine(directory, id + ".deleting.json");
            var source = File.Exists(intent) ? intent : MetadataPath(id);
            var metadata = ReadDeletionMetadata(source, id);
            if (metadata.WorldId != worldId || metadata.Save.CreatedUtc != expectedCreatedUtc)
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
                var metadata = ReadDeletionMetadata(path, id);
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
            File.Delete(TimelinePath(worldId));
        }
    }

    public void RecoverDeletions()
    {
        lock (gate)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, "*.deleting.json"))
            {
                var id = Path.GetFileName(path)[..^".deleting.json".Length];
                var metadata = ReadDeletionMetadata(path, id);
                if (metadata.WorldId is null)
                    throw new InvalidDataException("The pending save deletion identity is invalid.");
                Delete(metadata.Save.Id, metadata.WorldId, metadata.Save.CreatedUtc);
            }
        }
    }

    private static Metadata ReadDeletionMetadata(string path, string id)
    {
        var metadata = DeserializeMetadata(File.ReadAllBytes(path));
        // Deletion needs a verified identity, not playable routing/settings.
        // Malformed records must enter the existing pending-cleanup path.
        if (!IsId(id) || metadata?.Save is null || metadata.Save.Id != id)
            throw new InvalidDataException("The save deletion identity is invalid.");
        return metadata;
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
                    var item = DeserializeMetadata(File.ReadAllBytes(path));
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
                    if (worldId is null || item.WorldId == worldId) entries.Add(WithValidBranch(item.Save));
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
            // Each branch keeps its own newest autosaves, so playing one branch
            // never retires another branch's history. Unknown histories stay
            // recoverable rather than being merged into the pre-branch group.
            var candidates = List(worldId).Where(item => item.IsAutosave && !ReadMetadata(item.Id).HasInvalidBranchMetadata)
                .GroupBy(item => item.Branch?.Id ?? LegacyBranchKey, StringComparer.Ordinal)
                .SelectMany(branch => branch.Where(item => item.Id != preserveId)
                    // Branch positions keep creation order when the clock moves backwards.
                    // Genuine pre-branch saves have position zero and keep their UTC ordering.
                    .OrderByDescending(item => item.BranchPosition)
                    .ThenByDescending(item => item.CreatedUtc)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Skip(branch.Any(item => item.Id == preserveId) ? count - 1 : count))
                .ToArray();
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
            metadata = DeserializeMetadata(File.ReadAllBytes(MetadataPath(id)));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The manual save metadata is invalid.", exception);
        }
        if (!IsValidMetadata(metadata, id))
            throw new InvalidDataException("The manual save metadata is invalid.");
        return metadata;
    }

    private static Metadata? DeserializeMetadata(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Save", out var save) ||
            save.ValueKind != JsonValueKind.Object)
            return JsonSerializer.Deserialize<Metadata>(bytes);

        // Parse required identity/checkpoint/routing fields strictly. Optional
        // branch fields cannot make an otherwise playable checkpoint disappear.
        using var core = new MemoryStream();
        using (var writer = new Utf8JsonWriter(core))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != "Save" || property.Value.ValueKind != JsonValueKind.Object)
                {
                    property.WriteTo(writer);
                    continue;
                }
                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                foreach (var field in property.Value.EnumerateObject())
                    if (field.Name is not ("Branch" or "ContinuedFromId" or "ContinuedFromCreatedUtc" or "BranchPosition"))
                        field.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        var metadata = JsonSerializer.Deserialize<Metadata>(core.ToArray());
        if (metadata?.Save is null) return metadata;
        try
        {
            var branch = JsonSerializer.Deserialize<BranchMetadata>(save.GetRawText());
            if (branch is not null)
            {
                var parsedSave = metadata.Save with
                {
                    Branch = branch.Branch,
                    ContinuedFromId = branch.ContinuedFromId,
                    ContinuedFromCreatedUtc = branch.ContinuedFromCreatedUtc,
                    BranchPosition = branch.BranchPosition
                };
                var validSave = WithValidBranch(parsedSave);
                return metadata with
                {
                    Save = validSave,
                    HasInvalidBranchMetadata = validSave != parsedSave
                };
            }
        }
        catch (JsonException) { }
        return metadata with { HasInvalidBranchMetadata = true };
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
