using System.Globalization;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>What caused a bridge to be built. The two triggers stay separate.</summary>
public static class BridgeTriggers
{
    /// <summary>A generated Road's route met a bridgeable river.</summary>
    public const string Road = "road";

    /// <summary>Agents repeatedly waded across the same narrow crossing.</summary>
    public const string Traffic = "traffic";
}

/// <summary>The supported bridge designs: one straight plank deck per span length.</summary>
public static class BridgeDesigns
{
    public const string PlankSpanOne = "plank_span_1";
    public const string PlankSpanTwo = "plank_span_2";

    /// <summary>The widest bridgeable river, in tiles. Wider water is never bridged.</summary>
    public const int MaximumSpanTiles = 2;

    public static string ForSpan(int spanTiles) => spanTiles switch
    {
        1 => PlankSpanOne,
        2 => PlankSpanTwo,
        _ => throw new ArgumentOutOfRangeException(nameof(spanTiles), "Only rivers one or two tiles wide are bridgeable."),
    };
}

/// <summary>
/// A permanent, saved bridge. Movement, the map view and tile inspection all
/// read this same record: its deck tiles become a passable crossing only
/// through <see cref="SeededMap.BridgeDecks"/>, which is rebuilt from these
/// records. Entrances are listed west-to-east or north-to-south, and the span
/// runs between them.
/// </summary>
public sealed record BridgeState(
    string Id,
    string Design,
    string Trigger,
    IReadOnlyList<GridPoint> Entrances,
    IReadOnlyList<GridPoint> Span,
    long BuiltTick,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RouteId = null);

/// <summary>
/// A validated prospective crossing: both banks' entrance tiles, the whole
/// river span between them (one or two tiles) and the design that fits it.
/// Entrances are canonical: west-to-east or north-to-south.
/// </summary>
public sealed record RiverCrossing(
    GridPoint EntranceA,
    IReadOnlyList<GridPoint> Span,
    GridPoint EntranceB,
    BridgeAxis Axis,
    string Design)
{
    /// <summary>Stable identity derived only from the crossing's geometry.</summary>
    public string Id => RiverBridgeRules.BridgeId(Span[0], Axis, Span.Count);

    public IReadOnlyList<GridPoint> Entrances => [EntranceA, EntranceB];
}

/// <summary>
/// Deterministic geometry and spacing rules for river bridges. A crossing is
/// only over river water (never lake, ocean or coast), at most two tiles wide,
/// straight, and lands on buildable ground at both ends.
/// </summary>
public static class RiverBridgeRules
{
    /// <summary>
    /// Work limit for comparing the banks of two crossings. It bounds how much
    /// river is examined; it is not a spacing distance. When the limit is
    /// reached the banks are not treated as the same, so a needed bridge is
    /// never blocked by an unfinished comparison.
    /// </summary>
    public const int MaximumBankSearchTiles = 4_096;

    private static readonly (int X, int Y)[] Cardinal = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private static readonly (int X, int Y)[] Surrounding =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    public static string BridgeId(GridPoint firstSpanTile, BridgeAxis axis, int spanTiles) =>
        string.Create(CultureInfo.InvariantCulture,
            $"bridge-{firstSpanTile.X}-{firstSpanTile.Y}-{(axis == BridgeAxis.EastWest ? "ew" : "ns")}-{spanTiles}");

    public static BridgeAxis AxisOf(BridgeState bridge) =>
        bridge.Entrances[0].Y == bridge.Span[0].Y ? BridgeAxis.EastWest : BridgeAxis.NorthSouth;

    /// <summary>
    /// Finds the crossing that starts at <paramref name="bank"/> and heads one
    /// cardinal step in (<paramref name="dx"/>, <paramref name="dy"/>). Existing
    /// bridge decks are not a new crossing unless <paramref name="ignoreBuiltDecks"/>
    /// is set, which saved-bridge validation uses to re-check its own geometry.
    /// </summary>
    public static bool TryFindCrossing(SeededMap map, GridPoint bank, int dx, int dy,
        out RiverCrossing? crossing, bool ignoreBuiltDecks = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        crossing = null;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return false;
        bank = map.WrapColumn(bank);
        if (!map.Contains(bank) || !map.IsBuildable(bank)) return false;
        var span = new List<GridPoint>(BridgeDesigns.MaximumSpanTiles);
        var current = bank;
        while (true)
        {
            current = map.WrapColumn(new GridPoint(current.X + dx, current.Y + dy));
            if (!map.Contains(current) || current == bank) return false;
            if (map.IsBuildable(current)) break;
            if (!map.IsRiverWater(current) || !ignoreBuiltDecks && map.IsBridgeDeck(current) ||
                span.Count == BridgeDesigns.MaximumSpanTiles)
                return false;
            span.Add(current);
        }
        if (span.Count == 0) return false;
        var far = current;
        var axis = dy == 0 ? BridgeAxis.EastWest : BridgeAxis.NorthSouth;
        crossing = dx + dy > 0
            ? new RiverCrossing(bank, span.ToArray(), far, axis, BridgeDesigns.ForSpan(span.Count))
            : new RiverCrossing(far, span.AsEnumerable().Reverse().ToArray(), bank, axis, BridgeDesigns.ForSpan(span.Count));
        return true;
    }

