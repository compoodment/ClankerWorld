using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly HFlowContainer familyLegend = new();

    /// <summary>
    /// The Family Tree's key, drawn with the tree's own line and heart, and a
    /// reminder that clicking someone opens their Profile. Redrawn when the
    /// theme changes so its colours match the tree.
    /// </summary>
    private void FillFamilyLegend()
    {
        foreach (var child in familyLegend.GetChildren())
        {
            familyLegend.RemoveChild(child);
            child.QueueFree();
        }
        familyLegend.AddThemeConstantOverride("h_separation", 6);
        familyLegend.AddThemeConstantOverride("v_separation", 2);
        void Key(Texture2D mark, string text)
        {
            var key = new HBoxContainer();
            key.AddThemeConstantOverride("separation", 6);
            key.AddChild(new TextureRect { Texture = mark, StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            key.AddChild(new Label { Text = text, ThemeTypeVariation = "DimLabel" });
            key.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
            familyLegend.AddChild(key);
        }
        var line = Image.CreateEmpty(14, 4, false, Image.Format.Rgba8);
        line.FillRect(new Rect2I(0, 1, 14, 2), UiTheme.Current.Primary);
        Key(ImageTexture.CreateFromImage(line), "Parent and child");
        Key(PixelIcons.Texture(PixelGlyph.Heart, UiTheme.Current.Partner, UiTheme.Current.Partner, 1), "Partners");
        familyLegend.AddChild(new Label { Text = "Click someone to open their Profile", ThemeTypeVariation = "DimLabel" });
    }
}
