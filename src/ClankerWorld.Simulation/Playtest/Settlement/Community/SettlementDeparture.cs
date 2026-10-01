using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A completed membership exit, including the once-only allowance and limited collection right.</summary>
public sealed record SettlementDeparture(string HouseholdId, long Tick, string Cause,
    IReadOnlyList<string> CareGroup, IReadOnlyList<string> AllowanceLotIds, int AllowancePortions);

public sealed partial class PrivateWorldRuntime
{
    private IReadOnlyList<string> MovingCareGroup(string actor) => SocietyFixture.MovingCareGroup(society.Checkpoint, actor);

    private bool CanFitCareGroup(string householdId, string actor)
    {
        if (HouseForHousehold(householdId) is not { } house) return false;
        var definition = BuildingStorageRules.EffectiveDefinition(
            worldContent.Buildings.Single(item => item.CanonicalId == house.DefinitionId), house);
        var group = MovingCareGroup(actor).ToHashSet(StringComparer.Ordinal);
        var residents = society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            (person.HouseholdId == householdId || group.Contains(person.Id)));
        return !HouseResidentCapacityRules.Calculate(residents, definition.Width, definition.Height).IsOvercrowded;
    }

    /// <summary>The displacement caller must decide eligibility and notice; this performs the same authoritative exit as a voluntary move.</summary>
    public bool DisplaceAdult(string actor)
    {
        gate.Wait();
        try { return inhabitants.ContainsKey(actor) && MovingCareGroup(actor).Count == 1 && DepartHousehold(actor, "displaced"); }
        finally { gate.Release(); }
    }

    private bool DepartHousehold(string actor, string cause)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            return false;
        var group = MovingCareGroup(actor);
        if (group.Any(id => society.Checkpoint.GetInhabitant(id).HouseholdId != householdId)) return false;
        var allowanceIds = new List<string>();
        var allowance = 0;
        // Ownership changes now, location does not. Collection remains a separate physical action.
        var nextInventory = society.Checkpoint.Inventory;
        foreach (var lot in nextInventory.Lots.Where(lot => lot.OwnerId == householdId && lot.CarrierId is null &&
                     IsEdibleFood(lot.ItemKind)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray())
        {
            var available = PersonalEquipmentRules.AvailableQuantity(nextInventory, lot);
            var quantity = Math.Min(2 - allowance, available);
            if (quantity <= 0) continue;
            var transferId = $"departure:{actor}:{WorldTick}:{inhabitants[actor].Departures?.Count ?? 0}:{lot.Id}";
            InventoryGroundPosition? ground = lot.StorageBuildingId is null
                ? lot.GroundPosition ?? new InventoryGroundPosition(SettlementStoragePosition.X, SettlementStoragePosition.Y)
                : null;
            nextInventory = InventoryFixture.Transfer(nextInventory, transferId, householdId, actor,
                lot.Id, quantity, "departure_allowance", lot.StorageBuildingId, destinationGroundPosition: ground);
            allowanceIds.Add(quantity == lot.Quantity ? lot.Id : lot.Id + "#transfer:" + transferId);
            allowance += quantity;
            if (allowance == 2) break;
        }
        var result = society.Apply(checkpoint => SocietyFixture.LeaveHousehold(checkpoint, actor));
        if (result.CreatedId is null) return false;
        ApplyInventoryTransition(_ => nextInventory);
        foreach (var id in group)
        {
            var person = inhabitants[id];
            inhabitants[id] = person with
            {
                Project = person.Project is { } project ? project with { Stage = "paused", Blocker = "Left the owning household" } : null,
                Housing = new(Blocker: id == actor ? HousingBlockers.NoHousehold : HousingBlockers.NoAuthorizedHome),
                LastDecisionContext = null,
            };
        }
        inhabitants[actor] = inhabitants[actor] with
        {
            Departures = (inhabitants[actor].Departures ?? []).Append(
                new SettlementDeparture(householdId, WorldTick, cause, group, allowanceIds, allowance)).ToArray(),
        };
        // A carried delivery is borrowed household stock, never a personal windfall on leaving.
        foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                     lot.DeliveryBuildingId is { } delivery && worldSimulation.Buildings.Any(building =>
                         building.InstanceId == delivery && building.HouseholdId == householdId)).ToArray())
        {
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"depart-delivery:{actor}:{WorldTick}:{lot.Id}", actor, householdId, lot.Id, lot.Quantity,
                "delivery_ownership_restored"));
            ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
                $"depart-custody:{actor}:{WorldTick}:{lot.Id}", lot.Id, householdId, lot.Quantity, actor));
        }
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("household_left", $"{actor}|{householdId}|{cause}|{allowance}");
        return true;
    }

    private IEnumerable<InventoryLot> PersonalGoodsAwaitingCollection(string actor) => society.Checkpoint.Inventory.Lots.Where(lot =>
        lot.OwnerId == actor && !PersonalEquipmentRules.IsCarried(lot, actor) && lot.CarrierId is null &&
        lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
        (lot.GroundPosition is not null || lot.StorageBuildingId is { } storageId &&
            worldSimulation.Buildings.Any(building => building.InstanceId == storageId && building.HouseholdId is { } home &&
                (society.Checkpoint.GetInhabitant(actor).HouseholdId == home ||
                 inhabitants[actor].Departures?.Any(departure => departure.HouseholdId == home) == true))));

    private IEnumerable<InventoryLot> BorrowedGoods(string actor) => society.Checkpoint.Inventory.Lots.Where(lot =>
        lot.OwnerId != actor && lot.CarrierId == actor && lot.Quantity > 0);

    private void AddDepartureCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor)) return;
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not null)
            candidates.Add(new("household_leave", "Leave your household without a vote; keep responsibility for your dependent children and collect your personal belongings physically.", 115));
        else if (inhabitants[actor].Housing?.Request is null && !AskableHouseholds(actor).Any())
            candidates.Add(new("household_found", "Start your own household with your dependent children. A House still needs a legal site, materials and work.", 19));
        if (FreeCarryCapacity(actor) > 0)
        {
            foreach (var lot in PersonalGoodsAwaitingCollection(actor).OrderBy(lot => lot.Id, StringComparer.Ordinal))
                candidates.Add(new("household_collect:" + lot.Id, $"Physically collect your own {lot.ItemKind.Replace('_', ' ')}; other household stock remains private.", 20));
        }
        foreach (var lot in BorrowedGoods(actor).OrderBy(lot => lot.Id, StringComparer.Ordinal))
            if (HouseForHousehold(lot.OwnerId) is not null)
                candidates.Add(new("household_return:" + lot.Id, $"Physically return borrowed {lot.ItemKind.Replace('_', ' ')} to its owning household.", 22));
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is { } home && HouseForHousehold(home) is { } house &&
            StorageRoom(house.InstanceId) > 0)
        {
            foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                         PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
                         lot.Id != inhabitants[actor].Equipment?.ClothingLotId && lot.Id != inhabitants[actor].Equipment?.CarryAidLotId &&
                         !IsEdibleFood(lot.ItemKind) && AvailableLotQuantity(lot) > 0).OrderBy(lot => lot.Id, StringComparer.Ordinal))
                candidates.Add(new("household_store_personal:" + lot.Id, $"Store your own {lot.ItemKind.Replace('_', ' ')} in your House while keeping personal ownership.", 95));
        }
        foreach (var child in society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
                     person.AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
                     person.HouseholdId is not null && person.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId &&
                     person.PrimaryCaregiverId != actor))
            candidates.Add(new("household_accept_care:" + child.Id, $"Explicitly accept primary care of {child.Name}, so their current caregiver may leave without taking them.", 112));
    }

    private void ApplyDepartureCandidate(string actor, string candidate)
    {
        if (candidate == "household_leave") { DepartHousehold(actor, "voluntary"); return; }
        if (candidate == "household_found")
        {
            if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not null ||
                inhabitants[actor].Housing?.Request is not null || AskableHouseholds(actor).Any()) return;
            var householdId = $"household:solo:{actor}:{WorldTick}";
            var group = MovingCareGroup(actor);
            society.Apply(checkpoint => SocietyFixture.CreateHousehold(checkpoint, householdId,
                society.Checkpoint.GetInhabitant(actor).Name + "'s household", group));
            foreach (var id in group)
                inhabitants[id] = inhabitants[id] with { Housing = new(Blocker: HousingBlocker(id)), LastDecisionContext = null };
            AppendEvent("household_founded", $"{actor}|{householdId}");
            return;
        }
        if (candidate.StartsWith("household_accept_care:", StringComparison.Ordinal))
        {
            var child = candidate["household_accept_care:".Length..];
            var result = society.Apply(checkpoint => SocietyFixture.AcceptReplacementCare(checkpoint, actor, child));
            if (result.CreatedId is not null) AppendEvent("replacement_care_accepted", $"{actor}|{child}");
            return;
        }
        var collect = candidate.StartsWith("household_collect:", StringComparison.Ordinal);
        var store = candidate.StartsWith("household_store_personal:", StringComparison.Ordinal);
        var returnBorrowed = candidate.StartsWith("household_return:", StringComparison.Ordinal);
        if (!collect && !store && !returnBorrowed) return;
        var lotId = candidate[(collect ? "household_collect:".Length : store ? "household_store_personal:".Length : "household_return:".Length)..];
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (lot is null) return;
        if (collect && !PersonalGoodsAwaitingCollection(actor).Any(item => item.Id == lotId)) return;
        if (returnBorrowed && !BorrowedGoods(actor).Any(item => item.Id == lotId)) return;
        if (store && (lot.OwnerId != actor || !PersonalEquipmentRules.IsCarried(lot, actor))) return;
        var house = collect ? null : HouseForHousehold(returnBorrowed ? lot.OwnerId : HouseholdFor(actor));
        if (!collect && house is null) return;
        var destination = collect ? HouseholdStockPosition(lot) : house!.Position;
        // Collection/return at an entrance grants no general shelter or cooking access.
        var range = collect && lot.GroundPosition is not null ? ResourceInteractionRange : 1;
        if (!IsWithinInteractionRange(inhabitants[actor].Position, destination, range))
        {
            MoveToward(actor, inhabitants[actor], destination, "personal_goods", range);
            return;
        }
        var quantity = Math.Min(AvailableLotQuantity(lot), collect ? FreeCarryCapacity(actor) : StorageRoom(house!.InstanceId));
        if (quantity <= 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
            $"personal:{actor}:{WorldTick}:{lot.Id}", lot.Id, lot.OwnerId, quantity,
            collect ? actor : null, collect ? null : house!.InstanceId));
        AppendEvent(collect ? "personal_goods_collected" : returnBorrowed ? "borrowed_goods_returned" : "personal_goods_stored",
            $"{actor}|{lot.ItemKind}|{quantity}|{lot.OwnerId}");
    }

    private static void ValidateDepartures(IEnumerable<PlaytestInhabitantState> physical, SocietyCheckpoint society, int schemaVersion)
    {
        var people = society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var households = society.Households.Select(home => home.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var person in physical)
        {
            if (person.Departures is not { } departures) continue;
            if (schemaVersion < 39 || departures.Count == 0 || departures.Any(item => item is null ||
                !households.Contains(item.HouseholdId) || item.Tick < 0 || item.Tick > society.WorldTick ||
                item.Cause is not ("voluntary" or "displaced") || item.AllowancePortions is < 0 or > 2 ||
                item.CareGroup is null || !item.CareGroup.Contains(person.InhabitantId, StringComparer.Ordinal) ||
                item.CareGroup.Distinct(StringComparer.Ordinal).Count() != item.CareGroup.Count ||
                item.CareGroup.Any(id => !people.Contains(id)) || item.AllowanceLotIds is null ||
                item.AllowanceLotIds.Count > item.AllowancePortions ||
                item.AllowanceLotIds.Distinct(StringComparer.Ordinal).Count() != item.AllowanceLotIds.Count ||
                item.AllowanceLotIds.Any(string.IsNullOrWhiteSpace)))
                throw new InvalidDataException("The saved household departure record is invalid.");
        }
    }

    private string DepartureNote(string actor)
    {
        var goods = PersonalGoodsAwaitingCollection(actor).Sum(lot => lot.Quantity);
        var borrowed = BorrowedGoods(actor).Sum(lot => lot.Quantity);
        var children = MovingCareGroup(actor).Count(id => id != actor);
        return $"Personal goods awaiting collection: {goods} units; borrowed goods to return: {borrowed} units. " +
            (children > 0 ? $"Primary care: {children} dependents; they move with you unless another adult explicitly accepts care." : "No dependent care group.");
    }

    private void MaintainMovingCareGroups()
    {
        foreach (var adult in inhabitants.Values.Where(person => person.Departures is { Count: > 0 }).ToArray())
        {
            foreach (var childId in MovingCareGroup(adult.InhabitantId).Where(id => id != adult.InhabitantId))
            {
                if (!inhabitants.TryGetValue(childId, out var child) ||
                    IsWithinInteractionRange(child.Position, adult.Position, 1)) continue;
                MoveToward(childId, child, adult.Position, "follow_caregiver", 1);
            }
        }
    }
}
