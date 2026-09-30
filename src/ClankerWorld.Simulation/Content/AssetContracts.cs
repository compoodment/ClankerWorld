using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Simulation.Content;

/// <summary>
/// The permitted origins of an inert asset. This is provenance data, not a
/// runtime capability grant.
/// </summary>
public enum AssetSourceKind
{
    Unknown,
    Repository,
    Human,
    Generated,
    Transformed,
    Inhabitant,
}

/// <summary>
/// Copyright/permission knowledge is deliberately separate from runtime
/// approval. Unknown rights may be retained for local inspection but cannot be
/// exported.
/// </summary>
public enum AssetRightsStatus
{
    Unknown,
    Known,
    Restricted,
}

public enum AssetRedistributionStatus
{
    Unknown,
    Allowed,
    Restricted,
}

public enum AssetDiagnosticSeverity
{
    Error,
    Warning,
}

public enum AssetPngColorType
{
    Grayscale = 0,
    Truecolor = 2,
    Indexed = 3,
    GrayscaleAlpha = 4,
    TruecolorAlpha = 6,
}

public enum AssetPngColorProfile
{
    Unspecified,
    Srgb,
}

/// <summary>
/// Rights and notice metadata carried with an asset. A license identifier or
/// bundled license reference is required when rights are known or restricted.
/// An unknown status is a warning at normalization time and makes export
/// unavailable by default.
/// </summary>
public sealed record AssetRightsMetadata(
    AssetRightsStatus Status,
    string? RightsHolder,
    string? LicenseId,
    string? BundledLicenseReference,
    AssetRedistributionStatus Redistribution,
    IReadOnlyList<string> AttributionNotices)
{
    public bool ExportAllowed =>
        Status == AssetRightsStatus.Known &&
        Redistribution == AssetRedistributionStatus.Allowed &&
        (!string.IsNullOrWhiteSpace(LicenseId) || !string.IsNullOrWhiteSpace(BundledLicenseReference));

    public static AssetRightsMetadata UnknownRights(
        string? rightsHolder = null,
        IReadOnlyList<string>? attributionNotices = null) =>
        new(
            AssetRightsStatus.Unknown,
            rightsHolder,
            null,
            null,
            AssetRedistributionStatus.Unknown,
            attributionNotices ?? []);
}

/// <summary>
/// Human and machine provenance for an asset. Upstream IDs are immutable asset
/// IDs, never paths or URLs, and are sorted when normalized.
/// </summary>
public sealed record AssetProvenance(
    AssetSourceKind SourceKind,
    string CreatorId,
    string ProposerId,
    AssetRightsMetadata Rights,
    IReadOnlyList<string> UpstreamAssetIds);

/// <summary>
/// A host-resolved preview reference. It contains only a canonical identifier
/// and digest; the simulation never opens a file, URL, process, or renderer.
/// </summary>
public sealed record AssetPreviewReference(
    string PreviewId,
    string PreviewDigest);

/// <summary>
/// The versioned acceptance ceilings for inert raster assets. The defaults are
/// the Phase 5 <c>asset-budget-v1</c> policy and count decoded pixels as
/// canonical RGBA8 frames, independent of a renderer's compression format.
/// </summary>
public sealed record AssetBudgetPolicy
{
    public const string Version = "asset-budget-v1";
    public const long MiB = 1_048_576;

    public long MaxCandidateBytes { get; init; } = 4 * MiB;

    public long MaxDecodedBytes { get; init; } = 16 * MiB;

    public int MaxWidth { get; init; } = 2_048;

    public int MaxHeight { get; init; } = 2_048;

    public int MaxFrames { get; init; } = 256;

    public int MaxAnimationDurationMilliseconds { get; init; } = 30_000;

    public int MaxAnimationSampleRateHz { get; init; } = 60;

    public long MaxDurableStorageBytes { get; init; } = 8 * MiB;

    public static AssetBudgetPolicy Default { get; } = new();

    public void Validate()
    {
        if (MaxCandidateBytes <= 0 || MaxDecodedBytes <= 0 || MaxDurableStorageBytes <= 0 ||
            MaxWidth <= 0 || MaxHeight <= 0 || MaxFrames <= 0 ||
            MaxAnimationDurationMilliseconds < 0 || MaxAnimationSampleRateHz < 0)
        {
            throw new ArgumentException("Asset budget limits must be positive, except zero-valued animation limits.", nameof(AssetBudgetPolicy));
        }
    }
}

/// <summary>
/// An untrusted, in-memory raster proposal. It carries no path, URL, callback,
/// executable payload, or file handle.
/// </summary>
public sealed record InertRasterAssetCandidate(
    string PackageDigest,
    string LocalId,
    ContentVersion Version,
    string DisplayName,
    ReadOnlyMemory<byte> PngBytes,
    AssetProvenance Provenance,
    AssetPreviewReference Preview)
{
    public string CanonicalId() => AssetRules.CanonicalAssetId(PackageDigest, LocalId, Version);
}

/// <summary>
/// PNG structure and animation metadata after safe normalization. IDAT and
/// fdAT payloads are deliberately not decoded by this contract.
/// </summary>
public sealed record AssetPngMetadata(
    int Width,
    int Height,
    AssetPngColorType ColorType,
    int BitDepth,
    bool HasAlpha,
    bool IsInterlaced,
    AssetPngColorProfile ColorProfile,
    int FrameCount,
    int AnimationDurationMilliseconds,
    int AnimationSampleRateHz,
    IReadOnlyList<int> FrameDurationsMilliseconds,
    int LoopCount)
{
    public bool IsAnimated => FrameCount > 1;

    public long DecodedRgba8Bytes => checked((long)Width * Height * 4 * FrameCount);

    public int DurationMilliseconds => AnimationDurationMilliseconds;

    public int SampleRateHz => AnimationSampleRateHz;
}

