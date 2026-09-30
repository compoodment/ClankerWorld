using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.World;

public enum WorldSizePreset
{
    Small,
    Medium,
    Large,
    Huge,
    Mega,
}

public enum WaterKind : byte
{
    Land,
    Ocean,
    Lake,
    River,
}

public enum ClimateMode : byte
{
    Balanced,
    Uniform,
    Dominant,
}

public enum ClimateZone : byte
{
    Tropical,
    Dry,
    Temperate,
    Cold,
    Polar,
}

public enum ResourceAbundance : byte
{
    Sparse,
    Normal,
    Abundant,
}

public enum GenerationAmount : byte
{
    Normal,
    Low,
    High,
}

public sealed record GeographyOptions(
    string Seed,
    WorldSizePreset Size,
    bool WrapEastWest = true,
    int WaterPercent = 45,
    ClimateMode ClimateMode = ClimateMode.Balanced,
    ClimateZone SelectedClimate = ClimateZone.Temperate,
    bool LatitudeCooling = true,
    ResourceAbundance ResourceAbundance = ResourceAbundance.Normal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int HydrologyVersion = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] GenerationAmount ForestCover = GenerationAmount.Normal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] GenerationAmount MountainRelief = GenerationAmount.Normal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] GenerationAmount RiverAbundance = GenerationAmount.Normal);

public readonly record struct GeographyTile(byte Elevation, byte Rainfall, WaterKind Water,
    byte Temperature, ClimateZone Climate);

/// <summary>
/// Compact generated geography. Elevation, rainfall, temperature and climate
/// are independent layers; visual terrain is projected from them later.
/// </summary>
public sealed class GeneratedGeography
{
    private readonly byte[] elevation;
    private readonly byte[] rainfall;
    private readonly byte[] water;
    private readonly byte[] temperature;
    private readonly byte[] climate;
    private readonly int[] drainage;

    internal GeneratedGeography(int width, int height, bool wrapsEastWest, byte[] elevation,
        byte[] rainfall, byte[] water, byte[] temperature, byte[] climate, int[] drainage)
    {
        Width = width;
        Height = height;
        WrapsEastWest = wrapsEastWest;
        this.elevation = elevation;
        this.rainfall = rainfall;
        this.water = water;
        this.temperature = temperature;
        this.climate = climate;
        this.drainage = drainage;
    }

    public int Width { get; }
    public int Height { get; }
    public bool WrapsEastWest { get; }
    public GeographyTile At(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (x >= Width || y >= Height) throw new ArgumentOutOfRangeException(x >= Width ? nameof(x) : nameof(y));
        var index = (y * Width) + x;
        return new GeographyTile(elevation[index], rainfall[index], (WaterKind)water[index],
            temperature[index], (ClimateZone)climate[index]);
    }

    public int Count(WaterKind kind) => water.Count(value => value == (byte)kind);

    public (int X, int Y)? DownstreamAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (x >= Width || y >= Height) throw new ArgumentOutOfRangeException(x >= Width ? nameof(x) : nameof(y));
        var index = drainage[(y * Width) + x];
        return index < 0 ? null : (index % Width, index / Width);
    }
}

/// <summary>
/// A deterministic 2D terrain foundation. FastNoiseLite supplies elevation;
/// drainage, lake/ocean classification, and rivers are separate map passes.
/// The preset dimensions are provisional benchmarking targets, not final art
/// or gameplay scale promises.
/// </summary>
public static class GeographyGenerator
{
    public const int CurrentHydrologyVersion = 1;
    public const int ChunkSize = 64;

