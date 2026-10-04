using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingVariantContentTests
{
    [Fact]
    public void AlternativesRetainEveryNativeRecipeWithTheirOwnExactWorkstationIdentity()
    {
        var originals = OriginalContent();
        var before = Originals().Select(ContentPackageManifestCodec.Encode).ToArray();
        var variantIds = new HashSet<string>(StringComparer.Ordinal);
        var copiedRecipes = 0;
        foreach (var variant in Variants())
        {
            var manifest = variant.Manifest;
            var content = ContentDefinitionPayloadCodec.ApplyPackage(new([], []), manifest);
            var building = Assert.Single(content.Buildings);
            var original = originals.Buildings.Single(item => item.LocalId == variant.OriginalLocalId);
            Assert.True(variantIds.Add(manifest.PackageId));
            Assert.DoesNotContain(Originals(), item => item.PackageId == manifest.PackageId);
            Assert.NotEqual(original.CanonicalId, building.CanonicalId);
            Assert.Equal((variant.Width, variant.Height, 1), (building.Width, building.Height, building.Capacity));
            Assert.Equal(variant.Costs.OrderBy(item => item.ResourceId), building.BuildCosts.OrderBy(item => item.ResourceId));
            Assert.Equal(original.Tags, building.Tags);
            Assert.Contains($"{variant.Width}×{variant.Height}", building.DisplayName, StringComparison.Ordinal);

            var recipes = originals.Recipes.Where(item => item.WorkstationBuildingId == original.CanonicalId).ToArray();
            Assert.Equal(variant.RecipeCount, recipes.Length);
            Assert.Equal(recipes.Length, content.Recipes.Count);
            foreach (var recipe in recipes)
            {
                var copy = Assert.Single(content.Recipes, item => item.LocalId == building.LocalId + "-" + recipe.LocalId);
                Assert.Equal(building.CanonicalId, copy.WorkstationBuildingId);
                Assert.NotEqual(recipe.CanonicalId, copy.CanonicalId);
                Assert.Equal(recipe.Inputs, copy.Inputs);
                Assert.Equal(recipe.Outputs, copy.Outputs);
                Assert.Equal(recipe.DurationTicks, copy.DurationTicks);
                Assert.Equal(recipe.Tags, copy.Tags);
                copiedRecipes++;
            }
            Assert.Equal(ContentPackageManifestCodec.Encode(manifest),
                ContentPackageManifestCodec.Encode(ContentPackageManifestCodec.Decode(ContentPackageManifestCodec.Encode(manifest))));
        }
        Assert.Equal(33, copiedRecipes);
        var after = Originals().Select(ContentPackageManifestCodec.Encode).ToArray();
        Assert.Equal(before.Length, after.Length);
        for (var index = 0; index < before.Length; index++) Assert.Equal(before[index], after[index]);
    }

    [Fact]
    public void OriginalBuildingNamespacesAndDependencyVersionsRemainCompatible()
    {
        // These are the original shipped namespaces on the integration base.
        // Adding a size must not rename old buildings, recipes or saved jobs.
        Assert.Equal("sha256:5b73afd27397cbb45acbf52e7cb64d1c0cd51fcc4905ff2909a2e3dd20c48589", FarmContent.Create().PackageDigest);
        Assert.Equal("sha256:4a829f062413118b761319985d93bbb8a143bfb9cb222461c6a184583b33317f", BlacksmithContent.Create().PackageDigest);
        Assert.Equal("sha256:8d931edcdb6339695b0a5842db5263b0507c6a3b2862a25e7451158f3b93c4d2", TailorContent.Create().PackageDigest);
        Assert.Equal("sha256:ece8e1ba6a25c9fd467d62914c61932992a0a48bef201980587f6f646da7aa87", CareContent.Create().PackageDigest);
        Assert.Equal("sha256:d0a43acfe761243feaf71ed161690cb739b8dfcd8245c3f3a2911c51fc7e7030", RestaurantContent.Create().PackageDigest);

        foreach (var variant in Variants())
        {
            var manifest = variant.Manifest;
            Assert.Equal(variant.Dependencies.OrderBy(item => item.Id), manifest.Dependencies
                .Select(item => (Id: item.PackageId, Minimum: item.VersionRange.Minimum.ToString()))
                .OrderBy(item => item.Id));
            var packages = Originals().Append(manifest).ToArray();
            Assert.True(PrivateWorldRuntime.PreviewContent(packages, [manifest.PackageId]).IsSuccess);
            foreach (var dependency in manifest.Dependencies)
            {
                Assert.False(dependency.Optional);
                Assert.Equal(ContentVersion.Parse("2.0.0"), dependency.VersionRange.MaximumExclusive);
                Assert.False(PrivateWorldRuntime.PreviewContent(packages.Where(item => item.PackageId != dependency.PackageId),
                    [manifest.PackageId]).IsSuccess);
                var tooOld = dependency.PackageId == BlacksmithContent.PackageId ? "1.1.0" :
                    dependency.PackageId == RestaurantContent.PackageId ? "1.0.0" : "0.9.9";
                var incompatible = packages.Select(item => item.PackageId == dependency.PackageId
                    ? item with { Version = ContentVersion.Parse(tooOld) } : item);
                Assert.False(PrivateWorldRuntime.PreviewContent(incompatible, [manifest.PackageId]).IsSuccess);
            }
        }
    }

    [Fact]
    public async Task IncrementalActivationWaitsForActiveSourcesAndNeverResurrectsARolledBackAlternative()
    {
        using var world = new PrivateWorldRuntime("incremental-building-variants", _ => new VariantActionProvider());
        Assert.True(world.StageStarterContent());
        var variants = Variants().Select(item => item.Manifest).ToArray();
        for (var tick = 0; tick < 16 && variants.Any(manifest =>
                 !world.Content.Packages.Any(item => item.Manifest.PackageId == manifest.PackageId &&
                     item.Lifecycle == ContentPackageLifecycle.Active)); tick++)
        {
            var before = world.Content.Packages;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            foreach (var manifest in variants)
            {
                var added = world.Content.Packages.SingleOrDefault(item => item.Manifest.PackageId == manifest.PackageId);
                if (added is null || before.Any(item => item.Manifest.PackageId == manifest.PackageId)) continue;
                foreach (var dependency in manifest.Dependencies)
                {
                    var active = Assert.Single(before, item => item.Manifest.PackageId == dependency.PackageId);
                    Assert.Equal(ContentPackageLifecycle.Active, active.Lifecycle);
                    Assert.True(dependency.VersionRange.Contains(active.Manifest.Version));
                }
                Assert.True(added.ActivationTick is null || added.ActivationTick > added.StagedTick);
            }
        }
        foreach (var manifest in variants)
            Assert.Equal(ContentPackageLifecycle.Active,
                Assert.Single(world.Content.Packages, item => item.Manifest.PackageId == manifest.PackageId).Lifecycle);
        Assert.Equal(33, world.WorldContent.Recipes.Count(recipe => variants.Any(item => item.PackageDigest == recipe.PackageDigest)));

        world.RollbackContent(TailorVariantContent.PackageId, "Keep the existing smaller Tailor Shop.");
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new VariantActionProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        for (var tick = 0; tick < 2; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal(ContentPackageLifecycle.Quarantined,
            Assert.Single(world.Content.Packages, item => item.Manifest.PackageId == TailorVariantContent.PackageId).Lifecycle);
        Assert.DoesNotContain(world.WorldContent.Buildings,
            building => building.PackageDigest == TailorVariantContent.Create().PackageDigest);
    }

    private static ContentPackageManifest[] Originals() =>
        [StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(),
            FarmContent.Create(), BlacksmithContent.Create(), TailorContent.Create(),
            CareContent.Create(), RestaurantContent.Create(), OrnamentContent.Create()];

    private static DeclarativeWorldContentState OriginalContent() => Originals().Aggregate(
        new DeclarativeWorldContentState([], []), ContentDefinitionPayloadCodec.ApplyPackage);

    private static Variant[] Variants() =>
    [
        new(FarmhouseVariantContent.Create(), "farmhouse-1x1", 1, 2, [new("wood", 16), new("stone", 4)], 1,
            [(FarmContent.PackageId, "1.0.0")]),
        new(BlacksmithVariantContent.Create(), "blacksmith-1x2", 2, 2, [new("wood", 24), new("stone", 8)], 19,
            [(BlacksmithContent.PackageId, "1.2.0"), (OrnamentContent.PackageId, "1.0.0")]),
        new(TailorVariantContent.Create(), "tailor-shop-1x1", 2, 2, [new("wood", 32), new("fiber", 8)], 6,
            [(TailorContent.PackageId, "1.0.0"), (CareContent.PackageId, "1.0.0")]),
        new(ClinicVariantContent.Create(), "clinic-1x2", 1, 1, [new("wood", 5), new("stone", 2)], 1,
            [(CareContent.PackageId, "1.0.0")]),
        new(RestaurantVariantContent.Create(), "restaurant-1x2", 2, 2, [new("wood", 16), new("stone", 4)], 6,
            [(RestaurantContent.PackageId, "1.0.1")]),
    ];

    private sealed record Variant(ContentPackageManifest Manifest, string OriginalLocalId, int Width, int Height,
        ContentQuantity[] Costs, int RecipeCount, (string Id, string Minimum)[] Dependencies);
}
