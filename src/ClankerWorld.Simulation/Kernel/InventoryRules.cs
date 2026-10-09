namespace ClankerWorld.Simulation.Kernel;

/// <summary>Predicates shared by inventory authority and callers choosing physical goods.</summary>
public static class InventoryRules
{
    public static bool IsActive(InventoryReservationState state) => state is InventoryReservationState.Reserved or
        InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed;

    // This raw quantity preserves existing helpers that deliberately do not clamp.
    public static int UnreservedQuantity(InventoryIndex index, InventoryLot lot) =>
        lot.Quantity - index.ReservedQuantity(lot.Id);

    public static int UsableQuantity(InventoryIndex index, InventoryLot lot) =>
        lot.ConditionBasisPoints <= 0 || lot.FreshnessBasisPoints <= 0 ? 0 : Math.Max(0, UnreservedQuantity(index, lot));

    public static bool IsUsableContainer(InventoryLot container) =>
        InventoryContainerRules.IsContainer(container.ItemKind) && container.ConditionBasisPoints > 0;

    public static bool HasUsableContainer(InventoryIndex index, InventoryLot lot) =>
        lot.ContainerLotId is not { } id || index.Find(id) is { } container && IsUsableContainer(container);

    public static bool HasOwnedPhysicalQuantity(InventoryLot lot, string ownerId, int quantity) =>
        lot.OwnerId == ownerId && quantity > 0 && lot.Quantity >= quantity;

    public static bool HasOwnedUsableQuantity(InventoryLot lot, string ownerId, int quantity) =>
        HasOwnedPhysicalQuantity(lot, ownerId, quantity) && lot.ConditionBasisPoints != 0 && lot.FreshnessBasisPoints != 0;

    public static bool HasUnreservedQuantity(InventoryIndex index, InventoryLot lot, int quantity) =>
        index.ReservedQuantity(lot.Id) <= lot.Quantity - quantity;

    public static int ReservableQuantity(InventoryIndex index, InventoryLot lot)
    {
        var quantity = UsableQuantity(index, lot);
        return CanReserve(index, lot, lot.OwnerId, quantity) ? quantity : 0;
    }

    public static bool CanReserve(InventoryIndex index, InventoryLot lot, string ownerId, int quantity) =>
        !InventoryContainerRules.IsContainer(lot.ItemKind) && HasUsableContainer(index, lot) &&
        HasOwnedUsableQuantity(lot, ownerId, quantity) && HasUnreservedQuantity(index, lot, quantity);

    public static bool AreUnreserved(InventoryIndex index, IEnumerable<string> lotIds) =>
        !lotIds.Any(index.HasActiveReservation);

    // Damage does not prevent moving physical property; an active family reservation does.
    public static bool CanMoveFamily(InventoryIndex index, InventoryLot root) =>
        root.ContainerLotId is null && !index.HasReservedFamily(root.Id);

    public static bool SharesContainerOwnerAndLocation(InventoryLot container, InventoryLot contents, string ownerId) =>
        InventoryContainerRules.IsContainer(container.ItemKind) && container.Quantity == 1 &&
        container.ContainerLotId is null && container.OwnerId == ownerId && contents.OwnerId == ownerId &&
        container.StorageBuildingId == contents.StorageBuildingId && container.DeliveryBuildingId == contents.DeliveryBuildingId &&
        container.CarrierId == contents.CarrierId && (contents.ContainerLotId == container.Id
            ? contents.GroundPosition is null : container.GroundPosition == contents.GroundPosition);

    public static bool CanTakeOut(InventoryIndex index, InventoryLot contents, string ownerId, int quantity) =>
        contents.ContainerLotId is { } id && index.Find(id) is { } container && IsUsableContainer(container) &&
        HasOwnedPhysicalQuantity(contents, ownerId, quantity) && HasUnreservedQuantity(index, contents, quantity) &&
        SharesContainerOwnerAndLocation(container, contents, ownerId) && CanMoveFamily(index, container);
}
