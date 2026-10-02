using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public enum ToolFamily
{
    Axe,
    Pickaxe,
    Hoe,
    Hammer,
    Sickle,
    Knife,
}

/// <summary>Trial tool performance. Tier and wear rates are balance values, not fixed design promises.</summary>
public sealed record ToolDefinition(string ItemKind, ToolFamily Family, int Tier,
    int GatherQuantity, int WorkUnits, int WearLossBasisPoints);

/// <summary>A pure extraction calculation; the runtime applies it only after capacity and route checks pass.</summary>
public sealed record ToolGatheringPlan(int Quantity, string? ToolLotId, int WearLossBasisPoints,
    int TreeSeedQuantity, bool FellTree);

/// <summary>One work action's saved-tool effect; values are provisional.</summary>
public sealed record ToolWorkPlan(string ToolLotId, int WorkUnits, int WearLossBasisPoints);

/// <summary>Tool tiers and the resource gates they enforce in the normal gathering path.</summary>
public static class ToolProgressionRules
{
    private static readonly ToolDefinition[] Definitions =
    [
        new("wooden_axe", ToolFamily.Axe, 1, 6, 1, 2_000),
        new("stone_axe", ToolFamily.Axe, 2, 7, 1, 1_250),
        new("iron_axe", ToolFamily.Axe, 3, 8, 1, 1_000),
        new("wooden_pickaxe", ToolFamily.Pickaxe, 1, 6, 1, 2_000),
        new("stone_pickaxe", ToolFamily.Pickaxe, 2, 7, 1, 1_250),
        new("iron_pickaxe", ToolFamily.Pickaxe, 3, 8, 1, 1_000),
        new("wooden_hoe", ToolFamily.Hoe, 1, 0, 2, 1_000),
        new("iron_hoe", ToolFamily.Hoe, 3, 0, 3, 1_000),
        new("wooden_hammer", ToolFamily.Hammer, 1, 0, 2, 2_000),
        new("stone_hammer", ToolFamily.Hammer, 2, 0, 3, 1_250),
        new("wooden_sickle", ToolFamily.Sickle, 1, 0, 2, 2_000),
        new("iron_sickle", ToolFamily.Sickle, 3, 0, 4, 1_000),
        new("iron_knife", ToolFamily.Knife, 3, 0, 2, 1_000),
    ];

    public static IReadOnlyList<ToolDefinition> All => Definitions;

    public static ToolDefinition? Find(string itemKind) =>
        Definitions.SingleOrDefault(tool => tool.ItemKind == itemKind);

    /// <summary>Direct actions require a top-level carried lot; container contents must be retrieved first.</summary>
    public static bool IsTopLevelCarriedLot(InventoryLot lot, string actorId) =>
        PersonalEquipmentRules.IsCarried(lot, actorId) && lot.DeliveryBuildingId is null &&
        lot.ContainerLotId is null;

