using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A smaller Clinic with the unchanged physical herbal medicine recipe.</summary>
public static class ClinicVariantContent
{
    public const string PackageId = "clankerworld-clinic-1x1-v1";

    public static ContentPackageManifest Create() => WorkstationVariantContent.Create(
        PackageId, "clinic-1x1", "Clinic (1×1)", 1, 1,
        [new("wood", 5), new("stone", 2)], CareContent.Clinic1x2(),
        [HouseContent.Create(), TailorContent.Create(), CareContent.Create()],
        [WorkstationVariantContent.Require(CareContent.PackageId, "1.0.0")]);
}
