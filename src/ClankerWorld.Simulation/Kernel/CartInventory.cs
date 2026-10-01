namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    public static InventoryCheckpoint PutDownCartCargo(InventoryCheckpoint checkpoint, string lotId,
        string ownerId, string cartId, InventoryGroundPosition position)
    {
        ValidateCheckpoint(checkpoint);
        var root = checkpoint.GetLot(lotId);
        EnsureOwnerAndExactQuantity(root, ownerId, root.Quantity);
        if (root.CartId != cartId || root.ContainerLotId is not null)
            throw new InvalidOperationException("Put down actual cargo or its whole vessel from this cart.");
        foreach (var item in checkpoint.Lots.Where(item => item.Id == lotId || item.ContainerLotId == lotId))
            EnsureUnreservedQuantity(checkpoint, item, item.Quantity);
        return Commit(checkpoint, lots: checkpoint.Lots.Select(item => item.Id == lotId || item.ContainerLotId == lotId
            ? item with { CartId = null, GroundPosition = position } : item).ToArray(),
            eventKind: "cart_cargo_put_down", detail: $"{cartId}:{lotId}:{position.X},{position.Y}");
    }

    public static InventoryCheckpoint RestoreCartCondition(InventoryCheckpoint checkpoint, string lotId, string ownerId)
    {
        ValidateCheckpoint(checkpoint);
        var lot = checkpoint.GetLot(lotId);
        EnsureOwnerAndExactQuantity(lot, ownerId, 1);
        EnsureUnreservedQuantity(checkpoint, lot, 1);
        if (lot.ItemKind != "handcart" || lot.Quantity != 1 || lot.FreshnessBasisPoints == 0)
            throw new InvalidOperationException("Repair one actual handcart without replacing its cargo.");
        return Commit(checkpoint, lots: checkpoint.Lots.Select(item => item.Id == lotId
            ? item with { ConditionBasisPoints = 10_000 } : item).ToArray(), eventKind: "cart_repaired", detail: lotId);
    }

    public static InventoryCheckpoint LoadCart(InventoryCheckpoint checkpoint, string operationId,
        string ownerId, string lotId, int quantity, string cartId, InventoryGroundPosition position)
    {
        ValidateCheckpoint(checkpoint);
        var source = checkpoint.GetLot(lotId);
        EnsureOwnerAndAvailableQuantity(checkpoint, source, ownerId, quantity);
        EnsurePortableTransfer(checkpoint, source);
        if (source.CartId is not null || source.ContainerLotId is not null || source.ItemKind == "handcart")
            throw new InvalidOperationException("Load a loose item or a whole vessel; carts cannot carry other carts.");
        var id = source.Id;
        if (quantity < source.Quantity)
        {
            id += "#cart:" + operationId;
            checkpoint = SplitLot(checkpoint, source.Id, quantity, id);
        }
        var lots = checkpoint.Lots.Select(lot => lot.Id == id || lot.ContainerLotId == id
            ? lot with { CartId = cartId, GroundPosition = position, StorageBuildingId = null, DeliveryBuildingId = null }
            : lot).ToArray();
        return Commit(checkpoint, lots: lots, eventKind: "cart_loaded", detail: $"{operationId}:{cartId}:{id}:{quantity}");
    }

    public static InventoryCheckpoint UnloadCart(InventoryCheckpoint checkpoint, string operationId,
        string sender, string recipient, string lotId, int quantity, string cartId, string? storageBuildingId = null)
    {
        ValidateCheckpoint(checkpoint);
        var source = checkpoint.GetLot(lotId);
        EnsureOwnerAndAvailableQuantity(checkpoint, source, sender, quantity);
        if (source.CartId != cartId || source.ContainerLotId is not null)
            throw new InvalidOperationException("Unload the actual cargo item or its whole vessel from this cart.");
        foreach (var child in checkpoint.Lots.Where(item => item.ContainerLotId == lotId))
            EnsureUnreservedQuantity(checkpoint, child, child.Quantity);
        var id = source.Id;
        if (quantity < source.Quantity)
        {
            id += "#unload:" + operationId;
            checkpoint = SplitLot(checkpoint, source.Id, quantity, id);
        }
        checkpoint = checkpoint with
        {
            Lots = checkpoint.Lots.Select(lot => lot.Id == id || lot.ContainerLotId == id
                ? lot with { CartId = null, GroundPosition = null } : lot).ToArray(),
        };
        return Transfer(checkpoint, operationId, sender, recipient, id, quantity, "cart_unloaded", storageBuildingId);
    }
}
