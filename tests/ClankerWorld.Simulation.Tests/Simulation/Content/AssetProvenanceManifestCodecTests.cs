using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class AssetProvenanceManifestCodecTests
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
    public void CanonicalBytesAreOrderIndependentAndRoundTripToTheNormalizedAsset()
    {
        var asset = Normalize();
        var manifest = AssetProvenanceManifest.From(asset);
        var reordered = manifest with
        {
            Provenance = manifest.Provenance with
            {
                UpstreamAssetIds = manifest.Provenance.UpstreamAssetIds.Reverse().ToArray(),
                Rights = manifest.Provenance.Rights with
                {
                    AttributionNotices = manifest.Provenance.Rights.AttributionNotices.Reverse().ToArray(),
                },
            },
        };

        var encoded = AssetProvenanceManifestCodec.Encode(manifest);
        var reorderedEncoded = AssetProvenanceManifestCodec.Encode(reordered);
        var decoded = AssetProvenanceManifestCodec.Decode(encoded);

        Assert.Equal(encoded, reorderedEncoded);
        Assert.Equal(
            AssetProvenanceManifestCodec.ComputeManifestDigest(manifest),
            AssetProvenanceManifestCodec.ComputeManifestDigest(reordered));
        Assert.Equal(encoded, AssetProvenanceManifestCodec.Encode(decoded));
        Assert.Equal(asset.AssetId, decoded.AssetId);
        Assert.Equal(asset.NormalizedDigest, decoded.NormalizedDigest);
        Assert.Equal(asset.CanonicalMetadata, decoded.CanonicalMetadata);
        var decodedAsset = decoded.ToNormalizedAsset();
        Assert.Equal(asset.Png.Width, decodedAsset.Png.Width);
        Assert.Equal(asset.Png.Height, decodedAsset.Png.Height);
        Assert.Equal(asset.Png.ColorType, decodedAsset.Png.ColorType);
        Assert.Equal(asset.Png.FrameCount, decodedAsset.Png.FrameCount);
        Assert.Equal(asset.Png.FrameDurationsMilliseconds, decodedAsset.Png.FrameDurationsMilliseconds);
    }

    [Fact]
    public void DecoderRejectsPrettyJsonAndTamperedIdentity()
    {
        var manifest = AssetProvenanceManifest.From(Normalize());
        var encoded = AssetProvenanceManifestCodec.Encode(manifest);
        var element = JsonSerializer.Deserialize<JsonElement>(encoded);
        var pretty = JsonSerializer.SerializeToUtf8Bytes(element, PrettyJsonOptions);

        Assert.Throws<InvalidDataException>(() => AssetProvenanceManifestCodec.Decode(pretty));

        var replacement = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        var encodedText = Encoding.UTF8.GetString(encoded);
        var digestOffset = encodedText.IndexOf(manifest.NormalizedDigest, StringComparison.Ordinal);
        Assert.True(digestOffset >= 0);
        var tampered = Encoding.UTF8.GetBytes(
            string.Concat(
                encodedText[..digestOffset],
                replacement,
                encodedText[(digestOffset + manifest.NormalizedDigest.Length)..]));
        var exception = Assert.Throws<InvalidDataException>(
            () => AssetProvenanceManifestCodec.Decode(tampered));
        Assert.Contains("identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownRightsRemainRepresentableButCannotExport()
    {
        var asset = Normalize(AssetRightsMetadata.UnknownRights());
        var manifest = AssetProvenanceManifest.From(asset);
        var decoded = AssetProvenanceManifestCodec.Decode(AssetProvenanceManifestCodec.Encode(manifest));

        Assert.False(decoded.CanExport);
        Assert.Equal(AssetRightsStatus.Unknown, decoded.Provenance.Rights.Status);
    }

    private static NormalizedInertRasterAsset Normalize(AssetRightsMetadata? rights = null)
    {
        var candidate = new InertRasterAssetCandidate(
            PackageDigest,
            "sunroot",
            ContentVersion.Parse("1.0.0"),
            "Sunroot",
            StaticPng(),
            new AssetProvenance(
                AssetSourceKind.Transformed,
                "creator:fixture",
                "owner:fixture",
                rights ?? new AssetRightsMetadata(
                        AssetRightsStatus.Known,
                        "ClankerWorld project",
                        "spdx:MIT",
                        null,
                        AssetRedistributionStatus.Allowed,
                        ["z-notice", "a-notice"]),
                [
                    AssetRules.CanonicalAssetId(PackageDigest, "zeta", ContentVersion.Parse("1.0.0")),
                    AssetRules.CanonicalAssetId(PackageDigest, "alpha", ContentVersion.Parse("1.0.0")),
                ]),
            new AssetPreviewReference("sunroot-preview", PreviewDigest));
        var result = AssetNormalizer.Normalize(candidate);
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

        var chunks = new List<byte[]>
        {
            Chunk("IHDR", ihdr),
            Chunk("IDAT", []),
            Chunk("IEND", []),
        };
        return [.. new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, .. chunks.SelectMany(chunk => chunk)];
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
