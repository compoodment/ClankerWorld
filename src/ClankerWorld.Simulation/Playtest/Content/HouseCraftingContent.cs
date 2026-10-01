using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Ordinary House crafting; recipe amounts and times are provisional.</summary>
public static class HouseCraftingContent
{
    public const string PackageId = "clankerworld-house-crafting-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-house-crafting-v1:1.0.0:rope-basket")));
        var version = ContentVersion.Parse("1.0.0");
        RecipeDefinition[] recipes =
        [
            new(digest, "twist-rope", version, "Twist fiber into rope", [new("fiber", 3)], [new("rope", 1)],
                12, HouseContent.House1x1().CanonicalId, ["house-crafting"]),
            new(digest, "weave-basket", version, "Weave a carrying basket", [new("fiber", 3), new("rope", 1)],
                [new("basket", 1)], 16, HouseContent.House1x1().CanonicalId, ["carry-aid", "house-crafting"]),
        ];
        var houses = ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(HouseContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], houses);
    }
}
