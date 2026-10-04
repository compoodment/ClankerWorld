using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Authoritative first-Town prototype state. Identity, membership and the
/// border are world facts; wrapped seams and overlapping Town claims are not
/// modeled yet. <see cref="ResidentIds"/> is the only record of Town
/// membership: household, House residence and position never change it.
/// </summary>
public sealed record TownRuntimeState(
    string Id,
    string Name,
    string FoundingState,
    long FoundedTick,
    IReadOnlyList<string> ResidentIds,
    IReadOnlyList<string> AssignedBuildingIds,
    IReadOnlyList<GridPoint> BorderTiles,
    GridPoint? OriginSite = null,
    TownGovernanceState? Governance = null,
    TownGovernmentState? Government = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<TownAdmissionRecord>? Admissions = null)
{
    [JsonRequired]
    public TownLandHearingState LandHearings { get; init; } = TownLandHearingState.Create();

    [JsonRequired]
    public IReadOnlyList<TownConstructionProject> Projects { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<TownMarketState> Markets { get; init; } = [];

    [JsonRequired]
    public TownNonviolentState Nonviolent { get; init; } = TownNonviolentState.Create();
}

/// <summary>
/// What one passed admission proposal did to recorded membership. The council
/// proposal holds the votes; this record holds the newcomer's own acceptance
/// and the care group that actually moved, so an approval applies at most once.
/// </summary>
/// <param name="Status"><c>approved</c> waits for the newcomer to accept a request
/// someone else made; <c>admitted</c> changed membership; <c>lapsed</c> could not
/// be applied (see <paramref name="Reason"/>).</param>
/// <param name="PreviousTownId">For <c>approved</c>, the newcomer's Town when the
/// council approved; for <c>admitted</c>, the Town the care group left. Null means none.</param>
/// <param name="MemberIds">For <c>admitted</c>, the newcomer and the dependent children who moved with them.</param>
public sealed record TownAdmissionRecord(
    string ProposalId,
    string SubjectId,
    string Status,
    long DecidedTick,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousTownId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? MemberIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

/// <summary>
/// A Town's border keeps about <see cref="SpareTileMargin"/> tiles of spare
/// land around its buildings and Roads, following the Town's shape rather than
/// a rectangle, so there is room to build inside it. Water is left out. The
/// saved border is authoritative: it only grows, as buildings and Roads join.
/// </summary>
public static class TownBorderRules
{
    public const string FirstTownId = "town:first";
    public const string FirstTownName = "First Town";
    public const int SpareTileMargin = 3;

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
        return new TownRuntimeState(
            FirstTownId,
            FirstTownName,
            founded ? "founded" : "founding",
            0,
            (residents ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            [],
            Around(map, anchors),
            originSite);
    }

    /// <summary>
    /// Land within the margin of the given tiles: up to three tiles straight
    /// out, with the corners rounded off.
    /// </summary>
    public static IReadOnlyList<GridPoint> Around(SeededMap map, IEnumerable<GridPoint> core)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(core);
        var tiles = new HashSet<GridPoint>();
        foreach (var point in core)
            for (var dy = -SpareTileMargin; dy <= SpareTileMargin; dy++)
                for (var dx = -SpareTileMargin; dx <= SpareTileMargin; dx++)
                {
                    var tile = new GridPoint(point.X + dx, point.Y + dy);
                    if (Math.Abs(dx) + Math.Abs(dy) <= SpareTileMargin + 1 && map.IsLand(tile)) tiles.Add(tile);
                }
        return Ordered(tiles);
    }

    /// <summary>The border grown to keep spare land around more tiles, such as a new building or Road.</summary>
    public static IReadOnlyList<GridPoint> Expand(SeededMap map, TownRuntimeState town, IEnumerable<GridPoint> core)
    {
        ArgumentNullException.ThrowIfNull(town);
        return Ordered(town.BorderTiles.Concat(Around(map, core)).ToHashSet());
    }

    public static IReadOnlyList<GridPoint> ExpandForBuilding(
        SeededMap map,
        TownRuntimeState town,
        GridPoint position,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return Expand(map, town, Rectangle(position, width, height));
    }

    /// <summary>Whether every tile of a footprint is inside the border or directly beside it.</summary>
    public static bool IsWithinOrAdjacent(TownRuntimeState town, GridPoint position, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(town);
        var border = town.BorderTiles.ToHashSet();
        return Rectangle(position, width, height).All(tile =>
            Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx =>
                border.Contains(new GridPoint(tile.X + dx, tile.Y + dy)))));
    }

    private static IEnumerable<GridPoint> Rectangle(GridPoint position, int width, int height) =>
        Enumerable.Range(0, height).SelectMany(dy =>
            Enumerable.Range(0, width).Select(dx => new GridPoint(position.X + dx, position.Y + dy)));

    private static GridPoint[] Ordered(IEnumerable<GridPoint> tiles) =>
        tiles.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
}
