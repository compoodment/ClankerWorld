using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private CognitionCandidate? StorageOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!ReadyForBriefInteraction(actor) || HouseForHousehold(HouseholdFor(actor)) is not { } house ||
            StorageRoom(house.InstanceId) <= 0 ||
            !IsWithinInteractionRange(person.Position, house.Position, 1) &&
            FindUnoccupiedRoute(actor, person.Position, house.Position, 1).Count == 0)
            return null;
        var order = instruction.Order!;
        var lot = PersonalStorageLots(actor, house.InstanceId)
            .FirstOrDefault(item => item.ItemKind == (order.TargetEquipmentKind ?? order.TargetMaterialKind));
        var subject = order.Action == "store_equipment" ? "equipment" : "material";
        return lot is null ? null : new(order.Action, $"Carry your own {subject} to your House and store it as personal property.", 0, lot.Id);
    }

    private void ExecuteStorageOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var candidate = StorageOrderCandidateFor(instruction, person);
        if (candidate?.DestinationId is not { } lotId)
        {
            SetOrderStatus(instruction, "blocked", StorageOrderBlockedReason(instruction, person));
            return;
        }
        var order = instruction.Order!;
        var remaining = order.QuantityIsExplicit && !order.RepeatUntilCancelled
            ? order.RequestedUnits - order.CompletedUnits : int.MaxValue;
        if (StorePersonalGoods(instruction.TargetInhabitantId, lotId, remaining) is { } effect)
        {
            // Split inventory identities can grow; keep the saved receipt bounded.
            var receipt = "store:personal:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(effect.MoveId)));
            CreditOrderEffect(instruction, receipt,
                order.ProgressUnit == "storage_loads" ? 1 : effect.Quantity);
        }
    }

    private string StorageOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        var subject = order.Action == "store_equipment" ? "equipment" : "materials";
        if (!AgePermitsCandidate(actor, order.Action))
            return $"This agent is too young to put {subject} in House storage.";
        if (HouseForHousehold(HouseholdFor(actor)) is not { } house)
            return $"The agent needs a House held by their household to store personal {subject}.";
        if (StorageRoom(house.InstanceId) <= 0)
            return $"The House storage is full; make room before storing more {subject}.";
        if (!PersonalStorageLots(actor, house.InstanceId).Any(lot => lot.ItemKind == (order.TargetEquipmentKind ?? order.TargetMaterialKind)))
            return order.Action == "store_equipment"
                ? "No matching personal equipment is available to store; carry some that is not worn, reserved or promised for delivery."
                : "No matching personal material is available to store; carry some that is not reserved or promised for delivery.";
        if (!ReadyForBriefInteraction(actor))
            return $"The agent needs warmth before storing {subject}.";
        return "No open walking route reaches the agent's House right now.";
    }
}
