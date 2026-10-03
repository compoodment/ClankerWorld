using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.World;

/// <summary>
/// Shapes mountains as a few large massifs (issue #683). Each massif has a
/// centre, a long axis and a size, and rises as a stretched, gently bent dome
/// so it reads as a range. Peaks form along its crest, inside a walkable
/// mountain rim. Leftover patches are flattened, foothills rise around each
/// massif out to its hill reach, and peaks never wall off any land.
/// </summary>
/// <remarks>
/// The shape values here are provisional, like the massif counts, minimum size
/// and hill reach in <see cref="TerrainPlacementRules"/>. They change once the
/// owner has reviewed generated maps.
/// </remarks>
internal static class MountainMassifs
{
    // How irregular a massif's outline and crest are, as a share of its size.
    private const double EdgeRoughness = 0.25;
    private const double CrestRoughness = 0.35;
    private const float EdgeNoiseFrequency = 0.04f;
    private const float CrestNoiseFrequency = 0.12f;

    // The crest runs along this share of the long axis and across this share
    // of the short axis. Peaks stay inside this share of the outline, leaving
    // a mountain rim to walk around them.
    private const double CrestLength = 0.6;
    private const double CrestWidth = 0.18;
    private const double CrestLimit = 0.8;

    // A massif is this many times longer than it is wide, and its axis bends
    // by up to this share of its length at the ends.
    private const int MinimumAspectPercent = 240;
    private const int MaximumAspectPercent = 360;
    private const int MaximumBendPerMille = 150;

    // Placement tries this many centres and directions, needs this share of
    // the planned outline on free land, and keeps massifs this many tiles
    // apart so they never merge into one region.
    private const int PlacementAttempts = 32;
    private const int CentreSamples = 3;
    private const double MinimumLandShare = 0.6;
    private const int SeparationTiles = 3;

    // Foothills keep falling for this many tiles beyond the hill band.
    private const int FoothillTaperTiles = 2;

    // The furthest a roughened outline can reach, in plain-ellipse units.
    private const double OutlineLimit = 1 / (1 - EdgeRoughness);

    private const byte Mountain = SeededMap.MountainElevationThreshold;
    private const byte Peak = SeededMap.PeakElevationThreshold;

    private sealed record Massif(int X, int Y, double AxisX, double AxisY, double Long, double Short, double Bend);

    private readonly record struct Cell(int Index, int X, int Y, double Along, double Across);

