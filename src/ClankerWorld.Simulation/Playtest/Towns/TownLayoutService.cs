using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A bounded, player-readable reason for a construction site's rank.</summary>
public sealed record TownConstructionSiteReason(string Code, string Description);

public static class TownConstructionSiteReasonCodes
{
    public const string BuildableGround = "buildable_ground";
    public const string FootAccess = "foot_access";
    public const string TownCompactness = "town_compactness";
    public const string TownGrowth = "town_growth";
    public const string OpenMeadow = "terrain_open_meadow";
    public const string ForestPreservation = "terrain_forest";
    public const string NearbyMaterial = "nearby_material";
    public const string PurposeCluster = "purpose_cluster";
    public const string RoadFrontage = "road_frontage";
}

/// <summary>
/// A legal construction site ranked by the shared Town layout service.
/// RouteCost uses the map's deterministic movement-cost units.
/// </summary>
public sealed record TownConstructionSiteCandidate(
    GridPoint Position,
    int Score,
    int RouteCost,
    int TownBorderGrowthTiles,
    IReadOnlyList<TownConstructionSiteReason> Reasons);

public sealed record TownLayoutResource(MapResource Resource, bool Available);

public sealed record TownLayoutBuilding(PlacedBuilding Building, BuildingDefinition Definition);

/// <summary>
/// Immutable runtime facts used by every construction-site query in one
/// inhabitant decision or project continuation. Candidate anchors are clipped
/// to the bounds of the first Town's border plus its spare-land margin.
/// </summary>
public sealed class TownLayoutContext
{
    public TownLayoutContext(
        SeededMap map,
        TownRuntimeState? town,
        IEnumerable<GridPoint> occupiedTiles,
        IReadOnlyDictionary<GridPoint, int> reachableFootCosts,
        IEnumerable<TownLayoutResource> resources,
        IEnumerable<TownLayoutBuilding> buildings,
        IEnumerable<GridPoint>? roadTiles = null,
        IEnumerable<GridPoint>? requiredNeighborTiles = null,
        IEnumerable<GridPoint>? requiredLandTiles = null,
        GridPoint? requiredEntranceOffset = null,
        IEnumerable<GridPoint>? requiredFootprintOffsets = null,
        IEnumerable<GridPoint>? permittedRoadOffsets = null,
        IEnumerable<GridPoint>? protectedTiles = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(occupiedTiles);
        ArgumentNullException.ThrowIfNull(reachableFootCosts);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(buildings);
        if (map.Width < 1 || map.Height < 1)
            throw new ArgumentOutOfRangeException(nameof(map));

        Map = map;
        Town = town;
        OccupiedTiles = occupiedTiles.ToHashSet();
        ReachableFootCosts = reachableFootCosts.ToDictionary(item => item.Key, item => item.Value);
        if (ReachableFootCosts.Any(item => !map.Contains(item.Key) || item.Value < 0))
            throw new ArgumentException("Reachable site costs must be non-negative map positions.", nameof(reachableFootCosts));
        Resources = resources.ToArray();
        Buildings = buildings.ToArray();
        RequiredNeighborTiles = requiredNeighborTiles?.ToHashSet();
        RequiredLandTiles = requiredLandTiles?.ToHashSet();
        RequiredEntranceOffset = requiredEntranceOffset;
        RequiredFootprintOffsets = requiredFootprintOffsets?.ToHashSet();
        PermittedRoadOffsets = (permittedRoadOffsets ?? []).ToHashSet();
        ProtectedTiles = (protectedTiles ?? []).ToHashSet();
        RoadTiles = (roadTiles ?? []).ToHashSet();
        CandidateAnchors = town is null
            ? ReachableFootCosts.Keys.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray()
            : CandidateBounds(map, town);
        TownBorder = town?.BorderTiles.ToHashSet();
    }

    // Gathered once per layout rather than for every site a ranking scores.
    internal IReadOnlySet<GridPoint>? TownBorder { get; }

    private readonly Dictionary<string, GridPoint[]> availableMaterialPositions = new(StringComparer.Ordinal);

    internal GridPoint[] AvailableMaterialPositions(string kind, Func<string, string, bool> matches)
    {
        if (!availableMaterialPositions.TryGetValue(kind, out var positions))
        {
            positions = Resources.Where(item => item.Available && matches(item.Resource.Kind, kind))
                .Select(item => item.Resource.Position).ToArray();
            availableMaterialPositions[kind] = positions;
        }
        return positions;
    }

