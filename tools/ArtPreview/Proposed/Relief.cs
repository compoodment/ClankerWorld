using System.Diagnostics;
using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Relief;

/// <summary>
/// Mountains, peaks and hills drawn as one landform from the map's elevation
/// instead of one small picture per tile. A smooth height field is built from
/// the tile elevations, given spurs and gullies that run downhill from the
/// range's spine, and shaded as seen from straight above with the light in
/// the north-west: rock and snow on mountain and peak ground, and soft,
/// see-through slope shading over the grass or forest of the foothills.
/// </summary>
public sealed class ReliefProposal : IArtProposal, IArtSetProvider
{
    public string Family => "relief";
    public string Name => "relief";

    public IEnumerable<Entry> Render()
    {
        var range = SceneSpec.MountainRange();
        var set = new ArtSet { Name = Name };
        Apply(set);
        var range32 = SceneComposer.Render(range, set, 32);
        yield return new(Family, "range.32", range32,
            "The mountain range at 32 px: one massif with a snowy spine of peaks, spurs and gullies down both flanks, foothill slopes over the grass and forest");
        yield return new(Family, "range.16", SceneComposer.Render(range, set, 16),
            "The same range at the 16 px mid-zoom size, drawn from the same height field");
        var town = SceneSpec.TownCorner();
        yield return new(Family, "town.32", SceneComposer.Render(town, set, 32),
            "The reference Town corner: its small mountain in the north-east and the foothills around it");
        yield return new(Family, "town.16", SceneComposer.Render(town, set, 16), "The Town corner at 16 px");
        yield return ChunkSeams(range);
        yield return new(Family, "massif.closeup", range32.GetRegion(new Rect2I(5 * 32, 5 * 32, 10 * 32, 8 * 32)),
            "A 10 × 8-tile crop of the range at 32 px, 1×: spine, spurs, snow and the massif's edge");
    }

#if DEBUG
    private const string BuildKind = "unoptimised debug";
#else
    private const string BuildKind = "optimised";
#endif

    public void Apply(ArtSet set) =>
        set.Relief = (map, tileSize) => RenderRelief(map, new Rect2I(0, 0, map.Width, map.Height), tileSize);

    /// <summary>
    /// The range drawn from four separately rendered quadrant chunks, with a
    /// note counting pixels that differ from one whole-map render, checking
    /// the east–west wrap seam, and timing one 16 × 16-tile chunk.
    /// </summary>
    private Entry ChunkSeams(SceneSpec range)
    {
        var map = range.Map();
        var halfW = map.Width / 2;
        var halfH = map.Height / 2;
        var chunked = new ArtSet { Name = Name };
        Image? pasted = null;
        chunked.Relief = (m, size) =>
        {
            var whole = Image.CreateEmpty(m.Width * size, m.Height * size, false, Image.Format.Rgba8);
            foreach (var (x, y, w, h) in new[] { (0, 0, halfW, halfH), (halfW, 0, m.Width - halfW, halfH), (0, halfH, halfW, m.Height - halfH), (halfW, halfH, m.Width - halfW, m.Height - halfH) })
            {
                var chunk = RenderRelief(m, new Rect2I(x, y, w, h), size);
                whole.BlitRect(chunk, new Rect2I(0, 0, chunk.GetWidth(), chunk.GetHeight()), new Vector2I(x * size, y * size));
            }
            pasted = whole;
            return whole;
        };
        var seams = SceneComposer.Render(range, chunked, 32);
        var single = RenderRelief(map, new Rect2I(0, 0, map.Width, map.Height), 32);
        var differing = CountDifferences(single, pasted!);

        // Every pixel over a Mountain or Peak tile must be opaque, at both sizes.
        var seeThrough = 0;
        foreach (var size in new[] { 32, 16 })
        {
            var layer = size == 32 ? single : RenderRelief(map, new Rect2I(0, 0, map.Width, map.Height), size);
            for (var y = 0; y < map.Height * size; y++)
                for (var x = 0; x < map.Width * size; x++)
                    if (map.StyleAt(x / size, y / size) is TerrainStyle.Mountain or TerrainStyle.Peak && layer.GetPixel(x, y).A < 1f)
                        seeThrough++;
        }

        // On a map that wraps east–west, a chunk reaching past the east edge
        // must continue exactly into the chunk at the west edge.
        var wrapped = WorldTerrainMap.FromPacked(
            new OwnerWorldPackedTerrain(range.Width, range.Height, "terrain-kind-v1", Convert.ToBase64String(new byte[range.Width * range.Height])),
            new OwnerWorldPackedMapLayers(range.Width, range.Height, "map-layers-v1",
                Convert.ToBase64String(Enumerable.Repeat((byte)2, range.Width * range.Height).ToArray()), Convert.ToBase64String(range.Elevation),
                Convert.ToBase64String(range.Hydrology), Convert.ToBase64String(range.Surface), Convert.ToBase64String(range.Vegetation)),
            wrapsEastWest: true);
        var across = RenderRelief(wrapped, new Rect2I(map.Width - 6, 0, 12, map.Height), 32);
        var west = RenderRelief(wrapped, new Rect2I(0, 0, 6, map.Height), 32);
        var wrapDiffering = CountDifferences(across.GetRegion(new Rect2I(6 * 32, 0, 6 * 32, map.Height * 32)), west);

        // Timing of one 16 × 16-tile chunk at 32 px over the range: the
        // fastest of a few runs after a warm-up, so other work on the machine
        // does not count.
        RenderRelief(map, new Rect2I(0, 0, 16, 16), 32);
        var milliseconds = double.MaxValue;
        for (var run = 0; run < 7; run++)
        {
            var watch = Stopwatch.StartNew();
            RenderRelief(map, new Rect2I(8, 2, 16, 16), 32);
            milliseconds = Math.Min(milliseconds, watch.Elapsed.TotalMilliseconds);
        }
        return new(Family, "chunk-seams", seams,
            $"Four 16 × 10-tile chunks rendered separately and pasted together: {differing} pixels differ from a single render, " +
            $"{wrapDiffering} differ across the east–west wrap seam, and {seeThrough} pixels over Mountain or Peak tiles are not opaque. " +
            $"A 16 × 16-tile chunk at 32 px over the range took {milliseconds:0} ms in this {BuildKind} build");
    }

