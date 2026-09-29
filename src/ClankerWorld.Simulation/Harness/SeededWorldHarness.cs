using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Persistence;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Harness;

/// <summary>
/// A coordinate in the first world's bounded, logical ground grid.
/// </summary>
public readonly record struct GridPoint(int X, int Y);

public enum TerrainKind
{
    Meadow,
    Water,
    Mountain,
    River,
    Lake,
    Ocean,
    Peak,
    Sand,
    Forest,
    Snow,
}

// These are independent facts; TerrainKind remains the stable v1 manifest/save
// compatibility projection and the fallback for legacy maps and fixtures.
// Values 0-4 were shipped in map-layers-v1. Keep them stable for private saves.
public enum SurfaceKind : byte
{
    Grass,
    Sand,
    Rock,
    Snow,
    Water,
    ForestFloor,
    DryScrub,
    FertileSoil,
}

// Values 0-4 were shipped in map-layers-v1. Keep them stable for private saves.
public enum VegetationCover : byte { None, Grass, Forest, Scrub, Tundra, Cactus }

public enum ResourceState
{
    Available,
    Depleted,
}

public sealed record TerrainTile(GridPoint Position, TerrainKind Terrain);

public sealed record CampObject(string Id, string Kind, GridPoint Position);

public sealed record MapResource(
    string Id,
    string Kind,
    GridPoint Position,
    bool IsRenewable,
    string? TreeKind = null,
    string? NaturalObjectKind = null);

/// <summary>
/// The selected deterministic map attempt and its canonical manifest lock.
/// </summary>
public sealed record SeededMap(
    int Width,
    int Height,
    int GenerationAttempt,
    IReadOnlyList<TerrainTile> Tiles,
    IReadOnlyList<CampObject> CampObjects,
    IReadOnlyList<MapResource> Resources,
    string ManifestDigest)
{
    public const byte MountainElevationThreshold = 215;
    public const byte PeakElevationThreshold = 245;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? ClimateZones { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? ElevationLevels { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? HydrologyKinds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? SurfaceKinds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? VegetationKinds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WrapsEastWest { get; init; }

    // Keep the index outside the record: a cache field would silently change
    // record equality and could be copied into a `with` map with new tiles.
    private static readonly ConditionalWeakTable<SeededMap, byte[]> TerrainIndexes = new();
    private static readonly ConditionalWeakTable<SeededMap, HashSet<GridPoint>> CampReachability = new();

    public bool Contains(GridPoint point) =>
        point.X >= 0 && point.X < Width && point.Y >= 0 && point.Y < Height;

    public bool IsPassable(GridPoint point) =>
        Contains(point) && (IsOpenGroundAt(point) || IsMountainAt(point) || IsNarrowRiverCrossing(point));

    public int FootTravelCost(GridPoint point) => !IsPassable(point)
        ? throw new ArgumentOutOfRangeException(nameof(point), "The tile cannot be crossed on foot.")
        : IsRiverAt(point) || IsMountainAt(point) ? 200 : 100;

    public int FootDistance(GridPoint origin, GridPoint destination)
    {
        var horizontal = Math.Abs(origin.X - destination.X);
        if (WrapsEastWest)
            horizontal = Math.Min(horizontal, Width - horizontal);
        return Math.Max(horizontal, Math.Abs(origin.Y - destination.Y));
    }

    public int FootRouteHeuristicCost(GridPoint origin, GridPoint destination)
    {
        var horizontal = Math.Abs(origin.X - destination.X);
        if (WrapsEastWest)
            horizontal = Math.Min(horizontal, Width - horizontal);
        var vertical = Math.Abs(origin.Y - destination.Y);
        var diagonal = Math.Min(horizontal, vertical);
        return checked(diagonal * 141 + (Math.Max(horizontal, vertical) - diagonal) * 100);
    }

    public bool IsDiagonalFootStep(GridPoint origin, GridPoint destination)
    {
        if (!Contains(origin) || !Contains(destination)) return false;
        var horizontal = Math.Abs(origin.X - destination.X);
        if (WrapsEastWest) horizontal = Math.Min(horizontal, Width - horizontal);
        return horizontal == 1 && Math.Abs(origin.Y - destination.Y) == 1;
    }

    public bool CanFootStep(GridPoint origin, GridPoint destination)
    {
        if (!IsPassable(origin) || !IsPassable(destination) || FootDistance(origin, destination) != 1)
            return false;
        var originIsRiver = IsRiverAt(origin);
        var destinationIsRiver = IsRiverAt(destination);
        if (originIsRiver || destinationIsRiver)
        {
            // A narrow river is a bank-to-bank crossing, not a footpath along
            // the channel or a diagonal shortcut through the water.
            if (originIsRiver && destinationIsRiver || IsDiagonalFootStep(origin, destination))
                return false;
            var river = originIsRiver ? origin : destination;
            var bank = originIsRiver ? destination : origin;
            var dx = bank.X - river.X;
            if (WrapsEastWest)
            {
                if (dx == Width - 1) dx = -1;
                else if (dx == 1 - Width) dx = 1;
            }
            var dy = bank.Y - river.Y;
            return Math.Abs(dx) + Math.Abs(dy) == 1 && IsDryBank(bank) &&
                IsDryBank(new GridPoint(river.X - dx, river.Y - dy));
        }
        if (!IsDiagonalFootStep(origin, destination)) return true;
        // Both orthogonal shoulders must be traversable. A diagonal cannot
        // squeeze around a wall, peak, deep river, or map edge.
        return IsPassable(new GridPoint(destination.X, origin.Y)) &&
            IsPassable(new GridPoint(origin.X, destination.Y));
    }

    public int FootStepCost(GridPoint origin, GridPoint destination) =>
        !CanFootStep(origin, destination)
            ? throw new ArgumentOutOfRangeException(nameof(destination), "The foot step is illegal.")
            : checked(FootTravelCost(destination) * (IsDiagonalFootStep(origin, destination) ? 141 : 100) / 100);

    public IEnumerable<GridPoint> FootNeighbors(GridPoint point)
    {
        if (!Contains(point))
            throw new ArgumentOutOfRangeException(nameof(point));
        var seen = new HashSet<GridPoint>();
        foreach (var (dx, dy) in new (int X, int Y)[]
        {
            (0, -1), (1, 0), (0, 1), (-1, 0),
            (1, -1), (1, 1), (-1, 1), (-1, -1),
        })
        {
            var x = point.X + dx;
            if (WrapsEastWest) x = (x % Width + Width) % Width;
            var next = new GridPoint(x, point.Y + dy);
            if (seen.Add(next) && CanFootStep(point, next))
                yield return next;
        }
    }

    // Construction eligibility is separate from travel: future mountain
    // paths must not silently become build sites when traversal is expanded.
    public bool IsBuildable(GridPoint point) => Contains(point) && IsOpenGroundAt(point) &&
        SurfaceAt(point) is not (SurfaceKind.Rock or SurfaceKind.Water);

    public ClimateZone? ClimateAt(GridPoint point) =>
        Contains(point) && ClimateZones is { } zones && zones.Length == Width * Height
            ? (ClimateZone)zones[point.Y * Width + point.X] : null;

    public byte? ElevationAt(GridPoint point) => LayerAt(ElevationLevels, point);
    public WaterKind? HydrologyAt(GridPoint point) => LayerAt(HydrologyKinds, point) is { } value
        ? (WaterKind)value : null;
    public SurfaceKind? SurfaceAt(GridPoint point) => LayerAt(SurfaceKinds, point) is { } value
        ? (SurfaceKind)value : null;
    public VegetationCover? VegetationAt(GridPoint point) => LayerAt(VegetationKinds, point) is { } value
        ? (VegetationCover)value : null;

    public TerrainKind? TerrainKindAt(GridPoint point)
    {
        if (!Contains(point)) return null;
        var kind = TerrainAt(point);
        return kind == byte.MaxValue ? null : (TerrainKind)kind;
    }

    private byte? LayerAt(byte[]? layer, GridPoint point) =>
        Contains(point) && layer?.Length == Width * Height
            ? layer[point.Y * Width + point.X] : null;

    private static readonly ConditionalWeakTable<SeededMap, int[]> FootComponents = new();

    public bool IsReachableOnFoot(GridPoint origin, GridPoint destination)
    {
        if (!Contains(origin) || !Contains(destination) || !IsPassable(origin) || !IsPassable(destination)) return false;
        var components = FootComponents.GetValue(this, static map =>
        {
            var labels = new int[checked(map.Width * map.Height)];
            var component = 0;
            foreach (var tile in map.Tiles)
            {
                var index = tile.Position.Y * map.Width + tile.Position.X;
                if (labels[index] != 0 || !map.IsPassable(tile.Position)) continue;
                component++;
                foreach (var point in MapAcceptance.ReachableFrom(map, tile.Position))
                    labels[point.Y * map.Width + point.X] = component;
            }
            return labels;
        });
        return components[origin.Y * Width + origin.X] == components[destination.Y * Width + destination.X];
    }

    public bool IsReachableFromCampOnFoot(GridPoint point) => Contains(point) &&
        CampReachability.GetValue(this, static map =>
            MapAcceptance.ReachableFrom(map,
                map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
                map.Resources.First(item => item.Id == "berry-patch").Position)).Contains(point);

    private static bool IsOpenGround(byte kind) => kind is
        (byte)TerrainKind.Meadow or (byte)TerrainKind.Sand or
        (byte)TerrainKind.Forest or (byte)TerrainKind.Snow;

    private bool IsOpenGroundAt(GridPoint point)
    {
        var terrain = TerrainAt(point);
        if (!IsLandAt(point) || IsMountainAt(point) || IsPeakAt(point)) return false;
        return HydrologyAt(point) is not null || ElevationAt(point) is not null ||
            SurfaceAt(point) is not null || IsOpenGround(terrain);
    }

    private bool IsLandAt(GridPoint point) => HydrologyAt(point) is { } water
        ? water == WaterKind.Land
        : SurfaceAt(point) is SurfaceKind.Water ? false
        : TerrainAt(point) switch
        {
            (byte)TerrainKind.Water or (byte)TerrainKind.River or
                (byte)TerrainKind.Lake or (byte)TerrainKind.Ocean => false,
            _ => true,
        };

    private bool IsRiverAt(GridPoint point) => HydrologyAt(point) is { } water
        ? water == WaterKind.River
        : TerrainAt(point) == (byte)TerrainKind.River;

    private bool IsMountainAt(GridPoint point)
    {
        if (!IsLandAt(point)) return false;
        return ElevationAt(point) is { } elevation
            ? elevation >= MountainElevationThreshold && elevation < PeakElevationThreshold
            : TerrainAt(point) == (byte)TerrainKind.Mountain;
    }

    private bool IsPeakAt(GridPoint point) => ElevationAt(point) is { } elevation
        ? elevation >= PeakElevationThreshold
        : TerrainAt(point) == (byte)TerrainKind.Peak;

    private bool IsNarrowRiverCrossing(GridPoint point)
    {
        if (!IsRiverAt(point))
            return false;
        var west = new GridPoint(point.X - 1, point.Y);
        var east = new GridPoint(point.X + 1, point.Y);
        var north = new GridPoint(point.X, point.Y - 1);
        var south = new GridPoint(point.X, point.Y + 1);
        return IsDryBank(west) && IsDryBank(east) ||
            IsDryBank(north) && IsDryBank(south);
    }

    private bool IsDryBank(GridPoint point)
    {
        if (WrapsEastWest && point.Y >= 0 && point.Y < Height)
            point = new GridPoint((point.X % Width + Width) % Width, point.Y);
        if (!Contains(point) || !IsLandAt(point) || IsPeakAt(point)) return false;
        return ElevationAt(point) is not null || SurfaceAt(point) is not null ||
            IsOpenGround(TerrainAt(point)) || IsMountainAt(point);
    }

    private byte TerrainAt(GridPoint point) =>
        TerrainIndexes.GetValue(this, static map =>
        {
            var indexed = new byte[checked(map.Width * map.Height)];
            Array.Fill(indexed, byte.MaxValue);
            foreach (var tile in map.Tiles)
                if (map.Contains(tile.Position))
                    indexed[tile.Position.Y * map.Width + tile.Position.X] = checked((byte)tile.Terrain);
            return indexed;
        })[point.Y * Width + point.X];

    public CampObject GetObject(string id) =>
        CampObjects.Single(mapObject => string.Equals(mapObject.Id, id, StringComparison.Ordinal));

    public MapResource GetResource(string id) =>
        Resources.Single(resource => string.Equals(resource.Id, id, StringComparison.Ordinal));
}

/// <summary>
/// Generates the tiny fixed-corpus temperate camp fixture. It intentionally has
/// only enough terrain variation to exercise the validation rules.
/// </summary>
public static class SeededMapGenerator
{
    public const int MaximumAttempts = 32;
    public const string GeneratorId = "temperate-fixture";
    public const string GeneratorVersion = "v1";
    public const string GeneratorConfigDigest = "sha256:temperate-fixture-config-v2";
    public const string FertileLandResourceId = "fertile-land";

    public static SeededMap Generate(string worldSeed, bool includeLegacyBedroll = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);

        var diagnostics = new List<string>();
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var candidate = CreateCandidate(worldSeed, attempt, includeLegacyBedroll);
            var validation = MapAcceptance.Validate(candidate);
            if (validation.IsValid)
            {
                return candidate;
            }

            diagnostics.Add($"attempt {attempt}: {validation.Failure}");
        }

        throw new InvalidOperationException(
            $"No valid temperate fixture map for seed '{worldSeed}' after {MaximumAttempts} attempts: " +
            string.Join("; ", diagnostics));
    }

    private static SeededMap CreateCandidate(string worldSeed, int attempt, bool includeLegacyBedroll)
    {
        const int width = 6;
        const int height = 5;
        var terrain = new TerrainKind[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                terrain[y, x] = TerrainKind.Meadow;
            }
        }

        var safeObstacles = new[]
        {
            new GridPoint(5, 0),
            new GridPoint(5, 4),
            new GridPoint(0, 4),
            new GridPoint(4, 4),
        };
        var random = Pcg32XshRrV1.Create(worldSeed, $"worldgen/attempt:{attempt}");
        var waterIndex = (int)(random.NextUInt() % (uint)safeObstacles.Length);
        var mountainIndex = (waterIndex + 1 + (int)(random.NextUInt() % (uint)(safeObstacles.Length - 1))) %
            safeObstacles.Length;
        terrain[safeObstacles[waterIndex].Y, safeObstacles[waterIndex].X] = TerrainKind.Water;
        terrain[safeObstacles[mountainIndex].Y, safeObstacles[mountainIndex].X] = TerrainKind.Mountain;

        var tiles = new List<TerrainTile>(width * height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                tiles.Add(new TerrainTile(new GridPoint(x, y), terrain[y, x]));
            }
        }

        var campObjects = new[]
        {
            new CampObject("campfire", "cooking", new GridPoint(2, 0)),
            new CampObject("founder-scout", "founder", new GridPoint(0, 0)),
            new CampObject("shelter", "shelter", new GridPoint(0, 1)),
            new CampObject("storage", "storage", new GridPoint(1, 1)),
        };
        if (includeLegacyBedroll)
            campObjects = [new CampObject("bedroll", "bedroll", new GridPoint(1, 0)), .. campObjects];
        var resources = new[]
        {
            new MapResource("berry-patch", "food", new GridPoint(4, 1), true),
            new MapResource("timber-tree", "construction", new GridPoint(4, 3), false),
            new MapResource(FertileLandResourceId, "fertile_land", new GridPoint(2, 3), false),
        };
        var withoutDigest = new SeededMap(width, height, attempt, tiles, campObjects, resources, string.Empty);
        return withoutDigest with { ManifestDigest = MapManifestCodec.Digest(withoutDigest) };
    }
}

