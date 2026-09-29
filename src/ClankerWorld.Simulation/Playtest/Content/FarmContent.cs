using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Incremental universal-grain and household Farmhouse content.</summary>
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
                [new("seed", 1)], [new("grain", 6), new("seed", 2)], 60, null, ["crop", "grain"]),
            new(digest, "mill-grain", version, "Mill grain into flour",
                [new("grain", 4)], [new("flour", 3)], 20, farmhouse.CanonicalId, ["grain-processing"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [farmhouse], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-farm-v1:1.0.0:farmhouse-grain-flour")));
}