    /// <summary>
    /// Draws the relief layer for a rectangle of tiles: an image of
    /// <c>tiles.Size × tileSize</c> pixels to blend over the ground before
    /// roads. It is opaque over Mountain and Peak tiles, fades onto the land
    /// around them and is see-through foothill shading on hill tiles. Every
    /// pixel is a function of global map position only, and neighbours outside
    /// the rectangle are sampled, so separately rendered chunks join without
    /// seams; the map's east–west wrap is honoured. The rectangle may reach
    /// past the east or west edge of a wrapping map.
    /// </summary>
    public static Image RenderRelief(WorldTerrainMap map, Rect2I tiles, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (tiles.Size.X <= 0 || tiles.Size.Y <= 0 || tileSize <= 0)
            return Image.CreateEmpty(Math.Max(1, tiles.Size.X * tileSize), Math.Max(1, tiles.Size.Y * tileSize), false, Image.Format.Rgba8);
        return new Painter(map, tiles, tileSize).Paint();
    }

    private static int CountDifferences(Image a, Image b)
    {
        var first = a.GetData();
        var second = b.GetData();
        var count = 0;
        for (var index = 0; index < first.Length; index += 4)
            if (first[index] != second[index] || first[index + 1] != second[index + 1] ||
                first[index + 2] != second[index + 2] || first[index + 3] != second[index + 3])
                count++;
        return count;
    }

    /// <summary>Colour ramps from the style guide, darkest first: edge, shade, base, light, highlight.</summary>
    private static class Ramps
    {
        public static readonly Color[] Rock = Ramp("4A4542", "625B56", "756D68", "8B837D", "A49C95");
        /// <summary>Bare rock beside the snow: the Rock ramp drawn toward the Peak ramp's cooler greys.</summary>
        public static readonly Color[] HighRock = Ramp("524D4B", "6C6764", "837F7B", "9C9995", "B9B9B5");
        public static readonly Color[] Snow = Ramp("9AAAA8", "B8C6C4", "CCD7D1", "DCE5E0", "F4F8F6");

        /// <summary>Warm sunlight and cool blue-green shade laid over foothill ground.</summary>
        public static readonly Color HillLight = new("F4EBC0");
        public static readonly Color HillShade = new("1E3340");

        private static Color[] Ramp(params string[] hex) => hex.Select(value => new Color(value)).ToArray();
    }

    /// <summary>What a tile contributes to the relief.</summary>
    private enum Ground : byte { Land, Massif, Water }

    /// <summary>What a rock pixel shows, before light.</summary>
    private enum Surface : byte { Rock, HighRock, Snow }

    /// <summary>
    /// Renders one rectangle. Fields are computed on a pixel window a few
    /// pixels larger than the output, so slopes, lines and rims at the edge of
    /// the rectangle see their real neighbours.
    /// </summary>
    private sealed class Painter
    {
        // Margin in pixels around the output for slopes, lines and the rim.
        private const int Margin = 3;
        // Margin in tiles for the smooth kernel and the spurs.
        private const int TileMargin = 5;

        // The height field, in elevation units (0–255).
        private const float WaterFloor = 172f;       // rivers and lakes carve the height field down to this
        private const float SummitJitter = 9f;       // per-tile summit variation, so a flat-topped spine breaks into peaks
        private const float SpurHeight = 7f;         // height of the spurs on the massif
        private const float SpursPerTile = 1.0f;     // spurs across one tile of slope
        private const float SpurLength = 1.0f;       // kernel reach down the slope, in tiles
        private const float SpurBreadth = 0.8f;      // kernel reach across the slope, in tiles

        // Light comes from the north-west; slopes in elevation units per tile are scaled by this.
        private const float Exaggeration = 1f / 30f;

        private static readonly object CoastLock = new();
        private static readonly Dictionary<(int Piece, int Size), byte[]> CoastMasks = [];

        private readonly WorldTerrainMap map;
        private readonly Rect2I tiles;
        private readonly int size;
        private readonly int wrapWidth;

        // Tile window (global, unwrapped coordinates).
        private readonly int tileLeft, tileTop, tileWidth, tileHeight;
        private readonly float[] elevation;
        private readonly Ground[] ground;
        private readonly float[] hillWeight;
        private readonly float[] peak;
        private readonly Dictionary<int, (byte[]? Massif, byte[]? Land)> coastCovers = [];

        // Pixel window (global, unwrapped coordinates).
        private readonly int pixelLeft, pixelTop, pixelWidth, pixelHeight;

        public Painter(WorldTerrainMap map, Rect2I tiles, int size)
        {
            this.map = map;
            this.tiles = tiles;
            this.size = size;
            wrapWidth = map.WrapsEastWest ? map.Width : 0;
            tileLeft = tiles.Position.X - TileMargin;
            tileTop = tiles.Position.Y - TileMargin;
            tileWidth = tiles.Size.X + TileMargin * 2;
            tileHeight = tiles.Size.Y + TileMargin * 2;
            elevation = new float[tileWidth * tileHeight];
            ground = new Ground[tileWidth * tileHeight];
            hillWeight = new float[tileWidth * tileHeight];
            peak = new float[tileWidth * tileHeight];
            for (var j = 0; j < tileHeight; j++)
                for (var i = 0; i < tileWidth; i++)
                    ReadTile(tileLeft + i, tileTop + j, j * tileWidth + i);
            pixelLeft = tiles.Position.X * size - Margin;
            pixelTop = tiles.Position.Y * size - Margin;
            pixelWidth = tiles.Size.X * size + Margin * 2;
            pixelHeight = tiles.Size.Y * size + Margin * 2;
        }

        private int MapX(int x) => wrapWidth > 0 ? Wrap(x, wrapWidth) : Math.Clamp(x, 0, map.Width - 1);

        private TerrainStyle StyleAt(int x, int y) => map.StyleAt(MapX(x), Math.Clamp(y, 0, map.Height - 1));

        private static bool IsMassif(TerrainStyle style) => style is TerrainStyle.Mountain or TerrainStyle.Peak;

