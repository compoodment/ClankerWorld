using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// One kind of stored item: its icon on a small square, the amount in a dark
/// badge in the corner, and, when named, the item's name underneath on up to
/// two short lines. Hovering shows the name and amount.
/// </summary>
public partial class ItemSlot : Control
{
    private const int Padding = 6;
    private const int BadgeFontSize = UiFonts.Body;
    private const int NameLineHeight = 13;
    private const int NamedWidth = 52;
    private static readonly Color BadgeColor = new("2E2016");
    // Darker in the Dark theme, where the square itself is dark brown.
    private static readonly Color DarkBadgeColor = new("120C07");
    private static readonly Color BadgeText = new("FFF3D6");

    public ItemSlot()
    {
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <summary>The item kind, such as <c>wooden_axe</c>; kinds without an icon show a crate.</summary>
    public string Kind { get; private set; } = "crate";

    public int Quantity { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Icon size in interface pixels; a whole multiple of 16 keeps the pixels square.</summary>
    public int IconSize { get; init; } = 32;

    /// <summary>Whether the item's name is written under the square.</summary>
    public bool Named { get; init; }

    private int Box => IconSize + Padding * 2;

    private int CellWidth => Named ? Math.Max(Box, NamedWidth) : Box;

    public void SetItem(string kind, int quantity, string displayName)
    {
        Kind = kind;
        Quantity = quantity;
        DisplayName = displayName;
        TooltipText = $"{displayName} × {quantity.ToString(CultureInfo.CurrentCulture)}";
        CustomMinimumSize = new Vector2(CellWidth, Box + (Named ? NameLineHeight * 2 + 2 : 0));
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        // A new theme swaps the palette the square is drawn in.
        if (what == NotificationThemeChanged) QueueRedraw();
    }

    public override void _Draw()
    {
        var palette = UiTheme.Current;
        var font = UiFonts.Text;
        var left = Mathf.Floor((CellWidth - Box) / 2f);
        var dark = palette.Name == "dark";
        var square = dark ? palette.Paper.Lightened(0.07f) : palette.Paper.Darkened(0.06f);
        DrawRect(new Rect2(left, 0, Box, Box), square);
        DrawRect(new Rect2(left + 0.5f, 0.5f, Box - 1, Box - 1), palette.PaperEdge, false, 1);
        // Sit the icon a little high so the badge covers less of it.
        DrawTextureRect(ItemIcons.Texture(Kind, IconSize), new Rect2(left + Padding, Padding - 2, IconSize, IconSize), false);

        var amount = Quantity.ToString(CultureInfo.CurrentCulture);
        var amountWidth = font.GetStringSize(amount, HorizontalAlignment.Left, -1, BadgeFontSize).X;
        var badge = new Rect2(left + Box - amountWidth - 7, Box - 13, amountWidth + 7, 13);
        DrawRect(badge, dark ? DarkBadgeColor : BadgeColor);
        DrawString(font, new Vector2(badge.Position.X + 3.5f, badge.End.Y - 3), amount,
            fontSize: BadgeFontSize, modulate: BadgeText);

        if (!Named) return;
        foreach (var (line, index) in NameLines(font).Select((line, index) => (line, index)))
        {
            var width = font.GetStringSize(line, HorizontalAlignment.Left, -1, BadgeFontSize).X;
            DrawString(font, new Vector2(Mathf.Floor((CellWidth - width) / 2), Box + 12 + index * NameLineHeight),
                line, fontSize: BadgeFontSize, modulate: palette.InkMuted);
        }
    }

    /// <summary>The name wrapped by word into at most two lines of the cell's width.</summary>
    private List<string> NameLines(Font font)
    {
        var lines = new List<string> { string.Empty };
        foreach (var word in DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var joined = lines[^1].Length == 0 ? word : lines[^1] + " " + word;
            if (lines[^1].Length > 0 && lines.Count < 2 &&
                font.GetStringSize(joined, HorizontalAlignment.Left, -1, BadgeFontSize).X > CellWidth)
                lines.Add(word);
            else
                lines[^1] = joined;
        }
        return lines;
    }
}
