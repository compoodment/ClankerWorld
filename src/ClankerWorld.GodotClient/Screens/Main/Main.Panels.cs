using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>Shared text styling for the Town panel and Event Log.</summary>
public partial class Main
{
    private static Color HeadingText => UiTheme.Current.Section;
    private static Color DimText => UiTheme.Current.InkMuted;
    private static Color WarningText => UiTheme.Current.Warning;
    private static Color LinkText => UiTheme.Current.Link;
    private string? renderedTownPanel;

    private enum TownStyle { Heading, Name, Detail, Body, Note, Warning }

    /// <summary>One styled row of the Town panel.</summary>
    private readonly record struct TownLine(TownStyle Style, string Text);

    /// <summary>
    /// Writes sectioned Town facts with headings and dimmed placeholders
    /// instead of empty gaps. An unchanged panel is left alone so its scroll
    /// position survives observation refreshes.
    /// </summary>
    private void WriteTownPanel(IReadOnlyList<TownLine> lines)
    {
        var signature = string.Join("\n", lines.Select(line => $"{line.Style}|{line.Text}"));
        if (renderedTownPanel == signature && worldDetails.GetParsedText().Length > 0) return;
        renderedTownPanel = signature;
        worldDetails.Clear();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            switch (line.Style)
            {
                case TownStyle.Heading:
                    if (index > 0) worldDetails.Newline();
                    worldDetails.PushFont(UiFonts.Headings, worldDetails.GetThemeFontSize("normal_font_size"));
                    worldDetails.PushColor(HeadingText);
                    worldDetails.AddText(line.Text);
                    worldDetails.Pop();
                    worldDetails.Pop();
                    break;
                case TownStyle.Detail:
                    worldDetails.PushIndent(1);
                    worldDetails.AddText(line.Text);
                    worldDetails.Pop();
                    break;
                case TownStyle.Note:
                    worldDetails.PushColor(DimText);
                    worldDetails.AddText(line.Text);
                    worldDetails.Pop();
                    break;
                case TownStyle.Warning:
                    worldDetails.PushIndent(1);
                    worldDetails.PushColor(WarningText);
                    worldDetails.AddText(line.Text);
                    worldDetails.Pop();
                    worldDetails.Pop();
                    break;
                default:
                    worldDetails.AddText(line.Text);
                    break;
            }
            if (index < lines.Count - 1) worldDetails.Newline();
        }
    }

    /// <summary>Splits a displayed world clock ("date · time") so a day can head its rows.</summary>
    private static (string Date, string Time) SplitClock(string clock)
    {
        var split = clock.LastIndexOf(" · ", StringComparison.Ordinal);
        return split < 0 ? (clock, string.Empty) : (clock[..split], clock[(split + 3)..]);
    }
}
