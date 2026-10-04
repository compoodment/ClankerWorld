using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? ProductionOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order!;
        var recipe = PrivateWorldProductionOrderCatalog.Find(worldContent, order.TargetRecipeId)?.Recipe;
        if (recipe is null || !ReadyForBriefInteraction(instruction.TargetInhabitantId)) return null;
        if (BoundProductionJob(instruction) is { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job)
            return ProductionOrderBuilding(instruction, recipe) is null ? null :
                new("produce_item", "Continue the ordered production at its reserved workstation.", 0, job.BuildingInstanceId);
        if (!TryFindRecipeSite(recipe, out var buildingId, out _, instruction.TargetInhabitantId)) return null;
        return new("produce_item", "Prepare and complete the requested recipe using real workstation inputs.", 0, buildingId);
    }

    private PlacedBuilding? ProductionOrderBuilding(OwnerQueuedInstruction instruction, RecipeDefinition recipe)
    {
        var order = instruction.Order!;
        var actor = instruction.TargetInhabitantId;
        var building = worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == order.ProductionBuildingId);
        if (building is null || building.DefinitionId != recipe.WorkstationBuildingId ||
            order.TargetPosition is { } target && building.Position != target ||
            building.HouseholdId is { } household && society.Checkpoint.GetInhabitant(actor).HouseholdId != household)
            return null;
        var definition = worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == building.DefinitionId);
        return definition is null || building.HouseholdId is null && definition.Tags.Any(IsHouseholdBuildingTag)
            ? null : building;
    }

    private WorldProductionJob? BoundProductionJob(OwnerQueuedInstruction instruction) =>
        instruction.Order is { Action: "produce_item", ProductionJobId: { } id } order
            ? worldSimulation.ProductionJobs.FirstOrDefault(job => job.JobId == id &&
                job.OrderInstructionId == instruction.InstructionId &&
                job.WorkerId == instruction.TargetInhabitantId && job.RecipeId == order.TargetRecipeId &&
                job.BuildingInstanceId == order.ProductionBuildingId && job.StartedTick >= order.ProductionProjectStartedTick)
            : null;

    private static bool IsProductionOrderProject(OwnerQueuedInstruction instruction, SettlementProject? project) =>
        instruction.Order is { Action: "produce_item", ProductionProjectStartedTick: { } started } order &&
        project is not null && project.StartedTick == started &&
        project.CandidateId == "build:recipe:" + order.TargetRecipeId &&
        (order.ProductionJobId is null || project.JobId == order.ProductionJobId);

    private void ExecuteProductionOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        instruction = instructionsByIdempotency[instruction.IdempotencyKey];
        var order = instruction.Order!;
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
        {
            SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
            return;
        }
        var entry = PrivateWorldProductionOrderCatalog.Find(worldContent, order.TargetRecipeId);
        if (entry is null || ProductionOrderCandidateFor(instruction, person)?.DestinationId is not { } buildingId)
        {
            SetOrderStatus(instruction, "blocked", ProductionOrderBlockedReason(instruction));
            return;
        }

        if (BoundProductionJob(instruction) is { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job)
        {
            var building = ProductionOrderBuilding(instruction, entry.Recipe)!;
            if (person.Position != building.Position)
            {
                PauseProductionOrderJob(instruction, job);
                MoveToward(actor, person, building.Position, "project", 0);
                return;
            }
            if (job.State == WorldProductionJobState.Paused)
            {
                if (!CanResumeHeldInputs(actor, job.BuildingInstanceId, job.InputReservationIds) ||
                    !TryResumeKnife(actor, job.JobId, out var knifeId))
                {
                    SetOrderStatus(instruction, "blocked", "The reserved ingredients or production tool are no longer available.");
                    return;
                }
                var deadline = checked(WorldTick + job.CompletionTick - job.PausedAtTick!.Value);
                ApplyInventoryTransition(inventory => InventoryFixture.SetReservationDeadline(inventory, job.InputReservationIds, deadline));
                worldSimulation = worldSimulation with
                {
                    ProductionJobs = worldSimulation.ProductionJobs.Select(item => item.JobId == job.JobId
                        ? item with { State = WorldProductionJobState.Running, CompletionTick = deadline, PausedAtTick = null, ToolLotId = knifeId }
                        : item).ToArray(),
                };
                AppendEvent("production_order_resumed", $"{instruction.InstructionId}:{job.JobId}");
            }
            return;
        }

        if (!IsProductionOrderProject(instruction, person.Project) ||
            person.Project!.Stage is "completed" or "cancelled" || person.Project.JobId is not null)
        {
            var project = new SettlementProject("build:recipe:" + entry.Recipe.CanonicalId,
                entry.Recipe.DisplayName, WorldTick, "acquiring", LastTransitionTick: WorldTick);
            order = order with { ProductionBuildingId = buildingId, ProductionJobId = null, ProductionProjectStartedTick = WorldTick };
            instruction = instruction with { Order = order };
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction;
            inhabitants[actor] = person with { Project = project };
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("project_chosen", $"{actor}:{project.CandidateId}");
        }
        if (inhabitants[actor].Project is { RequiresFreshChoice: true } pausedProject)
            SetProject(actor, pausedProject with { Stage = "acquiring", RequiresFreshChoice = false, Blocker = null });
        ContinueProject(actor, inhabitants[actor]);
        var currentProject = inhabitants[actor].Project!;
        if (currentProject.JobId is { } startedJobId && worldSimulation.ProductionJobs.Any(job =>
                job.JobId == startedJobId && job.OrderInstructionId == instruction.InstructionId &&
                job.WorkerId == actor && job.RecipeId == order.TargetRecipeId &&
                job.BuildingInstanceId == order.ProductionBuildingId && job.StartedTick == WorldTick))
        {
            var current = instructionsByIdempotency[instruction.IdempotencyKey];
            instructionsByIdempotency[instruction.IdempotencyKey] = current with
            {
                Order = current.Order! with { ProductionJobId = startedJobId },
            };
        }
        if (currentProject.Stage is "blocked" or "cancelled")
            SetOrderStatus(instruction, "blocked", currentProject.Blocker ?? "The requested production cannot continue yet.");
    }

    private string ProductionOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        if (!AgePermitsCandidate(instruction.TargetInhabitantId, "produce_item"))
            return "This agent is too young to do workstation production.";
        if (PrivateWorldProductionOrderCatalog.Find(worldContent, instruction.Order!.TargetRecipeId) is null)
            return "The requested recipe is not active.";
        if (IsProductionOrderProject(instruction, inhabitants[instruction.TargetInhabitantId].Project) &&
            inhabitants[instruction.TargetInhabitantId].Project?.Blocker is { } blocker)
            return blocker;
        return "The requested workstation is unavailable, full, outside this household's access, or cannot be reached.";
    }

    // Called before the inventory clock advances: pause and hold inputs before
    // expiry or a due completion can outrun an already urgent survival need.
    private void MaintainProductionOrdersBeforeTick()
    {
        foreach (var instruction in instructionsByIdempotency.Values.Where(item =>
                     item.Order is { Action: "produce_item" } order && IsActiveOrder(order.Status)).ToArray())
        {
            var job = BoundProductionJob(instruction);
            if (job is not { State: WorldProductionJobState.Running or WorldProductionJobState.Paused }) continue;
            var recipe = worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == job.RecipeId);
            if (!inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) || recipe is null ||
                !AdultResident(instruction.TargetInhabitantId) || ProductionOrderBuilding(instruction, recipe) is not { } building ||
                !IsProductionOrderProject(instruction, person.Project))
            {
                CancelProductionForOrder(instruction);
                SetOrderStatus(instruction, "blocked", "The bound production worker or workstation is no longer available.");
                continue;
            }
            if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
            {
                PauseProductionOrderJob(instruction, job);
                SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
            }
            else if (person.Position != building.Position || IsConversationBusy(person.InhabitantId) ||
                     instruction.Order!.WaitForDecisionAfterFailure)
                PauseProductionOrderJob(instruction, job);
        }
    }

    private void PauseProductionOrderJob(OwnerQueuedInstruction instruction, WorldProductionJob job)
    {
        if (job.State != WorldProductionJobState.Running) return;
        ApplyInventoryTransition(inventory => InventoryFixture.HoldReservations(inventory, job.InputReservationIds));
        worldSimulation = worldSimulation with
        {
            ProductionJobs = worldSimulation.ProductionJobs.Select(item => item.JobId == job.JobId
                ? item with { State = WorldProductionJobState.Paused, PausedAtTick = WorldTick } : item).ToArray(),
        };
        AppendEvent("production_order_paused", $"{instruction.InstructionId}:{job.JobId}");
    }

    private bool IsActiveProductionOrderJob(string jobId) => instructionsByIdempotency.Values.Any(instruction =>
        instruction.Order is { Action: "produce_item" } order && IsActiveOrder(order.Status) &&
        order.ProductionJobId == jobId);

    private void CancelProductionForOrder(OwnerQueuedInstruction instruction)
    {
        if (instruction.Order?.Action != "produce_item") return;
        if (BoundProductionJob(instruction) is { State: WorldProductionJobState.Running or WorldProductionJobState.Paused } job)
        {
            ApplyInventoryTransition(inventory =>
            {
                foreach (var id in job.InputReservationIds)
                    if (inventory.Reservations.FirstOrDefault(item => item.Id == id) is
                        { State: InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed })
                        inventory = InventoryFixture.ReleaseReservation(inventory, id, "production_order_cancelled");
                return inventory;
            });
            worldSimulation = worldSimulation with
            {
                ProductionJobs = worldSimulation.ProductionJobs.Select(item => item.JobId == job.JobId
                    ? item with { State = WorldProductionJobState.Cancelled } : item).ToArray(),
            };
            AppendEvent("recipe_cancelled", $"{job.JobId}:{job.RecipeId}");
        }
        if (inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) &&
            IsProductionOrderProject(instruction, person.Project))
            SetProject(person.InhabitantId, person.Project! with { Stage = "cancelled", Blocker = "The production order stopped." });
    }

    private void CreditProductionOrderJob(WorldProductionJob job, RecipeDefinition recipe)
    {
        var instruction = PendingInstructionFor(job.WorkerId);
        if (instruction?.Order is not { Action: "produce_item" } order || BoundProductionJob(instruction)?.JobId != job.JobId ||
            !inhabitants.TryGetValue(job.WorkerId, out var person) || !IsProductionOrderProject(instruction, person.Project)) return;
        var units = order.ProgressUnit == "production_batches" ? 1 :
            recipe.Outputs.Where(output => output.ResourceId == order.TargetOutputKind).Sum(output => output.Amount);
        var receipt = ProductionOrderReceipt(job.JobId);
        SetProject(job.WorkerId, person.Project! with { Stage = "completed", Blocker = null });
        CreditOrderEffect(instruction, receipt, units);
    }

    private static string ProductionOrderReceipt(string jobId) =>
        "produce:job:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));

    private static void ValidateProductionOrderBindings(WorldContentSimulationState simulation,
        DeclarativeWorldContentState content, IEnumerable<PlaytestInhabitantState> physical,
        IEnumerable<OwnerQueuedInstruction> instructions)
    {
        var productionInstructions = instructions.Where(instruction => instruction.Order?.Action == "produce_item").ToArray();
        if (productionInstructions.Select(instruction => instruction.InstructionId).Distinct(StringComparer.Ordinal).Count() !=
            productionInstructions.Length)
            throw new InvalidDataException("Production order identities are not unique.");
        var orders = productionInstructions.ToDictionary(instruction => instruction.InstructionId, StringComparer.Ordinal);
        var people = physical.ToDictionary(person => person.InhabitantId, StringComparer.Ordinal);
        var jobs = simulation.ProductionJobs.ToDictionary(job => job.JobId, StringComparer.Ordinal);
        foreach (var job in simulation.ProductionJobs.Where(job => job.OrderInstructionId is not null))
        {
            if (!orders.TryGetValue(job.OrderInstructionId!, out var instruction) ||
                job.WorkerId != instruction.TargetInhabitantId || job.RecipeId != instruction.Order!.TargetRecipeId ||
                job.BuildingInstanceId != instruction.Order.ProductionBuildingId || job.StartedTick < instruction.SubmittedTick)
                throw new InvalidDataException("A production job does not belong to its recorded order.");
            if (job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused &&
                (!IsActiveOrder(instruction.Order.Status) || instruction.Order.ProductionJobId != job.JobId ||
                 !people.TryGetValue(job.WorkerId, out var person) || !IsProductionOrderProject(instruction, person.Project) ||
                 person.Project!.WorkDone != ProjectWorkTicks || person.Project.Stage is not ("waiting" or "paused") ||
                 person.Project.RequiresFreshChoice))
                throw new InvalidDataException("Active ordered production has no matching worker project and order.");
        }
        foreach (var instruction in orders.Values)
        {
            var order = instruction.Order!;
            var recipe = PrivateWorldProductionOrderCatalog.Find(content, order.TargetRecipeId) ??
                throw new InvalidDataException("A production order references an unsupported recipe.");
            if (order.ProductionProjectStartedTick is { } started && started < instruction.SubmittedTick)
                throw new InvalidDataException("An ordered production project predates its instruction.");
            if (order.ProductionBuildingId is { } buildingId &&
                simulation.Buildings.FirstOrDefault(building => building.InstanceId == buildingId) is { } building &&
                building.DefinitionId != recipe.Recipe.WorkstationBuildingId)
                throw new InvalidDataException("An order is bound to a different recipe workstation.");
            if (order.ProductionJobId is { } jobId &&
                (!jobs.TryGetValue(jobId, out var bound) || bound.OrderInstructionId != instruction.InstructionId ||
                 bound.StartedTick < order.ProductionProjectStartedTick))
                throw new InvalidDataException("An order is bound to an unrelated production job.");
            var completed = simulation.ProductionJobs.Where(job => job.OrderInstructionId == instruction.InstructionId &&
                job.State == WorldProductionJobState.Completed).ToArray();
            var measuredUnits = Math.Min(1_000_000L, (long)completed.Length *
                (order.ProgressUnit == "production_batches" ? 1 : recipe.OutputQuantity));
            var latest = completed.OrderBy(job => job.CompletionTick).ThenBy(job => job.JobId, StringComparer.Ordinal).LastOrDefault();
            if (order.CompletedUnits != measuredUnits || order.CompletedUnits > 0 &&
                ProductionOrderReceipt(latest!.JobId) != order.LastEffectId)
                throw new InvalidDataException("Production order progress has no matching completed jobs.");
        }
    }
}
