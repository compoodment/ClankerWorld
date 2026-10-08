using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private int FreeCarryCapacity(string actor) => Math.Max(0, PersonalEquipmentRules.Capacity(
        society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment) + HorseCargoCapacity(actor) -
        PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment) - ReservedBusinessCarrySpace(actor));

    private string EquipmentNote(string actor)
    {
        var inventory = society.Checkpoint.Inventory;
        var equipment = inhabitants[actor].Equipment;
        var clothing = PersonalEquipmentRules.EquippedUnit(inventory, actor, equipment?.ClothingLotId);
        var aid = PersonalEquipmentRules.EquippedUnit(inventory, actor, equipment?.CarryAidLotId);
        var ornament = PersonalEquipmentRules.EquippedUnit(inventory, actor, equipment?.OrnamentLotId);
        return $"Carrying {PersonalEquipmentRules.CarriedQuantity(inventory, actor, equipment)} of " +
            $"{PersonalEquipmentRules.Capacity(inventory, actor, equipment) + HorseCargoCapacity(actor)} units. Wearing " +
            $"{clothing?.ItemKind.Replace('_', ' ') ?? "no garment"}. Carry aid: {aid?.ItemKind ?? "none"}." +
            (ornament is null ? string.Empty : $" Ornament: {ornament.ItemKind.Replace('_', ' ')}.");
    }

    private InventoryLot? EquippedGarment(string actor) =>
        PersonalEquipmentRules.EquippedUnit(society.Checkpoint.Inventory, actor,
            inhabitants[actor].Equipment?.ClothingLotId) is { } selected && AvailableLotQuantity(selected) > 0
            ? selected : null;

    private int ClothingProtection(string actor, GridPoint position) => EquippedGarment(actor) is { } garment
        ? PersonalEquipmentRules.Protection(garment, WeatherAt(position)) : 0;

    private bool CanEquipPrivateItem(string actor, InventoryLot item, bool carryAid)
    {
        var inventory = society.Checkpoint.Inventory;
        var equipment = inhabitants[actor].Equipment;
        var previous = PersonalEquipmentRules.EquippedUnit(inventory, actor,
            carryAid ? equipment?.CarryAidLotId : equipment?.ClothingLotId);
        var before = PersonalEquipmentRules.CarriedQuantity(inventory, actor, equipment);
        // The new unit fills the slot; a displaced unit stays real cargo.
        var after = before + (previous is null ? 0 : 1) -
            (PersonalEquipmentRules.IsCarried(item, actor) ? 1 : 0);
        var capacity = carryAid ? item.ItemKind == "leather_sack" ? 32 : item.ItemKind == "sack" ? PersonalEquipmentRules.SackCapacity
            : PersonalEquipmentRules.BasketCapacity : PersonalEquipmentRules.Capacity(inventory, actor, equipment);
        return after <= before && capacity >= PersonalEquipmentRules.Capacity(inventory, actor, equipment) ||
            after + ReservedBusinessCarrySpace(actor) <= capacity + HorseCargoCapacity(actor);
    }

    private int MissingRepairInputUnits(string actor, InventoryLot item) =>
        PersonalEquipmentRules.RepairMaterials(item.ItemKind).Sum(input => Math.Max(0, input.Amount -
            society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
                lot.DeliveryBuildingId is null && lot.ItemKind == input.ResourceId).Sum(AvailableLotQuantity)));

    private IEnumerable<InventoryLot> PrivateEquipmentSources(string actor) => society.Checkpoint.Inventory.Lots
        // Borrowed goods, carried by this agent or by another member, are not theirs to take.
        .Where(lot => AvailableLotQuantity(lot) > 0 && lot.DeliveryBuildingId is null &&
            (lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) ||
                lot.OwnerId == society.Checkpoint.GetInhabitant(actor).HouseholdId &&
                lot.CarrierId is null && CanReachSharedItem(actor, lot)))
        .Concat(AvailableWarehouseStock(actor)).DistinctBy(lot => lot.Id);

    private InventoryLot? BetterGarment(string actor)
    {
        var current = EquippedGarment(actor);
        var weather = WeatherAt(inhabitants[actor].Position);
        return PrivateEquipmentSources(actor).Where(lot => PersonalEquipmentRules.IsGarment(lot.ItemKind) &&
                lot.Id != current?.Id && CanEquipPrivateItem(actor, lot, false) &&
                (current is null || PersonalEquipmentRules.Protection(lot, weather) >
                    PersonalEquipmentRules.Protection(current, weather)))
            .OrderByDescending(lot => PersonalEquipmentRules.Protection(lot, weather))
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private InventoryLot? BetterCarryAid(string actor)
    {
        var capacity = PersonalEquipmentRules.Capacity(society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment);
        return PrivateEquipmentSources(actor).Where(lot => PersonalEquipmentRules.IsCarryAid(lot.ItemKind) &&
                CanEquipPrivateItem(actor, lot, true) &&
                (lot.ItemKind == "leather_sack" ? 32 : lot.ItemKind == "sack" ? PersonalEquipmentRules.SackCapacity : PersonalEquipmentRules.BasketCapacity) > capacity)
            .OrderByDescending(lot => lot.ItemKind == "leather_sack" ? 2 : lot.ItemKind == "sack" ? 1 : 0)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private InventoryLot? RepairableEquipment(string actor, PlaytestInhabitantState person) =>
        WornEquipmentItems(actor).FirstOrDefault(lot => CanPrepareEquipmentRepair(actor, person, lot));

    private bool CanPrepareEquipmentRepair(string actor, PlaytestInhabitantState person, InventoryLot lot) =>
        MissingRepairInputUnits(actor, lot) <= FreeCarryCapacity(actor) &&
        EquipmentRepairSite(actor, lot) is { } site &&
        (person.Position == site.Position || FindUnoccupiedRoute(actor, person.Position, site.Position, 0).Count > 0) &&
        PersonalEquipmentRules.RepairMaterials(lot.ItemKind).All(input => HasCarriedOwnItem(actor, input.ResourceId) ||
            SharedItem(input.ResourceId, actor) is not null);

    private IEnumerable<InventoryLot> WornEquipmentItems(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.Quantity == 1 && lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
            lot.DeliveryBuildingId is null && lot.ConditionBasisPoints <= 4_000 &&
            (PersonalEquipmentRules.IsGarment(lot.ItemKind) || PersonalEquipmentRules.IsCarryAid(lot.ItemKind)) &&
            society.Checkpoint.Inventory.Reservations.All(item => item.LotId != lot.Id || item.State is not
                (InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed)))
        .OrderBy(lot => lot.Id == inhabitants[actor].Equipment?.CarryAidLotId ? 0 : 1)
        .ThenBy(lot => lot.Id, StringComparer.Ordinal);

    private PlacedBuilding? EquipmentRepairSite(string actor, InventoryLot lot) =>
        HouseholdBuildingWithTag(HouseholdFor(actor), lot.ItemKind == "basket" ? "house" : "tailor");

    private void AddEquipmentCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (OutdoorExposure(person.Position) > 0 && BetterGarment(actor) is { } garment)
            candidates.Add(new("wear_clothing", $"Collect and wear {garment.ItemKind.Replace('_', ' ')} to keep warm outdoors.", 3));
        if (!AdultResident(actor)) return;
        if (BetterCarryAid(actor) is { } aid)
            candidates.Add(new("equip_carry_aid", $"Equip a {aid.ItemKind} to carry more supplies.", 14));
        if (!NeedsUrgentFood(person) && !NeedsUrgentWarmth(person) && RepairableEquipment(actor, person) is { } worn &&
            EquipmentRepairSite(actor, worn) is { } site)
            candidates.Add(new("repair_equipment", $"Bring materials to repair the worn {worn.ItemKind.Replace('_', ' ')}.", 12, site.InstanceId));
    }

    private void EquipPrivateItem(string actor, PlaytestInhabitantState person, bool carryAid)
    {
        var item = carryAid ? BetterCarryAid(actor) : BetterGarment(actor);
        if (item is null) return;
        if (item.OwnerId != actor)
        {
            var position = HouseholdStockPosition(item);
            var range = HouseholdStockInteractionRange(item);
            if (!IsWithinInteractionRange(person.Position, position, range))
            {
                MoveToward(actor, person, position, "equipment", range);
                return;
            }
        }
        var equippedId = item.Id;
        var previous = inhabitants[actor].Equipment;
        var equipment = previous ?? new PersonalEquipment();
        InventoryCheckpoint Equip(InventoryCheckpoint inventory)
        {
            if (item.OwnerId != actor)
            {
                var transferId = $"equipment:{WorldTick}:{actor}:{item.Id}";
                inventory = InventoryFixture.Transfer(inventory, transferId, item.OwnerId, actor, item.Id, 1, "equipment_collected");
                equippedId = item.Quantity == 1 ? item.Id : item.Id + "#transfer:" + transferId;
            }
            else if (item.Quantity > 1)
            {
                equippedId = $"equipped:{WorldTick}:{actor}:{item.Id}";
                inventory = InventoryFixture.SplitLot(inventory, item.Id, 1, equippedId);
            }
            inhabitants[actor] = inhabitants[actor] with
            {
                Equipment = carryAid ? equipment with { CarryAidLotId = equippedId } : equipment with { ClothingLotId = equippedId },
            };
            return inventory;
        }
        try
        {
            ApplyInventoryTransition(Equip);
        }
        catch
        {
            inhabitants[actor] = inhabitants[actor] with { Equipment = previous };
            throw;
        }
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("equipment_equipped", $"{actor}|{equippedId}|{item.ItemKind}");
    }

    private bool CanContinueEquipmentRepair(string actor) => inhabitants[actor].Equipment?.Repair is { } repair &&
        !NonviolentEquipmentRepairPaused(actor) &&
        !NeedsUrgentFood(inhabitants[actor]) && !NeedsUrgentWarmth(inhabitants[actor]) &&
        repair.MaterialReservationIds.All(id => society.Checkpoint.Inventory.GetReservation(id) is
        { State: InventoryReservationState.Reserved } reservation && reservation.ExpiryTick >= WorldTick);

    private void RepairEquipment(string actor, PlaytestInhabitantState person, string? requestedLotId = null, string? orderInstructionId = null)
    {
        var target = requestedLotId is null ? RepairableEquipment(actor, person) : WornEquipmentItems(actor).FirstOrDefault(lot => lot.Id == requestedLotId);
        if (!AdultResident(actor) || NeedsUrgentFood(person) || NeedsUrgentWarmth(person) ||
            target is null || EquipmentRepairSite(actor, target) is not { } site) return;
        if (!CanPrepareEquipmentRepair(actor, person, target)) return;
        foreach (var input in PersonalEquipmentRules.RepairMaterials(target.ItemKind))
        {
            if (!HasCarriedOwnItem(actor, input.ResourceId))
            {
                CollectEquipment(actor, person, input.ResourceId);
                return;
            }
        }
        if (person.Position != site.Position)
        {
            MoveToward(actor, person, site.Position, "repair_equipment", 0);
            return;
        }
        var ids = new List<string>();
        ApplyInventoryTransition(inventory =>
        {
            foreach (var input in PersonalEquipmentRules.RepairMaterials(target.ItemKind))
            {
                var material = inventory.Lots.OrderBy(lot => lot.Id, StringComparer.Ordinal).First(lot =>
                    lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
                    lot.ItemKind == input.ResourceId && PersonalEquipmentRules.AvailableQuantity(inventory, lot) >= input.Amount);
                var id = $"repair:{WorldTick}:{actor}:{input.ResourceId}";
                inventory = InventoryFixture.Reserve(inventory, id, actor, material.Id, input.Amount,
                    "equipment_repair", WorldTick + 120);
                ids.Add(id);
            }
            return inventory;
        });
        inhabitants[actor] = inhabitants[actor] with
        {
            Equipment = (person.Equipment ?? new PersonalEquipment()) with
            { Repair = new(target.Id, site.InstanceId, WorldTick, 0, ids.ToArray(), orderInstructionId) },
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("equipment_repair_started", $"{actor}|{target.Id}|{site.InstanceId}");
    }

    private EquipmentRepairWork? ContinueEquipmentRepair(string actor)
    {
        var person = inhabitants[actor];
        if (person.Equipment?.Repair is not { } repair) return null;
        if (!CanContinueEquipmentRepair(actor) ||
            worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == repair.BuildingId) is not { } site ||
            site.HouseholdId != HouseholdFor(actor) || person.Position != site.Position ||
            society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == repair.LotId) is not { } target ||
            !PersonalEquipmentRules.IsCarried(target, actor) || target.Quantity != 1 ||
            repair.MaterialReservationIds.Any(id => society.Checkpoint.Inventory.GetReservation(id) is not
            { State: InventoryReservationState.Reserved } reservation || reservation.ExpiryTick < WorldTick))
        {
            CancelEquipmentRepair(actor);
            return null;
        }
        if (!SettlementIllnessRules.AllowsWork(actor, WorldTick, person.Survival?.IllnessBasisPoints ?? 0)) return null;
        var progress = repair.WorkDone + 1;
        if (progress < PersonalEquipmentRules.RepairWorkTicks)
        {
            inhabitants[actor] = person with { Equipment = person.Equipment with { Repair = repair with { WorkDone = progress } } };
            return null;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.RepairSingleUnit(inventory, repair.LotId, 6_000, repair.MaterialReservationIds));
        inhabitants[actor] = inhabitants[actor] with { Equipment = person.Equipment with { Repair = null } };
        GainSkill(actor, SettlementSkillKind.Crafting);
        RecordNonviolentRepairCompletion(actor, target, repair.LotId,
            $"repair-equipment:{repair.StartedTick}:{WorldTick}:{actor}:{repair.LotId}", person.Position, repair.MaterialReservationIds);
        AppendEvent("equipment_repaired", $"{actor}|{repair.LotId}");
        return repair;
    }

    private void CancelEquipmentRepair(string actor)
    {
        if (inhabitants[actor].Equipment?.Repair is not { } repair) return;
        ApplyInventoryTransition(inventory =>
        {
            foreach (var id in repair.MaterialReservationIds)
                if (inventory.GetReservation(id).State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed)
                    inventory = InventoryFixture.ReleaseReservation(inventory, id, "equipment_repair_interrupted");
            return inventory;
        });
        var person = inhabitants[actor];
        inhabitants[actor] = person with { Equipment = person.Equipment! with { Repair = null } };
        AppendEvent("equipment_repair_interrupted", $"{actor}|{repair.LotId}");
    }

    private void WearEquippedClothing()
    {
        foreach (var person in inhabitants.Values.ToArray())
        {
            if (person.Equipment?.Repair is not null && !CanContinueEquipmentRepair(person.InhabitantId))
                CancelEquipmentRepair(person.InhabitantId);
            // Garments wear in cold or wet weather only. The night chill is
            // felt as lost warmth, but adds no wear the agreed design did not ask for.
            if (person.Equipment?.Repair?.LotId == person.Equipment?.ClothingLotId ||
                WeatherExposure(person.Position) == 0 || EquippedGarment(person.InhabitantId) is not { } garment) continue;
            ApplyInventoryTransition(inventory => InventoryFixture.WearSingleUnit(inventory, garment.Id, 10));
        }
    }

    private void WearCarryAid(string actor)
    {
        if (inhabitants[actor].Equipment?.Repair is not null ||
            PersonalEquipmentRules.EquippedUnit(society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment?.CarryAidLotId) is not { } aid ||
            AvailableLotQuantity(aid) == 0 || PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor,
                inhabitants[actor].Equipment) == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.WearSingleUnit(inventory, aid.Id, 10));
    }
}