        /// <summary>
        /// Reads one tile's facts, wrapping east–west or clamping at the map
        /// edge. Mountain and Peak tiles are massif, and so is bare Rock
        /// ground touching them: the rocky apron at the mountain's foot.
        /// </summary>
        private void ReadTile(int x, int y, int index)
        {
            var mx = MapX(x);
            var my = Math.Clamp(y, 0, map.Height - 1);
            var style = map.StyleAt(mx, my);
            float raw = map.ElevationAt(mx, my) ?? style switch
            {
                TerrainStyle.Peak => 250,
                TerrainStyle.Mountain => 228,
                _ => 150,
            };
            if (TerrainTextures.IsWater(style))
            {
                ground[index] = Ground.Water;
                elevation[index] = MathF.Min(raw, WaterFloor);
                return;
            }
            // High tiles vary a little, so a spine clamped at the top of the
            // elevation range still rises and falls into separate summits.
            var summit = SmoothStep(222f, 250f, raw);
            elevation[index] = raw + summit * SummitJitter * ((PixelArt.Hash(mx, my, 211) & 0xFF) / 127.5f - 1f);
            var apron = style == TerrainStyle.Rock && Enumerable.Range(0, 9).Any(n => n != 4 && IsMassif(StyleAt(x + n % 3 - 1, y + n / 3 - 1)));
            if (IsMassif(style) || apron)
            {
                ground[index] = Ground.Massif;
                hillWeight[index] = 1f;
                peak[index] = style == TerrainStyle.Peak ? 1f : 0f;
                return;
            }
            ground[index] = Ground.Land;
            if (raw >= 215) hillWeight[index] = 1f; // high ground under forest or other cover
            else if (map.IsHillAt(mx, my)) hillWeight[index] = 0.35f + 0.65f * (raw - 190) / 25f;
        }

        private int TileIndex(int x, int y) => (y - tileTop) * tileWidth + (x - tileLeft);

        /// <summary>How far the base height has climbed from the foothills toward the summits, 0–1.</summary>
        private static float Rise(float baseHeight) => SmoothStep(200f, 240f, baseHeight);

        /// <summary>How strongly spurs show at a given rise: fading out across the foothills, full from a third of the way up.</summary>
        private static float SpurPresence(float rise) => SmoothStep(-0.25f, 0.3f, rise);

        /// <summary>
        /// Builds the height field, shades it, decides rock, snow, lines and
        /// scree, and writes the output rectangle (without the margin).
        /// </summary>
        public Image Paint()
        {
            if (!AnyReliefNear())
                return Image.CreateEmpty(tiles.Size.X * size, tiles.Size.Y * size, false, Image.Format.Rgba8);
            var count = pixelWidth * pixelHeight;
            var baseHeight = new float[count];
            var strength = new float[count];
            var massifShare = new float[count];
            var peakShare = new float[count];
            SmoothFields(baseHeight, strength, massifShare, peakShare);

            var kernels = new SpurKernels(this);
            var bend = new NoiseField(this, 1, 77);
            var swell = new NoiseField(this, 1, 57);
            var wander = new NoiseField(this, 2, 91);
            var edge = new NoiseField(this, 1, 41);
            var edgeDetail = new NoiseField(this, 3, 43);
            var height = new float[count];
            var spur = new float[count];
            var hillHeight = new float[count];
            var pattern = SpurPattern(kernels, bend);
            for (var y = 0; y < pixelHeight; y++)
            {
                var v = (pixelTop + y + 0.5f) / size;
                for (var x = 0; x < pixelWidth; x++)
                {
                    var index = y * pixelWidth + x;
                    var b = baseHeight[index];
                    var presence = SpurPresence(Rise(b));
                    if (presence <= 0f && strength[index] <= 0.001f) { height[index] = b; hillHeight[index] = b; continue; }
                    var u = (pixelLeft + x + 0.5f) / size;
                    var s = SpurHeight * presence * pattern[index];
                    spur[index] = s;
                    height[index] = b + s;
                    // Foothills: a softened broad slope with the spurs running out into it as low
                    // ridges, and gentle swells, so the shading breaks up instead of ringing the massif.
                    hillHeight[index] = 0.7f * b + 0.7f * s + 6f * strength[index] * (swell.Sample(u, v) - 0.5f);
                }
            }

            var light = Light(height);
            var hillLight = Light(hillHeight);
            var rock = new float[count];
            for (var y = 1; y < pixelHeight - 1; y++)
            {
                var v = (pixelTop + y + 0.5f) / size;
                for (var x = 1; x < pixelWidth - 1; x++)
                {
                    var index = y * pixelWidth + x;
                    if (massifShare[index] > 0f)
                        rock[index] = RockCover(pixelLeft + x, pixelTop + y, (pixelLeft + x + 0.5f) / size, v, spur[index], massifShare[index], edge, edgeDetail);
                }
            }

            // Surface zone and light step for the rock and the rim around it.
            var surface = new Surface[count];
            var tone = new sbyte[count];
            for (var y = 1; y < pixelHeight - 1; y++)
            {
                var v = (pixelTop + y + 0.5f) / size;
                for (var x = 1; x < pixelWidth - 1; x++)
                {
                    var index = y * pixelWidth + x;
                    if (rock[index] <= 0f && rock[index - 1] < 1f && rock[index + 1] < 1f && rock[index - pixelWidth] < 1f && rock[index + pixelWidth] < 1f)
                        continue;
                    surface[index] = Zone(baseHeight[index], peakShare[index], spur[index], wander.Sample((pixelLeft + x + 0.5f) / size, v) - 0.5f);
                    tone[index] = surface[index] == Surface.Snow
                        ? (sbyte)Tone(light[index], 0.07f, 0.3f, 0.07f, 0.42f)
                        : (sbyte)Tone(light[index], 0.10f, 0.42f, 0.10f, 0.56f);
                }
            }
            Despeckle(tone, surface);

            // Line work: a light line along the spine, a dark line in the deepest gullies.
            var lines = new sbyte[count];
            for (var y = 1; y < pixelHeight - 1; y++)
                for (var x = 1; x < pixelWidth - 1; x++)
                {
                    var index = y * pixelWidth + x;
                    if (rock[index] >= 1f) lines[index] = (sbyte)LineAt(baseHeight, height, index, surface[index]);
                }
            var scree = Scree(rock, massifShare, baseHeight, spur);

            var outWidth = tiles.Size.X * size;
            var output = new byte[outWidth * tiles.Size.Y * size * 4];
            for (var y = Margin; y < pixelHeight - Margin; y++)
                for (var x = Margin; x < pixelWidth - Margin; x++)
                {
                    var index = y * pixelWidth + x;
                    Color color;
                    if (scree[index] >= 0)
                        color = Ramps.Rock[scree[index]];
                    else if (rock[index] >= 1f)
                        color = RockColor(surface[index], tone[index], lines[index]);
                    else
                    {
                        color = HillColor(hillLight[index], strength[index] * LandShare(pixelLeft + x, pixelTop + y));
                        Color? over = null;
                        if (rock[index] > 0f)
                            over = new Color(RockColor(surface[index], tone[index], 0), rock[index]);
                        else if (rock[index - 1] >= 1f || rock[index + 1] >= 1f || rock[index - pixelWidth] >= 1f || rock[index + pixelWidth] >= 1f)
                            // A half-transparent rim where the rock ends.
                            over = new Color(RockColor(surface[index], tone[index], 0), 0.5f);
                        if (over is { } top) color = color.A > 0f ? color.Blend(top) : top;
                        if (color.A <= 0f) continue;
                    }
                    var o = ((y - Margin) * outWidth + (x - Margin)) * 4;
                    output[o] = ToByte(color.R);
                    output[o + 1] = ToByte(color.G);
                    output[o + 2] = ToByte(color.B);
                    output[o + 3] = ToByte(color.A);
                }
            return Image.CreateFromData(outWidth, tiles.Size.Y * size, false, Image.Format.Rgba8, output);
        }

