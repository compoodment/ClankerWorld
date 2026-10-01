using System.Collections.ObjectModel;
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
    int UiScalePercent = DisplayUiScalePolicy.Automatic,
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
/// UI Scale offers named sizes that magnify the whole interface. Small and
/// Large are whole multiples and perfectly crisp; Medium (150%) and Extra
/// large (300% at 4K) fill the gaps, with Medium's pixel letters slightly
/// uneven. Sizes that would leave too little room are not offered.
/// </summary>
public static class DisplayUiScalePolicy
{
    /// <summary>Saved as the UI Scale when the game picks it from the screen size.</summary>
    public const int Automatic = 0;

    /// <summary>The smallest area, in unscaled interface pixels, the menus and panels are laid out for.</summary>
    public const int MinimumWidth = 960;
    public const int MinimumHeight = 540;

    /// <summary>Automatic aims to leave the interface about this many pixels tall.</summary>
    private const float AutomaticHeight = 720;

    private static readonly ReadOnlyCollection<int> SupportedValues = Array.AsReadOnly(new[] { Automatic, 100, 150, 200, 300 });

    public static IReadOnlyList<int> SupportedPercentages => SupportedValues;

    /// <summary>What a size is called in Settings.</summary>
    public static string Name(int percent) => NormalizePercent(percent) switch
    {
        100 => "Small",
        150 => "Medium",
        200 => "Large",
        300 => "Extra large",
        _ => "Automatic",
    };

    /// <summary>
    /// Keeps a supported choice, moves an older step such as 400% to the
    /// nearest size, and treats anything else as Automatic.
    /// </summary>
    public static int NormalizePercent(int percent) =>
        SupportedValues.Contains(percent) ? percent :
        percent is > 100 and <= 400 ? SupportedValues.Skip(1).MinBy(value => Math.Abs(value - percent)) : Automatic;

    /// <summary>
    /// How many screen pixels each interface pixel covers for a choice on a
    /// screen this size. Automatic picks the size that leaves the interface
    /// closest to 720 pixels tall: Small at 720p, Medium at 1080p, Large at
    /// 1440p and Extra large at 4K. Any choice drops to the next smaller size
    /// until the interface keeps at least the minimum area.
    /// </summary>
    public static float FittingFactor(int percent, float width, float height)
    {
        var choice = NormalizePercent(percent);
        var steps = SupportedValues.Skip(1).Select(value => value / 100f).ToArray();
        var factor = choice == Automatic
            ? steps.MinBy(step => Math.Abs(height / step - AutomaticHeight))
            : choice / 100f;
        while (factor > 1 && (width / factor < MinimumWidth || height / factor < MinimumHeight))
            factor = steps.Where(step => step < factor).Max();
        return factor;
    }

    /// <summary>Whether a size has room on a screen this size, so Settings lists it.</summary>
    public static bool Fits(int percent, float width, float height) =>
        percent == Automatic || Math.Abs(FittingFactor(percent, width, height) * 100 - percent) < 0.5f;
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
            var preferences = File.Exists(path)
                ? JsonSerializer.Deserialize<GameDisplayPreferences>(File.ReadAllText(path)) ?? new()
                : new();
            return preferences with { UiScalePercent = DisplayUiScalePolicy.NormalizePercent(preferences.UiScalePercent) };
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences with
            {
                UiScalePercent = DisplayUiScalePolicy.NormalizePercent(preferences.UiScalePercent),
            }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
