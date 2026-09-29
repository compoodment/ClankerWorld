using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Additive cultivation content; exhausted wild timber remains exhausted.</summary>
public static class ForestryContent
{
    public const string PackageId = "clankerworld-forestry-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-forestry-v1:1.0.0:managed-coppice:seed2-wood24-seed2:1440ticks")));
        var version = ContentVersion.Parse("1.0.0");
        RecipeDefinition[] recipes =
        [
            new(digest, "managed-coppice", version, "Plant managed coppice", [new("seed", 2)],
                [new("wood", 24), new("seed", 2)], KernelClock.TicksPerDay, null, ["crop", "forestry"]),
        ];
        return StarterContent.BuildManifest(PackageId, version, digest, [], recipes,
            [new ContentDependency(SettlementContent.PackageId, new ContentVersionRange(version, ContentVersion.Parse("2.0.0")))]);
    }
}
