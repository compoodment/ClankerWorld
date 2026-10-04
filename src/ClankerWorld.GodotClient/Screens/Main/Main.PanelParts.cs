using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// Small pictures several panels share: an agent's portrait, a tile as it
/// looks on the map, and keys drawn as keycaps.
/// </summary>
public partial class Main
{
    /// <summary>A small framed portrait: the agent's own map sprite on green, or on grey once they have died.</summary>
    private static ImageTexture AgentPortrait(OwnerWorldInhabitant person, bool living)
    {
        var age = person.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail;
        var sprite = AgentSprites.Sprite(AgentSprites.VariantFor(person.Id), AgentSprites.StageIndex(age), 16);
        var dark = UiTheme.Current.Name == "dark";
        var image = Image.CreateEmpty(20, 20, false, Image.Format.Rgba8);
        image.Fill(UiTheme.Current.WoodEdge);
        image.FillRect(new Rect2I(1, 1, 18, 18), living ? dark ? new Color("3E5A2E") : new Color("8FB06A") : UiTheme.Current.InkFaint);
        sprite.Convert(Image.Format.Rgba8);
        image.BlendRect(sprite, new Rect2I(0, 0, 16, 16), new Vector2I(2, 2));
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// The tile as it looks on the map: its own ground texture with any tree
    /// or plant drawn on top, cropped to a square and framed.
    /// </summary>
    private static ImageTexture TileSwatch(WorldTerrainMap map, int x, int y, int size, OwnerWorldResource? resource = null)
    {
        var style = map.StyleAt(x, y);
        var ground = TerrainTextures.IsWater(style)
            ? WaterTextures.Block(style, 16).GetRegion(new Rect2I(0, 0, 16, 16))
            : TerrainTextures.Tile(style, TerrainTextures.VariantAt(x, y), 16);
        ground.Convert(Image.Format.Rgba8);
        if (resource is not null && ResourceSpriteImage(resource) is { } sprite)
            ground.BlendRect(sprite, new Rect2I(0, 0, 16, 16), Vector2I.Zero);
        var crop = ground.GetRegion(new Rect2I((16 - size) / 2, (16 - size) / 2, size, size));
        var framed = Image.CreateEmpty(size + 2, size + 2, false, Image.Format.Rgba8);
        framed.Fill(UiTheme.Current.WoodEdge);
        framed.BlitRect(crop, new Rect2I(0, 0, size, size), new Vector2I(1, 1));
        return ImageTexture.CreateFromImage(framed);
    }

    /// <summary>The map sprite for a tree or natural site, if it has one.</summary>
    private static ImageTexture? ResourceSprite(OwnerWorldResource resource) =>
        ResourceSpriteImage(resource) is { } image ? ImageTexture.CreateFromImage(image) : null;

    private static Image? ResourceSpriteImage(OwnerWorldResource resource)
    {
        NatureSprite? sprite;
        if (resource.TreeKind is { } species)
            sprite = TreeArtManifest.For(species, resource.TreeStage ?? "mature")?.Sprite ?? TreeArtManifest.For(species, "mature")?.Sprite;
        else if (resource.NaturalObjectKind is { } kind)
            sprite = NatureSprites.ForNaturalObject(kind, resource.Quantity == 0 || resource.State != "available", resource.IsRenewable);
        else
            sprite = NatureSprites.ForCampResource(resource.Kind, resource.Quantity == 0 || resource.State != "available", resource.IsRenewable);
        return sprite is { } found ? NatureSprites.Sprite(found, 16) : null;
    }

    private static readonly string[] MouseActions = ["Click", "Wheel", "Middle-drag"];

    /// <summary>
    /// A key drawn as a raised keycap, or a mouse action with the mouse icon.
    /// The thick bottom edge is the key's shadow, so the letter is centred on
    /// the face above it.
    /// </summary>
    private static Control Keycap(string key)
    {
        var p = UiTheme.Current;
        if (MouseActions.Contains(key))
        {
            var mouse = new HBoxContainer();
            mouse.AddThemeConstantOverride("separation", 4);
            mouse.AddChild(new TextureRect
            {
                Texture = PixelIcons.Texture(PixelGlyph.Mouse, p.Ink, p.Primary, 1),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            mouse.AddChild(new Label { Text = key });
            return mouse;
        }
        var cap = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = p.Button,
            BorderColor = p.ButtonEdge,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 3,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
            ContentMarginTop = 0,
            ContentMarginBottom = 2,
        });
        cap.AddChild(new Label { Text = key, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(10, 0) });
        return cap;
    }
}
