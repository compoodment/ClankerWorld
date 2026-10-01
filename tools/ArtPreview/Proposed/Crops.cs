using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Crops;

/// <summary>
/// Proposed crop art: farm-field overlays for the Fertile soil tile (one per
/// crop and growth state, rounds 1 and 2) and the orchard tree stages,
/// including the planted sapling.
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

    /// <summary>
    /// Potato flesh and skin, from the Timber ramp as the potato item icon
    /// uses it: highlight D2AC77, light A77C52, base 8A6440. Planted and
    /// left-over potatoes are outlined in the soil edge step.
    /// </summary>
    private static readonly Ramp Tuber = Ramp.Of("4A3A2A", "6E4E31", "8A6440", "A77C52", "D2AC77");

    /// <summary>Every field: three crops by five growth states.</summary>
    private static readonly (Crop Crop, Growth Growth)[] AllFields =
        Enum.GetValues<Crop>().SelectMany(crop => Enum.GetValues<Growth>().Select(growth => (crop, growth))).ToArray();

    private static readonly NatureSprite[] OrchardStages =
        [NatureSprite.OrchardGrowing, NatureSprite.OrchardFruiting, NatureSprite.OrchardPicked];

    /// <summary>The orchard trees on the sheet, youngest first: the sapling is new and has no game sprite yet.</summary>
    private static readonly (string Id, Func<int, Image> Draw, string Note)[] OrchardTrees =
    [
        ("OrchardSapling", OrchardSapling, "A newly planted orchard tree: a small lumpy crown in the orchard ramp, standing in a ring of dug soil."),
        ("OrchardGrowing", size => Orchard(NatureSprite.OrchardGrowing, size), OrchardNote(NatureSprite.OrchardGrowing)),
        ("OrchardFruiting", size => Orchard(NatureSprite.OrchardFruiting, size), OrchardNote(NatureSprite.OrchardFruiting)),
        ("OrchardPicked", size => Orchard(NatureSprite.OrchardPicked, size), OrchardNote(NatureSprite.OrchardPicked)),
    ];

    private static readonly Dictionary<(NatureSprite, int), Image> OrchardCache = [];

    public IEnumerable<Entry> Render()
    {
        // Each size: the fifteen fields with the grain patch after them (two
        // sheet rows), then the potato and greens patches, the orchard trees
        // over grass, the bare orchard sprites and the bare field overlays.
        foreach (var size in new[] { 32, 16 })
        {
            var suffix = size == 32 ? "" : ".16";
            foreach (var (crop, growth) in AllFields)
                yield return new(Family, FieldId(crop, growth) + suffix, OnSoil(Field(crop, growth, size), size), FieldNote(crop, growth));
            yield return new(Family, "field.tiling" + suffix, Tiling(Crop.Grain, size),
                "A 3×3 patch of mature grain over the soil variants the map picks, to judge seams and repetition.");
        }
        foreach (var size in new[] { 32, 16 })
            foreach (var crop in new[] { Crop.Potato, Crop.Greens })
                yield return new(Family, $"field.{crop.ToString().ToLowerInvariant()}.tiling" + (size == 32 ? "" : ".16"), Tiling(crop, size),
                    $"A 3×3 patch of mature {(crop == Crop.Potato ? "potatoes" : "greens")} over the soil variants the map picks, to judge seams and repetition.");
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, draw, note) in OrchardTrees)
                yield return new(Family, id + (size == 32 ? "" : ".16"),
                    Bitmap.Over(TerrainTextures.Tile(TerrainStyle.Grass, 0, size), draw(size), 0, 0), note);
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, draw, _) in OrchardTrees)
                yield return new(Family, id + (size == 32 ? "" : ".16") + ".sprite", draw(size));
        foreach (var size in new[] { 32, 16 })
            foreach (var (crop, growth) in AllFields)
                yield return new(Family, FieldId(crop, growth) + (size == 32 ? "" : ".16") + ".sprite", Field(crop, growth, size));
    }

    /// <summary>The reference scene: the three orchard stages come from here, every other nature sprite from the game.</summary>
    public void Apply(ArtSet set)
    {
        set.Nature = (sprite, size) => Array.IndexOf(OrchardStages, sprite) >= 0 ? Orchard(sprite, size) : NatureSprites.Sprite(sprite, size);
    }

    private static string FieldId(Crop crop, Growth growth) =>
        $"field.{crop.ToString().ToLowerInvariant()}.{growth.ToString().ToLowerInvariant()}";

    private static string FieldNote(Crop crop, Growth growth) => (crop, growth) switch
    {
        (_, Growth.Prepared) => "Tilled rows: a lit furrow wall and a shaded trough four pixels apart, a few clods with their shadow. The same for every crop.",
        (Crop.Potato, Growth.Seeded) => "The tilled rows with seed potatoes set in at every planting spot, where the plants will come up.",
        (_, Growth.Seeded) => "The tilled rows with seed in Timber light dotted along every trough. Grain and greens share it.",
        (Crop.Potato, Growth.Sprout) => "Small leafy clumps at every planting spot, outlined and shadowed: young potato plants.",
        (Crop.Greens, Growth.Sprout) => "Small round rosettes at every planting spot, lit north-west with a pale heart.",
        (_, Growth.Sprout) => "Two-pixel shoots standing in every trough, lit tip to the north.",
        (Crop.Potato, Growth.Harvested) => "Dug soil: a turned hollow at every plant, clods, and a few left-over potatoes.",
        (Crop.Greens, Growth.Harvested) => "Cut stumps at every plant: a pale cut stem in a ring of trimmed leaf bases.",
        (_, Growth.Harvested) => "Pale cut stubble along the rows, one loose straw and a few clods on the furrows.",
        (Crop.Grain, _) => "Ripe grain covering the soil: rows of lit ears, each row shading a thin line on the next, with slow lighter swells.",
        (Crop.Potato, _) => "Rows of separate low leafy mounds in the canopy ramp, lit north-west, outlined, soil showing between the rows.",
        _ => "Staggered round rosettes in canopy light with a cream heart and faint midrib, each with its own outline and shadow.",
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

    /// <summary>A 3×3 patch of one mature crop over the soil variants the map would pick there.</summary>
    private static Image Tiling(Crop crop, int size)
    {
        var patch = Bitmap.Empty(size * 3, size * 3);
        var overlay = Field(crop, Growth.Mature, size);
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
                // Potatoes are their own planting stock, so a seeded potato field shows the planted potatoes.
                if (crop == Crop.Potato) SeedPotatoes(plot);
                else Seeds(plot);
                Clods(plot, plot.Fine ? 3 : 1, 2);
                break;
            case Growth.Sprout when crop == Crop.Potato:
                PotatoSprouts(plot);
                break;
            case Growth.Sprout when crop == Crop.Greens:
                GreensSprouts(plot);
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
            case Growth.Harvested when crop == Crop.Potato:
                DugPotatoes(plot);
                break;
            case Growth.Harvested when crop == Crop.Greens:
                CutGreens(plot);
                Clods(plot, plot.Fine ? 4 : 2, 3);
                break;
            case Growth.Harvested:
                Stubble(plot);
                Clods(plot, plot.Fine ? 4 : 2, 3);
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
    /// The planting spots of a potato or greens field, where the mature
    /// plants stand, so every state lines up with the one before it. At 32 px
    /// four staggered rows of four, eight pixels apart, in tile pixels and
    /// with the same small jitter the mature potatoes use; at 16 px four
    /// staggered rows of four, four pixels apart, as the 16 px mature plants.
    /// Index numbers each spot for per-plant variation.
    /// </summary>
    private static IEnumerable<(float X, float Y, int Index)> Stations(Crop crop, bool fine)
    {
        for (var row = 0; row < 4; row++)
            for (var index = 0; index < 4; index++)
            {
                if (fine)
                {
                    var shift = row % 2 == 0 ? 0f : 4f;
                    var x = crop == Crop.Potato
                        ? 4f + index * 8 + shift + ((int)(PixelArt.Hash(index, row, 71) % 3) - 1) * 0.5f
                        : 4.5f + index * 8 + shift;
                    yield return (x, 4.5f + row * 8, index + row * 4);
                }
                else if (crop == Crop.Potato)
                    // The middle of a 16 px mound, which starts at an even column shifted per row.
                    yield return ((int)(PixelArt.Hash(row, 0, 70) % 2) * 2 + index * 4 + 1, row * RowPeriod + 1, index + row * 4);
                else
                    yield return (row % 2 * 2 + 1 + index * 4, row * RowPeriod + 1, index + row * 4);
            }
    }

    /// <summary>
    /// Seeded potatoes: potatoes are their own planting stock, so at every
    /// planting spot a seed potato sits half set into the furrow, with turned
    /// soil heaped on its south-east. At 32 px a 2×2 piece (lit north-west
    /// pixel, soil-edge pixel to the south-east); at 16 px one bright pixel,
    /// sparser and lighter than the grain's seed dots.
    /// </summary>
    private static void SeedPotatoes(Plot plot)
    {
        foreach (var (cx, cy, _) in Stations(Crop.Potato, plot.Fine))
        {
            var x = (int)MathF.Floor(cx);
            var y = (int)MathF.Floor(cy);
            if (!plot.Fine)
            {
                plot.Mark(x, y, Tuber.Highlight);
                continue;
            }
            plot.Mark(x - 1, y - 1, Tuber.Highlight);
            plot.Mark(x, y - 1, Tuber.Light);
            plot.Mark(x - 1, y, Tuber.Light);
            plot.Mark(x, y, Tuber.Base);
            plot.Mark(x + 1, y, Tuber.Edge);
            plot.Mark(x, y + 1, Tuber.Edge);
            plot.Mark(x + 1, y + 1, Soil.Light);
            plot.Mark(x + 2, y + 1, Soil.Highlight);
        }
    }

    /// <summary>
    /// Sprouting potatoes: a small leafy clump at every planting spot, about
    /// two thirds the width of a grown mound: three leaflets (west, east and
    /// north) in canopy shade, so the outline notches between them, with the
    /// base and a light leaf toward the north-west, shadowed like the mature
    /// plants. Darker and more ragged than a greens rosette. At 16 px a
    /// two-pixel clump.
    /// </summary>
    private static void PotatoSprouts(Plot plot)
    {
        foreach (var (cx, cy, index) in Stations(Crop.Potato, plot.Fine))
        {
            if (!plot.Fine)
            {
                plot.Leaf((int)cx, (int)cy, Canopy.Base);
                plot.Leaf((int)cx + 1, (int)cy, Canopy.Shade);
                continue;
            }
            plot.Blob(plot.Ground, cx + 1, cy + 1.4f, 3f, 1.8f, Shadow);
            foreach (var (dx, dy) in new[] { (-1.4f, 0.4f), (1.4f, 0.4f), (0f, -0.9f) })
                plot.Blob(plot.Plants, cx + dx, cy + dy, 1.15f, 1.05f, Canopy.Shade, 3, index, 0.2f);
            plot.Blob(plot.Plants, cx - 1.4f, cy + 0.1f, 0.75f, 0.65f, Canopy.Base);
            plot.Blob(plot.Plants, cx - 0.2f, cy - 1.2f, 0.75f, 0.65f, Canopy.Base);
            plot.Leaf((int)MathF.Floor(cx - 1.0f), (int)MathF.Floor(cy - 1.6f), Canopy.Light);
        }
    }

    /// <summary>
    /// Sprouting greens: a small round rosette at every planting spot, in
    /// canopy light with a highlight toward the north-west and a faint cream
    /// heart, shadowed and outlined like the mature rosettes. At 16 px one
    /// light pixel, which the outline rings.
    /// </summary>
    private static void GreensSprouts(Plot plot)
    {
        foreach (var (cx, cy, index) in Stations(Crop.Greens, plot.Fine))
        {
            if (!plot.Fine)
            {
                plot.Leaf((int)cx, (int)cy, Canopy.Light);
                continue;
            }
            plot.Blob(plot.Ground, cx + 0.8f, cy + 1.2f, 1.9f, 1.5f, Shadow);
            plot.Blob(plot.Plants, cx, cy, 1.7f, 1.7f, Canopy.Base, 5, index, 0.2f);
            plot.Blob(plot.Plants, cx - 0.4f, cy - 0.4f, 1.1f, 1.1f, Canopy.Light);
            var x = (int)MathF.Floor(cx);
            var y = (int)MathF.Floor(cy);
            plot.Leaf(x - 1, y - 1, Canopy.Highlight);
            plot.Leaf(x, y, Vein with { A = 0.4f });
        }
    }

    /// <summary>
    /// Harvested potatoes: the rows have been dug. Each plant leaves a turned
    /// hollow (shaded north-west wall, lit south-east lip of thrown-up soil),
    /// clods lie between them, and a few small potatoes the diggers missed
    /// lie on top, each outlined in the soil edge with a lit north-west pixel.
    /// At 16 px a shade pixel per plant and two pale potato pixels.
    /// </summary>
    private static void DugPotatoes(Plot plot)
    {
        foreach (var (cx, cy, index) in Stations(Crop.Potato, plot.Fine))
        {
            if (!plot.Fine)
            {
                plot.Mark((int)cx, (int)cy, Soil.Edge);
                plot.Mark((int)cx + 1, (int)cy, Soil.Shade);
                plot.Mark((int)cx + 1, (int)cy + 1, Soil.Highlight);
                continue;
            }
            // Each hollow is a little different in size and outline, so a dug field does not look stamped.
            var grow = (PixelArt.Hash(index, 7, 75) % 3) * 0.3f;
            plot.Blob(plot.Ground, cx + 0.7f, cy + 0.8f, 3.1f + grow, 2.1f, Soil.Light, 5, index, 0.22f);
            plot.Blob(plot.Ground, cx, cy, 2.9f + grow, 1.9f, Soil.Edge with { A = 0.85f }, 5, index, 0.22f);
            plot.Blob(plot.Ground, cx + 0.5f, cy + 0.6f, 2.1f + grow, 1.2f, Soil.Shade, 4, index + 2, 0.2f);
            // Soil thrown up beside the hollow, on alternate sides.
            var side = index % 2 == 0 ? 1 : -1;
            plot.Mark((int)MathF.Floor(cx + side * (4 + grow)), (int)MathF.Floor(cy), Soil.Highlight);
            plot.Mark((int)MathF.Floor(cx + side * (4 + grow)) + 1, (int)MathF.Floor(cy) + 1, Soil.Shade);
        }
        Clods(plot, plot.Fine ? 6 : 2, 4);
        // A few left-over potatoes at fixed, irregular spots; more would repeat as a pattern across a dug field.
        var leftovers = plot.Fine ? new[] { (9, 9), (22, 18), (5, 26) } : new[] { (5, 5), (11, 11) };
        foreach (var (x, y) in leftovers)
        {
            if (!plot.Fine)
            {
                plot.Mark(x, y, Tuber.Highlight);
                plot.Mark(x + 1, y, Tuber.Light);
                continue;
            }
            // A small potato, three by two, with its outline in the soil edge.
            foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (2, -1), (-1, 0), (3, 0), (-1, 1), (3, 1), (0, 2), (1, 2), (2, 2) })
                plot.Mark(x + dx, y + dy, Tuber.Edge);
            plot.Mark(x, y, Tuber.Highlight);
            plot.Mark(x + 1, y, Tuber.Light);
            plot.Mark(x + 2, y, Tuber.Light);
            plot.Mark(x, y + 1, Tuber.Light);
            plot.Mark(x + 1, y + 1, Tuber.Light);
            plot.Mark(x + 2, y + 1, Tuber.Base);
        }
    }

    /// <summary>
    /// Harvested greens: every head has been cut, leaving a stump at each
    /// planting spot: a pale cream cut stem in a small ring of trimmed outer
    /// leaf bases (canopy shade and base, the north-west one lit), shadowed
    /// and outlined. At 16 px a single cream pixel, which the outline rings.
    /// </summary>
    private static void CutGreens(Plot plot)
    {
        foreach (var (cx, cy, index) in Stations(Crop.Greens, plot.Fine))
        {
            var x = (int)MathF.Floor(cx);
            var y = (int)MathF.Floor(cy);
            if (!plot.Fine)
            {
                plot.Leaf(x, y, Vein);
                continue;
            }
            plot.Blob(plot.Ground, cx + 0.8f, cy + 1.2f, 2f, 1.5f, Shadow);
            plot.Blob(plot.Plants, cx, cy, 1.8f, 1.6f, Canopy.Shade, 4, index, 0.3f);
            plot.Leaf(x - 1, y - 1, Canopy.Light);
            plot.Leaf(x, y - 1, Canopy.Base);
            plot.Leaf(x, y, Vein);
            plot.Leaf(x - 1, y, Canopy.Base);
        }
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
    /// The orchard sapling (N6): a newly planted tree, a small lumpy crown in
    /// the orchard ramp (shade body, base and light pulled north-west, a
    /// highlight), standing in a ring of dug soil that shows it was planted.
    /// Smaller than the growing stage.
    /// </summary>
    public static Image OrchardSapling(int size)
    {
        var tree = new TreeSprite(size);
        // The planting ring: a soil rim round a soil floor, a few crumbs on its lit north-west edge.
        tree.GroundEllipse(16.4f, 17.6f, 8.4f, 6.4f, Soil.Shade with { A = 0.85f });
        tree.GroundEllipse(16.2f, 17.4f, 7.2f, 5.3f, Soil.Base with { A = 0.95f });
        if (tree.Fine)
            foreach (var (x, y) in new[] { (10, 15), (13, 12), (11, 20) }) tree.GroundDot(x, y, Soil.Light);
        tree.Shadow(17, 18.5f, 5.8f, 4.4f);
        tree.Lobed(16, 16, 5.4f, OrchardCanopy.Shade, 5, 1, 0.16f);
        tree.Lobed(15.5f, 15.5f, 4.6f, OrchardCanopy.Base, 5, 1, 0.16f);
        tree.Lobed(14.8f, 14.8f, 3f, OrchardCanopy.Light, 4, 2, 0.16f);
        tree.Canvas.Disc(14, 14, 1.3f, OrchardCanopy.Highlight);
        return tree.Compose(OrchardCanopy.Edge);
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

        /// <summary>An ellipse on the ground layer (tile units), for marks the outline must not wrap, such as a planting ring.</summary>
        public void GroundEllipse(float cx, float cy, float rx, float ry, Color color) => groundCanvas.Ellipse(cx, cy, rx, ry, color);

        /// <summary>One ground-layer pixel (tile units); used only at 32 px, where a tile unit is one pixel.</summary>
        public void GroundDot(float x, float y, Color color) => groundCanvas.Dot(x, y, color);

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