/// <summary>A starter camp with facilities but no pre-created person.</summary>
public static class BaseCampMapGenerator
{
    public static SeededMap Generate(string worldSeed, bool includeLegacyBedroll = false)
    {
        var fixture = SeededMapGenerator.Generate(worldSeed, includeLegacyBedroll);
        var objects = fixture.CampObjects
            .Where(item => item.Kind != "founder")
            .Concat([
                new CampObject("second-shelter", "shelter", new GridPoint(3, 0)),
                new CampObject("workshop", "workshop", new GridPoint(0, 2)),
                new CampObject("camp-path", "path", new GridPoint(2, 1)),
            ]).ToArray();
        var candidate = fixture with { CampObjects = objects, ManifestDigest = string.Empty };
        var map = candidate with { ManifestDigest = MapManifestCodec.Digest(candidate) };
        var validation = MapAcceptance.Validate(map, allowEmptyCamp: true);
        if (!validation.IsValid)
            throw new InvalidOperationException($"The generated base camp is invalid: {validation.Failure}");
        return map;
    }
}

/// <summary>
/// Projects generated 2D geography into the current physical-map contract and
/// places the ordinary empty starter camp on a connected buildable patch.
/// Climate is retained independently from its provisional ground appearance.
/// </summary>
public static class GeneratedCampMapGenerator
{
    private const int CampWidth = 6;
    private const int CampHeight = 5;

    public static SeededMap Generate(GeographyOptions options, bool includeLegacyBedroll = false)
        => GenerateCore(options, includeLegacyBedroll, legacyLayout: false, retainLegacyCamp: false);

    // Preserve the exact map lineage of pre-site-selection generated worlds.
    // Their saved camp objects remain valid and are never silently rewritten.
    public static SeededMap GenerateWithLegacyCamp(GeographyOptions options, bool includeLegacyBedroll = false)
        => GenerateCore(options, includeLegacyBedroll, legacyLayout: false, retainLegacyCamp: true);

    // Reconstruct the pre-layer generated manifest when validating old saves.
    // The old resource placement and camp-site rules are part of that map's
    // identity; accepting its self-digest alone would not prove provenance.
    public static SeededMap GenerateLegacy(GeographyOptions options, bool includeLegacyBedroll)
        => GenerateCore(options, includeLegacyBedroll, legacyLayout: true, retainLegacyCamp: true);

