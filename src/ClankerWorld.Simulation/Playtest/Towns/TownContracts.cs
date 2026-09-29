using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Authoritative first-Town prototype state. Identity and membership are
/// world facts; the rectangular border rule is deliberately provisional.
/// </summary>
public sealed record TownRuntimeState(
    string Id,
    string Name,
    string FoundingState,
    long FoundedTick,
    IReadOnlyList<string> ResidentIds,
    IReadOnlyList<string> AssignedBuildingIds,
    IReadOnlyList<GridPoint> BorderTiles,
    GridPoint? OriginSite = null);

/// <summary>
/// A conservative single-Town geometry prototype: start with the starter-camp
/// object bounds plus one tile of spare space, then grow the rectangle around
/// any Town-assigned building footprint plus one tile. Bounds clip at map
/// edges. Wrapped seams and multiple/overlapping Town claims are not modeled.
/// </summary>
public static class TownBorderRules
{
    public const string FirstTownId = "town:first";
    public const string FirstTownName = "First Town";
    public const int SpareTileMargin = 1;

    public static TownRuntimeState CreateFirstTown(SeededMap map, IEnumerable<string>? residents = null,
        bool founded = false, GridPoint? originSite = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (originSite is { } selected && !map.IsBuildable(selected))
            throw new ArgumentException("The first Town site must be buildable ground.", nameof(originSite));
        GridPoint[] anchors = originSite is { } site ? [site] : map.CampObjects
            .Where(item => item.Kind is not ("founder" or "bedroll"))
            .Select(item => item.Position).ToArray();
        if (anchors.Length == 0)
            throw new ArgumentException("Choose a Town origin before establishing a Town on a map without camp objects.", nameof(originSite));
        var border = Rectangle(map,
            anchors.Min(point => point.X) - SpareTileMargin,
            anchors.Min(point => point.Y) - SpareTileMargin,
            anchors.Max(point => point.X) + SpareTileMargin,
            anchors.Max(point => point.Y) + SpareTileMargin);
        return new TownRuntimeState(
            FirstTownId,
            FirstTownName,
            founded ? "founded" : "founding",
            0,
            (residents ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            [],
            border,
            originSite);
    }

    public static bool IsWithinOrAdjacent(TownRuntimeState town, GridPoint position, int width, int height)
    {
        var bounds = Bounds(town.BorderTiles);
        return position.X >= bounds.MinX - SpareTileMargin &&
            position.Y >= bounds.MinY - SpareTileMargin &&
            position.X + width - 1 <= bounds.MaxX + SpareTileMargin &&
            position.Y + height - 1 <= bounds.MaxY + SpareTileMargin;
    }

    public static IReadOnlyList<GridPoint> ExpandForBuilding(
        SeededMap map,
        TownRuntimeState town,
        GridPoint position,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(town);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var bounds = Bounds(town.BorderTiles);
        return Rectangle(map,
            Math.Min(bounds.MinX, position.X - SpareTileMargin),
            Math.Min(bounds.MinY, position.Y - SpareTileMargin),
            Math.Max(bounds.MaxX, position.X + width - 1 + SpareTileMargin),
            Math.Max(bounds.MaxY, position.Y + height - 1 + SpareTileMargin));
    }

    public static IReadOnlyList<GridPoint> ExpectedBorder(
        SeededMap map,
        TownRuntimeState town,
        IEnumerable<PlacedBuilding> buildings,
        IReadOnlyDictionary<string, BuildingDefinition> definitions)
    {
        var expected = CreateFirstTown(map, town.ResidentIds,
            founded: town.FoundingState == "founded", originSite: town.OriginSite);
        foreach (var building in buildings.Where(item => item.TownId == town.Id)
                     .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(building.DefinitionId, out var definition))
                throw new InvalidDataException("A Town-assigned building has no active definition.");
            expected = expected with
            {
                AssignedBuildingIds = expected.AssignedBuildingIds.Append(building.InstanceId)
                    .Order(StringComparer.Ordinal).ToArray(),
                BorderTiles = ExpandForBuilding(map, expected, building.Position, definition.Width, definition.Height),
            };
        }
        return expected.BorderTiles;
    }

    private static GridPoint[] Rectangle(SeededMap map, int minX, int minY, int maxX, int maxY)
    {
        minX = Math.Clamp(minX, 0, map.Width - 1);
        minY = Math.Clamp(minY, 0, map.Height - 1);
        maxX = Math.Clamp(maxX, minX, map.Width - 1);
        maxY = Math.Clamp(maxY, minY, map.Height - 1);
        return Enumerable.Range(minY, maxY - minY + 1)
            .SelectMany(y => Enumerable.Range(minX, maxX - minX + 1).Select(x => new GridPoint(x, y)))
            .ToArray();
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) Bounds(IReadOnlyList<GridPoint> border)
    {
        if (border.Count == 0) throw new InvalidDataException("A Town border cannot be empty.");
        return (border.Min(point => point.X), border.Min(point => point.Y),
            border.Max(point => point.X), border.Max(point => point.Y));
    }
}
