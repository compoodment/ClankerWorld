using System.Globalization;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Scalar, signed transport records for the private-world data-only content
/// lifecycle. The wire format never accepts a client-supplied lock digest or
/// activation tick; those are authoritative server outputs.
/// </summary>
public sealed record OwnerContentDependencyAction(
    string PackageId,
    string MinimumVersion,
    string MaximumExclusiveVersion,
    bool Optional);

public sealed record OwnerContentDefinitionAction(
    string Kind,
    string LocalId,
    string Version,
    string DisplayName,
    string PayloadDigest,
    string? PayloadJson = null);

public sealed record OwnerContentAssetReservationAction(
    string AssetId,
    string NormalizedDigest,
    string DecodeProfile,
    long DurableStorageBytes,
    long DecodedCacheBytes,
    long GpuBytes,
    int RenderUnits);

public sealed record OwnerContentPackageAction(
    string PackageId,
    string Version,
    string PackageDigest,
    IReadOnlyList<OwnerContentDependencyAction> Dependencies,
    IReadOnlyList<OwnerContentDefinitionAction> Definitions,
    IReadOnlyList<string> DeclaredCapabilities,
    IReadOnlyList<OwnerContentAssetReservationAction>? Assets = null);

public sealed record OwnerContentPackageIdAction(string PackageId);

public sealed record OwnerContentRollbackAction(string PackageId, string Reason);

public sealed record OwnerBuildingPlacementAction(
    string InstanceId,
    string DefinitionId,
    int X,
    int Y);

public sealed record OwnerProductionStartAction(
    string RecipeId,
    string BuildingInstanceId,
    string WorkerId);

public sealed record OwnerContentPackageReceipt(
    string Operation,
    bool Applied,
    string PackageId,
    string Version,
    string PackageDigest,
    string Lifecycle,
    string? LockDigest,
    long? ValidationTick,
    long? StagedTick,
    long? ActivationTick,
    string? Failure,
    string? ManifestDigest = null)
{
    public static OwnerContentPackageReceipt From(
        string operation,
        ContentPackageRecord record) => new(
            operation,
            true,
            record.Manifest.PackageId,
            record.Manifest.Version.ToString(),
            record.Manifest.PackageDigest,
            record.Lifecycle.ToString().ToLowerInvariant(),
            record.LockDigest,
            record.ValidationTick,
            record.StagedTick,
            record.ActivationTick,
            null,
            record.ManifestDigest);
}

public static class OwnerContentBinding
{
    public static string ProposePayload(OwnerContentPackageAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(action.Dependencies);
        ArgumentNullException.ThrowIfNull(action.Definitions);
        ArgumentNullException.ThrowIfNull(action.DeclaredCapabilities);
        var assets = action.Assets ?? [];

        var lines = new List<string>
        {
            "clankerworld.owner-content-propose.v1",
            $"package-id={EncodeRequired(action.PackageId, nameof(action.PackageId))}",
            $"version={EncodeRequired(action.Version, nameof(action.Version))}",
            $"package-digest={EncodeRequired(action.PackageDigest, nameof(action.PackageDigest))}",
            $"dependency-count={action.Dependencies.Count.ToString(CultureInfo.InvariantCulture)}",
            $"definition-count={action.Definitions.Count.ToString(CultureInfo.InvariantCulture)}",
            $"asset-count={assets.Count.ToString(CultureInfo.InvariantCulture)}",
            $"capability-count={action.DeclaredCapabilities.Count.ToString(CultureInfo.InvariantCulture)}",
        };

        for (var index = 0; index < action.Dependencies.Count; index++)
        {
            var dependency = action.Dependencies[index] ?? throw new ArgumentException(
                "Content dependencies cannot contain null.",
                nameof(action));
            var prefix = $"dependency-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.package-id={EncodeRequired(dependency.PackageId, nameof(dependency.PackageId))}");
            lines.Add($"{prefix}.minimum={EncodeRequired(dependency.MinimumVersion, nameof(dependency.MinimumVersion))}");
            lines.Add($"{prefix}.maximum={EncodeRequired(dependency.MaximumExclusiveVersion, nameof(dependency.MaximumExclusiveVersion))}");
            lines.Add($"{prefix}.optional={dependency.Optional.ToString().ToLowerInvariant()}");
        }

