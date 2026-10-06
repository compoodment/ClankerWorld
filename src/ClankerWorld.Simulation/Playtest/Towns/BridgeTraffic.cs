using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>One completed wade from one bank of a one- or two-tile crossing to the other.</summary>
public sealed record BridgeTrafficCrossing(string CrossingId, string AgentId, long Tick);

/// <summary>
/// An agent standing in the river partway across a one- or two-tile crossing.
/// It stays open while the agent steps between that crossing's river tiles,
/// and becomes evidence only if a step out of the water reaches the opposite bank.
/// </summary>
public sealed record BridgeTrafficWade(string AgentId, string CrossingId, GridPoint EntryBank);

/// <summary>Saved, bounded evidence for the traffic bridge trigger.</summary>
public sealed record BridgeTrafficState(
    IReadOnlyList<BridgeTrafficWade> InProgress,
    IReadOnlyList<BridgeTrafficCrossing> Completed)
{
    public static BridgeTrafficState Empty { get; } = new([], []);

    [JsonIgnore]
    public bool IsEmpty => InProgress.Count == 0 && Completed.Count == 0;
}

/// <summary>
/// The traffic trigger, kept separate from generated Roads. Only real,
/// committed steps count: route previews, blocked moves, waiting and turning
/// back never add evidence, and walking on a Road or an existing bridge is not
/// wading. A bridge appears after <see cref="CrossingThreshold"/> completed
/// crossings by at least <see cref="DistinctAgentThreshold"/> agents within
/// <see cref="WindowDays"/> world days at the same crossing of one or two
/// river tiles (the owner's initial playtest threshold, which applies to
/// both widths).
/// </summary>
public static class BridgeTrafficRules
{
    public const int CrossingThreshold = 6;
    public const int DistinctAgentThreshold = 2;
    public const int WindowDays = 2;

    /// <summary>
    /// Evidence kept per agent per crossing. Keeping one fewer than the
    /// threshold per agent decides the rule exactly while bounding the save.
    /// </summary>
    public const int MaximumCrossingsPerAgent = CrossingThreshold - 1;

    public static long WindowTicks(int ticksPerDay) => checked((long)WindowDays * ticksPerDay);

