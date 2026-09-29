using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The Main Menu backdrop: a side-view valley with snowy mountains,
/// patchwork fields, a river and two small Towns, drawn from fixed seeds at
/// 320 × 180 logical pixels. The still layers are built once for daytime
/// (Light theme) or dusk (Dark theme); <see cref="MenuBackdrop"/> moves the
/// clouds, smoke, birds, water, stars, lights and fireflies on top.
/// </summary>
public sealed class MenuScene
{
    public const int Width = 320;
    public const int Height = 180;

    public readonly record struct Cloud(Image Image, float X, float Y, float Speed);
    public readonly record struct Chimney(Vector2 Position, bool Forge);
    public readonly record struct Sparkle(Vector2I Position, Color Color, int Phase);
    public readonly record struct Star(Vector2I Position, Color Color, bool Large, int Phase);
    public readonly record struct Glow(Image Image, Vector2I Position, float Strength, bool Forge);

    private const float SkyDepth = -1;
    private const int Horizon = 104;

    private static readonly Color Grass = new("5F8F5B");
    private static readonly Color GrassDark = new("426D4B");
    private static readonly Color GrassLight = new("8DB870");
    private static readonly Color Forest = new("3F5D42");
    private static readonly Color Peak = new("AEB2B0");
    private static readonly Color Snow = new("E4EAE6");
    private static readonly Color Soil = new("735F45");
    private static readonly Color Sand = new("BAA77B");
    private static readonly Color River = new("4786AB");
    private static readonly Color Beam = new("6B4A30");
    private static readonly Color Iron = new("3A3028");

    // Roof colors match the placed-building sprites on the map.
    private static readonly (Color Lit, Color Shade, Color Edge, Color Ridge) HouseRoof =
        (new("C66A45"), new("9E4E34"), new("5E2E22"), new("E08E64"));
    private static readonly (Color Lit, Color Shade, Color Edge, Color Ridge) WarehouseRoof =
        (new("758390"), new("59656F"), new("343C43"), new("97A5B0"));
    private static readonly (Color Lit, Color Shade, Color Edge, Color Ridge) FarmhouseRoof =
        (new("D2AE5E"), new("A98A45"), new("6B5528"), new("E6C77B"));
    private static readonly (Color Lit, Color Shade, Color Edge, Color Ridge) BlacksmithRoof =
        (new("62666E"), new("4A4E55"), new("2B2E33"), new("80858E"));

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
    private readonly float[] depth = new float[Width * Height];
    private readonly bool[] emissive = new bool[Width * Height];
    private readonly bool[] water = new bool[Width * Height];
    private readonly List<(Vector2 Center, float Radius, Color Color, float Strength, bool Forge)> lights = [];
    private readonly List<Chimney> chimneys = [];
    private readonly List<Vector2> fireflyHomes = [];

    private MenuScene(bool night)
    {
        Night = night;
        Array.Fill(depth, SkyDepth);
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

    public static MenuScene Create(bool night) => new(night);

    private static float Unit(uint hash) => hash / 4294967296f;

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

    private static Color Scale(Color value, float amount) =>
        new(value.R * amount, value.G * amount, value.B * amount);

    private void Put(int x, int y, Color value, float layerDepth, bool glowing = false)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var index = y * Width + x;
        color[index] = value;
        depth[index] = layerDepth;
        emissive[index] = glowing;
    }

    private float DepthAt(int x, int y) => depth[y * Width + x];

    private bool WaterAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && water[y * Width + x];

    /// <summary>Stepped sky gradient with a thin checker between bands, as pixel-art skies are painted.</summary>
    private static Color[] SkyRows(bool night)
    {
        Color[] stops = night
            ? [new("14183A"), new("2C2A56"), new("5E3E6E"), new("C47862")]
            : [new("7AB0D6"), new("A6CDE4"), new("D2E2E6"), new("F2E6C6")];
        var rows = new Color[Height];
        for (var y = 0; y < Height; y++)
            rows[y] = SkyBand(stops, Mathf.Min(1f, (float)y / Horizon), 0);
        return rows;
    }

