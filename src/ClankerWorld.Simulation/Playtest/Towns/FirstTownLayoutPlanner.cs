using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A feasible five-building starting layout, not an accepted or saved Town.</summary>
public sealed record FirstTownLayout(
    GridPoint RoughSite,
    IReadOnlyList<FirstTownLayoutBuilding> Buildings,
    IReadOnlyList<GridPoint> RoadTiles);

/// <summary>A planned building; <see cref="Entrance"/> is the Road tile its door faces.</summary>
public sealed record FirstTownLayoutBuilding(string Role, string DefinitionId,
    GridPoint Position, int Width, int Height, GridPoint Entrance);

/// <summary>
/// Lays out the first Town street first: a main road that winds with the land
/// through the chosen site, side streets branching off it at uneven spacing,
/// angles and lengths, then the five starting buildings on lots whose doors
/// face those streets. Streets are cut back to run a few tiles past the last
/// door on them. The same site on the same map always gives the same layout.
/// This does not alter the world; paused setup remains the authority for
/// accepting a plan and its starter supplies.
/// </summary>
public static class FirstTownLayoutPlanner
{
    private const int StartSearchRadius = 3;
    private const int AttemptsPerStart = 4;
    private const int MaximumStarts = 12;

    private static readonly (string Role, BuildingDefinition Definition)[] Starters =
    [
        ("warehouse", WarehouseContent.Warehouse2x2()),
        ("house-a", HouseContent.House1x1()),
        ("house-b", HouseContent.House1x1()),
        ("farmhouse", FarmContent.Farmhouse1x1()),
        ("blacksmith", BlacksmithContent.Blacksmith1x2()),
    ];

    // The Warehouse is the largest and the Town's centre, so it chooses first.
    private static readonly string[] PlacementOrder = ["warehouse", "blacksmith", "farmhouse", "house-a", "house-b"];

