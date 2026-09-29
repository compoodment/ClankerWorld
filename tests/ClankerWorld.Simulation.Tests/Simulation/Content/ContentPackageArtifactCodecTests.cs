using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContentPackageArtifactCodecTests
{
    private const string PackageDigest =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string PreviewDigest =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void NestedArtifactRoundTripsCanonicallyWithoutAssetBytes()
    {
        var asset = Normalize();
        var artifact = new ContentPackageArtifact(
            Manifest(asset),
            [AssetProvenanceManifest.From(asset)]);

        var encoded = ContentPackageArtifactCodec.Encode(artifact);
        var decoded = ContentPackageArtifactCodec.Decode(encoded);

        Assert.Equal(encoded, ContentPackageArtifactCodec.Encode(decoded));
        Assert.Equal(artifact.ManifestDigest, decoded.ManifestDigest);
        Assert.True(decoded.CanExport);
        Assert.Equal(asset.AssetId, Assert.Single(decoded.Assets).AssetId);
        Assert.DoesNotContain(Convert.ToBase64String(StaticPng()), Encoding.UTF8.GetString(encoded), StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactRequiresEveryDeclaredAssetProvenanceManifest()
    {
        var asset = Normalize();
        var artifact = new ContentPackageArtifact(Manifest(asset), []);

        var exception = Assert.Throws<InvalidDataException>(
            () => ContentPackageArtifactCodec.Encode(artifact));
        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalTransportCanRetainUnknownRightsButExportCannot()
    {
        var asset = Normalize(AssetRightsMetadata.UnknownRights());
        var artifact = new ContentPackageArtifact(
            Manifest(asset),
            [AssetProvenanceManifest.From(asset)]);

        Assert.False(artifact.CanExport);
        Assert.NotEmpty(ContentPackageArtifactCodec.Encode(artifact));
        Assert.Throws<InvalidOperationException>(
            () => ContentPackageArtifactCodec.EncodeForExport(artifact));
    }

    [Fact]
    public void ArtifactRejectsForbiddenCapabilitiesAndNonCanonicalFormatting()
    {
        var asset = Normalize();
        var forbidden = new ContentPackageArtifact(
            Manifest(asset) with { DeclaredCapabilities = ["execute"] },
            [AssetProvenanceManifest.From(asset)]);
        Assert.Throws<InvalidOperationException>(() => ContentPackageArtifactCodec.Encode(forbidden));

        var artifact = new ContentPackageArtifact(
            Manifest(asset),
            [AssetProvenanceManifest.From(asset)]);
        var pretty = JsonSerializer.SerializeToUtf8Bytes(
            JsonSerializer.Deserialize<JsonElement>(ContentPackageArtifactCodec.Encode(artifact)),
            PrettyJsonOptions);
        Assert.Throws<InvalidDataException>(() => ContentPackageArtifactCodec.Decode(pretty));
    }

    private static ContentPackageManifest Manifest(NormalizedInertRasterAsset asset) => new(
        "artifact-package",
        ContentVersion.Parse("1.0.0"),
        PackageDigest,
        [],
        [],
        ["data_only"],
        [WorldAssetReservationRequest.From(asset)]);

    private static NormalizedInertRasterAsset Normalize(AssetRightsMetadata? rights = null)
    {
        var result = AssetNormalizer.Normalize(
            new InertRasterAssetCandidate(
                PackageDigest,
                "sunroot",
                ContentVersion.Parse("1.0.0"),
                "Sunroot",
                StaticPng(),
                new AssetProvenance(
                    AssetSourceKind.Repository,
                    "repo:clankerworld",
                    "owner:fixture",
                    rights ?? new AssetRightsMetadata(
                        AssetRightsStatus.Known,
                        "ClankerWorld project",
                        "spdx:MIT",
                        null,
                        AssetRedistributionStatus.Allowed,
                        ["ClankerWorld project"]),
                    []),
                new AssetPreviewReference("sunroot-preview", PreviewDigest)));
        Assert.True(result.IsValid, result.Diagnostic);
        return Assert.IsType<NormalizedInertRasterAsset>(result.Asset);
    }

    private static byte[] StaticPng()
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), 1);
        ihdr[8] = 8;
        ihdr[9] = (byte)AssetPngColorType.Truecolor;
        return
        [
            .. new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            .. Chunk("IHDR", ihdr),
            .. Chunk("IDAT", []),
            .. Chunk("IEND", []),
        ];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var result = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), checked((uint)data.Length));
        Encoding.ASCII.GetBytes(type, result.AsSpan(4, 4));
        data.CopyTo(result, 8);
        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(8 + data.Length, 4),
            Crc32(result.AsSpan(4, 4 + data.Length)));
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}