    /// <summary>Re-derives a crossing from its identity and checks that it still exists on this map.</summary>
    public static bool TryResolve(SeededMap map, string id, out RiverCrossing? crossing, bool ignoreBuiltDecks = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        crossing = null;
        if (!TryParseId(id, out var first, out var axis, out var spanTiles)) return false;
        var (dx, dy) = axis == BridgeAxis.EastWest ? (1, 0) : (0, 1);
        var bank = map.WrapColumn(new GridPoint(first.X - dx, first.Y - dy));
        return TryFindCrossing(map, bank, dx, dy, out crossing, ignoreBuiltDecks) &&
            crossing!.Span.Count == spanTiles && crossing.Span[0] == first && crossing.Id == id;
    }

    public static BridgeState ToBridge(RiverCrossing crossing, string trigger, long builtTick, string? routeId) =>
        new(crossing.Id, crossing.Design, trigger, crossing.Entrances, crossing.Span.ToArray(), builtTick, routeId);

    public static RiverCrossing ToCrossing(BridgeState bridge) =>
        new(bridge.Entrances[0], bridge.Span, bridge.Entrances[1], AxisOf(bridge), bridge.Design);

    /// <summary>The passable decks for the map overlay, or null when there are no bridges.</summary>
    public static IReadOnlyDictionary<GridPoint, BridgeAxis>? Decks(IEnumerable<BridgeState> bridges)
    {
        ArgumentNullException.ThrowIfNull(bridges);
        var decks = new Dictionary<GridPoint, BridgeAxis>();
        foreach (var bridge in bridges)
        {
            var axis = AxisOf(bridge);
            foreach (var tile in bridge.Span) decks.Add(tile, axis);
        }
        return decks.Count == 0 ? null : decks;
    }

    public static bool SameDecks(IReadOnlyDictionary<GridPoint, BridgeAxis>? left,
        IReadOnlyDictionary<GridPoint, BridgeAxis>? right)
    {
        if (left is null || left.Count == 0) return right is null || right.Count == 0;
        if (right is null || left.Count != right.Count) return false;
        return left.All(item => right.TryGetValue(item.Key, out var axis) && axis == item.Value);
    }

    /// <summary>True when an existing bridge already joins the same connected banks as this crossing.</summary>
    public static bool IsRedundant(SeededMap map, RiverCrossing crossing, IEnumerable<RiverCrossing> existing) =>
        existing.Any(item => item.Id != crossing.Id && SharesBanks(map, crossing, item));