    private static SeededMap GenerateCore(GeographyOptions options, bool includeLegacyBedroll,
        bool legacyLayout, bool retainLegacyCamp)
    {
        ArgumentNullException.ThrowIfNull(options);
        // This bridge still allocates one object per tile for the old
        // physical-map contract. The compact geography source supports all
        // presets; larger playable maps need a compact save/projection first.
        if (options.Size > WorldSizePreset.Medium)
            throw new NotSupportedException("Large generated worlds require the compact playable-map contract.");
        var geography = GeographyGenerator.Generate(options);
        var width = geography.Width;
        var height = geography.Height;
        var tiles = new TerrainTile[checked(width * height)];
        var kinds = new TerrainKind[tiles.Length];
        var climateZones = new byte[tiles.Length];
        var elevationLevels = new byte[tiles.Length];
        var hydrologyKinds = new byte[tiles.Length];
        var surfaceKinds = new byte[tiles.Length];
        var legacySurfaceKinds = new byte[tiles.Length];
        var vegetationKinds = new byte[tiles.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var tile = geography.At(x, y);
                var kind = tile.Water switch
                {
                    WaterKind.Ocean => TerrainKind.Ocean,
                    WaterKind.Lake => TerrainKind.Lake,
                    WaterKind.River => TerrainKind.River,
                    _ when tile.Elevation >= SeededMap.PeakElevationThreshold => TerrainKind.Peak,
                    _ when tile.Elevation >= SeededMap.MountainElevationThreshold => TerrainKind.Mountain,
                    _ when tile.Climate is ClimateZone.Polar or ClimateZone.Cold => TerrainKind.Snow,
                    _ when tile.Climate == ClimateZone.Dry => TerrainKind.Sand,
                    _ when tile.Rainfall >= 150 && tile.Climate is ClimateZone.Tropical or ClimateZone.Temperate => TerrainKind.Forest,
                    _ => TerrainKind.Meadow,
                };
                var index = y * width + x;
                kinds[index] = kind;
                climateZones[index] = (byte)tile.Climate;
                elevationLevels[index] = tile.Elevation;
                hydrologyKinds[index] = (byte)tile.Water;
                legacySurfaceKinds[index] = (byte)(tile.Water != WaterKind.Land ? SurfaceKind.Water :
                    tile.Elevation >= SeededMap.MountainElevationThreshold ? SurfaceKind.Rock :
                    tile.Climate is ClimateZone.Polar or ClimateZone.Cold ? SurfaceKind.Snow :
                    tile.Climate == ClimateZone.Dry ? SurfaceKind.Sand : SurfaceKind.Grass);
                // Final surface classification is a separate pass because
                // shore/beach choices depend on neighboring hydrology.
                surfaceKinds[index] = legacySurfaceKinds[index];
                vegetationKinds[index] = (byte)(tile.Water != WaterKind.Land ||
                    tile.Elevation >= SeededMap.MountainElevationThreshold
                    ? VegetationCover.None :
                    tile.Climate == ClimateZone.Dry && tile.Rainfall <= 46 && tile.Temperature >= 92
                        ? VegetationCover.Cactus :
                    tile.Climate == ClimateZone.Dry ? VegetationCover.Scrub :
                    tile.Climate is ClimateZone.Polar or ClimateZone.Cold ? VegetationCover.Tundra :
                    tile.Rainfall >= 150 && tile.Climate is ClimateZone.Tropical or ClimateZone.Temperate
                        ? VegetationCover.Forest : VegetationCover.Grass);
                tiles[index] = new TerrainTile(new GridPoint(x, y), kind);
            }

        ClassifySurfaces(geography, surfaceKinds, vegetationKinds, width, height, options.WrapEastWest);

        var origin = legacyLayout
            ? FindLegacyCampOrigin(kinds, width, height)
            : FindCampOrigin(hydrologyKinds, elevationLevels, legacySurfaceKinds, width, height);
        var template = BaseCampMapGenerator.Generate(options.Seed, includeLegacyBedroll);
        var objects = (retainLegacyCamp ? template.CampObjects : []).Select(item => item with
        {
            Position = new GridPoint(item.Position.X + origin.X, item.Position.Y + origin.Y),
        }).ToArray();
        var resources = template.Resources.Select(item => item with
        {
            Position = new GridPoint(item.Position.X + origin.X, item.Position.Y + origin.Y),
            IsRenewable = item.Id == "timber-tree" || item.IsRenewable,
            TreeKind = !legacyLayout && item.Id == "timber-tree" ? "broadleaf" : item.TreeKind,
            NaturalObjectKind = legacyLayout ? null : item.Id switch
            {
                "berry-patch" => "berry_bush",
                SeededMapGenerator.FertileLandResourceId => "fertile_soil",
                _ => null,
            },
        }).ToArray();
        var distributed = legacyLayout
            ? GenerateLegacyResourceSites(options, kinds, width, height, objects, resources)
            : GenerateResourceSites(options, geography, hydrologyKinds, elevationLevels,
            legacySurfaceKinds, vegetationKinds, objects, resources);
        List<MapResource> trees = legacyLayout ? [] : GenerateTrees(options, geography, vegetationKinds, width, height, objects,
            resources.Concat(distributed).ToArray());
        List<MapResource> orchards = legacyLayout ? [] : GenerateOrchards(options, geography, legacySurfaceKinds, vegetationKinds, width, height, objects,
            resources.Concat(distributed).Concat(trees).ToArray());
        List<MapResource> geology = [];
        if (!legacyLayout)
        {
            geology = GenerateGeologySites(options, geography, objects,
                resources.Concat(distributed).Concat(trees).Concat(orchards).ToArray());
            MarkFertileSoilSites(surfaceKinds,
                resources.Concat(distributed).Concat(trees).Concat(orchards).Concat(geology), width, height);
        }
        var withoutDigest = new SeededMap(width, height, 0, tiles, objects,
            resources.Concat(distributed).Concat(trees).Concat(orchards).Concat(geology).ToArray(), string.Empty)
        {
            ClimateZones = climateZones,
            ElevationLevels = elevationLevels,
            HydrologyKinds = hydrologyKinds,
            SurfaceKinds = surfaceKinds,
            VegetationKinds = vegetationKinds,
            WrapsEastWest = options.WrapEastWest,
        };
        var map = withoutDigest with { ManifestDigest = MapManifestCodec.Digest(withoutDigest) };
        var validation = MapAcceptance.Validate(map, allowEmptyCamp: true);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Generated world is invalid: {validation.Failure}");
        return map;
    }

    private static List<MapResource> GenerateLegacyResourceSites(GeographyOptions options,
        TerrainKind[] kinds, int width, int height, IReadOnlyList<CampObject> camp,
        IReadOnlyList<MapResource> starter)
    {
        const int spacing = 16;
        var occupied = camp.Select(item => item.Position)
            .Concat(starter.Select(item => item.Position)).ToHashSet();
        var sites = new List<MapResource>();
        for (var top = 0; top < height; top += spacing)
            for (var left = 0; left < width; left += spacing)
            {
                var sitesInCell = options.ResourceAbundance switch
                {
                    ResourceAbundance.Sparse => (left / spacing + top / spacing) % 2 == 0 ? 1 : 0,
                    ResourceAbundance.Normal => 1,
                    ResourceAbundance.Abundant => 2,
                    _ => throw new ArgumentOutOfRangeException(nameof(options)),
                };
                var random = Pcg32XshRrV1.Create(options.Seed, $"resource-site:{left},{top}");
                for (var site = 0; site < sitesInCell; site++)
                    for (var attempt = 0; attempt < 12; attempt++)
                    {
                        var x = left + (int)(random.NextUInt() % (uint)Math.Min(spacing, width - left));
                        var y = top + (int)(random.NextUInt() % (uint)Math.Min(spacing, height - top));
                        var position = new GridPoint(x, y);
                        var kind = kinds[y * width + x];
                        if (kind is not (TerrainKind.Meadow or TerrainKind.Sand or TerrainKind.Forest or TerrainKind.Snow) ||
                            occupied.Contains(position)) continue;
                        var selection = random.NextUInt() % 4;
                        var resourceKind = kind switch
                        {
                            TerrainKind.Forest => "construction",
                            TerrainKind.Sand => selection == 0 ? "fiber" : "stone",
                            TerrainKind.Snow => selection == 0 ? "food" : "stone",
                            _ => selection switch
                            {
                                0 => "fertile_land",
                                1 => "food",
                                2 => "fiber",
                                _ => "seed",
                            },
                        };
                        var renewable = resourceKind is "construction" or "food" or "fiber" or "seed";
                        var id = site == 0 ? $"wild-{left}-{top}" : $"wild-{left}-{top}-{site}";
                        sites.Add(new MapResource(id, resourceKind, position, renewable));
                        occupied.Add(position);
                        break;
                    }
            }
        return sites;
    }

