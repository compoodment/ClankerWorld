using ArtPreview;
using ArtPreview.Proposed.Weather;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Polish;

/// <summary>
/// Shared drawing for the October 9 visual-polish review: the reference Town
/// corner without its agents, a float RGB canvas that keeps tints exact, and
/// small helpers for sprites, dots and loops. Each idea below is its own
/// proposal family with options A, B and sometimes C.
/// </summary>
internal static class Polish
{
    public const int FramesPerSecond = 12;
    private static readonly Dictionary<(int, bool, bool, bool), Image> Scenes = [];

    /// <summary>The Town corner at a tile size, optionally without agents or without buildings.</summary>
    public static Image Scene(int size, bool agents = false, bool buildings = true, bool nature = true)
    {
        if (Scenes.TryGetValue((size, agents, buildings, nature), out var cached)) return cached;
        var spec = SceneSpec.TownCorner();
        if (!agents) spec.Agents.Clear();
        if (!buildings) spec.Buildings.Clear();
        if (!nature) spec.Nature.Clear();
        var image = SceneComposer.Render(spec, new ArtSet(), size);
        Scenes[(size, agents, buildings, nature)] = image;
        return image;
    }

    public static IReadOnlyList<SceneBuilding> Buildings { get; } = SceneSpec.TownCorner().Buildings;

    /// <summary>Frames for a loop of the given length.</summary>
    public static IReadOnlyList<Image> Loop(double seconds, Func<double, Image> frame) =>
        Enumerable.Range(0, (int)Math.Round(seconds * FramesPerSecond)).Select(index => frame(index / (double)FramesPerSecond)).ToList();

    public static float Smooth(float t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    public static float Hash01(int x, int y, int salt) => PixelArt.Hash(x, y, salt) % 10_000 / 10_000f;

    /// <summary>The centre of the pixels of one exact colour inside a footprint, or null.</summary>
    public static Vector2? Find(Image scene, Rect2I footprint, int size, params Color[] colours)
    {
        var keys = colours.Select(Key).ToHashSet();
        float sx = 0, sy = 0; var count = 0;
        for (var y = footprint.Position.Y * size; y < footprint.End.Y * size; y++)
            for (var x = footprint.Position.X * size; x < footprint.End.X * size; x++)
                if (keys.Contains(Key(scene.GetPixel(x, y)))) { sx += x; sy += y; count++; }
        return count == 0 ? null : new Vector2(sx / count + 0.5f, sy / count + 0.5f);
    }

    private static int Key(Color c) => (int)MathF.Round(c.R * 255) << 16 | (int)MathF.Round(c.G * 255) << 8 | (int)MathF.Round(c.B * 255);
}

/// <summary>A picture being drawn: float RGB so repeated tints stay exact, opaque throughout.</summary>
internal sealed class Canvas
{
    private readonly float[] rgb;

    public Canvas(Image scene)
    {
        Width = scene.GetWidth();
        Height = scene.GetHeight();
        var bytes = scene.GetData();
        rgb = new float[Width * Height * 3];
        for (var index = 0; index < Width * Height; index++)
            for (var channel = 0; channel < 3; channel++)
                rgb[index * 3 + channel] = bytes[index * 4 + channel] / 255f;
    }

    public int Width { get; }
    public int Height { get; }

    public Color Get(int x, int y)
    {
        var i = (y * Width + x) * 3;
        return new Color(rgb[i], rgb[i + 1], rgb[i + 2]);
    }

    public void Set(int x, int y, Color colour)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = (y * Width + x) * 3;
        rgb[i] = colour.R; rgb[i + 1] = colour.G; rgb[i + 2] = colour.B;
    }

    /// <summary>Paints a colour over a pixel at its alpha.</summary>
    public void Put(int x, int y, Color colour)
    {
        if (colour.A <= 0 || x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = (y * Width + x) * 3;
        rgb[i] += (colour.R - rgb[i]) * colour.A;
        rgb[i + 1] += (colour.G - rgb[i + 1]) * colour.A;
        rgb[i + 2] += (colour.B - rgb[i + 2]) * colour.A;
    }

    public void Fill(int x, int y, int w, int h, Color colour)
    {
        for (var j = y; j < y + h; j++) for (var i = x; i < x + w; i++) Put(i, j, colour);
    }

    public void Disc(Vector2 centre, float radius, Color colour)
    {
        for (var y = (int)MathF.Floor(centre.Y - radius); y <= (int)MathF.Ceiling(centre.Y + radius); y++)
            for (var x = (int)MathF.Floor(centre.X - radius); x <= (int)MathF.Ceiling(centre.X + radius); x++)
                if ((new Vector2(x + 0.5f, y + 0.5f) - centre).LengthSquared() <= radius * radius) Put(x, y, colour);
    }

    /// <summary>Multiplies the whole picture toward a tint by an amount, as the game's night tint does.</summary>
    public void Multiply(Color tint, float amount)
    {
        if (amount <= 0) return;
        var m = new[] { 1 + (tint.R - 1) * amount, 1 + (tint.G - 1) * amount, 1 + (tint.B - 1) * amount };
        for (var i = 0; i < rgb.Length; i++) rgb[i] *= m[i % 3];
    }

    /// <summary>Blends every pixel of another picture of the same size over this one.</summary>
    public void Mix(Canvas other, float amount)
    {
        if (amount <= 0) return;
        for (var i = 0; i < rgb.Length; i++) rgb[i] += (other.rgb[i] - rgb[i]) * amount;
    }

    /// <summary>Draws a sprite scaled to <paramref name="drawn"/> pixels with its top-left corner at a whole pixel.</summary>
    public void Stamp(Image sprite, int x, int y, int drawn)
    {
        var copy = (Image)sprite.Duplicate();
        if (copy.GetWidth() != drawn) copy.Resize(drawn, drawn, Image.Interpolation.Nearest);
        for (var j = 0; j < drawn; j++)
            for (var i = 0; i < drawn; i++)
                Put(x + i, y + j, copy.GetPixel(i, j));
    }

    public Canvas Crop(Rect2I region, int outWidth, int outHeight)
    {
        var image = Image.CreateEmpty(region.Size.X, region.Size.Y, false, Image.Format.Rgba8);
        image.BlitRect(ToImage(), region, Vector2I.Zero);
        if (region.Size.X != outWidth || region.Size.Y != outHeight) image.Resize(outWidth, outHeight, Image.Interpolation.Nearest);
        return new Canvas(image);
    }

    public Image ToImage()
    {
        var bytes = new byte[Width * Height * 4];
        for (var index = 0; index < Width * Height; index++)
        {
            for (var channel = 0; channel < 3; channel++)
                bytes[index * 4 + channel] = (byte)Math.Round(Math.Clamp(rgb[index * 3 + channel], 0, 1) * 255);
            bytes[index * 4 + 3] = 255;
        }
        return Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, bytes);
    }
}

/// <summary>
/// Idea 1, smooth movement. An agent walks four tiles east along the main
/// street and a cow walks four tiles along the meadow, one tile per world
/// update (about one a second), as the host reports them.
/// </summary>
public sealed class MovementProposal : IArtProposal, IAnimatedArtProposal
{
    private const double Seconds = 4;
    public string Family => "movement";

    private static readonly (string Id, string Note)[] Looks =
    [
        ("a-today", "A: today, jumping a tile at each update"),
        ("b-glide", "B: gliding steadily between tiles, walk frames every quarter second"),
        ("c-glide-bob", "C: gliding, with a one-pixel bob on each step"),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var (id, note) in Looks) yield return new(Family, id + "-32", Frame(id, 32, 1.5), note);
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, _) in Looks) yield return ($"{id}-{size}", Polish.Loop(Seconds, t => Frame(id, size, t)));
    }

    private static Image Frame(string look, int size, double t)
    {
        var canvas = new Canvas(Polish.Scene(size));
        var east = AgentSprites.FacingToward(1, 0);
        var step = (int)Math.Floor(t);
        var within = (float)(t - step);
        var walking = look != "a-today" || within < 0.75f;
        var walkFrame = look == "a-today"
            ? (step % 2 == 0 ? AgentFrame.Walk1 : AgentFrame.Walk2)
            : ((int)(t * 4) % 2 == 0 ? AgentFrame.Walk1 : AgentFrame.Walk2);
        var bob = look == "c-glide-bob" && (int)(t * 4) % 2 == 1 ? -1 : 0;

        var drawn = (int)MathF.Round(size * SceneComposer.AgentSpriteScale);
        var agentX = (look == "a-today" ? 8 + step : 8 + step + within) * size + size / 2f;
        var agentY = 6 * size + size / 2f;
        var agent = AgentSprites.Sprite(1, AgentSprites.StageIndex("adult"), east, walking ? walkFrame : AgentFrame.Still, drawn >= 24 ? 32 : 16);
        canvas.Stamp(agent, (int)MathF.Round(agentX - drawn / 2f), (int)MathF.Round(agentY - drawn / 2f) + bob * Math.Max(1, size / 32), drawn);

        var cowX = (look == "a-today" ? 9 + step : 9 + step + within) * size;
        var cowStep = look == "a-today" ? (step % 2 + 1) : ((int)(t * 4) % 2 + 1);
        var cow = AnimalSprites.Sprite("cow", east, young: false, mounted: false, size: size, step: cowStep);
        canvas.Stamp(cow, (int)MathF.Round(cowX), 10 * size + (look == "c-glide-bob" ? bob : 0), size);
        return canvas.ToImage();
    }
}

