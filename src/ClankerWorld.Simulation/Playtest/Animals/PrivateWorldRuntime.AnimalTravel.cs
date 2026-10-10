using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private AnimalState? RidingAnimal(string actor) => animalWorld.Animals.FirstOrDefault(animal => animal.RiderId == actor &&
        MayRideAnimal(actor, animal) && AnimalRules.HasCare(animal, WorldTick) && animal.SaddleLotId is not null);
    private int HorseCargoCapacity(string actor) => RidingAnimal(actor) is null ? 0 : AnimalRules.RidingCargo;

    private void SaddleAnimal(string actor, AnimalState animal)
    {
        if (!AnimalHouseholdMember(actor, animal) || animal.Species != "horse" || animal.SaddleLotId is not null ||
            inhabitants[actor].Position != animal.Position) return;
        var saddle = AnimalSuppliesAtHand(actor, animal).FirstOrDefault(lot => lot.ItemKind == "saddle" &&
            lot.OwnerId == animal.HouseholdId && lot.ContainerLotId is null);
        if (saddle is null) return;
        var operation = "fit-saddle-" + AnimalKey(animal.Id + ":" + WorldTick + ":" + nextEventId);
        var unitId = saddle.Quantity == 1 ? saddle.Id : saddle.Id + "#move:" + operation;
        var reservationId = operation + "-held";
        ApplyInventoryTransition(inventory => InventoryFixture.Reserve(InventoryFixture.Relocate(inventory, operation,
            saddle.Id, saddle.OwnerId, 1, groundPosition: new(animal.Position.X, animal.Position.Y)), reservationId,
            saddle.OwnerId, unitId, 1, "animal-saddle:" + animal.Id, long.MaxValue));
        SetAnimal(animal with { SaddleLotId = unitId, SaddleReservationId = reservationId });
        FinishAnimalSupplyTrip(actor);
        AppendEvent("animal_saddled", actor + ":" + animal.Id, animal.Position);
    }

    private void EndAnimalRide(AnimalState animal, string reason)
    {
        if (animal.RiderId is not { } rider) return;
        SetAnimal(animal with { RiderId = null });
        if (inhabitants.TryGetValue(rider, out var person))
        {
            // Put down the excess at the rider's actual position. Held job inputs stay held;
            // losing a mount never cancels a reservation or changes property ownership.
            foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId is null &&
                         PersonalEquipmentRules.IsCarried(lot, rider) && lot.DeliveryBuildingId is null &&
                         !PersonalEquipmentRules.IsSelected(person.Equipment, lot.Id)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray())
            {
                var inventory = society.Checkpoint.Inventory;
                var excess = PersonalEquipmentRules.CarriedQuantity(inventory, rider, person.Equipment) -
                    PersonalEquipmentRules.Capacity(inventory, rider, person.Equipment);
                if (excess <= 0) break;
                if (InventoryContainerRules.IsContainer(lot.ItemKind) && HasActiveContainerReservation(inventory, lot.Id)) continue;
                var quantity = InventoryContainerRules.IsContainer(lot.ItemKind) ? 1 : Math.Min(excess, PhysicalUnreservedQuantity(lot));
                if (quantity <= 0) continue;
                ApplyInventoryTransition(current => InventoryFixture.Relocate(current,
                    "dismount-cargo-" + AnimalKey(rider + ":" + WorldTick + ":" + nextEventId + ":" + lot.Id),
                    lot.Id, lot.OwnerId, quantity, groundPosition: new(person.Position.X, person.Position.Y)));
            }
        }
        AppendEvent("horse_dismounted", rider + ":" + animal.Id + ":" + reason, animal.Position);
    }

    private void MoveAnimal(AnimalState animal, GridPoint next)
    {
        if (!map.CanFootStep(animal.Position, next)) throw new InvalidOperationException("Animals use legal foot steps.");
        if (animal.SaddleLotId is { } saddle && animal.SaddleReservationId is { } held)
            ApplyInventoryTransition(inventory => InventoryFixture.MoveAnimalSaddle(inventory, saddle, held, animal.Id,
                new(animal.Position.X, animal.Position.Y), new(next.X, next.Y), map.WrapsEastWest ? map.Width : 0));
        if (animal.ReadyProductLotId is { } product && animal.ReadyProductReservationId is { } productHeld)
            ApplyInventoryTransition(inventory => InventoryFixture.MoveAnimalProduct(inventory, product, productHeld, animal.Id,
                new(animal.Position.X, animal.Position.Y), new(next.X, next.Y), map.WrapsEastWest ? map.Width : 0));
        SetAnimal(animal with { Position = next });
    }

    private void MoveAttachedAnimal(string actor, GridPoint from, GridPoint to)
    {
        var animal = animalWorld.Animals.FirstOrDefault(item => item.RiderId == actor || item.LeaderId == actor);
        if (animal is null) return;
        if (animal.Position != from) throw new InvalidOperationException("The rider or leader must be beside the animal.");
        MoveAnimal(animal, to);
    }
}
