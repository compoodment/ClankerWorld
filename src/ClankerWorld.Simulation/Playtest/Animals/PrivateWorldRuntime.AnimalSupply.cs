using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record AnimalSupplyChoice(InventoryLot Lot, PlacedBuilding Yard, int Quantity,
        string? AnimalId = null, string? Action = null)
    {
        public string Id => "animal:supply:" + AnimalKey(Lot.Id + ":" + Yard.InstanceId + ":" + AnimalId + ":" + Action);
    }

    private IEnumerable<AnimalSupplyChoice> AnimalSupplyChoices(string actor)
    {
        if (!AdultResident(actor) || HouseholdFor(actor) is not { } household || AnimalYard(household) is not { } yard ||
            animalWorld.SupplyTrips.Any(trip => trip.ActorId == actor)) yield break;
        var inventory = society.Checkpoint.Inventory;
        var animals = animalWorld.Animals.Where(animal => animal.DiedTick is null && animal.HouseholdId == household).ToArray();
        var sources = inventory.Lots.Where(lot => lot.ContainerLotId is null && lot.DeliveryBuildingId is null &&
            lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 && AvailableLotQuantity(lot) > 0 &&
            (lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) || lot.OwnerId == household &&
                (lot.CarrierId is null || PersonalEquipmentRules.IsCarried(lot, actor))) &&
            !HasActiveContainerReservation(inventory, lot.Id) && (lot.StorageBuildingId is null || CanRemoveWorkstationStock(inventory, lot, 1)))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        foreach (var animal in animals.OrderBy(animal => animal.Id, StringComparer.Ordinal))
        {
            var definition = AnimalRules.Definition(animal.Species);
            var action = animal.ReadyProductLotId is not null && animal.Species == "cow" ? "collect" :
                animal.Species == "horse" && animal.SaddleLotId is null ? "saddle" : "care";
            if (action == "care" && AnimalRules.HasCare(animal, WorldTick)) continue;
            foreach (var root in sources.Where(lot => !PersonalEquipmentRules.IsCarried(lot, actor)))
            {
                var wanted = action switch
                {
                    "collect" => root.ItemKind == InventoryContainerRules.WaterJug && root.OwnerId == household &&
                        inventory.Lots.Where(content => content.ContainerLotId == root.Id).All(content => content.ItemKind == "milk") &&
                        ContainerContentsQuantity(inventory, root.Id) <= InventoryContainerRules.WaterJugCapacity - 2 ? 1 : 0,
                    "saddle" => root.ItemKind == "saddle" && root.OwnerId == household ? 1 : 0,
                    _ => AnimalRules.IsFeed(root.ItemKind) ? Math.Max(0, definition.DailyFeed - AnimalSuppliesAtHand(actor, animal)
                        .Where(lot => AnimalRules.IsFeed(lot.ItemKind) && PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor)).Sum(AvailableLotQuantity)) :
                        root.ItemKind == InventoryContainerRules.WaterJug && inventory.Lots.Any(content => content.ContainerLotId == root.Id &&
                            content.ItemKind == InventoryContainerRules.FreshWater && AvailableLotQuantity(content) >= definition.DailyWater) &&
                        !AnimalSuppliesAtHand(actor, animal).Any(lot => PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor) && lot.ItemKind == InventoryContainerRules.FreshWater &&
                            AvailableLotQuantity(lot) >= definition.DailyWater) ? 1 : 0,
                };
                var quantity = Math.Min(wanted, Math.Min(AvailableLotQuantity(root), FreeCarryCapacity(actor)));
                if (quantity <= 0 || !VesselFits(root, FreeCarryCapacity(actor)) ||
                    AnimalRules.IsFeed(root.ItemKind) && !AnimalFeedMayBeSpent(actor, animal, root, quantity) ||
                    !CanReachAnimalSupply(actor, root)) continue;
                yield return new(root, yard, quantity, animal.Id, action);
            }
        }
        if (animals.Length == 0) yield break;
        var feedTarget = Math.Min(8, animals.Sum(animal => AnimalRules.Definition(animal.Species).DailyFeed *
            (1 + (animal.Pregnancy is null ? 0 : 1))) + 2);
        var feedStock = inventory.Lots.Where(lot => lot.StorageBuildingId == yard.InstanceId && AnimalRules.IsFeed(lot.ItemKind)).Sum(AvailableLotQuantity);
        var hasWater = inventory.Lots.Any(lot => lot.StorageBuildingId == yard.InstanceId && lot.ItemKind == InventoryContainerRules.FreshWater && AvailableLotQuantity(lot) >= 2);
        foreach (var root in sources.Where(lot => lot.StorageBuildingId != yard.InstanceId))
        {
            var wanted = AnimalRules.IsFeed(root.ItemKind) ? feedTarget - feedStock :
                !hasWater && root.ItemKind == InventoryContainerRules.WaterJug && inventory.Lots.Any(content =>
                    content.ContainerLotId == root.Id && content.ItemKind == InventoryContainerRules.FreshWater && AvailableLotQuantity(content) > 0) ? 1 : 0;
            var quantity = Math.Min(wanted, Math.Min(AvailableLotQuantity(root), StorageRoom(yard.InstanceId)));
            if (!PersonalEquipmentRules.IsCarried(root, actor)) quantity = Math.Min(quantity, FreeCarryCapacity(actor));
            if (quantity <= 0 || !VesselFits(root, StorageRoom(yard.InstanceId)) ||
                !PersonalEquipmentRules.IsCarried(root, actor) && !VesselFits(root, FreeCarryCapacity(actor)) ||
                AnimalRules.IsFeed(root.ItemKind) && !AnimalFeedMayBeSpent(actor, animals[0], root, quantity) ||
                !CanReachAnimalSupply(actor, root)) continue;
            yield return new(root, yard, quantity);
        }
    }

    private bool CanReachAnimalSupply(string actor, InventoryLot root) => PersonalEquipmentRules.IsCarried(root, actor) ||
        root.CarrierId is null && (inhabitants[actor].Position == HouseholdStockPosition(root) || CanReachSharedItem(actor, root));

    private void AddAnimalSupplyCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (animalWorld.SupplyTrips.FirstOrDefault(trip => trip.ActorId == actor) is { } trip)
            candidates.Add(new("animal:supply_trip:" + AnimalKey(actor), trip.AnimalId is null ?
                "Carry the collected feed or jug to the household animal yard." : "Carry the collected supply to the animal and finish its care task.", 14, trip.AnimalId ?? trip.YardId));
        else foreach (var choice in AnimalSupplyChoices(actor).Take(12))
            candidates.Add(new(choice.Id, $"Collect physical {choice.Lot.ItemKind.Replace('_', ' ')} for " +
                (choice.AnimalId is null ? "the animal yard supply stock." : Animal(choice.AnimalId)!.Name + "."), 14,
                choice.AnimalId ?? choice.Yard.InstanceId));
    }

    private bool ApplyAnimalSupplyCandidate(string actor, string id)
    {
        if (id.StartsWith("animal:supply_trip:", StringComparison.Ordinal))
        {
            if (animalWorld.SupplyTrips.FirstOrDefault(trip => trip.ActorId == actor) is not { } trip) return true;
            var inventory = society.Checkpoint.Inventory;
            var lot = inventory.Lots.FirstOrDefault(lot => lot.Id == trip.LotId && PersonalEquipmentRules.IsCarried(lot, actor));
            var yard = worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == trip.YardId && building.HouseholdId == HouseholdFor(actor));
            if (lot is null || yard is null || !AdultResident(actor)) { FinishAnimalSupplyTrip(actor); return true; }
            if (trip.AnimalId is { } animalId)
            {
                var animal = Animal(animalId);
                if (animal is null || !AnimalHouseholdMember(actor, animal)) { FinishAnimalSupplyTrip(actor); return true; }
                _ = ApplyAnimalCandidate(actor, new AnimalChoice(trip.Action!, animal.Id).Id);
                if (trip.Action == "care" && AnimalRules.HasCare(Animal(animal.Id)!, WorldTick) ||
                    trip.Action == "collect" && Animal(animal.Id)!.ReadyProductLotId is null ||
                    trip.Action == "saddle" && Animal(animal.Id)!.SaddleLotId is not null) FinishAnimalSupplyTrip(actor);
                // A partial feed/water pickup needs the other ingredient before walking to the animal.
                if (trip.Action == "care" && AnimalCareInputs(actor, animal, AnimalRules.Definition(animal.Species).DailyFeed,
                        AnimalRules.Definition(animal.Species).DailyWater) is null) FinishAnimalSupplyTrip(actor);
                return true;
            }
            if (inhabitants[actor].Position != yard.Position) { MoveToward(actor, inhabitants[actor], yard.Position, "animal_yard_supply"); return true; }
            var quantity = InventoryContainerRules.IsContainer(lot.ItemKind) ? VesselFits(lot, StorageRoom(yard.InstanceId)) ? 1 : 0 :
                Math.Min(AvailableLotQuantity(lot), StorageRoom(yard.InstanceId));
            if (quantity <= 0) return true;
            ApplyInventoryTransition(current => lot.OwnerId == yard.HouseholdId ? InventoryFixture.Relocate(current,
                "animal-deliver-" + WorldTick + "-" + nextEventId, lot.Id, lot.OwnerId, quantity, storageBuildingId: yard.InstanceId) :
                InventoryFixture.Transfer(current, "animal-donate-" + WorldTick + "-" + nextEventId, lot.OwnerId, yard.HouseholdId!,
                    lot.Id, quantity, "animal_yard_supplied", destinationStorageBuildingId: yard.InstanceId));
            FinishAnimalSupplyTrip(actor);
            AppendEvent("animal_yard_supplied", actor + ":" + yard.InstanceId);
            return true;
        }
        if (!id.StartsWith("animal:supply:", StringComparison.Ordinal)) return false;
        var choice = AnimalSupplyChoices(actor).FirstOrDefault(choice => choice.Id == id);
        if (choice is null) return true;
        var source = choice.Lot;
        if (!PersonalEquipmentRules.IsCarried(source, actor))
        {
            var position = HouseholdStockPosition(source);
            if (!IsWithinInteractionRange(inhabitants[actor].Position, position, HouseholdStockInteractionRange(source)))
            { MoveToward(actor, inhabitants[actor], position, "animal_supply_pickup", HouseholdStockInteractionRange(source)); return true; }
            var operation = "animal-pickup-" + AnimalKey(actor + ":" + WorldTick + ":" + nextEventId);
            ApplyInventoryTransition(current => InventoryFixture.Relocate(current, operation, source.Id, source.OwnerId,
                choice.Quantity, carrierId: actor));
            if (choice.Quantity < source.Quantity) source = society.Checkpoint.Inventory.GetLot(source.Id + "#move:" + operation);
        }
        animalWorld = animalWorld with { SupplyTrips = animalWorld.SupplyTrips.Append(new(actor, source.Id,
            choice.Yard.InstanceId, choice.AnimalId, choice.Action)).OrderBy(trip => trip.ActorId, StringComparer.Ordinal).ToArray() };
        return true;
    }

    private void FinishAnimalSupplyTrip(string actor) => animalWorld = animalWorld with
    { SupplyTrips = animalWorld.SupplyTrips.Where(trip => trip.ActorId != actor).ToArray() };
}