/// <summary>
/// Idea 3, chimney smoke from buildings someone is using: two Houses and the
/// Blacksmith's forge here, while the other Houses stand empty and smokeless.
/// </summary>
public sealed class SmokeProposal : IArtProposal, IAnimatedArtProposal
{
    private const double Seconds = 3;
    public string Family => "smoke";
    private static readonly Color SootColour = new("2A2622");
    private static readonly Color[] Embers = [new("E0662A"), new("F5A742"), new("FFE08A")];

    private static readonly (string Id, string Note)[] Looks =
    [
        ("a-wisps", "A: thin wisps, three small puffs drifting east and fading"),
        ("b-column", "B: a fuller rising column that leans with the wind"),
        ("c-puffs", "C: one round puff now and then"),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, note) in Looks) yield return new(Family, $"{id}-{size}", Frame(id, size, 1.2), note);
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, _) in Looks) yield return ($"{id}-{size}", Polish.Loop(Seconds, t => Frame(id, size, t)));
    }

    /// <summary>The chimneys of the buildings in use: two Houses by their flue, the Blacksmith by its forge.</summary>
    private static List<Vector2> Sources(int size)
    {
        var scene = Polish.Scene(size);
        var sources = new List<Vector2>();
        foreach (var building in Polish.Buildings)
        {
            var origin = building.Footprint.Position;
            if (building.Kind == BuildingKind.House && origin is { X: 9, Y: 2 } or { X: 13, Y: 7 } &&
                Polish.Find(scene, building.Footprint, size, SootColour) is { } flue)
                sources.Add(flue);
            if (building.Kind == BuildingKind.Blacksmith && Polish.Find(scene, building.Footprint, size, Embers) is { } forge)
                sources.Add(forge);
        }
        return sources;
    }

    private static Image Frame(string look, int size, double t)
    {
        var canvas = new Canvas(Polish.Scene(size));
        var unit = size / 32f;
        var index = 0;
        foreach (var source in Sources(size))
        {
            var (count, life, radius, rise, drift) = look switch
            {
                "b-column" => (9, 2.6, 3.4f, 26f, 11f),
                "c-puffs" => (2, 3.0, 4.2f, 18f, 7f),
                _ => (4, 2.2, 2.6f, 22f, 10f),
            };
            for (var p = 0; p < count; p++)
            {
                // Each puff repeats every loop, offset so the stream is seamless.
                var age = ((t / Seconds + p / (double)count + index * 0.37) % 1.0) * Seconds;
                if (age > life) continue;
                var k = (float)(age / life);
                var wobble = MathF.Sin((float)age * 3.1f + p) * 1.2f * unit;
                var centre = source + new Vector2(drift * k * unit + wobble, -rise * k * unit - unit);
                var r = MathF.Max(0.6f, (radius * (0.6f + 0.8f * k)) * unit);
                var alpha = 0.78f * (1 - k * k) * Polish.Smooth(k * 6);
                canvas.Disc(centre + new Vector2(0.6f * unit, 0.6f * unit), r, new Color(0.25f, 0.24f, 0.24f, alpha * 0.35f));
                canvas.Disc(centre, r, new Color(0.84f, 0.84f, 0.82f, alpha));
                canvas.Disc(centre - new Vector2(r * 0.35f, r * 0.35f), r * 0.45f, new Color(0.95f, 0.95f, 0.93f, alpha * 0.7f));
            }
            index++;
        }
        return canvas.ToImage();
    }
}

