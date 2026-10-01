namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
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
            source.DeliveryBuildingId != vessel.DeliveryBuildingId)
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
                lot.DeliveryBuildingId != vessel.DeliveryBuildingId)
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
            } : lot).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();

    private static void EnsurePortableTransfer(InventoryCheckpoint checkpoint, InventoryLot lot)
    {
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