        /// <summary>
        /// Whether any massif or hill tile lies within three tiles of the
        /// rectangle. Every mark this layer makes comes from such a tile no
        /// more than two tiles away, so a chunk of plain lowland, most of any
        /// map, is returned empty at once.
        /// </summary>
        private bool AnyReliefNear()
        {
            for (var y = tiles.Position.Y - 3; y < tiles.End.Y + 3; y++)
                for (var x = tiles.Position.X - 3; x < tiles.End.X + 3; x++)
                {
                    var index = TileIndex(x, y);
                    if (ground[index] == Ground.Massif || hillWeight[index] > 0f) return true;
                }
            return false;
        }

        /// <summary>
        /// The spur pattern for every pixel of the window. At 32 px it is
        /// sampled on every second global pixel and interpolated between:
        /// the pattern is made of flat faces, which interpolation keeps flat,
        /// and it costs a quarter as much. Samples sit on global coordinates,
        /// so chunks still agree.
        /// </summary>
        private float[] SpurPattern(SpurKernels kernels, NoiseField bend)
        {
            var stride = size >= 32 ? 2 : 1;
            var left = FloorDiv(pixelLeft, stride);
            var top = FloorDiv(pixelTop, stride);
            var columns = FloorDiv(pixelLeft + pixelWidth - 1, stride) - left + 2;
            var rows = FloorDiv(pixelTop + pixelHeight - 1, stride) - top + 2;
            var samples = new float[columns * rows];
            for (var j = 0; j < rows; j++)
            {
                var v = ((top + j) * stride + 0.5f) / size;
                for (var i = 0; i < columns; i++)
                {
                    var u = ((left + i) * stride + 0.5f) / size;
                    samples[j * columns + i] = kernels.Sample(u, v, 1.1f * (bend.Sample(u, v) - 0.5f));
                }
            }
            var pattern = new float[pixelWidth * pixelHeight];
            for (var y = 0; y < pixelHeight; y++)
            {
                var gy = pixelTop + y;
                var j = FloorDiv(gy, stride) - top;
                var fy = (gy - (j + top) * stride) / (float)stride;
                for (var x = 0; x < pixelWidth; x++)
                {
                    var gx = pixelLeft + x;
                    var i = FloorDiv(gx, stride) - left;
                    var fx = (gx - (i + left) * stride) / (float)stride;
                    var at = j * columns + i;
                    var upper = samples[at] + (samples[at + 1] - samples[at]) * fx;
                    var lower = samples[at + columns] + (samples[at + columns + 1] - samples[at + columns]) * fx;
                    pattern[y * pixelWidth + x] = upper + (lower - upper) * fy;
                }
            }
            return pattern;
        }

        /// <summary>
        /// Light on each pixel from the north-west, −1 to 1: positive on
        /// slopes facing the light, negative on slopes facing away, zero on
        /// flat ground and on slopes facing north-east or south-west.
        /// </summary>
        private float[] Light(float[] height)
        {
            var light = new float[height.Length];
            var scale = 0.5f * size * Exaggeration;
            for (var y = 1; y < pixelHeight - 1; y++)
                for (var x = 1; x < pixelWidth - 1; x++)
                {
                    var index = y * pixelWidth + x;
                    var gx = (height[index + 1] - height[index - 1]) * scale;
                    var gy = (height[index + pixelWidth] - height[index - pixelWidth]) * scale;
                    light[index] = (gx + gy) * 0.70710678f / MathF.Sqrt(1f + gx * gx + gy * gy);
                }
            return light;
        }