/// <summary>
/// The immutable result of accepting an inert raster candidate. The original
/// and normalized digests are content identities; provenance, rights, and
/// preview data remain inspectable but cannot cause host-side work.
/// </summary>
public sealed record NormalizedInertRasterAsset(
    string AssetId,
    string DisplayName,
    string OriginalDigest,
    string NormalizedDigest,
    AssetProvenance Provenance,
    AssetPreviewReference Preview,
    AssetPngMetadata Png,
    string CanonicalMetadata,
    long CandidateBytes,
    long DecodedBytes,
    long DurableStorageBytes)
{
    public bool CanExport => Provenance.Rights.ExportAllowed;

    public AssetPngMetadata Metadata => Png;

    public long PixelBytes => DecodedBytes;
}

public sealed record AssetDiagnostic(
    AssetDiagnosticSeverity Severity,
    string Code,
    string Field,
    string Message,
    string? ObservedValue = null,
    string? Limit = null)
{
    public override string ToString() =>
        string.Join(
            '|',
            Severity.ToString().ToLowerInvariant(),
            Code,
            Field,
            ObservedValue ?? string.Empty,
            Limit ?? string.Empty,
            Message);
}

/// <summary>
/// Deterministic validation/normalization output. Diagnostics are ordered by
/// severity, code, field, observed value, and limit.
/// </summary>
public sealed record AssetNormalizationResult(
    bool IsValid,
    string InputDigest,
    NormalizedInertRasterAsset? Asset,
    IReadOnlyList<AssetDiagnostic> Diagnostics)
{
    public bool CanExport => IsValid && Asset?.CanExport == true;

    public string? FailureCode => Diagnostics
        .Where(diagnostic => diagnostic.Severity == AssetDiagnosticSeverity.Error)
        .Select(diagnostic => diagnostic.Code)
        .FirstOrDefault();

    public string Diagnostic => string.Join('\n', Diagnostics.Select(diagnostic => diagnostic.ToString()));

    public string DiagnosticText => Diagnostic;
}

/// <summary>
/// Naming and digest helpers shared by the asset contract. Asset IDs reuse the
/// existing immutable content-definition form with the registered <c>asset</c>
/// kind.
/// </summary>
public static class AssetRules
{
    public const string SchemaKind = "asset";

    public static string CanonicalAssetId(
        string packageDigest,
        string localId,
        ContentVersion version)
    {
        ValidateVersion(version);
        return ContentPackageRules.CanonicalDefinitionId(packageDigest, SchemaKind, localId, version);
    }

    public static string CanonicalId(
        string packageDigest,
        string localId,
        ContentVersion version) => CanonicalAssetId(packageDigest, localId, version);

    public static void ValidateAssetId(string value) => ValidateCanonicalAssetId(value);

    public static void ValidateCanonicalAssetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var assetMarker = "/asset/";
        var markerIndex = value.IndexOf(assetMarker, StringComparison.Ordinal);
        var versionMarker = value.LastIndexOf('@');
        if (markerIndex <= 0 || versionMarker <= markerIndex + assetMarker.Length ||
            versionMarker == value.Length - 1 || value.IndexOf('/', versionMarker + 1) >= 0)
        {
            throw new ArgumentException($"Asset ID '{value}' is not canonical.", nameof(value));
        }

        var packageDigest = value[..markerIndex];
        var localId = value[(markerIndex + assetMarker.Length)..versionMarker];
        var versionText = value[(versionMarker + 1)..];
        ContentPackageRules.ValidateDigest(packageDigest, nameof(value));
        ContentPackageRules.ValidateLocalId(localId);
        var version = ContentVersion.Parse(versionText);
        ValidateVersion(version);
        if (!string.Equals(value, CanonicalAssetId(packageDigest, localId, version), StringComparison.Ordinal))
        {
            throw new ArgumentException($"Asset ID '{value}' is not canonical.", nameof(value));
        }
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";

    internal static string CanonicalSourceKind(AssetSourceKind sourceKind) => sourceKind switch
    {
        AssetSourceKind.Repository => "repository",
        AssetSourceKind.Human => "human",
        AssetSourceKind.Generated => "generated",
        AssetSourceKind.Transformed => "transformed",
        AssetSourceKind.Inhabitant => "inhabitant",
        _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
    };

