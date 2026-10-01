using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed record EquipmentState(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WornClothingLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CarryAidLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WeaponLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ShieldLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ArmorLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OrnamentLotId = null);

public sealed record EquipmentChangeResult(bool Applied, string? Failure = null);

/// <summary>Trial carrying and clothing balance. Contents of carried vessels count as carried goods.</summary>
public static class CarryEquipmentRules
{
    public const int BasicCapacity = 32;

    public static bool IsClothing(string kind) => kind is "clothing" or "padded_coat" or "rain_cloak";
    public static bool IsCarryAid(string kind) => kind is "basket" or "sack";

    public static long Load(InventoryCheckpoint inventory, string actor) => inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null && lot.GroundPosition is null &&
            (lot.ContainerLotId is null || inventory.Lots.Any(container => container.Id == lot.ContainerLotId &&
                container.StorageBuildingId is null && container.GroundPosition is null)))
        .Sum(lot => (long)lot.Quantity);

    public static int Capacity(InventoryCheckpoint inventory, PlaytestInhabitantState person)
    {
        var aid = inventory.Lots.FirstOrDefault(lot => lot.Id == person.Equipment?.CarryAidLotId &&
            lot.OwnerId == person.InhabitantId && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            lot.ContainerLotId is null && lot.GroundPosition is null && lot.Quantity > 0 &&
            lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0);
        return aid?.ItemKind switch { "basket" => 48, "sack" => 64, _ => BasicCapacity };
    }

    public static int Room(InventoryCheckpoint inventory, PlaytestInhabitantState person) =>
        checked((int)Math.Max(0, Capacity(inventory, person) - Load(inventory, person.InhabitantId)));

    public static int Protection(InventoryLot? garment, WeatherKind weather)
    {
        if (garment is null || garment.Quantity == 0 || garment.ConditionBasisPoints == 0 || garment.FreshnessBasisPoints == 0) return 0;
        var protection = garment.ItemKind switch
        {
            "clothing" => 35,
            "padded_coat" => weather == WeatherKind.Snow ? 65 : 55,
            "rain_cloak" => weather is WeatherKind.Rain or WeatherKind.Storm ? 55 : 25,
            _ => 0,
        };
        return (protection * garment.ConditionBasisPoints + 9_999) / 10_000;
    }
}

public sealed partial class PrivateWorldRuntime
{
    private static readonly string[] WearableClothingKinds = ["clothing", "padded_coat", "rain_cloak"];
    private int CarryingRoom(string actor) => BusinessCarryingRoom(actor);

    private void ValidateCarryingTransition(InventoryCheckpoint before, InventoryCheckpoint after, string? committedBusinessOfferId = null)
    {
        foreach (var person in inhabitants.Values)
        {
            var oldLoad = CarryEquipmentRules.Load(before, person.InhabitantId);
            var newLoad = CarryEquipmentRules.Load(after, person.InhabitantId);
            if (newLoad > CarryEquipmentRules.Capacity(after, person) -
                ReservedBusinessCarrySpace(person.InhabitantId, committedBusinessOfferId) && newLoad > oldLoad)
                throw new InvalidOperationException("The agent cannot carry that load; deliver goods or equip a larger carrying aid first.");
        }
    }