    /// <summary>
    /// Replaces every mountain on dry land with a few planned massifs and
    /// raises their foothills. Water is unchanged.
    /// </summary>
    internal static void Raise(GeographyOptions options, string seed, byte[] elevation, byte[] water,
        int width, int height)
    {
        var wrap = options.WrapEastWest;
        var length = elevation.Length;
        var baseElevation = (byte[])elevation.Clone();
        var land = new List<int>();
        for (var index = 0; index < length; index++)
            if (water[index] == (byte)WaterKind.Land) land.Add(index);
        if (land.Count == 0) return;

        var random = Pcg32XshRrV1.Create(seed, "mountain-massifs/v1");
        var (minimumCount, maximumCount) = TerrainPlacementRules.MassifCount(options.Size);
        var count = minimumCount + (int)(random.NextUInt() % (uint)(maximumCount - minimumCount + 1));
        var (minimumShare, maximumShare) = TerrainPlacementRules.MountainSharePerMille(options.MountainRelief);
        var share = minimumShare + (int)(random.NextUInt() % (uint)(maximumShare - minimumShare + 1));
        var targetTiles = (long)land.Count * share / 1000;
        var weights = new int[count];
        for (var massif = 0; massif < count; massif++) weights[massif] = 65 + (int)(random.NextUInt() % 71);
        var weightTotal = weights.Sum();
        // Larger massifs are placed first, while the most land is free.
        var areas = weights
            .Select(weight => Math.Max(targetTiles * weight / weightTotal, 2L * TerrainPlacementRules.MinimumMassifTiles))
            .OrderByDescending(area => area).ToArray();

        var inland = DistanceFromWater(water, width, height, wrap);
        var reserved = new bool[length];
        var cells = new List<Cell>();
        var massifs = new List<Massif>();
        foreach (var plannedArea in areas)
        {
            var aspect = (MinimumAspectPercent +
                (int)(random.NextUInt() % (uint)(MaximumAspectPercent - MinimumAspectPercent + 1))) / 100.0;
            var bend = ((int)(random.NextUInt() % (2 * MaximumBendPerMille + 1)) - MaximumBendPerMille) / 1000.0;
            Massif? best = null;
            var bestScore = MinimumLandShare;
            // Where the land is too broken up for the planned size, a smaller
            // massif is tried before giving up on this one.
            for (var area = (double)plannedArea; best is null && area >= TerrainPlacementRules.MinimumMassifTiles; area /= 2)
            {
                var shortAxis = Math.Sqrt(area / (Math.PI * aspect));
                var longAxis = aspect * shortAxis;
                for (var attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    // The most inland of a few random land tiles, so ranges
                    // sit inside continents more often than on their coasts.
                    var centre = land[(int)(random.NextUInt() % (uint)land.Count)];
                    for (var sample = 1; sample < CentreSamples; sample++)
                    {
                        var other = land[(int)(random.NextUInt() % (uint)land.Count)];
                        if (inland[other] > inland[centre]) centre = other;
                    }
                    var (axisX, axisY) = RandomDirection(random);
                    var candidate = new Massif(centre % width, centre / width, axisX, axisY, longAxis, shortAxis, bend);
                    var score = LandShare(candidate);
                    if (score <= bestScore) continue;
                    best = candidate;
                    bestScore = score;
                }
            }
            if (best is null) continue;
            // Grow the chosen massif to make up for the water inside its
            // outline, as long as it still keeps clear of the others.
            var grown = best with
            {
                Long = best.Long / Math.Sqrt(bestScore),
                Short = best.Short / Math.Sqrt(bestScore),
            };
            if (LandShare(grown) > 0) best = grown;
            Collect(best, SeparationTiles, OutlineLimit, width, height, wrap, cells);
            foreach (var cell in cells) reserved[cell.Index] = true;
            massifs.Add(best);
        }

        // The share of a massif's plain outline on dry land, or zero when its
        // surroundings would touch a massif already placed.
        double LandShare(Massif candidate)
        {
            Collect(candidate, SeparationTiles, OutlineLimit, width, height, wrap, cells);
            if (cells.Any(cell => reserved[cell.Index])) return 0;
            Collect(candidate, 0, 1, width, height, wrap, cells);
            return cells.Count(cell => water[cell.Index] == (byte)WaterKind.Land) /
                (Math.PI * candidate.Long * candidate.Short);
        }

        var owner = new byte[length];
        var edgeNoise = GeographyGenerator.LayerNoise(seed, "massif-edge", EdgeNoiseFrequency, width, wrap);
        var crestNoise = GeographyGenerator.LayerNoise(seed, "massif-crest", CrestNoiseFrequency, width, wrap);
        for (var id = 1; id <= massifs.Count; id++)
        {
            var massif = massifs[id - 1];
            Collect(massif, 0, OutlineLimit, width, height, wrap, cells);
            foreach (var cell in cells)
            {
                if (water[cell.Index] != (byte)WaterKind.Land) continue;
                var outline = Distance(massif, cell, 0) * (1 + EdgeRoughness * edgeNoise(cell.X, cell.Y));
                if (outline > 1) continue;
                var crest = Math.Sqrt(Square(cell.Along / (CrestLength * massif.Long)) +
                    Square(cell.Across / (CrestWidth * massif.Short))) *
                    (1 + CrestRoughness * crestNoise(cell.X, cell.Y));
                elevation[cell.Index] = crest <= 1 && outline <= CrestLimit
                    ? (byte)Math.Min(255, Peak + (int)(10 * (1 - crest)))
                    : (byte)(Mountain + Math.Min(Peak - Mountain - 1, (int)((Peak - Mountain - 1) * (1 - outline) / 0.6)));
                owner[cell.Index] = (byte)id;
            }
        }

        var sizes = KeepOneWholePiecePerMassif(owner, massifs.Count, elevation, baseElevation, width, height, wrap);
        // A peak keeps only if its own massif surrounds it on all eight
        // sides, so peaks never meet water, lowland or the map's edge.
        for (var index = 0; index < length; index++)
        {
            if (owner[index] == 0 || elevation[index] < Peak) continue;
            var x = index % width;
            var y = index / width;
            var surrounded = y > 0 && y < height - 1 && (wrap || x > 0 && x < width - 1);
            foreach (var near in EightNeighbours(index, width, height, wrap))
                surrounded &= owner[near] == owner[index];
            if (!surrounded) elevation[index] = Peak - 1;
        }
        RaiseFoothills(owner, sizes, elevation, water, width, height, wrap);
    }

