using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Menu;

/// <summary>
/// Round-1 proposal for the Main Menu valley (STYLE.md section 13). It renders
/// <see cref="MenuSceneProposed"/>, a copy of the game's <c>MenuScene</c> with
/// the same composition, Towns, river course and animation lists, improved in
/// place: three ridge bands with atmospheric depth, a varied tree line, fields
/// with rows and hedges, a banked river with a reflection band, the map's Road
/// and plank bridge, the map's roof materials, foreground tufts and flowers,
/// and at dusk warm F2C14E windows and a faint mist band.
/// </summary>
public sealed class MenuProposal : IArtProposal, IArtSetProvider
{
    public string Family => "menu";
    public string Name => "menu";

    /// <summary>Composes each scene the way the baseline does: Sky, then Land, then each Cloud at its start position.</summary>
    public IEnumerable<Entry> Render()
    {
        foreach (var night in new[] { false, true })
        {
            var scene = MenuSceneProposed.Create(night);
            var composed = scene.Sky.Duplicate();
            Sheet.Blend(composed, scene.Land, 0, 0);
            foreach (var cloud in scene.Clouds) Sheet.Blend(composed, cloud.Image, (int)cloud.X, (int)cloud.Y);
            yield return new Entry(Family, night ? "valley.dusk" : "valley.day", composed,
                night
                    ? "Dusk: same valley and lights; warm F2C14E windows, faint mist band over the far fields."
                    : "Day: three ridge bands, varied tree line, rowed fields with hedges, banked river, map roofs and bridge.");
        }
    }

    /// <summary>The menu backdrop is not part of the map scene, so nothing is replaced.</summary>
    public void Apply(ArtSet set)
    {
    }
}

/// <summary>
/// The Main Menu backdrop: a side-view valley with snowy mountains,
/// patchwork fields, a river and two small Towns, drawn from fixed seeds at
/// 320 × 180 logical pixels. The still layers are built once for daytime
/// (Light theme) or dusk (Dark theme); the game's <c>MenuBackdrop</c> moves the
/// clouds, smoke, birds, water, stars, lights and fireflies on top.
/// Proposed replacement for <c>ClankerWorld.GodotClient.UI.MenuScene</c>: the
/// public surface (sizes, records, lists and colours) is unchanged.
/// </summary>
public sealed class MenuSceneProposed
{
    public const int Width = 320;
    public const int Height = 180;

    public readonly record struct Cloud(Image Image, float X, float Y, float Speed);
    public readonly record struct Chimney(Vector2 Position, bool Forge);
    public readonly record struct Sparkle(Vector2I Position, Color Color, int Phase);
    public readonly record struct Star(Vector2I Position, Color Color, bool Large, int Phase);
    public readonly record struct Glow(Image Image, Vector2I Position, float Strength, bool Forge);

    /// <summary>
    /// Painting layers from back to front. The layer decides how much of the
    /// sky colour a pixel takes on (atmospheric depth, rule M2) and lets later
    /// painters ask what is already under a pixel.
    /// </summary>
    private enum Layer : byte
    {
        Sky,
        FarRidge,
        Mountain,
        NearRidge,
        TreeLine,
        Fields,
        Meadow,
        Town,
        Foreground,
    }

    private const int Horizon = 104;

