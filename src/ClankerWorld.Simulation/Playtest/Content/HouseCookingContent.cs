using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Adds House meal production without changing the shipped House package.</summary>
public static class HouseCookingContent
{
    public const string PackageId = "clankerworld-house-cooking-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-house-cooking-v1:1.0.0:meal")));
        var version = ContentVersion.Parse("1.0.0");
        var recipe = new RecipeDefinition(digest, "house-meal", version, "Cook a household meal",
            [new("food", 2), new("wood", 1)], [new("food", 4)], 12,
            HouseContent.House1x1().CanonicalId, ["food", "house-cooking"]);
        var houseDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], [recipe],
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], houseDefinitions);
    }
}
