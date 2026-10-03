using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Named household meals made from physical ingredients at a House.</summary>
public static class HouseCookingContent
{
    public const string PackageId = "clankerworld-house-cooking-v1";

    public static ContentPackageManifest Create()
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("clankerworld-house-cooking-v1:1.0.1:potatoes-greens-porridge-bread-stew")));
        var version = ContentVersion.Parse("1.0.1");
        var houseDefinitions = ContentDefinitionPayloadCodec.ApplyPackage(
            new DeclarativeWorldContentState([], []), HouseContent.Create());
        return StarterContent.BuildManifest(PackageId, version, digest, [],
            Recipes(digest, version, HouseContent.House1x1().CanonicalId, true),
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")))], houseDefinitions);
    }

    internal static IReadOnlyList<RecipeDefinition> Recipes(string digest, ContentVersion version,
        string workstationId, bool simpleMeals)
    {
        var recipes = new List<RecipeDefinition>();
        void Add(string id, string name, IReadOnlyList<ContentQuantity> inputs, string output) =>
            recipes.Add(new RecipeDefinition(digest, id, version, name, inputs, [new(output, 2)],
                12, workstationId, ["food", "named-meal"]));
        if (simpleMeals)
        {
            Add("house-meal", "Cook a potato meal", [new("potatoes", 2), new("wood", 1)], "simple_meal");
            Add("wild-green-meal", "Cook a wild greens meal", [new("wild_greens", 2), new("wood", 1)], "simple_meal");
            Add("cultivated-green-meal", "Cook a cultivated greens meal", [new("cultivated_greens", 2), new("wood", 1)], "simple_meal");
        }
        Add("porridge", "Cook porridge", [new("grain", 1), new(InventoryContainerRules.FreshWater, 1), new("wood", 1)], "porridge");
        Add("berry-porridge", "Cook berry porridge", [new("grain", 1), new("berries", 1), new(InventoryContainerRules.FreshWater, 1), new("wood", 1)], "berry_porridge");
        Add("fruit-porridge", "Cook fruit porridge", [new("grain", 1), new("fruit", 1), new(InventoryContainerRules.FreshWater, 1), new("wood", 1)], "fruit_porridge");
        Add("bread", "Bake bread", [new("flour", 2), new(InventoryContainerRules.FreshWater, 1), new("wood", 1)], "bread");
        Add("vegetable-stew", "Cook vegetable stew", [new("potatoes", 1), new("cultivated_greens", 1), new(InventoryContainerRules.FreshWater, 1), new("wood", 1)], "stew");
        return recipes;
    }
}
