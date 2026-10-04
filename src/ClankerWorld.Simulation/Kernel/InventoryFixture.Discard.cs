namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    /// <summary>Explicitly discard unreserved goods, including empty reusable vessels.</summary>
    public static InventoryCheckpoint Discard(InventoryCheckpoint checkpoint, string ownerId, string lotId, int quantity)
    {
        ValidateCheckpoint(checkpoint);
        var lot = checkpoint.GetLot(lotId);
        EnsureOwnerAndAvailablePhysicalQuantity(checkpoint, lot, ownerId, quantity);
        if (lot.ContainerLotId is not null || checkpoint.Lots.Any(item => item.ContainerLotId == lotId))
            throw new InvalidOperationException("Empty a vessel before discarding it or its contents.");
        var lots = lot.Quantity == quantity
            ? checkpoint.Lots.Where(item => item.Id != lotId).ToArray()
            : checkpoint.Lots.Select(item => item.Id == lotId ? item with { Quantity = item.Quantity - quantity } : item).ToArray();
        return Commit(checkpoint, lots: lots, eventKind: "goods_discarded", detail: $"{lotId}:{quantity}");
    }
}
