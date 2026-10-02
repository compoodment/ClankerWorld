using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Private Restaurant stock and cooking; physical sales belong to business trade.</summary>
public static class RestaurantContent
{
    public const string PackageId = "clankerworld-restaurant-v1";

    public static BuildingDefinition Restaurant1x2()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-restaurant-v1:1.0.1:restaurant-1x2-porridge-bread-stew-meals")));
        return new BuildingDefinition(digest, "restaurant-1x2", ContentVersion.Parse("1.0.1"),
            "Restaurant", 1, 2, 1, [new("wood", 8), new("stone", 2)], ["restaurant", "food-stock"]);
    }

    public static ContentPackageManifest Create()
    {
        var restaurant = Restaurant1x2();
        var recipes = HouseCookingContent.Recipes(restaurant.PackageDigest, restaurant.Version,
            restaurant.CanonicalId, false).Append(new RecipeDefinition(restaurant.PackageDigest,
                "restaurant-meal", restaurant.Version, "Prepare a Restaurant meal",
                [new("bread", 1), new("cultivated_greens", 1), new("wood", 1)],
                [new("restaurant_meal", 2)], 12, restaurant.CanonicalId, ["food", "named-meal"])).ToArray();
        return StarterContent.BuildManifest(PackageId, restaurant.Version, restaurant.PackageDigest,
            [restaurant], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")))]);
    }
}
