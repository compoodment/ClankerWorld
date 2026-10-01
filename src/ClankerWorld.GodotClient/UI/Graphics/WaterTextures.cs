using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Seamless water: each water style is one block of sixteen by sixteen tiles
/// that repeats across the map, so depth pools, wave crests, sparkles and
/// shallow-water ripples run across tile edges instead of restarting in every
/// tile. The water keeps its style's flat base color, which the overview,
/// shore bands and water-to-water blends also use. Soft, rounded pools one to
/// three tiles across give it depth: darker on the ocean, lighter on lakes,
/// a faint lift on rivers and sandy patches under the shallow-water net.
/// Crests gather in some stretches and thin out in others, so the surface
/// reads as swell rather than one flat sheet.
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
            image.BlitRect(new BlockPainter(style, size).Paint(), new Rect2I(0, 0, period, period),
                new Vector2I(0, Row(style) * period));
        AtlasImages[size] = image;
        return image;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>The five steps of a water ramp; the base is the style's overview colour.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight);

    private static Ramp RampFor(TerrainStyle style) => style switch
    {
        TerrainStyle.Ocean => new(new("24405C"), new("2A4F73"), new("325F89"), new("3E6F9A"), new("5C8DB5")),
        TerrainStyle.Lake => new(new("3A5F7A"), new("4A7B9D"), new("598FB3"), new("6A9FC0"), new("8ABBD6")),
        TerrainStyle.River => new(new("2F5A75"), new("3B7294"), new("4786AB"), new("5695B8"), new("7FB4CF")),
        _ => new(new("345A78"), new("3F6E92"), new("4B7FA7"), new("6A9AC0"), new("9CC3DB")),
    };

    /// <summary>
    /// What a style's surface carries. Blotch coverage is the share of the
    /// block painted in the depth tone, crest counts are per 32 px block and
    /// scale with the tile size, and the blotch mix is the blend weight toward
    /// the shade or light step.
    /// </summary>
    private readonly record struct Look(
        float BlotchCoverage, bool BlotchDark, float BlotchMix, int Crests, int CrestLength, int Sparkles, bool Caustics);

    private static Look LookFor(TerrainStyle style) => style switch
    {
        // Open sea: dark depth blotches over a sixth of the block, the longest crests.
        TerrainStyle.Ocean => new(0.18f, true, 0.5f, 96, 5, 14, false),
        // Lakes: lighter pools, calmer and shorter crests.
        TerrainStyle.Lake => new(0.15f, false, 0.45f, 50, 4, 12, false),
        // Rivers stay the clearest: a faint lift of light, short ripples.
        TerrainStyle.River => new(0.09f, false, 0.35f, 120, 3, 8, false),
        // Shallow water: light sandy patches beneath the caustic net.
        _ => new(0.12f, false, 0.4f, 40, 4, 8, true),
    };

    /// <summary>Paints one wrapped block; every placement repeats across its edges.</summary>
    private sealed class BlockPainter(TerrainStyle style, int size)
    {
        private readonly int period = BlockTiles * size;
        private readonly float unit = size / 32f;
        private readonly Ramp ramp = RampFor(style);
        private readonly Look look = LookFor(style);
        private readonly Image image = Image.CreateEmpty(BlockTiles * size, BlockTiles * size, false, Image.Format.Rgba8);

        public Image Paint()
        {
            image.Fill(ramp.Base);
            Blotches();
            if (look.Caustics) Caustics();
            Crests();
            Sparkles();
            return image;
        }

        private void Put(int x, int y, Color color) => Set(Mod(x, period), Mod(y, period), color);

        private Color At(int x, int y) => image.GetPixel(Mod(x, period), Mod(y, period));

        /// <summary>
        /// Stores a colour on the nearest of the 8-bit steps the image holds.
        /// Godot truncates a channel when it stores it, so blended tones are
        /// snapped first to keep the game's pixels exactly as drawn.
        /// </summary>
        private void Set(int x, int y, Color color) =>
            image.SetPixel(x, y, new Color(Step(color.R), Step(color.G), Step(color.B), Step(color.A)));

        private static float Step(float channel) => Math.Clamp(MathF.Round(channel * 255f), 0f, 255f) / 255f;

        /// <summary>
        /// Depth variation in two calm layers: a very faint slow swell two to
        /// four tiles across, and on top rounded blotches one to three tiles
        /// across, each cut from periodic noise at the level that covers the
        /// style's share of the block. The blotch is a blend toward one ramp
        /// step with a half-strength rim, so its edge is soft but never a
        /// gradient band.
        /// </summary>
        private void Blotches()
        {
            var toward = look.BlotchDark ? ramp.Shade : ramp.Light;
            // Slow swell: broad, barely there, so the surface is not one flat sheet.
            var swell = Threshold(new PeriodicNoise(PixelArt.Hash((int)style, 1, 37), period, [2, 4], [0.7f, 0.3f]), 0.3f);
            var swellTone = ramp.Base.Lerp(toward, look.BlotchMix * 0.35f);
            for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                    if (swell[y * period + x]) Set(x, y, swellTone);
            // Blotches: the depth marks the eye reads, with a soft rim. Specks
            // smaller than a few pixels would read as noise, so they go.
            var inside = Threshold(new PeriodicNoise(PixelArt.Hash((int)style, 1, 41), period, [4, 8, 16], [0.6f, 0.3f, 0.1f]), look.BlotchCoverage);
            DropSpecks(inside, (int)(40 * unit * unit));
            var tone = ramp.Base.Lerp(toward, look.BlotchMix);
            bool Inside(int x, int y) => inside[Mod(y, period) * period + Mod(x, period)];
            for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                {
                    if (!Inside(x, y)) continue;
                    var edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                    Set(x, y, edge ? image.GetPixel(x, y).Lerp(tone, 0.5f) : tone);
                }
        }

        /// <summary>
        /// The pixels whose noise value lies below the level that covers
        /// <paramref name="coverage"/> of the block. The level is read off a
        /// histogram, so the coverage is the same whatever the seed produces.
        /// </summary>
        private bool[] Threshold(PeriodicNoise noise, float coverage)
        {
            var field = new float[period * period];
            var histogram = new int[256];
            for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                {
                    var value = noise.At(x, y);
                    field[y * period + x] = value;
                    histogram[Math.Clamp((int)(value * 255f), 0, 255)]++;
                }
            var wanted = (int)(coverage * field.Length);
            var cut = 0;
            for (var seen = 0; cut < 255 && seen + histogram[cut] < wanted; cut++) seen += histogram[cut];
            var level = cut / 255f;
            var inside = new bool[field.Length];
            for (var index = 0; index < field.Length; index++) inside[index] = field[index] < level;
            return inside;
        }

        /// <summary>
        /// Clears every connected patch of the wrapped mask that holds fewer
        /// than <paramref name="minimum"/> pixels (four-connected, across
        /// the block edges), so blotches never shrink to specks.
        /// </summary>
        private void DropSpecks(bool[] mask, int minimum)
        {
            var seen = new bool[mask.Length];
            var patch = new List<int>();
            var queue = new Queue<int>();
            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (var start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || seen[start]) continue;
                patch.Clear();
                queue.Enqueue(start);
                seen[start] = true;
                while (queue.Count > 0)
                {
                    var index = queue.Dequeue();
                    patch.Add(index);
                    var x = index % period;
                    var y = index / period;
                    foreach (var (dx, dy) in steps)
                    {
                        var next = Mod(y + dy, period) * period + Mod(x + dx, period);
                        if (!mask[next] || seen[next]) continue;
                        seen[next] = true;
                        queue.Enqueue(next);
                    }
                }
                if (patch.Count < minimum)
                    foreach (var index in patch) mask[index] = false;
            }
        }

        /// <summary>
        /// Short arcs in the highlight step with a one-pixel trough toward the
        /// shade step beneath, gathered where a swell field is high and sparse
        /// elsewhere. The two end pixels sit one row lower, bending the dash
        /// into a crest.
        /// </summary>
        private void Crests()
        {
            var random = new PixelArt.Stream(PixelArt.Hash((int)style, size, 5));
            var swell = new PeriodicNoise(PixelArt.Hash((int)style, 2, 71), period, [4, 8, 16], [0.55f, 0.3f, 0.15f]);
            var count = (int)(look.Crests * unit * unit) + 4;
            var placed = new List<(int X, int Y)>();
            var spacing = Math.Max(5, (int)(14 * unit));
            for (var attempt = 0; attempt < count * 16 && placed.Count < count; attempt++)
            {
                var x = random.Range(0, period);
                var y = random.Range(0, period);
                if (random.Range(0, 100) / 100f > swell.At(x, y) * 1.4f - 0.2f) continue;
                if (placed.Any(other => WrappedDistance(other.X, x) < spacing * 1.6f && WrappedDistance(other.Y, y) < spacing))
                    continue;
                placed.Add((x, y));
                var length = Math.Clamp((int)Math.Round((look.CrestLength + random.Range(-1, 2)) * Math.Max(0.6f, unit)), 2, 5);
                var crest = ramp.Highlight;
                for (var step = 0; step < length; step++)
                {
                    var end = length > 2 && (step == 0 || step == length - 1);
                    var row = end ? y + 1 : y;
                    Put(x + step, row, crest);
                    if (size >= 32 && !end) Put(x + step, row + 1, At(x + step, row + 1).Lerp(ramp.Shade, 0.6f));
                }
            }
        }

        /// <summary>A few single highlight pixels, three in a row where a glint catches.</summary>
        private void Sparkles()
        {
            var random = new PixelArt.Stream(PixelArt.Hash((int)style, size, 9));
            for (var index = 0; index < look.Sparkles; index++)
            {
                var x = random.Range(0, period);
                var y = random.Range(0, period);
                Put(x, y, ramp.Highlight);
                if (size >= 32 && index % 3 == 0)
                {
                    Put(x - 1, y, ramp.Light);
                    Put(x + 1, y, ramp.Light);
                }
            }
        }

        /// <summary>
        /// Light playing on a shallow bottom: the thin boundaries between
        /// jittered wrapped cells, drawn a step lighter where a patch field
        /// says the net shows, so it reads as moving light rather than
        /// cracked ground.
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
            var patches = new PeriodicNoise(PixelArt.Hash((int)style, size, 29), period, [4, 8, 16], [0.55f, 0.3f, 0.15f]);
            for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                {
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
                        Set(x, y, image.GetPixel(x, y).Lerp(ramp.Light, 0.4f));
                }
        }

        private float WrappedDistance(float a, float b)
        {
            var distance = MathF.Abs(a - b);
            return MathF.Min(distance, period - distance);
        }
    }

    /// <summary>
    /// Octaves of gradient noise that repeat exactly every <c>period</c>
    /// pixels; gradient noise avoids the axis-aligned streaks of value noise,
    /// so blotches read as rounded pools. Values centre on 0.5.
    /// </summary>
    private readonly struct PeriodicNoise(uint seed, int period, int[] cells, float[] weights)
    {
        public float At(int x, int y)
        {
            var value = 0.5f;
            for (var octave = 0; octave < cells.Length; octave++) value += Octave(x, y, cells[octave], octave) * weights[octave];
            return value;
        }

        private float Octave(int x, int y, int count, int salt)
        {
            var cell = (float)period / count;
            var fx = x / cell;
            var fy = y / cell;
            var x0 = (int)MathF.Floor(fx);
            var y0 = (int)MathF.Floor(fy);
            var tx = fx - x0;
            var ty = fy - y0;
            var top = Lerp(Gradient(x0, y0, tx, ty, count, salt), Gradient(x0 + 1, y0, tx - 1, ty, count, salt), Fade(tx));
            var bottom = Lerp(Gradient(x0, y0 + 1, tx, ty - 1, count, salt), Gradient(x0 + 1, y0 + 1, tx - 1, ty - 1, count, salt), Fade(tx));
            return Lerp(top, bottom, Fade(ty));
        }

        private float Gradient(int cx, int cy, float dx, float dy, int count, int salt)
        {
            var angle = PixelArt.Hash(Mod(cx, count), Mod(cy, count), (int)seed + salt * 131) % 1024 / 1024f * MathF.Tau;
            return MathF.Cos(angle) * dx + MathF.Sin(angle) * dy;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    }
}
