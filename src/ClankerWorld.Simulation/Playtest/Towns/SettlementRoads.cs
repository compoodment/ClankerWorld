using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

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

    /// <summary>
    /// Joins a building that has joined a Town to the Road network and returns
    /// the new Road tiles. A building beside a Road faces it and needs no new
    /// Road; otherwise a new street runs from its best entrance to the nearest
    /// Road, inside the Town border where it can. Either way, streets near its
    /// door then run on past it (<see cref="ExtendStreetsPastDoors"/>).
    /// </summary>
    private List<GridPoint> GenerateRoadToBuilding(PlacedBuilding building, IReadOnlySet<GridPoint>? border = null)
    {
        var laid = new List<GridPoint>();
        if (building.TownId is null) return laid;
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
            .Where(point => !occupied.Contains(point) && map.IsBuildable(point) &&
                WorldContentSimulationRules.IsEntrance(buildingDesign, building.Position, point))
            .Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        if (entrances.Length == 0)
        {
            AppendEvent("town_road_unconnected", $"{building.TownId}:{building.InstanceId}:no_entrance");
            return laid;
        }
        var network = roadTiles.Count > 0 ? roadTiles.ToHashSet() : map.CampObjects
            .Where(item => item.Id == "storage")
            .SelectMany(item => map.FootNeighbors(item.Position)
                .Where(point => !map.IsDiagonalFootStep(item.Position, point)))
            .Where(point => map.IsBuildable(point) && !occupied.Contains(point))
            .ToHashSet();
        if (network.Count == 0) network.Add(entrances[0]);

        var route = (border is null ? null : FindRoadRoute(entrances, network, occupied, border)) ??
            FindRoadRoute(entrances, network, occupied, null);
        if (route is null)
        {
            AppendEvent("town_road_unconnected", $"{building.TownId}:{building.InstanceId}:land_route_unavailable");
            return laid;
        }
        foreach (var tile in route)
            if (roadTiles.Add(tile)) laid.Add(tile);
        // The route starts at the entrance it was laid from, so the door faces this Road.
        SetBuildingEntrance(building.InstanceId, route[0]);
        if (laid.Count > 0)
            AppendEvent("town_road_generated", $"{building.TownId}:{building.InstanceId}:tiles:{laid.Count}");
        var extended = ExtendStreetsPastDoors(building, occupied);
        if (extended.Count > 0)
            AppendEvent("town_road_extended", $"{building.TownId}:{building.InstanceId}:tiles:{extended.Count}");
        laid.AddRange(extended);
        return laid;
    }

    /// <summary>
    /// The cheapest new street from any entrance to the network, as the tiles
    /// from the entrance to the first Road tile. It may step diagonally where
    /// both corner tiles are clear, and pays extra for each tile that would run
    /// beside an existing Road, so it meets streets rather than shadowing them.
    /// </summary>
    private List<GridPoint>? FindRoadRoute(GridPoint[] entrances, HashSet<GridPoint> network,
        HashSet<GridPoint> occupied, IReadOnlySet<GridPoint>? border)
    {
        const int BesideRoadCost = 60;
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
                var route = new List<GridPoint> { current };
                while (predecessor.TryGetValue(current, out var previous))
                {
                    current = previous;
                    route.Add(current);
                }
                route.Reverse();
                return route;
            }

            foreach (var next in map.FootNeighbors(current))
            {
                var joins = network.Contains(next);
                if (!map.IsBuildable(next) || occupied.Contains(next) && !joins ||
                    border is not null && !joins && !border.Contains(next))
                    continue;
                if (map.IsDiagonalFootStep(current, next) &&
                    (Math.Abs(next.X - current.X) != 1 || !IsClearCorner(new GridPoint(next.X, current.Y)) ||
                     !IsClearCorner(new GridPoint(current.X, next.Y))))
                    continue;
                var cost = checked(priority.Cost + map.FootStepCost(current, next) +
                    (!joins && TownStreets.Directions.Any(step =>
                        roadTiles.Contains(new GridPoint(next.X + step.X, next.Y + step.Y))) ? BesideRoadCost : 0));
                if (best.TryGetValue(next, out var known) && known <= cost) continue;
                best[next] = cost;
                predecessor[next] = current;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }
        return null;

        bool IsClearCorner(GridPoint tile) => map.IsBuildable(tile) && !occupied.Contains(tile);
    }

    /// <summary>
    /// Streets run on about three tiles past the last door on them, leaving free
    /// frontage for the next building. Each dead end fewer tiles than that past
    /// its nearest door carries on in its own direction where the land allows,
    /// keeping clear of other streets.
    /// </summary>
    private List<GridPoint> ExtendStreetsPastDoors(PlacedBuilding building, HashSet<GridPoint> occupied)
    {
        var extended = new List<GridPoint>();
        var doors = worldSimulation.Buildings.Where(item => item.Entrance is not null)
            .Select(item => item.Entrance!.Value).ToHashSet();
        var ordered = roadTiles.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        var streets = new TownStreets(map, occupied, ordered);
        var random = Pcg32XshRrV1.Create(worldSeed, $"town-streets/{building.InstanceId}");
        foreach (var end in ordered)
        {
            var linked = TownStreets.Linked(roadTiles, end).ToArray();
            if (linked.Length != 1 || StepsToDoor(end, doors) is not { } steps || steps >= TownStreets.RunOnTiles)
                continue;
            var path = streets.Wander(end, TownStreets.DirectionBetween(linked[0], end),
                TownStreets.RunOnTiles - steps, 0, random);
            foreach (var tile in path)
                if (roadTiles.Add(tile)) extended.Add(tile);
        }
        return extended;
    }

    /// <summary>Road steps from a tile to the nearest door, looking no further than a street's run-on.</summary>
    private int? StepsToDoor(GridPoint start, HashSet<GridPoint> doors)
    {
        var steps = new Dictionary<GridPoint, int> { [start] = 0 };
        var pending = new Queue<GridPoint>([start]);
        while (pending.TryDequeue(out var current))
        {
            if (doors.Contains(current)) return steps[current];
            if (steps[current] >= TownStreets.RunOnTiles) continue;
            foreach (var next in TownStreets.Linked(roadTiles, current))
                if (steps.TryAdd(next, steps[current] + 1)) pending.Enqueue(next);
        }
        return null;
    }

    private void SetBuildingEntrance(string instanceId, GridPoint entrance)
    {
        worldSimulation = worldSimulation with
        {
            Buildings = worldSimulation.Buildings
                .Select(item => item.InstanceId == instanceId ? item with { Entrance = entrance } : item)
                .ToArray(),
        };
    }

    private static void ValidateRoads(IReadOnlyList<GridPoint> roads, SeededMap map, FounderSetupState? setup)
    {
        if (roads.Count == 0) return;
        if (setup is null || roads.Distinct().Count() != roads.Count ||
            roads.Any(point => !map.IsBuildable(point)))
            throw new InvalidDataException("Saved Roads contain duplicate, invalid, or unaffiliated ground tiles.");
    }
}
