using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Terrain;

/// <summary>
/// Proposed ground tiles. The look stays the game's calm, straight-top-down
/// ground, but each tile now carries soft, rounded mottling in one or two
/// neighbouring steps of its own colour ramp instead of isolated specks;
/// mountains and peaks are pyramids of rock seen from above and lit from the
/// north-west instead of side-view triangles; hills are open lit and shaded
/// crescents instead of rings; rock is flat facets; fertile soil is ridged,
/// clodded farmland; and the scrub, brush and tundra-snow styles each carry
/// one identifying motif (bushes, gravel, brush, sage, peeking tundra). Every tile
/// keeps its style's base colour from <see cref="TerrainTextures.BaseColor"/>,
/// repeats seamlessly with itself and with its other variant, and is drawn
/// deterministically from <see cref="PixelArt.Hash"/> and <see cref="PixelArt.Stream"/>.
/// </summary>
public sealed class TerrainProposal : IArtProposal, IArtSetProvider
{
    public string Family => "terrain";
    public string Name => "terrain";

    /// <summary>Styles drawn here, in sheet order. Any other style falls back to the current generator.</summary>
    private static readonly TerrainStyle[] Drawn =
    [
        TerrainStyle.Grass, TerrainStyle.ForestGrass, TerrainStyle.ForestFloor, TerrainStyle.DenseForestFloor,
        TerrainStyle.Sand, TerrainStyle.Rock, TerrainStyle.Mountain, TerrainStyle.Peak,
        TerrainStyle.Snow, TerrainStyle.Tundra, TerrainStyle.FertileSoil,
        // Round 2: the scrub, brush and tundra-snow styles.
        TerrainStyle.ScrubGrass, TerrainStyle.ScrubSand, TerrainStyle.DryScrub, TerrainStyle.DryBrush,
        TerrainStyle.DesertBrush, TerrainStyle.TundraSnow,
    ];

    /// <summary>Styles that get a 3×3 <c>.tiling</c> sample on the sheet.</summary>
    private static readonly TerrainStyle[] TilingSamples =
    [
        TerrainStyle.Grass, TerrainStyle.Mountain, TerrainStyle.Sand,
        TerrainStyle.ScrubGrass, TerrainStyle.DryScrub, TerrainStyle.FertileSoil,
    ];

    private static readonly Dictionary<(TerrainStyle Style, int Variant, int Size), Image> Tiles = [];
    private static readonly Dictionary<(int Variant, int Size), Image> HillImages = [];

    /// <summary>Whether this proposal draws the style (otherwise the current generator does).</summary>
    public static bool Draws(TerrainStyle style) => Array.IndexOf(Drawn, style) >= 0;

    public IEnumerable<Entry> Render()
    {
        // Two styles per sheet row: v0, v1, v0.16, v1.16 side by side.
        foreach (var style in Drawn)
        {
            for (var variant = 0; variant < TerrainTextures.VariantCount; variant++)
                yield return new(Family, $"{style}.v{variant}", Tile(style, variant, 32), Note(style, variant));
            for (var variant = 0; variant < TerrainTextures.VariantCount; variant++)
                yield return new(Family, $"{style}.v{variant}.16", Tile(style, variant, 16), "16 px mid-zoom tile, drawn separately");
        }
        for (var variant = 0; variant < TerrainTextures.VariantCount; variant++)
        {
            var mask = HillOverlay(variant, 32);
            yield return new(Family, $"hill.v{variant}.on_grass", Bitmap.Over(Tile(TerrainStyle.Grass, 0, 32), mask, 0, 0),
                "Hill relief: an open lit crescent on the north-west and a shaded crescent on the south-east; grass shows through");
            yield return new(Family, $"hill.v{variant}.mask", mask, "Transparent overlay drawn over any ground");
        }
        foreach (var style in TilingSamples)
            yield return new(Family, $"{style}.tiling", Tiling(style), "3×3 repeat mixing both variants with TerrainTextures.VariantAt");
    }

    public void Apply(ArtSet set)
    {
        set.Tile = (style, variant, size) => Draws(style) ? Tile(style, variant, size) : TerrainTextures.Tile(style, variant, size);
        set.Hill = HillOverlay;
    }

    /// <summary>One ground tile, generated once per style, variant and size (32 or 16).</summary>
    public static Image Tile(TerrainStyle style, int variant, int size)
    {
        var key = (style, Math.Clamp(variant, 0, TerrainTextures.VariantCount - 1), size);
        if (!Tiles.TryGetValue(key, out var cached))
        {
            cached = Paint(key.style, key.Item2, size);
            Tiles[key] = cached;
        }
        return cached.Duplicate();
    }

    /// <summary>One transparent hill overlay for a tile of the given size.</summary>
    public static Image HillOverlay(int variant, int size)
    {
        var key = (Math.Clamp(variant, 0, TerrainTextures.VariantCount - 1), size);
        if (!HillImages.TryGetValue(key, out var cached))
        {
            cached = PaintHill(key.Item1, size);
            HillImages[key] = cached;
        }
        return cached.Duplicate();
    }

    /// <summary>A 3×3 block of tiles mixing variants the way the map does, to judge seams and repetition.</summary>
    private static Image Tiling(TerrainStyle style)
    {
        var image = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                image.BlitRect(Tile(style, TerrainTextures.VariantAt(x, y), 32), new Rect2I(0, 0, 32, 32), new Vector2I(x * 32, y * 32));
        return image;
    }

