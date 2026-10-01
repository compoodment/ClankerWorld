using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Physical ornaments, with provisional material costs and work times.</summary>
public static class OrnamentContent
{
    public const string PackageId = "clankerworld-ornaments-v1";

    public static bool IsOrnament(string kind) => kind is "gold_ornament" or "diamond_ornament";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-ornaments-v1:1.0.0:refine-gold-make-ornament-set-diamond")));
        var smith = BlacksmithContent.Blacksmith1x2();
        RecipeDefinition[] recipes =
        [
            new(digest, "refine-gold", version, "Refine gold ore", [new("gold_ore", 2), new("wood", 1)],
                [new("gold", 1)], 24, smith.CanonicalId, ["metalworking"]),
            new(digest, "gold-ornament", version, "Make a gold ornament", [new("gold", 2)],
                [new("gold_ornament", 1)], 24, smith.CanonicalId, ["ornament"]),
            new(digest, "set-diamond", version, "Set a diamond in an ornament", [new("gold_ornament", 1), new("diamond", 1)],
                [new("diamond_ornament", 1)], 28, smith.CanonicalId, ["ornament"]),
        ];
        var baseContent = ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), BlacksmithContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(BlacksmithContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], baseContent);
    }
}
