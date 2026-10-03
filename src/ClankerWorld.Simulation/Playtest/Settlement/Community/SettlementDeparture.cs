using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A completed membership exit, including the once-only allowance and limited collection right.</summary>
public sealed record SettlementDeparture(string HouseholdId, long Tick, string Cause,
    IReadOnlyList<string> CareGroup, IReadOnlyList<string> AllowanceLotIds, int AllowancePortions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettlementProject? SharedProject = null);

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
        try { return inhabitants.ContainsKey(actor) && !HasDependentInPrimaryCare(actor) && DepartHousehold(actor, "displaced"); }
        finally { gate.Release(); }
    }

    private bool DepartHousehold(string actor, string cause)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            return false;
        if (cause == "displaced" && (HasDependentInPrimaryCare(actor) ||
            HoldsRelocationNotice(actor) && !MayRelocateFromHousehold(actor)))
            return false;
        var group = MovingCareGroup(actor);
        var sharedProject = inhabitants[actor].Project;
        if (group.Any(id => society.Checkpoint.GetInhabitant(id).HouseholdId != householdId)) return false;
        var allowanceIds = new List<string>();
        var allowance = 0;
        // Ownership changes now, location does not. Collection remains a separate physical action.
        var nextInventory = society.Checkpoint.Inventory;
        // Food sealed in a storage pot stays with the household; the allowance takes loose portions.
        foreach (var lot in nextInventory.Lots.Where(lot => lot.OwnerId == householdId && lot.CarrierId is null &&
                     lot.ContainerLotId is null && IsEdibleFood(lot.ItemKind)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray())
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
                Project = null,
                // A move-out notice ends with membership; a pending request elsewhere stays open.
                Housing = id == actor
                    ? (person.Housing ?? new()) with { Blocker = HousingBlockers.NoHousehold, Relocation = null }
                    : new(Blocker: HousingBlockers.NoAuthorizedHome),
                LastDecisionContext = null,
            };
        }
        inhabitants[actor] = inhabitants[actor] with
        {
            Departures = (inhabitants[actor].Departures ?? []).Append(
                new SettlementDeparture(householdId, WorldTick, cause, group, allowanceIds, allowance, sharedProject)).ToArray(),
        };
        var ownedBuildings = worldSimulation.Buildings.Where(building => building.HouseholdId == householdId)
            .Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
        // Work for the leaver's own goods, such as their handcart, is not household work
        // anyone else may finish: it stops, and its reserved materials stay with the leaver.
        var personalJobs = worldSimulation.ProductionJobs.Where(job => job.WorkerId == actor && job.OwnerId == actor &&
            ownedBuildings.Contains(job.BuildingInstanceId) && job.State == WorldProductionJobState.Running).ToArray();
        foreach (var job in personalJobs)
            ApplyInventoryTransition(inventory =>
            {
                foreach (var id in job.InputReservationIds)
                    if (inventory.Reservations.SingleOrDefault(item => item.Id == id) is { State: InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed })
                        inventory = InventoryFixture.ReleaseReservation(inventory, id, "personal_work_stopped_on_leaving");
                return inventory;
            });
        var personalJobIds = personalJobs.Select(job => job.JobId).ToHashSet(StringComparer.Ordinal);
        worldSimulation = worldSimulation with
        {
            ProductionJobs = worldSimulation.ProductionJobs.Select(job => personalJobIds.Contains(job.JobId)
                ? job with { State = WorldProductionJobState.Cancelled }
                : job.WorkerId == actor && ownedBuildings.Contains(job.BuildingInstanceId) && job.State == WorldProductionJobState.Running
                ? job with { State = WorldProductionJobState.Paused, PausedAtTick = WorldTick } : job).ToArray(),
            BuildingExpansions = worldSimulation.BuildingExpansions?.Select(job => job.WorkerId == actor &&
                job.OwnerId == householdId && job.State == WorldProductionJobState.Running
                ? job with { State = WorldProductionJobState.Paused, PausedAtTick = WorldTick } : job).ToArray(),
        };
        var heldReservations = worldSimulation.ProductionJobs.Where(job => job.WorkerId == actor &&
                ownedBuildings.Contains(job.BuildingInstanceId) && job.State == WorldProductionJobState.Paused)
            .SelectMany(job => job.InputReservationIds)
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.WorkerId == actor &&
                job.OwnerId == householdId && job.State == WorldProductionJobState.Paused).SelectMany(job => job.InputReservationIds))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (heldReservations.Length > 0)
            ApplyInventoryTransition(inventory => InventoryFixture.HoldReservations(inventory, heldReservations));
        // A carried delivery is borrowed household stock, never a personal windfall on leaving.
        foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                     lot.ContainerLotId is null && lot.DeliveryBuildingId is { } delivery && worldSimulation.Buildings.Any(building =>
                         building.InstanceId == delivery && building.HouseholdId == householdId)).ToArray())
        {
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"depart-delivery:{actor}:{WorldTick}:{lot.Id}", actor, householdId, lot.Id, lot.Quantity,
                "delivery_ownership_restored"));
            ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
                $"depart-custody:{actor}:{WorldTick}:{lot.Id}", lot.Id, householdId, lot.Quantity, actor));
        }
        foreach (var job in personalJobs) AppendEvent("recipe_cancelled", $"{job.JobId}:{job.RecipeId}");
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("household_left", $"{actor}|{householdId}|{cause}|{allowance}");
        return true;
    }

    private int PhysicalUnreservedQuantity(InventoryLot lot) => Math.Max(0, lot.Quantity -
        society.Checkpoint.Inventory.Reservations.Where(item => item.LotId == lot.Id && item.State is
            InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed)
            .Sum(item => item.Quantity));

    private IEnumerable<InventoryLot> PersonalGoodsAwaitingCollection(string actor) => society.Checkpoint.Inventory.Lots.Where(lot =>
        // A parked handcart stays on the ground with its cargo; its owner pulls it rather than carrying it.
        lot.OwnerId == actor && !PersonalEquipmentRules.IsCarried(lot, actor) && lot.CarrierId is null &&
        lot.ItemKind != InventoryContainerRules.Handcart &&
        lot.DeliveryBuildingId is null && lot.ContainerLotId is null && PhysicalUnreservedQuantity(lot) > 0 &&
        !(InventoryContainerRules.IsContainer(lot.ItemKind) && HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id)) &&
        (lot.GroundPosition is not null || lot.StorageBuildingId is { } storageId &&
            worldSimulation.Buildings.Any(building => building.InstanceId == storageId && building.HouseholdId is { } home &&
                (society.Checkpoint.GetInhabitant(actor).HouseholdId == home ||
                 inhabitants[actor].Departures?.Any(departure => departure.HouseholdId == home) == true))));

    /// <summary>A vessel moves with its contents, so it needs room for all of them and nothing reserved.</summary>
    private bool VesselFits(InventoryLot lot, int room) => !InventoryContainerRules.IsContainer(lot.ItemKind) ||
        !HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id) &&
        room >= ContainerFamilyQuantity(society.Checkpoint.Inventory, lot.Id);

    private IEnumerable<InventoryLot> BorrowedGoods(string actor) => society.Checkpoint.Inventory.Lots.Where(lot =>
        lot.OwnerId != actor && lot.CarrierId == actor && lot.ContainerLotId is null && lot.Quantity > 0);

    private IEnumerable<(string Id, string BuildingId, string Worker, long Completion, long PausedAt, IReadOnlyList<string> Reservations)> PausedHouseholdWork(string actor)
    {
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household is null) yield break;
        foreach (var job in worldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Paused &&
                     worldSimulation.Buildings.Any(building => building.InstanceId == job.BuildingInstanceId && building.HouseholdId == household)))
            yield return (job.JobId, job.BuildingInstanceId, job.WorkerId, job.CompletionTick, job.PausedAtTick!.Value, job.InputReservationIds);
        foreach (var job in (worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Paused && job.OwnerId == household))
            yield return (job.JobId, job.BuildingInstanceId, job.WorkerId, job.CompletionTick, job.PausedAtTick!.Value, job.InputReservationIds);
    }

    private bool CanResumeHeldInputs(string actor, string buildingId, IReadOnlyList<string> reservationIds)
    {
        var building = worldSimulation.Buildings.Single(item => item.InstanceId == buildingId);
        var site = new InventoryGroundPosition(building.Position.X, building.Position.Y);
        return reservationIds.All(id =>
        {
            var reservation = society.Checkpoint.Inventory.GetReservation(id);
            var lot = society.Checkpoint.Inventory.GetLot(reservation.LotId);
            return reservation.State == InventoryReservationState.Reserved && lot.Quantity >= reservation.Quantity &&
                lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 &&
                (lot.OwnerId == building.HouseholdId || lot.OwnerId == actor) &&
                (lot.StorageBuildingId == buildingId || lot.GroundPosition == site ||
                 PersonalEquipmentRules.IsPhysicallyCarried(society.Checkpoint.Inventory, lot, actor));
        });
    }

    /// <summary>
    /// A knife-assisted recipe resumes only with a knife the resuming member
    /// carries: the departed worker's own knife, or this member's best one.
    /// </summary>
    private bool TryResumeKnife(string actor, string jobId, out string? knifeId)
    {
        knifeId = worldSimulation.ProductionJobs.SingleOrDefault(item => item.JobId == jobId)?.ToolLotId;
        if (knifeId is null) return true;
        var inventory = society.Checkpoint.Inventory;
        if (ToolProgressionRules.PlanWorkForLot(inventory, actor, ToolFamily.Knife, knifeId) is not null) return true;
        knifeId = ToolProgressionRules.BestUsableTool(inventory, actor, ToolFamily.Knife)?.Id;
        return knifeId is not null;
    }

    private void ResumePausedHouseholdWork(string actor, string jobId)
    {
        var matches = PausedHouseholdWork(actor).Where(job => job.Id == jobId).ToArray();
        if (matches.Length != 1) return;
        var job = matches[0];
        if (!CanResumeHeldInputs(actor, job.BuildingId, job.Reservations) || !TryResumeKnife(actor, jobId, out var knifeId)) return;
        var building = worldSimulation.Buildings.Single(item => item.InstanceId == job.BuildingId);
        if (inhabitants[actor].Position != building.Position)
        {
            MoveToward(actor, inhabitants[actor], building.Position, "resume_household_work");
            return;
        }
        var expansion = (worldSimulation.BuildingExpansions ?? []).SingleOrDefault(item => item.JobId == jobId);
        if (expansion is not null && !CanFitExpansion(building, expansion.TargetPosition, expansion.TargetFootprint, out _, expansion.JobId)) return;
        var deadline = checked(WorldTick + Math.Max(1, job.Completion - job.PausedAt));
        ApplyInventoryTransition(inventory => InventoryFixture.SetReservationDeadline(inventory, job.Reservations, deadline));
        worldSimulation = worldSimulation with
        {
            ProductionJobs = worldSimulation.ProductionJobs.Select(item => item.JobId == jobId
                ? item with { WorkerId = actor, State = WorldProductionJobState.Running, CompletionTick = deadline, PausedAtTick = null, ToolLotId = knifeId } : item).ToArray(),
            BuildingExpansions = worldSimulation.BuildingExpansions?.Select(item => item.JobId == jobId
                ? item with { WorkerId = actor, State = WorldProductionJobState.Running, CompletionTick = deadline, PausedAtTick = null } : item).ToArray(),
        };
        AppendEvent("household_work_resumed", $"{actor}|{jobId}|{building.HouseholdId}");
    }

    private void ReconcilePausedHouseholdWork()
    {
        bool Available(IReadOnlyList<string> ids) => ids.All(id =>
        {
            var reservation = society.Checkpoint.Inventory.Reservations.SingleOrDefault(item => item.Id == id);
            var lot = reservation is null ? null : society.Checkpoint.Inventory.Lots.SingleOrDefault(item => item.Id == reservation.LotId);
            return reservation is { State: InventoryReservationState.Reserved } && lot is { ConditionBasisPoints: > 0, FreshnessBasisPoints: > 0 } &&
                reservation.OwnerId == lot.OwnerId && lot.Quantity >= reservation.Quantity;
        });
        var cancelledProduction = worldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Paused &&
            !Available(job.InputReservationIds)).ToArray();
        var cancelledExpansions = (worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Paused &&
            (!Available(job.InputReservationIds) || job.InputReservationIds.Any(id =>
                society.Checkpoint.Inventory.Reservations.SingleOrDefault(item => item.Id == id) is { } reservation &&
                reservation.OwnerId != job.OwnerId && reservation.OwnerId != job.WorkerId))).ToArray();
        foreach (var ids in cancelledProduction.Select(job => job.InputReservationIds).Concat(cancelledExpansions.Select(job => job.InputReservationIds)))
            ApplyInventoryTransition(inventory =>
            {
                foreach (var id in ids)
                    if (inventory.Reservations.SingleOrDefault(item => item.Id == id) is { State: InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed })
                        inventory = InventoryFixture.ReleaseReservation(inventory, id, "paused_work_materials_unavailable");
                return inventory;
            });
        var productionIds = cancelledProduction.Select(job => job.JobId).ToHashSet(StringComparer.Ordinal);
        var expansionIds = cancelledExpansions.Select(job => job.JobId).ToHashSet(StringComparer.Ordinal);
        worldSimulation = worldSimulation with
        {
            ProductionJobs = worldSimulation.ProductionJobs.Select(job => productionIds.Contains(job.JobId)
                ? job with { State = WorldProductionJobState.Cancelled } : job).ToArray(),
            BuildingExpansions = worldSimulation.BuildingExpansions?.Select(job => expansionIds.Contains(job.JobId)
                ? job with { State = WorldProductionJobState.Cancelled, Failure = "Paused expansion materials are no longer available." } : job).ToArray(),
        };
        foreach (var job in cancelledProduction) AppendEvent("recipe_cancelled", $"{job.JobId}:{job.RecipeId}");
        foreach (var job in cancelledExpansions) AppendEvent("building_expansion_cancelled", $"{job.BuildingInstanceId}:{job.JobId}:Paused expansion materials are no longer available.");
    }

    /// <summary>Maps and field records are the belongings the built-in chooser stores at home and leaves there.</summary>
    private static bool KeptAtHomeByRoutine(string itemKind) => itemKind is "field_map" or "field_record";

    private void AddDepartureCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor)) return;
        foreach (var job in PausedHouseholdWork(actor))
            if (CanResumeHeldInputs(actor, job.BuildingId, job.Reservations) && TryResumeKnife(actor, job.Id, out _))
                candidates.Add(new("household_resume_work:" + job.Id,
                    "Go to your household's building and take over its paused work using the same committed materials.", 17, job.BuildingId));
        var home = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (home is not null)
            candidates.Add(new("household_leave", "Leave your household without a vote; keep responsibility for your dependent children and collect your personal belongings physically.", 115));
        // Built-in rules never give up a home early: the notice period is time to find room
        // or finish an expansion, and the deadline moves the adult out when neither happens.
        if (inhabitants[actor].Housing?.Request is null && MayFoundHousehold(actor) && !AskableHouseholds(actor).Any())
            candidates.Add(new("household_found", home is null
                    ? "Start your own household with your dependent children. A House still needs a legal site, materials and work."
                    : "Move out of your overcrowded House and start your own household with your dependent children. A House still needs a legal site, materials and work.",
                home is null ? 19 : 104));
        if (FreeCarryCapacity(actor) > 0)
        {
            foreach (var lot in PersonalGoodsAwaitingCollection(actor).Where(lot => VesselFits(lot, FreeCarryCapacity(actor)))
                         .OrderBy(lot => lot.Id, StringComparer.Ordinal))
            {
                var knowledgeAtHome = KeptAtHomeByRoutine(lot.ItemKind) &&
                    lot.StorageBuildingId is { } storageId &&
                    worldSimulation.Buildings.Any(building => building.InstanceId == storageId &&
                        building.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId);
                // Knowledge safely stored at home is not a recovery errand.
                // Keep collection available to a deliberate choice without
                // making the built-in chooser undo its own storage next tick.
                candidates.Add(new("household_collect:" + lot.Id,
                    $"Physically collect your own {lot.ItemKind.Replace('_', ' ')}; other household stock remains private.",
                    knowledgeAtHome ? 110 : 20));
            }
        }
        foreach (var lot in BorrowedGoods(actor).OrderBy(lot => lot.Id, StringComparer.Ordinal))
            if (lot.OwnerId != society.Checkpoint.GetInhabitant(actor).HouseholdId && HouseForHousehold(lot.OwnerId) is { } ownerHouse &&
                VesselFits(lot, StorageRoom(ownerHouse.InstanceId)))
                candidates.Add(new("household_return:" + lot.Id, $"Physically return borrowed {lot.ItemKind.Replace('_', ' ')} to its owning household.", 22));
        if (home is not null && HouseForHousehold(home) is { } house && StorageRoom(house.InstanceId) > 0)
        {
            foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                         PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
                         // Worn clothing, the carry aid, a worn ornament and a tool under repair stay with the adult.
                         !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id) &&
                         !IsEdibleFood(lot.ItemKind) && PhysicalUnreservedQuantity(lot) > 0 &&
                         VesselFits(lot, StorageRoom(house.InstanceId))).OrderBy(lot => lot.Id, StringComparer.Ordinal))
                candidates.Add(new("household_store_personal:" + lot.Id, $"Store your own {lot.ItemKind.Replace('_', ' ')} in your House while keeping personal ownership.",
                    // The built-in chooser collects other stored belongings at once,
                    // so it puts away only what it then leaves at home.
                    KeptAtHomeByRoutine(lot.ItemKind) ? 95 : 110));
        }
        foreach (var child in society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
                     person.AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
                     person.HouseholdId is not null && person.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId &&
                     person.PrimaryCaregiverId != actor &&
                     // A child with no living caregiver is placed through the guardian search instead.
                     SocietyFixture.HasActivePrimaryCaregiver(society.Checkpoint, person.Id)))
            candidates.Add(new("household_accept_care:" + child.Id, $"Explicitly accept primary care of {child.Name}, so their current caregiver may leave without taking them.", 112));
    }

    /// <summary>
    /// An adult with no household may start one. So may a member of an
    /// overcrowded House who has a move-out notice, or whose House cannot grow
    /// any further even with the Council's land permission, taking their
    /// dependents with them.
    /// </summary>
    private bool MayFoundHousehold(string actor)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } home) return true;
        if (MayRelocateFromHousehold(actor)) return true;
        return HouseResidentCapacity(home) is { IsOvercrowded: true } && HouseForHousehold(home) is { } house &&
            !HouseCanExpandFurther(house);
    }

    /// <summary>
    /// A House can still grow while an expansion is under way, or when a larger
    /// footprint fits and the household already has the use of its extra land
    /// or could still get it by asking the Council.
    /// </summary>
    private bool HouseCanExpandFurther(PlacedBuilding house) =>
        HouseExpansionUnderWay(house) || HouseCanExpandNow(house) || HouseCanExpandWithLandPermission(house);

    private bool HouseExpansionUnderWay(PlacedBuilding house) => (worldSimulation.BuildingExpansions ?? []).Any(job =>
        job.BuildingInstanceId == house.InstanceId &&
        job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused);

    private bool HouseCanExpandNow(PlacedBuilding house) =>
        ExpansionShapes(house).Any(shape => CanFitExpansion(house, shape.Position, shape.Footprint, out _));

    /// <summary>
    /// A larger footprint fits, and every extra tile the household may not use
    /// yet is the Town's own land that no other household holds or has asked for.
    /// </summary>
    private bool HouseCanExpandWithLandPermission(PlacedBuilding house)
    {
        if (house.TownId is not { } townId || house.HouseholdId is not { } household) return false;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var original = WorldContentSimulationRules.Footprint(definition, house).ToHashSet();
        var heldByOthers = HouseholdLandHeldByOthers(household);
        return ExpansionShapes(house).Any(shape =>
            CanFitExpansion(house, shape.Position, shape.Footprint, out _, requireLandRights: false) &&
            WorldContentSimulationRules.Footprint(
                    BuildingStorageRules.WithSize(definition, shape.Footprint.Width, shape.Footprint.Height), shape.Position)
                .Where(tile => !original.Contains(tile) && !MayExpandOntoLand(house, tile))
                .All(tile => TownLandRightsRules.IsCoveredByTownTitle(tile, townId, townLandTitles) &&
                    !heldByOthers.Contains(tile)));
    }

    private void ApplyDepartureCandidate(string actor, string candidate)
    {
        if (candidate.StartsWith("household_resume_work:", StringComparison.Ordinal))
        {
            ResumePausedHouseholdWork(actor, candidate["household_resume_work:".Length..]);
            return;
        }
        if (candidate == "household_leave") { DepartHousehold(actor, MayRelocateFromHousehold(actor) ? "displaced" : "voluntary"); return; }
        if (candidate == "household_found")
        {
            if (!AdultResident(actor) || !MayFoundHousehold(actor) ||
                inhabitants[actor].Housing?.Request is not null || AskableHouseholds(actor).Any()) return;
            // Splitting from an overcrowded House is an ordinary departure first.
            if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not null &&
                !DepartHousehold(actor, MayRelocateFromHousehold(actor) ? "displaced" : "voluntary")) return;
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
        if (store && (lot.OwnerId != actor || lot.ContainerLotId is not null || !PersonalEquipmentRules.IsCarried(lot, actor) ||
                      PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id))) return;
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
        var room = collect ? FreeCarryCapacity(actor) : StorageRoom(house!.InstanceId);
        var quantity = InventoryContainerRules.IsContainer(lot.ItemKind)
            ? VesselFits(lot, room) ? 1 : 0
            : Math.Min(PhysicalUnreservedQuantity(lot), room);
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
            foreach (var departure in departures)
                if (departure?.SharedProject is { } plan) ValidateProject(plan, society.WorldTick);
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
        var paused = PausedHouseholdWork(actor).ToArray();
        var blocked = paused.Count(job => !CanResumeHeldInputs(actor, job.BuildingId, job.Reservations) ||
            !TryResumeKnife(actor, job.Id, out _));
        return $"Personal collection: {goods} units; borrowed returns: {borrowed} units. Paused household work: {paused.Length}; {blocked} blocked by unavailable or privately held inputs or a missing knife. " +
            (children > 0 ? $"Primary care: {children} dependents move with you unless care is explicitly accepted." : "No dependent care group.");
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
