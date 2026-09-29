using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Ground look for one tile, derived from its water, cover, elevation and surface.</summary>
public enum TerrainStyle : byte
{
    Grass,
    ForestGrass,
    ScrubGrass,
    Tundra,
    ForestFloor,
    DenseForestFloor,
    Sand,
    ScrubSand,
    DryScrub,
    DryBrush,
    DesertBrush,
    Rock,
    Mountain,
    Peak,
    Snow,
    TundraSnow,
    FertileSoil,
    Ocean,
    Lake,
    River,
    ShallowWater,
    Unknown,
}

/// <summary>
/// Provisional pixel-art ground tiles: two calm variants per terrain style,
/// generated at 32×32 (and 16×16 for mid zoom). Details are a few deliberate
/// pixel clusters—tufts, pebbles, cracks, ripples—kept inside each tile so
/// neighbors join without seams, rather than per-pixel grain or striped
/// transitions. The flat base colors remain the overview palette.
/// </summary>
public static class TerrainTextures
{
    public const int VariantCount = 2;
    private static readonly int StyleCount = Enum.GetValues<TerrainStyle>().Length;
    private static readonly Dictionary<int, ImageTexture> Atlases = [];
    private static readonly Dictionary<int, Image> AtlasImages = [];

    public static Color BaseColor(TerrainStyle style) => style switch
    {
        TerrainStyle.Grass => new Color("5F8F5B"),
        TerrainStyle.ForestGrass => new Color("426D4B"),
        TerrainStyle.ScrubGrass => new Color("988857"),
        TerrainStyle.Tundra => new Color("869586"),
        TerrainStyle.ForestFloor => new Color("4D684A"),
        TerrainStyle.DenseForestFloor => new Color("3F5D42"),
        TerrainStyle.Sand => new Color("BAA77B"),
        TerrainStyle.ScrubSand => new Color("AA985F"),
        TerrainStyle.DryScrub => new Color("8F8159"),
        TerrainStyle.DryBrush => new Color("897A51"),
        TerrainStyle.DesertBrush => new Color("907D57"),
        TerrainStyle.Rock or TerrainStyle.Mountain => new Color("756D68"),
        TerrainStyle.Peak => new Color("AEB2B0"),
        TerrainStyle.Snow => new Color("CCD7D1"),
        TerrainStyle.TundraSnow => new Color("D6DDD4"),
        TerrainStyle.FertileSoil => new Color("735F45"),
        TerrainStyle.Ocean => new Color("325F89"),
        TerrainStyle.Lake => new Color("598FB3"),
        TerrainStyle.River => new Color("4786AB"),
        TerrainStyle.ShallowWater => new Color("4B7FA7"),
        _ => new Color("9B5463"),
    };

    public static bool IsWater(TerrainStyle style) =>
        style is TerrainStyle.Ocean or TerrainStyle.Lake or TerrainStyle.River or TerrainStyle.ShallowWater;

    /// <summary>Roughly one tile in four uses the busier second variant.</summary>
    public static int VariantAt(int x, int y) => PixelArt.Hash(x, y, 17) % 4 == 0 ? 1 : 0;

    /// <summary>The shared atlas at 32 px, or 16 px for mid zoom where 32 px detail would alias.</summary>
    public static int AtlasTileSize(int drawnTileSize) => drawnTileSize >= 32 ? 32 : 16;