    public static (int Width, int Height) Dimensions(WorldSizePreset size) => size switch
    {
        WorldSizePreset.Small => (256, 128),
        WorldSizePreset.Medium => (512, 256),
        WorldSizePreset.Large => (1024, 512),
        WorldSizePreset.Huge => (2048, 1024),
        WorldSizePreset.Mega => (4096, 2048),
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    public static GeneratedGeography Generate(GeographyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Seed);
        if (options.WaterPercent is < 10 or > 80)
            throw new ArgumentOutOfRangeException(nameof(options), "Water percentage must be between 10 and 80.");
        if (!Enum.IsDefined(options.ClimateMode) || !Enum.IsDefined(options.SelectedClimate) ||
            !Enum.IsDefined(options.ResourceAbundance) || !Enum.IsDefined(options.ForestCover) ||
            !Enum.IsDefined(options.MountainRelief) || !Enum.IsDefined(options.RiverAbundance))
            throw new ArgumentOutOfRangeException(nameof(options), "The climate selection is invalid.");

        if (options.HydrologyVersion is < 0 or > CurrentHydrologyVersion)
            throw new ArgumentOutOfRangeException(nameof(options), "The hydrology version is unsupported.");
        var (width, height) = Dimensions(options.Size);
        var length = checked(width * height);
        var elevation = new byte[length];
        var rainfall = new byte[length];
        var water = new byte[length];
        var temperature = new byte[length];
        var climate = new byte[length];
        var elevationNoise = NewNoise(NoiseSeed(options.Seed, "elevation"), 0.012f);
        var rainNoise = NewNoise(NoiseSeed(options.Seed, "rainfall"), 0.018f);
        var temperatureNoise = NewNoise(NoiseSeed(options.Seed, "temperature"), 0.007f);
        var dominanceNoise = NewNoise(NoiseSeed(options.Seed, "climate-dominance"), 0.006f);

        // A circle in noise-input space makes the *flat* map's east and west
        // edges neighbors. Its third coordinate never becomes a game axis.
        var radius = width / (2f * MathF.PI);
        var circleX = new float[width];
        var circleZ = new float[width];
        if (options.WrapEastWest)
            for (var x = 0; x < width; x++)
            {
                var angle = 2f * MathF.PI * x / width;
                circleX[x] = radius * MathF.Cos(angle);
                circleZ[x] = radius * MathF.Sin(angle);
            }
        var histogram = new int[256];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = options.WrapEastWest
                    ? elevationNoise.GetNoise(circleX[x], y, circleZ[x])
                    : elevationNoise.GetNoise(x, y);
                var wetness = options.WrapEastWest
                    ? rainNoise.GetNoise(circleX[x], y, circleZ[x])
                    : rainNoise.GetNoise(x, y);
                var temperatureVariation = options.WrapEastWest
                    ? temperatureNoise.GetNoise(circleX[x], y, circleZ[x])
                    : temperatureNoise.GetNoise(x, y);
                var dominantVariation = options.WrapEastWest
                    ? dominanceNoise.GetNoise(circleX[x], y, circleZ[x])
                    : dominanceNoise.GetNoise(x, y);
                var scaled = (byte)Math.Clamp((int)MathF.Round((value + 1f) * 127.5f), 0, 255);
                scaled = (byte)Math.Clamp(scaled + (options.MountainRelief switch
                {
                    GenerationAmount.Low => -Math.Max(0, scaled - 130) / 2,
                    GenerationAmount.High => Math.Max(0, scaled - 130) / 2,
                    _ => 0,
                }), 0, 255);
                var index = y * width + x;
                elevation[index] = scaled;
                rainfall[index] = (byte)Math.Clamp((int)MathF.Round((wetness + 1f) * 127.5f), 0, 255);
                var latitude = Math.Abs((y + 0.5f) / height - 0.5f) * 2f;
                var heat = (options.LatitudeCooling ? 224f - 178f * latitude : 160f) +
                    temperatureVariation * 25f - Math.Max(0, scaled - 175) * 0.45f;
                temperature[index] = (byte)Math.Clamp((int)MathF.Round(heat), 0, 255);
                var natural = NaturalClimate(temperature[index], rainfall[index]);
                var polarCap = options.LatitudeCooling && latitude >= 0.9f;
                var chosen = polarCap ? ClimateZone.Polar : options.ClimateMode switch
                {
                    ClimateMode.Uniform => options.SelectedClimate,
                    // Coherent noise makes the dominant climate form broad
                    // regions; its exact land share is a playtest target.
                    ClimateMode.Dominant when dominantVariation > -0.18f => options.SelectedClimate,
                    _ => natural,
                };
                climate[index] = (byte)chosen;
                histogram[scaled]++;
            }
        }

        var targetWaterTiles = length * options.WaterPercent / 100;
        var waterLevel = 0;
        var covered = 0;
        while (waterLevel < histogram.Length - 1 && covered + histogram[waterLevel] < targetWaterTiles)
            covered += histogram[waterLevel++];
        for (var index = 0; index < length; index++)
            if (elevation[index] <= waterLevel) water[index] = (byte)WaterKind.Lake;

        ClassifyOceans(water, width, height, options.WrapEastWest);
        if (options.HydrologyVersion >= 1)
            BoundInlandLakes(elevation, water, width, height, options.WrapEastWest);
        var drainage = RouteRivers(elevation, rainfall, water, width, height, options.WrapEastWest,
            lakesAreTerminals: options.HydrologyVersion >= 1, options.RiverAbundance);
        return new GeneratedGeography(width, height, options.WrapEastWest, elevation, rainfall, water,
            temperature, climate, drainage);
    }

    private static ClimateZone NaturalClimate(byte temperature, byte rainfall) =>
        temperature < 48 ? ClimateZone.Polar :
        temperature < 105 ? ClimateZone.Cold :
        rainfall < 77 ? ClimateZone.Dry :
        temperature > 188 ? ClimateZone.Tropical : ClimateZone.Temperate;

    private static FastNoiseLite NewNoise(int seed, float frequency)
    {
        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(4);
        noise.SetFrequency(frequency);
        return noise;
    }

    private static int NoiseSeed(string worldSeed, string layer)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(worldSeed + ":" + layer));
        return BinaryPrimitives.ReadInt32LittleEndian(digest);
    }

    private static void ClassifyOceans(byte[] water, int width, int height, bool wrap)
    {
        var visited = new bool[water.Length];
        var largest = new List<int>();
        var borderBodies = new List<int>();
        var queue = new Queue<int>();
        Span<int> neighbors = stackalloc int[4];
        for (var start = 0; start < water.Length; start++)
        {
            if (water[start] != (byte)WaterKind.Lake || visited[start]) continue;
            var component = new List<int>();
            var touchesBorder = false;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var current))
            {
                component.Add(current);
                var x = current % width;
                var y = current / width;
                touchesBorder |= y == 0 || y == height - 1 || (!wrap && (x == 0 || x == width - 1));
                var neighborCount = WriteNeighbors(current, width, height, wrap, neighbors);
                for (var neighborIndex = 0; neighborIndex < neighborCount; neighborIndex++)
                {
                    var neighbor = neighbors[neighborIndex];
                    if (water[neighbor] != (byte)WaterKind.Lake || visited[neighbor]) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            if (component.Count > largest.Count) largest = component;
            if (touchesBorder) borderBodies.AddRange(component);
        }

        // Open-map border water is sea; on a wrapped cylindrical map there is
        // no east/west border, so keep the largest sea even if it misses a pole.
        foreach (var index in wrap ? largest : borderBodies) water[index] = (byte)WaterKind.Ocean;
        if (water.All(value => value != (byte)WaterKind.Ocean))
            foreach (var index in largest) water[index] = (byte)WaterKind.Ocean;
    }

    private static void BoundInlandLakes(byte[] elevation, byte[] water, int width, int height, bool wrap)
    {
        // Provisional geography tuning: at most half a percent of the map per inland lake.
        // Keep a connected low basin, then move removed water to connected ocean shoreline.
        // This changes geography, not just the label on a second sea.
        var maximumLakeArea = Math.Max(16, water.Length / 200);
        var inlandBasin = water.Select(value => value == (byte)WaterKind.Lake).ToArray();
        var visited = new bool[water.Length];
        var queue = new Queue<int>();
        var removed = 0;
        Span<int> neighbors = stackalloc int[4];
        for (var start = 0; start < water.Length; start++)
        {
            if (water[start] != (byte)WaterKind.Lake || visited[start]) continue;
            var component = new List<int>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var current))
            {
                component.Add(current);
                var count = WriteNeighbors(current, width, height, wrap, neighbors);
                for (var n = 0; n < count; n++)
                {
                    var next = neighbors[n];
                    if (visited[next] || water[next] != (byte)WaterKind.Lake) continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (component.Count <= maximumLakeArea) continue;
            var frontier = new PriorityQueue<int, (int Elevation, int Index)>();
            var offered = new HashSet<int>();
            var retained = new HashSet<int>();
            var deepest = component.OrderBy(index => elevation[index]).ThenBy(index => index).First();
            frontier.Enqueue(deepest, (elevation[deepest], deepest));
            offered.Add(deepest);
            while (retained.Count < maximumLakeArea && frontier.TryDequeue(out var current, out _))
            {
                retained.Add(current);
                var count = WriteNeighbors(current, width, height, wrap, neighbors);
                for (var n = 0; n < count; n++)
                {
                    var next = neighbors[n];
                    if (water[next] == (byte)WaterKind.Lake && offered.Add(next))
                        frontier.Enqueue(next, (elevation[next], next));
                }
            }
            foreach (var index in component)
                if (!retained.Contains(index))
                {
                    water[index] = (byte)WaterKind.Land;
                    removed++;
                }
        }

        var shoreline = new PriorityQueue<int, (int Elevation, int Index)>();
        var queued = new bool[water.Length];
        for (var index = 0; index < water.Length; index++)
            if (water[index] == (byte)WaterKind.Ocean)
                OfferNeighbors(index);
        while (removed > 0 && shoreline.TryDequeue(out var index, out _))
        {
            // Preserve a dry shoreline around retained lakes instead of joining them to the sea.
            var count = WriteNeighbors(index, width, height, wrap, neighbors);
            var touchesLake = false;
            for (var n = 0; n < count; n++) touchesLake |= water[neighbors[n]] == (byte)WaterKind.Lake;
            if (touchesLake) continue;
            water[index] = (byte)WaterKind.Ocean;
            removed--;
            OfferNeighbors(index);
        }
        if (removed != 0)
            throw new InvalidOperationException("The inland-water layout could not preserve its water coverage.");

        void OfferNeighbors(int index)
        {
            Span<int> adjacent = stackalloc int[4];
            var count = WriteNeighbors(index, width, height, wrap, adjacent);
            for (var n = 0; n < count; n++)
            {
                var next = adjacent[n];
                if (queued[next] || water[next] != (byte)WaterKind.Land) continue;
                queued[next] = true;
                // Prefer existing dry coastline over reflooding a basin we just reduced.
                // The finite penalty still leaves a fallback for unusually water-heavy maps.
                shoreline.Enqueue(next, (elevation[next] + (inlandBasin[next] ? 256 : 0), next));
            }
        }
    }

    private static int[] RouteRivers(byte[] elevation, byte[] rainfall, byte[] water, int width, int height, bool wrap, bool lakesAreTerminals, GenerationAmount abundance)
    {
        var length = water.Length;
        var floodedHeight = new int[length];
        var downstream = new int[length];
        var flow = new int[length];
        var visited = new bool[length];
        var visitedOrder = new List<int>(length);
        var frontier = new PriorityQueue<int, (int Height, int Index)>();
        Span<int> neighbors = stackalloc int[4];
        Array.Fill(downstream, -1);

        for (var index = 0; index < length; index++)
        {
            if (water[index] != (byte)WaterKind.Ocean && !(lakesAreTerminals && water[index] == (byte)WaterKind.Lake)) continue;
            visited[index] = true;
            floodedHeight[index] = elevation[index] * 1024;
            frontier.Enqueue(index, (floodedHeight[index], index));
        }

        while (frontier.TryDequeue(out var current, out _))
        {
            visitedOrder.Add(current);
            var neighborCount = WriteNeighbors(current, width, height, wrap, neighbors);
            for (var neighborIndex = 0; neighborIndex < neighborCount; neighborIndex++)
            {
                var neighbor = neighbors[neighborIndex];
                if (visited[neighbor]) continue;
                visited[neighbor] = true;
                // Raising a trapped cell just enough to reach an outlet
                // prevents cycles while keeping its river path deterministic.
                floodedHeight[neighbor] = Math.Max(elevation[neighbor] * 1024, floodedHeight[current] + 1);
                downstream[neighbor] = current;
                frontier.Enqueue(neighbor, (floodedHeight[neighbor], neighbor));
            }
        }

        for (var order = visitedOrder.Count - 1; order >= 0; order--)
        {
            var index = visitedOrder[order];
            if (water[index] == (byte)WaterKind.Ocean || lakesAreTerminals && water[index] == (byte)WaterKind.Lake) continue;
            flow[index] += 1 + (rainfall[index] / 64);
            var outlet = downstream[index];
            if (outlet >= 0) flow[outlet] += flow[index];
        }

        // Larger catchments have wider/longer rivers. The threshold is a
        // provisional visual-tuning value, not a climate or hydrology law.
        var threshold = abundance switch { GenerationAmount.Low => 288, GenerationAmount.High => 72, _ => 144 };
        for (var index = 0; index < length; index++)
            if (water[index] == (byte)WaterKind.Land && flow[index] >= threshold)
                water[index] = (byte)WaterKind.River;
        return downstream;
    }

    private static int WriteNeighbors(int index, int width, int height, bool wrap, Span<int> result)
    {
        var x = index % width;
        var y = index / width;
        var count = 0;
        if (y > 0) result[count++] = index - width;
        if (x + 1 < width) result[count++] = index + 1;
        else if (wrap) result[count++] = index - x;
        if (y + 1 < height) result[count++] = index + width;
        if (x > 0) result[count++] = index - 1;
        else if (wrap) result[count++] = index + width - 1;
        return count;
    }
}
