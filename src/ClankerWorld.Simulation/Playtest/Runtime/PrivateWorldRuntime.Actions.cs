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
        if (IsWithinInteractionRange(state.Position, destination, interactionRange))
        {
            AppendEvent("destination_reached", $"{inhabitantId}:{reason}");
            return;
        }

        if (state.TravelCooldownTicks > 0)
        {
            inhabitants[inhabitantId] = state with { TravelCooldownTicks = state.TravelCooldownTicks - 1 };
            return;
        }

        var route = FindUnoccupiedRoute(inhabitantId, state.Position, destination, interactionRange);
        if (route.Count < 2)
        {
            RecordMovementBlocked(inhabitantId, state, "no_route");
            return;
        }

        var next = route[1];
        if (!MayPullCartStep(inhabitantId, state.Position, next))
        {
            RecordMovementBlocked(inhabitantId, state, "cart_needs_repair");
            return;
        }
        inhabitants[inhabitantId] = state with
        {
            Position = next,
            MoveWaitTicks = 0,
            TravelCooldownTicks = (RoadStepCost(state.Position, next) + 99) / 100 - 1 +
                SettlementIllnessRules.TravelDelayTicks(state.Survival?.IllnessBasisPoints ?? 0) +
                CartTravelDelay(inhabitantId, state.Position, next),
        };
        MovePulledCart(inhabitantId, next);
        MoveRiddenHorse(inhabitantId);
        WearCarryAid(inhabitantId);
        RecordBridgeTraffic(inhabitantId, state.Position, next);
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
                 WeatherAt(building.Position) == WeatherKind.Storm && HasHouseGuestInvitation(inhabitantId, building.InstanceId))))
            occupied.Remove(destination);
        // An occupied exact destination cannot be reached. Keep the household
        // sharing exception above, and avoid searching an entire map for it.
        if (interactionRange == 0 && origin != destination && occupied.Contains(destination))
            return [];
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
                if (occupied.Contains(next) ||
                    map.IsDiagonalFootStep(current, next) &&
                    (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                     occupied.Contains(new GridPoint(current.X, next.Y))))
                {
                    continue;
                }

                var cost = checked(priority.Cost + RoadStepCost(current, next));
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
            AppendEvent("movement_blocked", $"{inhabitantId}:{reason}:wait={waitTicks}");
        }
    }

    private bool IsWithinInteractionRange(GridPoint origin, GridPoint destination, int interactionRange) =>
        map.FootDistance(origin, destination) <= interactionRange;

    private MapResource? AvailableFoodSource(string actor, GridPoint position) => map.Resources
        .Where(resource => FoodItems.IsEdible(resource.Kind) &&
            CarryingRoom(actor) >= (resource.TreeKind == TreeGrowthRules.Orchard ? 2 : 1) &&
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available &&
            map.IsReachableOnFoot(position, resource.Position))
        .OrderBy(resource => map.FootDistance(resource.Position, position))
        .ThenBy(resource => resource.Id, StringComparer.Ordinal)
        .FirstOrDefault(resource => IsWithinInteractionRange(position, resource.Position, ResourceInteractionRange) ||
            FindUnoccupiedRoute(actor, position, resource.Position, ResourceInteractionRange).Count > 0);

    private void HarvestFood(string inhabitantId, PlaytestInhabitantState state)
    {
        var source = AvailableFoodSource(inhabitantId, state.Position);
        HarvestFoodAtSource(inhabitantId, state, source);
    }

    private void HarvestFoodAtSource(string inhabitantId, PlaytestInhabitantState state, MapResource? source)
    {
        if (source is null || !IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange))
        {
            AppendEvent("harvest_failed", $"{inhabitantId}:not_at_available_food");
            return;
        }

        var harvestYield = Math.Min(Math.Max(0, CarryingRoom(inhabitantId) - (source.TreeKind == TreeGrowthRules.Orchard ? 1 : 0)), source.TreeKind == TreeGrowthRules.Orchard
            ? TreeGrowthRules.OrchardFruitPerPick : HarvestFoodYield);
        if (harvestYield == 0)
        {
            AppendEvent("carrying_full", inhabitantId);
            return;
        }
        var ecologyResource = worldSystems.Ecology.GetResource(source.Id);
        var harvest = EcologyRules.Harvest(ecologyResource, 1);
        if (!harvest.IsValid || harvest.Resource is null)
        {
            SyncEcologyResourceStates();
            AppendEvent("harvest_failed", $"{inhabitantId}:{harvest.Failure ?? "food_depleted"}");
            return;
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
        ApplyInventoryTransition(inventory => InventoryFixture.AddLot(
            inventory,
            $"food:harvest:{WorldTick:D10}:{inhabitantId}",
            source.Kind == "food" ? source.NaturalObjectKind == "wild_greens" ? "wild_greens" : "berries" : source.Kind,
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
    }

    private string HouseholdFor(string actor) => society.Checkpoint.GetInhabitant(actor).HouseholdId ?? actor;

    private string ProductionOwnerFor(PlacedBuilding? building, string workerId) =>
        building is not null && worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
            PortNavigationRules.IsPort(definition)) ? building.TownId ?? throw new InvalidOperationException("A Port must belong to a Town.")
            : building?.HouseholdId ?? society.Checkpoint.GetInhabitant(workerId).HouseholdId ?? workerId;

    private GridPoint BuildingWorkPosition(PlacedBuilding building) => BuildingWorkPosition(
        worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position);

    private GridPoint BuildingWorkPosition(BuildingDefinition definition, GridPoint position) => PortNavigationRules.IsPort(definition)
        ? PortNavigationRules.Geometry(map, definition, position).WorkPosition : position;

    private static bool IsHouseholdBuildingTag(string tag) => HouseholdBuildingKinds.IsKindTag(tag);

    private static string? HouseholdBuildingKind(BuildingDefinition definition) => HouseholdBuildingKinds.KindOf(definition);

    private PlacedBuilding? HouseForHousehold(string householdId) => HouseholdBuildingWithTag(householdId, "house");

    /// <summary>The household's own building of a kind, such as its Farmhouse; first by instance ID.</summary>
    private PlacedBuilding? HouseholdBuildingWithTag(string householdId, string tag) => worldSimulation.Buildings
        .Where(building => building.HouseholdId == householdId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains(tag, StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();

    private GridPoint HouseholdStockPosition(InventoryLot lot) => lot.GroundPosition is { } ground
        ? new GridPoint(ground.X, ground.Y) : lot.StorageBuildingId is { } buildingId
        ? worldSimulation.Buildings.Single(building => building.InstanceId == buildingId).Position
        : SettlementStoragePosition;

    private static int HouseholdStockInteractionRange(InventoryLot lot) =>
        lot.GroundPosition is not null ? 0 : lot.StorageBuildingId is null ? ResourceInteractionRange : 0;

    private InventoryLot? AvailableSharedFood(string actor) =>
        society.Checkpoint.GetInhabitant(actor).HouseholdId is not null && MayCollectSharedFood(actor)
        ? PreferredFood(HouseholdFor(actor), actor).FirstOrDefault(lot =>
            (lot.StorageBuildingId is null ||
             society.Checkpoint.GetInhabitant(actor).HouseholdId == lot.OwnerId) &&
            CanCollectHouseholdServing(actor, lot) &&
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, HouseholdStockPosition(lot),
                HouseholdStockInteractionRange(lot)).Count > 0)
        : null;

    private void CollectSharedFood(string inhabitantId, PlaytestInhabitantState state)
    {
        if (AvailableSharedFood(inhabitantId) is not { } lot)
            return;
        var supplyPoint = HouseholdStockPosition(lot);
        var interactionRange = HouseholdStockInteractionRange(lot);
        if (!IsWithinInteractionRange(state.Position, supplyPoint, interactionRange))
        {
            MoveToward(inhabitantId, state, supplyPoint, "household_food", interactionRange);
            return;
        }

        if (!CollectHouseholdServing(inhabitantId, lot, $"household-food:{WorldTick}:{inhabitantId}", "household_food_share")) return;
        if (lot.ItemKind == "milk" && lot.ContainerLotId is { } vesselId)
        {
            AppendEvent("household_milk_collected", $"{inhabitantId}:{vesselId}");
            return;
        }
        AppendEvent("household_food_collected", $"{inhabitantId}:{lot.Id}:1");
    }

    private bool CanCollectHouseholdServing(string actor, InventoryLot food) => CarryingRoom(actor) > 0 &&
        (food.ContainerLotId is null || society.Checkpoint.Inventory.Lots.Any(vessel =>
            vessel.Id == food.ContainerLotId && AvailableLotQuantity(vessel) == 1)) &&
        (food.ItemKind != "milk" || food.ContainerLotId is null || MilkJugIsUnreserved(food.ContainerLotId) &&
            InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, food.ContainerLotId, 1) <= CarryingRoom(actor));

    // A solid serving can leave its pot. Milk travels with the whole jug;
    // after collection, consuming its contents keeps the actual vessel.
    private bool CollectHouseholdServing(string actor, InventoryLot food, string operationId, string purpose)
    {
        if (food.OwnerId != HouseholdFor(actor) || !CanCollectHouseholdServing(actor, food)) return false;
        var collectedId = food.ItemKind == "milk" && food.ContainerLotId is { } vesselId ? vesselId : food.Id;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, operationId, food.OwnerId,
            actor, collectedId, 1, purpose));
        return true;
    }

    private void ConsumeFood(string inhabitantId, PlaytestInhabitantState state)
    {
        var lot = PreferredFood(inhabitantId, inhabitantId).FirstOrDefault();
        if (lot is null)
        {
            AppendEvent("consumption_failed", $"{inhabitantId}:no_food");
            return;
        }

        society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint, inhabitantId, lot.Id, 1));
        inhabitants[inhabitantId] = state with
        {
            HungerBasisPoints = Math.Min(10_000, state.HungerBasisPoints + MealFullness(lot)),
            Survival = AfterMeal(state, lot)
        };
        AppendEvent("food_consumed", inhabitantId);
    }

}
