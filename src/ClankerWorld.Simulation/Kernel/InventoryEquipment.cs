namespace ClankerWorld.Simulation.Kernel;

public static partial class InventoryFixture
{
    /// <summary>Wears one selected unreserved unit, retaining its quantity and identity.</summary>
    public static InventoryCheckpoint WearSingleUnit(InventoryCheckpoint checkpoint, string lotId,
        int conditionLossBasisPoints)
    {
        ValidateCheckpoint(checkpoint);
        if (conditionLossBasisPoints is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(conditionLossBasisPoints));
        var lot = checkpoint.GetLot(lotId);
        if (lot.Quantity != 1 || lot.ConditionBasisPoints == 0)
            throw new InvalidOperationException("Wear requires one selected unit with remaining condition.");
        EnsureUnreservedQuantity(checkpoint, lot, 1);
        return Commit(checkpoint,
            lots: checkpoint.Lots.Select(item => item.Id == lot.Id
                ? item with { ConditionBasisPoints = Math.Max(0, lot.ConditionBasisPoints - conditionLossBasisPoints) }
                : item).ToArray(),
            eventKind: "equipment_worn", detail: $"{lot.Id}:{conditionLossBasisPoints}");
    }

    /// <summary>Consumes the owner's exact repair inputs and restores one selected unit atomically.</summary>
    public static InventoryCheckpoint RepairSingleUnit(InventoryCheckpoint checkpoint, string lotId,
        int conditionRestoredBasisPoints, IReadOnlyList<string> materialReservationIds)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentNullException.ThrowIfNull(materialReservationIds);
        if (conditionRestoredBasisPoints is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(conditionRestoredBasisPoints));
        var lot = checkpoint.GetLot(lotId);
        if (lot.Quantity != 1 || lot.ConditionBasisPoints >= 10_000)
            throw new InvalidOperationException("Repair requires one selected worn unit.");
        EnsureUnreservedQuantity(checkpoint, lot, 1);
        if (materialReservationIds.Count == 0 || materialReservationIds.Any(string.IsNullOrWhiteSpace) ||
            materialReservationIds.Distinct(StringComparer.Ordinal).Count() != materialReservationIds.Count)
            throw new InvalidOperationException("Repair requires distinct reserved material inputs.");
        foreach (var id in materialReservationIds)
        {
            var reservation = checkpoint.GetReservation(id);
            if (reservation.OwnerId != lot.OwnerId || reservation.LotId == lot.Id ||
                reservation.Purpose != "equipment_repair" || reservation.State != InventoryReservationState.Reserved ||
                checkpoint.WorldTick > reservation.ExpiryTick)
                throw new InvalidOperationException("Repair inputs must be live reservations held by the equipment owner.");
        }
        // Each intermediate checkpoint is local. A later failed consumption
        // returns neither restored condition nor partially spent inputs.
        var repaired = checkpoint;
        foreach (var id in materialReservationIds.Order(StringComparer.Ordinal))
            repaired = ConsumeReservation(repaired, id);
        return Commit(repaired,
            lots: repaired.Lots.Select(item => item.Id == lot.Id
                ? item with { ConditionBasisPoints = Math.Min(10_000, lot.ConditionBasisPoints + conditionRestoredBasisPoints) }
                : item).ToArray(),
            eventKind: "equipment_repaired", detail: $"{lot.Id}:{conditionRestoredBasisPoints}");
    }
}
