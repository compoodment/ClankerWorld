using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>First household-owned House design; legacy Shelter stays loadable for existing saves.</summary>
public static class HouseContent
{
    public const string PackageId = "clankerworld-house-v1";

    public static BuildingDefinition House1x1()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-house-v1:1.0.0:house-1x1")));
        var version = ContentVersion.Parse("1.0.0");
        return new BuildingDefinition(digest, "house-1x1", version, "House", 1, 1, 1,
            [new("wood", 8)], ["house", "shelter", "cooking", "storage"]);
    }

    public static ContentPackageManifest Create()
    {
        var house = House1x1();
        var version = house.Version;
        var digest = house.PackageDigest;
        return StarterContent.BuildManifest(PackageId, version, digest, [house], [],
            [new ContentDependency(SettlementContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }
}
