using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Incremental household Blacksmith and distinct starter-tool content.</summary>
public static class BlacksmithContent
{
    public const string PackageId = "clankerworld-blacksmith-v1";

    public static BuildingDefinition Blacksmith1x2()
    {
        var digest = PackageDigest();
        var version = ContentVersion.Parse("1.0.0");
        return new BuildingDefinition(digest, "blacksmith-1x2", version, "Blacksmith", 1, 2, 1,
            [new("wood", 12), new("stone", 4)], ["blacksmith", "metalworking"]);
    }

    public static ContentPackageManifest Create()
    {
        var blacksmith = Blacksmith1x2();
        var version = blacksmith.Version;
        var digest = blacksmith.PackageDigest;
        RecipeDefinition[] recipes =
        [
            new(digest, "wooden-axe", version, "Make wooden axe",
                [new("wood", 3)], [new("wooden_axe", 1)], 20, blacksmith.CanonicalId, ["tool", "woodcutting"]),
            new(digest, "wooden-pickaxe", version, "Make wooden pickaxe",
                [new("wood", 3)], [new("wooden_pickaxe", 1)], 20, blacksmith.CanonicalId, ["tool", "mining"]),
            new(digest, "refine-iron", version, "Refine iron ore",
                [new("iron_ore", 2), new("wood", 1)], [new("iron", 1)], 24,
                blacksmith.CanonicalId, ["metalworking"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [blacksmith], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-blacksmith-v1:1.0.0:wooden-axe-pickaxe-refined-iron")));
}
