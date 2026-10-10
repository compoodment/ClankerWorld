using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved rose dawn and gold dusk multiply, shared with the reference renderer.</summary>
public static class GoldenHourTint
{
    public const float MaximumAmount = 0.22f;
    public static readonly Color Rose = new("E9A3A0");
    public static readonly Color Gold = new("F2B160");

    public static Color ColorAt(bool dawn) => dawn ? Rose : Gold;

    /// <summary>Zero at full day/night; strongest halfway through the host's seasonal twilight.</summary>
    public static float StrengthAt(float darkness)
    {
        var dark = Math.Clamp(darkness, 0, 1);
        return 4 * dark * (1 - dark);
    }

    public static Color Multiplier(bool dawn, float strength) =>
        Colors.White.Lerp(ColorAt(dawn), MaximumAmount * Math.Clamp(strength, 0, 1));
}
