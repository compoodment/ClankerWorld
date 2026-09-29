using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int MaximumLandRoadSearchTiles = 32_768;

    private void RepairSavedRoadFootprints()
    {
        var occupied = worldSimulation.Buildings.SelectMany(building =>
        {
            var design = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            return WorldContentSimulationRules.Footprint(design, building.Position);
        }).ToHashSet();
        var removed = roadTiles.RemoveWhere(occupied.Contains);
        if (removed > 0) AppendEvent("saved_road_footprints_repaired", $"removed:{removed}");
    }

    private int RoadStepCost(GridPoint from, GridPoint to)
    {
        var cost = map.FootStepCost(from, to);
        // One ground tile per tick is already the movement floor. This
        // provisional factor removes diagonal wait ticks and biases routing
        // toward an existing Road without making illness delays disappear.
        return roadTiles.Contains(from) && roadTiles.Contains(to)
            ? Math.Max(1, cost * 70 / 100) : cost;
    }

    private void GenerateRoadToBuilding(PlacedBuilding building)
    {
        if (building.TownId is null) return;
        var buildingDesign = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var footprint = WorldContentSimulationRules.Footprint(buildingDesign, building.Position).ToHashSet();
        var occupied = map.Resources.Select(item => item.Position)
            .Concat(map.CampObjects.Select(item => item.Position))
            .Concat(worldSimulation.Buildings
                .SelectMany(item =>
                {
                    var design = worldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId);
                    return WorldContentSimulationRules.Footprint(design, item.Position);
                }))
            .ToHashSet();
        var entrances = footprint.SelectMany(point => map.FootNeighbors(point)
                .Where(next => !map.IsDiagonalFootStep(point, next)))
            .Where(point => !occupied.Contains(point) && map.IsBuildable(point))
            .Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        if (entrances.Length == 0)
        {
            AppendEvent("town_road_unconnected", $"{building.TownId}:{building.InstanceId}:no_entrance");
            return;
        }
        var network = roadTiles.Count > 0 ? roadTiles.ToHashSet() : map.CampObjects
            .Where(item => item.Id == "storage")
            .SelectMany(item => map.FootNeighbors(item.Position)
                .Where(point => !map.IsDiagonalFootStep(item.Position, point)))
            .Where(point => map.IsBuildable(point) && !occupied.Contains(point))
            .ToHashSet();
        if (network.Count == 0) network.Add(entrances[0]);
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int>();
        var predecessor = new Dictionary<GridPoint, GridPoint>();
        var order = 0;
        foreach (var entrance in entrances)
        {
            best[entrance] = 0;
            open.Enqueue(entrance, (0, entrance.Y, entrance.X, order++));
        }

        while (open.TryDequeue(out var current, out var priority) && best.Count <= MaximumLandRoadSearchTiles)
        {
            if (priority.Cost != best[current]) continue;
            if (network.Contains(current))
            {
                var added = 0;
                while (true)
                {
                    if (roadTiles.Add(current)) added++;
                    if (!predecessor.TryGetValue(current, out var previous)) break;
                    current = previous;
                }
                if (added > 0)
                    AppendEvent("town_road_generated", $"{building.TownId}:{building.InstanceId}:tiles:{added}");
                return;
            }

            foreach (var next in map.FootNeighbors(current))
            {
                if (!map.IsBuildable(next) || occupied.Contains(next) && !network.Contains(next) ||
                    map.IsDiagonalFootStep(current, next))
                    continue;
                var cost = checked(priority.Cost + map.FootStepCost(current, next));
                if (best.TryGetValue(next, out var previous) && previous <= cost) continue;
                best[next] = cost;
                predecessor[next] = current;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }
        AppendEvent("town_road_unconnected", $"{building.TownId}:{building.InstanceId}:land_route_unavailable");
    }

    private static void ValidateRoads(IReadOnlyList<GridPoint> roads, SeededMap map, FounderSetupState? setup)
    {
        if (roads.Count == 0) return;
        if (setup is null || roads.Distinct().Count() != roads.Count ||
            roads.Any(point => !map.IsBuildable(point)))
            throw new InvalidDataException("Saved Roads contain duplicate, invalid, or unaffiliated ground tiles.");
    }
}
