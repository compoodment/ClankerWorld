using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool CanRideAnimal(string actor, HouseholdAnimal animal) => AdultResident(actor) &&
        animal.Kind == LivestockKind.Horse && animal.NaturalDeathTick is null &&
        (HouseholdFor(actor) == animal.HouseholdId || animal.PermittedRiderIds?.Contains(actor, StringComparer.Ordinal) == true);

    public LivestockActionResult AllowHorseRiding(string actor, string animalId, string rider, bool allowed)
    {
        gate.Wait();
        try { return AllowHorseRidingCore(actor, animalId, rider, allowed); }
        finally { gate.Release(); }
    }

    private LivestockActionResult AllowHorseRidingCore(string actor, string animalId, string rider, bool allowed)
    {
        var animal = livestock.FirstOrDefault(item => item.Id == animalId);
        if (animal is null || !CanCareForAnimal(actor, animal) || animal.Kind != LivestockKind.Horse ||
            !AdultResident(rider) || HouseholdFor(rider) == animal.HouseholdId)
            return new(false, "A household adult grants or revokes a named outside adult's horse permission.");
        var ids = (animal.PermittedRiderIds ?? []).Where(id => id != rider).ToList();
        if (allowed)
        {
            if (ids.Count >= 16) return new(false, "Revoke an earlier rider permission first.");
            ids.Add(rider);
        }
        ReplaceAnimal(animal with
        {
            PermittedRiderIds = ids.Count == 0 ? null : ids.Order(StringComparer.Ordinal).ToArray(),
            RiderId = !allowed && animal.RiderId == rider ? null : animal.RiderId
        });
        AppendEvent(allowed ? "horse_permission_granted" : "horse_permission_revoked", $"{actor}:{animalId}:{rider}");
        return new(true);
    }

    public LivestockActionResult RideHorse(string actor, string animalId, bool mount)
    {
        gate.Wait();
        try { return RideHorseCore(actor, animalId, mount); }
        finally { gate.Release(); }
    }

    private LivestockActionResult RideHorseCore(string actor, string animalId, bool mount)
    {
        var animal = livestock.FirstOrDefault(item => item.Id == animalId);
        if (animal is null || !AdultResident(actor)) return new(false, "Choose a living adult and an actual horse.");
        if (!mount)
        {
            if (animal.RiderId != actor) return new(false, "Only its current rider dismounts this horse.");
            ReplaceAnimal(animal with { RiderId = null });
            AppendEvent("horse_dismounted", $"{actor}:{animalId}:chosen");
            return new(true);
        }
        if (!CanRideAnimal(actor, animal) || !LivestockRules.HasCare(animal, WorldTick))
            return new(false, "A fed, watered and cared-for horse needs its household's riding permission.");
        if (animal.RiderId is not null || livestock.Any(item => item.RiderId == actor) ||
            !IsWithinInteractionRange(inhabitants[actor].Position, animal.Position, 1))
            return new(false, "Reach an available horse and dismount any previous horse first.");
        ReplaceAnimal(animal with { RiderId = actor, Position = inhabitants[actor].Position });
        MoveRiddenHorse(actor);
        AppendEvent("horse_mounted", $"{actor}:{animalId}");
        return new(true);
    }

    private bool CanRetrieveHorseCargo(string actor, InventoryLot lot) => lot.OwnerId == actor ||
        AdultResident(actor) && HouseholdFor(actor) == lot.OwnerId;

    public LivestockActionResult LoadHorseCargo(string actor, string animalId, string lotId, int quantity)
    {
        gate.Wait();
        try { return LoadHorseCargoCore(actor, animalId, lotId, quantity); }
        finally { gate.Release(); }
    }

    private LivestockActionResult LoadHorseCargoCore(string actor, string animalId, string lotId, int quantity)
    {
        var animal = livestock.FirstOrDefault(item => item.Id == animalId);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (animal is null || lot is null || !CanRideAnimal(actor, animal) || !LivestockRules.HasCare(animal, WorldTick) ||
            !AnimalSupplyAtHand(actor, lot) || lot.ContainerLotId is not null || lot.ItemKind == "handcart" ||
            quantity <= 0 || AvailableLotQuantity(lot) < quantity ||
            !IsWithinInteractionRange(inhabitants[actor].Position, animal.Position, 1))
            return new(false, "Bring owned unreserved cargo or its whole vessel to a permitted, cared-for horse.");
        var used = society.Checkpoint.Inventory.Lots.Where(item => item.AnimalId == animalId).Sum(item => (long)item.Quantity);
        if (InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lotId, quantity) > LivestockRules.HorseCargoCapacity - used)
            return new(false, "The horse's cargo space is full, including all vessel contents.");
        try
        {
            ApplyInventoryTransition(inventory => InventoryFixture.LoadAnimal(inventory, $"horse-load:{WorldTick}:{nextEventId}",
                lot.OwnerId, lotId, quantity, animalId, new(animal.Position.X, animal.Position.Y)));
        }
        catch (InvalidOperationException failure) { return new(false, failure.Message); }
        AppendEvent("horse_cargo_loaded", $"{actor}:{animalId}:{lotId}:{quantity}");
        return new(true);
    }

    public LivestockActionResult UnloadHorseCargo(string actor, string animalId, string lotId, int quantity,
        string? destinationBuildingId = null)
    {
        gate.Wait();
        try { return UnloadHorseCargoCore(actor, animalId, lotId, quantity, destinationBuildingId); }
        finally { gate.Release(); }
    }

    private LivestockActionResult UnloadHorseCargoCore(string actor, string animalId, string lotId, int quantity,
        string? destinationBuildingId = null)
    {
        var animal = livestock.FirstOrDefault(item => item.Id == animalId);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (!AdultResident(actor) || animal is null || lot is null || lot.AnimalId != animalId ||
            lot.ContainerLotId is not null || !CanRetrieveHorseCargo(actor, lot) || quantity <= 0 || AvailableLotQuantity(lot) < quantity ||
            !IsWithinInteractionRange(inhabitants[actor].Position, animal.Position, 1))
            return new(false, "Reach and retrieve your actual cargo; riding permission does not transfer cargo ownership.");
        var destination = destinationBuildingId is null ? null : worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == destinationBuildingId);
        var load = InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lotId, quantity);
        if (destinationBuildingId is not null && (destination is null || destination.HouseholdId != HouseholdFor(actor) ||
            inhabitants[actor].Position != destination.Position || StorageRoom(destinationBuildingId) < load) ||
            destinationBuildingId is null && CarryingRoom(actor) < load)
            return new(false, "Make actual room in your hands or a reachable household building first.");
        try
        {
            ApplyInventoryTransition(inventory => InventoryFixture.UnloadAnimal(inventory, $"horse-unload:{WorldTick}:{nextEventId}",
                lot.OwnerId, destination?.HouseholdId ?? actor, lotId, quantity, animalId, destinationBuildingId));
        }
        catch (InvalidOperationException failure) { return new(false, failure.Message); }
        AppendEvent("horse_cargo_unloaded", $"{actor}:{animalId}:{lotId}:{quantity}");
        return new(true);
    }

    private void MoveRiddenHorse(string actor)
    {
        var animal = livestock.FirstOrDefault(item => item.RiderId == actor);
        if (animal is null) return;
        if (!AdultResident(actor) || !inhabitants.TryGetValue(actor, out var rider))
        {
            ReplaceAnimal(animal with { RiderId = null });
            return;
        }
        var position = rider.Position;
        ReplaceAnimal(animal with { Position = position });
        if (society.Checkpoint.Inventory.Lots.Any(item => item.AnimalId == animal.Id && item.GroundPosition != new InventoryGroundPosition(position.X, position.Y)))
            ApplyInventoryTransition(inventory => inventory with
            {
                Lots = inventory.Lots.Select(item => item.AnimalId == animal.Id
                ? item with { GroundPosition = new(position.X, position.Y) } : item).ToArray()
            });
    }

    private void AddHorseCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var animal in livestock.Where(item => item.Kind == LivestockKind.Horse))
        {
            if (CanCareForAnimal(actor, animal))
            {
                foreach (var rider in inhabitants.Keys.Where(id => AdultResident(id) && HouseholdFor(id) != animal.HouseholdId &&
                             !(animal.PermittedRiderIds ?? []).Contains(id, StringComparer.Ordinal) &&
                             IsWithinInteractionRange(inhabitants[actor].Position, inhabitants[id].Position, 2)).Order(StringComparer.Ordinal).Take(4))
                    candidates.Add(new($"horse_allow:{animal.Id}|{rider}",
                        $"Allow {society.Checkpoint.GetInhabitant(rider).Name} to ride the household horse without changing its owner.", 110, rider));
                foreach (var rider in animal.PermittedRiderIds ?? [])
                    candidates.Add(new($"horse_revoke:{animal.Id}|{rider}", "Revoke this named horse-riding permission without confiscating cargo.", 110, rider));
            }
            if (CanRideAnimal(actor, animal) && LivestockRules.HasCare(animal, WorldTick) && animal.RiderId is null &&
                !livestock.Any(item => item.RiderId == actor) && FindUnoccupiedRoute(actor, inhabitants[actor].Position, animal.Position, 1).Count > 0)
                candidates.Add(new($"horse_mount:{animal.Id}", "Reach and ride a cared-for horse with household permission.", 40, animal.Id));
            if (animal.RiderId == actor) candidates.Add(new($"horse_dismount:{animal.Id}", "Dismount and leave the horse and its actual cargo here.", 90, animal.Id));
            if (animal.RiderId == actor && CarryEquipmentRules.Load(society.Checkpoint.Inventory, actor) > 24 &&
                HorseLoadCandidate(actor, animal) is { } load)
                candidates.Add(new($"horse_load:{animal.Id}|{load.Id}", "Load personally carried bulk goods onto this permitted horse.", 15, animal.Id));
            foreach (var lot in society.Checkpoint.Inventory.Lots.Where(item => item.AnimalId == animal.Id && item.ContainerLotId is null && CanRetrieveHorseCargo(actor, item)))
            {
                var home = HouseForHousehold(HouseholdFor(actor));
                var atHome = home is not null && inhabitants[actor].Position == home.Position &&
                    InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, lot.Quantity) <= StorageRoom(home.InstanceId);
                if (FindUnoccupiedRoute(actor, inhabitants[actor].Position, animal.Position, 1).Count > 0 &&
                    (atHome || !CanRideAnimal(actor, animal) && InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, lot.Quantity) <= CarryingRoom(actor)))
                    candidates.Add(new($"horse_unload:{animal.Id}|{lot.Id}", "Retrieve owned cargo from this horse into local household stock or your hands.", 16, animal.Id));
            }
        }
    }

    private InventoryLot? HorseLoadCandidate(string actor, HouseholdAnimal animal)
    {
        var room = LivestockRules.HorseCargoCapacity - society.Checkpoint.Inventory.Lots.Where(item => item.AnimalId == animal.Id).Sum(item => (long)item.Quantity);
        return society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ContainerLotId is null &&
                lot.GroundPosition is null && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                !IsEquippedLot(actor, lot.Id) && lot.ItemKind is "wood" or "stone" or "iron_ore" or "grain" or "wool" or "hide" &&
                AvailableLotQuantity(lot) > 0 && Math.Min(8, AvailableLotQuantity(lot)) <= room)
            .OrderByDescending(lot => lot.Quantity).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private void ApplyHorseCandidate(string actor, string candidate)
    {
        var separator = candidate.IndexOf(':');
        var action = candidate[..separator];
        var parts = candidate[(separator + 1)..].Split('|', 2);
        var animal = livestock.FirstOrDefault(item => item.Id == parts[0]);
        if (animal is null) return;
        if (action is "horse_allow" or "horse_revoke")
        {
            if (parts.Length == 2) AllowHorseRidingCore(actor, animal.Id, parts[1], action == "horse_allow");
            return;
        }
        var person = inhabitants[actor];
        if (!IsWithinInteractionRange(person.Position, animal.Position, 1)) { MoveToward(actor, person, animal.Position, "horse", 1); return; }
        if (action == "horse_mount") RideHorseCore(actor, animal.Id, true);
        else if (action == "horse_dismount") RideHorseCore(actor, animal.Id, false);
        else if (action == "horse_load" && parts.Length == 2 && HorseLoadCandidate(actor, animal) is { } load && load.Id == parts[1])
            LoadHorseCargoCore(actor, animal.Id, load.Id, Math.Min(8, AvailableLotQuantity(load)));
        else if (action == "horse_unload" && parts.Length == 2 && society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == parts[1]) is { } lot)
        {
            var home = HouseForHousehold(HouseholdFor(actor));
            UnloadHorseCargoCore(actor, animal.Id, lot.Id, lot.Quantity,
                home is not null && person.Position == home.Position && InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, lot.Quantity) <= StorageRoom(home.InstanceId)
                    ? home.InstanceId : null);
        }
    }
}