        /// <summary>
        /// Interpolates the tile elevations, the foothill strength and the
        /// shares of massif and peak tiles with a cubic B-spline, smooth
        /// everywhere so no tile edge shows in the shading. Done in two
        /// passes: along each tile row, then down the pixel rows.
        /// </summary>
        private void SmoothFields(float[] baseHeight, float[] strength, float[] massifShare, float[] peakShare)
        {
            Span<float> w = stackalloc float[4];
            // Pass 1: each tile row interpolated across the pixel columns.
            var rowE = new float[tileHeight * pixelWidth];
            var rowHill = new float[tileHeight * pixelWidth];
            var rowMassif = new float[tileHeight * pixelWidth];
            var rowPeak = new float[tileHeight * pixelWidth];
            for (var x = 0; x < pixelWidth; x++)
            {
                var s = (pixelLeft + x + 0.5f) / size - 0.5f;
                var i0 = (int)MathF.Floor(s);
                BSpline(s - i0, w);
                var first = i0 - 1 - tileLeft;
                for (var j = 0; j < tileHeight; j++)
                {
                    float e = 0, hill = 0, massif = 0, top = 0;
                    for (var a = 0; a < 4; a++)
                    {
                        var tile = j * tileWidth + first + a;
                        e += w[a] * elevation[tile];
                        hill += w[a] * hillWeight[tile];
                        if (ground[tile] == Ground.Massif) massif += w[a];
                        top += w[a] * peak[tile];
                    }
                    var at = j * pixelWidth + x;
                    rowE[at] = e; rowHill[at] = hill; rowMassif[at] = massif; rowPeak[at] = top;
                }
            }
            // Pass 2: the rows combined down each pixel row.
            for (var y = 0; y < pixelHeight; y++)
            {
                var s = (pixelTop + y + 0.5f) / size - 0.5f;
                var j0 = (int)MathF.Floor(s);
                BSpline(s - j0, w);
                var first = j0 - 1 - tileTop;
                for (var x = 0; x < pixelWidth; x++)
                {
                    float e = 0, hill = 0, massif = 0, top = 0;
                    for (var b = 0; b < 4; b++)
                    {
                        var at = (first + b) * pixelWidth + x;
                        e += w[b] * rowE[at];
                        hill += w[b] * rowHill[at];
                        massif += w[b] * rowMassif[at];
                        top += w[b] * rowPeak[at];
                    }
                    var index = y * pixelWidth + x;
                    baseHeight[index] = e;
                    strength[index] = hill;
                    massifShare[index] = massif;
                    peakShare[index] = top;
                }
            }
        }

        /// <summary>The smooth elevation and its slope (units per tile) at a point in tile space.</summary>
        public (float Height, float SlopeX, float SlopeY) BaseAt(float u, float v)
        {
            Span<float> wx = stackalloc float[4];
            Span<float> wy = stackalloc float[4];
            Span<float> dx = stackalloc float[4];
            Span<float> dy = stackalloc float[4];
            var sx = u - 0.5f;
            var sy = v - 0.5f;
            var i0 = (int)MathF.Floor(sx);
            var j0 = (int)MathF.Floor(sy);
            BSpline(sx - i0, wx);
            BSpline(sy - j0, wy);
            BSplineSlope(sx - i0, dx);
            BSplineSlope(sy - j0, dy);
            float h = 0, gx = 0, gy = 0;
            for (var b = 0; b < 4; b++)
            {
                var row = TileIndex(i0 - 1, j0 - 1 + b);
                for (var a = 0; a < 4; a++)
                {
                    var e = elevation[row + a];
                    h += wx[a] * wy[b] * e;
                    gx += dx[a] * wy[b] * e;
                    gy += wx[a] * dy[b] * e;
                }
            }
            return (h, gx, gy);
        }

        /// <summary>
        /// How much of a pixel is rock (0 to 1). Mountain and Peak tiles are
        /// rock throughout. Around them the rock follows a smooth envelope of
        /// the massif with a wandering edge, pushed out along spur crests and
        /// pulled in along gullies, and always reaching far enough to hide the
        /// tile squares and the old mountain edge pieces. On a river or lake
        /// beside the massif it fills the shore's land fringe.
        /// </summary>
        private float RockCover(int px, int py, float u, float v, float spur, float massifShare, NoiseField edge, NoiseField detail)
        {
            var tx = FloorDiv(px, size);
            var ty = FloorDiv(py, size);
            var here = ground[TileIndex(tx, ty)];
            if (here == Ground.Massif) return 1f;
            if (here == Ground.Water) return CoastCover(tx, ty, px - tx * size, py - ty * size);
            var nearest = float.MaxValue;
            for (var j = ty - 1; j <= ty + 1; j++)
                for (var i = tx - 1; i <= tx + 1; i++)
                {
                    if (ground[TileIndex(i, j)] != Ground.Massif) continue;
                    var dx = MathF.Max(0, MathF.Max(i - u, u - (i + 1)));
                    var dy = MathF.Max(0, MathF.Max(j - v, v - (j + 1)));
                    nearest = MathF.Min(nearest, dx * dx + dy * dy);
                }
            // Edge pieces of the old mountain tiles reach a quarter tile into their neighbours.
            var least = 0.26f + 0.14f * detail.Sample(u, v);
            if (nearest < least * least) return 1f;
            // Beyond that the edge wanders, but never into separate islands far from the massif.
            if (nearest > 0.7f * 0.7f || massifShare < 0.1f) return 0f;
            var envelope = massifShare - 0.18f + 0.26f * (edge.Sample(u, v) - 0.5f) + 0.08f * (detail.Sample(u, v) - 0.5f) + 0.03f * spur;
            return envelope > 0f ? 1f : 0f;
        }

        /// <summary>The shore's land fringe on a water tile, where the land beside it is massif.</summary>
        private float CoastCover(int tx, int ty, int lx, int ly)
        {
            var (massif, _) = CoastMasksAt(tx, ty);
            if (massif is null) return 0f;
            var atlas = TerrainTextures.AtlasTileSize(size);
            var cover = massif[ly * atlas / size * atlas + lx * atlas / size] / 255f;
            return cover >= 0.75f ? 1f : cover > 0.2f ? 0.5f : 0f;
        }

        /// <summary>
        /// The land fringes the game's shoreline pieces draw into a water
        /// tile: from massif neighbours only, and from any land. Null where
        /// there is none. Cached per tile.
        /// </summary>
        private (byte[]? Massif, byte[]? Land) CoastMasksAt(int tx, int ty)
        {
            var key = TileIndex(tx, ty);
            if (coastCovers.TryGetValue(key, out var cached)) return cached;
            (byte[]? Massif, byte[]? Land) result = (null, null);
            if (ty >= 0 && ty < map.Height)
            {
                var atlas = TerrainTextures.AtlasTileSize(size);
                lock (CoastLock)
                {
                    var pieces = new List<(TerrainStyle Style, int Piece)>();
                    TerrainTransitions.CollectCoast(map, MapX(tx), ty, map.WrapsEastWest, pieces);
                    foreach (var (style, piece) in pieces)
                    {
                        if (!CoastMasks.TryGetValue((piece, atlas), out var mask))
                        {
                            var image = CoastEdges.Piece(CoastEdges.LandRow, piece, atlas);
                            mask = new byte[atlas * atlas];
                            for (var y = 0; y < atlas; y++)
                                for (var x = 0; x < atlas; x++)
                                    mask[y * atlas + x] = ToByte(image.GetPixel(x, y).A);
                            CoastMasks[(piece, atlas)] = mask;
                        }
                        result.Land ??= new byte[atlas * atlas];
                        Union(result.Land, mask);
                        if (!IsMassif(style)) continue;
                        result.Massif ??= new byte[atlas * atlas];
                        Union(result.Massif, mask);
                    }
                }
            }
            coastCovers[key] = result;
            return result;
        }

