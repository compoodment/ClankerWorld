using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Atomic durable storage for the Phase 2 composite runtime. World state is
/// intentionally separate from paired-device authority state: a simulation
/// recovery must not accidentally turn a world save into credential storage.
/// </summary>
public sealed class OwnerWorldStateFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly object gate = new();
    private readonly IOwnerApprovedAssetReferencePolicy approvedAssetReferencePolicy;
    private readonly IDecisionProvider decisionProvider;

    public OwnerWorldStateFile(
        string path,
        IOwnerApprovedAssetReferencePolicy? approvedAssetReferencePolicy = null,
        IDecisionProvider? decisionProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        // A state file is never an authority to trust an asset reference. The
        // current host catalog must approve every restored reference as the
        // runtime replays its authored history.
        this.approvedAssetReferencePolicy = approvedAssetReferencePolicy ??
            DenyAllApprovedAssetReferencePolicy.Instance;
        this.decisionProvider = decisionProvider ?? new DeterministicDecisionProvider();
    }

    public string Path { get; }

    /// <summary>
    /// Loads the only saved world for this host or writes a fresh deterministic
    /// genesis state. A configured seed is an identity check, not a request to
    /// silently replace an existing world.
    /// </summary>
    public OwnerWorldRuntime LoadOrCreate(string worldSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        lock (gate)
        {
            if (!File.Exists(Path))
            {
                var created = new OwnerWorldRuntime(worldSeed, approvedAssetReferencePolicy, decisionProvider);
                SaveUnsafe(created.ExportState());
                return created;
            }

            var json = File.ReadAllText(Path);
            var state = JsonSerializer.Deserialize<OwnerWorldRuntimeState>(json, JsonOptions) ??
                throw new InvalidDataException("The Phase 2 runtime state file is empty.");
            var restored = OwnerWorldRuntime.Restore(
                state,
                worldSeed,
                approvedAssetReferencePolicy,
                decisionProvider);
            // Rewrite the validated canonical projection so later failures are
            // not hidden behind a stale representation.
            SaveUnsafe(restored.ExportState());
            return restored;
        }
    }

    public void Save(OwnerWorldRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (gate)
        {
            SaveUnsafe(runtime.ExportState());
        }
    }

    private void SaveUnsafe(OwnerWorldRuntimeState state)
    {
        var directory = System.IO.Path.GetDirectoryName(Path) ??
            throw new InvalidOperationException("The Phase 2 runtime state path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
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
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
