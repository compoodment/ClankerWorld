using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A one-line label that shortens text too long for its width and ends it
/// with three full stops. Godot's own shortening adds the ellipsis character,
/// which the body font draws at mid-height.
/// </summary>
public partial class FittedLabel : Label
{
    private string fullText = string.Empty;

    public FittedLabel()
    {
        // Trimming without a mark keeps the minimum width small; the text set
        // here always fits, so Godot never needs to trim it.
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimChar;
    }

    /// <summary>The whole text; <see cref="Label.Text"/> holds what fits.</summary>
    public string FullText
    {
        get => fullText;
        set
        {
            fullText = value;
            Fit();
        }
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationResized or (int)NotificationThemeChanged) Fit();
    }

    private void Fit()
    {
        var width = Size.X;
        var font = GetThemeFont("font");
        var size = GetThemeFontSize("font_size");
        Text = width <= 0 || font is null ? fullText : Shorten(fullText, width, text => font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X);
    }

    /// <summary>The longest start of <paramref name="text"/> that fits with "..." after it, or the whole text if it fits.</summary>
    public static string Shorten(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width) return text;
        int low = 0, high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (measure(text[..middle].TrimEnd() + "...") <= width) low = middle;
            else high = middle - 1;
        }
        return text[..low].TrimEnd() + "...";
    }
}
