using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The selected-tile card: a swatch of the ground with its name as the
/// heading, the facts in plain words underneath, then a row for each thing
/// standing on the tile with its own map sprite.
/// </summary>
public partial class Main
{
    private readonly TextureRect tileSwatch = new();
    private readonly Label tileTitle = new();
    private readonly Label tileSubtitle = new();
    private readonly GridContainer tileFacts = new();
    private readonly Label tileThingsHeading = new();
    private readonly VBoxContainer tileThings = new();

    private void BuildTileCard(Control content)
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        tileSwatch.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        tileSwatch.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        tileSwatch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        header.AddChild(tileSwatch);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        titles.AddThemeConstantOverride("separation", 0);
        tileTitle.ThemeTypeVariation = "HeadingLabel";
        titles.AddChild(tileTitle);
        tileSubtitle.ThemeTypeVariation = "DimLabel";
        titles.AddChild(tileSubtitle);
        header.AddChild(titles);
        var closeTile = CloseButton("Close tile inspection");
        closeTile.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        closeTile.Pressed += ClearTileSelection;
        header.AddChild(closeTile);
        body.AddChild(header);

        tileFacts.Columns = 2;
        tileFacts.AddThemeConstantOverride("h_separation", 14);
        tileFacts.AddThemeConstantOverride("v_separation", 2);
        body.AddChild(tileFacts);

        tileThingsHeading.Text = "HERE";
        tileThingsHeading.ThemeTypeVariation = "SectionLabel";
        body.AddChild(tileThingsHeading);
        tileThings.AddThemeConstantOverride("separation", 4);
        body.AddChild(tileThings);

        // The facts also stay as plain text for checks and assistive reading.
        selectedTileText.Hide();
        body.AddChild(selectedTileText);

