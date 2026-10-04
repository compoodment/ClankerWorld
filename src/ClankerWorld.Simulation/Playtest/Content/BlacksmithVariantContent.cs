using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A larger Blacksmith with the same tools, refining, ornaments and personal cart work.</summary>
public static class BlacksmithVariantContent
{
    public const string PackageId = "clankerworld-blacksmith-2x2-v1";

    public static ContentPackageManifest Create() => WorkstationVariantContent.Create(
        PackageId, "blacksmith-2x2", "Blacksmith (2×2)", 2, 2,
        [new("wood", 24), new("stone", 8)], BlacksmithContent.Blacksmith1x2(),
        [BlacksmithContent.Create(), OrnamentContent.Create()],
        [WorkstationVariantContent.Require(BlacksmithContent.PackageId, "1.2.0"),
            WorkstationVariantContent.Require(OrnamentContent.PackageId, "1.0.0")]);
}
