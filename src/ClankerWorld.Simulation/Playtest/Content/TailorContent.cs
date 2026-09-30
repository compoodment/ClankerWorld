using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Household Tailor Shop: plant fiber becomes cloth, an item of its own, and
/// cloth becomes clothing. Costs, ratios and work times
/// are provisional playtest values; six fiber per garment matches the retired
/// Weaving frame.
/// </summary>
public static class TailorContent
{
    public const string PackageId = "clankerworld-tailor-v1";

    public static BuildingDefinition TailorShop1x1()
    {
        var digest = PackageDigest();
        var version = ContentVersion.Parse("1.0.0");
        return new BuildingDefinition(digest, "tailor-shop-1x1", version, "Tailor Shop", 1, 1, 1,
            [new("wood", 8), new("fiber", 2)], ["tailor", "clothing-making"]);
    }

    public static ContentPackageManifest Create()
    {
        var shop = TailorShop1x1();
        var version = shop.Version;
        var digest = shop.PackageDigest;
        RecipeDefinition[] recipes =
        [
            new(digest, "weave-cloth", version, "Weave cloth",
                [new("fiber", 3)], [new("cloth", 1)], 20, shop.CanonicalId, ["cloth"]),
            new(digest, "sew-clothing", version, "Sew clothing",
                [new("cloth", 2)], [new("clothing", 1)], 24, shop.CanonicalId, ["clothing"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [shop], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-tailor-v1:1.0.0:tailor-shop-cloth-clothing")));
}
