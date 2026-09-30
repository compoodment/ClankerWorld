using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The game's pixel lettering. Body text is Fusion Pixel 12px, bundled
/// unmodified under the SIL Open Font License (see <c>Fonts/fusion-pixel-OFL.txt</c>).
/// Headings use <see cref="TimberFont"/>. Text is drawn without smoothing and
/// base sizes are whole multiples of <see cref="PixelSize"/>. Whole-number UI
/// scales keep letters crisp; fractional UI scales still need a later pass.
/// </summary>
public static class UiFonts
{
    /// <summary>The size at which one font pixel is one screen pixel.</summary>
    public const int PixelSize = 12;

    /// <summary>Body text, and small section labels such as INTERFACE.</summary>
    public const int Body = PixelSize;

    /// <summary>Panel headings, agent names and the clock.</summary>
    public const int Heading = PixelSize * 2;

    /// <summary>Screen titles such as Game Settings.</summary>
    public const int Title = PixelSize * 3;

    private const string TextPath = "res://UI/Theme/Fonts/fusion-pixel-12px-proportional.woff2";

    public static FontFile Text { get; } = LoadText();

    public static FontFile Headings { get; } = TimberFont.Create(PixelSize, Text);

    private static FontFile LoadText()
    {
        var font = GD.Load<FontFile>(TextPath);
        font.Antialiasing = TextServer.FontAntialiasing.None;
        font.Hinting = TextServer.Hinting.None;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        font.GenerateMipmaps = false;
        font.MultichannelSignedDistanceField = false;
        return font;
    }
}
