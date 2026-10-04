using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // Cache only private dependency descriptors, not manifests that the registry
    // exposes through saved state. Waiting for a missing dependency stays cheap,
    // and each world's eventual package still receives its own fresh manifest.
    private static readonly Lazy<Dictionary<string, ContentDependency[]>> BuildingVariantDependencies = new(() =>
        new[]
        {
            FarmhouseVariantContent.Create(), BlacksmithVariantContent.Create(), TailorVariantContent.Create(),
            ClinicVariantContent.Create(), RestaurantVariantContent.Create(),
        }.ToDictionary(manifest => manifest.PackageId, manifest => manifest.Dependencies.ToArray(), StringComparer.Ordinal));

    private void StageBuildingVariantContent()
    {
        // None of these sidecars depends on another sidecar, so one registry
        // snapshot is sufficient for all five staging decisions.
        var packages = contentRegistry.ExportState().Packages;
        var active = packages.Where(package => package.Lifecycle == ContentPackageLifecycle.Active).ToArray();
        StageBuildingVariantContent(FarmhouseVariantContent.PackageId, FarmhouseVariantContent.Create, packages, active);
        StageBuildingVariantContent(BlacksmithVariantContent.PackageId, BlacksmithVariantContent.Create, packages, active);
        StageBuildingVariantContent(TailorVariantContent.PackageId, TailorVariantContent.Create, packages, active);
        StageBuildingVariantContent(ClinicVariantContent.PackageId, ClinicVariantContent.Create, packages, active);
        StageBuildingVariantContent(RestaurantVariantContent.PackageId, RestaurantVariantContent.Create, packages, active);
    }

    private void StageBuildingVariantContent(string packageId, Func<ContentPackageManifest> create,
        IReadOnlyList<ContentPackageRecord> packages, ContentPackageRecord[] active)
    {
        // Existing records include deliberate owner rollback and quarantine decisions.
        if (packages.Any(package => package.Manifest.PackageId == packageId))
            return;

        if (!BuildingVariantDependencies.Value[packageId].All(dependency => active.Any(package =>
                package.Manifest.PackageId == dependency.PackageId &&
                dependency.VersionRange.Contains(package.Manifest.Version))))
            return;

        var manifest = create();
        var resolution = ContentPackageResolver.Resolve(active.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("building_variant_content_staged", manifest.PackageId);
    }
}
