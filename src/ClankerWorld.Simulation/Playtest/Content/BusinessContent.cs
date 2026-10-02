using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Optional household shops. Construction and stock quantities are provisional.</summary>
public static class BusinessContent
{
    public const string PackageId = "clankerworld-business-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-business-v1:1.0.0:store-1x1-1x2")));
        var version = ContentVersion.Parse("1.0.0");
        BuildingDefinition[] buildings =
        [
            new(digest, "store-1x1", version, "Store", 1, 1, 1,
                [new("wood", 8), new("stone", 2)], ["store", "business", "storage"]),
            new(digest, "store-1x2", version, "Store", 1, 2, 1,
                [new("wood", 16), new("stone", 4)], ["store", "business", "storage"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, buildings, [],
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }
}