    private static GridPoint FindLegacyCampOrigin(TerrainKind[] kinds, int width, int height)
    {
        var centerX = (width - CampWidth) / 2;
        var centerY = (height - CampHeight) / 2;
        var maxRadius = width + height;
        for (var radius = 0; radius <= maxRadius; radius++)
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var offsetY = radius - Math.Abs(offsetX);
                if (TryLegacySite(centerX + offsetX, centerY + offsetY, kinds, width, height))
                    return new GridPoint(centerX + offsetX, centerY + offsetY);
                if (offsetY > 0 && TryLegacySite(centerX + offsetX, centerY - offsetY, kinds, width, height))
                    return new GridPoint(centerX + offsetX, centerY - offsetY);
            }
        throw new InvalidOperationException("The generated geography has no suitable base-camp clearing.");
    }

    private static bool TryLegacySite(int left, int top, TerrainKind[] kinds, int width, int height)
    {
        if (left < 0 || top < 0 || left + CampWidth > width || top + CampHeight > height)
            return false;
        if (left / GeographyGenerator.ChunkSize != (left + CampWidth - 1) / GeographyGenerator.ChunkSize ||
            top / GeographyGenerator.ChunkSize != (top + CampHeight - 1) / GeographyGenerator.ChunkSize)
            return false;
        for (var y = top; y < top + CampHeight; y++)
            for (var x = left; x < left + CampWidth; x++)
                if (kinds[y * width + x] is not (TerrainKind.Meadow or TerrainKind.Sand or
                    TerrainKind.Forest or TerrainKind.Snow)) return false;
        return true;
    }

    private static void ClassifySurfaces(GeneratedGeography geography, byte[] surfaces, byte[] vegetation,
        int width, int height, bool wrap)
    {
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var tile = geography.At(x, y);
                if (tile.Water != WaterKind.Land)
                {
                    surfaces[index] = (byte)SurfaceKind.Water;
                    continue;
                }
                if (tile.Elevation >= SeededMap.MountainElevationThreshold)
                {
                    surfaces[index] = (byte)SurfaceKind.Rock;
                    continue;
                }
                if (tile.Climate is ClimateZone.Polar or ClimateZone.Cold)
                {
                    surfaces[index] = (byte)SurfaceKind.Snow;
                    continue;
                }
                if (IsAdjacentToWater(geography, x, y, wrap) && tile.Elevation < 175)
                {
                    surfaces[index] = (byte)SurfaceKind.Sand;
                    continue;
                }
                if (tile.Climate == ClimateZone.Dry)
                {
                    surfaces[index] = (byte)(tile.Rainfall <= 42
                        ? SurfaceKind.Sand : SurfaceKind.DryScrub);
                    continue;
                }
                if (vegetation[index] == (byte)VegetationCover.Forest && tile.Rainfall >= 185)
                {
                    surfaces[index] = (byte)SurfaceKind.ForestFloor;
                    continue;
                }
                // FertileSoil is assigned only at a saved fertile-land site;
                // climate and rainfall are not a per-tile fertility model.
                surfaces[index] = (byte)SurfaceKind.Grass;
            }
    }

    private static void MarkFertileSoilSites(byte[] surfaces, IEnumerable<MapResource> resources,
        int width, int height)
    {
        foreach (var resource in resources)
        {
            if (resource.NaturalObjectKind != "fertile_soil") continue;
            var point = resource.Position;
            if (point.X < 0 || point.X >= width || point.Y < 0 || point.Y >= height) continue;
            surfaces[point.Y * width + point.X] = (byte)SurfaceKind.FertileSoil;
        }
    }

    private static bool IsAdjacentToWater(GeneratedGeography geography, int x, int y, bool wrap)
    {
        foreach (var (dx, dy) in new (int X, int Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
        {
            var nextX = x + dx;
            var nextY = y + dy;
            if (wrap) nextX = (nextX % geography.Width + geography.Width) % geography.Width;
            if (nextX >= 0 && nextX < geography.Width && nextY >= 0 && nextY < geography.Height &&
                geography.At(nextX, nextY).Water != WaterKind.Land)
                return true;
        }
        return false;
    }

    private static List<MapResource> GenerateResourceSites(GeographyOptions options, GeneratedGeography geography,
        byte[] hydrologyKinds, byte[] elevationLevels, byte[] surfaceKinds, byte[] vegetationKinds,
        IReadOnlyList<CampObject> camp, IReadOnlyList<MapResource> starter)
    {
        const int spacing = 16;
        var width = geography.Width;
        var height = geography.Height;
        var occupied = camp.Select(item => item.Position)
            .Concat(starter.Select(item => item.Position)).ToHashSet();
        var sites = new List<MapResource>();
        for (var top = 0; top < height; top += spacing)
            for (var left = 0; left < width; left += spacing)
            {
                var sitesInCell = options.ResourceAbundance switch
                {
                    ResourceAbundance.Sparse => (left / spacing + top / spacing) % 2 == 0 ? 1 : 0,
                    ResourceAbundance.Normal => 1,
                    ResourceAbundance.Abundant => 2,
                    _ => throw new ArgumentOutOfRangeException(nameof(options)),
                };
                var random = Pcg32XshRrV1.Create(options.Seed, $"resource-site:{left},{top}");
                // A few candidates let coast and mountains leave some cells
                // empty without ever placing a site on water or high ground.
                for (var site = 0; site < sitesInCell; site++)
                {
                    for (var attempt = 0; attempt < 12; attempt++)
                    {
                        var x = left + (int)(random.NextUInt() % (uint)Math.Min(spacing, width - left));
                        var y = top + (int)(random.NextUInt() % (uint)Math.Min(spacing, height - top));
                        var position = new GridPoint(x, y);
                        var index = y * width + x;
                        var surface = (SurfaceKind)surfaceKinds[index];
                        var vegetation = (VegetationCover)vegetationKinds[index];
                        if (!IsGeneratedBuildable(hydrologyKinds[index], elevationLevels[index], surface) ||
                            occupied.Contains(position)) continue;
                        var selection = random.NextUInt() % 4;
                        var resourceKind = vegetation == VegetationCover.Forest ? "construction" : surface switch
                        {
                            SurfaceKind.Sand => selection == 0 ? "fiber" : "stone",
                            SurfaceKind.Snow => selection == 0 ? "food" : "stone",
                            _ => selection switch
                            {
                                0 => "fertile_land",
                                1 => "food",
                                2 => "fiber",
                                _ => "seed",
                            },
                        };
                        var renewable = resourceKind is "construction" or "food" or "fiber" or "seed";
                        var id = site == 0 ? $"wild-{left}-{top}" : $"wild-{left}-{top}-{site}";
                        var tile = geography.At(x, y);
                        sites.Add(new MapResource(id, resourceKind, position, renewable,
                            resourceKind == "construction" ? TreeKindFor(tile.Climate) : null,
                            NaturalObjectFor(resourceKind, tile, x, y, geography, options.WrapEastWest)));
                        occupied.Add(position);
                        break;
                    }
                }
            }
        return sites;
    }

    private static List<MapResource> GenerateTrees(GeographyOptions options, GeneratedGeography geography,
        byte[] vegetationKinds,
        int width, int height, IReadOnlyList<CampObject> camp, IReadOnlyList<MapResource> existing)
    {
        // Bounded, individually harvestable trees share the chunk resource
        // budget. They are objects, not a second meaning of forest ground.
        const int cellSize = 8;
        const int maximumGeneratedTreesPerChunk = 32;
        var occupied = camp.Select(item => item.Position)
            .Concat(existing.Select(item => item.Position)).ToHashSet();
        var perChunk = existing.GroupBy(item =>
                (item.Position.X / GeographyGenerator.ChunkSize,
                    item.Position.Y / GeographyGenerator.ChunkSize))
            .ToDictionary(group => group.Key, group => group.Count());
        var treesPerChunk = new Dictionary<(int, int), int>();
        var trees = new List<MapResource>();
        for (var top = 0; top < height; top += cellSize)
            for (var left = 0; left < width; left += cellSize)
            {
                var chunk = (left / GeographyGenerator.ChunkSize, top / GeographyGenerator.ChunkSize);
                if (perChunk.GetValueOrDefault(chunk) >= WorldSystemsConfig.Default.MaxResourcesPerChunk ||
                    treesPerChunk.GetValueOrDefault(chunk) >= maximumGeneratedTreesPerChunk)
                    continue;
                var random = Pcg32XshRrV1.Create(options.Seed, $"tree:{left},{top}");
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var x = left + (int)(random.NextUInt() % (uint)Math.Min(cellSize, width - left));
                    var y = top + (int)(random.NextUInt() % (uint)Math.Min(cellSize, height - top));
                    var position = new GridPoint(x, y);
                    var vegetation = (VegetationCover)vegetationKinds[y * width + x];
                    if (occupied.Contains(position) || vegetation is not (VegetationCover.Forest or VegetationCover.Grass))
                        continue;
                    // Meadows carry scattered trees; forest cover remains denser.
                    if (vegetation == VegetationCover.Grass && random.NextUInt() % 4 != 0)
                        continue;
                    var climate = geography.At(x, y).Climate;
                    var treeKind = climate == ClimateZone.Cold ||
                        (climate == ClimateZone.Temperate && random.NextUInt() % 3 == 0)
                            ? "conifer" : "broadleaf";
                    trees.Add(new MapResource($"tree-{left}-{top}", "construction", position, true, treeKind));
                    occupied.Add(position);
                    perChunk[chunk] = perChunk.GetValueOrDefault(chunk) + 1;
                    treesPerChunk[chunk] = treesPerChunk.GetValueOrDefault(chunk) + 1;
                    break;
                }
            }
        return trees;
    }

    private static string TreeKindFor(ClimateZone climate) =>
        climate == ClimateZone.Cold ? "conifer" : "broadleaf";

    private static string? NaturalObjectFor(string resourceKind, GeographyTile tile, int x, int y,
        GeneratedGeography geography, bool wrap) => resourceKind switch
        {
            "food" => (unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ tile.Rainfall) & 1) == 0
                ? "berry_bush" : "wild_greens",
            "fiber" => IsAdjacentToWater(geography, x, y, wrap) ? "reeds" : "fiber_plant",
            "stone" => "stone_outcrop",
            "seed" => "wild_seed_patch",
            "fertile_land" => "fertile_soil",
            _ => null,
        };

    private static List<MapResource> GenerateOrchards(GeographyOptions options, GeneratedGeography geography,
        byte[] surfaceKinds, byte[] vegetationKinds, int width, int height, IReadOnlyList<CampObject> camp,
        IReadOnlyList<MapResource> existing)
    {
        // One generic fruit tree per suitable chunk, after woodland trees have
        // been placed. Species, density and seasonality are not final content.
        var occupied = camp.Select(item => item.Position)
            .Concat(existing.Select(item => item.Position)).ToHashSet();
        var perChunk = existing.GroupBy(item =>
                (item.Position.X / GeographyGenerator.ChunkSize,
                    item.Position.Y / GeographyGenerator.ChunkSize))
            .ToDictionary(group => group.Key, group => group.Count());
        var orchards = new List<MapResource>();
        for (var top = 0; top < height; top += GeographyGenerator.ChunkSize)
            for (var left = 0; left < width; left += GeographyGenerator.ChunkSize)
            {
                var chunk = (left / GeographyGenerator.ChunkSize, top / GeographyGenerator.ChunkSize);
                if (perChunk.GetValueOrDefault(chunk) >= WorldSystemsConfig.Default.MaxResourcesPerChunk)
                    continue;
                var random = Pcg32XshRrV1.Create(options.Seed, $"orchard:{left},{top}");
                for (var attempt = 0; attempt < 96; attempt++)
                {
                    var x = left + (int)(random.NextUInt() %
                        (uint)Math.Min(GeographyGenerator.ChunkSize, width - left));
                    var y = top + (int)(random.NextUInt() %
                        (uint)Math.Min(GeographyGenerator.ChunkSize, height - top));
                    var position = new GridPoint(x, y);
                    var index = y * width + x;
                    if (surfaceKinds[index] != (byte)SurfaceKind.Grass ||
                        vegetationKinds[index] != (byte)VegetationCover.Grass || occupied.Contains(position) ||
                        geography.At(x, y).Climate is not (ClimateZone.Temperate or ClimateZone.Tropical))
                        continue;
                    orchards.Add(new MapResource($"orchard-{left}-{top}", "fruit", position, true, "orchard"));
                    occupied.Add(position);
                    break;
                }
            }
        return orchards;
    }

    private static List<MapResource> GenerateGeologySites(GeographyOptions options,
        GeneratedGeography geography, IReadOnlyList<CampObject> camp, IReadOnlyList<MapResource> existing)
    {
        var columns = (geography.Width + GeographyGenerator.ChunkSize - 1) / GeographyGenerator.ChunkSize;
        var rows = (geography.Height + GeographyGenerator.ChunkSize - 1) / GeographyGenerator.ChunkSize;
        var chunkCount = columns * rows;
        var occupied = camp.Select(item => item.Position)
            .Concat(existing.Select(item => item.Position)).ToHashSet();
        var resourcesByChunk = existing.GroupBy(item =>
                (item.Position.X / GeographyGenerator.ChunkSize, item.Position.Y / GeographyGenerator.ChunkSize))
            .ToDictionary(group => group.Key, group => group.Count());
        var additions = new List<MapResource>();
        var count = options.ResourceAbundance == ResourceAbundance.Abundant ? 2 : 1;

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunkX = chunkIndex % columns;
            var chunkY = chunkIndex / columns;
            if (options.ResourceAbundance == ResourceAbundance.Sparse && (chunkX + chunkY) % 2 != 0)
                continue;
            AddGeologySite("stone_outcrop", "stone", (tile, x, y) =>
                    tile.Water == WaterKind.Land && tile.Elevation is >= 205 and < SeededMap.PeakElevationThreshold,
                chunkX, chunkY, chunkIndex);
        }

        foreach (var (naturalKind, resourceKind) in new[]
        {
            ("iron_outcrop", "iron_ore"),
            ("gold_outcrop", "gold_ore"),
            ("diamond_outcrop", "diamond"),
        })
        {
            var random = Pcg32XshRrV1.Create(options.Seed, "natural-deposit-order:" + naturalKind);
            var startChunk = (int)(random.NextUInt() % (uint)chunkCount);
            for (var site = 0; site < count; site++)
            {
                for (var offset = 0; offset < chunkCount; offset++)
                {
                    var chunkIndex = (startChunk + offset + site) % chunkCount;
                    var chunkX = chunkIndex % columns;
                    var chunkY = chunkIndex / columns;
                    if (TryGeologySite(naturalKind, resourceKind,
                        (tile, _, _) => tile.Water == WaterKind.Land &&
                            tile.Elevation is >= 215 and < SeededMap.PeakElevationThreshold,
                        chunkX, chunkY, chunkIndex))
                        break;
                }
            }
        }

        for (var site = 0; site < count; site++)
        {
            var random = Pcg32XshRrV1.Create(options.Seed, $"natural-deposit-order:clay:{site}");
            var startChunk = (int)(random.NextUInt() % (uint)chunkCount);
            for (var offset = 0; offset < chunkCount; offset++)
            {
                var chunkIndex = (startChunk + offset + site) % chunkCount;
                var chunkX = chunkIndex % columns;
                var chunkY = chunkIndex / columns;
                if (TryGeologySite("clay_bank", "clay", (tile, x, y) =>
                        tile.Water == WaterKind.Land && tile.Elevation < 185 &&
                        IsAdjacentToWater(geography, x, y, options.WrapEastWest),
                    chunkX, chunkY, chunkIndex))
                    break;
            }
        }
        return additions;

        void AddGeologySite(string naturalKind, string resourceKind,
            Func<GeographyTile, int, int, bool> suitable, int chunkX, int chunkY, int generationIndex) =>
            TryGeologySite(naturalKind, resourceKind, suitable, chunkX, chunkY, generationIndex);

        bool TryGeologySite(string naturalKind, string resourceKind,
            Func<GeographyTile, int, int, bool> suitable, int chunkX, int chunkY, int generationIndex)
        {
            var chunk = (chunkX, chunkY);
            if (resourcesByChunk.GetValueOrDefault(chunk) >= WorldSystemsConfig.Default.MaxResourcesPerChunk)
                return false;
            var left = chunkX * GeographyGenerator.ChunkSize;
            var top = chunkY * GeographyGenerator.ChunkSize;
            var width = Math.Min(GeographyGenerator.ChunkSize, geography.Width - left);
            var height = Math.Min(GeographyGenerator.ChunkSize, geography.Height - top);
            var random = Pcg32XshRrV1.Create(options.Seed,
                $"natural-site:{naturalKind}:{generationIndex}:{chunkX},{chunkY}");
            for (var attempt = 0; attempt < 128; attempt++)
            {
                var x = left + (int)(random.NextUInt() % (uint)width);
                var y = top + (int)(random.NextUInt() % (uint)height);
                var position = new GridPoint(x, y);
                if (occupied.Contains(position) || !suitable(geography.At(x, y), x, y)) continue;
                additions.Add(new MapResource($"geology-{naturalKind}-{x}-{y}", resourceKind,
                    position, false, NaturalObjectKind: naturalKind));
                occupied.Add(position);
                resourcesByChunk[chunk] = resourcesByChunk.GetValueOrDefault(chunk) + 1;
                return true;
            }

            var start = (int)(random.NextUInt() % (uint)(width * height));
            for (var offset = 0; offset < width * height; offset++)
            {
                var local = (start + offset) % (width * height);
                var x = left + local % width;
                var y = top + local / width;
                var position = new GridPoint(x, y);
                if (occupied.Contains(position) || !suitable(geography.At(x, y), x, y)) continue;
                additions.Add(new MapResource($"geology-{naturalKind}-{x}-{y}", resourceKind,
                    position, false, NaturalObjectKind: naturalKind));
                occupied.Add(position);
                resourcesByChunk[chunk] = resourcesByChunk.GetValueOrDefault(chunk) + 1;
                return true;
            }
            return false;
        }
    }

    private static GridPoint FindCampOrigin(byte[] hydrologyKinds, byte[] elevationLevels,
        byte[] surfaceKinds, int width, int height)
    {
        var centerX = (width - CampWidth) / 2;
        var centerY = (height - CampHeight) / 2;
        var maxRadius = width + height;
        for (var radius = 0; radius <= maxRadius; radius++)
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var offsetY = radius - Math.Abs(offsetX);
                if (TrySite(centerX + offsetX, centerY + offsetY, hydrologyKinds, elevationLevels,
                    surfaceKinds, width, height))
                    return new GridPoint(centerX + offsetX, centerY + offsetY);
                if (offsetY > 0 && TrySite(centerX + offsetX, centerY - offsetY, hydrologyKinds,
                    elevationLevels, surfaceKinds, width, height))
                    return new GridPoint(centerX + offsetX, centerY - offsetY);
            }
        throw new InvalidOperationException("The generated geography has no suitable base-camp clearing.");
    }

    private static bool TrySite(int left, int top, byte[] hydrologyKinds, byte[] elevationLevels,
        byte[] surfaceKinds, int width, int height)
    {
        if (left < 0 || top < 0 || left + CampWidth > width || top + CampHeight > height)
            return false;
        if (left / GeographyGenerator.ChunkSize != (left + CampWidth - 1) / GeographyGenerator.ChunkSize ||
            top / GeographyGenerator.ChunkSize != (top + CampHeight - 1) / GeographyGenerator.ChunkSize)
            return false;
        for (var y = top; y < top + CampHeight; y++)
            for (var x = left; x < left + CampWidth; x++)
            {
                var index = y * width + x;
                if (!IsGeneratedBuildable(hydrologyKinds[index], elevationLevels[index],
                    (SurfaceKind)surfaceKinds[index])) return false;
            }
        return true;
    }

    private static bool IsGeneratedBuildable(byte hydrology, byte elevation, SurfaceKind surface) =>
        hydrology == (byte)WaterKind.Land && elevation < SeededMap.MountainElevationThreshold &&
        surface is SurfaceKind.Grass or SurfaceKind.Sand or SurfaceKind.Snow or
            SurfaceKind.ForestFloor or SurfaceKind.DryScrub or SurfaceKind.FertileSoil;

}

