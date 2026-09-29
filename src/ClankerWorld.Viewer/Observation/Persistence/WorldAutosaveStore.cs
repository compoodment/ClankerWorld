using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

public sealed record WorldAutosaveSettings(
    string WorldId, bool Enabled, int IntervalMinutes, int RotationCount,
    DateTimeOffset LastSavedUtc, long LastWorldTick);

/// <summary>
/// Per-world autosave schedule. The per-tick active checkpoint remains crash
/// recovery; this schedule creates player-loadable rotating snapshots.
/// </summary>
public sealed class WorldAutosaveStore
{
    private readonly object gate = new();
    private readonly string path;
    private WorldAutosaveSettings state;

    public WorldAutosaveStore(string activeSavePath, string worldId, bool allowWorldSwitch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeSavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        path = Path.GetFullPath(activeSavePath) + ".autosave.json";
        if (File.Exists(path))
        {
            state = JsonSerializer.Deserialize<WorldAutosaveSettings>(File.ReadAllBytes(path))
                ?? throw new InvalidDataException("Autosave settings are empty.");
            if (state.WorldId != worldId)
            {
                if (!allowWorldSwitch)
                    throw new InvalidDataException("Autosave settings belong to another world.");
                state = new WorldAutosaveSettings(worldId, true, 5, 5, DateTimeOffset.UtcNow, -1);
                Save(state);
            }
            Validate(state.IntervalMinutes, state.RotationCount);
            RestrictPermissions(path);
        }
        else
        {
            state = new WorldAutosaveSettings(worldId, true, 5, 5, DateTimeOffset.UtcNow, -1);
            Save(state);
        }
    }

    public WorldAutosaveSettings Capture()
    {
        lock (gate) return state;
    }

    public WorldAutosaveSettings Configure(bool enabled, int intervalMinutes, int rotationCount)
    {
        Validate(intervalMinutes, rotationCount);
        lock (gate)
        {
            var next = state with
            {
                Enabled = enabled,
                IntervalMinutes = intervalMinutes,
                RotationCount = rotationCount,
                LastSavedUtc = DateTimeOffset.UtcNow,
            };
            Save(next);
            state = next;
            return next;
        }
    }

    public void RestoreFromCheckpoint(WorldAutosaveSettings checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Validate(checkpoint.IntervalMinutes, checkpoint.RotationCount);
        lock (gate)
        {
            if (checkpoint.WorldId != state.WorldId)
                throw new InvalidDataException("Autosave settings belong to another world.");
            Save(checkpoint);
            state = checkpoint;
        }
    }

    public WorldAutosaveSettings SelectWorld(string worldId, WorldAutosaveSettings? previous)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        lock (gate)
        {
            if (previous is not null && previous.WorldId != worldId)
                throw new InvalidDataException("Autosave settings belong to another world.");
            var next = previous ?? new WorldAutosaveSettings(worldId, true, 5, 5,
                DateTimeOffset.UtcNow, -1);
            Validate(next.IntervalMinutes, next.RotationCount);
            Save(next);
            state = next;
            return next;
        }
    }

    public ManualWorldSave? MaybeSave(DateTimeOffset now, PrivateWorldRuntime runtime,
        ProviderConfigurationStore providers, ManualWorldSaveStore saves)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(saves);
        lock (gate)
        {
            if (!state.Enabled || now - state.LastSavedUtc < TimeSpan.FromMinutes(state.IntervalMinutes) ||
                runtime.WorldTick == state.LastWorldTick)
                return null;
            if (runtime.Society.WorldId != state.WorldId)
                throw new InvalidDataException("Autosave settings belong to another world.");
            var next = state with { LastSavedUtc = now, LastWorldTick = runtime.WorldTick };
            var saved = saves.CreateAutosave(runtime,
                providers.CaptureRuntimeConfiguration().Assignments ?? [], next);
            Save(next);
            state = next;
            saves.KeepNewestAutosaves(Math.Max(1, state.RotationCount), saved.Id, state.WorldId);
            return saved;
        }
    }

    public static void Validate(int intervalMinutes, int rotationCount)
    {
        if (intervalMinutes is not (1 or 2 or 5 or 10 or 15 or 30))
            throw new ArgumentException("Autosave interval must be 1, 2, 5, 10, 15, or 30 minutes.");
        if (rotationCount is not (0 or 3 or 5 or 10))
            throw new ArgumentException("Autosave rotation must be off, 3, 5, or 10.");
    }

    private void Save(WorldAutosaveSettings next)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(next));
                stream.Flush(flushToDisk: true);
            }
            RestrictPermissions(temporary);
            File.Move(temporary, path, overwrite: true);
            RestrictPermissions(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RestrictPermissions(string filename)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(filename, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
