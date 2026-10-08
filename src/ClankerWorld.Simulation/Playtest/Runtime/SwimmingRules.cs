using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Agent swimming geometry; ordinary foot, cart and animal movement stays separate.</summary>
public static class SwimmingRules
{
    // Provisional values for hands-on playtesting, as agreed in #1288.
    public const int StepCost = 800;
    public const int WarmthLossPerStep = 200;
    public const int MinimumStartingWarmth = 6_000;
    public const int MaximumStartingIllness = 2_499;
    public const int MaximumCarriedUnits = 4;

    public static bool IsSwimmingWater(SeededMap map, GridPoint point) =>
        map.Contains(point) && !map.IsPassable(point) &&
        (map.HydrologyAt(point) is WaterKind.Lake or WaterKind.River ||
         map.HydrologyAt(point) is null && map.TerrainKindAt(point) is TerrainKind.Lake or TerrainKind.River);

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