/// <summary>Canonical digest for the independent generated-map layer bytes.</summary>
public static class MapLayerManifestCodec
{
    private const string Header = "clankerworld.map-layers/v1";

    public static string? Digest(SeededMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.ClimateZones is not { } climate || map.ElevationLevels is not { } elevation ||
            map.HydrologyKinds is not { } hydrology || map.SurfaceKinds is not { } surface ||
            map.VegetationKinds is not { } vegetation)
            return null;

        var expectedLength = checked(map.Width * map.Height);
        if (climate.Length != expectedLength || elevation.Length != expectedLength ||
            hydrology.Length != expectedLength || surface.Length != expectedLength ||
            vegetation.Length != expectedLength)
            throw new InvalidDataException("The private-world map layers do not match their dimensions.");

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Header);
            writer.Write(map.Width);
            writer.Write(map.Height);
            foreach (var layer in new[] { climate, elevation, hydrology, surface, vegetation })
            {
                writer.Write(layer.Length);
                writer.Write(layer);
            }
        }
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))))
            .ToLowerInvariant();
    }
}

/// <summary>
/// Explicit canonical bytes for the genesis map manifest. The digest is not
/// included in its own input, avoiding self-referential serialization.
/// </summary>
public static class MapManifestCodec
{
    private const string Header = "clankerworld.seeded-map/v1";