    /// <summary>
    /// Lowers just enough peaks that no land is split by a peak wall: every
    /// piece of land joined through peaks is also joined around or through
    /// them on walkable ground. Rivers are not land, so they are never blamed
    /// on peaks.
    /// </summary>
    internal static void OpenPeakPasses(byte[] elevation, byte[] water, int width, int height, bool wrap)
    {
        var length = elevation.Length;
        var open = new int[length];
        var landPiece = new int[length];
        var queue = new int[length];
        var neighbours = new int[4];
        while (true)
        {
            var openSizes = Label(open, index => IsLand(index) && elevation[index] < Peak);
            var landPieces = Label(landPiece, IsLand).Count;
            // The largest walkable piece of each land piece, and the first
            // other walkable piece found inside the same land.
            var main = new int[landPieces];
            Array.Fill(main, -1);
            for (var index = 0; index < length; index++)
                if (open[index] >= 0 && (main[landPiece[index]] < 0 ||
                        openSizes[open[index]] > openSizes[main[landPiece[index]]]))
                    main[landPiece[index]] = open[index];
            var target = -1;
            for (var index = 0; index < length && target < 0; index++)
                if (open[index] >= 0 && open[index] != main[landPiece[index]])
                    target = index;
            if (target < 0) return;

            // A 0-1 search from the main piece through its land finds the
            // route to the cut-off piece that crosses the fewest peaks.
            var mainPiece = main[landPiece[target]];
            var cost = new int[length];
            var parent = new int[length];
            Array.Fill(cost, int.MaxValue);
            var frontier = new LinkedList<int>();
            for (var index = 0; index < length; index++)
                if (open[index] == mainPiece)
                {
                    cost[index] = 0;
                    parent[index] = -1;
                    frontier.AddLast(index);
                }
            var reached = -1;
            while (frontier.First is { } first && reached < 0)
            {
                var current = first.Value;
                frontier.RemoveFirst();
                if (open[current] >= 0 && open[current] != mainPiece)
                {
                    reached = current;
                    break;
                }
                var count = Neighbours(current, neighbours);
                for (var n = 0; n < count; n++)
                {
                    var next = neighbours[n];
                    if (!IsLand(next)) continue;
                    var step = elevation[next] >= Peak ? 1 : 0;
                    if (cost[current] + step >= cost[next]) continue;
                    cost[next] = cost[current] + step;
                    parent[next] = current;
                    if (step == 0) frontier.AddFirst(next);
                    else frontier.AddLast(next);
                }
            }
            if (reached < 0)
                throw new InvalidOperationException("A peak wall could not be opened.");
            for (var index = reached; index >= 0; index = parent[index])
                if (elevation[index] >= Peak) elevation[index] = Peak - 1;
        }

        bool IsLand(int index) => water[index] == (byte)WaterKind.Land;

        List<int> Label(int[] labels, Func<int, bool> include)
        {
            Array.Fill(labels, -1);
            var sizes = new List<int>();
            for (var start = 0; start < length; start++)
            {
                if (labels[start] >= 0 || !include(start)) continue;
                var head = 0;
                var tail = 0;
                labels[start] = sizes.Count;
                queue[tail++] = start;
                while (head < tail)
                {
                    var current = queue[head++];
                    var count = Neighbours(current, neighbours);
                    for (var n = 0; n < count; n++)
                    {
                        var next = neighbours[n];
                        if (labels[next] >= 0 || !include(next)) continue;
                        labels[next] = sizes.Count;
                        queue[tail++] = next;
                    }
                }
                sizes.Add(tail);
            }
            return sizes;
        }

        // Walking moves between edge-sharing tiles; a diagonal step needs both
        // shoulders open, so edge neighbours decide what is cut off.
        int Neighbours(int index, Span<int> result)
        {
            var x = index % width;
            var y = index / width;
            var count = 0;
            if (y > 0) result[count++] = index - width;
            if (y + 1 < height) result[count++] = index + width;
            if (x > 0) result[count++] = index - 1;
            else if (wrap) result[count++] = index + width - 1;
            if (x + 1 < width) result[count++] = index + 1;
            else if (wrap) result[count++] = index - x;
            return count;
        }
    }

