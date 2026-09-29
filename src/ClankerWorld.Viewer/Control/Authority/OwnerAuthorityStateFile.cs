using System.Text.Json;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Stores only the non-secret server half of owner-device authority state.
/// The state file is intentionally separate from world snapshots: paired
/// public keys and anti-replay hashes are operational security material, not
/// simulation state and not observation data.
/// </summary>
public sealed class OwnerAuthorityStateFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly object gate = new();
    private string? lastSavedJson;
    private long writeCount;

    public OwnerAuthorityStateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }
    public long WriteCount => Interlocked.Read(ref writeCount);

    public OwnerAuthorityStore LoadOrCreate(
        OwnerAuthorityIdentity initialIdentity,
        IOwnerAuthorityClock? clock = null,
        IOwnerAuthorityRandom? random = null,
        bool allowWorldSwitch = false)
    {
        ArgumentNullException.ThrowIfNull(initialIdentity);
        lock (gate)
        {
            if (!File.Exists(Path))
            {
                var created = new OwnerAuthorityStore(
                    initialIdentity,
                    clock ?? SystemOwnerAuthorityClock.Instance,
                    random ?? CryptographicOwnerAuthorityRandom.Instance);
                SaveUnsafe(created.ExportState());
                return created;
            }

            var json = File.ReadAllText(Path);
            var state = JsonSerializer.Deserialize<OwnerAuthorityState>(json, JsonOptions) ??
                throw new InvalidDataException("The owner-authority state file is empty.");
            if (state.Authority is null ||
                !string.Equals(initialIdentity.ServerAuthorityId, state.Authority.ServerAuthorityId, StringComparison.Ordinal) ||
                (!allowWorldSwitch && !string.Equals(initialIdentity.WorldId, state.Authority.WorldId, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("The owner-authority state belongs to another server authority or world.");
            }

            var restored = OwnerAuthorityStore.Restore(
                state with { Challenges = [] },
                clock ?? SystemOwnerAuthorityClock.Instance,
                random ?? CryptographicOwnerAuthorityRandom.Instance);
            // Challenges belong to one host process. Restart rejects every old
            // nonce, including pending ones, so no consumed nonce can resurrect.
            // Preserve existing file bytes until durable pairing/device state changes.
            lastSavedJson = SerializeDurable(restored.ExportState());
            return restored;
        }
    }

    public void Save(OwnerAuthorityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        lock (gate)
        {
            SaveUnsafe(store.ExportState());
        }
    }

    private void SaveUnsafe(OwnerAuthorityState state)
    {
        var json = SerializeDurable(state);
        if (string.Equals(lastSavedJson, json, StringComparison.Ordinal))
        {
            return;
        }
        var directory = System.IO.Path.GetDirectoryName(Path) ??
            throw new InvalidOperationException("The owner-authority state path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            RestrictPermissions(temporaryPath);
            File.Move(temporaryPath, Path, overwrite: true);
            RestrictPermissions(Path);
            lastSavedJson = json;
            Interlocked.Increment(ref writeCount);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string SerializeDurable(OwnerAuthorityState state) =>
        JsonSerializer.Serialize(state with { Challenges = [] }, JsonOptions);

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
