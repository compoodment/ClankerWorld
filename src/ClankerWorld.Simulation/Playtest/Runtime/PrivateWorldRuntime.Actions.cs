using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void MoveToward(
        string inhabitantId,
        PlaytestInhabitantState state,
        GridPoint destination,
        string reason,
        int interactionRange = 0)
    {
        guardianPlacementActions.Add(inhabitantId);
        if (IsWithinInteractionRange(state.Position, destination, interactionRange))
        {
            AppendEvent("destination_reached", $"{inhabitantId}:{reason}");
            return;
        }

        if (state.TravelCooldownTicks > 0)
        {
            inhabitants[inhabitantId] = state with { TravelCooldownTicks = state.TravelCooldownTicks - 1 };
            KeepPlannedRoute(inhabitantId, reason, destination);
            return;
        }

        if (state.Departures is { Count: > 0 } && MovingCareGroup(inhabitantId).Any(id => id != inhabitantId &&
                inhabitants[id].GuardianPlacement is null &&
                !IsWithinInteractionRange(inhabitants[id].Position, state.Position, 2)))
        {
            RecordMovementBlocked(inhabitantId, state, "waiting_for_dependent");
            return;
        }
        if (AttachedHandcart(inhabitantId) is { } heldCart && !CanPullHandcart(heldCart))
        {
            ParkHandcart(inhabitantId, "equipment_unusable");
            RecordMovementBlocked(inhabitantId, state, "cart_unusable");
            return;
        }
        var route = FindUnoccupiedRoute(inhabitantId, state.Position, destination, interactionRange);
        if (route.Count < 2)
        {
            RecordMovementBlocked(inhabitantId, state, "no_route");
            return;
        }

        var next = route[1];
        var travelCost = TravelStepCost(inhabitantId, state.Position, next);
        MoveAttachedHandcart(inhabitantId, state.Position, next);
        inhabitants[inhabitantId] = state with
        {
            Position = next,
            MoveWaitTicks = 0,
            TravelCooldownTicks = (travelCost + 99) / 100 - 1 +
                SettlementIllnessRules.TravelDelayTicks(state.Survival?.IllnessBasisPoints ?? 0),
        };
        RecordPlannedRoute(inhabitantId, reason, destination, route);
        RecordBridgeTraffic(inhabitantId, state.Position, next);
        WearCarryAid(inhabitantId);
        AppendEvent("inhabitant_moved", $"{inhabitantId}:{state.Position.X},{state.Position.Y}->{next.X},{next.Y}:{reason}");
    }

    private List<GridPoint> FindUnoccupiedRoute(
        string inhabitantId,
        GridPoint origin,
        GridPoint destination,
        int interactionRange)
    {
        var occupied = inhabitants.Values
            .Where(item => item.InhabitantId != inhabitantId)
            .Select(item => item.Position)
            .ToHashSet();
        if (interactionRange == 0 && worldSimulation.Buildings.Any(building =>
                building.Position == destination &&
                building.HouseholdId is not null &&
                (building.HouseholdId == society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId ||
                 WeatherAt(building.Position) == WeatherKind.Storm && HasHouseGuestInvitation(inhabitantId, building.InstanceId) ||
                 CanEnterGuardianPlacementHouse(inhabitantId, building))))
            occupied.Remove(destination);
        // An occupied exact destination cannot be reached. Keep the household
        // sharing exception above, and avoid searching an entire map for it.
        if (interactionRange == 0 && origin != destination && occupied.Contains(destination))
            return [];
        if (AttachedHandcart(inhabitantId) is null)
            return SharedUnoccupiedRoute(origin, occupied, destination, interactionRange);

        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int> { [origin] = 0 };
        var predecessor = new Dictionary<GridPoint, GridPoint>();
        var order = 0;
        open.Enqueue(origin, (0, origin.Y, origin.X, order++));

        while (open.TryDequeue(out var current, out var priority))
        {
            if (priority.Cost != best[current])
                continue;
            if (IsWithinInteractionRange(current, destination, interactionRange))
            {
                var route = new List<GridPoint> { current };
                while (current != origin)
                {
                    current = predecessor[current];
                    route.Add(current);
                }

                route.Reverse();
                return route;
            }

            foreach (var next in map.FootNeighbors(current))
            {
                if (handcartHitches.Any(hitch => hitch.PullerId == inhabitantId) && !LegalHandcartStep(current, next) ||
                    occupied.Contains(next) ||
                    map.IsDiagonalFootStep(current, next) &&
                    (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                     occupied.Contains(new GridPoint(current.X, next.Y))))
                {
                    continue;
                }

                var cost = checked(priority.Cost + TravelStepCost(inhabitantId, current, next));
                if (best.TryGetValue(next, out var previous) && previous <= cost)
                    continue;
                best[next] = cost;
                predecessor[next] = current;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }

        return [];
    }

    private void RecordMovementBlocked(
        string inhabitantId,
        PlaytestInhabitantState state,
        string reason)
    {
        var waitTicks = checked(state.MoveWaitTicks + 1);
        inhabitants[inhabitantId] = state with { MoveWaitTicks = waitTicks };
        if (waitTicks == 1 || waitTicks % 30 == 0)
        {
            if (AttachedHandcart(inhabitantId) is not null || reason == "cart_unusable")
                AppendEvent("handcart_blocked", $"{inhabitantId}:{reason}");
            AppendEvent("movement_blocked", $"{inhabitantId}:{reason}:wait={waitTicks}");
        }
    }

    private bool IsWithinInteractionRange(GridPoint origin, GridPoint destination, int interactionRange) =>
        map.FootDistance(origin, destination) <= interactionRange;

    private IEnumerable<MapResource> EligibleFoodSources(string actor, GridPoint position) => map.Resources
        .Where(resource => resource.Kind is "food" or "fruit" &&
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available &&
            map.IsReachableOnFoot(position, resource.Position))
        .OrderBy(resource => map.FootDistance(resource.Position, position))
        .ThenBy(resource => resource.Id, StringComparer.Ordinal)
        .Where(resource => IsWithinInteractionRange(position, resource.Position, ResourceInteractionRange) ||
            FindUnoccupiedRoute(actor, position, resource.Position, ResourceInteractionRange).Count > 0);

    private MapResource? AvailableFoodSource(string actor, GridPoint position)
    {
        var room = FreeCarryCapacity(actor);
        return EligibleFoodSources(actor, position).FirstOrDefault(source => FoodHarvestCarryUnits(source) <= room);
    }

    private int MinimumFoodPickupCarryUnits(string actor, GridPoint position, bool forDependent)
    {
        var serving = forDependent ? AvailableHouseholdServing(actor, position) : AvailableSharedFood(actor);
        if (serving is not null)
            return 1;
        return EligibleFoodSources(actor, position).Select(FoodHarvestCarryUnits).DefaultIfEmpty(0).Min();
    }

    private static int FoodHarvestQuantity(MapResource source) => source.TreeKind == TreeGrowthRules.Orchard
        ? TreeGrowthRules.OrchardFruitPerPick : HarvestFoodYield;

    private static int FoodHarvestCarryUnits(MapResource source) => FoodHarvestQuantity(source) +
        (source.TreeKind == TreeGrowthRules.Orchard ? TreeGrowthRules.OrchardSeedsPerPick : 0);

    private FoodHarvestEffect? HarvestFood(
        string inhabitantId,
        PlaytestInhabitantState state,
        MapResource? requestedSource = null)
    {
        var source = requestedSource ?? AvailableFoodSource(inhabitantId, state.Position);
        if (source is null || !IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange))
        {
            AppendEvent("harvest_failed", $"{inhabitantId}:not_at_available_food");
            return null;
        }

        var harvestYield = FoodHarvestQuantity(source);
        if (FreeCarryCapacity(inhabitantId) < FoodHarvestCarryUnits(source))
        {
            AppendEvent("carrying_full", inhabitantId);
            return null;
        }
        var ecologyResource = worldSystems.Ecology.GetResource(source.Id);
        var harvest = EcologyRules.Harvest(ecologyResource, 1);
        if (!harvest.IsValid || harvest.Resource is null)
        {
            SyncEcologyResourceStates();
            AppendEvent("harvest_failed", $"{inhabitantId}:{harvest.Failure ?? "food_depleted"}");
            return null;
        }

        var harvested = source.TreeKind == "orchard" && harvest.Resource.Quantity == 0
            ? harvest.Resource with
            {
                NextRegenerationDay = WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex +
                    harvest.Resource.RegenerationIntervalDays,
            }
            : harvest.Resource;
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources
                    .Select(resource => resource.Id == source.Id ? harvested : resource)
                    .ToArray(),
            },
        };
        SyncEcologyResourceStates();
        var harvestedLotId = $"food:harvest:{WorldTick:D10}:{inhabitantId}";
        ApplyInventoryTransition(inventory => InventoryFixture.AddLot(
            inventory, harvestedLotId,
            source.Kind == "fruit" ? "fruit" : source.NaturalObjectKind == "wild_greens" ? "wild_greens" : "berries",
            inhabitantId,
            harvestYield,
            WorldTick));

        AppendEvent("food_harvested", $"{inhabitantId}:{harvestYield}");
        if (source.TreeKind == TreeGrowthRules.Orchard)
        {
            var lotId = $"orchard-seed:{WorldTick:D10}:{inhabitantId}";
            ApplyInventoryTransition(inventory => InventoryFixture.Reserve(
                InventoryFixture.AddLot(inventory, lotId, TreeGrowthRules.OrchardSeedItem, inhabitantId,
                    TreeGrowthRules.OrchardSeedsPerPick, WorldTick), OrchardReplantingPrefix + lotId,
                inhabitantId, lotId, 1, "orchard_replanting", long.MaxValue));
            AppendEvent("fruit_harvested", $"{inhabitantId}:{source.Id}:{harvestYield}:picked");
        }
        return new FoodHarvestEffect(harvestedLotId,
            source.Kind == "fruit" ? "fruit" : source.NaturalObjectKind == "wild_greens" ? "wild_greens" : "berries",
            harvestYield, source.Id);
    }

    private string HouseholdFor(string actor) => society.Checkpoint.GetInhabitant(actor).HouseholdId ?? actor;

    private string ProductionOwnerFor(PlacedBuilding? building, string workerId) =>
        building?.HouseholdId ?? society.Checkpoint.GetInhabitant(workerId).HouseholdId ?? workerId;

    private static bool IsHouseholdBuildingTag(string tag) => HouseholdBuildingKinds.IsKindTag(tag);

    private static string? HouseholdBuildingKind(BuildingDefinition definition) => HouseholdBuildingKinds.KindOf(definition);

    private PlacedBuilding? HouseForHousehold(string householdId) => HouseholdBuildingWithTag(householdId, "house");

    /// <summary>The household's own building of a kind, such as its Farmhouse; first by instance ID.</summary>
    private PlacedBuilding? HouseholdBuildingWithTag(string householdId, string tag) => worldSimulation.Buildings
        .Where(building => building.HouseholdId == householdId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains(tag, StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();

    private GridPoint HouseholdStockPosition(InventoryLot lot) => lot.CarrierId is { } carrier ? inhabitants[carrier].Position : lot.GroundPosition is { } ground ? new(ground.X, ground.Y)
        : lot.StorageBuildingId is { } buildingId
        ? worldSimulation.Buildings.Single(building => building.InstanceId == buildingId).Position
        : SettlementStoragePosition;

    private static int HouseholdStockInteractionRange(InventoryLot lot) =>
        lot.StorageBuildingId is null && lot.GroundPosition is null ? ResourceInteractionRange : 0;

    private InventoryLot? AvailableHouseholdServing(string actor, GridPoint position, string? requiredItemKind = null)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId)
            return null;
        var loose = PreferredFood(householdId, actor).FirstOrDefault(lot =>
            (requiredItemKind is null || lot.ItemKind == requiredItemKind) &&
            lot.CarrierId is null && (lot.StorageBuildingId is null ||
             householdId == lot.OwnerId) &&
            (IsWithinInteractionRange(position, HouseholdStockPosition(lot), HouseholdStockInteractionRange(lot)) ||
             FindUnoccupiedRoute(actor, position, HouseholdStockPosition(lot), HouseholdStockInteractionRange(lot)).Count > 0));
        if (loose is not null)
            return loose;
        return HouseForHousehold(householdId) is { } house &&
            FindFoodInPot(householdId, house.InstanceId, requiredItemKind) is { } potFood &&
            (position == house.Position || FindUnoccupiedRoute(actor, position, house.Position, 0).Count > 0)
                ? potFood.Food : null;
    }

    private InventoryLot? AvailableSharedFood(string actor, string? requiredItemKind = null) => MayCollectSharedFood(actor)
        ? AvailableHouseholdServing(actor, inhabitants[actor].Position, requiredItemKind) : null;

    private bool TryCollectHouseholdServing(string actor, PlaytestInhabitantState state, InventoryLot food,
        string operationId, string purpose, string movementReason)
    {
        if (FreeCarryCapacity(actor) < 1 || AvailableLotQuantity(food) < 1 || food.CarrierId is not null ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId || food.OwnerId != householdId)
            return false;
        var supplyPoint = HouseholdStockPosition(food);
        var interactionRange = HouseholdStockInteractionRange(food);
        if (!IsWithinInteractionRange(state.Position, supplyPoint, interactionRange))
        {
            MoveToward(actor, state, supplyPoint, movementReason, interactionRange);
            return false;
        }
        if (food.ContainerLotId is { } potId)
        {
            if (HouseForHousehold(householdId) is not { } house ||
                society.Checkpoint.Inventory.GetLot(potId) is not { ItemKind: InventoryContainerRules.StoragePot } pot ||
                pot.OwnerId != householdId || pot.StorageBuildingId != house.InstanceId || pot.ConditionBasisPoints <= 0 ||
                HasActiveContainerReservation(society.Checkpoint.Inventory, potId))
                return false;
            ApplyInventoryTransition(inventory => InventoryFixture.TakeFromContainer(inventory,
                operationId, householdId, actor, potId, food.Id, 1));
            AppendEvent("food_taken_from_pot", $"{actor}:{potId}:{food.Id}:1");
        }
        else
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                operationId, householdId, actor, food.Id, 1, purpose));
        return true;
    }

    private void CollectSharedFood(
        string inhabitantId,
        PlaytestInhabitantState state,
        string? requiredItemKind = null)
    {
        if (FreeCarryCapacity(inhabitantId) == 0) return;
        if (AvailableSharedFood(inhabitantId, requiredItemKind) is not { } lot)
            return;
        if (TryCollectHouseholdServing(inhabitantId, state, lot, $"household-food:{WorldTick}:{inhabitantId}",
                "household_food_share", "household_food"))
            AppendEvent("household_food_collected", $"{inhabitantId}:{lot.Id}:1");
    }

    private string? ConsumeFood(string inhabitantId, PlaytestInhabitantState state, string? requiredItemKind = null)
    {
        var lot = PreferredFood(inhabitantId, inhabitantId)
            .FirstOrDefault(item => requiredItemKind is null || item.ItemKind == requiredItemKind);
        if (lot is null)
        {
            AppendEvent("consumption_failed", $"{inhabitantId}:no_food");
            return null;
        }

        society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint, inhabitantId, lot.Id, 1));
        inhabitants[inhabitantId] = state with
        {
            HungerBasisPoints = Math.Min(10_000, state.HungerBasisPoints + FoodNourishment(lot.ItemKind)),
            Survival = AfterMeal(state, lot)
        };
        AppendEvent("food_consumed", inhabitantId);
        return lot.Id;
    }

}