    public static byte[] Encode(SeededMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        builder.Append("dimensions=").Append(map.Width).Append('x').Append(map.Height).Append('\n');
        builder.Append("generation_attempt=").Append(map.GenerationAttempt).Append('\n');
        if (map.ClimateZones is not null)
            builder.Append("climate-zones=").Append(Convert.ToBase64String(map.ClimateZones)).Append('\n');
        foreach (var tile in map.Tiles.OrderBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X))
        {
            builder.Append("tile=")
                .Append(tile.Position.X).Append(',').Append(tile.Position.Y).Append('|')
                .Append(ToWireValue(tile.Terrain)).Append('\n');
        }

        foreach (var mapObject in map.CampObjects.OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal))
        {
            builder.Append("object=")
                .Append(mapObject.Id).Append('|').Append(mapObject.Kind).Append('|')
                .Append(mapObject.Position.X).Append(',').Append(mapObject.Position.Y).Append('\n');
        }

        foreach (var resource in map.Resources.OrderBy(resource => resource.Id, StringComparer.Ordinal))
        {
            builder.Append("resource=")
                .Append(resource.Id).Append('|').Append(resource.Kind).Append('|')
                .Append(resource.Position.X).Append(',').Append(resource.Position.Y).Append('|')
                .Append(resource.IsRenewable ? "renewable" : "finite");
            if (resource.TreeKind is { } treeKind)
                builder.Append("|tree:").Append(treeKind);
            if (resource.NaturalObjectKind is { } naturalObjectKind)
                builder.Append("|natural:").Append(naturalObjectKind);
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static string Digest(SeededMap map) =>
        Convert.ToHexStringLower(SHA256.HashData(Encode(map)));

    private static string ToWireValue(TerrainKind terrain) => terrain switch
    {
        TerrainKind.Meadow => "meadow",
        TerrainKind.Water => "water",
        TerrainKind.Mountain => "mountain",
        TerrainKind.River => "river",
        TerrainKind.Lake => "lake",
        TerrainKind.Ocean => "ocean",
        TerrainKind.Peak => "peak",
        TerrainKind.Sand => "sand",
        TerrainKind.Forest => "forest",
        TerrainKind.Snow => "snow",
        _ => throw new ArgumentOutOfRangeException(nameof(terrain)),
    };
}

public sealed record MapValidationResult(bool IsValid, string? Failure)
{
    public static MapValidationResult Valid { get; } = new(true, null);

    public static MapValidationResult Invalid(string failure) => new(false, failure);
}

/// <summary>
/// The first-world generated-map acceptance checks used by the seed corpus.
/// </summary>
public static class MapAcceptance
{
    public static MapValidationResult Validate(SeededMap map, bool allowEmptyCamp = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Width <= 0 || map.Height <= 0 || map.GenerationAttempt < 0 ||
            map.GenerationAttempt >= SeededMapGenerator.MaximumAttempts)
        {
            return MapValidationResult.Invalid("The map dimensions or generation attempt are invalid.");
        }

        if (map.Tiles.Count != map.Width * map.Height ||
            map.Tiles.Select(tile => tile.Position).Distinct().Count() != map.Tiles.Count ||
            map.Tiles.Any(tile => !map.Contains(tile.Position)))
        {
            return MapValidationResult.Invalid("The logical grid is not a complete bounded rectangle.");
        }

        if (map.ClimateZones is { } zones &&
            (zones.Length != map.Width * map.Height || zones.Any(zone => !Enum.IsDefined((ClimateZone)zone))))
            return MapValidationResult.Invalid("The climate layer is invalid.");
        if (map.ElevationLevels is { } elevation && elevation.Length != map.Width * map.Height ||
            map.HydrologyKinds is { } hydrology &&
                (hydrology.Length != map.Width * map.Height || hydrology.Any(value => !Enum.IsDefined((WaterKind)value))) ||
            map.SurfaceKinds is { } surfaces &&
                (surfaces.Length != map.Width * map.Height || surfaces.Any(value => !Enum.IsDefined((SurfaceKind)value))) ||
            map.VegetationKinds is { } vegetation &&
                (vegetation.Length != map.Width * map.Height || vegetation.Any(value => !Enum.IsDefined((VegetationCover)value))))
            return MapValidationResult.Invalid("A generated geography layer is invalid.");

        var founder = map.CampObjects.SingleOrDefault(mapObject =>
            string.Equals(mapObject.Kind, "founder", StringComparison.Ordinal));
        if (!allowEmptyCamp && (founder is null || !map.IsBuildable(founder.Position)))
        {
            return MapValidationResult.Invalid("The founder must occupy passable ground.");
        }
        if (allowEmptyCamp && founder is not null)
            return MapValidationResult.Invalid("An empty base camp cannot contain a founder marker.");

        var requiredKinds = new[] { "shelter", "storage", "cooking" };
        if ((!allowEmptyCamp || map.CampObjects.Count > 0) && requiredKinds.Any(kind => !map.CampObjects.Any(mapObject =>
                string.Equals(mapObject.Kind, kind, StringComparison.Ordinal))))
        {
            return MapValidationResult.Invalid("A required camp-start placement is missing.");
        }

        if (map.CampObjects.Select(mapObject => mapObject.Id).Distinct(StringComparer.Ordinal).Count() != map.CampObjects.Count ||
            map.CampObjects.Select(mapObject => mapObject.Position).Distinct().Count() != map.CampObjects.Count ||
            map.CampObjects.Any(mapObject => !map.IsBuildable(mapObject.Position)))
        {
            return MapValidationResult.Invalid("Camp-start objects are duplicated or overlap impassable terrain.");
        }

        if (map.Resources.Select(resource => resource.Id).Distinct(StringComparer.Ordinal).Count() != map.Resources.Count ||
            map.Resources.Any(resource => !IsValidResourcePosition(map, resource) ||
                (resource.TreeKind is not null &&
                    resource.TreeKind switch
                    {
                        "broadleaf" or "conifer" => resource.Kind != "construction",
                        "orchard" => resource.Kind != "fruit",
                        _ => true,
                    }) ||
                resource.NaturalObjectKind is { } naturalObjectKind &&
                    !IsValidNaturalObjectKind(resource.Kind, resource.TreeKind, naturalObjectKind)) ||
            map.Resources.Where(resource => resource.TreeKind is not null || resource.NaturalObjectKind is not null)
                .GroupBy(resource => resource.Position).Any(group => group.Count() > 1))
        {
            return MapValidationResult.Invalid("Resource placements are invalid.");
        }

        if (!map.Resources.Any(resource => resource.IsRenewable &&
                string.Equals(resource.Kind, "food", StringComparison.Ordinal)) ||
            !map.Resources.Any(resource => string.Equals(resource.Kind, "construction", StringComparison.Ordinal)) ||
            !map.Resources.Any(resource => string.Equals(resource.Id, SeededMapGenerator.FertileLandResourceId, StringComparison.Ordinal) &&
                string.Equals(resource.Kind, "fertile_land", StringComparison.Ordinal)))
        {
            return MapValidationResult.Invalid("Reachable food, construction, or fertile-land resources are missing.");
        }

        var startingPoint = founder?.Position ??
            map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            map.Resources.FirstOrDefault(item => item.Id == "berry-patch")?.Position;
        if (startingPoint is null)
            return MapValidationResult.Invalid("The starting area has no reachable food resource.");
        var reachable = ReachableFrom(map, startingPoint.Value);
        var starterResources = allowEmptyCamp
            ? map.Resources.Where(resource => resource.Id is "berry-patch" or "timber-tree" or "fertile-land")
            : map.Resources;
        if (map.CampObjects.Any(mapObject => !reachable.Contains(mapObject.Position)) ||
            starterResources.Any(resource => !reachable.Contains(resource.Position)))
        {
            return MapValidationResult.Invalid("A required camp-start route crosses an impassable boundary.");
        }

        if (!string.Equals(MapManifestCodec.Digest(map), map.ManifestDigest, StringComparison.Ordinal))
        {
            return MapValidationResult.Invalid("The manifest digest does not match the selected map.");
        }

        return MapValidationResult.Valid;
    }

    private static bool IsValidResourcePosition(SeededMap map, MapResource resource) =>
        resource.NaturalObjectKind is "stone_outcrop" or "iron_outcrop" or "gold_outcrop" or "diamond_outcrop"
            ? map.IsPassable(resource.Position)
            : map.IsBuildable(resource.Position);

    private static bool IsValidNaturalObjectKind(string resourceKind, string? treeKind, string naturalObjectKind) =>
        treeKind is null && naturalObjectKind switch
        {
            "berry_bush" or "wild_greens" => resourceKind == "food",
            "fiber_plant" or "reeds" => resourceKind == "fiber",
            "stone_outcrop" => resourceKind == "stone",
            "iron_outcrop" => resourceKind == "iron_ore",
            "gold_outcrop" => resourceKind == "gold_ore",
            "diamond_outcrop" => resourceKind == "diamond",
            "clay_bank" => resourceKind == "clay",
            "wild_seed_patch" => resourceKind == "seed",
            "fertile_soil" => resourceKind == "fertile_land",
            _ => false,
        };

    internal static HashSet<GridPoint> ReachableFrom(SeededMap map, GridPoint origin)
    {
        var visited = new HashSet<GridPoint> { origin };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(origin);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in map.FootNeighbors(current))
            {
                if (map.IsPassable(next) && visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }

}

