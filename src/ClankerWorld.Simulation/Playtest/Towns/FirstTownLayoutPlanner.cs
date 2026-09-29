using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A feasible five-building starting layout, not an accepted or saved Town.</summary>
public sealed record FirstTownLayout(
    GridPoint RoughSite,
    IReadOnlyList<FirstTownLayoutBuilding> Buildings,
    IReadOnlyList<GridPoint> RoadTiles);

public sealed record FirstTownLayoutBuilding(string Role, string DefinitionId,
    GridPoint Position, int Width, int Height);

/// <summary>
/// Finds a deterministic dry-land starting footprint near a rough site. This
/// does not assign household claims or alter the world; paused setup remains
/// the authority for accepting a plan and its starter supplies.
/// </summary>
public static class FirstTownLayoutPlanner
{
    private const int BuildingSearchRadius = 9;
    private const int RoadSearchRadius = 12;
    private const int MaximumWarehouseCandidates = 24;

    public static FirstTownLayout? Plan(SeededMap map, GridPoint roughSite)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsBuildable(roughSite)) return null;
        var unavailable = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        var definitions = new (string Role, BuildingDefinition Definition)[]
        {
            ("warehouse", WarehouseContent.Warehouse2x2()),
            ("house-a", HouseContent.House1x1()),
            ("house-b", HouseContent.House1x1()),
            ("farmhouse", FarmContent.Farmhouse1x1()),
            ("blacksmith", BlacksmithContent.Blacksmith1x2()),
        };
        foreach (var warehouse in CandidateAnchors(map, roughSite, 4)
                     .Where(point => Fits(map, definitions[0].Definition, point, unavailable))
                     .Take(MaximumWarehouseCandidates))
        {
            var placed = new List<FirstTownLayoutBuilding>();
            var occupied = new HashSet<GridPoint>(unavailable);
            AddBuilding(definitions[0], warehouse, placed, occupied);
            var warehouseEntrances = Entrances(definitions[0].Definition, warehouse)
                .Where(point => map.IsBuildable(point) && !occupied.Contains(point)).ToArray();
            if (warehouseEntrances.Length == 0) continue;
            var roads = new HashSet<GridPoint> { warehouseEntrances[0] };
            var complete = true;
            foreach (var definition in definitions.Skip(1))
            {
                var found = false;
                foreach (var candidate in CandidateAnchors(map, roughSite, BuildingSearchRadius))
                {
                    if (!Fits(map, definition.Definition, candidate, occupied) || roads.Contains(candidate) ||
                        Footprint(definition.Definition, candidate).Any(roads.Contains))
                        continue;
                    var ownFootprint = Footprint(definition.Definition, candidate).ToHashSet();
                    var path = Entrances(definition.Definition, candidate)
                        .Select(entrance => RoadPath(map, roughSite, entrance, roads, occupied, ownFootprint))
                        .FirstOrDefault(route => route is not null);
                    if (path is null) continue;
                    AddBuilding(definition, candidate, placed, occupied);
                    roads.UnionWith(path);
                    found = true;
                    break;
                }
                if (found) continue;
                complete = false;
                break;
            }
            if (complete)
                return new FirstTownLayout(roughSite, placed,
                    roads.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray());
        }
        return null;
    }

    private static GridPoint[] CandidateAnchors(SeededMap map, GridPoint center, int radius)
    {
        var minX = Math.Max(0, center.X - radius);
        var maxX = Math.Min(map.Width - 1, center.X + radius);
        var minY = Math.Max(0, center.Y - radius);
        var maxY = Math.Min(map.Height - 1, center.Y + radius);
        return Enumerable.Range(minY, maxY - minY + 1)
            .SelectMany(y => Enumerable.Range(minX, maxX - minX + 1).Select(x => new GridPoint(x, y)))
            .OrderBy(point => Math.Abs(point.X - center.X) + Math.Abs(point.Y - center.Y))
            .ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
    }

    private static IEnumerable<GridPoint> Footprint(BuildingDefinition definition, GridPoint anchor) =>
        Enumerable.Range(0, definition.Height).SelectMany(dy =>
            Enumerable.Range(0, definition.Width).Select(dx => new GridPoint(anchor.X + dx, anchor.Y + dy)));

    private static GridPoint[] Entrances(BuildingDefinition definition, GridPoint anchor) =>
        Footprint(definition, anchor)
            .SelectMany(point => new[]
            {
                new GridPoint(point.X, point.Y - 1),
                new GridPoint(point.X + 1, point.Y),
                new GridPoint(point.X, point.Y + 1),
                new GridPoint(point.X - 1, point.Y),
            })
            .Where(point => point.X < anchor.X || point.X >= anchor.X + definition.Width ||
                point.Y < anchor.Y || point.Y >= anchor.Y + definition.Height)
            .Distinct()
            .OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();

    private static bool Fits(SeededMap map, BuildingDefinition definition, GridPoint anchor,
        HashSet<GridPoint> occupied) =>
        Footprint(definition, anchor).All(point => map.IsBuildable(point) && !occupied.Contains(point));

    private static void AddBuilding((string Role, BuildingDefinition Definition) item, GridPoint anchor,
        List<FirstTownLayoutBuilding> placed, HashSet<GridPoint> occupied)
    {
        placed.Add(new FirstTownLayoutBuilding(item.Role, item.Definition.CanonicalId, anchor,
            item.Definition.Width, item.Definition.Height));
        occupied.UnionWith(Footprint(item.Definition, anchor));
    }

    private static List<GridPoint>? RoadPath(SeededMap map, GridPoint center,
        GridPoint start, HashSet<GridPoint> network, HashSet<GridPoint> occupied,
        HashSet<GridPoint> ownFootprint)
    {
        if (!RoadGround(start)) return null;
        var queue = new Queue<GridPoint>();
        var predecessor = new Dictionary<GridPoint, GridPoint>();
        var visited = new HashSet<GridPoint> { start };
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (network.Contains(current))
            {
                var path = new List<GridPoint> { current };
                while (current != start)
                {
                    current = predecessor[current];
                    path.Add(current);
                }
                return path;
            }
            foreach (var next in map.FootNeighbors(current))
            {
                if (map.IsDiagonalFootStep(current, next) ||
                    Math.Abs(next.X - center.X) > RoadSearchRadius ||
                    Math.Abs(next.Y - center.Y) > RoadSearchRadius || visited.Contains(next) ||
                    !RoadGround(next))
                    continue;
                visited.Add(next);
                predecessor[next] = current;
                queue.Enqueue(next);
            }
        }
        return null;

        bool RoadGround(GridPoint point) => map.IsBuildable(point) && !ownFootprint.Contains(point) &&
            !occupied.Contains(point);
    }
}