/// <summary>
/// Idea 7, weather that leaves a mark: snow lying on roofs in winter,
/// puddles after rain, fallen leaves under trees in autumn and footprints
/// in snow that fade.
/// </summary>
public sealed class WeatherMarksProposal : IArtProposal, IAnimatedArtProposal
{
    public string Family => "weathermarks";
    private static readonly Color Snow = new("E9EEF2");
    private static readonly Color SnowShade = new("C9D3DC");

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
        {
            yield return new(Family, $"roof-snow-a-{size}", RoofSnow(size, blanket: false), "Roof snow A: a dusting, roofs still show through");
            yield return new(Family, $"roof-snow-b-{size}", RoofSnow(size, blanket: true), "Roof snow B: roofs covered, edges and chimneys still dark");
            yield return new(Family, $"ground-snow-a-{size}", SnowyTown(size, patchy: false), "Ground snow A: open ground covered, grass tips and trodden Roads showing");
            yield return new(Family, $"ground-snow-b-{size}", SnowyTown(size, patchy: true), "Ground snow B: patchy cover that thins and melts unevenly");
            yield return new(Family, $"puddles-a-{size}", Puddles(size, wetGround: false), "Puddles A: small puddles on Roads and bare ground");
            yield return new(Family, $"puddles-b-{size}", Puddles(size, wetGround: true), "Puddles B: puddles plus darker, wet-looking ground");
            yield return new(Family, $"roof-snow-a2-{size}", RoofSnowFollowingSlopes(size), "Roof snow A, second round: the shaded slope covered, the sunny slope keeping snow near the ridge, following each roof's shape");
            foreach (var look in PuddleLooks)
                yield return new(Family, $"puddles-r3-{look}-{size}", Puddles(size, look), $"Puddles, third round: {look}");
            yield return new(Family, $"leaves-a-{size}", Leaves(size, carpet: false), "Leaves A: a few fallen leaves under each tree");
            yield return new(Family, $"leaves-b-{size}", Leaves(size, carpet: true), "Leaves B: a carpet of leaves under trees and around them");
        }
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var size in new[] { 32, 16 })
            yield return ($"footprints-{size}", Polish.Loop(5, t => Footprints(size, t)));
        foreach (var look in PuddleLooks)
            yield return ($"puddles-r3-{look}-rain-32", Polish.Loop(2.4, t => Puddles(32, look, t + 0.001)));
    }

    /// <summary>Building pixels: where the scene with buildings differs from the scene without them, inside each footprint.</summary>
    private static bool[] Roofs(int size)
    {
        var with = Polish.Scene(size);
        var without = Polish.Scene(size, buildings: false);
        var mask = new bool[with.GetWidth() * with.GetHeight()];
        foreach (var building in Polish.Buildings)
            for (var y = building.Footprint.Position.Y * size; y < building.Footprint.End.Y * size; y++)
                for (var x = building.Footprint.Position.X * size; x < building.Footprint.End.X * size; x++)
                    if (with.GetPixel(x, y) != without.GetPixel(x, y)) mask[y * with.GetWidth() + x] = true;
        return mask;
    }

    /// <summary>The world's winter: the approved 30% desaturation on everything, standing in for the seasonal palette.</summary>
    private static Canvas Winter(int size)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var c = canvas.Get(x, y);
                var grey = c.R * 0.2126f + c.G * 0.7152f + c.B * 0.0722f;
                canvas.Set(x, y, c.Lerp(new Color(grey, grey, grey), 0.3f));
            }
        return canvas;
    }

    private static Image RoofSnow(int size, bool blanket)
    {
        var canvas = Winter(size);
        var roofs = Roofs(size);
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                if (!roofs[y * canvas.Width + x]) continue;
                var c = canvas.Get(x, y);
                var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                if (luma < 0.2f) continue; // edges, flues and doorways stay dark
                // Snow keeps the roof's own light and shade, so courses and ridges still read.
                var snow = SnowShade.Lerp(Snow, Math.Clamp((luma - 0.2f) * 2.2f, 0, 1));
                if (blanket) canvas.Set(x, y, c.Lerp(snow, 0.78f));
                else if (Upper(x, y, size)) canvas.Set(x, y, c.Lerp(snow, 0.7f));
            }
        return canvas.ToImage();
    }

    /// <summary>The north half of a roof, with a ragged edge two pixels deep: where a light fall stays.</summary>
    private static bool Upper(int x, int y, int size)
    {
        var building = Polish.Buildings.FirstOrDefault(b => b.Footprint.HasPoint(new(x / size, y / size)));
        if (building is null) return false;
        var top = building.Footprint.Position.Y * size;
        var line = top + building.Footprint.Size.Y * size * 0.5f + (Polish.Hash01(x / Math.Max(1, size / 16), 0, 43) - 0.5f) * 4 * size / 32f;
        return y < line;
    }

    private enum RoofShape { GableEastWest, GableNorthSouth, Hipped, Cone }

    /// <summary>The shape each building's roof art draws, read off the art.</summary>
    private static RoofShape ShapeOf(BuildingKind kind) => kind switch
    {
        BuildingKind.Warehouse => RoofShape.GableEastWest,
        BuildingKind.Blacksmith => RoofShape.GableNorthSouth,
        BuildingKind.Silo => RoofShape.Cone,
        _ => RoofShape.Hipped,
    };

    /// <summary>
    /// Which building's roof each pixel belongs to, or -1. Like <see cref="Roofs"/>,
    /// but leaves out the building's shadow on the grass (a pixel that is only a
    /// darker copy of the ground), the Blacksmith's open forge yard and the
    /// Farmhouse's grain sacks.
    /// </summary>
    private static int[] RoofOwners(int size)
    {
        var with = Polish.Scene(size);
        var without = Polish.Scene(size, buildings: false);
        var owners = new int[with.GetWidth() * with.GetHeight()];
        Array.Fill(owners, -1);
        for (var i = 0; i < Polish.Buildings.Count; i++)
        {
            var f = Polish.Buildings[i].Footprint;
            var kind = Polish.Buildings[i].Kind;
            var right = kind == BuildingKind.Blacksmith ? f.Position.X * size + f.Size.X * size * 0.66f : f.End.X * size;
            var bottom = kind == BuildingKind.Farmhouse ? f.Position.Y * size + f.Size.Y * size * 0.68f : f.End.Y * size;
            for (var y = f.Position.Y * size; y < bottom; y++)
                for (var x = f.Position.X * size; x < right; x++)
                {
                    Color w = with.GetPixel(x, y), o = without.GetPixel(x, y);
                    if (w == o) continue;
                    float rr = w.R / MathF.Max(o.R, 0.01f), rg = w.G / MathF.Max(o.G, 0.01f), rb = w.B / MathF.Max(o.B, 0.01f);
                    var shadow = MathF.Max(rr, MathF.Max(rg, rb)) - MathF.Min(rr, MathF.Min(rg, rb)) < 0.1f && rg is > 0.35f and < 0.98f;
                    if (!shadow) owners[y * with.GetWidth() + x] = i;
                }
        }
        return owners;
    }

    /// <summary>
    /// Snow that follows each roof's own slopes. The shaded faces (north and
    /// east, away from the north-west light the art uses) stay covered. Each
    /// sunlit face (south and west) keeps snow from its ridge down to a ragged
    /// melt line that runs along its eave, and below that a thin, broken
    /// dusting, so no face is ever bare. A gable roof has two faces, a hipped
    /// roof four (the nearest eave decides the face), and the Silo's cone
    /// melts on its south-west side.
    /// </summary>
    private static Image RoofSnowFollowingSlopes(int size)
    {
        var canvas = Winter(size);
        var owners = RoofOwners(size);
        var bounds = new (int X0, int Y0, int X1, int Y1)[Polish.Buildings.Count];
        Array.Fill(bounds, (int.MaxValue, int.MaxValue, -1, -1));
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var i = owners[y * canvas.Width + x];
                if (i < 0) continue;
                var b = bounds[i];
                bounds[i] = (Math.Min(b.X0, x), Math.Min(b.Y0, y), Math.Max(b.X1, x), Math.Max(b.Y1, y));
            }
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var i = owners[y * canvas.Width + x];
                if (i < 0) continue;
                var c = canvas.Get(x, y);
                var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                if (luma < 0.2f) continue; // outlines, flues and eave shadow stay dark
                var amount = SlopeCover(x, y, size, ShapeOf(Polish.Buildings[i].Kind), bounds[i], i);
                if (amount <= 0) continue;
                canvas.Set(x, y, c.Lerp(SnowShade.Lerp(Snow, Math.Clamp((luma - 0.2f) * 2.2f, 0, 1)), amount));
            }
        return canvas.ToImage();
    }

    private static float SlopeCover(int x, int y, int size, RoofShape shape, (int X0, int Y0, int X1, int Y1) roof, int seed)
    {
        const float shaded = 0.74f, sunlit = 0.64f, dusting = 0.24f;
        float w = roof.X1 - roof.X0 + 1, h = roof.Y1 - roof.Y0 + 1;
        float px = x + 0.5f - roof.X0, py = y + 0.5f - roof.Y0;
        var grain = Math.Max(1, size / 16);
        // On a sunlit face: snow down to a ragged line about 45% of the way from
        // ridge to eave, then a thin dusting with a few brighter flecks.
        float Sunlit(float fromEave, float depth, float along)
        {
            var line = depth * (0.55f + (Polish.Hash01((int)(along / (2 * grain)), seed, 47) - 0.5f) * 0.18f);
            if (fromEave > line) return sunlit;
            return Polish.Hash01(x / grain, y / grain, 53 + seed) < 0.2f ? sunlit * 0.75f : dusting;
        }
        switch (shape)
        {
            case RoofShape.GableEastWest:
                return py < h / 2 ? shaded : Sunlit(h - py, h / 2, px);
            case RoofShape.GableNorthSouth:
                return px >= w / 2 ? shaded : Sunlit(px, w / 2, py);
            case RoofShape.Cone:
            {
                float dx = px - w / 2, dy = py - h / 2, r = MathF.Sqrt(dx * dx + dy * dy), radius = w / 2;
                if (dx - dy > 0) return shaded;
                return Sunlit(radius - r, radius, MathF.Atan2(dy, dx) * radius);
            }
            default:
            {
                // A hipped roof at 45 degrees: the nearest eave names the face.
                float north = py, south = h - py, west = px, east = w - px;
                var depth = MathF.Min(w, h) / 2;
                var nearest = MathF.Min(MathF.Min(north, south), MathF.Min(west, east));
                if (nearest == north || nearest == east) return shaded;
                return nearest == south ? Sunlit(south, depth, px) : Sunlit(west, depth, py);
            }
        }
    }

    /// <summary>
    /// Puddles, third round: four looks with different moods, after
    /// computment asked for "total different vibes". Each is shaped and
    /// painted its own way; buildings, plants and agents stay on top, dry.
    /// <list type="bullet">
    /// <item><b>mirror</b>: still, glassy pools that mirror the sky: deep blue, a pale band and a cloud.</item>
    /// <item><b>muddy</b>: wide, murky brown pools with a dull milky sheen and mud flecks around them.</item>
    /// <item><b>outlined</b>: chunky puddles drawn like the sprites, with a dark outline, two flat blues and a shine.</item>
    /// <item><b>sheen</b>: the whole Road dark and glistening, with streaks of light and only a few small pools.</item>
    /// </list>
    /// </summary>
    private static readonly Vector2[] Dips = [new(10.6f, 9.4f), new(13.5f, 2.6f)];

    internal static readonly string[] PuddleLooks = ["mirror", "muddy", "outlined", "sheen"];

    private sealed class PuddleField
    {
        public required SceneSpec Spec;
        public required Image WithAgents, Plain, Bare;
        public required int Size, Width, Height;
        public required float Scale;
        public required float[] Water;

        /// <summary>Open ground with nothing drawn over it: no building, plant or agent.</summary>
        public bool Open(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            var tile = new Vector2I(x / Size, y / Size);
            if (!Ground(Spec, tile.X, tile.Y) || Spec.Bridges.ContainsKey(tile)) return false;
            return WithAgents.GetPixel(x, y) == Plain.GetPixel(x, y) && Plain.GetPixel(x, y) == Bare.GetPixel(x, y);
        }

        public bool Dirt(int x, int y)
        {
            if (!Open(x, y)) return false;
            var c = Plain.GetPixel(x, y);
            return Spec.Roads.Contains(new(x / Size, y / Size)) && c.R > c.G + 0.02f;
        }

        public bool Field(int x, int y) => Open(x, y) && Spec.Surface[y / Size * Spec.Width + x / Size] == 7;
        public bool Wet(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Water[y * Width + x] > 0;
        public (float X, float Y) World(int x, int y) => ((x + 0.5f) * Scale, (y + 0.5f) * Scale);

        /// <summary>Removes puddles smaller than <paramref name="pixels"/> at close zoom and returns the rest as pixel lists.</summary>
        public List<List<int>> Blobs(int pixels)
        {
            var seen = new bool[Water.Length];
            var minimum = (int)(pixels / (Scale * Scale));
            var kept = new List<List<int>>();
            for (var start = 0; start < Water.Length; start++)
            {
                if (Water[start] <= 0 || seen[start]) continue;
                var blob = new List<int> { start }; seen[start] = true;
                for (var k = 0; k < blob.Count; k++)
                {
                    int bx = blob[k] % Width, by = blob[k] / Width;
                    foreach (var (nx, ny) in new[] { (bx + 1, by), (bx - 1, by), (bx, by + 1), (bx, by - 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                        var i = ny * Width + nx;
                        if (Water[i] > 0 && !seen[i]) { seen[i] = true; blob.Add(i); }
                    }
                }
                if (blob.Count < minimum) foreach (var i in blob) Water[i] = 0;
                else kept.Add(blob);
            }
            return kept;
        }
    }

    private static PuddleField NewPuddleField(int size)
    {
        var withAgents = Polish.Scene(size, agents: true);
        return new PuddleField
        {
            Spec = SceneSpec.TownCorner(), WithAgents = withAgents, Plain = Polish.Scene(size), Bare = Polish.Scene(size, nature: false),
            Size = size, Width = withAgents.GetWidth(), Height = withAgents.GetHeight(), Scale = 32f / size,
            Water = new float[withAgents.GetWidth() * withAgents.GetHeight()],
        };
    }

    /// <summary>Noise-shaped pools on Roads, fields and the grass dips; <paramref name="smooth"/> rounds them off.</summary>
    private static void NoisePools(PuddleField f, float road, float field, bool smooth)
    {
        for (var y = 0; y < f.Height; y++)
            for (var x = 0; x < f.Width; x++)
            {
                if (!f.Open(x, y)) continue;
                var (wx, wy) = f.World(x, y);
                var n = smooth ? Noise(wx / 13f + 5, wy / 10f + 2) : 0.72f * Noise(wx / 15f, wy / 12f) + 0.28f * Noise(wx / 5f + 17, wy / 5f + 5);
                var dip = Dips.Min(d => (new Vector2(wx, wy) / 32f - d).Length());
                var threshold = f.Dirt(x, y) ? road : f.Field(x, y) ? field : 0.48f + 0.3f * dip;
                if (n > threshold) f.Water[y * f.Width + x] = Math.Clamp((n - threshold) / 0.14f, 0.05f, 1f);
            }
    }

    private static Image Puddles(int size, string look, double t = 0)
    {
        var f = NewPuddleField(size);
        var canvas = new Canvas(f.WithAgents);
        var unit = size / 32f;
        switch (look)
        {
            case "mirror":
            {
                NoisePools(f, 0.6f, 0.65f, smooth: true);
                var blobs = f.Blobs(60);
                var deep = new Color("2E5878"); var shallow = new Color("4F82A6"); var skyBand = new Color("9CCBE6"); var cloud = new Color("EEF6FA");
                for (var y = 0; y < f.Height; y++)
                    for (var x = 0; x < f.Width; x++)
                    {
                        var d = f.Water[y * f.Width + x];
                        if (d <= 0) { if (Near(f, x, y, 1) && f.Open(x, y)) canvas.Set(x, y, canvas.Get(x, y).Lerp(new Color("3B2C1E"), 0.35f)); continue; }
                        var (wx, wy) = f.World(x, y);
                        var colour = shallow.Lerp(deep, d);
                        // The sky mirrored in a soft diagonal band, brightest across the middle.
                        var band = MathF.Abs(((wx * 0.45f + wy) % 22f) - 11f);
                        if (band < 2.2f) colour = colour.Lerp(skyBand, 0.75f);
                        else if (band < 3.4f) colour = colour.Lerp(skyBand, 0.35f);
                        if (!f.Wet(x, y - 1) || !f.Wet(x - 1, y)) colour = new Color("1F3D55"); // the bank's shadow
                        canvas.Set(x, y, colour);
                    }
                // One small cloud mirrored in each big pool.
                foreach (var blob in blobs.Where(b => b.Count > 140 / (f.Scale * f.Scale)))
                {
                    var cx = (int)blob.Average(i => i % f.Width); var cy = (int)blob.Average(i => i / f.Width);
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -3; dx <= 3; dx++)
                        {
                            if (Math.Abs(dx) + Math.Abs(dy) * 2 > 3 || size < 32 && (Math.Abs(dx) > 1 || dy != 0)) continue;
                            if (f.Wet(cx + dx, cy + dy) && f.Wet(cx + dx - 1, cy + dy - 1)) canvas.Set(cx + dx, cy + dy, cloud);
                        }
                }
                break;
            }
            case "muddy":
            {
                // Wide, shallow pools that spread over most of a Road's width.
                NoisePools(f, 0.55f, 0.62f, smooth: false);
                f.Blobs(40);
                var murk = new Color("5A4836"); var silt = new Color("8A7556"); var glint = new Color("E0D2B0");
                for (var y = 0; y < f.Height; y++)
                    for (var x = 0; x < f.Width; x++)
                    {
                        var d = f.Water[y * f.Width + x];
                        var c = canvas.Get(x, y);
                        if (d <= 0)
                        {
                            if (!f.Open(x, y)) continue;
                            if (Near(f, x, y, 1)) canvas.Set(x, y, c.Lerp(new Color("3E2C1B"), 0.45f));
                            else if (Near(f, x, y, 3) && Polish.Hash01(x, y, 71) < 0.12f) canvas.Set(x, y, c.Lerp(new Color("4A3420"), 0.6f)); // mud flecks
                            continue;
                        }
                        var (mx, my) = f.World(x, y);
                        var colour = c.Lerp(silt.Lerp(murk, d), 0.85f);
                        // A dull, milky sheen drifting across the murk.
                        if (Noise(mx / 7f + 13, my / 2.5f + 3) > 0.66f) colour = colour.Lerp(glint, 0.35f);
                        if (!f.Wet(x, y - 1) || !f.Wet(x - 1, y)) colour = new Color("3A2A1A");
                        else if (!f.Wet(x, y + 1) || !f.Wet(x + 1, y)) colour = colour.Lerp(glint, 0.5f);
                        canvas.Set(x, y, colour);
                    }
                break;
            }
            case "outlined":
            {
                // Chunky rounded puddles, one to three per Road tile, plus the dips and fields.
                foreach (var tile in f.Spec.Roads)
                {
                    if (f.Spec.Bridges.ContainsKey(tile) || Polish.Hash01(tile.X, tile.Y, 7) > 0.5f) continue;
                    var count = 1 + (int)(Polish.Hash01(tile.X, tile.Y, 29) * 3);
                    for (var n = 0; n < count; n++)
                    {
                        var centre = new Vector2(tile.X + 0.2f + 0.6f * Polish.Hash01(tile.X * 3 + n, tile.Y, 9), tile.Y + 0.35f + 0.3f * Polish.Hash01(tile.X, tile.Y * 3 + n, 11)) * size;
                        var rx = (3.5f + 4f * Polish.Hash01(tile.X + n, tile.Y, 13)) * unit; var ry = rx * (0.55f + 0.25f * Polish.Hash01(tile.X, tile.Y + n, 17));
                        for (var y = (int)(centre.Y - ry - 1); y <= centre.Y + ry + 1; y++)
                            for (var x = (int)(centre.X - rx - 1); x <= centre.X + rx + 1; x++)
                                if (f.Dirt(x, y) && MathF.Pow((x + 0.5f - centre.X) / rx, 2) + MathF.Pow((y + 0.5f - centre.Y) / ry, 2) <= 1) f.Water[y * f.Width + x] = 1;
                    }
                }
                NoisePools(f, 2f, 0.68f, smooth: true);
                for (var i = 0; i < f.Water.Length; i++) if (f.Water[i] > 0) f.Water[i] = 1;
                var blobs = f.Blobs(14);
                var outline = new Color("26343E"); var fill = new Color("5E93B4"); var light = new Color("8FC0DA"); var shine = new Color("F4FAFC");
                for (var y = 0; y < f.Height; y++)
                    for (var x = 0; x < f.Width; x++)
                    {
                        if (!f.Wet(x, y)) { if (f.Open(x, y) && Near(f, x, y, 1)) canvas.Set(x, y, outline); continue; }
                        canvas.Set(x, y, fill);
                    }
                foreach (var blob in blobs)
                {
                    int x0 = blob.Min(i => i % f.Width), x1 = blob.Max(i => i % f.Width), y0 = blob.Min(i => i / f.Width), y1 = blob.Max(i => i / f.Width);
                    // The lighter upper-left part, then a shine dash.
                    foreach (var i in blob)
                    {
                        int x = i % f.Width, y = i / f.Width;
                        if ((x - x0) / (float)Math.Max(1, x1 - x0) + (y - y0) / (float)Math.Max(1, y1 - y0) < 0.75f && f.Wet(x - 1, y - 1)) canvas.Set(x, y, light);
                    }
                    var sx = x0 + (x1 - x0) / 4 + 1; var sy = y0 + (y1 - y0) / 3;
                    for (var k = 0; k < Math.Max(1, 3 * size / 32); k++) if (f.Wet(sx + k, sy)) canvas.Set(sx + k, sy, shine);
                }
                break;
            }
            default: // sheen
            {
                NoisePools(f, 0.7f, 0.74f, smooth: true);
                f.Blobs(24);
                for (var y = 0; y < f.Height; y++)
                    for (var x = 0; x < f.Width; x++)
                    {
                        var c = canvas.Get(x, y);
                        if (f.Wet(x, y)) { canvas.Set(x, y, new Color("3C5F76").Lerp(new Color("B7D3E0"), f.Wet(x, y - 1) && f.Wet(x - 1, y) ? 0 : 0.6f)); continue; }
                        if (!f.Dirt(x, y)) continue;
                        // Wet, darker, cooler dirt, with short streaks of light along it.
                        var (wx, wy) = f.World(x, y);
                        var wet = c.Lerp(new Color("3A3029"), 0.5f);
                        var streak = Noise(wx / 4f + 50, wy / 1f + 20);
                        if (streak > 0.66f) wet = wet.Lerp(new Color("D6E2E8"), Math.Min(0.6f, (streak - 0.66f) * 4f));
                        canvas.Set(x, y, wet);
                    }
                break;
            }
        }
        if (t > 0) Ripples(canvas, f, t);
        return canvas.ToImage();
    }

    private static bool Near(PuddleField f, int x, int y, int reach)
    {
        for (var dy = -reach; dy <= reach; dy++) for (var dx = -reach; dx <= reach; dx++) if (f.Wet(x + dx, y + dy)) return true;
        return false;
    }

    /// <summary>Rain landing on the puddles: rings that open and fade, scattered in time and place.</summary>
    private static void Ripples(Canvas canvas, PuddleField f, double t)
    {
        var unit = f.Size / 32f;
        var wet = Enumerable.Range(0, f.Water.Length).Where(i => f.Water[i] > 0).ToArray();
        if (wet.Length == 0) return;
        // About one drop for every 60 pixels of water, each landing somewhere new every cycle.
        for (var n = 0; n < wet.Length / 60; n++)
        {
            var cycle = t / 1.2 + Polish.Hash01(n, 1, 81);
            var phase = cycle % 1.0;
            var spot = wet[(int)(Polish.Hash01(n, (int)cycle % 2, 83) * wet.Length)]; // two cycles per 2.4 s loop, so it repeats seamlessly
            int x = spot % f.Width, y = spot / f.Width;
            var r = (float)(1 + phase * 4) * unit;
            var alpha = (float)(1 - phase) * 0.85f;
            for (var a = 0; a < 24; a++)
            {
                var px = (int)MathF.Round(x + MathF.Cos(a * MathF.PI / 12) * r); var py = (int)MathF.Round(y + MathF.Sin(a * MathF.PI / 12) * r * 0.7f);
                if (f.Wet(px, py)) canvas.Put(px, py, new Color("E6F0F4") with { A = alpha });
            }
        }
    }

    private static bool Ground(SceneSpec spec, int x, int y) =>
        spec.Roads.Contains(new(x, y)) || spec.Hydrology[y * spec.Width + x] == 0 && !spec.Buildings.Any(b => b.Footprint.HasPoint(new(x, y)));

    /// <summary>A Town after snowfall: lying snow on open ground and Roads, and snow on roofs.</summary>
    private static Image SnowyTown(int size, bool patchy)
    {
        var canvas = Winter(size);
        GroundSnow(canvas, size, patchy);
        var roofs = Roofs(size);
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                if (!roofs[y * canvas.Width + x]) continue;
                var c = canvas.Get(x, y);
                var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                if (luma < 0.2f) continue;
                canvas.Set(x, y, c.Lerp(SnowShade.Lerp(Snow, Math.Clamp((luma - 0.2f) * 2.2f, 0, 1)), patchy ? 0.45f : 0.6f));
            }
        return canvas.ToImage();
    }

    /// <summary>
    /// Snow lying on the ground: open land turns white while its darker motifs
    /// (grass tufts, stones) still show a little; Roads stay greyer where feet
    /// and carts tread it down. Trees, bushes, people and water are untouched.
    /// Patchy cover thins along a slow noise so it melts unevenly.
    /// </summary>
    private static void GroundSnow(Canvas canvas, int size, bool patchy, bool agents = true)
    {
        var spec = SceneSpec.TownCorner();
        var roofs = Roofs(size);
        var withThings = Polish.Scene(size, agents: agents);
        var withoutAgents = Polish.Scene(size, agents: false, buildings: true, nature: false);
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var tx = x / size; var ty = y / size;
                if (spec.Hydrology[ty * spec.Width + tx] != 0 || spec.Bridges.ContainsKey(new(tx, ty))) continue;
                // Roofs get their own snow; yards, shadows and doorsteps get ground snow.
                if (roofs[y * canvas.Width + x]) continue;
                // Skip anything standing on the ground: trees, plants and agents.
                if (withThings.GetPixel(x, y) != withoutAgents.GetPixel(x, y)) continue;
                var c = canvas.Get(x, y);
                // Within a Road tile only the packed dirt is trodden; its grassy edges snow over like any ground.
                var road = spec.Roads.Contains(new(tx, ty)) && c.R > c.G - 0.02f;
                var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                var amount = road ? 0.72f : 0.9f - Math.Clamp(0.35f - luma, 0, 0.35f) * 0.9f;
                if (patchy)
                {
                    var n = Noise(x / (1.1f * size), y / (1.1f * size));
                    amount *= Polish.Smooth((n - 0.38f) / 0.18f);
                }
                if (amount <= 0) continue;
                var snow = SnowShade.Lerp(Snow, Math.Clamp(luma * 1.8f, 0, 1));
                canvas.Set(x, y, c.Lerp(road ? snow.Lerp(new Color("B7BCBF"), 0.3f) : snow, amount));
            }
    }

    private static float Noise(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float R(int a, int b) => Polish.Hash01(a, b, 59);
        float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        float Lerp(float a, float b, float t) => a + (b - a) * t;
        return Lerp(Lerp(R(ix, iy), R(ix + 1, iy), sx), Lerp(R(ix, iy + 1), R(ix + 1, iy + 1), sx), sy);
    }

    private static Image Puddles(int size, bool wetGround)
    {
        var spec = SceneSpec.TownCorner();
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        if (wetGround) canvas.Multiply(new Color("6F7C80"), 0.3f);
        var unit = size / 32f;
        for (var ty = 0; ty < spec.Height; ty++)
            for (var tx = 0; tx < spec.Width; tx++)
            {
                if (!Ground(spec, tx, ty)) continue;
                var road = spec.Roads.Contains(new(tx, ty));
                if (Polish.Hash01(tx, ty, 7) > (road ? 0.75f : 0.07f)) continue;
                var centre = new Vector2((tx + 0.25f + 0.5f * Polish.Hash01(tx, ty, 9)) * size, (ty + 0.3f + 0.4f * Polish.Hash01(tx, ty, 11)) * size);
                var w = (road ? 5.5f : 4f) * unit * (0.7f + 0.6f * Polish.Hash01(tx, ty, 13));
                var h = w * 0.55f;
                for (var y = (int)(centre.Y - h - 1); y <= (int)(centre.Y + h + 1); y++)
                    for (var x = (int)(centre.X - w - 1); x <= (int)(centre.X + w + 1); x++)
                    {
                        var d = new Vector2((x + 0.5f - centre.X) / w, (y + 0.5f - centre.Y) / h).Length();
                        if (d > 1) continue;
                        canvas.Put(x, y, d > 0.78f ? new Color("4A5A60") with { A = 0.75f } : new Color("5B7684") with { A = 0.85f });
                    }
                // A sky glint on the north-west of the water.
                canvas.Put((int)(centre.X - w * 0.35f), (int)(centre.Y - h * 0.3f), new Color("B7D0DA") with { A = 0.9f });
            }
        return canvas.ToImage();
    }

    private static Image Leaves(int size, bool carpet)
    {
        if (!carpet) return AutumnLeavesReview.Draw(size);
        var spec = SceneSpec.TownCorner();
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        canvas.Multiply(new Color("E8C9A0"), 0.12f); // the approved autumn warmth, standing in for the palette
        var bare = Polish.Scene(size, agents: true, nature: false);
        var full = Polish.Scene(size, agents: true);
        Color[] colours = [new("C9762E"), new("A8552A"), new("D9A23F"), new("8A5A2B")];
        var trees = spec.Nature.Where(item => item.Value is NatureSprite.Broadleaf or NatureSprite.OrchardFruiting or NatureSprite.OrchardPicked)
            .Select(item => item.Key).ToList();
        var dot = Math.Max(1, size / 16);
        foreach (var tree in trees)
        {
            var centre = new Vector2((tree.X + 0.55f) * size, (tree.Y + 0.6f) * size);
            var count = carpet ? 70 : 14;
            var reach = (carpet ? 1.15f : 0.75f) * size;
            for (var n = 0; n < count; n++)
            {
                // Denser near the trunk, thinning outward, and drifting a little east with the wind.
                var angle = Polish.Hash01(tree.X * 97 + n, tree.Y, 17) * MathF.Tau;
                var radius = MathF.Sqrt(Polish.Hash01(tree.X, tree.Y * 97 + n, 19)) * reach;
                var x = (int)(centre.X + MathF.Cos(angle) * radius * 1.15f + size * 0.08f);
                var y = (int)(centre.Y + MathF.Sin(angle) * radius * 0.8f);
                if (x < 0 || y < 0 || x >= canvas.Width || y >= canvas.Height) continue;
                var tx = x / size; var ty = y / size;
                if (!Ground(spec, tx, ty) || spec.Roads.Contains(new(tx, ty))) continue;
                // Leaves lie on the ground, never on top of a tree or bush.
                if (bare.GetPixel(x, y) != full.GetPixel(x, y)) continue;
                canvas.Fill(x, y, dot, dot, colours[(int)(Polish.Hash01(x, y, 23) * colours.Length)] with { A = 0.85f });
            }
        }
        return canvas.ToImage();
    }

    /// <summary>An agent crossing snowy ground leaves prints that fade over a few seconds (a game hour or so).</summary>
    private static Image Footprints(int size, double t)
    {
        // Snow lying on open ground, as the world reports it for a snowy region.
        var canvas = new Canvas(Polish.Scene(size));
        GroundSnow(canvas, size, patchy: false, agents: false);
        var speed = 1.6;
        var x0 = 6 * size + size / 2f;
        var y = 9.4f * size;
        var traveled = (float)(t * speed * size);
        var stride = size * 0.28f;
        var dot = Math.Max(1, size / 16);
        for (var n = 0; n * stride < traveled; n++)
        {
            var age = (traveled - n * stride) / (float)(speed * size);
            var alpha = 0.9f * (1 - Polish.Smooth(age / 3.6f));
            var px = x0 + n * stride;
            var py = y + (n % 2 == 0 ? -2.5f : 2.5f) * size / 32f;
            // A pressed print: a shaded hollow with its lit north-west lip.
            canvas.Fill((int)px, (int)py, dot * 3, dot * 2, new Color("7D8B99") with { A = alpha });
            canvas.Fill((int)px, (int)py, dot * 3, dot, new Color("98A6B3") with { A = alpha * 0.8f });
        }
        var drawn = (int)MathF.Round(size * SceneComposer.AgentSpriteScale);
        var agent = AgentSprites.Sprite(2, AgentSprites.StageIndex("adult"), AgentSprites.FacingToward(1, 0),
            (int)(t * 4) % 2 == 0 ? AgentFrame.Walk1 : AgentFrame.Walk2, drawn >= 24 ? 32 : 16);
        canvas.Stamp(agent, (int)(x0 + traveled - drawn / 2f), (int)(y - drawn * 0.62f), drawn);
        return canvas.ToImage();
    }
}

