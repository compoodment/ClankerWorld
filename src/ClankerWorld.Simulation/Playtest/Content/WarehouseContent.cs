using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Town Warehouse; the legacy Storehouse definition remains loadable in old saves.</summary>
public static class WarehouseContent
{
    public const string PackageId = "clankerworld-warehouse-v1";

    public static BuildingDefinition Warehouse2x2()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-warehouse-v1:1.0.0:warehouse-2x2")));
        var version = ContentVersion.Parse("1.0.0");
        return new BuildingDefinition(digest, "warehouse-2x2", version, "Warehouse", 2, 2, 1,
            [new("wood", 12)], ["warehouse"]);
    }

    public static ContentPackageManifest Create()
    {
        var warehouse = Warehouse2x2();
        return StarterContent.BuildManifest(PackageId, warehouse.Version, warehouse.PackageDigest,
            [warehouse], [], [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(warehouse.Version, ContentVersion.Parse("2.0.0")))]);
    }
}
