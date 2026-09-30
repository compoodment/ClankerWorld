using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Keeps a household workstation stocked for its own recipes. Recipes there
/// use only on-site stock, so an adult of the holding household carries the
/// inputs in: from what they carry, from the household's own stock, or by
/// gathering from a reachable source. The Farmhouse, Blacksmith and House
/// keep their older, dedicated hauling.
/// </summary>
public sealed partial class PrivateWorldRuntime
{
    private const string SupplyWorkstationPrefix = "supply_workstation:";

    /// <summary>Enough on-site stock for two batches of the largest recipe needing the item.</summary>
    private const int SupplyBatches = 2;

    private sealed record WorkstationSupplyNeed(PlacedBuilding Building, BuildingDefinition Definition,
        string ItemKind, int Missing, InventoryLot? Carried, InventoryLot? HouseholdStock, MapResource? Source);

    private static bool HasDedicatedSupply(BuildingDefinition definition) =>
        definition.Tags.Any(tag => tag is "house" or "farmhouse" or "blacksmith");

    private IEnumerable<WorkstationSupplyNeed> WorkstationSupplyNeeds(string actor)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            yield break;
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var building in worldSimulation.Buildings.Where(item => item.HouseholdId == householdId)
                     .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(building.DefinitionId, out var definition) ||
                HouseholdBuildingKind(definition) is null || HasDedicatedSupply(definition))
                continue;
            var recipes = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == definition.CanonicalId &&
                    NeedsRecipeOutput(recipe, householdId))
                .OrderBy(recipe => recipe.CanonicalId, StringComparer.Ordinal).ToArray();
            foreach (var input in recipes.SelectMany(recipe => recipe.Inputs).GroupBy(input => input.ResourceId))
            {
                var target = input.Max(item => item.Amount) * SupplyBatches;
                var stocked = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                        lot.StorageBuildingId == building.InstanceId && lot.ItemKind == input.Key)
                    .Sum(AvailableLotQuantity);
                var incoming = society.Checkpoint.Inventory.Lots.Where(lot =>
                        lot.DeliveryBuildingId == building.InstanceId && lot.ItemKind == input.Key)
                    .Sum(AvailableLotQuantity);
                var missing = target - stocked - incoming;
                if (missing <= 0)
                    continue;
                var carried = society.Checkpoint.Inventory.Lots
                    .Where(lot => lot.OwnerId == actor && lot.ItemKind == input.Key &&
                        lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0)
                    .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
                var stock = carried is not null ? null : SpareHouseholdStock(householdId, input.Key, building.InstanceId);
                var source = carried is not null || stock is not null ? null : MaterialSource(input.Key, actor);
                if (carried is null && stock is null && source is null)
                    continue;
                yield return new WorkstationSupplyNeed(building, definition, input.Key, missing, carried, stock, source);
            }
        }
    }

    /// <summary>Household stock not already set aside at another workstation.</summary>
    private InventoryLot? SpareHouseholdStock(string householdId, string itemKind, string destinationId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.ItemKind == itemKind &&
                lot.StorageBuildingId != destinationId && AvailableLotQuantity(lot) > 0 &&
                (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                    building.InstanceId == lot.StorageBuildingId && worldContent.Buildings.Any(definition =>
                        definition.CanonicalId == building.DefinitionId &&
                        definition.Tags.Any(tag => tag is "house" or "silo")))))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddWorkstationSupplyCandidate(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || CarriedHouseDelivery(actor) is not null ||
            WorkstationSupplyNeeds(actor).FirstOrDefault() is not { } need)
            return;
        candidates.Add(new CognitionCandidate(SupplyWorkstationPrefix + need.ItemKind,
            $"Bring {need.ItemKind} into the household {need.Definition.DisplayName} for its work.",
            24, need.Building.InstanceId));
    }

    private void SupplyWorkstation(string actor, PlaytestInhabitantState state, string itemKind)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            WorkstationSupplyNeeds(actor).FirstOrDefault(item => item.ItemKind == itemKind) is not { } need)
            return;
        var building = need.Building;
        if (need.Carried is { } carried)
        {
            if (state.Position != building.Position)
            {
                MoveToward(actor, state, building.Position, "supply_workstation", 0);
                return;
            }
            var quantity = Math.Min(need.Missing, AvailableLotQuantity(carried));
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"workstation-supply:{WorldTick}:{actor}", actor, householdId, carried.Id, quantity,
                "workstation_supplied", building.InstanceId));
            AppendEvent("workstation_supplied", $"{actor}:{carried.Id}:{quantity}:{building.InstanceId}");
            return;
        }
        if (need.HouseholdStock is { } stock)
        {
            var position = HouseholdStockPosition(stock);
            var range = HouseholdStockInteractionRange(stock);
            if (!IsWithinInteractionRange(state.Position, position, range))
            {
                MoveToward(actor, state, position, "supply_workstation", range);
                return;
            }
            var quantity = Math.Min(HouseHaulLoadQuantity, Math.Min(need.Missing, AvailableLotQuantity(stock)));
            // The existing delivery step carries the picked-up load into the building.
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"workstation-pickup:{WorldTick}:{actor}", householdId, actor, stock.Id, quantity,
                "workstation_input_picked_up", destinationDeliveryBuildingId: building.InstanceId));
            AppendEvent("workstation_input_picked_up", $"{actor}:{stock.Id}:{quantity}:{building.InstanceId}");
            return;
        }
        if (need.Source is { } source)
            GatherProjectMaterial(actor, state, itemKind, source);
    }
}