        AddPanelContents(selectedTilePanel, body);
        selectedTilePanel.CustomMinimumSize = new Vector2(280, 0);
        selectedTilePanel.Resized += PositionSelectedTilePanel;
        selectedTilePanel.ZIndex = 80;
        selectedTilePanel.Hide();
        content.AddChild(selectedTilePanel);
    }

    /// <summary>
    /// The tile as it looks on the map: its own ground texture with any tree
    /// or plant drawn on top, cropped to a square and framed, for the tile
    /// card and the hover readout.
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

    /// <summary>The map sprite for a tree or natural site on the tile, if it has one.</summary>
    private static ImageTexture? ResourceSprite(OwnerWorldResource resource) =>
        ResourceSpriteImage(resource) is { } image ? ImageTexture.CreateFromImage(image) : null;

    private static Image? ResourceSpriteImage(OwnerWorldResource resource)
    {
        NatureSprite? sprite = null;
        if (resource.TreeKind is { } species)
            sprite = TreeArtManifest.For(species, resource.TreeStage ?? "mature")?.Sprite ?? TreeArtManifest.For(species, "mature")?.Sprite;
        else if (resource.NaturalObjectKind is { } kind)
        {
            var spent = resource.Quantity == 0 || resource.State != "available";
            sprite = spent ? (resource.IsRenewable ? NatureSprite.Regrowing : NatureSprite.Depleted) : kind switch
            {
                "berry_bush" => NatureSprite.BerryBush,
                "wild_greens" => NatureSprite.WildGreens,
                "fiber_plant" => NatureSprite.FiberPlant,
                "reeds" => NatureSprite.Reeds,
                "stone_outcrop" => NatureSprite.StoneOutcrop,
                "wild_seed_patch" => NatureSprite.WildSeedPatch,
                "fertile_soil" => NatureSprite.FertileSoil,
                "iron_outcrop" => NatureSprite.IronOutcrop,
                "gold_outcrop" => NatureSprite.GoldOutcrop,
                "diamond_outcrop" => NatureSprite.DiamondOutcrop,
                "clay_bank" => NatureSprite.ClayBank,
                _ => null,
            };
        }
        else sprite = NatureSprites.ForCampResource(resource.Kind);
        return sprite is { } found ? NatureSprites.Sprite(found, 16) : null;
    }

    /// <summary>Height in words, with the exact value kept for those who want it.</summary>
    private static string HeightWords(byte level) => level switch
    {
        < 85 => $"Low · {level} of 255",
        < 170 => $"Middle · {level} of 255",
        _ => $"High · {level} of 255",
    };

    private void AddTileFact(string key, string value)
    {
        tileFacts.AddChild(new Label { Text = key, ThemeTypeVariation = "DimLabel" });
        tileFacts.AddChild(new Label { Text = value });
    }

    private void AddTileThing(Texture2D? icon, string name, string detail)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(16, 16),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        row.AddChild(new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        if (detail.Length > 0)
            row.AddChild(new Label { Text = detail, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        tileThings.AddChild(row);
    }

    /// <summary>The ground's everyday name: water first, then the surface, then the terrain kind.</summary>
    private static string GroundName(WorldTerrainMap map, int x, int y)
    {
        var water = WorldTerrainMap.HydrologyName(map.HydrologyAt(x, y));
        if (water is not null and not "Land") return water;
        var vegetation = WorldTerrainMap.VegetationName(map.VegetationAt(x, y));
        if (vegetation is "Forest" or "Dense forest") return vegetation;
        return WorldTerrainMap.NameFor(map.At(x, y));
    }

    private void RenderTileCard(OwnerWorldSnapshot snapshot, Vector2I tile, string? weather, int? moisture)
    {
        var map = terrainMap!;
        foreach (var child in tileFacts.GetChildren().Concat(tileThings.GetChildren()))
        {
            child.GetParent().RemoveChild(child);
            child.QueueFree();
        }
        tileSwatch.Texture = TileSwatch(map, tile.X, tile.Y, 16,
            snapshot.Resources.FirstOrDefault(item => item.Position.X == tile.X && item.Position.Y == tile.Y));
        tileTitle.Text = GroundName(map, tile.X, tile.Y);
        var climate = WorldTerrainMap.ClimateName(map.ClimateAt(tile.X, tile.Y));
        tileSubtitle.Text = climate is null ? $"Tile {tile.X}, {tile.Y}" : $"Tile {tile.X}, {tile.Y} · {climate}";

        if (WorldTerrainMap.SurfaceName(map.SurfaceAt(tile.X, tile.Y)) is { } surface) AddTileFact("Ground", surface);
        if (WorldTerrainMap.VegetationName(map.VegetationAt(tile.X, tile.Y)) is { } plants and not "None") AddTileFact("Plants", plants);
        if (map.IsHillAt(tile.X, tile.Y)) AddTileFact("Land", "Hills");
        if (map.ElevationAt(tile.X, tile.Y) is { } level) AddTileFact("Height", HeightWords(level));
        if (weather is not null) AddTileFact("Weather", Pretty(weather));
        if (moisture is { } wet) AddTileFact("Soil", $"{(wet < 30 ? "Dry" : wet < 65 ? "Damp" : "Wet")} · {wet}%");
        if (snapshot.Towns.FirstOrDefault(item => item.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y)) is { } town)
            AddTileFact("Town", town.Name);

        var palette = UiTheme.Current;
        foreach (var building in snapshot.PlacedBuildings.Where(item =>
                     tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
                     tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height))
        {
            var owner = building.HouseholdId is { } household
                ? snapshot.Stockpiles.FirstOrDefault(item => item.OwnerId == household)?.Name ?? Pretty(household)
                : building.TownId is not null ? "Shared" : string.Empty;
            AddTileThing(PixelIcons.Themed(PixelGlyph.House, palette.Name == "dark" ? new Color("D89A5A") : new Color("B8733A"), 1),
                building.DisplayName ?? Pretty(building.DefinitionId), owner);
        }
        foreach (var resource in snapshot.Resources.Where(item => item.Position.X == tile.X && item.Position.Y == tile.Y))
        {
            var name = resource.TreeKind is { } tree ? $"{Pretty(tree)} tree"
                : WorldTerrainMap.NaturalObjectName(resource.NaturalObjectKind) ?? $"{Pretty(resource.Kind)} site";
            var detail = resource.TreeKind is not null
                ? Pretty(resource.TreeStage ?? resource.State)
                : resource.Quantity is { } quantity ? $"{quantity} left" : Pretty(resource.State);
            AddTileThing(ResourceSprite(resource), name, detail);
        }
        foreach (var item in snapshot.Objects.Where(item => item.Position.X == tile.X && item.Position.Y == tile.Y))
            AddTileThing(null, Pretty(item.Kind), string.Empty);
        if (snapshot.RoadTiles.Any(point => point.X == tile.X && point.Y == tile.Y))
            AddTileThing(null, "Road", string.Empty);
        if (BridgeAt(snapshot, tile) is { } bridge)
            AddTileThing(null, "Bridge", bridge.Trigger == "road" ? "on a Road" : "at a crossing");
        var hasThings = tileThings.GetChildCount() > 0;
        tileThingsHeading.Visible = hasThings;
        tileThings.Visible = hasThings;
        selectedTilePanel.ResetSize();
    }
}
