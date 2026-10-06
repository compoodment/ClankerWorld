using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private int AnimalDayTicks => worldSystems.Config.TicksPerDay;
    private void StageAnimalContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (new[] { TailorVariantContent.PackageId, RestaurantVariantContent.PackageId }.Any(id =>
                !packages.Any(package => package.Manifest.PackageId == id && package.Lifecycle == ContentPackageLifecycle.Active))) return;
        StageBuiltInContent(AnimalContent.PackageId, RestaurantContent.PackageId, AnimalContent.Create, "animal_content_staged");
    }
    private static string AnimalKey(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    private AnimalState? Animal(string id) => animalWorld.Animals.FirstOrDefault(item => item.Id == id);
    private void SetAnimal(AnimalState animal) => animalWorld = animalWorld with
    {
        Animals = animalWorld.Animals.Where(item => item.Id != animal.Id).Append(animal)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
    };
    private PlacedBuilding? AnimalYard(string? household) => household is null ? null : HouseholdBuildingWithTag(household, AnimalContent.YardTag);
    private PlacedBuilding? AssignedAnimalYard(AnimalState animal) => worldSimulation.Buildings.FirstOrDefault(yard =>
        yard.InstanceId == animal.YardId && yard.HouseholdId == animal.HouseholdId);
    private int AnimalYardCapacity(PlacedBuilding yard)
    {
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == yard.DefinitionId);
        var effective = BuildingStorageRules.EffectiveDefinition(definition, yard);
        return Math.Min(AnimalRules.PopulationCap, effective.Width * effective.Height);
    }
    private int AnimalPlacesUsed(string household, string? yardId = null) => animalWorld.Animals
        .Where(item => item.DiedTick is null && item.HouseholdId == household && (yardId is null || item.YardId == yardId))
        .Sum(item => 1 + (item.Pregnancy is null ? 0 : 1)) + animalWorld.Offers.Where(offer =>
            offer.ReceivingHouseholdId == household && (yardId is null || offer.ReceivingYardId == yardId))
            .Sum(offer => 1 + (Animal(offer.AnimalId)?.Pregnancy is null ? 0 : 1));
    private bool HasAnimalSpace(string household, PlacedBuilding yard) =>
        AnimalPlacesUsed(household) < AnimalRules.PopulationCap && AnimalPlacesUsed(household, yard.InstanceId) < AnimalYardCapacity(yard);
    private bool AnimalHouseholdMember(string actor, AnimalState animal) => animal.HouseholdId is not null &&
        AdultResident(actor) && HouseholdFor(actor) == animal.HouseholdId;
    private bool MayCareForAnimal(string actor, AnimalState animal) => animal.DiedTick is null && AdultResident(actor) &&
        (AnimalHouseholdMember(actor, animal) || animal.CarePermissions.Contains(actor, StringComparer.Ordinal));
    private bool MayRideAnimal(string actor, AnimalState animal) => animal.Species == "horse" &&
        animal.DiedTick is null && AdultResident(actor) &&
        (AnimalHouseholdMember(actor, animal) || animal.RidingPermissions.Contains(actor, StringComparer.Ordinal));
    private GridPoint[] YardTiles(PlacedBuilding yard) => WorldContentSimulationRules.Footprint(
        worldContent.Buildings.Single(item => item.CanonicalId == yard.DefinitionId), yard).ToArray();
    private GridPoint? FreeAnimalYardTile(PlacedBuilding yard, string? animalId = null, GridPoint? near = null) => YardTiles(yard)
        .Where(tile => map.IsPassable(tile) && (near is null || map.FootDistance(tile, near.Value) <= 1) && !animalWorld.Animals.Any(item => item.Id != animalId &&
            item.DiedTick is null && item.Position == tile) && !inhabitants.Values.Any(person => person.Position == tile))
        .OrderBy(tile => tile.Y).ThenBy(tile => tile.X).Cast<GridPoint?>().FirstOrDefault();

    private void SeedWildAnimals()
    {
        if (animalWorld.Seeded || geographyOptions is null) return;
        var blocked = worldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(map.Resources.Select(item => item.Position)).Concat(RoadAndBridgeTiles()).ToHashSet();
        var greens = map.Resources.Where(item => item.NaturalObjectKind == "wild_greens")
            .OrderBy(item => AnimalKey(worldSeed + ":animal-herd:" + item.Id), StringComparer.Ordinal).ToArray();
        var shores = FreshWaterShorePositions();
        var result = new List<AnimalState>();
        var usedForage = new HashSet<string>(StringComparer.Ordinal);
        foreach (var species in AnimalRules.Species)
        {
            var source = greens.FirstOrDefault(item => !usedForage.Contains(item.Id) && shores.Any(shore => map.FootDistance(item.Position, shore) <= 6) &&
                map.FootNeighbors(item.Position).Count(tile => map.IsBuildable(tile) && !blocked.Contains(tile) && !result.Any(animal => animal.Position == tile)) >= 2);
            if (source is null) continue;
            usedForage.Add(source.Id);
            var sites = map.FootNeighbors(source.Position).Where(tile => map.IsBuildable(tile) && !blocked.Contains(tile) &&
                    !result.Any(item => item.Position == tile)).OrderBy(tile => tile.Y).ThenBy(tile => tile.X).Take(2).ToArray();
            for (var index = 0; index < sites.Length; index++)
                result.Add(new("animal-wild-" + species.Id + "-" + index, species.Id + " " + (index + 1),
                    species.Id, index == 0 ? "female" : "male", -(long)species.AdultDays * AnimalDayTicks,
                    sites[index], "herd-" + species.Id));
        }
        animalWorld = new(true, result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), []);
        AppendEvent("animals_arrived", $"wild:{result.Count}");
    }

    private void AdvanceAnimals(long tick)
    {
        SeedWildAnimals();
        foreach (var original in animalWorld.Animals.ToArray())
        {
            var animal = Animal(original.Id)!;
            var definition = AnimalRules.Definition(animal.Species);
            if (animal.DiedTick is not null) continue;
            if (tick - animal.BornTick >= (long)definition.LifespanDays * AnimalDayTicks)
            {
                EndAnimalRide(animal, "old_age");
                if (animal.SaddleReservationId is { } saddleHeld)
                    ApplyInventoryTransition(inventory => InventoryFixture.ReleaseReservation(inventory, saddleHeld, "animal_died"));
                animal = Animal(animal.Id)!;
                ClearAnimalProduct(animal, discard: true);
                if (animal.Species != "chicken" && animal.HouseholdId is { } owner)
                    ApplyInventoryTransition(inventory => InventoryFixture.AddLot(inventory,
                        "animal-hide-" + AnimalKey(animal.Id), "hide", owner, 1, tick,
                        groundPosition: new(animal.Position.X, animal.Position.Y)));
                SetAnimal(animal with { DiedTick = tick, Pregnancy = null, RiderId = null, LeaderId = null, SaddleLotId = null, SaddleReservationId = null, TamingWork = null,
                    LeadDestination = null, ReadyProductLotId = null, ReadyProductReservationId = null, ProductProgressTicks = 0 });
                AppendEvent("animal_died", animal.Id + ":old_age", animal.Position);
                continue;
            }
            if (animal.HouseholdId is null) animal = AdvanceWildAnimalCare(animal, tick);
            if (animal.TamingWork is { } taming && (!AdultResident(taming.ActorId) ||
                    inhabitants[taming.ActorId].Position != animal.Position))
                SetAnimal(animal = animal with { TamingWork = null });
            if (animal.RiderId is { } rider && (!MayRideAnimal(rider, animal) || !AnimalRules.HasCare(animal, tick) ||
                    !inhabitants.TryGetValue(rider, out var person) || person.Position != animal.Position || PassengerBoat(rider) is not null))
                EndAnimalRide(animal, "care_or_permission_lost");
            animal = Animal(animal.Id)!;
            if (animal.LeaderId is { } leader && (!AdultResident(leader) || HouseholdFor(leader) != animal.HouseholdId ||
                    !inhabitants.TryGetValue(leader, out var leadingPerson) || leadingPerson.Position != animal.Position))
                SetAnimal(animal = animal with { LeaderId = null, LeadDestination = null });
            if (animal.ReadyProductLotId is { } lotId && society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == lotId)
                    is not { FreshnessBasisPoints: > 0, ConditionBasisPoints: > 0 })
            {
                ClearAnimalProduct(animal, discard: true);
                SetAnimal(animal = animal with { ReadyProductLotId = null, ReadyProductReservationId = null });
            }
            if (!AnimalRules.HasCare(animal, tick - 1)) continue;
            if (animal.HouseholdId is not null && AnimalRules.IsAdult(animal, tick, AnimalDayTicks) &&
                AnimalRules.HasProduct(animal) && animal.ReadyProductLotId is null)
            {
                var progress = animal.ProductProgressTicks + 1;
                if (progress >= definition.ProductDays * AnimalDayTicks)
                {
                    var productId = "animal-product-" + AnimalKey(animal.Id + ":" + tick);
                    var reservationId = productId + "-held";
                    ApplyInventoryTransition(inventory => InventoryFixture.Reserve(InventoryFixture.AddLot(inventory,
                        productId, definition.Product!, animal.HouseholdId, definition.ProductQuantity, tick,
                        groundPosition: new(animal.Position.X, animal.Position.Y)), reservationId,
                        animal.HouseholdId, productId, definition.ProductQuantity, "animal-product:" + animal.Id, long.MaxValue));
                    animal = animal with { ProductProgressTicks = 0, ReadyProductLotId = productId, ReadyProductReservationId = reservationId };
                    AppendEvent("animal_product_ready", animal.Id + ":" + definition.Product, animal.Position);
                }
                else animal = animal with { ProductProgressTicks = progress };
                SetAnimal(animal);
            }
            if (animal.Pregnancy is { } pregnancy)
            {
                var progress = pregnancy.ProgressTicks + 1;
                if (progress >= definition.GestationDays * AnimalDayTicks)
                {
                    var yard = animal.YardId is null ? null : worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == animal.YardId);
                    var position = yard is null ? map.FootNeighbors(animal.Position).Where(tile => map.IsBuildable(tile) &&
                        !animalWorld.Animals.Any(item => item.DiedTick is null && item.Position == tile)).Cast<GridPoint?>().FirstOrDefault()
                        : FreeAnimalYardTile(yard, near: animal.Position);
                    if (position is null) continue;
                    var childId = "animal-born-" + AnimalKey(animal.Id + ":" + pregnancy.StartedTick);
                    SetAnimal(new(childId, definition.Id + " " + childId[^4..], definition.Id,
                        AnimalKey(childId)[0] % 2 == 0 ? "female" : "male", tick, position.Value, animal.HerdId,
                        animal.HouseholdId, animal.YardId));
                    animal = animal with { Pregnancy = null, BreedingReadyTick = tick + (long)AnimalRules.RecoveryDays * AnimalDayTicks };
                    AppendEvent("animal_born", childId + ":" + animal.Id, position);
                }
                else animal = animal with { Pregnancy = pregnancy with { ProgressTicks = progress } };
                SetAnimal(animal);
            }
            else if (animal.Sex == "female" && AnimalRules.IsAdult(animal, tick, AnimalDayTicks) && tick >= animal.BreedingReadyTick &&
                     !animalWorld.Offers.Any(offer => offer.AnimalId == animal.Id) && CanStartAnimalPregnancy(animal, tick))
            {
                var male = animalWorld.Animals.FirstOrDefault(item => item.Species == animal.Species && item.Sex == "male" &&
                    item.DiedTick is null && item.HouseholdId == animal.HouseholdId && item.YardId == animal.YardId &&
                    item.HerdId == animal.HerdId && AnimalRules.IsAdult(item, tick, AnimalDayTicks) && AnimalRules.HasCare(item, tick));
                if (male is not null)
                {
                    SetAnimal(animal = animal with { Pregnancy = new(male.Id, 0, tick) });
                    AppendEvent("animal_breeding_started", animal.Id + ":" + male.Id, animal.Position);
                }
            }
            WanderAnimal(animal, tick);
        }
        ReconcileAnimalCustody();
    }

    private void ReconcileAnimalCustody()
    {
        foreach (var animal in animalWorld.Animals.ToArray())
        {
            if (animal.RiderId is { } rider && (!MayRideAnimal(rider, animal) || !AnimalRules.HasCare(animal, WorldTick) ||
                    !inhabitants.TryGetValue(rider, out var person) || person.Position != animal.Position))
                EndAnimalRide(animal, "care_or_permission_lost");
            if (animal.LeaderId is { } leader && (!AnimalHouseholdMember(leader, animal) ||
                    inhabitants[leader].Position != animal.Position))
                SetAnimal(Animal(animal.Id)! with { LeaderId = null, LeadDestination = null });
        }
        animalWorld = animalWorld with { Offers = animalWorld.Offers.Where(ValidAnimalOffer).ToArray(),
            SupplyTrips = animalWorld.SupplyTrips.Where(trip => AdultResident(trip.ActorId) &&
                worldSimulation.Buildings.Any(yard => yard.InstanceId == trip.YardId && MaySupplyAnimalYard(trip.ActorId, yard)) &&
                society.Checkpoint.Inventory.Lots.Any(lot => lot.Id == trip.LotId && PersonalEquipmentRules.IsCarried(lot, trip.ActorId))).ToArray() };
    }

    private bool CanStartAnimalPregnancy(AnimalState animal, long tick)
    {
        if (animal.HouseholdId is null)
            return animalWorld.Animals.Where(item => item.DiedTick is null && item.HouseholdId is null && item.HerdId == animal.HerdId)
                .Sum(item => 1 + (item.Pregnancy is null ? 0 : 1)) < AnimalRules.PopulationCap;
        var yard = AssignedAnimalYard(animal);
        if (yard is null || !HasAnimalSpace(animal.HouseholdId, yard)) return false;
        var need = animalWorld.Animals.Where(item => item.HouseholdId == animal.HouseholdId && item.DiedTick is null)
            .Sum(item => AnimalRules.Definition(item.Species).DailyFeed * ((AnimalRules.HasCare(item, tick) ? 0 : 1) + (item.Pregnancy is null ? 0 : 1))) +
            AnimalRules.Definition(animal.Species).DailyFeed;
        var stock = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == animal.HouseholdId &&
            lot.StorageBuildingId == yard.InstanceId && AnimalRules.IsFeed(lot.ItemKind)).Sum(AvailableLotQuantity);
        var waterNeed = animalWorld.Animals.Where(item => item.HouseholdId == animal.HouseholdId && item.DiedTick is null)
            .Sum(item => AnimalRules.Definition(item.Species).DailyWater * ((AnimalRules.HasCare(item, tick) ? 0 : 1) + (item.Pregnancy is null ? 0 : 1))) +
            AnimalRules.Definition(animal.Species).DailyWater;
        return stock >= need && society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == animal.HouseholdId &&
            lot.StorageBuildingId == yard.InstanceId && lot.ItemKind == InventoryContainerRules.FreshWater).Sum(AvailableLotQuantity) >= waterNeed;
    }

    private void ClearAnimalProduct(AnimalState animal, bool discard)
    {
        if (animal.ReadyProductReservationId is { } reservationId && society.Checkpoint.Inventory.Reservations.Any(item =>
                item.Id == reservationId && item.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed))
            ApplyInventoryTransition(inventory => InventoryFixture.ReleaseReservation(inventory, reservationId, "animal_product_released"));
        if (discard && animal.ReadyProductLotId is { } lotId && society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId) is { } lot)
            ApplyInventoryTransition(inventory => InventoryFixture.Discard(inventory, lot.OwnerId, lot.Id, lot.Quantity));
    }

    private void WanderAnimal(AnimalState animal, long tick)
    {
        if (animal.HouseholdId is null || animal.RiderId is not null || animal.LeaderId is not null || animal.ReadyProductLotId is not null ||
            tick % 12 != 0 || AssignedAnimalYard(animal) is not { } yard || !YardTiles(yard).Contains(animal.Position)) return;
        var tiles = YardTiles(yard).ToHashSet();
        var next = map.FootNeighbors(animal.Position).Where(tile => tiles.Contains(tile) && map.CanFootStep(animal.Position, tile) &&
            !inhabitants.Values.Any(person => person.Position == tile) && !animalWorld.Animals.Any(item => item.DiedTick is null && item.Id != animal.Id && item.Position == tile))
            .OrderBy(tile => AnimalKey(animal.Id + ":" + tick + ":" + tile), StringComparer.Ordinal).Cast<GridPoint?>().FirstOrDefault();
        if (next is { } position) MoveAnimal(animal, position);
    }
}
