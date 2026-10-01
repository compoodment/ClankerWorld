using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Harness;

/// <summary>
/// Rules that decide where sand, forest floor and hills form on a generated
/// map, and which ground can hold ordinary trees and plants. Generation runs
/// them as separate passes: elevation and water, then surface, then vegetation
/// eligibility, then object placement.
/// </summary>
/// <remarks>
/// The numbers here are provisional starting values. They were chosen from
/// fixed-seed measurements, not from owner-reviewed maps, and are expected to
/// change once the owner has reviewed generated maps.
/// </remarks>
public static class TerrainPlacementRules
{
    /// <summary>Dry-climate land at or below this rainfall is desert sand.</summary>
    public const byte DesertMaximumRainfall = 42;

    /// <summary>A beach needs low ground beside the ocean; higher shores stay grass.</summary>
    public const byte BeachMaximumElevation = 175;

    /// <summary>Scale of the stretches of ocean shore that become beach.</summary>
    public const float BeachNoiseFrequency = 0.045f;

    /// <summary>Shore tiles whose beach noise reaches this value become sand; the rest stay grass.</summary>
    public const float BeachNoiseThreshold = 0.3f;

    /// <summary>Provisional chance that an eligible forest-grass tile holds a tree.</summary>
    public const int ForestGrassTreePercent = 35;

    /// <summary>Open meadows have occasional trees rather than forest density.</summary>
    public const int MeadowTreePercent = 2;

    /// <summary>Scale of the tree groves inside a forest.</summary>
    public const float GroveNoiseFrequency = 0.11f;

    /// <summary>Forest tiles must reach this grove noise to become forest floor.</summary>
    public const float GroveNoiseThreshold = 0.15f;

    /// <summary>
    /// Forest floor is limited per 64×64 chunk so that every forest-floor tile
    /// can hold a harvestable tree within the chunk's resource budget.
    /// </summary>
    public const int MaximumForestFloorTilesPerChunk = 24;

    /// <summary>
    /// Resource sites a generated map may place in one chunk. The rest of the
    /// world-systems chunk limit stays free for sites the running world adds
    /// later, such as the three settlement sites beside the first Town.
    /// </summary>
    public static int GeneratedResourcesPerChunk => WorldSystemsConfig.Default.MaxResourcesPerChunk - 8;

    /// <summary>Hills never reach below this elevation; generation raises the foothills around each massif above it.</summary>
    public const byte HillMinimumElevation = 190;

    /// <summary>The narrowest hill band, in tiles counting diagonal steps as one, around the smallest mountain region.</summary>
    public const int MinimumHillReach = 3;

    /// <summary>The widest hill band, around the largest massifs.</summary>
    public const int MaximumHillReach = 8;

    /// <summary>The hill band gains one tile for each step of this size in the square root of a massif's tile count.</summary>
    public const int HillReachSquareRootStep = 7;

    /// <summary>
    /// Mountain regions smaller than this many tiles are flattened to just
    /// below mountain height, so only whole massifs remain.
    /// </summary>
    public const int MinimumMassifTiles = 150;

    /// <summary>
    /// The starting clearing always has a stone outcrop it can walk to within
    /// this many tiles, counting diagonal steps as one. If none formed there
    /// naturally, one is placed on the highest ground in reach.
    /// </summary>
    public const int FirstTownStoneReach = 32;

    /// <summary>
    /// Elevation noise outside a massif at or above this height is squeezed
    /// into the band just below mountain height, so it never forms a stray
    /// mountain but can still hold stone and foothills.
    /// </summary>
    public const byte HighlandFlattenStart = 205;

