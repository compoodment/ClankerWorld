using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Copies a shipped workstation's work into a separately identified building size.</summary>
internal static class WorkstationVariantContent
{
    internal static ContentDependency Require(string packageId, string minimumVersion) =>
        new(packageId, new(ContentVersion.Parse(minimumVersion), ContentVersion.Parse("2.0.0")));

    internal static ContentPackageManifest Create(string packageId, string localId, string displayName,
        int width, int height, IReadOnlyList<ContentQuantity> buildCosts, BuildingDefinition original,
        IReadOnlyList<ContentPackageManifest> sourcePackages, IReadOnlyList<ContentDependency> dependencies)
    {
        var version = ContentVersion.Parse("1.0.0");
        var source = new DeclarativeWorldContentState([], []);
        foreach (var package in sourcePackages)
            source = ContentDefinitionPayloadCodec.ApplyPackage(source, package);
        var recipes = source.Recipes.Where(recipe => recipe.WorkstationBuildingId == original.CanonicalId)
            .OrderBy(recipe => recipe.CanonicalId, StringComparer.Ordinal).ToArray();
        if (recipes.Length == 0)
            throw new InvalidOperationException("A workstation variant requires its original recipes.");

        var costs = buildCosts.OrderBy(cost => cost.ResourceId, StringComparer.Ordinal).ToArray();
        var requiredPackages = dependencies.OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal).ToArray();
        // Hash the complete semantic payload before assigning the final IDs.
        // The workstation's local ID stands in for its not-yet-known canonical
        // ID, avoiding a digest cycle without ignoring any recipe semantics.
        var descriptor = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = "clankerworld-workstation-variant/v1",
            PackageId = packageId,
            Version = version.ToString(),
            Building = new
            {
                LocalId = localId,
                DisplayName = displayName,
                Width = width,
                Height = height,
                Capacity = 1,
                BuildCosts = costs,
                original.Tags,
                SourceDefinitionId = original.CanonicalId,
                SourcePayloadDigest = original.PayloadDigest,
            },
            Recipes = recipes.Select(recipe => new
            {
                LocalId = localId + "-" + recipe.LocalId,
                recipe.DisplayName,
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                WorkstationLocalId = localId,
                recipe.Tags,
                SourceDefinitionId = recipe.CanonicalId,
                SourcePayloadDigest = recipe.PayloadDigest,
            }).ToArray(),
            Dependencies = requiredPackages.Select(dependency => new
            {
                dependency.PackageId,
                Minimum = dependency.VersionRange.Minimum.ToString(),
                MaximumExclusive = dependency.VersionRange.MaximumExclusive.ToString(),
                dependency.Optional,
            }).ToArray(),
        });
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(descriptor));
        var building = new BuildingDefinition(digest, localId, version, displayName,
            width, height, 1, costs, original.Tags);
        var companions = recipes.Select(recipe => new RecipeDefinition(digest,
            localId + "-" + recipe.LocalId, version, recipe.DisplayName,
            recipe.Inputs, recipe.Outputs, recipe.DurationTicks, building.CanonicalId, recipe.Tags));
        return StarterContent.BuildManifest(packageId, version, digest, [building], companions, requiredPackages);
    }
}
