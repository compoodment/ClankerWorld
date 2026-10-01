#pragma warning disable CA1859, CA1822, CA1861
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

// LOCAL ONLY - never commit. A proposed Developer tools panel for the owner to accept or not.
public partial class Main
{
    private static ImageTexture DevSwatch(TerrainStyle style)
    {
        var ground = TerrainTextures.IsWater(style)
            ? WaterTextures.Block(style, 16).GetRegion(new Rect2I(0, 0, 16, 16))
            : TerrainTextures.Tile(style, 0, 16);
        ground.Convert(Image.Format.Rgba8);
        return ImageTexture.CreateFromImage(ground);
    }

    private static ImageTexture DevSprite(NatureSprite sprite, TerrainStyle ground = TerrainStyle.Grass)
    {
        var image = TerrainTextures.Tile(ground, 0, 16);
        image.Convert(Image.Format.Rgba8);
        image.BlendRect(NatureSprites.Sprite(sprite, 16), new Rect2I(0, 0, 16, 16), Vector2I.Zero);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture DevHouse()
    {
        var image = TerrainTextures.Tile(TerrainStyle.Grass, 0, 16);
        image.Convert(Image.Format.Rgba8);
        var house = BuildingSprites.Render(BuildingKind.House, 1, 1, 16, BuildingDoor.Default);
        house.Convert(Image.Format.Rgba8);
        image.BlendRect(house, new Rect2I(0, 0, 16, 16), Vector2I.Zero);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A square tool button with its picture, and a short caption underneath.</summary>
    private static VBoxContainer DevTool(Texture2D icon, string caption, bool pressed, string tip, bool framedIcon = true)
    {
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 2);
        var button = new Button
        {
            ToggleMode = true,
            ButtonPressed = pressed,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tip,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        StyleButton(button);
        button.CustomMinimumSize = new Vector2(30, 30);
        var picture = new TextureRect
        {
            Texture = icon,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(picture);
        if (framedIcon && icon.GetWidth() == 16)
        {
            // A thin dark frame so map pictures read as tiles.
            var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                BorderColor = UiTheme.Current.WoodEdge,
                BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            });
            frame.SetAnchorsPreset(Control.LayoutPreset.Center);
            frame.OffsetLeft = -9; frame.OffsetTop = -9; frame.OffsetRight = 9; frame.OffsetBottom = 9;
            button.AddChild(frame);
        }
        column.AddChild(button);
        column.AddChild(new Label { Text = caption, ThemeTypeVariation = "DimLabel", HorizontalAlignment = HorizontalAlignment.Center });
        return column;
    }

    /// <summary>Linked buttons with an icon and a word each, the chosen one pressed.</summary>
    private static PanelContainer DevSegments(IEnumerable<(Texture2D Icon, string Text, string Tip)> options, int chosen)
    {
        var panel = new PanelContainer { ThemeTypeVariation = "SegmentedPanel" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        var index = 0;
        foreach (var (icon, text, tip) in options)
        {
            row.AddChild(new Button
            {
                Text = text,
                Icon = icon,
                ToggleMode = true,
                ButtonPressed = index++ == chosen,
                ThemeTypeVariation = "TabButton",
                TooltipText = tip,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 26),
            });
        }
        panel.AddChild(row);
        return panel;
    }

    private static HBoxContainer DevSection(string title, string? note = null)
    {
        var band = new HBoxContainer();
        band.AddThemeConstantOverride("separation", 8);
        band.AddChild(new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" });
        band.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        if (note is not null) band.AddChild(new Label { Text = note, ThemeTypeVariation = "DimLabel" });
        return band;
    }

    private PanelContainer MockDevTools()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);

        var hint = new HBoxContainer();
        hint.AddThemeConstantOverride("separation", 6);
        hint.AddChild(new TextureRect { Texture = PixelIcons.Texture(PixelGlyph.Pause, UiTheme.Current.EmberDark, UiTheme.Current.EmberDark, 1), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        hint.AddChild(new Label { Text = "Changes apply while time is paused.", ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        hint.AddChild(Keycap("F10"));
        body.AddChild(hint);

        body.AddChild(DevSection("Weather here"));
        body.AddChild(DevSegments(new[] { ("clear", "Clear"), ("cloudy", "Cloudy"), ("rain", "Rain"), ("storm", "Storm"), ("snow", "Snow") }
            .Select(item => ((Texture2D)PixelIcons.Weather(item.Item1, 1), item.Item2, $"Make it {item.Item2.ToLowerInvariant()} where you are looking")), 0));

        body.AddChild(DevSection("Season"));
        body.AddChild(DevSegments(new[] { "spring", "summer", "autumn", "winter" }
            .Select(season => ((Texture2D)PixelIcons.Season(season, 1), Pretty(season), $"Change the season to {season}")), 0));

        body.AddChild(DevSection("Place on the map"));
        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 4);
        grid.AddChild(DevTool(DevSwatch(TerrainStyle.Grass), "Meadow", false, "Turn tiles into meadow"));
        grid.AddChild(DevTool(DevSwatch(TerrainStyle.Lake), "Water", false, "Turn tiles into water"));
        grid.AddChild(DevTool(DevSwatch(TerrainStyle.Mountain), "Mountain", false, "Turn tiles into mountain"));
        grid.AddChild(DevTool(DevSprite(NatureSprite.Broadleaf), "Tree", false, "Plant a tree"));
        grid.AddChild(DevTool(DevSprite(NatureSprite.BerryBush), "Berries", true, "Place a berry bush"));
        grid.AddChild(DevTool(DevSprite(NatureSprite.StoneOutcrop), "Stone", false, "Place a stone outcrop"));
        grid.AddChild(DevTool(DevSprite(NatureSprite.ClayBank), "Clay", false, "Place a clay bank"));
        grid.AddChild(DevTool(DevSprite(NatureSprite.FiberPlant), "Fibre", false, "Place a fibre plant"));
        grid.AddChild(DevTool(DevHouse(), "House", false, "Place a House"));
        grid.AddChild(DevTool(PixelIcons.Texture(PixelGlyph.Close, UiTheme.Current.Bad, UiTheme.Current.Bad, 1), "Remove", false, "Remove what is on a tile", framedIcon: false));
        var gridCenter = new CenterContainer();
        gridCenter.AddChild(grid);
        body.AddChild(gridCenter);
        var how = new HBoxContainer();
        how.AddThemeConstantOverride("separation", 6);
        how.AddChild(new TextureRect { Texture = PixelIcons.Texture(PixelGlyph.Mouse, UiTheme.Current.Ink, UiTheme.Current.Primary, 1), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
        how.AddChild(new Label { Text = "Click tiles to place berry bushes. Right-click or Esc to stop.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 0) });
        body.AddChild(how);

        var admin = new Button { Text = "Devices, retries and aging  ›", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
        StyleButton(admin);
        body.AddChild(admin);

        var panel = new PanelContainer();
        AddClosablePanelContents(panel, "Developer tools", body);
        panel.CustomMinimumSize = new Vector2(350, 0);
        panel.ZIndex = 85;
        return panel;
    }
}
