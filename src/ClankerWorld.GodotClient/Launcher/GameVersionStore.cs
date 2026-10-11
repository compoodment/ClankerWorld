using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClankerWorld.GodotClient.Launcher;

/// <summary>An installed game version, with its folder's size on disk.</summary>
public sealed record InstalledVersion(GameVersionName Version, string Directory, long SizeBytes, string? SourceRevision)
{
    public string Executable => Path.Combine(Directory, LauncherLayout.GameExecutable);
}

/// <summary>Why an install or repair stopped. Nothing is half-installed after any of these.</summary>
public sealed class GameInstallException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Installs, checks, repairs and removes game versions in the launcher's
/// versions folder. A download is checked against the release's published
/// SHA-256 before anything is unpacked or run. A version's own
/// <c>manifest.sha256</c> lets Repair find files that changed since.
/// Nothing here ever touches the saves, logs or settings folders.
/// </summary>
public sealed class GameVersionStore(LauncherLayout layout, HttpClient http)
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;

    public IReadOnlyList<InstalledVersion> Installed()
    {
        if (!Directory.Exists(layout.VersionsDirectory)) return [];
        var installed = new List<InstalledVersion>();
        foreach (var directory in Directory.EnumerateDirectories(layout.VersionsDirectory))
        {
            var version = GameVersionName.Parse(Path.GetFileName(directory));
            if (version is null || !File.Exists(Path.Combine(directory, LauncherLayout.GameExecutable)) ||
                !File.Exists(Path.Combine(directory, LauncherLayout.PackageManifest)))
                continue;
            installed.Add(new InstalledVersion(version, directory, SizeOf(directory), ReadSourceRevision(directory)));
        }
        installed.Sort((a, b) => b.Version.CompareTo(a.Version));
        return installed;
    }

    /// <summary>Files that are missing or differ from the version's own manifest.</summary>
    public static IReadOnlyList<string> Verify(InstalledVersion version)
    {
        var problems = new List<string>();
        (string Relative, string Hash)[] manifest;
        try
        {
            manifest = ReadManifest(Path.Combine(version.Directory, LauncherLayout.PackageManifest)).ToArray();
            var names = manifest.Select(item => item.Relative).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(LauncherLayout.GameExecutable) ||
                !names.Contains(Path.Combine("host", "ClankerWorld.Viewer.exe")))
                return [LauncherLayout.PackageManifest];
            foreach (var path in Directory.EnumerateFiles(version.Directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(version.Directory, path);
                if (relative != LauncherLayout.PackageManifest && !names.Contains(relative)) problems.Add(relative);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return [LauncherLayout.PackageManifest];
        }
        foreach (var (relative, expected) in manifest)
        {
            var path = Path.Combine(version.Directory, relative);
            try
            {
                if (!File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    problems.Add(relative);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add(relative);
            }
        }
        return problems;
    }

    /// <summary>
    /// Downloads, checks and installs a release, replacing any copy already
    /// installed (which is how Repair works). The old copy is kept until the
    /// new one is fully in place.
    /// </summary>
    public async Task<InstalledVersion> InstallAsync(GameRelease release, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!GameReleaseFeed.IsTrustedDownload(release.Package.DownloadUrl) ||
            !GameReleaseFeed.IsTrustedDownload(release.PackageHash.DownloadUrl))
            throw new GameInstallException("This download doesn't come from ClankerWorld's releases, so it wasn't installed.");
        if (release.Package.Size > MaximumPackageBytes)
            throw new GameInstallException("This download is larger than any ClankerWorld version, so it wasn't installed.");
        Directory.CreateDirectory(layout.DownloadsDirectory);
        var expected = ParseHashFile(await http.GetStringAsync(release.PackageHash.DownloadUrl, cancellationToken),
            release.Package.Name);
        var zip = Path.Combine(layout.DownloadsDirectory, release.Package.Name + ".part");
        try
        {
            var actual = await DownloadAsync(release.Package, zip, progress, cancellationToken);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new GameInstallException(
                    "The download didn't match the published file, so it wasn't installed. Your saves are untouched. Try again.");
            return InstallPackage(zip, release.Version);
        }
        finally { TryDelete(zip); }
    }

    /// <summary>Unpacks an already checked package into its version folder.</summary>
    public InstalledVersion InstallPackage(string zipPath, GameVersionName version)
    {
        Directory.CreateDirectory(layout.VersionsDirectory);
        var staging = Path.Combine(layout.VersionsDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        var destination = layout.VersionDirectory(version);
        var retired = Path.Combine(layout.VersionsDirectory, $".old-{version}-{Guid.NewGuid():N}");
        try
        {
            Extract(zipPath, GameReleaseFeed.PackageName(version) + "/", staging);
            if (!File.Exists(Path.Combine(staging, LauncherLayout.GameExecutable)))
                throw new GameInstallException("This download isn't a ClankerWorld game package, so it wasn't installed.");
            var staged = new InstalledVersion(version, staging, 0, null);
            if (Verify(staged).Count > 0)
                throw new GameInstallException("This download's files don't match its own list, so it wasn't installed.");
            try
            {
                if (Directory.Exists(destination)) Directory.Move(destination, retired);
                Directory.Move(staging, destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (!Directory.Exists(destination) && Directory.Exists(retired)) Directory.Move(retired, destination);
                throw new GameInstallException($"Close ClankerWorld {version} first, then try again.", exception);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new GameInstallException("This download is damaged, so it wasn't installed. Try again.", exception);
        }
        finally
        {
            TryDeleteDirectory(staging);
            // A failed rollback must leave its only usable copy for startup recovery.
            if (Directory.Exists(destination)) TryDeleteDirectory(retired);
        }
        return Installed().Single(installed => installed.Version == version);
    }

    /// <summary>Removes one version's program files. Worlds, keys and logs stay where they are.</summary>
    public void Remove(InstalledVersion version)
    {
        var expected = Path.GetFullPath(layout.VersionDirectory(version.Version));
        if (!string.Equals(Path.GetFullPath(version.Directory), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Only folders in the versions folder can be removed.");
        var retired = Path.Combine(layout.VersionsDirectory, $".removed-{version.Version}-{Guid.NewGuid():N}");
        try { Directory.Move(expected, retired); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new GameInstallException($"Close ClankerWorld {version.Version} first, then try again.", exception);
        }
        TryDeleteDirectory(retired);
    }

    /// <summary>Clears leftovers from an install or removal that was interrupted.</summary>
    public void CleanUp()
    {
        if (Directory.Exists(layout.VersionsDirectory))
            foreach (var directory in Directory.EnumerateDirectories(layout.VersionsDirectory).ToArray())
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".staging-", StringComparison.Ordinal) || RetiredVersion(name, ".removed-") is not null)
                    TryDeleteDirectory(directory);
                else if (RetiredVersion(name, ".old-") is { } version)
                {
                    var destination = layout.VersionDirectory(version);
                    try
                    {
                        if (!Directory.Exists(destination)) Directory.Move(directory, destination);
                        else TryDeleteDirectory(directory);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Keep the backup if it cannot yet be restored (for example, while in use).
                    }
                }
            }
        if (Directory.Exists(layout.DownloadsDirectory))
            foreach (var file in Directory.EnumerateFiles(layout.DownloadsDirectory, "*.part"))
                TryDelete(file);
    }

    public static string ParseHashFile(string text, string fileName)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 66) continue;
            var hash = trimmed[..64];
            var name = trimmed[64..].TrimStart(' ', '*');
            if (name == fileName && hash.All(Uri.IsHexDigit)) return hash;
        }
        throw new GameInstallException("This release has no checksum for its download, so it wasn't installed.");
    }

    private static GameVersionName? RetiredVersion(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length + 33 &&
        name[^33] == '-' && Guid.TryParseExact(name[^32..], "N", out _)
            ? GameVersionName.Parse(name[prefix.Length..^33]) : null;

    public static IEnumerable<(string Relative, string Hash)> ReadManifest(string manifestPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(manifestPath))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length < 67 || line[64] != ' ' || line[65] is not (' ' or '*'))
                throw new InvalidDataException("The version's file list is invalid.");
            var hash = line[..64];
            var relative = line[66..];
            if (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
            if (!hash.All(Uri.IsHexDigit) || !IsSafeRelativePath(relative) || !names.Add(relative))
                throw new InvalidDataException("The version's file list is invalid.");
            yield return (relative.Replace('/', Path.DirectorySeparatorChar), hash);
        }
    }

    private async Task<string> DownloadAsync(ReleaseAsset asset, string path, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > MaximumPackageBytes || total > asset.Size)
                    throw new GameInstallException("The download was larger than the published file, so it wasn't installed.");
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                progress?.Report((double)total / asset.Size);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Extract(string zipPath, string prefix, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("The package has files outside its version folder.");
            var relative = name[prefix.Length..];
            if (relative.Length == 0 || relative.EndsWith('/')) continue;
            if (!IsSafeRelativePath(relative)) throw new InvalidDataException("The package has an unsafe file name.");
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException("The package has an unsafe file name.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static bool IsSafeRelativePath(string relative) =>
        relative.Length is > 0 and < 260 && !relative.StartsWith('/') && !relative.Contains(':', StringComparison.Ordinal) &&
        !relative.Contains('\\', StringComparison.Ordinal) &&
        relative.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    private static string? ReadSourceRevision(string directory)
    {
        const string prefix = "# Source commit: ";
        try
        {
            return File.ReadLines(Path.Combine(directory, LauncherLayout.PackageManifest)).Take(4)
                .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();
        }
        catch (IOException) { return null; }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static long SizeOf(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
