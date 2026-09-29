using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Seamless water: each water style is one block of sixteen by sixteen tiles
/// that repeats across the map, so wave crests, sparkles and shallow-water
/// ripples run across tile edges instead of restarting in every tile. The
/// water keeps its style's flat base color, which the overview, shore bands
/// and water-to-water blends also use; crests gather in some stretches and
/// thin out in others, so the surface reads as swell without color patches.
/// </summary>
public static class WaterTextures
{
    public const int BlockTiles = 16;
    private static readonly TerrainStyle[] Styles =
        [TerrainStyle.Ocean, TerrainStyle.Lake, TerrainStyle.River, TerrainStyle.ShallowWater];
    private static readonly Dictionary<int, ImageTexture> Atlases = [];
    private static readonly Dictionary<int, Image> AtlasImages = [];

    public static ImageTexture Atlas(int atlasTileSize)
    {
        if (Atlases.TryGetValue(atlasTileSize, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(AtlasImage(atlasTileSize));
        Atlases[atlasTileSize] = texture;
        return texture;
    }

    /// <summary>The part of the repeating block that belongs at this map tile.</summary>
    public static Rect2 Region(TerrainStyle style, int x, int y, int atlasTileSize) =>
        new(Mod(x, BlockTiles) * atlasTileSize,
            (Row(style) * BlockTiles + Mod(y, BlockTiles)) * atlasTileSize,
            atlasTileSize, atlasTileSize);

    /// <summary>One style's whole repeating block, for inspection and tests.</summary>
    public static Image Block(TerrainStyle style, int atlasTileSize)
    {
        var period = BlockTiles * atlasTileSize;
        return AtlasImage(atlasTileSize).GetRegion(new Rect2I(0, Row(style) * period, period, period));
    }

    private static int Row(TerrainStyle style) => style switch
    {
        TerrainStyle.Ocean => 0,
        TerrainStyle.Lake => 1,
        TerrainStyle.River => 2,
        TerrainStyle.ShallowWater => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Only water styles have a water block."),
    };

    private static Image AtlasImage(int size)
    {
        if (AtlasImages.TryGetValue(size, out var cached)) return cached;
        var period = BlockTiles * size;
        var image = Image.CreateEmpty(period, period * Styles.Length, false, Image.Format.Rgba8);
        foreach (var style in Styles)
            new BlockPainter(image, Row(style) * period, period, size, style).Paint();
        AtlasImages[size] = image;
        return image;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private readonly record struct Look(int Crests, int CrestLength, float Crest, int Sparkles, bool Caustics);

    private static Look LookFor(TerrainStyle style) => style switch
    {
        // Open sea has the most and longest wave crests.
        TerrainStyle.Ocean => new Look(110, 6, 0.24f, 18, false),
        // Lakes are calmer: fewer and shorter crests.
        TerrainStyle.Lake => new Look(55, 5, 0.2f, 14, false),
        // Rivers ripple more, in short marks.
        TerrainStyle.River => new Look(140, 4, 0.2f, 10, false),
        // Shallow water shows light rippling across the bottom.
        _ => new Look(45, 5, 0.16f, 10, true),
    };

    /// <summary>Paints one wrapped block; every placement repeats across its edges.</summary>
    private sealed class BlockPainter(Image image, int top, int period, int size, TerrainStyle style)
    {
        private readonly Color baseColor = TerrainTextures.BaseColor(style);
        private readonly Look look = LookFor(style);
        private readonly float unit = size / 32f;

        public void Paint()
        {
            PixelArt.Fill(image, new Rect2I(0, top, period, period), baseColor);
            if (look.Caustics) Caustics();
            Crests();
            Sparkles();
        }

        private void Put(int x, int y, Color color) =>
            image.SetPixel(Mod(x, period), top + Mod(y, period), color);

        private Color At(int x, int y) => image.GetPixel(Mod(x, period), top + Mod(y, period));

        /// <summary>
        /// Short arcs lit along their top with a darker trough beneath, spaced
        /// apart so they read as gentle swell rather than noise.
        /// </summary>
        private void Crests()
        {
            var random = new PixelArt.Stream(PixelArt.Hash((int)style, size, 5));
            var swell = new PeriodicNoise(PixelArt.Hash((int)style, size, 71), period);
            var count = (int)(look.Crests * unit * unit) + 4;
            var placed = new List<(int X, int Y)>();
            var spacing = Math.Max(5, (int)(14 * unit));
            for (var attempt = 0; attempt < count * 16 && placed.Count < count; attempt++)
            {
                var x = random.Range(0, period);
                var y = random.Range(0, period);
                // Crests gather where the swell is high and thin out elsewhere.
                if (random.Range(0, 100) / 100f > swell.At(x, y) * 1.4f - 0.2f) continue;
                if (placed.Any(other => WrappedDistance(other.X, x) < spacing * 1.6f && WrappedDistance(other.Y, y) < spacing))
                    continue;
                placed.Add((x, y));
                var length = Math.Max(3, (int)Math.Round((look.CrestLength + random.Range(-2, 3)) * Math.Max(0.6f, unit)));
                var crestColor = PixelArt.Shade(At(x, y), look.Crest);
                var troughColor = PixelArt.Shade(At(x, y + 1), -0.07f);
                // The two end pixels sit one row lower, bending the line into a swell.
                Put(x, y + 1, crestColor);
                Put(x + length - 1, y + 1, crestColor);
                for (var step = 1; step < length - 1; step++)
                {
                    Put(x + step, y, crestColor);
                    if (size >= 32 && step > 1 && step < length - 2) Put(x + step, y + 1, troughColor);
                }
            }
        }

        private void Sparkles()
        {
            var random = new PixelArt.Stream(PixelArt.Hash((int)style, size, 9));
            var sparkle = PixelArt.Shade(baseColor, 0.42f);
            for (var index = 0; index < look.Sparkles; index++)
            {
                var x = random.Range(0, period);
                var y = random.Range(0, period);
                Put(x, y, sparkle);
                if (size >= 32 && index % 3 == 0)
                {
                    var glint = PixelArt.Shade(baseColor, 0.22f);
                    Put(x - 1, y, glint);
                    Put(x + 1, y, glint);
                }
            }
        }

        /// <summary>
        /// Light playing on a shallow bottom: the thin boundaries between
        /// wrapped cells are drawn one step lighter, like a loose net.
        /// </summary>
        private void Caustics()
        {
            // One jittered point per grid cell, so each pixel only needs to
            // look at the nine cells around it.
            var random = new PixelArt.Stream(PixelArt.Hash((int)style, size, 13));
            var cells = BlockTiles * 3 / 2;
            var cell = (float)period / cells;
            var points = new (float X, float Y)[cells * cells];
            for (var cy = 0; cy < cells; cy++)
                for (var cx = 0; cx < cells; cx++)
                    points[cy * cells + cx] = ((cx + random.Range(10, 90) / 100f) * cell, (cy + random.Range(10, 90) / 100f) * cell);
            var threshold = Math.Max(0.7f, 1.1f * unit);
            var patches = new PeriodicNoise(PixelArt.Hash((int)style, size, 29), period);
            for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                {
                    // Only part of the net shows at a time, so it reads as
                    // moving light rather than cracked ground.
                    if (patches.At(x, y) < 0.52f) continue;
                    var nearest = float.MaxValue;
                    var second = float.MaxValue;
                    var homeX = (int)(x / cell);
                    var homeY = (int)(y / cell);
                    for (var oy = -1; oy <= 1; oy++)
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            var (px, py) = points[Mod(homeY + oy, cells) * cells + Mod(homeX + ox, cells)];
                            var dx = WrappedDistance(px, x);
                            var dy = WrappedDistance(py, y);
                            var distance = MathF.Sqrt(dx * dx + dy * dy);
                            if (distance < nearest)
                            {
                                second = nearest;
                                nearest = distance;
                            }
                            else if (distance < second)
                            {
                                second = distance;
                            }
                        }
                    if (second - nearest < threshold)
                        image.SetPixel(x, top + y, PixelArt.Shade(image.GetPixel(x, top + y), 0.055f));
                }
        }

        private float WrappedDistance(float a, float b)
        {
            var distance = MathF.Abs(a - b);
            return MathF.Min(distance, period - distance);
        }
    }

    /// <summary>
    /// Three octaves of gradient noise that repeat exactly every
    /// <c>period</c> pixels; gradient noise avoids the axis-aligned streaks
    /// of plain value noise, so patches read as rounded pools.
    /// </summary>
    private readonly struct PeriodicNoise(uint seed, int period)
    {
        public float At(int x, int y) =>
            0.5f + Octave(x, y, 4, 0) * 0.55f + Octave(x, y, 8, 1) * 0.3f + Octave(x, y, 16, 2) * 0.15f;

        private float Octave(int x, int y, int cells, int salt)
        {
            var cell = (float)period / cells;
            var fx = x / cell;
            var fy = y / cell;
            var x0 = (int)MathF.Floor(fx);
            var y0 = (int)MathF.Floor(fy);
            var tx = fx - x0;
            var ty = fy - y0;
            var top = Lerp(Gradient(x0, y0, tx, ty, cells, salt), Gradient(x0 + 1, y0, tx - 1, ty, cells, salt), Fade(tx));
            var bottom = Lerp(Gradient(x0, y0 + 1, tx, ty - 1, cells, salt), Gradient(x0 + 1, y0 + 1, tx - 1, ty - 1, cells, salt), Fade(tx));
            return Lerp(top, bottom, Fade(ty));
        }

        private float Gradient(int cx, int cy, float dx, float dy, int cells, int salt)
        {
            var angle = PixelArt.Hash(Mod(cx, cells), Mod(cy, cells), (int)seed + salt * 131) % 1024 / 1024f * MathF.Tau;
            return MathF.Cos(angle) * dx + MathF.Sin(angle) * dy;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    }
}
