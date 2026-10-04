using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Paid communal posts; their Road edge is bound by the project Site and Entrance.</summary>
public static class StreetLanternContent
{
    public const string PackageId = "clankerworld-street-lanterns-v1";
    public const string LanternTag = "street_lantern";
    public const string StoneTag = "stone_lantern";
    public const string HangingTag = "hanging_lantern";

    private static readonly ContentVersion Version = ContentVersion.Parse("1.0.0");
    private static readonly string Digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-street-lanterns-v1:1.0.0:1x1-stone-street-lamp-stone4-hanging-street-lantern-wood4-iron1")));
    private static readonly BuildingDefinition StoneDefinition = new(Digest, "stone-lantern", Version,
        "Stone street lamp", 1, 1, 0, [new("stone", 4)], [LanternTag, StoneTag, "civic", "communal"]);
    private static readonly BuildingDefinition HangingDefinition = new(Digest, "hanging-lantern", Version,
        "Hanging street lantern", 1, 1, 0, [new("wood", 4), new("iron", 1)], [LanternTag, HangingTag, "civic", "communal"]);

    public static BuildingDefinition Stone() => StoneDefinition;

    public static BuildingDefinition Hanging() => HangingDefinition;

    public static BuildingDefinition? Definition(string? definitionId) =>
        definitionId == StoneDefinition.CanonicalId ? Stone() :
        definitionId == HangingDefinition.CanonicalId ? Hanging() : null;

    public static bool IsLantern(string? definitionId) =>
        definitionId == StoneDefinition.CanonicalId || definitionId == HangingDefinition.CanonicalId;

    public static bool IsRoadEdge(GridPoint site, GridPoint road) =>
        Math.Abs((long)site.X - road.X) + Math.Abs((long)site.Y - road.Y) == 1;

    public static ContentPackageManifest Create() => StarterContent.BuildManifest(PackageId, Version, Digest,
        [Stone(), Hanging()], [],
        [new(HouseContent.PackageId, new(Version, ContentVersion.Parse("2.0.0")))]);
}
