using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>Shared text styling for the Event Log and thoughts reader.</summary>
public partial class Main
{
    private static Color HeadingText => UiTheme.Current.Section;
    private static Color DimText => UiTheme.Current.InkMuted;
    private static Color WarningText => UiTheme.Current.Warning;
    private static Color LinkText => UiTheme.Current.Link;

    /// <summary>Splits a displayed world clock ("date · time") so a day can head its rows.</summary>
    private static (string Date, string Time) SplitClock(string clock)
    {
        var split = clock.LastIndexOf(" · ", StringComparison.Ordinal);
        return split < 0 ? (clock, string.Empty) : (clock[..split], clock[(split + 3)..]);
    }
}
