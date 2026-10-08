using System.Runtime.CompilerServices;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Agent swimming geometry; ordinary foot, cart and animal movement stays separate.</summary>
public static class SwimmingRules
{
    // Provisional values for hands-on playtesting, as agreed in #1288.
    public const int StepCost = 800;
    public const int WarmthLossPerTick = 75;
    public const int MinimumStartingWarmth = 6_000;
    public const int MaximumStartingIllness = 2_499;
    public const int MaximumCarriedUnits = 4;

    private static readonly ConditionalWeakTable<SeededMap, int[]> Components = new();

    public static bool IsReachable(SeededMap map, GridPoint from, GridPoint to)
    {
        if (!map.Contains(from) || !map.Contains(to) ||
            !(map.IsPassable(from) || IsSwimmingWater(map, from)) ||
            !(map.IsPassable(to) || IsSwimmingWater(map, to))) return false;
        // Like foot connectivity, this follows immutable map identity. A bridge changes the map.
        // Task selection needs connectivity, not a weighted route for every potential resource.
        var components = Components.GetValue(map, static map =>
        {
            var labels = new int[checked(map.Width * map.Height)];
            var pending = new Queue<GridPoint>();
            var component = 0;
            foreach (var tile in map.Tiles)
            {
                var index = tile.Position.Y * map.Width + tile.Position.X;
                if (labels[index] != 0 || !(map.IsPassable(tile.Position) || IsSwimmingWater(map, tile.Position))) continue;
                labels[index] = ++component;
                pending.Enqueue(tile.Position);
                while (pending.TryDequeue(out var point))
                {
                    foreach (var neighbor in Neighbors(map, point))
                    {
                        var neighborIndex = neighbor.Y * map.Width + neighbor.X;
                        if (labels[neighborIndex] != 0) continue;
                        labels[neighborIndex] = component;
                        pending.Enqueue(neighbor);
                    }
                }
            }
            return labels;
        });
        return components[from.Y * map.Width + from.X] == components[to.Y * map.Width + to.X];
    }

    public static bool IsSwimmingWater(SeededMap map, GridPoint point) =>
        map.Contains(point) &&
        (map.HydrologyAt(point) is WaterKind.Lake or WaterKind.River ||
         map.HydrologyAt(point) is null && map.TerrainKindAt(point) is TerrainKind.Lake or TerrainKind.River) &&
        !map.IsPassable(point);

    public static bool CanStep(SeededMap map, GridPoint from, GridPoint to) =>
        map.CanFootStep(from, to) ||
        map.Contains(from) && map.Contains(to) && map.FootDistance(from, to) == 1 &&
        !map.IsDiagonalFootStep(from, to) &&
        (IsSwimmingWater(map, from) || IsSwimmingWater(map, to)) &&
        (map.IsPassable(from) || IsSwimmingWater(map, from)) &&
        (map.IsPassable(to) || IsSwimmingWater(map, to)) &&
        map.BridgeDecks?.ContainsKey(from) != true && map.BridgeDecks?.ContainsKey(to) != true;

    private static readonly (int X, int Y)[] NeighborOffsets =
        [(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)];

    public static IEnumerable<GridPoint> Neighbors(SeededMap map, GridPoint point)
    {
        // Keep the foot search's stable order, including the short east/west wrap.
        var seen = map.WrapsEastWest && map.Width < 3 ? new HashSet<GridPoint>() : null;
        foreach (var (dx, dy) in NeighborOffsets)
        {
            var x = point.X + dx;
            if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
            var next = new GridPoint(x, point.Y + dy);
            if (seen?.Add(next) == false || !CanStep(map, point, next)) continue;
            yield return next;
        }
    }
}
