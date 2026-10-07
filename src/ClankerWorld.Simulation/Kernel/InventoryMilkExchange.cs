namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    /// <summary>Empties only unreserved spoiled milk, keeping its reusable jug and any fresh contents.</summary>
    public static InventoryCheckpoint EmptySpoiledMilk(InventoryCheckpoint checkpoint, string ownerId, string jugId)
    {
        ValidateCheckpoint(checkpoint);
        var jug = checkpoint.GetLot(jugId);
        EnsureOwnerAndAvailablePhysicalQuantity(checkpoint, jug, ownerId, 1);
        if (jug.ItemKind != InventoryContainerRules.WaterJug)
            throw new InvalidOperationException("Spoiled milk can only be emptied from its actual jug.");
        var spoiled = checkpoint.Lots.Where(lot => lot.ContainerLotId == jugId && lot.ItemKind == "milk" && lot.FreshnessBasisPoints == 0).ToArray();
        if (spoiled.Length == 0) throw new InvalidOperationException("The jug has no spoiled milk.");
        foreach (var lot in spoiled) EnsureOwnerAndAvailablePhysicalQuantity(checkpoint, lot, ownerId, lot.Quantity);
        return Commit(checkpoint, lots: checkpoint.Lots.Where(lot => !spoiled.Any(item => item.Id == lot.Id)).ToArray(),
            eventKind: "spoiled_milk_emptied", detail: jugId);
    }

    /// <summary>Exchanges one held portion into a receiving jug; both vessels retain their owners.</summary>
    public static InventoryCheckpoint ExchangeMilk(InventoryCheckpoint checkpoint, string operationId,
        string milkReservationId, string receivingJugId, string buyerId, string paymentId, InventoryGroundPosition position)
    {
        ValidateCheckpoint(checkpoint);
        var held = checkpoint.GetReservation(milkReservationId);
        var milk = checkpoint.GetLot(held.LotId);
        var jug = checkpoint.GetLot(receivingJugId);
        var payment = checkpoint.GetLot(paymentId);
        var contents = checkpoint.Lots.Where(lot => lot.ContainerLotId == jug.Id).ToArray();
        if (held.State != InventoryReservationState.Reserved || held.Quantity != 1 || !held.IsExclusive ||
            !held.Purpose.StartsWith("milk-sale:", StringComparison.Ordinal) || milk.ItemKind != "milk" ||
            milk.ContainerLotId is null || milk.FreshnessBasisPoints == 0 || jug.ItemKind != InventoryContainerRules.WaterJug ||
            jug.ConditionBasisPoints == 0 || jug.StorageBuildingId is not null || jug.GroundPosition is not null ||
            !(jug.CarrierId == buyerId || jug.CarrierId is null && jug.OwnerId == buyerId) ||
            contents.Any(lot => lot.ItemKind != "milk") || contents.Sum(lot => lot.Quantity) >= InventoryContainerRules.WaterJugCapacity ||
            payment.OwnerId != buyerId || IsContainerRelated(payment) || payment.StorageBuildingId is not null ||
            payment.GroundPosition is not null || payment.DeliveryBuildingId is not null ||
            !(payment.CarrierId == buyerId || payment.CarrierId is null))
            throw new InvalidOperationException("A milk exchange needs the exact held milk, carried receiving jug and personal payment.");
        EnsureOwnerAndAvailableQuantity(checkpoint, payment, buyerId, 1);
        var next = ReleaseReservation(checkpoint, held.Id, "milk_sale_completed");
        next = Transfer(next, operationId + "-payment", buyerId, milk.OwnerId, payment.Id, 1, "milk_sale_payment",
            destinationGroundPosition: position);
        var moved = milk with
        {
            Id = milk.Id + "#milk:" + operationId,
            Quantity = 1,
            OwnerId = jug.OwnerId,
            ContainerLotId = jug.Id,
            StorageBuildingId = null,
            DeliveryBuildingId = null,
            GroundPosition = null,
            CarrierId = jug.CarrierId,
            ProvenanceLotId = milk.Id
        };
        if (next.Lots.Any(lot => lot.Id == moved.Id)) throw new InvalidOperationException("Duplicate milk exchange identity.");
        var lots = next.Lots.Where(lot => lot.Id != milk.Id).Concat(milk.Quantity > 1 ? [milk with { Quantity = milk.Quantity - 1 }] : [])
            .Append(moved).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        return Commit(next, lots: lots, eventKind: "milk_exchanged", detail: operationId);
    }
}