    /// <summary>
    /// How many massifs a world of each size has. Only Small and Medium are
    /// playable; the larger counts keep the same spirit for later sizes.
    /// </summary>
    public static (int Minimum, int Maximum) MassifCount(WorldSizePreset size) => size switch
    {
        WorldSizePreset.Small => (1, 2),
        WorldSizePreset.Medium => (2, 4),
        WorldSizePreset.Large => (3, 6),
        WorldSizePreset.Huge => (5, 10),
        WorldSizePreset.Mega => (8, 16),
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    /// <summary>
    /// The share of dry land, in tenths of a percent, that a world's massifs
    /// aim to cover before coasts and rivers trim them. Normal sits inside the
    /// agreed 5–12% Balanced target; Low and High stay distinct choices.
    /// </summary>
    public static (int Minimum, int Maximum) MountainSharePerMille(GenerationAmount relief) => relief switch
    {
        GenerationAmount.Low => (25, 35),
        GenerationAmount.High => (140, 170),
        _ => (75, 95),
    };

    /// <summary>
    /// How far, counting diagonal steps as one, the hill band reaches out
    /// from a connected mountain region of this many tiles. Larger massifs
    /// have wider foothills.
    /// </summary>
    public static int HillReach(int massifTiles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(massifTiles);
        var root = 0;
        while ((root + 1) * (root + 1) <= massifTiles) root++;
        return Math.Clamp(root / HillReachSquareRootStep, MinimumHillReach, MaximumHillReach);
    }

    /// <summary>
    /// Whether ground can hold an ordinary tree or plant. Sand, water and
    /// mountain rock cannot; stone outcrops and clay banks are not plants and
    /// may still stand there.
    /// </summary>
    public static bool CanHoldOrdinaryVegetation(SurfaceKind surface) =>
        surface is not (SurfaceKind.Sand or SurfaceKind.Water or SurfaceKind.Rock);

    /// <summary>Trees of any kind and the plant sites that grow from the ground.</summary>
    public static bool IsOrdinaryVegetation(MapResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.TreeKind is not null || resource.NaturalObjectKind is
            "berry_bush" or "wild_greens" or "fiber_plant" or "reeds" or "wild_seed_patch";
    }

    /// <summary>
    /// Marks the hill band around mountain regions: dry land below mountain
    /// height, at least <see cref="HillMinimumElevation"/> high, within the
    /// <see cref="HillReach"/> of a connected mountain region's size. Regions
    /// join diagonal neighbours and wrap east/west only on a wrapped map.
    /// Hills are a visual layer only; they walk and build like the ground
    /// beneath them. The Godot client's <c>HillBand</c> copies this rule.
    /// </summary>
    public static bool[] ClassifyHills(byte[] elevation, byte[] hydrology, int width, int height,
        bool wrapEastWest)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(hydrology);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var length = checked(width * height);
        if (elevation.Length != length || hydrology.Length != length)
            throw new ArgumentException("The elevation and water layers must match the map size.");

        // Label each connected mountain region, then give every tile the most
        // steps it may still lie from some region: that region's reach minus
        // its distance in eight-direction steps. Larger regions reach further.
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

        var remaining = new int[length];
        Array.Fill(remaining, -1);
        var buckets = new List<int>[MaximumHillReach + 1];
        for (var reach = 0; reach <= MaximumHillReach; reach++) buckets[reach] = [];
        for (var index = 0; index < length; index++)
            if (region[index] >= 0)
            {
                remaining[index] = HillReach(sizes[region[index]]);
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
            hills[index] = remaining[index] >= 0 && hydrology[index] == (byte)WaterKind.Land &&
                elevation[index] is >= HillMinimumElevation and < SeededMap.MountainElevationThreshold;
        return hills;

        bool IsMountain(int index) => hydrology[index] == (byte)WaterKind.Land &&
            elevation[index] >= SeededMap.MountainElevationThreshold;

        int Neighbour(int index, int direction)
        {
            var dx = direction switch { 0 or 3 or 5 => -1, 2 or 4 or 7 => 1, _ => 0 };
            var dy = direction switch { 0 or 1 or 2 => -1, 5 or 6 or 7 => 1, _ => 0 };
            var y = index / width + dy;
            if (y < 0 || y >= height) return -1;
            var x = index % width + dx;
            if (wrapEastWest) x = (x + width) % width;
            else if (x < 0 || x >= width) return -1;
            return y * width + x;
        }
    }
}
