using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

internal sealed record DeliveryOrderInput(string ItemKind, string BuildingKind);

/// <summary>Known delivery nouns and the native policies permitted for each destination.</summary>
internal static class PrivateWorldDeliveryOrderCatalog
{
    internal static IReadOnlyList<PrivateWorldCustodyOrderCatalog.Goods> Subjects { get; } = CreateSubjects();

    internal static DeliveryOrderInput[] AvailableInputs(DeclarativeWorldContentState content) =>
        PrivateWorldProductionOrderCatalog.Available(content)
            .Select(item => (item.Recipe, BuildingKind: HouseholdBuildingKinds.KindOf(content.Buildings.Single(building =>
                building.CanonicalId == item.Recipe.WorkstationBuildingId))))
            .Where(item => item.BuildingKind is "blacksmith" or "tailor" or "clinic" or "restaurant" ||
                item.BuildingKind == "house" && item.Recipe.Tags.Any(tag => tag is "pottery" or "care" or "named-meal"))
            .SelectMany(item => item.Recipe.Inputs.Select(input => new DeliveryOrderInput(input.ResourceId, item.BuildingKind!)))
            .Distinct().ToArray();

    internal static bool IsValidTarget(string? purpose, string? itemKind, string? buildingKind,
        IReadOnlyList<DeliveryOrderInput> inputs)
    {
        if (itemKind is null || !Subjects.Any(subject => subject.ItemKind == itemKind)) return false;
        return purpose switch
        {
            "household_stock" => buildingKind == "house" && itemKind != "fresh_water" ||
                buildingKind is "farmhouse" or "silo" && FarmFieldRules.IsFarmStock(itemKind),
            "workstation_input" => inputs.Any(input => input.ItemKind == itemKind && input.BuildingKind == buildingKind),
            "household_food" => buildingKind == "house" && (itemKind is "food" or "berries" or "fruit" or "wild_greens" or "cultivated_greens" ||
                PrivateWorldCustodyOrderCatalog.PreparedFoods.Any(food => food.ItemKind == itemKind)),
            "town_surplus" => buildingKind == "warehouse" && itemKind is "wood" or "stone" or "fiber" or "tree_seed",
            "store_stock" => buildingKind == "store" && BusinessRules.MaySell("store", itemKind),
            _ => false,
        };
    }

    internal static bool IsValidRoute(string? purpose, string? route, string? itemKind, string? buildingKind) =>
        (purpose, route, buildingKind) switch
        {
            ("household_stock", "house_stock", "house") => true,
            ("household_stock", "farm_stock", "farmhouse" or "silo") => true,
            ("household_stock", "farm_flour", "house") => itemKind == "flour",
            ("workstation_input", "blacksmith_input", "blacksmith") => true,
            ("workstation_input", "workstation_input", "house" or "blacksmith" or "tailor" or "clinic" or "restaurant") => true,
            ("household_food", "household_food", "house") => true,
            ("town_surplus", "town_surplus", "warehouse") => true,
            ("store_stock", "store_stock", "store") => true,
            _ => false,
        };

    private static PrivateWorldCustodyOrderCatalog.Goods[] CreateSubjects()
    {
        PrivateWorldCustodyOrderCatalog.Goods[] additional =
        [
            new("wood", ["wood"]), new("stone", ["stone", "stones"]),
            new("fiber", ["fiber", "fibre", "plant fiber", "plant fibre"]),
            new("clay", ["clay"]), new("iron_ore", ["iron ore"]), new("gold_ore", ["gold ore"]),
            new("diamond", ["diamond", "diamonds"]),
            new("clothing", ["clothing", "clothes", "basic clothing", "garment", "garments"]),
            new("padded_coat", ["padded coat", "padded coats"]), new("rain_cloak", ["rain cloak", "rain cloaks"]),
            new("basket", ["basket", "baskets"]), new("sack", ["sack", "sacks"]),
            new("food", ["food", "meal", "meals"]), new("berries", ["berry", "berries"]),
            new("fruit", ["fruit"]), new("wild_greens", ["wild greens"]),
            new("cultivated_greens", ["cultivated greens"]), new("fresh_water", ["fresh water"]),
        ];
        return PrivateWorldCustodyOrderCatalog.All.Concat(PrivateWorldCustodyOrderCatalog.PreparedFoods).Concat(additional).Concat(ToolProgressionRules.All.Select(tool =>
        {
            var name = tool.ItemKind.Replace('_', ' ');
            var plural = name.EndsWith("knife", StringComparison.Ordinal) ? name[..^5] + "knives" : name + "s";
            return new PrivateWorldCustodyOrderCatalog.Goods(tool.ItemKind, [name, plural]);
        })).ToArray();
    }
}