        private static void Union(byte[] into, byte[] mask)
        {
            for (var index = 0; index < into.Length; index++) into[index] = Math.Max(into[index], mask[index]);
        }

        /// <summary>
        /// Snow on the highest ground and on the map's Peak tiles, lying
        /// longer in gullies than on crests; a narrow band of paler bare rock
        /// beside it; the massif's own rock below.
        /// </summary>
        private static Surface Zone(float baseHeight, float peakShare, float spur, float wander)
        {
            var snow = (baseHeight - 238f) / 7f + 2.2f * (peakShare - 0.3f) + 0.9f * wander - 0.25f * spur;
            if (snow > 0f) return Surface.Snow;
            if (snow > -0.7f) return Surface.HighRock;
            return Surface.Rock;
        }

        /// <summary>
        /// Line work at a pixel: +1 on the spine of bare rock, where the
        /// smooth height field crests on the upper massif, so one crisp light
        /// line runs along the ridge (on snow the split between its sunlit and
        /// blue-grey halves already draws it); −1 in the deepest gullies of
        /// the rock, where the slope flips sharply.
        /// </summary>
        private int LineAt(float[] baseHeight, float[] height, int index, Surface surface)
        {
            var rise = Rise(baseHeight[index]);
            if (rise < 0.2f || surface == Surface.Snow) return 0;
            if (rise > 0.5f && OnSpine(baseHeight, index)) return 1;
            if (rise > 0.85f) return 0;
            var h = height[index];
            var gully = 0f;
            Span<int> steps = [1, pixelWidth, pixelWidth + 1, pixelWidth - 1];
            foreach (var step in steps)
            {
                var before = height[index - step];
                var after = height[index + step];
                if (h <= before && h <= after) gully = MathF.Max(gully, before + after - 2 * h);
            }
            // Slope change across the pixel, in elevation units per tile.
            return gully * size > 46f + 200f / size ? -1 : 0;
        }

        /// <summary>
        /// Whether a pixel lies on a crest of the smooth height field: it
        /// curves down strongly across one direction, and the pixel sits
        /// within half a pixel of the top along that direction. This gives a
        /// single clean line along a ridge rather than a scatter of maxima.
        /// </summary>
        private bool OnSpine(float[] field, int index)
        {
            var scale = (float)size * size;
            var c = field[index];
            var xx = (field[index + 1] - 2 * c + field[index - 1]) * scale;
            var yy = (field[index + pixelWidth] - 2 * c + field[index - pixelWidth]) * scale;
            var xy = (field[index + pixelWidth + 1] - field[index + pixelWidth - 1] - field[index - pixelWidth + 1] + field[index - pixelWidth - 1]) * 0.25f * scale;
            // Most negative curvature, in elevation units per tile², and its direction.
            var mean = (xx + yy) * 0.5f;
            var spread = MathF.Sqrt((xx - yy) * (xx - yy) * 0.25f + xy * xy);
            var curve = mean - spread;
            if (curve > -45f) return false;
            float ex = xy, ey = curve - xx;
            if (MathF.Abs(ex) + MathF.Abs(ey) < 1e-6f) { ex = curve - yy; ey = xy; }
            var length = MathF.Sqrt(ex * ex + ey * ey);
            if (length < 1e-6f) return false;
            ex /= length;
            ey /= length;
            var gx = (field[index + 1] - field[index - 1]) * 0.5f * size;
            var gy = (field[index + pixelWidth] - field[index - pixelWidth]) * 0.5f * size;
            return MathF.Abs(gx * ex + gy * ey) <= -curve * 0.5f / size;
        }

        /// <summary>
        /// Scree: small stones scattered along the massif's foot and at the
        /// bottom of its lower gullies, at most one per 4 × 4-pixel cell of
        /// the global pixel grid, so each stone lies inside one chunk. Returns
        /// a Rock ramp step per pixel, or −1 where there is no stone.
        /// </summary>
        private sbyte[] Scree(float[] rock, float[] massifShare, float[] baseHeight, float[] spur)
        {
            const int cell = 4;
            var scree = new sbyte[pixelWidth * pixelHeight];
            Array.Fill(scree, (sbyte)-1);
            var stone = size >= 32 ? 2 : 1;
            var period = wrapWidth * size / cell;
            for (var cy = FloorDiv(pixelTop + Margin, cell); cy * cell < pixelTop + pixelHeight - Margin; cy++)
                for (var cx = FloorDiv(pixelLeft + Margin, cell); cx * cell < pixelLeft + pixelWidth - Margin; cx++)
                {
                    var hash = PixelArt.Hash(period > 0 ? Wrap(cx, period) : cx, cy, 401);
                    var ax = cx * cell + (int)(hash % (uint)(cell - stone + 1)) - pixelLeft;
                    var ay = cy * cell + (int)((hash >> 8) % (uint)(cell - stone + 1)) - pixelTop;
                    if (ax < 1 || ay < 1 || ax + stone >= pixelWidth || ay + stone >= pixelHeight) continue;
                    var anchor = ay * pixelWidth + ax;
                    if (ground[TileIndex(FloorDiv(ax + pixelLeft, size), FloorDiv(ay + pixelTop, size))] == Ground.Water) continue;
                    var chance = (hash >> 16) % 100;
                    var placed = rock[anchor] <= 0f
                        // On the land just beyond the rock: thick at the edge, thinning outward.
                        ? massifShare[anchor] > 0.1f && chance < (massifShare[anchor] - 0.1f) * 110f
                        // On the lower rock, at the bottom of gullies.
                        : rock[anchor] >= 1f && Rise(baseHeight[anchor]) < 0.22f && spur[anchor] < -2.5f && chance < 10;
                    if (!placed) continue;
                    // Lit on its north-west side, shaded on its south-east side.
                    if (stone == 1) { scree[anchor] = (sbyte)(rock[anchor] > 0f ? 4 : 3); continue; }
                    scree[anchor] = 4;
                    scree[anchor + 1] = 3;
                    scree[anchor + pixelWidth] = 2;
                    scree[anchor + pixelWidth + 1] = 1;
                }
            return scree;
        }