/// <summary>
/// Eight-direction A* with deterministic cardinal/diagonal costs and queue ordering.
/// </summary>
public static class DeterministicRouteFinder
{
    public static IReadOnlyList<GridPoint> Find(SeededMap map, GridPoint origin, GridPoint destination)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsPassable(origin) || !map.IsPassable(destination))
        {
            throw new InvalidOperationException("Routing endpoints must be passable.");
        }

        if (TryFind(map, origin, destination, out var route)) return route;
        throw new InvalidOperationException("No passable route exists between the requested points.");
    }

    public static bool TryFind(SeededMap map, GridPoint origin, GridPoint destination,
        out IReadOnlyList<GridPoint> route)
    {
        ArgumentNullException.ThrowIfNull(map);
        route = [];
        if (!map.IsPassable(origin) || !map.IsPassable(destination)) return false;

        var open = new PriorityQueue<RouteNode, RoutePriority>();
        var predecessor = new Dictionary<GridPoint, GridPoint>();
        var best = new Dictionary<GridPoint, RouteRecord>();
        var nodeId = 0;
        var start = new RouteNode(origin, origin, 0, nodeId++);
        open.Enqueue(start, ToPriority(map, start, destination));
        best.Add(origin, new RouteRecord(0, origin));

        while (open.TryDequeue(out var node, out _))
        {
            var record = best[node.Position];
            if (node.G != record.Cost || node.Predecessor != record.Predecessor)
            {
                continue;
            }

            if (node.Position == destination)
            {
                route = Reconstruct(predecessor, origin, destination);
                return true;
            }

            foreach (var next in map.FootNeighbors(node.Position))
            {
                if (!map.IsPassable(next))
                {
                    continue;
                }

                var candidate = new RouteRecord(checked(node.G + map.FootStepCost(node.Position, next)), node.Position);
                if (best.TryGetValue(next, out var old) && Compare(candidate, old) >= 0)
                {
                    continue;
                }

                best[next] = candidate;
                predecessor[next] = node.Position;
                var nextNode = new RouteNode(next, node.Position, candidate.Cost, nodeId++);
                open.Enqueue(nextNode, ToPriority(map, nextNode, destination));
            }
        }

        return false;
    }

    private static int Compare(RouteRecord left, RouteRecord right)
    {
        var result = left.Cost.CompareTo(right.Cost);
        if (result != 0)
        {
            return result;
        }

        result = left.Predecessor.Y.CompareTo(right.Predecessor.Y);
        return result != 0 ? result : left.Predecessor.X.CompareTo(right.Predecessor.X);
    }

    private static List<GridPoint> Reconstruct(
        Dictionary<GridPoint, GridPoint> predecessor,
        GridPoint origin,
        GridPoint destination)
    {
        var route = new List<GridPoint> { destination };
        var current = destination;
        while (current != origin)
        {
            current = predecessor[current];
            route.Add(current);
        }

        route.Reverse();
        return route;
    }

    private static RoutePriority ToPriority(SeededMap map, RouteNode node, GridPoint destination)
    {
        var heuristic = map.FootRouteHeuristicCost(node.Position, destination);
        return new RoutePriority(
            checked(node.G + heuristic),
            heuristic,
            node.G,
            node.Position.Y,
            node.Position.X,
            node.Predecessor.Y,
            node.Predecessor.X,
            node.NodeId);
    }

    private sealed record RouteNode(GridPoint Position, GridPoint Predecessor, int G, int NodeId);

    private readonly record struct RouteRecord(int Cost, GridPoint Predecessor);

    private readonly record struct RoutePriority(
        int F,
        int H,
        int G,
        int Y,
        int X,
        int PredecessorY,
        int PredecessorX,
        int NodeId) : IComparable<RoutePriority>
    {
        public int CompareTo(RoutePriority other)
        {
            var result = F.CompareTo(other.F);
            result = result != 0 ? result : H.CompareTo(other.H);
            result = result != 0 ? result : G.CompareTo(other.G);
            result = result != 0 ? result : Y.CompareTo(other.Y);
            result = result != 0 ? result : X.CompareTo(other.X);
            result = result != 0 ? result : PredecessorY.CompareTo(other.PredecessorY);
            result = result != 0 ? result : PredecessorX.CompareTo(other.PredecessorX);
            return result != 0 ? result : NodeId.CompareTo(other.NodeId);
        }
    }
}

public sealed record HarnessActor(
    string Id,
    GridPoint Position,
    int HungerBasisPoints,
    int FoodItems,
    int WoodItems);

public sealed record RuntimeResource(string Id, ResourceState State);

/// <summary>
/// The complete small state for the #88 fixture. The map manifest stays
/// immutable; resource availability and the actor live in the tick state.
/// </summary>
public sealed record HarnessWorld(
    WorldIdentity Identity,
    SeededMap Map,
    HarnessActor Actor,
    IReadOnlyList<RuntimeResource> Resources,
    IReadOnlyList<PersistenceEvent> Events)
{
    public RuntimeResource GetResource(string id) =>
        Resources.Single(resource => string.Equals(resource.Id, id, StringComparison.Ordinal));
}

/// <summary>
/// One deliberately small ordered kernel path: needs first, then exactly one
/// movement/work action, then a durable event for the completed tick.
/// </summary>
public static class ScriptedHarness
{
    private const int NeedDrainPerTick = 100;
    private const int FoodRecovery = 2_000;
    private const string ActorId = "actor-scout";

    public static HarnessWorld CreateGenesis(string worldSeed)
    {
        var map = SeededMapGenerator.Generate(worldSeed);
        var identity = CreateIdentity(worldSeed, map);
        return CreateGenesis(identity);
    }

    public static HarnessWorld CreateGenesis(WorldIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var map = SeededMapGenerator.Generate(identity.WorldSeed);
        if (map.GenerationAttempt != identity.GenerationAttempt ||
            !string.Equals(map.ManifestDigest, identity.InitialMapManifestDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved map identity does not reproduce the selected manifest.");
        }

        var founder = map.GetObject("founder-scout");
        var actor = new HarnessActor(ActorId, founder.Position, 5_000, 0, 0);
        var resources = map.Resources
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => new RuntimeResource(resource.Id, ResourceState.Available))
            .ToArray();
        return new HarnessWorld(identity with { WorldTick = 0 }, map, actor, resources, []);
    }

    public static HarnessWorld RunToFoodConsumed(HarnessWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var berry = world.Map.GetResource("berry-patch");
        var current = MoveUntilAt(world, berry.Position);
        current = Harvest(current, berry.Id);
        return Consume(current);
    }

    public static HarnessWorld RunEntireSequence(string worldSeed) =>
        RunToFoodConsumed(CreateGenesis(worldSeed));

    /// <summary>
    /// Advances exactly one committed action from the small scripted fixture.
    /// This lets a host own the world clock without teaching a client how to
    /// mutate or replay simulation state.
    /// </summary>
    public static bool TryAdvanceOneAction(HarnessWorld world, out HarnessWorld advanced)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.Actor.FoodItems > 0)
        {
            advanced = Consume(world);
            return true;
        }

        var berry = world.Map.GetResource("berry-patch");
        if (world.GetResource(berry.Id).State == ResourceState.Available)
        {
            advanced = world.Actor.Position == berry.Position
                ? Harvest(world, berry.Id)
                : MoveOneStep(world, berry.Position);
            return true;
        }

        advanced = world;
        return false;
    }

    /// <summary>
    /// Phase 3 executor entry points. They reuse the fixture's needs/resource
    /// commit path while allowing cognition to select the action instead of a
    /// hard-coded script selecting it.
    /// </summary>
    public static HarnessWorld ApplyMovement(HarnessWorld world, GridPoint destination)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Actor.Position == destination)
        {
            throw new InvalidOperationException("A Phase 3 movement action must advance to a different tile.");
        }

        return Commit(
            world,
            actor => actor with { Position = destination },
            resources => resources,
            1,
            $"move:{ActorId}:{destination.X},{destination.Y}");
    }

    public static HarnessWorld ApplyHarvest(HarnessWorld world, string resourceId)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Harvest(world, resourceId);
    }

    public static HarnessWorld ApplyConsume(HarnessWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Consume(world);
    }

    public static HarnessWorld ApplyIdle(HarnessWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Commit(
            world,
            actor => actor,
            resources => resources,
            0,
            $"idle:{ActorId}");
    }

    public static HarnessWorld ReplayFromGenesis(WorldIdentity identity, IReadOnlyList<PersistenceEvent> events)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(events);
        var replay = CreateGenesis(identity with { WorldTick = 0 });
        foreach (var expected in events.OrderBy(worldEvent => worldEvent.EventId))
        {
            replay = ReplayOne(replay, expected);
        }

        return replay;
    }

    private static HarnessWorld ReplayOne(HarnessWorld world, PersistenceEvent expected)
    {
        if (expected.Detail is null)
        {
            throw new InvalidDataException("Harness events must include an action detail.");
        }

        HarnessWorld replayed;
        if (expected.Detail.StartsWith("move:", StringComparison.Ordinal))
        {
            var components = expected.Detail.Split(':', StringSplitOptions.None);
            if (components.Length != 3 || !string.Equals(components[1], ActorId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The move event detail is invalid.");
            }

            replayed = MoveOneStep(world, ParsePoint(components[2]));
        }
        else if (expected.Detail.StartsWith("harvest:", StringComparison.Ordinal))
        {
            replayed = Harvest(world, expected.Detail["harvest:".Length..]);
        }
        else if (string.Equals(expected.Detail, $"consume:{ActorId}", StringComparison.Ordinal))
        {
            replayed = Consume(world);
        }
        else
        {
            throw new InvalidDataException("The harness event action is not recognized.");
        }

        var actual = replayed.Events[^1];
        if (actual != expected)
        {
            throw new InvalidDataException("Replaying the action log produced a different committed event.");
        }

        return replayed;
    }

    private static HarnessWorld MoveUntilAt(HarnessWorld world, GridPoint destination)
    {
        var current = world;
        while (current.Actor.Position != destination)
        {
            current = MoveOneStep(current, destination);
        }

        return current;
    }

    private static HarnessWorld MoveOneStep(HarnessWorld world, GridPoint destination)
    {
        var route = DeterministicRouteFinder.Find(world.Map, world.Actor.Position, destination);
        if (route.Count < 2)
        {
            throw new InvalidOperationException("A movement event must advance to a different tile.");
        }

        var next = route[1];
        return Commit(
            world,
            actor => actor with { Position = next },
            resources => resources,
            1,
            $"move:{ActorId}:{next.X},{next.Y}");
    }

    private static HarnessWorld Harvest(HarnessWorld world, string resourceId)
    {
        var source = world.Map.GetResource(resourceId);
        if (world.Actor.Position != source.Position || world.GetResource(resourceId).State != ResourceState.Available)
        {
            throw new InvalidOperationException("Harvesting requires an available source at the actor's position.");
        }

        return Commit(
            world,
            actor => source.Kind switch
            {
                "food" => actor with { FoodItems = checked(actor.FoodItems + 1) },
                "construction" => actor with { WoodItems = checked(actor.WoodItems + 1) },
                _ => throw new InvalidOperationException("The resource kind is not harvestable by this fixture."),
            },
            resources => resources
                .Select(resource => string.Equals(resource.Id, resourceId, StringComparison.Ordinal)
                    ? resource with { State = ResourceState.Depleted }
                    : resource)
                .ToArray(),
            2,
            $"harvest:{resourceId}");
    }

    private static HarnessWorld Consume(HarnessWorld world)
    {
        if (world.Actor.FoodItems <= 0)
        {
            throw new InvalidOperationException("Consuming requires an available food item.");
        }

        return Commit(
            world,
            actor => actor with
            {
                FoodItems = actor.FoodItems - 1,
                HungerBasisPoints = ClampBasisPoints(actor.HungerBasisPoints + FoodRecovery),
            },
            resources => resources,
            3,
            $"consume:{ActorId}");
    }

    private static HarnessWorld Commit(
        HarnessWorld world,
        Func<HarnessActor, HarnessActor> action,
        Func<IReadOnlyList<RuntimeResource>, IReadOnlyList<RuntimeResource>> resourceAction,
        int counterDelta,
        string detail)
    {
        var nextTick = checked(world.Identity.WorldTick + 1);
        var needsApplied = world.Actor with
        {
            HungerBasisPoints = ClampBasisPoints(world.Actor.HungerBasisPoints - NeedDrainPerTick),
        };
        var actor = action(needsApplied);
        var worldEvent = new PersistenceEvent(
            checked(world.Events.Count + 1L),
            nextTick,
            PersistenceEventKind.CounterAdjusted,
            counterDelta,
            null,
            detail);
        return world with
        {
            Identity = world.Identity with { WorldTick = nextTick },
            Actor = actor,
            Resources = resourceAction(world.Resources),
            Events = world.Events.Append(worldEvent).ToArray(),
        };
    }

    private static WorldIdentity CreateIdentity(string worldSeed, SeededMap map) => new(
        $"harness-{worldSeed}",
        "deterministic-kernel-contract/phase-1",
        "phase-1-harness/v1",
        "1",
        "one-tick-per-minute/v1",
        0,
        worldSeed,
        SeededMapGenerator.GeneratorId,
        SeededMapGenerator.GeneratorVersion,
        SeededMapGenerator.GeneratorConfigDigest,
        map.GenerationAttempt,
        map.ManifestDigest,
        "content-lock/empty-v1",
        "asset-lock/empty-v1");

    private static int ClampBasisPoints(int value) => Math.Clamp(value, 0, 10_000);

    private static GridPoint ParsePoint(string value)
    {
        var parts = value.Split(',', StringSplitOptions.None);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y))
        {
            throw new InvalidDataException("A grid coordinate must use invariant x,y integers.");
        }

        return new GridPoint(x, y);
    }
}

