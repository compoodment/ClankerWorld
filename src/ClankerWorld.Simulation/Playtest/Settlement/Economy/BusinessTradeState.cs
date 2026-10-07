using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Binds an inventory barter offer to its physical shop and household authority.
/// The inventory owns the actual lots, terms, acceptance and reservations.
/// </summary>
public sealed record BusinessTradeState(
    string OfferId, string BuildingInstanceId, string SellerHouseholdId,
    string BuyerId, GridPoint Position, long ProposedTick, string GoodsKind, string PaymentKind,
    string? SellerActorId = null,
    string? CancellationReason = null);

public static class BusinessRules
{
    public static string? KindOf(BuildingDefinition definition) =>
        definition.Tags.FirstOrDefault(tag => tag is "farmhouse" or "blacksmith" or "tailor" or
            "store" or "restaurant" or "clinic");

    public static bool MaySell(string businessKind, string itemKind) => businessKind switch
    {
        "farmhouse" => itemKind is "food" or "berries" or "wild_greens" or "fruit" or
            FarmFieldRules.Grain or FarmFieldRules.Potatoes or FarmFieldRules.Greens or
            FarmFieldRules.GrainSeed or FarmFieldRules.GreensSeed or FarmFieldRules.OrchardSeed or "flour",
        "blacksmith" => BlacksmithMaySell(itemKind),
        "tailor" => itemKind is "cloth" or "clothing" or "padded_coat" or "rain_cloak" or "sack" or "leather_sack" or "saddle" or "leather",
        "restaurant" => itemKind is "porridge" or "berry_porridge" or "fruit_porridge" or
            "bread" or "stew" or "restaurant_meal" or "cooked_eggs" or "milk_porridge" or "rich_meal",
        "clinic" => itemKind is "bandage" or "medicine",
        "store" => !AgentKnowledgeRules.IsArtifactKind(itemKind) &&
            itemKind is not ("fresh_water" or "storage_pot" or "water_jug" or "iron"),
        _ => false,
    };

    private static bool BlacksmithMaySell(string itemKind) =>
        itemKind is "iron" or "tool" or "wooden_axe" or "stone_axe" or "iron_axe" or
            "wooden_pickaxe" or "stone_pickaxe" or "iron_pickaxe" or "wooden_hoe" or "iron_hoe" or
            "wooden_hammer" or "stone_hammer" or "wooden_sickle" or "iron_sickle" or "iron_knife" or
            OrnamentContent.Gold or "diamond" || OrnamentContent.IsOrnament(itemKind);
}
