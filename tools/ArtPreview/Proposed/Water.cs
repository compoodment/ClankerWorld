using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Water;

/// <summary>
/// Proposed water: the four seamless 16 × 16-tile blocks (ocean, lake, river,
/// shallow water) redrawn with gentle depth blotches, ramp-coloured crests and
/// a few sparkles, plus a shallow river ford overlay for a one-tile crossing.
/// Base colours are unchanged (style rule W1); every mark wraps across the
/// block edges so the repeat stays invisible; everything is drawn from fixed
/// seeds with <see cref="PixelArt.Hash"/> and <see cref="PixelArt.Stream"/>.
/// </summary>
public sealed class WaterProposal : IArtProposal, IArtSetProvider
{
    public string Family => "water";
    public string Name => "water";

    /// <summary>Tiles per side of one repeating block, as in <see cref="WaterTextures.BlockTiles"/>.</summary>
    public const int BlockTiles = WaterTextures.BlockTiles;

    private static readonly TerrainStyle[] Styles =
        [TerrainStyle.Ocean, TerrainStyle.Lake, TerrainStyle.River, TerrainStyle.ShallowWater];

    /// <summary>Finished blocks per (style, atlas tile size), so the scene and the sheet share one drawing.</summary>
    private static readonly Dictionary<(TerrainStyle Style, int Size), Image> Blocks = [];

    public IEnumerable<Entry> Render()
    {
        // The 32 px blocks and fords first, their 16 px versions in the row
        // beneath, so the contact sheet lines the two sizes up.
        foreach (var size in new[] { 32, 16 })
        {
            var suffix = size == 32 ? string.Empty : ".16";
            foreach (var style in Styles)
                yield return new(Family, $"{style}.block{suffix}", Block(style, size),
                    $"Seamless {BlockTiles}×{BlockTiles}-tile block at {size} px; the map draws tile (x, y) from (x mod {BlockTiles}, y mod {BlockTiles}).");
            var riverTile = Block(TerrainStyle.River, size).GetRegion(new Rect2I(0, 0, size, size));
            foreach (var northSouth in new[] { true, false })
            {
                var id = northSouth ? "ford.ns" : "ford.ew";
                var sprite = Ford(northSouth, size);
                yield return new(Family, id + suffix, Bitmap.Over(riverTile, sprite, 0, 0),
                    "Shallow one-tile river crossing: stepping stones with lighter water between, shown over a river tile.");
                yield return new(Family, id + ".sprite" + suffix, sprite, "The bare transparent ford overlay.");
            }
        }
    }

    public void Apply(ArtSet set)
    {
        var fallback = set.WaterTile;
        set.WaterTile = (style, x, y, atlasTileSize) =>
        {
            if (Array.IndexOf(Styles, style) < 0 || atlasTileSize is not (32 or 16)) return fallback(style, x, y, atlasTileSize);
            return Block(style, atlasTileSize).GetRegion(new Rect2I(
                Mod(x, BlockTiles) * atlasTileSize, Mod(y, BlockTiles) * atlasTileSize, atlasTileSize, atlasTileSize));
        };
    }

    /// <summary>One style's whole repeating block at the given atlas tile size (32 or 16 px).</summary>
    public static Image Block(TerrainStyle style, int atlasTileSize)
    {
        if (Blocks.TryGetValue((style, atlasTileSize), out var cached)) return cached;
        var block = new BlockPainter(style, atlasTileSize).Paint();
        Blocks[(style, atlasTileSize)] = block;
        return block;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>The five steps of a water ramp from the style guide's master palette.</summary>
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
    /// block painted in the depth tone (rule W3), crest counts are per 32 px
    /// block and scale with the tile size, and the trough and sparkle values
    /// are blend weights toward the shade and highlight steps.
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

        private void Put(int x, int y, Color color) => image.SetPixel(Mod(x, period), Mod(y, period), color);

        private Color At(int x, int y) => image.GetPixel(Mod(x, period), Mod(y, period));

        /// <summary>
        /// Depth variation in two calm layers: a very faint slow swell two to
        /// four tiles across, and on top rounded blotches one to three tiles
        /// across, each cut from periodic noise at the level that covers the
        /// style's share of the block. The blotch is a blend toward one ramp
        /// step with a half-strength rim, so its edge is soft but never a
        /// gradient band (rule W3, S5).
        /// </summary>
        private void Blotches()
        {
            var toward = look.BlotchDark ? ramp.Shade : ramp.Light;
            // Slow swell: broad, barely there, so the surface is not one flat sheet.
            var swell = Threshold(new PeriodicNoise(PixelArt.Hash((int)style, 1, 37), period, [2, 4], [0.7f, 0.3f]), 0.3f);
            var swellTone = ramp.Base.Lerp(toward, look.BlotchMix * 0.35f);
            for (var y = 0; y < period; y++)
                for (var x = 0; x < period; x++)
                    if (swell[y * period + x]) image.SetPixel(x, y, swellTone);
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
                    image.SetPixel(x, y, edge ? image.GetPixel(x, y).Lerp(tone, 0.5f) : tone);
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
        /// elsewhere (rule W2). The two end pixels sit one row lower, bending
        /// the dash into a crest.
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
        /// Light playing on a shallow bottom, kept from the current art (rule
        /// W4): the thin boundaries between jittered wrapped cells, drawn a
        /// step lighter where a patch field says the net shows.
        /// </summary>
        private void Caustics()
        {
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
                        image.SetPixel(x, y, image.GetPixel(x, y).Lerp(ramp.Light, 0.4f));
                }
        }

        private float WrappedDistance(float a, float b)
        {
            var distance = MathF.Abs(a - b);
            return MathF.Min(distance, period - distance);
        }
    }

