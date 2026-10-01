using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Physical care supplies. Costs, work times and effects are provisional.</summary>
public static class CareContent
{
    public const string PackageId = "clankerworld-care-v1";
    public const int TreatmentTicks = 20;
    public const int BandageHealthPerTick = 50;
    public const int MedicineIllnessPerTick = 75;

    private static string Digest => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-care-v1:clinic1x2-wood10-stone4-bandage2-medicine2-gradual20")));

    public static BuildingDefinition Clinic1x2() => new(Digest, "clinic-1x2", ContentVersion.Parse("1.0.0"),
        "Clinic", 1, 2, 1, [new("wood", 10), new("stone", 4)], ["clinic", "care", "storage"]);

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var house = HouseContent.House1x1();
        var tailor = TailorContent.TailorShop1x1();
        var clinic = Clinic1x2();
        var baseContent = ContentDefinitionPayloadCodec.ApplyPackage(
            ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), HouseContent.Create()),
            TailorContent.Create());
        RecipeDefinition[] recipes =
        [
            new(Digest, "house-bandages", version, "Cut bandages", [new("cloth", 1)], [new("bandage", 2)],
                8, house.CanonicalId, ["care", "bandage"]),
            new(Digest, "tailor-bandages", version, "Cut bandages", [new("cloth", 1)], [new("bandage", 2)],
                8, tailor.CanonicalId, ["care", "bandage"]),
            new(Digest, "clinic-medicine", version, "Prepare herbal medicine",
                [new("medicinal_herbs", 2), new("water", 1), new("wood", 1)], [new("medicine", 2)],
                16, clinic.CanonicalId, ["care", "medicine"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, Digest, [clinic], recipes,
            [new(HouseContent.PackageId, new(version, ContentVersion.Parse("2.0.0"))),
             new(TailorContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], baseContent);
    }
}
