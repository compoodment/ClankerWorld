using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateEquipment(IEnumerable<PlaytestInhabitantState> people, SocietyCheckpoint society,
        WorldContentSimulationState? simulation, DeclarativeWorldContentState? content, int schema)
    {
        foreach (var person in people)
        {
            if (person.Equipment is not { } equipment) continue;
            ValidateEquipmentShape(equipment, society.WorldTick, schema);
            foreach (var (id, carryAid) in new[] { (equipment.ClothingLotId, false), (equipment.CarryAidLotId, true) })
            {
                if (id is null) continue;
                var unit = PersonalEquipmentRules.EquippedUnit(society.Inventory, person.InhabitantId, id);
                if (unit is null || !(carryAid ? PersonalEquipmentRules.IsCarryAid(unit.ItemKind) : PersonalEquipmentRules.IsGarment(unit.ItemKind)) ||
                    society.Inventory.Reservations.Any(item => item.LotId == id && item.State is
                        InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed))
                    throw new InvalidDataException("Equipped goods must be one physically carried, unreserved unit owned by that person.");
            }
            if (equipment.Repair is not { } repair) continue;
            var target = society.Inventory.Lots.FirstOrDefault(lot => lot.Id == repair.LotId);
            var building = simulation?.Buildings.FirstOrDefault(item => item.InstanceId == repair.BuildingId);
            var definition = content?.Buildings.FirstOrDefault(item => item.CanonicalId == building?.DefinitionId);
            var household = society.Inhabitants.Single(item => item.Id == person.InhabitantId).HouseholdId;
            if (target is null || target.Quantity != 1 || !PersonalEquipmentRules.IsCarried(target, person.InhabitantId) ||
                target.DeliveryBuildingId is not null || target.ConditionBasisPoints >= 10_000 ||
                society.Inventory.Reservations.Any(item => item.LotId == target.Id && item.State is
                    InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed) ||
                PersonalEquipmentRules.RepairMaterials(target.ItemKind).Count == 0 ||
                building is null || definition is null || household is null || building.HouseholdId != household ||
                person.Position != building.Position || !definition.Tags.Contains(target.ItemKind == "basket" ? "house" : "tailor", StringComparer.Ordinal))
                throw new InvalidDataException("The saved equipment repair has no valid carried item and private work site.");
            var actual = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in repair.MaterialReservationIds)
            {
                var reservation = society.Inventory.Reservations.FirstOrDefault(item => item.Id == id);
                var lot = society.Inventory.Lots.FirstOrDefault(item => item.Id == reservation?.LotId);
                if (reservation is null || lot is null || reservation.State != InventoryReservationState.Reserved ||
                    reservation.Purpose != "equipment_repair" || reservation.OwnerId != person.InhabitantId ||
                    reservation.ExpiryTick < society.WorldTick || reservation.ExpiryTick > repair.StartedTick + 120 ||
                    lot.Id == target.Id || !PersonalEquipmentRules.IsCarried(lot, person.InhabitantId) ||
                    lot.DeliveryBuildingId is not null || lot.ConditionBasisPoints == 0 || lot.FreshnessBasisPoints == 0)
                    throw new InvalidDataException("The saved equipment repair has invalid material reservations.");
                actual[lot.ItemKind] = actual.GetValueOrDefault(lot.ItemKind) + reservation.Quantity;
            }
            var expected = PersonalEquipmentRules.RepairMaterials(target.ItemKind);
            if (actual.Count != expected.Count || expected.Any(input => actual.GetValueOrDefault(input.ResourceId) != input.Amount))
                throw new InvalidDataException("The saved equipment repair does not reserve its exact material cost.");
        }
    }

    private static void ValidateEquipmentShape(PersonalEquipment equipment, long tick, int schema)
    {
        static bool InvalidId(string? id) => id is not null && (string.IsNullOrWhiteSpace(id) || id.Any(char.IsControl));
        if (schema < 34 || InvalidId(equipment.ClothingLotId) || InvalidId(equipment.CarryAidLotId) ||
            equipment.ClothingLotId is not null && equipment.ClothingLotId == equipment.CarryAidLotId ||
            equipment.Repair is { } repair &&
            (InvalidId(repair.LotId) || InvalidId(repair.BuildingId) || repair.LotId is null || repair.BuildingId is null ||
             repair.StartedTick < 0 || repair.StartedTick > tick || tick - repair.StartedTick > 120 ||
             repair.WorkDone < 0 || repair.WorkDone >= PersonalEquipmentRules.RepairWorkTicks ||
             repair.WorkDone > tick - repair.StartedTick || repair.MaterialReservationIds is null ||
             repair.MaterialReservationIds.Count is < 1 or > 2 || repair.MaterialReservationIds.Any(InvalidId) ||
             repair.MaterialReservationIds.Any(id => id is null) ||
             repair.MaterialReservationIds.Distinct(StringComparer.Ordinal).Count() != repair.MaterialReservationIds.Count))
            throw new InvalidDataException("The saved personal equipment is invalid.");
    }
}
