using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Household Blacksmith recipes. Costs and timings are provisional.</summary>
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
        List<RecipeDefinition> recipes =
        [
            Make("wooden-axe", "wooden_axe", "wooden axe", [new("wood", 3)], 20, "woodcutting"),
            Make("wooden-pickaxe", "wooden_pickaxe", "wooden pickaxe", [new("wood", 3)], 20, "mining"),
            Make("stone-axe", "stone_axe", "stone axe", [new("wood", 2), new("stone", 2)], 24, "woodcutting"),
            Make("stone-pickaxe", "stone_pickaxe", "stone pickaxe", [new("wood", 2), new("stone", 2)], 24, "mining"),
            Make("iron-axe", "iron_axe", "iron axe", [new("wood", 2), new("iron", 2)], 28, "woodcutting"),
            Make("iron-pickaxe", "iron_pickaxe", "iron pickaxe", [new("wood", 2), new("iron", 2)], 28, "mining"),
            Make("wooden-hoe", "wooden_hoe", "wooden hoe", [new("wood", 3)], 20, "farming"),
            Make("iron-hoe", "iron_hoe", "iron hoe", [new("wood", 2), new("iron", 2)], 28, "farming"),
            Make("hammer", "hammer", "hammer", [new("wood", 2), new("stone", 2)], 24, "construction"),
            Make("sickle", "sickle", "sickle", [new("wood", 1), new("iron", 1)], 24, "harvesting"),
            Make("knife", "knife", "knife", [new("iron", 1)], 20, "preparation"),
            new(digest, "refine-iron", version, "Refine iron ore",
                [new("iron_ore", 2), new("wood", 1)], [new("iron", 1)], 24,
                blacksmith.CanonicalId, ["metalworking"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [blacksmith], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);

        RecipeDefinition Make(string id, string output, string name, ContentQuantity[] inputs, int ticks, string purpose) =>
            new(digest, id, version, "Make " + name, inputs, [new(output, 1)], ticks,
                blacksmith.CanonicalId, ["tool", purpose]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-blacksmith-v1:1.0.0:tiered-tools-use-wear-repair-iron-v2")));
}
