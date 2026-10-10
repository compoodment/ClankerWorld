using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved finish-C pixels, shared by the native map and art reference.</summary>
public static class BuildingCompletionArt
{
    public const double Duration = 3;

    /// <summary>Paint horizontal pixel spans in order, retaining overlapping alpha at sparkle centres.</summary>
    public static void Paint(Rect2 rect, int size, double seconds, Action<int, int, int, Color> span)
    {
        var unit = size / 32f;
        var k = (float)Math.Clamp(seconds / 1.4, 0, 1);
        if (k < 1)
            for (var n = 0; n < 14; n++)
            {
                var angle = n / 14f * MathF.Tau;
                var edge = rect.Position + rect.Size / 2 + new Vector2(MathF.Cos(angle) * rect.Size.X * 0.55f, MathF.Sin(angle) * rect.Size.Y * 0.6f);
                var outward = (edge - (rect.Position + rect.Size / 2)).Normalized();
                var centre = edge + outward * 7 * unit * k + new Vector2(0, -3 * unit * MathF.Sin(k * MathF.PI));
                var radius = MathF.Max(0.7f, (1.5f + 2.5f * k) * unit);
                var colour = new Color("C9AC7C") with { A = 0.7f * (1 - k) };
                for (var y = (int)MathF.Floor(centre.Y - radius); y <= (int)MathF.Ceiling(centre.Y + radius); y++)
                {
                    var left = int.MaxValue;
                    var right = int.MinValue;
                    for (var x = (int)MathF.Floor(centre.X - radius); x <= (int)MathF.Ceiling(centre.X + radius); x++)
                        if ((new Vector2(x + 0.5f, y + 0.5f) - centre).LengthSquared() <= radius * radius)
                        {
                            left = Math.Min(left, x);
                            right = x;
                        }
                    if (right >= left) span(left, y, right - left + 1, colour);
                }
            }
        if (seconds > 2.4) return;
        for (var n = 0; n < 5; n++)
        {
            var phase = (float)((seconds * 1.3 + n * 0.21) % 1.0);
            var at = rect.Position + new Vector2(
                PixelArt.Hash(n, 1, 71) % 10_000 / 10_000f * rect.Size.X,
                PixelArt.Hash(n, 2, 71) % 10_000 / 10_000f * rect.Size.Y);
            var colour = new Color("FFF6D8") with { A = MathF.Sin(phase * MathF.PI) };
            var arm = (int)MathF.Max(1, 2 * unit);
            for (var d = -arm; d <= arm; d++)
            {
                span((int)at.X + d, (int)at.Y, 1, colour);
                span((int)at.X, (int)at.Y + d, 1, colour);
            }
        }
    }
}
