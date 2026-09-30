using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Private farm Silo, held by the household that holds the neighboring
/// Farmhouse. Its household's harvests are stored here. The cost is a
/// provisional playtest value; storage capacity is not decided yet.
/// </summary>
public static class SiloContent
{
    public const string PackageId = "clankerworld-silo-v1";

    public static BuildingDefinition Silo1x1()
    {
        var digest = PackageDigest();
        var version = ContentVersion.Parse("1.0.0");
        return new BuildingDefinition(digest, "silo-1x1", version, "Silo", 1, 1, 1,
            [new("wood", 6), new("stone", 2)], ["silo", "farm-storage"]);
    }

    public static ContentPackageManifest Create()
    {
        var silo = Silo1x1();
        return StarterContent.BuildManifest(PackageId, silo.Version, silo.PackageDigest, [silo], [],
            [new ContentDependency(FarmContent.PackageId,
                new ContentVersionRange(silo.Version, ContentVersion.Parse("2.0.0")))]);
    }

    private static string PackageDigest() => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-silo-v1:1.0.0:silo-1x1")));
}