/// <summary>
/// Idea 8, golden hour: warm light at dawn and dusk on either side of the
/// approved night tint (3C4C6E at 45%), over a whole day that loops.
/// </summary>
public sealed class GoldenHourProposal : IArtProposal, IAnimatedArtProposal
{
    private const double Seconds = 8;
    public string Family => "goldenhour";
    private static readonly Color Night = new("3C4C6E");

    private static readonly (string Id, string Note)[] Looks =
    [
        ("a-amber", "A: an amber wash, up to 22%, at dawn and dusk"),
        ("b-amber-glow", "B: the amber wash, plus warm light catching bright roofs and Roads"),
        ("c-rose-gold", "C: rose at dawn, gold at dusk"),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, note) in Looks) yield return new(Family, $"{id}-dusk-{size}", Frame(id, size, 2.0), note);
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var (id, _) in Looks) yield return ($"{id}-day-32", Polish.Loop(Seconds, t => Frame(id, 32, t)));
    }

    /// <summary>
    /// The loop: day (0–1 s), dusk glow building (1–2.5), into night (2.5–4),
    /// night (4–5), dawn glow (5–6.5) and back to day (6.5–8).
    /// </summary>
    private static (float Night, float Gold, bool Dawn) Light(double t) => t switch
    {
        < 1 => (0, 0, false),
        < 2.5 => (0, Polish.Smooth((float)(t - 1) / 1.5f), false),
        < 4 => (Polish.Smooth((float)(t - 2.5) / 1.5f), 1 - Polish.Smooth((float)(t - 2.5) / 1.5f), false),
        < 5 => (1, 0, true),
        < 6.5 => (1 - Polish.Smooth((float)(t - 5) / 1.5f), Polish.Smooth((float)(t - 5) / 1.5f), true),
        _ => (0, 1 - Polish.Smooth((float)(t - 6.5) / 1.5f), true),
    };

    private static Image Frame(string look, int size, double t)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        var (night, gold, dawn) = Light(t);
        var warm = look == "c-rose-gold" ? (dawn ? new Color("E9A3A0") : new Color("F2B160")) : new Color("F0B066");
        canvas.Multiply(warm, 0.22f * gold);
        if (look == "b-amber-glow" && gold > 0)
            for (var y = 0; y < canvas.Height; y++)
                for (var x = 0; x < canvas.Width; x++)
                {
                    var c = canvas.Get(x, y);
                    var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                    if (luma > 0.5f) canvas.Put(x, y, new Color("FFD9A0") with { A = 0.35f * gold * (luma - 0.5f) * 2 });
                }
        canvas.Multiply(Night, 0.45f * night);
        return canvas.ToImage();
    }
}

