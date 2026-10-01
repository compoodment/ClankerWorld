namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Client-side, provisional advice for choosing a first Town site. It never
/// decides whether the host can place the starter layout.
/// </summary>
public sealed class TownSiteGuidance
{
    // These starting values are for playtesting, not placement rules. The
    // owner has left the exact suitability weights and search extent open.
    public const int NearbyRadius = 8;

    private const byte FoodBit = 1;
    private const byte FarmlandBit = 2;
    private const byte WoodBit = 4;
    private const byte StoneBit = 8;
    private const int RoadSpaceRadius = 8;
    private const float RoadSpaceWeight = 1f;

    private readonly byte[] nearbyFactors;
    private readonly bool[] buildableSites;
    private readonly float[] openGroundForRoads;

    private TownSiteGuidance(int width, int height, byte[] nearbyFactors,
        bool[] buildableSites, float[] openGroundForRoads)
    {
        Width = width;
        Height = height;
        this.nearbyFactors = nearbyFactors;
        this.buildableSites = buildableSites;
        this.openGroundForRoads = openGroundForRoads;
    }

    public int Width { get; }
    public int Height { get; }

    public static TownSiteGuidance Create(WorldTerrainMap terrain, OwnerWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(snapshot);

        var width = terrain.Width;
        var height = terrain.Height;
        var length = checked(width * height);
        var featureCells = new byte[length];
        var blockedForRoads = new bool[length];
        var buildableSites = new bool[length];

        foreach (var resource in snapshot.Resources)
        {
            if (!TryIndex(resource.Position.X, resource.Position.Y, terrain, out var index)) continue;
            if (IsUseful(resource))
            {
                if (IsFood(resource)) featureCells[index] |= FoodBit;
                if (IsFarmland(resource)) featureCells[index] |= FarmlandBit;
                if (IsWood(resource)) featureCells[index] |= WoodBit;
                if (IsStone(resource)) featureCells[index] |= StoneBit;
            }
            blockedForRoads[index] = true;
        }

        foreach (var item in snapshot.Objects)
            if (TryIndex(item.Position.X, item.Position.Y, terrain, out var index))
                blockedForRoads[index] = true;

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (terrain.SurfaceAt(x, y) == 7) featureCells[index] |= FarmlandBit;
                buildableSites[index] = IsBuildableGround(terrain, x, y);
            }

        var foodPrefix = BuildPrefix(featureCells, width, height, FoodBit);
        var farmlandPrefix = BuildPrefix(featureCells, width, height, FarmlandBit);
        var woodPrefix = BuildPrefix(featureCells, width, height, WoodBit);
        var stonePrefix = BuildPrefix(featureCells, width, height, StoneBit);
        var roadSpacePrefix = BuildRoadSpacePrefix(terrain, buildableSites, blockedForRoads);
        var nearbyFactors = new byte[length];
        var openGroundForRoads = new float[length];

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var factors = (byte)0;
                if (CountNear(foodPrefix, width, height, x, y, NearbyRadius, terrain.WrapsEastWest) > 0)
                    factors |= FoodBit;
                if (CountNear(farmlandPrefix, width, height, x, y, NearbyRadius, terrain.WrapsEastWest) > 0)
                    factors |= FarmlandBit;
                if (CountNear(woodPrefix, width, height, x, y, NearbyRadius, terrain.WrapsEastWest) > 0)
                    factors |= WoodBit;
                if (CountNear(stonePrefix, width, height, x, y, NearbyRadius, terrain.WrapsEastWest) > 0)
                    factors |= StoneBit;
                nearbyFactors[index] = factors;

