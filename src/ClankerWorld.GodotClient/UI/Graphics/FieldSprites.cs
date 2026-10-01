using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>The crops a farm field's art can show.</summary>
public enum FieldCrop : byte
{
    Grain,
    Potato,
    Greens,
}

/// <summary>The growth states a farm field's art shows, from tilled rows to stubble.</summary>
public enum FieldGrowth : byte
{
    Prepared,
    Seeded,
    Sprout,
    Mature,
    Harvested,
}

/// <summary>
/// Farm-field overlays approved in the second art review: one transparent
/// picture per crop and growth state, drawn over the tilled Fertile soil
/// ground tile. Rows run east–west on a four-pixel period at both 32 px and
/// the separately drawn 16 px size, and every mark wraps round the tile
/// edges, so a field several tiles across joins without a seam. Plants are
/// lit from the north-west, cast a soft shadow to the south-east and carry a
/// one-pixel outline in the canopy edge step. Every blended pixel is snapped
/// to an 8-bit step (<see cref="PixelArt.Snap"/>) so the game matches the
/// reviewed pictures. Textures are generated once per crop, state and size;
/// the map layer draws them with nearest filtering.
/// </summary>
public static class FieldSprites
{
    /// <summary>Rows repeat every four pixels at both sizes, which divides both tile sizes, so rows continue into the next tile.</summary>
    private const int RowPeriod = 4;

    /// <summary>Within a row period: row 0 is the lit south wall of the furrow (it faces the north-west light), row 3 the shaded bottom of the next furrow.</summary>
    private const int CrestRow = 0;
    private const int TroughRow = 3;

    /// <summary>The ground shadow colour every nature sprite uses.</summary>
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    private static readonly Dictionary<(FieldCrop, FieldGrowth, int), Image> Images = [];
    private static readonly Dictionary<(FieldCrop, FieldGrowth, int), ImageTexture> Textures = [];
    private static readonly Dictionary<(FieldCrop, FieldGrowth), Color> OverviewColors = [];

