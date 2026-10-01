using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A button whose word is drawn one pixel bolder, by repeating it one pixel
/// to the right in the same ink. Godot's own emboldening thickens strokes
/// up and down as well, which closes small letters such as "e". The button
/// keeps the width of its widest caption, so switching between captions
/// such as "Pause" and "Paused" never moves the controls beside it.
/// </summary>
public partial class BoldTextButton : Button
{
    private string caption = string.Empty;
    private Texture2D? glyph;
    private string[] captions = [];

    /// <summary>The word shown after the glyph.</summary>
    public string Caption
    {
        get => caption;
        set
        {
            if (caption == value) return;
            caption = value;
            AccessibilityName = value;
            Resize();
        }
    }

    /// <summary>The icon drawn before the word.</summary>
    public Texture2D? Glyph
    {
        get => glyph;
        set
        {
            glyph = value;
            Resize();
        }
    }

    /// <summary>Every caption the button may show; it is as wide as the widest.</summary>
    public string[] Captions
    {
        get => captions;
        set
        {
            captions = value;
            Resize();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged) Resize();
    }

    private void Resize()
    {
        var font = GetThemeFont("font");
        var size = GetThemeFontSize("font_size");
        var style = GetThemeStylebox("normal");
        var widest = captions.Append(caption)
            .Max(word => font.GetStringSize(word, HorizontalAlignment.Left, -1, size).X);
        var glyphWidth = glyph is null ? 0 : glyph.GetWidth() + GetThemeConstant("h_separation");
        var width = style.GetMargin(Side.Left) + glyphWidth + Mathf.Ceil(widest) + 1 + style.GetMargin(Side.Right);
        CustomMinimumSize = new Vector2(width, CustomMinimumSize.Y);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var mode = GetDrawMode();
        var (ink, tint) = mode switch
        {
            DrawMode.Disabled => ("font_disabled_color", "icon_disabled_color"),
            DrawMode.Pressed => ("font_pressed_color", "icon_pressed_color"),
            DrawMode.Hover => ("font_hover_color", "icon_hover_color"),
            DrawMode.HoverPressed => ("font_hover_pressed_color", "icon_hover_pressed_color"),
            _ => ("font_color", "icon_normal_color"),
        };
        var x = GetThemeStylebox("normal").GetMargin(Side.Left);
        if (glyph is not null)
        {
            DrawTexture(glyph, new Vector2(x, Mathf.Floor((Size.Y - glyph.GetHeight()) / 2)), GetThemeColor(tint));
            x += glyph.GetWidth() + GetThemeConstant("h_separation");
        }
        if (caption.Length == 0) return;
        var font = GetThemeFont("font");
        var size = GetThemeFontSize("font_size");
        var baseline = Mathf.Floor((Size.Y - font.GetHeight(size)) / 2) + font.GetAscent(size);
        var color = GetThemeColor(ink);
        for (var offset = 0; offset < 2; offset++)
            DrawString(font, new Vector2(x + offset, baseline), caption, fontSize: size, modulate: color);
    }
}