    public static ToolDefinition? RequiredToolForGathering(string itemKind, MapResource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKind);
        ArgumentNullException.ThrowIfNull(source);
        return RequiredTool(itemKind, source);
    }

    /// <summary>Returns the best usable, unreserved tool the actor physically carries.</summary>
    public static InventoryLot? BestUsableTool(InventoryCheckpoint inventory, string actorId, ToolFamily family)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));

        return inventory.Lots
            .Where(lot => IsTopLevelCarriedLot(lot, actorId) && lot.ConditionBasisPoints > 0 &&
                lot.FreshnessBasisPoints > 0 && AvailableQuantity(inventory, lot) > 0)
            .Select(lot => (Lot: lot, Definition: Find(lot.ItemKind)))
            .Where(item => item.Definition is { } definition && definition.Family == family)
            .OrderByDescending(item => item.Definition!.Tier)
            .ThenByDescending(item => item.Lot.ConditionBasisPoints)
            .ThenBy(item => item.Lot.Id, StringComparer.Ordinal)
            .Select(item => item.Lot)
            .FirstOrDefault();
    }

    public static ToolWorkPlan? PlanWork(InventoryCheckpoint inventory, string actorId, ToolFamily family)
    {
        var lot = BestUsableTool(inventory, actorId, family);
        var definition = lot is null ? null : Find(lot.ItemKind);
        return definition is null ? null : new ToolWorkPlan(lot!.Id,
            definition.WorkUnits, definition.WearLossBasisPoints);
    }

    public static ToolWorkPlan? PlanWorkForLot(InventoryCheckpoint inventory, string actorId,
        ToolFamily family, string? toolLotId)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        if (string.IsNullOrWhiteSpace(toolLotId)) return null;

        var lot = inventory.Lots.FirstOrDefault(item => item.Id == toolLotId &&
            IsTopLevelCarriedLot(item, actorId) &&
            item.ConditionBasisPoints > 0 && item.FreshnessBasisPoints > 0 && AvailableQuantity(inventory, item) > 0);
        var definition = lot is null ? null : Find(lot.ItemKind);
        return definition is null || definition.Family != family
            ? null
            : new ToolWorkPlan(lot!.Id, definition.WorkUnits, definition.WearLossBasisPoints);
    }

    public static bool UsesKnife(RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.Tags.Contains("food", StringComparer.Ordinal) ||
            recipe.Tags.Contains("preparation", StringComparer.Ordinal);
    }

    public static int WorkDuration(int normalTicks, int workUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workUnits);
        return Math.Max(1, checked((normalTicks + workUnits - 1) / workUnits));
    }

    /// <summary>
    /// Calculates one real resource action without changing the world or inventory.
    /// The caller uses this same quantity and seed count for its carrying-capacity precheck.
    /// </summary>
    public static ToolGatheringPlan? PlanGather(string itemKind, MapResource source,
        InventoryCheckpoint inventory, string actorId, int sourceQuantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKind);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceQuantity);

        if (sourceQuantity <= 0)
            return null;

        if (itemKind == "wood" && IsLooseWood(source))
            return new ToolGatheringPlan(1, null, 0, 0, false);

        var required = RequiredToolForGathering(itemKind, source);
        if (required is null)
            return new ToolGatheringPlan(4, null, 0, 0, false);

        var toolLot = inventory.Lots
            .Where(lot => IsTopLevelCarriedLot(lot, actorId) && lot.ConditionBasisPoints > 0 &&
                lot.FreshnessBasisPoints > 0 && AvailableQuantity(inventory, lot) > 0)
            .Select(lot => (Lot: lot, Definition: Find(lot.ItemKind)))
            .Where(item => item.Definition is { } definition && definition.Family == required.Family &&
                definition.Tier >= required.Tier)
            .OrderByDescending(item => item.Definition!.Tier)
            .ThenByDescending(item => item.Lot.ConditionBasisPoints)
            .ThenBy(item => item.Lot.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (toolLot.Definition is null)
            return null;

        var seedQuantity = TreeGrowthRules.IsWoodTree(source.TreeKind) && sourceQuantity == 1
            ? TreeGrowthRules.TreeSeedsPerFelledTree
            : 0;
        return new ToolGatheringPlan(toolLot.Definition.GatherQuantity, toolLot.Lot.Id,
            toolLot.Definition.WearLossBasisPoints, seedQuantity,
            TreeGrowthRules.IsWoodTree(source.TreeKind));
    }

    public static IReadOnlyList<ContentQuantity> RepairMaterials(string itemKind) => itemKind switch
    {
        "wooden_axe" or "wooden_pickaxe" or "wooden_hoe" or "wooden_hammer" => [new("wood", 1)],
        "stone_axe" or "stone_pickaxe" or "stone_hammer" => [new("wood", 1), new("stone", 1)],
        "iron_axe" or "iron_pickaxe" or "iron_hoe" or "iron_sickle" => [new("wood", 1), new("iron", 1)],
        "wooden_sickle" => [new("wood", 1)],
        "iron_knife" => [new("iron", 1)],
        _ => [],
    };

    private static ToolDefinition? RequiredTool(string itemKind, MapResource source)
    {
        if (itemKind == "wood" && (TreeGrowthRules.IsWoodTree(source.TreeKind) || source.Kind == "construction"))
            return Definitions.Single(tool => tool.ItemKind == "wooden_axe");
        return itemKind switch
        {
            "stone" => Definitions.Single(tool => tool.ItemKind == "wooden_pickaxe"),
            "iron_ore" => Definitions.Single(tool => tool.ItemKind == "stone_pickaxe"),
            "gold_ore" or "diamond" => Definitions.Single(tool => tool.ItemKind == "iron_pickaxe"),
            _ => null,
        };
    }

    private static bool IsLooseWood(MapResource source) =>
        source.Kind == "wood" || source.NaturalObjectKind == "fallen_wood";

    private static int AvailableQuantity(InventoryCheckpoint inventory, InventoryLot lot)
    {
        var reserved = inventory.Reservations
            .Where(reservation => reservation.LotId == lot.Id && reservation.State is
                InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                InventoryReservationState.Committed)
            .Sum(reservation => reservation.Quantity);
        return Math.Max(0, lot.Quantity - reserved);
    }
}
