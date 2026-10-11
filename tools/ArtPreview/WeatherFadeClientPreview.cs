using ArtPreview.Proposed.Polish;
using ArtPreview.Proposed.Weather;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>The client's fade envelope applied to the approved weather-review scene.</summary>
internal static class WeatherFadeClientPreview
{
    internal static Image Frame(double time)
    {
        var clear = new Canvas(Polish.Scene(32, agents: true));
        var amount = WeatherFade.Progress(time - 0.8) * (1 - WeatherFade.Progress(time - 3.3));
        if (amount > 0)
            clear.Mix(new Canvas(WeatherProposal.Frame(WeatherLook.Streaks, "rain", 32, time, flash: false)), amount);
        return clear.ToImage();
    }
}
