using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Compact, indexed map facts shared by the local camera and overview.</summary>
public sealed class WorldTerrainMap
{
    private readonly byte[] terrain;
    private readonly byte[]? climate;
    private readonly byte[]? elevation;
    private readonly byte[]? hydrology;
    private readonly byte[]? surface;
    private readonly byte[]? vegetation;
    private TerrainStyle[]? styles;

    private WorldTerrainMap(int width, int height, byte[] terrain, byte[]? climate = null,
        byte[]? elevation = null, byte[]? hydrology = null, byte[]? surface = null,
        byte[]? vegetation = null)
    {
        Width = width;
        Height = height;
        this.terrain = terrain;
        this.climate = climate;
        this.elevation = elevation;
        this.hydrology = hydrology;
        this.surface = surface;
        this.vegetation = vegetation;
    }

    public int Width { get; }
    public int Height { get; }
    public bool HasMapLayers => climate is not null;

    public static WorldTerrainMap FromTiles(IReadOnlyList<OwnerWorldTile> tiles, int width, int height,
        OwnerWorldPackedMapLayers? layers = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var terrain = new byte[checked(width * height)];
        foreach (var tile in tiles)
        {
            if (tile.X < 0 || tile.X >= width || tile.Y < 0 || tile.Y >= height) continue;
            terrain[tile.Y * width + tile.X] = tile.Terrain switch
            {
                "meadow" => 1,
                "water" => 2,
                "mountain" => 3,
                "river" => 4,
                "lake" => 5,
                "ocean" => 6,
                "sand" => 7,
                "forest" => 8,
                "snow" => 9,
                "peak" => 10,
                _ => 0,
            };
        }
        return WithLayers(width, height, terrain, layers);
    }

    public static WorldTerrainMap FromPacked(OwnerWorldPackedTerrain packed,
        OwnerWorldPackedMapLayers? layers = null)
    {
        ArgumentNullException.ThrowIfNull(packed);
        if (packed.Encoding != "terrain-kind-v1" || packed.Width <= 0 || packed.Height <= 0)
            throw new InvalidDataException("The world terrain encoding or dimensions are unsupported.");
        var source = Convert.FromBase64String(packed.Data);
        if (source.Length != checked(packed.Width * packed.Height))
            throw new InvalidDataException("The packed world terrain length is invalid.");
        var terrain = new byte[source.Length];
        for (var index = 0; index < source.Length; index++)
            terrain[index] = source[index] switch
            {
                0 => 1, // meadow
                1 => 2, // fixture water
                2 => 3, // mountain
                3 => 4, // river
                4 => 5, // lake
                5 => 6, // ocean
                6 => 10, // peak
                7 => 7, // sand
                8 => 8, // forest
                9 => 9, // snow
                _ => throw new InvalidDataException("The packed world terrain contains an unknown kind."),
            };
        return WithLayers(packed.Width, packed.Height, terrain, layers);
    }

    public byte At(int x, int y) => terrain[y * Width + x];
    public byte? ClimateAt(int x, int y) => LayerAt(climate, x, y);
    public byte? ElevationAt(int x, int y) => LayerAt(elevation, x, y);
    public byte? HydrologyAt(int x, int y) => LayerAt(hydrology, x, y);
    public byte? SurfaceAt(int x, int y) => LayerAt(surface, x, y);
    public byte? VegetationAt(int x, int y) => LayerAt(vegetation, x, y);

    /// <summary>Render-only legacy projection; inspection keeps missing layer facts unavailable.</summary>
    public byte RenderSurfaceAt(int x, int y) => SurfaceAt(x, y) ?? At(x, y) switch
    {
        1 => 0, // meadow
        2 or 4 or 5 or 6 => 4, // water, river, lake, ocean
        3 or 10 => 2, // mountain, peak
        7 => 1, // sand
        8 => 5, // forest floor
        9 => 3, // snow
        _ => 0,
    };

