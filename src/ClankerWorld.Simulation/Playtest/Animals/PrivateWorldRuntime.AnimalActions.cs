using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record AnimalChoice(string Action, string AnimalId, string? OtherId = null)
    {
        public string Id => "animal:" + Action + ":" + AnimalKey(AnimalId + ":" + OtherId);
    }
    private IEnumerable<AnimalChoice> AnimalChoices(string actor)
    {
        if (!AdultResident(actor)) yield break;
        var person = inhabitants[actor];
        foreach (var animal in animalWorld.Animals.Where(item => item.DiedTick is not null && item.HouseholdId is null))
            if (map.FootDistance(person.Position, animal.Position) <= 4 && FreeCarryCapacity(actor) >= 1 &&
                WildAnimalHide(animal) is not null)
                yield return new("collect_hide", animal.Id);
        foreach (var animal in animalWorld.Animals.Where(item => item.DiedTick is null))
        {
            if (animal.LeaderId == actor) yield return new("lead_home", animal.Id);
            if (animal.RiderId == actor) { yield return new("dismount", animal.Id); continue; }
            if (animal.HouseholdId is null && MayLeadAnimal(actor, animal) &&
                (map.FootDistance(person.Position, animal.Position) <= 4 ||
                 PendingInstructionFor(actor)?.Order is { Action: "animal_tame" } order && order.TargetAnimalId == animal.Id) &&
                HouseholdFor(actor) is { } household && AnimalYard(household) is { } yard &&
                HasAnimalTransferSpace(household, yard, animal) && animal.LeaderId is null &&
                (animal.TamingWork is null || animal.TamingWork.ActorId == actor))
                yield return new("tame", animal.Id);
            if (MayCareForAnimal(actor, animal) && animal.CareUntilTick <= WorldTick) yield return new("care", animal.Id);
            if (MayCareForAnimal(actor, animal) && animal.ReadyProductLotId is not null) yield return new("collect", animal.Id);
            if (AnimalHouseholdMember(actor, animal) && MayLeadAnimal(actor, animal) && animal.LeaderId is null && animal.RiderId is null &&
                AssignedAnimalYard(animal) is { } home && !YardTiles(home).Contains(animal.Position))
                yield return new("lead_home", animal.Id);
            if (animal.Species == "horse" && animal.SaddleLotId is null && AnimalHouseholdMember(actor, animal))
                yield return new("saddle", animal.Id);
            if (MayRideAnimal(actor, animal) && animal.SaddleLotId is not null && animal.RiderId is null && animal.LeaderId is null &&
                AnimalRules.HasCare(animal, WorldTick) && AnimalRules.IsAdult(animal, WorldTick, AnimalDayTicks) &&
                AttachedHandcart(actor) is null && !animalWorld.Animals.Any(item => item.RiderId == actor || item.LeaderId == actor))
                yield return new("mount", animal.Id);
        }
    }
    private bool MayLeadAnimal(string actor, AnimalState animal) => AttachedHandcart(actor) is null && PassengerBoat(actor) is null &&
        !animalWorld.Animals.Any(other => other.RiderId == actor || other.Id != animal.Id && other.LeaderId == actor);
    private void AddAnimalCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var choice in AnimalChoices(actor))
        {
            var animal = Animal(choice.AnimalId)!;
            if (choice.Action == "care" && AnimalCareInputs(actor, animal, AnimalRules.Definition(animal.Species).DailyFeed,
                    AnimalRules.Definition(animal.Species).DailyWater) is null &&
                !AnimalSupplyChoices(actor).Any(supply => supply.AnimalId == animal.Id && supply.Action == "care")) continue;
            if (choice.Action == "collect" && (animal.ReadyProductLotId is not { } product ||
                    FreeCarryCapacity(actor) < society.Checkpoint.Inventory.GetLot(product).Quantity)) continue;
            var description = choice.Action switch
            {
                "tame" => $"Approach and tame {animal.Name} for your household with two feed and one jug water; lead it to the animal yard.",
                "care" => $"Bring physical feed and jug water to care for {animal.Name} for one day.",
                "collect" => $"Collect {AnimalRules.Definition(animal.Species).Product} from {animal.Name} into household goods; milk requires a household jug.",
                "collect_hide" => $"Collect the unowned hide left by {animal.Name}'s natural death at its actual position.",
                "lead_home" => $"Lead {animal.Name} along a legal route to the household's animal yard.",
                "saddle" => $"Fit a real household saddle on {animal.Name}.",
                "mount" => $"Mount {animal.Name} with permission for faster travel and eight additional cargo units.",
                _ => $"Dismount {animal.Name}, leaving the horse and extra cargo at their actual position.",
            };
            candidates.Add(new(choice.Id, description, choice.Action == "lead_home" ? 13 : choice.Action == "care" ? 15 :
                choice.Action is "collect" or "collect_hide" ? 16 : 110, animal.Id));
        }
        AddAnimalSupplyCandidates(candidates, actor);
        AddAnimalPermissionAndTradeCandidates(candidates, actor);
        AddMilkCandidates(candidates, actor);
        if (CarriedMilk(actor) is not null && inhabitants[actor].HungerBasisPoints < ComfortableFullness)
            candidates.Add(new("drink_milk", "Drink one portion of carried jug milk, leaving the reusable jug intact.", 0));
    }
    private bool ApplyAnimalCandidate(string actor, string candidateId)
    {
        if (ApplyMilkStockChoice(actor, candidateId) || ApplySpoiledMilkChoice(actor, candidateId)) return true;
        if (candidateId == "drink_milk")
        {
            if (CarriedMilk(actor) is { } milk)
            {
                ConsumeAnimalInputs(actor, "drink_milk", [(milk, 1)]);
                inhabitants[actor] = inhabitants[actor] with
                {
                    HungerBasisPoints = Math.Min(10_000,
                    inhabitants[actor].HungerBasisPoints + 3_000)
                };
                AppendEvent("milk_drunk", actor);
            }
            return true;
        }
        if (!candidateId.StartsWith("animal:", StringComparison.Ordinal)) return false;
        if (ApplyAnimalSupplyCandidate(actor, candidateId)) return true;
        var choice = AnimalChoices(actor).FirstOrDefault(item => item.Id == candidateId);
        if (choice is null) return true;
        var animal = Animal(choice.AnimalId)!;
        var person = inhabitants[actor];
        if (choice.Action is "care" or "collect" or "saddle")
        {
            var needsSupply = choice.Action switch
            {
                "care" => AnimalCareInputs(actor, animal, AnimalRules.Definition(animal.Species).DailyFeed,
                    AnimalRules.Definition(animal.Species).DailyWater) is null,
                "collect" => animal.Species == "cow" && !society.Checkpoint.Inventory.Lots.Any(lot =>
                    lot.ItemKind == InventoryContainerRules.WaterJug && lot.OwnerId == animal.HouseholdId &&
                    PersonalEquipmentRules.IsCarried(lot, actor) && !HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id) &&
                    society.Checkpoint.Inventory.Lots.Where(content => content.ContainerLotId == lot.Id).All(content => content.ItemKind == "milk") &&
                    ContainerContentsQuantity(society.Checkpoint.Inventory, lot.Id) <= InventoryContainerRules.WaterJugCapacity - 2),
                _ => !AnimalSuppliesAtHand(actor, animal).Any(lot => lot.ItemKind == "saddle" && lot.OwnerId == animal.HouseholdId),
            };
            if (needsSupply && AnimalSupplyChoices(actor).FirstOrDefault(supply => supply.AnimalId == animal.Id && supply.Action == choice.Action) is { } supply)
            { _ = ApplyAnimalSupplyCandidate(actor, supply.Id); return true; }
            if (choice.Action == "care" && needsSupply) return true;
        }
        if (choice.Action == "dismount") { EndAnimalRide(animal, "dismounted"); return true; }
        if (choice.Action == "care" && animalWorld.Animals.FirstOrDefault(other =>
                other.RiderId == actor && other.Id != animal.Id) is { } ridden)
        {
            EndAnimalRide(ridden, "dismounted");
            return true;
        }
        if (choice.Action is "collect" or "saddle" && animalWorld.Animals.FirstOrDefault(other =>
                other.RiderId == actor && other.Id != animal.Id) is { } riddenForWork)
        {
            EndAnimalRide(riddenForWork, "dismounted");
            return true;
        }
        if (person.Position != animal.Position && animal.LeaderId != actor)
        {
            MoveToward(actor, person, animal.Position, "animal_" + choice.Action);
            return true;
        }
        switch (choice.Action)
        {
            case "tame":
                if (HouseholdFor(actor) is not { } household || AnimalYard(household) is not { } yard || !HasAnimalTransferSpace(household, yard, animal)) break;
                if (AnimalCareInputs(actor, animal, 2, 1) is not { } tamingInputs) break;
                var work = animal.TamingWork?.ActorId == actor ? animal.TamingWork.WorkTicks + 1 : 1;
                if (work < AnimalRules.TamingWorkTicks) { SetAnimal(animal with { TamingWork = new(actor, work) }); break; }
                ConsumeAnimalInputs(actor, "tame:" + animal.Id, tamingInputs);
                SetAnimal(animal with
                {
                    HouseholdId = household,
                    YardId = yard.InstanceId,
                    HerdId = "household:" + household,
                    CareUntilTick = 0,
                    TamingWork = null,
                    LeaderId = actor,
                    LeadDestination = yard.Position,
                    WildFedUntilTick = 0,
                    WildWaterUntilTick = 0
                });
                AppendEvent("animal_tamed", actor + ":" + animal.Id, animal.Position);
                break;
            case "care":
                var definition = AnimalRules.Definition(animal.Species);
                if (AnimalCareInputs(actor, animal, definition.DailyFeed, definition.DailyWater) is not { } inputs) break;
                ConsumeAnimalInputs(actor, "care:" + animal.Id, inputs);
                FinishAnimalSupplyTrip(actor);
                SetAnimal(animal with { CareUntilTick = WorldTick + AnimalDayTicks });
                AppendEvent("animal_cared", actor + ":" + animal.Id, animal.Position);
                break;
            case "collect": CollectAnimalProduct(actor, animal); break;
            case "collect_hide":
                if (WildAnimalHide(animal) is not { } hide || FreeCarryCapacity(actor) < 1) break;
                ApplyInventoryTransition(inventory => InventoryFixture.Relocate(InventoryFixture.Transfer(inventory,
                    "collect-wild-hide-" + AnimalKey(actor + ":" + animal.Id + ":" + WorldTick), animal.Id, actor,
                    hide.Id, 1, "wild-animal-hide", destinationGroundPosition: hide.GroundPosition),
                    "carry-wild-hide-" + AnimalKey(actor + ":" + animal.Id + ":" + WorldTick), hide.Id, actor, 1, carrierId: actor));
                AppendEvent("animal_hide_collected", actor + ":" + animal.Id, animal.Position);
                break;
            case "lead_home": LeadAnimalHome(actor, animal); break;
            case "saddle": SaddleAnimal(actor, animal); break;
            case "mount": SetAnimal(animal with { RiderId = actor }); AppendEvent("horse_mounted", actor + ":" + animal.Id, animal.Position); break;
        }
        return true;
    }
    private IEnumerable<InventoryLot> AnimalSuppliesAtHand(string actor, AnimalState animal)
    {
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots.Where(lot => lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
            (lot.OwnerId == actor && PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor) ||
             lot.OwnerId == animal.HouseholdId && MayCareForAnimal(actor, animal) &&
             (PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor) ||
              lot.CarrierId is null && animal.YardId is { } yardId &&
              worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == yardId) is { } yard &&
              (lot.StorageBuildingId == yardId && inhabitants[actor].Position == yard.Position ||
               lot.GroundPosition is { } ground && YardTiles(yard).Contains(new(ground.X, ground.Y)) &&
               IsWithinInteractionRange(inhabitants[actor].Position, new(ground.X, ground.Y), 1)))))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal);
    }
    private List<(InventoryLot Lot, int Quantity)>? AnimalCareInputs(string actor, AnimalState animal, int feed, int water)
    {
        var inventory = society.Checkpoint.Inventory;
        var lots = AnimalSuppliesAtHand(actor, animal).Where(lot =>
            inhabitants[actor].Position == animal.Position ||
            PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor)).ToArray();
        var result = new List<(InventoryLot, int)>();
        string? FoodHousehold(InventoryLot lot) => lot.OwnerId == animal.HouseholdId ? animal.HouseholdId : HouseholdFor(actor);
        bool Take(Func<InventoryLot, bool> predicate, int needed)
        {
            // Use grain before ready-to-eat servings, keeping family food when other feed fits.
            foreach (var lot in lots.Where(predicate).OrderBy(lot => IsEdibleFood(lot.ItemKind)))
            {
                var quantity = Math.Min(needed, AvailableLotQuantity(lot));
                var household = FoodHousehold(lot);
                var selected = result.Where(input => IsEdibleFood(input.Item1.ItemKind) &&
                    FoodHousehold(input.Item1) == household).Sum(input => input.Item2);
                // Care costs are small; a protected remainder need not hide a usable serving.
                while (quantity > 0 &&
                    (AnimalRules.IsFeed(lot.ItemKind) && !AnimalFeedMayBeSpent(actor, animal, lot, quantity) ||
                     IsEdibleFood(lot.ItemKind) && !AnimalFoodReserveRemaining(actor, selected + quantity, household)))
                    quantity--;
                if (quantity == 0) continue;
                result.Add((lot, quantity)); needed -= quantity;
                if (needed == 0) return true;
            }
            return needed == 0;
        }
        return Take(lot => AnimalRules.IsFeed(lot.ItemKind) && lot.ContainerLotId is null, feed) &&
            Take(lot => lot.ItemKind == InventoryContainerRules.FreshWater && lot.ContainerLotId is { } container &&
                society.Checkpoint.Inventory.GetLot(container).ConditionBasisPoints > 0, water) &&
            result.Where(input => IsEdibleFood(input.Item1.ItemKind))
                .GroupBy(input => FoodHousehold(input.Item1))
                .All(group => AnimalFoodReserveRemaining(actor, group.Sum(input => input.Item2), group.Key)) ? result : null;
    }
    private bool AnimalFeedMayBeSpent(string actor, AnimalState animal, InventoryLot lot, int quantity)
    {
        if (lot.StorageBuildingId is not null && !CanRemoveWorkstationStock(society.Checkpoint.Inventory, lot, quantity)) return false;
        if (!IsEdibleFood(lot.ItemKind) || HouseholdFor(actor) is not { } household) return true;
        return AnimalFoodReserveRemaining(actor, quantity, lot.OwnerId == animal.HouseholdId ? animal.HouseholdId : null);
    }
    private bool AnimalFoodReserveRemaining(string actor, int quantity, string? stockHousehold = null)
    {
        var household = stockHousehold ?? HouseholdFor(actor);
        if (quantity == 0 || household is null) return true;
        var people = society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active && person.HouseholdId == household)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var ready = society.Checkpoint.Inventory.Lots.Where(item => IsEdibleFood(item.ItemKind) &&
            (item.OwnerId == household || people.Contains(item.OwnerId))).Sum(AvailableLotQuantity);
        return ready - quantity >= people.Count * 2 + (inhabitants.Values.Any(person => people.Contains(person.InhabitantId) && ActiveParenthood(person.Parenthood)) ? 4 : 0);
    }
    private void ConsumeAnimalInputs(string actor, string purpose, List<(InventoryLot Lot, int Quantity)> inputs)
    {
        ApplyInventoryTransition(inventory =>
        {
            var next = inventory;
            for (var index = 0; index < inputs.Count; index++)
            {
                var (lot, quantity) = inputs[index];
                var id = "animal-input-" + AnimalKey(actor + ":" + WorldTick + ":" + nextEventId + ":" + purpose + ":" + index);
                next = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(next, id, lot.OwnerId, lot.Id,
                    quantity, purpose, WorldTick), id);
            }
            return next;
        });
    }
    private InventoryLot? CarriedMilk(string actor) => society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
        lot.ItemKind == "milk" && lot.ContainerLotId is { } jug &&
        society.Checkpoint.Inventory.GetLot(jug).ConditionBasisPoints > 0 && AvailableLotQuantity(lot) > 0 &&
        PersonalEquipmentRules.IsPhysicallyCarried(society.Checkpoint.Inventory, lot, actor) &&
        (lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor)));

    private InventoryLot? WildAnimalHide(AnimalState animal) => animal.DiedTick is not null && animal.HouseholdId is null ?
        society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == "animal-hide-" + AnimalKey(animal.Id) &&
            lot.ItemKind == "hide" && lot.OwnerId == animal.Id && lot.Quantity == 1 && AvailableLotQuantity(lot) == 1 &&
            lot.GroundPosition == new InventoryGroundPosition(animal.Position.X, animal.Position.Y) &&
            lot.ContainerLotId is null && lot.StorageBuildingId is null && lot.CarrierId is null) : null;

    private void CollectAnimalProduct(string actor, AnimalState animal)
    {
        if (!MayCareForAnimal(actor, animal) || animal.ReadyProductLotId is not { } id ||
            society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == id) is not { } product ||
            product.FreshnessBasisPoints == 0 || FreeCarryCapacity(actor) < product.Quantity) return;
        var inventory = society.Checkpoint.Inventory;
        InventoryLot? jug = null;
        if (product.ItemKind == "milk")
        {
            jug = inventory.Lots.FirstOrDefault(lot => lot.ItemKind == InventoryContainerRules.WaterJug &&
                lot.OwnerId == animal.HouseholdId && lot.ConditionBasisPoints > 0 && !HasActiveContainerReservation(inventory, lot.Id) &&
                (PersonalEquipmentRules.IsCarried(lot, actor) || lot.StorageBuildingId == animal.YardId &&
                    worldSimulation.Buildings.Any(building => building.InstanceId == animal.YardId && building.Position == inhabitants[actor].Position)) &&
                inventory.Lots.Where(content => content.ContainerLotId == lot.Id).All(content => content.ItemKind == "milk") &&
                ContainerContentsQuantity(inventory, lot.Id) + product.Quantity <= InventoryContainerRules.WaterJugCapacity &&
                (PersonalEquipmentRules.IsCarried(lot, actor) ||
                 ContainerFamilyQuantity(inventory, lot.Id) + product.Quantity <= FreeCarryCapacity(actor)));
            if (jug is null) return;
        }
        ClearAnimalProduct(animal, discard: false);
        if (jug is null)
            ApplyInventoryTransition(current => InventoryFixture.Relocate(current, "animal-collect-" + WorldTick + "-" + nextEventId,
                product.Id, product.OwnerId, product.Quantity, carrierId: actor));
        else
        {
            var vessel = jug;
            ApplyInventoryTransition(current =>
            {
                var next = InventoryFixture.Relocate(current, "animal-milk-jug-" + WorldTick + "-" + nextEventId,
                    vessel.Id, vessel.OwnerId, 1, groundPosition: new(animal.Position.X, animal.Position.Y));
                next = InventoryFixture.PutIntoContainer(next, "animal-milk-" + WorldTick + "-" + nextEventId,
                    vessel.OwnerId, vessel.Id, product.Id, product.Quantity);
                return InventoryFixture.Relocate(next, "animal-milk-carry-" + WorldTick + "-" + nextEventId,
                    vessel.Id, vessel.OwnerId, 1, carrierId: actor);
            });
        }
        SetAnimal(animal with { ReadyProductLotId = null, ReadyProductReservationId = null });
        FinishAnimalSupplyTrip(actor);
        AppendEvent("animal_product_collected", actor + ":" + animal.Id + ":" + product.ItemKind + ":" + product.Quantity, animal.Position);
    }
    private void LeadAnimalHome(string actor, AnimalState animal)
    {
        if (!AnimalHouseholdMember(actor, animal) || AssignedAnimalYard(animal) is not { } yard ||
            animal.RiderId is not null || animal.LeaderId is not (null) && animal.LeaderId != actor) return;
        if (YardTiles(yard).Contains(animal.Position))
        {
            SetAnimal(animal with { LeaderId = null, LeadDestination = null, YardId = yard.InstanceId });
            return;
        }
        var destination = FreeAnimalYardTile(yard, animal.Id);
        if (destination is null) return;
        SetAnimal(animal with { LeaderId = actor, LeadDestination = destination });
        MoveToward(actor, inhabitants[actor], destination.Value, "lead_animal_home");
    }
}
