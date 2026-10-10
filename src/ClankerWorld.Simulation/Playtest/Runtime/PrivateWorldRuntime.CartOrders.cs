using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsCartOrder(string action) => action is "attach_handcart" or "park_handcart" or "repair_handcart" || IsCartCargoOrder(action);

    private OwnerInstructionOrder? ParseCartOrder(string text, string actor)
    {
        if (!AdultResident(actor)) return null;
        text = text.Trim();
        if (text.StartsWith("please ", StringComparison.OrdinalIgnoreCase)) text = text[7..].TrimStart();
        foreach (var (verb, action) in new[] { ("attach", "attach_handcart"), ("pull", "attach_handcart"),
                     ("park", "park_handcart"), ("unhitch", "park_handcart"), ("repair", "repair_handcart") })
        {
            if (!text.StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase)) continue;
            var subject = text[(verb.Length + 1)..].Trim();
            if (subject.StartsWith("my ", StringComparison.OrdinalIgnoreCase)) subject = subject[3..].TrimStart();
            foreach (var noun in new[] { "handcart", "cart" })
            {
                if (subject.Equals(noun, StringComparison.OrdinalIgnoreCase))
                    return new(action, "queued", 1, 0, "cart_tasks", false);
                if (!subject.StartsWith(noun + " ", StringComparison.OrdinalIgnoreCase)) continue;
                var id = subject[(noun.Length + 1)..].Trim();
                // Consume the whole target. Extra words and nonexistent IDs are not guessed away.
                if (IsValidInstructionIdentifier(id) && society.Checkpoint.Inventory.Lots.Any(lot => lot.Id == id && lot.ItemKind == InventoryContainerRules.Handcart))
                    return new(action, "queued", 1, 0, "cart_tasks", false) { TargetCartLotId = id };
                if (id.StartsWith("at ", StringComparison.OrdinalIgnoreCase) &&
                    PrivateWorldInstructionOrderParser.Parse("move to " + id[3..], map.Resources, FoodKnowledgeKind) is
                    { Action: "move_to", TargetPosition: { } position })
                {
                    var carts = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart &&
                        lot.OwnerId == actor && CartPosition(lot) == position).ToArray();
                    if (carts.Length == 1 && IsValidInstructionIdentifier(carts[0].Id))
                        return new(action, "queued", 1, 0, "cart_tasks", false) { TargetCartLotId = carts[0].Id };
                }
            }
        }
        return null;
    }

    private InventoryLot? CartForOrder(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        if (order.TargetCartLotId is { } target)
            return society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == target && lot.ItemKind == InventoryContainerRules.Handcart);
        if (IsCartCargoOrder(order.Action)) return CartForCargoOrder(instruction, person);
        if (order.Action == "park_handcart") return AttachedHandcart(actor);
        if (order.Action == "repair_handcart") return CartForRepairOrder(actor, person);
        return society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart &&
                lot.OwnerId == actor && IsValidInstructionIdentifier(lot.Id) && CanPullHandcart(lot) && !handcartHitches.Any(hitch => hitch.CartLotId == lot.Id))
            .OrderBy(lot => map.FootDistance(person.Position, CartPosition(lot))).ThenBy(lot => lot.Id, StringComparer.Ordinal)
            .FirstOrDefault(lot => person.Position == CartPosition(lot) || FindUnoccupiedRoute(actor, person.Position, CartPosition(lot), 0).Count > 0);
    }

    private static GridPoint CartPosition(InventoryLot cart) => new(cart.GroundPosition!.Value.X, cart.GroundPosition.Value.Y);

    private CognitionCandidate? CartOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (IsCartCargoOrder(instruction.Order!.Action)) return CartCargoOrderCandidateFor(instruction, person);
        if (CartOrderBlocker(instruction, person) is not null) return null;
        var cart = CartForOrder(instruction, person)!;
        if (instruction.Order!.Action == "repair_handcart") return CartRepairOrderCandidateFor(instruction, cart);
        return new(instruction.Order!.Action == "park_handcart" ? "park_handcart" : AttachCartPrefix + cart.Id,
            instruction.Order.Action == "park_handcart" ? "Park the selected attached cart here, retaining its cargo." :
                "Reach and attach the selected owned cart through its normal physical action.", 0, cart.Id);
    }

    private string? CartOrderBlocker(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (IsCartCargoOrder(instruction.Order!.Action)) return CartCargoOrderBlocker(instruction, person);
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor)) return "Only an adult can pull, park or repair a handcart.";
        var cart = CartForOrder(instruction, person);
        if (cart is null) return "Waiting for an accessible owned cart suitable for this task.";
        if (cart.OwnerId != actor) return "You do not own the selected cart.";
        if (instruction.Order!.Action == "repair_handcart") return CartRepairOrderBlocker(actor, person, cart);
        if (instruction.Order!.Action == "park_handcart")
            return AttachedHandcart(actor)?.Id == cart.Id ? null : "The selected cart is not attached to you.";
        if (cart.ConditionBasisPoints == 0) return "The selected cart is broken; it needs repair before pulling.";
        if (!CanPullHandcart(cart)) return "The selected cart or its cargo is reserved for another task.";
        if (animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor))
            return "Dismount or stop leading the animal before attaching a cart.";
        if (AttachedHandcart(actor) is not null) return "Park your attached cart before attaching another cart.";
        if (handcartHitches.Any(hitch => hitch.CartLotId == cart.Id)) return "The selected cart is already attached.";
        return person.Position == CartPosition(cart) || FindUnoccupiedRoute(actor, person.Position, CartPosition(cart), 0).Count > 0
            ? null : "Waiting for a clear legal route to the selected cart.";
    }

    private void ExecuteCartOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (IsCartCargoOrder(instruction.Order!.Action))
        { ExecuteCartCargoOrderStep(instruction, person); return; }
        if (CartOrderBlocker(instruction, person) is { } blocker)
        {
            SetOrderStatus(instruction, "blocked", blocker);
            return;
        }
        var cart = CartForOrder(instruction, person)!;
        if (instruction.Order!.TargetCartLotId is null)
        {
            instruction = instructionsByIdempotency[instruction.IdempotencyKey] with
            { Order = instructionsByIdempotency[instruction.IdempotencyKey].Order! with { TargetCartLotId = cart.Id } };
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction;
        }
        if (instruction.Order!.Action == "repair_handcart")
        {
            ExecuteCartRepairOrderStep(instruction, person, cart);
            return;
        }
        var wasAttached = AttachedHandcart(instruction.TargetInhabitantId)?.Id == cart.Id;
        var parking = instruction.Order!.Action == "park_handcart";
        ApplyHandcartCandidate(instruction.TargetInhabitantId, person, parking ? "park_handcart" : AttachCartPrefix + cart.Id);
        var isAttached = AttachedHandcart(instruction.TargetInhabitantId)?.Id == cart.Id;
        if (parking ? wasAttached && !isAttached : !wasAttached && isAttached)
            CreditOrderEffect(instruction, CartOrderEffectId(instruction), 1);
    }

    private static string CartOrderEffectId(OwnerQueuedInstruction instruction) => "cart-order:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{instruction.InstructionId}|{instruction.TargetInhabitantId}|{instruction.Order!.Action}|{instruction.Order.TargetCartLotId}")));

    private static void ValidateCartOrderBindings(InventoryCheckpoint inventory, IEnumerable<OwnerQueuedInstruction> instructions)
    {
        var carts = inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart).Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, InventoryReservation>? repairReservations = null;
        foreach (var instruction in instructions)
        {
            if (instruction.Order is not { } order || !IsCartOrder(order.Action)) continue;
            if (order.TargetCartLotId is { } id && !carts.Contains(id))
                throw new InvalidDataException("A cart order references a missing handcart.");
            if (order.Action != "repair_handcart" || order.CompletedUnits == 0) continue;
            repairReservations ??= inventory.Reservations.ToDictionary(item => item.Id, StringComparer.Ordinal);
            if (CartRepairOrderReceiptTick(instruction, inventory.WorldTick) is not { } tick ||
                !CartRepairKinds.All(kind => repairReservations.TryGetValue($"cart-repair:{tick}:{instruction.TargetInhabitantId}:{kind}", out var input) &&
                    input.OwnerId == instruction.TargetInhabitantId && input.Quantity == 1 && input.Purpose == "equipment_repair" &&
                    input.ExpiryTick == tick + 1 && input.State == InventoryReservationState.Completed))
                throw new InvalidDataException("A cart repair order lacks its actual consumed repair inputs.");
        }
    }
}