    private InventoryLot? EquippedClothing(string actor) => society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
        lot.Id == inhabitants[actor].Equipment?.WornClothingLotId && lot.OwnerId == actor &&
        lot.StorageBuildingId is null && lot.Quantity > 0);

    private bool IsEquippedLot(string actor, string lotId) => inhabitants[actor].Equipment is { } equipment &&
        (equipment.WornClothingLotId == lotId || equipment.CarryAidLotId == lotId || IsAdditionalEquippedLot(actor, lotId));

    /// <summary>Equip personally carried gear. Changing a carrying aid never discards its cargo.</summary>
    public EquipmentChangeResult EquipItem(string actor, string lotId)
    {
        gate.Wait();
        try { return EquipItemCore(actor, lotId); }
        finally { gate.Release(); }
    }

    public EquipmentChangeResult RemoveCarryAid(string actor)
    {
        gate.Wait();
        try
        {
            if (!inhabitants.TryGetValue(actor, out var person)) return new(false, "This agent is not active.");
            if (CarryEquipmentRules.Load(society.Checkpoint.Inventory, actor) + ReservedBusinessCarrySpace(actor) >
                CarryEquipmentRules.BasicCapacity)
                return new(false, "Put down or deliver some carried goods before removing the carrying aid.");
            inhabitants[actor] = person with { Equipment = (person.Equipment ?? new()) with { CarryAidLotId = null } };
            AppendEvent("carry_aid_removed", actor);
            return new(true);
        }
        finally { gate.Release(); }
    }

    private EquipmentChangeResult EquipItemCore(string actor, string lotId)
    {
        if (!inhabitants.TryGetValue(actor, out var person)) return new(false, "This agent is not active.");
        var inventory = society.Checkpoint.Inventory;
        var lot = inventory.Lots.FirstOrDefault(item => item.Id == lotId && item.OwnerId == actor &&
            item.StorageBuildingId is null && item.DeliveryBuildingId is null &&
            item.ContainerLotId is null && item.GroundPosition is null && AvailableLotQuantity(item) > 0);
        if (lot is not null && GearSlot(lot.ItemKind) is not null) return EquipPersonalGearLot(actor, lot);
        if (lot is null || !CarryEquipmentRules.IsClothing(lot.ItemKind) && !CarryEquipmentRules.IsCarryAid(lot.ItemKind))
            return new(false, "The agent must carry a usable garment, basket or sack to equip it.");
        var equipment = person.Equipment ?? new EquipmentState();
        var proposed = person with
        {
            Equipment = CarryEquipmentRules.IsCarryAid(lot.ItemKind)
            ? equipment with { CarryAidLotId = lot.Id } : equipment with { WornClothingLotId = lot.Id }
        };
        if (CarryEquipmentRules.IsCarryAid(lot.ItemKind) && CarryEquipmentRules.Load(inventory, actor) + ReservedBusinessCarrySpace(actor) >
            CarryEquipmentRules.Capacity(inventory, proposed))
            return new(false, "Deliver some goods before changing to a smaller carrying aid.");
        if (lot.Quantity > 1)
        {
            var equippedId = $"{lot.Id}#equipped:{WorldTick}:{actor}";
            ApplyInventoryTransition(current => InventoryFixture.SplitLot(current, lot.Id, 1, equippedId));
            proposed = proposed with
            {
                Equipment = CarryEquipmentRules.IsCarryAid(lot.ItemKind)
                ? proposed.Equipment! with { CarryAidLotId = equippedId }
                : proposed.Equipment! with { WornClothingLotId = equippedId }
            };
        }
        inhabitants[actor] = proposed;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("equipment_worn", $"{actor}|{lot.ItemKind}");
        return new(true);
    }

    private InventoryLot? BestPersonalClothing(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            lot.ContainerLotId is null && lot.GroundPosition is null && AvailableLotQuantity(lot) > 0 &&
            CarryEquipmentRules.IsClothing(lot.ItemKind))
        .OrderByDescending(lot => CarryEquipmentRules.Protection(lot, WeatherAt(inhabitants[actor].Position)))
        .ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private string? BestSharedClothing(string actor, int currentProtection) => WearableClothingKinds
        .Where(kind => society.Checkpoint.Inventory.Lots.Any(lot => lot.ItemKind == kind &&
            (lot.OwnerId == HouseholdFor(actor) || lot.OwnerId == TownForResident(actor)) &&
            AvailableLotQuantity(lot) > 0 && CarryEquipmentRules.Protection(lot, WeatherAt(inhabitants[actor].Position)) > currentProtection))
        .Select(kind => SharedItem(kind, actor)).Where(lot => lot is not null)
        .OrderByDescending(lot => CarryEquipmentRules.Protection(lot, WeatherAt(inhabitants[actor].Position)))
        .ThenBy(lot => lot!.Id, StringComparer.Ordinal).FirstOrDefault()?.ItemKind;

    private void WearBestClothing(string actor, PlaytestInhabitantState person)
    {
        var current = CarryEquipmentRules.Protection(EquippedClothing(actor), WeatherAt(person.Position));
        if (BestPersonalClothing(actor) is { } personal && CarryEquipmentRules.Protection(personal, WeatherAt(person.Position)) > current)
        {
            _ = EquipItemCore(actor, personal.Id);
            return;
        }
        if (BestSharedClothing(actor, current) is { } kind && SharedItem(kind, actor) is { } stock &&
            CarryEquipmentRules.Protection(stock, WeatherAt(person.Position)) > current && CarryingRoom(actor) > 0)
        {
            CollectEquipment(actor, person, kind);
            if (BestPersonalClothing(actor) is { } collected) _ = EquipItemCore(actor, collected.Id);
        }
    }

    private void AddEquipmentCandidates(List<CognitionCandidate> candidates, string actor)
    {
        var person = inhabitants[actor];
        var currentProtection = CarryEquipmentRules.Protection(EquippedClothing(actor), WeatherAt(person.Position));
        var personal = BestPersonalClothing(actor);
        var sharedKind = BestSharedClothing(actor, currentProtection);
        var shared = sharedKind is null ? null : SharedItem(sharedKind, actor);
        if (personal is not null && CarryEquipmentRules.Protection(personal, WeatherAt(person.Position)) > currentProtection ||
            shared is not null && CarryingRoom(actor) > 0 && CarryEquipmentRules.Protection(shared, WeatherAt(person.Position)) > currentProtection)
            candidates.Add(new("wear_clothing", "Put on usable clothing suited to the weather.", WeatherExposure(person.Position) > 0 ? 3 : 18));

        if (!AdultResident(actor)) return;
        if (CarryingRoom(actor) <= 1 &&
            PersonalGoodsForStorage(actor) is not null && society.Checkpoint.GetInhabitant(actor).HouseholdId is { } household &&
            HouseForHousehold(household) is { } home && StorageRoom(home.InstanceId) > 0 &&
            FindUnoccupiedRoute(actor, person.Position, home.Position, 0).Count > 0)
            candidates.Add(new("store_carried_goods", "Deliver carried supplies to the household House to make room.", 16, home.InstanceId));
        var capacity = CarryEquipmentRules.Capacity(society.Checkpoint.Inventory, person);
        foreach (var kind in new[] { "sack", "basket" })
        {
            var targetCapacity = kind == "sack" ? 64 : 48;
            if (targetCapacity <= capacity) continue;
            var aid = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == actor &&
                lot.ItemKind == kind && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0);
            if (aid is not null || CarryingRoom(actor) > 0 && SharedItem(kind, actor) is not null)
                candidates.Add(new("equip_carry:" + kind, $"Use a {kind} to carry larger loads.", 19));
        }
        foreach (var id in new[] { person.Equipment?.WornClothingLotId, person.Equipment?.CarryAidLotId })
        {
            if (id is not null && RepairSite(actor, id) is { } site)
                candidates.Add(new("repair_gear:" + id, "Bring worn equipment to household stock for repair.", 17, site.InstanceId));
        }
    }

    private InventoryLot? PersonalGoodsForStorage(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            lot.ContainerLotId is null && lot.GroundPosition is null &&
            lot.ContainerLotId is null && AvailableLotQuantity(lot) > 0 && !IsEquippedLot(actor, lot.Id) &&
            (!IsEdibleFood(lot.ItemKind) && ToolCapabilities.ForItem(lot.ItemKind) is null && !VesselRules.IsVessel(lot.ItemKind) ||
                AvailableLotQuantity(lot) > 1) &&
            !CarryEquipmentRules.IsClothing(lot.ItemKind) && !CarryEquipmentRules.IsCarryAid(lot.ItemKind))
        .OrderByDescending(lot => lot.Quantity).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void StoreCarriedGoods(string actor, PlaytestInhabitantState person)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            HouseForHousehold(household) is not { } house || PersonalGoodsForStorage(actor) is not { } goods) return;
        if (person.Position != house.Position)
        {
            MoveToward(actor, person, house.Position, "store_carried_goods", 0);
            return;
        }
        var reserve = IsEdibleFood(goods.ItemKind) || ToolCapabilities.ForItem(goods.ItemKind) is not null ? 1 : 0;
        var quantity = Math.Min(StorageRoom(house.InstanceId), Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(goods) - reserve));
        if (quantity <= 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"carried-stock:{WorldTick}:{actor}", actor, household, goods.Id, quantity,
            "carried_goods_stored", house.InstanceId));
        AppendEvent("carried_goods_stored", $"{actor}|{goods.ItemKind}|{quantity}|{house.InstanceId}");
    }

    private void EquipCarryAid(string actor, PlaytestInhabitantState person, string kind)
    {
        if (!CarryEquipmentRules.IsCarryAid(kind)) return;
        var item = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == actor && lot.ItemKind == kind &&
            lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0);
        if (item is null)
        {
            CollectEquipment(actor, person, kind);
            item = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == actor && lot.ItemKind == kind &&
                lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0);
        }
        if (item is not null) _ = EquipItemCore(actor, item.Id);
    }

    private static string RepairMaterial(string kind) => kind == "basket" ? "fiber" : "cloth";

    private PlacedBuilding? RepairSite(string actor, string lotId)
    {
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId && item.OwnerId == actor &&
            item.Quantity == 1 && item.ConditionBasisPoints < 7_500 && IsEquippedLot(actor, item.Id));
        if (lot is null || !CarryEquipmentRules.IsClothing(lot.ItemKind) && !CarryEquipmentRules.IsCarryAid(lot.ItemKind) ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household) return null;
        return worldSimulation.Buildings.Where(building => building.HouseholdId == household &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Any(tag => tag is "house" or "tailor")) &&
                HasIngredientsAtBuilding([new(RepairMaterial(lot.ItemKind), 1)], household, building.InstanceId) &&
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, building.Position, 0).Count > 0)
            .OrderBy(building => map.FootDistance(inhabitants[actor].Position, building.Position))
            .ThenBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();
    }

    private void RepairGear(string actor, PlaytestInhabitantState person, string lotId)
    {
        if (RepairSite(actor, lotId) is not { } building) return;
        if (person.Position != building.Position)
        {
            MoveToward(actor, person, building.Position, "repair_equipment", 0);
            return;
        }
        var lot = society.Checkpoint.Inventory.GetLot(lotId);
        var material = RepairMaterial(lot.ItemKind);
        ApplyInventoryTransition(inventory =>
        {
            var reserved = ReserveQuantities(inventory, [new(material, 1)], $"repair-gear:{WorldTick}:{actor}",
                WorldTick, building.HouseholdId!, out var ids, building.InstanceId);
            foreach (var id in ids) reserved = InventoryFixture.ConsumeReservation(reserved, id);
            return reserved with
            {
                Lots = reserved.Lots.Select(item => item.Id == lotId
                ? item with { ConditionBasisPoints = 10_000 } : item).ToArray()
            };
        });
        AppendEvent("equipment_repaired", $"{actor}|{lot.ItemKind}|{building.InstanceId}");
    }

    private void WearClothing()
    {
        var ids = inhabitants.Values.Select(person => person.Equipment?.WornClothingLotId)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        WearEquipmentLots(ids);
    }

    private void WearCarryAid(string actor)
    {
        if (inhabitants[actor].Equipment?.CarryAidLotId is { } id) WearEquipmentLots(new HashSet<string>(StringComparer.Ordinal) { id });
    }

    private void WearEquipmentLots(HashSet<string> ids)
    {
        if (ids.Count == 0) return;
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(lot => ids.Contains(lot.Id) && lot.ConditionBasisPoints > 0
                ? lot with { ConditionBasisPoints = lot.ConditionBasisPoints - 1 } : lot).ToArray(),
        });
    }

    private static void ValidateEquipment(PrivateWorldRuntimeState state)
    {
        foreach (var person in state.Inhabitants)
        {
            if (person.Equipment is not { } equipment) continue;
            foreach (var (id, carry) in new[] { (equipment.WornClothingLotId, false), (equipment.CarryAidLotId, true) })
            {
                if (id is null) continue;
                var lot = state.Society.Society.Inventory.Lots.FirstOrDefault(item => item.Id == id);
                if (lot is null || lot.OwnerId != person.InhabitantId || lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null ||
                    lot.ContainerLotId is not null || lot.GroundPosition is not null ||
                    lot.Quantity != 1 || (carry ? !CarryEquipmentRules.IsCarryAid(lot.ItemKind) : !CarryEquipmentRules.IsClothing(lot.ItemKind)))
                    throw new InvalidDataException("Equipped gear must reference one personally carried item of the correct kind.");
            }
        }
    }
}
