using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A bounded paid Market; starter count, material costs and work are provisional.</summary>
public static class MarketContent
{
    public const string PackageId = "clankerworld-market-v1";
    public const string HallTag = "market";
    public const string StallTag = "market_stall";
    public const int PlazaWidth = 7;
    public const int PlazaHeight = 4;
    public const int MaximumStalls = 8;
    public static IReadOnlyList<int> StarterSlotIndexes { get; } = Array.AsReadOnly(new[] { 0, 4 });

    private static string Digest => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-market-v1:1.0.0:hall2x2-plaza7x4-starters0,4-wood24-stone8-fiber4:stall1x1-wood4-fiber2")));

    public static BuildingDefinition Hall2x2() => new(Digest, "market-2x2", ContentVersion.Parse("1.0.0"),
        "Market", 2, 2, 0, [new("wood", 24), new("stone", 8), new("fiber", 4)],
        [HallTag, "civic", "communal"]);

    public static BuildingDefinition Stall1x1() => new(Digest, "market-stall-1x1", ContentVersion.Parse("1.0.0"),
        "Market stall", 1, 1, 0, [new("wood", 4), new("fiber", 2)], [StallTag, "communal"]);

    public static GridPoint HallEntrance(GridPoint hallSite) => new(hallSite.X + 1, hallSite.Y + 2);
    public static GridPoint PlazaOrigin(GridPoint hallSite) => new(hallSite.X - 2, hallSite.Y + 2);

    public static GridPoint StallSite(GridPoint hallSite, int slot)
    {
        if (slot is < 0 or >= MaximumStalls) throw new ArgumentOutOfRangeException(nameof(slot));
        var plaza = PlazaOrigin(hallSite);
        var column = new[] { 1, 2, 4, 5 }[slot % 4];
        return new(plaza.X + column, plaza.Y + (slot < 4 ? 1 : 2));
    }

    public static GridPoint StallEntrance(GridPoint hallSite, int slot)
    {
        var site = StallSite(hallSite, slot);
        return new(site.X, site.Y + (slot < 4 ? -1 : 1));
    }

    public static IEnumerable<GridPoint> PlazaTiles(GridPoint hallSite)
    {
        var plaza = PlazaOrigin(hallSite);
        return Enumerable.Range(0, PlazaHeight).SelectMany(y => Enumerable.Range(0, PlazaWidth)
            .Select(x => new GridPoint(plaza.X + x, plaza.Y + y)));
    }

    public static IEnumerable<GridPoint> SiteTiles(GridPoint hallSite) =>
        WorldContentSimulationRules.Footprint(Hall2x2(), hallSite).Concat(PlazaTiles(hallSite));

    public static string MarketId(string projectId) => projectId + ":market";
    public static string StarterStallBuildingId(string projectId, int slot) =>
        TownProjectRules.BuildingId(projectId + ":starter-stall:" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static ContentPackageManifest Create()
    {
        var hall = Hall2x2();
        return StarterContent.BuildManifest(PackageId, hall.Version, hall.PackageDigest, [hall, Stall1x1()], [],
            [new(TownHallContent.PackageId, new(hall.Version, ContentVersion.Parse("2.0.0")))]);
    }
}
