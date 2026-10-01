using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Household-made storage pots and reusable water jugs.</summary>
public static class PotteryContent
{
    public const string PackageId = "clankerworld-pottery-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-pottery-v1:1.0.0:storage-pot-water-jug")));
        var version = ContentVersion.Parse("1.0.0");
        RecipeDefinition[] recipes =
        [
            new(digest, "storage-pot", version, "Make a storage pot",
                [new("clay", 2), new("wood", 1)], [new(InventoryContainerRules.StoragePot, 1)], 24,
                HouseContent.House1x1().CanonicalId, ["container", "pottery", "food-storage"]),
            new(digest, "water-jug", version, "Make a water jug",
                [new("clay", 2), new("wood", 1)], [new(InventoryContainerRules.WaterJug, 1)], 24,
                HouseContent.House1x1().CanonicalId, ["container", "pottery", "fresh-water"]),
        ];
        var houseDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], houseDefinitions);
    }
}
