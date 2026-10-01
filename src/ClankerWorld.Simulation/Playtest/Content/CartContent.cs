using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Provisional handcart recipe, capacity and wear.</summary>
public static class CartContent
{
    public const string PackageId = "clankerworld-handcart-v1";
    public const int Capacity = 128;
    public const int WearPerStep = 5;

    public static ContentPackageManifest Create()
    {
        var version = ContentVersion.Parse("1.0.0");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-handcart-v1:wood8-iron2-rope2-work32-capacity128-wear5")));
        var smith = BlacksmithContent.Blacksmith1x2();
        var baseContent = ContentDefinitionPayloadCodec.ApplyPackage(new DeclarativeWorldContentState([], []), BlacksmithContent.Create());
        var recipe = new RecipeDefinition(digest, "make-handcart", version, "Make a handcart",
            [new("wood", 8), new("iron", 2), new("rope", 2)], [new("handcart", 1)], 32,
            smith.CanonicalId, ["cart", "transport"]);
        return StarterContent.BuildManifest(PackageId, version, digest, [], [recipe],
            [new(BlacksmithContent.PackageId, new(version, ContentVersion.Parse("2.0.0")))], baseContent);
    }
}
