namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    /// <summary>The runtime checks the owner's physical proximity before loading carried or co-located goods.</summary>
    public static InventoryCheckpoint LoadHandcart(InventoryCheckpoint checkpoint, string operationId,
        string ownerId, string cartId, string cargoId, int quantity)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var cart = RequireHandcart(checkpoint, ownerId, cartId);
        var cargo = checkpoint.GetLot(cargoId);
        if (cargo.OwnerId != ownerId || cargo.ContainerLotId is not null ||
            cargo.StorageBuildingId is not null || cargo.DeliveryBuildingId is not null ||
            cargo.GroundPosition is { } ground && ground != cart.GroundPosition)
            throw new InvalidOperationException("Cart cargo must be carried by its owner or beside the cart.");
        EnsureUsableContainer(cart);
        EnsureOwnerAndAvailableQuantity(checkpoint, cargo, ownerId, quantity);
        if (!InventoryContainerRules.Allows(cart.ItemKind, cargo.ItemKind))
            throw new InvalidOperationException("The cart carries loose goods; vessels must be carried separately.");
        EnsureNoActiveReservations(checkpoint, checkpoint.Lots
            .Where(lot => lot.Id == cart.Id || lot.ContainerLotId == cart.Id).Select(lot => lot.Id));
        var filled = checkpoint.Lots.Where(lot => lot.ContainerLotId == cart.Id).Sum(lot => lot.Quantity);
        if (quantity > InventoryContainerRules.HandcartCapacity - filled)
            throw new InvalidOperationException("The cart does not have room for those goods.");
        var loadedId = quantity == cargo.Quantity ? cargo.Id : $"{cargo.Id}#contained:{cart.Id}:{operationId}";
        if (loadedId != cargo.Id && checkpoint.Lots.Any(lot => lot.Id == loadedId))
            throw new InvalidOperationException("This load ID is already in use.");
        var loaded = cargo with
        {
            Id = loadedId,
            Quantity = quantity,
            ContainerLotId = cart.Id,
            GroundPosition = null,
            ProvenanceLotId = loadedId == cargo.Id ? cargo.ProvenanceLotId : cargo.Id,
        };
        var lots = checkpoint.Lots.Where(lot => lot.Id != cargo.Id).ToList();
        if (quantity < cargo.Quantity) lots.Add(cargo with { Quantity = cargo.Quantity - quantity });
        lots.Add(loaded);
        return Commit(checkpoint, lots: lots.OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
            eventKind: "handcart_loaded", detail: $"{cart.Id}:{cargo.Id}:{quantity}");
    }

    /// <summary>Unload even a broken cart or spoiled load. Physical goods are never repaired or discarded by unloading.</summary>
    public static InventoryCheckpoint UnloadHandcart(InventoryCheckpoint checkpoint, string operationId,
        string ownerId, string cartId, string cargoId, int quantity, bool ontoGround = false)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var cart = RequireHandcart(checkpoint, ownerId, cartId);
        var cargo = checkpoint.GetLot(cargoId);
        if (cargo.ContainerLotId != cart.Id)
            throw new InvalidOperationException("The goods must be inside this cart.");
        EnsureOwnerAndAvailablePhysicalQuantity(checkpoint, cargo, ownerId, quantity);
        EnsureNoActiveReservations(checkpoint, checkpoint.Lots
            .Where(lot => lot.Id == cart.Id || lot.ContainerLotId == cart.Id).Select(lot => lot.Id));
        var splitId = quantity == cargo.Quantity ? cargo.Id : $"{cargo.Id}#unloaded:{operationId}";
        if (splitId != cargo.Id && checkpoint.Lots.Any(lot => lot.Id == splitId))
            throw new InvalidOperationException("This unload ID is already in use.");
        var unloaded = cargo with
        {
            Id = splitId,
            Quantity = quantity,
            ContainerLotId = null,
            GroundPosition = ontoGround ? cart.GroundPosition : null,
            ProvenanceLotId = splitId == cargo.Id ? cargo.ProvenanceLotId : cargo.Id,
        };
        var lots = checkpoint.Lots.Where(lot => lot.Id != cargo.Id).ToList();
        if (quantity < cargo.Quantity) lots.Add(cargo with { Quantity = cargo.Quantity - quantity });
        lots.Add(unloaded);
        return Commit(checkpoint, lots: lots.OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
            eventKind: "handcart_unloaded", detail: $"{cart.Id}:{cargo.Id}:{quantity}");
    }

    /// <summary>Move a cart family after the runtime has checked the attached puller's legal next step.</summary>
    public static InventoryCheckpoint MoveHandcart(InventoryCheckpoint checkpoint, string ownerId,
        string cartId, InventoryGroundPosition next, int wearLossBasisPoints)
    {
        ValidateCheckpoint(checkpoint);
        var cart = RequireHandcart(checkpoint, ownerId, cartId);
        if (cart.ConditionBasisPoints <= 0 || next.X < 0 || next.Y < 0)
            throw new InvalidOperationException("A broken cart cannot be pulled.");
        EnsureNoActiveReservations(checkpoint, checkpoint.Lots
            .Where(lot => lot.Id == cart.Id || lot.ContainerLotId == cart.Id).Select(lot => lot.Id));
        var moved = Commit(checkpoint, lots: checkpoint.Lots.Select(lot => lot.Id == cartId
                ? lot with { GroundPosition = next } : lot).ToArray(),
            eventKind: "handcart_moved", detail: $"{cartId}:{next.X},{next.Y}");
        return WearSingleUnit(moved, cartId, wearLossBasisPoints);
    }

    private static InventoryLot RequireHandcart(InventoryCheckpoint checkpoint, string ownerId, string cartId)
    {
        var cart = checkpoint.GetLot(cartId);
        if (cart.ItemKind != InventoryContainerRules.Handcart || cart.OwnerId != ownerId || cart.GroundPosition is null)
            throw new InvalidOperationException("The cart must belong to its user and have a physical position.");
        return cart;
    }
}
