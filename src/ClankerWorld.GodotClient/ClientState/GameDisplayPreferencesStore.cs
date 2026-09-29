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
    int UiScalePercent = 100,
    bool? Fullscreen = null,
    string Theme = "light")
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

public static class DisplayUiScalePolicy
{
    private static readonly ReadOnlyCollection<int> SupportedValues = Array.AsReadOnly(new[] { 100, 125, 150, 175, 200 });

    public static IReadOnlyList<int> SupportedPercentages => SupportedValues;

    public static int NormalizePercent(int percent) => SupportedValues.Contains(percent) ? percent : 100;

    public static int IndexOfPercent(int percent)
    {
        var normalizedPercent = NormalizePercent(percent);
        for (var index = 0; index < SupportedValues.Count; index++)
            if (SupportedValues[index] == normalizedPercent) return index;
        return 0;
    }

    public static float ScaleFactor(int percent) => NormalizePercent(percent) / 100f;
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
