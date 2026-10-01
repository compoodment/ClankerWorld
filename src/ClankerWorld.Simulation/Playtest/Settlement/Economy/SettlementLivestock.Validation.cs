using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateLivestock(PrivateWorldRuntimeState state)
    {
        var animals = state.Livestock ?? [];
        if (animals.Any(animal => animal.RiderId is { } actor &&
            (state.BoatTransport?.Boats ?? []).Any(boat => boat.Journey?.PassengerId == actor)))
            throw new InvalidDataException("A horse rider cannot also board a boat.");
        var tick = state.Society.Society.WorldTick;
        var people = state.Society.Society.Inhabitants;
        if (animals.Count > 4096 || state.SchemaVersion < 32 &&
            (animals.Count > 0 || state.Society.Society.Inventory.Lots.Any(lot => lot.AnimalId is not null)) ||
            animals.Select(animal => animal.Id).Distinct(StringComparer.Ordinal).Count() != animals.Count)
            throw new InvalidDataException("The saved livestock list has duplicate identities or an unsupported schema.");
        foreach (var animal in animals)
        {
            if (string.IsNullOrWhiteSpace(animal.Id) || animal.Id != animal.Id.Trim() ||
                string.IsNullOrWhiteSpace(animal.AcquisitionId) || animal.AcquisitionId != animal.AcquisitionId.Trim() ||
                !Enum.IsDefined(animal.Kind) || !state.Map.IsPassable(animal.Position) ||
                !state.Society.Society.Households.Any(household => household.Id == animal.HouseholdId) ||
                animal.AcquiredTick < 0 || animal.AcquiredTick > tick ||
                animal.LastProductTick < animal.AcquiredTick || animal.LastProductTick > tick ||
                animal.FedUntilTick < 0 || animal.WateredUntilTick < 0 || animal.CaredUntilTick < 0 ||
                animal.FedUntilTick > tick + LivestockRules.CareTicks || animal.WateredUntilTick > tick + LivestockRules.CareTicks ||
                animal.CaredUntilTick > tick + LivestockRules.CareTicks ||
                animal.PendingProductQuantity < 0 || animal.PendingProductQuantity > LivestockRules.ProductQuantity(animal.Kind) ||
                animal.Kind == LivestockKind.Horse && animal.PendingProductQuantity != 0 ||
                animal.NaturalDeathTick is { } death && (death < animal.AcquiredTick || death > tick ||
                    animal.RiderId is not null || animal.PendingProductQuantity != 0) ||
                animal.HideCollected && (animal.NaturalDeathTick is null || animal.Kind == LivestockKind.Chicken))
                throw new InvalidDataException($"Animal '{animal.Id}' has invalid ownership, location or care/product state.");
            var permissions = animal.PermittedRiderIds ?? [];
            if (permissions.Count > 16 || permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Count ||
                permissions.Any(id => !people.Any(person => person.Id == id)) ||
                animal.Kind != LivestockKind.Horse && (animal.RiderId is not null || permissions.Count > 0))
                throw new InvalidDataException("Only actual horses have named resident rider permissions.");
            if (animal.RiderId is { } rider && (!LivestockRules.HasCare(animal, tick) ||
                !people.Any(person => person.Id == rider && person.Status == SocietyInhabitantStatus.Active &&
                    person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder &&
                    (person.HouseholdId == animal.HouseholdId || permissions.Contains(rider, StringComparer.Ordinal))) ||
                !state.Inhabitants.Any(person => person.InhabitantId == rider && person.Position == animal.Position)))
                throw new InvalidDataException("A rider requires actual care, permission and the horse's physical location.");
            var cargo = state.Society.Society.Inventory.Lots.Where(lot => lot.AnimalId == animal.Id).ToArray();
            if (cargo.Length > 0 && animal.Kind != LivestockKind.Horse || cargo.Sum(lot => (long)lot.Quantity) > LivestockRules.HorseCargoCapacity ||
                cargo.Any(lot => lot.GroundPosition != new InventoryGroundPosition(animal.Position.X, animal.Position.Y) || lot.ItemKind == "handcart"))
                throw new InvalidDataException("Actual horse cargo and vessel contents must fit and stay at its position.");
        }
        if (animals.Where(animal => animal.RiderId is not null).GroupBy(animal => animal.RiderId).Any(group => group.Count() > 1) ||
            state.Society.Society.Inventory.Lots.Any(lot => lot.AnimalId is { } id && !animals.Any(animal => animal.Id == id)))
            throw new InvalidDataException("A resident rides one actual horse and cargo references an actual saved animal.");
    }
}
