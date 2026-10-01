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
/// Pixel-art ground tiles: two calm variants per terrain style, generated at
/// 32×32 and drawn separately at 16×16 for mid zoom. Grass, forest ground,
/// sand, rock, snow and tundra carry soft, rounded mottling in neighbouring
/// steps of their own colour ramp, which repeats seamlessly across tile edges
/// and between the two variants, plus a few small motifs kept one pixel clear
/// of the edges. The other styles keep the provisional look: a few deliberate
/// pixel clusters on flat colour. Every tile keeps its style's base colour,
/// which the overview palette uses.
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

    /// <summary>Overview color for a hill: its ground color, warmed and slightly darkened.</summary>
    public static Color HillColor(Color ground) => ground.Lerp(new Color("7E6F4B"), 0.28f);

    private static readonly Dictionary<int, ImageTexture> HillAtlases = [];
    private static readonly Dictionary<int, Image> HillAtlasImages = [];

    /// <summary>
    /// Provisional hill relief drawn over a tile's own ground: low mounds with a
    /// lit north-west rim and a shaded south-east rim on a clear background,
    /// so grass, forest or snow still shows beneath.
    /// </summary>
    public static ImageTexture HillAtlas(int atlasTileSize)
    {
        if (HillAtlases.TryGetValue(atlasTileSize, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(HillAtlasImage(atlasTileSize));
        HillAtlases[atlasTileSize] = texture;
        return texture;
    }

    public static Rect2 HillRegion(int variant, int atlasTileSize) =>
        new(variant * atlasTileSize, 0, atlasTileSize, atlasTileSize);

    /// <summary>One hill overlay, for inspection and tests.</summary>
    public static Image HillOverlay(int variant, int atlasTileSize) =>
        HillAtlasImage(atlasTileSize).GetRegion(new Rect2I(variant * atlasTileSize, 0, atlasTileSize, atlasTileSize));

    private static Image HillAtlasImage(int size)
    {
        if (HillAtlasImages.TryGetValue(size, out var cached)) return cached;
        var image = Image.CreateEmpty(size * VariantCount, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var unit = size / 32f;
        // Variant 0 is one broad mound; variant 1 two smaller ones.
        Mound(image, size / 2, (int)(20 * unit), (int)(12 * unit), (int)(7 * unit));
        Mound(image, size + (int)(11 * unit), (int)(22 * unit), (int)(8 * unit), (int)(5 * unit));
        Mound(image, size + (int)(21 * unit), (int)(14 * unit), (int)(8 * unit), (int)(5 * unit));
        HillAtlasImages[size] = image;
        return image;
    }

    private static void Mound(Image image, int centerX, int centerY, int radiusX, int radiusY)
    {
        radiusX = Math.Max(3, radiusX);
        radiusY = Math.Max(2, radiusY);
        var body = new Color(0.22f, 0.17f, 0.08f, 0.10f);
        var lit = new Color(1f, 0.97f, 0.84f, 0.30f);
        var shade = new Color(0.12f, 0.09f, 0.05f, 0.38f);
        for (var dy = -radiusY; dy <= radiusY; dy++)
            for (var dx = -radiusX; dx <= radiusX; dx++)
            {
                var distance = (float)(dx * dx) / (radiusX * radiusX) + (float)(dy * dy) / (radiusY * radiusY);
                if (distance > 1f) continue;
                // The outer ring of the mound is its rim: lit facing the
                // north-west light, shaded on the far side.
                var rim = distance > 0.62f;
                var color = !rim ? body : dx + dy * 2 < 0 ? lit : shade;
                PixelArt.Put(image, centerX + dx, centerY + dy, color);
            }
    }

    private static Image AtlasImage(int size)
    {
        if (AtlasImages.TryGetValue(size, out var cached)) return cached;
        var image = Image.CreateEmpty(size * VariantCount, size * StyleCount, false, Image.Format.Rgba8);
        foreach (var style in Enum.GetValues<TerrainStyle>())
            for (var variant = 0; variant < VariantCount; variant++)
            {
                var tile = new Rect2I(variant * size, (int)style * size, size, size);
                if (Ground.Draws(style))
                    image.BlitRect(Ground.Paint(style, variant, size), new Rect2I(0, 0, size, size), tile.Position);
                else
                    Paint(image, tile, style, variant);
            }
        AtlasImages[size] = image;
        return image;
    }

    /// <summary>The provisional look for the styles that are not drawn as mottled ground yet.</summary>
    private static void Paint(Image image, Rect2I tile, TerrainStyle style, int variant)
    {
        var baseColor = BaseColor(style);
        PixelArt.Fill(image, tile, baseColor);
        var painter = new Painter(image, tile, new PixelArt.Stream(PixelArt.Hash((int)style, variant, tile.Size.X)));
        var busy = variant == 1;
        switch (style)
        {
            case TerrainStyle.ScrubGrass:
            case TerrainStyle.ScrubSand:
            case TerrainStyle.DryBrush:
                painter.Bushes(busy ? 3 : 2, new Color("6E7040"), new Color("8D9152"));
                painter.Specks(busy ? 4 : 2, PixelArt.Shade(baseColor, -0.10f));
                break;
            case TerrainStyle.DryScrub:
            case TerrainStyle.DesertBrush:
                painter.Specks(busy ? 5 : 3, PixelArt.Shade(baseColor, -0.12f));
                painter.Pebbles(busy ? 2 : 1, new Color("7B7462"));
                if (busy) painter.Bushes(1, new Color("6E7040"), new Color("8D9152"));
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
            case TerrainStyle.TundraSnow:
                painter.Ripples(busy ? 3 : 2, new Color("B8C6C4"), 3, 5);
                painter.Specks(busy ? 3 : 2, new Color("F4F8F6"));
                painter.Specks(2, new Color("98A796"));
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
    }

    /// <summary>
    /// Mottled ground for grass, forest grass, forest floor, sand, rock, snow
    /// and tundra. Each tile carries soft, rounded patches in one or two
    /// neighbouring steps of its own colour ramp instead of isolated specks,
    /// keeps its style's base colour, repeats seamlessly with itself and with
    /// its other variant, and is drawn deterministically from
    /// <see cref="PixelArt.Hash"/> and <see cref="PixelArt.Stream"/>. The 16 px
    /// tiles are drawn separately rather than shrunk.
    /// </summary>
    private static class Ground
    {
        private static readonly TerrainStyle[] Drawn =
        [
            TerrainStyle.Grass, TerrainStyle.ForestGrass, TerrainStyle.ForestFloor, TerrainStyle.DenseForestFloor,
            TerrainStyle.Sand, TerrainStyle.Rock, TerrainStyle.Snow, TerrainStyle.Tundra,
        ];

        // Accent colours borrowed from other ramps for motifs (at most two per tile).
        private static readonly Color RockLight = new("8B837D");
        private static readonly Color RockShade = new("625B56");
        private static readonly Color TimberShade = new("6E4E31");
        private static readonly Color ScrubLight = new("A8975F");
        private static readonly Color ScrubShade = new("86784C");

        public static bool Draws(TerrainStyle style) => Array.IndexOf(Drawn, style) >= 0;

        /// <summary>Five steps of one hue family; the base is the style's overview colour.</summary>
        private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
        {
            public static Ramp Of(TerrainStyle style, string edge, string shade, string light, string highlight) =>
                new(new Color(edge), new Color(shade), BaseColor(style), new Color(light), new Color(highlight));
        }

        private static Ramp RampOf(TerrainStyle style) => style switch
        {
            TerrainStyle.Grass => Ramp.Of(style, "3B5E3A", "527F4F", "6FA069", "86B37A"),
            TerrainStyle.ForestGrass => Ramp.Of(style, "2E4D35", "395F41", "4E7D56", "5E8E64"),
            TerrainStyle.ForestFloor => Ramp.Of(style, "34483A", "435B41", "597759", "6A8866"),
            TerrainStyle.DenseForestFloor => Ramp.Of(style, "2B4030", "36503A", "4A6B4C", "5A7D5A"),
            TerrainStyle.Sand => Ramp.Of(style, "7E6E4A", "A08F66", "C8B78C", "E3D6B5"),
            TerrainStyle.Rock => Ramp.Of(style, "4A4542", "625B56", "8B837D", "A49C95"),
            TerrainStyle.Snow => Ramp.Of(style, "9AAAA8", "B8C6C4", "DCE5E0", "F4F8F6"),
            TerrainStyle.Tundra => Ramp.Of(style, "5A675B", "757F75", "96A596", "A8B6A8"),
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "This style is not drawn as mottled ground."),
        };

        /// <summary>Paints one tile of the given size (32 or 16): base fill, then the style's mottling and motifs.</summary>
        public static Image Paint(TerrainStyle style, int variant, int size)
        {
            var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var ramp = RampOf(style);
            image.Fill(ramp.Base);
            var busy = variant == 1;
            var painter = new GroundPainter(image, size, new PixelArt.Stream(PixelArt.Hash((int)style * 7 + 3, variant, size)));
            var salt = (int)style * 101 + 11;
            switch (style)
            {
                case TerrainStyle.Grass:
                    painter.Mottle(salt, variant, 0.13f, 0f, ramp.Shade, ramp.Light);
                    painter.Tufts(busy ? 4 : 2, ramp.Shade, ramp.Highlight);
                    if (busy) painter.Pebbles(1, RockLight, RockShade);
                    break;
                case TerrainStyle.ForestGrass:
                    painter.Mottle(salt, variant, 0.13f, 0f, ramp.Shade, ramp.Light);
                    painter.Tufts(busy ? 4 : 2, ramp.Edge, ramp.Highlight);
                    if (busy) painter.Leaves(2, ScrubShade, ScrubLight);
                    break;
                case TerrainStyle.ForestFloor:
                case TerrainStyle.DenseForestFloor:
                    painter.Mottle(salt, variant, 0.12f, 0.06f, ramp.Shade, ramp.Light);
                    painter.Leaves(busy ? 3 : 2, TimberShade, ScrubShade);
                    painter.Tufts(1, ramp.Edge, ramp.Highlight);
                    if (busy) painter.Twig(TimberShade);
                    break;
                case TerrainStyle.Sand:
                    painter.Mottle(salt, variant, 0f, 0.15f, ramp.Shade, ramp.Light, broad: true);
                    painter.Ripples(busy ? 3 : 2, ramp.Light, ramp.Shade);
                    if (busy) painter.Specks(3, ramp.Shade);
                    break;
                case TerrainStyle.Rock:
                    painter.Facets(salt, ramp);
                    painter.Cracks(busy ? 2 : 1, ramp.Edge, ramp.Highlight);
                    if (busy) painter.Pebbles(2, ramp.Highlight, ramp.Edge);
                    break;
                case TerrainStyle.Snow:
                    // Both variants share v0's soft patches, so they mix without blotches; v1 only adds a drift and sparkle.
                    painter.Mottle(salt, 0, 0.04f, 0.15f, ramp.Shade, ramp.Light, broad: true);
                    painter.Drifts(busy ? 2 : 1, ramp.Shade, ramp.Highlight);
                    painter.Specks(busy ? 3 : 2, ramp.Highlight);
                    break;
                case TerrainStyle.Tundra:
                    painter.Mottle(salt, variant, 0.11f, 0.06f, ramp.Shade, ramp.Light, broad: true);
                    painter.Lichen(busy ? 3 : 2, ramp.Light, ramp.Highlight);
                    if (busy) painter.Pebbles(1, ramp.Highlight, ramp.Edge);
                    if (busy) painter.Tufts(2, ScrubShade, ScrubLight);
                    break;
            }
            return image;
        }

        /// <summary>Draws into one square tile image; pixel coordinates are clipped to the tile.</summary>
        private struct GroundPainter(Image image, int size, PixelArt.Stream random)
        {
            private PixelArt.Stream random = random;

            /// <summary>Output pixels per 32-unit tile pixel: 1 at 32 px, 0.5 at 16 px.</summary>
            private readonly float unit = size / 32f;

            /// <summary>Counts are authored for 32 px tiles; 16 px tiles keep about half.</summary>
            private readonly int Count(int authored) => size >= 32 ? authored : Math.Max(1, (authored + 1) / 2);

            private readonly bool Small => size < 32;

            private readonly void Put(int x, int y, Color color)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) return;
                image.SetPixel(x, y, color);
            }

            /// <summary>A random spot for a motif of the given pixel size, one pixel clear of every edge.</summary>
            private (int X, int Y) Spot(int width, int height) =>
                (random.Range(1, Math.Max(2, size - width - 1)), random.Range(1, Math.Max(2, size - height - 1)));

            /// <summary>
            /// Soft mottling: the darkest and lightest parts of a smooth,
            /// tile-periodic noise field become irregular blobs in the shade and
            /// light steps, kept apart by base colour. Lone pixels are folded
            /// back into their surroundings, so the result is patches, never
            /// grain. The noise is weighted toward patches about four pixels
            /// across and spread evenly, so a field of identical tiles reads as
            /// one soft texture rather than a repeating pattern of big shapes;
            /// the broad grain, for smooth ground like sand and snow, makes
            /// fewer, larger and softer patches. The fractions are of the tile's
            /// pixels (about 60% of that at 16 px, which keeps only the
            /// impression), pooled over both variants so the two share one
            /// threshold and meet without a seam.
            /// </summary>
            public readonly void Mottle(int salt, int variant, float shadeFraction, float lightFraction, Color shade, Color light, bool broad = false)
            {
                var fields = broad
                    ? Field.Pair(size, salt, Field.Octave(16, 0.3f), Field.Octave(8, 0.45f), Field.Octave(4, 0.25f))
                    : Field.Pair(size, salt, Field.Octave(16, 0.1f), Field.Octave(8, 0.3f), Field.Octave(4, 0.6f));
                var keep = Small ? 0.6f : 1f;
                var (low, high) = Field.Thresholds(fields, shadeFraction * keep, lightFraction * keep);
                var field = fields[variant];
                var classes = new int[size, size];
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                        classes[x, y] = field[x, y] < low ? -1 : field[x, y] > high ? 1 : 0;
                RemoveSpecks(classes, size);
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        if (classes[x, y] < 0) Put(x, y, shade);
                        else if (classes[x, y] > 0) Put(x, y, light);
                    }
            }

            /// <summary>
            /// Grass tufts: a small "^" of blades in the dark colour with a lit tip.
            /// At 16 px a tuft is two dark pixels and the tip.
            /// </summary>
            public void Tufts(int count, Color blade, Color tip)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    if (!Small)
                    {
                        var (x, y) = Spot(3, 3);
                        Put(x, y + 2, blade);
                        Put(x + 1, y + 1, blade);
                        Put(x + 2, y + 2, blade);
                        Put(x + 1, y, tip);
                    }
                    else
                    {
                        var (x, y) = Spot(2, 2);
                        Put(x, y + 1, blade);
                        Put(x + 1, y + 1, blade);
                        Put(x + 1, y, tip);
                    }
                }
            }

            /// <summary>Single pixels of one colour, for sand grains and snow sparkle.</summary>
            public void Specks(int count, Color color)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    var (x, y) = Spot(1, 1);
                    Put(x, y, color);
                }
            }

            /// <summary>Small pebbles lit from the north-west: a lit pixel pair over a shaded one (one pixel at 16 px).</summary>
            public void Pebbles(int count, Color light, Color shade)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    if (!Small)
                    {
                        var (x, y) = Spot(2, 2);
                        Put(x, y, light);
                        Put(x + 1, y, light);
                        Put(x, y + 1, light);
                        Put(x + 1, y + 1, shade);
                    }
                    else
                    {
                        var (x, y) = Spot(2, 2);
                        Put(x, y, light);
                        Put(x + 1, y + 1, shade);
                    }
                }
            }

            /// <summary>Fallen leaves: an L of two pixels in the darker colour with a lighter pixel beside it.</summary>
            public void Leaves(int count, Color dark, Color light)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    var (x, y) = Spot(2, 2);
                    Put(x, y + 1, dark);
                    Put(x + 1, y + 1, dark);
                    Put(x + 1, y, light);
                }
            }

            /// <summary>A fallen twig: five pixels in a shallow bend, two at 16 px.</summary>
            public void Twig(Color color)
            {
                if (!Small)
                {
                    var (x, y) = Spot(5, 2);
                    Put(x, y + 1, color);
                    Put(x + 1, y + 1, color);
                    Put(x + 2, y, color);
                    Put(x + 3, y, color);
                    Put(x + 4, y, color);
                }
                else
                {
                    var (x, y) = Spot(2, 1);
                    Put(x, y, color);
                    Put(x + 1, y, color);
                }
            }

            /// <summary>
            /// Wind ripples on sand: a low arc in the light step whose middle has
            /// a one-pixel shaded lee on its south side. Shorter at 16 px.
            /// </summary>
            public void Ripples(int count, Color crest, Color lee)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    var length = Small ? random.Range(4, 6) : random.Range(7, 11);
                    var (x, y) = Spot(length, 3);
                    for (var step = 0; step < length; step++)
                    {
                        var rise = Small ? 0 : (int)MathF.Round(MathF.Sin(MathF.PI * (step + 0.5f) / length));
                        Put(x + step, y + 1 - rise, crest);
                        if (step > 0 && step < length - 1) Put(x + step, y + 2 - rise, lee);
                    }
                }
            }

            /// <summary>Snow drifts: a long low arc in the shade step with a lit crest along its north side.</summary>
            public void Drifts(int count, Color shade, Color crest)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    var length = Small ? random.Range(6, 9) : random.Range(11, 16);
                    var (x, y) = Spot(length, 3);
                    for (var step = 0; step < length; step++)
                    {
                        var rise = (int)MathF.Round((Small ? 0.6f : 1.2f) * MathF.Sin(MathF.PI * (step + 0.5f) / length));
                        Put(x + step, y + 2 - rise, shade);
                        if (step > length / 5 && step < length * 4 / 5) Put(x + step, y + 1 - rise, crest);
                    }
                }
            }

            /// <summary>Short diagonal cracks in the edge step, with a lit lip pixel on their north-west side.</summary>
            public void Cracks(int count, Color crack, Color lip)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    if (!Small)
                    {
                        var (x, y) = Spot(5, 3);
                        Put(x, y, crack);
                        Put(x + 1, y + 1, crack);
                        Put(x + 2, y + 1, crack);
                        Put(x + 3, y + 2, crack);
                        Put(x + 4, y + 2, crack);
                        Put(x + 2, y, lip);
                        Put(x + 4, y + 1, lip);
                    }
                    else
                    {
                        var (x, y) = Spot(3, 2);
                        Put(x, y, crack);
                        Put(x + 1, y + 1, crack);
                        Put(x + 2, y + 1, crack);
                    }
                }
            }

            /// <summary>Lichen patches on tundra: a rounded blob in the light step with a highlight centre (smaller at 16 px).</summary>
            public void Lichen(int count, Color light, Color highlight)
            {
                for (var index = 0; index < Count(count); index++)
                {
                    if (!Small)
                    {
                        var (x, y) = Spot(4, 3);
                        Put(x + 1, y, light);
                        Put(x + 2, y, light);
                        Put(x, y + 1, light);
                        Put(x + 1, y + 1, highlight);
                        Put(x + 2, y + 1, light);
                        Put(x + 3, y + 1, light);
                        Put(x + 1, y + 2, light);
                        Put(x + 2, y + 2, light);
                    }
                    else
                    {
                        var (x, y) = Spot(2, 2);
                        Put(x, y, highlight);
                        Put(x + 1, y, light);
                        Put(x, y + 1, light);
                    }
                }
            }

            /// <summary>
            /// Bare rock: the tile is split into a few large angular facets (a
            /// Voronoi pattern that repeats with the tile), each a flat plane in
            /// the light, base or shade step. Where a shaded facet lies just
            /// south or east of a lit one, a one-pixel crease in the edge step
            /// marks the step down, so the stone reads as lit from the
            /// north-west; the gentler boundaries are tone changes only. Both
            /// variants share the facets so they meet without a seam; v1 adds a
            /// crack and stones.
            /// </summary>
            public readonly void Facets(int salt, Ramp ramp)
            {
                // One seed per region of the tile, in 32-unit space, nudged by the salt:
                // 1 light, 0 base, −1 shade. Base is the most common so the tile stays calm.
                (float X, float Y, int Level)[] anchors =
                    [(7f, 7f, 1), (22f, 5f, 0), (15f, 17f, 0), (28f, 19f, 1), (6f, 25f, -1), (20f, 28f, 0)];
                var seeds = new (float X, float Y, int Level)[anchors.Length];
                for (var i = 0; i < anchors.Length; i++)
                {
                    var hash = PixelArt.Hash(i, salt, 313);
                    seeds[i] = (anchors[i].X + (hash & 7) * 0.8f - 2.8f, anchors[i].Y + (hash >> 3 & 7) * 0.8f - 2.8f, anchors[i].Level);
                }
                var scale = 1f / unit;
                int Cell(int x, int y)
                {
                    var u = (x + 0.5f) * scale;
                    var v = (y + 0.5f) * scale;
                    var best = 0;
                    var bestDistance = float.MaxValue;
                    for (var i = 0; i < seeds.Length; i++)
                    {
                        var dx = MathF.Abs(u - seeds[i].X) % 32f;
                        var dy = MathF.Abs(v - seeds[i].Y) % 32f;
                        dx = MathF.Min(dx, 32f - dx); // the pattern repeats with the tile
                        dy = MathF.Min(dy, 32f - dy);
                        var distance = dx * dx + dy * dy;
                        if (distance < bestDistance) (best, bestDistance) = (i, distance);
                    }
                    return best;
                }
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var level = seeds[Cell(x, y)].Level;
                        var steepStep = level < 0 && (seeds[Cell(x - 1, y)].Level > 0 || seeds[Cell(x, y - 1)].Level > 0);
                        if (steepStep) Put(x, y, ramp.Edge);
                        else if (level > 0) Put(x, y, ramp.Light);
                        else if (level < 0) Put(x, y, ramp.Shade);
                    }
            }

            /// <summary>
            /// Keeps the mottling to soft patches. A shade or light pixel
            /// survives only if it has a neighbour of its own kind both across
            /// (west or east) and down (north or south), which removes lone
            /// specks and one-pixel-thin streaks; a base pixel enclosed on three
            /// or four sides by one kind is filled. Neighbours wrap round the
            /// tile so the result still repeats seamlessly. Two passes settle
            /// what the first one exposes.
            /// </summary>
            private static void RemoveSpecks(int[,] classes, int size)
            {
                for (var pass = 0; pass < 2; pass++)
                    for (var y = 0; y < size; y++)
                        for (var x = 0; x < size; x++)
                        {
                            var self = classes[x, y];
                            var west = classes[(x + size - 1) % size, y];
                            var east = classes[(x + 1) % size, y];
                            var north = classes[x, (y + size - 1) % size];
                            var south = classes[x, (y + 1) % size];
                            if (self != 0)
                            {
                                if ((west != self && east != self) || (north != self && south != self)) classes[x, y] = 0;
                                continue;
                            }
                            foreach (var kind in (ReadOnlySpan<int>)[-1, 1])
                            {
                                var around = (west == kind ? 1 : 0) + (east == kind ? 1 : 0) + (north == kind ? 1 : 0) + (south == kind ? 1 : 0);
                                if (around >= 3) classes[x, y] = kind;
                            }
                        }
            }
        }

        /// <summary>
        /// Smooth noise that repeats with the tile, built from soft round bumps
        /// (one per lattice cell, at a hashed offset and strength), so its
        /// thresholds give rounded, organic patches instead of the square
        /// blocks of plain lattice noise. A patch leaving one edge comes back in
        /// on the opposite edge. Within six pixels of the edge both variants use
        /// the same shared bumps and only further in do their own take over, so
        /// v0 and v1 meet without a seam. The 16 px tile samples the same field
        /// at half resolution.
        /// </summary>
        private sealed class Field
        {
            private readonly int size;
            private readonly float[] values;

            /// <summary>One octave: bump spacing in 32-unit tile space (dividing 32) and its weight.</summary>
            public readonly record struct OctaveSpec(int Spacing, float Weight);

            public static OctaveSpec Octave(int spacing, float weight) => new(spacing, weight);

            private Field(int size, int salt, int variant, OctaveSpec[] octaves)
            {
                this.size = size;
                values = new float[size * size];
                var toTile = 32f / size;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var u = (x + 0.5f) * toTile;
                        var v = (y + 0.5f) * toTile;
                        var inside = Math.Clamp(MathF.Min(MathF.Min(u, 32f - u), MathF.Min(v, 32f - v)) / 6f, 0f, 1f);
                        var own = inside * inside * (3f - 2f * inside); // 0 on the edge, 1 six pixels in
                        var sum = 0f;
                        foreach (var (spacing, weight) in octaves)
                        {
                            var octaveSalt = salt + spacing * 7;
                            var shared = Bumps(u, v, spacing, octaveSalt);
                            var mine = own > 0f ? Bumps(u, v, spacing, octaveSalt + 1000 * (variant + 1)) : shared;
                            sum += weight * (shared + (mine - shared) * own);
                        }
                        values[y * size + x] = sum;
                    }
            }

            /// <summary>
            /// The fields of both variants, which agree along the tile edges,
            /// rescaled together so their values run from 0 to 1.
            /// </summary>
            public static Field[] Pair(int size, int salt, params OctaveSpec[] octaves)
            {
                Field[] pair = [new(size, salt, 0, octaves), new(size, salt, 1, octaves)];
                var low = pair.Min(field => field.values.Min());
                var high = pair.Max(field => field.values.Max());
                var scale = high > low ? 1f / (high - low) : 0f;
                foreach (var field in pair)
                    for (var i = 0; i < field.values.Length; i++)
                        field.values[i] = (field.values[i] - low) * scale;
                return pair;
            }

            /// <summary>The value at a pixel; coordinates wrap round the tile.</summary>
            public float this[int x, int y] => values[Wrap(y) * size + Wrap(x)];

            private int Wrap(int index) => (index % size + size) % size;

            /// <summary>Thresholds that put the given fractions of the pooled pixels below and above them.</summary>
            public static (float Low, float High) Thresholds(Field[] fields, float lowFraction, float highFraction)
            {
                var all = fields.SelectMany(field => field.values).OrderBy(value => value).ToArray();
                var low = lowFraction <= 0f ? float.MinValue : all[Math.Clamp((int)(all.Length * lowFraction), 0, all.Length - 1)];
                var high = highFraction <= 0f ? float.MaxValue : all[Math.Clamp((int)(all.Length * (1f - highFraction)), 0, all.Length - 1)];
                return (low, high);
            }

            /// <summary>
            /// Sum of soft bumps at a 32-unit point: each lattice cell holds one
            /// bump of strength −1 to 1 at a hashed offset, reaching one spacing
            /// out. Cells wrap round the tile, so the sum repeats with it.
            /// </summary>
            private static float Bumps(float u, float v, int spacing, int salt)
            {
                var cells = 32 / spacing;
                var cellX = (int)MathF.Floor(u / spacing);
                var cellY = (int)MathF.Floor(v / spacing);
                var reachSquared = spacing * spacing * 1.1f;
                var sum = 0f;
                for (var j = cellY - 2; j <= cellY + 2; j++)
                    for (var i = cellX - 2; i <= cellX + 2; i++)
                    {
                        var wrappedI = (i % cells + cells) % cells;
                        var wrappedJ = (j % cells + cells) % cells;
                        var hash = PixelArt.Hash(wrappedI, wrappedJ, salt);
                        var bumpX = (i + (hash & 0xFF) / 255f) * spacing;
                        var bumpY = (j + (hash >> 8 & 0xFF) / 255f) * spacing;
                        var strength = (hash >> 16 & 0xFF) / 127.5f - 1f;
                        var falloff = 1f - (Sq(u - bumpX) + Sq(v - bumpY)) / reachSquared;
                        if (falloff > 0f) sum += strength * falloff * falloff;
                    }
                return sum;
            }

            private static float Sq(float value) => value * value;
        }
    }
}