    private static Color SkyBand(Color[] stops, float position, int offset)
    {
        const int levels = 16;
        var band = Mathf.Min(levels, (int)(position * levels) + offset);
        var t = (float)band / levels * (stops.Length - 1);
        var index = Mathf.Min((int)t, stops.Length - 2);
        return stops[index].Lerp(stops[index + 1], t - index);
    }

    private static Image BuildSky(bool night)
    {
        Color[] stops = night
            ? [new("14183A"), new("2C2A56"), new("5E3E6E"), new("C47862")]
            : [new("7AB0D6"), new("A6CDE4"), new("D2E2E6"), new("F2E6C6")];
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

    private void BuildLand()
    {
        PaintDistantRange();
        PaintMountains();
        PaintForestRidge();
        var hillTop = PaintFields();
        var groundTop = PaintMeadow();
        PaintRiver(hillTop, groundTop);
        PaintTowns(PaintRoad());
        PaintForeground();
    }

    private void PaintDistantRange()
    {
        for (var x = 0; x < Width; x++)
        {
            var top = 58 + 16 * Fbm(x, 3, 70) - 10 * Mathf.Exp(-Mathf.Pow((x - 120) / 40f, 2)) -
                8 * Mathf.Exp(-Mathf.Pow((x - 300) / 30f, 2));
            for (var y = (int)top; y < 104; y++)
            {
                var lit = Fbm(x + y * 0.6f, 8, 14) > 0.48f;
                var value = lit ? Peak : Scale(Peak, 0.9f);
                if (y < top + 4 + 3 * Fbm(x, 9, 6)) value = lit ? Snow : Scale(Snow, 0.9f);
                Put(x, y, value, 0.78f);
            }
        }
    }

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
                Put(x, y, value, 0.32f);
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
                    if (xx < 0 || xx >= Width || yy >= Height || DepthAt(xx, yy) != 0.32f || yy < ridge[xx] + 1) continue;
                    var value = step < snowy ? (litSide ? snow : shadedSnow) : litSide ? rib : shadedRib;
                    Put(xx, yy, value, 0.32f);
                }
            }
        }
    }

    private void PaintForestRidge()
    {
        var top = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            top[x] = (int)(97 + 3 * Mathf.Sin(x * 0.045f + 1.1f) + 2 * Fbm(x, 70, 20));
            for (var y = top[x]; y < 140; y++) Put(x, y, Forest.Lerp(Grass, 0.2f), 0.4f);
        }
        for (var x = 0; x < Width; x += 3 + (int)(PixelArt.Hash(x, 3, 71) % 4))
        {
            var height = 5 + (int)(PixelArt.Hash(x, 1, 71) % 5);
            var broad = PixelArt.Hash(x, 2, 71) % 4 == 0;
            var baseY = top[x] + 1;
            for (var dy = 0; dy < height; dy++)
            {
                var half = broad
                    ? (int)Mathf.Sqrt(Mathf.Max(0, height * height / 4f - (dy - height / 2f) * (dy - height / 2f)))
                    : (dy + 1) / 2;
                for (var dx = -half; dx <= half; dx++)
                    Put(x + dx, baseY - height + dy, dx > 0 ? Scale(Forest, 0.92f) : Forest, 0.4f);
            }
        }
    }

    private int[] PaintFields()
    {
        var top = new int[Width];
        Color[] patches =
        [
            Grass, Grass.Lerp(new Color("C9B25E"), 0.55f), Grass.Lerp(GrassDark, 0.5f),
            Soil.Lerp(Grass, 0.35f), Grass.Lerp(new Color("A7C27A"), 0.4f),
        ];
        var hedge = GrassDark.Lerp(Forest, 0.5f);
        for (var x = 0; x < Width; x++)
        {
            top[x] = (int)(110 + 4 * Mathf.Sin(x * 0.024f + 2.2f) + 3 * Mathf.Sin(x * 0.061f + 0.5f));
            for (var y = top[x]; y < Height; y++)
            {
                var below = y - top[x];
                var band = below / 7;
                var column = (int)((x + band * 23 + below * 1.4f) / 38);
                var kind = (int)(PixelArt.Hash(column, band, 81) % (uint)patches.Length);
                var value = patches[kind];
                if (kind == 1 && below % 2 == 0) value = Scale(value, 0.93f);
                if (kind == 3 && (x + band) % 3 == 0) value = Scale(value, 0.9f);
                var edgeColumn = (int)((x + 1 + band * 23 + below * 1.4f) / 38) != column;
                if ((below % 7 == 0) || edgeColumn) value = hedge;
                Put(x, y, value, 0.26f);
            }
        }
        for (var x = 0; x < Width; x += 5)
        {
            if (PixelArt.Hash(x, 9, 83) % 3 != 0) continue;
            for (var dy = 0; dy < 3; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (Mathf.Abs(dx) + dy < 3)
                        Put(x + dx, top[x] - dy, dx >= 0 ? GrassDark : GrassDark.Lerp(Grass, 0.4f), 0.26f);
        }
        return top;
    }

    private int[] PaintMeadow()
    {
        var top = new int[Width];
        for (var x = 0; x < Width; x++)
        {
            top[x] = (int)(134 + 3 * Mathf.Sin(x * 0.019f + 0.3f) + 2 * Mathf.Sin(x * 0.052f + 1.7f));
            for (var y = top[x]; y < Height; y++)
            {
                var detail = PixelArt.Hash(x, y, 91) % 29;
                var value = detail == 0 ? Grass.Lerp(GrassLight, 0.6f) : detail == 1 ? GrassDark : Grass;
                if (y > 168) value = value.Lerp(GrassDark, 0.45f);
                Put(x, y, value, y < 168 ? 0.1f : 0);
            }
        }
        return top;
    }

    /// <summary>The river comes over the far hill crest and winds, widening, toward the viewer.</summary>
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
        var bank = Sand.Lerp(Grass, 0.35f);
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!WaterAt(x, y)) continue;
                var layer = y < groundTop[x] ? 0.26f : 0.1f;
                var edge = (x + 1 < Width && !WaterAt(x + 1, y)) || (x > 0 && !WaterAt(x - 1, y)) ||
                    (y + 1 < Height && !WaterAt(x, y + 1)) || (y > 0 && !WaterAt(x, y - 1));
                Put(x, y, edge ? Scale(River, 0.84f) : River, layer);
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1) })
                {
                    var bx = x + dx;
                    var by = y + dy;
                    if (bx >= 0 && by >= 0 && bx < Width && by < Height && !WaterAt(bx, by) && by > hillTop[bx])
                        Put(bx, by, bank, by < groundTop[bx] ? 0.26f : 0.1f);
                }
            }
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) =>
        0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);

    private int PaintRoad()
    {
        (int X, int Y)[] road = [(0, 165), (20, 163), (60, 161), (100, 163), (140, 166), (176, 166), (212, 162), (250, 164), (290, 161), (320, 162)];
        var roadY = new float[Width];
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
                Put(x, y - 1, new Color("9A7050"), 0.08f);
                Put(x, y, new Color("8A6244"), 0.08f);
                Put(x, y + 1, new Color("6E4E36"), 0.08f);
                Put(x, y - 4, new Color("6E4E36"), 0.08f);
                if (x % 4 == 0 || x == from || x == to)
                {
                    Put(x, y - 3, new Color("7A5A3A"), 0.08f);
                    Put(x, y - 2, new Color("7A5A3A"), 0.08f);
                }
                continue;
            }
            Put(x, y - 1, new Color("C9B08A"), 0.08f);
            Put(x, y, PixelArt.Hash(x, y, 101) % 11 == 0 ? new Color("A08868") : new Color("BFA27A"), 0.08f);
            Put(x, y + 1, new Color("A68A63"), 0.08f);
        }
        return from;
    }

    private void PaintTowns(int bridgeStart)
    {
        // Left Town: Farmhouse, Warehouse and a House.
        House(28, 158, 18, 10, FarmhouseRoof, thatched: true);
        Warehouse(56, 160, 28, 12);
        House(88, 157, 16, 9, HouseRoof);
        // Right Town: a Blacksmith and three Houses.
        Blacksmith(224, 158, 20, 10);
        House(252, 161, 18, 10, HouseRoof);
        House(278, 153, 14, 8, HouseRoof);
        House(298, 162, 18, 10, HouseRoof);
        foreach (var (x, baseY) in new[] { (50, 163), (118, 164), (214, 162), (292, 162) }) LanternPost(x, baseY);
        Person(14, 164, 0);
        Person(108, 165, 3, lantern: true);
        Person(112, 165, 4, child: true);
        Person(bridgeStart + 6, 164, 1);
        Person(270, 164, 2);
        Person(246, 163, 5, lantern: true);
    }

    private void Walls(int x, int top, int baseY, int width, Color wall, Color wallShade, bool timbered)
    {
        for (var y = top; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                var value = xx >= x + width - 2 ? wallShade : wall;
                if (timbered && (xx == x || xx == x + width - 1 || y == top)) value = Beam;
                Put(xx, y, value, 0.08f);
            }
        if (timbered)
            for (var xx = x; xx < x + width; xx++)
                Put(xx, top + (baseY - top) / 2, Beam, 0.08f);
    }

    private void GableRoof(int x, int top, int width, (Color Lit, Color Shade, Color Edge, Color Ridge) roof, bool thatched)
    {
        var rise = (width + 3) / 2;
        for (var dy = 0; dy < rise; dy++)
        {
            var y = top - rise + dy + 1;
            var half = dy + 1;
            for (var xx = x + width / 2 - half; xx < x + width / 2 + half + width % 2; xx++)
            {
                if (xx < x - 2 || xx > x + width + 1) continue;
                var value = xx < x + width / 2 ? roof.Lit : roof.Shade;
                if (thatched && (xx + y) % 3 == 0) value = Scale(value, 0.88f);
                else if (!thatched && (y - top) % 2 == 0 && PixelArt.Hash(xx, y, 5) % 3 == 0) value = Scale(value, 0.9f);
                Put(xx, y, value, 0.08f);
            }
        }
        for (var xx = x - 2; xx < x + width + 2; xx++) Put(xx, top, roof.Edge, 0.08f);
        Put(x + width / 2, top - rise + 1, roof.Ridge, 0.08f);
    }

    private void ChimneyAt(int x, int roofTop, bool forge)
    {
        for (var y = roofTop + 1; y < roofTop + 5; y++)
        {
            Put(x, y, new Color("8A7F76"), 0.08f);
            Put(x + 1, y, new Color("6E655D"), 0.08f);
        }
        chimneys.Add(new Chimney(new Vector2(x + 0.5f, roofTop), forge));
    }

    private void Window(int x, int baseY, Color? shutters)
    {
        var glass = Night ? new Color("FFC870") : new Color("6E8AA0");
        for (var y = baseY - 7; y < baseY - 4; y++)
        {
            Put(x, y, glass, 0.08f, Night);
            Put(x + 1, y, glass, 0.08f, Night);
            if (shutters is { } shutter)
            {
                Put(x - 1, y, shutter, 0.08f);
                Put(x + 2, y, shutter, 0.08f);
            }
        }
        Put(x, baseY - 7, Night ? new Color("FFE0A0") : new Color("A8C4D8"), 0.08f, Night);
        lights.Add((new Vector2(x + 0.5f, baseY - 6), 6, new Color("FFC46E"), 0.45f, false));
    }

    private void House(int x, int baseY, int width, int wallHeight, (Color Lit, Color Shade, Color Edge, Color Ridge) roof, bool thatched = false)
    {
        var top = baseY - wallHeight;
        Walls(x, top, baseY, width, new Color("E6D6B0"), new Color("C7B38B"), timbered: true);
        GableRoof(x, top, width, roof, thatched);
        ChimneyAt(x + width - 5, top - (width + 3) / 2 + 1, forge: false);
        for (var y = baseY - 6; y < baseY; y++)
        {
            Put(x + 2, y, Beam, 0.08f);
            Put(x + 3, y, new Color("5A3C26"), 0.08f);
        }
        Put(x + 3, baseY - 3, new Color("C9A060"), 0.08f);
        if (x + width - 6 > x + 4) Window(x + width - 6, baseY, Scale(roof.Lit, 0.8f));
    }

    private void Blacksmith(int x, int baseY, int width, int wallHeight)
    {
        var top = baseY - wallHeight;
        for (var y = top; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                var value = xx >= x + width - 2 ? new Color("7D7870") : new Color("9A958C");
                if ((xx + y / 2 * 2) % 5 == 0) value = Scale(value, 0.9f);
                Put(xx, y, value, 0.08f);
            }
        GableRoof(x, top, width, BlacksmithRoof, thatched: false);
        ChimneyAt(x + width - 5, top - (width + 3) / 2 + 1, forge: true);
        // The open forge glows day and night.
        var door = x + width / 2 - 2;
        for (var y = baseY - 7; y < baseY; y++)
            for (var xx = door; xx < door + 5; xx++)
                Put(xx, y, new Color("2B2420"), 0.08f);
        for (var y = baseY - 3; y < baseY; y++)
            for (var xx = door + 1; xx < door + 4; xx++)
                Put(xx, y, (xx + y) % 2 == 0 ? new Color("FFB45A") : new Color("FF8A3C"), 0.08f, glowing: true);
        lights.Add((new Vector2(door + 2.5f, baseY - 2), 9, new Color("FF8C3C"), 0.55f, true));
        Window(x + 2, baseY, null);
    }

    private void Warehouse(int x, int baseY, int width, int wallHeight)
    {
        var top = baseY - wallHeight;
        for (var y = top; y < baseY; y++)
            for (var xx = x; xx < x + width; xx++)
            {
                var value = (xx - x) % 4 == 0 ? new Color("7E5C3E") : new Color("9C7650");
                if (xx >= x + width - 2) value = Scale(value, 0.85f);
                Put(xx, y, value, 0.08f);
            }
        for (var dy = 0; dy < 7; dy++)
        {
            var y = top - 7 + dy + 1;
            var inset = 6 - dy;
            for (var xx = x - 2 + inset; xx < x + width + 2 - inset; xx++)
            {
                var value = xx < x + width / 2 ? WarehouseRoof.Lit : WarehouseRoof.Shade;
                if (dy % 2 == 0 && PixelArt.Hash(xx, y, 6) % 3 == 0) value = Scale(value, 0.9f);
                Put(xx, y, value, 0.08f);
            }
        }
        for (var xx = x - 2; xx < x + width + 2; xx++) Put(xx, top, WarehouseRoof.Edge, 0.08f);
        // Double doors with a cross brace, and crates stacked outside.
        var door = x + width / 2 - 4;
        for (var y = baseY - 9; y < baseY; y++)
            for (var xx = door; xx < door + 8; xx++)
            {
                var column = xx - door;
                var value = column is 0 or 7 || y == baseY - 9 ? new Color("5A3C26") : Beam;
                if (column + (y - baseY) == -1 || 7 - column + (y - baseY) == -1) value = new Color("8E6C47");
                Put(xx, y, value, 0.08f);
            }
        foreach (var (crateX, height) in new[] { (x + width + 1, 3), (x + width + 5, 4), (x - 5, 3) })
            for (var y = baseY - height; y < baseY; y++)
                for (var xx = crateX; xx < crateX + 3; xx++)
                    Put(xx, y, (xx + y) % 3 == 0 ? new Color("7E5C3E") : new Color("A07A4E"), 0.08f);
        lights.Add((new Vector2(x + width / 2, baseY - 5), 10, new Color("FFC46E"), 0.25f, false));
    }

    private void LanternPost(int x, int baseY)
    {
        for (var y = baseY - 9; y < baseY; y++) Put(x, y, Iron, 0.07f);
        Put(x - 1, baseY - 9, Iron, 0.07f);
        Put(x + 1, baseY - 9, Iron, 0.07f);
        Put(x, baseY - 10, Night ? new Color("FFD27A") : new Color("8E8A70"), 0.07f, Night);
        lights.Add((new Vector2(x, baseY - 10), 10, new Color("FFCE78"), 0.5f, false));
    }

    private void Person(int x, int baseY, int look, bool lantern = false, bool child = false)
    {
        var height = child ? 5 : 7;
        var shirt = Shirts[look % Shirts.Length];
        Put(x, baseY - height, Hair[(look + 2) % Hair.Length], 0.06f);
        Put(x + 1, baseY - height, Hair[(look + 2) % Hair.Length], 0.06f);
        Put(x, baseY - height + 1, Skins[look % Skins.Length], 0.06f);
        Put(x + 1, baseY - height + 1, Skins[look % Skins.Length], 0.06f);
        for (var y = baseY - height + 2; y < baseY - 1; y++)
        {
            Put(x, y, shirt, 0.06f);
            Put(x + 1, y, Scale(shirt, 0.8f), 0.06f);
        }
        Put(x, baseY - 1, Iron, 0.06f);
        Put(x + 1, baseY - 1, Iron, 0.06f);
        if (!lantern) return;
        Put(x + 2, baseY - 3, Night ? new Color("FFD27A") : new Color("9A8A60"), 0.06f, Night);
        lights.Add((new Vector2(x + 2, baseY - 3), 7, new Color("FFCE78"), 0.5f, false));
    }

    private void PaintForeground()
    {
        Tree(12, 160, 7, 0.09f);
        foreach (var (x, baseY, radius) in new[] { (10, 179, 6), (22, 180, 4), (150, 181, 5), (304, 180, 7), (290, 181, 4) })
        {
            Bush(x, baseY, radius);
            fireflyHomes.Add(new Vector2(x, baseY - radius - 3));
        }
        Rock(128, 179, 9, 5);
        Rock(262, 180, 7, 4);
        fireflyHomes.Add(new Vector2(70, 172));
        fireflyHomes.Add(new Vector2(236, 174));
        var blade = GrassDark.Lerp(Forest, 0.5f);
        Color[] flowers = [new("F2D86E"), new("F4F0E6"), new("D8604E"), new("A88AD8")];
        for (var x = 0; x < Width; x++)
            for (var y = 170; y < Height; y++)
            {
                var roll = PixelArt.Hash(x, y, 121) % 100;
                if (DepthAt(x, y) != 0) continue;
                if (roll < 7)
                {
                    var length = 2 + (int)(roll % 3);
                    for (var k = 0; k < length; k++) Put(x + (roll % 2 == 1 ? k / 2 : 0), y - k, blade, 0);
                }
                else if (roll == 7)
                    Put(x, y, flowers[PixelArt.Hash(x, y, 3) % 4], 0);
            }
    }

    private void Tree(int x, int baseY, int radius, float layer)
    {
        for (var y = baseY - 4; y <= baseY; y++)
        {
            Put(x, y, Beam, layer);
            Put(x + 1, y, new Color("5A3C26"), layer);
        }
        var centerY = baseY - 4 - radius;
        for (var y = centerY - radius - 1; y <= centerY + radius + 1; y++)
            for (var xx = x - radius - 2; xx <= x + radius + 2; xx++)
            {
                var bump = PixelArt.Hash(xx, y, 141) % 3 * 0.35f;
                var distance = new Vector2((xx - x - 0.5f) / (radius + 1), (y - centerY) / (radius + 0.5f)).Length();
                if (distance > 1 + bump * 0.12f) continue;
                var lit = new Vector2(xx - x + radius * 0.35f, y - centerY + radius * 0.45f).Length() < radius * 0.75f;
                var value = lit ? Grass.Lerp(GrassLight, 0.45f) : GrassDark;
                if (!lit && PixelArt.Hash(xx, y, 143) % 6 == 0) value = Forest;
                Put(xx, y, value, layer);
            }
    }

    private void Bush(int x, int baseY, int radius)
    {
        for (var y = baseY - radius * 2; y <= baseY; y++)
            for (var xx = x - radius - 1; xx <= x + radius + 1; xx++)
            {
                var distance = new Vector2((xx - x) / (radius + 1f), (y - (baseY - radius)) / (float)radius).Length();
                if (distance > 1) continue;
                var value = xx - x > -radius * 0.2f || y > baseY - radius ? Forest : Forest.Lerp(Grass, 0.5f);
                if (PixelArt.Hash(xx, y, 110) % 7 == 0) value = Scale(value, 0.85f);
                Put(xx, y, value, 0);
            }
    }

    private void Rock(int x, int baseY, int width, int height)
    {
        for (var y = baseY - height; y <= baseY; y++)
            for (var xx = x; xx < x + width; xx++)
                if (xx - x + (baseY - y) * 0.6f < width && x + width - xx + (baseY - y) * 0.9f < width + 2)
                    Put(xx, y, xx - x < width / 2f ? new Color("8E8680") : new Color("6E6660"), 0);
    }

    /// <summary>Night grading and distance haze toward the sky color of the same row.</summary>
    private Color Graded(Color value, float layerDepth, int y, Color[] skyRows)
    {
        if (Night)
            value = new Color(value.R * 0.30f + 6 / 255f, value.G * 0.35f + 9 / 255f, value.B * 0.56f + 26 / 255f);
        var haze = layerDepth * (Night ? 0.8f : 0.75f);
        return value.Lerp(skyRows[Mathf.Min(y, Night ? 62 : Horizon - 1)], haze);
    }

    private Image ComposeLand(Color[] skyRows)
    {
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var index = y * Width + x;
                if (depth[index] < 0) continue;
                var value = emissive[index] ? color[index] : Graded(color[index], depth[index], y, skyRows);
                image.SetPixel(x, y, new Color(value, 1));
            }
        return image;
    }

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

    private List<Sparkle> BuildSparkles(Color[] skyRows)
    {
        var sparkles = new List<Sparkle>();
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!WaterAt(x, y) || !WaterAt(x - 1, y) || !WaterAt(x + 1, y) || !WaterAt(x, y - 1) || !WaterAt(x, y + 1)) continue;
                var value = Graded(new Color("A9D2EA"), DepthAt(x, y), y, skyRows);
                sparkles.Add(new Sparkle(new Vector2I(x, y), value, (int)(PixelArt.Hash(x / 2, y, 97) % 24)));
            }
        return sparkles;
    }

    private List<Vector2I> BuildMoonReflection()
    {
        var points = new List<Vector2I>();
        for (var y = 150; y < Height; y += 3)
        {
            var row = Enumerable.Range(0, Width).Where(x => WaterAt(x, y)).ToArray();
            if (row.Length == 0) continue;
            var middle = (row.Min() + row.Max()) / 2f;
            for (var x = (int)(middle - 1); x < (int)(middle + 2 + (y - 150) / 12f); x++)
                if (WaterAt(x, y) && !emissive[y * Width + x]) points.Add(new Vector2I(x, y));
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