        for (var index = 0; index < action.Definitions.Count; index++)
        {
            var definition = action.Definitions[index] ?? throw new ArgumentException(
                "Content definitions cannot contain null.",
                nameof(action));
            var prefix = $"definition-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.kind={EncodeRequired(definition.Kind, nameof(definition.Kind))}");
            lines.Add($"{prefix}.local-id={EncodeRequired(definition.LocalId, nameof(definition.LocalId))}");
            lines.Add($"{prefix}.version={EncodeRequired(definition.Version, nameof(definition.Version))}");
            lines.Add($"{prefix}.display-name={EncodeRequired(definition.DisplayName, nameof(definition.DisplayName))}");
            lines.Add($"{prefix}.payload-digest={EncodeRequired(definition.PayloadDigest, nameof(definition.PayloadDigest))}");
            lines.Add($"{prefix}.payload-json={EncodeOptional(definition.PayloadJson)}");
        }

        for (var index = 0; index < assets.Count; index++)
        {
            var asset = assets[index] ?? throw new ArgumentException(
                "Content asset reservations cannot contain null.",
                nameof(action));
            var prefix = $"asset-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.asset-id={EncodeRequired(asset.AssetId, nameof(asset.AssetId))}");
            lines.Add($"{prefix}.normalized-digest={EncodeRequired(asset.NormalizedDigest, nameof(asset.NormalizedDigest))}");
            lines.Add($"{prefix}.decode-profile={EncodeRequired(asset.DecodeProfile, nameof(asset.DecodeProfile))}");
            lines.Add($"{prefix}.durable-storage={asset.DurableStorageBytes.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{prefix}.decoded-cache={asset.DecodedCacheBytes.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{prefix}.gpu-bytes={asset.GpuBytes.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{prefix}.render-units={asset.RenderUnits.ToString(CultureInfo.InvariantCulture)}");
        }

        for (var index = 0; index < action.DeclaredCapabilities.Count; index++)
        {
            lines.Add($"capability-{index.ToString(CultureInfo.InvariantCulture)}={EncodeRequired(
                action.DeclaredCapabilities[index],
                nameof(action.DeclaredCapabilities))}");
        }

        return string.Join('\n', lines);
    }

    public static string PackageIdPayload(string operation, OwnerContentPackageIdAction action) => string.Join(
        '\n',
        "clankerworld.owner-content-lifecycle.v1",
        $"operation={EncodeRequired(operation, nameof(operation))}",
        $"package-id={EncodeRequired(action.PackageId, nameof(action.PackageId))}");

    public static string RollbackPayload(OwnerContentRollbackAction action) => string.Join(
        '\n',
        "clankerworld.owner-content-rollback.v1",
        $"package-id={EncodeRequired(action.PackageId, nameof(action.PackageId))}",
        $"reason={EncodeRequired(action.Reason, nameof(action.Reason))}");

    public static string BuildingPlacementPayload(OwnerBuildingPlacementAction action) => string.Join(
        '\n',
        "clankerworld.owner-building-placement.v1",
        $"instance-id={EncodeRequired(action.InstanceId, nameof(action.InstanceId))}",
        $"definition-id={EncodeRequired(action.DefinitionId, nameof(action.DefinitionId))}",
        $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
        $"y={action.Y.ToString(CultureInfo.InvariantCulture)}");

    public static string ProductionStartPayload(OwnerProductionStartAction action) => string.Join(
        '\n',
        "clankerworld.owner-production-start.v1",
        $"recipe-id={EncodeRequired(action.RecipeId, nameof(action.RecipeId))}",
        $"building-instance-id={EncodeRequired(action.BuildingInstanceId, nameof(action.BuildingInstanceId))}",
        $"worker-id={EncodeRequired(action.WorkerId, nameof(action.WorkerId))}");

    public static bool TryMapManifest(
        OwnerContentPackageAction? action,
        out ContentPackageManifest? manifest,
        out string failure)
    {
        manifest = null;
        failure = string.Empty;
        if (action is null || action.Dependencies is null || action.Definitions is null || action.DeclaredCapabilities is null)
        {
            failure = "A content package and all of its dependency, definition, and capability lists are required.";
            return false;
        }

        try
        {
            var dependencies = action.Dependencies
                .Select(dependency =>
                {
                    ArgumentNullException.ThrowIfNull(dependency);
                    return new ContentDependency(
                        dependency.PackageId,
                        new ContentVersionRange(
                            ContentVersion.Parse(dependency.MinimumVersion),
                            ContentVersion.Parse(dependency.MaximumExclusiveVersion)),
                        dependency.Optional);
                })
                .ToArray();
            var definitions = action.Definitions
                .Select(definition =>
                {
                    ArgumentNullException.ThrowIfNull(definition);
                    return new ContentDefinition(
                        definition.Kind,
                        definition.LocalId,
                        ContentVersion.Parse(definition.Version),
                        definition.DisplayName,
                        definition.PayloadDigest,
                        definition.PayloadJson);
                })
                .ToArray();
            var assets = (action.Assets ?? [])
                .Select(asset =>
                {
                    ArgumentNullException.ThrowIfNull(asset);
                    return new WorldAssetReservationRequest(
                        asset.AssetId,
                        asset.NormalizedDigest,
                        asset.DecodeProfile,
                        asset.DurableStorageBytes,
                        asset.DecodedCacheBytes,
                        asset.GpuBytes,
                        asset.RenderUnits);
                })
                .ToArray();
            manifest = new ContentPackageManifest(
                action.PackageId,
                ContentVersion.Parse(action.Version),
                action.PackageDigest,
                dependencies,
                definitions,
                action.DeclaredCapabilities.ToArray(),
                assets);
            manifest.Validate();
            return true;
        }
        catch (ArgumentException exception)
        {
            failure = exception.Message;
            return false;
        }
        catch (FormatException exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static string EncodeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string EncodeOptional(string? value) => value is null
        ? "-"
        : Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
