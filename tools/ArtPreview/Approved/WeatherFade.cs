using ArtPreview.Proposed.Polish;
using ArtPreview.Proposed.Weather;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Approved;

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
    internal static (float Weather, float Cloud) Amount(string look, double t)
    {
        if (look == "a-today") return (t is >= 1 and < 3.5 ? 1 : 0, 0);
        float Ramp(double start, double length) => Polish.Smooth((float)((t - start) / length));
        if (look == "b-fade") return (Ramp(0.8, 1.2) * (1 - Ramp(3.3, 1.2)), 0);
        // C: the light dims first, then the rain arrives; the rain stops before the light returns.
        var cloud = Ramp(0.5, 0.8) * (1 - Ramp(3.9, 0.8));
        return (Ramp(1.1, 0.9) * (1 - Ramp(3.2, 0.9)), cloud);
    }

    internal static Image Frame(string look, double t)
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

