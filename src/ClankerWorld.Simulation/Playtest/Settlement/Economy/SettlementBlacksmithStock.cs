using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string MineMaterialPrefix = "mine_material:";

    private void AddCraftToolCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        foreach (var kind in Enum.GetValues<ToolKind>())
        {
            if (SharedTool(actor, kind) is not { } shared ||
                ToolCapabilities.ForItem(shared.ItemKind) is not { } tool ||
                CarriedTool(actor, kind) is { } carried &&
                ToolCapabilities.ForItem(carried.ItemKind)!.Tier >= tool.Tier) continue;
            var id = tool.ItemKind switch
            {
                "wooden_axe" => "collect_wooden_axe",
                "wooden_pickaxe" => "collect_wooden_pickaxe",
                _ => CollectToolPrefix + tool.ItemKind,
            };
            candidates.Add(new(id, $"Collect an accessible {tool.ItemKind.Replace('_', ' ')} for work.", 18));
        }
    }

    private PlacedBuilding? BlacksmithForHousehold(string householdId) =>
        HouseholdBuildingWithTag(householdId, "blacksmith");

    private int BlacksmithOreStocked(string householdId, string blacksmithId) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
            lot.StorageBuildingId == blacksmithId && lot.ItemKind == "iron_ore").Sum(AvailableLotQuantity);

    private InventoryLot? PersonalSmithOre(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == "iron_ore" &&
            lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddBlacksmithOreCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith || !NeedsSmithIron(householdId) ||
            BlacksmithOreStocked(householdId, blacksmith.InstanceId) >= 8) return;
        if (PersonalSmithOre(actor) is not null)
        {
            candidates.Add(new("deliver_smith_ore", "Carry mined iron ore into the household Blacksmith.", 22, blacksmith.InstanceId));
            return;
        }
        if (MaterialSource("iron_ore", actor) is not { } source) return;
        candidates.Add(new("gather_smith_ore", "Mine iron ore with a stone or iron pickaxe for the household Blacksmith.", 30, source.Id));
    }

    private void GatherBlacksmithOre(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith || !NeedsSmithIron(householdId) ||
            BlacksmithOreStocked(householdId, blacksmith.InstanceId) >= 8 ||
            PersonalSmithOre(actor) is not null || MaterialSource("iron_ore", actor) is not { } source) return;
        if (CollectGatheringTool(actor, state, "iron_ore")) return;
        GatherProjectMaterial(actor, state, "iron_ore", source);
    }

    private void DeliverBlacksmithOre(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            PersonalSmithOre(actor) is not { } ore) return;
        var stocked = BlacksmithOreStocked(householdId, blacksmith.InstanceId);
        if (stocked >= 8) return;
        if (state.Position != blacksmith.Position)
        {
            MoveToward(actor, state, blacksmith.Position, "smith_ore", 0);
            return;
        }
        var quantity = Math.Min(8 - stocked, AvailableLotQuantity(ore));
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"smith-ore-delivery:{WorldTick}:{actor}", actor, householdId, ore.Id,
            quantity, "smith_ore_delivered", blacksmith.InstanceId));
        AppendEvent("smith_ore_delivered", $"{actor}:{ore.Id}:{quantity}:{blacksmith.InstanceId}");
    }

    private IEnumerable<(string Kind, int Target)> SmithInputTargets(string householdId, string smithId)
    {
        var definitionId = worldSimulation.Buildings.Single(item => item.InstanceId == smithId).DefinitionId;
        var needed = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == definitionId &&
            NeedsRecipeOutput(recipe, householdId));
        foreach (var input in needed.SelectMany(recipe => recipe.Inputs).GroupBy(input => input.ResourceId)
                     .OrderBy(input => input.Key == "wood" ? 0 : input.Key == "stone" ? 1 : 2)
                     .ThenBy(input => input.Key, StringComparer.Ordinal))
            yield return (input.Key, Math.Max(input.Max(item => item.Amount) * 2, input.Key == "iron_ore" ? 8 : 2));
        // Worn tools need a physical repair input even after the fresh-tool
        // reserve is full and its crafting recipes stop asking for supplies.
        foreach (var group in ToolsForHousehold(householdId).Where(lot => lot.ConditionBasisPoints is > 0 and <= 4_000)
                     .Select(lot => ToolCapabilities.ForItem(lot.ItemKind)).Where(tool => tool is not null)
                     .GroupBy(tool => tool!.RepairMaterial).OrderBy(group => group.Key, StringComparer.Ordinal))
            yield return (group.Key, 2);
    }

    private (string Kind, int Missing, InventoryLot? Personal, InventoryLot? Stock, ClankerWorld.Simulation.Harness.MapResource? Source)?
        BlacksmithInputNeed(string actor, string householdId, string smithId)
    {
        foreach (var (kind, target) in SmithInputTargets(householdId, smithId))
        {
            var stocked = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.StorageBuildingId == smithId && lot.ItemKind == kind).Sum(AvailableLotQuantity);
            var incoming = society.Checkpoint.Inventory.Lots.Where(lot => lot.DeliveryBuildingId == smithId &&
                lot.ItemKind == kind).Sum(AvailableLotQuantity);
            var missing = target - stocked - incoming;
            if (missing <= 0) continue;
            var personal = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                lot.ItemKind == kind && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                AvailableLotQuantity(lot) > 0).OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            var stock = personal is null ? society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.ItemKind == kind && lot.StorageBuildingId != smithId && AvailableLotQuantity(lot) > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault() : null;
            var source = personal is null && stock is null ? MaterialSource(kind, actor) : null;
            if (personal is not null || stock is not null || source is not null)
                return (kind, missing, personal, stock, source);
        }
        return null;
    }

    private void AddBlacksmithStockCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            BlacksmithForHousehold(householdId) is not { } smith ||
            BlacksmithInputNeed(actor, householdId, smith.InstanceId) is not { } need) return;
        candidates.Add(new("haul_smith_input",
            $"Bring {need.Kind.Replace('_', ' ')} into the household Blacksmith for making and repairing tools.", 24, smith.InstanceId));
    }

    private void HaulBlacksmithInput(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } smith ||
            BlacksmithInputNeed(actor, householdId, smith.InstanceId) is not { } need) return;
        if (need.Personal is { } personal)
        {
            if (state.Position != smith.Position)
            {
                MoveToward(actor, state, smith.Position, "smith_input", 0);
                return;
            }
            var quantity = Math.Min(need.Missing, AvailableLotQuantity(personal));
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"smith-input-delivery:{WorldTick}:{actor}", actor, householdId, personal.Id,
                quantity, "smith_input_delivered", smith.InstanceId));
            AppendEvent("smith_input_delivered", $"{actor}:{personal.Id}:{quantity}:{smith.InstanceId}");
            return;
        }
        if (need.Stock is { } input)
        {
            var source = HouseholdStockPosition(input);
            var range = HouseholdStockInteractionRange(input);
            if (!IsWithinInteractionRange(state.Position, source, range))
            {
                MoveToward(actor, state, source, "smith_input", range);
                return;
            }
            var quantity = Math.Min(HouseHaulLoadQuantity, Math.Min(need.Missing, AvailableLotQuantity(input)));
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"smith-input-pickup:{WorldTick}:{actor}", householdId, actor, input.Id,
                quantity, "smith_input_picked_up", destinationDeliveryBuildingId: smith.InstanceId));
            AppendEvent("smith_input_picked_up", $"{actor}:{input.Id}:{quantity}:{smith.InstanceId}");
            return;
        }
        if (need.Source is { } resource)
        {
            if (CollectGatheringTool(actor, state, need.Kind)) return;
            GatherProjectMaterial(actor, state, need.Kind, resource);
        }
    }

    private void AddMiningCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var kind in new[] { "stone", "iron_ore", "gold_ore", "diamond" })
            if (CarriedTool(actor, ToolKind.Pickaxe, ToolCapabilities.RequiredMiningTier(kind)) is not null &&
                society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind)
                    .Sum(AvailableLotQuantity) < 2 && MaterialSource(kind, actor) is { } source)
                candidates.Add(new(MineMaterialPrefix + kind,
                    $"Extract {kind.Replace('_', ' ')} from the finite outcrop with the carried pickaxe.", 35, source.Id));
    }

    private void MineMaterial(string actor, PlaytestInhabitantState state, string kind)
    {
        if (!AdultResident(actor) || kind is not ("stone" or "iron_ore" or "gold_ore" or "diamond") ||
            MaterialSource(kind, actor) is not { } source) return;
        GatherProjectMaterial(actor, state, kind, source);
    }
}
