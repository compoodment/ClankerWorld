namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved B transition: a smooth 1.2-second change in regional coverage.</summary>
public static class WeatherFade
{
    public const double Seconds = 1.2;
    public const double FrameSeconds = 1.0 / 12;

    public static float Progress(double elapsed)
    {
        var t = (float)Math.Clamp(elapsed / Seconds, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
