using System.Text.Json;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Immutable, host-owned allow-list for authored asset references. It is read
/// once during host startup; owner-device requests can name a reference but
/// cannot change this catalog or upload asset bytes.
/// </summary>
public sealed class ApprovedAssetCatalog : IOwnerApprovedAssetReferencePolicy
{
    /// <summary>
    /// The only catalog document version understood by this host. Unknown
    /// versions are rejected rather than guessed across a trust boundary.
    /// </summary>
    public const int SchemaVersion = 1;

    private readonly HashSet<OwnerApprovedAssetReference> approvedReferences;

    private ApprovedAssetCatalog(
        string path,
        IEnumerable<OwnerApprovedAssetReference> approvedReferences)
    {
        Path = path;
        this.approvedReferences = new HashSet<OwnerApprovedAssetReference>(approvedReferences);
    }

    /// <summary>
    /// The configured catalog path. A missing file intentionally represents an
    /// empty catalog; it never grants an implicit approval.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Loads a complete, valid catalog snapshot. Missing files fail closed to
    /// an empty catalog. Existing files that are malformed or ambiguous throw
    /// during startup so a partial operator update can never become an
    /// accidental allow-list.
    /// </summary>
    public static ApprovedAssetCatalog LoadOrDeny(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new ApprovedAssetCatalog(fullPath, []);
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(fullPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            return new ApprovedAssetCatalog(fullPath, ParseDocument(document.RootElement));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The approved-asset catalog is not valid JSON.", exception);
        }
    }

    /// <inheritdoc />
    public bool IsApproved(OwnerApprovedAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return approvedReferences.Contains(reference);
    }

    /// <summary>
    /// Returns a detached deterministic view useful only for diagnostics and
    /// tests. It deliberately exposes references, not catalog mutation.
    /// </summary>
    public IReadOnlyList<OwnerApprovedAssetReference> GetApprovedReferences() => approvedReferences
        .OrderBy(reference => reference.AssetId, StringComparer.Ordinal)
        .ThenBy(reference => reference.AssetDigest, StringComparer.Ordinal)
        .Select(reference => reference with { })
        .ToArray();

    private static List<OwnerApprovedAssetReference> ParseDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The approved-asset catalog root must be an object.");
        }

        var properties = ReadProperties(root, "approved-asset catalog");
        if (!properties.TryGetValue("schemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out var version) ||
            version != SchemaVersion)
        {
            throw new InvalidDataException(
                $"The approved-asset catalog must declare schemaVersion {SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        if (!properties.TryGetValue("references", out var references) ||
            references.ValueKind != JsonValueKind.Array ||
            properties.Keys.Any(name => name is not "schemaVersion" and not "references"))
        {
            throw new InvalidDataException(
                "The approved-asset catalog must contain only schemaVersion and references.");
        }

        var result = new List<OwnerApprovedAssetReference>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in references.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Every approved-asset reference must be an object.");
            }

            var entry = ReadProperties(element, "approved-asset reference");
            if (!entry.TryGetValue("assetId", out var assetIdElement) ||
                !entry.TryGetValue("assetDigest", out var assetDigestElement) ||
                assetIdElement.ValueKind != JsonValueKind.String ||
                assetDigestElement.ValueKind != JsonValueKind.String ||
                entry.Keys.Any(name => name is not "assetId" and not "assetDigest"))
            {
                throw new InvalidDataException(
                    "Every approved-asset reference must contain only assetId and assetDigest strings.");
            }

            var assetId = assetIdElement.GetString()!;
            var assetDigest = assetDigestElement.GetString()!;
            if (!IsCanonicalAssetId(assetId) || !IsCanonicalSha256Digest(assetDigest))
            {
                throw new InvalidDataException(
                    "Approved asset references require a canonical asset ID and a lowercase sha256:<64-hex> digest.");
            }

            if (!ids.Add(assetId))
            {
                throw new InvalidDataException("Approved asset references cannot reuse an asset ID.");
            }

            result.Add(new OwnerApprovedAssetReference(assetId, assetDigest));
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ReadProperties(JsonElement element, string label)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException($"The {label} contains a duplicate '{property.Name}' property.");
            }
        }

        return result;
    }

    private static bool IsCanonicalAssetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCanonicalSha256Digest(string value)
    {
        const string prefix = "sha256:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 64)
        {
            return false;
        }

        foreach (var character in value[prefix.Length..])
        {
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
