using System.Buffers.Binary;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class AssetGovernanceTests
{
    private const string PackageDigest =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string PreviewDigest =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void RightsAndProvenanceAreRequiredAndUnknownRightsBlockExport()
    {
        var incomplete = Candidate(
            provenance: Provenance() with
            {
                CreatorId = string.Empty,
                Rights = new AssetRightsMetadata(
                    AssetRightsStatus.Known,
                    null,
                    null,
                    null,
                    AssetRedistributionStatus.Allowed,
                    []),
            });

        var rejected = AssetNormalizer.Normalize(incomplete);

        Assert.False(rejected.IsValid);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "provenance_required");
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "rights_license_required");

        var unknownRights = AssetNormalizer.Normalize(
            Candidate(provenance: Provenance() with { Rights = AssetRightsMetadata.UnknownRights() }));

        Assert.True(unknownRights.IsValid, unknownRights.Diagnostic);
        Assert.False(unknownRights.CanExport);
        Assert.Contains(unknownRights.Diagnostics, diagnostic => diagnostic.Code == "rights_unknown");
    }

    [Fact]
    public void PngMetadataNormalizesDimensionsColorAndAnimationWithoutInflatingPayloads()
    {
        var candidate = Candidate(bytes: StaticPng(
            width: 3,
            height: 2,
            colorType: AssetPngColorType.TruecolorAlpha,
            includeSrgb: true,
            textPayload: "execute:never"));

        var result = AssetNormalizer.Normalize(candidate);

        Assert.True(result.IsValid, result.Diagnostic);
        var asset = Assert.IsType<NormalizedInertRasterAsset>(result.Asset);
        Assert.Equal(3, asset.Png.Width);
        Assert.Equal(2, asset.Png.Height);
        Assert.Equal(AssetPngColorType.TruecolorAlpha, asset.Png.ColorType);
        Assert.Equal(8, asset.Png.BitDepth);
        Assert.True(asset.Png.HasAlpha);
        Assert.Equal(AssetPngColorProfile.Srgb, asset.Png.ColorProfile);
        Assert.Equal(1, asset.Png.FrameCount);
        Assert.Equal(0, asset.Png.DurationMilliseconds);
        Assert.Equal(0, asset.Png.SampleRateHz);
        Assert.Equal(24, asset.DecodedBytes);
        Assert.Contains("\"colorType\":\"truecolor-alpha\"", asset.CanonicalMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("execute:never", asset.CanonicalMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetFailuresReportStableReasonCodesAndObservedLimits()
    {
        var dimensionFailure = AssetNormalizer.Normalize(
            Candidate(bytes: StaticPng(width: 2, height: 1)),
            new AssetBudgetPolicy { MaxWidth = 1 });

        Assert.False(dimensionFailure.IsValid);
        var dimensionDiagnostic = Assert.Single(
            dimensionFailure.Diagnostics,
            diagnostic => diagnostic.Field == "width");
        Assert.Equal("asset_budget_breach", dimensionDiagnostic.Code);
        Assert.Equal("2", dimensionDiagnostic.ObservedValue);
        Assert.Equal("1", dimensionDiagnostic.Limit);

        var decodeBombFailure = AssetNormalizer.Normalize(
            Candidate(bytes: StaticPng(width: 2, height: 2)),
            new AssetBudgetPolicy { MaxDecodedBytes = 15 });

        Assert.False(decodeBombFailure.IsValid);
        Assert.Contains(decodeBombFailure.Diagnostics, diagnostic => diagnostic.Code == "decode_bomb");

        var compressedFailure = AssetNormalizer.Normalize(
            Candidate(bytes: StaticPng()),
            new AssetBudgetPolicy { MaxCandidateBytes = 32 });

        Assert.False(compressedFailure.IsValid);
        Assert.Contains(compressedFailure.Diagnostics, diagnostic =>
            diagnostic.Code == "asset_budget_breach" && diagnostic.Field == "candidate_bytes");
    }

    private static InertRasterAssetCandidate Candidate(
        byte[]? bytes = null,
        AssetProvenance? provenance = null,
        AssetPreviewReference? preview = null) => new(
        PackageDigest,
        "sunroot",
        ContentVersion.Parse("1.0.0"),
        "Sunroot",
        bytes ?? StaticPng(),
        provenance ?? Provenance(),
        preview ?? new AssetPreviewReference("sunroot-preview", PreviewDigest));

    private static AssetProvenance Provenance() => new(
        AssetSourceKind.Repository,
        "repo:clankerworld",
        "owner:fixture",
        new AssetRightsMetadata(
            AssetRightsStatus.Known,
            "ClankerWorld project",
            "spdx:MIT",
            null,
            AssetRedistributionStatus.Allowed,
            ["ClankerWorld project"]),
        []);

    private static byte[] StaticPng(
        int width = 1,
        int height = 1,
        AssetPngColorType colorType = AssetPngColorType.Truecolor,
        bool includeSrgb = false,
        string? textPayload = null)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = (byte)colorType;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;

        var chunks = new List<byte[]> { Chunk("IHDR", ihdr) };
        if (includeSrgb)
        {
            chunks.Add(Chunk("sRGB", [0]));
        }

        if (textPayload is not null)
        {
            chunks.Add(Chunk("tEXt", Encoding.UTF8.GetBytes(textPayload)));
        }

        chunks.Add(Chunk("IDAT", []));
        chunks.Add(Chunk("IEND", []));
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
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
            }
        }

        return ~crc;
    }
}
