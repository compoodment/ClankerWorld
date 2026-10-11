using System.Diagnostics;
using System.Text.Json;

namespace ClankerWorld.GodotClient.Launcher;

/// <summary>The launcher's own choices. Developer mode is off until the player turns it on.</summary>
public sealed record LauncherSettings(bool DeveloperMode = false, string? LastPlayedVersion = null)
{
    public static LauncherSettings Load(LauncherLayout layout)
    {
        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllBytes(layout.SettingsPath)) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(LauncherLayout layout)
    {
        Directory.CreateDirectory(layout.LauncherDirectory);
        var temporary = layout.SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this));
        File.Move(temporary, layout.SettingsPath, overwrite: true);
    }
}

/// <summary>A world in the player's saves and the version that last saved it, if known.</summary>
public sealed record SavedWorld(string Name, GameVersionName? SavedBy);

/// <summary>
/// Reads the game's world list without changing it, so the Versions page can
/// say which worlds still need a version. It never writes to the saves folder.
/// </summary>
public static class SavedWorlds
{
    public static IReadOnlyList<SavedWorld> Read(LauncherLayout layout)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(layout.WorldCatalogPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Worlds", out var worlds) || worlds.ValueKind != JsonValueKind.Array)
                return [];
            return worlds.EnumerateArray()
                .Where(world => world.ValueKind == JsonValueKind.Object)
                .Select(world => new SavedWorld(
                    world.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "World",
                    world.TryGetProperty("GameVersion", out var version) && version.ValueKind == JsonValueKind.String
                        ? GameVersionName.Parse(version.GetString()) : null))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}

/// <summary>Starts an installed game version.</summary>
public static class GameStarter
{
    /// <summary>
    /// Start options the game reads: which launcher started it, so it can
    /// offer Quit to Launcher, and whether Developer mode is on.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string launcherExecutable, bool developerMode) =>
        developerMode
            ? ["--", "--launcher=" + launcherExecutable, "--developer-mode"]
            : ["--", "--launcher=" + launcherExecutable];

    public static Process Start(InstalledVersion version, string launcherExecutable, bool developerMode)
    {
        var start = new ProcessStartInfo(version.Executable)
        {
            WorkingDirectory = version.Directory,
            UseShellExecute = false,
        };
        foreach (var argument in Arguments(launcherExecutable, developerMode)) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new GameInstallException($"ClankerWorld {version.Version} didn't start.");
    }
}
