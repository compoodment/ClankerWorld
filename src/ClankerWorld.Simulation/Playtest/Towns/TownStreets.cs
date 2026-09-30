using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Street geometry shared by Town layout and growth. Streets wind in 45° steps
/// around blocked ground, take a diagonal step only where both corner tiles are
/// clear, keep a one-tile gap from other streets instead of running beside
/// them, and run on a few tiles past the last door on them.
/// </summary>
public sealed class TownStreets
{
    /// <summary>How far a street runs on past the last door on it.</summary>
    public const int RunOnTiles = 3;

    /// <summary>East, then clockwise in 45° steps; opposite directions are four apart.</summary>
    public static readonly (int X, int Y)[] Directions =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    private readonly SeededMap map;
    private readonly HashSet<GridPoint> blocked;
    private readonly List<GridPoint> order = [];
    private readonly HashSet<GridPoint> roads = [];

    /// <param name="blocked">Tiles a street may not use: trees, resources, camp objects and footprints.</param>
    public TownStreets(SeededMap map, IEnumerable<GridPoint> blocked, IEnumerable<GridPoint>? existing = null)
    {
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        this.blocked = (blocked ?? throw new ArgumentNullException(nameof(blocked))).ToHashSet();
        foreach (var tile in existing ?? []) Add(tile);
    }

    /// <summary>Road tiles in the order they were laid.</summary>
    public IReadOnlyList<GridPoint> Tiles => order;

    public bool Contains(GridPoint tile) => roads.Contains(tile);

    public void Add(GridPoint tile)
    {
        if (roads.Add(tile)) order.Add(tile);
    }

    public void Block(IEnumerable<GridPoint> tiles) => blocked.UnionWith(tiles);

    /// <summary>Clear, buildable ground that a street could use.</summary>
    public bool IsFree(GridPoint tile) => map.IsBuildable(tile) && !blocked.Contains(tile) && !roads.Contains(tile);

    /// <summary>
    /// Whether a street can step from one tile in a direction: the next tile
    /// is free, and a diagonal step has both corner tiles clear.
    /// </summary>
    public bool CanStep(GridPoint from, int direction, out GridPoint next)
    {
        var (dx, dy) = Directions[direction];
        next = new GridPoint(from.X + dx, from.Y + dy);
        if (!IsFree(next)) return false;
        return dx == 0 || dy == 0 ||
            IsClearCorner(new GridPoint(from.X + dx, from.Y)) && IsClearCorner(new GridPoint(from.X, from.Y + dy));
    }

    private bool IsClearCorner(GridPoint tile) => map.IsBuildable(tile) && !blocked.Contains(tile);

