using System.Text.Json;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContentPackageManifestCodecTests
{
    private const string PackageDigest =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string DependencyDigest =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void CanonicalManifestBytesAreOrderIndependentAndRoundTripWithAStableDigest()
    {
        var manifest = Manifest();
        var reordered = manifest with
        {
            Dependencies = manifest.Dependencies.Reverse().ToArray(),
            Definitions = manifest.Definitions.Reverse().ToArray(),
            DeclaredCapabilities = manifest.DeclaredCapabilities.Reverse().ToArray(),
            AssetReservations = manifest.AssetReservations!.Reverse().ToArray(),
        };

        var encoded = ContentPackageManifestCodec.Encode(manifest);
        var reorderedEncoded = ContentPackageManifestCodec.Encode(reordered);
        var decoded = ContentPackageManifestCodec.Decode(encoded);

        Assert.Equal(encoded, reorderedEncoded);
        Assert.Equal(
            ContentPackageManifestCodec.ComputeManifestDigest(manifest),
            ContentPackageManifestCodec.ComputeManifestDigest(reordered));
        Assert.Equal(encoded, ContentPackageManifestCodec.Encode(decoded));
        Assert.Equal(manifest.PackageId, decoded.PackageId);
        Assert.Equal(manifest.PackageDigest, decoded.PackageDigest);
        Assert.Equal(manifest.Definitions.Count, decoded.Definitions.Count);
        Assert.Equal(manifest.AssetReservations!.Count, decoded.AssetReservations!.Count);
    }

    [Fact]
    public void DecoderRejectsNonCanonicalJsonFormatting()
    {
        var encoded = ContentPackageManifestCodec.Encode(Manifest());
        var document = JsonSerializer.Deserialize<JsonElement>(encoded);
        var pretty = JsonSerializer.SerializeToUtf8Bytes(document, PrettyJsonOptions);

        var exception = Assert.Throws<InvalidDataException>(() => ContentPackageManifestCodec.Decode(pretty));
        Assert.Contains("canonical byte form", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryPersistsAndVerifiesTheCanonicalManifestDigest()
    {
        var registry = new ContentPackageRegistry();
        var record = registry.Propose(Manifest());
        var expectedDigest = ContentPackageManifestCodec.ComputeManifestDigest(record.Manifest);

        Assert.Equal(expectedDigest, record.ManifestDigest);

        var restored = ContentPackageRegistry.Restore(registry.ExportState());
        Assert.Equal(expectedDigest, Assert.Single(restored.ExportState().Packages).ManifestDigest);

        var tampered = registry.ExportState() with
        {
            Packages = [record with { ManifestDigest = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" }],
        };
        Assert.Throws<InvalidDataException>(() => ContentPackageRegistry.Restore(tampered));
    }

    private static ContentPackageManifest Manifest() => new(
        "codec-package",
        ContentVersion.Parse("1.0.0"),
        PackageDigest,
        [
            new ContentDependency(
                "zeta-dependency",
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0"))),
            new ContentDependency(
                "alpha-dependency",
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("3.0.0")),
                Optional: true),
        ],
        [
            new ContentDefinition(
                "recipe",
                "berry-meal",
                ContentVersion.Parse("1.0.0"),
                "Berry meal",
                DependencyDigest,
                "{\"schema\":\"recipe/v1\"}"),
            new ContentDefinition(
                "building",
                "camp-kitchen",
                ContentVersion.Parse("1.0.0"),
                "Camp kitchen",
                PackageDigest,
                "{\"schema\":\"building/v1\"}"),
        ],
        ["zeta", "alpha"],
        [
            new WorldAssetReservationRequest(
                AssetRules.CanonicalAssetId(PackageDigest, "zeta", ContentVersion.Parse("1.0.0")),
                DependencyDigest,
                "rgba8",
                10,
                20,
                20,
                2),
            new WorldAssetReservationRequest(
                AssetRules.CanonicalAssetId(PackageDigest, "alpha", ContentVersion.Parse("1.0.0")),
                PackageDigest,
                "rgba8",
                10,
                20,
                20,
                1),
        ]);
}
