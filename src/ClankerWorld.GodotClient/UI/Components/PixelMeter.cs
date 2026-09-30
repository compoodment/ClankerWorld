using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>What a condition meter measures, which sets its color.</summary>
public enum MeterKind
{
    Fullness,
    Warmth,
    Diet,
    Illness,
}

/// <summary>
/// A captioned bar of ten pixel segments for part of an agent's condition,
/// such as "Warmth ▰▰▰▰▰▰▰▰▱▱". Hovering shows the exact percentage.
/// </summary>
public partial class PixelMeter : Control
{
    private const int Segments = 10;
    private const int SegmentWidth = 5;
    private const int BarHeight = 9;
    private int percent;
    private int captionWidth = 56;

    public PixelMeter()
    {
        MouseFilter = MouseFilterEnum.Pass;
        CustomMinimumSize = new Vector2(captionWidth + BarWidth, 14);
    }

    public MeterKind Kind { get; init; }

    public string Caption { get; init; } = string.Empty;

    /// <summary>Room kept for the caption, so meters stacked together line up.</summary>
    public int CaptionWidth
    {
        get => captionWidth;
        init
        {
            captionWidth = value;
            CustomMinimumSize = new Vector2(captionWidth + BarWidth, 14);
        }
    }

    private static int BarWidth => Segments * (SegmentWidth + 1) + 1;

    public int Percent
    {
        get => percent;
        set
        {
            value = Math.Clamp(value, 0, 100);
            TooltipText = $"{Caption} {value}%";
            if (percent == value) return;
            percent = value;
            QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        // A new theme swaps the palette the bar is drawn in.
        if (what == NotificationThemeChanged) QueueRedraw();
    }

    public override void _Draw()
    {
        var palette = UiTheme.Current;
        var font = UiFonts.Text;
        var baseline = Mathf.Floor((Size.Y - font.GetHeight(UiFonts.Body)) / 2) + font.GetAscent(UiFonts.Body);
        DrawString(font, new Vector2(0, baseline), Caption, fontSize: UiFonts.Body, modulate: palette.InkMuted);
        var fill = Kind switch
        {
            MeterKind.Warmth => palette.MeterWarmth,
            MeterKind.Diet => palette.MeterDiet,
            MeterKind.Illness => palette.MeterIllness,
            _ => palette.MeterFullness,
        };
        var x = (float)captionWidth;
        var y = Mathf.Floor((Size.Y - BarHeight) / 2);
        DrawRect(new Rect2(x, y, BarWidth, BarHeight), palette.ButtonEdge);
        // Any amount above zero lights at least one segment.
        var lit = percent == 0 ? 0 : Math.Max(1, (int)Math.Round(percent / 100f * Segments));
        for (var index = 0; index < Segments; index++)
        {
            var cell = new Rect2(x + 1 + index * (SegmentWidth + 1), y + 1, SegmentWidth, BarHeight - 2);
            if (index >= lit)
            {
                DrawRect(cell, palette.Inset);
                continue;
            }
            DrawRect(cell, fill);
            DrawRect(new Rect2(cell.Position, new Vector2(SegmentWidth, 1)), fill.Lightened(0.35f));
            DrawRect(new Rect2(cell.Position.X, cell.End.Y - 1, SegmentWidth, 1), fill.Darkened(0.25f));
        }
    }
}
