using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Crops;

/// <summary>
/// Proposed crop art: farm-field overlays for the Fertile soil tile (one per
/// crop and growth state) and the orchard tree stages.
///
/// A field is a transparent overlay the size of one tile (32 px, or 16 px for
/// the mid-zoom atlas) drawn over the Fertile soil ground tile. Its rows run
/// east–west on a four-pixel period at both sizes, and every mark wraps
/// around the tile edges, so a field several tiles across joins without a
/// seam. Orchard trees keep the broadleaf silhouette in the orchard canopy
/// ramp. Rules applied from STYLE.md: P1–P3 (ramps), L1/L2/L4 (north-west
/// light, south-east shadow, edge outline), S1/S4 (separately drawn 16 px
/// versions), T5 (tilled soil), N2/N3 (canopy, fruit), N5 (fields), N6 (orchard).
/// </summary>
public sealed class CropsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "crops";
    public string Name => "crops";

    /// <summary>The crops a field can grow.</summary>
    public enum Crop { Grain, Potato, Greens }

    /// <summary>The growth states a field passes through (N5).</summary>
    public enum Growth { Prepared, Seeded, Sprout, Mature, Harvested }

    /// <summary>Rows repeat every four pixels at both sizes (T5), which divides both tile sizes, so rows continue into the next tile.</summary>
    private const int RowPeriod = 4;

    /// <summary>Within a row period: row 0 is the lit south wall of the furrow (it faces the north-west light), row 3 the shaded bottom of the next furrow.</summary>
    private const int CrestRow = 0;
    private const int TroughRow = 3;

    /// <summary>The L2 ground shadow colour used by every nature sprite.</summary>
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    /// <summary>One colour ramp from STYLE.md section 2, darkest to brightest.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));
    }

    private static readonly Ramp Soil = Ramp.Of("4A3A2A", "5C4B35", "735F45", "86704F", "9A8460");
    private static readonly Ramp Thatch = Ramp.Of("6B5528", "A98A45", "D2AE5E", "E6C77B", "F0DA9A");
    private static readonly Ramp Canopy = Ramp.Of("2E4A2A", "476B36", "557D3E", "6C9A4B", "8DB660");
    private static readonly Ramp OrchardCanopy = Ramp.Of("3C5F2E", "4C7A3A", "5E8C45", "79A657", "9BC66F");
    private static readonly Ramp Fruit = Ramp.Of("9A4E1E", "C8702E", "E0893F", "F6C27A", "FFE0A8");

    /// <summary>Sown seed is Timber light (N5); the heart and veins of a greens rosette are Cloth light.</summary>
    private static readonly Color Seed = new("A77C52");
    private static readonly Color Vein = new("E8DCC0");

    /// <summary>The round-1 fields: every grain state and the mature potato and greens.</summary>
    private static readonly (Crop Crop, Growth Growth)[] Round1Fields =
    [
        (Crop.Grain, Growth.Prepared), (Crop.Grain, Growth.Seeded), (Crop.Grain, Growth.Sprout),
        (Crop.Grain, Growth.Mature), (Crop.Grain, Growth.Harvested),
        (Crop.Potato, Growth.Mature), (Crop.Greens, Growth.Mature),
    ];

    private static readonly NatureSprite[] OrchardStages =
        [NatureSprite.OrchardGrowing, NatureSprite.OrchardFruiting, NatureSprite.OrchardPicked];

    private static readonly Dictionary<(NatureSprite, int), Image> OrchardCache = [];

    public IEnumerable<Entry> Render()
    {
        // Rows of eight on the sheet: each 32 px field above its 16 px version, the tiling patch at the end of each row.
        foreach (var size in new[] { 32, 16 })
        {
            var suffix = size == 32 ? "" : ".16";
            foreach (var (crop, growth) in Round1Fields)
                yield return new(Family, FieldId(crop, growth) + suffix, OnSoil(Field(crop, growth, size), size), FieldNote(crop, growth));
            yield return new(Family, "field.tiling" + suffix, Tiling(size),
                "A 3×3 patch of mature grain over the soil variants the map picks, to judge seams and repetition.");
        }
        foreach (var size in new[] { 32, 16 })
            foreach (var stage in OrchardStages)
                yield return new(Family, stage + (size == 32 ? "" : ".16"),
                    Bitmap.Over(TerrainTextures.Tile(TerrainStyle.Grass, 0, size), Orchard(stage, size), 0, 0), OrchardNote(stage));
        foreach (var size in new[] { 32, 16 })
            foreach (var stage in OrchardStages)
                yield return new(Family, stage + (size == 32 ? "" : ".16") + ".sprite", Orchard(stage, size));
        foreach (var size in new[] { 32, 16 })
            foreach (var (crop, growth) in Round1Fields)
                yield return new(Family, FieldId(crop, growth) + (size == 32 ? "" : ".16") + ".sprite", Field(crop, growth, size));
    }

    /// <summary>The reference scene: the three orchard stages come from here, every other nature sprite from the game.</summary>
    public void Apply(ArtSet set)
    {
        set.Nature = (sprite, size) => Array.IndexOf(OrchardStages, sprite) >= 0 ? Orchard(sprite, size) : NatureSprites.Sprite(sprite, size);
    }

    private static string FieldId(Crop crop, Growth growth) =>
        $"field.{crop.ToString().ToLowerInvariant()}.{growth.ToString().ToLowerInvariant()}";

    private static string FieldNote(Crop crop, Growth growth) => growth switch
    {
        Growth.Prepared => "Tilled rows: a lit furrow wall and a shaded trough four pixels apart, a few clods with their shadow.",
        Growth.Seeded => "The tilled rows with seed in Timber light dotted along every trough.",
        Growth.Sprout => "Two-pixel shoots standing in every trough, lit tip to the north.",
        Growth.Harvested => "Pale cut stubble along the rows, one loose straw and a few clods on the furrows.",
        _ => crop switch
        {
            Crop.Grain => "Ripe grain covering the soil: rows of lit ears, each row shading a thin line on the next, with slow lighter swells.",
            Crop.Potato => "Rows of separate low leafy mounds in the canopy ramp, lit north-west, outlined, soil showing between the rows.",
            _ => "Staggered round rosettes in canopy light with a cream heart and faint midrib, each with its own outline and shadow.",
        },
    };

    private static string OrchardNote(NatureSprite stage) => stage switch
    {
        NatureSprite.OrchardFruiting => "The broadleaf silhouette in the orchard ramp with five fruit, each lit north-west.",
        NatureSprite.OrchardPicked => "The same tree with its fruit gone.",
        _ => "A young orchard tree, two thirds of full size, lit the same way.",
    };

    /// <summary>The overlay over the game's Fertile soil tile, as the map will draw it.</summary>
    private static Image OnSoil(Image overlay, int size) =>
        Bitmap.Over(TerrainTextures.Tile(TerrainStyle.FertileSoil, 0, size), overlay, 0, 0);

    /// <summary>A 3×3 patch of mature grain over the soil variants the map would pick there.</summary>
    private static Image Tiling(int size)
    {
        var patch = Bitmap.Empty(size * 3, size * 3);
        var overlay = Field(Crop.Grain, Growth.Mature, size);
        for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
            {
                var soil = TerrainTextures.Tile(TerrainStyle.FertileSoil, TerrainTextures.VariantAt(x, y), size);
                patch.BlitRect(soil, new Rect2I(0, 0, size, size), new Vector2I(x * size, y * size));
                Sheet.Blend(patch, overlay, x * size, y * size);
            }
        return patch;
    }

    // ───────────────────────────── Fields ─────────────────────────────

    /// <summary>A transparent field overlay for one crop and growth state, 32 or 16 px, seamless on every side.</summary>
    public static Image Field(Crop crop, Growth growth, int size)
    {
        var plot = new Plot(size);
        // Ripe grain hides the soil, so its rows are drawn instead of furrows;
        // at 16 px mature potatoes and greens leave too little soil for them.
        var covered = growth == Growth.Mature && (crop == Crop.Grain || !plot.Fine);
        if (!covered) Furrows(plot);
        switch (growth)
        {
            case Growth.Prepared:
                Clods(plot, plot.Fine ? 5 : 2, 1);
                break;
            case Growth.Seeded:
                Seeds(plot);
                Clods(plot, plot.Fine ? 3 : 1, 2);
                break;
            case Growth.Sprout:
                Shoots(plot);
                break;
            case Growth.Mature when crop == Crop.Grain:
                Grain(plot);
                break;
            case Growth.Mature when crop == Crop.Potato:
                Potatoes(plot);
                break;
            case Growth.Mature:
                Greens(plot);
                break;
            case Growth.Harvested:
                // Grain leaves stubble; potatoes and greens leave only turned earth.
                if (crop == Crop.Grain) Stubble(plot);
                Clods(plot, plot.Fine ? (crop == Crop.Grain ? 4 : 8) : 2, 3);
                break;
        }
        return plot.Compose(Canopy.Edge);
    }

    /// <summary>
    /// Smooth tile-periodic variation in [-1, 1]: three plane waves whose
    /// whole-number frequencies repeat exactly once per tile, so slow light
    /// and dark swells continue into the next field tile without a seam.
    /// </summary>
    private static float Swell(int x, int y, int size, float phase)
    {
        var turn = 2f * MathF.PI / size;
        var a = MathF.Sin(turn * (x + 2 * y) + phase);
        var b = MathF.Sin(turn * (2 * x - y) + phase * 1.7f);
        var c = MathF.Sin(turn * (3 * x + y) + phase * 0.6f);
        return (a + b + 0.5f * c) / 2.5f;
    }

    /// <summary>
    /// Tilled rows (T5): a lit line in the soil light step where the furrow
    /// wall faces the north-west light, and a shaded trough line in the shade
    /// step just north of it, two steps apart and four pixels apart, running
    /// the full width. Short breaks keep them from reading as ruled lines.
    /// </summary>
    private static void Furrows(Plot plot)
    {
        var size = plot.Size;
        var stretch = plot.Fine ? 3 : 2;
        for (var y = 0; y < size; y++)
        {
            var row = y % RowPeriod;
            if (row != CrestRow && row != TroughRow) continue;
            var color = row == CrestRow ? Soil.Light : Soil.Shade;
            for (var x = 0; x < size; x++)
            {
                // About one stretch in eleven is left out of each line.
                if (PixelArt.Hash(x / stretch, y, 11 + size) % 11 == 0) continue;
                plot.Mark(x, y, color);
            }
        }
    }

    /// <summary>
    /// Clods on the ridge between furrows (T5): at 32 px a lit west pixel in
    /// the soil highlight, a light east pixel and a shade pixel under it to
    /// the south-east; at 16 px one highlight pixel.
    /// </summary>
    private static void Clods(Plot plot, int count, int salt)
    {
        var stream = new PixelArt.Stream(PixelArt.Hash(plot.Size, salt, 23));
        for (var index = 0; index < count; index++)
        {
            var x = stream.Range(0, plot.Size);
            var y = stream.Range(0, plot.Size / RowPeriod) * RowPeriod + 1;
            plot.Mark(x, y, Soil.Highlight);
            if (!plot.Fine) continue;
            plot.Mark(x + 1, y, Soil.Light);
            plot.Mark(x + 1, y + 1, Soil.Shade);
        }
    }

    /// <summary>Seeded (N5): Timber-light seed dotted along every trough, three pixels apart at 32 px and two at 16 px, with one here and there missing.</summary>
    private static void Seeds(Plot plot)
    {
        var spacing = plot.Fine ? 3 : 2;
        for (var y = TroughRow; y < plot.Size; y += RowPeriod)
            for (var x = (int)(PixelArt.Hash(0, y, 31) % spacing); x < plot.Size; x += spacing)
            {
                if (PixelArt.Hash(x, y, 32) % 5 == 0) continue;
                var jitter = plot.Fine ? (int)(PixelArt.Hash(x, y, 33) % 2) : 0;
                plot.Mark(x + jitter, y, Seed);
            }
    }

    /// <summary>
    /// Sprouting (N5): shoots standing in every trough. At 32 px each is two
    /// pixels, a canopy-base stem with a canopy-highlight tip north of it,
    /// and about one in four has a second leaf to the west; at 16 px each is
    /// one canopy-light pixel.
    /// </summary>
    private static void Shoots(Plot plot)
    {
        var spacing = plot.Fine ? 3 : 2;
        for (var y = TroughRow; y < plot.Size; y += RowPeriod)
            for (var x = (int)(PixelArt.Hash(1, y, 41) % spacing); x < plot.Size; x += spacing)
            {
                if (PixelArt.Hash(x, y, 42) % 6 == 0) continue;
                if (!plot.Fine)
                {
                    plot.Mark(x, y, Canopy.Light);
                    continue;
                }
                var sx = x + (int)(PixelArt.Hash(x, y, 43) % 2);
                plot.Mark(sx, y, Canopy.Base);
                plot.Mark(sx, y - 1, Canopy.Highlight);
                if (PixelArt.Hash(x, y, 44) % 4 == 0) plot.Mark(sx - 1, y - 1, Canopy.Light);
            }
    }

    /// <summary>
    /// Ripe grain (N5) covering the whole tile in the Thatch ramp. Each row is
    /// shaded like a low rounded bank: a lit top line, two base lines, and a
    /// one-pixel gap in the shade step where the row shades the next. Ears
    /// stand along the top of each row as two-pixel ticks two or three pixels
    /// apart, lit at the tip, with a shade pixel to the south-east; about one
    /// in four stands taller and breaks the gap above. A slow swell (as wind
    /// leaves in a field) lifts some stretches by one ramp step and dims the
    /// ear tips in others; it never darkens the gaps, which would repeat as a
    /// visible lattice across a field of identical tiles. At 16 px the ears
    /// become single lit pixels.
    /// </summary>
    private static void Grain(Plot plot)
    {
        var size = plot.Size;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var swell = Swell(x, y, size, 0.8f);
                var color = (y % RowPeriod) switch
                {
                    3 => Thatch.Shade,
                    2 => Thatch.Base,
                    // Where the swell rises, the middle line catches light too: one ramp step, never more.
                    1 => swell > 0.45f && PixelArt.Hash(x, y, 52) % 3 != 0 ? Thatch.Light : Thatch.Base,
                    _ => Thatch.Light,
                };
                plot.Mark(x, y, color);
            }
        for (var band = 0; band < size / RowPeriod; band++)
        {
            var top = band * RowPeriod;
            for (var x = (int)(PixelArt.Hash(band, size, 53) % 3); x < size;)
            {
                var swell = Swell(x, top, size, 0.8f);
                var tip = swell > -0.35f ? Thatch.Highlight : Thatch.Light;
                if (!plot.Fine)
                {
                    if (PixelArt.Hash(x, band, 57) % 3 != 0) plot.Mark(x, top, tip);
                    x += 2;
                    continue;
                }
                var tall = PixelArt.Hash(x, band, 54) % 4 == 0;
                var y = top - (tall ? 1 : 0);
                plot.Mark(x, y, tip);
                plot.Mark(x, y + 1, swell > -0.35f ? Thatch.Light : Thatch.Base);
                plot.Mark(x + 1, y + 2, Thatch.Shade);
                // Ears stand two or three pixels apart.
                x += PixelArt.Hash(x, band, 56) % 3 == 0 ? 3 : 2;
            }
        }
    }

    /// <summary>
    /// Harvested grain (N5): cut stubble along the ridge of every row. At
    /// 32 px each stalk end is a Thatch-base pixel with its Thatch-shade
    /// shadow below, two pixels apart with about one in three missing, plus
    /// one short loose straw; at 16 px a pale broken line in the Thatch shade
    /// and base steps.
    /// </summary>
    private static void Stubble(Plot plot)
    {
        for (var y = 1; y < plot.Size; y += RowPeriod)
            for (var x = (int)(PixelArt.Hash(2, y, 61) % 2); x < plot.Size; x += 2)
            {
                if (!plot.Fine)
                {
                    // At 16 px the stubble is a pale broken line along the ridge.
                    if (PixelArt.Hash(x, y, 62) % 6 != 0) plot.Mark(x, y, Thatch.Shade);
                    if (PixelArt.Hash(x + 1, y, 62) % 6 != 0) plot.Mark(x + 1, y, PixelArt.Hash(x, y, 63) % 2 == 0 ? Thatch.Base : Thatch.Shade);
                    continue;
                }
                if (PixelArt.Hash(x, y, 62) % 3 == 0) continue;
                plot.Mark(x, y, Thatch.Base);
                plot.Mark(x, y + 1, Thatch.Shade);
            }
        if (!plot.Fine) return;
        // One short loose straw: more would repeat as a visible motif across a block of identical tiles.
        var stream = new PixelArt.Stream(PixelArt.Hash(plot.Size, 3, 64));
        var sx = stream.Range(0, plot.Size);
        var sy = stream.Range(0, plot.Size / RowPeriod) * RowPeriod + 2;
        for (var step = 0; step < 3; step++) plot.Mark(sx + step, sy + step / 2, Thatch.Light);
    }

    /// <summary>
    /// Mature potatoes (N5): four rows of low leafy mounds, eight pixels
    /// apart, alternate rows shifted half a plant. Each mound is three
    /// overlapping lumps, narrow enough that the outline leaves a dark notch
    /// between neighbours, with soil showing between rows. At 16 px a mound
    /// is a lit top row over a shaded row and an edge-step shadow.
    /// </summary>
    private static void Potatoes(Plot plot)
    {
        if (!plot.Fine)
        {
            for (var row = 0; row < plot.Size / RowPeriod; row++)
            {
                var top = row * RowPeriod + 1;
                for (var x = (int)(PixelArt.Hash(row, 0, 70) % 2) * 2; x < plot.Size; x += 4)
                {
                    plot.Leaf(x, top, Canopy.Light);
                    plot.Leaf(x + 1, top, Canopy.Base);
                    plot.Leaf(x + 2, top, Canopy.Base);
                    for (var dx = 0; dx < 4; dx++) plot.Leaf(x + dx, top + 1, Canopy.Shade);
                    plot.Mark(x + 1, top + 2, Canopy.Edge);
                    plot.Mark(x + 2, top + 2, Canopy.Edge);
                }
            }
            return;
        }
        for (var row = 0; row < 4; row++)
        {
            var cy = 4.5f + row * 8;
            // Alternate rows shift half a plant so the mounds do not line up in columns.
            var shift = row % 2 == 0 ? 0f : 4f;
            for (var index = 0; index < 4; index++)
            {
                var jitter = ((int)(PixelArt.Hash(index, row, 71) % 3) - 1) * 0.5f;
                Mound(plot, 4f + index * 8 + shift + jitter, cy, index + row * 4);
            }
        }
    }

    /// <summary>
    /// One potato plant: a low mound wider than tall, built from a taller
    /// middle lump and a lower lump each side, in canopy shade with the base
    /// shifted north-west, a light patch and a highlight; its shadow falls
    /// south-east on the soil and the plot outlines it in the canopy edge.
    /// </summary>
    private static void Mound(Plot plot, float cx, float cy, int phase)
    {
        plot.Blob(plot.Ground, cx + 1, cy + 1.5f, 3.6f, 2.4f, Shadow);
        foreach (var (dx, dy, rx, ry) in new[] { (-1.9f, 0.6f, 1.5f, 1.6f), (1.9f, 0.6f, 1.5f, 1.6f), (0f, -0.2f, 2.4f, 2.3f) })
            plot.Blob(plot.Plants, cx + dx, cy + dy, rx, ry, Canopy.Shade, 4, phase, 0.15f);
        plot.Blob(plot.Plants, cx - 0.5f, cy - 0.5f, 2.3f, 1.8f, Canopy.Base, 5, phase + 1, 0.15f);
        plot.Blob(plot.Plants, cx - 1.0f, cy - 1.0f, 1.3f, 1.0f, Canopy.Light, 4, phase + 2, 0.1f);
        plot.Leaf((int)MathF.Floor(cx - 2.8f), (int)MathF.Floor(cy), Canopy.Base);
        plot.Leaf((int)MathF.Floor(cx - 1.5f), (int)MathF.Floor(cy - 1.7f), Canopy.Highlight);
    }

    /// <summary>
    /// Mature greens (N5): round rosettes eight pixels apart in four rows,
    /// alternate rows shifted half a plant. At 16 px a rosette is a five-pixel
    /// cross, lit north-west, with a pale heart; the cream is only a tint here and
    /// there is no shadow pixel, because both made the small grid too busy.
    /// </summary>
    private static void Greens(Plot plot)
    {
        if (!plot.Fine)
        {
            for (var row = 0; row < plot.Size / RowPeriod; row++)
            {
                var cy = row * RowPeriod + 1;
                for (var index = 0; index < plot.Size / 4; index++)
                {
                    var cx = row % 2 * 2 + 1 + index * 4;
                    plot.Leaf(cx, cy - 1, Canopy.Light);
                    plot.Leaf(cx - 1, cy, Canopy.Light);
                    plot.Leaf(cx, cy, Canopy.Highlight);
                    plot.Leaf(cx, cy, Vein with { A = 0.3f });
                    plot.Leaf(cx + 1, cy, Canopy.Base);
                    plot.Leaf(cx, cy + 1, Canopy.Base);
                }
            }
            return;
        }
        for (var row = 0; row < 4; row++)
        {
            var cy = 4.5f + row * 8;
            var shift = row % 2 == 0 ? 0f : 4f;
            for (var index = 0; index < 4; index++)
                Rosette(plot, 4.5f + index * 8 + shift, cy, index * 3 + row);
        }
    }

    /// <summary>
    /// One greens rosette: a round, softly ruffled body in canopy base whose
    /// lit north-west part is canopy light with a highlight, a cream heart,
    /// and a faint cream midrib running through it from north-west to
    /// south-east; shadow to the south-east.
    /// </summary>
    private static void Rosette(Plot plot, float cx, float cy, int phase)
    {
        plot.Blob(plot.Ground, cx + 1, cy + 1.5f, 3.2f, 2.6f, Shadow);
        plot.Blob(plot.Plants, cx, cy, 3.1f, 3.1f, Canopy.Base, 6, phase, 0.1f);
        plot.Blob(plot.Plants, cx - 0.5f, cy - 0.5f, 2.4f, 2.4f, Canopy.Light, 6, phase + 1, 0.1f);
        plot.Blob(plot.Plants, cx - 1.3f, cy - 1.3f, 0.9f, 0.9f, Canopy.Highlight);
        var x = (int)MathF.Floor(cx);
        var y = (int)MathF.Floor(cy);
        plot.Leaf(x, y, Vein);
        plot.Leaf(x - 1, y - 1, Vein with { A = 0.45f });
        plot.Leaf(x + 1, y + 1, Vein with { A = 0.35f });
    }

    /// <summary>
    /// A field overlay under construction: a ground layer (furrows, clods,
    /// seed, shadows) and a plant layer that gets a one-pixel outline (L4).
    /// Every mark wraps around the tile edges, so the overlay repeats without
    /// a seam in both directions.
    /// </summary>
    private sealed class Plot(int size)
    {
        public readonly int Size = size;
        public readonly Image Ground = Bitmap.Empty(size, size);
        public readonly Image Plants = Bitmap.Empty(size, size);

        /// <summary>True at 32 px, where two-pixel details are worth drawing (S1).</summary>
        public bool Fine => Size >= 32;

        private int Wrap(int value) => ((value % Size) + Size) % Size;

        /// <summary>Blends one ground pixel, wrapping around the tile.</summary>
        public void Mark(int x, int y, Color color) => Blend(Ground, x, y, color);

        /// <summary>Blends one plant pixel, wrapping around the tile.</summary>
        public void Leaf(int x, int y, Color color) => Blend(Plants, x, y, color);

        private void Blend(Image layer, int x, int y, Color color)
        {
            x = Wrap(x);
            y = Wrap(y);
            layer.SetPixel(x, y, layer.GetPixel(x, y).Blend(color));
        }

        /// <summary>
        /// A filled ellipse in pixels whose edge may bulge in soft lobes (like
        /// <see cref="PixelCanvas.Lumpy"/>; depth 0.1 matches it), wrapping
        /// around the tile.
        /// </summary>
        public void Blob(Image layer, float cx, float cy, float rx, float ry, Color color, int lobes = 0, float phase = 0, float depth = 0)
        {
            for (var y = (int)MathF.Floor(cy - ry - 1); y <= (int)MathF.Ceiling(cy + ry + 1); y++)
                for (var x = (int)MathF.Floor(cx - rx - 1); x <= (int)MathF.Ceiling(cx + rx + 1); x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    var reach = lobes > 0 ? 1 - depth + depth * MathF.Sin(MathF.Atan2(dy, dx) * lobes + phase) : 1f;
                    if (dx * dx + dy * dy <= reach * reach) Blend(layer, x, y, color);
                }
        }

        /// <summary>The finished overlay: the plant layer outlined in <paramref name="edge"/> (four neighbours, wrapping) over the ground layer.</summary>
        public Image Compose(Color edge)
        {
            var outlined = Plants.Duplicate();
            bool Solid(int x, int y) => Plants.GetPixel(Wrap(x), Wrap(y)).A > 0.5f;
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (Plants.GetPixel(x, y).A <= 0 && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)))
                        outlined.SetPixel(x, y, edge);
            var result = Ground.Duplicate();
            Sheet.Blend(result, outlined, 0, 0);
            return result;
        }
    }

    // ───────────────────────────── Orchard ─────────────────────────────

    /// <summary>An orchard tree stage on a transparent tile, 32 or 16 px (any size works; coordinates are in 32-unit tile space).</summary>
    public static Image Orchard(NatureSprite stage, int size)
    {
        if (OrchardCache.TryGetValue((stage, size), out var cached)) return cached;
        var tree = new TreeSprite(size);
        if (stage == NatureSprite.OrchardGrowing)
            OrchardCrown(tree, 16, 16, 8f, 6);
        else
        {
            OrchardCrown(tree, 16, 15, 11.5f, 7);
            if (stage == NatureSprite.OrchardFruiting)
                foreach (var (x, y) in new[] { (21, 11), (11, 18), (18, 21), (15, 7), (24, 17) })
                    FruitOn(tree, x, y);
        }
        var image = tree.Compose(OrchardCanopy.Edge);
        OrchardCache[(stage, size)] = image;
        return image;
    }

    /// <summary>
    /// A broadleaf canopy in the orchard ramp (N2, N6), drawn the way the
    /// proposed broadleaf is: the L2 shadow offset (+1, +3), a shade body,
    /// the base pulled north-west so a shade rim stays on the south-east, a
    /// light lobe and a highlight disc offset (−3, −3), and a few leaf dimples
    /// at 32 px. Offsets scale with the radius so a young tree is lit alike.
    /// </summary>
    private static void OrchardCrown(TreeSprite tree, float cx, float cy, float radius, int lobes)
    {
        var k = radius / 11.5f;
        tree.Shadow(cx + 1, cy + 3, radius + 0.5f, radius * 0.86f);
        tree.Lobed(cx, cy, radius, OrchardCanopy.Shade, lobes, 1, 0.13f);
        tree.Lobed(cx - 0.8f * k, cy - 0.8f * k, radius - 0.9f * k, OrchardCanopy.Base, lobes, 1, 0.13f);
        tree.Lobed(cx - 2 * k, cy - 2 * k, 7.5f * k, OrchardCanopy.Light, 5, 2, 0.13f);
        tree.Canvas.Disc(cx - 3 * k, cy - 3 * k, 3 * k, OrchardCanopy.Highlight);
        if (!tree.Fine) return;
        // Leaf dimples: single darker pixels where leaf clusters meet, placed relative to the centre.
        foreach (var (dx, dy) in new[] { (5f, -5f), (8f, 1f), (-5f, 5f), (2f, 7f), (6f, 6f) })
            tree.Canvas.Dot(cx + dx * k, cy + dy * k, OrchardCanopy.Shade);
        foreach (var (dx, dy) in new[] { (1f, -6f), (-6f, 1f), (2f, 1f) })
            tree.Canvas.Dot(cx + dx * k, cy + dy * k, OrchardCanopy.Base);
    }

    /// <summary>
    /// One fruit (N3). At 32 px a five-pixel round disc in the fruit base,
    /// its north and west pixels in the light step with the highlight at the
    /// north, and a shade pixel at the south-east; at 16 px one base pixel.
    /// </summary>
    private static void FruitOn(TreeSprite tree, int x, int y)
    {
        if (!tree.Fine)
        {
            tree.Put(x / 2, y / 2, Fruit.Base);
            return;
        }
        tree.Put(x, y - 1, Fruit.Highlight);
        tree.Put(x - 1, y, Fruit.Light);
        tree.Put(x, y, Fruit.Base);
        tree.Put(x + 1, y, Fruit.Base);
        tree.Put(x, y + 1, Fruit.Base);
        tree.Put(x + 1, y + 1, Fruit.Shade);
    }

    /// <summary>
    /// A tree sprite under construction: a body layer that gets the edge
    /// outline and a ground layer holding only the soft shadow, so the
    /// outline never wraps the shadow. Coordinates are in 32-unit tile space.
    /// </summary>
    private sealed class TreeSprite
    {
        public readonly int Size;
        public readonly float Unit;
        public readonly Image Body;
        public readonly Image Ground;
        public readonly PixelCanvas Canvas;
        private readonly PixelCanvas groundCanvas;

        public TreeSprite(int size)
        {
            Size = size;
            Unit = size / 32f;
            Body = Bitmap.Empty(size, size);
            Ground = Bitmap.Empty(size, size);
            var cell = new Rect2I(0, 0, size, size);
            Canvas = new PixelCanvas(Body, cell, Unit);
            groundCanvas = new PixelCanvas(Ground, cell, Unit);
        }

        /// <summary>True at 32 px, where single-pixel details are worth drawing (S1).</summary>
        public bool Fine => Unit >= 1;

        /// <summary>The L2 ground shadow, an ellipse already offset toward the south-east by the caller.</summary>
        public void Shadow(float cx, float cy, float rx, float ry) => groundCanvas.Ellipse(cx, cy, rx, ry, CropsProposal.Shadow);

        /// <summary>Sets one body pixel in image pixels, clipped to the sprite.</summary>
        public void Put(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            Body.SetPixel(x, y, Body.GetPixel(x, y).Blend(color));
        }

        /// <summary>A canopy like <see cref="PixelCanvas.Lumpy"/> with an adjustable lobe depth (0.1 matches Lumpy).</summary>
        public void Lobed(float centerX, float centerY, float radius, Color color, int lobes, float phase, float depth)
        {
            var cx = centerX * Unit;
            var cy = centerY * Unit;
            var r = radius * Unit;
            for (var y = (int)(cy - r - 2); y <= (int)(cy + r + 2); y++)
                for (var x = (int)(cx - r - 2); x <= (int)(cx + r + 2); x++)
                {
                    var offset = new Vector2(x + 0.5f - cx, y + 0.5f - cy);
                    var edge = r * (1 - depth + depth * Mathf.Sin(offset.Angle() * lobes + phase));
                    if (offset.Length() <= edge) Put(x, y, color);
                }
        }

        /// <summary>The finished sprite: the body outlined in <paramref name="edge"/> (four neighbours, L4) over its shadow.</summary>
        public Image Compose(Color edge)
        {
            var outlined = Body.Duplicate();
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && Body.GetPixel(x, y).A > 0.5f;
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (Body.GetPixel(x, y).A <= 0 && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)))
                        outlined.SetPixel(x, y, edge);
            var result = Ground.Duplicate();
            Sheet.Blend(result, outlined, 0, 0);
            return result;
        }
    }
}
