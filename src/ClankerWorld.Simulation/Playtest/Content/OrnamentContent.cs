using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Physical ornaments, with provisional material costs and work times.</summary>
public static class OrnamentContent
{
    public const string PackageId = "clankerworld-ornaments-v1";
    public const string Gold = "gold";
    public const string GoldOrnament = "gold_ornament";
    public const string DiamondOrnament = "diamond_ornament";

    public static bool IsOrnament(string kind) => kind is GoldOrnament or DiamondOrnament;

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "clankerworld-ornaments-v1:1.0.0:refine-gold-2ore-1wood-make-2gold-set-1diamond")));
        var smith = BlacksmithContent.Blacksmith1x2();
        RecipeDefinition[] recipes =
        [
            new(digest, "refine-gold", version, "Refine gold ore",
                [new("gold_ore", 2), new("wood", 1)], [new(Gold, 1)], 24,
                smith.CanonicalId, ["metalworking", "ornament"]),
            new(digest, "gold-ornament", version, "Make a gold ornament",
                [new(Gold, 2)], [new(GoldOrnament, 1)], 24,
                smith.CanonicalId, ["ornament"]),
            new(digest, "set-diamond", version, "Set a diamond in an ornament",
                [new(GoldOrnament, 1), new("diamond", 1)], [new(DiamondOrnament, 1)], 28,
                smith.CanonicalId, ["ornament"]),
        ];
        var externalDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), BlacksmithContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new(BlacksmithContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], externalDefinitions);
    }
}
