using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Crude household tools; material costs and work times are provisional.</summary>
public static class HouseToolsContent
{
    public const string PackageId = "clankerworld-house-tools-v1";
    public const string CrudeWoodenAxe = "crude_wooden_axe";
    public const string CrudeWoodenPickaxe = "crude_wooden_pickaxe";

    private static readonly ContentVersion Version = ContentVersion.Parse("1.0.0");
    private static readonly string HouseId = HouseContent.House1x1().CanonicalId;
    private static readonly string Digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(PackageId + ":1.0.0:house=" + HouseId +
            ":crude-wooden-axe:Make crude wooden axe:wood3:crude_wooden_axe1:24:house-tool,tool,woodcutting" +
            ":crude-wooden-pickaxe:Make crude wooden pickaxe:wood3:crude_wooden_pickaxe1:24:house-tool,tool,mining")));
    private static readonly RecipeDefinition[] Recipes =
    [
        new(Digest, "crude-wooden-axe", Version, "Make crude wooden axe",
            [new("wood", 3)], [new(CrudeWoodenAxe, 1)], 24, HouseId, ["house-tool", "tool", "woodcutting"]),
        new(Digest, "crude-wooden-pickaxe", Version, "Make crude wooden pickaxe",
            [new("wood", 3)], [new(CrudeWoodenPickaxe, 1)], 24, HouseId, ["house-tool", "tool", "mining"]),
    ];

    public static ContentPackageManifest Create()
    {
        var houseDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, Version, Digest, [], Recipes,
            [new(HouseContent.PackageId, new(Version, ContentVersion.Parse("2.0.0")))], houseDefinitions);
    }

    public static bool IsCrudeToolRecipe(RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return Recipes.Any(builtIn => recipe.CanonicalId == builtIn.CanonicalId &&
            recipe.PayloadDigest == builtIn.PayloadDigest);
    }
}
