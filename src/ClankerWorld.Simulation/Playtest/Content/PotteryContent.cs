using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Trial House pottery recipes; costs, work and vessel capacities are provisional.</summary>
public static class PotteryContent
{
    public const string PackageId = "clankerworld-pottery-v1";

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-pottery-v1:1.0.0:jug8-pot8-clay2-wood1")));
        var house = HouseContent.House1x1();
        var baseContent = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        RecipeDefinition[] recipes =
        [
            new(digest, "water-jug", version, "Make a water jug", [new("clay", 2), new("wood", 1)],
                [new("water_jug", 1)], 18, house.CanonicalId, ["pottery"]),
            new(digest, "storage-pot", version, "Make a food storage pot", [new("clay", 2), new("wood", 1)],
                [new("storage_pot", 1)], 18, house.CanonicalId, ["pottery"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))], baseContent);
    }
}

public static class VesselRules
{
    public const int FillingWorkTicks = 6;
    public static int Capacity(string kind) => kind is "water_jug" or "storage_pot" ? 8 : 0;
    public static bool IsVessel(string kind) => Capacity(kind) > 0;
    public static bool IsPotFood(string kind) => kind is "food" or "fruit" or "berries" or "wild_greens" or
        "cultivated_greens" or "potatoes" or "grain" or "flour" or "simple_meal" or "porridge" or "bread" or
        "vegetable_stew" or "restaurant_meal" or "egg" or "milk";
}