    /// <summary>How many free tiles lie straight ahead, up to a limit.</summary>
    public int OpenRun(GridPoint from, int direction, int limit = 20)
    {
        var count = 0;
        while (count < limit && CanStep(from, direction, out var next))
        {
            from = next;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Lays a street from a tile already on the network. It keeps its heading,
    /// sometimes bends 45° after running straight for a while, and bends
    /// around anything in its way. It stops when it can go no further or would
    /// run beside another street. It never turns more than 45° from the
    /// heading it set out on, so it cannot double back. A street shorter than
    /// <paramref name="minimum"/> tiles is taken up again.
    /// </summary>
    public List<GridPoint> Wander(GridPoint start, int direction, int length, double bendChance, Pcg32XshRrV1 random,
        int minimum = 1)
    {
        ArgumentNullException.ThrowIfNull(random);
        var heading = direction;
        var path = new List<GridPoint>();
        var current = start;
        var straight = 0;
        for (var step = 0; step < length; step++)
        {
            var choices = new List<int> { direction };
            if (straight >= 3 && Chance(random, bendChance))
                choices.Insert(0, Turn(direction, Chance(random, 0.5) ? 1 : -1));
            choices.Add(Turn(direction, 1));
            choices.Add(Turn(direction, -1));
            var moved = false;
            foreach (var choice in choices)
            {
                if (Math.Abs(Turn(choice - heading, 4) - 4) > 1 ||
                    !CanStep(current, choice, out var next) || RunsBeside(next, current, start, path)) continue;
                straight = choice == direction ? straight + 1 : 0;
                direction = choice;
                current = next;
                path.Add(next);
                Add(next);
                moved = true;
                break;
            }
            if (!moved) break;
        }
        if (path.Count >= minimum) return path;
        roads.ExceptWith(path);
        order.RemoveAll(path.Contains);
        return [];
    }

    /// <summary>
    /// A new street tile may touch only the tile it comes from, its own last
    /// two tiles and, while it leaves its parent street, the tiles around the
    /// branch point. Anything else would make two Roads run side by side.
    /// </summary>
    private bool RunsBeside(GridPoint next, GridPoint current, GridPoint start, List<GridPoint> path)
    {
        foreach (var (dx, dy) in Directions)
        {
            var near = new GridPoint(next.X + dx, next.Y + dy);
            if (!roads.Contains(near) || near == current) continue;
            if (path.Count >= 2 && (near == path[^1] || near == path[^2])) continue;
            if (path.Count < 2 && Math.Max(Math.Abs(near.X - start.X), Math.Abs(near.Y - start.Y)) <= 1) continue;
            return true;
        }
        return false;
    }

    public static int Turn(int direction, int eighths) => ((direction + eighths) % 8 + 8) % 8;

    public static int DirectionBetween(GridPoint from, GridPoint to) =>
        Array.IndexOf(Directions, (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y)));

    public static bool Chance(Pcg32XshRrV1 random, double probability) =>
        random.NextUInt() / (double)uint.MaxValue < probability;

    public static int Between(Pcg32XshRrV1 random, int minimum, int maximum) =>
        minimum + (int)(random.NextUInt() % (uint)(maximum - minimum + 1));

    /// <summary>
    /// Road tiles joined to a tile: orthogonal neighbours, and diagonal
    /// neighbours where neither corner tile between them is Road (otherwise the
    /// two are already joined through that corner).
    /// </summary>
    public static IEnumerable<GridPoint> Linked(IReadOnlySet<GridPoint> roads, GridPoint tile)
    {
        foreach (var (dx, dy) in Directions)
        {
            var next = new GridPoint(tile.X + dx, tile.Y + dy);
            if (!roads.Contains(next)) continue;
            if (dx != 0 && dy != 0 &&
                (roads.Contains(new GridPoint(tile.X + dx, tile.Y)) || roads.Contains(new GridPoint(tile.X, tile.Y + dy))))
                continue;
            yield return next;
        }
    }

    /// <summary>
    /// Where a building's anchor goes so that its door, on the given side and
    /// footprint tile along it, opens onto <paramref name="entrance"/>.
    /// </summary>
    public static GridPoint AnchorFacing(GridPoint entrance, int side, int along, int width, int height) => side switch
    {
        0 => new GridPoint(entrance.X - along, entrance.Y - height), // door on the south side
        1 => new GridPoint(entrance.X - along, entrance.Y + 1), // north
        2 => new GridPoint(entrance.X - width, entrance.Y - along), // east
        _ => new GridPoint(entrance.X + 1, entrance.Y - along), // west
    };

    /// <summary>
    /// Cuts back dead ends so each street runs on at most
    /// <see cref="RunOnTiles"/> tiles past the last door on it. A branch with
    /// no door on it at all is removed back to the street it leaves.
    /// </summary>
    public static HashSet<GridPoint> TrimToDoors(IEnumerable<GridPoint> tiles, IReadOnlySet<GridPoint> entrances)
    {
        var roads = tiles.ToHashSet();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var end in roads.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray())
            {
                if (entrances.Contains(end) || Linked(roads, end).Count() != 1) continue;
                var chain = new List<GridPoint> { end };
                GridPoint? previous = null;
                var current = end;
                var reachedDoor = false;
                while (true)
                {
                    var onward = Linked(roads, current).Where(next => next != previous).ToArray();
                    if (onward.Length != 1) break;
                    previous = current;
                    current = onward[0];
                    if (entrances.Contains(current))
                    {
                        reachedDoor = true;
                        break;
                    }
                    if (Linked(roads, current).Count() > 2) break;
                    chain.Add(current);
                }
                var keep = reachedDoor ? RunOnTiles : 0;
                if (chain.Count <= keep) continue;
                roads.ExceptWith(chain.Take(chain.Count - keep));
                changed = true;
                break;
            }
        }
        return roads;
    }
}
