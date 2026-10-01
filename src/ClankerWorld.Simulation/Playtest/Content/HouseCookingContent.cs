using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Concrete household meals; amounts, work and nutrition are provisional.</summary>
public static class HouseCookingContent
{
    public const string PackageId = "clankerworld-house-cooking-v1";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-house-cooking-v1:1.0.0:named-meals-water-fuel")));
        var house = HouseContent.House1x1();
        RecipeDefinition[] recipes =
        [
            new(digest, "house-meal", version, "Cook a potato meal",
                [new("potatoes", 2), new("wood", 1)], [new("simple_meal", 2)], 12,
                house.CanonicalId, ["meal", "house-cooking"]),
            new(digest, "wild-greens-meal", version, "Cook a wild greens meal",
                [new("wild_greens", 2), new("wood", 1)], [new("simple_meal", 2)], 12,
                house.CanonicalId, ["meal", "house-cooking"]),
            new(digest, "cultivated-greens-meal", version, "Cook a cultivated greens meal",
                [new("cultivated_greens", 2), new("wood", 1)], [new("simple_meal", 2)], 12,
                house.CanonicalId, ["meal", "house-cooking"]),
            .. SharedRecipes(digest, version, house.CanonicalId, "house-cooking"),
        ];
        var houseDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], houseDefinitions);
    }

    internal static RecipeDefinition[] SharedRecipes(string digest, ContentVersion version, string workstation, string tag) =>
    [
        new(digest, "porridge", version, "Cook porridge",
            [new("grain", 1), new("water", 1), new("wood", 1)], [new("porridge", 2)], 16,
            workstation, ["meal", tag]),
        new(digest, "berry-porridge", version, "Cook berry porridge",
            [new("grain", 1), new("berries", 1), new("water", 1), new("wood", 1)], [new("porridge", 2)], 16,
            workstation, ["meal", tag, "fruit-enriched"]),
        new(digest, "fruit-porridge", version, "Cook fruit porridge",
            [new("grain", 1), new("fruit", 1), new("water", 1), new("wood", 1)], [new("porridge", 2)], 16,
            workstation, ["meal", tag, "fruit-enriched"]),
        new(digest, "bread", version, "Bake bread",
            [new("flour", 2), new("water", 1), new("wood", 1)], [new("bread", 2)], 24,
            workstation, ["meal", tag]),
        new(digest, "vegetable-stew", version, "Cook vegetable stew",
            [new("potatoes", 1), new("cultivated_greens", 1), new("water", 1), new("wood", 1)], [new("vegetable_stew", 2)], 20,
            workstation, ["meal", tag]),
    ];

    public static bool IsMealRecipe(RecipeDefinition recipe) =>
        recipe.Tags.Any(tag => tag is "house-cooking" or "restaurant-cooking");
}
