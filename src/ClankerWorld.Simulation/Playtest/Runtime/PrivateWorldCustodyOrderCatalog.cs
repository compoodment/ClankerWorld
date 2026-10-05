namespace ClankerWorld.Simulation.Playtest;

/// <summary>Exact goods names for moving property, independent of gathering and repair subjects.</summary>
internal static class PrivateWorldCustodyOrderCatalog
{
    internal sealed record Goods(string ItemKind, string[] Names);

    internal static IReadOnlyList<Goods> PreparedFoods { get; } =
    [
        new("simple_meal", ["simple meal", "simple meals"]),
        new("porridge", ["porridge"]),
        new("berry_porridge", ["berry porridge"]),
        new("fruit_porridge", ["fruit porridge"]),
        new("bread", ["bread"]),
        new("stew", ["stew", "vegetable stew"]),
        new("restaurant_meal", ["restaurant meal", "restaurant meals"]),
    ];

    internal static IReadOnlyList<Goods> All { get; } =
    [
        new("rope", ["rope", "ropes"]),
        new("cloth", ["cloth"]),
        new("iron", ["iron", "refined iron"]),
        new("gold", ["gold", "refined gold"]),
        new("tool", ["workshop tool", "workshop tools"]),
        new("grain", ["grain"]),
        new("flour", ["flour"]),
        new("potatoes", ["potato", "potatoes"]),
        new("tree_seed", ["tree seed", "tree seeds"]),
        new("grain_seed", ["grain seed", "grain seeds"]),
        new("cultivated_green_seed", ["cultivated green seed", "cultivated green seeds", "cultivated greens seed", "cultivated greens seeds"]),
        new("orchard_seed", ["orchard seed", "orchard seeds"]),
        new("medicinal_herbs", ["medicinal herb", "medicinal herbs"]),
        new("bandage", ["bandage", "bandages"]),
        new("medicine", ["medicine", "herbal medicine"]),
        new("storage_pot", ["storage pot", "storage pots"]),
        new("water_jug", ["water jug", "water jugs"]),
        new("gold_ornament", ["gold ornament", "gold ornaments"]),
        new("diamond_ornament", ["diamond ornament", "diamond ornaments"]),
    ];

    internal static bool IsGoodsKind(string? kind) => kind is not null && All.Any(goods => goods.ItemKind == kind);

    internal static bool IsReturnKind(string? kind) => IsGoodsKind(kind) || PreparedFoods.Any(food => food.ItemKind == kind) ||
        PrivateWorldInstructionOrderParser.IsMaterialKind(kind) ||
        PrivateWorldInstructionOrderParser.IsEquipmentKind(kind) ||
        PrivateWorldInstructionOrderParser.IsToolKind(kind);
}