        /// <summary>
        /// How much of a pixel is land, for the foothill shading: 1 on land,
        /// and on water only the shore's land fringe that the game draws there.
        /// </summary>
        private float LandShare(int px, int py)
        {
            var tx = FloorDiv(px, size);
            var ty = FloorDiv(py, size);
            if (ground[TileIndex(tx, ty)] != Ground.Water) return 1f;
            var (_, land) = CoastMasksAt(tx, ty);
            if (land is null) return 0f;
            var atlas = TerrainTextures.AtlasTileSize(size);
            return land[(py - ty * size) * atlas / size * atlas + (px - tx * size) * atlas / size] >= 192 ? 1f : 0f;
        }

        /// <summary>Rock or snow colour for a zone, light step and line.</summary>
        private static Color RockColor(Surface surface, int tone, int line)
        {
            var ramp = surface switch
            {
                Surface.Snow => Ramps.Snow,
                Surface.HighRock => Ramps.HighRock,
                _ => Ramps.Rock,
            };
            if (line > 0) return ramp[4];
            if (line < 0) return ramp[Math.Clamp(1 + Math.Min(tone, 0), 0, 1)];
            return ramp[2 + tone];
        }

        /// <summary>See-through foothill shading: warm light on north-west slopes, cool shade on south-east ones.</summary>
        private static Color HillColor(float light, float strength)
        {
            if (strength <= 0.02f) return default;
            var tone = Tone(light * MathF.Min(1f, strength * 1.5f), 0.09f, 0.3f, 0.09f, 0.3f);
            return tone switch
            {
                2 => new Color(Ramps.HillLight, 0.2f),
                1 => new Color(Ramps.HillLight, 0.11f),
                -1 => new Color(Ramps.HillShade, 0.12f),
                -2 => new Color(Ramps.HillShade, 0.22f),
                _ => default,
            };
        }

        /// <summary>
        /// Removes single pixels whose light step or zone differs from all
        /// four neighbours, which agree, so slopes stay calm.
        /// </summary>
        private void Despeckle(sbyte[] tone, Surface[] surface)
        {
            var tones = (sbyte[])tone.Clone();
            var zones = (Surface[])surface.Clone();
            for (var y = 2; y < pixelHeight - 2; y++)
                for (var x = 2; x < pixelWidth - 2; x++)
                {
                    var index = y * pixelWidth + x;
                    var w = tones[index - 1];
                    if (w != tones[index] && w == tones[index + 1] && w == tones[index - pixelWidth] && w == tones[index + pixelWidth])
                        tone[index] = w;
                    var z = zones[index - 1];
                    if (z != zones[index] && z == zones[index + 1] && z == zones[index - pixelWidth] && z == zones[index + pixelWidth])
                        surface[index] = z;
                }
        }

        /// <summary>Quantises a light level into five steps, −2 to +2.</summary>
        private static int Tone(float light, float lit, float bright, float shaded, float dark) =>
            light > bright ? 2 : light > lit ? 1 : light < -dark ? -2 : light < -shaded ? -1 : 0;

        /// <summary>
        /// Smooth value noise over the tile window, with a whole number of
        /// cells per tile. Lattice values come from <see cref="PixelArt.Hash"/>
        /// on global lattice coordinates, wrapped east–west with the map, and
        /// are hashed once per render rather than per pixel.
        /// </summary>
        private sealed class NoiseField
        {
            private readonly int cells, left, top, width;
            private readonly float[] lattice;

            public NoiseField(Painter painter, int cellsPerTile, int salt)
            {
                cells = cellsPerTile;
                left = painter.tileLeft * cells;
                top = painter.tileTop * cells;
                width = painter.tileWidth * cells + 1;
                var height = painter.tileHeight * cells + 1;
                lattice = new float[width * height];
                var period = painter.wrapWidth * cells;
                for (var j = 0; j < height; j++)
                    for (var i = 0; i < width; i++)
                        lattice[j * width + i] = (PixelArt.Hash(period > 0 ? Wrap(left + i, period) : left + i, top + j, salt) & 0xFFFF) / 65535f;
            }

            /// <summary>Noise at a point in tile space, 0–1.</summary>
            public float Sample(float u, float v)
            {
                var x = u * cells - left;
                var y = v * cells - top;
                var ix = (int)x;
                var iy = (int)y;
                var fx = x - ix;
                var fy = y - iy;
                var sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
                var sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
                var at = iy * width + ix;
                var upper = lattice[at] + (lattice[at + 1] - lattice[at]) * sx;
                var lower = lattice[at + width] + (lattice[at + width + 1] - lattice[at + width]) * sx;
                return upper + (lower - upper) * sy;
            }
        }

        /// <summary>
        /// Spur kernels: short oriented waves laid down the slope of the
        /// smooth height field, two per tile cell at hashed positions. Summed,
        /// they form ridges and gullies that run from the spine down to the
        /// foot, the way a range erodes, instead of round bumps. The waves are
        /// triangular, so each spur has two flat faces, one catching the light
        /// and one turned from it, and reads as a facet rather than a bulge.
        /// </summary>
        private sealed class SpurKernels
        {
            private const int PerCell = 2;

            /// <summary>One kernel: centre, downhill direction, wave phase, height and spacing.</summary>
            private struct Kernel
            {
                public float X, Y, DownX, DownY, Phase, Amplitude, Spacing;
            }

            private readonly int cellLeft, cellTop, cellWidth, cellHeight;
            // For each half-tile cell under the pixel window, a copy of each kernel whose reach touches it.
            private readonly int[] listStart;
            private readonly Kernel[] listItems;
            // Whether any kernel touching the cell has spurs at all; flat cells skip the work.
            private readonly bool[] active;

