using System.Buffers;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Dijkstra's search over legal traveler steps from one origin, for routes that
/// avoid occupied tiles, run only as far as the questions asked of it need.
/// Tiles leave the queue in one fixed order (cost, then row, column and the
/// order they were queued) whatever is asked, and a tile's route is final once
/// it leaves. So the first tile within range of a destination, and the route to
/// it, are exactly what a fresh search for that destination alone would find,
/// and many questions from one origin cost at most one search of the map.
/// </summary>
public sealed class UnoccupiedRouteSearch : IDisposable
{
    private readonly SeededMap map;
    private readonly HashSet<GridPoint> occupied;
    private readonly Func<GridPoint, GridPoint, int> stepCost;
    private readonly Func<GridPoint, IEnumerable<GridPoint>> neighbors;
    private readonly int[] best;
    private readonly int[] predecessor;
    private readonly int[] finishedOrder;
    private readonly PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)> open = new();
    private int order;
    private int finished;
    private bool disposed;

    /// <param name="map">The map whose legal steps the search follows.</param>
    /// <param name="origin">Where every route starts, even if it is occupied.</param>
    /// <param name="occupied">Tiles a route may not enter, nor cut past diagonally. The search keeps the set, so it must not change.</param>
    /// <param name="stepCost">The cost of one foot step; it must stay the same while the search is used.</param>
    /// <param name="neighbors">Legal steps for this traveler; defaults to ordinary foot movement. It must stay the same while the search is used.</param>
    public UnoccupiedRouteSearch(SeededMap map, GridPoint origin, HashSet<GridPoint> occupied,
        Func<GridPoint, GridPoint, int> stepCost, Func<GridPoint, IEnumerable<GridPoint>>? neighbors = null)
    {
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        this.occupied = occupied ?? throw new ArgumentNullException(nameof(occupied));
        this.stepCost = stepCost ?? throw new ArgumentNullException(nameof(stepCost));
        this.neighbors = neighbors ?? map.FootNeighbors;
        if (!map.Contains(origin))
            throw new ArgumentOutOfRangeException(nameof(origin));
        Origin = origin;
        var tileCount = checked(map.Width * map.Height);
        best = ArrayPool<int>.Shared.Rent(tileCount);
        predecessor = ArrayPool<int>.Shared.Rent(tileCount);
        finishedOrder = ArrayPool<int>.Shared.Rent(tileCount);
        Array.Fill(best, int.MaxValue, 0, tileCount);
        Array.Fill(finishedOrder, -1, 0, tileCount);
        best[Index(origin)] = 0;
        open.Enqueue(origin, (0, origin.Y, origin.X, order++));
    }

    public GridPoint Origin { get; }

    public SeededMap Map => map;

    public IReadOnlySet<GridPoint> Occupied => occupied;

    /// <summary>
    /// The cheapest route from the origin to the first tile within
    /// <paramref name="interactionRange"/> of <paramref name="destination"/>,
    /// both ends included, or an empty route when no free route reaches it.
    /// </summary>
    public List<GridPoint> RouteTo(GridPoint destination, int interactionRange)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var goal = FirstFinishedWithin(destination, interactionRange);
        while (goal < 0 && open.TryDequeue(out var current, out var priority))
        {
            var index = Index(current);
            if (priority.Cost != best[index])
                continue;
            finishedOrder[index] = finished++;
            Expand(current, index, priority.Cost);
            if (map.FootDistance(current, destination) <= interactionRange)
                goal = index;
        }
        if (goal < 0)
            return [];

        var route = new List<GridPoint>();
        var start = Index(Origin);
        for (var step = goal; ; step = predecessor[step])
        {
            route.Add(new GridPoint(step % map.Width, step / map.Width));
            if (step == start) break;
        }
        route.Reverse();
        return route;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ArrayPool<int>.Shared.Return(best);
        ArrayPool<int>.Shared.Return(predecessor);
        ArrayPool<int>.Shared.Return(finishedOrder);
    }

    private void Expand(GridPoint current, int index, int cost)
    {
        foreach (var next in neighbors(current))
        {
            if (occupied.Contains(next) ||
                map.IsDiagonalFootStep(current, next) &&
                (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                 occupied.Contains(new GridPoint(current.X, next.Y))))
            {
                continue;
            }

            var nextCost = checked(cost + stepCost(current, next));
            var nextIndex = Index(next);
            if (best[nextIndex] <= nextCost)
                continue;
            best[nextIndex] = nextCost;
            predecessor[nextIndex] = index;
            open.Enqueue(next, (nextCost, next.Y, next.X, order++));
        }
    }

    // The tile within range that left the queue first, if any has left it yet.
    // Only rows within the range can hold one; FootDistance decides the columns,
    // including across an east/west wrap.
    private int FirstFinishedWithin(GridPoint destination, int interactionRange)
    {
        var first = -1;
        if (interactionRange < 0) return first;
        var top = Math.Max(0L, (long)destination.Y - interactionRange);
        var bottom = Math.Min(map.Height - 1L, (long)destination.Y + interactionRange);
        for (var y = (int)top; y <= bottom; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var index = y * map.Width + x;
                if (finishedOrder[index] < 0 || first >= 0 && finishedOrder[index] >= finishedOrder[first] ||
                    map.FootDistance(new GridPoint(x, y), destination) > interactionRange)
                    continue;
                first = index;
            }
        }
        return first;
    }

    private int Index(GridPoint point) => point.Y * map.Width + point.X;
}
