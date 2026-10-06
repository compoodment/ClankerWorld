using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A larger Restaurant with all six current named-meal recipes.</summary>
public static class RestaurantVariantContent
{
    public const string PackageId = "clankerworld-restaurant-2x2-v1";

    public static ContentPackageManifest Create() => WorkstationVariantContent.Create(
        PackageId, "restaurant-2x2", "Restaurant (2×2)", 2, 2,
        [new("wood", 16), new("stone", 4)], RestaurantContent.Restaurant1x2(),
        [RestaurantContent.Create()],
        [WorkstationVariantContent.Require(RestaurantContent.PackageId, "1.0.1")]);
}
