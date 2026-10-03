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
        var lot = PersonalStorageLots(actor, house.InstanceId)
            .FirstOrDefault(item => item.ItemKind == instruction.Order!.TargetMaterialKind);
        return lot is null ? null : new("store_material", "Carry your own material to your House and store it as personal property.", 0, lot.Id);
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
        if (!AgePermitsCandidate(actor, "store_material"))
            return "This agent is too young to put materials in House storage.";
        if (HouseForHousehold(HouseholdFor(actor)) is not { } house)
            return "The agent needs a House held by their household to store personal materials.";
        if (StorageRoom(house.InstanceId) <= 0)
            return "The House storage is full; make room before storing more materials.";
        if (!PersonalStorageLots(actor, house.InstanceId).Any(lot => lot.ItemKind == instruction.Order!.TargetMaterialKind))
            return "No matching personal material is available to store; carry some that is not reserved or promised for delivery.";
        if (!ReadyForBriefInteraction(actor))
            return "The agent needs warmth before storing materials.";
        return "No open walking route reaches the agent's House right now.";
    }
}