    private static string Note(TerrainStyle style, int variant) => (style, variant) switch
    {
        (TerrainStyle.Grass, 0) => "Small soft patches in the shade step and two tufts with lit tips",
        (TerrainStyle.Grass, _) => "Busier: more tufts and a pebble",
        (TerrainStyle.ForestGrass, 0) => "Darker grass ramp, same patches, darker tufts",
        (TerrainStyle.ForestGrass, _) => "Busier: tufts and a few fallen leaves",
        (TerrainStyle.ForestFloor or TerrainStyle.DenseForestFloor, 0) => "Mottled floor with moss and leaf litter",
        (TerrainStyle.ForestFloor or TerrainStyle.DenseForestFloor, _) => "Busier: a fallen twig and more litter",
        (TerrainStyle.Sand, 0) => "Pale mottling and wind ripples with a shaded lee",
        (TerrainStyle.Sand, _) => "Busier: a third ripple and a few darker grains",
        (TerrainStyle.Rock, 0) => "Flat facets in three tones, a crease where the stone steps down, one crack",
        (TerrainStyle.Rock, _) => "Busier: a second crack and loose stones",
        (TerrainStyle.Mountain, 0) => "Seen from above: a rock pyramid lit from the north-west, with a smaller one at its foot and scree",
        (TerrainStyle.Mountain, _) => "Seen from above: twin peaks and a knoll",
        (TerrainStyle.Peak, 0) => "A taller pyramid in the peak ramp under a snow cap, blue-grey snow on the shaded face",
        (TerrainStyle.Peak, _) => "Twin snow-capped peaks",
        (TerrainStyle.Snow, 0) => "Smooth snow with one drift and sparse sparkle",
        (TerrainStyle.Snow, _) => "Same soft patches as v0, with two drifts and a little more sparkle",
        (TerrainStyle.Tundra, 0) => "Grey-green mottling with lichen patches",
        (TerrainStyle.Tundra, _) => "Busier: more lichen, a stone and dry grass",
        (TerrainStyle.FertileSoil, 0) => "Tilled farmland: soft east–west ridges with a lit north face, clods, a shaded trough, a damp patch and a stone",
        (TerrainStyle.FertileSoil, _) => "Busier: a straw fleck, a pale root and a second damp patch",
        (TerrainStyle.ScrubGrass, 0) => "Dry grassland: soft patches, dry tufts and two low scrub bushes",
        (TerrainStyle.ScrubGrass, _) => "Busier: a third bush and more tufts",
        (TerrainStyle.ScrubSand, 0) => "Sandy ground: pale patches, a wind ripple, dry tufts and one small bush",
        (TerrainStyle.ScrubSand, _) => "Busier: a second bush and more tufts",
        (TerrainStyle.DryScrub, 0) => "Hard dry ground: pale patches, gravel and a dry tuft",
        (TerrainStyle.DryScrub, _) => "Busier: more gravel and a short crack",
        (TerrainStyle.DryBrush, 0) => "Dry ground with dark twiggy brush clumps",
        (TerrainStyle.DryBrush, _) => "Busier: a third clump and dry tufts",
        (TerrainStyle.DesertBrush, 0) => "Warm desert ground with grey-green sage clumps and a hairline crack",
        (TerrainStyle.DesertBrush, _) => "Busier: a third sage clump and a second crack",
        (TerrainStyle.TundraSnow, 0) => "Thin snow with grey-green tundra peeking through and a few blades",
        (TerrainStyle.TundraSnow, _) => "Busier: more tundra showing and a stone",
        _ => "",
    };

    // ---------------------------------------------------------------- ramps