    // A ramp is edge, shade, base, light, highlight (STYLE.md section 2).
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public Ramp(string edge, string shade, string baseColor, string light, string highlight)
            : this(new Color(edge), new Color(shade), new Color(baseColor), new Color(light), new Color(highlight))
        {
        }
    }

    private static readonly Ramp GrassRamp = new("3B5E3A", "527F4F", "5F8F5B", "6FA069", "86B37A");
    private static readonly Ramp ForestGrassRamp = new("2E4D35", "395F41", "426D4B", "4E7D56", "5E8E64");
    private static readonly Ramp ScrubRamp = new("66593A", "86784C", "988857", "A8975F", "BBAA70");
    private static readonly Ramp SandRamp = new("7E6E4A", "A08F66", "BAA77B", "C8B78C", "E3D6B5");
    private static readonly Ramp RockRamp = new("4A4542", "625B56", "756D68", "8B837D", "A49C95");
    private static readonly Ramp PeakRamp = new("5F5955", "8E8A85", "AEB2B0", "C4C7C5", "EEF3F1");
    private static readonly Ramp SnowRamp = new("9AAAA8", "B8C6C4", "CCD7D1", "DCE5E0", "F4F8F6");
    private static readonly Ramp SoilRamp = new("4A3A2A", "5C4B35", "735F45", "86704F", "9A8460");
    private static readonly Ramp RiverRamp = new("2F5A75", "3B7294", "4786AB", "5695B8", "7FB4CF");
    private static readonly Ramp DirtRamp = new("6E5538", "977852", "B99A6B", "C9AC7C", "D9C08F");
    private static readonly Ramp TimberRamp = new("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
    private static readonly Ramp ThatchRamp = new("6B5528", "A98A45", "D2AE5E", "E6C77B", "F0DA9A");
    private static readonly Ramp ClayTileRamp = new("5E2E22", "9E4E34", "C66A45", "E08E64", "EFA982");
    private static readonly Ramp SlateRamp = new("2B2E33", "4A4E55", "62666E", "80858E", "9A9FA7");
    private static readonly Ramp GreyTimberRamp = new("343C43", "59656F", "758390", "97A5B0", "AEBBC4");
    private static readonly Ramp DoorstepRamp = new("5F5848", "8C7F66", "B9AB8E", "C9BDA2", "DED3BC");
    private static readonly Ramp IronRamp = new("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
    private static readonly Ramp BroadleafRamp = new("2E4A2A", "476B36", "557D3E", "6C9A4B", "8DB660");
    private static readonly Ramp ConiferRamp = new("1F3E31", "2F5B45", "3F7358", "5E9278", "86B89A");

    /// <summary>The warm window light at dusk (rule M4).</summary>
    private static readonly Color WindowLight = new("F2C14E");
    private static readonly Color WindowLightTop = new("F8DA8A");
    private static readonly Color Plaster = new("E6D6B0");
    private static readonly Color PlasterShade = new("C7B38B");
    private static readonly Color Iron = new("3A3028");

    /// <summary>Roof materials of the map's buildings (rule B3).</summary>
    private enum Roofing
    {
        ClayTile,
        Thatch,
        Slate,
    }

    private static readonly Color[] Shirts =
        [new("3F6FA8"), new("B0523E"), new("4E8A5A"), new("C19A3A"), new("7A5A9E"), new("3E8C8C")];
    private static readonly Color[] Skins =
        [new("F0C8A0"), new("C99A6E"), new("8D5E3C"), new("E2B48A"), new("5E3B24"), new("B8845A")];
    private static readonly Color[] Hair =
        [new("3A2A1C"), new("6B4226"), new("1E1A18"), new("A8742E"), new("2E2420"), new("7A3A22")];

    private static readonly float[,] Bayer =
    {
        { 0 / 16f, 8 / 16f, 2 / 16f, 10 / 16f }, { 12 / 16f, 4 / 16f, 14 / 16f, 6 / 16f },
        { 3 / 16f, 11 / 16f, 1 / 16f, 9 / 16f }, { 15 / 16f, 7 / 16f, 13 / 16f, 5 / 16f },
    };

    private readonly Color[] color = new Color[Width * Height];
    private readonly Layer[] layer = new Layer[Width * Height];
    private readonly bool[] emissive = new bool[Width * Height];
    private readonly bool[] water = new bool[Width * Height];
    /// <summary>River pixels still showing water: not covered by the bridge, its shadow or anyone crossing.</summary>
    private readonly bool[] openWater = new bool[Width * Height];
    /// <summary>How much of the low sky a water pixel mirrors at dusk (the reflection band).</summary>
    private readonly float[] skyMirror = new float[Width * Height];
    private readonly float[] roadY = new float[Width];
    private readonly List<(Vector2 Center, float Radius, Color Color, float Strength, bool Forge)> lights = [];
    private readonly List<Chimney> chimneys = [];
    private readonly List<Vector2> fireflyHomes = [];

    private MenuSceneProposed(bool night)
    {
        Night = night;
        var skyRows = SkyRows(night);
        BuildLand();
        Sky = BuildSky(night);
        Land = ComposeLand(skyRows);
        Clouds = BuildClouds(night);
        Stars = night ? BuildStars() : [];
        Sparkles = BuildSparkles(skyRows);
        Glows = night ? BuildGlows() : [];
        Chimneys = chimneys;
        FireflyHomes = night ? fireflyHomes : [];
        MoonReflection = night ? BuildMoonReflection() : [];
    }

    public bool Night { get; }
    public Image Sky { get; }
    public Image Land { get; }
    public IReadOnlyList<Cloud> Clouds { get; }
    public IReadOnlyList<Star> Stars { get; }
    public IReadOnlyList<Sparkle> Sparkles { get; }
    public IReadOnlyList<Glow> Glows { get; }
    public IReadOnlyList<Chimney> Chimneys { get; }
    public IReadOnlyList<Vector2> FireflyHomes { get; }
    public IReadOnlyList<Vector2I> MoonReflection { get; }
    public Color SmokeColor => Night ? new Color("8C88A4") : new Color("EEECE8");
    public Color ForgeSmokeColor => Night ? new Color("646078") : new Color("AAA6A2");
    public static Color BirdColor => new("3C4254");

    public static MenuSceneProposed Create(bool night) => new(night);

    /// <summary>Maps a hash to 0..1.</summary>
    private static float Unit(uint hash) => hash / 4294967296f;

    /// <summary>Smooth one-dimensional value noise in 0..1.</summary>
    private static float Noise(float x, int seed, float scale)
    {
        var position = x / scale;
        var cell = Mathf.FloorToInt(position);
        var fraction = position - cell;
        fraction = fraction * fraction * (3 - 2 * fraction);
        var a = Unit(PixelArt.Hash(cell, 0, seed));
        var b = Unit(PixelArt.Hash(cell + 1, 0, seed));
        return a + (b - a) * fraction;
    }

    /// <summary>Smooth two-dimensional value noise in 0..1; scales stretch the blobs, wider than tall for ground seen at a low angle.</summary>
    private static float Noise2(float x, float y, int seed, float scaleX, float scaleY)
    {
        var px = x / scaleX;
        var py = y / scaleY;
        var cx = Mathf.FloorToInt(px);
        var cy = Mathf.FloorToInt(py);
        var fx = px - cx;
        var fy = py - cy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var a = Unit(PixelArt.Hash(cx, cy, seed));
        var b = Unit(PixelArt.Hash(cx + 1, cy, seed));
        var c = Unit(PixelArt.Hash(cx, cy + 1, seed));
        var d = Unit(PixelArt.Hash(cx + 1, cy + 1, seed));
        var top = a + (b - a) * fx;
        var bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>Four octaves of <see cref="Noise"/>, for ridge and tree-line outlines.</summary>
    private static float Fbm(float x, int seed, float scale, int octaves = 4)
    {
        float total = 0, amplitude = 1, norm = 0;
        for (var octave = 0; octave < octaves; octave++)
        {
            total += Noise(x, seed + octave * 17, scale / (1 << octave)) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
        }
        return total / norm;
    }

    /// <summary>Darkens a colour by a factor (people's shirts and the darker flower petal only).</summary>
    private static Color Scale(Color value, float amount) =>
        new(value.R * amount, value.G * amount, value.B * amount);

    /// <summary>Writes one scene pixel; any painter but the river's covers open water.</summary>
    private void Put(int x, int y, Color value, Layer onLayer, bool glowing = false, bool riverSurface = false)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var index = y * Width + x;
        color[index] = value;
        layer[index] = onLayer;
        emissive[index] = glowing;
        openWater[index] = riverSurface;
        if (!riverSurface) skyMirror[index] = 0;
    }

    private Layer LayerAt(int x, int y) => layer[y * Width + x];

    /// <summary>True where the river runs, whatever is painted over it.</summary>
    private bool WaterAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && water[y * Width + x];

    /// <summary>
    /// River that is still visible: not under the bridge, its railing or
    /// anyone crossing it. Animated sparkles and glints only go here.
    /// </summary>
    private bool OpenWaterAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && openWater[y * Width + x];

    /// <summary>Stepped sky gradient with a thin checker between bands, as pixel-art skies are painted.</summary>
    private static Color[] SkyRows(bool night)
    {
        var stops = SkyStops(night);
        var rows = new Color[Height];
        for (var y = 0; y < Height; y++)
            rows[y] = SkyBand(stops, Mathf.Min(1f, (float)y / Horizon), 0);
        return rows;
    }

    /// <summary>The sky gradient's colour stops, top to horizon, for day or dusk.</summary>
    private static Color[] SkyStops(bool night) => night
        ? [new("14183A"), new("2C2A56"), new("5E3E6E"), new("C47862")]
        : [new("7AB0D6"), new("A6CDE4"), new("D2E2E6"), new("F2E6C6")];

    /// <summary>The colour of one of the sixteen stepped sky bands.</summary>
    private static Color SkyBand(Color[] stops, float position, int offset)
    {
        const int levels = 16;
        var band = Mathf.Min(levels, (int)(position * levels) + offset);
        var t = (float)band / levels * (stops.Length - 1);
        var index = Mathf.Min((int)t, stops.Length - 2);
        return stops[index].Lerp(stops[index + 1], t - index);
    }

    /// <summary>The sky layer, unchanged from the game: stepped gradient, low sun by day, crescent moon at dusk.</summary>
    private static Image BuildSky(bool night)
    {
        var stops = SkyStops(night);
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        for (var y = 0; y < Height; y++)
        {
            var position = Mathf.Min(1f, (float)y / Horizon);
            var fraction = position * 16 - (int)(position * 16);
            var band = SkyBand(stops, position, 0);
            var next = SkyBand(stops, position, 1);
            for (var x = 0; x < Width; x++)
                image.SetPixel(x, y, fraction > 0.8f && (x + y) % 2 == 0 ? next : band);
        }
        if (night)
        {
            // A crescent moon with a faint two-step halo.
            const int moonX = 272, moonY = 22, radius = 8;
            for (var y = moonY - radius - 6; y <= moonY + radius + 6; y++)
                for (var x = moonX - radius - 6; x <= moonX + radius + 6; x++)
                {
                    var distance = Mathf.Sqrt((x - moonX) * (x - moonX) + (y - moonY) * (y - moonY));
                    var bite = Mathf.Sqrt((x - moonX - 3.5f) * (x - moonX - 3.5f) + (y - moonY + 2) * (y - moonY + 2));
                    var halo = new Color("968CB4");
                    if (distance <= radius && bite > radius - 0.5f)
                        image.SetPixel(x, y, distance < radius - 1 || x < moonX ? new Color("F6F0D6") : new Color("D6CEB4"));
                    else if (distance > radius && distance <= radius + 2)
                        image.SetPixel(x, y, image.GetPixel(x, y).Lerp(halo, 0.22f));
                    else if (distance > radius + 2 && distance <= radius + 5)
                        image.SetPixel(x, y, image.GetPixel(x, y).Lerp(halo, 0.10f));
                }
            return image;
        }
        // A low morning sun with stepped glow rings.
        const int sunX = 52, sunY = 32, sunRadius = 9;
        var glow = new Color("FCF0CE");
        for (var y = sunY - sunRadius - 12; y <= sunY + sunRadius + 12; y++)
            for (var x = sunX - sunRadius - 12; x <= sunX + sunRadius + 12; x++)
            {
                var distance = Mathf.Sqrt((x - sunX) * (x - sunX) + (y - sunY) * (y - sunY));
                if (distance <= sunRadius) image.SetPixel(x, y, new Color("FFF6DA"));
                else if (distance <= sunRadius + 3) image.SetPixel(x, y, image.GetPixel(x, y).Lerp(glow, 0.55f));
                else if (distance <= sunRadius + 7) image.SetPixel(x, y, image.GetPixel(x, y).Lerp(glow, 0.28f));
                else if (distance <= sunRadius + 12) image.SetPixel(x, y, image.GetPixel(x, y).Lerp(glow, 0.12f));
            }
        return image;
    }

    /// <summary>Paints every land layer back to front.</summary>
    private void BuildLand()
    {
        PaintFarRidges();
        PaintMountains();
        PaintNearRidge();
        PaintTreeLine();
        var hillTop = PaintFields();
        var groundTop = PaintMeadow();
        PaintRiver(hillTop, groundTop);
        PaintTowns(PaintRoad());
        PaintForeground();
    }

    /// <summary>
    /// The far ridge band behind the main peaks (M2: mixed 45% toward the
    /// sky when composed). Same outline as the game's distant range, with
    /// west faces lit and a thin snow cap.
    /// </summary>
    private void PaintFarRidges()
    {
        var top = new float[Width + 1];
        for (var x = 0; x <= Width; x++)
            top[x] = 58 + 16 * Fbm(x, 3, 70) - 10 * Mathf.Exp(-Mathf.Pow((x - 120) / 40f, 2)) -
                8 * Mathf.Exp(-Mathf.Pow((x - 300) / 30f, 2));
        for (var x = 0; x < Width; x++)
        {
            // Ground rising to the east means this is a west-facing, lit slope.
            var rising = top[Mathf.Min(Width, x + 2)] < top[Mathf.Max(0, x - 2)];
            for (var y = (int)top[x]; y < Horizon; y++)
            {
                var lit = Fbm(x + y * 0.6f, 8, 14) + (rising ? 0.14f : -0.14f) > 0.5f;
                var value = lit ? PeakRamp.Base : PeakRamp.Shade;
                if (y < top[x] + 4 + 3 * Fbm(x, 9, 6)) value = lit ? SnowRamp.Highlight : SnowRamp.Shade;
                Put(x, y, value, Layer.FarRidge);
            }
        }
    }

    /// <summary>
    /// The middle ridge band: the game's snowy peaks, unchanged in outline and
    /// colour (M2: mixed 25% toward the sky). A one-pixel crease now marks
    /// where each lit face turns into shade.
    /// </summary>
    private void PaintMountains()
    {
        var lit = new Color(148 / 255f, 158 / 255f, 178 / 255f);
        var shaded = new Color(116 / 255f, 126 / 255f, 152 / 255f);
        var rib = new Color(104 / 255f, 112 / 255f, 138 / 255f);
        var shadedRib = new Color(134 / 255f, 144 / 255f, 168 / 255f);
        var snow = new Color(240 / 255f, 244 / 255f, 250 / 255f);
        var shadedSnow = new Color(192 / 255f, 204 / 255f, 224 / 255f);
        (int X, int Y, float Slope)[] peaks =
            [(22, 54, 1.05f), (70, 34, 0.95f), (132, 46, 1.1f), (178, 26, 0.9f), (236, 42, 1f), (282, 30, 0.92f), (332, 50, 1f)];
        var ridge = new float[Width];
        var owner = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            var best = float.MaxValue;
            for (var k = 0; k < peaks.Length; k++)
            {
                var dx = x - peaks[k].X;
                var height = peaks[k].Y + Mathf.Abs(dx) * peaks[k].Slope +
                    (Fbm(x, 20 + k, 7) - 0.5f) * 7 * Mathf.Min(1, Mathf.Abs(dx) / 8f);
                if (height < best)
                {
                    best = height;
                    owner[x] = k;
                }
            }
            ridge[x] = best;
        }
        for (var x = 0; x < Width; x++)
        {
            var (peakX, peakY, _) = peaks[owner[x]];
            var snowLine = peakY + 7 + 6 * Fbm(x, 50 + owner[x], 3) + 3 * Fbm(x, 55, 11);
            for (var y = (int)ridge[x]; y < 108; y++)
            {
                var split = peakX + (Fbm(y, 40 + owner[x], 6) - 0.5f) * 5 + (y - peakY) * 0.1f;
                var litFace = x < split;
                var value = litFace ? lit : shaded;
                if (y < snowLine) value = litFace ? snow : shadedSnow;
                // The crease: the first shaded pixel below the summit.
                else if (!litFace && x - 1 < split && y > peakY + 3) value = rib;
                Put(x, y, value, Layer.Mountain);
            }
        }
        // Rock ribs run down each face; their upper ends hold snow.
        for (var k = 0; k < peaks.Length; k++)
        {
            var (peakX, peakY, _) = peaks[k];
            for (var r = 0; r < 5; r++)
            {
                var litSide = r < 3;
                var x = peakX + (litSide ? -(3 + (int)(PixelArt.Hash(k, r, 201) % 9)) : 2 + (int)(PixelArt.Hash(k, r, 202) % 7));
                var y = peakY + 5 + (int)(PixelArt.Hash(k, r, 203) % 9);
                var length = 12 + (int)(PixelArt.Hash(k, r, 204) % 18);
                var slope = (0.55f + PixelArt.Hash(k, r, 205) % 30 / 100f) * (litSide ? -1 : 1);
                var snowy = 5 + (int)(PixelArt.Hash(k, r, 206) % 6);
                for (var step = 0; step < length; step++)
                {
                    var xx = Mathf.RoundToInt(x + slope * step + (Fbm(step, k * 7 + r, 4) - 0.5f) * 2);
                    var yy = y + step;
                    if (xx < 0 || xx >= Width || yy >= Height || LayerAt(xx, yy) != Layer.Mountain || yy < ridge[xx] + 1) continue;
                    var value = step < snowy ? (litSide ? snow : shadedSnow) : litSide ? rib : shadedRib;
                    Put(xx, yy, value, Layer.Mountain);
                }
            }
        }
    }

    /// <summary>
    /// The near ridge band: low rocky shoulders in front of the peaks' feet,
    /// at full colour (M2). Rock ramp faces lit to the west, crease lines in
    /// the edge step down each face with scree fans below them, and forest
    /// grass with small conifers climbing the lower slopes into the tree line.
    /// </summary>
    private void PaintNearRidge()
    {
        (int X, int Y, float Slope)[] shoulders =
            [(-8, 66, 0.34f), (40, 78, 0.3f), (98, 73, 0.42f), (150, 86, 0.28f), (204, 81, 0.36f), (256, 86, 0.3f), (300, 71, 0.42f), (344, 64, 0.3f)];
        var ridge = new float[Width];
        var owner = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            var best = float.MaxValue;
            for (var k = 0; k < shoulders.Length; k++)
            {
                var dx = x - shoulders[k].X;
                var height = shoulders[k].Y + Mathf.Abs(dx) * shoulders[k].Slope +
                    (Fbm(x, 310 + k, 6) - 0.5f) * 4 * Mathf.Min(1, Mathf.Abs(dx) / 6f);
                if (height < best)
                {
                    best = height;
                    owner[x] = k;
                }
            }
            ridge[x] = best;
        }
        var litFaceAt = new bool[Width * Height];
        for (var x = 0; x < Width; x++)
        {
            var (peakX, peakY, _) = shoulders[owner[x]];
            var crest = (int)ridge[x];
            var greenLine = crest + 4 + 6 * Fbm(x, 340, 9);
            for (var y = crest; y < Horizon; y++)
            {
                var split = peakX + (Fbm(y, 330 + owner[x], 5) - 0.5f) * 4 + (y - peakY) * 0.15f;
                var litFace = x < split;
                litFaceAt[y * Width + x] = litFace;
                var mottle = Noise2(x, y, 335, 5, 3);
                Color value;
                if (y == crest) value = litFace ? RockRamp.Highlight : RockRamp.Base;
                else if (litFace) value = mottle < 0.32f ? RockRamp.Base : RockRamp.Light;
                else value = mottle > 0.7f ? RockRamp.Base : RockRamp.Shade;
                // Grass takes over the lower slopes in ragged patches.
                var cover = (y - greenLine) / 8f;
                if (cover > 0 && Noise2(x, y, 337, 6, 3) < cover)
                    value = litFace ? ForestGrassRamp.Light : ForestGrassRamp.Shade;
                Put(x, y, value, Layer.NearRidge);
            }
        }
        // Crease lines run down each face; scree fans out where they end.
        for (var k = 0; k < shoulders.Length; k++)
        {
            var (peakX, peakY, _) = shoulders[k];
            for (var r = 0; r < 4; r++)
            {
                var litSide = r < 2;
                var x0 = peakX + (litSide ? -(3 + (int)(PixelArt.Hash(k, r, 351) % 10)) : 3 + (int)(PixelArt.Hash(k, r, 352) % 9));
                if (x0 < 0 || x0 >= Width) continue;
                var y0 = (int)ridge[x0] + 1 + (int)(PixelArt.Hash(k, r, 353) % 3);
                var length = 5 + (int)(PixelArt.Hash(k, r, 354) % 7);
                var slope = (0.35f + PixelArt.Hash(k, r, 355) % 40 / 100f) * (litSide ? -1 : 1);
                var endX = x0;
                var endY = y0;
                for (var step = 0; step < length; step++)
                {
                    var xx = Mathf.RoundToInt(x0 + slope * step + (Fbm(step, 360 + k * 5 + r, 3) - 0.5f) * 1.5f);
                    var yy = y0 + step;
                    if (xx < 1 || xx >= Width - 1 || yy >= Horizon || LayerAt(xx, yy) != Layer.NearRidge) continue;
                    Put(xx, yy, RockRamp.Edge, Layer.NearRidge);
                    // The gully's west lip catches the light.
                    if (litFaceAt[yy * Width + xx - 1]) Put(xx - 1, yy, RockRamp.Highlight, Layer.NearRidge);
                    endX = xx;
                    endY = yy;
                }
                for (var dy = 1; dy <= 4; dy++)
                    for (var dx = -dy; dx <= dy; dx++)
                    {
                        var xx = endX + dx;
                        var yy = endY + dy;
                        if (xx < 0 || xx >= Width || yy >= Horizon || LayerAt(xx, yy) != Layer.NearRidge) continue;
                        var roll = PixelArt.Hash(xx, yy, 357) % 6;
                        if (roll == 0) Put(xx, yy, RockRamp.Highlight, Layer.NearRidge);
                        else if (roll == 1) Put(xx, yy, litFaceAt[yy * Width + xx] ? RockRamp.Light : RockRamp.Base, Layer.NearRidge);
                        else if (roll == 2 && dy > 2) Put(xx, yy, RockRamp.Shade, Layer.NearRidge);
                    }
            }
        }
        // A few small conifers have climbed the grassy lower slopes.
        for (var x = 2; x < Width - 2; x += 3)
        {
            if (PixelArt.Hash(x, 0, 361) % 3 != 0) continue;
            var y = (int)ridge[x] + 9 + (int)(PixelArt.Hash(x, 1, 361) % 8);
            if (y >= Horizon || LayerAt(x, y) != Layer.NearRidge) continue;
            if (color[y * Width + x] != ForestGrassRamp.Light && color[y * Width + x] != ForestGrassRamp.Shade) continue;
            Conifer(x, y, 3 + (int)(PixelArt.Hash(x, 2, 361) % 3), ConiferRamp.Base, ConiferRamp.Shade, ConiferRamp.Edge, Layer.NearRidge);
        }
    }

    /// <summary>
    /// A small side-view conifer: a tiered triangle, lit on the west half,
    /// shaded on the east, with a one-pixel tip.
    /// </summary>
    private void Conifer(int x, int baseY, int height, Color lit, Color shade, Color edge, Layer onLayer, Color? tierLight = null)
    {
        var maxHalf = Mathf.Max(1, height / 3 + 1);
        for (var dy = 0; dy < height; dy++)
        {
            var y = baseY - height + dy;
            // Every third row steps in a little, which gives the tiers.
            var half = Mathf.Min(maxHalf, (dy + 1) / 2 - (dy > 2 && dy % 3 == 0 ? 1 : 0));
            for (var dx = -half; dx <= half; dx++)
                Put(x + dx, y, dx < 0 ? lit : dx == half && half > 0 ? edge : shade, onLayer);
            if (tierLight is { } tip && half > 0 && dy % 3 == 2) Put(x - half, y, tip, onLayer);
        }
    }

    /// <summary>
    /// A small side-view broadleaf crown: a lumpy disc lit from the upper
    /// west. It uses the darker canopy steps (base, shade, edge) so distant
    /// crowns sit back instead of popping out as bright dots.
    /// </summary>
    private void Broadleaf(int x, int baseY, int radius, Layer onLayer, int salt)
    {
        var centerY = baseY - radius;
        for (var y = centerY - radius; y <= baseY; y++)
            for (var xx = x - radius - 1; xx <= x + radius + 1; xx++)
            {
                var distance = new Vector2((xx - x) / (radius + 0.5f), (y - centerY) / (radius + 0.3f)).Length();
                if (distance > 1 + (PixelArt.Hash(xx, y, salt) % 3) * 0.06f) continue;
                var lightSide = new Vector2(xx - x + radius * 0.45f, y - centerY + radius * 0.45f).Length() < radius * 0.7f;
                var shadedQuarter = xx > x && y > centerY;
                var value = lightSide ? BroadleafRamp.Base : shadedQuarter ? BroadleafRamp.Edge : BroadleafRamp.Shade;
                if (distance > 0.85f && xx > x && y > centerY - 1) value = BroadleafRamp.Edge;
                Put(xx, y, value, onLayer);
            }
    }

    /// <summary>
    /// The tree line along the foot of the near ridge: a dark understory with
    /// two rows of trees in front. Heights follow slow noise, so tall stands
    /// alternate with low scrub and gaps, and broadleaf crowns mix with conifers.
    /// </summary>
    private void PaintTreeLine()
    {
        var top = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            top[x] = (int)(97 + 3 * Mathf.Sin(x * 0.045f + 1.1f) + 2 * Fbm(x, 70, 20));
            for (var y = top[x]; y < 140; y++)
            {
                var value = Noise2(x, y, 72, 6, 2) > 0.6f ? ForestGrassRamp.Base : ForestGrassRamp.Shade;
                if (y < top[x] + 2) value = ConiferRamp.Shade;
                Put(x, y, value, Layer.TreeLine);
            }
        }
        // Back row: shorter, darker trees close together.
        for (var x = 1; x < Width; x += 2 + (int)(PixelArt.Hash(x, 4, 73) % 3))
        {
            var stand = Fbm(x, 74, 30);
            var height = 3 + (int)(stand * 7) + (int)(PixelArt.Hash(x, 5, 73) % 3);
            Conifer(x, top[x] + 1, height, ConiferRamp.Shade, ConiferRamp.Edge, ConiferRamp.Edge, Layer.TreeLine);
        }
        // Front row: lit trees, taller in stands, with gaps and broadleaf crowns.
        for (var x = 0; x < Width; x += 3 + (int)(PixelArt.Hash(x, 3, 71) % 4))
        {
            var stand = Fbm(x, 77, 28);
            var roll = PixelArt.Hash(x, 6, 71) % 10;
            if (roll == 9 && stand < 0.5f) continue;
            var height = 4 + (int)(stand * stand * 13) + (int)(PixelArt.Hash(x, 1, 71) % 3);
            if (PixelArt.Hash(x, 7, 71) % 11 == 0) height += 3;
            var baseY = top[x] + 2 + (int)(PixelArt.Hash(x, 8, 71) % 2);
            if (roll < 3)
                Broadleaf(x, baseY, Mathf.Clamp(height / 3, 2, 4), Layer.TreeLine, 75);
            else
            {
                Conifer(x, baseY, height, ConiferRamp.Base, ConiferRamp.Shade, ConiferRamp.Edge, Layer.TreeLine, ConiferRamp.Light);
                if (height > 9) Put(x, baseY - height, ConiferRamp.Light, Layer.TreeLine);
            }
        }
    }

    /// <summary>
    /// Patchwork fields on the far slope. The game's slanted patch layout is
    /// kept; each patch now shows its crop: rowed grain, furrowed soil, low
    /// green crop rows, young shoots or mottled pasture, framed by hedgerows
    /// with bumps, a shadow toward the viewer and the odd hedge tree.
    /// </summary>
    private int[] PaintFields()
    {
        var top = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            top[x] = (int)(110 + 4 * Mathf.Sin(x * 0.024f + 2.2f) + 3 * Mathf.Sin(x * 0.061f + 0.5f));
            for (var y = top[x]; y < Height; y++)
            {
                var below = y - top[x];
                var band = below / 7;
                var slant = x + band * 23 + below * 1.4f;
                var column = (int)(slant / 38);
                var value = FieldColor(column, band, x, y, below, slant);
                var edgeColumn = (int)((slant + 1) / 38) != column;
                if (below % 7 == 0 || edgeColumn) value = ForestGrassRamp.Shade;
                // Hedges cast a one-pixel shadow toward the viewer.
                else if (below % 7 == 1 && PixelArt.Hash(x, band, 84) % 4 != 0) value = FieldShade(value);
                Put(x, y, value, Layer.Fields);
            }
        }
        // Hedgerow bumps along each band's upper hedge, lit on top.
        for (var x = 0; x < Width; x++)
            for (var y = top[x] + 6; y < Height; y += 7)
            {
                var roll = PixelArt.Hash(x, y, 85) % 5;
                if (roll < 2) Put(x, y, roll == 0 ? ForestGrassRamp.Light : ForestGrassRamp.Base, Layer.Fields);
            }
        // A hedge tree now and then where the hedges meet.
        for (var x = 14; x < Width - 6; x += 27)
        {
            if (PixelArt.Hash(x, 0, 86) % 3 == 0) continue;
            var below = 7 * (1 + (int)(PixelArt.Hash(x, 1, 86) % 2));
            var y = top[x] + below;
            if (y > 130) continue;
            Broadleaf(x, y, 2, Layer.Fields, 87);
        }
        // Low bushes along the top edge, where the fields meet the trees.
        for (var x = 0; x < Width; x += 5)
        {
            if (PixelArt.Hash(x, 9, 83) % 3 != 0) continue;
            for (var dy = 0; dy < 3; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (Mathf.Abs(dx) + dy < 3)
                        Put(x + dx, top[x] - dy, dx < 0 || dy == 2 ? ForestGrassRamp.Light : ForestGrassRamp.Base, Layer.Fields);
        }
        return top;
    }

    /// <summary>One patch's crop texture: rows run along the slant or across the slope, chosen per patch.</summary>
    private static Color FieldColor(int column, int band, int x, int y, int below, float slant)
    {
        var kind = (int)(PixelArt.Hash(column, band, 81) % 5);
        var diagonal = PixelArt.Hash(column, band, 82) % 2 == 0;
        var row = diagonal ? (int)(slant / 1.5f) : below;
        var furrow = Mathf.PosMod(row, 3) == 2;
        return kind switch
        {
            // Pasture: soft mottling, no rows.
            0 => Noise2(x, y, 88, 7, 2) > 0.58f ? GrassRamp.Highlight : GrassRamp.Light,
            // Ripening grain in rows.
            1 => furrow ? ScrubRamp.Light : ScrubRamp.Highlight,
            // A low green crop in dark rows.
            2 => furrow ? GrassRamp.Shade : GrassRamp.Base,
            // Ploughed earth with furrows.
            3 => furrow ? SoilRamp.Light : SoilRamp.Highlight,
            // Young shoots.
            _ => furrow ? GrassRamp.Light : GrassRamp.Highlight,
        };
    }

    /// <summary>The next darker step for a field colour, used for hedge shadows.</summary>
    private static Color FieldShade(Color value)
    {
        foreach (var ramp in new[] { GrassRamp, ScrubRamp, SoilRamp })
        {
            if (value == ramp.Highlight) return ramp.Light;
            if (value == ramp.Light) return ramp.Base;
            if (value == ramp.Base) return ramp.Shade;
            if (value == ramp.Shade) return ramp.Edge;
        }
        return value;
    }

    /// <summary>
    /// The meadow in front of the fields and the darker foreground strip,
    /// mottled in soft wide blobs of neighbouring grass steps instead of
    /// per-pixel specks (rule T1).
    /// </summary>
    private int[] PaintMeadow()
    {
        var top = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            top[x] = (int)(134 + 3 * Mathf.Sin(x * 0.019f + 0.3f) + 2 * Mathf.Sin(x * 0.052f + 1.7f));
            for (var y = top[x]; y < Height; y++)
            {
                var mottle = Noise2(x, y, 91, 10, 3.5f);
                Color value;
                if (y < 168)
                    value = mottle > 0.66f ? GrassRamp.Light : mottle < 0.26f ? GrassRamp.Shade : GrassRamp.Base;
                else
                    value = mottle > 0.7f ? GrassRamp.Base : mottle < 0.3f ? GrassRamp.Edge : GrassRamp.Shade;
                // The meadow's upper rim, where the slope turns, catches light.
                if (y == top[x]) value = GrassRamp.Light;
                Put(x, y, value, y < 168 ? Layer.Meadow : Layer.Foreground);
            }
        }
        // Sparse tufts and the odd flower in the meadow.
        Color[] flowers = [new("F2D86E"), new("F4F0E6"), new("D8604E"), new("A88AD8")];
        for (var x = 0; x < Width; x++)
            for (var y = 140; y < 168; y++)
            {
                if (LayerAt(x, y) != Layer.Meadow) continue;
                var roll = PixelArt.Hash(x, y, 92) % 90;
                if (roll == 0)
                {
                    Put(x, y, GrassRamp.Edge, Layer.Meadow);
                    Put(x, y - 1, GrassRamp.Shade, Layer.Meadow);
                }
                else if (roll == 1 && y > 148)
                    Put(x, y, flowers[PixelArt.Hash(x, y, 93) % 4], Layer.Meadow);
            }
        return top;
    }

    /// <summary>
    /// The river comes over the far hill crest and winds, widening, toward the
    /// viewer. It is framed by a one-pixel lighter bank; the far reach mirrors
    /// the bright sky, the near reach carries a broken reflection band down
    /// its middle and a dark strip under the far bank.
    /// </summary>
    private void PaintRiver(int[] hillTop, int[] groundTop)
    {
        Vector2[] control =
        [
            new(300, 103), new(286, 113), new(262, 119), new(230, 126), new(210, 134),
            new(190, 146), new(198, 157), new(188, 167), new(172, 181),
        ];
        var points = new List<Vector2> { control[0] };
        points.AddRange(control);
        points.Add(control[^1]);
        for (var segment = 0; segment < control.Length - 1; segment++)
            for (var k = 0; k < 160; k++)
            {
                var center = CatmullRom(points[segment], points[segment + 1], points[segment + 2], points[segment + 3], k / 160f);
                var width = 1.8f + Mathf.Max(0, center.Y - 110) * 0.25f;
                var thickness = Mathf.Max(1, width * 0.3f);
                for (var y = Mathf.RoundToInt(center.Y - thickness / 2); y <= Mathf.RoundToInt(center.Y + thickness / 2); y++)
                    for (var x = Mathf.RoundToInt(center.X - width / 2); x <= Mathf.RoundToInt(center.X + width / 2); x++)
                        if (x >= 0 && y >= 0 && x < Width && y < Height && y > hillTop[x])
                            water[y * Width + x] = true;
            }
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!WaterAt(x, y)) continue;
                // Measure this pixel's place across its run of water in the row.
                var start = x;
                while (start > 0 && WaterAt(start - 1, y)) start--;
                var end = x;
                while (end + 1 < Width && WaterAt(end + 1, y)) end++;
                var across = end > start ? (float)(x - start) / (end - start) : 0.5f;
                var onLayer = y < groundTop[x] ? Layer.Fields : Layer.Meadow;
                var underBank = y > 0 && !WaterAt(x, y - 1);
                Color value;
                var mirror = 0f;
                if (y < 128)
                {
                    // At a grazing angle the far reach mirrors the bright sky.
                    value = underBank && y > 116 ? RiverRamp.Base : RiverRamp.Light;
                    mirror = 0.12f;
                }
                else if (underBank)
                    value = RiverRamp.Shade;
                else if (end - start >= 5 && across > 0.3f && across < 0.62f &&
                         PixelArt.Hash(x / 3, y, 95) % 4 != 0)
                {
                    value = PixelArt.Hash(x, y, 96) % 5 == 0 ? RiverRamp.Highlight : RiverRamp.Light;
                    mirror = 0.3f;
                }
                else
                    value = RiverRamp.Base;
                Put(x, y, value, onLayer, riverSurface: true);
                skyMirror[y * Width + x] = mirror;
            }
        }
        // A one-pixel lighter bank on every side of the water.
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!WaterAt(x, y)) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var bx = x + dx;
                    var by = y + dy;
                    if (bx < 0 || by < 0 || bx >= Width || by >= Height || WaterAt(bx, by) || by <= hillTop[bx]) continue;
                    var bank = by < 124 ? GrassRamp.Highlight : SandRamp.Light;
                    Put(bx, by, bank, by < groundTop[bx] ? Layer.Fields : Layer.Meadow);
                }
            }
    }

    /// <summary>A point on the Catmull-Rom curve through p1 and p2.</summary>
    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) =>
        0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);

    /// <summary>
    /// The Road through both Towns in the map's packed dirt (rule R1): a lit
    /// upper edge, a base with a few grey pebbles (R2) and a worn lower edge,
    /// both feathered so they never read as strokes. Over the river it becomes
    /// the map's plank bridge (R4) with a shadow band on the water.
    /// </summary>
    private int PaintRoad()
    {
        (int X, int Y)[] road = [(0, 165), (20, 163), (60, 161), (100, 163), (140, 166), (176, 166), (212, 162), (250, 164), (290, 161), (320, 162)];
        for (var i = 0; i + 1 < road.Length; i++)
            for (var x = road[i].X; x < road[i + 1].X; x++)
            {
                var t = (float)(x - road[i].X) / (road[i + 1].X - road[i].X);
                roadY[x] = road[i].Y + (road[i + 1].Y - road[i].Y) * (3 * t * t - 2 * t * t * t);
            }
        var wet = Enumerable.Range(150, 80).Where(x => WaterAt(x, Mathf.RoundToInt(roadY[x]))).ToArray();
        var from = wet.Min() - 3;
        var to = wet.Max() + 3;
        for (var x = 0; x < Width; x++)
        {
            var y = Mathf.RoundToInt(roadY[x]);
            if (x >= from && x <= to)
            {
                PaintBridgeColumn(x, y, from, to);
                continue;
            }
            var feather = PixelArt.Hash(x, y, 102) % 5;
            if (feather != 0) Put(x, y - 1, feather == 1 ? DirtRamp.Highlight : DirtRamp.Light, Layer.Town);
            var pebble = PixelArt.Hash(x, y, 101) % 14;
            Put(x, y, pebble == 0 ? RockRamp.Highlight : pebble == 1 ? RockRamp.Light : DirtRamp.Base, Layer.Town);
            Put(x, y + 1, PixelArt.Hash(x, y, 103) % 4 == 0 ? DirtRamp.Base : DirtRamp.Shade, Layer.Town);
            // Ruts wear the grass thin below the Road here and there.
            if (PixelArt.Hash(x, y, 104) % 3 == 0) Put(x, y + 2, DirtRamp.Shade.Lerp(GrassRamp.Shade, 0.5f), Layer.Town);
        }
        return from;
    }

    /// <summary>
    /// One column of the plank bridge seen from the side: deck planks with
    /// one-pixel seams (Timber shade), a dark beam under the deck, a rail in
    /// Timber edge on posts, taller end posts, and a 30% shadow on the water.
    /// </summary>
    private void PaintBridgeColumn(int x, int y, int from, int to)
    {
        var plank = Mathf.PosMod(x - from, 4);
        Put(x, y - 1, plank == 0 ? TimberRamp.Base : TimberRamp.Highlight, Layer.Town);
        Put(x, y, plank == 3 ? TimberRamp.Shade : plank == 0 ? TimberRamp.Light : TimberRamp.Base, Layer.Town);
        Put(x, y + 1, plank == 3 ? TimberRamp.Edge : TimberRamp.Shade, Layer.Town);
        Put(x, y + 2, TimberRamp.Edge, Layer.Town);
        // The deck's shadow on the water, 30% toward dark.
        for (var dy = 3; dy <= 4; dy++)
            if (WaterAt(x, y + dy))
                Put(x, y + dy, RiverRamp.Base.Lerp(RiverRamp.Edge, dy == 3 ? 0.6f : 0.3f), Layer.Town);
        var endPost = x - from <= 1 || to - x <= 1;
        if (endPost)
        {
            // Two posts at each end, one pixel taller than the rail, with a lit cap.
            var westColumn = x == from || x == to - 1;
            for (var dy = 2; dy <= 5; dy++) Put(x, y - dy, westColumn ? TimberRamp.Base : TimberRamp.Edge, Layer.Town);
            Put(x, y - 6, westColumn ? TimberRamp.Highlight : TimberRamp.Light, Layer.Town);
            return;
        }
        Put(x, y - 4, TimberRamp.Edge, Layer.Town);
        Put(x, y - 5, Mathf.PosMod(x, 2) == 0 ? TimberRamp.Shade : TimberRamp.Edge, Layer.Town);
        if (Mathf.PosMod(x - from, 4) == 0)
        {
            Put(x, y - 3, TimberRamp.Shade, Layer.Town);
            Put(x, y - 2, TimberRamp.Shade, Layer.Town);
        }
    }

    /// <summary>The two Towns, lantern posts and townsfolk, at the game's positions.</summary>
    private void PaintTowns(int bridgeStart)
    {
        // Left Town: Farmhouse, Warehouse and a House.
        House(28, 158, 18, 10, Roofing.Thatch);
        Warehouse(56, 160, 28, 12);
        House(88, 157, 16, 9, Roofing.ClayTile);
        // Right Town: a Blacksmith and three Houses.
        Blacksmith(224, 158, 20, 10);
        House(252, 161, 18, 10, Roofing.ClayTile);
        House(278, 153, 14, 8, Roofing.ClayTile);
        House(298, 162, 18, 10, Roofing.ClayTile);
        SheafStack(48, 158);
        foreach (var (x, baseY) in new[] { (50, 163), (118, 164), (214, 162), (292, 162) }) LanternPost(x, baseY);
        Person(14, 164, 0);
        Person(108, 165, 3, lantern: true);
        Person(112, 165, 4, child: true);
        Person(bridgeStart + 6, 164, 1);
        Person(270, 164, 2);
        Person(246, 163, 5, lantern: true);
    }

    /// <summary>A doorstep in Doorstep stone under a door, and a packed-dirt path from it down to the Road (rules B5, R3).</summary>
    private void Doorstep(int x, int width, int baseY)
    {
        for (var xx = x - 1; xx <= x + width; xx++)
            Put(xx, baseY, xx == x + width ? DoorstepRamp.Edge : xx == x - 1 ? DoorstepRamp.Base : DoorstepRamp.Shade, Layer.Town);
        var roadTop = Mathf.RoundToInt(roadY[Mathf.Clamp(x, 0, Width - 1)]) - 1;
        for (var y = baseY + 1; y < roadTop; y++)
        {
            // A long path runs diagonally down to the Road, so it reads as a
            // footpath rather than a post under the door.
            var shift = roadTop - baseY > 4 ? -(y - baseY - 1) : 0;
            for (var xx = x + shift - 1; xx <= x + shift + width; xx++)
            {
                var side = xx == x + shift - 1 || xx == x + shift + width;
                if (side && PixelArt.Hash(xx, y, 105) % 2 == 0) continue;
                Put(xx, y, side ? DirtRamp.Shade : DirtRamp.Base, Layer.Town);
            }
        }
    }

    /// <summary>Plastered, half-timbered walls with a shadow under the eave and a stone plinth.</summary>
    private void Walls(int x, int top, int baseY, int width, bool timbered)
    {
        for (var y = top; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                var value = xx >= x + width - 2 || y == top + 1 ? PlasterShade : Plaster;
                if (timbered && (xx == x || xx == x + width - 1 || y == top)) value = TimberRamp.Shade;
                if (y == baseY - 1) value = xx >= x + width - 2 ? DoorstepRamp.Edge : DoorstepRamp.Shade;
                Put(xx, y, value, Layer.Town);
            }
        if (timbered)
            for (var xx = x; xx < x + width; xx++)
                Put(xx, top + (baseY - top) / 2, TimberRamp.Shade, Layer.Town);
    }

    /// <summary>
    /// A gable roof seen end-on, in the map's material: offset clay tiles,
    /// jittered thatch strands with a bound ridge, or slate slabs. The west
    /// slope is lit with a light rim, the east slope shaded with an edge-step
    /// rim, and a light ridge cap sits on top (rules B3, B4, L1).
    /// </summary>
    private void GableRoof(int x, int top, int width, Roofing roofing)
    {
        var ramp = roofing switch
        {
            Roofing.Thatch => ThatchRamp,
            Roofing.Slate => SlateRamp,
            _ => ClayTileRamp,
        };
        var rise = (width + 3) / 2;
        var apex = top - rise + 1;
        for (var dy = 0; dy < rise; dy++)
        {
            var y = apex + dy;
            var half = dy + 1;
            var left = Mathf.Max(x - 2, x + width / 2 - half);
            var right = Mathf.Min(x + width + 1, x + width / 2 + half + width % 2 - 1);
            for (var xx = left; xx <= right; xx++)
            {
                var lit = xx < x + width / 2;
                var value = RoofTexture(roofing, ramp, lit, xx - x, dy);
                if (xx == left && left > x - 2) value = ramp.Light;
                else if (xx == right && right < x + width + 1) value = ramp.Edge;
                Put(xx, y, value, Layer.Town);
            }
        }
        if (roofing == Roofing.Thatch)
        {
            // A bound ridge, and a thick ragged eave instead of a hard line.
            for (var xx = x + width / 2 - 2; xx <= x + width / 2 + 1 + width % 2; xx++)
                Put(xx, apex + 2, xx < x + width / 2 ? ramp.Shade : ramp.Edge, Layer.Town);
            for (var xx = x - 2; xx < x + width + 2; xx++)
                Put(xx, top, PixelArt.Hash(xx, top, 7) % 3 == 0 ? ramp.Shade : ramp.Edge, Layer.Town);
        }
        else
            for (var xx = x - 2; xx < x + width + 2; xx++) Put(xx, top, ramp.Edge, Layer.Town);
        Put(x + width / 2, apex, ramp.Highlight, Layer.Town);
    }

    /// <summary>The roof material at one pixel: courses two rows tall, with tile or slab seams offset per course.</summary>
    private static Color RoofTexture(Roofing roofing, Ramp ramp, bool lit, int u, int dy)
    {
        var course = dy / 2;
        var lip = dy % 2 == 1;
        switch (roofing)
        {
            case Roofing.Thatch:
            {
                // Strands run down the slope; a strand is a column with a jittered start.
                var strand = PixelArt.Hash(u, (dy + (int)(PixelArt.Hash(u, 0, 8) % 3)) / 3, 9) % 6;
                if (lit) return strand == 0 ? ramp.Highlight : strand < 3 ? ramp.Light : ramp.Base;
                return strand == 0 ? ramp.Base : strand == 5 ? ramp.Edge : ramp.Shade;
            }
            case Roofing.Slate:
            {
                var seam = Mathf.PosMod(u + course * 2, 4) == 0;
                if (lit) return lip ? (seam ? ramp.Shade : ramp.Base) : seam ? ramp.Base : ramp.Light;
                return lip ? (seam ? ramp.Edge : ramp.Shade) : seam ? ramp.Shade : ramp.Base;
            }
            default:
            {
                var seam = Mathf.PosMod(u + course * 2, 3) == 0;
                if (lit) return lip ? (seam ? ramp.Shade : ramp.Base) : seam ? ramp.Light : ramp.Base;
                return lip ? (seam ? ramp.Edge : ramp.Shade) : seam ? ramp.Base : ramp.Shade;
            }
        }
    }

    /// <summary>A stone chimney with a two-pixel cap; smoke leaves from the same point as in the game (rule B6).</summary>
    private void ChimneyAt(int x, int roofTop, bool forge)
    {
        for (var y = roofTop + 2; y < roofTop + 6; y++)
        {
            Put(x, y, RockRamp.Light, Layer.Town);
            Put(x + 1, y, RockRamp.Shade, Layer.Town);
        }
        Put(x - 1, roofTop + 1, RockRamp.Base, Layer.Town);
        Put(x, roofTop + 1, RockRamp.Highlight, Layer.Town);
        Put(x + 1, roofTop + 1, RockRamp.Base, Layer.Town);
        Put(x + 2, roofTop + 1, RockRamp.Edge, Layer.Town);
        chimneys.Add(new Chimney(new Vector2(x + 0.5f, roofTop), forge));
    }

    /// <summary>A small window with a sill; blue glass by day, warm F2C14E light at dusk (rule M4).</summary>
    private void Window(int x, int baseY, Color? shutters)
    {
        var glass = Night ? WindowLight : new Color("6E8AA0");
        for (var y = baseY - 7; y < baseY - 4; y++)
        {
            Put(x, y, glass, Layer.Town, Night);
            Put(x + 1, y, Night ? glass : new Color("5A7488"), Layer.Town, Night);
            if (shutters is { } shutter)
            {
                Put(x - 1, y, shutter, Layer.Town);
                Put(x + 2, y, shutter, Layer.Town);
            }
        }
        Put(x, baseY - 7, Night ? WindowLightTop : new Color("A8C4D8"), Layer.Town, Night);
        for (var xx = x - 1; xx <= x + 2; xx++) Put(xx, baseY - 4, DoorstepRamp.Light, Layer.Town);
        lights.Add((new Vector2(x + 0.5f, baseY - 6), 6, WindowLight, 0.45f, false));
    }

    /// <summary>A plank door with a lintel, in Timber edge and shade (rule B5).</summary>
    private void Door(int x, int baseY, int width, int height)
    {
        for (var xx = x - 1; xx <= x + width; xx++) Put(xx, baseY - height - 1, TimberRamp.Edge, Layer.Town);
        for (var y = baseY - height; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
                Put(xx, y, xx == x ? TimberRamp.Shade : TimberRamp.Edge, Layer.Town);
    }

    /// <summary>A half-timbered House or Farmhouse: walls, roof, capped chimney, door, doorstep path and a shuttered window.</summary>
    private void House(int x, int baseY, int width, int wallHeight, Roofing roofing)
    {
        var top = baseY - wallHeight;
        Walls(x, top, baseY, width, timbered: true);
        GableRoof(x, top, width, roofing);
        ChimneyAt(x + width - 5, top - (width + 3) / 2 + 1, forge: false);
        Door(x + 2, baseY, 2, 6);
        Put(x + 3, baseY - 3, new Color("C9A060"), Layer.Town);
        Doorstep(x + 2, 2, baseY);
        var shutters = roofing == Roofing.Thatch ? TimberRamp.Base : ClayTileRamp.Shade;
        if (x + width - 6 > x + 4) Window(x + width - 6, baseY, shutters);
    }

    /// <summary>Coursed stone walls, a slate roof, the glowing forge door and an anvil in the yard (rule B6).</summary>
    private void Blacksmith(int x, int baseY, int width, int wallHeight)
    {
        var top = baseY - wallHeight;
        for (var y = top; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                var shaded = xx >= x + width - 2;
                var course = (y - top) / 2;
                var joint = (y - top) % 2 == 1 || Mathf.PosMod(xx - x + course * 2, 4) == 0;
                var value = shaded ? (joint ? RockRamp.Base : RockRamp.Light) : joint ? RockRamp.Light : RockRamp.Highlight;
                if (y == top + 1) value = shaded ? RockRamp.Base : RockRamp.Light;
                Put(xx, y, value, Layer.Town);
            }
        GableRoof(x, top, width, Roofing.Slate);
        ChimneyAt(x + width - 5, top - (width + 3) / 2 + 1, forge: true);
        // The open forge glows day and night.
        var door = x + width / 2 - 2;
        for (var xx = door - 1; xx <= door + 5; xx++) Put(xx, baseY - 8, RockRamp.Edge, Layer.Town);
        for (var y = baseY - 7; y < baseY; y++)
            for (var xx = door; xx < door + 5; xx++)
                Put(xx, y, new Color("2B2420"), Layer.Town);
        for (var y = baseY - 3; y < baseY; y++)
            for (var xx = door + 1; xx < door + 4; xx++)
                Put(xx, y, (xx + y) % 2 == 0 ? new Color("FFB45A") : new Color("FF8A3C"), Layer.Town, glowing: true);
        lights.Add((new Vector2(door + 2.5f, baseY - 2), 9, new Color("FF8C3C"), 0.55f, true));
        Doorstep(door, 5, baseY);
        Window(x + 2, baseY, null);
        Anvil(x - 5, baseY);
    }

    /// <summary>An anvil on a stump, three pixels tall, west of the forge.</summary>
    private void Anvil(int x, int baseY)
    {
        Put(x, baseY - 1, TimberRamp.Base, Layer.Town);
        Put(x + 1, baseY - 1, TimberRamp.Base, Layer.Town);
        Put(x + 2, baseY - 1, TimberRamp.Shade, Layer.Town);
        Put(x + 1, baseY - 2, IronRamp.Shade, Layer.Town);
        Put(x - 1, baseY - 3, IronRamp.Base, Layer.Town);
        Put(x, baseY - 3, IronRamp.Highlight, Layer.Town);
        Put(x + 1, baseY - 3, IronRamp.Light, Layer.Town);
        Put(x + 2, baseY - 3, IronRamp.Base, Layer.Town);
    }

    /// <summary>Plank walls under a hipped grey-timber roof with planks along the ridge, split loading doors and crates (rules B3, B6).</summary>
    private void Warehouse(int x, int baseY, int width, int wallHeight)
    {
        var top = baseY - wallHeight;
        for (var y = top; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                var board = Mathf.PosMod(xx - x, 4);
                var value = board == 0 ? TimberRamp.Shade : board == 1 ? TimberRamp.Light : TimberRamp.Base;
                if (xx >= x + width - 2) value = board == 0 ? TimberRamp.Edge : TimberRamp.Shade;
                if (y == top + 1) value = TimberRamp.Shade;
                Put(xx, y, value, Layer.Town);
            }
        var roof = GreyTimberRamp;
        for (var dy = 0; dy < 7; dy++)
        {
            var y = top - 7 + dy + 1;
            var inset = 6 - dy;
            var left = x - 2 + inset;
            var right = x + width + 1 - inset;
            for (var xx = left; xx <= right; xx++)
            {
                var lit = xx < x + width / 2;
                Color value;
                if (dy == 0) value = roof.Light;
                else if (dy % 2 == 0) value = lit ? roof.Shade : roof.Edge;
                else value = lit ? (PixelArt.Hash(xx / 5, y, 6) % 4 == 0 ? roof.Light : roof.Base) : roof.Shade;
                if (xx == left) value = roof.Light;
                else if (xx == right) value = roof.Edge;
                Put(xx, y, value, Layer.Town);
            }
        }
        for (var xx = x - 2; xx < x + width + 2; xx++) Put(xx, top, roof.Edge, Layer.Town);
        // Split loading doors with a cross brace, and crates stacked outside.
        var door = x + width / 2 - 4;
        for (var y = baseY - 9; y < baseY; y++)
            for (var xx = door; xx < door + 8; xx++)
            {
                var column = xx - door;
                var value = column is 0 or 7 or 3 || y == baseY - 9 ? TimberRamp.Edge : column is 1 or 4 ? TimberRamp.Base : TimberRamp.Shade;
                if (column + (y - baseY) == -1 || 7 - column + (y - baseY) == -1) value = TimberRamp.Light;
                Put(xx, y, value, Layer.Town);
            }
        Doorstep(door + 1, 6, baseY);
        foreach (var (crateX, height) in new[] { (x + width + 1, 3), (x + width + 5, 4), (x - 5, 3) })
            for (var y = baseY - height; y < baseY; y++)
                for (var xx = crateX; xx < crateX + 3; xx++)
                {
                    var value = y == baseY - height ? TimberRamp.Highlight : xx == crateX + 2 ? TimberRamp.Shade : TimberRamp.Light;
                    if ((xx - crateX + y) % 3 == 0 && y > baseY - height) value = TimberRamp.Base;
                    Put(xx, y, value, Layer.Town);
                }
        lights.Add((new Vector2(x + width / 2, baseY - 5), 10, new Color("FFC46E"), 0.25f, false));
    }

    /// <summary>Two stacked sheaves of grain beside the Farmhouse (rule B6).</summary>
    private void SheafStack(int x, int baseY)
    {
        for (var dy = 0; dy < 4; dy++)
            for (var dx = 0; dx < 3; dx++)
            {
                if (dy == 3 && dx != 1) continue;
                var value = dx == 0 ? ThatchRamp.Light : dx == 2 ? ThatchRamp.Shade : ThatchRamp.Base;
                if (dy == 1) value = ThatchRamp.Edge;
                Put(x + dx, baseY - 1 - dy, value, Layer.Town);
            }
    }

    /// <summary>An iron lantern post with a capped lamp; the lamp is lit at dusk.</summary>
    private void LanternPost(int x, int baseY)
    {
        for (var y = baseY - 9; y < baseY; y++) Put(x, y, Iron, Layer.Town);
        Put(x - 1, baseY - 9, Iron, Layer.Town);
        Put(x + 1, baseY - 9, Iron, Layer.Town);
        Put(x, baseY - 11, Iron, Layer.Town);
        Put(x, baseY - 10, Night ? new Color("FFD27A") : new Color("8E8A70"), Layer.Town, Night);
        lights.Add((new Vector2(x, baseY - 10), 10, new Color("FFCE78"), 0.5f, false));
    }

    /// <summary>A tiny townsperson in the agent palettes, optionally carrying a lantern that is lit at dusk.</summary>
    private void Person(int x, int baseY, int look, bool lantern = false, bool child = false)
    {
        var height = child ? 5 : 7;
        var shirt = Shirts[look % Shirts.Length];
        Put(x, baseY - height, Hair[(look + 2) % Hair.Length], Layer.Town);
        Put(x + 1, baseY - height, Hair[(look + 2) % Hair.Length], Layer.Town);
        Put(x, baseY - height + 1, Skins[look % Skins.Length], Layer.Town);
        Put(x + 1, baseY - height + 1, Skins[look % Skins.Length], Layer.Town);
        for (var y = baseY - height + 2; y < baseY - 1; y++)
        {
            Put(x, y, shirt, Layer.Town);
            Put(x + 1, y, Scale(shirt, 0.8f), Layer.Town);
        }
        Put(x, baseY - 1, Iron, Layer.Town);
        Put(x + 1, baseY - 1, Iron, Layer.Town);
        if (!lantern) return;
        Put(x + 2, baseY - 3, Night ? new Color("FFD27A") : new Color("9A8A60"), Layer.Town, Night);
        lights.Add((new Vector2(x + 2, baseY - 3), 7, new Color("FFCE78"), 0.5f, false));
    }

    /// <summary>
    /// The foreground strip: grass tufts with lit tips and a few flowers on
    /// stems, then the tree, bushes and rocks over them (same places as in
    /// the game, so the firefly homes are unchanged).
    /// </summary>
    private void PaintForeground()
    {
        Color[] flowers = [new("F2D86E"), new("F4F0E6"), new("D8604E"), new("A88AD8")];
        for (var x = 0; x < Width; x++)
            for (var y = 170; y < Height; y++)
            {
                if (LayerAt(x, y) != Layer.Foreground) continue;
                var roll = PixelArt.Hash(x, y, 121) % 100;
                if (roll < 5)
                {
                    // A tuft: a centre blade and one or two leaning blades, the tallest tip lit.
                    var length = 3 + (int)(roll % 2) + (y > 175 ? 1 : 0);
                    for (var k = 0; k < length; k++)
                        Put(x, y - k, k == length - 1 ? GrassRamp.Light : k == 0 ? GrassRamp.Edge : ForestGrassRamp.Shade, Layer.Foreground);
                    for (var k = 0; k < length - 1; k++)
                        Put(x - 1 - k / 2, y - k, k == length - 2 ? GrassRamp.Base : GrassRamp.Edge, Layer.Foreground);
                    if (roll % 2 == 0)
                        for (var k = 0; k < length - 1; k++)
                            Put(x + 1 + k / 2, y - k, ForestGrassRamp.Shade, Layer.Foreground);
                }
                else if ((roll == 7 || roll == 8) && PixelArt.Hash(x, y, 122) % 3 != 0)
                {
                    var bloom = flowers[PixelArt.Hash(x, y, 3) % 4];
                    Put(x, y, GrassRamp.Edge, Layer.Foreground);
                    Put(x, y - 1, bloom, Layer.Foreground);
                    if (roll == 8 && y > 174)
                    {
                        Put(x - 1, y - 1, bloom, Layer.Foreground);
                        Put(x, y - 2, bloom, Layer.Foreground);
                        Put(x - 1, y - 2, Scale(bloom, 0.82f), Layer.Foreground);
                    }
                }
            }
        Tree(12, 160, 7, Layer.Town);
        foreach (var (x, baseY, radius) in new[] { (10, 179, 6), (22, 180, 4), (150, 181, 5), (304, 180, 7), (290, 181, 4) })
        {
            Bush(x, baseY, radius);
            fireflyHomes.Add(new Vector2(x, baseY - radius - 3));
        }
        Rock(128, 179, 9, 5);
        Rock(262, 180, 7, 4);
        fireflyHomes.Add(new Vector2(70, 172));
        fireflyHomes.Add(new Vector2(236, 174));
    }

    /// <summary>
    /// The broadleaf tree by the left Town: three canopy tones with the
    /// highlight toward the north-west, an edge-step outline on the shaded
    /// rim, a timber trunk and a soft ground shadow to the south-east.
    /// </summary>
    private void Tree(int x, int baseY, int radius, Layer onLayer)
    {
        for (var dx = 0; dx <= 6; dx++)
            if (LayerAt(x + dx, baseY) is Layer.Meadow) Put(x + dx, baseY, GrassRamp.Shade, Layer.Meadow);
        for (var y = baseY - 4; y <= baseY; y++)
        {
            Put(x, y, TimberRamp.Base, onLayer);
            Put(x + 1, y, TimberRamp.Edge, onLayer);
        }
        var centerY = baseY - 4 - radius;
        var ramp = BroadleafRamp;
        bool Inside(int xx, int y) =>
            new Vector2((xx - x - 0.5f) / (radius + 1), (y - centerY) / (radius + 0.5f)).Length() <=
            1 + PixelArt.Hash(xx, y, 141) % 3 * 0.04f;
        for (var y = centerY - radius - 1; y <= centerY + radius + 1; y++)
            for (var xx = x - radius - 2; xx <= x + radius + 2; xx++)
            {
                if (!Inside(xx, y)) continue;
                var toLight = new Vector2(xx - x + radius * 0.4f, y - centerY + radius * 0.45f).Length();
                var value = toLight < radius * 0.35f ? ramp.Highlight : toLight < radius * 0.8f ? ramp.Light : ramp.Base;
                if (xx > x + 1 && y > centerY + 1 && toLight > radius * 1.1f) value = ramp.Shade;
                if (value == ramp.Base && PixelArt.Hash(xx, y, 143) % 7 == 0) value = ramp.Shade;
                // Outline the rim away from the light.
                var rim = !Inside(xx + 1, y) || !Inside(xx, y + 1);
                if (rim && (xx > x - 2 || y > centerY)) value = ramp.Edge;
                Put(xx, y, value, onLayer);
            }
    }

    /// <summary>A foreground bush: outlined, lit on the upper west, darker toward the ground.</summary>
    private void Bush(int x, int baseY, int radius)
    {
        var ramp = BroadleafRamp;
        bool Inside(int xx, int y) =>
            new Vector2((xx - x) / (radius + 1f), (y - (baseY - radius)) / (float)radius).Length() <= 1;
        for (var y = baseY - radius * 2; y <= baseY; y++)
            for (var xx = x - radius - 1; xx <= x + radius + 1; xx++)
            {
                if (!Inside(xx, y)) continue;
                var toLight = new Vector2(xx - x + radius * 0.45f, y - (baseY - radius) + radius * 0.5f).Length();
                var value = toLight < radius * 0.55f ? ramp.Light : toLight < radius * 1.05f ? ramp.Base : ramp.Shade;
                if (value == ramp.Base && PixelArt.Hash(xx, y, 110) % 6 == 0) value = ramp.Shade;
                if (!Inside(xx, y - 1) || !Inside(xx + 1, y) || !Inside(xx - 1, y)) value = toLight < radius * 0.9f ? ramp.Base : ramp.Edge;
                Put(xx, y, value, Layer.Foreground);
            }
    }

    /// <summary>A foreground boulder: lit west facet, shaded east facet, an edge-step outline and one crack.</summary>
    private void Rock(int x, int baseY, int width, int height)
    {
        bool Inside(int xx, int y) => y >= baseY - height && y <= baseY && xx >= x && xx < x + width &&
            xx - x + (baseY - y) * 0.6f < width && x + width - xx + (baseY - y) * 0.9f < width + 2;
        for (var y = baseY - height; y <= baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                if (!Inside(xx, y)) continue;
                var value = xx - x < width / 2f ? RockRamp.Light : RockRamp.Shade;
                if (!Inside(xx, y - 1) && xx - x < width / 2f) value = RockRamp.Highlight;
                if (!Inside(xx + 1, y) || !Inside(xx - 1, y)) value = RockRamp.Edge;
                if (xx == x + width / 2 && y == baseY - height / 2) value = RockRamp.Edge;
                Put(xx, y, value, Layer.Foreground);
            }
    }

    /// <summary>
    /// How much of the sky colour a layer takes on (rule M2): the far ridges
    /// 45%, the middle peaks 25%, the near ridge and everything in front of it
    /// none by day. At dusk the valley floor keeps a little of the evening air
    /// so it does not sink into black.
    /// </summary>
    private float Haze(Layer onLayer) => onLayer switch
    {
        Layer.FarRidge => 0.45f,
        Layer.Mountain => 0.25f,
        Layer.NearRidge or Layer.TreeLine or Layer.Fields => Night ? 0.12f : 0f,
        Layer.Meadow => Night ? 0.06f : 0f,
        _ => 0f,
    };

    /// <summary>Night grading and distance haze toward the sky color of the same row.</summary>
    private Color Graded(Color value, Layer onLayer, int y, Color[] skyRows)
    {
        if (Night)
            value = new Color(value.R * 0.30f + 6 / 255f, value.G * 0.35f + 9 / 255f, value.B * 0.56f + 26 / 255f);
        return value.Lerp(skyRows[Mathf.Min(y, Night ? 62 : Horizon - 1)], Haze(onLayer));
    }

    /// <summary>
    /// The faint dusk mist band (rule M4): a pale lavender veil lying over the
    /// far fields and the foot of the tree line, thicker in some places than
    /// others. It has two stepped strengths whose edges wander along the band
    /// in long horizontal wisps, so it never shows a dither checker.
    /// </summary>
    private static float MistAt(int x, int y)
    {
        var center = 116 + 3 * Mathf.Sin(x * 0.031f + 0.8f) + 2 * Fbm(x, 401, 40);
        var thickness = 6 + 5 * Fbm(x, 402, 55);
        var strength = 1 - Mathf.Abs(y - center) / thickness;
        if (strength <= 0) return 0;
        var wisp = 0.3f * (Noise2(x, y, 403, 16, 1.5f) - 0.5f);
        var level = strength > 0.55f + wisp ? 2 : strength > 0.12f + wisp ? 1 : 0;
        return level * 0.075f;
    }

    /// <summary>Grades every painted pixel into the Land image: night tint, layer haze, dusk sky reflection and mist.</summary>
    private Image ComposeLand(Color[] skyRows)
    {
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        var mist = new Color("A6A2C4");
        var lowSky = skyRows[Horizon - 4];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var index = y * Width + x;
                if (layer[index] == Layer.Sky) continue;
                var value = emissive[index] ? color[index] : Graded(color[index], layer[index], y, skyRows);
                if (Night && !emissive[index])
                {
                    // The reflection band mirrors the warm low sky at dusk.
                    if (skyMirror[index] > 0) value = value.Lerp(lowSky, skyMirror[index]);
                    if (layer[index] is Layer.NearRidge or Layer.TreeLine or Layer.Fields or Layer.Meadow)
                        value = value.Lerp(mist, MistAt(x, y));
                }
                image.SetPixel(x, y, new Color(value, 1));
            }
        return image;
    }

    /// <summary>The four drifting clouds, unchanged from the game.</summary>
    private static List<Cloud> BuildClouds(bool night)
    {
        var light = night ? new Color("46406C") : new Color("FAFAF6");
        var dark = night ? new Color("343058") : new Color("CEDAE8");
        var rim = night ? new Color("847CA4") : new Color("FFFFFF");
        (int X, int Y, float Speed, (int X, int Y, int R)[] Puffs)[] clouds =
        [
            (236, 26, 1.6f, [(-14, 0, 6), (-5, -3, 8), (6, -2, 7), (15, 0, 5)]),
            (20, 16, 2.2f, [(-8, 0, 5), (0, -2, 6), (8, 0, 4)]),
            (300, 54, 1.1f, [(-6, 0, 4), (2, -1, 5), (9, 0, 3)]),
            (120, 22, 1.9f, [(-6, 0, 4), (2, -2, 5), (9, 0, 4)]),
        ];
        var result = new List<Cloud>();
        foreach (var cloud in clouds)
        {
            const int left = 24, top = 16;
            var image = Image.CreateEmpty(left * 2 + 1, top + 3, false, Image.Format.Rgba8);
            bool Inside(int x, int y) => y <= top && cloud.Puffs.Any(puff =>
                new Vector2(x - left - puff.X, (y - top - puff.Y) * 1.25f).Length() <= puff.R);
            for (var y = 0; y <= top; y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    if (!Inside(x, y)) continue;
                    var upper = !Inside(x, y - 1);
                    var lower = y >= top - 2 || !Inside(x, y + 3);
                    image.SetPixel(x, y, upper ? rim : lower ? dark : light);
                }
            result.Add(new Cloud(image, cloud.X - left, cloud.Y - top, cloud.Speed));
        }
        return result;
    }

    /// <summary>The dusk stars, unchanged from the game.</summary>
    private static List<Star> BuildStars()
    {
        var stream = new PixelArt.Stream(11);
        var stars = new List<Star>();
        for (var i = 0; i < 190; i++)
        {
            var x = stream.Range(0, Width);
            var y = stream.Range(0, 80);
            var roll = stream.Range(0, 100);
            if (new Vector2(x - 272, y - 22).Length() < 15) continue;
            stars.Add(new Star(new Vector2I(x, y), roll >= 80 ? new Color("FAF6E2") : new Color("B0B2D6"), roll >= 96, stream.Range(0, 64)));
        }
        return stars;
    }

    /// <summary>Sparkle points on open water with open water on all four sides.</summary>
    private List<Sparkle> BuildSparkles(Color[] skyRows)
    {
        var sparkles = new List<Sparkle>();
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!OpenWaterAt(x, y) || !OpenWaterAt(x - 1, y) || !OpenWaterAt(x + 1, y) ||
                    !OpenWaterAt(x, y - 1) || !OpenWaterAt(x, y + 1)) continue;
                var value = Graded(new Color("A9D2EA"), LayerAt(x, y), y, skyRows);
                sparkles.Add(new Sparkle(new Vector2I(x, y), value, (int)(PixelArt.Hash(x / 2, y, 97) % 24)));
            }
        return sparkles;
    }

    /// <summary>The moon's shimmering reflection: short runs across the near river every third row.</summary>
    private List<Vector2I> BuildMoonReflection()
    {
        var points = new List<Vector2I>();
        for (var y = 150; y < Height; y += 3)
        {
            var row = Enumerable.Range(0, Width).Where(x => WaterAt(x, y)).ToArray();
            if (row.Length == 0) continue;
            var middle = (row.Min() + row.Max()) / 2f;
            for (var x = (int)(middle - 1); x < (int)(middle + 2 + (y - 150) / 12f); x++)
                if (OpenWaterAt(x, y) && !emissive[y * Width + x]) points.Add(new Vector2I(x, y));
        }
        return points;
    }

    /// <summary>Stepped, dithered warm pools around each window, lantern and the forge.</summary>
    private List<Glow> BuildGlows()
    {
        var glows = new List<Glow>();
        foreach (var (center, radius, tint, strength, forge) in lights)
        {
            var size = (int)radius * 2 + 1;
            var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var origin = new Vector2I(Mathf.FloorToInt(center.X - radius), Mathf.FloorToInt(center.Y - radius));
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var worldX = origin.X + x;
                    var worldY = origin.Y + y;
                    var distance = new Vector2(worldX - center.X, (worldY - center.Y) * 1.2f).Length() / radius;
                    if (distance >= 1) continue;
                    var step = Mathf.Floor((1 - distance) * 4 + Bayer[Mathf.PosMod(worldY, 4), Mathf.PosMod(worldX, 4)]) / 4;
                    if (step > 0) image.SetPixel(x, y, new Color(tint, strength * step * 0.7f));
                }
            glows.Add(new Glow(image, origin, strength, forge));
        }
        return glows;
    }
}
