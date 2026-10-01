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

    /// <summary>Hills never reach below this elevation, so a steep coast next to a mountain stays lowland.</summary>
    public const byte HillMinimumElevation = 190;

    /// <summary>How many tiles, counting diagonal steps as one, the hill band reaches out from a mountain.</summary>
    public const int HillReach = 3;

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
            "berry_bush" or "wild_greens" or "medicinal_herb_patch" or "fiber_plant" or "reeds" or "wild_seed_patch";
    }

    /// <summary>
    /// Marks the hill band around mountain regions: dry land below mountain
    /// height, at least <see cref="HillMinimumElevation"/> high, within
    /// <see cref="HillReach"/> tiles of a mountain or peak. Hills are a visual
    /// layer only; they walk and build like the ground beneath them.
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

        // Multi-source breadth-first search gives each tile its distance, in
        // eight-direction steps, to the nearest mountain or peak.
        var distance = new int[length];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        for (var index = 0; index < length; index++)
            if (hydrology[index] == (byte)WaterKind.Land && elevation[index] >= SeededMap.MountainElevationThreshold)
            {
                distance[index] = 0;
                queue.Enqueue(index);
            }
        var hills = new bool[length];
        while (queue.TryDequeue(out var current))
        {
            var next = distance[current] + 1;
            if (next > HillReach) continue;
            var x = current % width;
            var y = current / width;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nearY = y + dy;
                    var nearX = x + dx;
                    if (nearY < 0 || nearY >= height) continue;
                    if (wrapEastWest) nearX = (nearX % width + width) % width;
                    else if (nearX < 0 || nearX >= width) continue;
                    var near = nearY * width + nearX;
                    if (distance[near] <= next) continue;
                    distance[near] = next;
                    queue.Enqueue(near);
                    hills[near] = hydrology[near] == (byte)WaterKind.Land &&
                        elevation[near] is >= HillMinimumElevation and < SeededMap.MountainElevationThreshold;
                }
        }
        return hills;
    }
}
