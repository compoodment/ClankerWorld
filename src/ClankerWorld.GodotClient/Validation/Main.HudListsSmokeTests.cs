using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>Every label on one Agents list card, joined, for checks.</summary>
    private string RosterCardText(int index)
    {
        var rows = rosterCards.GetChild(0).GetChildren().OfType<PanelContainer>().ToArray();
        return index < rows.Length
            ? string.Join(" ", rows[index].FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text))
            : string.Empty;
    }

    /// <summary>
    /// The Event Log has a heading per day, an icon on every row, a Find button
    /// for each located event kept clear of the scrollbar, a dot on the rows
    /// that were new, and it ends above the bottom of the screen.
    /// </summary>
    private void VerifyEventRows()
    {
        var bands = eventRows.GetChildren().OfType<HBoxContainer>().Where(row => row.GetChildCount() > 0 && row.GetChild(0) is Label label && label.ThemeTypeVariation == "SectionLabel").ToArray();
        var rows = eventRows.GetChildren().OfType<HBoxContainer>().Except(bands).ToArray();
        if (bands.Length == 0 || rows.Length == 0 || rows.Any(row => row.GetChildren().OfType<TextureRect>().FirstOrDefault()?.Texture is null))
            throw new InvalidOperationException("Every Event Log row needs a kind icon under a heading for its day.");
        if (!rows.Any(row => row.GetChildren().OfType<Button>().Any(button => ShowsFind(button))))
            throw new InvalidOperationException("Events with a place must have a Find button.");
        if (!rows.Any(row => row.GetChild(0) is ColorRect { Color.A: > 0 }))
            throw new InvalidOperationException("Events that arrived since the log was last opened must keep a dot.");
        if (eventRows.GetParent() is not MarginContainer gap || gap.GetThemeConstant("margin_right") != SettingsScrollGap)
            throw new InvalidOperationException("The Find buttons must keep a gap before the Event Log's scrollbar.");
        if (eventsPanel.GetGlobalRect().End.Y > GetViewport().GetVisibleRect().End.Y + 1)
            throw new InvalidOperationException($"The Event Log must end above the bottom of the screen: {eventsPanel.GetGlobalRect()}.");
    }

    private static bool ShowsFind(Button button) =>
        button.Icon is Texture2D icon && icon.GetWidth() == PixelIcons.Grid;

    /// <summary>The controls list draws keys as keycaps in the current theme's colours, grouped by what they do.</summary>
    private void VerifyControlsKeycaps()
    {
        var texts = controlsPanel.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text).ToArray();
        foreach (var section in new[] { "MAP", "TIME AND AGENTS", "MOUSE", "PANELS" })
            if (!texts.Contains(section))
                throw new InvalidOperationException($"The controls list must group keys under {section}.");
        var caps = controlsPanel.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false).OfType<PanelContainer>()
            .Select(panel => panel.GetThemeStylebox("panel")).OfType<StyleBoxFlat>().Where(style => style.BorderWidthBottom == 3).ToArray();
        if (caps.Length < 10 || caps.Any(style => style.BgColor != UiTheme.Current.Button))
            throw new InvalidOperationException("Keys must be drawn as keycaps in the current theme's button colour.");
    }
}