    internal static string CanonicalRightsStatus(AssetRightsStatus status) => status switch
    {
        AssetRightsStatus.Unknown => "unknown",
        AssetRightsStatus.Known => "known",
        AssetRightsStatus.Restricted => "restricted",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    internal static string CanonicalRedistributionStatus(AssetRedistributionStatus status) => status switch
    {
        AssetRedistributionStatus.Unknown => "unknown",
        AssetRedistributionStatus.Allowed => "allowed",
        AssetRedistributionStatus.Restricted => "restricted",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    internal static string CanonicalColorType(AssetPngColorType colorType) => colorType switch
    {
        AssetPngColorType.Grayscale => "grayscale",
        AssetPngColorType.Truecolor => "truecolor",
        AssetPngColorType.Indexed => "indexed",
        AssetPngColorType.GrayscaleAlpha => "grayscale-alpha",
        AssetPngColorType.TruecolorAlpha => "truecolor-alpha",
        _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
    };

    private static void ValidateVersion(ContentVersion version)
    {
        if (version.Major < 0 || version.Minor < 0 || version.Patch < 0)
        {
            throw new ArgumentException("Asset versions cannot contain negative components.", nameof(version));
        }
    }
}

/// <summary>
/// Normalizes and validates data-only PNG candidates. The reader validates PNG
/// structure and CRCs, but never inflates image payloads or interprets textual,
/// ICC, or other embedded executable content.
/// </summary>
public static class AssetNormalizer
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static AssetNormalizationResult Normalize(
        InertRasterAssetCandidate candidate,
        AssetBudgetPolicy? budget = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        budget ??= AssetBudgetPolicy.Default;
        budget.Validate();

        var diagnostics = new List<AssetDiagnostic>();
        var inputDigest = AssetRules.Sha256(candidate.PngBytes.Span);
        string? assetId = null;

        try
        {
            assetId = AssetRules.CanonicalAssetId(candidate.PackageDigest, candidate.LocalId, candidate.Version);
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Error(
                "canonical_asset_id",
                "asset_id",
                "invalid",
                null,
                "The asset package digest, local ID, and version must form a canonical asset ID."));
        }
        catch (FormatException)
        {
            diagnostics.Add(Error(
                "canonical_asset_id",
                "asset_id",
                "invalid",
                null,
                "The asset version must be a stable major.minor.patch version."));
        }

        ValidateCandidateFields(candidate, diagnostics);

        var candidateBytes = (long)candidate.PngBytes.Length;
        if (candidateBytes > budget.MaxCandidateBytes)
        {
            diagnostics.Add(Budget(
                "candidate_bytes",
                candidateBytes,
                budget.MaxCandidateBytes,
                "The exact input PNG exceeds the compressed-byte budget."));
        }

        AssetPngMetadata? png = null;
        if (candidateBytes <= budget.MaxCandidateBytes)
        {
            var parsed = ParsePng(candidate.PngBytes.Span, budget);
            diagnostics.AddRange(parsed.Diagnostics);
            png = parsed.Metadata;
        }

        if (png is not null)
        {
            var decodedBytes = png.DecodedRgba8Bytes;
            if (decodedBytes > budget.MaxDecodedBytes)
            {
                diagnostics.Add(Budget(
                    "decoded_bytes",
                    decodedBytes,
                    budget.MaxDecodedBytes,
                    "The canonical RGBA8 decode reservation exceeds the decode-bomb limit.",
                    code: "decode_bomb"));
            }

            if (png.FrameCount > budget.MaxFrames)
            {
                diagnostics.Add(Budget(
                    "frame_count",
                    png.FrameCount,
                    budget.MaxFrames,
                    "The animation frame count exceeds the asset budget."));
            }

            if (png.AnimationDurationMilliseconds > budget.MaxAnimationDurationMilliseconds)
            {
                diagnostics.Add(Budget(
                    "animation_duration_ms",
                    png.AnimationDurationMilliseconds,
                    budget.MaxAnimationDurationMilliseconds,
                    "The animation duration exceeds the asset budget."));
            }

            if (png.AnimationSampleRateHz > budget.MaxAnimationSampleRateHz)
            {
                diagnostics.Add(Budget(
                    "animation_sample_rate_hz",
                    png.AnimationSampleRateHz,
                    budget.MaxAnimationSampleRateHz,
                    "The animation sample rate exceeds the asset budget."));
            }
        }

        var orderedDiagnostics = OrderDiagnostics(diagnostics);
        if (assetId is null || png is null || orderedDiagnostics.Any(diagnostic => diagnostic.Severity == AssetDiagnosticSeverity.Error))
        {
            return new AssetNormalizationResult(false, inputDigest, null, orderedDiagnostics);
        }

        var originalDigest = inputDigest;
        var normalizedDigest = AssetRules.Sha256(Encoding.UTF8.GetBytes(CanonicalPngIdentity(originalDigest, png)));
        var canonicalMetadata = CanonicalMetadata(candidate, assetId, originalDigest, normalizedDigest, png);
        var durableStorageBytes = checked((long)candidate.PngBytes.Length + Encoding.UTF8.GetByteCount(canonicalMetadata));
        if (durableStorageBytes > budget.MaxDurableStorageBytes)
        {
            var storageDiagnostic = Budget(
                "durable_storage",
                durableStorageBytes,
                budget.MaxDurableStorageBytes,
                "The normalized PNG and canonical metadata exceed the durable storage budget.");
            orderedDiagnostics = OrderDiagnostics([.. orderedDiagnostics, storageDiagnostic]);
            return new AssetNormalizationResult(false, inputDigest, null, orderedDiagnostics);
        }

        var asset = new NormalizedInertRasterAsset(
            assetId,
            candidate.DisplayName.Trim(),
            originalDigest,
            normalizedDigest,
            NormalizeProvenance(candidate.Provenance),
            new AssetPreviewReference(candidate.Preview.PreviewId.Trim(), candidate.Preview.PreviewDigest),
            png,
            canonicalMetadata,
            candidateBytes,
            png.DecodedRgba8Bytes,
            durableStorageBytes);
        return new AssetNormalizationResult(true, inputDigest, asset, orderedDiagnostics);
    }

    private static void ValidateCandidateFields(
        InertRasterAssetCandidate candidate,
        List<AssetDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(candidate.DisplayName) ||
            !string.Equals(candidate.DisplayName, candidate.DisplayName.Trim(), StringComparison.Ordinal) ||
            candidate.DisplayName.Any(char.IsControl))
        {
            diagnostics.Add(Error(
                "metadata_required",
                "display_name",
                "missing_or_noncanonical",
                null,
                "The display name must be non-empty, trimmed, and free of control characters."));
        }

