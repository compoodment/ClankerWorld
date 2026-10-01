using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private List<HouseholdAnimal> livestock = [];
    public IReadOnlyList<HouseholdAnimal> Livestock => livestock.ToArray();

    // Acquisition policy supplies this authority transition; it does not create starter animals or a tame/buy rule.
    public LivestockActionResult PlaceAcquiredLivestock(string id, LivestockKind kind, string householdId,
        GridPoint position, string acquisitionId)
    {
        gate.Wait();
        try
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || string.IsNullOrWhiteSpace(acquisitionId) ||
                acquisitionId != acquisitionId.Trim() || !Enum.IsDefined(kind) || livestock.Any(animal => animal.Id == id) ||
                !society.Checkpoint.Households.Any(household => household.Id == householdId) || !map.IsPassable(position))
                return new(false, "Acquisition must identify one real animal, its household, origin and reachable land position.");
            livestock.Add(new(id, kind, householdId, position, acquisitionId, WorldTick, 0, 0, 0, WorldTick));
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("livestock_acquired", $"{id}|{householdId}|{kind}|{acquisitionId}");
            return new(true);
        }
        finally { gate.Release(); }
    }

    private bool CanCareForAnimal(string actor, HouseholdAnimal animal) => AdultResident(actor) &&
        HouseholdFor(actor) == animal.HouseholdId && animal.NaturalDeathTick is null;

    public LivestockActionResult RecordNaturalLivestockDeath(string animalId)
    {
        gate.Wait();
        try
        {
            var animal = livestock.FirstOrDefault(item => item.Id == animalId);
            if (animal is null || animal.NaturalDeathTick is not null) return new(false, "Record one actual living animal's natural death.");
            ReplaceAnimal(LivestockRules.RecordNaturalDeath(animal, WorldTick));
            AppendEvent("livestock_natural_death", animalId);
            return new(true);
        }
        finally { gate.Release(); }
    }

    private bool AnimalSupplyOwned(string actor, InventoryLot lot) => lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor);

    private bool AnimalSupplyAtHand(string actor, InventoryLot lot) => AnimalSupplyOwned(actor, lot) &&
        lot.GroundPosition is null && lot.DeliveryBuildingId is null &&
        (lot.OwnerId == actor && lot.StorageBuildingId is null || lot.OwnerId == HouseholdFor(actor) &&
         lot.StorageBuildingId is { } siteId && worldSimulation.Buildings.Any(site => site.InstanceId == siteId &&
             site.HouseholdId == lot.OwnerId && site.Position == inhabitants[actor].Position));

    private IEnumerable<InventoryLot> AnimalSupplies(string actor, bool water) => society.Checkpoint.Inventory.Lots
        .Where(lot => AnimalSupplyOwned(actor, lot) && lot.GroundPosition is null && lot.DeliveryBuildingId is null &&
            AvailableLotQuantity(lot) > 0 && (water
                ? lot.ItemKind == "water" && lot.ContainerLotId is not null
                : lot.ContainerLotId is null && lot.ItemKind is "grain" or "wild_greens" or "cultivated_greens"))
        .Where(lot => water || AvailableLotQuantity(lot) > FarmPlantingReserve(lot.OwnerId, lot.ItemKind))
        .Where(lot => !water || society.Checkpoint.Inventory.Lots.Any(vessel => vessel.Id == lot.ContainerLotId &&
            vessel.ConditionBasisPoints > 0 && AvailableLotQuantity(vessel) == 1) &&
            !society.Checkpoint.Inventory.Reservations.Any(reservation => reservation.State is InventoryReservationState.Reserved or
                InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed &&
                society.Checkpoint.Inventory.Lots.Any(item => item.Id == reservation.LotId && item.ContainerLotId == lot.ContainerLotId)))
        .OrderBy(lot => lot.OwnerId == actor ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal);

    public LivestockActionResult CareForLivestock(string actor, string animalId, string care)
    {
        gate.Wait();
        try { return CareForLivestockCore(actor, animalId, care); }
        finally { gate.Release(); }
    }

    private LivestockActionResult CareForLivestockCore(string actor, string animalId, string care)
    {
        var animal = livestock.FirstOrDefault(item => item.Id == animalId);
        if (animal is null || !CanCareForAnimal(actor, animal)) return new(false, "Only an adult household member cares for its living animal.");
        if (!IsWithinInteractionRange(inhabitants[actor].Position, animal.Position, 1))
            return new(false, "Reach the animal with its care supplies first.");
        if (care is not ("feed" or "water" or "care")) return new(false, "Choose feed, water or care.");
        if (care != "care")
        {
            var supply = AnimalSupplies(actor, care == "water").FirstOrDefault(lot => AnimalSupplyAtHand(actor, lot));
            if (supply is null) return new(false, "Bring unreserved feed or actual jug water to the animal.");
            var reservation = $"animal-care:{WorldTick}:{nextEventId}:{animalId}:{care}";
            ApplyInventoryTransition(inventory => InventoryFixture.ConsumeReservation(
                InventoryFixture.Reserve(inventory, reservation, supply.OwnerId, supply.Id, 1, "livestock_care", WorldTick), reservation));
        }
        var updated = care switch
        {
            "feed" => animal with { FedUntilTick = WorldTick + LivestockRules.CareTicks },
            "water" => animal with { WateredUntilTick = WorldTick + LivestockRules.CareTicks },
            _ => animal with { CaredUntilTick = WorldTick + LivestockRules.CareTicks },
        };
        ReplaceAnimal(updated);
        AppendEvent("livestock_cared_for", $"{actor}:{animal.Id}:{care}");
        return new(true);
    }

    private void ReplaceAnimal(HouseholdAnimal animal)
    {
        livestock[livestock.FindIndex(item => item.Id == animal.Id)] = animal;
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private void AdvanceLivestock(long targetTick)
    {
        foreach (var animal in livestock.ToArray())
        {
            var cared = LivestockRules.HasCare(animal, targetTick);
            if (animal.RiderId is { } rider && (!cared || !CanRideAnimal(rider, animal)))
            {
                ReplaceAnimal(animal with { RiderId = null });
                AppendEvent("horse_dismounted", $"{rider}:{animal.Id}:care_or_permission");
            }
            if (LivestockRules.Product(animal.Kind) is not { } product || animal.NaturalDeathTick is not null) continue;
            // Pause the production clock during missed care. No catch-up yield or uncollected infinite stock.
            if (animal.PendingProductQuantity > 0)
            {
                if (FoodItems.IsPerishable(product) && (targetTick - animal.LastProductTick) * FoodItems.FreshnessLoss(product) >= 10_000)
                {
                    ReplaceAnimal(animal with { PendingProductQuantity = 0, LastProductTick = targetTick });
                    AppendEvent("livestock_product_spoiled", $"{animal.Id}:{product}");
                }
            }
            else if (!cared)
                ReplaceAnimal(livestock.Single(item => item.Id == animal.Id) with { LastProductTick = targetTick });
            else if (targetTick - animal.LastProductTick >= LivestockRules.ProductTicks)
            {
                ReplaceAnimal(animal with { PendingProductQuantity = LivestockRules.ProductQuantity(animal.Kind), LastProductTick = targetTick });
                AppendEvent("livestock_product_ready", $"{animal.Id}:{product}");
            }
        }
    }

    private static string? AnimalProduct(HouseholdAnimal animal) => animal.NaturalDeathTick is not null
        ? !animal.HideCollected && animal.Kind != LivestockKind.Chicken ? "hide" : null
        : animal.PendingProductQuantity > 0 ? LivestockRules.Product(animal.Kind) : null;

    private bool MilkJugIsUnreserved(string vesselId) => society.Checkpoint.Inventory.Lots.Any(lot => lot.Id == vesselId && AvailableLotQuantity(lot) == 1) &&
        !society.Checkpoint.Inventory.Reservations.Any(reservation =>
            (reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed) &&
            society.Checkpoint.Inventory.Lots.Any(lot => lot.Id == reservation.LotId && (lot.Id == vesselId || lot.ContainerLotId == vesselId)));

    private InventoryLot? MilkVessel(string actor, int amount) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == "water_jug" && lot.ContainerCapacity > 0 &&
            lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
            lot.ContainerLotId is null && AvailableLotQuantity(lot) == 1 && lot.ConditionBasisPoints > 0 &&
            InventoryFixture.ContainerRoom(society.Checkpoint.Inventory, lot.Id) >= amount &&
            society.Checkpoint.Inventory.Lots.Where(item => item.ContainerLotId == lot.Id).All(item => item.ItemKind == "milk"))
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    public LivestockActionResult CollectLivestockProduct(string actor, string animalId)
    {
        gate.Wait();
        try { return CollectLivestockProductCore(actor, animalId); }
        finally { gate.Release(); }
    }

    private LivestockActionResult CollectLivestockProductCore(string actor, string animalId)
    {
        var animal = livestock.FirstOrDefault(item => item.Id == animalId);
        if (!AdultResident(actor) || animal is null || HouseholdFor(actor) != animal.HouseholdId || AnimalProduct(animal) is not { } product)
            return new(false, "Collect an actual product of your household's animal.");
        if (!IsWithinInteractionRange(inhabitants[actor].Position, animal.Position, 1)) return new(false, "Reach the animal first.");
        if (product == "wool" && CarriedTool(actor, ToolKind.Knife) is null)
            return new(false, "Carry a usable knife for shearing first.");
        var quantity = product == "hide" ? 1 : animal.PendingProductQuantity;
        if (CarryingRoom(actor) < quantity || product == "milk" && MilkVessel(actor, quantity) is null)
            return new(false, "Make room for the product; milk needs an empty or milk-filled jug.");
        var vessel = product == "milk" ? MilkVessel(actor, quantity) : null;
        var lotId = $"animal-product:{animal.Id}:{WorldTick}:{nextEventId}";
        if (product == "wool") UseTool(actor, ToolKind.Knife);
        ApplyInventoryTransition(inventory => InventoryFixture.AddLot(inventory, lotId, product, actor, quantity,
            freshnessBasisPoints: FoodItems.IsPerishable(product)
                ? checked((int)Math.Max(1, 10_000 - (WorldTick - animal.LastProductTick) * FoodItems.FreshnessLoss(product))) : 10_000,
            containerLotId: vessel?.Id));
        var destination = product is "wool" or "hide"
            ? worldSimulation.Buildings.FirstOrDefault(site => site.HouseholdId == animal.HouseholdId &&
                worldContent.Buildings.Single(definition => definition.CanonicalId == site.DefinitionId).Tags.Contains("tailor"))
            : HouseForHousehold(animal.HouseholdId);
        var rootId = vessel?.Id ?? lotId;
        if (destination is not null && StorageRoom(destination.InstanceId) >=
            InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, rootId, vessel is null ? quantity : 1))
            ApplyInventoryTransition(inventory => inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == rootId || lot.ContainerLotId == rootId
                    ? lot with { DeliveryBuildingId = destination.InstanceId } : lot).ToArray(),
            });
        ReplaceAnimal(animal with { PendingProductQuantity = 0, HideCollected = product == "hide" || animal.HideCollected, LastProductTick = WorldTick });
        AppendEvent("livestock_product_collected", $"{actor}:{animal.Id}:{product}:{lotId}:{quantity}");
        return new(true);
    }

    private bool PrepareAnimalSupply(string actor, bool water)
    {
        if (AnimalSupplies(actor, water).Any(lot => lot.OwnerId == actor && AnimalSupplyAtHand(actor, lot))) return true;
        var source = AnimalSupplies(actor, water).FirstOrDefault(lot => lot.StorageBuildingId is { } id &&
            worldSimulation.Buildings.FirstOrDefault(site => site.InstanceId == id) is { } building &&
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, building.Position, 0).Count > 0 &&
            (water ? InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.ContainerLotId!, 1) : 1) <= CarryingRoom(actor));
        if (source is null) return false;
        var root = water ? society.Checkpoint.Inventory.GetLot(source.ContainerLotId!) : source;
        var quantity = water ? 1 : Math.Min(CarryingRoom(actor), Math.Min(4, AvailableLotQuantity(root) - FarmPlantingReserve(root.OwnerId, root.ItemKind)));
        if (quantity <= 0 || InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, root.Id, quantity) > CarryingRoom(actor)) return false;
        var site = worldSimulation.Buildings.Single(item => item.InstanceId == root.StorageBuildingId);
        var person = inhabitants[actor];
        if (person.Position != site.Position)
        {
            MoveToward(actor, person, site.Position, "livestock_supply");
            return false;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"animal-supply:{WorldTick}:{nextEventId}:{actor}",
            root.OwnerId, actor, root.Id, quantity, "livestock_supply_collected"));
        return true;
    }

    private bool CanObtainAnimalSupply(string actor, bool water) => AnimalSupplies(actor, water).Any(lot =>
        AnimalSupplyAtHand(actor, lot) || lot.StorageBuildingId is { } id &&
        worldSimulation.Buildings.FirstOrDefault(site => site.InstanceId == id) is { } building &&
        FindUnoccupiedRoute(actor, inhabitants[actor].Position, building.Position, 0).Count > 0 &&
        (water ? InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.ContainerLotId!, 1) : 1) <= CarryingRoom(actor));

    private void AddLivestockCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        foreach (var animal in livestock.Where(item => item.HouseholdId == HouseholdFor(actor))
                     .OrderBy(item => map.FootDistance(inhabitants[actor].Position, item.Position)).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (FindUnoccupiedRoute(actor, inhabitants[actor].Position, animal.Position, 1).Count == 0) continue;
            if (animal.NaturalDeathTick is null)
            {
                if (animal.FedUntilTick <= WorldTick + LivestockRules.ProductTicks && CanObtainAnimalSupply(actor, false))
                    candidates.Add(new($"animal_feed:{animal.Id}", "Bring local household feed to its animal.", 17, animal.Id));
                if (animal.WateredUntilTick <= WorldTick + LivestockRules.ProductTicks && CanObtainAnimalSupply(actor, true))
                    candidates.Add(new($"animal_water:{animal.Id}", "Give the animal actual jug water.", 18, animal.Id));
                if (animal.CaredUntilTick <= WorldTick + LivestockRules.ProductTicks)
                    candidates.Add(new($"animal_care:{animal.Id}", "Care for the household animal.", 19, animal.Id));
            }
            if (AnimalProduct(animal) is { } product && CarryingRoom(actor) >= (product == "hide" ? 1 : animal.PendingProductQuantity) &&
                (product != "milk" || MilkVessel(actor, animal.PendingProductQuantity) is not null))
            {
                if (product == "wool" && CarriedTool(actor, ToolKind.Knife) is null)
                {
                    if (SharedTool(actor, ToolKind.Knife) is not null)
                        candidates.Add(new($"animal_shearing_tool:{animal.Id}", "Collect a household knife before shearing the sheep.", 20, animal.Id));
                }
                else candidates.Add(new($"animal_collect:{animal.Id}", "Collect the animal's local product and bring it to household stock.", 20, animal.Id));
            }
        }
        AddHorseCandidates(candidates, actor);
    }

    private void ApplyLivestockCandidate(string actor, PlaytestInhabitantState person, string candidate)
    {
        var separator = candidate.IndexOf(':');
        if (separator < 0) return;
        var action = candidate[..separator];
        var id = candidate[(separator + 1)..];
        if (action.StartsWith("horse_", StringComparison.Ordinal)) { ApplyHorseCandidate(actor, candidate); return; }
        var animal = livestock.FirstOrDefault(item => item.Id == id);
        if (animal is null || !AdultResident(actor) || animal.HouseholdId != HouseholdFor(actor)) return;
        if (action == "animal_shearing_tool" && SharedTool(actor, ToolKind.Knife) is { } tool)
        {
            CollectEquipment(actor, person, tool.ItemKind);
            return;
        }
        if (action is "animal_feed" or "animal_water" && !PrepareAnimalSupply(actor, action == "animal_water")) return;
        person = inhabitants[actor];
        if (!IsWithinInteractionRange(person.Position, animal.Position, 1))
        {
            MoveToward(actor, person, animal.Position, "livestock", 1);
            return;
        }
        if (action == "animal_collect") CollectLivestockProductCore(actor, id);
        else if (action is "animal_feed" or "animal_water" or "animal_care") CareForLivestockCore(actor, id, action[7..]);
    }
}
