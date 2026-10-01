using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public enum ToolKind { Axe, Pickaxe, Hoe, Hammer, Sickle, Knife }

/// <summary>Trial tool balance shared by resource, field and construction work.</summary>
public sealed record ToolCapability(string ItemKind, ToolKind Kind, int Tier,
    int WorkQuantity, int WearPerUse, string RepairMaterial);

public static class ToolCapabilities
{
    // All yields, wear and repair costs are provisional playtest values.
    public static IReadOnlyList<ToolCapability> All { get; } =
    [
        new("wooden_axe", ToolKind.Axe, 1, 6, 500, "wood"),
        new("stone_axe", ToolKind.Axe, 2, 8, 250, "stone"),
        new("iron_axe", ToolKind.Axe, 3, 10, 125, "iron"),
        new("wooden_pickaxe", ToolKind.Pickaxe, 1, 6, 500, "wood"),
        new("stone_pickaxe", ToolKind.Pickaxe, 2, 8, 250, "stone"),
        new("iron_pickaxe", ToolKind.Pickaxe, 3, 10, 125, "iron"),
        new("wooden_hoe", ToolKind.Hoe, 1, 1, 500, "wood"),
        new("iron_hoe", ToolKind.Hoe, 3, 2, 125, "iron"),
        new("hammer", ToolKind.Hammer, 2, 2, 250, "stone"),
        new("sickle", ToolKind.Sickle, 3, 2, 125, "iron"),
        new("knife", ToolKind.Knife, 3, 2, 125, "iron"),
    ];

    public static ToolCapability? ForItem(string itemKind) =>
        All.FirstOrDefault(tool => tool.ItemKind == itemKind);

    /// <summary>Stored, in-transit, worn out or spoiled tools cannot perform carried work.</summary>
    public static InventoryLot? Best(IEnumerable<InventoryLot> lots, ToolKind kind, int minimumTier = 1) =>
        lots.Where(lot => lot.Quantity > 0 && lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 &&
                lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                ForItem(lot.ItemKind) is { } tool && tool.Kind == kind && tool.Tier >= minimumTier)
            .OrderByDescending(lot => ForItem(lot.ItemKind)!.Tier)
            .ThenByDescending(lot => lot.ConditionBasisPoints)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    public static int RequiredMiningTier(string itemKind) => itemKind switch
    {
        "stone" => 1,
        "iron_ore" => 2,
        "gold_ore" or "gold" or "diamond" => 3,
        _ => 0,
    };
}
