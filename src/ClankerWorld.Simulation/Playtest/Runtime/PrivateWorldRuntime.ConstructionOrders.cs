using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Paid construction history survives removal, reassignment and later projects.</summary>
public sealed record WorldConstructionReceipt(
    string InstructionId, string ActorId, string BuildingInstanceId, string DefinitionId,
    string OwnerId, string MaterialOwnerId, GridPoint Position, long StartedTick,
    long CompletedTick, IReadOnlyList<string> InputReservationIds);

public sealed partial class PrivateWorldRuntime
{
    private static string ConstructionInstanceId(string instructionId) =>
        "ordered-building-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instructionId))).ToLowerInvariant();

    private bool BuildingIdentityIsReserved(string instanceId, string? constructionInstructionId = null) =>
        instanceId.StartsWith("ordered-building-", StringComparison.Ordinal) &&
            (constructionInstructionId is null || ConstructionInstanceId(constructionInstructionId) != instanceId) ||
        (worldSimulation.ConstructionReceipts ?? []).Any(receipt => receipt.BuildingInstanceId == instanceId) ||
        instructionsByIdempotency.Values.Any(instruction => instruction.Order is { } order &&
            (order.ExpansionBinding?.BuildingInstanceId == instanceId ||
             order.ShelterBinding is { } shelter && shelter.BuildingInstanceId == instanceId &&
                 shelter.BuildingPlacedTick == WorldTick ||
             order.ConstructionInstanceId == instanceId && instruction.InstructionId != constructionInstructionId));

    private static string ConstructionOrderReceipt(string instanceId) =>
        "construction:building:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId))).ToLowerInvariant();

    private static bool IsConstructionOrderProject(OwnerQueuedInstruction instruction, SettlementProject? project) =>
        instruction.Order is
        {
            Action: "construct_building", ConstructionPosition: { } position,
            ConstructionStartedTick: { } started
        } order &&
        project is not null && project.OrderInstructionId == instruction.InstructionId &&
        project.StartedTick == started && project.JobId is null &&
        project.CandidateId == TownConstructionCandidateIds.Building(order.TargetDefinitionId!, position);

    private bool TryConstructionOrderSite(OwnerQueuedInstruction instruction, PlaytestInhabitantState person,
        out BuildingDefinition? definition, out string? household, out GridPoint position, out string failure)
    {
        var order = instruction.Order!;
        var actor = instruction.TargetInhabitantId;
        var catalog = PrivateWorldBuildingOrderCatalog.Find(worldContent, order.TargetDefinitionId);
        definition = catalog?.Definition;
        household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        position = default;
        failure = "The requested building design is no longer active.";
        if (definition is null || !PrivateWorldBuildingOrderCatalog.Supports("construct_building", catalog!.BuildingKind)) return false;
        failure = "Only an adult household member can construct this building.";
        if (!AdultResident(actor) || household is null) return false;
        failure = "The household bound to this construction order has changed.";
        if (order.ConstructionOwnerId is { } owner && owner != household) return false;
        failure = "Another project is already in progress; finish or stop it before starting this construction.";
        if (person.Project is { Stage: not ("completed" or "cancelled") } project &&
            !IsConstructionOrderProject(instruction, project) && !OrdinaryProjectCanYieldToOrder(project)) return false;
        var kind = HouseholdBuildingKind(definition);
        failure = "This household already holds the requested building.";
        if (HouseholdBuildingWithTag(household, kind!) is not null) return false;
        failure = "Only a household holding a Farmhouse can build a Silo.";
        if (kind == "silo" && FarmhouseForHousehold(household) is null) return false;
        failure = "Another household member is already planning this building.";
        var householdId = household;
        if (inhabitants.Values.Any(other => other.InhabitantId != actor &&
                other.Project is { Stage: not ("completed" or "cancelled") } otherProject &&
                society.Checkpoint.GetInhabitant(other.InhabitantId).HouseholdId == householdId &&
                TownConstructionCandidateIds.TryParse(otherProject.CandidateId, out var selection) && selection.IsBuilding &&
                worldContent.Buildings.Any(item => item.CanonicalId == selection.DefinitionId && HouseholdBuildingKind(item) == kind)))
            return false;
        var target = order.ConstructionPosition ?? order.TargetPosition;
        var layout = CreateTownLayoutContext(actor, target, definition);
        failure = target is null ? "No legal construction site is currently available."
            : "The requested construction site is not legal or cannot be reached.";
        if (target is { } requested)
        {
            if (!TownLayoutService.TryEvaluateConstructionSite(layout, definition, requested, out _)) return false;
            position = requested;
        }
        else
        {
            var choices = TownLayoutService.RankConstructionSites(layout, definition);
            if (choices.Count == 0) return false;
            position = choices[0].Position;
        }
        failure = string.Empty;
        return true;
    }