    /// <summary>Bit mask of cardinal edges where land meets generated water; N/E/S/W are bits 1/2/4/8.</summary>
    public byte WaterEdgeMaskAt(int x, int y, bool wrapsEastWest)
    {
        if (!HasMapLayers || HydrologyAt(x, y) is not 0) return 0;
        return EdgeMask(x, y, wrapsEastWest,
            (nx, ny) => HydrologyAt(nx, ny) is { } value && value != 0);
    }

    /// <summary>Bit mask where a neighboring ground surface differs, for broken natural transition edges.</summary>
    public byte SurfaceBoundaryMaskAt(int x, int y, bool wrapsEastWest)
    {
        if (!HasMapLayers || SurfaceAt(x, y) is not { } current || current == 4) return 0;
        return EdgeMask(x, y, wrapsEastWest,
            (nx, ny) => SurfaceAt(nx, ny) is { } adjacent && adjacent != current);
    }

    private byte EdgeMask(int x, int y, bool wrapsEastWest, Func<int, int, bool> isEdge)
    {
        byte mask = 0;
        if (y > 0 && isEdge(x, y - 1)) mask |= 1;
        var east = x + 1;
        if (east == Width && wrapsEastWest) east = 0;
        if (east < Width && isEdge(east, y)) mask |= 2;
        if (y + 1 < Height && isEdge(x, y + 1)) mask |= 4;
        var west = x - 1;
        if (west < 0 && wrapsEastWest) west = Width - 1;
        if (west >= 0 && isEdge(west, y)) mask |= 8;
        return mask;
    }

    /// <summary>Flat overview color: the base of the tile's pixel-art ground style.</summary>
    public Color DisplayColorAt(int x, int y) => TerrainTextures.BaseColor(StyleAt(x, y));

    /// <summary>
    /// Ground style from independent surface/cover facts, falling back to the
    /// v1 terrain projection for old maps. Cover overrides elevation, matching
    /// the original flat palette.
    /// </summary>
    public TerrainStyle StyleAt(int x, int y)
    {
        // Ground transitions read eight neighbors per drawn tile, so styles
        // are derived once per map; the layers never change after loading.
        if (styles is null)
        {
            var computed = new TerrainStyle[checked(Width * Height)];
            for (var index = 0; index < computed.Length; index++) computed[index] = ComputeStyle(index);
            styles = computed;
        }
        return styles[y * Width + x];
    }

    private TerrainStyle ComputeStyle(int index)
    {
        if (!HasMapLayers)
            return terrain[index] switch
            {
                1 => TerrainStyle.Grass,
                2 => TerrainStyle.ShallowWater,
                3 => TerrainStyle.Mountain,
                4 => TerrainStyle.River,
                5 => TerrainStyle.Lake,
                6 => TerrainStyle.Ocean,
                7 => TerrainStyle.Sand,
                8 => TerrainStyle.ForestGrass,
                9 => TerrainStyle.Snow,
                10 => TerrainStyle.Peak,
                _ => TerrainStyle.Unknown,
            };
        var water = hydrology![index];
        if (water != 0)
            return water switch
            {
                1 => TerrainStyle.Ocean,
                2 => TerrainStyle.Lake,
                3 => TerrainStyle.River,
                _ => TerrainStyle.Unknown,
            };
        var ground = surface![index];
        var covered = (vegetation![index], ground) switch
        {
            (2, 0) => TerrainStyle.ForestGrass,
            (2, 5) => TerrainStyle.DenseForestFloor,
            (3, 0) => TerrainStyle.ScrubGrass,
            (3, 6) => TerrainStyle.DryBrush,
            (4, 0) => TerrainStyle.Tundra,
            (3, 1) => TerrainStyle.ScrubSand,
            (4, 3) => TerrainStyle.TundraSnow,
            // Cacti are not part of the current art direction; dry cactus
            // cover reads as brush.
            (5, 1) or (5, 6) => TerrainStyle.DesertBrush,
            _ => (TerrainStyle?)null,
        };
        if (covered is { } style) return style;
        if (elevation![index] >= 245) return TerrainStyle.Peak;
        if (elevation[index] >= 215) return TerrainStyle.Mountain;
        return ground switch
        {
            0 => TerrainStyle.Grass,
            1 => TerrainStyle.Sand,
            2 => TerrainStyle.Rock,
            3 => TerrainStyle.Snow,
            4 => TerrainStyle.ShallowWater,
            5 => TerrainStyle.ForestFloor,
            6 => TerrainStyle.DryScrub,
            7 => TerrainStyle.FertileSoil,
            _ => TerrainStyle.Unknown,
        };
    }