    // Each massif keeps only its largest connected piece, so a coast or lake
    // that clips it leaves no stray patch. A massif whose piece is still
    // below the minimum size is flattened entirely.
    private static int[] KeepOneWholePiecePerMassif(byte[] owner, int massifCount, byte[] elevation,
        byte[] baseElevation, int width, int height, bool wrap)
    {
        var length = owner.Length;
        var piece = new int[length];
        Array.Fill(piece, -1);
        var pieceSizes = new List<int>();
        var largest = new int[massifCount + 1];
        Array.Fill(largest, -1);
        var queue = new int[length];
        for (var start = 0; start < length; start++)
        {
            if (owner[start] == 0 || piece[start] >= 0) continue;
            var label = pieceSizes.Count;
            var head = 0;
            var tail = 0;
            piece[start] = label;
            queue[tail++] = start;
            while (head < tail)
            {
                var current = queue[head++];
                foreach (var near in EightNeighbours(current, width, height, wrap))
                {
                    if (owner[near] != owner[start] || piece[near] >= 0) continue;
                    piece[near] = label;
                    queue[tail++] = near;
                }
            }
            pieceSizes.Add(tail);
            if (largest[owner[start]] < 0 || tail > pieceSizes[largest[owner[start]]])
                largest[owner[start]] = label;
        }

        var sizes = new int[massifCount + 1];
        for (var index = 0; index < length; index++)
        {
            if (owner[index] == 0) continue;
            var kept = largest[owner[index]];
            if (piece[index] == kept && pieceSizes[kept] >= TerrainPlacementRules.MinimumMassifTiles)
            {
                sizes[owner[index]]++;
                continue;
            }
            elevation[index] = baseElevation[index];
            owner[index] = 0;
        }
        return sizes;
    }

    // Raises dry land around each massif into a foothill slope that stays at
    // hill height out to the massif's hill reach, then eases back to lowland.
    private static void RaiseFoothills(byte[] owner, int[] sizes, byte[] elevation, byte[] water,
        int width, int height, bool wrap)
    {
        var length = owner.Length;
        var distance = new int[length];
        Array.Fill(distance, int.MaxValue);
        var queue = new List<int>();
        for (var id = 1; id < sizes.Length; id++)
        {
            if (sizes[id] == 0) continue;
            var reach = TerrainPlacementRules.HillReach(sizes[id]);
            queue.Clear();
            for (var index = 0; index < length; index++)
                if (owner[index] == id)
                {
                    distance[index] = 0;
                    queue.Add(index);
                }
            for (var head = 0; head < queue.Count; head++)
            {
                var current = queue[head];
                var next = distance[current] + 1;
                if (next > reach + FoothillTaperTiles) continue;
                foreach (var near in EightNeighbours(current, width, height, wrap))
                {
                    if (distance[near] <= next) continue;
                    distance[near] = next;
                    queue.Add(near);
                    if (water[near] != (byte)WaterKind.Land || owner[near] != 0) continue;
                    const int top = Mountain - 1;
                    const int span = top - TerrainPlacementRules.HillMinimumElevation;
                    var slope = next <= reach
                        ? top - (next - 1) * span / reach
                        : TerrainPlacementRules.HillMinimumElevation - 1 - (next - reach - 1) * 8;
                    elevation[near] = (byte)Math.Max(elevation[near], slope);
                }
            }
            foreach (var index in queue) distance[index] = int.MaxValue;
        }
    }