            public SpurKernels(Painter painter)
            {
                // Kernels reach at most one tile, so cells one tile beyond the pixel window suffice.
                cellLeft = painter.tiles.Position.X - 2;
                cellTop = painter.tiles.Position.Y - 2;
                cellWidth = painter.tiles.Size.X + 4;
                cellHeight = painter.tiles.Size.Y + 4;
                var kernels = new Kernel[cellWidth * cellHeight * PerCell];
                for (var cy = 0; cy < cellHeight; cy++)
                    for (var cx = 0; cx < cellWidth; cx++)
                        for (var k = 0; k < PerCell; k++)
                        {
                            var gx = cellLeft + cx;
                            var gy = cellTop + cy;
                            var hx = painter.wrapWidth > 0 ? Wrap(gx, painter.wrapWidth) : gx;
                            var hash = PixelArt.Hash(hx, gy, 300 + k);
                            var kernel = new Kernel
                            {
                                X = gx + (hash & 0xFF) / 256f,
                                Y = gy + ((hash >> 8) & 0xFF) / 256f,
                                Phase = ((hash >> 16) & 0xFF) / 256f,
                                // Spurs a little closer or wider apart from place to place.
                                Spacing = SpursPerTile * (0.9f + 0.2f * (PixelArt.Hash(hx, gy, 320 + k) & 0xFF) / 255f),
                                // A flat kernel keeps a direction so its window still fades with distance.
                                DownX = 1f,
                            };
                            var (height, sx, sy) = painter.BaseAt(kernel.X, kernel.Y);
                            var slope = MathF.Sqrt(sx * sx + sy * sy);
                            if (SpurPresence(Rise(height)) > 0f && slope >= 0.5f)
                            {
                                kernel.DownX = -sx / slope;
                                kernel.DownY = -sy / slope;
                                kernel.Amplitude = SmoothStep(1.5f, 8f, slope) * (0.7f + 0.6f * (((hash >> 24) & 0xFF) / 255f));
                            }
                            kernels[(cy * cellWidth + cx) * PerCell + k] = kernel;
                        }

                // Candidate lists per half-tile cell, from the kernels in the
                // 3 × 3 tile cells around it that can reach it.
                listStart = new int[cellWidth * cellHeight * 4 + 1];
                active = new bool[cellWidth * cellHeight * 4];
                var items = new List<Kernel>();
                for (var sy = 0; sy < cellHeight * 2; sy++)
                    for (var sx = 0; sx < cellWidth * 2; sx++)
                    {
                        var sub = sy * cellWidth * 2 + sx;
                        listStart[sub] = items.Count;
                        int cx = sx / 2, cy = sy / 2;
                        if (cx == 0 || cy == 0 || cx == cellWidth - 1 || cy == cellHeight - 1) continue;
                        float left = cellLeft + sx * 0.5f, top = cellTop + sy * 0.5f;
                        for (var j = cy - 1; j <= cy + 1; j++)
                            for (var i = cx - 1; i <= cx + 1; i++)
                                for (var k = 0; k < PerCell; k++)
                                {
                                    var kernel = kernels[(j * cellWidth + i) * PerCell + k];
                                    // Nearest point of the half-tile cell to the kernel; skip kernels out of reach.
                                    var nx = Math.Clamp(kernel.X, left, left + 0.5f) - kernel.X;
                                    var ny = Math.Clamp(kernel.Y, top, top + 0.5f) - kernel.Y;
                                    if (nx * nx + ny * ny >= SpurLength * SpurLength) continue;
                                    items.Add(kernel);
                                    if (kernel.Amplitude > 0f) active[sub] = true;
                                }
                    }
                listStart[^1] = items.Count;
                listItems = items.ToArray();
            }

            /// <summary>The spur pattern at a point, about −1 to 1; <paramref name="bend"/> shifts the wave phase.</summary>
            public float Sample(float u, float v, float bend)
            {
                var cell = ((int)MathF.Floor(v * 2) - cellTop * 2) * cellWidth * 2 + (int)MathF.Floor(u * 2) - cellLeft * 2;
                if (!active[cell]) return 0f;
                float sum = 0, weights = 0;
                const float invLength = 1f / SpurLength;
                const float invBreadth2 = 1f / (SpurBreadth * SpurBreadth);
                var end = listStart[cell + 1];
                for (var item = listStart[cell]; item < end; item++)
                {
                    ref readonly var kernel = ref listItems[item];
                    var ox = u - kernel.X;
                    var oy = v - kernel.Y;
                    var along = (ox * kernel.DownX + oy * kernel.DownY) * invLength;
                    var across = ox * kernel.DownY - oy * kernel.DownX;
                    var r2 = along * along + across * across * invBreadth2;
                    if (r2 >= 1f) continue;
                    var window = (1 - r2) * (1 - r2);
                    weights += window;
                    if (kernel.Amplitude <= 0f) continue;
                    // A triangle wave across the slope: flat faces, sharp crests and gullies.
                    // The offset keeps the argument positive so truncation is a floor.
                    var t = across * kernel.Spacing + kernel.Phase + bend + 8f;
                    var wave = 4f * MathF.Abs(t - (int)t - 0.5f) - 1f;
                    sum += window * kernel.Amplitude * wave;
                }
                return sum / (weights + 0.2f);
            }
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>Cubic B-spline weights for the four taps around a fraction <paramref name="t"/>.</summary>
    private static void BSpline(float t, Span<float> w)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var m = 1 - t;
        w[0] = m * m * m / 6f;
        w[1] = (3 * t3 - 6 * t2 + 4) / 6f;
        w[2] = (-3 * t3 + 3 * t2 + 3 * t + 1) / 6f;
        w[3] = t3 / 6f;
    }

    /// <summary>Derivatives of the B-spline weights.</summary>
    private static void BSplineSlope(float t, Span<float> w)
    {
        var t2 = t * t;
        var m = 1 - t;
        w[0] = -m * m / 2f;
        w[1] = (3 * t2 - 4 * t) / 2f;
        w[2] = (-3 * t2 + 2 * t + 1) / 2f;
        w[3] = t2 / 2f;
    }

    private static float SmoothStep(float from, float to, float value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static int Wrap(int value, int period) => (value % period + period) % period;

    private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
