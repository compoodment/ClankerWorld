using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

public sealed record CatalogWorld(
    string Id, string Name, string WorldId, string Seed, DateTimeOffset UpdatedUtc,
    IReadOnlyList<InhabitantProviderAssignment> Assignments,
    WorldAutosaveSettings? AutosaveSettings,
    string Compatibility = "unknown", string? CompatibilityReason = null);

public sealed record WorldCatalogSnapshot(string ActiveId, IReadOnlyList<CatalogWorld> Worlds);

/// <summary>Private installation-level index of independently selectable worlds.</summary>
public sealed class WorldCatalogStore
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly string indexPath;
    private WorldCatalogSnapshot index;
    public bool RecoveredSelection { get; }

    public WorldCatalogStore(string activeSavePath, PrivateWorldRuntimeState activeState,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings autosaveSettings)
    {
        directory = Path.GetFullPath(activeSavePath) + ".worlds";
        indexPath = Path.Combine(directory, "catalog.json");
        if (File.Exists(indexPath))
        {
            index = JsonSerializer.Deserialize<WorldCatalogSnapshot>(File.ReadAllBytes(indexPath))
                ?? throw new InvalidDataException("The world catalog is empty.");
            Validate(index);
            var matching = index.Worlds.SingleOrDefault(world => world.WorldId == activeState.Society.Society.WorldId);
            if (matching is null)
                throw new InvalidDataException("The active save is not in the world catalog.");
            // A crash between the atomic active checkpoint and the index update
            // can leave the index one selection behind. The checkpoint wins.
            if (index.ActiveId != matching.Id)
            {
                RecoveredSelection = true;
                index = index with { ActiveId = matching.Id };
                WriteIndex(index);
            }
        }
        else
        {
            var entry = new CatalogWorld(Guid.NewGuid().ToString("N"), "First World",
                activeState.Society.Society.WorldId, activeState.WorldSeed, DateTimeOffset.UtcNow,
                assignments.ToArray(), autosaveSettings);
            index = new WorldCatalogSnapshot(entry.Id, [entry]);
            WriteSnapshot(entry.Id, activeState);
            WriteIndex(index);
        }
    }

    public WorldCatalogSnapshot Capture()
    {
        lock (gate) return index;
    }

    public CatalogWorld Active()
    {
        lock (gate) return index.Worlds.Single(world => world.Id == index.ActiveId);
    }

    public CatalogWorld Add(string name, PrivateWorldRuntimeState state)
    {
        name = NormalizeName(name);
        lock (gate)
        {
            if (index.Worlds.Any(world => world.WorldId == state.Society.Society.WorldId))
                throw new InvalidOperationException("This world already exists.");
            var entry = new CatalogWorld(Guid.NewGuid().ToString("N"), name,
                state.Society.Society.WorldId, state.WorldSeed, DateTimeOffset.UtcNow, [], null);
            WriteSnapshot(entry.Id, state);
            WriteIndex(index with { Worlds = [.. index.Worlds, entry] });
            index = index with { Worlds = [.. index.Worlds, entry] };
            return entry;
        }
    }

    public void ArchiveActive(PrivateWorldRuntimeState state,
        IReadOnlyList<InhabitantProviderAssignment> assignments, WorldAutosaveSettings autosaveSettings)
    {
        lock (gate)
        {
            var active = index.Worlds.Single(world => world.Id == index.ActiveId);
            if (active.WorldId != state.Society.Society.WorldId)
                throw new InvalidDataException("The active world changed outside the catalog.");
            var updated = active with
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Assignments = assignments.ToArray(),
                AutosaveSettings = autosaveSettings,
            };
            WriteSnapshot(active.Id, state);
            var next = index with { Worlds = index.Worlds.Select(world => world.Id == active.Id ? updated : world).ToArray() };
            WriteIndex(next);
            index = next;
        }
    }

    public PrivateWorldRuntimeState Read(string id)
    {
        lock (gate)
        {
            var entry = index.Worlds.SingleOrDefault(world => world.Id == id)
                ?? throw new FileNotFoundException("The selected world does not exist.");
            var state = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(SnapshotPath(id)));
            if (state.Society.Society.WorldId != entry.WorldId || state.WorldSeed != entry.Seed)
                throw new InvalidDataException("The selected world checkpoint does not match its catalog entry.");
            return state;
        }
    }

    public void Select(string id)
    {
        lock (gate)
        {
            if (!index.Worlds.Any(world => world.Id == id))
                throw new FileNotFoundException("The selected world does not exist.");
            var next = index with { ActiveId = id };
            WriteIndex(next);
            index = next;
        }
    }

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("World name must be 1–80 printable characters.", nameof(value));
        return name;
    }

    private static void Validate(WorldCatalogSnapshot state)
    {
        if (state.Worlds.Count == 0 || !state.Worlds.Any(world => world.Id == state.ActiveId) ||
            state.Worlds.Select(world => world.Id).Distinct(StringComparer.Ordinal).Count() != state.Worlds.Count ||
            state.Worlds.Select(world => world.WorldId).Distinct(StringComparer.Ordinal).Count() != state.Worlds.Count)
            throw new InvalidDataException("The world catalog index is invalid.");
        foreach (var world in state.Worlds)
        {
            if (world.Id.Length != 32 || !world.Id.All(char.IsAsciiHexDigit) ||
                string.IsNullOrWhiteSpace(world.WorldId) || string.IsNullOrWhiteSpace(world.Seed) ||
                world.Assignments is null)
                throw new InvalidDataException("A world catalog entry is invalid.");
        }
    }

    private string SnapshotPath(string id)
    {
        if (id.Length != 32 || !id.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Invalid world ID.", nameof(id));
        return Path.Combine(directory, id + ".save");
    }

    private void WriteSnapshot(string id, PrivateWorldRuntimeState state) =>
        WriteAtomic(SnapshotPath(id), PrivateWorldRuntimeCodec.Encode(state));

    private void WriteIndex(WorldCatalogSnapshot next) =>
        WriteAtomic(indexPath, JsonSerializer.SerializeToUtf8Bytes(next));

    private void WriteAtomic(string destination, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
