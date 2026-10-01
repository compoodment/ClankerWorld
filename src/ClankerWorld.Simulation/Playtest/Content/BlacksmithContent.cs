using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Incremental household Blacksmith and distinct starter-tool content.</summary>
public static class BlacksmithContent
{
    public const string PackageId = "clankerworld-blacksmith-v1";

    // All rates and trial costs are provisional. The digest changes because
    // the immutable package now contains the complete agreed tool roster.
    private const string ContentRevision =
        "clankerworld-blacksmith-v1:1.1.0:axes-pickaxes-hoes-hammers-sickle-knife-refined-iron";

    public static BuildingDefinition Blacksmith1x2()
    {
        var digest = PackageDigest();
        var version = ContentVersion.Parse("1.1.0");
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
            new(digest, "stone-axe", version, "Make stone axe",
                [new("wood", 2), new("stone", 2)], [new("stone_axe", 1)], 24,
                blacksmith.CanonicalId, ["tool", "woodcutting"]),
            new(digest, "stone-pickaxe", version, "Make stone pickaxe",
                [new("wood", 2), new("stone", 2)], [new("stone_pickaxe", 1)], 24,
                blacksmith.CanonicalId, ["tool", "mining"]),
            new(digest, "iron-axe", version, "Make iron axe",
                [new("wood", 2), new("iron", 2)], [new("iron_axe", 1)], 30,
                blacksmith.CanonicalId, ["tool", "woodcutting"]),
            new(digest, "iron-pickaxe", version, "Make iron pickaxe",
                [new("wood", 2), new("iron", 2)], [new("iron_pickaxe", 1)], 30,
                blacksmith.CanonicalId, ["tool", "mining"]),
            new(digest, "wooden-hoe", version, "Make wooden hoe",
                [new("wood", 3)], [new("wooden_hoe", 1)], 20, blacksmith.CanonicalId, ["tool", "farming"]),
            new(digest, "iron-hoe", version, "Make iron hoe",
                [new("wood", 1), new("iron", 1)], [new("iron_hoe", 1)], 30,
                blacksmith.CanonicalId, ["tool", "farming"]),
            new(digest, "wooden-hammer", version, "Make wooden hammer",
                [new("wood", 2)], [new("wooden_hammer", 1)], 20,
                blacksmith.CanonicalId, ["tool", "building"]),
            new(digest, "stone-hammer", version, "Make stone hammer",
                [new("wood", 1), new("stone", 2)], [new("stone_hammer", 1)], 24,
                blacksmith.CanonicalId, ["tool", "building"]),
            new(digest, "sickle", version, "Make sickle",
                [new("wood", 1), new("iron", 1)], [new("sickle", 1)], 30,
                blacksmith.CanonicalId, ["tool", "harvesting"]),
            new(digest, "iron-knife", version, "Make iron knife",
                [new("iron", 1)], [new("iron_knife", 1)], 30,
                blacksmith.CanonicalId, ["tool", "preparation"]),
            new(digest, "refine-iron", version, "Refine iron ore",
                [new("iron_ore", 2), new("wood", 1)], [new("iron", 1)], 24,
                blacksmith.CanonicalId, ["metalworking"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [blacksmith], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")))]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(ContentRevision)));
}
