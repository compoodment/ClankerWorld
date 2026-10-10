using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>A horizontal run of the approved smoke pixels, with their original translucent colour.</summary>
public readonly record struct SmokeSpan(Rect2 Area, Color Color);

/// <summary>Smoke B from the October 9 review: nine soft puffs forming a rising, east-leaning column.</summary>
public static class SmokeArt
{
    public const double FrameSeconds = 1.0 / 12;
    private static readonly Dictionary<(BuildingKind Kind, int Width, int Height, int Size, BuildingDoor Door), Vector2?> Sources = [];

    public static bool InUse(BuildingKind? kind, bool occupied, bool working) =>
        kind == BuildingKind.House && occupied || kind == BuildingKind.Blacksmith && working;

    // Weathering changes colours and covers pixels, but does not move the flue or forge.
    /// <summary>The centre of the existing sooty flue or glowing forge pixels in the actual building atlas.</summary>
    public static Vector2? Source(BuildingKind kind, int width, int height, int size, BuildingDoor door)
    {
        var key = (kind, width, height, size, door);
        if (Sources.TryGetValue(key, out var cached)) return cached;
        using var image = BuildingSprites.Render(kind, width, height, size, door);
        float xSum = 0, ySum = 0;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var color = image.GetPixel(x, y);
                var rgb = Key(color);
                if (color.A <= 0 || !(kind == BuildingKind.House && rgb == 0x2A2622 ||
                    kind == BuildingKind.Blacksmith && rgb is 0xE0662A or 0xF5A742 or 0xFFE08A)) continue;
                xSum += x; ySum += y; count++;
            }
        Vector2? source = count == 0 ? null : new Vector2(xSum / count + 0.5f, ySum / count + 0.5f);
        Sources.Add(key, source);
        return source;
    }

    private static int Key(Color color) => (int)MathF.Round(color.R * 255) << 16 |
        (int)MathF.Round(color.G * 255) << 8 | (int)MathF.Round(color.B * 255);

    /// <summary>The approved column unchanged, grouping equal-colour pixels by row to keep drawing cheap.</summary>
    public static void AppendColumn(List<SmokeSpan> output, Vector2 source, int size, double time, int index, Vector2 origin, float scale)
    {
        var unit = size / 32f;
        for (var p = 0; p < 9; p++)
        {
            var age = ((time / 3 + p / 9.0 + index * 0.37) % 1.0) * 3;
            if (age > 2.6) continue;
            var k = (float)(age / 2.6);
            var wobble = MathF.Sin((float)age * 3.1f + p) * 1.2f * unit;
            var centre = source + new Vector2(11 * k * unit + wobble, -26 * k * unit - unit);
            var radius = MathF.Max(0.6f, 3.4f * (0.6f + 0.8f * k) * unit);
            var smooth = Math.Clamp(k * 6, 0, 1);
            var alpha = 0.78f * (1 - k * k) * (smooth * smooth * (3 - 2 * smooth));
            Disc(output, centre + new Vector2(0.6f * unit, 0.6f * unit), radius,
                new Color(0.25f, 0.24f, 0.24f, alpha * 0.35f), origin, scale);
            Disc(output, centre, radius, new Color(0.84f, 0.84f, 0.82f, alpha), origin, scale);
            Disc(output, centre - new Vector2(radius * 0.35f, radius * 0.35f), radius * 0.45f,
                new Color(0.95f, 0.95f, 0.93f, alpha * 0.7f), origin, scale);
        }
    }

    private static void Disc(List<SmokeSpan> output, Vector2 centre, float radius, Color color, Vector2 origin, float scale)
    {
        if (color.A <= 0) return;
        for (var y = (int)MathF.Floor(centre.Y - radius); y <= (int)MathF.Ceiling(centre.Y + radius); y++)
        {
            var first = int.MaxValue;
            var last = int.MinValue;
            for (var x = (int)MathF.Floor(centre.X - radius); x <= (int)MathF.Ceiling(centre.X + radius); x++)
                if ((new Vector2(x + 0.5f, y + 0.5f) - centre).LengthSquared() <= radius * radius)
                {
                    first = Math.Min(first, x);
                    last = x;
                }
            if (first <= last) output.Add(new(new Rect2(origin + new Vector2(first, y) * scale,
                new Vector2(last - first + 1, 1) * scale), color));
        }
    }
}