    /// <summary>Records one committed foot step. Every other kind of movement leaves the evidence unchanged.</summary>
    public static BridgeTrafficState RecordStep(BridgeTrafficState state, SeededMap map, string agentId,
        GridPoint from, GridPoint to, long tick)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        // Most steps are on land by someone who is not wading: nothing to record.
        if (!map.IsRiverWater(to) && state.InProgress.All(item => item.AgentId != agentId)) return state;
        var inProgress = state.InProgress.Where(item => item.AgentId != agentId).ToList();
        var completed = state.Completed.ToList();
        if (state.InProgress.FirstOrDefault(item => item.AgentId == agentId) is { } wade &&
            RiverBridgeRules.TryResolve(map, wade.CrossingId, out var waded) && waded!.Span.Contains(from))
        {
            if (to == Opposite(waded, wade.EntryBank))
            {
                completed.Add(new BridgeTrafficCrossing(wade.CrossingId, agentId, tick));
                var excess = completed.Where(item => item.CrossingId == wade.CrossingId && item.AgentId == agentId)
                    .OrderByDescending(item => item.Tick).Skip(MaximumCrossingsPerAgent).ToHashSet();
                completed.RemoveAll(excess.Contains);
            }
            // A step to the other river tile of a two-tile crossing is still
            // the same wade; any other step ends it.
            else if (waded.Span.Contains(to))
                inProgress.Add(wade);
        }
        if (map.IsRiverWater(to) && !map.IsBridgeDeck(to) && !map.IsRiverWater(from) &&
            CardinalStep(map, from, to) is { } step &&
            RiverBridgeRules.TryFindCrossing(map, from, step.X, step.Y, out var entered))
            inProgress.Add(new BridgeTrafficWade(agentId, entered!.Id, from));
        return Canonical(inProgress, completed);
    }

    /// <summary>
    /// Drops expired crossings and abandoned wades: an agent that died, moved
    /// away by other means or whose crossing gained a bridge is no longer wading.
    /// </summary>
    public static BridgeTrafficState Prune(BridgeTrafficState state, SeededMap map, long tick, int ticksPerDay,
        IReadOnlyDictionary<string, GridPoint> activePositions)
    {
        ArgumentNullException.ThrowIfNull(state);
        var window = WindowTicks(ticksPerDay);
        var inProgress = state.InProgress.Where(item =>
            activePositions.TryGetValue(item.AgentId, out var position) &&
            RiverBridgeRules.TryResolve(map, item.CrossingId, out var crossing) && crossing!.Span.Contains(position)).ToList();
        var completed = state.Completed.Where(item => tick - item.Tick < window &&
            RiverBridgeRules.TryResolve(map, item.CrossingId, out _)).ToList();
        return inProgress.Count == state.InProgress.Count && completed.Count == state.Completed.Count
            ? state
            : Canonical(inProgress, completed);
    }

    /// <summary>Crossings that currently meet the threshold, in a stable order.</summary>
    public static IReadOnlyList<string> ReadyCrossings(BridgeTrafficState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Completed.GroupBy(item => item.CrossingId, StringComparer.Ordinal)
            .Where(group => group.Count() >= CrossingThreshold &&
                group.Select(item => item.AgentId).Distinct(StringComparer.Ordinal).Count() >= DistinctAgentThreshold)
            .Select(group => group.Key).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Removes all evidence for one crossing, such as after a bridge is built there.</summary>
    public static BridgeTrafficState Forget(BridgeTrafficState state, string crossingId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.InProgress.All(item => item.CrossingId != crossingId) &&
            state.Completed.All(item => item.CrossingId != crossingId))
            return state;
        return Canonical(state.InProgress.Where(item => item.CrossingId != crossingId).ToList(),
            state.Completed.Where(item => item.CrossingId != crossingId).ToList());
    }

    /// <summary>Checks untrusted saved evidence: bounded, recent, and only for real unbridged crossings.</summary>
    public static void Validate(BridgeTrafficState state, SeededMap map, long tick, int ticksPerDay,
        IReadOnlySet<string> knownAgents, IReadOnlyDictionary<string, GridPoint> activePositions,
        IEnumerable<BridgeState> bridges)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (state?.InProgress is null || state.Completed is null)
            throw new InvalidDataException("Saved bridge traffic evidence is missing.");
        var bridged = bridges.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var window = WindowTicks(ticksPerDay);
        bool IsOpenCrossing(string id, out RiverCrossing? crossing) =>
            RiverBridgeRules.TryResolve(map, id, out crossing, ignoreBuiltDecks: true) && !bridged.Contains(id);
        var wading = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wade in state.InProgress)
        {
            if (wade is null || !wading.Add(wade.AgentId) ||
                !activePositions.TryGetValue(wade.AgentId, out var position) ||
                !IsOpenCrossing(wade.CrossingId, out var crossing) ||
                !crossing!.Span.Contains(position) ||
                wade.EntryBank != crossing.EntranceA && wade.EntryBank != crossing.EntranceB)
                throw new InvalidDataException("Saved bridge traffic contains an invalid crossing in progress.");
        }
        var seen = new HashSet<(string, string, long)>();
        foreach (var crossing in state.Completed)
        {
            if (crossing is null || !knownAgents.Contains(crossing.AgentId) ||
                crossing.Tick < 0 || crossing.Tick > tick || tick - crossing.Tick >= window ||
                !IsOpenCrossing(crossing.CrossingId, out _) ||
                !seen.Add((crossing.CrossingId, crossing.AgentId, crossing.Tick)))
                throw new InvalidDataException("Saved bridge traffic contains an invalid or expired crossing.");
        }
        if (state.Completed.GroupBy(item => (item.CrossingId, item.AgentId))
            .Any(group => group.Count() > MaximumCrossingsPerAgent))
            throw new InvalidDataException("Saved bridge traffic keeps more crossings than its bound allows.");
    }

    private static GridPoint Opposite(RiverCrossing crossing, GridPoint entry) =>
        entry == crossing.EntranceA ? crossing.EntranceB : crossing.EntranceA;

    private static (int X, int Y)? CardinalStep(SeededMap map, GridPoint from, GridPoint to)
    {
        var dx = to.X - from.X;
        if (map.WrapsEastWest && Math.Abs(dx) == map.Width - 1) dx = -Math.Sign(dx);
        var dy = to.Y - from.Y;
        return Math.Abs(dx) + Math.Abs(dy) == 1 ? (dx, dy) : null;
    }

    private static BridgeTrafficState Canonical(List<BridgeTrafficWade> inProgress, List<BridgeTrafficCrossing> completed) =>
        new(inProgress.OrderBy(item => item.AgentId, StringComparer.Ordinal).ToArray(),
            completed.OrderBy(item => item.CrossingId, StringComparer.Ordinal).ThenBy(item => item.Tick)
                .ThenBy(item => item.AgentId, StringComparer.Ordinal).ToArray());
}
