using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static readonly string LegacyCookingPackageDigest = StarterContent.Create().PackageDigest;
    private void StageRestaurantContent() => StageBuiltInContent(RestaurantContent.PackageId,
        HouseContent.PackageId, RestaurantContent.Create, "restaurant_content_staged");

    private bool NeedsCookedMeals(string householdId, bool restaurant = false)
    {
        var population = society.Checkpoint.Inhabitants.Count(person => person.HouseholdId == householdId &&
            person.Status == ClankerWorld.Simulation.Society.SocietyInhabitantStatus.Active);
        var stock = society.Checkpoint.Inventory.Lots.Where(lot => IsPreparedMeal(lot.ItemKind) &&
            (lot.OwnerId == householdId || inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == householdId))
            .Sum(AvailableLotQuantity);
        var pending = worldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Running &&
            worldSimulation.Buildings.Any(site => site.InstanceId == job.BuildingInstanceId && site.HouseholdId == householdId))
            .Sum(job => worldContent.Recipes.First(recipe => recipe.CanonicalId == job.RecipeId).Outputs
                .Where(output => IsPreparedMeal(output.ResourceId)).Sum(output => output.Amount));
        // A Restaurant also maintains a small sale batch. Business offers remain physical stock.
        return stock + pending < Math.Max(2, population * 2) + (restaurant ? 2 : 0);
    }

    private static bool IsPreparedMeal(string kind) => kind is "simple_meal" or "porridge" or "bread" or
        "vegetable_stew" or "restaurant_meal";

    private bool KeepsPlantingReserve(RecipeDefinition recipe, string householdId) =>
        !HouseCookingContent.IsMealRecipe(recipe) || recipe.Inputs.All(input => !FoodItems.IsPlantingStock(input.ResourceId) ||
            HouseholdCropStock(householdId, input.ResourceId) - input.Amount >= FarmPlantingReserve(householdId, input.ResourceId));

    private int CookingSupplyQuantity(InventoryLot lot, string householdId) => lot.ItemKind == "potatoes"
        ? (int)Math.Min(AvailableLotQuantity(lot), Math.Max(0, HouseholdCropStock(householdId, lot.ItemKind) -
            FarmPlantingReserve(householdId, lot.ItemKind))) : AvailableLotQuantity(lot);

    private string MissingWorkstationIngredients(RecipeDefinition recipe, string householdId, string buildingId) =>
        string.Join(", ", recipe.Inputs.Where(input => society.Checkpoint.Inventory.Lots
                .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == buildingId && lot.ItemKind == input.ResourceId)
                .Sum(AvailableLotQuantity) < input.Amount)
            .Select(input => input.ResourceId.Replace('_', ' ')));

    private WorldProductionJob? FoodProductionJob(InventoryLot lot)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var origin = lot.Id;
        while (lot.ProvenanceLotId is { } id && visited.Add(lot.Id))
        {
            origin = id;
            if (society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == id) is not { } parent) break;
            lot = parent;
        }
        return worldSimulation.ProductionJobs.FirstOrDefault(job => origin.StartsWith(job.JobId + ":output:", StringComparison.Ordinal));
    }

    private bool IsEnrichedPorridge(InventoryLot lot) => lot.ItemKind == "porridge" && FoodProductionJob(lot) is { } job &&
        worldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId &&
            recipe.Tags.Contains("fruit-enriched", StringComparer.Ordinal));

    private int MealFullness(InventoryLot lot) => FoodItems.Fullness(lot.ItemKind) + (IsEnrichedPorridge(lot) ? 500 : 0);

    private bool IsLegacyCampCooking(RecipeDefinition recipe) => geographyOptions is not null && founderSetup is not null &&
        recipe.PackageDigest == LegacyCookingPackageDigest && recipe.LocalId == "meal";

    private IEnumerable<(PlacedBuilding Building, string Liquid, int Quantity)> NeededWorkstationLiquids(string householdId)
    {
        foreach (var building in worldSimulation.Buildings.Where(site => site.HouseholdId == householdId))
            foreach (var input in worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == building.DefinitionId &&
                         recipe.Inputs.Any(item => item.ResourceId is "water" or "milk") && NeedsRecipeOutput(recipe, householdId))
                         .SelectMany(recipe => recipe.Inputs).Where(input => input.ResourceId is "water" or "milk")
                         .GroupBy(input => input.ResourceId))
                yield return (building, input.Key, input.Max(item => item.Amount) * SupplyBatches);
    }

    private int AvailableWorkstationLiquid(string householdId, string buildingId, string liquid) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == buildingId &&
                lot.ItemKind == liquid && lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
                lot.CartId is null && lot.AnimalId is null && lot.ContainerLotId is { } vesselId &&
                society.Checkpoint.Inventory.Lots.Any(vessel => vessel.Id == vesselId && AvailableLotQuantity(vessel) > 0))
            .Sum(AvailableLotQuantity);

    // Moving an indivisible vessel must leave the source's own needed liquid
    // batches intact. A different vessel's reserved contents are not spare stock.
    private bool KeepsWorkstationLiquidReserve(InventoryLot jug)
    {
        if (jug.StorageBuildingId is not { } sourceId) return true;
        var source = worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == sourceId);
        if (source?.HouseholdId != jug.OwnerId) return false;
        var needs = NeededWorkstationLiquids(jug.OwnerId).Where(need => need.Building.InstanceId == sourceId).ToArray();
        return society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId == jug.Id).GroupBy(lot => lot.ItemKind)
            .All(contents => AvailableWorkstationLiquid(jug.OwnerId, sourceId, contents.Key) - contents.Sum(AvailableLotQuantity) >=
                needs.Where(need => need.Liquid == contents.Key).Select(need => need.Quantity).DefaultIfEmpty(0).Max());
    }

    /// <summary>Only filled, unreserved whole jugs are moved; water contents never leave their vessel.</summary>
    private InventoryLot? WaterJugForWorkstation(string actor, PlacedBuilding building, string liquid = "water")
    {
        var householdId = HouseholdFor(actor);
        var inventory = society.Checkpoint.Inventory;
        var incoming = inventory.Lots.Where(lot => lot.DeliveryBuildingId == building.InstanceId).Sum(lot => lot.Quantity);
        return inventory.Lots.Where(jug => jug.ItemKind == "water_jug" && jug.ContainerLotId is null &&
                jug.StorageBuildingId != building.InstanceId && jug.GroundPosition is null && jug.CartId is null &&
                jug.AnimalId is null && AvailableLotQuantity(jug) == 1 && KeepsWorkstationLiquidReserve(jug) &&
                (jug.OwnerId == actor && jug.StorageBuildingId is null && jug.DeliveryBuildingId is null ||
                 jug.OwnerId == householdId && CanReachSharedItem(actor, jug)) &&
                inventory.Lots.Any(water => water.ContainerLotId == jug.Id && water.ItemKind == liquid && AvailableLotQuantity(water) > 0) &&
                !inventory.Reservations.Any(reservation => (reservation.State is InventoryReservationState.Reserved or
                    InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed) &&
                    (reservation.LotId == jug.Id || inventory.Lots.Any(contents => contents.Id == reservation.LotId && contents.ContainerLotId == jug.Id))) &&
                InventoryFixture.TransferLoadQuantity(inventory, jug.Id, 1) <= Math.Max(0, StorageRoom(building.InstanceId) - incoming) &&
                (jug.OwnerId == actor || InventoryFixture.TransferLoadQuantity(inventory, jug.Id, 1) <= CarryingRoom(actor)))
            .OrderBy(jug => jug.OwnerId == actor ? 0 : 1).ThenBy(jug => jug.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Shared cooking/care hook: physically collect and deliver one whole owned filled jug.</summary>
    private void SupplyWorkstationWater(string actor, PlaytestInhabitantState person, PlacedBuilding building, string liquid = "water")
    {
        if (!AdultResident(actor) || building.HouseholdId != HouseholdFor(actor) || WaterJugForWorkstation(actor, building, liquid) is not { } jug)
            return;
        if (jug.OwnerId == actor)
        {
            if (person.Position != building.Position)
            {
                MoveToward(actor, person, building.Position, "workstation_water", 0);
                return;
            }
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"workstation-water:{WorldTick}:{actor}", actor, building.HouseholdId!, jug.Id, 1,
                "workstation_water_delivered", building.InstanceId));
            AppendEvent("workstation_water_delivered", $"{actor}:{jug.Id}:{building.InstanceId}");
            return;
        }
        var position = HouseholdStockPosition(jug);
        var range = HouseholdStockInteractionRange(jug);
        if (!IsWithinInteractionRange(person.Position, position, range))
        {
            MoveToward(actor, person, position, "workstation_water", range);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"workstation-water-pickup:{WorldTick}:{actor}", jug.OwnerId, actor, jug.Id, 1,
            "workstation_water_picked_up", destinationDeliveryBuildingId: building.InstanceId));
        AppendEvent("workstation_water_picked_up", $"{actor}:{jug.Id}:{building.InstanceId}");
    }
}
