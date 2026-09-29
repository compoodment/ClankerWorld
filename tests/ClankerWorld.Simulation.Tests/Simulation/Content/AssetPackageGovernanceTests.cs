using System.Buffers.Binary;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class AssetPackageGovernanceTests
{
    private const string PackageDigest =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string PreviewDigest =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void AggregateBudgetsRejectTotalsWithoutPartiallyAcceptingThePackage()
    {
        var first = Normalize("alpha", StaticPng(width: 2, height: 1));
        var second = Normalize("beta", StaticPng(width: 2, height: 1));

        var result = AssetPackageGovernance.Validate(
            [first, second],
            new AssetPackageBudgetPolicy
            {
                MaxAssets = 2,
                MaxCandidateBytes = first.Asset!.CandidateBytes + second.Asset!.CandidateBytes - 1,
                MaxDecodedBytes = first.Asset.DecodedBytes + second.Asset.DecodedBytes - 1,
                MaxDurableStorageBytes = first.Asset.DurableStorageBytes + second.Asset.DurableStorageBytes - 1,
                MaxFrames = 2,
                MaxWidth = 2,
                MaxHeight = 1,
            });

        Assert.False(result.IsValid);
        Assert.False(result.CanExport);
        Assert.Equal(2, result.Totals.AssetCount);
        Assert.Equal(2, result.Totals.FrameCount);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "candidate_bytes");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "decoded_bytes");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "durable_storage");
    }

    [Fact]
    public void CanonicalIdsAndNormalizedDigestsMustBothBeUnique()
    {
        var sameId = Normalize("alpha", StaticPng());
        var sameIdDifferentDigest = Normalize("alpha", StaticPng(textPayload: "different"));
        var sameDigest = Normalize("beta", StaticPng());

        var result = AssetPackageGovernance.Validate([sameId, sameIdDifferentDigest, sameDigest]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "duplicate_asset_id");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "duplicate_asset_digest");
    }

    [Fact]
    public void AggregateCountFramesAndDimensionsAreBounded()
    {
        var first = Normalize("alpha", StaticPng(width: 3, height: 2));
        var second = Normalize("beta", StaticPng());

        var result = AssetPackageGovernance.Validate(
            [first, second],
            new AssetPackageBudgetPolicy
            {
                MaxAssets = 1,
                MaxFrames = 1,
                MaxWidth = 2,
                MaxHeight = 1,
            });

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "asset_count");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "frame_count");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "width");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "package_budget_breach" && diagnostic.Field == "height");
    }

    [Fact]
    public void UnknownRightsRemainLocalOnlyAndExportPolicyCanMakeThemAHardFailure()
    {
        var unknownRights = Normalize(
            "unlicensed",
            StaticPng(),
            Provenance() with { Rights = AssetRightsMetadata.UnknownRights() });

        var local = AssetPackageGovernance.Validate([unknownRights]);

        Assert.True(local.IsValid, local.Diagnostic);
        Assert.False(local.CanExport);
        Assert.Contains(local.Diagnostics, diagnostic => diagnostic.Code == "export_rights_blocked");

        var export = AssetPackageGovernance.Validate(
            [unknownRights],
            new AssetPackageBudgetPolicy { ExportPolicy = AssetPackageExportPolicy.RequireExportable });

        Assert.False(export.IsValid);
        Assert.False(export.CanExport);
        var rightsDiagnostic = Assert.Single(
            export.Diagnostics,
            diagnostic => diagnostic.Code == "export_rights_blocked");
        Assert.Equal(AssetDiagnosticSeverity.Error, rightsDiagnostic.Severity);
    }

    [Fact]
    public void DiagnosticsAndPreviewContractsAreStableRegardlessOfInputOrder()
    {
        var alpha = Normalize("alpha", StaticPng(width: 2, height: 2));
        var beta = Normalize("beta", StaticPng(width: 3, height: 1));
        var policy = new AssetPackageBudgetPolicy
        {
            MaxCandidateBytes = 1,
            MaxDecodedBytes = 1,
            MaxDurableStorageBytes = 1,
            MaxFrames = 1,
            MaxWidth = 1,
            MaxHeight = 1,
        };

        var first = AssetPackageGovernance.Validate([beta, alpha], policy);
        var second = AssetPackageGovernance.Validate([alpha, beta], policy);

        Assert.Equal(first.Diagnostic, second.Diagnostic);
        Assert.Equal(first.Totals, second.Totals);
        Assert.Equal(
            first.PreviewContracts.Select(contract => contract.AssetId),
            second.PreviewContracts.Select(contract => contract.AssetId));
        Assert.All(first.PreviewContracts, contract => Assert.True(contract.IsMetadataOnly));
        Assert.Equal(
            first.Diagnostics.Select(diagnostic => diagnostic.ToString()),
            first.Diagnostics
                .OrderBy(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Field, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ObservedValue, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Limit, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void PreviewIsolationContractContainsMetadataOnlyAndNoHostLocator()
    {
        var result = AssetPackageGovernance.Validate([Normalize("alpha", StaticPng())]);

        var preview = Assert.Single(result.PreviewContracts);
        Assert.True(preview.IsMetadataOnly);
        Assert.Equal("alpha", preview.AssetId.Split("/asset/", StringSplitOptions.None)[1].Split('@')[0]);
        Assert.Equal(1, preview.Width);
        Assert.Equal(1, preview.Height);
        Assert.Equal(1, preview.FrameCount);
        Assert.DoesNotContain("/", preview.PreviewId, StringComparison.Ordinal);
        Assert.DoesNotContain("://", preview.PreviewId, StringComparison.Ordinal);
    }

    private static AssetNormalizationResult Normalize(
        string localId,
        byte[] bytes,
        AssetProvenance? provenance = null) =>
        AssetNormalizer.Normalize(
            new InertRasterAssetCandidate(
                PackageDigest,
                localId,
                ContentVersion.Parse("1.0.0"),
                localId,
                bytes,
                provenance ?? Provenance(),
                new AssetPreviewReference($"{localId}-preview", PreviewDigest)));

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
        string? textPayload = null)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = (byte)AssetPngColorType.Truecolor;

        var chunks = new List<byte[]> { Chunk("IHDR", ihdr) };
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
