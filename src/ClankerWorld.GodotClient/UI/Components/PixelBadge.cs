using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A small round marker drawn pixel by pixel, so it stays a true circle at
/// every UI Scale: a ring and a fill, with optional text such as an unread
/// count. Text wider than the circle stretches it into a pill.
/// </summary>
public partial class PixelBadge : Control
{
    private string text = string.Empty;
    private int diameter = 15;

    public PixelBadge()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resize();
    }

    /// <summary>The circle's size in interface pixels when it holds no wide text.</summary>
    public int Diameter
    {
        get => diameter;
        init
        {
            diameter = Math.Max(3, value);
            Resize();
        }
    }

    /// <summary>Uses the warning colours rather than the alert red.</summary>
    public bool Warning { get; init; }

    public string Text
    {
        get => text;
        set
        {
            if (text == value) return;
            text = value;
            Resize();
            QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged) QueueRedraw();
    }

    private void Resize()
    {
        var width = diameter;
        if (text.Length > 0)
            width = Math.Max(diameter, (int)Mathf.Ceil(UiFonts.Headings.GetStringSize(text, HorizontalAlignment.Left, -1, UiFonts.Body).X) + 8);
        CustomMinimumSize = new Vector2(width, diameter);
        Size = CustomMinimumSize;
    }

    public override void _Draw()
    {
        var palette = UiTheme.Current;
        var fill = Warning ? palette.Ember : palette.Seal;
        var ring = Warning ? palette.ButtonEdge : palette.SealRing;
        var width = (int)CustomMinimumSize.X;
        for (var y = 0; y < diameter; y++)
            for (var x = 0; x < width; x++)
            {
                if (!Inside(x, y, width)) continue;
                var edge = !Inside(x - 1, y, width) || !Inside(x + 1, y, width) ||
                    !Inside(x, y - 1, width) || !Inside(x, y + 1, width);
                DrawRect(new Rect2(x, y, 1, 1), edge ? ring : fill);
            }
        if (text.Length == 0) return;
        // Counts use the Timber lettering, like other headings and labels.
        var font = UiFonts.Headings;
        var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, UiFonts.Body);
        var baseline = Mathf.Floor((diameter - font.GetHeight(UiFonts.Body)) / 2) + font.GetAscent(UiFonts.Body);
        DrawString(font, new Vector2(Mathf.Floor((width - size.X) / 2), baseline), text,
            fontSize: UiFonts.Body, modulate: UiTheme.ReadableInk(palette, palette.SealInk, fill));
    }

    /// <summary>Whether a pixel's centre lies within the circle, or the pill it stretches into.</summary>
    private bool Inside(int x, int y, int width)
    {
        if (x < 0 || y < 0 || x >= width || y >= diameter) return false;
        var radius = diameter / 2f;
        var centreY = radius;
        // The nearest point on the line between the two end circles' centres.
        var centreX = Math.Clamp(x + 0.5f, radius, width - radius);
        var dx = x + 0.5f - centreX;
        var dy = y + 0.5f - centreY;
        return dx * dx + dy * dy <= radius * radius;
    }
}
