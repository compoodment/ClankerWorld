using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record TreeOrderPlan(GridPoint Site, string Species, InventoryLot Seed, bool Carried);

    private static bool IsTreePlantingOrder(string action) =>
        action is "plant_tree" or "plant_broadleaf" or "plant_conifer" or "plant_orchard";

    private GridPoint? TreeOrderSite(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (instruction.Order!.TargetPosition is not { } site)
            return PlantingSite(instruction.TargetInhabitantId, person.Position);
        if (!map.Contains(site) || towns.Any(town => town.BorderTiles.Contains(site)) ||
            PlantingSiteRefusal(site, PlantingObstacles()) is not null || TreeAreaFull(site)) return null;
        return IsWithinInteractionRange(person.Position, site, ResourceInteractionRange) ||
            FindUnoccupiedRoute(instruction.TargetInhabitantId, person.Position, site, ResourceInteractionRange).Count > 0
            ? site : null;
    }

    private string TreeOrderSpecies(OwnerInstructionOrder order, GridPoint site) => order.Action switch
    {
        "plant_broadleaf" => TreeGrowthRules.Broadleaf,
        "plant_conifer" => TreeGrowthRules.Conifer,
        "plant_orchard" => TreeGrowthRules.Orchard,
        _ => PlantingSpecies(site),
    };

    private InventoryLot? CarriedTreeOrderSeed(string actor, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == kind && PersonalEquipmentRules.IsCarried(lot, actor) &&
            lot.ContainerLotId is null && lot.DeliveryBuildingId is null && PlantingSeedQuantity(lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? CollectableTreeOrderSeed(string actor, string kind) =>
        RecoverablePersonalGoods(actor, kind).Where(lot => PlantingSeedQuantity(lot) > 0)
            .OrderBy(lot => map.FootDistance(inhabitants[actor].Position, HouseholdStockPosition(lot)))
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault() ?? SharedItem(kind, actor);

    private TreeOrderPlan? TreePlantingOrderPlan(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor) || TreeOrderSite(instruction, person) is not { } site) return null;
        var species = TreeOrderSpecies(instruction.Order!, site);
        var kind = TreeGrowthRules.SeedItem(species);
        if (CarriedTreeOrderSeed(actor, kind) is { } carried) return new(site, species, carried, true);
        return FreeCarryCapacity(actor) > 0 && CollectableTreeOrderSeed(actor, kind) is { } stock
            ? new(site, species, stock, false) : null;
    }

    private CognitionCandidate? TreePlantingOrderCandidate(OwnerQueuedInstruction instruction, PlaytestInhabitantState person) =>
        TreePlantingOrderPlan(instruction, person) is { } plan
            ? new(instruction.Order!.Action, plan.Carried
                ? "Walk to the requested planting site and plant one sapling using your actual seed."
                : "Collect a permitted planting seed before travelling to the planting site.", 0, plan.Seed.Id)
            : null;

    private void ExecuteTreePlantingOrder(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (TreePlantingOrderPlan(instruction, person) is not { } plan)
        {
            SetOrderStatus(instruction, "blocked", TreePlantingOrderBlockedReason(instruction, person));
            return;
        }
        if (!plan.Carried)
        {
            if (plan.Seed.OwnerId == actor) _ = CollectPersonalGoods(actor, plan.Seed.Id, 1);
            else CollectEquipment(actor, person, TreeGrowthRules.SeedItem(plan.Species));
            return;
        }
        if (!IsWithinInteractionRange(person.Position, plan.Site, ResourceInteractionRange))
        {
            MoveToward(actor, person, plan.Site, "owner_order_tree_planting", ResourceInteractionRange);
            return;
        }
        var result = PlantTreeCore(actor, plan.Species, plan.Seed.Id, plan.Site);
        if (result.Planted) CreditOrderEffect(instruction, "tree:plant:" + result.TreeId, 1);
        else SetOrderStatus(instruction, "blocked", "The planting site or seed is no longer available; the seed was kept.");
    }

    private string TreePlantingOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor)) return "This agent is too young to plant trees.";
        if (!ReadyForBriefInteraction(actor)) return "The agent needs warmth before planting trees.";
        if (instruction.Order!.TargetPosition is { } site)
        {
            if (!map.Contains(site)) return "The requested planting tile is outside this world.";
            if (towns.Any(town => town.BorderTiles.Contains(site))) return "Plant new trees outside Town borders.";
            if (PlantingSiteRefusal(site, PlantingObstacles()) is not null)
                return "The requested planting tile needs empty grass or forest ground, clear of buildings, Roads and existing objects.";
            if (TreeAreaFull(site)) return "The requested planting area has reached its tree and resource limit.";
        }
        if (TreeOrderSite(instruction, person) is not { } destination)
            return "No open walking route reaches a free planting site outside the Town.";
        var kind = TreeGrowthRules.SeedItem(TreeOrderSpecies(instruction.Order!, destination));
        if (CarriedTreeOrderSeed(actor, kind) is not null) return "The planting site is temporarily unavailable.";
        if (FreeCarryCapacity(actor) <= 0) return "Make carrying space to collect a planting seed.";
        return $"Carry an available {kind.Replace('_', ' ')} or leave one in permitted reachable stock; reserved and foreign seeds cannot be used.";
    }
}
