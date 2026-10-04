using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void InterruptOrdinaryFieldWorkForCustody(string actor, string lotId)
    {
        if (FarmWorkFor(actor) is { Work: { OrderInstructionId: null } work } field &&
            (work.HoeLotId == lotId || work.SickleLotId == lotId))
            CancelFarmWork(field);
    }

    private PlacedBuilding? CustodyOrderHouse(OwnerQueuedInstruction instruction, string? ownerId)
    {
        var order = instruction.Order!;
        if (ownerId is null || HouseForHousehold(ownerId) is not { } house ||
            order.TargetPosition is { } requested && house.Position != requested) return null;
        if (order.TargetStorageBuildingId is not null &&
            (house.InstanceId != order.TargetStorageBuildingId || house.HouseholdId != order.TargetStorageOwnerId ||
             house.Position != order.TargetStoragePosition)) return null;
        return house;
    }

    private OwnerQueuedInstruction BindCustodyOrder(OwnerQueuedInstruction instruction, PlacedBuilding house,
        string? lotId = null)
    {
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        var order = current.Order!;
        if (order.TargetStorageBuildingId is null)
            order = order with
            {
                TargetStorageBuildingId = house.InstanceId,
                TargetStorageOwnerId = house.HouseholdId,
                TargetStoragePosition = house.Position,
            };
        if (lotId is not null) order = order with { TargetLotId = lotId };
        current = current with { Order = order };
        instructionsByIdempotency[current.IdempotencyKey] = current;
        checkpointSchemaVersion = StateSchemaVersion;
        return current;
    }

    private IEnumerable<InventoryLot> ReturnOrderGoods(OwnerQueuedInstruction instruction) =>
        ReturnableBorrowedGoods(instruction.TargetInhabitantId).Where(lot =>
            lot.ItemKind == instruction.Order!.TargetItemKind &&
            (instruction.Order.TargetLotId is null || lot.Id == instruction.Order.TargetLotId) &&
            (instruction.Order.TargetStorageOwnerId is null || lot.OwnerId == instruction.Order.TargetStorageOwnerId));

    private CognitionCandidate? ReturnOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (!ReadyForBriefInteraction(actor)) return null;
        foreach (var lot in ReturnOrderGoods(instruction).OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            if (CustodyOrderHouse(instruction, lot.OwnerId) is not { } house || StorageRoom(house.InstanceId) <= 0 ||
                !VesselFits(lot, StorageRoom(house.InstanceId)) ||
                !IsWithinInteractionRange(person.Position, house.Position, 1) &&
                FindUnoccupiedRoute(actor, person.Position, house.Position, 1).Count == 0) continue;
            return new("return_borrowed", "Carry borrowed goods back to their owning household's House without changing ownership.", 0, lot.Id);
        }
        return null;
    }

    private void ExecuteReturnOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var candidate = ReturnOrderCandidateFor(instruction, person);
        if (candidate?.DestinationId is not { } lotId)
        {
            SetOrderStatus(instruction, "blocked", ReturnOrderBlockedReason(instruction, person));
            return;
        }
        var lot = ReturnOrderGoods(instruction).Single(item => item.Id == lotId);
        var house = CustodyOrderHouse(instruction, lot.OwnerId)!;
        instruction = BindCustodyOrder(instruction, house, lotId);
        var order = instruction.Order!;
        var remaining = order.QuantityIsExplicit && !order.RepeatUntilCancelled
            ? order.RequestedUnits - order.CompletedUnits : int.MaxValue;
        if (ReturnBorrowedGoods(instruction.TargetInhabitantId, lotId, remaining,
                order.TargetStorageBuildingId, order.TargetStorageOwnerId, order.TargetStoragePosition) is not { } effect) return;
        var receipt = "return:borrowed:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(effect.MoveId)));
        CreditOrderEffect(instruction, receipt, order.ProgressUnit == "return_loads" ? 1 : effect.Quantity);
        // Another load may use another lot, but it must keep the original owner and House.
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        instructionsByIdempotency[instruction.IdempotencyKey] = current with { Order = current.Order! with { TargetLotId = null } };
    }

    private string ReturnOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        if (!AgePermitsCandidate(actor, order.Action)) return "This agent is too young to return borrowed goods.";
        if (order.TargetStorageOwnerId is { } owner && CustodyOrderHouse(instruction, owner) is null)
            return "The selected return House moved, changed owner or is no longer available; the order keeps its original destination.";
        var lots = ReturnOrderGoods(instruction).ToArray();
        if (lots.Length == 0)
            return "No matching borrowed goods are available; the selected goods must still be carried, unreserved and free of delivery promises.";
        var destinations = lots.Select(lot => (Lot: lot, House: CustodyOrderHouse(instruction, lot.OwnerId)))
            .Where(item => item.House is not null).ToArray();
        if (destinations.Length == 0) return "The goods' owning household has no House at the requested destination.";
        if (destinations.All(item => StorageRoom(item.House!.InstanceId) <= 0 ||
                !VesselFits(item.Lot, StorageRoom(item.House!.InstanceId))))
            return "The owning House needs room for the returned goods, including each whole vessel and all its contents.";
        if (!ReadyForBriefInteraction(actor)) return "The agent needs warmth before returning borrowed goods.";
        return "No open walking route reaches the owning household's House right now.";
    }

    private static void ValidateCustodyOrderBindings(SocietyCheckpoint society,
        IEnumerable<OwnerQueuedInstruction> instructions)
    {
        // Removal, reassignment and expansion are legitimate world changes. They
        // block the saved destination at execution time rather than corrupting it.
        foreach (var instruction in instructions)
            if (instruction.Order?.TargetStorageOwnerId is { } owner &&
                !society.Households.Any(household => household.Id == owner))
                throw new InvalidDataException("A custody order references an unknown owning household.");
    }
}
