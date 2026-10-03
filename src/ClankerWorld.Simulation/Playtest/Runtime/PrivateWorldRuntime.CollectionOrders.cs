using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? CollectionOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!ReadyForBriefInteraction(actor) || FreeCarryCapacity(actor) <= 0) return null;
        var lot = PersonalGoodsAwaitingCollection(actor)
            .Where(item => item.ItemKind == instruction.Order!.TargetMaterialKind)
            .OrderBy(item => map.FootDistance(person.Position, HouseholdStockPosition(item)))
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault(item =>
            {
                var destination = HouseholdStockPosition(item);
                var range = item.GroundPosition is not null ? ResourceInteractionRange : 1;
                return IsWithinInteractionRange(person.Position, destination, range) ||
                    FindUnoccupiedRoute(actor, person.Position, destination, range).Count > 0;
            });
        return lot is null ? null : new("collect_material", "Walk to your own stored or dropped material and pick it up within your carrying limit.", 0, lot.Id);
    }

    private void ExecuteCollectionOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var candidate = CollectionOrderCandidateFor(instruction, person);
        if (candidate?.DestinationId is not { } lotId)
        {
            SetOrderStatus(instruction, "blocked", CollectionOrderBlockedReason(instruction, person));
            return;
        }
        var order = instruction.Order!;
        var remaining = order.QuantityIsExplicit && !order.RepeatUntilCancelled
            ? order.RequestedUnits - order.CompletedUnits : int.MaxValue;
        if (CollectPersonalGoods(instruction.TargetInhabitantId, lotId, remaining) is { } effect)
        {
            var receipt = "collect:personal:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(effect.MoveId)));
            CreditOrderEffect(instruction, receipt,
                order.ProgressUnit == "collection_loads" ? 1 : effect.Quantity);
        }
    }

    private string CollectionOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AgePermitsCandidate(actor, "collect_material"))
            return "This agent is too young to collect stored materials.";
        if (FreeCarryCapacity(actor) <= 0)
            return "Carrying space is full; make room before collecting more materials.";
        if (!PersonalGoodsAwaitingCollection(actor).Any(lot => lot.ItemKind == instruction.Order!.TargetMaterialKind))
            return "No matching personal material is available to collect; goods must be your own and not reserved or promised for delivery.";
        if (!ReadyForBriefInteraction(actor))
            return "The agent needs warmth before collecting materials.";
        return "No open walking route reaches the agent's personal material right now.";
    }
}
