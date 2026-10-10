using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record PersonalStorageEffect(string MoveId, string ItemKind, int Quantity);

    private IEnumerable<InventoryLot> PersonalStorageLots(string actor, string houseId) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
            // Keep worn equipment and tools under repair with their wearer.
            !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id) &&
            !IsEdibleFood(lot.ItemKind) && PhysicalUnreservedQuantity(lot) > 0 &&
            VesselFits(lot, StorageRoomAfterInboundDeliveries(houseId))).OrderBy(lot => lot.Id, StringComparer.Ordinal);

    private PersonalStorageEffect? StorePersonalGoods(string actor, string lotId, int maximumQuantity = int.MaxValue,
        string? requiredHouseId = null, string? requiredHouseholdId = null, GridPoint? requiredPosition = null)
    {
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor) || maximumQuantity <= 0 ||
            HouseForHousehold(HouseholdFor(actor)) is not { } house || StorageRoomAfterInboundDeliveries(house.InstanceId) <= 0)
            return null;
        if (requiredHouseId is not null && (house.InstanceId != requiredHouseId ||
            house.HouseholdId != requiredHouseholdId || house.Position != requiredPosition)) return null;
        var lot = PersonalStorageLots(actor, house.InstanceId).FirstOrDefault(item => item.Id == lotId);
        if (lot is null) return null;
        if (requiredHouseId is not null) InterruptOrdinaryFieldWorkForCustody(actor, lotId);
        var person = inhabitants[actor];
        if (!IsWithinInteractionRange(person.Position, house.Position, 1))
        {
            MoveToward(actor, person, house.Position, "personal_goods", 1);
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
            moveId, lot.Id, actor, quantity, storageBuildingId: house.InstanceId));
        AppendEvent("personal_goods_stored", $"{actor}|{lot.ItemKind}|{quantity}|{actor}");
        return new(moveId, lot.ItemKind, quantity);
    }
}