    /// <summary>
    /// The ford overlay (rule W5): a band of lighter water across the tile with
    /// five flat stepping stones in the Rock ramp, lit from the north-west.
    /// <paramref name="northSouth"/> runs the crossing from the north edge to
    /// the south edge (over an east–west river); otherwise it runs east–west.
    /// </summary>
    public static Image Ford(bool northSouth, int atlasTileSize)
    {
        var image = Bitmap.Empty(atlasTileSize, atlasTileSize);
        var unit = atlasTileSize / 32f;
        var shallow = RampFor(TerrainStyle.ShallowWater);
        var rock = new Ramp(new("4A4542"), new("625B56"), new("756D68"), new("8B837D"), new("A49C95"));
        var random = new PixelArt.Stream(PixelArt.Hash(northSouth ? 1 : 2, atlasTileSize, 83));
        // Lighter water: a band 12 units wide whose edge wanders by a pixel,
        // with a half-alpha rim so it meets the river softly.
        var half = 6f;
        var wander = new int[32];
        for (var i = 0; i < 32; i++) wander[i] = random.Range(0, 3) - 1;
        for (var py = 0; py < atlasTileSize; py++)
            for (var px = 0; px < atlasTileSize; px++)
            {
                var along = (northSouth ? py : px) / unit;
                var across = ((northSouth ? px : py) + 0.5f) / unit - 16f + wander[(int)along % 32] * 0.5f;
                var distance = MathF.Abs(across);
                if (distance < half - 1 / unit) image.SetPixel(px, py, new Color(shallow.Light, 0.85f));
                else if (distance < half) image.SetPixel(px, py, new Color(shallow.Light, 0.45f));
            }
        var canvas = new PixelCanvas(image, new Rect2I(0, 0, atlasTileSize, atlasTileSize), unit);
        // Five stones, 6.5 units apart, each nudged across the band so the
        // line is not ruler-straight.
        for (var index = 0; index < 5; index++)
        {
            var alongCenter = 3.5f + index * 6.25f;
            var acrossCenter = 16f + (random.Range(0, 3) - 1) * 0.75f;
            var (cx, cy) = northSouth ? (acrossCenter, alongCenter) : (alongCenter, acrossCenter);
            var rx = northSouth ? 2.4f : 2.8f;
            var ry = northSouth ? 2.8f : 2.4f;
            if (atlasTileSize < 32)
            {
                // At 16 px a stone is a 2 × 2 px dot (4 units): a dark edge
                // with a lit north-west pixel.
                canvas.Rect(cx - 2, cy - 2, 4, 4, rock.Edge);
                canvas.Rect(cx - 2, cy - 2, 2, 2, rock.Light);
                continue;
            }
            // Flat top lit from the north-west: edge outline, shade crescent
            // on the south-east, base top, light crescent on the north-west.
            canvas.Ellipse(cx, cy, rx + 1, ry + 1, rock.Edge);
            canvas.Ellipse(cx, cy, rx, ry, rock.Shade);
            canvas.Ellipse(cx - 0.5f, cy - 0.5f, rx - 0.5f, ry - 0.5f, rock.Base);
            canvas.Ellipse(cx - 1f, cy - 1f, rx - 1.5f, ry - 1.5f, rock.Light);
            // A ripple pixel lapping the stone's downstream (south-east) side.
            canvas.Dot(cx + rx + 0.5f, cy + 1, shallow.Highlight);
        }
        return image;
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
