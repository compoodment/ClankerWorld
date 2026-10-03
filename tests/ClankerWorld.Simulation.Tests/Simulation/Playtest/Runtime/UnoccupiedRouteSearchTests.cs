using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// One shared search answers many route questions from one origin. Each answer
/// must be exactly the route a fresh search for that question alone finds.
/// </summary>
public sealed class UnoccupiedRouteSearchTests
{
    [Fact]
    public void SharedSearchFindsTheSameRoutesAsFreshSearchesOnAGeneratedMap()
    {
        var map = GeographyCandidateSelector.GenerateCandidate(
            new GeographyOptions("shared-route-search", WorldSizePreset.Small));

        CompareWithFreshSearches(map, origins: 4, questions: 24, seed: 832);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedSearchFindsTheSameRoutesAsFreshSearchesWithOrWithoutAWrap(bool wraps)
    {
        // Mountains cost more than meadow and lakes block the way, so routes bend
        // and many tiles tie on cost.
        var map = new SeededMap(24, 16, 0,
            [.. from y in Enumerable.Range(0, 16)
                from x in Enumerable.Range(0, 24)
                select new TerrainTile(new GridPoint(x, y),
                    x is 7 or 8 && y is > 2 and < 13 ? TerrainKind.Lake
                    : (x * 5 + y * 3) % 11 == 0 ? TerrainKind.Mountain : TerrainKind.Meadow)],
            [], [], string.Empty);
        map = map with { WrapsEastWest = wraps };

        CompareWithFreshSearches(map, origins: 8, questions: 40, seed: wraps ? 2 : 1);
    }

    [Fact]
    public void ARouteMayStartOnAnOccupiedTileButNeverEntersOneOrCutsPastOne()
    {
        var map = new SeededMap(5, 3, 0,
            [.. from y in Enumerable.Range(0, 3)
                from x in Enumerable.Range(0, 5)
                select new TerrainTile(new GridPoint(x, y), TerrainKind.Meadow)],
            [], [], string.Empty);
        var origin = new GridPoint(0, 1);
        // A wall across column 2 with one gap at the bottom; the corner at (1, 2)
        // stops the diagonal from (1, 1) to the gap.
        HashSet<GridPoint> occupied = [origin, new(2, 0), new(2, 1), new(1, 2)];
        using var search = new UnoccupiedRouteSearch(map, origin, occupied, map.FootStepCost);

        Assert.Equal([origin], search.RouteTo(new GridPoint(1, 1), 1));
        Assert.Empty(search.RouteTo(new GridPoint(2, 1), 0));
        Assert.Empty(search.RouteTo(new GridPoint(4, 1), 0));
        Assert.Equal(FreshRoute(map, origin, occupied, map.FootStepCost, new GridPoint(1, 0), 0),
            search.RouteTo(new GridPoint(1, 0), 0));
    }

    private static void CompareWithFreshSearches(SeededMap map, int origins, int questions, int seed)
    {
        var random = new Random(seed);
        var passable = map.Tiles.Select(tile => tile.Position).Where(map.IsPassable).ToArray();
        var everywhere = map.Tiles.Select(tile => tile.Position).ToArray();
        // A pattern of Road tiles, so some steps are cheaper, as in a Town.
        Func<GridPoint, GridPoint, int> cost = (from, to) =>
            IsRoad(from) && IsRoad(to) ? Math.Max(1, map.FootStepCost(from, to) * 70 / 100) : map.FootStepCost(from, to);

        for (var round = 0; round < origins; round++)
        {
            var origin = passable[random.Next(passable.Length)];
            // Crowd the origin, so blocked steps and diagonal corners come up,
            // and block a few tiles anywhere. The first origin is occupied itself.
            var occupied = new HashSet<GridPoint>();
            if (round == 0) occupied.Add(origin);
            while (occupied.Count < 18)
            {
                var near = map.WrapColumn(new GridPoint(origin.X + random.Next(-5, 6), origin.Y + random.Next(-5, 6)));
                if (map.Contains(near) && near != origin) occupied.Add(near);
            }
            for (var extra = 0; extra < 6; extra++) occupied.Add(passable[random.Next(passable.Length)]);

            using var shared = new UnoccupiedRouteSearch(map, origin, occupied, cost);
            for (var question = 0; question < questions; question++)
            {
                var destination = (question % 4) switch
                {
                    0 => map.WrapColumn(new GridPoint(origin.X + random.Next(-8, 9), origin.Y + random.Next(-8, 9))),
                    1 => everywhere[random.Next(everywhere.Length)],
                    2 => origin,
                    _ => passable[random.Next(passable.Length)],
                };
                var range = random.Next(0, 3);

                Assert.Equal(FreshRoute(map, origin, occupied, cost, destination, range),
                    shared.RouteTo(destination, range));
            }
        }
    }

    private static bool IsRoad(GridPoint point) => (point.X * 7 + point.Y * 3) % 5 == 0;

    // The search FindUnoccupiedRoute ran for every question before routes were shared.
    private static List<GridPoint> FreshRoute(SeededMap map, GridPoint origin, HashSet<GridPoint> occupied,
        Func<GridPoint, GridPoint, int> stepCost, GridPoint destination, int interactionRange)
    {
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int> { [origin] = 0 };
        var predecessor = new Dictionary<GridPoint, GridPoint>();
        var order = 0;
        open.Enqueue(origin, (0, origin.Y, origin.X, order++));

        while (open.TryDequeue(out var current, out var priority))
        {
            if (priority.Cost != best[current])
                continue;
            if (map.FootDistance(current, destination) <= interactionRange)
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

                var cost = checked(priority.Cost + stepCost(current, next));
                if (best.TryGetValue(next, out var previous) && previous <= cost)
                    continue;
                best[next] = cost;
                predecessor[next] = current;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }

        return [];
    }
}
