using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A separately buildable long Farmhouse; its trial cost is provisional.</summary>
public static class FarmhouseVariantContent
{
    public const string PackageId = "clankerworld-farmhouse-1x2-v1";

    public static ContentPackageManifest Create() => WorkstationVariantContent.Create(
        PackageId, "farmhouse-1x2", "Farmhouse (1×2)", 1, 2,
        [new("wood", 16), new("stone", 4)], FarmContent.Farmhouse1x1(),
        [FarmContent.Create()],
        [WorkstationVariantContent.Require(FarmContent.PackageId, "1.0.0")]);
}
