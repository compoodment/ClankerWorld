using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record FoodHarvestEffect(string LotId, string ItemKind, int Quantity, string ResourceId);

    private CognitionCandidate? OrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order;
        if (order is null || !IsActiveOrder(order.Status) || !AgePermitsCandidate(instruction.TargetInhabitantId, order.Action))
            return null;
        if (order.TargetPosition is { } requestedPosition && !map.Contains(requestedPosition))
            return null;

        if (order.Action == "consume_food")
        {
            var carried = PreferredFood(instruction.TargetInhabitantId, instruction.TargetInhabitantId)
                .FirstOrDefault(lot => order.TargetFoodKind is null || lot.ItemKind == order.TargetFoodKind);
            if (carried is not null)
                return new CognitionCandidate("consume_food", "Eat the requested carried food item.", 0);
            if (order.TargetFoodKind is null && AvailableSharedFood(instruction.TargetInhabitantId) is { } shared)
                return new CognitionCandidate("collect_shared_food", "Collect food that is available to this household.", 0, shared.Id);
            return null;
        }

        if (order.Action is not ("seek_food" or "harvest_food"))
            return null;

        var source = KnownFoodSourceForOrder(instruction, person);
        if (source is not null)
        {
            if (order.Action == "seek_food")
                return new CognitionCandidate("seek_food", "Travel to the food site named by this order.", 0, source.Id);
            return IsWithinInteractionRange(person.Position, source.Position, ResourceInteractionRange)
                ? new CognitionCandidate("harvest_food", "Gather food from the requested known site.", 0, source.Id)
                : new CognitionCandidate("seek_food", "Travel to the requested known food site.", 0, source.Id);
        }

        if (KnownTargetIsUnavailable(instruction) || !CanExploreForOrder(person))
            return null;
        return new CognitionCandidate("explore", "Explore nearby terrain to discover a suitable food site.", 0);
    }

    private MapResource? KnownFoodSourceForOrder(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order!;
        return map.Resources
            .Where(resource => resource.Kind is "food" or "fruit" &&
                resources.GetValueOrDefault(resource.Id) == ResourceState.Available &&
                (order.TargetResourceId is null || resource.Id == order.TargetResourceId) &&
                !(order.RepeatUntilCancelled && order.Action == "seek_food" &&
                    order.LastEffectId?.StartsWith($"arrival:{resource.Id}:", StringComparison.Ordinal) == true) &&
                (order.TargetFoodKind is null || FoodKnowledgeKind(resource) == order.TargetFoodKind) &&
                knowledge.Facts.Any(fact => fact.OwnerId == instruction.TargetInhabitantId &&
                    fact.Position == resource.Position &&
                    fact.ResourceKinds.Contains(FoodKnowledgeKind(resource), StringComparer.Ordinal) &&
                    (order.TargetPosition is null || fact.Position == order.TargetPosition.Value)) &&
                map.IsReachableOnFoot(person.Position, resource.Position))
            .OrderBy(resource => map.FootDistance(person.Position, resource.Position))
            .ThenBy(resource => resource.Id, StringComparer.Ordinal)
            .FirstOrDefault(resource => IsWithinInteractionRange(person.Position, resource.Position, ResourceInteractionRange) ||
                FindUnoccupiedRoute(instruction.TargetInhabitantId, person.Position, resource.Position, ResourceInteractionRange).Count > 0);
    }

    private bool KnownTargetIsUnavailable(OwnerQueuedInstruction instruction)
    {
        var order = instruction.Order!;
        if (order.TargetResourceId is { } resourceId)
        {
            var resource = map.Resources.SingleOrDefault(item => item.Id == resourceId);
            return resource is null || knowledge.Facts.Any(fact => fact.OwnerId == instruction.TargetInhabitantId &&
                fact.Position == resource.Position &&
                fact.ResourceKinds.Contains(FoodKnowledgeKind(resource), StringComparer.Ordinal));
        }
        if (order.TargetPosition is { } position)
            return !map.Contains(position) || knowledge.Facts.Any(fact =>
                fact.OwnerId == instruction.TargetInhabitantId && fact.Position == position);
        return false;
    }

    private bool CanExploreForOrder(PlaytestInhabitantState person)
    {
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person)) return false;
        if (person.Exploration?.OutingPath.Count > 0)
            return HasWarmthForOuting(person);
        return person.HungerBasisPoints >= OutingFullnessReserve && HasWarmthForOuting(person) &&
            map.FootNeighbors(person.Position).Any(map.IsPassable);
    }

    private bool ShouldInterruptOrder(
        PlaytestInhabitantState person,
        OwnerQueuedInstruction instruction,
        CognitionCandidate? orderCandidate)
    {
        if (NeedsUrgentWarmth(person))
            return orderCandidate?.Id is not ("wear_clothing" or "tend_fire" or "seek_warmth");
        if (!NeedsUrgentFood(person)) return false;
        return orderCandidate?.Id is not ("consume_food" or "collect_shared_food" or "seek_food" or "harvest_food");
    }

    private CognitionCandidate? UrgentSurvivalCandidateFor(string inhabitantId, PlaytestInhabitantState person) =>
        CreateCandidates(inhabitantId, person)
            .Where(candidate => IsSurvivalCandidate(candidate.Id))
            .OrderBy(candidate => candidate.DeterministicPriority)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool IsSurvivalCandidate(string candidateId) => candidateId is
        "consume_food" or "collect_shared_food" or "harvest_food" or "seek_food" or
        "wear_clothing" or "tend_fire" or "seek_warmth";

    private void ExecuteOrderStep(
        OwnerQueuedInstruction instruction,
        PlaytestInhabitantState person,
        CognitionCandidate candidate)
    {
        var order = instruction.Order!;
        SetOrderStatus(instruction, "doing", null, waitForDecision: false);
        var actor = instruction.TargetInhabitantId;
        switch (candidate.Id)
        {
            case "consume_food":
                if (ConsumeFood(actor, person, order.TargetFoodKind) is { } consumedLotId)
                    CreditOrderEffect(instruction, $"consume:{WorldTick:D10}:{actor}:{consumedLotId}", 1);
                else
                    SetOrderStatus(instruction, "blocked", "No matching food is available to eat.");
                return;
            case "collect_shared_food":
                CollectSharedFood(actor, person);
                return;
            case "explore":
                inhabitants[actor] = person;
                Explore(actor, person);
                return;
            case "seek_food":
                {
                    var target = KnownFoodSourceForOrder(instruction, inhabitants[actor]);
                    if (target is null)
                    {
                        SetOrderStatus(instruction, "blocked", OrderBlockedReason(instruction, inhabitants[actor]));
                        return;
                    }
                    var current = inhabitants[actor];
                    if (IsWithinInteractionRange(current.Position, target.Position, ResourceInteractionRange))
                    {
                        if (order.Action == "seek_food")
                            CreditOrderEffect(instruction,
                                $"arrival:{target.Id}:{current.Position.X}:{current.Position.Y}", 1);
                        return;
                    }
                    MoveToward(actor, current, target.Position, "owner_order_food", ResourceInteractionRange);
                    current = inhabitants[actor];
                    if (order.Action == "seek_food" &&
                        IsWithinInteractionRange(current.Position, target.Position, ResourceInteractionRange))
                        CreditOrderEffect(instruction,
                            $"arrival:{target.Id}:{current.Position.X}:{current.Position.Y}", 1);
                    return;
                }
            case "harvest_food":
                {
                    var source = KnownFoodSourceForOrder(instruction, inhabitants[actor]);
                    if (source is null)
                    {
                        SetOrderStatus(instruction, "blocked", OrderBlockedReason(instruction, inhabitants[actor]));
                        return;
                    }
                    if (!IsWithinInteractionRange(inhabitants[actor].Position, source.Position, ResourceInteractionRange))
                    {
                        MoveToward(actor, inhabitants[actor], source.Position, "owner_order_harvest", ResourceInteractionRange);
                        return;
                    }
                    if (HarvestFood(actor, inhabitants[actor], source) is { } effect &&
                        (order.TargetFoodKind is null || effect.ItemKind == order.TargetFoodKind))
                        CreditOrderEffect(instruction, $"harvest:{effect.LotId}",
                            order.ProgressUnit == "harvests" ? 1 : effect.Quantity);
                    else
                        SetOrderStatus(instruction, "blocked", "The requested food site did not yield the requested food.");
                    return;
                }
            default:
                SetOrderStatus(instruction, "blocked", "This task is not available in the current action registry.");
                return;
        }
    }

    private void CreditOrderEffect(OwnerQueuedInstruction instruction, string effectId, int units)
    {
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        var order = current.Order!;
        if (units <= 0 || order.LastEffectId == effectId || !IsActiveOrder(order.Status)) return;
        var completed = Math.Min(1_000_000, order.CompletedUnits + units);
        var finished = !order.RepeatUntilCancelled && completed >= order.RequestedUnits;
        instructionsByIdempotency[current.IdempotencyKey] = current with
        {
            Order = order with
            {
                CompletedUnits = completed,
                LastEffectId = effectId,
                Status = finished ? "finished" : "doing",
                BlockedReason = null,
            },
        };
        if (finished)
        {
            completedInstructionIds.Add(current.InstructionId);
            AppendEvent("instruction_applied", $"{current.InstructionId}:{order.Action}");
            AppendEvent("instruction_order_finished", $"{current.TargetInhabitantId}:{current.InstructionId}:{effectId}");
        }
        else
        {
            AppendEvent("instruction_order_progress", $"{current.TargetInhabitantId}:{current.InstructionId}:{completed}");
        }
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private void SetOrderStatus(
        OwnerQueuedInstruction instruction,
        string status,
        string? reason,
        bool waitForDecision = false)
    {
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        if (current.Order is not { } order || !IsActiveOrder(order.Status)) return;
        if (order.Status == status && order.BlockedReason == reason &&
            order.WaitForDecisionAfterFailure == waitForDecision) return;
        instructionsByIdempotency[current.IdempotencyKey] = current with
        {
            Order = order with
            {
                Status = status,
                BlockedReason = reason,
                WaitForDecisionAfterFailure = waitForDecision,
            },
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("instruction_order_status", $"{current.TargetInhabitantId}:{current.InstructionId}:{status}");
    }

    private string OrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (instruction.Order?.Action == "consume_food")
            return "No matching food is carried or available from the household store.";
        if (instruction.Order?.TargetPosition is not null || instruction.Order?.TargetResourceId is not null)
            return "The named food site has not been found or is not currently reachable.";
        return !CanExploreForOrder(person)
            ? "The agent needs to be safe and rested before exploring for food."
            : "No suitable food site is known yet; the order will retry exploration at a normal decision opportunity.";
    }
}