    public SeededMap Map { get; }

    public TownRuntimeState? Town { get; }

    public IReadOnlySet<GridPoint> OccupiedTiles { get; }

    public IReadOnlyDictionary<GridPoint, int> ReachableFootCosts { get; }

    public IReadOnlyList<TownLayoutResource> Resources { get; }

    public IReadOnlyList<TownLayoutBuilding> Buildings { get; }

    public IReadOnlySet<GridPoint> RoadTiles { get; }

    public IReadOnlyList<GridPoint> CandidateAnchors { get; }

    /// <summary>
    /// When set, a legal footprint must lie within <see cref="NeighborReach"/>
    /// tiles of one of these, as a Silo must stand near its household's
    /// Farmhouse; sites that touch rank first.
    /// </summary>
    public IReadOnlySet<GridPoint>? RequiredNeighborTiles { get; }

    public IReadOnlySet<GridPoint>? RequiredLandTiles { get; }

    public GridPoint? RequiredEntranceOffset { get; }

    public IReadOnlySet<GridPoint>? RequiredFootprintOffsets { get; }

    public IReadOnlySet<GridPoint> PermittedRoadOffsets { get; }

    /// <summary>Occupied tiles, such as another project's site or doorway, that stay closed even where a Road is permitted.</summary>
    public IReadOnlySet<GridPoint> ProtectedTiles { get; }

    /// <summary>How far, in tiles including diagonals, a site may be from its required neighbor. Provisional.</summary>
    public const int NeighborReach = 2;

    public TerrainKind? TerrainAt(GridPoint position) => Map.TerrainKindAt(position);

    internal static IReadOnlyList<GridPoint> CandidateBounds(SeededMap map, TownRuntimeState? town)
    {
        if (town is null)
        {
            return Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPoint(x, y))).ToArray();
        }

        if (town.BorderTiles.Count == 0)
            throw new ArgumentException("A Town layout needs a non-empty border.", nameof(town));

        var minX = Math.Max(0, town.BorderTiles.Min(point => point.X) - TownBorderRules.SpareTileMargin);
        var minY = Math.Max(0, town.BorderTiles.Min(point => point.Y) - TownBorderRules.SpareTileMargin);
        var maxX = Math.Min(map.Width - 1, town.BorderTiles.Max(point => point.X) + TownBorderRules.SpareTileMargin);
        var maxY = Math.Min(map.Height - 1, town.BorderTiles.Max(point => point.Y) + TownBorderRules.SpareTileMargin);
        return Enumerable.Range(minY, maxY - minY + 1)
            .SelectMany(y => Enumerable.Range(minX, maxX - minX + 1).Select(x => new GridPoint(x, y))).ToArray();
    }
}

/// <summary>
/// One authoritative, deterministic construction-site query for Town work.
/// Legal sites are returned with bounded ranking evidence; no caller may
/// replace this with a map-wide first-match scan.
/// </summary>
public static class TownLayoutService
{
    public const int DefaultCandidateLimit = 5;
    public const int MaximumCandidateLimit = 8;

