using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Household Stores and a shared Market with separate physical stalls. Numbers are provisional.</summary>
public static class BusinessContent
{
    public const string PackageId = "clankerworld-business-v1";
    public const int PlotWidth = 10;
    public const int PlotHeight = 12;
    public const string StallKind = "market_stall";
    private static readonly ContentVersion Version = ContentVersion.Parse("1.0.0");
    private static readonly string Digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-business-v1:1.0.0:store1x1-store1x2-market2x2-stall1x1-plot10x12")));

    public static BuildingDefinition Store1x1() => new(Digest, "store-1x1", Version, "Store", 1, 1, 1,
        [new("wood", 8), new("stone", 2)], ["store", "storage", "business"]);

    public static BuildingDefinition Store1x2() => new(Digest, "store-1x2", Version, "Store", 1, 2, 1,
        [new("wood", 12), new("stone", 4)], ["store", "storage", "business"]);

    public static BuildingDefinition Market() => new(Digest, "market-2x2", Version, "Market", 2, 2, 1,
        [new("wood", 20), new("stone", 8)], ["market", "business", "communal"]);

    public static BuildingDefinition Stall() => new(Digest, "market-stall-1x1", Version, "Market stall", 1, 1, 1,
        [], [StallKind, "storage", "business", "market-slot"]);

    public static ContentPackageManifest Create() => StarterContent.BuildManifest(
        PackageId, Version, Digest, [Store1x1(), Store1x2(), Market(), Stall()], [],
        [new ContentDependency(HouseContent.PackageId,
            new ContentVersionRange(Version, ContentVersion.Parse("2.0.0")))]);
}
