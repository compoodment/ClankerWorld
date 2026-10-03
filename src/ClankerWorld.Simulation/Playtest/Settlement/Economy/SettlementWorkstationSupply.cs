using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

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

    /// <summary>The source station's live two-batch input need, independent of available supply actions.</summary>
    private int WorkstationInputTarget(PlacedBuilding building, string itemKind) => building.HouseholdId is { } householdId
        ? worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == building.DefinitionId &&
                !recipe.IsCrop && !IsGenericFoodRecipe(recipe) && NeedsRecipeOutput(recipe, householdId))
            .SelectMany(recipe => recipe.Inputs).Where(input => input.ResourceId == itemKind)
            .Select(input => input.Amount).DefaultIfEmpty(0).Max() * SupplyBatches
        : 0;

    private static int UsableWorkstationQuantity(InventoryCheckpoint inventory, InventoryLot lot) =>
        lot.ContainerLotId is { } containerId && !inventory.Lots.Any(container =>
            container.Id == containerId && container.ConditionBasisPoints > 0)
            ? 0 : PersonalEquipmentRules.AvailableQuantity(inventory, lot);

    private static int WorkstationOnsiteQuantity(InventoryCheckpoint inventory, PlacedBuilding building, string itemKind) =>
        building.HouseholdId is { } householdId ? inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.StorageBuildingId == building.InstanceId && lot.ItemKind == itemKind)
            .Sum(lot => UsableWorkstationQuantity(inventory, lot)) : 0;

    private int WorkstationIncomingQuantity(InventoryCheckpoint inventory, PlacedBuilding building, string itemKind) =>
        building.HouseholdId is { } householdId ? inventory.Lots.Where(lot => lot.DeliveryBuildingId == building.InstanceId &&
                lot.ItemKind == itemKind && (lot.OwnerId == householdId ||
                    society.Checkpoint.Inhabitants.Any(person => person.Id == lot.OwnerId &&
                        person.HouseholdId == householdId && person.Status == SocietyInhabitantStatus.Active)) &&
                CanCreditWorkstationIncoming(inventory, lot))
            .Sum(lot => UsableWorkstationQuantity(inventory, lot)) : 0;

    private static bool CanCreditWorkstationIncoming(InventoryCheckpoint inventory, InventoryLot lot)
    {
        var root = lot.ContainerLotId is { } containerId ? inventory.GetLot(containerId) : lot;
        return !InventoryContainerRules.IsContainer(root.ItemKind) ||
            !UnusableDeliveryStock(inventory, root) && !HasActiveContainerReservation(inventory, root.Id);
    }

    private int WorkstationSourceSurplus(InventoryCheckpoint inventory, InventoryLot resource)
    {
        if (resource.StorageBuildingId is not { } sourceId ||
            worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == sourceId &&
                building.HouseholdId == resource.OwnerId) is not { } source)
            return int.MaxValue;
        var available = WorkstationOnsiteQuantity(inventory, source, resource.ItemKind);
        var incoming = WorkstationIncomingQuantity(inventory, source, resource.ItemKind);
        return Math.Max(0, available + incoming - WorkstationInputTarget(source, resource.ItemKind));
    }

    /// <summary>Actual available loose stock above its held source station's live input reserve.</summary>
    private int WorkstationStockSurplus(InventoryCheckpoint inventory, InventoryLot resource) =>
        Math.Min(UsableWorkstationQuantity(inventory, resource),
            WorkstationSourceSurplus(inventory, resource));

    /// <summary>A whole vessel may leave only when every moved ingredient is source surplus.</summary>
    private bool CanRemoveWorkstationStock(InventoryCheckpoint inventory, InventoryLot root, int quantity)
    {
        if (quantity <= 0) return false;
        if (!InventoryContainerRules.IsContainer(root.ItemKind))
            return quantity <= WorkstationStockSurplus(inventory, root);
        if (quantity != 1 || UnusableDeliveryStock(inventory, root) || HasActiveContainerReservation(inventory, root.Id))
            return false;
        return inventory.Lots.Where(lot => lot.ContainerLotId == root.Id)
            .GroupBy(lot => lot.ItemKind).All(group =>
                group.Sum(lot => UsableWorkstationQuantity(inventory, lot)) <=
                    WorkstationSourceSurplus(inventory, group.First()));
    }

    private IEnumerable<WorkstationSupplyNeed> WorkstationSupplyNeeds(string actor)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            yield break;
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var building in worldSimulation.Buildings.Where(item => item.HouseholdId == householdId)
                     .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(building.DefinitionId, out var definition) ||
                HouseholdBuildingKind(definition) is null)
                continue;
            var recipes = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == definition.CanonicalId &&
                    NeedsRecipeOutput(recipe, householdId) &&
                    (!HasDedicatedSupply(definition) || recipe.Tags.Any(tag => tag is "pottery" or "care" or "named-meal")))
                .OrderBy(recipe => recipe.CanonicalId, StringComparer.Ordinal).ToArray();
            foreach (var input in recipes.SelectMany(recipe => recipe.Inputs).GroupBy(input => input.ResourceId))
            {
                var target = input.Max(item => item.Amount) * SupplyBatches;
                var inventory = society.Checkpoint.Inventory;
                var stocked = WorkstationOnsiteQuantity(inventory, building, input.Key);
                var incoming = WorkstationIncomingQuantity(inventory, building, input.Key);
                var missing = target - stocked - incoming;
                if (missing <= 0)
                    continue;
                var deliveryRoom = WorkstationDeliveryRoom(inventory, building.InstanceId);
                var carried = inventory.Lots
                    .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) && lot.ItemKind == input.Key &&
                        AvailableLotQuantity(lot) > 0)
                    .Select(lot => lot.ContainerLotId is { } containerId
                        ? inventory.GetLot(containerId) : lot)
                    .Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) &&
                        lot.DeliveryBuildingId is null && deliveryRoom > 0 && AvailableLotQuantity(lot) > 0 &&
                        (!InventoryContainerRules.IsContainer(lot.ItemKind) ||
                         ContainerFamilyQuantity(inventory, lot.Id) <= deliveryRoom) &&
                        (!InventoryContainerRules.IsContainer(lot.ItemKind) ||
                         !HasActiveContainerReservation(inventory, lot.Id)))
                    .DistinctBy(lot => lot.Id)
                    .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
                var stock = carried is not null ? null : SpareHouseholdStock(actor, householdId, input.Key, building) ??
                    AvailableWarehouseStock(actor, input.Key).FirstOrDefault(lot =>
                        WorkstationPickupQuantity(actor, inventory, lot, building.InstanceId, int.MaxValue) > 0);
                var source = carried is not null || stock is not null ? null : MaterialSource(input.Key, actor);
                if (source is not null && (deliveryRoom <= 0 ||
                    ProjectMaterialCarryUnits(actor, input.Key, source) > FreeCarryCapacity(actor)))
                    source = null;
                if (carried is null && stock is null && source is null)
                    continue;
                if (stock is not null && FreeCarryCapacity(actor) == 0 || source is not null &&
                    FreeCarryCapacity(actor) < ProjectMaterialCarryUnits(actor, input.Key, source))
                    continue;
                yield return new WorkstationSupplyNeed(building, definition, input.Key, missing, carried, stock, source);
            }
        }
    }

    /// <summary>Household stock not already set aside at another workstation.</summary>
    private int WorkstationDeliveryRoom(InventoryCheckpoint inventory, string destinationId) =>
        Math.Max(0, StorageRoom(destinationId) - inventory.Lots
            .Where(lot => lot.DeliveryBuildingId == destinationId).Sum(lot => lot.Quantity));

    private int WorkstationPickupQuantity(string actor, InventoryCheckpoint inventory, InventoryLot stock,
        string destinationId, int missing)
    {
        var capacity = Math.Min(FreeCarryCapacity(actor), WorkstationDeliveryRoom(inventory, destinationId));
        if (capacity <= 0)
            return 0;
        return InventoryContainerRules.IsContainer(stock.ItemKind)
            ? ContainerFamilyQuantity(inventory, stock.Id) <= capacity &&
                CanRemoveWorkstationStock(inventory, stock, 1) ? 1 : 0
            : Math.Min(HouseHaulLoadQuantity, Math.Min(capacity, Math.Min(missing,
                WorkstationStockSurplus(inventory, stock))));
    }

    private InventoryLot? SpareHouseholdStock(string actor, string householdId, string itemKind,
        PlacedBuilding destination)
    {
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.ItemKind == itemKind &&
                AvailableLotQuantity(lot) > 0)
            .Select(lot => lot.ContainerLotId is { } containerId
                ? inventory.GetLot(containerId) : lot)
            .Where(lot => lot.OwnerId == householdId && lot.CarrierId is null && lot.StorageBuildingId != destination.InstanceId &&
                lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
                (!InventoryContainerRules.IsContainer(lot.ItemKind) ||
                 !HasActiveContainerReservation(inventory, lot.Id)) &&
                (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                    building.InstanceId == lot.StorageBuildingId && worldContent.Buildings.Any(definition =>
                        definition.CanonicalId == building.DefinitionId &&
                        definition.Tags.Any(tag => tag is "house" or "silo" or "farmhouse" or "restaurant")))))
            .Where(lot => WorkstationPickupQuantity(actor, inventory, lot, destination.InstanceId,
                int.MaxValue) > 0)
            .Where(lot => IsWithinInteractionRange(inhabitants[actor].Position, HouseholdStockPosition(lot),
                    HouseholdStockInteractionRange(lot)) ||
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, HouseholdStockPosition(lot),
                    HouseholdStockInteractionRange(lot)).Count > 0)
            .DistinctBy(lot => lot.Id)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

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
            var currentInventory = society.Checkpoint.Inventory;
            var room = WorkstationDeliveryRoom(currentInventory, building.InstanceId);
            var quantity = InventoryContainerRules.IsContainer(carried.ItemKind)
                ? ContainerFamilyQuantity(currentInventory, carried.Id) <= room ? 1 : 0
                : Math.Min(room, Math.Min(need.Missing, AvailableLotQuantity(carried)));
            if (quantity <= 0) return;
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
            var currentInventory = society.Checkpoint.Inventory;
            var quantity = WorkstationPickupQuantity(actor, currentInventory, stock, building.InstanceId, need.Missing);
            if (quantity == 0) return;
            // The existing delivery step carries the picked-up load into the building.
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"workstation-pickup:{WorldTick}:{actor}", stock.OwnerId, actor, stock.Id, quantity,
                "workstation_input_picked_up", destinationDeliveryBuildingId: building.InstanceId));
            AppendEvent("workstation_input_picked_up", $"{actor}:{stock.Id}:{quantity}:{building.InstanceId}");
            return;
        }
        if (need.Source is { } source)
            GatherProjectMaterial(actor, state, itemKind, source);
    }
}
