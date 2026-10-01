using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string CollectToolPrefix = "collect_tool:";
    private const string RepairToolPrefix = "repair_tool:";

    private InventoryLot? CarriedTool(string actor, ToolKind kind, int minimumTier = 1) =>
        ToolCapabilities.Best(society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            AvailableLotQuantity(lot) > 0), kind, minimumTier);

    private ToolCapability? UseTool(string actor, ToolKind kind, int minimumTier = 1)
    {
        if (CarriedTool(actor, kind, minimumTier) is not { } lot) return null;
        var tool = ToolCapabilities.ForItem(lot.ItemKind)!;
        var usedId = lot.Quantity > 1 ? $"tool-use:{WorldTick}:{actor}:{lot.Id}" : lot.Id;
        ApplyInventoryTransition(inventory =>
        {
            if (lot.Quantity > 1)
            {
                inventory = InventoryFixture.SplitLot(inventory, lot.Id, 1, usedId);
            }
            return InventoryFixture.ChangeCondition(inventory, usedId, actor, -tool.WearPerUse, "tool_used");
        });
        AppendEvent("tool_used", $"{actor}:{usedId}:{tool.ItemKind}");
        return tool;
    }

    private InventoryLot? SharedTool(string actor, ToolKind kind, int minimumTier = 1) =>
        ToolCapabilities.All.Where(tool => tool.Kind == kind && tool.Tier >= minimumTier)
            .OrderByDescending(tool => tool.Tier)
            .Select(tool => SharedItem(tool.ItemKind, actor)).FirstOrDefault(lot => lot is not null);

    /// <summary>Collect the right tool before attempting gated resource work.</summary>
    private bool CollectGatheringTool(string actor, PlaytestInhabitantState state, string itemKind)
    {
        var kind = itemKind == "wood" ? ToolKind.Axe : ToolKind.Pickaxe;
        var tier = ToolCapabilities.RequiredMiningTier(itemKind);
        if (itemKind != "wood" && tier == 0 || CarriedTool(actor, kind, Math.Max(1, tier)) is not null)
            return false;
        if (SharedTool(actor, kind, Math.Max(1, tier)) is not { } shared) return false;
        CollectEquipment(actor, state, shared.ItemKind);
        return true;
    }

    private IEnumerable<InventoryLot> ToolsForHousehold(string householdId) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId ||
            inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == householdId);

    private bool NeedsToolOutput(string itemKind, string? ownerId)
    {
        if (ownerId is null || ToolCapabilities.ForItem(itemKind) is not { } wanted) return true;
        var householdTools = ToolsForHousehold(ownerId).Where(lot => AvailableLotQuantity(lot) > 0);
        // A better tool satisfies the need for its earlier tier. Keep enough
        // for each adult plus one spare, including what members already carry.
        var target = Math.Max(1, society.Checkpoint.Inhabitants.Count(person =>
            person.HouseholdId == ownerId && AdultResident(person.Id))) + 1;
        var existing = householdTools.Where(lot => ToolCapabilities.ForItem(lot.ItemKind) is { } tool &&
            tool.Kind == wanted.Kind && tool.Tier >= wanted.Tier).Sum(lot => lot.Quantity);
        if (existing >= target) return false;
        // Make first-tier replacement tools when materials for a higher tier
        // are unavailable; do not let an unusable ore vein block the bootstrap.
        if (wanted.Tier > 1 && !worldSimulation.Buildings.Any(building => building.HouseholdId == ownerId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("blacksmith", StringComparer.Ordinal)))) return false;
        return true;
    }

    private bool NeedsSmithIron(string householdId) =>
        ToolsForHousehold(householdId).Where(lot => lot.ItemKind == "iron" &&
            AvailableLotQuantity(lot) > 0).Sum(lot => lot.Quantity) < 4;

    private (InventoryLot Lot, PlacedBuilding Smith, ToolCapability Tool)? ToolRepairNeed(string actor)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            BlacksmithForHousehold(household) is not { } smith) return null;
        foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                     lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                     lot.Quantity == 1 && lot.ConditionBasisPoints is > 0 and <= 4_000 && AvailableLotQuantity(lot) > 0)
                     .OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            var tool = ToolCapabilities.ForItem(lot.ItemKind);
            if (tool is not null && HasIngredientsAtBuilding([new(tool.RepairMaterial, 1)], household, smith.InstanceId))
                return (lot, smith, tool);
        }
        return null;
    }

    private void AddToolRepairCandidate(List<CognitionCandidate> candidates, string actor)
    {
        if (ToolRepairNeed(actor) is not { } need) return;
        candidates.Add(new(RepairToolPrefix + need.Lot.Id,
            $"Take the worn {need.Tool.ItemKind.Replace('_', ' ')} to the household Blacksmith for repair.",
            19, need.Smith.InstanceId));
    }

    private void RepairTool(string actor, PlaytestInhabitantState state, string lotId)
    {
        if (!AdultResident(actor) || ToolRepairNeed(actor) is not { } need || need.Lot.Id != lotId) return;
        if (state.Position != need.Smith.Position)
        {
            MoveToward(actor, state, need.Smith.Position, "tool_repair", 0);
            return;
        }
        var household = need.Smith.HouseholdId!;
        var hammer = CarriedTool(actor, ToolKind.Hammer) is { } carriedHammer && carriedHammer.Id != lotId
            ? UseTool(actor, ToolKind.Hammer) : null;
        // Trial: hand repairs restore half the condition scale; a hammer
        // restores the whole tool for the same one-unit material cost.
        var restoredCondition = hammer is null ? Math.Min(10_000, need.Lot.ConditionBasisPoints + 5_000) : 10_000;
        ApplyInventoryTransition(inventory =>
        {
            var material = inventory.Lots.Where(lot => lot.OwnerId == household &&
                    lot.StorageBuildingId == need.Smith.InstanceId && lot.ItemKind == need.Tool.RepairMaterial &&
                    AvailableLotQuantity(lot) > 0).OrderBy(lot => lot.Id, StringComparer.Ordinal).First();
            var reservation = $"tool-repair:{WorldTick}:{actor}:{lotId}";
            inventory = InventoryFixture.Reserve(inventory, reservation, household, material.Id, 1, "tool_repair", WorldTick);
            inventory = InventoryFixture.ConsumeReservation(inventory, reservation);
            return InventoryFixture.ChangeCondition(inventory, lotId, actor, restoredCondition - need.Lot.ConditionBasisPoints, "tool_repaired");
        });
        AppendEvent("tool_repaired", $"{actor}:{lotId}:{need.Smith.InstanceId}");
    }
}