    /// <summary>Five steps of one hue family from the style guide; the base is the overview colour.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(TerrainStyle style, string edge, string shade, string light, string highlight) =>
            new(new Color(edge), new Color(shade), TerrainTextures.BaseColor(style), new Color(light), new Color(highlight));
    }

    /// <summary>The style-guide ramp for a drawn style; its base is exactly today's overview colour.</summary>
    private static Ramp RampOf(TerrainStyle style) => style switch
    {
        TerrainStyle.Grass => Ramp.Of(style, "3B5E3A", "527F4F", "6FA069", "86B37A"),
        TerrainStyle.ForestGrass => Ramp.Of(style, "2E4D35", "395F41", "4E7D56", "5E8E64"),
        TerrainStyle.ForestFloor => Ramp.Of(style, "34483A", "435B41", "597759", "6A8866"),
        TerrainStyle.DenseForestFloor => Ramp.Of(style, "2B4030", "36503A", "4A6B4C", "5A7D5A"),
        TerrainStyle.Sand => Ramp.Of(style, "7E6E4A", "A08F66", "C8B78C", "E3D6B5"),
        TerrainStyle.Rock or TerrainStyle.Mountain => Ramp.Of(style, "4A4542", "625B56", "8B837D", "A49C95"),
        TerrainStyle.Peak => Ramp.Of(style, "5F5955", "8E8A85", "C4C7C5", "EEF3F1"),
        TerrainStyle.Snow => Ramp.Of(style, "9AAAA8", "B8C6C4", "DCE5E0", "F4F8F6"),
        TerrainStyle.FertileSoil => Ramp.Of(style, "4A3A2A", "5C4B35", "86704F", "9A8460"),
        TerrainStyle.Tundra => Ramp.Of(style, "5A675B", "757F75", "96A596", "A8B6A8"),
        TerrainStyle.ScrubGrass => Ramp.Of(style, "66593A", "86784C", "A8975F", "BBAA70"),
        TerrainStyle.DryScrub => Ramp.Of(style, "5E5339", "7E7250", "9C8E64", "B0A275"),
        // The styles below have no row of their own in the style guide; each uses the
        // neighbouring guide ramp moved so that its base is the style's overview colour.
        TerrainStyle.ScrubSand => Ramp.Of(style, "6E5F38", "948450", "B9A86F", "CBBC8A"),   // between Scrub grass and Sand
        TerrainStyle.DryBrush => Ramp.Of(style, "584C32", "786B48", "96875C", "AA9B6D"),    // Dry scrub / brush, one shade darker
        TerrainStyle.DesertBrush => Ramp.Of(style, "5F4F37", "7F6C4C", "9F8B62", "B39F76"), // Dry scrub / brush, a touch warmer
        TerrainStyle.TundraSnow => Ramp.Of(style, "A1ADA5", "BEC8BF", "E2E8E0", "F4F8F6"),  // Snow, a touch greener
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    // Accent colours borrowed from other ramps for motifs (rule P1: at most two per tile).
    private static readonly Color RockLight = new("8B837D");
    private static readonly Color RockShade = new("625B56");
    private static readonly Color TimberShade = new("6E4E31");
    private static readonly Color ScrubLight = new("A8975F");
    private static readonly Color ScrubShade = new("86784C");
    private static readonly Color SnowEdge = new("9AAAA8");
    // Round 2 accents. Straw on tilled soil is Thatch shade; scrub bushes keep the
    // olive of today's bush motif; desert sage borrows the Tundra shade and light.
    private static readonly Color Straw = new("A98A45");
    private static readonly Color RockHighlight = new("A49C95");
    private static readonly Color BushDark = new("6E7040");
    private static readonly Color BushLight = new("8D9152");
    private static readonly Color TundraBase = new("869586");
    private static readonly Color TundraShade = new("757F75");
    private static readonly Color TundraLight = new("96A596");

    // ---------------------------------------------------------------- tiles

    /// <summary>Paints one tile: base fill, then the style's mottling and motifs.</summary>
    private static Image Paint(TerrainStyle style, int variant, int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var ramp = RampOf(style);
        image.Fill(ramp.Base);
        var busy = variant == 1;
        var painter = new Painter(image, size, new PixelArt.Stream(PixelArt.Hash((int)style * 7 + 3, variant, size)));
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
            case TerrainStyle.Mountain:
                painter.Relief(ramp, variant, salt, snowy: false);
                break;
            case TerrainStyle.Peak:
                painter.Relief(ramp, variant, salt, snowy: true);
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
            case TerrainStyle.FertileSoil:
                painter.Tilth(salt, variant, ramp);
                painter.SoilMotifs(busy, ramp, RockLight, Straw);
                break;
            case TerrainStyle.ScrubGrass:
                painter.Mottle(salt, variant, 0.13f, 0f, ramp.Shade, ramp.Light);
                painter.Tufts(busy ? 4 : 3, ramp.Shade, ramp.Highlight);
                painter.Bushes(busy ? 3 : 2, BushDark, BushLight, ramp.Edge);
                break;
            case TerrainStyle.ScrubSand:
                painter.Mottle(salt, variant, 0.06f, 0.14f, ramp.Shade, ramp.Light, broad: true);
                painter.Ripples(1, ramp.Light, ramp.Shade);
                painter.Tufts(busy ? 4 : 2, ramp.Shade, ramp.Highlight);
                painter.Bushes(busy ? 2 : 1, BushDark, BushLight, ramp.Edge);
                break;
            case TerrainStyle.DryScrub:
                painter.Mottle(salt, variant, 0.07f, 0.13f, ramp.Shade, ramp.Light, broad: true);
                painter.Gravel(busy ? 3 : 2, RockHighlight, RockLight, ramp.Edge);
                painter.Tufts(1, ramp.Shade, ramp.Highlight);
                if (busy) painter.Cracks(1, ramp.Edge, ramp.Highlight);
                break;
            case TerrainStyle.DryBrush:
                painter.Mottle(salt, variant, 0.08f, 0.12f, ramp.Shade, ramp.Light);
                painter.Brush(busy ? 3 : 2, ramp.Edge, BushDark, BushLight);
                if (busy) painter.Tufts(2, ramp.Shade, ramp.Highlight);
                break;
            case TerrainStyle.DesertBrush:
                painter.Mottle(salt, variant, 0.05f, 0.13f, ramp.Shade, ramp.Light, broad: true);
                painter.Bushes(busy ? 3 : 2, TundraShade, TundraLight, ramp.Edge);
                painter.HairlineCracks(busy ? 2 : 1, ramp.Shade);
                break;
            case TerrainStyle.TundraSnow:
                painter.Mottle(salt, 0, 0.05f, 0.12f, ramp.Shade, ramp.Light, broad: true);
                painter.Peeks(busy ? 3 : 2, TundraBase, TundraShade, ramp.Highlight);
                if (size >= 32) painter.Tufts(busy ? 2 : 1, TundraShade, TundraBase); // at 16 px a tuft would read as a second peek
                if (busy) painter.Pebbles(1, ramp.Highlight, ramp.Edge);
                break;
        }
        return image;
    }

    // ---------------------------------------------------------------- hills

    /// <summary>
    /// Hill relief over the tile's own ground (rule T4): one broad mound (v0)
    /// or two smaller ones (v1), each drawn only as a lit crescent on its
    /// north-west rim and a shaded crescent on its south-east rim.
    /// </summary>
    private static Image PaintHill(int variant, int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var unit = size / 32f;
        if (variant == 0)
        {
            Mound(image, unit, 16f, 16.5f, 12f, 8.5f);
        }
        else
        {
            Mound(image, unit, 10f, 21.5f, 7.5f, 5.5f);
            Mound(image, unit, 22f, 10.5f, 7.5f, 5.5f);
        }
        return image;
    }

    /// <summary>
    /// One mound in 32-unit tile space. The lit crescent is the part of the
    /// mound's ellipse not covered by the same ellipse moved two pixels toward
    /// the south-east, so it is two pixels thick at the north-west and thins
    /// to nothing at the sides; the shaded crescent mirrors it on the
    /// south-east. A further pixel inside each crescent is drawn at half alpha
    /// so the slope fades into the hilltop. The sides are left open, so the
    /// mound never closes into a ring.
    /// </summary>
    private static void Mound(Image image, float unit, float centerX, float centerY, float radiusX, float radiusY)
    {
        var lit = new Color(1f, 0.97f, 0.84f, 0.28f);   // T4: light at alpha 0.25–0.30
        var shade = new Color(0.12f, 0.09f, 0.05f, 0.34f); // T4: shade at alpha 0.30–0.38
        var step = (size: image.GetWidth(), pixel: 1f / unit); // one output pixel, in 32-unit space
        var core = (unit >= 1f ? 2f : 1f) * step.pixel;       // T4: two pixels thick at 32 px, one at 16 px
        var diagonal = 1f / MathF.Sqrt(2f);
        bool Inside(float x, float y, float shift) =>
            Sq((x - centerX - shift * diagonal) / radiusX) + Sq((y - centerY - shift * diagonal) / radiusY) <= 1f;
        for (var py = 0; py < step.size; py++)
            for (var px = 0; px < step.size; px++)
            {
                var x = (px + 0.5f) * step.pixel;
                var y = (py + 0.5f) * step.pixel;
                if (!Inside(x, y, 0f)) continue;
                // Which way this bit of the mound faces: +1 toward the north-west light, −1 away.
                var dx = (x - centerX) / radiusX;
                var dy = (y - centerY) / radiusY;
                var facing = -(dx + dy) * diagonal / MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
                if (MathF.Abs(facing) < 0.3f) continue; // keep the sides open
                var away = facing > 0 ? 1f : -1f;      // move the covering ellipse away from this rim
                var band = !Inside(x, y, away * core) ? 1f : !Inside(x, y, away * (core + step.pixel)) ? 0.5f : 0f;
                if (band == 0f) continue;
                var color = facing > 0 ? lit : shade;
                PixelArt.Put(image, px, py, new Color(color, color.A * band));
            }
    }

    private static float Sq(float value) => value * value;

    // ---------------------------------------------------------------- painter

    /// <summary>Draws into one square tile image; pixel coordinates are clipped to the tile.</summary>
    private struct Painter(Image image, int size, PixelArt.Stream random)
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

        /// <summary>
        /// Draws a small motif from text rows: 'l', 'd' and 's' pick the first,
        /// second and third colour, any other character leaves the ground.
        /// </summary>
        private readonly void Stamp(int x, int y, string[] rows, Color l, Color d, Color s)
        {
            for (var row = 0; row < rows.Length; row++)
                for (var column = 0; column < rows[row].Length; column++)
                {
                    var mark = rows[row][column];
                    if (mark == 'l') Put(x + column, y + row, l);
                    else if (mark == 'd') Put(x + column, y + row, d);
                    else if (mark == 's') Put(x + column, y + row, s);
                }
        }

        /// <summary>A random spot for a motif of the given pixel size, one pixel clear of every edge.</summary>
        private (int X, int Y) Spot(int width, int height) =>
            (random.Range(1, Math.Max(2, size - width - 1)), random.Range(1, Math.Max(2, size - height - 1)));

        /// <summary>
        /// Soft mottling (rule T1): the darkest and lightest parts of a smooth,
        /// tile-periodic noise field become irregular blobs in the shade and
        /// light steps, kept apart by base colour. Lone pixels are folded back
        /// into their surroundings, so the result is patches, never grain.
        /// The noise is weighted toward patches about four pixels across and
        /// spread evenly, so a field of identical tiles reads as one soft
        /// texture rather than a repeating pattern of big shapes; the broad
        /// grain, for smooth ground like sand and snow, makes fewer, larger and
        /// softer patches. The fractions
        /// are of the tile's pixels (about 60% of that at 16 px, which keeps
        /// only the impression), pooled over both variants so the two share
        /// one threshold and meet without a seam.
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

        /// <summary>A fallen twig: four pixels in a shallow bend, two at 16 px.</summary>
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
        /// Wind ripples on sand (rule T6): a low arc in the light step whose
        /// middle has a one-pixel shaded lee on its south side. Shorter at 16 px.
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

        /// <summary>Snow drifts (rule T6): a long low arc in the shade step with a lit crest along its north side.</summary>
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
        /// Bare rock (rule T6): the tile is split into a few large angular
        /// facets (a Voronoi pattern that repeats with the tile), each a flat
        /// plane in the light, base or shade step. Where a shaded facet lies
        /// just south or east of a lit one, a one-pixel crease in the edge step
        /// marks the step down, so the stone reads as lit from the north-west;
        /// the gentler boundaries are tone changes only. Both variants share
        /// the facets so they meet without a seam; v1 adds a crack and stones.
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

        /// <summary>Rows per ridge of tilled soil: eight at 32 px, four at 16 px, so ridges keep the same spacing on the ground at both zooms.</summary>
        private readonly int RidgePeriod => Small ? 4 : 8;

        /// <summary>
        /// Tilled farmland (rule T5) seen from above: soft ridges of turned earth
        /// running east–west, four to a tile at both sizes. At 32 px each
        /// eight-row ridge has a lit north face (light step, two rows), a flat top
        /// (base), a shaded south shoulder (shade step) and a trough (shade, edge
        /// step where it runs deeper); at 16 px the same profile is squeezed into
        /// four rows. The ridge rows line up with the crop field rows drawn on
        /// top: every ridge's lit face and trough fall on a crop row's lit and
        /// shaded lines, so a field sits two crop rows to a ridge at 32 px and one
        /// at 16 px. The lit face is broken into clods three to seven pixels long
        /// by one-pixel dips, and some clods start with a highlight pixel where
        /// their north-west corner catches the light; the trough deepens to the
        /// edge step in short runs and the shoulder swells over them, so ridges
        /// thicken and thin along their length. One or two damp patches lie in
        /// the troughs, where every step drops by one and the next lit face loses
        /// its shine. Ridge pattern decisions are made per column, so ridges run
        /// on into the next tile, and columns within three pixels of an edge use
        /// a pattern shared by both variants, so the variants meet without a seam.
        /// </summary>
        public void Tilth(int salt, int variant, Ramp ramp)
        {
            Color[] steps = [ramp.Edge, ramp.Shade, ramp.Base, ramp.Light, ramp.Highlight]; // index = level + 2
            var period = RidgePeriod;
            var width = size;
            int Own(int x) => x > 2 && x < width - 3 ? salt + 1000 * (variant + 1) : salt;
            int Wrap(int x) => (x % width + width) % width;
            // A dip between two clods on a ridge's lit face.
            bool Dip(int x, int ridge) => PixelArt.Hash(Wrap(x), ridge, Own(Wrap(x))) % (width >= 32 ? 5 : 4) == 0;
            // A deeper stretch of trough, in runs of three pixels (two at 16 px).
            bool Deep(int x, int ridge) => PixelArt.Hash(Wrap(x) / (width >= 32 ? 3 : 2), ridge, Own(Wrap(x)) + 7) % 4 == 0;
            // Roughness in two-pixel runs, for the edges of the lit face and the shoulder.
            bool Rough(int x, int ridge, int which, int oneIn) => PixelArt.Hash(Wrap(x) / 2, ridge, Own(Wrap(x)) + which) % oneIn == 0;
            var levels = new int[size, size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var ridge = y / period;
                    // The 32 px profile row; the 16 px tile keeps rows 0 (lit face), 3 (top), 6 (shoulder) and 7 (trough).
                    var row = Small ? (y % period) switch { 0 => 0, 1 => 3, 2 => 6, _ => 7 } : y % period;
                    levels[x, y] = row switch
                    {
                        // Lit face: a dip shows the base; the first pixel after a dip may catch the light.
                        0 => Dip(x, ridge) ? 0 : Dip(x - 1, ridge) && PixelArt.Hash(x, ridge, Own(x) + 3) % 2 == 0 ? 2 : 1,
                        // Lower lit face, ragged where it meets the top.
                        1 => Rough(x, ridge, 5, 3) ? 0 : 1,
                        // Upper shoulder: shaded only over part of a deep stretch.
                        5 => Deep(x, ridge) && PixelArt.Hash(x, ridge, Own(x) + 13) % 2 == 0 ? -1 : 0,
                        // Shoulder: shaded, with short breaks at 32 px; at 16 px only over deep stretches.
                        6 => Small ? (Deep(x, ridge) && PixelArt.Hash(x, ridge, Own(x) + 11) % 3 != 0 ? -1 : 0) : Rough(x, ridge, 11, 4) ? 0 : -1,
                        7 => Deep(x, ridge) ? -2 : -1,
                        _ => 0,
                    };
                }
            // Damp patches: a long, low stretch of one trough where water has soaked in, one
            // pixel clear of every edge. The trough and shoulder drop a step (the shoulder only
            // away from the tips), and the lit face just south loses its shine.
            for (var patch = 0; patch < (variant == 1 && !Small ? 2 : 1); patch++)
            {
                var half = Small ? 3 : random.Range(6, 10);
                var centre = random.Range(half + 1, size - half - 1);
                var trough = random.Range(0, size / period - 1) * period + period - 1;
                for (var x = centre - half; x <= centre + half; x++)
                {
                    var reach = half - Math.Abs(x - centre); // 0 at the tips
                    levels[x, trough] = Math.Max(-2, levels[x, trough] - 1);
                    if (reach >= 2) levels[x, trough - 1] = Math.Max(-2, levels[x, trough - 1] - 1);
                    if (reach >= 3) levels[x, trough + 1] = Math.Min(levels[x, trough + 1], 0);
                }
            }
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    Put(x, y, steps[levels[x, y] + 2]);
        }

        /// <summary>
        /// The small things lying on tilled soil, all on the ridge tops and one
        /// pixel clear of the edges: clods (a highlight pixel, a light pixel and
        /// shaded pixels to the south-east), a clod fallen into a trough that
        /// breaks its dark line and a small stone (Rock light with a shadow in
        /// the soil edge step). The busier variant, about one tile in four, adds
        /// the occasional pale straw fleck and pale root, so they never line up
        /// in a grid across a field. At 16 px each is one pixel and the root is
        /// left out.
        /// </summary>
        public void SoilMotifs(bool busy, Ramp ramp, Color stone, Color straw)
        {
            var period = RidgePeriod;
            for (var index = 0; index < (Small ? 1 : 3); index++)
            {
                var (x, y) = OnRidge(3);
                Put(x, y, ramp.Highlight);
                if (Small) continue;
                Put(x + 1, y, ramp.Light);
                Put(x + 1, y + 1, ramp.Shade);
                Put(x + 2, y + 1, ramp.Shade);
            }
            if (!Small)
            {
                var x = random.Range(2, size - 4);
                var y = random.Range(0, size / period - 1) * period + period - 1;
                Put(x, y, ramp.Light);
                Put(x + 1, y, ramp.Base);
                Put(x + 2, y, ramp.Edge);
            }
            {
                var (x, y) = OnRidge(2);
                Put(x, y, stone);
                if (!Small)
                {
                    Put(x + 1, y, stone);
                    Put(x + 1, y + 1, ramp.Edge);
                }
            }
            if (busy)
            {
                var (x, y) = OnRidge(3);
                Put(x, y, straw);
                if (!Small)
                {
                    Put(x + 1, y, straw);
                    Put(x + 2, y + 1, straw);
                }
            }
            if (busy && !Small)
            {
                // A pale root: a short kinked line in the soil highlight.
                var (x, y) = OnRidge(4);
                Put(x, y + 1, ramp.Highlight);
                Put(x + 1, y, ramp.Highlight);
                Put(x + 2, y, ramp.Highlight);
                Put(x + 3, y + 1, ramp.Highlight);
            }
        }

        /// <summary>
        /// A spot on a ridge top of tilled soil for a motif of the given width
        /// and up to two rows tall: rows 2–3 of a 32 px ridge, row 1 of a 16 px
        /// one, one pixel clear of every edge.
        /// </summary>
        private (int X, int Y) OnRidge(int width) =>
            (random.Range(1, size - width - 1), random.Range(0, size / RidgePeriod) * RidgePeriod + (Small ? 1 : random.Range(2, 4)));

        /// <summary>
        /// Low scrub bushes seen from above (rule T6, scrub keeps the bush motif):
        /// a rounded clump four pixels across, lit on its north-west and darker
        /// on its south-east, with two pixels of ground shadow to the south-east.
        /// At 16 px a bush is a lit pixel, two dark pixels and the shadow.
        /// </summary>
        public void Bushes(int count, Color dark, Color light, Color shadow)
        {
            for (var index = 0; index < Count(count); index++)
            {
                if (!Small)
                {
                    // Rows of a 5 × 4 cell: l lit leaves, d shaded leaves, s ground shadow.
                    var (x, y) = Spot(5, 4);
                    Stamp(x, y, [" ll  ", "llld ", "lldds", " dds "], light, dark, shadow);
                }
                else
                {
                    var (x, y) = Spot(3, 3);
                    Put(x, y, light);
                    Put(x + 1, y, dark);
                    Put(x, y + 1, dark);
                    Put(x + 1, y + 1, shadow);
                }
            }
        }

        /// <summary>
        /// Dry brush: a dense, solid clump of dark olive twigs, lit with a few
        /// olive leaves on its north-west, its south-east side and a few twig
        /// tips poking out of the crown in the dark twig colour. Wider, darker
        /// and spikier than a scrub bush, so brush reads at 16 px as dark clumps.
        /// At 16 px a clump is three pixels wide: a leaf pixel, two core pixels
        /// and a dark twig base, wider and darker than a 16 px scrub bush.
        /// </summary>
        public void Brush(int count, Color twig, Color core, Color leaf)
        {
            for (var index = 0; index < Count(count); index++)
            {
                if (!Small)
                {
                    // l olive leaf, d dark olive core, s dark twigs, tips and south-east edge.
                    var (x, y) = Spot(6, 5);
                    Stamp(x, y, [" s  s ", " llds ", "slddds", " ddds ", "  ss  "], leaf, core, twig);
                }
                else
                {
                    var (x, y) = Spot(3, 2);
                    Stamp(x, y, ["ldd", " ss"], leaf, core, twig);
                }
            }
        }

        /// <summary>
        /// Gravel: a small pebble lit on its north-west (two stone tones) with a
        /// shadow pixel to the east, and one loose grit pixel beside it. At
        /// 16 px one lit pixel and its shadow.
        /// </summary>
        public void Gravel(int count, Color stone, Color stoneShade, Color shadow)
        {
            for (var index = 0; index < Count(count); index++)
            {
                if (!Small)
                {
                    var (x, y) = Spot(4, 3);
                    Stamp(x, y, ["ll  ", "lds ", "   d"], stone, stoneShade, shadow);
                }
                else
                {
                    var (x, y) = Spot(2, 2);
                    Put(x, y, stone);
                    Put(x + 1, y + 1, shadow);
                }
            }
        }

        /// <summary>Hairline cracks in baked ground: a short forked line in one dark step (shorter at 16 px).</summary>
        public void HairlineCracks(int count, Color crack)
        {
            for (var index = 0; index < Count(count); index++)
            {
                if (!Small)
                {
                    var (x, y) = Spot(7, 4);
                    Put(x, y + 1, crack);
                    Put(x + 1, y + 1, crack);
                    Put(x + 2, y + 2, crack);
                    Put(x + 3, y + 2, crack);
                    Put(x + 4, y + 1, crack);
                    Put(x + 5, y, crack);
                    Put(x + 4, y + 3, crack);
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

        /// <summary>
        /// Tundra peeking through thin snow: a small rounded hole in the snow
        /// showing the Tundra base, its north rim in the Tundra shade where the
        /// snow wall shades it, and a lit snow lip (the given highlight) on the
        /// south rim that faces the light. A 2 × 2 patch at 16 px.
        /// </summary>
        public void Peeks(int count, Color ground, Color shade, Color lip)
        {
            for (var index = 0; index < Count(count); index++)
            {
                if (!Small)
                {
                    var (x, y) = Spot(5, 4);
                    Stamp(x, y, [" ddd ", "dllll", " lll ", "  ss "], ground, shade, lip);
                }
                else
                {
                    var (x, y) = Spot(2, 2);
                    Stamp(x, y, ["dd", "ll"], ground, shade, lip);
                }
            }
        }

        /// <summary>
        /// Top-down mountain relief (rule T3). Each peak is a pyramid of rock
        /// seen from above: four flat faces meet at the summit over a jittered
        /// diamond footprint, so the summit reads as the point where the tones
        /// meet, the way today's triangles read but without the side view. The
        /// face toward the north-west light takes the light step, the face away
        /// from it the shade step, and the two faces across the light
        /// (north-east and south-west) keep the base, so the foot fades into
        /// the ground. The upper half of every face that is not shaded is one
        /// step lighter, which puts a lit cap on the summit. Where the shaded
        /// face meets the others there is a one-pixel crease in the edge step,
        /// a little noise roughens every ridge, scree lies around the foot, and
        /// overlapping peaks keep whichever is higher. Peaks add a snow cap with
        /// a ragged edge: Peak highlight on the lit and level faces, the snow
        /// ramp's edge step on the shaded one, dark enough to read against the
        /// pale peak ground. The peaks stay inside the tile, so neighbouring
        /// mountain tiles meet on flat, scree-strewn ground and never show a seam.
        /// </summary>
        public void Relief(Ramp ramp, int variant, int salt, bool snowy)
        {
            var peaks = Peaks(variant, snowy);
            var noise = Field.Pair(size, salt + 5, Field.Octave(8, 0.6f), Field.Octave(4, 0.4f))[variant];
            var pad = size + 2;
            var faceOf = new int[pad, pad];    // peak × 16 + face, or −1 on the flat foot
            var toneOf = new int[pad, pad];    // 1 light, 0 base, −1 shade
            var gaugeOf = new float[pad, pad]; // 0 at a summit, 1 at the foot, more beyond it
            for (var y = -1; y <= size; y++)
                for (var x = -1; x <= size; x++)
                {
                    var u = (x + 0.5f) / unit;
                    var v = (y + 0.5f) / unit;
                    var (bestHeight, face, tone, nearest) = (0f, -1, 0, float.MaxValue);
                    for (var p = 0; p < peaks.Length; p++)
                    {
                        // Each side's gauge is nudged by its own patch of noise so ridges and the foot are ragged, not ruled.
                        var (gauge, side) = peaks[p].Locate(u, v, index => 0.16f * (noise[x + index * 11 + p * 5, y + index * 7] - 0.5f));
                        nearest = MathF.Min(nearest, gauge);
                        var h = peaks[p].Height * (1f - gauge);
                        if (h <= bestHeight) continue;
                        (bestHeight, face, tone) = (h, p * 16 + side, peaks[p].Tone(side));
                        gaugeOf[x + 1, y + 1] = gauge;
                    }
                    faceOf[x + 1, y + 1] = face;
                    toneOf[x + 1, y + 1] = tone;
                    if (face < 0) gaugeOf[x + 1, y + 1] = nearest;
                }
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var face = faceOf[x + 1, y + 1];
                    if (face < 0) continue;
                    var tone = toneOf[x + 1, y + 1];
                    var gauge = gaugeOf[x + 1, y + 1];
                    // A crease: the shaded face against a lit or level face to its north or west.
                    var crease = false;
                    foreach (var (dx, dy) in (ReadOnlySpan<(int, int)>)[(-1, 0), (0, -1), (-1, -1)])
                    {
                        var other = faceOf[x + 1 + dx, y + 1 + dy];
                        crease |= tone < 0 && other >= 0 && other != face && toneOf[x + 1 + dx, y + 1 + dy] >= 0;
                    }
                    var snowLine = 0.56f + 0.3f * (noise[x + 3, y + 9] - 0.5f);
                    Color color;
                    if (snowy && gauge < snowLine) color = tone < 0 ? SnowEdge : ramp.Highlight;
                    else if (crease) color = ramp.Edge;
                    // Height bands: the upper part of each face that is not shaded is one step lighter.
                    else if (tone > 0) color = gauge < 0.48f && !snowy ? ramp.Highlight : ramp.Light;
                    else if (tone < 0) color = ramp.Shade;
                    else color = gauge < 0.52f ? ramp.Light : ramp.Base;
                    Put(x, y, color);
                }
            // Scree: loose stones on the flat ground just beyond the foot.
            var placed = 0;
            for (var attempt = 0; attempt < 80 && placed < Count(6); attempt++) // T1: at most six small details
            {
                var (x, y) = Spot(2, 2);
                var gauge = gaugeOf[x + 1, y + 1];
                if (faceOf[x + 1, y + 1] >= 0 || gauge > 1.4f) continue;
                placed++;
                if (!Small && placed % 3 == 0)
                {
                    Put(x, y, ramp.Light);
                    Put(x + 1, y + 1, ramp.Shade);
                }
                else Put(x, y, placed % 2 == 0 ? ramp.Shade : ramp.Light);
            }
        }

        /// <summary>
        /// The peaks of each variant, as summit, relative height and the
        /// footprint's north, east, south and west corners in 32-unit space:
        /// v0 one broad peak with a small one at its south-east foot, v1 twin
        /// peaks with a knoll. Snow-capped peaks stand taller and fill most of
        /// their tile. Every corner keeps two pixels clear of the tile edge.
        /// </summary>
        private static Peak[] Peaks(int variant, bool snowcapped) => snowcapped
            ? variant == 0
                ? [new(15f, 14.5f, 13f, [(16f, 2f), (30f, 15f), (14.5f, 30f), (2f, 16.5f)])]
                :
                [
                    new(11f, 18.5f, 10f, [(11.5f, 8f), (21f, 19f), (10.5f, 30f), (2f, 18f)]),
                    new(21.5f, 11.5f, 12f, [(21f, 2f), (30f, 12f), (22f, 22f), (11.5f, 11f)]),
                ]
            : variant == 0
            ?
            [
                new(14f, 14.5f, 12f, [(15f, 2.5f), (27f, 14f), (13.5f, 27.5f), (2.5f, 16f)]),
                new(24.5f, 23.5f, 5.5f, [(24f, 17.5f), (29.5f, 23f), (24.5f, 29.5f), (18.5f, 23.5f)]),
            ]
            :
            [
                new(10f, 18.5f, 9f, [(10.5f, 9.5f), (19f, 19f), (9.5f, 28.5f), (2.5f, 18f)]),
                new(21f, 11f, 11f, [(20.5f, 2.5f), (29.5f, 11.5f), (21.5f, 21f), (12f, 10.5f)]),
                new(25.5f, 25f, 4f, [(25.5f, 20.5f), (29.5f, 25.5f), (25f, 29.5f), (21f, 24.5f)]),
            ];

        /// <summary>
        /// A peak seen from above: its summit, relative height and the corners
        /// of its footprint, in order round the summit. Each side of the
        /// footprint carries one flat face sloping down from the summit.
        /// </summary>
        private sealed record Peak(float X, float Y, float Height, (float X, float Y)[] Corners)
        {
            /// <summary>
            /// The face a point lies on and its gauge: 0 at the summit, 1 on the
            /// foot, more beyond it. <paramref name="nudge"/> may shift each
            /// side's gauge a little to roughen the ridges.
            /// </summary>
            public (float Gauge, int Face) Locate(float u, float v, Func<int, float>? nudge = null)
            {
                var (best, face) = (float.MinValue, 0);
                for (var side = 0; side < Corners.Length; side++)
                {
                    var (nx, ny, distance) = Side(side);
                    var gauge = (nx * (u - X) + ny * (v - Y)) / distance + (nudge?.Invoke(side) ?? 0f);
                    if (gauge > best) (best, face) = (gauge, side);
                }
                return (best, face);
            }

            /// <summary>A face's tone from the way it faces: 1 toward the north-west light, −1 away, 0 across.</summary>
            public int Tone(int face)
            {
                var (nx, ny, _) = Side(face);
                var facing = -(nx + ny) / MathF.Sqrt(2f);
                return facing > 0.45f ? 1 : facing < -0.45f ? -1 : 0;
            }

            /// <summary>The outward unit normal of a footprint side and its distance from the summit.</summary>
            private (float X, float Y, float Distance) Side(int index)
            {
                var (ax, ay) = Corners[index];
                var (bx, by) = Corners[(index + 1) % Corners.Length];
                var length = MathF.Sqrt(Sq(bx - ax) + Sq(by - ay));
                var nx = (by - ay) / length;
                var ny = -(bx - ax) / length;
                var distance = nx * (ax - X) + ny * (ay - Y);
                return distance < 0f ? (-nx, -ny, -distance) : (nx, ny, distance);
            }
        }

        /// <summary>
        /// Keeps the mottling to soft patches (rule T1). A shade or light pixel
        /// survives only if it has a neighbour of its own kind both across
        /// (west or east) and down (north or south), which removes lone specks
        /// and one-pixel-thin streaks; a base pixel enclosed on three or four
        /// sides by one kind is filled. Neighbours wrap round the tile so the
        /// result still repeats seamlessly. Two passes settle what the first
        /// one exposes.
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

    // ---------------------------------------------------------------- field

    /// <summary>
    /// Smooth noise that repeats with the tile, built from soft round bumps
    /// (one per lattice cell, at a hashed offset and strength), so its
    /// thresholds give rounded, organic patches instead of the square blocks
    /// of plain lattice noise. A patch leaving one edge comes back in on the
    /// opposite edge. Within six pixels of the edge both variants use the same
    /// shared bumps and only further in do their own take over, so v0 and v1
    /// meet without a seam. The 16 px tile samples the same field at half
    /// resolution.
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
    }
}
