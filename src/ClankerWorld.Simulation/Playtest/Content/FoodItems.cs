namespace ClankerWorld.Simulation.Playtest;

/// <summary>Named food uses and provisional nourishment and storage balance.</summary>
public static class FoodItems
{
    public static bool IsEdible(string kind) => kind is "food" or "fruit" or "berries" or
        "wild_greens" or "cultivated_greens" or "simple_meal" or "porridge" or "bread" or
        "vegetable_stew" or "restaurant_meal" or "eggs" or "milk";

    public static bool IsFarmStock(string kind) => kind is "grain" or "grain_seed" or
        "cultivated_green_seed" or "potatoes" or "cultivated_greens";

    public static bool IsPlantingStock(string kind) => kind is "grain_seed" or "cultivated_green_seed" or
        "potatoes" or "tree_seed" or "orchard_seed";

    public static bool IsPerishable(string kind) => IsEdible(kind) || kind is "potatoes" or "flour";

    public static int Fullness(string kind) => kind switch
    {
        "berries" or "wild_greens" => 1_800,
        "fruit" => 2_200,
        "cultivated_greens" or "eggs" or "milk" => 2_400,
        "bread" or "porridge" => 3_400,
        "vegetable_stew" => 3_800,
        "restaurant_meal" => 4_400,
        _ => 3_000,
    };

    public static int FreshnessLoss(string kind) => kind switch
    {
        "grain" or "grain_seed" or "cultivated_green_seed" or "tree_seed" or "orchard_seed" => 0,
        "flour" => 2,
        "bread" or "potatoes" or "food" => 4,
        "berries" or "fruit" or "wild_greens" or "cultivated_greens" => 8,
        "simple_meal" or "porridge" or "vegetable_stew" or "restaurant_meal" or "milk" => 14,
        "eggs" => 6,
        _ => 12,
    };
}
