using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsAnimalOrder(string action) => action is "animal_care" or "animal_collect" or "animal_tame" or
        "animal_lead_home" or "animal_saddle" or "animal_mount" or "animal_dismount";

    private OwnerInstructionOrder? ParseAnimalOrder(string text, string actor)
    {
        if (!AdultResident(actor)) return null;
        var repeat = false;
        if (text.StartsWith("repeat ", StringComparison.OrdinalIgnoreCase)) { repeat = true; text = text[7..]; }
        foreach (var (prefix, action) in new[] { ("care for ", "care"), ("collect from ", "collect"), ("tame ", "tame"),
                     ("lead home ", "lead_home"), ("saddle ", "saddle"), ("mount ", "mount"), ("dismount ", "dismount") })
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || repeat && action is not ("care" or "collect")) continue;
            var name = text[prefix.Length..].Trim();
            var targets = animalWorld.Animals.Where(animal => animal.DiedTick is null && animal.Id == name).ToArray();
            if (targets.Length == 0) targets = animalWorld.Animals.Where(animal => animal.DiedTick is null &&
                string.Equals(animal.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (targets.Length != 1) return null;
            var animal = targets[0];
            if (!AnimalHouseholdMember(actor, animal) && !MayCareForAnimal(actor, animal) && !MayRideAnimal(actor, animal) &&
                !(action == "tame" && animal.HouseholdId is null && map.FootDistance(inhabitants[actor].Position, animal.Position) <= 4)) return null;
            return new("animal_" + action, "queued", 1, 0, "animal_tasks", repeat) { TargetAnimalId = animal.Id };
        }
        return null;
    }

    private CognitionCandidate? AnimalOrderCandidate(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var animal = Animal(instruction.Order!.TargetAnimalId!);
        return AdultResident(actor) && animal is { DiedTick: null } ?
            new("animal_order", "Follow the order for " + animal.Name + ", using its actual location, permissions and physical supplies.", 0, animal.Id) : null;
    }

    private void ExecuteAnimalOrder(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        var animal = Animal(order.TargetAnimalId!);
        if (animal is not { DiedTick: null }) return;
        var action = order.Action[7..];
        if (animalWorld.SupplyTrips.FirstOrDefault(trip => trip.ActorId == actor) is { } trip &&
            (trip.AnimalId != animal.Id || trip.Action != action))
            FinishAnimalSupplyTrip(actor);
        var oldCare = animal.CareUntilTick;
        var oldProduct = animal.ReadyProductLotId;
        var choice = AnimalChoices(actor).FirstOrDefault(choice => choice.AnimalId == animal.Id && choice.Action == action);
        var previousPosition = inhabitants[actor].Position;
        if (choice is not null) _ = ApplyAnimalCandidate(actor, choice.Id);
        animal = Animal(animal.Id)!;
        var finished = action switch
        {
            "care" => MayCareForAnimal(actor, animal) && animal.CareUntilTick > oldCare,
            "collect" => oldProduct is not null && animal.ReadyProductLotId is null && MayCareForAnimal(actor, animal),
            "tame" => AnimalHouseholdMember(actor, animal),
            "lead_home" => AnimalHouseholdMember(actor, animal) && AssignedAnimalYard(animal) is { } yard &&
                YardTiles(yard).Contains(animal.Position) && animal.LeaderId is null,
            "saddle" => AnimalHouseholdMember(actor, animal) && animal.SaddleLotId is not null,
            "mount" => animal.RiderId == actor,
            _ => animal.RiderId is null,
        };
        if (finished)
        {
            CreditOrderEffect(instruction, "animal-order:" + AnimalKey(instruction.InstructionId + ":" + animal.Id + ":" +
                (action == "care" ? animal.CareUntilTick.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                 action == "collect" ? oldProduct : WorldTick.ToString(System.Globalization.CultureInfo.InvariantCulture))), 1);
            return;
        }
        if (inhabitants[actor].Position != previousPosition) return;
        if (action is "care" or "collect" or "saddle")
        {
            if (animalWorld.SupplyTrips.FirstOrDefault(trip => trip.ActorId == actor && trip.AnimalId == animal.Id) is not null)
                _ = ApplyAnimalSupplyCandidate(actor, "animal:supply_trip:" + AnimalKey(actor));
            else if (AnimalSupplyChoices(actor).FirstOrDefault(supply => supply.AnimalId == animal.Id && supply.Action == action) is { } supply)
                _ = ApplyAnimalSupplyCandidate(actor, supply.Id);
        }
        if (choice is null) SetOrderStatus(instruction, "blocked", "Waiting for this animal's permission, physical supplies, product or free yard place.");
    }

    private void CancelAnimalSupplyForOrder(OwnerQueuedInstruction instruction)
    {
        if (instruction.Order is not { } order || !IsAnimalOrder(order.Action)) return;
        if (animalWorld.SupplyTrips.Any(trip => trip.ActorId == instruction.TargetInhabitantId &&
                trip.AnimalId == order.TargetAnimalId && trip.Action == order.Action[7..]))
            FinishAnimalSupplyTrip(instruction.TargetInhabitantId);
    }
}
