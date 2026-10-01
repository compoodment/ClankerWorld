namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    public static InventoryCheckpoint LoadAnimal(InventoryCheckpoint checkpoint, string operationId,
        string ownerId, string lotId, int quantity, string animalId, InventoryGroundPosition position)
    {
        ValidateCheckpoint(checkpoint);
        var source = checkpoint.GetLot(lotId);
        EnsureOwnerAndAvailableQuantity(checkpoint, source, ownerId, quantity);
        EnsurePortableTransfer(checkpoint, source);
        if (source.ContainerLotId is not null || source.ItemKind == "handcart")
            throw new InvalidOperationException("Load a loose cargo item or its whole vessel onto the horse.");
        var id = source.Id;
        if (quantity < source.Quantity)
        {
            id += "#animal:" + operationId;
            checkpoint = SplitLot(checkpoint, source.Id, quantity, id);
        }
        return Commit(checkpoint, lots: checkpoint.Lots.Select(lot => lot.Id == id || lot.ContainerLotId == id
            ? lot with { AnimalId = animalId, GroundPosition = position, StorageBuildingId = null, DeliveryBuildingId = null }
            : lot).ToArray(), eventKind: "animal_loaded", detail: $"{operationId}:{animalId}:{id}:{quantity}");
    }

    public static InventoryCheckpoint UnloadAnimal(InventoryCheckpoint checkpoint, string operationId,
        string sender, string recipient, string lotId, int quantity, string animalId, string? storageBuildingId = null)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        var source = checkpoint.GetLot(lotId);
        EnsureOwnerAndAvailableQuantity(checkpoint, source, sender, quantity);
        if (source.AnimalId != animalId || source.ContainerLotId is not null)
            throw new InvalidOperationException("Unload the actual horse cargo or its whole vessel.");
        foreach (var child in checkpoint.Lots.Where(lot => lot.ContainerLotId == lotId))
            EnsureUnreservedQuantity(checkpoint, child, child.Quantity);
        var id = source.Id;
        if (quantity < source.Quantity)
        {
            id += "#unload-animal:" + operationId;
            checkpoint = SplitLot(checkpoint, source.Id, quantity, id);
        }
        checkpoint = checkpoint with
        {
            Lots = checkpoint.Lots.Select(lot => lot.Id == id || lot.ContainerLotId == id
                ? lot with { AnimalId = null, GroundPosition = null } : lot).ToArray(),
        };
        EnsurePortableTransfer(checkpoint, checkpoint.GetLot(id));
        if (sender == recipient)
            return Commit(checkpoint, lots: checkpoint.Lots.Select(lot => lot.Id == id || lot.ContainerLotId == id
                ? lot with { StorageBuildingId = storageBuildingId, DeliveryBuildingId = null } : lot).ToArray(),
                eventKind: "animal_unloaded", detail: $"{operationId}:{animalId}:{id}:{quantity}");
        return Transfer(checkpoint, operationId, sender, recipient, id, quantity, "animal_unloaded", storageBuildingId);
    }
}
