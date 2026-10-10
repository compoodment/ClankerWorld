namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved glide/bob cadence: two walk frames, one quarter second each.</summary>
public static class WalkingMotion
{
    public const double GlideSeconds = 1;
    public const double FrameSeconds = 0.25;

    public static int StepAt(double seconds) => (int)(Math.Floor(seconds / FrameSeconds) % 2) + 1;

    public static int BobAt(double seconds) => StepAt(seconds) == 2 ? -1 : 0;
}
