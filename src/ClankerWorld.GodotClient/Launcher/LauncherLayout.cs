namespace ClankerWorld.GodotClient.Launcher;

/// <summary>
/// Where the launcher keeps game versions and its own settings. Everything sits
/// under the same per-user folder the game's own host uses for saves, keys and
/// logs (<c>%LOCALAPPDATA%\ClankerWorld</c>), but in separate subfolders, so
/// installing, repairing or removing a version never touches player data.
/// </summary>
public sealed record LauncherLayout(string Root)
{
    public const string GameExecutable = "ClankerWorld.exe";
    public const string PackageManifest = "manifest.sha256";

    public static LauncherLayout ForUser(string localApplicationData) =>
        new(Path.Combine(localApplicationData, "ClankerWorld"));

    public string VersionsDirectory => Path.Combine(Root, "versions");
    public string LauncherDirectory => Path.Combine(Root, "launcher");
    public string DownloadsDirectory => Path.Combine(LauncherDirectory, "downloads");
    public string SettingsPath => Path.Combine(LauncherDirectory, "settings.json");

    /// <summary>The game's own data folders, written by its host (#1564).</summary>
    public string SavesDirectory => Path.Combine(Root, "saves");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string WorldCatalogPath => Path.Combine(SavesDirectory, "private-world.json.worlds", "catalog.json");

    public string VersionDirectory(GameVersionName version) => Path.Combine(VersionsDirectory, version.ToString());
}
