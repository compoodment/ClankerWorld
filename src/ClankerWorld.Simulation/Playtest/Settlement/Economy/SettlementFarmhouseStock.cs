using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private PlacedBuilding? FarmhouseForHousehold(string householdId) =>
        HouseholdBuildingWithTag(householdId, "farmhouse");

    private bool IsFarmStorage(PlacedBuilding building) => worldContent.Buildings.Single(definition =>
        definition.CanonicalId == building.DefinitionId).Tags.Any(tag => tag is "farmhouse" or "silo");

    private int FarmStorageFree(string buildingId, bool includeDeliveries = true) =>
        includeDeliveries ? FarmhouseGrainDeliveryRoom(buildingId) : StorageRoom(buildingId);

    private PlacedBuilding? FarmStorageFor(string householdId, string kind)
    {
        var farmhouse = FarmhouseForHousehold(householdId);
        var silo = HouseholdBuildingWithTag(householdId, "silo");
        var choices = kind == FarmFieldRules.Grain ? new[] { farmhouse, silo } : new[] { silo, farmhouse };
        return choices.FirstOrDefault(building => building is not null && FarmStorageFree(building.InstanceId) > 0);
    }

    private bool IsOwnedFieldHarvest(InventoryLot lot, string householdId) => lot.GroundPosition is { } ground &&
        fields.Any(field => field.HouseholdId == householdId && field.Position.X == ground.X && field.Position.Y == ground.Y);

    private PlacedBuilding? FarmDeliveryStorage(string householdId, InventoryLot lot) =>
        lot.ItemKind == FarmFieldRules.Grain && lot.StorageBuildingId is not null
            ? FarmhouseForHousehold(householdId) is { } farmhouse && FarmStorageFree(farmhouse.InstanceId) > 0 ? farmhouse : null
            : FarmStorageFor(householdId, lot.ItemKind);

    private InventoryLot? FarmGrainForDelivery(string householdId, string farmhouseId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && FarmFieldRules.IsFarmStock(lot.ItemKind) &&
                lot.ContainerLotId is null && lot.CartId is null && lot.AnimalId is null && lot.DeliveryBuildingId is null &&
                (lot.ItemKind == "grain" ? lot.StorageBuildingId != farmhouseId : lot.StorageBuildingId is null) &&
                !(lot.ItemKind == "grain" && IsOwnedFieldHarvest(lot, householdId) && HouseholdBuildingWithTag(householdId, "silo") is not null) &&
                AvailableLotQuantity(lot) > 0 && FarmDeliveryStorage(householdId, lot) is { } destination &&
                destination.InstanceId != lot.StorageBuildingId && FarmGrainDeliveryQuantity(lot) > 0)
            .OrderBy(lot => lot.GroundPosition is not null ? 0 : 1)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private int FarmGrainDeliveryQuantity(InventoryLot lot)
    {
        var available = AvailableLotQuantity(lot);
        if (lot.ItemKind != FarmFieldRules.Grain || lot.StorageBuildingId is not { } sourceId) return available;
        var source = worldSimulation.Buildings.Single(building => building.InstanceId == sourceId);
        // Keep the same two-batch cooking supply that ordinary delivery seeks.
        // Reserving only one batch would shuttle the second unit back and forth
        // while the House waits for its other real ingredients.
        var target = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == source.DefinitionId &&
                NeedsRecipeOutput(recipe, lot.OwnerId)).SelectMany(recipe => recipe.Inputs)
            .Where(input => input.ResourceId == lot.ItemKind).Select(input => input.Amount * SupplyBatches).DefaultIfEmpty(0).Max();
        var stocked = society.Checkpoint.Inventory.Lots.Where(stock => stock.OwnerId == lot.OwnerId &&
            stock.StorageBuildingId == sourceId && stock.ItemKind == lot.ItemKind).Sum(AvailableLotQuantity);
        return Math.Min(available, Math.Max(0, stocked - target));
    }

    private int FarmhouseGrainDeliveryRoom(string farmhouseId) => Math.Max(0,
        StorageRoom(farmhouseId) - society.Checkpoint.Inventory.Lots
            .Where(lot => lot.DeliveryBuildingId == farmhouseId).Sum(lot => lot.Quantity));

    private void AddFarmGrainCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarryingRoom(actor) == 0 || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } mill ||
            FarmGrainForDelivery(householdId, mill.InstanceId) is not { } grain ||
            FarmDeliveryStorage(householdId, grain) is not { } farmhouse)
            return;
        var source = HouseholdStockPosition(grain);
        var range = HouseholdStockInteractionRange(grain);
        if ((!IsWithinInteractionRange(state.Position, source, range) &&
             FindUnoccupiedRoute(actor, state.Position, source, range).Count == 0) ||
            FindUnoccupiedRoute(actor, source, farmhouse.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("haul_farm_grain",
            "Carry household crops and seeds from their actual location to farm storage.", 24, farmhouse.InstanceId));
    }

    private void HaulFarmGrain(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarryingRoom(actor) == 0 || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } mill ||
            FarmGrainForDelivery(householdId, mill.InstanceId) is not { } grain ||
            FarmDeliveryStorage(householdId, grain) is not { } farmhouse)
            return;
        var source = HouseholdStockPosition(grain);
        var range = HouseholdStockInteractionRange(grain);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "farm_grain", range);
            return;
        }
        var quantity = Math.Min(FarmhouseGrainDeliveryRoom(farmhouse.InstanceId),
            Math.Min(CarryingRoom(actor), Math.Min(HouseHaulLoadQuantity, FarmGrainDeliveryQuantity(grain))));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"farm-grain-pickup:{WorldTick}:{actor}", householdId, actor, grain.Id,
            quantity, "farm_grain_picked_up", destinationDeliveryBuildingId: farmhouse.InstanceId));
        AppendEvent("farm_grain_picked_up", $"{actor}:{grain.Id}:{quantity}:{farmhouse.InstanceId}");
    }

    private InventoryLot? FarmFlourForHouse(string householdId, string farmhouseId) =>
        society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == householdId && lot.ItemKind == "flour" &&
                lot.StorageBuildingId == farmhouseId && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddFarmFlourCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } farmhouse ||
            HouseForHousehold(householdId) is not { } house || StorageRoom(house.InstanceId) == 0 ||
            FarmFlourForHouse(householdId, farmhouse.InstanceId) is null)
            return;
        if ((!IsWithinInteractionRange(state.Position, farmhouse.Position, 0) &&
             FindUnoccupiedRoute(actor, state.Position, farmhouse.Position, 0).Count == 0) ||
            FindUnoccupiedRoute(actor, farmhouse.Position, house.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("haul_farm_flour",
            "Carry household flour from its Farmhouse to its House.", 25, house.InstanceId));
    }

    private void HaulFarmFlour(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } farmhouse ||
            HouseForHousehold(householdId) is not { } house || StorageRoom(house.InstanceId) == 0 ||
            FarmFlourForHouse(householdId, farmhouse.InstanceId) is not { } flour)
            return;
        if (state.Position != farmhouse.Position)
        {
            MoveToward(actor, state, farmhouse.Position, "farm_flour", 0);
            return;
        }
        var inbound = society.Checkpoint.Inventory.Lots.Where(lot => lot.DeliveryBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        var quantity = Math.Min(CarryingRoom(actor), Math.Min(Math.Max(0, StorageRoom(house.InstanceId) - inbound), Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(flour))));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"farm-flour-pickup:{WorldTick}:{actor}", householdId, actor, flour.Id,
            quantity, "farm_flour_picked_up", destinationDeliveryBuildingId: house.InstanceId));
        AppendEvent("farm_flour_picked_up", $"{actor}:{flour.Id}:{quantity}:{house.InstanceId}");
    }
}
