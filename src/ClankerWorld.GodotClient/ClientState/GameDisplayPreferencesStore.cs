using System.Text.Json;

namespace ClankerWorld.GodotClient.ClientState;

/// <summary>
/// Installation-local display choices. These do not alter world time or save data.
/// </summary>
public sealed record GameDisplayPreferences(
    bool UseTwelveHourClock = false,
    string DateFormat = "dmy",
    int WindowWidth = 1280,
    int WindowHeight = 720,
    bool? Fullscreen = null,
    string Theme = "light",
    bool CloudHaze = true,
    bool LightningFlashes = true)
{
    // Older settings did not record window mode. Default those installations
    // to fullscreen, while honoring an explicit windowed choice thereafter.
    public bool UsesFullscreen => Fullscreen ?? true;
}

/// <summary>
/// The interface is magnified by a whole number picked from the screen size,
/// so pixel letters, frames and icons always stay crisp. There is no setting:
/// in-between sizes made the pixel letters uneven.
/// </summary>
public static class DisplayUiScalePolicy
{
    /// <summary>The smallest area, in unscaled interface pixels, the menus and panels are laid out for.</summary>
    public const int MinimumWidth = 960;
    public const int MinimumHeight = 540;

    /// <summary>
    /// The whole-number scale for a screen this size. It keeps the interface
    /// near 720 pixels tall: 100% on small screens, 200% at 1080p and 1440p,
    /// 300% at 4K, and lower whenever the interface would have less than the
    /// minimum area.
    /// </summary>
    public static int FittingFactor(float width, float height)
    {
        var factor = Math.Max(1, (int)Math.Round(height / 720.0, MidpointRounding.AwayFromZero));
        while (factor > 1 && (width / factor < MinimumWidth || height / factor < MinimumHeight))
            factor--;
        return factor;
    }
}

public readonly record struct DisplayDimensions(int Width, int Height)
{
    public bool IsReasonable => Width is >= 640 and <= 8192 && Height is >= 360 and <= 8192;
}

/// <summary>The game always draws at the screen's own resolution.</summary>
public static class DisplayResolutionPolicy
{
    public static DisplayDimensions AutomaticRenderSize(
        DisplayDimensions monitor, DisplayDimensions window, bool fullscreen) =>
        fullscreen && monitor.IsReasonable ? monitor :
        window.IsReasonable ? window :
        monitor.IsReasonable ? monitor : new DisplayDimensions(1280, 720);
}

public sealed class GameDisplayPreferencesStore(string path)
{
    private readonly string path = Path.GetFullPath(path);

    public GameDisplayPreferences Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<GameDisplayPreferences>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(GameDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(path) ??
            throw new InvalidOperationException("The game settings path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
