using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Physical writing supplies; material ratios and work times are provisional.</summary>
public static class KnowledgeContent
{
    public const string PackageId = "clankerworld-knowledge-v1";
    public const string Paper = "paper";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "clankerworld-knowledge-v1:1.0.0:house-paper-2fiber-1fresh-water-2paper-16ticks")));
        RecipeDefinition[] recipes =
        [
            new(digest, "house-paper", version, "Make paper",
                [new("fiber", 2), new(InventoryContainerRules.FreshWater, 1)], [new(Paper, 2)], 16,
                HouseContent.House1x1().CanonicalId, ["knowledge", "paper"]),
        ];
        var houseDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new(HouseContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], houseDefinitions);
    }
}
