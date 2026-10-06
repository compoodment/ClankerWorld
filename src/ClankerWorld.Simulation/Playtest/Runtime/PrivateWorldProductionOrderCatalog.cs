using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

internal sealed record ProductionOrderRecipe(
    RecipeDefinition Recipe,
    string OutputKind,
    int OutputQuantity,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<string> Verbs);

/// <summary>Exact shipped recipes whose outputs have an understood production-order name.</summary>
internal static class PrivateWorldProductionOrderCatalog
{
    private sealed record Entry(string PayloadDigest, string[] Subjects, string[] Verbs);

    private static readonly Dictionary<string, Entry> Entries = BuildEntries();

    internal static IReadOnlyList<ProductionOrderRecipe> Available(DeclarativeWorldContentState content) =>
        content.Recipes.Select(Describe).OfType<ProductionOrderRecipe>().ToArray();

    internal static ProductionOrderRecipe? Find(DeclarativeWorldContentState content, string? recipeId) =>
        recipeId is null ? null : Describe(content.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == recipeId));

    private static ProductionOrderRecipe? Describe(RecipeDefinition? recipe) =>
        recipe is not null && Entries.TryGetValue(recipe.CanonicalId, out var entry) &&
        recipe.PayloadDigest == entry.PayloadDigest && !recipe.IsCrop && recipe.WorkstationBuildingId is not null &&
        recipe.Outputs.Count == 1 && recipe.Outputs[0].Amount > 0
            ? new(recipe, recipe.Outputs[0].ResourceId, recipe.Outputs[0].Amount, entry.Subjects, entry.Verbs)
            : null;

    private static Dictionary<string, Entry> BuildEntries()
    {
        ContentPackageManifest[] packages =
        [
            StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(), HouseCookingContent.Create(), FarmContent.Create(),
            BlacksmithContent.Create(), TailorContent.Create(), PotteryContent.Create(), OrnamentContent.Create(), CareContent.Create(),
            RestaurantContent.Create(), RestaurantVariantContent.Create(),
        ];
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var package in packages)
            foreach (var definition in package.Definitions.Where(definition => definition.Kind == RecipeDefinition.SchemaKind))
            {
                var names = NamesFor(package.PackageId, definition.LocalId);
                if (names is null) continue;
                result.Add(definition.CanonicalId(package.PackageDigest),
                    new(definition.PayloadDigest, names.Value.Subjects, names.Value.Verbs));
            }
        return result;
    }

    private static (string[] Subjects, string[] Verbs)? NamesFor(string package, string recipe)
    {
        if (package is HouseCookingContent.PackageId or RestaurantContent.PackageId or RestaurantVariantContent.PackageId)
        {
            var largerRestaurant = package == RestaurantVariantContent.PackageId;
            if (largerRestaurant) recipe = recipe["restaurant-2x2-".Length..];
            string[]? meals = recipe switch
            {
                "house-meal" => ["potato meal", "potato meals"],
                "wild-green-meal" => ["wild green meal", "wild green meals"],
                "cultivated-green-meal" => ["cultivated green meal", "cultivated green meals"],
                "porridge" => ["porridge"],
                "berry-porridge" => ["berry porridge"],
                "fruit-porridge" => ["fruit porridge"],
                "bread" => ["bread"],
                "vegetable-stew" => ["stew", "vegetable stew"],
                "restaurant-meal" => ["restaurant meal", "restaurant meals"],
                _ => null,
            };
            if (meals is null) return null;
            var building = package == HouseCookingContent.PackageId ? "house" :
                largerRestaurant ? "restaurant 2x2" : "restaurant";
            var qualified = meals.Select(meal => building + " " + meal).ToArray();
            // Shared meal names must choose a recipe, not whichever workstation is visited first.
            string[] subjects;
            if (recipe is "house-meal" or "wild-green-meal" or "cultivated-green-meal")
                subjects = meals.Concat(qualified).ToArray();
            else if (recipe == "restaurant-meal")
                subjects = largerRestaurant ? ["restaurant 2x2 meal", "restaurant 2x2 meals"] : meals;
            else subjects = qualified;
            return (subjects, ["make", "cook", "prepare"]);
        }
        if (package == BlacksmithContent.PackageId)
        {
            if (recipe == "refine-iron") return (["iron", "refined iron"], ["make", "refine"]);
            var name = recipe.Replace('-', ' ');
            var plural = name.EndsWith("knife", StringComparison.Ordinal) ? name[..^5] + "knives" : name + "s";
            return ([name, plural], ["make", "craft"]);
        }
        return (package, recipe) switch
        {
            (StarterContent.PackageId, "tools") => (["workshop tool", "workshop tools"], ["make", "craft"]),
            (HouseContent.PackageId, "twist-rope") => (["rope", "ropes"], ["make", "craft", "twist"]),
            (HouseContent.PackageId, "weave-basket") => (["basket", "baskets"], ["make", "craft", "weave"]),
            (FarmContent.PackageId, "mill-grain") => (["flour"], ["make", "mill"]),
            (TailorContent.PackageId, "weave-cloth") => (["cloth"], ["make", "craft", "weave"]),
            (TailorContent.PackageId, "sew-clothing") => (["clothing", "clothes", "garment", "garments", "basic clothing", "basic garment", "basic garments"], ["make", "craft", "sew"]),
            (TailorContent.PackageId, "sew-padded-coat") => (["padded coat", "padded coats"], ["make", "craft", "sew"]),
            (TailorContent.PackageId, "sew-rain-cloak") => (["rain cloak", "rain cloaks"], ["make", "craft", "sew"]),
            (TailorContent.PackageId, "sew-sack") => (["sack", "sacks"], ["make", "craft", "sew"]),
            (PotteryContent.PackageId, "storage-pot") => (["storage pot", "storage pots"], ["make", "craft"]),
            (PotteryContent.PackageId, "water-jug") => (["water jug", "water jugs"], ["make", "craft"]),
            (OrnamentContent.PackageId, "refine-gold") => (["gold", "refined gold"], ["make", "refine"]),
            (OrnamentContent.PackageId, "gold-ornament") => (["gold ornament", "gold ornaments"], ["make", "craft"]),
            (OrnamentContent.PackageId, "set-diamond") => (["diamond ornament", "diamond ornaments"], ["make", "craft"]),
            (CareContent.PackageId, "house-bandages") => (["bandage", "bandages", "house bandage", "house bandages"], ["make", "cut"]),
            (CareContent.PackageId, "tailor-bandages") => (["bandage", "bandages", "tailor bandage", "tailor bandages"], ["make", "cut"]),
            (CareContent.PackageId, "clinic-medicine") => (["medicine", "herbal medicine"], ["make", "prepare"]),
            _ => null,
        };
    }
}
