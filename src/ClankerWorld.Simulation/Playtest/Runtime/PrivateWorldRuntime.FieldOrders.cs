using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsFieldOrder(string action) => action is "till_field" or "plant_field" or "tend_field" or "harvest_field";

    private static FarmWorkKind FieldOrderKind(string action) => action switch
    {
        "till_field" => FarmWorkKind.Till,
        "plant_field" => FarmWorkKind.Plant,
        "tend_field" => FarmWorkKind.Tend,
        "harvest_field" => FarmWorkKind.Harvest,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private bool CanWalkToFieldOrderSite(string actor, GridPoint from, GridPoint destination, int range = 0) =>
        IsWithinInteractionRange(from, destination, range) || FindUnoccupiedRoute(actor, from, destination, range).Count > 0;

    private GridPoint? FieldOrderSite(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var household = HouseholdFor(actor);
        if (!AdultResident(actor) || FarmhouseForHousehold(household) is not { } farmhouse) return null;
        var order = instruction.Order!;
        var kind = FieldOrderKind(order.Action);
        if (kind is FarmWorkKind.Till or FarmWorkKind.Tend &&
            ToolProgressionRules.PlanWork(society.Checkpoint.Inventory, actor, ToolFamily.Hoe) is null) return null;
        if (kind == FarmWorkKind.Till)
            return NearbyFarmTiles(farmhouse.Position).Where(FarmableFreeTile)
                .Where(point => order.TargetPosition is null || point == order.TargetPosition)
                .OrderBy(point => map.FootDistance(person.Position, point))
                .ThenByDescending(point => fertility.At(point))
                .ThenBy(point => point.Y).ThenBy(point => point.X)
                .Cast<GridPoint?>().FirstOrDefault(point => CanWalkToFieldOrderSite(actor, person.Position, point!.Value));
        return fields.Where(field => field.HouseholdId == household && field.Work is null &&
                (order.TargetPosition is null || field.Position == order.TargetPosition) &&
                (kind == FarmWorkKind.Plant ? field.Stage is FarmFieldStage.Prepared or FarmFieldStage.Harvested :
                    (order.TargetCropKind is null || field.Crop == order.TargetCropKind) &&
                    (kind == FarmWorkKind.Tend ? field.Stage == FarmFieldStage.Growing && !field.Tended : field.Stage == FarmFieldStage.Ready)))
            .OrderBy(field => map.FootDistance(person.Position, field.Position))
            .ThenBy(field => field.Position.Y).ThenBy(field => field.Position.X)
            .Where(field => CanWalkToFieldOrderSite(actor, person.Position, field.Position) &&
                (kind != FarmWorkKind.Plant || !HasOtherInhabitantClaimedPlanting(field, actor) &&
                    PlantingStock(actor, field, order.TargetCropKind!) is { } stock &&
                    (stock.OwnerId == actor || CanWalkToFieldOrderSite(actor, person.Position,
                        HouseholdStockPosition(stock), HouseholdStockInteractionRange(stock)))))
            .Select(field => (GridPoint?)field.Position).FirstOrDefault();
    }

    private CognitionCandidate? FieldOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (FarmWorkFor(instruction.TargetInhabitantId) is { Work: { } work })
            return new("work_field", work.OrderInstructionId == instruction.InstructionId
                ? "Finish the requested field work with its real tools and planting stock."
                : "End the previous field work and release unused planting stock before starting this task.", 0);
        return FieldOrderSite(instruction, person) is null ? null :
            new("work_field", "Walk to a suitable household field and complete the requested work.", 0);
    }

    private void ExecuteFieldOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (FarmWorkFor(actor) is { Work: { } work } current)
        {
            if (work.OrderInstructionId == instruction.InstructionId)
            {
                ContinueFarmWork(actor, out var completed);
                if (completed is not null)
                {
                    var identity = $"{actor}:{WorldTick}:{FarmFieldRules.FieldId(completed.Position)}:{work.Kind}:{completed.Cycle}";
                    var receipt = "field:work:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                    CreditOrderEffect(instruction, receipt, 1);
                }
                return;
            }
            CancelFarmWork(current);
        }
        if (FieldOrderSite(instruction, person) is not { } site)
        {
            SetOrderStatus(instruction, "blocked", FieldOrderBlockedReason(instruction));
            return;
        }
        ApplyFieldCandidate(actor, person, FarmCandidate(FieldOrderKind(instruction.Order!.Action), site,
            instruction.Order.TargetCropKind), instruction.InstructionId);
    }

    private string FieldOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        if (!AgePermitsCandidate(actor, instruction.Order!.Action)) return "This agent is too young to work on fields.";
        if (instruction.Order.TargetPosition is { } target && !map.Contains(target))
            return "The requested tile is outside this world.";
        if (FarmhouseForHousehold(HouseholdFor(actor)) is null) return "The agent needs a household with a Farmhouse.";
        var kind = FieldOrderKind(instruction.Order.Action);
        if (kind is FarmWorkKind.Till or FarmWorkKind.Tend &&
            ToolProgressionRules.PlanWork(society.Checkpoint.Inventory, actor, ToolFamily.Hoe) is null)
            return "Carry a usable hoe for this field work.";
        if (instruction.Order.TargetPosition is not null)
            return kind switch
            {
                FarmWorkKind.Till => "The requested tile needs free farmable land near the household's Farmhouse and an open walking route.",
                FarmWorkKind.Plant => "Planting at the requested tile needs a prepared household field, usable planting stock, carrying space and open walking routes.",
                FarmWorkKind.Tend => "No available household field at the requested tile has the requested crop ready for tending along an open walking route.",
                _ => "No available household field at the requested tile has the requested crop ready to harvest along an open walking route.",
            };
        return kind switch
        {
            FarmWorkKind.Till => "No open walking route reaches free farmable land near the household's Farmhouse.",
            FarmWorkKind.Plant => "Planting needs an available prepared household field, usable planting stock and carrying space, with open walking routes.",
            FarmWorkKind.Tend => "No available household field with the requested crop needs tending along an open walking route.",
            _ => "No available household field with the requested crop is ready to harvest along an open walking route.",
        };
    }

    private void CancelFieldWorkForOrder(OwnerQueuedInstruction instruction)
    {
        if (FarmWorkFor(instruction.TargetInhabitantId) is { Work: { } work } field &&
            work.OrderInstructionId == instruction.InstructionId)
            CancelFarmWork(field);
    }

    private static void ValidateFieldOrderBindings(IEnumerable<FarmFieldState> savedFields,
        IEnumerable<OwnerQueuedInstruction> instructions)
    {
        foreach (var field in savedFields)
        {
            if (field.Work is not { OrderInstructionId: { } orderId } work) continue;
            var instruction = instructions.Where(item => item.TargetInhabitantId == work.WorkerId &&
                    item.Kind == OwnerInstructionKind.MustDo && item.Order is { } order && IsActiveOrder(order.Status))
                .OrderBy(item => item.SubmissionSequence).FirstOrDefault();
            if (instruction is null || instruction.InstructionId != orderId || instruction.Order is not { } task ||
                !IsFieldOrder(task.Action) || task.Status == "queued" || FieldOrderKind(task.Action) != work.Kind ||
                work.LastWorkedTick < instruction.SubmittedTick ||
                task.TargetPosition is { } target && target != field.Position ||
                task.TargetCropKind is { } crop && crop != (work.Kind == FarmWorkKind.Plant ? work.Crop : field.Crop))
                throw new InvalidDataException("The saved field work does not belong to its active order.");
        }
    }
}
