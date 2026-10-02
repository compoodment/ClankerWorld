using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// One new-street question: join any of <see cref="Starts"/> (a building's
/// entrance tiles) to the <see cref="Network"/>. <see cref="Map"/> must already
/// carry the built bridge decks. Blocked tiles are building footprints,
/// resources and camp objects; a street never passes through them unless they
/// are already part of the network. <see cref="Roads"/> are the existing Road
/// tiles a new street should meet rather than run beside, and a non-null
/// <see cref="Border"/> keeps the street inside the Town border.
/// </summary>
public sealed record RoadRouteRequest(
    SeededMap Map,
    IReadOnlyList<GridPoint> Starts,
    IReadOnlySet<GridPoint> Network,
    IReadOnlySet<GridPoint> Blocked,
    IReadOnlyList<BridgeState> Bridges,
    IReadOnlySet<GridPoint>? Roads = null,
    IReadOnlySet<GridPoint>? Border = null);

/// <summary>
/// A complete, not yet committed street: every land tile it uses in order
/// from an entrance to the network, the new crossings it needs and the
/// existing bridges it reuses. Nothing is saved until the whole proposal
/// validates.
/// </summary>
public sealed record RoadRouteProposal(
    IReadOnlyList<GridPoint> RoadTiles,
    IReadOnlyList<RiverCrossing> NewCrossings,
    IReadOnlyList<string> UsedBridgeIds);

public sealed record RoadRouteResult(RoadRouteProposal? Proposal, string Outcome);

public static class RoadRouteOutcomes
{
    public const string Connected = "connected";
    public const string NoEntrance = "no_entrance";
    public const string RouteUnavailable = "route_unavailable";
    public const string RedundantCrossing = "redundant_crossing";
}

/// <summary>
/// The Town's new-street search, deterministic and bounded. A street steps
/// over buildable ground, diagonally only where both corner tiles are clear,
/// and pays extra for each tile beside an existing Road so it meets streets
/// rather than shadowing them. It may also cross an existing bridge, or a
/// validated new crossing of a river up to two tiles wide, in a straight
/// cardinal line. Ties break by cost, then row, then column, then discovery
/// order.
/// </summary>
public static class RoadRoutePlanner
{
    /// <summary>The most tiles one route search may settle.</summary>
    public const int MaximumSearchTiles = 32_768;

    /// <summary>Extra cost for a new street tile beside an existing Road.</summary>
    public const int BesideRoadCost = 60;

    /// <summary>
    /// A new crossing is costed per water tile like wading a one-tile river,
    /// so an existing bridge is cheaper than building a new one.
    /// </summary>
    public const int ProspectiveWaterTileCost = 200;

    private const int MaximumAttempts = 4;

    private static readonly (int X, int Y)[] Cardinal = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private enum LinkKind { Start, Ground, Bridge, Crossing }

    private readonly record struct Link(GridPoint From, LinkKind Kind, string? BridgeId, RiverCrossing? Crossing);

