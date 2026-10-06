using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Additive built-in content; never rewrites an existing starter package identity.</summary>
public static class SettlementContent
{
    public const string PackageId = "clankerworld-settlement-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-settlement-v1:1.0.0:stone-hearth:grain:stone-fiber-seed")));
        var version = ContentVersion.Parse("1.0.0");
        var hearth = new BuildingDefinition(digest, "stone-hearth", version, "Stone hearth", 1, 1, 2,
            [new("stone", 8), new("wood", 4)], ["cooking", "warmth"]);
        // The Weaving frame and its woven clothing are gone; the Tailor Shop
        // makes clothing now (TailorContent).
        RecipeDefinition[] recipes =
        [
            new(digest, "hearty-meal", version, "Hearty meal", [new("food", 2), new("wood", 1)], [new("food", 5)], 12, hearth.CanonicalId, ["food"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [hearth], recipes,
            [new ContentDependency(StarterContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }
}