/// <summary>
/// Idea 9, stock you can see: log, crate and sack piles beside the
/// Warehouse and between the Farmhouse and Silo, growing with what is stored.
/// </summary>
public sealed class StockProposal : IArtProposal
{
    public string Family => "stock";

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var placement in new[] { "a-at-door", "b-along-wall" })
                foreach (var (level, name) in new[] { (0, "empty"), (1, "some"), (2, "full") })
                    yield return new(Family, $"{placement}-{name}-{size}", Frame(placement, level, size),
                        (placement == "a-at-door" ? "A: piles on the ground beside the building" : "B: stacks against the building's wall") + $", {name}");
    }

    private static Image Frame(string placement, int level, int size)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        if (level == 0) return canvas.ToImage();
        var unit = size / 32f;
        // Warehouse at (6,2) 2×2: wood and crates south of it. Farmhouse (2,7) and Silo (4,8): grain sacks at (3,8).
        if (placement == "a-at-door")
        {
            Logs(canvas, new Vector2(6.1f * size, 4.15f * size), unit, level == 2 ? 5 : 2);
            Crates(canvas, new Vector2(7.05f * size, 4.15f * size), unit, level == 2 ? 4 : 1);
            Sacks(canvas, new Vector2(3.05f * size, 8.1f * size), unit, level == 2 ? 6 : 2);
        }
        else
        {
            Logs(canvas, new Vector2(6.05f * size, 3.98f * size), unit, level == 2 ? 4 : 2, row: true);
            Crates(canvas, new Vector2(7.0f * size, 3.95f * size), unit, level == 2 ? 3 : 1, row: true);
            Sacks(canvas, new Vector2(3.02f * size, 7.85f * size), unit, level == 2 ? 5 : 2, row: true);
        }
        return canvas.ToImage();
    }

    private static void Shadow(Canvas c, float x, float y, float w, float h, float unit) =>
        c.Fill((int)(x + 2 * unit), (int)(y + 2 * unit), (int)MathF.Max(1, w), (int)MathF.Max(1, h), new Color(0.05f, 0.08f, 0.05f, 0.28f));

    private static void Logs(Canvas c, Vector2 at, float unit, int rows, bool row = false)
    {
        var len = 15 * unit; var th = 4 * unit;
        for (var r = 0; r < rows; r++)
        {
            var x = at.X + (row ? 0 : (r % 2) * 3 * unit); var y = at.Y + r * (row ? th * 0.7f : th + unit * 0.5f);
            Shadow(c, x, y, len, th, unit);
            c.Fill((int)x, (int)y, (int)len, (int)MathF.Max(1, th), new Color("6E4E31"));
            c.Fill((int)x, (int)y, (int)len, (int)MathF.Max(1, th * 0.4f), new Color("8A6440"));
            c.Fill((int)(x + len - th), (int)y, (int)MathF.Max(1, th), (int)MathF.Max(1, th), new Color("D2AC77"));
            c.Put((int)(x + len - th / 2), (int)(y + th / 2), new Color("8A6440"));
        }
    }

    private static void Crates(Canvas c, Vector2 at, float unit, int count, bool row = false)
    {
        var s = 9 * unit;
        for (var n = 0; n < count; n++)
        {
            var x = at.X + (row ? n * (s + unit) : (n % 2) * (s + unit));
            var y = at.Y + (row ? 0 : (n / 2) * (s + unit)) - (row ? 0 : 0);
            Shadow(c, x, y, s, s, unit);
            c.Fill((int)x, (int)y, (int)s, (int)s, new Color("3F2A1A"));
            c.Fill((int)(x + unit), (int)(y + unit), (int)MathF.Max(1, s - 2 * unit), (int)MathF.Max(1, s - 2 * unit), new Color("A77C52"));
            c.Fill((int)(x + unit), (int)(y + s / 2), (int)MathF.Max(1, s - 2 * unit), (int)MathF.Max(1, unit), new Color("6E4E31"));
            c.Put((int)(x + unit), (int)(y + unit), new Color("D2AC77"));
        }
    }

    private static void Sacks(Canvas c, Vector2 at, float unit, int count, bool row = false)
    {
        var r = 3.8f * unit;
        for (var n = 0; n < count; n++)
        {
            var centre = at + new Vector2(r + (row ? n * (2 * r + unit * 0.5f) : (n % 2) * (2 * r)), r + (row ? 0 : (n / 2) * (1.6f * r)));
            c.Disc(centre + new Vector2(2 * unit, 2 * unit), r, new Color(0.05f, 0.08f, 0.05f, 0.28f));
            c.Disc(centre, r, new Color("7E6E4A"));
            c.Disc(centre, MathF.Max(0.6f, r - unit), new Color("C8B78C"));
            c.Disc(centre - new Vector2(r * 0.35f, r * 0.35f), MathF.Max(0.5f, r * 0.35f), new Color("E3D6B5"));
            c.Put((int)centre.X, (int)(centre.Y - r + unit), new Color("7E6E4A"));
        }
    }
}