                var roadSpace = CountNear(roadSpacePrefix, width, height, x, y, RoadSpaceRadius,
                    terrain.WrapsEastWest);
                var area = NearbyArea(width, height, x, y, RoadSpaceRadius, terrain.WrapsEastWest);
                openGroundForRoads[index] = area == 0 ? 0 : (float)roadSpace / area;
            }

        return new TownSiteGuidance(width, height, nearbyFactors, buildableSites, openGroundForRoads);
    }

    public TownSiteAssessment At(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        var index = y * Width + x;
        var factors = nearbyFactors[index];
        var openGround = openGroundForRoads[index];
        var factorCount = BitCount((byte)(factors & (FoodBit | FarmlandBit | WoodBit | StoneBit)));
        return new TownSiteAssessment(
            buildableSites[index],
            (factors & FoodBit) != 0,
            (factors & FarmlandBit) != 0,
            (factors & WoodBit) != 0,
            (factors & StoneBit) != 0,
            openGround,
            (factorCount + RoadSpaceWeight * openGround) / 5f);
    }

    private static int BitCount(byte value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }

    private static bool IsUseful(OwnerWorldResource resource) =>
        resource.State is not ("depleted" or "stump") && resource.Quantity is not 0;

    private static bool IsFood(OwnerWorldResource resource) =>
        resource.Kind is "food" or "fruit" || resource.NaturalObjectKind is
            "berry_bush" or "wild_greens" or "wild_seed_patch";

    private static bool IsFarmland(OwnerWorldResource resource) =>
        resource.Kind == "fertile_land" || resource.NaturalObjectKind == "fertile_soil";

    private static bool IsWood(OwnerWorldResource resource) =>
        resource.TreeKind is not null || resource.Kind is "wood" or "construction";

    private static bool IsStone(OwnerWorldResource resource) =>
        resource.Kind == "stone" || resource.NaturalObjectKind == "stone_outcrop";

    private static bool TryIndex(int x, int y, WorldTerrainMap terrain, out int index)
    {
        if (y < 0 || y >= terrain.Height)
        {
            index = -1;
            return false;
        }
        if (terrain.WrapsEastWest) x = Mod(x, terrain.Width);
        if (x < 0 || x >= terrain.Width)
        {
            index = -1;
            return false;
        }
        index = y * terrain.Width + x;
        return true;
    }

    private static bool IsBuildableGround(WorldTerrainMap terrain, int x, int y)
    {
        var kind = terrain.At(x, y);
        var hydrology = terrain.HydrologyAt(x, y);
        var surface = terrain.SurfaceAt(x, y);
        var elevation = terrain.ElevationAt(x, y);
        var land = hydrology is { } water
            ? water == 0
            : surface != 4 && kind is not (2 or 4 or 5 or 6);
        if (!land) return false;

        var mountain = elevation is { } level ? level is >= 215 and < 245 : kind == 3;
        var peak = elevation is { } peakLevel ? peakLevel >= 245 : kind == 10;
        if (mountain || peak || surface is 2 or 4) return false;

        // Detailed geography determines open ground when available. Legacy
        // terrain-only maps use the same four open ground kinds as the host.
        return terrain.HasMapLayers || kind is 1 or 7 or 8 or 9;
    }

    private static int[] BuildPrefix(byte[] cells, int width, int height, byte bit)
    {
        var prefix = new int[checked((width + 1) * (height + 1))];
        var stride = width + 1;
        for (var y = 0; y < height; y++)
        {
            var row = 0;
            for (var x = 0; x < width; x++)
            {
                if ((cells[y * width + x] & bit) != 0) row++;
                prefix[(y + 1) * stride + x + 1] = prefix[y * stride + x + 1] + row;
            }
        }
        return prefix;
    }

    private static int[] BuildRoadSpacePrefix(WorldTerrainMap terrain, bool[] buildableSites,
        bool[] blockedForRoads)
    {
        var width = terrain.Width;
        var height = terrain.Height;
        var prefix = new int[checked((width + 1) * (height + 1))];
        var stride = width + 1;
        for (var y = 0; y < height; y++)
        {
            var row = 0;
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (buildableSites[index] && !blockedForRoads[index]) row++;
                prefix[(y + 1) * stride + x + 1] = prefix[y * stride + x + 1] + row;
            }
        }
        return prefix;
    }

    private static int CountNear(int[] prefix, int width, int height, int x, int y, int radius, bool wraps)
    {
        var top = Math.Max(0, y - radius);
        var bottom = Math.Min(height - 1, y + radius);
        if (top > bottom) return 0;

        if (!wraps || width > radius * 2 + 1)
        {
            var left = x - radius;
            var right = x + radius;
            if (!wraps)
                return CountRectangle(prefix, width, Math.Max(0, left), top,
                    Math.Min(width - 1, right), bottom);
            if (left < 0)
                return CountRectangle(prefix, width, 0, top, right, bottom) +
                    CountRectangle(prefix, width, width + left, top, width - 1, bottom);
            if (right >= width)
                return CountRectangle(prefix, width, left, top, width - 1, bottom) +
                    CountRectangle(prefix, width, 0, top, right - width, bottom);
            return CountRectangle(prefix, width, left, top, right, bottom);
        }

        // On a narrow wrapped map, count each map tile once rather than
        // counting the same tile repeatedly as the neighborhood loops around.
        return CountRectangle(prefix, width, 0, top, width - 1, bottom);
    }

    private static int NearbyArea(int width, int height, int x, int y, int radius, bool wraps)
    {
        var rows = Math.Min(height - 1, y + radius) - Math.Max(0, y - radius) + 1;
        var columns = wraps
            ? Math.Min(width, radius * 2 + 1)
            : Math.Min(width - 1, x + radius) - Math.Max(0, x - radius) + 1;
        return Math.Max(0, rows) * Math.Max(0, columns);
    }

    private static int CountRectangle(int[] prefix, int width, int left, int top, int right, int bottom)
    {
        if (left > right || top > bottom) return 0;
        var stride = width + 1;
        return prefix[(bottom + 1) * stride + right + 1]
            - prefix[top * stride + right + 1]
            - prefix[(bottom + 1) * stride + left]
            + prefix[top * stride + left];
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;
}

public readonly record struct TownSiteAssessment(
    bool IsBuildableGround,
    bool HasNearbyFood,
    bool HasNearbyFarmland,
    bool HasNearbyWood,
    bool HasNearbyStone,
    float OpenGroundForRoads,
    float GuidanceStrength);
