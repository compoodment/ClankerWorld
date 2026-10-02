using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Household care supplies; recipes, costs and work times are provisional.</summary>
public static class CareContent
{
    public const string PackageId = "clankerworld-care-v1";
    public const string MedicinalHerbs = "medicinal_herbs";
    public const string Bandage = "bandage";
    public const string Medicine = "medicine";

    private static string PackageDigest => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-care-v1:1.0.0:clinic1x2-wood10-stone4-bandage2-medicine2-fresh-water")));

    public static BuildingDefinition Clinic1x2() => new(PackageDigest, "clinic-1x2", ContentVersion.Parse("1.0.0"),
        "Clinic", 1, 2, 1, [new("wood", 10), new("stone", 4)], ["clinic", "care", "storage"]);

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var house = HouseContent.House1x1();
        var tailor = TailorContent.TailorShop1x1();
        var clinic = Clinic1x2();
        var externalDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), HouseContent.Create()),
            TailorContent.Create());
        RecipeDefinition[] recipes =
        [
            new(PackageDigest, "house-bandages", version, "Cut bandages", [new("cloth", 1)], [new(Bandage, 2)],
                8, house.CanonicalId, ["care", "bandage"]),
            new(PackageDigest, "tailor-bandages", version, "Cut bandages", [new("cloth", 1)], [new(Bandage, 2)],
                8, tailor.CanonicalId, ["care", "bandage"]),
            new(PackageDigest, "clinic-medicine", version, "Prepare herbal medicine",
                [new(MedicinalHerbs, 2), new(InventoryContainerRules.FreshWater, 1), new("wood", 1)],
                [new(Medicine, 2)], 16, clinic.CanonicalId, ["care", "medicine"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, PackageDigest, [clinic], recipes,
            [new(HouseContent.PackageId, new(version, ContentVersion.Parse("2.0.0"))),
                new(TailorContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], externalDefinitions);
    }
}
