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
    int RenderWidth = 1280,
    int RenderHeight = 720,
    bool? AutoRenderResolution = null,
    int UiScalePercent = DisplayUiScalePolicy.Automatic,
    bool? Fullscreen = null,
    string Theme = "light",
    bool CloudHaze = true,
    bool LightningFlashes = true)
{
    // Older settings did not record window mode. Default those installations
    // to fullscreen, while honoring an explicit windowed choice thereafter.
    public bool UsesFullscreen => Fullscreen ?? true;
    // Older settings have no mode flag. Their default 720p value was not a
    // useful indication of the monitor's native resolution, so migrate it to
    // Automatic while preserving explicit non-default render choices.
    public bool UsesAutomaticRenderResolution => AutoRenderResolution ??
        RenderWidth == 1280 && RenderHeight == 720;
}

/// <summary>
/// UI Scale magnifies the whole interface by a whole number, so pixel fonts,
/// frames and icons stay crisp.
/// </summary>
public static class DisplayUiScalePolicy
{
    /// <summary>Saved as the UI Scale when the game picks it from the screen size.</summary>
    public const int Automatic = 0;

    /// <summary>The smallest area, in unscaled interface pixels, the menus and panels are laid out for.</summary>
    public const int MinimumWidth = 960;
    public const int MinimumHeight = 540;

    private static readonly ReadOnlyCollection<int> SupportedValues = Array.AsReadOnly(new[] { Automatic, 100, 200, 300, 400 });

    public static IReadOnlyList<int> SupportedPercentages => SupportedValues;

    /// <summary>
    /// Keeps a supported choice, moves an older in-between step such as 150%
    /// to the nearest whole one, and treats anything else as Automatic.
    /// </summary>
    public static int NormalizePercent(int percent) =>
        SupportedValues.Contains(percent) ? percent :
        percent is > 100 and < 400 ? (int)Math.Round(percent / 100.0, MidpointRounding.AwayFromZero) * 100 : Automatic;

    public static int IndexOfPercent(int percent)
    {
        var normalizedPercent = NormalizePercent(percent);
        for (var index = 0; index < SupportedValues.Count; index++)
            if (SupportedValues[index] == normalizedPercent) return index;
        return 0;
    }

    /// <summary>
    /// The whole-number scale for a choice on a screen this size. Automatic
    /// keeps the interface near 720 pixels tall: 100% on small screens, 200% at
    /// 1080p and 1440p, 300% at 4K. Any choice is lowered until the interface
    /// keeps at least the minimum area.
    /// </summary>
    public static int FittingFactor(int percent, float width, float height)
    {
        var choice = NormalizePercent(percent);
        var factor = choice == Automatic
            ? Math.Max(1, (int)Math.Round(height / 720.0, MidpointRounding.AwayFromZero))
            : choice / 100;
        while (factor > 1 && (width / factor < MinimumWidth || height / factor < MinimumHeight))
            factor--;
        return factor;
    }
}

public readonly record struct DisplayDimensions(int Width, int Height)
{
    public bool IsReasonable => Width is >= 640 and <= 8192 && Height is >= 360 and <= 8192;
}

public static class DisplayResolutionPolicy
{
    private static readonly DisplayDimensions[] StandardRenderSizes =
    [
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
    ];

    public static DisplayDimensions AutomaticRenderSize(
        DisplayDimensions monitor, DisplayDimensions window, bool fullscreen) =>
        fullscreen && monitor.IsReasonable ? monitor :
        window.IsReasonable ? window :
        monitor.IsReasonable ? monitor : new DisplayDimensions(1280, 720);

    public static IReadOnlyList<DisplayDimensions> FixedRenderSizes(
        DisplayDimensions monitor, DisplayDimensions? saved = null)
    {
        var ceiling = monitor.IsReasonable ? monitor : new DisplayDimensions(1920, 1080);
        return StandardRenderSizes
            .Where(size => size.Width <= ceiling.Width && size.Height <= ceiling.Height)
            .Append(ceiling)
            .Concat(saved is { IsReasonable: true } ? [saved.Value] : [])
            .Distinct()
            .OrderBy(size => (long)size.Width * size.Height)
            .ThenBy(size => size.Width)
            .ToArray();
    }
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
