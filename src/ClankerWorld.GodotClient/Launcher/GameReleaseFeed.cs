using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ClankerWorld.GodotClient.Launcher;

/// <summary>A downloadable file attached to a published release.</summary>
public sealed record ReleaseAsset(string Name, long Size, Uri DownloadUrl);

/// <summary>
/// A published game release: its package and the file holding the package's
/// SHA-256. Both come from the project's own GitHub releases.
/// </summary>
public sealed record GameRelease(GameVersionName Version, string Tag, bool PreRelease, Uri Page,
    ReleaseAsset Package, ReleaseAsset PackageHash);

/// <summary>A published launcher release, which the player installs by hand.</summary>
public sealed record LauncherRelease(GameVersionName Version, Uri Page);

public sealed record ReleaseFeedResult(IReadOnlyList<GameRelease> Games, LauncherRelease? NewestLauncher);

/// <summary>
/// Reads the project's published releases. Game releases are tagged
/// <c>v&lt;version&gt;</c>; launcher releases are tagged
/// <c>launcher-v&lt;version&gt;</c>. Downloads only ever come from
/// <see cref="Host"/>, over HTTPS.
/// </summary>
public sealed class GameReleaseFeed(HttpClient http, Uri? releasesApi = null)
{
    public const string Repository = "ClankerWorldOrg/ClankerWorld";
    public static readonly Uri DefaultReleasesApi = new($"https://api.github.com/repos/{Repository}/releases?per_page=50");
    private static readonly string[] Hosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    public static string PackageName(GameVersionName version) => $"ClankerWorld-{version}-windows-x64";

    public async Task<ReleaseFeedResult> FetchAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, releasesApi ?? DefaultReleasesApi);
        request.Headers.UserAgent.ParseAdd("ClankerWorld-Launcher");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(cancellationToken) ?? [];
        return Read(releases);
    }

    public static ReleaseFeedResult Read(IEnumerable<GitHubRelease> releases)
    {
        var games = new List<GameRelease>();
        LauncherRelease? launcher = null;
        foreach (var release in releases)
        {
            if (release.Draft || release.TagName is null || !TryPage(release.HtmlUrl, out var page)) continue;
            if (release.TagName.StartsWith("launcher-v", StringComparison.Ordinal))
            {
                var launcherVersion = GameVersionName.Parse(release.TagName["launcher-v".Length..]);
                if (launcherVersion is not null && (launcher is null || launcherVersion.CompareTo(launcher.Version) > 0))
                    launcher = new LauncherRelease(launcherVersion, page);
                continue;
            }
            if (!release.TagName.StartsWith('v')) continue;
            var version = GameVersionName.Parse(release.TagName[1..]);
            if (version is null) continue;
            var name = PackageName(version) + ".zip";
            var package = Asset(release, name);
            var hash = Asset(release, name + ".sha256");
            if (package is null || hash is null) continue;
            games.Add(new GameRelease(version, release.TagName, release.PreRelease, page, package, hash));
        }
        games.Sort((a, b) => b.Version.CompareTo(a.Version));
        return new ReleaseFeedResult(games, launcher);
    }

    /// <summary>Only the project's own release downloads are accepted.</summary>
    public static bool IsTrustedDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && Hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    private static ReleaseAsset? Asset(GitHubRelease release, string name)
    {
        var asset = release.Assets?.SingleOrDefault(candidate => candidate.Name == name);
        return asset?.BrowserDownloadUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            IsTrustedDownload(uri) && asset.Size > 0
            ? new ReleaseAsset(name, asset.Size, uri)
            : null;
    }

    private static bool TryPage(string? url, out Uri page) =>
        Uri.TryCreate(url, UriKind.Absolute, out page!) && page.Scheme == Uri.UriSchemeHttps &&
        page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
}

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool PreRelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset>? Assets);

public sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
