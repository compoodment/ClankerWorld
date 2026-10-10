using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record PersonalCollectionEffect(string MoveId, string ItemKind, int Quantity);

    private bool CanReachPersonalGoods(string actor, InventoryLot lot)
    {
        var position = inhabitants[actor].Position;
        var destination = HouseholdStockPosition(lot);
        var range = lot.GroundPosition is not null ? ResourceInteractionRange : 1;
        return IsWithinInteractionRange(position, destination, range) ||
            FindUnoccupiedRoute(actor, position, destination, range).Count > 0;
    }

    private PersonalCollectionEffect? CollectPersonalGoods(string actor, string lotId, int maximumQuantity = int.MaxValue)
    {
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor) || maximumQuantity <= 0 || FreeCarryCapacity(actor) <= 0)
            return null;
        var lot = PersonalGoodsAwaitingCollection(actor).FirstOrDefault(item => item.Id == lotId);
        if (lot is null || !CanReachPersonalGoods(actor, lot)) return null;
        var destination = HouseholdStockPosition(lot);
        var range = lot.GroundPosition is not null ? ResourceInteractionRange : 1;
        // Owners collect at the entrance, including former members and heirs; this grants no other private access.
        if (!IsWithinInteractionRange(inhabitants[actor].Position, destination, range))
        {
            MoveToward(actor, inhabitants[actor], destination, "personal_goods", range);
            return null;
        }
        var room = FreeCarryCapacity(actor);
        var quantity = InventoryContainerRules.IsContainer(lot.ItemKind)
            ? VesselFits(lot, room) ? 1 : 0
            : Math.Min(PhysicalUnreservedQuantity(lot), room);
        quantity = Math.Min(quantity, maximumQuantity);
        if (quantity <= 0) return null;
        var moveId = $"personal:{actor}:{WorldTick}:{lot.Id}";
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
            moveId, lot.Id, lot.OwnerId, quantity, actor));
        AppendEvent("personal_goods_collected", $"{actor}|{lot.ItemKind}|{quantity}|{lot.OwnerId}");
        return new(moveId, lot.ItemKind, quantity);
    }
}
