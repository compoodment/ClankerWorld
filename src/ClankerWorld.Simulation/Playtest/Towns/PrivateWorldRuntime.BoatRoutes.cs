using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private HashSet<GridPoint>? cachedBoatWaterObstacles;
    private readonly Dictionary<(GridPoint Origin, string PortId, string DefinitionId, GridPoint PortPosition), IReadOnlyList<GridPoint>> boatWaterRoutes = [];

    private IReadOnlyList<GridPoint> BoatWaterRoute(GridPoint origin, PlacedBuilding destination)
    {
        // Boat choices ask about the same idle asset for several agents on every
        // tick. Cache only deterministic geography, never permissions or occupancy.
        // A new bridge or water building invalidates every affected saved route.
        var obstacles = WaterObstacles().Where(point => !map.IsLand(point)).ToHashSet();
        if (cachedBoatWaterObstacles is null || !cachedBoatWaterObstacles.SetEquals(obstacles))
        {
            cachedBoatWaterObstacles = obstacles;
            boatWaterRoutes.Clear();
        }
        var key = (origin, destination.InstanceId, destination.DefinitionId, destination.Position);
        if (boatWaterRoutes.TryGetValue(key, out var cached)) return cached;
        var route = PortNavigationRules.WaterRoute(map, origin, Berths(map, PortGeometryFor(destination)), obstacles);
        if (boatWaterRoutes.Count >= 128) boatWaterRoutes.Clear(); // Bound the optional runtime cache.
        boatWaterRoutes.Add(key, route);
        return route;
    }
}
