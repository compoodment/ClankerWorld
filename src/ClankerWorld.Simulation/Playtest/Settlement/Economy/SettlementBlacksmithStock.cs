using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void AddCraftToolCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        foreach (var (kind, candidate) in new[]
                 { ("wooden_axe", "collect_wooden_axe"), ("wooden_pickaxe", "collect_wooden_pickaxe") })
        {
            if (!HasCarriedItem(actor, kind) && SharedItem(kind, actor) is not null)
                candidates.Add(new CognitionCandidate(candidate,
                    $"Collect an accessible {kind.Replace('_', ' ')} for resource work.", 18));
        }
    }

    private PlacedBuilding? BlacksmithForHousehold(string householdId) => worldSimulation.Buildings
        .Where(building => building.HouseholdId == householdId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("blacksmith", StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();

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
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithOreStocked(householdId, blacksmith.InstanceId) >= 2)
            return;
        if (PersonalSmithOre(actor) is not null)
        {
            if (state.Position == blacksmith.Position ||
                FindUnoccupiedRoute(actor, state.Position, blacksmith.Position, 0).Count > 0)
                candidates.Add(new CognitionCandidate("deliver_smith_ore",
                    "Carry mined iron ore into the household Blacksmith.", 22, blacksmith.InstanceId));
            return;
        }
        if (society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == householdId &&
                lot.ItemKind == "iron_ore" && AvailableLotQuantity(lot) > 0))
            return;
        if (MaterialSource("iron_ore", actor) is not { } source ||
            !IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange) &&
            FindUnoccupiedRoute(actor, state.Position, source.Position, ResourceInteractionRange).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("gather_smith_ore",
            "Mine iron ore for the household Blacksmith.", 30, source.Id));
    }

    private void GatherBlacksmithOre(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithOreStocked(householdId, blacksmith.InstanceId) >= 2 ||
            PersonalSmithOre(actor) is not null ||
            MaterialSource("iron_ore", actor) is not { } source)
            return;
        if (!HasCarriedItem(actor, "wooden_pickaxe") && SharedItem("wooden_pickaxe", actor) is not null)
        {
            CollectEquipment(actor, state, "wooden_pickaxe");
            return;
        }
        GatherProjectMaterial(actor, state, "iron_ore", source);
    }

    private void DeliverBlacksmithOre(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            PersonalSmithOre(actor) is not { } ore)
            return;
        var stocked = BlacksmithOreStocked(householdId, blacksmith.InstanceId);
        if (stocked >= 2) return;
        if (state.Position != blacksmith.Position)
        {
            MoveToward(actor, state, blacksmith.Position, "smith_ore", 0);
            return;
        }
        var quantity = Math.Min(2 - stocked, AvailableLotQuantity(ore));
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"smith-ore-delivery:{WorldTick}:{actor}", actor, householdId, ore.Id,
            quantity, "smith_ore_delivered", blacksmith.InstanceId));
        AppendEvent("smith_ore_delivered", $"{actor}:{ore.Id}:{quantity}:{blacksmith.InstanceId}");
    }

    private InventoryLot? BlacksmithInputForDelivery(string householdId, string blacksmithId)
    {
        foreach (var (kind, target) in new[] { ("wood", 6), ("iron_ore", 2) })
        {
            var stocked = society.Checkpoint.Inventory.Lots
                .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == blacksmithId &&
                    lot.ItemKind == kind).Sum(AvailableLotQuantity);
            if (stocked >= target) continue;
            var source = society.Checkpoint.Inventory.Lots
                .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId != blacksmithId &&
                    lot.ItemKind == kind && AvailableLotQuantity(lot) > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            if (source is not null) return source;
        }
        return null;
    }

    private void AddBlacksmithStockCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithInputForDelivery(householdId, blacksmith.InstanceId) is not { } input)
            return;
        var source = HouseholdStockPosition(input);
        var range = HouseholdStockInteractionRange(input);
        if ((!IsWithinInteractionRange(state.Position, source, range) &&
             FindUnoccupiedRoute(actor, state.Position, source, range).Count == 0) ||
            FindUnoccupiedRoute(actor, source, blacksmith.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("haul_smith_input",
            $"Carry household {input.ItemKind} into its Blacksmith for on-site tool work.",
            24, blacksmith.InstanceId));
    }

    private void HaulBlacksmithInput(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithInputForDelivery(householdId, blacksmith.InstanceId) is not { } input)
            return;
        var source = HouseholdStockPosition(input);
        var range = HouseholdStockInteractionRange(input);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "smith_input", range);
            return;
        }
        var stocked = society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == blacksmith.InstanceId &&
                lot.ItemKind == input.ItemKind).Sum(AvailableLotQuantity);
        var target = input.ItemKind == "wood" ? 6 : 2;
        var quantity = Math.Min(HouseHaulLoadQuantity, Math.Min(target - stocked, AvailableLotQuantity(input)));
        if (quantity <= 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"smith-input-pickup:{WorldTick}:{actor}", householdId, actor, input.Id,
            quantity, "smith_input_picked_up", destinationDeliveryBuildingId: blacksmith.InstanceId));
        AppendEvent("smith_input_picked_up", $"{actor}:{input.Id}:{quantity}:{blacksmith.InstanceId}");
    }
}