    private CognitionCandidate? ConstructionOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person) =>
        ReadyForBriefInteraction(instruction.TargetInhabitantId) &&
        TryConstructionOrderSite(instruction, person, out _, out _, out var position, out _)
            ? new("construct_building", "Prepare and complete the requested household building at its selected site.",
                0, $"build-site:{position.X},{position.Y}") : null;

    private string ConstructionOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (!TryConstructionOrderSite(instruction, person, out _, out _, out _, out var failure)) return failure;
        return IsConstructionOrderProject(instruction, person.Project) && person.Project?.Blocker is { } blocker
            ? blocker : "The ordered construction is waiting for usable materials or access to its site.";
    }

    private void ExecuteConstructionOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        instruction = instructionsByIdempotency[instruction.IdempotencyKey];
        var actor = instruction.TargetInhabitantId;
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
        {
            SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
            return;
        }
        if (!TryConstructionOrderSite(instruction, person, out var definition, out var household, out var position, out var failure))
        {
            SetOrderStatus(instruction, "blocked", failure);
            return;
        }
        if (!IsConstructionOrderProject(instruction, person.Project) || person.Project!.Stage == "cancelled")
        {
            var order = instruction.Order!;
            var started = order.ConstructionStartedTick ?? WorldTick;
            order = order with
            {
                ConstructionOwnerId = household,
                ConstructionPosition = position,
                ConstructionStartedTick = started,
                ConstructionInstanceId = ConstructionInstanceId(instruction.InstructionId),
            };
            instruction = instruction with { Order = order };
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction;
            var project = new SettlementProject(TownConstructionCandidateIds.Building(definition!.CanonicalId, position),
                definition.DisplayName, started, "acquiring", LastTransitionTick: WorldTick,
                OrderInstructionId: instruction.InstructionId);
            inhabitants[actor] = person with { Project = project };
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("project_chosen", $"{actor}:{project.CandidateId}");
        }
        if (inhabitants[actor].Project is { Stage: "paused" } paused)
            SetProject(actor, paused with { Stage = "acquiring", Blocker = null, RequiresFreshChoice = false });
        ContinueProject(actor, inhabitants[actor]);
        if (inhabitants[actor].Project is { Stage: "blocked" or "cancelled" } blocked)
            SetOrderStatus(instruction, "blocked", blocked.Blocker ?? "The ordered construction cannot continue yet.");
    }

    private void PauseConstructionForOrder(OwnerQueuedInstruction instruction)
    {
        if (inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) &&
            IsConstructionOrderProject(instruction, person.Project) &&
            person.Project is { Stage: not ("completed" or "cancelled" or "paused") } project)
            SetProject(person.InhabitantId, project with { Stage = "paused", Blocker = "Food or warmth needs come first." });
    }

    private void CancelConstructionForOrder(OwnerQueuedInstruction instruction)
    {
        if (inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) &&
            IsConstructionOrderProject(instruction, person.Project) &&
            person.Project is { Stage: not ("completed" or "cancelled") } project)
            SetProject(person.InhabitantId, project with { Stage = "cancelled", Blocker = "The construction order stopped." });
    }

    private void MaintainConstructionOrdersBeforeTick()
    {
        foreach (var instruction in instructionsByIdempotency.Values.Where(item =>
                     item.Order is { Action: "construct_building", ConstructionOwnerId: not null } order &&
                     IsActiveOrder(order.Status)).ToArray())
        {
            if (!inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) ||
                !AdultResident(instruction.TargetInhabitantId) ||
                society.Checkpoint.GetInhabitant(instruction.TargetInhabitantId).HouseholdId != instruction.Order!.ConstructionOwnerId)
            {
                CancelConstructionForOrder(instruction);
                SetOrderStatus(instruction, "blocked", "The bound construction worker or household is no longer available.");
            }
            else if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
                SetOrderStatus(instruction, "interrupted", "Food or warmth needs come first.");
        }
    }

    private OwnerQueuedInstruction? ConstructionInstructionForProject(string actor, SettlementProject? project) =>
        project?.OrderInstructionId is { } id
            ? instructionsByIdempotency.Values.FirstOrDefault(instruction => instruction.InstructionId == id &&
                instruction.TargetInhabitantId == actor && instruction.Order is { } order && IsActiveOrder(order.Status) &&
                IsConstructionOrderProject(instruction, project)) : null;

    private void RecordConstructionOrderCompletion(OwnerQueuedInstruction instruction, string materialOwner)
    {
        var order = instruction.Order!;
        var purpose = "building:" + order.ConstructionInstanceId;
        var receipt = new WorldConstructionReceipt(instruction.InstructionId, instruction.TargetInhabitantId,
            order.ConstructionInstanceId!, order.TargetDefinitionId!, order.ConstructionOwnerId!, materialOwner,
            order.ConstructionPosition!.Value, order.ConstructionStartedTick!.Value, WorldTick,
            society.Checkpoint.Inventory.Reservations.Where(item => item.Purpose == purpose)
                .Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        worldSimulation = worldSimulation with
        {
            ConstructionReceipts = (worldSimulation.ConstructionReceipts ?? []).Append(receipt)
                .OrderBy(item => item.InstructionId, StringComparer.Ordinal).ToArray(),
        };
        CreditOrderEffect(instruction, ConstructionOrderReceipt(receipt.BuildingInstanceId), 1);
    }

    private static void ValidateConstructionOrderBindings(WorldContentSimulationState? simulation,
        DeclarativeWorldContentState content, SocietyCheckpoint society,
        IEnumerable<PlaytestInhabitantState> inhabitants, IEnumerable<OwnerQueuedInstruction> instructions, long worldTick)
    {
        var savedInstructions = instructions.ToArray();
        if (savedInstructions.Any(item => item is null || !ValidConstructionIdentity(item.InstructionId)) ||
            savedInstructions.Select(item => item.InstructionId).Distinct(StringComparer.Ordinal).Count() != savedInstructions.Length)
            throw new InvalidDataException("Construction orders require unique instruction IDs.");
        var instructionById = savedInstructions.ToDictionary(item => item.InstructionId, StringComparer.Ordinal);
        var receipts = simulation?.ConstructionReceipts ?? [];
        if (receipts.Any(item => item is null || !ValidConstructionIdentity(item.InstructionId) ||
                !ValidConstructionIdentity(item.ActorId) || !ValidConstructionIdentity(item.BuildingInstanceId) ||
                !ValidConstructionIdentity(item.DefinitionId) || !ValidConstructionIdentity(item.OwnerId) ||
                !ValidConstructionIdentity(item.MaterialOwnerId)) ||
            receipts.Select(item => item.InstructionId).Distinct(StringComparer.Ordinal).Count() != receipts.Count ||
            receipts.Select(item => item.BuildingInstanceId).Distinct(StringComparer.Ordinal).Count() != receipts.Count)
            throw new InvalidDataException("Construction completion receipts must be unique.");
        var householdIds = society.Households.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var receipt in receipts)
        {
            if (!instructionById.TryGetValue(receipt.InstructionId, out var instruction) ||
                instruction.Order is not { Action: "construct_building", CompletedUnits: 1, Status: "finished" } order ||
                receipt.ActorId != instruction.TargetInhabitantId || receipt.DefinitionId != order.TargetDefinitionId ||
                receipt.BuildingInstanceId != ConstructionInstanceId(instruction.InstructionId) ||
                receipt.BuildingInstanceId != order.ConstructionInstanceId || receipt.OwnerId != order.ConstructionOwnerId ||
                receipt.Position != order.ConstructionPosition || receipt.StartedTick != order.ConstructionStartedTick ||
                receipt.StartedTick < instruction.SubmittedTick || receipt.CompletedTick < receipt.StartedTick || receipt.CompletedTick > worldTick ||
                !householdIds.Contains(receipt.OwnerId) ||
                receipt.MaterialOwnerId != receipt.OwnerId && receipt.MaterialOwnerId != receipt.ActorId ||
                PrivateWorldBuildingOrderCatalog.Find(content, receipt.DefinitionId) is not { } catalog ||
                !PrivateWorldBuildingOrderCatalog.Supports("construct_building", catalog.BuildingKind) ||
                receipt.MaterialOwnerId == receipt.ActorId && !catalog.Definition.Tags.Contains("house", StringComparer.Ordinal) ||
                receipt.InputReservationIds is null || receipt.InputReservationIds.Any(id => !ValidConstructionIdentity(id)) ||
                receipt.InputReservationIds.Distinct(StringComparer.Ordinal).Count() != receipt.InputReservationIds.Count)
                throw new InvalidDataException("A construction receipt does not match its completed owner order.");
            var purpose = "building:" + receipt.BuildingInstanceId;
            var definition = catalog.Definition;
            var paid = society.Inventory.Reservations.Where(item => item.Purpose == purpose).ToArray();
            if (!paid.Select(item => item.Id).ToHashSet(StringComparer.Ordinal).SetEquals(receipt.InputReservationIds) ||
                paid.Any(item => item.State != InventoryReservationState.Completed || item.OwnerId != receipt.MaterialOwnerId ||
                    item.ExpiryTick != receipt.CompletedTick) ||
                definition.BuildCosts.Select((cost, index) => (cost, index)).Any(entry =>
                    paid.Where(item => item.Id.StartsWith($"{purpose}:quantity:{entry.index}:lot:", StringComparison.Ordinal))
                        .Sum(item => (long)item.Quantity) != entry.cost.Amount) ||
                paid.Any(item => !definition.BuildCosts.Select((_, index) => index).Any(index =>
                    item.Id == $"{purpose}:quantity:{index}:lot:{item.LotId}")))
                throw new InvalidDataException("A construction receipt lacks its actual completed material payments.");
            if (simulation!.Buildings.FirstOrDefault(item => item.InstanceId == receipt.BuildingInstanceId) is { } placed &&
                (placed.DefinitionId != receipt.DefinitionId || placed.PlacedTick != receipt.CompletedTick))
                throw new InvalidDataException("A constructed building differs from its completion receipt.");
        }
        foreach (var instruction in savedInstructions.Where(item => item.Order?.Action == "construct_building"))
        {
            var order = instruction.Order!;
            if (PrivateWorldBuildingOrderCatalog.Find(content, order.TargetDefinitionId) is null ||
                order.ConstructionOwnerId is { } owner && !householdIds.Contains(owner) ||
                order.ConstructionInstanceId is { } instance && instance != ConstructionInstanceId(instruction.InstructionId))
                throw new InvalidDataException("A construction order has an invalid definition or household binding.");
            var receipt = receipts.FirstOrDefault(item => item.InstructionId == instruction.InstructionId);
            if (order.CompletedUnits != (receipt is null ? 0 : 1) ||
                order.LastEffectId != (receipt is null ? null : ConstructionOrderReceipt(receipt.BuildingInstanceId)))
                throw new InvalidDataException("Construction progress requires an actual paid completion receipt.");
        }
        foreach (var person in inhabitants.Where(item => item.Project?.OrderInstructionId is not null))
        {
            var project = person.Project!;
            if (!instructionById.TryGetValue(project.OrderInstructionId!, out var instruction) ||
                instruction.TargetInhabitantId != person.InhabitantId || !IsConstructionOrderProject(instruction, project) ||
                project.Stage == "completed" && (project.WorkDone != ProjectWorkTicks ||
                    !receipts.Any(item => item.InstructionId == instruction.InstructionId)) ||
                project.Stage is not ("completed" or "cancelled") && !IsActiveOrder(instruction.Order!.Status))
                throw new InvalidDataException("An ordered construction project does not match its saved order.");
        }
    }

    private static bool ValidConstructionIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);
}
