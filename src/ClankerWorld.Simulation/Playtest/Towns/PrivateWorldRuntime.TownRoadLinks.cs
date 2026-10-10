using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private GridPoint[] BuildingRoadEntrances(PlacedBuilding building, HashSet<GridPoint> occupied)
    {
        var design = BuildingStorageRules.EffectiveDefinition(
            worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building);
        return WorldContentSimulationRules.Footprint(design, building).SelectMany(point => map.FootNeighbors(point)
                .Where(next => !map.IsDiagonalFootStep(point, next)))
            .Where(point => (!occupied.Contains(point) || roadTiles.Contains(point)) && map.IsBuildable(point) &&
                WorldContentSimulationRules.IsEntrance(design, building.Position, point) &&
                (!design.Tags.Contains(TownHallContent.HallTag, StringComparer.Ordinal) || point == TownHallContent.Entrance(building.Position)) &&
                (!design.Tags.Any(tag => tag is MarketContent.HallTag or MarketContent.StallTag) || point == building.Entrance) &&
                (!PortNavigationRules.IsPort(design) || point == building.Entrance))
            .Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
    }

    private void GenerateRoadBetweenTowns(TownRuntimeState town, PlacedBuilding firstBuilding)
    {
        if (town.OriginSite is not { } origin) return;
        var nearest = towns.Where(other => other.Id != town.Id && other.OriginSite is not null)
            .OrderBy(other => map.FootDistance(origin, other.OriginSite!.Value))
            .ThenBy(other => other.Id, StringComparer.Ordinal).FirstOrDefault();
        if (nearest is null) return;
        var physical = RoadOccupiedTiles();
        var occupied = physical.Concat(HouseholdLandHeldByOthers(null)).ToHashSet();
        var nearestBorder = nearest.BorderTiles.ToHashSet();
        var network = roadTiles.Where(nearestBorder.Contains).ToHashSet();
        if (network.Count == 0)
        {
            network.UnionWith(worldSimulation.Buildings.Where(building => building.TownId == nearest.Id && nearestBorder.Contains(building.Position))
                .SelectMany(building => BuildingRoadEntrances(building, occupied)).Where(nearestBorder.Contains));
            if (network.Count == 0 && map.IsBuildable(nearest.OriginSite!.Value) && !occupied.Contains(nearest.OriginSite.Value))
                network.Add(nearest.OriginSite.Value);
        }
        var localBuilding = town.BorderTiles.Contains(firstBuilding.Position);
        var starts = localBuilding ? BuildingRoadEntrances(firstBuilding, occupied) :
            map.IsBuildable(origin) && (!occupied.Contains(origin) || roadTiles.Contains(origin)) ? new[] { origin } : [];
        var request = new RoadRouteRequest(map, starts, network,
            occupied, Bridges, roadTiles, ReuseRoads: true, Occupied: physical);
        var result = RoadRoutePlanner.Plan(request);
        var failure = result.Proposal is { } proposal ? RoadRoutePlanner.Validate(request, proposal) : result.Outcome;
        if (failure is not null)
        {
            AppendEvent("town_road_link_unconnected", $"{town.Id}|{nearest.Id}|{failure}");
            return;
        }
        var route = result.Proposal!;
        var added = route.RoadTiles.Count(roadTiles.Add);
        if (localBuilding) SetBuildingEntrance(firstBuilding.InstanceId, route.RoadTiles[0]);
        CommitRoadBridges(route.NewCrossings, $"road:town-link:{town.Id}:{nearest.Id}");
        AppendEvent("town_road_linked", $"{town.Id}|{nearest.Id}|tiles:{added}");
    }
}