    private byte? LayerAt(byte[]? layer, int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height && layer is not null
            ? layer[y * Width + x] : null;

    private static WorldTerrainMap WithLayers(int width, int height, byte[] terrain,
        OwnerWorldPackedMapLayers? layers)
    {
        if (layers is null) return new WorldTerrainMap(width, height, terrain);
        if (layers.Width != width || layers.Height != height ||
            layers.Encoding is not ("map-layers-v1" or "map-layers-v2"))
            throw new InvalidDataException("The packed world map layers have an unsupported encoding or dimensions.");
        var length = checked(width * height);
        var climate = DecodeLayer(layers.Climate, length, 5, "climate");
        var elevation = DecodeLayer(layers.Elevation, length, null, "elevation");
        var hydrology = DecodeLayer(layers.Hydrology, length, 3, "hydrology");
        var surface = DecodeLayer(layers.Surface, length, 7, "surface");
        var vegetation = DecodeLayer(layers.Vegetation, length, 5, "vegetation");
        return new WorldTerrainMap(width, height, terrain, climate, elevation, hydrology, surface, vegetation);
    }

    private static byte[] DecodeLayer(string encoded, int expectedLength, int? maximumValue, string name)
    {
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length != expectedLength || maximumValue is { } max && bytes.Any(value => value > max))
            throw new InvalidDataException($"The packed world {name} layer is invalid.");
        return bytes;
    }

    public static string NameFor(byte kind) => kind switch
    {
        1 => "Meadow",
        2 => "Water",
        3 => "Mountain",
        4 => "River",
        5 => "Lake",
        6 => "Ocean",
        7 => "Sand",
        8 => "Forest",
        9 => "Snow",
        10 => "Peak",
        _ => "Unavailable",
    };

    public static string? ClimateName(byte? value) => value switch
    {
        0 => "Tropical",
        1 => "Dry",
        2 => "Temperate",
        3 => "Cold",
        4 => "Polar",
        _ => null,
    };

    public static string? SurfaceName(byte? value) => value switch
    {
        0 => "Grass",
        1 => "Sand",
        2 => "Rock",
        3 => "Snow",
        4 => "Water",
        5 => "Forest floor",
        6 => "Dry scrub",
        7 => "Fertile soil",
        _ => null,
    };

    public static string? HydrologyName(byte? value) => value switch
    {
        0 => "Land",
        1 => "Ocean",
        2 => "Lake",
        3 => "River",
        _ => null,
    };

    public static string? VegetationName(byte? value) => value switch
    {
        0 => "None",
        1 => "Grass",
        2 => "Forest",
        3 => "Scrub",
        4 => "Tundra",
        5 => "Cactus",
        _ => null,
    };

    public static string? NaturalObjectName(string? value) => value switch
    {
        "berry_bush" => "Berry bush",
        "wild_greens" => "Wild greens",
        "fiber_plant" => "Fiber plant",
        "reeds" => "Reeds",
        "stone_outcrop" => "Stone outcrop",
        "iron_outcrop" => "Iron outcrop",
        "gold_outcrop" => "Gold outcrop",
        "diamond_outcrop" => "Diamond outcrop",
        "clay_bank" => "Clay bank",
        "wild_seed_patch" => "Wild seed patch",
        "fertile_soil" => "Fertile soil",
        _ => null,
    };
}
