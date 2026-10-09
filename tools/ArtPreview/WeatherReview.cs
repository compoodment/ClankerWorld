using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Weather;

/// <summary>The four overlay looks for the weather review (#1325).</summary>
public enum WeatherLook
{
    /// <summary>A: the previous game overlay, redrawn here pixel by pixel from WeatherLayer.</summary>
    Current,
    /// <summary>B: crisp one-pixel streaks on exact pixel slopes, small bursts instead of rings, cross-shaped flakes.</summary>
    Streaks,
    /// <summary>C: almost nothing falls; rain shows as ripples and wet glints, storms add wind lines, snow settles as a dusting.</summary>
    Ripples,
    /// <summary>D: soft drifting cloud shadows mark where the weather is, and drops fall only under them.</summary>
    Shadows,
}

/// <summary>
/// Completed weather review: the previous overlay and
/// three alternatives, each for rain, storm and snow, drawn over the
/// reference Town corner at 32 and 16 px per tile. The weather covers the
/// scene except a clear north-west corner, with the same softened, wandering
/// edge the game uses, so each option also shows how a region ends. Stills
/// go on the review sheet; <see cref="Animate"/> writes a seamless
/// three-second loop of each for the review page.
/// </summary>
public sealed class WeatherProposal : IArtProposal, IAnimatedArtProposal
{
    public const int FramesPerSecond = 12;
    public const double LoopSeconds = 3;
    private const double StillTime = 1.1;
    /// <summary>The animated storms flash once, at this point in the loop.</summary>
    private const double FlashAt = 1.7;
    private static readonly string[] Kinds = ["rain", "storm", "snow"];
    private static readonly Dictionary<int, Image> Scenes = [];
    private static readonly SceneSpec Spec = SceneSpec.TownCorner();

