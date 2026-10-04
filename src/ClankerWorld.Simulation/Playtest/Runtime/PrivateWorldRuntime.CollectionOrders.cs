using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? CollectionOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!ReadyForBriefInteraction(actor) || FreeCarryCapacity(actor) <= 0) return null;
        var lot = CollectionOrderGoods(instruction)
            .Where(item => VesselFits(item, FreeCarryCapacity(actor)))
            .OrderBy(item => map.FootDistance(person.Position, HouseholdStockPosition(item)))
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault(item =>
            {
                var destination = HouseholdStockPosition(item);
                var range = item.GroundPosition is not null ? ResourceInteractionRange : 1;
                return IsWithinInteractionRange(person.Position, destination, range) ||
                    FindUnoccupiedRoute(actor, person.Position, destination, range).Count > 0;
            });
        return lot is null ? null : new(instruction.Order!.Action,
            $"Walk to your own stored or dropped {CollectionOrderGoodsName(instruction.Order)} and pick it up within your carrying limit.", 0, lot.Id);
    }

    private IEnumerable<InventoryLot> CollectionOrderGoods(OwnerQueuedInstruction instruction) =>
        PersonalGoodsAwaitingCollection(instruction.TargetInhabitantId).Where(lot =>
            (instruction.Order!.Action switch
            {
                "collect_food" => IsEdibleFood(lot.ItemKind) &&
                    (instruction.Order.TargetFoodKind is null || lot.ItemKind == instruction.Order.TargetFoodKind),
                "collect_equipment" => lot.ItemKind == instruction.Order.TargetEquipmentKind,
                "collect_goods" => lot.ItemKind == instruction.Order.TargetItemKind,
                _ => lot.ItemKind == instruction.Order.TargetMaterialKind,
            }) &&
            (instruction.Order.TargetPosition is null || HouseholdStockPosition(lot) == instruction.Order.TargetPosition));

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

    private static string CollectionOrderGoodsName(OwnerInstructionOrder order) =>
        order.Action switch
        {
            "collect_food" => "food",
            "collect_equipment" => "equipment",
            "collect_goods" => "goods",
            _ => "material",
        };

    private string CollectionOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var goods = CollectionOrderGoodsName(instruction.Order!);
        var pluralGoods = instruction.Order!.Action == "collect_material" ? "materials" : goods;
        if (!AgePermitsCandidate(actor, instruction.Order.Action))
            return $"This agent is too young to collect stored {pluralGoods}.";
        if (instruction.Order!.TargetPosition is { } target && !map.Contains(target))
            return "The requested tile is outside this world.";
        if (FreeCarryCapacity(actor) <= 0)
            return $"Carrying space is full; make room before collecting more {pluralGoods}.";
        if (!CollectionOrderGoods(instruction).Any())
            return instruction.Order.TargetPosition is not null
                ? $"No matching personal {goods} is available at the requested tile; goods must be your own and not reserved or promised for delivery."
                : $"No matching personal {goods} is available to collect; goods must be your own and not reserved or promised for delivery.";
        if (!CollectionOrderGoods(instruction).Any(lot => VesselFits(lot, FreeCarryCapacity(actor))))
            return "The vessel and all its contents must fit together; make more carrying space first.";
        if (!ReadyForBriefInteraction(actor))
            return $"The agent needs warmth before collecting {pluralGoods}.";
        return $"No open walking route reaches the agent's personal {goods} right now.";
    }
}