        if (candidate.Provenance is null)
        {
            diagnostics.Add(Error(
                "provenance_required",
                "provenance",
                "missing",
                null,
                "An asset must retain source, creator, proposer, rights, and upstream provenance metadata."));
        }
        else
        {
            ValidateProvenance(candidate.Provenance, diagnostics);
        }

        if (candidate.Preview is null)
        {
            diagnostics.Add(Error(
                "preview_required",
                "preview",
                "missing",
                null,
                "An accepted asset must carry a digest-bound preview reference."));
        }
        else
        {
            ValidatePreview(candidate.Preview, diagnostics);
        }
    }

    private static void ValidateProvenance(
        AssetProvenance provenance,
        List<AssetDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(provenance.SourceKind) || provenance.SourceKind == AssetSourceKind.Unknown)
        {
            diagnostics.Add(Error(
                "provenance_required",
                "provenance.source_kind",
                "unknown",
                null,
                "The source kind must be explicitly declared."));
        }

        ValidateIdentity(provenance.CreatorId, "provenance.creator_id", diagnostics);
        ValidateIdentity(provenance.ProposerId, "provenance.proposer_id", diagnostics);

        if (provenance.Rights is null)
        {
            diagnostics.Add(Error(
                "rights_required",
                "provenance.rights",
                "missing",
                null,
                "Rights status and redistribution metadata are required."));
        }
        else
        {
            ValidateRights(provenance.Rights, diagnostics);
        }

        if (provenance.UpstreamAssetIds is null)
        {
            diagnostics.Add(Error(
                "provenance_required",
                "provenance.upstream_asset_ids",
                "missing",
                null,
                "The upstream asset list must be explicit, even when empty."));
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var upstreamAssetId in provenance.UpstreamAssetIds)
            {
                try
                {
                    AssetRules.ValidateCanonicalAssetId(upstreamAssetId);
                    if (!seen.Add(upstreamAssetId))
                    {
                        diagnostics.Add(Error(
                            "provenance_duplicate",
                            "provenance.upstream_asset_ids",
                            upstreamAssetId,
                            null,
                            "Upstream asset IDs must be unique."));
                    }
                }
                catch (ArgumentException)
                {
                    diagnostics.Add(Error(
                        "provenance_reference",
                        "provenance.upstream_asset_ids",
                        "invalid",
                        "canonical_asset_id",
                        "Upstream references must be canonical asset IDs."));
                }
                catch (FormatException)
                {
                    diagnostics.Add(Error(
                        "provenance_reference",
                        "provenance.upstream_asset_ids",
                        "invalid",
                        "canonical_asset_id",
                        "Upstream references must be canonical asset IDs."));
                }
            }

            if (provenance.SourceKind == AssetSourceKind.Transformed && provenance.UpstreamAssetIds.Count == 0)
            {
                diagnostics.Add(Error(
                    "provenance_required",
                    "provenance.upstream_asset_ids",
                    "empty",
                    "one_or_more",
                    "Transformed assets must retain at least one upstream asset reference."));
            }
        }
    }

    private static void ValidateRights(
        AssetRightsMetadata rights,
        List<AssetDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(rights.Status))
        {
            diagnostics.Add(Error(
                "rights_required",
                "provenance.rights.status",
                "invalid",
                null,
                "Rights status must be known, restricted, or explicitly unknown."));
        }
        else if (rights.Status == AssetRightsStatus.Unknown)
        {
            diagnostics.Add(Warning(
                "rights_unknown",
                "provenance.rights.status",
                "unknown",
                "known_or_restricted",
                "Unknown rights are retained for local use but block export by default."));
        }

        if (!Enum.IsDefined(rights.Redistribution))
        {
            diagnostics.Add(Error(
                "rights_required",
                "provenance.rights.redistribution",
                "invalid",
                null,
                "Redistribution status must be explicit."));
        }
        else if (rights.Redistribution == AssetRedistributionStatus.Unknown)
        {
            diagnostics.Add(Warning(
                "redistribution_unknown",
                "provenance.rights.redistribution",
                "unknown",
                "allowed_or_restricted",
                "Unknown redistribution status blocks export by default."));
        }

        if (rights.Status != AssetRightsStatus.Unknown &&
            string.IsNullOrWhiteSpace(rights.LicenseId) &&
            string.IsNullOrWhiteSpace(rights.BundledLicenseReference))
        {
            diagnostics.Add(Error(
                "rights_license_required",
                "provenance.rights.license",
                "missing",
                "license_id_or_bundled_reference",
                "Known or restricted rights must name a license or bundled license reference."));
        }

        if (!string.IsNullOrWhiteSpace(rights.LicenseId))
        {
            ValidateOpaqueReference(rights.LicenseId, "provenance.rights.license_id", diagnostics);
        }

        if (!string.IsNullOrWhiteSpace(rights.BundledLicenseReference))
        {
            ValidateOpaqueReference(
                rights.BundledLicenseReference,
                "provenance.rights.bundled_license_reference",
                diagnostics);
        }

        if (rights.AttributionNotices is null)
        {
            diagnostics.Add(Error(
                "rights_required",
                "provenance.rights.attribution_notices",
                "missing",
                "explicit_list",
                "Attribution notices must be represented by an explicit list."));
        }
        else
        {
            foreach (var notice in rights.AttributionNotices)
            {
                if (string.IsNullOrWhiteSpace(notice) || notice.Any(char.IsControl))
                {
                    diagnostics.Add(Error(
                        "rights_notice",
                        "provenance.rights.attribution_notices",
                        "invalid",
                        "non_empty_text",
                        "Attribution notices must be non-empty text without control characters."));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rights.RightsHolder))
        {
            ValidateIdentity(rights.RightsHolder, "provenance.rights.rights_holder", diagnostics);
        }
    }

    private static void ValidatePreview(
        AssetPreviewReference preview,
        List<AssetDiagnostic> diagnostics)
    {
        try
        {
            ContentPackageRules.ValidateLocalId(preview.PreviewId);
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Error(
                "preview_reference",
                "preview.id",
                "invalid",
                "canonical_local_id",
                "Preview references must use a canonical local identifier."));
        }

        try
        {
            ContentPackageRules.ValidateDigest(preview.PreviewDigest, nameof(preview.PreviewDigest));
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Error(
                "preview_reference",
                "preview.digest",
                "invalid",
                "sha256:<64-hex>",
                "Preview references must bind to a lowercase SHA-256 digest."));
        }
    }

    private static void ValidateIdentity(
        string? value,
        string field,
        List<AssetDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl) ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains('\\'))
        {
            diagnostics.Add(Error(
                "provenance_required",
                field,
                "missing_or_noncanonical",
                "opaque_identity",
                "Provenance identities must be explicit opaque values, not paths or URLs."));
        }
    }

    private static void ValidateOpaqueReference(
        string value,
        string field,
        List<AssetDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl) ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.StartsWith('/') ||
            value.Contains("..", StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "rights_reference",
                field,
                "invalid",
                "inert_reference",
                "Rights references must be inert identifiers, not filesystem or network locations."));
        }
    }

    private static PngParseResult ParsePng(ReadOnlySpan<byte> bytes, AssetBudgetPolicy budget)
    {
        var diagnostics = new List<AssetDiagnostic>();
        if (bytes.Length < PngSignature.Length || !bytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return Malformed("png.signature", "PNG signature is missing or invalid.");
        }

        var offset = PngSignature.Length;
        var seenIhdr = false;
        var seenIend = false;
        var seenIdat = false;
        var seenFdat = false;
        var seenActl = false;
        var seenSrgb = false;
        var seenTrns = false;
        var indexedPaletteEntries = 0;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = default(AssetPngColorType);
        var hasAlpha = false;
        var interlaced = false;
        var colorProfile = AssetPngColorProfile.Unspecified;
        var declaredFrameCount = 0u;
        var loopCount = 0u;
        var frameDurations = new List<int>();
        long durationTotal = 0;
        uint expectedSequence = 0;

        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12)
            {
                return Malformed("png.chunks", "PNG ended inside a chunk header or trailer.");
            }

            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var remainingAfterHeader = bytes.Length - offset - 12;
            if (chunkLength > (uint)remainingAfterHeader)
            {
                return Malformed("png.chunk_length", "PNG chunk length exceeds the remaining input bytes.");
            }

            var dataLength = (int)chunkLength;
            var typeOffset = offset + 4;
            var dataOffset = offset + 8;
            var crcOffset = dataOffset + dataLength;
            var chunkType = bytes.Slice(typeOffset, 4);
            var chunkData = bytes.Slice(dataOffset, dataLength);
            var suppliedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(crcOffset, 4));
            if (!IsValidChunkType(chunkType) || PngCrc(chunkType, chunkData) != suppliedCrc)
            {
                return Malformed("png.chunk_crc", "PNG contains an invalid chunk type or CRC.");
            }

            if (chunkType.SequenceEqual("IHDR"u8))
            {
                if (seenIhdr || offset != PngSignature.Length || dataLength != 13)
                {
                    return Malformed("png.ihdr", "PNG must contain one 13-byte IHDR as its first chunk.");
                }

                seenIhdr = true;
                var rawWidth = BinaryPrimitives.ReadUInt32BigEndian(chunkData[..4]);
                var rawHeight = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(4, 4));
                if (rawWidth == 0 || rawHeight == 0 || rawWidth > int.MaxValue || rawHeight > int.MaxValue)
                {
                    return Malformed("png.dimensions", "PNG dimensions must be non-zero signed integers.");
                }

                width = (int)rawWidth;
                height = (int)rawHeight;
                bitDepth = chunkData[8];
                if (!Enum.IsDefined((AssetPngColorType)chunkData[9]) ||
                    chunkData[10] != 0 || chunkData[11] != 0 || chunkData[12] > 1)
                {
                    return Malformed("png.ihdr", "PNG compression, filter, color, or interlace metadata is unsupported.");
                }

                colorType = (AssetPngColorType)chunkData[9];
                if (!ValidBitDepth(colorType, bitDepth))
                {
                    return Malformed("png.bit_depth", "PNG bit depth is invalid for its color type.");
                }

                hasAlpha = colorType is AssetPngColorType.GrayscaleAlpha or AssetPngColorType.TruecolorAlpha;
                interlaced = chunkData[12] == 1;
                if (width > budget.MaxWidth)
                {
                    diagnostics.Add(Budget("width", width, budget.MaxWidth, "PNG width exceeds the asset budget."));
                }

                if (height > budget.MaxHeight)
                {
                    diagnostics.Add(Budget("height", height, budget.MaxHeight, "PNG height exceeds the asset budget."));
                }

                if (diagnostics.Count > 0)
                {
                    return new PngParseResult(null, diagnostics);
                }
            }
            else if (!seenIhdr)
            {
                return Malformed("png.ihdr", "PNG contains a chunk before IHDR.");
            }
            else if (chunkType.SequenceEqual("PLTE"u8))
            {
                if (seenIdat || dataLength == 0 || dataLength % 3 != 0 || dataLength > 768)
                {
                    return Malformed("png.palette", "PNG palette metadata is malformed or appears after image data.");
                }

                indexedPaletteEntries = dataLength / 3;
            }
            else if (chunkType.SequenceEqual("tRNS"u8))
            {
                if (seenIdat || seenTrns)
                {
                    return Malformed("png.transparency", "PNG transparency metadata is duplicated or appears after image data.");
                }

                seenTrns = true;
                hasAlpha = true;
            }
            else if (chunkType.SequenceEqual("sRGB"u8))
            {
                if (seenSrgb || dataLength != 1 || chunkData[0] > 3)
                {
                    return Malformed("png.color_profile", "PNG sRGB metadata is malformed or duplicated.");
                }

                seenSrgb = true;
                colorProfile = AssetPngColorProfile.Srgb;
            }
            else if (chunkType.SequenceEqual("acTL"u8))
            {
                if (seenActl || seenIdat || dataLength != 8)
                {
                    return Malformed("png.animation", "PNG animation control metadata is malformed or misplaced.");
                }

                seenActl = true;
                declaredFrameCount = BinaryPrimitives.ReadUInt32BigEndian(chunkData[..4]);
                loopCount = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(4, 4));
                if (declaredFrameCount == 0)
                {
                    return Malformed("png.animation.frames", "PNG animation must declare at least one frame.");
                }

                if (declaredFrameCount > (uint)budget.MaxFrames)
                {
                    diagnostics.Add(Budget(
                        "frame_count",
                        declaredFrameCount,
                        budget.MaxFrames,
                        "The declared animation frame count exceeds the asset budget."));
                    return new PngParseResult(null, diagnostics);
                }
            }
            else if (chunkType.SequenceEqual("fcTL"u8))
            {
                if (!seenActl || dataLength != 26 || frameDurations.Count >= budget.MaxFrames)
                {
                    return Malformed("png.animation.frame_control", "PNG frame control metadata is malformed or exceeds the frame budget.");
                }

                var sequence = BinaryPrimitives.ReadUInt32BigEndian(chunkData[..4]);
                if (sequence != expectedSequence)
                {
                    return Malformed("png.animation.sequence", "PNG animation sequence numbers are not contiguous.");
                }

                expectedSequence = checked(expectedSequence + 1);
                var frameWidth = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(4, 4));
                var frameHeight = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(8, 4));
                var frameX = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(12, 4));
                var frameY = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(16, 4));
                if (frameWidth == 0 || frameHeight == 0 ||
                    frameWidth > (uint)width || frameHeight > (uint)height ||
                    frameX > (uint)width - frameWidth || frameY > (uint)height - frameHeight ||
                    chunkData[24] > 1 || chunkData[25] > 1)
                {
                    return Malformed("png.animation.frame_bounds", "PNG frame bounds or dispose/blend metadata is invalid.");
                }

                var delayNumerator = BinaryPrimitives.ReadUInt16BigEndian(chunkData.Slice(20, 2));
                var delayDenominator = BinaryPrimitives.ReadUInt16BigEndian(chunkData.Slice(22, 2));
                var denominator = delayDenominator == 0 ? 100L : delayDenominator;
                var durationNumerator = delayNumerator * 1000L;
                if (durationNumerator == 0 || durationNumerator % denominator != 0)
                {
                    return Malformed("png.animation.timing", "PNG frame timing must normalize to a positive integer millisecond duration.");
                }

                var duration = durationNumerator / denominator;
                if (duration > int.MaxValue)
                {
                    return Malformed("png.animation.timing", "PNG frame timing exceeds the supported integer millisecond range.");
                }

                frameDurations.Add((int)duration);
                durationTotal = checked(durationTotal + duration);
                if (durationTotal > budget.MaxAnimationDurationMilliseconds)
                {
                    diagnostics.Add(Budget(
                        "animation_duration_ms",
                        durationTotal,
                        budget.MaxAnimationDurationMilliseconds,
                        "The animation duration exceeds the asset budget."));
                    return new PngParseResult(null, diagnostics);
                }
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (!seenIdat && seenActl && frameDurations.Count == 0)
                {
                    return Malformed("png.animation.frame_control", "An animated PNG image frame must have a preceding fcTL chunk.");
                }

                seenIdat = true;
            }
            else if (chunkType.SequenceEqual("fdAT"u8))
            {
                if (!seenActl || !seenIdat || dataLength < 4 || frameDurations.Count < 2)
                {
                    return Malformed("png.animation.frame_data", "PNG frame data is malformed or appears before the first image frame.");
                }

                var sequence = BinaryPrimitives.ReadUInt32BigEndian(chunkData[..4]);
                if (sequence != expectedSequence)
                {
                    return Malformed("png.animation.sequence", "PNG animation sequence numbers are not contiguous.");
                }

                expectedSequence = checked(expectedSequence + 1);
                seenFdat = true;
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                if (dataLength != 0 || seenIend || offset + 12 != bytes.Length)
                {
                    return Malformed("png.iend", "PNG IEND must be empty and terminate the input.");
                }

                seenIend = true;
            }

            offset = crcOffset + 4;
            if (seenIend)
            {
                break;
            }
        }

        if (!seenIhdr || !seenIend || !seenIdat || !Enum.IsDefined(colorType))
        {
            return Malformed("png.structure", "PNG must contain IHDR, image data, and a terminal IEND.");
        }

        if (colorType == AssetPngColorType.Indexed && indexedPaletteEntries == 0)
        {
            return Malformed("png.palette", "Indexed PNGs must declare a palette before image data.");
        }

        if (seenActl)
        {
            if (declaredFrameCount != (uint)frameDurations.Count || frameDurations.Count == 0 || !seenFdat && frameDurations.Count > 1)
            {
                return Malformed("png.animation.frames", "PNG animation frame declarations do not match its image data.");
            }
        }
        else if (frameDurations.Count != 0 || seenFdat)
        {
            return Malformed("png.animation", "PNG contains animation data without an animation control chunk.");
        }

        var frameCount = seenActl ? frameDurations.Count : 1;
        var normalizedDurations = seenActl ? frameDurations.ToArray() : [0];
        var sampleRate = 0;
        if (frameCount > 1)
        {
            var timingQuantum = normalizedDurations.Aggregate(GreatestCommonDivisor);
            if (timingQuantum <= 0 || 1000 % timingQuantum != 0)
            {
                return Malformed("png.animation.sample_rate", "PNG animation timing does not produce an integer sample rate.");
            }

            sampleRate = 1000 / timingQuantum;
        }

        return new PngParseResult(
            new AssetPngMetadata(
                width,
                height,
                colorType,
                bitDepth,
                hasAlpha,
                interlaced,
                colorProfile,
                frameCount,
                checked((int)durationTotal),
                sampleRate,
                normalizedDurations,
                checked((int)Math.Min(loopCount, int.MaxValue))),
            diagnostics);
    }

    private static string CanonicalPngIdentity(string originalDigest, AssetPngMetadata png) =>
        string.Join(
            '|',
            "normalized-png-v1",
            originalDigest,
            png.Width.ToString(CultureInfo.InvariantCulture),
            png.Height.ToString(CultureInfo.InvariantCulture),
            AssetRules.CanonicalColorType(png.ColorType),
            png.BitDepth.ToString(CultureInfo.InvariantCulture),
            png.HasAlpha ? "alpha" : "opaque",
            png.IsInterlaced ? "interlaced" : "non-interlaced",
            png.ColorProfile == AssetPngColorProfile.Srgb ? "srgb" : "unspecified",
            png.FrameCount.ToString(CultureInfo.InvariantCulture),
            png.AnimationDurationMilliseconds.ToString(CultureInfo.InvariantCulture),
            png.AnimationSampleRateHz.ToString(CultureInfo.InvariantCulture),
            png.LoopCount.ToString(CultureInfo.InvariantCulture),
            string.Join(',', png.FrameDurationsMilliseconds.Select(value => value.ToString(CultureInfo.InvariantCulture))));

    private static string CanonicalMetadata(
        InertRasterAssetCandidate candidate,
        string assetId,
        string originalDigest,
        string normalizedDigest,
        AssetPngMetadata png)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        AppendString(builder, "schema", "clankerworld.inert-raster.v1");
        AppendString(builder, "assetId", assetId);
        AppendString(builder, "displayName", candidate.DisplayName.Trim());
        AppendString(builder, "originalDigest", originalDigest);
        AppendString(builder, "normalizedDigest", normalizedDigest);
        AppendString(builder, "format", "png");
        builder.Append(",\"png\":{");
        AppendNumber(builder, "width", png.Width);
        AppendNumber(builder, "height", png.Height);
        AppendString(builder, "colorType", AssetRules.CanonicalColorType(png.ColorType));
        AppendNumber(builder, "bitDepth", png.BitDepth);
        AppendBoolean(builder, "hasAlpha", png.HasAlpha);
        AppendBoolean(builder, "interlaced", png.IsInterlaced);
        AppendString(builder, "colorProfile", png.ColorProfile == AssetPngColorProfile.Srgb ? "srgb" : "unspecified");
        AppendNumber(builder, "frameCount", png.FrameCount);
        AppendNumber(builder, "durationMs", png.AnimationDurationMilliseconds);
        AppendNumber(builder, "sampleRateHz", png.AnimationSampleRateHz);
        AppendNumber(builder, "loopCount", png.LoopCount);
        AppendNumberArray(builder, "frameDurationsMs", png.FrameDurationsMilliseconds);
        builder.Append('}');

        builder.Append(",\"provenance\":{");
        AppendString(builder, "sourceKind", AssetRules.CanonicalSourceKind(candidate.Provenance.SourceKind));
        AppendString(builder, "creatorId", candidate.Provenance.CreatorId.Trim());
        AppendString(builder, "proposerId", candidate.Provenance.ProposerId.Trim());
        AppendStringArray(builder, "upstreamAssetIds", candidate.Provenance.UpstreamAssetIds.Order(StringComparer.Ordinal));
        builder.Append(",\"rights\":{");
        AppendString(builder, "status", AssetRules.CanonicalRightsStatus(candidate.Provenance.Rights.Status));
        AppendNullableString(builder, "rightsHolder", candidate.Provenance.Rights.RightsHolder?.Trim());
        AppendNullableString(builder, "licenseId", candidate.Provenance.Rights.LicenseId?.Trim());
        AppendNullableString(builder, "bundledLicenseReference", candidate.Provenance.Rights.BundledLicenseReference?.Trim());
        AppendString(builder, "redistribution", AssetRules.CanonicalRedistributionStatus(candidate.Provenance.Rights.Redistribution));
        AppendStringArray(builder, "attributionNotices", candidate.Provenance.Rights.AttributionNotices.Select(notice => notice.Trim()).Order(StringComparer.Ordinal));
        builder.Append('}');
        builder.Append('}');

        builder.Append(",\"preview\":{");
        AppendString(builder, "id", candidate.Preview.PreviewId.Trim());
        AppendString(builder, "digest", candidate.Preview.PreviewDigest);
        builder.Append('}');
        builder.Append('}');
        return builder.ToString();
    }

    private static AssetProvenance NormalizeProvenance(AssetProvenance provenance) =>
        provenance with
        {
            CreatorId = provenance.CreatorId.Trim(),
            ProposerId = provenance.ProposerId.Trim(),
            UpstreamAssetIds = provenance.UpstreamAssetIds.Order(StringComparer.Ordinal).ToArray(),
            Rights = provenance.Rights with
            {
                RightsHolder = provenance.Rights.RightsHolder?.Trim(),
                LicenseId = provenance.Rights.LicenseId?.Trim(),
                BundledLicenseReference = provenance.Rights.BundledLicenseReference?.Trim(),
                AttributionNotices = provenance.Rights.AttributionNotices.Select(notice => notice.Trim()).Order(StringComparer.Ordinal).ToArray(),
            },
        };

    private static bool ValidBitDepth(AssetPngColorType colorType, int bitDepth) => colorType switch
    {
        AssetPngColorType.Grayscale => bitDepth is 1 or 2 or 4 or 8 or 16,
        AssetPngColorType.Truecolor => bitDepth is 8 or 16,
        AssetPngColorType.Indexed => bitDepth is 1 or 2 or 4 or 8,
        AssetPngColorType.GrayscaleAlpha or AssetPngColorType.TruecolorAlpha => bitDepth is 8 or 16,
        _ => false,
    };

    private static bool IsValidChunkType(ReadOnlySpan<byte> type)
    {
        if (type.Length != 4)
        {
            return false;
        }

        for (var index = 0; index < type.Length; index++)
        {
            if ((type[index] is < (byte)'A' or > (byte)'Z') &&
                (type[index] is < (byte)'a' or > (byte)'z'))
            {
                return false;
            }
        }

        return (type[2] & 0x20) == 0;
    }

    private static uint PngCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in type)
        {
            crc = CrcByte(crc, value);
        }

        foreach (var value in data)
        {
            crc = CrcByte(crc, value);
        }

        return ~crc;
    }

    private static uint CrcByte(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
        }

        return crc;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return Math.Abs(left);
    }

    private static PngParseResult Malformed(string field, string message) =>
        new(null, [Error("malformed_input", field, "invalid", null, message)]);

    private static AssetDiagnostic Error(
        string code,
        string field,
        string observed,
        string? limit,
        string message) =>
        new(AssetDiagnosticSeverity.Error, code, field, message, observed, limit);

    private static AssetDiagnostic Warning(
        string code,
        string field,
        string observed,
        string? limit,
        string message) =>
        new(AssetDiagnosticSeverity.Warning, code, field, message, observed, limit);

    private static AssetDiagnostic Budget(
        string field,
        long observed,
        long limit,
        string message,
        string code = "asset_budget_breach") =>
        Error(
            code,
            field,
            observed.ToString(CultureInfo.InvariantCulture),
            limit.ToString(CultureInfo.InvariantCulture),
            message);

    private static AssetDiagnostic[] OrderDiagnostics(IEnumerable<AssetDiagnostic> diagnostics) => diagnostics
        .OrderBy(diagnostic => diagnostic.Severity)
        .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Field, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.ObservedValue, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Limit, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
        .ToArray();

    private static void AppendString(StringBuilder builder, string name, string value)
    {
        AppendComma(builder);
        builder.Append(JsonSerializer.Serialize(name));
        builder.Append(':');
        builder.Append(JsonSerializer.Serialize(value));
    }

    private static void AppendNullableString(StringBuilder builder, string name, string? value)
    {
        AppendComma(builder);
        builder.Append(JsonSerializer.Serialize(name));
        builder.Append(':');
        builder.Append(value is null ? "null" : JsonSerializer.Serialize(value));
    }

    private static void AppendNumber(StringBuilder builder, string name, int value)
    {
        AppendComma(builder);
        builder.Append(JsonSerializer.Serialize(name));
        builder.Append(':');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendBoolean(StringBuilder builder, string name, bool value)
    {
        AppendComma(builder);
        builder.Append(JsonSerializer.Serialize(name));
        builder.Append(':');
        builder.Append(value ? "true" : "false");
    }

    private static void AppendNumberArray(StringBuilder builder, string name, IEnumerable<int> values)
    {
        AppendComma(builder);
        builder.Append(JsonSerializer.Serialize(name));
        builder.Append(": [".Trim());
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(']');
    }

    private static void AppendStringArray(StringBuilder builder, string name, IEnumerable<string> values)
    {
        AppendComma(builder);
        builder.Append(JsonSerializer.Serialize(name));
        builder.Append(": [".Trim());
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(JsonSerializer.Serialize(value));
        }

        builder.Append(']');
    }

    private static void AppendComma(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '{' && builder[^1] != '[' && builder[^1] != ',')
        {
            builder.Append(',');
        }
    }

    private sealed record PngParseResult(
        AssetPngMetadata? Metadata,
        IReadOnlyList<AssetDiagnostic> Diagnostics);
}
