using System.Text;
using System.Text.RegularExpressions;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class DocumentationTests
{
    private static readonly string[] AllowedDocumentStatuses =
        ["active", "complete", "frozen", "history", "proposal", "superseded"];

    [Fact]
    public void CanonicalDocumentationSourcesExist()
    {
        var root = FindRepositoryRoot();
        var required = new[]
        {
            "README.md",
            "CONTRIBUTING.md",
            "AGENTS.md",
            "CHANGELOG.md",
            "docs/README.md",
            "docs/playing.md",
            "docs/what-works.md",
            "docs/game-design/README.md",
            "docs/game-design/world.md",
            "docs/game-design/agents-and-families.md",
            "docs/game-design/towns.md",
            "docs/game-design/interface-and-art.md",
            "docs/game-design/inventions-and-mods.md",
            "docs/game-design/saves.md",
            "docs/game-design/content-list.md",
            "docs/development/README.md",
            "docs/development/how-it-works.md",
            "docs/development/build-and-test.md",
            "docs/development/device-pairing.md",
            "docs/development/saves-and-replay.md",
            "docs/development/releasing.md",
        };

        foreach (var relativePath in required)
        {
            Assert.True(
                File.Exists(Path.Combine(root, relativePath)),
                $"Required canonical documentation is missing: {relativePath}");
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void DocumentationFilesDeclareTheirAuthorityMetadata(string checkoutNewline)
    {
        var root = FindRepositoryRoot();
        var documentationRoot = Path.Combine(root, "docs");
        foreach (var path in Directory.EnumerateFiles(documentationRoot, "*.md", SearchOption.AllDirectories))
        {
            // Parse each checkout newline variant before normalizing anything.
            var text = File.ReadAllText(path).ReplaceLineEndings(checkoutNewline);
            Assert.StartsWith("---" + checkoutNewline, text, StringComparison.Ordinal);
            var frontMatterStart = 3 + checkoutNewline.Length;
            var closingDelimiter = text.IndexOf(checkoutNewline + "---" + checkoutNewline,
                frontMatterStart, StringComparison.Ordinal);
            Assert.True(closingDelimiter > 0, $"Front matter is not closed: {Relative(root, path)}");
            var frontMatter = text[frontMatterStart..closingDelimiter];
            Assert.Contains("title:", frontMatter, StringComparison.Ordinal);
            Assert.Contains("type:", frontMatter, StringComparison.Ordinal);
            Assert.Contains("status:", frontMatter, StringComparison.Ordinal);
            Assert.Contains("updated:", frontMatter, StringComparison.Ordinal);

            var status = frontMatter
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.StartsWith("status:", StringComparison.Ordinal))["status:".Length..]
                .Trim();
            Assert.Contains(status, AllowedDocumentStatuses);
        }
    }

    [Fact]
    public void LocalMarkdownLinksResolveToExistingFilesAndHeadings()
    {
        var root = FindRepositoryRoot();
        var markdownFiles = Directory
            .EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(path => !Relative(root, path).StartsWith(".git/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var sourcePath in markdownFiles)
        {
            var source = File.ReadAllText(sourcePath);
            foreach (Match match in MarkdownLink().Matches(source))
            {
                var rawTarget = match.Groups["target"].Value.Trim('<', '>');
                if (IsExternalOrPageAnchor(rawTarget))
                {
                    continue;
                }

                var parts = rawTarget.Split('#', 2);
                var decodedPath = Uri.UnescapeDataString(parts[0]);
                var targetPath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(sourcePath)!, decodedPath));
                Assert.True(
                    File.Exists(targetPath) || Directory.Exists(targetPath),
                    $"Broken local link in {Relative(root, sourcePath)}: {rawTarget}");

                if (parts.Length == 2 && File.Exists(targetPath) &&
                    string.Equals(Path.GetExtension(targetPath), ".md", StringComparison.OrdinalIgnoreCase))
                {
                    var expectedAnchor = Uri.UnescapeDataString(parts[1]);
                    var anchors = MarkdownHeadings(File.ReadAllLines(targetPath));
                    Assert.True(
                        anchors.Contains(expectedAnchor),
                        $"Broken heading link in {Relative(root, sourcePath)}: {rawTarget}");
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ClankerWorld.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ClankerWorld repository root.");
    }

    private static bool IsExternalOrPageAnchor(string target) =>
        target.StartsWith('#') ||
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> MarkdownHeadings(IEnumerable<string> lines)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var match = MarkdownHeading().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var slug = GitHubHeadingSlug(match.Groups["heading"].Value);
            duplicateCounts.TryGetValue(slug, out var duplicateCount);
            duplicateCounts[slug] = duplicateCount + 1;
            anchors.Add(duplicateCount == 0 ? slug : $"{slug}-{duplicateCount}");
        }

        return anchors;
    }

    private static string GitHubHeadingSlug(string heading)
    {
        var result = new StringBuilder(heading.Length);
        foreach (var character in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                result.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                result.Append('-');
            }
        }

        return result.ToString();
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    [GeneratedRegex("""\[[^\]]+\]\((?<target>[^\s)]+)(?:\s+["'][^"']*["'])?\)""")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^#{1,6}\s+(?<heading>.+?)\s*#*$")]
    private static partial Regex MarkdownHeading();
}