/// <summary>
/// Persists #88 through the #87 envelope. The payload is canonical state inside
/// the existing snapshot; the outer event log remains the append-only authority.
/// </summary>
public static class HarnessPersistence
{
    public static PersistedWorld Save(HarnessWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new PersistedWorld(
            CanonicalPersistenceCodec.EncodeSnapshot(new WorldSnapshot(ToMiniatureState(world))),
            CanonicalPersistenceCodec.EncodeEventLog(world.Events));
    }

    public static HarnessWorld Load(PersistedWorld save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var snapshot = CanonicalPersistenceCodec.DecodeSnapshot(save.SnapshotBytes);
        var events = CanonicalPersistenceCodec.DecodeEventLog(save.EventLogBytes);
        var genericReplay = WorldReplay.ReplayGenesis(
            snapshot.State.Identity with { WorldTick = 0 },
            events);
        if (genericReplay.Counter != snapshot.State.Counter ||
            genericReplay.LastEventId != snapshot.State.LastEventId ||
            genericReplay.Identity.WorldTick != snapshot.State.Identity.WorldTick)
        {
            throw new InvalidDataException("The snapshot does not agree with its ordered event suffix.");
        }

        var decoded = HarnessStateCodec.Decode(snapshot.State.Identity, snapshot.State.CanonicalStatePayload, events);
        var physicalReplay = ScriptedHarness.ReplayFromGenesis(snapshot.State.Identity, events);
        if (!string.Equals(StateDigest(decoded), StateDigest(physicalReplay), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The canonical state payload does not match replayed actions.");
        }

        return decoded;
    }

    public static string StateDigest(HarnessWorld world) =>
        CanonicalPersistenceCodec.StateDigest(ToMiniatureState(world));

    public static string EventDigest(HarnessWorld world) =>
        CanonicalPersistenceCodec.EventDigest(world.Events);

    private static MiniatureWorldState ToMiniatureState(HarnessWorld world) => new(
        world.Identity,
        checked(world.Events.Sum(worldEvent => worldEvent.CounterDelta)),
        world.Events.Count == 0 ? 0 : world.Events[^1].EventId,
        HarnessStateCodec.Encode(world));
}

public static class HarnessStateCodec
{
    private const string Header = "clankerworld.seeded-harness-state/v2";

    public static string Encode(HarnessWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        builder.Append("map_manifest_base64=")
            .Append(Convert.ToBase64String(MapManifestCodec.Encode(world.Map))).Append('\n');
        builder.Append("actor=")
            .Append(world.Actor.Id).Append('|')
            .Append(world.Actor.Position.X).Append('|').Append(world.Actor.Position.Y).Append('|')
            .Append(world.Actor.HungerBasisPoints).Append('|')
            .Append(world.Actor.FoodItems).Append('|').Append(world.Actor.WoodItems).Append('\n');
        foreach (var resource in world.Resources.OrderBy(resource => resource.Id, StringComparer.Ordinal))
        {
            builder.Append("resource=").Append(resource.Id).Append('|')
                .Append(resource.State == ResourceState.Available ? "available" : "depleted").Append('\n');
        }

        return builder.ToString();
    }

    public static HarnessWorld Decode(
        WorldIdentity identity,
        string? payload,
        IReadOnlyList<PersistenceEvent> events)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(events);
        if (string.IsNullOrEmpty(payload))
        {
            throw new InvalidDataException("The harness snapshot does not contain canonical state payload.");
        }

        var lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The harness state format is not supported.");
        }

        var encodedManifest = ValueAfterPrefix(lines[1], "map_manifest_base64=");
        byte[] manifest;
        try
        {
            manifest = Convert.FromBase64String(encodedManifest);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The harness map manifest is not base64.", exception);
        }

        var genesis = ScriptedHarness.CreateGenesis(identity);
        var expectedManifest = MapManifestCodec.Encode(genesis.Map);
        if (!manifest.SequenceEqual(expectedManifest) ||
            !string.Equals(MapManifestCodec.Digest(genesis.Map), identity.InitialMapManifestDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved map manifest does not match the world identity.");
        }

        var actorParts = ValueAfterPrefix(lines[2], "actor=").Split('|', StringSplitOptions.None);
        if (actorParts.Length != 6 || !string.Equals(actorParts[0], genesis.Actor.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved actor state is invalid.");
        }

        var actor = new HarnessActor(
            actorParts[0],
            new GridPoint(ParseInteger(actorParts[1]), ParseInteger(actorParts[2])),
            ParseBasisPoints(actorParts[3]),
            ParseNonNegativeInteger(actorParts[4]),
            ParseNonNegativeInteger(actorParts[5]));
        if (!genesis.Map.IsPassable(actor.Position))
        {
            throw new InvalidDataException("The saved actor is not on passable ground.");
        }

        var savedResources = lines.Skip(3)
            .Select(ParseResource)
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .ToArray();
        if (savedResources.Length != genesis.Resources.Count ||
            !savedResources.Select(resource => resource.Id).SequenceEqual(
                genesis.Resources.Select(resource => resource.Id),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException("The saved resource state does not match the manifest.");
        }

        return new HarnessWorld(identity, genesis.Map, actor, savedResources, events.ToArray());
    }

    private static RuntimeResource ParseResource(string line)
    {
        var parts = ValueAfterPrefix(line, "resource=").Split('|', StringSplitOptions.None);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new InvalidDataException("The saved resource line is invalid.");
        }

        return parts[1] switch
        {
            "available" => new RuntimeResource(parts[0], ResourceState.Available),
            "depleted" => new RuntimeResource(parts[0], ResourceState.Depleted),
            _ => throw new InvalidDataException("The saved resource state is invalid."),
        };
    }

    private static string ValueAfterPrefix(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal)
            ? line[prefix.Length..]
            : throw new InvalidDataException($"Expected canonical payload line '{prefix}'.");

    private static int ParseInteger(string value)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidDataException("The saved integer value is invalid.");
        }

        return result;
    }

    private static int ParseNonNegativeInteger(string value)
    {
        var result = ParseInteger(value);
        return result >= 0 ? result : throw new InvalidDataException("The saved quantity must not be negative.");
    }

    private static int ParseBasisPoints(string value)
    {
        var result = ParseNonNegativeInteger(value);
        return result <= 10_000 ? result : throw new InvalidDataException("The saved need must be basis points.");
    }
}
