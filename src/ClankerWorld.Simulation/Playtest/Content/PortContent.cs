using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Accepted Port geometry and boat materials; all quantities and timings are provisional.</summary>
public static class PortContent
{
    public const string PackageId = "clankerworld-ports-v1";
    public const string BoatOutputKind = "communal_boat";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-ports-v1:1.0.0:cardinal-ports-communal-boat-one-passenger")));
        var directions = new[] { "north", "east", "south", "west" };
        var buildings = directions.Select(direction => new BuildingDefinition(digest,
            "port-" + direction, version, "Port (" + direction + ")",
            direction is "east" or "west" ? 4 : 2, direction is "east" or "west" ? 2 : 4,
            1, [new("wood", 16), new("stone", 4)], ["port", "boat-building", "port-facing:" + direction])).ToArray();
        var recipes = buildings.Select(building => new RecipeDefinition(digest,
            "small-boat-" + directions[Array.IndexOf(buildings, building)], version, "Build a communal boat",
            [new("wood", 8), new("rope", 2), new("iron", 2)], [new(BoatOutputKind, 1)], 48,
            building.CanonicalId, ["boat", "transport"])).ToArray();
        return StarterContent.BuildManifest(PackageId, version, digest, buildings, recipes,
            [new ContentDependency(HouseCraftingContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0"))),
             new ContentDependency(BlacksmithContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }
}
