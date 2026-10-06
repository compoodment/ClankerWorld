using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record ExpansionOrderPlan(PlacedBuilding Building, GridPoint Position,
        BuildingFootprintRevision Footprint);

    private PlacedBuilding? ExpansionOrderBuilding(OwnerQueuedInstruction instruction)
    {
        if (instruction.Order is not { Action: "expand_building", ExpansionBinding: { } binding } order ||
            !AdultResident(instruction.TargetInhabitantId)) return null;
        var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == binding.BuildingInstanceId);
        if (building is null || building.DefinitionId != binding.DefinitionId || building.Position != binding.ExpectedPosition ||
            (building.Footprint?.Revision ?? 0) != binding.ExpectedRevision || ExpansionOwner(building) != binding.OwnerId ||
            order.TargetPosition is { } requested && requested != binding.ExpectedPosition ||
            PrivateWorldBuildingOrderCatalog.Find(worldContent, building.DefinitionId)?.BuildingKind != order.TargetBuildingKind)
            return null;
        return order.TargetBuildingKind == "house"
            ? HouseholdFor(instruction.TargetInhabitantId) == building.HouseholdId ? building : null
            : TownForResident(instruction.TargetInhabitantId) == building.TownId ? building : null;
    }

    private static bool ExpansionJobMatchesOrder(BuildingExpansionJob job, OwnerQueuedInstruction instruction) =>
        instruction.Order is { Action: "expand_building", ExpansionBinding: { } binding } &&
        job.OrderInstructionId == instruction.InstructionId && job.WorkerId == instruction.TargetInhabitantId &&
        job.BuildingInstanceId == binding.BuildingInstanceId && job.DefinitionId == binding.DefinitionId &&
        job.OwnerId == binding.OwnerId && job.ExpectedPosition == binding.ExpectedPosition &&
        job.ExpectedRevision == binding.ExpectedRevision && job.TargetPosition == binding.TargetPosition &&
        job.TargetFootprint == binding.TargetFootprint && job.StartedTick >= instruction.SubmittedTick;

    private BuildingExpansionJob? BoundExpansionJob(OwnerQueuedInstruction instruction) =>
        instruction.Order?.ExpansionBinding?.JobId is { } id
            ? (worldSimulation.BuildingExpansions ?? []).SingleOrDefault(job => job.JobId == id &&
                ExpansionJobMatchesOrder(job, instruction)) : null;

    private (GridPoint Position, BuildingFootprintRevision Footprint) ExpansionShapeForOrder(
        PlacedBuilding building, OwnerQueuedInstruction? instruction) =>
        ExpansionShapes(building).FirstOrDefault(shape =>
            (instruction?.Order?.ExpansionBinding is not { } binding ||
             shape.Position == binding.TargetPosition && shape.Footprint == binding.TargetFootprint) &&
            CanFitExpansion(building, shape.Position, shape.Footprint, out _));

    private ExpansionOrderPlan? ExpansionPlanFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor)) return null;
        var candidates = order.ExpansionBinding is null
            ? worldSimulation.Buildings.OrderBy(building => building.InstanceId, StringComparer.Ordinal)
            : worldSimulation.Buildings.Where(building => ExpansionOrderBuilding(instruction)?.InstanceId == building.InstanceId);
        foreach (var building in candidates)
        {
            if (PrivateWorldBuildingOrderCatalog.Find(worldContent, building.DefinitionId)?.BuildingKind != order.TargetBuildingKind ||
                order.TargetPosition is { } position && position != building.Position ||
                !MayExpandBuilding(actor, building, out _) ||
                person.Position != building.Position && FindUnoccupiedRoute(actor, person.Position, building.Position, 0).Count == 0)
                continue;
            var shape = ExpansionShapeForOrder(building, instruction);
            if (shape.Footprint is null) continue;
            var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            var costs = BuildingStorageRules.ExpansionCosts(definition, building, shape.Footprint);
            if (!CanAcquireProjectInputs(costs, HouseholdFor(actor), actor) ||
                !CanAcquireExpansionMaterials(actor, building, costs)) continue;
            return new(building, shape.Position, shape.Footprint);
        }
        return null;
    }

    private CognitionCandidate? ExpansionOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (!ReadyForBriefInteraction(instruction.TargetInhabitantId)) return null;
        if (BoundExpansionJob(instruction) is { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job)
            return ExpansionOrderBuilding(instruction) is { } building &&
                CanFitExpansion(building, job.TargetPosition, job.TargetFootprint, out _, job.JobId) &&
                ExpansionOrderInputsAvailable(job, building) &&
                (person.Position == building.Position || FindUnoccupiedRoute(person.InhabitantId, person.Position, building.Position, 0).Count > 0)
                ? new("expand_building", "Continue the ordered expansion using its committed materials.", 0, job.BuildingInstanceId) : null;
        return ExpansionPlanFor(instruction, person) is { } plan
            ? new("expand_building", "Bring the materials and complete the building's next permitted expansion.", 0, plan.Building.InstanceId) : null;
    }

    private void ExecuteExpansionOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        instruction = instructionsByIdempotency[instruction.IdempotencyKey];
        var actor = instruction.TargetInhabitantId;
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
        {
            PauseExpansionForOrder(instruction);
            SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
            return;
        }
        if (BoundExpansionJob(instruction) is { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job)
        {
            if (ExpansionOrderCandidateFor(instruction, person) is null || ExpansionOrderBuilding(instruction) is not { } building)
            {
                PauseExpansionForOrder(instruction);
                SetOrderStatus(instruction, "blocked", ExpansionOrderBlockedReason(instruction));
                return;
            }
            if (person.Position != building.Position)
            {
                PauseExpansionForOrder(instruction);
                MoveToward(actor, person, building.Position, "building_expansion", 0);
                return;
            }
            if (job.State == WorldProductionJobState.Paused)
            {
                var deadline = checked(WorldTick + job.CompletionTick - job.PausedAtTick!.Value);
                ApplyInventoryTransition(inventory => InventoryFixture.SetReservationDeadline(inventory, job.InputReservationIds, deadline));
                worldSimulation = worldSimulation with
                {
                    BuildingExpansions = worldSimulation.BuildingExpansions!.Select(item => item.JobId == job.JobId
                        ? item with { State = WorldProductionJobState.Running, CompletionTick = deadline, PausedAtTick = null } : item).ToArray(),
                };
                AppendEvent("expansion_order_resumed", $"{instruction.InstructionId}:{job.JobId}");
            }
            return;
        }
        if (ExpansionPlanFor(instruction, person) is not { } plan)
        {
            SetOrderStatus(instruction, "blocked", ExpansionOrderBlockedReason(instruction));
            return;
        }
        if (instruction.Order!.ExpansionBinding is null)
        {
            instruction = instruction with
            {
                Order = instruction.Order with
                {
                    ExpansionBinding = new(plan.Building.InstanceId, plan.Building.DefinitionId, ExpansionOwner(plan.Building),
                    plan.Building.Position, plan.Building.Footprint?.Revision ?? 0, plan.Position, plan.Footprint),
                }
            };
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction;
            checkpointSchemaVersion = StateSchemaVersion;
        }
        ApplyBuildingExpansionCandidate(actor, person, plan.Building.InstanceId, instruction);
    }

    private void BindStartedExpansionOrder(OwnerQueuedInstruction instruction, BuildingExpansionJob job)
    {
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        instructionsByIdempotency[instruction.IdempotencyKey] = current with
        {
            Order = current.Order! with { ExpansionBinding = current.Order!.ExpansionBinding! with { JobId = job.JobId } },
        };
    }

    private bool ExpansionOrderInputsAvailable(BuildingExpansionJob job, PlacedBuilding building)
    {
        var inventory = society.Checkpoint.Inventory;
        var site = ExpansionGroundPosition(building);
        return job.InputReservationIds.All(id =>
        {
            var reservation = inventory.Reservations.SingleOrDefault(item => item.Id == id);
            var lot = reservation is null ? null : inventory.Lots.SingleOrDefault(item => item.Id == reservation.LotId);
            return reservation is { State: InventoryReservationState.Reserved } &&
                lot is { ConditionBasisPoints: > 0, FreshnessBasisPoints: > 0 } && lot.Quantity >= reservation.Quantity &&
                reservation.OwnerId == lot.OwnerId && lot.DeliveryBuildingId is null &&
                (lot.OwnerId == job.OwnerId && (lot.StorageBuildingId == building.InstanceId || lot.GroundPosition == site) ||
                 lot.OwnerId == job.WorkerId && PersonalEquipmentRules.IsCarried(lot, job.WorkerId));
        });
    }

    private void MaintainExpansionOrdersBeforeTick()
    {
        foreach (var instruction in instructionsByIdempotency.Values.Where(item =>
                     item.Order is { Action: "expand_building" } order && IsActiveOrder(order.Status)).ToArray())
        {
            if (BoundExpansionJob(instruction) is not { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job) continue;
            if (!inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) ||
                ExpansionOrderBuilding(instruction) is not { } building || !ExpansionOrderInputsAvailable(job, building))
            {
                CancelExpansionForOrder(instruction);
                SetOrderStatus(instruction, "blocked", "The selected building, worker or committed materials are no longer available.");
                continue;
            }
            if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
            {
                PauseExpansionForOrder(instruction);
                SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
            }
            else if (person.Position != building.Position || IsConversationBusy(person.InhabitantId) ||
                     instruction.Order!.WaitForDecisionAfterFailure)
                PauseExpansionForOrder(instruction);
        }
    }

    private void PauseExpansionForOrder(OwnerQueuedInstruction instruction)
    {
        if (BoundExpansionJob(instruction) is not { State: WorldProductionJobState.Running } job) return;
        ApplyInventoryTransition(inventory => InventoryFixture.HoldReservations(inventory, job.InputReservationIds));
        worldSimulation = worldSimulation with
        {
            BuildingExpansions = worldSimulation.BuildingExpansions!.Select(item => item.JobId == job.JobId
                ? item with { State = WorldProductionJobState.Paused, PausedAtTick = WorldTick } : item).ToArray(),
        };
        AppendEvent("expansion_order_paused", $"{instruction.InstructionId}:{job.JobId}");
    }

    private void CancelExpansionForOrder(OwnerQueuedInstruction instruction)
    {
        if (BoundExpansionJob(instruction) is not { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job) return;
        ApplyInventoryTransition(inventory =>
        {
            foreach (var id in job.InputReservationIds)
                if (inventory.Reservations.SingleOrDefault(item => item.Id == id) is
                    { State: InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed })
                    inventory = InventoryFixture.ReleaseReservation(inventory, id, "owner_expansion_stopped");
            return inventory;
        });
        worldSimulation = worldSimulation with
        {
            BuildingExpansions = worldSimulation.BuildingExpansions!.Select(item => item.JobId == job.JobId
                ? item with { State = WorldProductionJobState.Cancelled, Failure = "The expansion order stopped." } : item).ToArray(),
        };
        AppendEvent("building_expansion_cancelled", $"{job.BuildingInstanceId}:{job.JobId}:The expansion order stopped.");
    }

    private void CreditExpansionOrderJob(BuildingExpansionJob job)
    {
        if (job.OrderInstructionId is not { } id) return;
        var instruction = instructionsByIdempotency.Values.SingleOrDefault(item => item.InstructionId == id);
        if (instruction is null || BoundExpansionJob(instruction) is not { State: WorldProductionJobState.Completed } completed ||
            completed.JobId != job.JobId) return;
        CreditOrderEffect(instruction, ExpansionOrderReceipt(job.JobId), 1);
    }

    private static string ExpansionOrderReceipt(string jobId) =>
        "expand:job:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));

    private string ExpansionOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        if (!AdultResident(instruction.TargetInhabitantId)) return "An adult resident is needed to expand a building.";
        if (instruction.Order!.ExpansionBinding is not null && ExpansionOrderBuilding(instruction) is null)
            return "The selected building moved, changed owner or changed size; this order keeps its original building and expansion.";
        if (instruction.Order.ExpansionBinding is { } binding && ExpansionOrderBuilding(instruction) is { } building &&
            !CanFitExpansion(building, binding.TargetPosition, binding.TargetFootprint, out var failure, binding.JobId))
            return failure;
        return "The building needs more space, a permitted expansion site, reachable materials and an authorized worker before this order can continue.";
    }

    private static void ValidateExpansionOrderBindings(WorldContentSimulationState? simulation,
        DeclarativeWorldContentState content, SocietyCheckpoint society, IReadOnlyList<TownRuntimeState>? savedTowns,
        IEnumerable<OwnerQueuedInstruction> instructions, long worldTick)
    {
        var expansionInstructions = instructions.Where(item => item.Order?.Action == "expand_building").ToArray();
        if (expansionInstructions.Select(item => item.InstructionId).Distinct(StringComparer.Ordinal).Count() != expansionInstructions.Length)
            throw new InvalidDataException("Expansion order identities are not unique.");
        var orders = expansionInstructions.ToDictionary(item => item.InstructionId, StringComparer.Ordinal);
        var jobs = simulation?.BuildingExpansions ?? [];
        foreach (var job in jobs.Where(item => item.OrderInstructionId is not null))
        {
            if (!orders.TryGetValue(job.OrderInstructionId!, out var instruction) || !ExpansionJobMatchesOrder(job, instruction) ||
                job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused &&
                    (!IsActiveOrder(instruction.Order!.Status) || instruction.Order.Status == "queued" ||
                     instruction.Order.ExpansionBinding!.JobId != job.JobId) ||
                job.State == WorldProductionJobState.Completed && job.CompletionTick > worldTick)
                throw new InvalidDataException("An expansion job does not belong to its recorded owner order.");
        }
        foreach (var instruction in orders.Values)
        {
            var order = instruction.Order!;
            if (order.ExpansionBinding is { } binding)
            {
                var entry = PrivateWorldBuildingOrderCatalog.Find(content, binding.DefinitionId);
                if (simulation is null || !ValidExpansionBindingId(binding.BuildingInstanceId) || !ValidExpansionBindingId(binding.OwnerId) ||
                    entry is null || entry.BuildingKind != order.TargetBuildingKind ||
                    !(order.TargetBuildingKind == "house" ? society.Households.Any(home => home.Id == binding.OwnerId) :
                        savedTowns?.Any(town => town.Id == binding.OwnerId) == true) ||
                    !ValidExpansionBindingPosition(binding.ExpectedPosition) || !ValidExpansionBindingPosition(binding.TargetPosition) ||
                    order.TargetPosition is { } requested && requested != binding.ExpectedPosition ||
                    binding.TargetFootprint is null || !BuildingStorageRules.IsSupported(entry.Definition, binding.TargetFootprint) ||
                    binding.ExpectedRevision != binding.TargetFootprint.Revision - 1 ||
                    !PlausibleExpansionAnchors(binding) ||
                    binding.JobId is { } id && (!ValidExpansionBindingId(id) ||
                        !jobs.Any(job => job.JobId == id && ExpansionJobMatchesOrder(job, instruction))))
                    throw new InvalidDataException("An owner expansion binding is malformed or references unrelated work.");
            }
            var completed = jobs.Where(job => job.OrderInstructionId == instruction.InstructionId &&
                job.State == WorldProductionJobState.Completed).ToArray();
            if (completed.Length > 1 || order.CompletedUnits != completed.Length ||
                (completed.Length == 0 ? order.LastEffectId is not null :
                    order.LastEffectId != ExpansionOrderReceipt(completed[0].JobId) ||
                    order.ExpansionBinding?.JobId != completed[0].JobId))
                throw new InvalidDataException("Expansion order progress has no matching completed expansion.");
        }
    }

    private static bool ValidExpansionBindingId(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 512 && value == value.Trim() && !value.Any(char.IsControl);

    private static bool ValidExpansionBindingPosition(GridPoint position) =>
        position.X is >= -10_000_000 and <= 10_000_000 && position.Y is >= -10_000_000 and <= 10_000_000;

    private static bool PlausibleExpansionAnchors(OwnerBuildingExpansionBinding binding)
    {
        var dx = binding.ExpectedPosition.X - binding.TargetPosition.X;
        var dy = binding.ExpectedPosition.Y - binding.TargetPosition.Y;
        // Native next-stage footprints add at most one row or column around the original anchor.
        return dx is 0 or 1 && dy is 0 or 1 && (dx == 0 || dy == 0) &&
            (binding.TargetFootprint.Width > 1 || dx == 0) && (binding.TargetFootprint.Height > 1 || dy == 0) &&
            (binding.TargetFootprint.Width != 2 || binding.TargetFootprint.Height != 3 || dx == 0) &&
            (binding.TargetFootprint.Width != 3 || binding.TargetFootprint.Height != 2 || dy == 0);
    }
}
