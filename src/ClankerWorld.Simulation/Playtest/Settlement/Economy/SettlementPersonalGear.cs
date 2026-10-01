using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string EquipPersonalGearPrefix = "equip_personal_gear:";
    private const string GiftOrnamentPrefix = "gift_ornament:";
    private const string RepairCombatGearPrefix = "repair_combat_gear:";
    private static readonly string[] PersonalGearKinds = ["diamond_ornament", "gold_ornament", "sword", "spear", "shield", "basic_armor"];

    private static string? GearSlot(string kind) => OrnamentContent.IsOrnament(kind) ? "ornament"
        : CombatGearContent.IsWeapon(kind) ? "weapon" : kind == "shield" ? "shield" : kind == "basic_armor" ? "armor" : null;

    private static string? EquippedGearId(EquipmentState equipment, string slot) => slot switch
    {
        "ornament" => equipment.OrnamentLotId,
        "weapon" => equipment.WeaponLotId,
        "shield" => equipment.ShieldLotId,
        "armor" => equipment.ArmorLotId,
        _ => null,
    };

    private static EquipmentState SetGearSlot(EquipmentState equipment, string slot, string? id) => slot switch
    {
        "ornament" => equipment with { OrnamentLotId = id },
        "weapon" => equipment with { WeaponLotId = id },
        "shield" => equipment with { ShieldLotId = id },
        "armor" => equipment with { ArmorLotId = id },
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private InventoryLot? PersonalGear(string actor, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == kind && lot.StorageBuildingId is null &&
            lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private bool IsAdditionalEquippedLot(string actor, string lotId) => inhabitants[actor].Equipment is { } equipment &&
        new[] { equipment.WeaponLotId, equipment.ShieldLotId, equipment.ArmorLotId, equipment.OrnamentLotId }.Contains(lotId, StringComparer.Ordinal);

    private bool NeedsPersonalGearOutput(string kind, string? household)
    {
        var available = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == kind &&
            (household is null || lot.OwnerId == household || inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == household))
            .Sum(AvailableLotQuantity);
        var adults = inhabitants.Keys.Count(actor => AdultResident(actor) && (household is null || HouseholdFor(actor) == household));
        return available < adults + 1;
    }

    private void AddPersonalGearCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        var equipment = inhabitants[actor].Equipment ?? new();
        foreach (var kind in PersonalGearKinds)
        {
            var slot = GearSlot(kind)!;
            var current = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == EquippedGearId(equipment, slot) &&
                lot.OwnerId == actor && lot.ConditionBasisPoints > 0);
            if (current is not null && !(kind == "diamond_ornament" && current.ItemKind == "gold_ornament")) continue;
            if (PersonalGear(actor, kind) is not null || CarryingRoom(actor) > 0 && SharedItem(kind, actor) is not null)
                candidates.Add(new(EquipPersonalGearPrefix + kind,
                    $"Collect and equip an accessible {kind.Replace('_', ' ')}.", OrnamentContent.IsOrnament(kind) ? 40 : 39));
        }
        var spare = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == actor &&
            OrnamentContent.IsOrnament(lot.ItemKind) && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            lot.ContainerLotId is null && lot.GroundPosition is null &&
            AvailableLotQuantity(lot) > 0 && !IsAdditionalEquippedLot(actor, lot.Id));
        if (spare is not null)
            foreach (var recipient in inhabitants.Values.Where(person => person.InhabitantId != actor &&
                         person.Equipment?.OrnamentLotId is null &&
                         society.Checkpoint.GetInhabitant(person.InhabitantId).Status == SocietyInhabitantStatus.Active &&
                         FindUnoccupiedRoute(actor, inhabitants[actor].Position, person.Position, 1).Count > 0)
                         .OrderBy(person => person.InhabitantId, StringComparer.Ordinal))
                candidates.Add(new(GiftOrnamentPrefix + recipient.InhabitantId,
                    "Bring a personally owned spare ornament to this agent as a gift.", 110, recipient.InhabitantId));
        foreach (var id in new[] { equipment.WeaponLotId, equipment.ShieldLotId, equipment.ArmorLotId })
            if (id is not null && CombatGearRepairNeed(actor, id) is { } repair)
                candidates.Add(new(RepairCombatGearPrefix + id, "Repair worn gear with the household Blacksmith's iron.", 19, repair.Smith.InstanceId));
    }

    private void EquipPersonalGear(string actor, PlaytestInhabitantState person, string kind)
    {
        if (GearSlot(kind) is not { } slot) return;
        var lot = PersonalGear(actor, kind);
        if (lot is null)
        {
            CollectEquipment(actor, person, kind);
            lot = PersonalGear(actor, kind);
        }
        if (lot is null || AvailableLotQuantity(lot) <= 0) return;
        _ = EquipPersonalGearLot(actor, lot);
    }

    private EquipmentChangeResult EquipPersonalGearLot(string actor, InventoryLot lot)
    {
        var slot = GearSlot(lot.ItemKind)!;
        var id = lot.Id;
        if (lot.Quantity > 1)
        {
            id = $"gear-equipped:{WorldTick}:{nextEventId}:{actor}:{lot.Id}";
            ApplyInventoryTransition(inventory => InventoryFixture.SplitLot(inventory, lot.Id, 1, id));
        }
        inhabitants[actor] = inhabitants[actor] with { Equipment = SetGearSlot(inhabitants[actor].Equipment ?? new(), slot, id) };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("personal_gear_equipped", $"{actor}|{lot.ItemKind}|{id}");
        return new(true);
    }

    public EquipmentChangeResult GiveOrnament(string actor, string recipientId, string lotId)
    {
        gate.Wait();
        try { return GiveOrnamentCore(actor, recipientId, lotId); }
        finally { gate.Release(); }
    }

    private EquipmentChangeResult GiveOrnamentCore(string actor, string recipientId, string lotId)
    {
        if (!AdultResident(actor) || actor == recipientId || !inhabitants.TryGetValue(recipientId, out var recipient) ||
            !IsWithinInteractionRange(inhabitants[actor].Position, recipient.Position, 1))
            return new(false, "Bring the ornament to a nearby living recipient first.");
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId && item.OwnerId == actor &&
            OrnamentContent.IsOrnament(item.ItemKind) && item.StorageBuildingId is null && item.DeliveryBuildingId is null &&
            item.ContainerLotId is null && item.GroundPosition is null &&
            AvailableLotQuantity(item) > 0);
        if (lot is null) return new(false, "This agent must personally carry an unreserved usable ornament to give it.");
        if (CarryingRoom(recipientId) < 1) return new(false, "The recipient has no room to carry the ornament.");
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"ornament-gift:{WorldTick}:{nextEventId}:{actor}:{recipientId}:{lotId}",
            actor, recipientId, lot.Id, 1, "ornament_given"));
        if (lot.Quantity == 1 && inhabitants[actor].Equipment?.OrnamentLotId == lot.Id)
            inhabitants[actor] = inhabitants[actor] with { Equipment = inhabitants[actor].Equipment! with { OrnamentLotId = null } };
        AppendEvent("ornament_given", $"{actor}|{recipientId}|{lot.Id}");
        return new(true);
    }

    private void GiftSpareOrnament(string actor, PlaytestInhabitantState person, string recipientId)
    {
        if (!inhabitants.TryGetValue(recipientId, out var recipient)) return;
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.OwnerId == actor &&
            OrnamentContent.IsOrnament(item.ItemKind) && item.StorageBuildingId is null && item.DeliveryBuildingId is null &&
            item.ContainerLotId is null && item.GroundPosition is null &&
            AvailableLotQuantity(item) > 0 && !IsAdditionalEquippedLot(actor, item.Id));
        if (lot is null) return;
        if (!IsWithinInteractionRange(person.Position, recipient.Position, 1))
        {
            MoveToward(actor, person, recipient.Position, "ornament_gift", 1);
            return;
        }
        _ = GiveOrnamentCore(actor, recipientId, lot.Id);
    }

    private (InventoryLot Lot, PlacedBuilding Smith)? CombatGearRepairNeed(string actor, string lotId)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            BlacksmithForHousehold(household) is not { } smith) return null;
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId && item.OwnerId == actor &&
            item.Quantity == 1 && item.StorageBuildingId is null && item.DeliveryBuildingId is null &&
            item.ContainerLotId is null && item.GroundPosition is null &&
            CombatGearContent.IsGear(item.ItemKind) && item.ConditionBasisPoints is > 0 and <= 4_000 && AvailableLotQuantity(item) == 1);
        return lot is not null && HasIngredientsAtBuilding([new("iron", 1)], household, smith.InstanceId) ? (lot, smith) : null;
    }

    private void RepairCombatGear(string actor, PlaytestInhabitantState person, string lotId)
    {
        if (CombatGearRepairNeed(actor, lotId) is not { } need) return;
        if (person.Position != need.Smith.Position)
        {
            MoveToward(actor, person, need.Smith.Position, "combat_gear_repair", 0);
            return;
        }
        ApplyInventoryTransition(inventory =>
        {
            inventory = ReserveQuantities(inventory, [new("iron", 1)], $"combat-gear-repair:{WorldTick}:{actor}",
                WorldTick, need.Smith.HouseholdId!, out var ids, need.Smith.InstanceId);
            foreach (var id in ids) inventory = InventoryFixture.ConsumeReservation(inventory, id);
            return InventoryFixture.ChangeCondition(inventory, lotId, actor, 10_000 - need.Lot.ConditionBasisPoints, "combat_gear_repaired");
        });
        AppendEvent("combat_gear_repaired", $"{actor}|{lotId}|{need.Smith.InstanceId}");
    }

    private static void ValidatePersonalGear(PrivateWorldRuntimeState state)
    {
        foreach (var person in state.Inhabitants)
        {
            if (person.Equipment is not { } equipment) continue;
            foreach (var slot in new[] { "ornament", "weapon", "shield", "armor" })
            {
                if (EquippedGearId(equipment, slot) is not { } id) continue;
                var lot = state.Society.Society.Inventory.Lots.FirstOrDefault(item => item.Id == id);
                if (lot is null || lot.OwnerId != person.InhabitantId || lot.Quantity != 1 ||
                    lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null ||
                    lot.ContainerLotId is not null || lot.GroundPosition is not null || GearSlot(lot.ItemKind) != slot ||
                    state.Society.Society.Inventory.Reservations.Any(reservation => reservation.LotId == id &&
                        reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed))
                    throw new InvalidDataException("Equipped personal gear must reference one personally carried item of the correct kind.");
            }
        }
    }
}
