using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void CollectFieldHarvest(string actor, PlaytestInhabitantState state)
    {
        var household = HouseholdFor(actor);
        if (FarmhouseForHousehold(household) is not { } farmhouse) return;
        var stock = FieldHarvestStock(actor, state, household, farmhouse);
        if (stock?.GroundPosition is not { } ground) return;
        var point = new GridPoint(ground.X, ground.Y);
        if (state.Position != point) { MoveToward(actor, state, point, "field_harvest", 0); return; }
        var store = FieldHarvestStorage(household, stock, farmhouse);
        var quantity = Math.Min(FarmhouseGrainDeliveryRoom(store.InstanceId),
            Math.Min(CarryingRoom(actor), Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(stock))));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"field-pickup:{WorldTick}:{actor}",
            household, actor, stock.Id, quantity,
            "field_harvest_collected", destinationDeliveryBuildingId: store.InstanceId));
        AppendEvent("field_harvest_collected", $"{actor}:{stock.Id}:{store.InstanceId}");
    }

    private PlacedBuilding FieldHarvestStorage(string household, InventoryLot stock, PlacedBuilding farmhouse) =>
        stock.ItemKind is "grain" or "grain_seed" or FarmFieldRules.GreensSeed ||
        stock.ItemKind == "potatoes" && society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "potatoes").Sum(AvailableLotQuantity) <= 1
            ? HouseholdBuildingWithTag(household, "silo") ?? farmhouse : HouseForHousehold(household) ?? farmhouse;

    private bool CanCollectFieldHarvest(string actor, PlaytestInhabitantState state, InventoryLot stock, PlacedBuilding store)
    {
        if (stock.GroundPosition is not { } ground || CarryingRoom(actor) == 0 || FarmhouseGrainDeliveryRoom(store.InstanceId) == 0)
            return false;
        var point = new GridPoint(ground.X, ground.Y);
        return (state.Position == point || FindUnoccupiedRoute(actor, state.Position, point, 0).Count > 0) &&
            (point == store.Position || FindUnoccupiedRoute(actor, point, store.Position, 0).Count > 0);
    }

    private InventoryLot? FieldHarvestStock(string actor, PlaytestInhabitantState state, string household, PlacedBuilding farmhouse) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.GroundPosition is { } ground && FarmFields.Any(field => field.HouseholdId == household &&
                field.Position == new GridPoint(ground.X, ground.Y)) && lot.ContainerLotId is null && lot.CartId is null && lot.AnimalId is null &&
            (lot.ItemKind == "grain" || FoodItems.IsEdible(lot.ItemKind) || FoodItems.IsPlantingStock(lot.ItemKind)) &&
            AvailableLotQuantity(lot) > 0 && CanCollectFieldHarvest(actor, state, lot,
                FieldHarvestStorage(household, lot, farmhouse)))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
}
