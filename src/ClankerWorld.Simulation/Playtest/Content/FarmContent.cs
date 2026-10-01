using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Named household crops and on-site grain processing.</summary>
public static class FarmContent
{
    public const string PackageId = "clankerworld-farm-v1";

    public static BuildingDefinition Farmhouse1x1()
    {
        var digest = PackageDigest();
        var version = ContentVersion.Parse("1.0.0");
        return new BuildingDefinition(digest, "farmhouse-1x1", version, "Farmhouse", 1, 1, 1,
            [new("wood", 8), new("stone", 2)], ["farmhouse", "crop-processing"]);
    }

    public static ContentPackageManifest Create()
    {
        var farmhouse = Farmhouse1x1();
        var version = farmhouse.Version;
        var digest = farmhouse.PackageDigest;
        RecipeDefinition[] recipes =
        [
            new(digest, "universal-grain-field", version, "Grow universal grain",
                [new("grain_seed", 1)], [new("grain", 6), new("grain_seed", 2)], 80, null, ["crop", "grain", "farm-crop"]),
            new(digest, "potato-field", version, "Grow potatoes",
                [new("potatoes", 1)], [new("potatoes", 6)], 70, null, ["crop", "farm-crop"]),
            new(digest, "greens-field", version, "Grow cultivated greens",
                [new("greens_seed", 1)], [new("cultivated_greens", 6), new("greens_seed", 2)], 60, null, ["crop", "farm-crop"]),
            new(digest, "mill-grain", version, "Mill grain into flour",
                [new("grain", 1)], [new("flour", 1)], 20, farmhouse.CanonicalId, ["grain-processing"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [farmhouse], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-farm-v1:1.0.0:tiled-grain-potatoes-greens-flour")));
}