    public static IReadOnlyList<TownConstructionSiteCandidate> RankConstructionSites(
        TownLayoutContext context,
        BuildingDefinition definition,
        int maximumCandidates = DefaultCandidateLimit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        if (maximumCandidates is < 1 or > MaximumCandidateLimit)
            throw new ArgumentOutOfRangeException(nameof(maximumCandidates));

        return context.CandidateAnchors
            .Select(position => TryEvaluate(context, definition, position, out var candidate) ? candidate : null)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.TownBorderGrowthTiles)
            .ThenBy(candidate => candidate.RouteCost)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .Take(maximumCandidates)
            .ToArray();
    }

    public static bool TryEvaluateConstructionSite(
        TownLayoutContext context,
        BuildingDefinition definition,
        GridPoint position,
        out TownConstructionSiteCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        return TryEvaluate(context, definition, position, out candidate);
    }

    private static bool TryEvaluate(
        TownLayoutContext context,
        BuildingDefinition definition,
        GridPoint position,
        out TownConstructionSiteCandidate? candidate)
    {
        candidate = null;
        var map = context.Map;
        if (!map.Contains(position))
            return false;
        var footprint = context.RequiredFootprintOffsets is { } offsets
            ? offsets.Select(offset => new GridPoint(position.X + offset.X, position.Y + offset.Y)).ToArray()
            : Footprint(definition, position).ToArray();
        if (footprint.Any(point => !map.Contains(point) || !map.IsBuildable(point) ||
                context.OccupiedTiles.Contains(point) && !(context.RoadTiles.Contains(point) &&
                    !context.ProtectedTiles.Contains(point) &&
                    context.PermittedRoadOffsets.Contains(new GridPoint(point.X - position.X, point.Y - position.Y)))))
            return false;
        if (context.RequiredLandTiles is { } titled && footprint.Any(point => !titled.Contains(point)))
            return false;
        if (context.RequiredEntranceOffset is { } offset)
        {
            var entrance = new GridPoint(position.X + offset.X, position.Y + offset.Y);
            if (!map.IsBuildable(entrance) ||
                context.OccupiedTiles.Contains(entrance) && !context.RoadTiles.Contains(entrance) ||
                !context.ReachableFootCosts.ContainsKey(entrance))
                return false;
        }
        var neighborDistance = context.RequiredNeighborTiles is { } neighbors
            ? footprint.SelectMany(point => neighbors.Select(neighbor =>
                Math.Max(Math.Abs(neighbor.X - point.X), Math.Abs(neighbor.Y - point.Y)))).DefaultIfEmpty(int.MaxValue).Min()
            : 0;
        if (neighborDistance > TownLayoutContext.NeighborReach)
            return false;
        if (context.TownBorder is { } border &&
            !TownBorderRules.IsWithinOrAdjacent(border, position, definition.Width, definition.Height))
            return false;
        if (!context.ReachableFootCosts.TryGetValue(position, out var routeCost))
            return false;

        var reasons = new List<TownConstructionSiteReason>
        {
            new(TownConstructionSiteReasonCodes.BuildableGround, $"All {footprint.Length} footprint tiles are clear, buildable ground."),
            new(TownConstructionSiteReasonCodes.FootAccess, "A legal unoccupied foot route reaches this site."),
        };
        var score = Math.Max(0, 24 - routeCost / 100);
        var expansion = ExpansionFor(context, definition, position);
        if (context.Town is not null)
        {
            if (expansion == 0)
            {
                score += 32;
                reasons.Add(new(TownConstructionSiteReasonCodes.TownCompactness, "Fits inside the current Town border."));
            }
            else
            {
                score -= Math.Min(expansion, 40);
                reasons.Add(new(TownConstructionSiteReasonCodes.TownGrowth, $"Expands the Town border by {expansion} tiles."));
            }
        }

        // New buildings prefer free frontage on an existing street, so the
        // Town grows along its Roads.
        if (FacesRoad(context, definition, position))
        {
            score += 24;
            reasons.Add(new(TownConstructionSiteReasonCodes.RoadFrontage, "Its door can face an existing Road."));
        }

        var terrain = context.TerrainAt(position);
        if (terrain == TerrainKind.Meadow)
        {
            score += 8;
            reasons.Add(new(TownConstructionSiteReasonCodes.OpenMeadow, "Uses open meadow ground."));
        }
        else if (terrain == TerrainKind.Forest)
        {
            score -= 4;
            reasons.Add(new(TownConstructionSiteReasonCodes.ForestPreservation, "Open meadow sites rank ahead of forest ground."));
        }

        if (context.RequiredNeighborTiles is not null)
        {
            score += neighborDistance == 1 ? 12 : 0;
            reasons.Add(new(TownConstructionSiteReasonCodes.PurposeCluster, neighborDistance == 1
                ? "Stands right beside the building it serves."
                : $"Stands {neighborDistance} tiles from the building it serves."));
        }
        AddMaterialReasons(context, definition, position, reasons, ref score);
        AddPurposeReason(context, definition, position, reasons, ref score);
        candidate = new TownConstructionSiteCandidate(position, score, routeCost, expansion, reasons.ToArray());
        return true;
    }

    private static void AddMaterialReasons(
        TownLayoutContext context,
        BuildingDefinition definition,
        GridPoint position,
        List<TownConstructionSiteReason> reasons,
        ref int score)
    {
        foreach (var kind in definition.BuildCosts.Select(item => item.ResourceId).Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            var nearest = context.AvailableMaterialPositions(kind, ResourceMatches)
                .Select(source => context.Map.FootDistance(position, source))
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            if (nearest > 5)
                continue;

            var bonus = nearest <= 1 ? 12 : nearest <= 3 ? 8 : 4;
            score += bonus;
            reasons.Add(new(TownConstructionSiteReasonCodes.NearbyMaterial, $"Available {kind} is {nearest} map tile(s) away."));
        }
    }

    private static void AddPurposeReason(
        TownLayoutContext context,
        BuildingDefinition definition,
        GridPoint position,
        List<TownConstructionSiteReason> reasons,
        ref int score)
    {
        if (definition.Tags.Count == 0)
            return;

        var related = context.Buildings.Where(item =>
                (context.Town is null || item.Building.TownId == context.Town.Id) &&
                item.Definition.Tags.Intersect(definition.Tags, StringComparer.Ordinal).Any())
            .Select(item => (Building: item, Distance: context.Map.FootDistance(position, item.Building.Position)))
            .Where(item => item.Distance <= 5)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Building.Building.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (related.Building is null)
            return;

        var role = definition.Tags.Intersect(related.Building.Definition.Tags, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).First();
        score += related.Distance <= 2 ? 10 : 5;
        var relationship = context.Town is null ? "existing" : "Town's existing";
        reasons.Add(new(TownConstructionSiteReasonCodes.PurposeCluster, $"Near {relationship} {role} building."));
    }

    private static bool FacesRoad(TownLayoutContext context, BuildingDefinition definition, GridPoint position) =>
        context.RoadTiles.Count > 0 && Footprint(definition, position).Any(tile => TownStreets.Directions
            .Where(step => step.X == 0 || step.Y == 0)
            .Any(step => context.RoadTiles.Contains(new GridPoint(tile.X + step.X, tile.Y + step.Y))));

    // The tiles the border would gain: those around the footprint it doesn't already hold, which is what
    // TownBorderRules.ExpandForBuilding adds, without copying and sorting the whole border for every site.
    private static int ExpansionFor(TownLayoutContext context, BuildingDefinition definition, GridPoint position)
    {
        if (context.TownBorder is not { } border)
            return 0;

        return TownBorderRules.Around(context.Map, Footprint(definition, position)).Count(point => !border.Contains(point));
    }

    private static IEnumerable<GridPoint> Footprint(BuildingDefinition definition, GridPoint origin)
    {
        for (var y = 0; y < definition.Height; y++)
            for (var x = 0; x < definition.Width; x++)
                yield return new GridPoint(origin.X + x, origin.Y + y);
    }

    private static bool ResourceMatches(string sourceKind, string requiredKind) =>
        sourceKind == requiredKind || requiredKind == "wood" && sourceKind == "construction";
}

