using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Physical paper for learned knowledge; quantities and work are provisional.</summary>
public static class KnowledgeGoodsContent
{
    public const string PackageId = "clankerworld-knowledge-goods-v1";
    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-knowledge-goods-v1:1.0.0:fiber-water-paper-physical-writing")));
        var house = HouseContent.House1x1();
        RecipeDefinition[] recipes =
        [
            new(digest, "make-paper", version, "Make paper from fiber and fresh water",
                [new("fiber", 2), new("water", 1)], [new("paper", 2)], 16, house.CanonicalId, ["house-crafting", "paper"]),
        ];
        var houses = ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(HouseContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0"))),
             new ContentDependency(PotteryContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], houses);
    }
}
