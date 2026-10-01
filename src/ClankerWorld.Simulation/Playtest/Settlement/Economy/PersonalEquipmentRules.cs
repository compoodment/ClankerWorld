using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed record PersonalEquipment(string? ClothingLotId = null, string? CarryAidLotId = null,
    EquipmentRepairWork? Repair = null);

public sealed record EquipmentRepairWork(string LotId, string BuildingId, long StartedTick,
    int WorkDone, IReadOnlyList<string> MaterialReservationIds);

/// <summary>Provisional protection and capacity from real, singly equipped inventory units.</summary>
public static class PersonalEquipmentRules
{
    public const int BaseCapacity = 8;
    public const int BasketCapacity = 16;
    public const int SackCapacity = 24;
    public const int RepairWorkTicks = 8;

    public static bool IsGarment(string kind) => kind is "clothing" or "padded_coat" or "rain_cloak";
    public static bool IsCarryAid(string kind) => kind is "basket" or "sack";
    public static bool IsCarried(InventoryLot lot, string actor) =>
        (lot.CarrierId == actor || lot.CarrierId is null && lot.OwnerId == actor) &&
        lot.StorageBuildingId is null && lot.GroundPosition is null;

    public static int AvailableQuantity(InventoryCheckpoint inventory, InventoryLot lot) =>
        lot.ConditionBasisPoints == 0 || lot.FreshnessBasisPoints == 0 ? 0 : Math.Max(0, lot.Quantity -
            inventory.Reservations.Where(item => item.LotId == lot.Id && item.State is
                InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                InventoryReservationState.Committed).Sum(item => item.Quantity));

    public static InventoryLot? EquippedUnit(InventoryCheckpoint inventory, string actor, string? id) =>
        id is null ? null : inventory.Lots.FirstOrDefault(lot => lot.Id == id && lot.Quantity == 1 &&
            lot.OwnerId == actor && IsCarried(lot, actor) && lot.DeliveryBuildingId is null);

    public static int Capacity(InventoryCheckpoint inventory, string actor, PersonalEquipment? equipment)
    {
        var aid = EquippedUnit(inventory, actor, equipment?.CarryAidLotId);
        return aid is null || AvailableQuantity(inventory, aid) == 0 ? BaseCapacity : aid.ItemKind switch
        {
            "basket" => BasketCapacity,
            "sack" => SackCapacity,
            _ => BaseCapacity,
        };
    }

    public static int CarriedQuantity(InventoryCheckpoint inventory, string actor, PersonalEquipment? equipment)
    {
        // Delivery loads and vessel contents remain physical cargo. Flat lots
        // count each unit once; there is no recursive container multiplier.
        var clothing = EquippedUnit(inventory, actor, equipment?.ClothingLotId);
        var aid = EquippedUnit(inventory, actor, equipment?.CarryAidLotId);
        return inventory.Lots.Where(lot => IsCarried(lot, actor)).Sum(lot => lot.Quantity) -
            (clothing is null ? 0 : 1) - (aid is null || aid.Id == clothing?.Id ? 0 : 1);
    }

    public static int FreeCapacity(InventoryCheckpoint inventory, string actor, PersonalEquipment? equipment) =>
        Math.Max(0, Capacity(inventory, actor, equipment) - CarriedQuantity(inventory, actor, equipment));

    public static int Protection(InventoryLot garment, WeatherKind weather)
    {
        var wet = weather is WeatherKind.Rain or WeatherKind.Storm;
        var full = garment.ItemKind switch
        {
            "padded_coat" => wet ? 10 : 55,
            "rain_cloak" => wet ? 55 : 15,
            "clothing" => wet ? 10 : 35,
            _ => 0,
        };
        return full * garment.ConditionBasisPoints / 10_000;
    }

    public static IReadOnlyList<ContentQuantity> RepairMaterials(string kind) => kind switch
    {
        "basket" => [new("fiber", 1), new("rope", 1)],
        "clothing" or "padded_coat" or "rain_cloak" or "sack" => [new("cloth", 1)],
        _ => [],
    };
}
