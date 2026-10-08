using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The small badge on a building's corner with how many people are inside
/// (agreed October 1): people inside a building are hidden from the map, and
/// the building's card names them. Drawn in the conversation badge's colours,
/// a person icon and the count on a cream pill with a dark rim.
/// </summary>
public partial class OccupancyBadge : Control
{
    private static readonly Color Rim = new("1E1712");
    private static readonly Color Fill = new("F4E7BE");
    private static readonly Color Ink = new("493522");
    private int count;

    public OccupancyBadge()
    {
        // Hover shows who is inside; clicks fall through to the map, which selects the building.
        MouseFilter = MouseFilterEnum.Pass;
        ZIndex = 6;
    }

    /// <summary>How many people are inside.</summary>
    public int Count
    {
        get => count;
        set { if (count != value) { count = value; QueueRedraw(); } }
    }

    /// <summary>The badge's width and height for a map tile of <paramref name="tileSize"/> pixels.</summary>
    public static Vector2 SizeFor(int tileSize, int count)
    {
        var height = Mathf.Clamp(MathF.Round(tileSize * 0.34f), 12f, 20f);
        var digits = Math.Clamp(count, 0, 99).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return new Vector2(height + digits * height * 0.55f + 2, height);
    }

    public override void _Draw()
    {
        var height = Size.Y;
        var radius = height / 2;
        // A pill: two end discs and the band between them, rim first.
        DrawCircle(new Vector2(radius, radius), radius, Rim);
        DrawCircle(new Vector2(Size.X - radius, radius), radius, Rim);
        DrawRect(new Rect2(radius, 0, Size.X - height, height), Rim);
        DrawCircle(new Vector2(radius, radius), radius - 1, Fill);
        DrawCircle(new Vector2(Size.X - radius, radius), radius - 1, Fill);
        DrawRect(new Rect2(radius, 1, Size.X - height, height - 2), Fill);
        var scale = Math.Max(1, (int)(height - 4) / 12);
        var icon = PixelIcons.Texture(PixelGlyph.Person, Ink, Ink, scale);
        var iconSize = 12f * scale;
        DrawTextureRect(icon, new Rect2(new Vector2(2, (height - iconSize) / 2).Floor(), new Vector2(iconSize, iconSize)), false);
        var font = UiFonts.Text;
        var fontSize = (int)Math.Max(8, height - 6);
        var text = Math.Clamp(count, 0, 99).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        var left = 2 + iconSize + (Size.X - 2 - iconSize - 2 - textSize.X) / 2;
        DrawString(font, new Vector2(left, (height - textSize.Y) / 2 + font.GetAscent(fontSize)).Floor(), text, fontSize: fontSize, modulate: Ink);
    }
}
