using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A physical shared civic meeting place. Its material cost is provisional.</summary>
public static class TownHallContent
{
    public const string PackageId = "clankerworld-civic-v1";
    private static readonly ContentVersion Version = ContentVersion.Parse("1.0.0");
    private static readonly string Digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-civic-v1:1.0.0:town-hall-3x4")));

    public static BuildingDefinition TownHall() => new(Digest, "town-hall-3x4", Version, "Town Hall", 3, 4, 8,
        [new("wood", 24), new("stone", 12)], ["town_hall", "communal", "civic"]);

    public static ContentPackageManifest Create() => StarterContent.BuildManifest(PackageId, Version, Digest,
        [TownHall()], [], [new ContentDependency(HouseContent.PackageId,
            new ContentVersionRange(Version, ContentVersion.Parse("2.0.0")))]);
}
