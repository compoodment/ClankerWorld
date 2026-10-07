using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

public static class AnimalContent
{
    public const string PackageId = "clankerworld-animals-v1";
    public const string YardTag = "animal-yard";
    private static readonly string Digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("clankerworld-animals-v1:1.0.0:yard-eggs-milk-wool-leather-saddle")));
    private static readonly ContentVersion Version = ContentVersion.Parse("1.0.0");

    public static BuildingDefinition Yard() => new(Digest, "animal-yard-2x2", Version, "Animal yard", 2, 2, 1,
        [new("wood", 8), new("rope", 2)], [YardTag, "animal-care"]);

    public static ContentPackageManifest Create()
    {
        var basis = new DeclarativeWorldContentState([], []);
        foreach (var manifest in new[] { HouseContent.Create(), TailorContent.Create(), RestaurantContent.Create(), CareContent.Create(), TailorVariantContent.Create(), RestaurantVariantContent.Create() })
            basis = ContentDefinitionPayloadCodec.ApplyPackage(basis, manifest);
        var recipes = new List<RecipeDefinition>();
        void Add(string id, string name, string station, ContentQuantity[] inputs, ContentQuantity[] outputs, int ticks, string tag) =>
            recipes.Add(new(Digest, id, Version, name, inputs, outputs, ticks, station, [tag, "animal-product"]));
        foreach (var (station, prefix) in new[] { (HouseContent.House1x1().CanonicalId, "house"),
                     (RestaurantContent.Restaurant1x2().CanonicalId, "restaurant") })
        {
            Add(prefix + "-cook-eggs", "Cook eggs", station, [new("eggs", 2), new("wood", 1)], [new("cooked_eggs", 2)], 12, "named-meal");
            Add(prefix + "-milk-porridge", "Cook milk porridge", station,
                [new("grain", 1), new("milk", 1), new("wood", 1)], [new("milk_porridge", 2)], 12, "named-meal");
        }
        Add("rich-restaurant-meal", "Prepare a rich Restaurant meal", RestaurantContent.Restaurant1x2().CanonicalId,
            [new("bread", 1), new("eggs", 1), new("cultivated_greens", 1), new("wood", 1)], [new("rich_meal", 2)], 12, "named-meal");
        var tailor = TailorContent.TailorShop1x1().CanonicalId;
        Add("wool-padded-coat", "Sew a wool padded coat", tailor, [new("wool", 2), new("cloth", 2)], [new("padded_coat", 1)], 32, "clothing");
        Add("process-leather", "Process leather", tailor, [new("hide", 1), new("fresh_water", 1), new("wood", 1)], [new("leather", 2)], 16, "care");
        Add("leather-sack", "Sew a leather sack", tailor, [new("leather", 2), new("rope", 1)], [new("leather_sack", 1)], 24, "carry-aid");
        Add("saddle", "Make a saddle", tailor, [new("leather", 2), new("cloth", 2), new("rope", 1)], [new("saddle", 1)], 24, "animal-care");
        var originalRecipes = recipes.ToArray();
        foreach (var variant in basis.Buildings.Where(building => building.LocalId is "tailor-shop-2x2" or "restaurant-2x2"))
        {
            var original = variant.Tags.Contains("tailor") ? tailor : RestaurantContent.Restaurant1x2().CanonicalId;
            foreach (var recipe in originalRecipes.Where(recipe => recipe.WorkstationBuildingId == original))
                Add(variant.LocalId + "-" + recipe.LocalId, recipe.DisplayName, variant.CanonicalId, recipe.Inputs.ToArray(),
                    recipe.Outputs.ToArray(), recipe.DurationTicks, recipe.Tags.Single(tag => tag != "animal-product"));
        }
        return StarterContent.BuildManifest(PackageId, Version, Digest, [Yard()], recipes,
            new[] { HouseContent.PackageId, TailorContent.PackageId, RestaurantContent.PackageId, TailorVariantContent.PackageId, RestaurantVariantContent.PackageId }.Select(id =>
                new ContentDependency(id, new ContentVersionRange(Version, ContentVersion.Parse("2.0.0")))).ToArray(), basis);
    }
}
