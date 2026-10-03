namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The hill band at the foot of each mountain region, read from the saved
/// elevation and water layers. This copies the simulation's
/// <c>TerrainPlacementRules.ClassifyHills</c>, which this client project
/// cannot reference; a simulation test checks that the two agree tile for tile.
/// </summary>
/// <remarks>
/// Hills are dry land below mountain height but at least 190 high, within a
/// reach that grows with the size of the connected mountain region (diagonal
/// neighbours join; east/west wraps only on a wrapped map). The reach runs from
/// three tiles for small regions to eight for the largest massifs.
/// </remarks>
public static class HillBand
{
    private const byte MountainElevation = 215;
    private const byte HillMinimumElevation = 190;
    private const int MinimumHillReach = 3;
    private const int MaximumHillReach = 8;
    private const int HillReachSquareRootStep = 7;

    /// <summary>How far the hill band reaches from a mountain region of this many tiles.</summary>
    public static int Reach(int regionTiles)
    {
        var root = 0;
        while ((root + 1) * (root + 1) <= regionTiles) root++;
        return Math.Clamp(root / HillReachSquareRootStep, MinimumHillReach, MaximumHillReach);
    }

    /// <summary>Marks every hill tile. Hydrology 0 is dry land.</summary>
    public static bool[] Classify(byte[] elevation, byte[] hydrology, int width, int height, bool wrapsEastWest)
    {
        var length = width * height;
        var region = new int[length];
        Array.Fill(region, -1);
        var sizes = new List<int>();
        var queue = new int[length];
        for (var start = 0; start < length; start++)
        {
            if (region[start] >= 0 || !IsMountain(start)) continue;
            var label = sizes.Count;
            var head = 0;
            var tail = 0;
            region[start] = label;
            queue[tail++] = start;
            while (head < tail)
            {
                var current = queue[head++];
                for (var direction = 0; direction < 8; direction++)
                {
                    var near = Neighbour(current, direction);
                    if (near < 0 || region[near] >= 0 || !IsMountain(near)) continue;
                    region[near] = label;
                    queue[tail++] = near;
                }
            }
            sizes.Add(tail);
        }

        // Each tile keeps the most steps it may still lie from some region:
        // that region's reach minus the tile's distance from it.
        var remaining = new int[length];
        Array.Fill(remaining, -1);
        var buckets = new List<int>[MaximumHillReach + 1];
        for (var reach = 0; reach <= MaximumHillReach; reach++) buckets[reach] = [];
        for (var index = 0; index < length; index++)
            if (region[index] >= 0)
            {
                remaining[index] = Reach(sizes[region[index]]);
                buckets[remaining[index]].Add(index);
            }
        for (var reach = MaximumHillReach; reach > 0; reach--)
            foreach (var current in buckets[reach])
            {
                if (remaining[current] != reach) continue;
                for (var direction = 0; direction < 8; direction++)
                {
                    var near = Neighbour(current, direction);
                    if (near < 0 || remaining[near] >= reach - 1) continue;
                    remaining[near] = reach - 1;
                    buckets[reach - 1].Add(near);
                }
            }

        var hills = new bool[length];
        for (var index = 0; index < length; index++)
            hills[index] = remaining[index] >= 0 && hydrology[index] == 0 &&
                elevation[index] is >= HillMinimumElevation and < MountainElevation;
        return hills;

        bool IsMountain(int index) => hydrology[index] == 0 && elevation[index] >= MountainElevation;

        int Neighbour(int index, int direction)
        {
            var dx = direction switch { 0 or 3 or 5 => -1, 2 or 4 or 7 => 1, _ => 0 };
            var dy = direction switch { 0 or 1 or 2 => -1, 5 or 6 or 7 => 1, _ => 0 };
            var y = index / width + dy;
            if (y < 0 || y >= height) return -1;
            var x = index % width + dx;
            if (wrapsEastWest) x = (x + width) % width;
            else if (x < 0 || x >= width) return -1;
            return y * width + x;
        }
    }
}
