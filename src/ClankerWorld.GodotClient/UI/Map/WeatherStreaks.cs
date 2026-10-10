using Godot;

namespace ClankerWorld.GodotClient.UI;

internal enum WeatherStreakShape { RainClose, RainMid, Burst, StormClose, StormMid, Cross, Dot }
internal readonly record struct WeatherInkPixel(int X, int Y, Color Color);
internal sealed record WeatherPixelShape(int Width, int Height, IReadOnlyList<WeatherInkPixel> Pixels);
internal readonly record struct WeatherStreakFrame(WeatherStreakShape Shape, int X, int Y, float Opacity);
internal readonly record struct WeatherStreakParticle(float Progress, Vector2 Landing, uint Seed)
{
    public float Threshold => (Seed & 1023) / 1023f;
}

/// <summary>
/// The approved October 8 pixel-streak drawing: shared by the live weather
/// overlay and its review images. Geometry and ink stay exact; the client
/// caches each little shape as one nearest-filtered texture.
/// </summary>
internal static class WeatherStreaks
{
    public const float Cell = 26f;
    private const double LoopSeconds = 3;
    private static readonly Color Rain = new("DDEBFF");
    private static readonly Color Snow = new("FFFFFF");
    private static readonly Color RainTint = new Color("2B4A66") with { A = 0.12f };
    private static readonly Color StormTint = new Color("1B2433") with { A = 0.36f };
    private static readonly Color SnowTint = new Color("E8F0FF") with { A = 0.1f };
    private static readonly WeatherPixelShape[] Shapes =
    [
        RainShape(5), RainShape(3),
        new(3, 2, [new(1, 1, Rain with { A = 0.8f }), new(0, 0, Rain with { A = 0.5f }), new(2, 0, Rain with { A = 0.5f })]),
        StormShape(12), StormShape(8),
        new(3, 3, [new(1, 1, Snow with { A = 0.95f }), new(2, 1, Snow with { A = 0.45f }),
            new(0, 1, Snow with { A = 0.45f }), new(1, 2, Snow with { A = 0.45f }), new(1, 0, Snow with { A = 0.45f })]),
        new(1, 1, [new(0, 0, Snow with { A = 0.95f })]),
    ];

    public static WeatherPixelShape Shape(WeatherStreakShape shape) => Shapes[(int)shape];
    public static Color Tint(char kind) => kind switch { 's' => StormTint, 'r' => RainTint, _ => SnowTint };

    public static WeatherStreakParticle ParticleAt(int cx, int cy, int slot, double time, char kind)
    {
        var seed = PixelArt.Hash(cx, cy, slot * 977 + 7);
        var spread = ((seed >> 10) & 255) / 255.0;
        var period = (kind switch { 's' => 0.4, 'r' => 0.6, _ => 3.0 }) * (1 + spread * 0.5);
        var repeats = Math.Max(1, (int)Math.Round(LoopSeconds / period));
        period = LoopSeconds / repeats;
        var phase = ((seed >> 18) & 255) / 255.0 * period;
        var cycle = (long)Math.Floor((time + phase) / period);
        var progress = (float)((time + phase) / period - cycle);
        var looped = (int)(((cycle % repeats) + repeats) % repeats);
        var land = PixelArt.Hash(cx * 31 + looped, cy * 17 + slot, 48);
        return new(progress, new((cx + (land & 255) / 255f) * Cell, (cy + ((land >> 8) & 255) / 255f) * Cell), seed);
    }

    /// <summary>The approved bands move east through the storm without changing its region.</summary>
    public static float Gust(float x, int size, double time) =>
        0.55f + 0.45f * MathF.Sin((float)(x / (size * 6f) - time / LoopSeconds) * MathF.Tau);

    public static WeatherStreakFrame Frame(char kind, WeatherStreakParticle particle, int size)
    {
        var (px, py) = (particle.Landing.X, particle.Landing.Y);
        var progress = particle.Progress;
        if (kind == 'r')
        {
            const float LandsAt = 0.8f;
            var length = size >= 24 ? 5 : 3;
            return progress < LandsAt
                ? new(size >= 24 ? WeatherStreakShape.RainClose : WeatherStreakShape.RainMid,
                    (int)px, (int)(py - size * 1.4f * (1 - progress / LandsAt)) - length, 1)
                : new(WeatherStreakShape.Burst, (int)px - 1, (int)py - 1, 1 - (progress - LandsAt) / (1 - LandsAt));
        }
        if (kind == 's')
        {
            var length = size >= 24 ? 12 : 8;
            var fall = (int)(size * 2.4f * (1 - progress));
            return new(size >= 24 ? WeatherStreakShape.StormClose : WeatherStreakShape.StormMid,
                (int)px + fall / 2, (int)py - fall - length, 1);
        }
        var drift = Cell * 1.4f * (1 - progress);
        var sway = MathF.Sin((progress * 2 + (particle.Seed >> 26) / 64f) * MathF.Tau) * 1.5f;
        var x = (int)MathF.Round(px - drift * 0.5f + sway);
        var y = (int)MathF.Round(py - drift);
        var opacity = Math.Min(1, Math.Min(progress / 0.12f, (1 - progress) / 0.2f));
        var cross = size >= 24 || (particle.Seed & 3) == 0;
        return new(cross ? WeatherStreakShape.Cross : WeatherStreakShape.Dot, x - (cross ? 1 : 0), y - (cross ? 1 : 0), opacity);
    }

    private static WeatherPixelShape RainShape(int length) => new(1, length,
        Enumerable.Range(0, length).Select(index => new WeatherInkPixel(0, index, Rain with { A = index == 0 ? 0.35f : 0.78f })).ToArray());

    private static WeatherPixelShape StormShape(int length) => new(length / 2 + 1, length,
        Enumerable.Range(0, length).Select(index => new WeatherInkPixel(length / 2 - (index + 1) / 2, index,
            Rain with { A = index < 2 ? 0.3f : 0.62f })).ToArray());
}
