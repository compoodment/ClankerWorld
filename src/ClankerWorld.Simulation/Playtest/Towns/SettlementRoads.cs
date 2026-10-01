using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void RepairSavedRoadFootprints()
    {
        var occupied = worldSimulation.Buildings.SelectMany(building =>
        {
            var design = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            return WorldContentSimulationRules.Footprint(design, building);
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
        // A Road bridge's deck is part of its Road; a traffic bridge's is not.
        return IsRoadSurface(from) && IsRoadSurface(to)
            ? Math.Max(1, cost * 70 / 100) : cost;
    }

    private bool IsRoadSurface(GridPoint point) => roadTiles.Contains(point) || roadBridgeDecks.Contains(point);

    /// <summary>
    /// Joins a building that has joined a Town to the Road network and returns
    /// the new Road tiles. A building beside a Road faces it and needs no new
    /// Road; otherwise a new street runs from its best entrance to the nearest
    /// Road, inside the Town border where it can, crossing a river up to two
    /// tiles wide on a new bridge where its route needs one. Either way,
    /// streets near its door then run on past it
    /// (<see cref="ExtendStreetsPastDoors"/>). Each proposal is validated whole
    /// before any Road tile or bridge is saved.
    /// </summary>
    private List<GridPoint> GenerateRoadToBuilding(PlacedBuilding building, IReadOnlySet<GridPoint>? border = null)
    {
        var laid = new List<GridPoint>();
        if (building.TownId is null) return laid;
        var buildingDesign = BuildingStorageRules.EffectiveDefinition(
            worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building);
        var footprint = WorldContentSimulationRules.Footprint(buildingDesign, building).ToHashSet();
        var occupied = RoadBlockedTiles();
        var entrances = footprint.SelectMany(point => map.FootNeighbors(point)
                .Where(next => !map.IsDiagonalFootStep(point, next)))
            .Where(point => !occupied.Contains(point) && map.IsBuildable(point) &&
                WorldContentSimulationRules.IsEntrance(buildingDesign, building.Position, point))
            .Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        if (entrances.Length == 0)
        {
            AppendEvent("town_road_unconnected", $"{building.TownId}:{building.InstanceId}:{RoadRouteOutcomes.NoEntrance}");
            return laid;
        }
        var network = roadTiles.Count > 0 ? roadTiles.ToHashSet() : map.CampObjects
            .Where(item => item.Id == "storage")
            .SelectMany(item => map.FootNeighbors(item.Position)
                .Where(point => !map.IsDiagonalFootStep(item.Position, point)))
            .Where(point => map.IsBuildable(point) && !occupied.Contains(point))
            .ToHashSet();
        if (network.Count == 0) network.Add(entrances[0]);

        var request = new RoadRouteRequest(map, entrances, network, occupied, Bridges, roadTiles, border);
        var result = border is null ? null : RoadRoutePlanner.Plan(request);
        if (result?.Proposal is null)
        {
            request = request with { Border = null };
            result = RoadRoutePlanner.Plan(request);
        }
        var failure = result.Proposal is { } proposal ? RoadRoutePlanner.Validate(request, proposal) : result.Outcome;
        if (failure is not null)
        {
            AppendEvent("town_road_unconnected", $"{building.TownId}:{building.InstanceId}:{failure}");
            return laid;
        }
        var route = result.Proposal!;
        var routeId = $"road:{building.TownId}:{building.InstanceId}";
        foreach (var tile in route.RoadTiles)
            if (roadTiles.Add(tile)) laid.Add(tile);
        // The route starts at the entrance it was laid from, so the door faces this Road.
        SetBuildingEntrance(building.InstanceId, route.RoadTiles[0]);
        if (laid.Count > 0)
            AppendEvent("town_road_generated", $"{building.TownId}:{building.InstanceId}:tiles:{laid.Count}");
        CommitRoadBridges(route.NewCrossings, routeId);

        var (extended, crossings) = ExtendStreetsPastDoors(building, occupied);
        if (extended.Count == 0) return laid;
        var roadsAfter = roadTiles.Concat(extended).ToHashSet();
        if (RoadRoutePlanner.ValidateGrowth(map, occupied, roadsAfter, Bridges, extended, crossings) is { } refused)
        {
            AppendEvent("town_road_extension_refused", $"{building.TownId}:{building.InstanceId}:{refused}");
            return laid;
        }
        roadTiles.UnionWith(extended);
        AppendEvent("town_road_extended", $"{building.TownId}:{building.InstanceId}:tiles:{extended.Count}");
        CommitRoadBridges(crossings, routeId);
        laid.AddRange(extended);
        return laid;
    }

    /// <summary>
    /// Streets run on about three tiles past the last door on them, leaving free
    /// frontage for the next building. Each dead end fewer tiles than that past
    /// its nearest door carries on in its own direction where the land allows,
    /// keeping clear of other streets. A street heading straight at a river up
    /// to two tiles wide may cross it on a new bridge. Nothing is saved here:
    /// the caller validates and commits the returned tiles and crossings.
    /// </summary>
    private (List<GridPoint> Tiles, List<RiverCrossing> Crossings) ExtendStreetsPastDoors(PlacedBuilding building,
        HashSet<GridPoint> occupied)
    {
        var extended = new List<GridPoint>();
        var crossings = new List<RiverCrossing>();
        var existing = bridges.Select(RiverBridgeRules.ToCrossing).ToArray();
        var working = roadTiles.ToHashSet();
        var doors = worldSimulation.Buildings.Where(item => item.Entrance is not null)
            .Select(item => item.Entrance!.Value).ToHashSet();
        var ordered = working.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        var streets = new TownStreets(map, occupied, ordered);
        var random = Pcg32XshRrV1.Create(worldSeed, $"town-streets/{building.InstanceId}");
        foreach (var end in ordered)
        {
            var linked = LinkedRoads(working, end, existing, crossings).ToArray();
            if (linked.Length != 1 || StepsToDoor(working, end, doors, existing, crossings) is not { } steps ||
                steps >= TownStreets.RunOnTiles)
                continue;
            var path = streets.Wander(end, TownStreets.DirectionBetween(linked[0], end),
                TownStreets.RunOnTiles - steps, 0, random,
                crossRiver: (from, direction) => RunOnCrossing(from, direction, occupied, existing, crossings),
                crossings: crossings);
            foreach (var tile in path)
                if (working.Add(tile)) extended.Add(tile);
        }
        return (extended, crossings);
    }

    /// <summary>
    /// A new bridge for a street running on straight at a river: a legal
    /// crossing of one or two river tiles whose far bank is clear, that shares
    /// no water with another new crossing and joins banks no bridge joins yet.
    /// </summary>
    private RiverCrossing? RunOnCrossing(GridPoint from, int direction, HashSet<GridPoint> occupied,
        IReadOnlyList<RiverCrossing> existing, IReadOnlyList<RiverCrossing> pending)
    {
        var (dx, dy) = TownStreets.Directions[direction];
        if (dx != 0 && dy != 0 || !RiverBridgeRules.TryFindCrossing(map, from, dx, dy, out var crossing))
            return null;
        var far = crossing!.EntranceA == from ? crossing.EntranceB : crossing.EntranceA;
        if (occupied.Contains(far) || pending.Any(item => item.Span.Intersect(crossing.Span).Any()) ||
            RiverBridgeRules.IsRedundant(map, crossing, existing.Concat(pending)))
            return null;
        return crossing;
    }

    /// <summary>
    /// Road tiles joined to a tile: its street neighbours, plus the far end of
    /// any bridge that has Road at both ends, so a street that crosses a river
    /// is not mistaken for two dead ends.
    /// </summary>
    private static IEnumerable<GridPoint> LinkedRoads(IReadOnlySet<GridPoint> roads, GridPoint tile,
        IReadOnlyList<RiverCrossing> existing, IReadOnlyList<RiverCrossing> pending)
    {
        foreach (var next in TownStreets.Linked(roads, tile)) yield return next;
        foreach (var crossing in existing.Concat(pending))
        {
            if (crossing.EntranceA == tile && roads.Contains(crossing.EntranceB)) yield return crossing.EntranceB;
            else if (crossing.EntranceB == tile && roads.Contains(crossing.EntranceA)) yield return crossing.EntranceA;
        }
    }

    /// <summary>Road steps from a tile to the nearest door, looking no further than a street's run-on.</summary>
    private static int? StepsToDoor(IReadOnlySet<GridPoint> roads, GridPoint start, HashSet<GridPoint> doors,
        IReadOnlyList<RiverCrossing> existing, IReadOnlyList<RiverCrossing> pending)
    {
        var steps = new Dictionary<GridPoint, int> { [start] = 0 };
        var queue = new Queue<GridPoint>([start]);
        while (queue.TryDequeue(out var current))
        {
            if (doors.Contains(current)) return steps[current];
            if (steps[current] >= TownStreets.RunOnTiles) continue;
            foreach (var next in LinkedRoads(roads, current, existing, pending))
                if (steps.TryAdd(next, steps[current] + 1)) queue.Enqueue(next);
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
