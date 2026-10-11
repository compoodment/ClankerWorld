using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

public sealed record RecoveryCleanupPreview(string WorldId, int KeepCount, string Digest,
    IReadOnlyList<ManualWorldSave> Remove, IReadOnlyList<ManualWorldSave> Keep);

public sealed partial class ManualWorldSaveStore
{
    // Only copies made explicitly as recovery checkpoints carry this provenance.
    // Names, age and legacy files never confer deletion authority.
    private sealed record RecoveryProvenance(string SourceSaveId, long Sequence);

    private RecoveryProvenance NextRecovery(string sourceId)
    {
        var latest = List().Select(save => ReadMetadata(save.Id).Recovery)
            .Where(item => item?.SourceSaveId == sourceId && item.Sequence > 0)
            .Select(item => item!.Sequence).DefaultIfEmpty(0).Max();
        return new RecoveryProvenance(sourceId, checked(latest + 1));
    }

    public ManualWorldSave CreateLoadRecovery(PrivateWorldRuntime runtime,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings? settings)
    {
        lock (gate)
        {
            var worldId = runtime.Society.WorldId;
            var source = ReadTimeline(worldId)?.ContinuedFromId;
            // Without a named source, retain the checkpoint as an ordinary save.
            var named = List(worldId).FirstOrDefault(save => save.Id == source && !save.IsAutosave &&
                ReadMetadata(save.Id).Recovery is null);
            return CreateCore("Before loading", runtime, assignments, settings, false,
                named is null ? null : NextRecovery(named.Id));
        }
    }

    public RecoveryCleanupPreview PreviewRecoveryCleanup(string worldId, int keepCount, PrivateWorldStateFile stateFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        ArgumentNullException.ThrowIfNull(stateFile);
        if (keepCount is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(keepCount));
        lock (gate)
        {
            List<ManualWorldSave> keep = [];
            List<(ManualWorldSave Save, RecoveryProvenance Provenance)> verified = [];
            List<string> inventory = [worldId, keepCount.ToString(CultureInfo.InvariantCulture)];
            var timeline = ReadTimeline(worldId);
            var timelinePath = TimelinePath(worldId);
            if (timeline is null && File.Exists(timelinePath))
                throw new InvalidDataException("Recovery cleanup cannot verify the active save's timeline.");
            if (File.Exists(timelinePath)) inventory.Add(Fingerprint(File.ReadAllBytes(timelinePath)));
            foreach (var save in List(worldId).OrderBy(save => save.Id, StringComparer.Ordinal))
            {
                var metadata = ReadMetadata(save.Id);
                inventory.Add(save.Id + ":" + Fingerprint(File.ReadAllBytes(MetadataPath(save.Id))));
                var provenance = metadata.Recovery;
                if (provenance is null || save.IsAutosave || !IsId(provenance.SourceSaveId) || provenance.Sequence <= 0)
                {
                    keep.Add(save);
                    continue;
                }
                try
                {
                    var bytes = File.ReadAllBytes(CommittedStatePath(save.Id, metadata));
                    inventory.Add(save.Id + ":" + Fingerprint(bytes));
                    var checkpoint = PrivateWorldRuntimeCodec.Decode(bytes);
                    if (checkpoint.Society.Society.WorldId != worldId)
                        throw new InvalidDataException("Recovery identity does not match.");
                    stateFile.VerifyRequiredHistory(checkpoint);
                    using var restored = PrivateWorldRuntime.Restore(checkpoint);
                    verified.Add((save, provenance));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
                {
                    // Unverifiable checkpoints remain available for recovery.
                    inventory.Add(save.Id + ":unverified");
                    keep.Add(save);
                }
            }
            List<ManualWorldSave> remove = [];
            foreach (var group in verified.GroupBy(item => item.Provenance.SourceSaveId, StringComparer.Ordinal))
            {
                var ordered = group.OrderByDescending(item => item.Provenance.Sequence)
                    .ThenBy(item => item.Save.Id, StringComparer.Ordinal).ToArray();
                // At least one verified recovery survives for every source, even
                // when another (newer) copy is damaged or belongs to another schema.
                var retained = ordered.Take(keepCount).Select(item => item.Save.Id)
                    .ToHashSet(StringComparer.Ordinal);
                if (timeline?.ContinuedFromId is { } activeId) retained.Add(activeId);
                keep.AddRange(ordered.Where(item => retained.Contains(item.Save.Id)).Select(item => item.Save));
                remove.AddRange(ordered.Where(item => !retained.Contains(item.Save.Id)).Select(item => item.Save));
            }
            var kept = keep.OrderBy(save => save.Id, StringComparer.Ordinal).ToArray();
            var removed = remove.OrderBy(save => save.Id, StringComparer.Ordinal).ToArray();
            inventory.AddRange(kept.Select(save => "keep:" + save.Id));
            inventory.AddRange(removed.Select(save => "remove:" + save.Id));
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', inventory))));
            return new RecoveryCleanupPreview(worldId, keepCount, digest, removed, kept);
        }
    }

    public IReadOnlyList<string> CleanRecoveryHistory(string worldId, int keepCount, string expectedDigest,
        PrivateWorldStateFile stateFile)
    {
        lock (gate)
        {
            var preview = PreviewRecoveryCleanup(worldId, keepCount, stateFile);
            if (!string.Equals(preview.Digest, expectedDigest, StringComparison.Ordinal))
                throw new InvalidOperationException("Recovery history changed. Preview it again before deleting.");
            foreach (var save in preview.Remove) Delete(save.Id, worldId, save.CreatedUtc);
            return preview.Remove.Select(save => save.Id).ToArray();
        }
    }
}
