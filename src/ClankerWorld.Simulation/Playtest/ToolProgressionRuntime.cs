using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string RepairToolPrefix = "repair_tool:";

    private bool CanGatherFromSource(string actor, string itemKind, MapResource source)
    {
        var inventory = society.Checkpoint.Inventory;
        var quantity = worldSystems.Ecology.GetResource(source.Id).Quantity;
        if (ToolProgressionRules.PlanGather(itemKind, source, inventory, actor, quantity) is not null)
            return true;
        var required = ToolProgressionRules.RequiredToolForGathering(itemKind, source);
        return required is null || FindReachableSharedGatheringTool(actor, required) is not null;
    }

    private int AvailableGatherQuantity(string actor, string itemKind, MapResource source)
    {
        var inventory = society.Checkpoint.Inventory;
        var quantity = worldSystems.Ecology.GetResource(source.Id).Quantity;
        var plan = ToolProgressionRules.PlanGather(itemKind, source, inventory, actor, quantity);
        if (plan is not null) return plan.Quantity;
        if (RequiredGatheringTool(itemKind, source) is not { } required) return 0;
        return FindReachableSharedGatheringTool(actor, required) is { } tool ? tool.GatherQuantity : 0;
    }

    private static ToolDefinition? RequiredGatheringTool(string itemKind, MapResource source) =>
        ToolProgressionRules.RequiredToolForGathering(itemKind, source);

    private ToolDefinition? FindReachableSharedGatheringTool(string actor, ToolDefinition required)
    {
        return ToolProgressionRules.All
            .Where(definition => definition.Family == required.Family && definition.Tier >= required.Tier)
            .Select(definition => (Definition: definition, Lot: SharedItem(definition.ItemKind, actor)))
            .Where(item => item.Lot is { ConditionBasisPoints: > 0, FreshnessBasisPoints: > 0 })
            .OrderByDescending(item => item.Definition.Tier)
            .ThenByDescending(item => item.Lot!.ConditionBasisPoints)
            .ThenBy(item => item.Lot!.Id, StringComparer.Ordinal)
            .Select(item => item.Definition)
            .FirstOrDefault();
    }

    private bool CollectToolForGathering(string actor, PlaytestInhabitantState state,
        string itemKind, MapResource source)
    {
        if (ToolProgressionRules.PlanGather(itemKind, source, society.Checkpoint.Inventory, actor,
                worldSystems.Ecology.GetResource(source.Id).Quantity) is not null ||
            RequiredGatheringTool(itemKind, source) is not { } required ||
            FindReachableSharedGatheringTool(actor, required) is not { } available)
            return false;
        CollectEquipment(actor, state, available.ItemKind);
        return true;
    }

    private void ApplyGatheringInventory(string actor, string itemKind, ToolGatheringPlan plan)
    {
        ApplyInventoryTransition(inventory =>
        {
            var updated = inventory;
            var wornToolLotId = plan.ToolLotId;
            if (wornToolLotId is not null)
            {
                var tool = updated.GetLot(wornToolLotId);
                if (tool.Quantity > 1)
                {
                    wornToolLotId = $"{tool.Id}:use:{WorldTick}:{actor}";
                    updated = InventoryFixture.SplitLot(updated, tool.Id, 1, wornToolLotId);
                }
            }

            updated = InventoryFixture.AddLot(updated, $"material:{WorldTick}:{actor}", itemKind,
                actor, plan.Quantity, WorldTick);
            if (plan.TreeSeedQuantity > 0)
            {
                updated = InventoryFixture.AddLot(updated, $"tree-seed:{WorldTick}:{actor}",
                    TreeGrowthRules.TreeSeedItem, actor, plan.TreeSeedQuantity, WorldTick);
            }
            if (wornToolLotId is not null && plan.WearLossBasisPoints > 0)
                updated = InventoryFixture.WearSingleUnit(updated, wornToolLotId, plan.WearLossBasisPoints);
            return updated;
        });
    }

    private void ApplyToolWork(string actor, params ToolWorkPlan[] plans)
    {
        ApplyInventoryTransition(inventory => ApplyToolWorkToInventory(inventory, actor, WorldTick, plans));
    }

    private static InventoryCheckpoint ApplyToolWorkToInventory(InventoryCheckpoint inventory, string actor,
        long tick, IReadOnlyList<ToolWorkPlan> plans)
    {
        var updated = inventory;
        foreach (var plan in plans)
        {
            var lot = updated.GetLot(plan.ToolLotId);
            var definition = ToolProgressionRules.Find(lot.ItemKind);
            if (definition is null || ToolProgressionRules.PlanWorkForLot(updated, actor,
                    definition.Family, lot.Id) is null)
                throw new InvalidOperationException("The selected tool must remain usable and physically carried by its owner.");
            var usedLotId = lot.Id;
            if (lot.Quantity > 1)
            {
                usedLotId = $"{lot.Id}:use:{tick}:{actor}";
                updated = InventoryFixture.SplitLot(updated, lot.Id, 1, usedLotId);
            }
            updated = InventoryFixture.WearSingleUnit(updated, usedLotId, plan.WearLossBasisPoints);
        }
        return updated;
    }

    private void AddToolRepairCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseholdBuildingWithTag(householdId, "blacksmith") is not { } blacksmith ||
            !inhabitants.TryGetValue(actor, out var state) ||
            !IsWithinInteractionRange(state.Position, blacksmith.Position, 0) &&
            FindUnoccupiedRoute(actor, state.Position, blacksmith.Position, 0).Count == 0)
            return;

        var inventory = society.Checkpoint.Inventory;
        foreach (var tool in inventory.Lots.Where(lot => lot.OwnerId == actor &&
                     lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
                     ToolProgressionRules.Find(lot.ItemKind) is not null &&
                     lot.ConditionBasisPoints < 10_000 && UnreservedQuantity(inventory, lot) > 0)
                 .OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            var materials = ToolProgressionRules.RepairMaterials(tool.ItemKind);
            if (materials.Count == 0 || materials.Any(input => !CanPrepareRepairInput(actor, input.ResourceId)))
                continue;
            candidates.Add(new CognitionCandidate(RepairToolPrefix + tool.Id,
                $"Repair the worn {tool.ItemKind.Replace('_', ' ')} at the household Blacksmith.", 23,
                blacksmith.InstanceId));
        }
    }

    private bool CanPrepareRepairInput(string actor, string itemKind) =>
        HasCarriedItem(actor, itemKind) || SharedItem(itemKind, actor) is not null ||
        MaterialSource(itemKind, actor) is not null;

    private void RepairTool(string actor, PlaytestInhabitantState state, string lotId)
    {
        var inventory = society.Checkpoint.Inventory;
        var tool = inventory.Lots.FirstOrDefault(lot => lot.Id == lotId && lot.OwnerId == actor &&
            lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
            ToolProgressionRules.Find(lot.ItemKind) is not null && lot.ConditionBasisPoints < 10_000 &&
            UnreservedQuantity(inventory, lot) > 0);
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        var blacksmith = householdId is null ? null : HouseholdBuildingWithTag(householdId, "blacksmith");
        if (tool is null || blacksmith is null)
            return;

        var materialNeeds = ToolProgressionRules.RepairMaterials(tool.ItemKind);
        foreach (var input in materialNeeds)
        {
            if (HasCarriedMaterial(actor, input.ResourceId, input.Amount)) continue;
            if (SharedItem(input.ResourceId, actor) is not null)
            {
                CollectEquipment(actor, state, input.ResourceId);
                return;
            }
            if (MaterialSource(input.ResourceId, actor) is { } source)
            {
                GatherProjectMaterial(actor, state, input.ResourceId, source);
                return;
            }
            return;
        }

        if (state.Position != blacksmith.Position)
        {
            MoveToward(actor, state, blacksmith.Position, "repair_tool", 0);
            return;
        }

        var repairId = tool.Id;
        ApplyInventoryTransition(current =>
        {
            var updated = current;
            var currentTool = updated.GetLot(repairId);
            if (currentTool.Quantity > 1)
            {
                repairId = $"{currentTool.Id}:repair:{WorldTick}:{actor}";
                updated = InventoryFixture.SplitLot(updated, currentTool.Id, 1, repairId);
            }

            var reservations = new List<string>();
            foreach (var input in materialNeeds)
            {
                var material = updated.Lots.Where(lot => lot.OwnerId == actor &&
                        lot.ItemKind == input.ResourceId && lot.StorageBuildingId is null &&
                        lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
                        AvailableLotQuantity(lot) >= input.Amount)
                    .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
                if (material is null)
                    throw new InvalidOperationException("Repair materials must be physically carried by the tool owner.");
                var reservationId = $"equipment-repair:{WorldTick}:{actor}:{repairId}:{input.ResourceId}";
                updated = InventoryFixture.Reserve(updated, reservationId, actor, material.Id,
                    input.Amount, "equipment_repair", checked(WorldTick + 1));
                reservations.Add(reservationId);
            }

            return InventoryFixture.RepairSingleUnit(updated, repairId, 10_000, reservations);
        });
        AppendEvent("tool_repaired", $"{actor}:{repairId}:{blacksmith.InstanceId}");
    }

    private bool HasCarriedMaterial(string actor, string itemKind, int quantity) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind &&
                lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.GroundPosition is null)
            .Sum(AvailableLotQuantity) >= quantity;

    private static int UnreservedQuantity(InventoryCheckpoint inventory, InventoryLot lot)
    {
        var reserved = inventory.Reservations.Where(reservation => reservation.LotId == lot.Id &&
                reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                    InventoryReservationState.Committed)
            .Sum(reservation => reservation.Quantity);
        return Math.Max(0, lot.Quantity - reserved);
    }

}