/// <summary>
/// Idea 11, a smoother camera: Find moves the view from the Farmhouse to the
/// east Houses, then the view zooms in one step.
/// </summary>
public sealed class CameraProposal : IAnimatedArtProposal
{
    private const double Seconds = 4;
    public string Family => "camera";

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var look in new[] { "a-today", "b-ease", "c-ease-settle" })
            yield return ($"{look}-32", Polish.Loop(Seconds, t => Frame(look, t)));
    }

    private static float Ease(string look, float k) => look switch
    {
        "a-today" => k > 0 ? 1 : 0,
        "b-ease" => 1 - MathF.Pow(1 - Math.Clamp(k, 0, 1), 3),
        // A slower start and a soft settle that overshoots by about 4%.
        _ => Math.Clamp(k, 0, 1) is var x ? 1 + 2.2f * MathF.Pow(x - 1, 3) + 1.2f * MathF.Pow(x - 1, 2) : 0,
    };

    private static Image Frame(string look, double t)
    {
        const int size = 32, viewW = 320, viewH = 192;
        var scene = new Canvas(Polish.Scene(size, agents: true));
        var from = new Vector2(4.5f, 6.5f) * size;
        var to = new Vector2(14.5f, 7.0f) * size;
        var move = Ease(look, (float)(t - 0.6) / 0.7f);
        var centre = from.Lerp(to, move);
        var zoom = 1f + 0.5f * Ease(look, (float)(t - 2.2) / 0.35f);
        if (t < 0.6) centre = from;
        var w = viewW / zoom; var h = viewH / zoom;
        var x = (int)Math.Clamp(MathF.Round(centre.X - w / 2), 0, scene.Width - w);
        var y = (int)Math.Clamp(MathF.Round(centre.Y - h / 2), 0, scene.Height - h);
        return scene.Crop(new Rect2I(x, y, (int)w, (int)h), viewW, viewH).ToImage();
    }
}

