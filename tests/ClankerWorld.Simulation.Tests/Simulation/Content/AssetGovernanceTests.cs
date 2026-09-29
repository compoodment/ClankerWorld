using System.Buffers.Binary;
using System.Security.Cryptography;
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
    public void ApngTimingIsNormalizedToIntegerMillisecondsAndAnIntegerSampleRate()
    {
        var result = AssetNormalizer.Normalize(Candidate(bytes: AnimatedPng()));

        Assert.True(result.IsValid, result.Diagnostic);
        var png = Assert.IsType<NormalizedInertRasterAsset>(result.Asset).Png;
        Assert.Equal(2, png.FrameCount);
        Assert.Equal(200, png.AnimationDurationMilliseconds);
        Assert.Equal(10, png.AnimationSampleRateHz);
        Assert.Equal([100, 100], png.FrameDurationsMilliseconds);
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

    [Fact]
    public void NormalizationAndDiagnosticsAreDeterministicAcrossRunsAndInputOrder()
    {
        var first = AssetNormalizer.Normalize(Candidate(
            provenance: Provenance() with
            {
                UpstreamAssetIds =
                [
                    AssetRules.CanonicalAssetId(PackageDigest, "zeta", ContentVersion.Parse("1.0.0")),
                    AssetRules.CanonicalAssetId(PackageDigest, "alpha", ContentVersion.Parse("1.0.0")),
                ],
                Rights = Provenance().Rights with { AttributionNotices = ["z-notice", "a-notice"] },
            }));
        var second = AssetNormalizer.Normalize(Candidate(
            provenance: Provenance() with
            {
                UpstreamAssetIds =
                [
                    AssetRules.CanonicalAssetId(PackageDigest, "alpha", ContentVersion.Parse("1.0.0")),
                    AssetRules.CanonicalAssetId(PackageDigest, "zeta", ContentVersion.Parse("1.0.0")),
                ],
                Rights = Provenance().Rights with { AttributionNotices = ["a-notice", "z-notice"] },
            }));

        Assert.True(first.IsValid, first.Diagnostic);
        Assert.True(second.IsValid, second.Diagnostic);
        Assert.Equal(first.InputDigest, second.InputDigest);
        Assert.Equal(first.Asset!.NormalizedDigest, second.Asset!.NormalizedDigest);
        Assert.Equal(first.Asset.CanonicalMetadata, second.Asset.CanonicalMetadata);
        Assert.Equal(first.Diagnostic, second.Diagnostic);
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

    private static byte[] AnimatedPng()
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), 1);
        ihdr[8] = 8;
        ihdr[9] = (byte)AssetPngColorType.TruecolorAlpha;

        var animation = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(animation.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(animation.AsSpan(4, 4), 0);

        var firstControl = FrameControl(sequence: 0, delayNumerator: 1, delayDenominator: 10);
        var secondControl = FrameControl(sequence: 1, delayNumerator: 1, delayDenominator: 10);
        var secondData = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(secondData, 2);

        return
        [
            .. new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            .. Chunk("IHDR", ihdr),
            .. Chunk("acTL", animation),
            .. Chunk("fcTL", firstControl),
            .. Chunk("IDAT", []),
            .. Chunk("fcTL", secondControl),
            .. Chunk("fdAT", secondData),
            .. Chunk("IEND", []),
        ];
    }

    private static byte[] FrameControl(uint sequence, ushort delayNumerator, ushort delayDenominator)
    {
        var data = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(20, 2), delayNumerator);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22, 2), delayDenominator);
        data[24] = 0;
        data[25] = 0;
        return data;
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
