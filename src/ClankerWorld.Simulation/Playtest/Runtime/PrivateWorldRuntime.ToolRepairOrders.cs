using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? ToolRepairOrderCandidateFor(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var tool = RepairableTools(actor).FirstOrDefault(lot =>
            lot.OwnerId == actor && lot.ConditionBasisPoints > 0 && lot.ItemKind == instruction.Order!.TargetEquipmentKind);
        return tool is null ? null : new("repair_tool",
            "Collect the materials and repair the requested personal tool at your household's Blacksmith.", 0, tool.Id);
    }

    private void ExecuteToolRepairOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (ToolRepairOrderCandidateFor(instruction)?.DestinationId is not { } lotId)
        {
            SetOrderStatus(instruction, "blocked", ToolRepairOrderBlockedReason(instruction));
            return;
        }
        var actor = instruction.TargetInhabitantId;
        if (RepairTool(actor, person, lotId) is not { } repairedLotId) return;
        var identity = $"{actor}:{WorldTick}:{repairedLotId}";
        var receipt = "repair:tool:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        CreditOrderEffect(instruction, receipt, 1);
    }

    private string ToolRepairOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor)) return "This agent is too young to repair tools.";
        var inventory = society.Checkpoint.Inventory;
        if (!inventory.Lots.Any(lot => lot.OwnerId == actor &&
                lot.ItemKind == instruction.Order!.TargetEquipmentKind &&
                ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) &&
                lot.ConditionBasisPoints is > 0 and < 10_000 && UnreservedQuantity(inventory, lot) > 0))
            return "Carry a matching worn personal tool that is available for repair.";
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household is null || HouseholdBuildingWithTag(household, "blacksmith") is null)
            return "The agent needs their household's Blacksmith to repair this tool.";
        return "The repair needs accessible materials, carrying space and an open route to the household Blacksmith.";
    }
}
