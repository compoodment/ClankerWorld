using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A larger Tailor Shop, including the ordinary cloth bandage recipe.</summary>
public static class TailorVariantContent
{
    public const string PackageId = "clankerworld-tailor-shop-2x2-v1";

    public static ContentPackageManifest Create() => WorkstationVariantContent.Create(
        PackageId, "tailor-shop-2x2", "Tailor Shop (2×2)", 2, 2,
        [new("wood", 32), new("fiber", 8)], TailorContent.TailorShop1x1(),
        [HouseContent.Create(), TailorContent.Create(), CareContent.Create()],
        [WorkstationVariantContent.Require(TailorContent.PackageId, "1.0.0"),
            WorkstationVariantContent.Require(CareContent.PackageId, "1.0.0")]);
}