/// <summary>
/// Idea 12, moments: a building finishing and a grave where someone died.
/// </summary>
public sealed class MomentsProposal : IArtProposal, IAnimatedArtProposal
{
    private const double Seconds = 3;
    public string Family => "moments";
    private static readonly Rect2I Finished = new(13, 7, 2, 1);

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
        {
            yield return new(Family, $"finish-a-dust-{size}", Finish("a-dust", size, 0.5), "Finished A: a ring of dust settling");
            yield return new(Family, $"finish-b-flag-{size}", Finish("b-flag", size, 1.2), "Finished B: dust, then a small flag on the roof for a moment");
            yield return new(Family, $"finish-c-sparkle-{size}", Finish("c-sparkle", size, 0.9), "Finished C: dust and a few twinkles");
            yield return new(Family, $"grave-a2-cross-{size}", GraveOnMap(GraveSprites.Cross(), size), "Grave A, second round: a wooden cross with grain, planted in a mound");
            yield return new(Family, $"grave-b2-stone-{size}", GraveOnMap(GraveSprites.Headstone(), size), "Grave B, second round: a carved headstone on a plinth, with a little moss");
            foreach (var (id, note) in new[] { ("grave-a-cross", "Grave A: a small wooden cross"), ("grave-b-stone", "Grave B: a rounded headstone"), ("grave-c-mound", "Grave C: an earth mound with flowers") })
                yield return new(Family, $"{id}-{size}", Grave(id, size), note);
        }
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var look in new[] { "a-dust", "b-flag", "c-sparkle" })
                yield return ($"finish-{look}-{size}", Polish.Loop(Seconds, t => Finish(look, size, t)));
    }

    private static Image Finish(string look, int size, double t)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        var unit = size / 32f;
        var rect = new Rect2(Finished.Position * size, Finished.Size * size);
        var k = (float)Math.Clamp(t / 1.4, 0, 1);
        // Dust puffs rise from the footprint's edges, spread outward and settle.
        if (k < 1)
            for (var n = 0; n < 14; n++)
            {
                var a = n / 14f * MathF.Tau;
                var edge = (rect.Position + rect.Size / 2) + new Vector2(MathF.Cos(a) * rect.Size.X * 0.55f, MathF.Sin(a) * rect.Size.Y * 0.6f);
                var outward = (edge - (rect.Position + rect.Size / 2)).Normalized();
                var centre = edge + outward * 7 * unit * k + new Vector2(0, -3 * unit * MathF.Sin(k * MathF.PI));
                canvas.Disc(centre, MathF.Max(0.7f, (1.5f + 2.5f * k) * unit), new Color("C9AC7C") with { A = 0.7f * (1 - k) });
            }
        var roofTop = new Vector2((rect.Position + rect.Size / 2).X, rect.Position.Y + 4 * unit);
        if (look == "b-flag" && t > 0.4 && t < 2.8)
        {
            var rise = Polish.Smooth((float)(t - 0.4) / 0.4f);
            var fade = 1 - Polish.Smooth((float)(t - 2.3) / 0.5f);
            var pole = (int)MathF.Round(9 * unit * rise);
            canvas.Fill((int)roofTop.X, (int)(roofTop.Y - pole), (int)MathF.Max(1, unit), pole, new Color("3F2A1A") with { A = fade });
            var wave = MathF.Sin((float)t * 9) > 0 ? 0 : 1;
            canvas.Fill((int)(roofTop.X + unit), (int)(roofTop.Y - pole), (int)MathF.Max(1, 5 * unit), (int)MathF.Max(1, 3 * unit) + wave, new Color("D9AE3C") with { A = fade });
            canvas.Fill((int)(roofTop.X + unit), (int)(roofTop.Y - pole), (int)MathF.Max(1, 5 * unit), (int)MathF.Max(1, unit), new Color("FFE28A") with { A = fade });
        }
        if (look == "c-sparkle")
            for (var n = 0; n < 5; n++)
            {
                var phase = (float)((t * 1.3 + n * 0.21) % 1.0);
                if (t > 2.4) continue;
                var at = rect.Position + new Vector2(Polish.Hash01(n, 1, 71) * rect.Size.X, Polish.Hash01(n, 2, 71) * rect.Size.Y);
                var a = MathF.Sin(phase * MathF.PI);
                var arm = (int)MathF.Max(1, 2 * unit);
                for (var d = -arm; d <= arm; d++)
                {
                    canvas.Put((int)at.X + d, (int)at.Y, new Color("FFF6D8") with { A = a });
                    canvas.Put((int)at.X, (int)at.Y + d, new Color("FFF6D8") with { A = a });
                }
            }
        return canvas.ToImage();
    }

    private static Image GraveOnMap(Image sprite, int size)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        canvas.Stamp(sprite, 7 * size, 10 * size, size);
        return canvas.ToImage();
    }

    private static Image Grave(string look, int size)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        var unit = size / 32f * 1.3f;
        var c = new Vector2(7.5f * size, 10.5f * size);
        void R(float x, float y, float w, float h, string hex) =>
            canvas.Fill((int)MathF.Round(c.X + x * unit), (int)MathF.Round(c.Y + y * unit), (int)MathF.Max(1, MathF.Round(w * unit)), (int)MathF.Max(1, MathF.Round(h * unit)), new Color(hex));
        // A low earth mound under each marker.
        canvas.Disc(c + new Vector2(2, 5) * unit, 6 * unit, new Color(0.05f, 0.08f, 0.05f, 0.28f));
        canvas.Disc(c + new Vector2(0, 3) * unit, 6 * unit, new Color("6E5538"));
        canvas.Disc(c + new Vector2(-1, 2) * unit, 4 * unit, new Color("977852"));
        switch (look)
        {
            case "grave-a-cross":
                R(1, -6, 2, 10, "1E1712"); R(-3, -3, 10, 2, "1E1712");
                R(0, -7, 2, 10, "8A6440"); R(-4, -4, 10, 2, "A77C52");
                break;
            case "grave-b-stone":
                R(-3, -6, 8, 9, "4A4E55"); R(-4, -7, 8, 9, "80858E"); R(-4, -7, 8, 1, "9A9FA7"); R(-4, -7, 1, 9, "9A9FA7");
                R(-1, -4, 2, 1, "62666E"); R(-2, -3, 4, 1, "62666E");
                break;
            default:
                foreach (var (x, y, hex) in new[] { (-3, 1, "E8C24A"), (2, 0, "D96A6A"), (0, 4, "E3D6B5"), (4, 3, "E8C24A") })
                {
                    R(x, y, 2, 2, hex);
                    R(x + 1, y + 2, 1, 1, "4A7033");
                }
                break;
        }
        return canvas.ToImage();
    }
}

