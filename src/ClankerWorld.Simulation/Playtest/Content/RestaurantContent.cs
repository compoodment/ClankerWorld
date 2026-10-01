using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Optional household food business; building costs and recipes are provisional.</summary>
public static class RestaurantContent
{
    public const string PackageId = "clankerworld-restaurant-v1";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-restaurant-v1:1.0.0:named-meals-wood16-stone4")));
        var restaurant = new BuildingDefinition(digest, "restaurant-2x2", version, "Restaurant", 2, 2, 2,
            [new("wood", 16), new("stone", 4)], ["restaurant", "business", "cooking", "storage"]);
        RecipeDefinition[] recipes =
        [
            .. HouseCookingContent.SharedRecipes(digest, version, restaurant.CanonicalId, "restaurant-cooking"),
            new(digest, "restaurant-meal", version, "Prepare a Restaurant meal",
                [new("bread", 1), new("cultivated_greens", 1), new("wood", 1)], [new("restaurant_meal", 2)], 20,
                restaurant.CanonicalId, ["meal", "restaurant-cooking"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [restaurant], recipes, []);
    }
}
