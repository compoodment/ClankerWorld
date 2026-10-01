using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Accepted physical gear; detailed combat actions remain separate.</summary>
public static class CombatGearContent
{
    public const string PackageId = "clankerworld-combat-gear-v1";
    public static bool IsWeapon(string kind) => kind is "spear" or "sword";
    public static bool IsGear(string kind) => IsWeapon(kind) || kind is "shield" or "basic_armor";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-combat-gear-v1:1.0.0:spear-sword-shield-basic-armor")));
        var smith = BlacksmithContent.Blacksmith1x2();
        RecipeDefinition[] recipes =
        [
            new(digest, "spear", version, "Make a spear", [new("wood", 2), new("iron", 1)],
                [new("spear", 1)], 24, smith.CanonicalId, ["equipment", "weapon"]),
            new(digest, "sword", version, "Make a sword", [new("wood", 1), new("iron", 2)],
                [new("sword", 1)], 28, smith.CanonicalId, ["equipment", "weapon"]),
            new(digest, "shield", version, "Make a shield", [new("wood", 2), new("cloth", 1), new("iron", 1)],
                [new("shield", 1)], 28, smith.CanonicalId, ["equipment", "defense"]),
            new(digest, "basic-armor", version, "Make basic armor", [new("cloth", 2), new("iron", 3)],
                [new("basic_armor", 1)], 32, smith.CanonicalId, ["equipment", "defense"]),
        ];
        var baseContent = ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), BlacksmithContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(BlacksmithContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], baseContent);
    }
}
