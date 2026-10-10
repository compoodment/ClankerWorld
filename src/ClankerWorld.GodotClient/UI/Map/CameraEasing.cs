using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved camera B curve: ease out without an overshoot.</summary>
internal static class CameraEasing
{
    public const float MoveSeconds = 0.7f;
    public const float ZoomSeconds = 0.35f;
    public static float Amount(float progress) => 1 - MathF.Pow(1 - Math.Clamp(progress, 0, 1), 3);

    public static Vector2 Destination(Vector2 from, Vector2 to, int width, bool wraps)
    {
        if (!wraps) return to;
        var difference = (to.X - from.X) % width;
        if (difference > width / 2f) difference -= width;
        if (difference < -width / 2f) difference += width;
        return new(from.X + difference, to.Y);
    }
}