    public string Family => "weather";

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var look in Enum.GetValues<WeatherLook>())
                foreach (var kind in Kinds)
                    yield return new(Family, Id(look, kind, size), Frame(look, kind, size, StillTime, flash: false), Note(look, kind));
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        var count = (int)(LoopSeconds * FramesPerSecond);
        foreach (var size in new[] { 32, 16 })
            foreach (var look in Enum.GetValues<WeatherLook>())
                foreach (var kind in Kinds)
                    yield return (Id(look, kind, size),
                        Enumerable.Range(0, count).Select(frame => Frame(look, kind, size, frame / (double)FramesPerSecond, flash: true)).ToList());
    }

    private static string Id(WeatherLook look, string kind, int size) => $"{(char)('a' + (int)look)}-{look.ToString().ToLowerInvariant()}-{kind}-{size}";

    private static string Note(WeatherLook look, string kind) => (look, kind) switch
    {
        (WeatherLook.Current, _) => "A: previous overlay",
        (WeatherLook.Streaks, "rain") => "B: straight pixel streaks, small bursts",
        (WeatherLook.Streaks, "storm") => "B: stepped slanting streaks in gusts",
        (WeatherLook.Streaks, _) => "B: cross flakes blown by wind",
        (WeatherLook.Ripples, "rain") => "C: ripples and wet glints",
        (WeatherLook.Ripples, "storm") => "C: ripples and wind lines",
        (WeatherLook.Ripples, _) => "C: slow flakes and a dusting",
        (WeatherLook.Shadows, "rain") => "D: rain under cloud shadows",
        (WeatherLook.Shadows, "storm") => "D: storm under dark shadows",
        _ => "D: snow under pale cloud",
    };

    /// <summary>The scene with one look's weather drawn over it at a moment in the loop.</summary>
    public static Image Frame(WeatherLook look, string kind, int size, double time, bool flash)
    {
        if (!Scenes.TryGetValue(size, out var scene))
            Scenes[size] = scene = SceneComposer.Render(Spec, new ArtSet(), size);
        var canvas = new Canvas(scene, size);
        switch (look)
        {
            case WeatherLook.Current: Current.Draw(canvas, kind, time); break;
            case WeatherLook.Streaks: Streaks.Draw(canvas, kind, time); break;
            case WeatherLook.Ripples: Ripples.Draw(canvas, kind, time); break;
            default: Shadows.Draw(canvas, kind, time); break;
        }
        if (kind == "storm" && flash) canvas.Flash(Current.Flash(time - FlashAt));
        canvas.Haze(time);
        return canvas.ToImage();
    }

    /// <summary>
    /// What a picture is drawn on: the scene image, its tile size, where the
    /// weather is, and the small set of drawing calls WeatherLayer uses.
    /// </summary>
    internal sealed class Canvas
    {
        private static readonly Dictionary<int, float[]> Coverages = [];
        private readonly float[] coverage;
        private readonly float[] rgb;

        public Canvas(Image scene, int size)
        {
            Size = size;
            Width = scene.GetWidth();
            Height = scene.GetHeight();
            var bytes = scene.GetData();
            rgb = new float[Width * Height * 3];
            for (var index = 0; index < Width * Height; index++)
                for (var channel = 0; channel < 3; channel++)
                    rgb[index * 3 + channel] = bytes[index * 4 + channel] / 255f;
            if (!Coverages.TryGetValue(size, out coverage!))
                Coverages[size] = coverage = Enumerable.Range(0, Width * Height)
                    .Select(index => CoverageAtTile((index % Width + 0.5f) / size, (index / Width + 0.5f) / size)).ToArray();
        }

        public int Size { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>The finished picture; the scene is opaque, so every pixel is too.</summary>
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

        /// <summary>
        /// How much of a point the weather covers, from 0 to 1: everything but
        /// a clear north-west corner, behind an edge warped by smooth noise
        /// and softened over about two tiles, as the game softens regions.
        /// </summary>
        public static float CoverageAtTile(float tx, float ty)
        {
            var warp = WeatherNoiseCopy.At(tx / 6f, ty / 6f, 11, 0) * 3.2f + WeatherNoiseCopy.At(tx / 3f, ty / 3f, 13, 0) * 1.2f;
            var along = tx * 0.8f + ty * 0.9f + warp - 9f;
            return SmoothStep(0f, 2.6f, along);
        }

        public float CoverageAt(float px, float py)
        {
            var x = Math.Clamp((int)px, 0, Width - 1);
            var y = Math.Clamp((int)py, 0, Height - 1);
            return coverage[y * Width + x];
        }

        public bool IsWater(float px, float py)
        {
            var tx = (int)(px / Size);
            var ty = (int)(py / Size);
            return tx >= 0 && ty >= 0 && tx < Spec.Width && ty < Spec.Height && Spec.Hydrology[ty * Spec.Width + tx] > 0;
        }

        public void Pixel(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || color.A <= 0) return;
            var index = (y * Width + x) * 3;
            var keep = 1 - color.A;
            rgb[index] = rgb[index] * keep + color.R * color.A;
            rgb[index + 1] = rgb[index + 1] * keep + color.G * color.A;
            rgb[index + 2] = rgb[index + 2] * keep + color.B * color.A;
        }

        /// <summary>Godot's DrawRect: a filled rectangle snapped to whole pixels.</summary>
        public void Rect(float x, float y, float width, float height, Color color)
        {
            var left = (int)MathF.Round(x);
            var top = (int)MathF.Round(y);
            for (var py = top; py < top + (int)MathF.Max(1, MathF.Round(height)); py++)
                for (var px = left; px < left + (int)MathF.Max(1, MathF.Round(width)); px++)
                    Pixel(px, py, color);
        }

        /// <summary>Godot's one-pixel DrawLine without antialiasing.</summary>
        public void Line(Vector2 from, Vector2 to, Color color)
        {
            var steps = (int)MathF.Max(MathF.Abs(to.X - from.X), MathF.Abs(to.Y - from.Y));
            var last = new Vector2I(int.MinValue, int.MinValue);
            for (var step = 0; step <= steps; step++)
            {
                var t = steps == 0 ? 0 : step / (float)steps;
                var point = new Vector2I((int)MathF.Floor(from.X + (to.X - from.X) * t), (int)MathF.Floor(from.Y + (to.Y - from.Y) * t));
                if (point == last) continue;
                last = point;
                Pixel(point.X, point.Y, color);
            }
        }

        /// <summary>A wash whose colour and strength each pixel decides, such as a weather tint or a cloud shadow.</summary>
        public void Wash(Func<int, int, Color> color)
        {
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    Pixel(x, y, color(x, y));
        }

        /// <summary>The game's soft storm flash, over the storm's own area only.</summary>
        public void Flash(float strength)
        {
            if (strength <= 0) return;
            Wash((x, y) => new Color(0.86f, 0.9f, 1f, strength * CoverageAt(x, y)));
        }

        /// <summary>
        /// The game's faint cloud haze at full overcast (Clouds B), the same
        /// over every option so only the weather itself differs.
        /// </summary>
        public void Haze(double time)
        {
            var drift = new Vector2((float)(time * 0.22), (float)(time * 0.06));
            Wash((x, y) =>
            {
                var tile = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);
                var texel = (tile - drift) / 2f;
                return new Color(0.96f, 0.98f, 1f, CloudAlpha(texel.X, texel.Y));
            });
        }

        private static float[]? cloud;

        /// <summary>The game's 128-texel repeating cloud pattern, sampled with linear filtering.</summary>
        private static float CloudAlpha(float u, float v)
        {
            const int Texels = 128;
            cloud ??= Enumerable.Range(0, Texels * Texels).Select(index =>
            {
                var x = index % Texels;
                var y = index / Texels;
                var value = 0.5f + WeatherNoiseCopy.At(x / 32f, y / 32f, 7, 4) * 0.6f +
                    WeatherNoiseCopy.At(x / 16f, y / 16f, 8, 8) * 0.28f + WeatherNoiseCopy.At(x / 8f, y / 8f, 9, 16) * 0.12f;
                return SmoothStep(0.56f, 0.8f, value) * 0.15f;
            }).ToArray();
            u -= 0.5f;
            v -= 0.5f;
            var x0 = (int)MathF.Floor(u);
            var y0 = (int)MathF.Floor(v);
            float At(int x, int y) => cloud[(((y % Texels) + Texels) % Texels) * Texels + ((x % Texels) + Texels) % Texels];
            var fx = u - x0;
            var fy = v - y0;
            return Lerp(Lerp(At(x0, y0), At(x0 + 1, y0), fx), Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }
    }

    /// <summary>
    /// Where one falling thing is at a moment: its cycle, how far through it
    /// is and where it lands. Periods are rounded so every particle repeats
    /// exactly within the loop, which makes the animations seamless.
    /// </summary>
    internal readonly record struct Particle(float Progress, Vector2 Landing, uint Seed, uint Land)
    {
        public static Particle At(int cx, int cy, int slot, double time, double period, float cell, int salt = 3)
        {
            var seed = PixelArt.Hash(cx, cy, slot * 977 + salt);
            var repeats = Math.Max(1, (int)Math.Round(LoopSeconds / period));
            period = LoopSeconds / repeats;
            var phase = ((seed >> 18) & 255) / 255.0 * period;
            var cycle = (long)Math.Floor((time + phase) / period);
            var progress = (float)((time + phase) / period - cycle);
            var looped = (int)(((cycle % repeats) + repeats) % repeats);
            var land = PixelArt.Hash(cx * 31 + looped, cy * 17 + slot, 41 + salt);
            return new(progress, new((cx + (land & 255) / 255f) * cell, (cy + ((land >> 8) & 255) / 255f) * cell), seed, land);
        }

        public float Threshold => (Seed & 1023) / 1023f;
        public double Spread => ((Seed >> 10) & 255) / 255.0;
    }

    /// <summary>A: the previous WeatherLayer, redrawn into the picture.</summary>
    internal static class Current
    {
        public const float DropCell = 34f;

        public static void Draw(Canvas canvas, string kind, double time)
        {
            var size = canvas.Size;
            var (alpha, r, g, b) = kind switch
            {
                "storm" => (0.34f, 0.02f, 0.04f, 0.1f),
                "rain" => (0.1f, 0.05f, 0.1f, 0.18f),
                _ => (0.07f, 0.95f, 0.97f, 1f),
            };
            canvas.Wash((x, y) => new Color(r, g, b, alpha * canvas.CoverageAt(x, y)));
            var drop = new Color(0.87f, 0.93f, 1f, 0.8f);
            var streak = new Color(0.8f, 0.87f, 0.97f, 0.62f);
            var flake = new Color(0.97f, 0.98f, 1f, 0.9f);
            var dropLength = Math.Clamp(size * 0.16f, 3f, 8f);
            var dropWidth = size >= 24 ? 2f : 1f;
            var streakLength = Math.Clamp(size * 0.36f, 5f, 14f);
            var ring = Math.Clamp(size * 0.15f, 3f, 6f);
            for (var cy = -1; cy <= canvas.Height / DropCell + 1; cy++)
                for (var cx = -1; cx <= canvas.Width / DropCell + 1; cx++)
                {
                    var here = canvas.CoverageAt((cx + 0.5f) * DropCell, (cy + 0.5f) * DropCell);
                    if (here <= 0) continue;
                    for (var slot = 0; slot < (kind == "storm" ? 2 : 1); slot++)
                    {
                        var period = kind switch { "storm" => 0.45, "rain" => 0.85, _ => 3.0 };
                        var spreadPeriod = kind switch { "storm" => 0.25, "rain" => 0.5, _ => 1.6 };
                        var seed = PixelArt.Hash(cx, cy, slot * 977 + 3);
                        if (here <= (seed & 1023) / 1023f) continue;
                        var particle = Particle.At(cx, cy, slot, time, period + ((seed >> 10) & 255) / 255.0 * spreadPeriod, DropCell);
                        var (px, py) = (particle.Landing.X, particle.Landing.Y);
                        var progress = particle.Progress;
                        switch (kind)
                        {
                            case "storm":
                                var fall = size * 2.2f;
                                var sx = px + fall * 0.4f * (1 - progress);
                                var sy = py - fall * (1 - progress) - streakLength;
                                canvas.Line(new(sx, sy), new(sx - streakLength * 0.4f, sy + streakLength), streak);
                                if (slot == 0 && ((particle.Land >> 16) & 3) == 0 && progress > 0.75f)
                                    Splash(canvas, new(px, py), (progress - 0.75f) / 0.25f, ring, drop);
                                break;
                            case "rain":
                                const float LandsAt = 0.62f;
                                if (progress < LandsAt)
                                {
                                    var left = 1 - progress / LandsAt;
                                    var fade = Math.Min(1, progress / 0.08f);
                                    canvas.Rect(MathF.Round(px + size * 1.6f * 0.12f * left), MathF.Round(py - size * 1.6f * left - dropLength),
                                        dropWidth, dropLength, drop with { A = drop.A * fade });
                                }
                                else Splash(canvas, new(px, py), (progress - LandsAt) / (1 - LandsAt), ring, drop);
                                break;
                            default:
                                var sway = MathF.Sin((progress * 2 + (seed >> 26) / 64f) * MathF.Tau) * Math.Max(2f, size * 0.08f);
                                var fy = py - DropCell * 1.3f * (1 - progress);
                                var fadeIn = Math.Min(1, Math.Min(progress / 0.12f, (1 - progress) / 0.2f));
                                var flakeSize = size >= 12 ? 2f : 1f;
                                canvas.Rect(MathF.Round(px + sway), MathF.Round(fy), flakeSize, flakeSize, flake with { A = flake.A * fadeIn });
                                break;
                        }
                    }
                }
        }

        public static void Splash(Canvas canvas, Vector2 center, float progress, float radius, Color color)
        {
            var r = 1 + progress * radius;
            for (var point = 0; point < 8; point++)
            {
                var angle = point * MathF.Tau / 8;
                canvas.Rect(MathF.Round(center.X + MathF.Cos(angle) * r), MathF.Round(center.Y + MathF.Sin(angle) * r * 0.45f), 1, 1,
                    color with { A = color.A * (1 - progress) });
            }
        }

        /// <summary>The game's flash curve: a soft rise and fade within half a second with one small echo.</summary>
        public static float Flash(double since)
        {
            if (since < 0 || since > 0.5) return 0;
            var main = since < 0.06 ? since / 0.06 : Math.Max(0, 1 - (since - 0.06) / 0.18);
            var echo = since is > 0.2 and < 0.5 ? 0.5 * Math.Sin((since - 0.2) / 0.3 * Math.PI) : 0;
            return (float)(0.3 * Math.Max(main, echo));
        }
    }

    /// <summary>
    /// B: crisp pixel weather. Rain is straight one-pixel streaks, lighter at
    /// the top, that land as a three-pixel burst; storms are longer streaks on
    /// an exact two-down-one-across step, coming in gusts that sweep east;
    /// snow is cross-shaped flakes blown sideways by the wind.
    /// </summary>
    internal static class Streaks
    {
        public static void Draw(Canvas canvas, string kind, double time)
        {
            var weather = kind switch { "storm" => 's', "rain" => 'r', _ => 'n' };
            var tint = WeatherStreaks.Tint(weather);
            canvas.Wash((x, y) => tint with { A = tint.A * canvas.CoverageAt(x, y) });
            var cell = WeatherStreaks.Cell;
            var slots = weather == 's' ? 3 : 2;
            for (var cy = -2; cy <= canvas.Height / cell + 1; cy++)
                for (var cx = -1; cx <= canvas.Width / cell + 2; cx++)
                    for (var slot = 0; slot < slots; slot++)
                    {
                        var particle = WeatherStreaks.ParticleAt(cx, cy, slot, time, weather);
                        var here = canvas.CoverageAt(particle.Landing.X, particle.Landing.Y);
                        if (weather == 's') here *= WeatherStreaks.Gust(particle.Landing.X, canvas.Size, time);
                        if (here <= particle.Threshold) continue;
                        var frame = WeatherStreaks.Frame(weather, particle, canvas.Size);
                        foreach (var pixel in WeatherStreaks.Shape(frame.Shape).Pixels)
                            canvas.Pixel(frame.X + pixel.X, frame.Y + pixel.Y, pixel.Color with { A = pixel.Color.A * frame.Opacity });
                    }
        }
    }

    /// <summary>
    /// C: a calm map where little falls. Rain shows as small ripples that open
    /// and fade, larger and more often on water, and brief wet glints on land;
    /// storms add faint wind lines blowing east; snow is a few slow flakes
    /// over a light dusting that settles where the snow falls.
    /// </summary>
    internal static class Ripples
    {
        private const float Cell = 22f;
        private static readonly Color Ring = new("DCE8F8");
        private static readonly Color Glint = new("F2F8FF");

        public static void Draw(Canvas canvas, string kind, double time)
        {
            var size = canvas.Size;
            var tint = kind switch
            {
                "storm" => new Color("1E2838") with { A = 0.3f },
                "rain" => new Color("27405C") with { A = 0.13f },
                _ => new Color("EEF4FF") with { A = 0.12f },
            };
            canvas.Wash((x, y) => tint with { A = tint.A * canvas.CoverageAt(x, y) });
            if (kind == "snow")
            {
                // Dusting: a fixed sprinkle of pale pixels, thicker toward the snow's centre.
                canvas.Wash((x, y) =>
                {
                    var here = canvas.IsWater(x, y) ? 0 : canvas.CoverageAt(x, y);
                    var hash = PixelArt.Hash(x, y, 57) & 1023;
                    return hash < 26 * here ? new Color("F8FBFF") with { A = 0.5f } : new Color(0, 0, 0, 0);
                });
            }
            for (var cy = -1; cy <= canvas.Height / Cell + 1; cy++)
                for (var cx = -1; cx <= canvas.Width / Cell + 1; cx++)
                    for (var slot = 0; slot < 2; slot++)
                    {
                        var period = kind switch { "storm" => 0.7, "rain" => 1.0, _ => 3.0 };
                        var particle = Particle.At(cx, cy, slot, time, period, Cell, 11);
                        var (px, py) = (particle.Landing.X, particle.Landing.Y);
                        var here = canvas.CoverageAt(px, py);
                        var water = canvas.IsWater(px, py);
                        if (kind == "snow")
                        {
                            if (slot > 0 || here * 0.5f <= particle.Threshold) continue;
                            var fall = Cell * 1.2f * (1 - particle.Progress);
                            var fade = Math.Min(1, Math.Min(particle.Progress / 0.15f, (1 - particle.Progress) / 0.25f));
                            canvas.Rect(MathF.Round(px + MathF.Sin(particle.Progress * MathF.Tau) * 2), MathF.Round(py - fall),
                                size >= 24 ? 2 : 1, size >= 24 ? 2 : 1, new Color("FFFFFF") with { A = 0.85f * fade });
                            continue;
                        }
                        // Water shows every ripple; land shows fewer, as glints.
                        if (here * (water ? 1f : 0.6f) <= particle.Threshold) continue;
                        if (water || slot == 0) Ripple(canvas, px, py, particle.Progress, water ? size * 0.16f : size * 0.09f);
                        else GlintAt(canvas, px, py, particle.Progress);
                    }
            if (kind == "storm") Wind(canvas, time);
        }

        private static void Ripple(Canvas canvas, float px, float py, float progress, float radius)
        {
            var r = 1 + progress * Math.Max(2f, radius);
            var steps = r > 3 ? 12 : 8;
            for (var point = 0; point < steps; point++)
            {
                var angle = point * MathF.Tau / steps;
                canvas.Pixel((int)MathF.Round(px + MathF.Cos(angle) * r), (int)MathF.Round(py + MathF.Sin(angle) * r * 0.5f),
                    Ring with { A = 0.7f * (1 - progress) });
            }
        }

        private static void GlintAt(Canvas canvas, float px, float py, float progress)
        {
            var fade = progress < 0.3f ? progress / 0.3f : Math.Max(0, 1 - (progress - 0.3f) / 0.4f);
            canvas.Pixel((int)px, (int)py, Glint with { A = 0.75f * fade });
            canvas.Pixel((int)px + 1, (int)py, Glint with { A = 0.4f * fade });
        }

        /// <summary>Long faint lines blowing east and a little south, a few at a time.</summary>
        private static void Wind(Canvas canvas, double time)
        {
            const float Lane = 40f;
            for (var lane = 0; lane < canvas.Height / Lane + 1; lane++)
                for (var slot = 0; slot < 2; slot++)
                {
                    var seed = PixelArt.Hash(lane, slot, 91);
                    var repeats = 2 + (int)(seed % 2);
                    var progress = (float)((time / (LoopSeconds / repeats) + (seed >> 8 & 255) / 255.0) % 1);
                    var y = lane * Lane + (seed >> 16 & 31);
                    var length = canvas.Size * 0.7f + (seed >> 21 & 15);
                    var x = -length + progress * (canvas.Width + length * 2);
                    var fade = MathF.Sin(progress * MathF.PI);
                    if (canvas.CoverageAt(x, y) <= 0.3f) continue;
                    canvas.Line(new(x, y), new(x + length, y + length * 0.12f), new Color("E6EEFA") with { A = 0.32f * fade });
                }
        }
    }

    /// <summary>
    /// D: cloud shadows. Soft shadow shapes in three steps of strength drift
    /// east over the weather's area and say from afar where it is; rain falls
    /// only under them as short drops with a one-pixel splash, storms bring
    /// darker, faster shadows with slanting streaks, and snow comes under a
    /// pale veil instead of a shadow.
    /// </summary>
    internal static class Shadows
    {
        private const float Cell = 28f;

        public static void Draw(Canvas canvas, string kind, double time)
        {
            var size = canvas.Size;
            // One pass of the drift per loop: shadows cross a whole noise period in three seconds.
            var speed = kind == "storm" ? 2f : 1f;
            var drift = (float)(time / LoopSeconds) * speed * 4f;
            var (shade, strength) = kind switch
            {
                "storm" => (new Color("161E2C"), 0.4f),
                "rain" => (new Color("1E2A3A"), 0.24f),
                _ => (new Color("EEF4FF"), 0.2f),
            };
            var threshold = kind == "storm" ? 0.38f : 0.48f;
            float Cover(float px, float py)
            {
                var tx = px / size;
                var ty = py / size;
                var value = 0.5f + WeatherNoiseCopy.At(tx / 4f - drift, ty / 4f, 31, 4) * 0.7f +
                    WeatherNoiseCopy.At(tx / 2f - drift * 2, ty / 2f, 37, 8) * 0.25f;
                // Three steps of strength, like the pixel shading elsewhere.
                var step = SmoothStep(threshold, threshold + 0.22f, value);
                return MathF.Round(step * 3) / 3f * canvas.CoverageAt(px, py);
            }
            canvas.Wash((x, y) => shade with { A = strength * Cover(x, y) });
            // A faint base tint so the whole region still reads between shadows.
            canvas.Wash((x, y) => shade with { A = strength * 0.25f * canvas.CoverageAt(x, y) });
            for (var cy = -1; cy <= canvas.Height / Cell + 1; cy++)
                for (var cx = -1; cx <= canvas.Width / Cell + 2; cx++)
                    for (var slot = 0; slot < 3; slot++)
                    {
                        var period = kind switch { "storm" => 0.42, "rain" => 0.7, _ => 3.0 };
                        var particle = Particle.At(cx, cy, slot, time, period, Cell, 19);
                        var (px, py) = (particle.Landing.X, particle.Landing.Y);
                        if (Cover(px, py) * 1.2f <= particle.Threshold) continue;
                        var progress = particle.Progress;
                        switch (kind)
                        {
                            case "rain":
                                const float LandsAt = 0.8f;
                                if (progress < LandsAt)
                                    canvas.Rect(px, py - size * 1.3f * (1 - progress / LandsAt) - 4, size >= 24 ? 2 : 1, size >= 24 ? 4 : 3,
                                        new Color("D6E4F6") with { A = 0.75f });
                                else
                                {
                                    var fade = 1 - (progress - LandsAt) / (1 - LandsAt);
                                    canvas.Pixel((int)px - 1, (int)py, new Color("D6E4F6") with { A = 0.6f * fade });
                                    canvas.Pixel((int)px + 1, (int)py, new Color("D6E4F6") with { A = 0.6f * fade });
                                }
                                break;
                            case "storm":
                                var length = Math.Clamp(size * 0.36f, 5f, 12f);
                                var fall = size * 2.2f * (1 - progress);
                                var sx = px + fall * 0.4f;
                                var sy = py - fall - length;
                                canvas.Line(new(sx, sy), new(sx - length * 0.4f, sy + length), new Color("C8D6EA") with { A = 0.62f });
                                break;
                            default:
                                var drop = Cell * 1.3f * (1 - progress);
                                var fadeIn = Math.Min(1, Math.Min(progress / 0.12f, (1 - progress) / 0.2f));
                                canvas.Rect(px + MathF.Sin(progress * MathF.Tau * 2) * 2, py - drop, size >= 24 ? 2 : 1, size >= 24 ? 2 : 1,
                                    new Color("FFFFFF") with { A = 0.9f * fadeIn });
                                break;
                        }
                    }
        }
    }

    private static float SmoothStep(float from, float to, float value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>
/// A copy of the client's WeatherNoise (in WeatherLayer.cs, which the preview
/// cannot compile because it is a Godot Control), so the region edges and the
/// cloud haze match the game exactly.
/// </summary>
internal static class WeatherNoiseCopy
{
    public static float At(float x, float y, int salt, int periodCells)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = x - x0;
        var ty = y - y0;
        var top = Lerp(Gradient(x0, y0, tx, ty, salt, periodCells), Gradient(x0 + 1, y0, tx - 1, ty, salt, periodCells), Fade(tx));
        var bottom = Lerp(Gradient(x0, y0 + 1, tx, ty - 1, salt, periodCells),
            Gradient(x0 + 1, y0 + 1, tx - 1, ty - 1, salt, periodCells), Fade(tx));
        return Lerp(top, bottom, Fade(ty));
    }

    private static float Gradient(int cx, int cy, float dx, float dy, int salt, int periodCells)
    {
        if (periodCells > 0)
        {
            cx = ((cx % periodCells) + periodCells) % periodCells;
            cy = ((cy % periodCells) + periodCells) % periodCells;
        }
        var angle = PixelArt.Hash(cx, cy, salt) % 1024 / 1024f * MathF.Tau;
        return MathF.Cos(angle) * dx + MathF.Sin(angle) * dy;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
}