    /// <summary>
    /// Compares the actual connected banks of two crossings, with no fixed
    /// radius. They share banks only when they cross the same river, joined by
    /// its water, and each end of one reaches an opposite end of the other by
    /// walking along that water's shore without crossing it. A tributary mouth
    /// or a separate stream breaks the shore, so a crossing over a different
    /// nearby stream never counts as the same banks.
    /// </summary>
    public static bool SharesBanks(SeededMap map, RiverCrossing first, RiverCrossing second)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!TryFindChannel(map, first.Span, second.Span, out var path)) return false;

        // The joining water, widened by one tile so a two-tile river's other
        // lane and its far shore are included.
        var channel = new HashSet<GridPoint>(first.Span.Concat(second.Span));
        foreach (var tile in path)
        {
            channel.Add(tile);
            foreach (var next in Around(map, tile, Surrounding))
                if (map.IsRiverWater(next)) channel.Add(next);
        }
        var shore = new HashSet<GridPoint>();
        foreach (var tile in channel)
            foreach (var next in Around(map, tile, Surrounding))
                if (map.IsLand(next)) shore.Add(next);

        var fromA = ShoreWalk(map, shore, first.EntranceA);
        var fromB = ShoreWalk(map, shore, first.EntranceB);
        return fromA.Contains(second.EntranceA) && fromB.Contains(second.EntranceB) ||
            fromA.Contains(second.EntranceB) && fromB.Contains(second.EntranceA);
    }

    private static bool TryFindChannel(SeededMap map, IReadOnlyList<GridPoint> from,
        IReadOnlyList<GridPoint> to, out List<GridPoint> path)
    {
        path = [];
        var targets = to.ToHashSet();
        var predecessor = new Dictionary<GridPoint, GridPoint?>();
        var queue = new Queue<GridPoint>();
        foreach (var start in from)
        {
            if (!predecessor.TryAdd(start, null)) continue;
            queue.Enqueue(start);
        }
        while (queue.TryDequeue(out var current))
        {
            if (targets.Contains(current))
            {
                for (GridPoint? step = current; step is { } value; step = predecessor[value])
                    path.Add(value);
                return true;
            }
            if (predecessor.Count >= MaximumBankSearchTiles) return false;
            foreach (var next in Around(map, current, Cardinal))
            {
                if (!map.IsRiverWater(next) || !predecessor.TryAdd(next, current)) continue;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    private static HashSet<GridPoint> ShoreWalk(SeededMap map, HashSet<GridPoint> shore, GridPoint start)
    {
        var reached = new HashSet<GridPoint>();
        if (!shore.Contains(start)) return reached;
        var queue = new Queue<GridPoint>();
        reached.Add(start);
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
            foreach (var next in Around(map, current, Cardinal))
                if (shore.Contains(next) && reached.Add(next))
                    queue.Enqueue(next);
        return reached;
    }

    private static IEnumerable<GridPoint> Around(SeededMap map, GridPoint point, (int X, int Y)[] offsets)
    {
        foreach (var (dx, dy) in offsets)
        {
            var next = map.WrapColumn(new GridPoint(point.X + dx, point.Y + dy));
            if (map.Contains(next)) yield return next;
        }
    }

    internal static bool TryParseId(string? id, out GridPoint first, out BridgeAxis axis, out int spanTiles)
    {
        first = default;
        axis = default;
        spanTiles = 0;
        if (id is null || !id.StartsWith("bridge-", StringComparison.Ordinal) || id.Length > 64) return false;
        var parts = id["bridge-".Length..].Split('-');
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y) ||
            parts[2] is not ("ew" or "ns") ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out spanTiles) ||
            spanTiles is < 1 or > BridgeDesigns.MaximumSpanTiles)
            return false;
        first = new GridPoint(x, y);
        axis = parts[2] == "ew" ? BridgeAxis.EastWest : BridgeAxis.NorthSouth;
        return BridgeId(first, axis, spanTiles) == id;
    }

    /// <summary>
    /// Checks saved bridges against the map: stable IDs, a supported design and
    /// trigger, a legal river span of one or two tiles between buildable banks,
    /// no shared decks, and Road bridges that still meet saved Road tiles at
    /// both ends. It does not require the Town or building that caused the
    /// bridge to still exist: bridges are permanent.
    /// </summary>
    public static void ValidateSaved(IReadOnlyList<BridgeState> bridges, SeededMap map,
        IReadOnlyCollection<GridPoint> roads, long worldTick)
    {
        ArgumentNullException.ThrowIfNull(bridges);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roads);
        var roadSet = roads as IReadOnlySet<GridPoint> ?? roads.ToHashSet();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var decks = new HashSet<GridPoint>();
        foreach (var bridge in bridges)
        {
            if (bridge is null || bridge.Entrances is not { Count: 2 } || bridge.Span is null ||
                !ids.Add(bridge.Id) ||
                !TryResolve(map, bridge.Id, out var crossing, ignoreBuiltDecks: true) ||
                bridge.Design != crossing!.Design ||
                bridge.Entrances[0] != crossing.EntranceA || bridge.Entrances[1] != crossing.EntranceB ||
                !bridge.Span.SequenceEqual(crossing.Span) ||
                bridge.BuiltTick < 0 || bridge.BuiltTick > worldTick ||
                bridge.Trigger is not (BridgeTriggers.Road or BridgeTriggers.Traffic) ||
                bridge.Trigger == BridgeTriggers.Road && (bridge.RouteId is null ||
                    !bridge.RouteId.StartsWith("road:", StringComparison.Ordinal) ||
                    bridge.RouteId.Length > 600 || !roadSet.Contains(bridge.Entrances[0]) ||
                    !roadSet.Contains(bridge.Entrances[1])) ||
                bridge.Trigger == BridgeTriggers.Traffic && bridge.RouteId is not null ||
                bridge.Span.Any(tile => !decks.Add(tile)))
                throw new InvalidDataException("Saved bridges contain an invalid, duplicate or overlapping crossing.");
        }
    }
}
