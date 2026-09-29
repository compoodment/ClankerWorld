using ClankerWorld.Simulation.Harness;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ApprovedAssetCatalogTests
{
    private const string PortraitAliceDigest =
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void MissingCatalogFailsClosedToNoApprovedReferences()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-missing-catalog-{Guid.NewGuid():N}.json");

        var catalog = ApprovedAssetCatalog.LoadOrDeny(path);

        Assert.Empty(catalog.GetApprovedReferences());
        Assert.False(catalog.IsApproved(new OwnerApprovedAssetReference("portrait-alice", PortraitAliceDigest)));
    }

    [Fact]
    public void CompleteCanonicalCatalogAllowsOnlyItsExactReferences()
    {
        using var fixture = new TemporaryDirectory();
        var path = System.IO.Path.Combine(fixture.Path, "approved-assets.json");
        File.WriteAllText(path, CatalogJson(("portrait-alice", PortraitAliceDigest)));

        var catalog = ApprovedAssetCatalog.LoadOrDeny(path);

        Assert.True(catalog.IsApproved(new OwnerApprovedAssetReference("portrait-alice", PortraitAliceDigest)));
        Assert.False(catalog.IsApproved(new OwnerApprovedAssetReference(
            "portrait-alice",
            "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")));
        Assert.False(catalog.IsApproved(new OwnerApprovedAssetReference("portrait-bob", PortraitAliceDigest)));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"references\":[{\"assetId\":\"portrait-alice\",\"assetDigest\":\"sha256:bad\"}]}")]
    [InlineData("{\"schemaVersion\":2,\"references\":[]}")]
    public void ExistingInvalidCatalogRefusesToStart(string content)
    {
        using var fixture = new TemporaryDirectory();
        var path = System.IO.Path.Combine(fixture.Path, "approved-assets.json");
        File.WriteAllText(path, content);

        Assert.Throws<InvalidDataException>(() => ApprovedAssetCatalog.LoadOrDeny(path));
    }

    [Fact]
    public void SavedAssetReferenceCannotRestoreWithoutTheSameHostApproval()
    {
        using var fixture = new TemporaryDirectory();
        var catalogPath = System.IO.Path.Combine(fixture.Path, "approved-assets.json");
        var statePath = System.IO.Path.Combine(fixture.Path, "runtime.json");
        File.WriteAllText(catalogPath, CatalogJson(("portrait-alice", PortraitAliceDigest)));

        var catalog = ApprovedAssetCatalog.LoadOrDeny(catalogPath);
        var firstStateFile = new OwnerWorldStateFile(statePath, catalog);
        var first = firstStateFile.LoadOrCreate("camp-alpha");
        Assert.True(first.Pause("owner-device:catalog-test"));
        var applied = first.ApplyAuthoringBatch(new OwnerAuthoringBatch(
            "catalog-asset",
            [new AddApprovedAssetReferenceOperation("portrait-alice", PortraitAliceDigest)],
            "owner-device:catalog-test"));
        Assert.True(applied.Applied, applied.Failure);
        firstStateFile.Save(first);

        File.Delete(catalogPath);
        var denyStateFile = new OwnerWorldStateFile(
            statePath,
            ApprovedAssetCatalog.LoadOrDeny(catalogPath));

        Assert.Throws<InvalidDataException>(() => denyStateFile.LoadOrCreate("camp-alpha"));
    }

    private static string CatalogJson(params (string AssetId, string AssetDigest)[] references) =>
        $$"""
        {"schemaVersion":1,"references":[{{string.Join(',', references.Select(reference =>
            $"{{\"assetId\":\"{reference.AssetId}\",\"assetDigest\":\"{reference.AssetDigest}\"}}"))}}]}
        """;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"clankerworld-approved-assets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
