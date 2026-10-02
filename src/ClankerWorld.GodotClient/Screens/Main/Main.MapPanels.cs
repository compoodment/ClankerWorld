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

    /// <summary>
    /// Where the World Map puts a Town's House mark: the middle of its tiles.
    /// On a map that wraps east-west, each tile is measured the short way
    /// round from the first, so a Town across the seam is not marked mid-map.
    /// </summary>
    private static Vector2 TownMarkerTile(OwnerWorldTown town, int mapWidth, bool wrapsEastWest)
    {
        var firstX = town.BorderTiles[0].X;
        var x = firstX + (float)town.BorderTiles.Average(tile =>
        {
            var dx = tile.X - firstX;
            if (wrapsEastWest && Math.Abs(dx) > mapWidth / 2) dx -= Math.Sign(dx) * mapWidth;
            return dx;
        }) + 0.5f;
        if (wrapsEastWest) x = ((x % mapWidth) + mapWidth) % mapWidth;
        return new Vector2(x, (float)town.BorderTiles.Average(tile => tile.Y) + 0.5f);
    }

    /// <summary>What a map filter draws, for the picture beside it.</summary>
    private enum FilterLook { Border, Property, Title, Use, Dispute }

    /// <summary>A patch of map with a filter's overlay drawn on it the way the map draws it.</summary>
    private static ImageTexture FilterSwatch(FilterLook look)
    {
        var image = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
        image.Fill(UiTheme.Current.WoodEdge);
        image.FillRect(new Rect2I(1, 1, 16, 16), MapGrass);
        void Tint(Color color) => image.FillRect(new Rect2I(4, 4, 12, 12), MapGrass.Lerp(color, 0.35f));
        void Edge(Color color)
        {
            image.FillRect(new Rect2I(4, 4, 12, 2), color);
            image.FillRect(new Rect2I(4, 4, 2, 12), color);
        }
        switch (look)
        {
            case FilterLook.Border:
                // A dashed corner of a Town border.
                for (var i = 0; i < 12; i++)
                {
                    if (i % 4 == 3) continue;
                    image.FillRect(new Rect2I(4 + i, 5, 1, 2), new Color("F6EBCF"));
                    image.FillRect(new Rect2I(4, 5 + i, 2, 1), new Color("F6EBCF"));
                }
                break;
            case FilterLook.Property:
                // A small roof inside a household's tinted, outlined tile.
                Tint(HouseholdTeal);
                Edge(HouseholdTeal);
                image.FillRect(new Rect2I(7, 8, 7, 6), new Color("B8573A"));
                image.FillRect(new Rect2I(7, 8, 7, 2), new Color("D9744E"));
                image.FillRect(new Rect2I(10, 12, 2, 2), new Color("4A2F1C"));
                break;
            case FilterLook.Title:
                Tint(new Color("659BC1"));
                Edge(new Color("A7D0EE"));
                break;
            case FilterLook.Use:
                Tint(new Color("9D89DF"));
                Edge(new Color("9D89DF"));
                break;
            case FilterLook.Dispute:
                Tint(new Color("A54545"));
                for (var y = 4; y < 16; y += 4) image.FillRect(new Rect2I(4, y, 12, 2), new Color("C95A55"));
                Edge(new Color("F08B83"));
                break;
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The map's colour for the first household's property.</summary>
    private static readonly Color HouseholdTeal = new("4DC7B9");

    /// <summary>One filter: what it shows, a line saying how, and its switch on the right.</summary>
    private static HBoxContainer FilterRow(CheckButton toggle, FilterLook look, string title, string detail)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new TextureRect
        {
            Texture = FilterSwatch(look),
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
