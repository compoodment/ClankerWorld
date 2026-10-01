using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>Small pictures that explain the World Map and the map filters.</summary>
public partial class Main
{
    private static readonly Color MapGrass = new("5F8F5B");
    private static readonly Color MapInk = new("1E1712");
    private static readonly Color MapCream = new("FFF6E0");

    /// <summary>
    /// "House Town · dot Agent · box Your view" under the World Map. Each mark
    /// sits on a patch of map green, drawn exactly as on the map, so it reads
    /// the same in both themes.
    /// </summary>
    private static HBoxContainer OverviewLegend()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 6);
        void Item(Image mark, string text, bool gap)
        {
            var patch = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
            patch.Fill(UiTheme.Current.WoodEdge);
            patch.FillRect(new Rect2I(1, 1, 14, 14), MapGrass);
            mark.Convert(Image.Format.Rgba8);
            patch.BlendRect(mark, new Rect2I(Vector2I.Zero, mark.GetSize()), (new Vector2I(16, 16) - mark.GetSize()) / 2);
            if (gap) row.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
            row.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(patch), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = text, ThemeTypeVariation = "DimLabel" });
        }
        Item(PixelIcons.Texture(PixelGlyph.House, MapInk, new Color("F2C14E"), 1).GetImage(), "Town", false);
        var dot = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        dot.Fill(MapInk);
        dot.FillRect(new Rect2I(1, 1, 2, 2), MapCream);
        Item(dot, "Agent", true);
        var view = Image.CreateEmpty(10, 8, false, Image.Format.Rgba8);
        view.Fill(new Color("FFF0B5"));
        view.FillRect(new Rect2I(1, 1, 8, 6), new Color(0, 0, 0, 0));
        Item(view, "Your view", true);
        return row;
    }

    /// <summary>A 16-pixel patch of grass showing what a filter draws on the map.</summary>
    private static ImageTexture FilterSwatch(bool border)
    {
        var image = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
        image.Fill(UiTheme.Current.WoodEdge);
        image.FillRect(new Rect2I(1, 1, 16, 16), MapGrass);
        if (border)
        {
            // A dashed corner of a Town border.
            for (var i = 0; i < 12; i++)
            {
                if (i % 4 == 3) continue;
                image.FillRect(new Rect2I(4 + i, 5, 1, 2), new Color("F6EBCF"));
                image.FillRect(new Rect2I(4, 5 + i, 2, 1), new Color("F6EBCF"));
            }
        }
        else
        {
            // A small roof with a household's warm tint around it.
            image.FillRect(new Rect2I(3, 4, 12, 11), new Color("E9B95A"));
            image.FillRect(new Rect2I(5, 6, 8, 7), new Color("B8573A"));
            image.FillRect(new Rect2I(5, 6, 8, 2), new Color("D9744E"));
            image.FillRect(new Rect2I(8, 11, 2, 2), new Color("4A2F1C"));
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>One filter: what it shows, a line saying how, and its switch on the right.</summary>
    private static HBoxContainer FilterRow(CheckButton toggle, bool border, string title, string detail)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new TextureRect
        {
            Texture = FilterSwatch(border),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", 0);
        text.AddChild(new Label { Text = title });
        text.AddChild(new Label { Text = detail, ThemeTypeVariation = "DimLabel" });
        row.AddChild(text);
        toggle.Text = string.Empty;
        toggle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(toggle);
        return row;
    }
}
