namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    /// <summary>Commit a vessel, its exact contents and other exchange goods from one unchanged snapshot.</summary>
    public static InventoryCheckpoint ReserveTogether(InventoryCheckpoint checkpoint,
        IReadOnlyList<InventoryReservation> reservations)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentNullException.ThrowIfNull(reservations);
        if (reservations.Count == 0 || reservations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != reservations.Count ||
            reservations.Select(item => item.ExpiryTick).Distinct().Count() != 1 ||
            reservations.Select(item => item.Purpose).Distinct(StringComparer.Ordinal).Count() != 1 ||
            reservations.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Purpose) ||
                item.State != InventoryReservationState.Reserved || item.Quantity <= 0 || item.ExpiryTick < checkpoint.WorldTick ||
                checkpoint.Reservations.Any(existing => existing.Id == item.Id)))
            throw new InvalidOperationException("A reservation group needs unique live identities and positive exact quantities.");
        foreach (var group in reservations.GroupBy(item => item.LotId, StringComparer.Ordinal))
        {
            var lot = checkpoint.GetLot(group.Key);
            if (group.Any(item => item.OwnerId != lot.OwnerId))
                throw new InvalidOperationException("Every grouped reservation must belong to the actual stock owner.");
            EnsureOwnerAndAvailableQuantity(checkpoint, lot, lot.OwnerId, checked(group.Sum(item => item.Quantity)));
        }
        return Commit(checkpoint,
            reservations: checkpoint.Reservations.Concat(reservations).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            eventKind: "inventory_reserved_together", detail: string.Join("|", reservations.Select(item => item.Id)));
    }

    /// <summary>Loads include contents: carrying a full jug moves its water too.</summary>
    public static int TransferLoadQuantity(InventoryCheckpoint checkpoint, string lotId, int quantity)
    {
        var vessel = checkpoint.GetLot(lotId);
        if (quantity <= 0 || quantity > vessel.Quantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        return checked(quantity + checkpoint.Lots.Where(lot => lot.ContainerLotId == lotId)
            .Sum(lot => lot.Quantity));
    }

    public static int ContainerRoom(InventoryCheckpoint checkpoint, string vesselLotId)
    {
        var vessel = checkpoint.GetLot(vesselLotId);
        return Math.Max(0, vessel.ContainerCapacity - checkpoint.Lots
            .Where(lot => lot.ContainerLotId == vesselLotId).Sum(lot => lot.Quantity));
    }

    /// <summary>Put owned, co-located goods in a vessel without creating or moving stock remotely.</summary>
    public static InventoryCheckpoint StoreInContainer(InventoryCheckpoint checkpoint, string operationId,
        string ownerId, string sourceLotId, int quantity, string vesselLotId)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var source = checkpoint.GetLot(sourceLotId);
        var vessel = checkpoint.GetLot(vesselLotId);
        EnsureOwnerAndAvailableQuantity(checkpoint, source, ownerId, quantity);
        EnsureOwnerAndAvailableQuantity(checkpoint, vessel, ownerId, 1);
        if (source.ContainerCapacity != 0 || source.Id == vessel.Id || source.ContainerLotId == vessel.Id ||
            vessel.ContainerCapacity <= 0 || ContainerRoom(checkpoint, vessel.Id) < quantity ||
            source.StorageBuildingId != vessel.StorageBuildingId ||
            source.DeliveryBuildingId != vessel.DeliveryBuildingId || source.GroundPosition != vessel.GroundPosition || source.CartId != vessel.CartId)
            throw new InvalidOperationException("The vessel needs room and the goods must be here with it.");
        var newId = source.Id + "#container:" + operationId;
        if (checkpoint.Lots.Any(lot => lot.Id == newId))
            throw new InvalidOperationException("This container operation was already used.");
        var lots = quantity == source.Quantity
            ? checkpoint.Lots.Select(lot => lot.Id == source.Id ? lot with { ContainerLotId = vessel.Id } : lot)
                .ToArray()
            : checkpoint.Lots.Select(lot => lot.Id == source.Id ? lot with { Quantity = lot.Quantity - quantity } : lot)
                .Append(source with
                {
                    Id = newId,
                    Quantity = quantity,
                    ProvenanceLotId = source.Id,
                    ContainerLotId = vessel.Id,
                }).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        ValidateLots(lots);
        return Commit(checkpoint, lots: lots, eventKind: "container_filled",
            detail: $"{operationId}:{source.Id}:{vessel.Id}:{quantity}");
    }

    /// <summary>Structural authority shared by transitions and current save validation.</summary>
    public static void ValidatePortableContainers(IReadOnlyList<InventoryLot> lots)
    {
        var byId = new Dictionary<string, InventoryLot>(StringComparer.Ordinal);
        foreach (var lot in lots)
            if (!byId.TryAdd(lot.Id, lot))
                throw new InvalidDataException("Portable stock has duplicate lot identities.");
        foreach (var lot in lots)
        {
            if (lot.ContainerCapacity is < 0 or > 1024 || lot.ContainerCapacity > 0 &&
                (lot.Quantity != 1 || lot.ContainerLotId is not null))
                throw new InvalidDataException("A portable vessel must be one unnested item with a bounded capacity.");
            if (lot.ContainerLotId is not { } containerId)
                continue;
            if (!byId.TryGetValue(containerId, out var vessel) || vessel.ContainerCapacity <= 0 ||
                lot.Id == containerId || lot.OwnerId != vessel.OwnerId ||
                lot.StorageBuildingId != vessel.StorageBuildingId ||
                lot.DeliveryBuildingId != vessel.DeliveryBuildingId || lot.GroundPosition != vessel.GroundPosition || lot.CartId != vessel.CartId)
                throw new InvalidDataException("Contained goods must have their vessel's owner and location.");
        }
        foreach (var group in lots.Where(lot => lot.ContainerLotId is not null).GroupBy(lot => lot.ContainerLotId!))
            if (group.Sum(lot => (long)lot.Quantity) > byId[group.Key].ContainerCapacity)
                throw new InvalidDataException("Contained goods exceed the vessel's capacity.");
    }

    private static InventoryLot[] MoveContainedLots(IReadOnlyList<InventoryLot> lots, string vesselId,
        string recipientId, string? storageId, string? deliveryId) => lots.Select(lot =>
            lot.ContainerLotId == vesselId ? lot with
            {
                OwnerId = recipientId,
                StorageBuildingId = storageId,
                DeliveryBuildingId = deliveryId,
                GroundPosition = null,
                CartId = null,
            } : lot).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();

    private static void EnsurePortableTransfer(InventoryCheckpoint checkpoint, InventoryLot lot)
    {
        if (lot.CartId is not null) throw new InvalidOperationException("Unload the actual cart cargo before transferring it.");
        // Water is carried in its jug; pouring between co-located vessels has its own transition.
        if (lot.ContainerLotId is not null && lot.ItemKind == "water")
            throw new InvalidOperationException("Carry the water jug or pour into another vessel.");
        foreach (var contained in checkpoint.Lots.Where(item => item.ContainerLotId == lot.Id))
            EnsureUnreservedQuantity(checkpoint, contained, contained.Quantity);
    }

    private static void EnsureContainerReservationAvailable(InventoryCheckpoint checkpoint, InventoryLot lot)
    {
        if (lot.ContainerLotId is { } vesselId)
            EnsureUnreservedQuantity(checkpoint, checkpoint.GetLot(vesselId), 1);
        foreach (var contained in checkpoint.Lots.Where(item => item.ContainerLotId == lot.Id))
            EnsureUnreservedQuantity(checkpoint, contained, contained.Quantity);
    }
}