    public static FirstTownLayout? Plan(SeededMap map, GridPoint roughSite)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsBuildable(roughSite)) return null;
        var blocked = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        var random = Pcg32XshRrV1.Create(string.IsNullOrWhiteSpace(map.ManifestDigest) ? "first-town" : map.ManifestDigest,
            $"first-town-layout/{roughSite.X},{roughSite.Y}");
        foreach (var start in StartTiles(map, roughSite, blocked))
            for (var attempt = 0; attempt < AttemptsPerStart; attempt++)
                if (TryPlan(map, roughSite, start, blocked, random) is { } layout)
                    return layout;
        return null;
    }

    /// <summary>The site itself, then the nearest clear tiles around it.</summary>
    private static IEnumerable<GridPoint> StartTiles(SeededMap map, GridPoint site, HashSet<GridPoint> blocked) =>
        Enumerable.Range(-StartSearchRadius, StartSearchRadius * 2 + 1)
            .SelectMany(dy => Enumerable.Range(-StartSearchRadius, StartSearchRadius * 2 + 1)
                .Select(dx => new GridPoint(site.X + dx, site.Y + dy)))
            .Where(point => map.IsBuildable(point) && !blocked.Contains(point))
            .OrderBy(point => Math.Abs(point.X - site.X) + Math.Abs(point.Y - site.Y))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .Take(MaximumStarts);

    private static FirstTownLayout? TryPlan(SeededMap map, GridPoint site, GridPoint start,
        HashSet<GridPoint> blocked, Pcg32XshRrV1 random)
    {
        var streets = new TownStreets(map, blocked, [start]);
        var mainRoad = LayMainRoad(streets, start, random);
        LaySideStreets(streets, mainRoad, random);

        var lots = new Dictionary<string, (GridPoint Anchor, GridPoint Entrance)>(StringComparer.Ordinal);
        var footprints = new HashSet<GridPoint>();
        foreach (var role in PlacementOrder)
        {
            var definition = Starters.Single(item => item.Role == role).Definition;
            if (BestLot(map, streets, blocked, footprints, definition, site, role == "warehouse", random) is not { } lot)
                return null;
            lots[role] = lot;
            footprints.UnionWith(WorldContentSimulationRules.Footprint(definition, lot.Anchor));
        }

        var entrances = lots.Values.Select(lot => lot.Entrance).ToHashSet();
        var roads = TownStreets.TrimToDoors(streets.Tiles, entrances);
        return new FirstTownLayout(site,
            Starters.Select(item => new FirstTownLayoutBuilding(item.Role, item.Definition.CanonicalId,
                lots[item.Role].Anchor, item.Definition.Width, item.Definition.Height, lots[item.Role].Entrance)).ToArray(),
            roads.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray());
    }

    /// <summary>
    /// The main road runs both ways from the start along the most open line
    /// through it, bending with the land.
    /// </summary>
    private static List<GridPoint> LayMainRoad(TownStreets streets, GridPoint start, Pcg32XshRrV1 random)
    {
        var heading = Enumerable.Range(0, 4)
            .Select(direction => (direction, open: streets.OpenRun(start, direction) +
                streets.OpenRun(start, TownStreets.Turn(direction, 4)) + (int)(random.NextUInt() % 3)))
            .OrderByDescending(item => item.open).ThenBy(item => item.direction).First().direction;
        var ahead = streets.Wander(start, heading, 14, 0.22, random);
        var behind = streets.Wander(start, TownStreets.Turn(heading, 4), 11, 0.22, random);
        behind.Reverse();
        return [.. behind, start, .. ahead];
    }

    /// <summary>
    /// Side streets leave the main road every three to five tiles, mostly at
    /// right angles and sometimes at 45°, usually alternating sides.
    /// </summary>
    private static void LaySideStreets(TownStreets streets, List<GridPoint> mainRoad, Pcg32XshRrV1 random)
    {
        var side = TownStreets.Chance(random, 0.5) ? 1 : -1;
        for (var index = TownStreets.Between(random, 2, 3); index < mainRoad.Count - 2; index += TownStreets.Between(random, 3, 5))
        {
            var along = TownStreets.DirectionBetween(mainRoad[index], mainRoad[index + 1]);
            var angle = (random.NextUInt() % 5) switch { 3 => 1, 4 => 3, _ => 2 };
            streets.Wander(mainRoad[index], TownStreets.Turn(along, side * angle),
                TownStreets.Between(random, 4, 8), 0.15, random, minimum: 3);
            if (TownStreets.Chance(random, 0.8)) side = -side;
        }
    }

    /// <summary>
    /// The best lot for a building: its door opens onto a street, and its
    /// footprint is clear and keeps a one-tile gap from other buildings.
    /// Nearer the site is better; the Warehouse keeps closest to it.
    /// </summary>
    private static (GridPoint Anchor, GridPoint Entrance)? BestLot(SeededMap map, TownStreets streets,
        HashSet<GridPoint> blocked, HashSet<GridPoint> footprints, BuildingDefinition definition, GridPoint site, bool central, Pcg32XshRrV1 random)
    {
        (GridPoint Anchor, GridPoint Entrance, double Score)? best = null;
        foreach (var entrance in streets.Tiles)
            for (var side = 0; side < 4; side++)
                for (var along = 0; along < (side < 2 ? definition.Width : definition.Height); along++)
                {
                    var anchor = TownStreets.AnchorFacing(entrance, side, along, definition.Width, definition.Height);
                    var footprint = WorldContentSimulationRules.Footprint(definition, anchor).ToArray();
                    if (footprint.Any(tile => !map.IsBuildable(tile) || blocked.Contains(tile) ||
                            streets.Contains(tile) || footprints.Contains(tile)) ||
                        footprint.Any(tile => TownStreets.Directions.Any(step =>
                            footprints.Contains(new GridPoint(tile.X + step.X, tile.Y + step.Y)))))
                        continue;
                    var distance = Math.Abs(anchor.X - site.X) + Math.Abs(anchor.Y - site.Y);
                    var score = distance * (central ? 3 : 1) + random.NextUInt() % 250 / 100.0;
                    if (best is null || score < best.Value.Score) best = (anchor, entrance, score);
                }
        return best is { } found ? (found.Anchor, found.Entrance) : null;
    }
}
