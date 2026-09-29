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
        inhabitants[inhabitantId] = state with
        {
            Position = next,
            MoveWaitTicks = 0,
            TravelCooldownTicks = (RoadStepCost(state.Position, next) + 99) / 100 - 1 +
                SettlementIllnessRules.TravelDelayTicks(state.Survival?.IllnessBasisPoints ?? 0),
        };
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
                building.HouseholdId == society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId))
            occupied.Remove(destination);
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

    private MapResource? AvailableFoodSource(GridPoint position) => map.Resources
        .Where(resource => resource.Kind is "food" or "fruit" &&
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available &&
            map.IsReachableOnFoot(position, resource.Position))
        .OrderBy(resource => map.FootDistance(resource.Position, position))
        .FirstOrDefault();

    private void HarvestFood(string inhabitantId, PlaytestInhabitantState state)
    {
        var source = AvailableFoodSource(state.Position);
        if (source is null || !IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange))
        {
            AppendEvent("harvest_failed", $"{inhabitantId}:not_at_available_food");
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
            source.Kind == "fruit" ? "fruit" : "food",
            inhabitantId,
            HarvestFoodYield,
            WorldTick));

        AppendEvent("food_harvested", $"{inhabitantId}:{HarvestFoodYield}");
        if (source.TreeKind == "orchard")
            AppendEvent("fruit_harvested", $"{inhabitantId}:{source.Id}:{HarvestFoodYield}:picked");
    }

    private string HouseholdFor(string actor) => society.Checkpoint.GetInhabitant(actor).HouseholdId ?? actor;

    private string ProductionOwnerFor(PlacedBuilding? building, string workerId) =>
        building?.HouseholdId ?? society.Checkpoint.GetInhabitant(workerId).HouseholdId ?? workerId;

    private static bool IsHouseholdBuildingTag(string tag) => tag is "house" or "farmhouse" or "blacksmith";

    private PlacedBuilding? HouseForHousehold(string householdId) => worldSimulation.Buildings
        .Where(building => building.HouseholdId == householdId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("house", StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();

    private GridPoint HouseholdStockPosition(InventoryLot lot) => lot.StorageBuildingId is { } buildingId
        ? worldSimulation.Buildings.Single(building => building.InstanceId == buildingId).Position
        : SettlementStoragePosition;

    private static int HouseholdStockInteractionRange(InventoryLot lot) =>
        lot.StorageBuildingId is null ? ResourceInteractionRange : 0;

    private InventoryLot? AvailableSharedFood(string actor) =>
        society.Checkpoint.GetInhabitant(actor).HouseholdId is not null && MayCollectSharedFood(actor)
        ? PreferredFood(HouseholdFor(actor), actor).FirstOrDefault(lot =>
            (lot.StorageBuildingId is null ||
             society.Checkpoint.GetInhabitant(actor).HouseholdId == lot.OwnerId) &&
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

        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(
            inventory, $"household-food:{WorldTick}:{inhabitantId}", HouseholdFor(inhabitantId), inhabitantId,
            lot.Id, 1, "household_food_share"));
        AppendEvent("household_food_collected", $"{inhabitantId}:{lot.Id}:1");
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
            HungerBasisPoints = Math.Min(10_000, state.HungerBasisPoints + 3_000),
            Survival = AfterMeal(state, lot)
        };
        AppendEvent("food_consumed", inhabitantId);
    }

}
