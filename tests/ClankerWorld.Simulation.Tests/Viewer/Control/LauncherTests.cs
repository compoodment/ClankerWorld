using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.GodotClient.Launcher;

namespace ClankerWorld.Simulation.Tests;

public sealed class LauncherTests
{
    private const string Download = "https://github.com/ClankerWorldOrg/ClankerWorld/releases/download/";
    private const string Page = "https://github.com/ClankerWorldOrg/ClankerWorld/releases/tag/";

    [Fact]
    public void VersionsSortNewestReleaseFirstAndOnlySafeNamesParse()
    {
        string[] ordered = ["0.1.0-alpha.1", "0.1.0-alpha.2", "0.1.0-alpha.10", "0.1.0-beta", "0.1.0", "0.2.0"];
        var parsed = ordered.Select(text => GameVersionName.Parse(text)!).ToArray();
        Assert.Equal(ordered, parsed.Reverse().OrderByDescending(version => version).Reverse().Select(version => version.ToString()));
        foreach (var unsafeName in new[] { "../0.1.0", "0.1", "v0.1.0", "0.1.0-", "0.1.0-a/b", "01.0.0", "0.1.0 " })
            Assert.Null(GameVersionName.Parse(unsafeName));
    }

    [Fact]
    public void TheFeedKeepsCompleteGameReleasesAndTheNewestLauncher()
    {
        var feed = GameReleaseFeed.Read(
        [
            Release("v0.1.0-alpha.1"),
            Release("v0.1.0-alpha.2"),
            Release("v0.1.0-alpha.3") with { Draft = true },
            Release("v0.1.0-alpha.4") with { Assets = Release("v0.1.0-alpha.4").Assets!.Take(1).ToArray() },
            Release("v0.1.0-alpha.5") with
            {
                Assets = Release("v0.1.0-alpha.5").Assets!.Select(asset => asset with
                {
                    BrowserDownloadUrl = asset.BrowserDownloadUrl!.Replace("github.com", "example.com", StringComparison.Ordinal),
                }).ToArray(),
            },
            new GitHubRelease("launcher-v1.0.0", Page + "launcher-v1.0.0", false, false, []),
            new GitHubRelease("launcher-v1.2.0", Page + "launcher-v1.2.0", false, false, []),
        ]);

        Assert.Equal(["0.1.0-alpha.2", "0.1.0-alpha.1"], feed.Games.Select(release => release.Version.ToString()));
        Assert.Equal("1.2.0", feed.NewestLauncher?.Version.ToString());
    }