    public static RoadRouteResult Plan(RoadRouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var starts = request.Starts.Distinct().Where(point => IsRoadGround(request, point))
            .OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
        if (starts.Length == 0) return new RoadRouteResult(null, RoadRouteOutcomes.NoEntrance);
        var existing = request.Bridges.Select(RiverBridgeRules.ToCrossing).ToArray();
        var redundancy = new Dictionary<string, bool>(StringComparer.Ordinal);
        var banned = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var proposal = Search(request, starts, existing, redundancy, banned);
            if (proposal is null) return new RoadRouteResult(null, RoadRouteOutcomes.RouteUnavailable);
            // One street must not build two bridges over the same banks, such
            // as crossing a meander twice. Refuse the later crossing and retry.
            var repeated = proposal.NewCrossings.Select((crossing, index) => (crossing, index))
                .FirstOrDefault(item => RiverBridgeRules.IsRedundant(request.Map, item.crossing,
                    proposal.NewCrossings.Take(item.index))).crossing;
            if (repeated is null) return new RoadRouteResult(proposal, RoadRouteOutcomes.Connected);
            banned.Add(repeated.Id);
        }
        return new RoadRouteResult(null, RoadRouteOutcomes.RedundantCrossing);
    }

    /// <summary>
    /// Validates a whole street proposal against the current map, buildings,
    /// resources and bridges before anything is committed. Returns null when
    /// it may be committed, or a stable reason code.
    /// </summary>
    public static string? Validate(RoadRouteRequest request, RoadRouteProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proposal);
        var map = request.Map;
        var tiles = proposal.RoadTiles;
        if (tiles.Count == 0 || !request.Starts.Contains(tiles[0]) || !request.Network.Contains(tiles[^1]))
            return "route_incomplete";
        if (tiles.Distinct().Count() != tiles.Count) return "route_repeats";
        if (tiles.Any(tile => !IsRoadGround(request, tile) || map.IsBridgeDeck(tile))) return "road_footprint_blocked";

        var bridgesById = request.Bridges.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (proposal.UsedBridgeIds.Any(id => !bridgesById.ContainsKey(id))) return "bridge_missing";
        var links = proposal.UsedBridgeIds.Select(id => bridgesById[id].Entrances)
            .Concat(proposal.NewCrossings.Select(item => item.Entrances)).ToArray();
        for (var index = 1; index < tiles.Count; index++)
        {
            var (from, to) = (tiles[index - 1], tiles[index]);
            if (!IsStreetStep(request, from, to) &&
                !links.Any(pair => pair[0] == from && pair[1] == to || pair[0] == to && pair[1] == from))
                return "route_broken";
        }
        var newTiles = tiles.Where(tile => !request.Network.Contains(tile)).ToArray();
        var roadsAfter = (request.Roads ?? new HashSet<GridPoint>()).Concat(tiles).ToHashSet();
        return ValidateGrowth(map, request.Blocked, roadsAfter, request.Bridges, newTiles, proposal.NewCrossings);
    }

    /// <summary>
    /// The footprint check shared by new streets and streets running on past a
    /// door: every new Road tile is clear buildable ground, and every new
    /// crossing is still a legal one- or two-tile river span with Road at both
    /// ends, shares no water with another, and joins banks no bridge joins.
    /// Returns null when the growth may be committed, or a stable reason code.
    /// </summary>
    public static string? ValidateGrowth(SeededMap map, IReadOnlySet<GridPoint> blocked,
        IReadOnlySet<GridPoint> roadsAfter, IReadOnlyList<BridgeState> bridges,
        IReadOnlyCollection<GridPoint> newTiles, IReadOnlyList<RiverCrossing> newCrossings)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (newTiles.Any(tile => !map.IsBuildable(tile) || map.IsBridgeDeck(tile) || blocked.Contains(tile)))
            return "road_footprint_blocked";
        var existing = bridges.Select(RiverBridgeRules.ToCrossing).ToList();
        var newDecks = new HashSet<GridPoint>();
        foreach (var crossing in newCrossings)
        {
            if (!RiverBridgeRules.TryResolve(map, crossing.Id, out var current) ||
                current!.EntranceA != crossing.EntranceA || current.EntranceB != crossing.EntranceB ||
                !current.Span.SequenceEqual(crossing.Span) || current.Design != crossing.Design ||
                !roadsAfter.Contains(crossing.EntranceA) || !roadsAfter.Contains(crossing.EntranceB) ||
                crossing.Span.Any(tile => !newDecks.Add(tile)))
                return "crossing_invalid";
            if (RiverBridgeRules.IsRedundant(map, crossing, existing)) return "crossing_redundant";
            existing.Add(crossing);
        }
        return null;
    }

    private static RoadRouteProposal? Search(RoadRouteRequest request, GridPoint[] starts,
        RiverCrossing[] existing, Dictionary<string, bool> redundancy, HashSet<string> banned)
    {
        var map = request.Map;
        var roads = request.Roads ?? new HashSet<GridPoint>();
        var byEntrance = request.Bridges.OrderBy(item => item.Id, StringComparer.Ordinal)
            .SelectMany(bridge => bridge.Entrances.Select(entrance => (Entrance: entrance, Bridge: bridge)))
            .ToLookup(item => item.Entrance, item => item.Bridge);
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int>();
        var predecessor = new Dictionary<GridPoint, Link>();
        var order = 0;
        foreach (var start in starts)
        {
            best[start] = 0;
            predecessor[start] = new Link(start, LinkKind.Start, null, null);
            open.Enqueue(start, (0, start.Y, start.X, order++));
        }

        while (open.TryDequeue(out var current, out var priority) && best.Count <= MaximumSearchTiles)
        {
            if (priority.Cost != best[current]) continue;
            if (request.Network.Contains(current)) return Reconstruct(current, predecessor);

            foreach (var next in map.FootNeighbors(current))
            {
                if (!Landing(next, out var joins) || !IsStreetStep(request, current, next)) continue;
                Relax(next, checked(priority.Cost + map.FootStepCost(current, next) + Beside(next, joins)),
                    new Link(current, LinkKind.Ground, null, null));
            }

            foreach (var (dx, dy) in Cardinal)
            {
                var next = map.WrapColumn(new GridPoint(current.X + dx, current.Y + dy));
                if (!map.IsRiverWater(next) || map.IsBridgeDeck(next) ||
                    !RiverBridgeRules.TryFindCrossing(map, current, dx, dy, out var crossing) ||
                    banned.Contains(crossing!.Id))
                    continue;
                var far = crossing.EntranceA == current ? crossing.EntranceB : crossing.EntranceA;
                if (!Landing(far, out var joins) || IsRedundant(crossing)) continue;
                Relax(far, checked(priority.Cost + crossing.Span.Count * ProspectiveWaterTileCost +
                    map.FootTravelCost(far) + Beside(far, joins)), new Link(current, LinkKind.Crossing, null, crossing));
            }

            foreach (var bridge in byEntrance[current])
            {
                var far = bridge.Entrances[0] == current ? bridge.Entrances[1] : bridge.Entrances[0];
                if (!Landing(far, out var joins)) continue;
                var walk = (bridge.Entrances[0] == current ? bridge.Span : bridge.Span.Reverse())
                    .Append(far).Prepend(current).ToArray();
                var cost = priority.Cost;
                for (var index = 1; index < walk.Length; index++)
                    cost = checked(cost + map.FootStepCost(walk[index - 1], walk[index]));
                Relax(far, checked(cost + Beside(far, joins)), new Link(current, LinkKind.Bridge, bridge.Id, null));
            }
        }
        return null;

        // Where a street may put its next tile: buildable, unblocked ground
        // inside the border, unless it joins the network there.
        bool Landing(GridPoint tile, out bool joins)
        {
            joins = request.Network.Contains(tile);
            return map.IsBuildable(tile) && (!request.Blocked.Contains(tile) || joins) &&
                (request.Border is null || joins || request.Border.Contains(tile));
        }

        int Beside(GridPoint tile, bool joins) => !joins && TownStreets.Directions.Any(step =>
            roads.Contains(new GridPoint(tile.X + step.X, tile.Y + step.Y))) ? BesideRoadCost : 0;

        void Relax(GridPoint next, int cost, Link link)
        {
            if (best.TryGetValue(next, out var previous) && previous <= cost) return;
            best[next] = cost;
            predecessor[next] = link;
            open.Enqueue(next, (cost, next.Y, next.X, order++));
        }

        bool IsRedundant(RiverCrossing crossing)
        {
            if (!redundancy.TryGetValue(crossing.Id, out var redundant))
                redundancy[crossing.Id] = redundant = RiverBridgeRules.IsRedundant(map, crossing, existing);
            return redundant;
        }
    }

    /// <summary>
    /// A legal step between two street tiles: one foot step, and a diagonal
    /// only where both corner tiles are clear ground (not across the seam).
    /// </summary>
    private static bool IsStreetStep(RoadRouteRequest request, GridPoint from, GridPoint to)
    {
        var map = request.Map;
        if (map.FootDistance(from, to) != 1 || !map.CanFootStep(from, to)) return false;
        if (!map.IsDiagonalFootStep(from, to)) return true;
        return Math.Abs(to.X - from.X) == 1 &&
            IsClearCorner(new GridPoint(to.X, from.Y)) && IsClearCorner(new GridPoint(from.X, to.Y));

        bool IsClearCorner(GridPoint tile) => map.IsBuildable(tile) && !request.Blocked.Contains(tile);
    }

    private static RoadRouteProposal Reconstruct(GridPoint end, Dictionary<GridPoint, Link> predecessor)
    {
        var tiles = new List<GridPoint>();
        var crossings = new List<RiverCrossing>();
        var bridges = new List<string>();
        var current = end;
        while (true)
        {
            tiles.Add(current);
            var link = predecessor[current];
            if (link.Kind == LinkKind.Start) break;
            if (link.Crossing is { } crossing) crossings.Add(crossing);
            if (link.BridgeId is { } bridgeId) bridges.Add(bridgeId);
            current = link.From;
        }
        tiles.Reverse();
        crossings.Reverse();
        bridges.Reverse();
        return new RoadRouteProposal(tiles, crossings, bridges);
    }

    private static bool IsRoadGround(RoadRouteRequest request, GridPoint point) =>
        request.Map.Contains(point) && request.Map.IsBuildable(point) &&
        (!request.Blocked.Contains(point) || request.Network.Contains(point));
}