/// <summary>
/// Idea 13, weather fading in and out: clear, then rain arrives in look B
/// (the approved pixel streaks) and leaves again.
/// </summary>
public sealed class WeatherFadeProposal : IAnimatedArtProposal
{
    private const double Seconds = 5;
    public string Family => "weatherfade";

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var look in new[] { "a-today", "b-fade", "c-clouds-first" })
            yield return ($"{look}-32", Polish.Loop(Seconds, t => Frame(look, t)));
    }

    /// <summary>How much of the weather shows: on from 1 s to 3.5 s.</summary>
    private static (float Weather, float Cloud) Amount(string look, double t)
    {
        if (look == "a-today") return (t is >= 1 and < 3.5 ? 1 : 0, 0);
        float Ramp(double start, double length) => Polish.Smooth((float)((t - start) / length));
        if (look == "b-fade") return (Ramp(0.8, 1.2) * (1 - Ramp(3.3, 1.2)), 0);
        // C: the light dims first, then the rain arrives; the rain stops before the light returns.
        var cloud = Ramp(0.5, 0.8) * (1 - Ramp(3.9, 0.8));
        return (Ramp(1.1, 0.9) * (1 - Ramp(3.2, 0.9)), cloud);
    }

    private static Image Frame(string look, double t)
    {
        var clear = new Canvas(Polish.Scene(32, agents: true));
        var (weather, cloud) = Amount(look, t);
        clear.Multiply(new Color("8A96A8"), 0.28f * cloud);
        if (weather > 0)
        {
            var rain = new Canvas(WeatherProposal.Frame(WeatherLook.Streaks, "rain", 32, t, flash: false));
            clear.Mix(rain, weather);
        }
        return clear.ToImage();
    }
}

/// <summary>
/// The second-round grave sprites, drawn on a 32 px grid like the game's
/// approved art: a dark outline, light from the north-west, shade to the
/// south-east and a soft shadow cast south-east on the ground.
/// </summary>
internal static class GraveSprites
{
    private static readonly Dictionary<char, Color> Palette = new()
    {
        [','] = new Color(0.05f, 0.08f, 0.05f, 0.28f),
        ['O'] = new("1E1712"),
        ['E'] = new("3F2A1A"), ['S'] = new("6E4E31"), ['B'] = new("8A6440"), ['L'] = new("A77C52"), ['H'] = new("D2AC77"),
        ['e'] = new("2B2E33"), ['s'] = new("62666E"), ['b'] = new("80858E"), ['l'] = new("9A9FA7"), ['h'] = new("B9BEC4"), ['i'] = new("4A4E55"),
        ['d'] = new("6E5538"), ['m'] = new("977852"), ['n'] = new("B99A6B"),
        ['g'] = new("4A7033"), ['G'] = new("6E9A48"), ['M'] = new("5B7A3A"),
    };

    private sealed class Grid
    {
        public readonly char[,] Cells = new char[32, 32];
        public Grid() { for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) Cells[x, y] = '.'; }
        public void Rect(int x0, int y0, int x1, int y1, char c) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Set(x, y, c); }
        public void Set(int x, int y, char c) { if (x is >= 0 and < 32 && y is >= 0 and < 32) Cells[x, y] = c; }
        public void Shadow(int x0, int y0, int x1, int y1) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) if (x is >= 0 and < 32 && y is >= 0 and < 32 && Cells[x, y] == '.') Cells[x, y] = ','; }

        /// <summary>A low earth mound: dark rim, mid fill, a lit north-west crown and grass tufts at its foot.</summary>
        public void Mound(int cx, int cy, float rx, float ry)
        {
            for (var y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                for (var x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                {
                    var d = MathF.Sqrt(MathF.Pow((x + 0.5f - cx) / rx, 2) + MathF.Pow((y + 0.5f - cy) / ry, 2));
                    if (d > 1) continue;
                    var lit = (x + 0.5f - cx) / rx + (y + 0.5f - cy) / ry < -0.55f;
                    Set(x, y, d > 0.82f ? 'd' : lit ? 'n' : 'm');
                }
            foreach (var (x, y, c) in new[] { (cx - (int)rx - 1, cy + 1, 'g'), (cx - (int)rx, cy, 'G'), (cx + (int)rx, cy + 1, 'g'), (cx + (int)rx + 1, cy, 'G'), (cx + 2, cy + (int)ry + 1, 'g') })
                Set(x, y, c);
        }

        public Image ToImage()
        {
            var bytes = new byte[32 * 32 * 4];
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var c = Palette.TryGetValue(Cells[x, y], out var colour) ? colour : new Color(0, 0, 0, 0);
                    var i = (y * 32 + x) * 4;
                    bytes[i] = (byte)Math.Round(c.R * 255); bytes[i + 1] = (byte)Math.Round(c.G * 255);
                    bytes[i + 2] = (byte)Math.Round(c.B * 255); bytes[i + 3] = (byte)Math.Round(c.A * 255);
                }
            return Image.CreateFromData(32, 32, false, Image.Format.Rgba8, bytes);
        }
    }

    public static Image Cross()
    {
        var g = new Grid();
        g.Shadow(15, 8, 20, 27); g.Shadow(10, 12, 25, 16);
        g.Rect(13, 5, 18, 26, 'O'); g.Rect(8, 10, 23, 14, 'O');
        g.Rect(14, 6, 17, 25, 'B'); g.Rect(9, 11, 22, 13, 'B');
        // Light on the north and west edges, shade on the south and east.
        g.Rect(14, 6, 17, 6, 'H'); g.Rect(14, 7, 14, 25, 'L'); g.Rect(9, 11, 22, 11, 'L'); g.Rect(9, 11, 9, 13, 'H'); g.Set(14, 6, 'H');
        g.Rect(17, 7, 17, 25, 'S'); g.Rect(10, 13, 22, 13, 'S'); g.Rect(18, 11, 22, 11, 'L');
        // Grain and the joint where the arm crosses the post.
        g.Set(15, 16, 'E'); g.Set(16, 19, 'E'); g.Set(15, 21, 'S'); g.Set(12, 12, 'S'); g.Set(20, 12, 'S'); g.Rect(14, 13, 17, 13, 'E');
        g.Mound(16, 26, 8.5f, 3.2f);
        g.Rect(14, 24, 17, 24, 'S'); // the post entering the mound
        return g.ToImage();
    }

    public static Image Headstone()
    {
        var g = new Grid();
        g.Shadow(12, 9, 24, 27);
        // A plinth, then the stone with a rounded top.
        g.Rect(8, 22, 23, 26, 'O'); g.Rect(9, 23, 22, 25, 's'); g.Rect(9, 23, 22, 23, 'b'); g.Rect(9, 23, 9, 25, 'l'); g.Rect(10, 25, 22, 25, 'e');
        g.Rect(10, 8, 21, 22, 'O'); g.Rect(12, 6, 19, 7, 'O'); g.Set(11, 7, 'O'); g.Set(20, 7, 'O');
        g.Rect(11, 9, 20, 21, 'b'); g.Rect(12, 8, 19, 8, 'b'); g.Rect(13, 7, 18, 7, 'h');
        g.Rect(12, 8, 19, 8, 'h'); g.Rect(11, 9, 11, 21, 'l'); g.Set(11, 9, 'h');
        g.Rect(20, 9, 20, 21, 's'); g.Rect(12, 21, 20, 21, 's');
        // A carved cross and two lines of lettering.
        g.Rect(15, 10, 16, 14, 'i'); g.Rect(13, 11, 18, 12, 'i'); g.Rect(15, 10, 15, 14, 'e');
        g.Rect(13, 16, 18, 16, 'i'); g.Rect(14, 18, 17, 18, 'i');
        // A little moss low on the west side.
        g.Set(11, 19, 'M'); g.Set(11, 20, 'M'); g.Set(12, 20, 'M'); g.Set(9, 24, 'M');
        g.Mound(16, 28, 7.5f, 2.6f);
        return g.ToImage();
    }
}
