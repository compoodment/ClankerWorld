using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? RepairOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (person.Equipment?.Repair is { } repair)
            return new("repair_equipment", repair.OrderInstructionId == instruction.InstructionId
                ? "Finish repairing the requested personal item with the reserved materials."
                : "End the previous repair and release its unused materials before starting this task.", 0, repair.LotId);
        var target = WornEquipmentItems(actor)
            .Where(lot => lot.ItemKind == instruction.Order!.TargetEquipmentKind)
            .FirstOrDefault(lot => MissingRepairInputUnits(actor, lot) <= FreeCarryCapacity(actor) &&
                EquipmentRepairSite(actor, lot) is { } site &&
                (person.Position == site.Position || FindUnoccupiedRoute(actor, person.Position, site.Position, 0).Count > 0) &&
                PersonalEquipmentRules.RepairMaterials(lot.ItemKind).All(input => HasCarriedOwnItem(actor, input.ResourceId) ||
                    SharedItem(input.ResourceId, actor) is not null));
        return target is null ? null : new("repair_equipment", "Collect the materials and repair the requested personal item at your household's work site.", 0, target.Id);
    }

    private void ExecuteRepairOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (person.Equipment?.Repair is { } repair)
        {
            if (repair.OrderInstructionId == instruction.InstructionId)
            {
                if (ContinueEquipmentRepair(actor) is { } effect)
                {
                    var identity = $"{actor}:{effect.StartedTick}:{effect.LotId}";
                    var receipt = "repair:equipment:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                    CreditOrderEffect(instruction, receipt, 1);
                }
                return;
            }
            CancelEquipmentRepair(actor);
            person = inhabitants[actor];
        }
        if (RepairOrderCandidateFor(instruction, person)?.DestinationId is not { } lotId)
        {
            SetOrderStatus(instruction, "blocked", RepairOrderBlockedReason(instruction));
            return;
        }
        RepairEquipment(actor, person, lotId, instruction.InstructionId);
    }

    private string RepairOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AgePermitsCandidate(actor, "repair_equipment"))
            return "This agent is too young to repair equipment.";
        var kind = instruction.Order!.TargetEquipmentKind!;
        var targets = WornEquipmentItems(actor).Where(lot => lot.ItemKind == kind).ToArray();
        if (targets.Length == 0)
            return "No matching worn personal item is available; carry an unreserved single item that needs repair.";
        if (targets.All(lot => EquipmentRepairSite(actor, lot) is null))
            return kind == "basket" ? "The agent needs their household's House to repair a basket." :
                "The agent needs their household's Tailor Shop to repair this item.";
        if (targets.All(lot => MissingRepairInputUnits(actor, lot) > FreeCarryCapacity(actor)))
            return "Make carrying space for the repair materials.";
        if (targets.All(lot => PersonalEquipmentRules.RepairMaterials(lot.ItemKind).Any(input =>
                !HasCarriedOwnItem(actor, input.ResourceId) && SharedItem(input.ResourceId, actor) is null)))
            return "The repair needs available materials in the agent's hands or household stock.";
        return "No open walking route reaches the repair site right now.";
    }

    private void CancelRepairForOrder(OwnerQueuedInstruction instruction)
    {
        if (inhabitants.TryGetValue(instruction.TargetInhabitantId, out var person) &&
            person.Equipment?.Repair?.OrderInstructionId == instruction.InstructionId)
            CancelEquipmentRepair(instruction.TargetInhabitantId);
    }

    private static void ValidateRepairOrderBindings(IEnumerable<PlaytestInhabitantState> people,
        InventoryCheckpoint inventory, IEnumerable<OwnerQueuedInstruction> instructions)
    {
        foreach (var person in people)
        {
            if (person.Equipment?.Repair is not { OrderInstructionId: { } orderId } repair) continue;
            var instruction = instructions.Where(item => item.TargetInhabitantId == person.InhabitantId &&
                    item.Kind == OwnerInstructionKind.MustDo && item.Order is { } order && IsActiveOrder(order.Status))
                .OrderBy(item => item.SubmissionSequence).FirstOrDefault();
            var target = inventory.Lots.FirstOrDefault(lot => lot.Id == repair.LotId);
            if (target?.OwnerId != person.InhabitantId || instruction is null || instruction.InstructionId != orderId || instruction.Order is not { Action: "repair_equipment" } task ||
                task.Status == "queued" || repair.StartedTick < instruction.SubmittedTick ||
                target.ItemKind != task.TargetEquipmentKind)
                throw new InvalidDataException("The saved equipment repair does not belong to its active order.");
        }
    }
}