    /// <summary>One colour ramp from the style guide, darkest to brightest.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));
    }

    private static readonly Ramp Soil = Ramp.Of("4A3A2A", "5C4B35", "735F45", "86704F", "9A8460");
    private static readonly Ramp Thatch = Ramp.Of("6B5528", "A98A45", "D2AE5E", "E6C77B", "F0DA9A");
    private static readonly Ramp Canopy = Ramp.Of("2E4A2A", "476B36", "557D3E", "6C9A4B", "8DB660");

    /// <summary>Sown seed is Timber light; the heart and veins of a greens rosette are Cloth light.</summary>
    private static readonly Color Seed = new("A77C52");
    private static readonly Color Vein = new("E8DCC0");

    /// <summary>
    /// Potato flesh and skin, from the Timber ramp as the potato item icon
    /// uses it. Planted and left-over potatoes are outlined in the soil edge step.
    /// </summary>
    private static readonly Ramp Tuber = Ramp.Of("4A3A2A", "6E4E31", "8A6440", "A77C52", "D2AC77");

    /// <summary>
    /// The art for a field's crop as the simulation names it: grain,
    /// potatoes or cultivated greens. An unknown or missing crop shows grain.
    /// </summary>
    public static FieldCrop CropFor(string? crop) => crop switch
    {
        "potatoes" => FieldCrop.Potato,
        "cultivated_greens" => FieldCrop.Greens,
        _ => FieldCrop.Grain,
    };

    /// <summary>
    /// The art for a field's stage as the host sends it: a field being
    /// prepared or prepared shows tilled rows, planted shows seed, growing
    /// shows sprouts, ready shows the ripe crop and harvested shows what is
    /// left after harvest. An unknown stage shows tilled rows.
    /// </summary>
    public static FieldGrowth GrowthFor(string? stage) => stage switch
    {
        "planted" => FieldGrowth.Seeded,
        "growing" => FieldGrowth.Sprout,
        "ready" => FieldGrowth.Mature,
        "harvested" => FieldGrowth.Harvested,
        _ => FieldGrowth.Prepared,
    };

    /// <summary>The shared overlay texture for one crop and state at the atlas size (32, or 16 for mid zoom).</summary>
    public static ImageTexture Texture(FieldCrop crop, FieldGrowth growth, int size)
    {
        var key = (crop, growth, size);
        if (Textures.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(Generated(crop, growth, size));
        Textures[key] = texture;
        return texture;
    }

    /// <summary>
    /// The flat colour a field shows at overview zoom, below the size where the
    /// map draws textures: the average of its 16 px overlay over the tilled
    /// soil tile, so the colour agrees with the art at closer zoom.
    /// </summary>
    public static Color OverviewColor(FieldCrop crop, FieldGrowth growth)
    {
        if (OverviewColors.TryGetValue((crop, growth), out var cached)) return cached;
        const int Size = 16;
        var soil = TerrainTextures.Tile(TerrainStyle.FertileSoil, 0, Size);
        var overlay = Generated(crop, growth, Size);
        var (red, green, blue) = (0f, 0f, 0f);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var pixel = soil.GetPixel(x, y).Blend(overlay.GetPixel(x, y));
                red += pixel.R;
                green += pixel.G;
                blue += pixel.B;
            }
        var color = new Color(red / (Size * Size), green / (Size * Size), blue / (Size * Size));
        OverviewColors[(crop, growth)] = color;
        return color;
    }

    /// <summary>One overlay, for inspection and tests.</summary>
    public static Image Overlay(FieldCrop crop, FieldGrowth growth, int size) =>
        Generated(crop, growth, size).GetRegion(new Rect2I(0, 0, size, size));

    private static Image Generated(FieldCrop crop, FieldGrowth growth, int size)
    {
        var key = (crop, growth, size);
        if (Images.TryGetValue(key, out var cached)) return cached;
        var image = Field(crop, growth, size);
        Images[key] = image;
        return image;
    }

    /// <summary>A transparent field overlay for one crop and growth state, 32 or 16 px, seamless on every side.</summary>
    private static Image Field(FieldCrop crop, FieldGrowth growth, int size)
    {
        var plot = new Plot(size);
        // Ripe grain hides the soil, so its rows are drawn instead of furrows;
        // at 16 px mature potatoes and greens leave too little soil for them.
        var covered = growth == FieldGrowth.Mature && (crop == FieldCrop.Grain || !plot.Fine);
        if (!covered) Furrows(plot);
        switch (growth)
        {
            case FieldGrowth.Prepared:
                Clods(plot, plot.Fine ? 5 : 2, 1);
                break;
            case FieldGrowth.Seeded:
                // Potatoes are their own planting stock, so a seeded potato field shows the planted potatoes.
                if (crop == FieldCrop.Potato) SeedPotatoes(plot);
                else Seeds(plot);
                Clods(plot, plot.Fine ? 3 : 1, 2);
                break;
            case FieldGrowth.Sprout when crop == FieldCrop.Potato:
                PotatoSprouts(plot);
                break;
            case FieldGrowth.Sprout when crop == FieldCrop.Greens:
                GreensSprouts(plot);
                break;
            case FieldGrowth.Sprout:
                Shoots(plot);
                break;
            case FieldGrowth.Mature when crop == FieldCrop.Grain:
                Grain(plot);
                break;
            case FieldGrowth.Mature when crop == FieldCrop.Potato:
                Potatoes(plot);
                break;
            case FieldGrowth.Mature:
                Greens(plot);
                break;
            case FieldGrowth.Harvested when crop == FieldCrop.Potato:
                DugPotatoes(plot);
                break;
            case FieldGrowth.Harvested when crop == FieldCrop.Greens:
                CutGreens(plot);
                Clods(plot, plot.Fine ? 4 : 2, 3);
                break;
            case FieldGrowth.Harvested:
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
    /// Tilled rows: a lit line in the soil light step where the furrow wall
    /// faces the north-west light, and a shaded trough line in the shade step
    /// just north of it, two steps apart and four pixels apart, running the
    /// full width. Short breaks keep them from reading as ruled lines.
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
    /// Clods on the ridge between furrows: at 32 px a lit west pixel in the
    /// soil highlight, a light east pixel and a shade pixel under it to the
    /// south-east; at 16 px one highlight pixel.
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

    /// <summary>Seeded: Timber-light seed dotted along every trough, three pixels apart at 32 px and two at 16 px, with one here and there missing.</summary>
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
    /// Sprouting grain: shoots standing in every trough. At 32 px each is two
    /// pixels, a canopy-base stem with a canopy-highlight tip north of it, and
    /// about one in four has a second leaf to the west; at 16 px each is one
    /// canopy-light pixel.
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
    /// Ripe grain covering the whole tile in the Thatch ramp. Each row is
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
    /// Harvested grain: cut stubble along the ridge of every row. At 32 px
    /// each stalk end is a Thatch-base pixel with its Thatch-shade shadow
    /// below, two pixels apart with about one in three missing, plus one short
    /// loose straw; at 16 px a pale broken line in the Thatch shade and base steps.
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
    /// Mature potatoes: four rows of low leafy mounds, eight pixels apart,
    /// alternate rows shifted half a plant. Each mound is three overlapping
    /// lumps, narrow enough that the outline leaves a dark notch between
    /// neighbours, with soil showing between rows. At 16 px a mound is a lit
    /// top row over a shaded row and an edge-step shadow.
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
    /// Mature greens: round rosettes eight pixels apart in four rows,
    /// alternate rows shifted half a plant. At 16 px a rosette is a five-pixel
    /// cross, lit north-west, with a pale heart; the cream is only a tint there
    /// and there is no shadow pixel, because both made the small grid too busy.
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
    /// four staggered rows of four, eight pixels apart, with the same small
    /// jitter the mature potatoes use; at 16 px four staggered rows of four,
    /// four pixels apart, as the 16 px mature plants. Index numbers each spot
    /// for per-plant variation.
    /// </summary>
    private static IEnumerable<(float X, float Y, int Index)> Stations(FieldCrop crop, bool fine)
    {
        for (var row = 0; row < 4; row++)
            for (var index = 0; index < 4; index++)
            {
                if (fine)
                {
                    var shift = row % 2 == 0 ? 0f : 4f;
                    var x = crop == FieldCrop.Potato
                        ? 4f + index * 8 + shift + ((int)(PixelArt.Hash(index, row, 71) % 3) - 1) * 0.5f
                        : 4.5f + index * 8 + shift;
                    yield return (x, 4.5f + row * 8, index + row * 4);
                }
                else if (crop == FieldCrop.Potato)
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
        foreach (var (cx, cy, _) in Stations(FieldCrop.Potato, plot.Fine))
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
        foreach (var (cx, cy, index) in Stations(FieldCrop.Potato, plot.Fine))
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
        foreach (var (cx, cy, index) in Stations(FieldCrop.Greens, plot.Fine))
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
        foreach (var (cx, cy, index) in Stations(FieldCrop.Potato, plot.Fine))
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
        foreach (var (cx, cy, index) in Stations(FieldCrop.Greens, plot.Fine))
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
    /// seed, shadows) and a plant layer that gets a one-pixel outline. Every
    /// mark wraps round the tile edges, so the overlay repeats without a seam
    /// in both directions.
    /// </summary>
    private sealed class Plot
    {
        public Plot(int size)
        {
            Size = size;
            Ground = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            Ground.Fill(Colors.Transparent);
            Plants = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            Plants.Fill(Colors.Transparent);
        }

        public int Size { get; }

        public Image Ground { get; }

        public Image Plants { get; }

        /// <summary>True at 32 px, where two-pixel details are worth drawing.</summary>
        public bool Fine => Size >= 32;

        private int Wrap(int value) => ((value % Size) + Size) % Size;

        /// <summary>Blends one ground pixel, wrapping round the tile.</summary>
        public void Mark(int x, int y, Color color) => Blend(Ground, x, y, color);

        /// <summary>Blends one plant pixel, wrapping round the tile.</summary>
        public void Leaf(int x, int y, Color color) => Blend(Plants, x, y, color);

        private void Blend(Image layer, int x, int y, Color color)
        {
            x = Wrap(x);
            y = Wrap(y);
            layer.SetPixel(x, y, PixelArt.Snap(layer.GetPixel(x, y).Blend(color)));
        }

        /// <summary>
        /// A filled ellipse in pixels whose edge may bulge in soft lobes (like
        /// <see cref="PixelCanvas.Lumpy"/>; depth 0.1 matches it), wrapping
        /// round the tile.
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
            var outlined = Plants.GetRegion(new Rect2I(0, 0, Size, Size));
            bool Solid(int x, int y) => Plants.GetPixel(Wrap(x), Wrap(y)).A > 0.5f;
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (Plants.GetPixel(x, y).A <= 0 && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)))
                        outlined.SetPixel(x, y, edge);
            var result = Ground.GetRegion(new Rect2I(0, 0, Size, Size));
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var over = outlined.GetPixel(x, y);
                    if (over.A <= 0) continue;
                    result.SetPixel(x, y, PixelArt.Snap(result.GetPixel(x, y).Blend(over)));
                }
            return result;
        }
    }
}