    // Steps, counting diagonal ones, from each tile to the nearest water or
    // to an edge where the map does not continue.
    private static int[] DistanceFromWater(byte[] water, int width, int height, bool wrap)
    {
        var length = water.Length;
        var distance = new int[length];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        for (var index = 0; index < length; index++)
        {
            var x = index % width;
            var y = index / width;
            if (water[index] != (byte)WaterKind.Land) distance[index] = 0;
            else if (y == 0 || y == height - 1 || !wrap && (x == 0 || x == width - 1)) distance[index] = 1;
            else continue;
            queue.Enqueue(index);
        }
        while (queue.TryDequeue(out var current))
            foreach (var near in EightNeighbours(current, width, height, wrap))
            {
                if (distance[near] <= distance[current] + 1) continue;
                distance[near] = distance[current] + 1;
                queue.Enqueue(near);
            }
        return distance;
    }

    // Gathers the map tiles whose plain-ellipse distance, with the axes
    // widened by the given number of tiles, is at most the limit.
    private static void Collect(Massif massif, int extraTiles, double limit, int width, int height, bool wrap,
        List<Cell> cells)
    {
        cells.Clear();
        // The farthest such tile lies within the widened ellipse's half-axes,
        // plus the sideways shift the bend adds at its ends.
        var reachAlong = (massif.Long + extraTiles) * limit;
        var reachAcross = (massif.Short + extraTiles) * limit +
            Math.Abs(massif.Bend) * reachAlong * reachAlong / massif.Long;
        var radius = (int)Math.Ceiling(Math.Sqrt(reachAlong * reachAlong + reachAcross * reachAcross)) + 1;
        var horizontal = wrap ? Math.Min(radius, (width - 1) / 2) : radius;
        for (var y = Math.Max(0, massif.Y - radius); y <= Math.Min(height - 1, massif.Y + radius); y++)
            for (var offset = -horizontal; offset <= horizontal; offset++)
            {
                var x = massif.X + offset;
                if (wrap) x = (x % width + width) % width;
                else if (x < 0 || x >= width) continue;
                var dx = (double)offset;
                var dy = (double)(y - massif.Y);
                var along = dx * massif.AxisX + dy * massif.AxisY;
                var across = dy * massif.AxisX - dx * massif.AxisY - massif.Bend * along * along / massif.Long;
                var cell = new Cell(y * width + x, x, y, along, across);
                if (Distance(massif, cell, extraTiles) <= limit) cells.Add(cell);
            }
    }

    private static double Distance(Massif massif, Cell cell, int extraTiles) =>
        Math.Sqrt(Square(cell.Along / (massif.Long + extraTiles)) + Square(cell.Across / (massif.Short + extraTiles)));

    private static double Square(double value) => value * value;

    // A seeded unit direction from whole numbers, so no platform-dependent
    // trigonometry decides a massif's axis.
    private static (double X, double Y) RandomDirection(Pcg32XshRrV1 random)
    {
        while (true)
        {
            var x = (int)(random.NextUInt() % 2049) - 1024;
            var y = (int)(random.NextUInt() % 2049) - 1024;
            var squared = x * x + y * y;
            if (squared is < 256 * 256 or > 1024 * 1024) continue;
            var norm = Math.Sqrt(squared);
            return (x / norm, y / norm);
        }
    }

    private static IEnumerable<int> EightNeighbours(int index, int width, int height, bool wrap)
    {
        var x = index % width;
        var y = index / width;
        for (var dy = -1; dy <= 1; dy++)
        {
            var nearY = y + dy;
            if (nearY < 0 || nearY >= height) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nearX = x + dx;
                if (wrap) nearX = (nearX + width) % width;
                else if (nearX < 0 || nearX >= width) continue;
                yield return nearY * width + nearX;
            }
        }
    }
}
