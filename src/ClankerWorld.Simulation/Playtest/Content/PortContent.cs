using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Approved Port geometry and provisional construction budgets.</summary>
public static class PortContent
{
    public const string PackageId = "clankerworld-ports-v1";
    public const string PortTag = "port";
    public static IReadOnlyList<ContentQuantity> BoatCosts { get; } =
        Array.AsReadOnly(new ContentQuantity[] { new("wood", 8), new("rope", 2), new("iron", 2) });

    private static string Digest => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-ports-v1:1.0.0:four-rotations-six-docks-shared-projects")));

    public static IReadOnlyList<BuildingDefinition> Definitions { get; } = Array.AsReadOnly(
        new[] { "north", "east", "south", "west" }.Select(direction => new BuildingDefinition(Digest,
            "port-" + direction, ContentVersion.Parse("1.0.0"), "Port (" + direction + ")",
            direction is "east" or "west" ? 4 : 2, direction is "east" or "west" ? 2 : 4,
            1, [new("wood", 16), new("stone", 4)], [PortTag, "port-facing-" + direction])).ToArray());

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        return StarterContent.BuildManifest(PackageId, version, Digest, Definitions, [],
            [new ContentDependency(TownHallContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }
}