/// <summary>Stable legal intention IDs for construction and site choices.</summary>
public static class TownConstructionCandidateIds
{
    private const string BuildingPrefix = "build:building:";
    private const string RecipePrefix = "build:recipe:";
    private const string SiteMarker = ":site:";

    public static string Building(string definitionId, GridPoint position) =>
        BuildingPrefix + definitionId + SiteMarker + position.X + "," + position.Y;

    public static string Recipe(string definitionId) => RecipePrefix + definitionId;

    public static bool TryParse(string candidateId, out TownConstructionCandidateSelection selection)
    {
        selection = default;
        if (string.IsNullOrWhiteSpace(candidateId))
            return false;

        var isBuilding = candidateId.StartsWith(BuildingPrefix, StringComparison.Ordinal);
        var prefixLength = isBuilding ? BuildingPrefix.Length :
            candidateId.StartsWith(RecipePrefix, StringComparison.Ordinal) ? RecipePrefix.Length : 0;
        if (prefixLength == 0)
            return false;

        var value = candidateId[prefixLength..];
        GridPoint? site = null;
        var marker = value.LastIndexOf(SiteMarker, StringComparison.Ordinal);
        if (marker >= 0)
        {
            if (!isBuilding || marker == 0)
                return false;
            var coordinates = value[(marker + SiteMarker.Length)..].Split(',', StringSplitOptions.None);
            if (coordinates.Length != 2 ||
                !int.TryParse(coordinates[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !int.TryParse(coordinates[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var y))
                return false;
            site = new GridPoint(x, y);
            value = value[..marker];
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > 480)
            return false;
        selection = new TownConstructionCandidateSelection(isBuilding, value, site);
        return true;
    }
}

public readonly record struct TownConstructionCandidateSelection(
    bool IsBuilding,
    string DefinitionId,
    GridPoint? SitePosition);
