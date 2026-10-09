using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record BorrowedReturnEffect(string MoveId, string ItemKind, int Quantity);

    private IEnumerable<InventoryLot> ReturnableBorrowedGoods(string actor) => CarriedBorrowedGoods(actor).Where(lot =>
        lot.DeliveryBuildingId is null && !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id) &&
        PhysicalUnreservedQuantity(lot) > 0 &&
        (!InventoryContainerRules.IsContainer(lot.ItemKind) || !HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id)));

    private BorrowedReturnEffect? ReturnBorrowedGoods(string actor, string lotId, int maximumQuantity = int.MaxValue,
        string? requiredHouseId = null, string? requiredHouseholdId = null, GridPoint? requiredPosition = null)
    {
        if (maximumQuantity <= 0 || requiredHouseId is not null &&
            (!AdultResident(actor) || !ReadyForBriefInteraction(actor))) return null;
        // Ordinary departure returns retain their original selection rules;
        // explicit orders additionally protect selected gear and deliveries.
        var goods = requiredHouseId is null ? BorrowedGoods(actor) : ReturnableBorrowedGoods(actor);
        var lot = goods.FirstOrDefault(item => item.Id == lotId);
        if (lot is null || HouseForHousehold(lot.OwnerId) is not { } house ||
            requiredHouseId is not null && (house.InstanceId != requiredHouseId ||
                house.HouseholdId != requiredHouseholdId || house.Position != requiredPosition)) return null;
        if (requiredHouseId is not null) InterruptOrdinaryFieldWorkForCustody(actor, lotId);
        // Returning at the entrance grants no access to private household stock.
        if (!IsWithinInteractionRange(inhabitants[actor].Position, house.Position, 1))
        {
            MoveToward(actor, inhabitants[actor], house.Position, "personal_goods", 1);
            return null;
        }
        var room = StorageRoomAfterInboundDeliveries(house.InstanceId);
        var quantity = InventoryContainerRules.IsContainer(lot.ItemKind)
            ? VesselFits(lot, room) ? 1 : 0
            : Math.Min(PhysicalUnreservedQuantity(lot), room);
        quantity = Math.Min(quantity, maximumQuantity);
        if (quantity <= 0) return null;
        var moveId = $"personal:{actor}:{WorldTick}:{lot.Id}";
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
            moveId, lot.Id, lot.OwnerId, quantity, storageBuildingId: house.InstanceId));
        AppendEvent("borrowed_goods_returned", $"{actor}|{lot.ItemKind}|{quantity}|{lot.OwnerId}");
        return new(moveId, lot.ItemKind, quantity);
    }
}