    [Fact]
    public async Task InstallChecksTheDownloadAndRepairAndRemoveNeverTouchSaves()
    {
        var root = Directory.CreateTempSubdirectory("launcher-install-");
        try
        {
            var layout = new LauncherLayout(root.FullName);
            Directory.CreateDirectory(layout.SavesDirectory);
            var save = Path.Combine(layout.SavesDirectory, "private-world.json");
            File.WriteAllText(save, "world");
            var version = GameVersionName.Parse("0.1.0-alpha.1")!;
            var package = Package(version, ("ClankerWorld.exe", "game"), ("host/ClankerWorld.Viewer.exe", "host"));
            var server = new FakeReleases();
            var release = server.Publish(version, package);
            using var http = new HttpClient(server);
            var store = new GameVersionStore(layout, http);

            // A download that doesn't match its published hash is never unpacked.
            server.Tamper = true;
            var refused = await Assert.ThrowsAsync<GameInstallException>(() => store.InstallAsync(release, null, CancellationToken.None));
            Assert.Contains("didn't match the published file", refused.Message, StringComparison.Ordinal);
            Assert.Empty(store.Installed());
            server.Tamper = false;

            var installed = await store.InstallAsync(release, null, CancellationToken.None);
            Assert.Equal(version, installed.Version);
            Assert.Equal(layout.VersionDirectory(version), installed.Directory);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", installed.SourceRevision);
            Assert.True(installed.SizeBytes > 0);
            Assert.Empty(GameVersionStore.Verify(installed));

            File.WriteAllText(Path.Combine(installed.Directory, "host", "ClankerWorld.Viewer.exe"), "changed");
            Assert.Equal([Path.Combine("host", "ClankerWorld.Viewer.exe")], GameVersionStore.Verify(installed));
            var repaired = await store.InstallAsync(release, null, CancellationToken.None);
            Assert.Empty(GameVersionStore.Verify(repaired));
            Assert.Single(Directory.EnumerateDirectories(layout.VersionsDirectory));

            store.Remove(repaired);
            Assert.Empty(store.Installed());
            Assert.Equal("world", File.ReadAllText(save));
            Assert.Empty(Directory.EnumerateFileSystemEntries(layout.DownloadsDirectory));
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("ClankerWorld-0.1.0-alpha.1-windows-x64/../escaped.txt")]
    [InlineData("elsewhere/ClankerWorld.exe")]
    public void APackageThatWritesOutsideItsFolderIsRefused(string entryName)
    {
        var root = Directory.CreateTempSubdirectory("launcher-unsafe-");
        try
        {
            var layout = new LauncherLayout(root.FullName);
            var version = GameVersionName.Parse("0.1.0-alpha.1")!;
            var zip = Path.Combine(root.FullName, "package.zip");
            File.WriteAllBytes(zip, Package(version, ("ClankerWorld.exe", "game")));
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open()))
                writer.Write("escaped");

            using var http = new HttpClient();
            Assert.Throws<GameInstallException>(() => new GameVersionStore(layout, http).InstallPackage(zip, version));
            Assert.False(File.Exists(Path.Combine(layout.VersionsDirectory, "escaped.txt")));
            Assert.Empty(Directory.EnumerateDirectories(layout.VersionsDirectory));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void SavedWorldsNameTheVersionThatLastSavedThemAndStartOptionsCarryDeveloperMode()
    {
        var root = Directory.CreateTempSubdirectory("launcher-worlds-");
        try
        {
            var layout = new LauncherLayout(root.FullName);
            Assert.Empty(SavedWorlds.Read(layout));
            Directory.CreateDirectory(Path.GetDirectoryName(layout.WorldCatalogPath)!);
            File.WriteAllText(layout.WorldCatalogPath,
                """{"ActiveId":"a","Worlds":[{"Name":"Old","GameVersion":"0.1.0-alpha.1"},{"Name":"Unknown"}]}""");
            var before = File.GetLastWriteTimeUtc(layout.WorldCatalogPath);

            var worlds = SavedWorlds.Read(layout);

            Assert.Equal([("Old", "0.1.0-alpha.1"), ("Unknown", (string?)null)],
                worlds.Select(world => (world.Name, world.SavedBy?.ToString())));
            Assert.Equal(before, File.GetLastWriteTimeUtc(layout.WorldCatalogPath));
            Assert.Equal(["--", "--launcher=L.exe"], GameStarter.Arguments("L.exe", developerMode: false));
            Assert.Equal(["--", "--launcher=L.exe", "--developer-mode"], GameStarter.Arguments("L.exe", developerMode: true));
        }
        finally { root.Delete(recursive: true); }
    }

    private static GitHubRelease Release(string tag)
    {
        var name = GameReleaseFeed.PackageName(GameVersionName.Parse(tag[1..])!) + ".zip";
        return new GitHubRelease(tag, Page + tag, false, true,
        [
            new GitHubReleaseAsset(name, 1000, Download + tag + "/" + name),
            new GitHubReleaseAsset(name + ".sha256", 100, Download + tag + "/" + name + ".sha256"),
        ]);
    }

    /// <summary>A package laid out like scripts/package-windows.sh makes it, manifest included.</summary>
    private static byte[] Package(GameVersionName version, params (string Path, string Text)[] files)
    {
        var folder = GameReleaseFeed.PackageName(version) + "/";
        var manifest = new StringBuilder()
            .Append("# ").Append(GameReleaseFeed.PackageName(version)).Append('\n')
            .Append("# Source commit: 0123456789abcdef0123456789abcdef01234567\n");
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                using (var stream = archive.CreateEntry(folder + path).Open()) stream.Write(bytes);
                manifest.Append(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()).Append(" *./").Append(path).Append('\n');
            }
            using var writer = new StreamWriter(archive.CreateEntry(folder + "manifest.sha256").Open());
            writer.Write(manifest.ToString());
        }
        return memory.ToArray();
    }

    private sealed class FakeReleases : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        public bool Tamper { get; set; }

        public GameRelease Publish(GameVersionName version, byte[] package)
        {
            var tag = "v" + version;
            var name = GameReleaseFeed.PackageName(version) + ".zip";
            files[Download + tag + "/" + name] = package;
            files[Download + tag + "/" + name + ".sha256"] = Encoding.ASCII.GetBytes(
                Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant() + " *" + name + "\n");
            return GameReleaseFeed.Read([Release(tag) with
            {
                Assets = [new GitHubReleaseAsset(name, package.Length, Download + tag + "/" + name),
                    new GitHubReleaseAsset(name + ".sha256", 100, Download + tag + "/" + name + ".sha256")],
            }]).Games.Single();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!files.TryGetValue(request.RequestUri!.ToString(), out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (Tamper && request.RequestUri.ToString().EndsWith(".zip", StringComparison.Ordinal))
            {
                bytes = (byte[])bytes.Clone();
                bytes[^1] ^= 1;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
