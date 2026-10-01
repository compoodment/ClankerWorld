using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool CropStockOwnedByHousehold(InventoryLot lot, string household) => lot.OwnerId == household ||
        inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == household;

    // Available quantity has already removed actual planting, cooking and trade reservations.
    private long HouseholdCropStock(string household, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && CropStockOwnedByHousehold(lot, household))
        .Sum(lot => (long)AvailableLotQuantity(lot));

    /// <summary>Extra unreserved planting units to keep, beyond actual kernel reservations.</summary>
    public int FarmPlantingReserve(string household, string kind)
    {
        if (!FoodItems.IsPlantingStock(kind)) return 0;
        var treeSeed = kind is TreeGrowthRules.TreeSeedItem or TreeGrowthRules.OrchardSeedItem;
        if (!treeSeed && FarmhouseForHousehold(household) is null) return 0;
        var matchingFields = fields.Where(field => field.HouseholdId == household &&
            FarmFieldRules.IsCrop(field.Work?.Crop ?? field.Crop) &&
            FarmFieldRules.PlantingItem((field.Work?.Crop ?? field.Crop)!) == kind).ToArray();
        var needed = Math.Max(1, matchingFields.Length);
        var inventory = society.Checkpoint.Inventory;
        var protectedQuantity = inventory.Reservations.Where(reservation => reservation.Quantity > 0 &&
            reservation.ExpiryTick >= WorldTick && reservation.State is (InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed) &&
            (reservation.Purpose == "orchard_replanting" && treeSeed ||
                reservation.Purpose is ("field_planting" or "field_replanting") && fields.Any(field => field.HouseholdId == household &&
                    (field.Work?.SeedReservationId == reservation.Id || field.ReplantingReservationId == reservation.Id))))
            .Sum(reservation => inventory.Lots.SingleOrDefault(lot => lot.Id == reservation.LotId) is { } lot &&
                lot.OwnerId == reservation.OwnerId && CropStockOwnedByHousehold(lot, household) && lot.ItemKind == kind &&
                lot.Quantity >= reservation.Quantity && lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0
                    ? reservation.Quantity : 0);
        return Math.Max(0, needed - protectedQuantity);
    }
}