    public static ImageTexture Atlas(int atlasTileSize)
    {
        if (Atlases.TryGetValue(atlasTileSize, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(AtlasImage(atlasTileSize));
        Atlases[atlasTileSize] = texture;
        return texture;
    }

    public static Rect2 Region(TerrainStyle style, int variant, int atlasTileSize) =>
        new(variant * atlasTileSize, (int)style * atlasTileSize, atlasTileSize, atlasTileSize);

    /// <summary>One generated tile, for inspection and tests.</summary>
    public static Image Tile(TerrainStyle style, int variant, int atlasTileSize) =>
        AtlasImage(atlasTileSize).GetRegion(new Rect2I(variant * atlasTileSize, (int)style * atlasTileSize,
            atlasTileSize, atlasTileSize));

    private static Image AtlasImage(int size)
    {
        if (AtlasImages.TryGetValue(size, out var cached)) return cached;
        var image = Image.CreateEmpty(size * VariantCount, size * StyleCount, false, Image.Format.Rgba8);
        foreach (var style in Enum.GetValues<TerrainStyle>())
            for (var variant = 0; variant < VariantCount; variant++)
                Paint(image, new Rect2I(variant * size, (int)style * size, size, size), style, variant);
        AtlasImages[size] = image;
        return image;
    }

    private static void Paint(Image image, Rect2I tile, TerrainStyle style, int variant)
    {
        var baseColor = BaseColor(style);
        PixelArt.Fill(image, tile, baseColor);
        var painter = new Painter(image, tile, new PixelArt.Stream(PixelArt.Hash((int)style, variant, tile.Size.X)));
        var busy = variant == 1;
        switch (style)
        {
            case TerrainStyle.Grass:
                painter.Tufts(busy ? 6 : 3, PixelArt.Shade(baseColor, -0.12f));
                painter.Specks(busy ? 3 : 2, PixelArt.Shade(baseColor, 0.10f));
                if (busy) painter.Pebbles(1, new Color("8A8A7C"));
                break;
            case TerrainStyle.ForestGrass:
                painter.Tufts(busy ? 6 : 4, PixelArt.Shade(baseColor, -0.14f));
                painter.Specks(busy ? 4 : 2, PixelArt.Shade(baseColor, 0.08f));
                if (busy) painter.Specks(2, new Color("7A6A3A"));
                break;
            case TerrainStyle.ScrubGrass:
            case TerrainStyle.ScrubSand:
            case TerrainStyle.DryBrush:
                painter.Bushes(busy ? 3 : 2, new Color("6E7040"), new Color("8D9152"));
                painter.Specks(busy ? 4 : 2, PixelArt.Shade(baseColor, -0.10f));
                break;
            case TerrainStyle.Tundra:
                painter.Specks(busy ? 5 : 3, new Color("A2B09F"));
                painter.Specks(busy ? 3 : 1, new Color("A89F72"));
                painter.Tufts(busy ? 2 : 1, PixelArt.Shade(baseColor, -0.12f));
                break;
            case TerrainStyle.ForestFloor:
            case TerrainStyle.DenseForestFloor:
                painter.Specks(busy ? 4 : 3, new Color("5E7A4E"));
                painter.Specks(busy ? 3 : 2, new Color("6B6545"));
                painter.Tufts(busy ? 2 : 1, PixelArt.Shade(baseColor, -0.16f));
                if (busy) painter.Twig(new Color("5A4A36"));
                break;
            case TerrainStyle.Sand:
                painter.Specks(busy ? 5 : 3, PixelArt.Shade(baseColor, -0.09f));
                painter.Ripples(busy ? 2 : 1, PixelArt.Shade(baseColor, 0.08f), 4, 6);
                if (busy) painter.Specks(1, new Color("E3D6B5"));
                break;
            case TerrainStyle.DryScrub:
            case TerrainStyle.DesertBrush:
                painter.Specks(busy ? 5 : 3, PixelArt.Shade(baseColor, -0.12f));
                painter.Pebbles(busy ? 2 : 1, new Color("7B7462"));
                if (busy) painter.Bushes(1, new Color("6E7040"), new Color("8D9152"));
                break;
            case TerrainStyle.Rock:
                painter.Cracks(busy ? 3 : 2, PixelArt.Shade(baseColor, -0.20f), PixelArt.Shade(baseColor, 0.14f));
                if (busy) painter.Pebbles(1, PixelArt.Shade(baseColor, 0.18f));
                break;
            case TerrainStyle.Mountain:
                painter.Mountains(busy, PixelArt.Shade(baseColor, 0.16f), PixelArt.Shade(baseColor, -0.20f),
                    PixelArt.Shade(baseColor, -0.34f), null);
                painter.Specks(2, PixelArt.Shade(baseColor, -0.14f));
                break;
            case TerrainStyle.Peak:
                painter.Mountains(busy, new Color("8E8A85"), new Color("5F5955"), new Color("4A4542"),
                    new Color("EEF3F1"));
                break;
            case TerrainStyle.Snow:
            case TerrainStyle.TundraSnow:
                painter.Ripples(busy ? 3 : 2, new Color("B8C6C4"), 3, 5);
                painter.Specks(busy ? 3 : 2, new Color("F4F8F6"));
                if (style == TerrainStyle.TundraSnow) painter.Specks(2, new Color("98A796"));
                break;
            case TerrainStyle.FertileSoil:
                painter.Pebbles(busy ? 4 : 3, PixelArt.Shade(baseColor, -0.16f));
                painter.Specks(busy ? 4 : 2, PixelArt.Shade(baseColor, 0.12f));
                break;
            case TerrainStyle.Ocean:
                painter.Ripples(busy ? 3 : 2, PixelArt.Shade(baseColor, 0.12f), 3, 5);
                break;
            case TerrainStyle.Lake:
            case TerrainStyle.ShallowWater:
                painter.Ripples(busy ? 3 : 2, PixelArt.Shade(baseColor, 0.10f), 3, 5);
                break;
            case TerrainStyle.River:
                painter.Ripples(busy ? 4 : 3, PixelArt.Shade(baseColor, 0.12f), 4, 7);
                break;
        }
    }

    /// <summary>Places detail clusters inside one tile, one pixel clear of its edges.</summary>
    private struct Painter(Image image, Rect2I tile, PixelArt.Stream random)
    {
        private readonly int size = tile.Size.X;
        private PixelArt.Stream random = random;

        // Counts are authored for 32 px tiles; 16 px tiles keep about half.
        private readonly int Count(int authored) => size >= 32 ? authored : Math.Max(1, (authored + 1) / 2);

        private (int X, int Y) Spot(int width, int height) =>
            (tile.Position.X + random.Range(1, size - width - 1), tile.Position.Y + random.Range(1, size - height - 1));

        public void Specks(int count, Color color)
        {
            for (var index = 0; index < Count(count); index++)
            {
                var (x, y) = Spot(1, 1);
                PixelArt.Put(image, x, y, color);
            }
        }

        public void Tufts(int count, Color color)
        {
            for (var index = 0; index < Count(count); index++)
            {
                var (x, y) = Spot(3, 2);
                PixelArt.Put(image, x, y + 1, color);
                PixelArt.Put(image, x + 1, y, color);
                PixelArt.Put(image, x + 2, y + 1, color);
            }
        }

        public void Pebbles(int count, Color color)
        {
            for (var index = 0; index < Count(count); index++)
            {
                var (x, y) = Spot(2, 2);
                PixelArt.Put(image, x, y + 1, color);
                PixelArt.Put(image, x + 1, y + 1, color);
                PixelArt.Put(image, x, y, color.Lightened(0.18f));
            }
        }

        public void Bushes(int count, Color dark, Color light)
        {
            for (var index = 0; index < Count(count); index++)
            {
                var (x, y) = Spot(3, 2);
                PixelArt.Put(image, x, y + 1, dark);
                PixelArt.Put(image, x + 1, y + 1, dark);
                PixelArt.Put(image, x + 2, y + 1, dark);
                PixelArt.Put(image, x + 1, y, light);
            }
        }

        public void Ripples(int count, Color color, int minimumLength, int maximumLength)
        {
            var scaleLength = size >= 32 ? 1 : 2;
            for (var index = 0; index < Count(count); index++)
            {
                var length = Math.Max(2, random.Range(minimumLength, maximumLength + 1) / scaleLength);
                var (x, y) = Spot(length, 1);
                for (var step = 0; step < length; step++) PixelArt.Put(image, x + step, y, color);
            }
        }

        public void Cracks(int count, Color crack, Color highlight)
        {
            for (var index = 0; index < Count(count); index++)
            {
                var (x, y) = Spot(4, 3);
                PixelArt.Put(image, x, y, crack);
                PixelArt.Put(image, x + 1, y + 1, crack);
                PixelArt.Put(image, x + 2, y + 1, crack);
                PixelArt.Put(image, x + 3, y + 2, crack);
                PixelArt.Put(image, x + 1, y, highlight);
                PixelArt.Put(image, x + 3, y + 1, highlight);
            }
        }

        /// <summary>
        /// Top-down relief: one mountain (or two smaller ones in the busy
        /// variant) with a lit west face, shaded east face and dark east edge.
        /// Peaks add a snowy summit.
        /// </summary>
        public void Mountains(bool twin, Color lit, Color shade, Color outline, Color? snow)
        {
            var unit = size / 32f;
            if (twin)
            {
                MountainShape((int)(11 * unit), (int)(27 * unit), (int)(8 * unit), (int)(10 * unit), lit, shade, outline, snow);
                MountainShape((int)(21 * unit), (int)(23 * unit), (int)(8 * unit), (int)(11 * unit), lit, shade, outline, snow);
            }
            else
            {
                MountainShape(size / 2, (int)(27 * unit), (int)(12 * unit), (int)(15 * unit), lit, shade, outline, snow);
            }
        }

        private readonly void MountainShape(int centerX, int baseY, int halfWidth, int height,
            Color lit, Color shade, Color outline, Color? snow)
        {
            height = Math.Max(3, height);
            halfWidth = Math.Max(2, halfWidth);
            var snowRows = snow is null ? 0 : Math.Max(1, height * 2 / 5);
            for (var row = 0; row < height; row++)
            {
                var y = tile.Position.Y + baseY - height + row + 1;
                var span = (row + 1) * halfWidth / height;
                for (var dx = -span; dx <= span; dx++)
                {
                    var x = tile.Position.X + centerX + dx;
                    var color = dx == span ? outline : dx < 0 ? lit : shade;
                    if (row < snowRows && snow is { } cap)
                        color = dx == span ? outline : dx < 0 ? cap : cap.Darkened(0.14f);
                    PixelArt.Put(image, x, y, color);
                }
            }
        }

        public void Twig(Color color)
        {
            var (x, y) = Spot(4, 2);
            PixelArt.Put(image, x, y + 1, color);
            PixelArt.Put(image, x + 1, y + 1, color);
            PixelArt.Put(image, x + 2, y, color);
            PixelArt.Put(image, x + 3, y, color);
        }
    }
}
