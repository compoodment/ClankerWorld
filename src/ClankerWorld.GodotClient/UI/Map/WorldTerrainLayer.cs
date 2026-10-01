using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Draws only camera-visible terrain; it never creates a node per tile.</summary>
public partial class WorldTerrainLayer : Control
{
    /// <summary>Below this tile size the one-pixel-per-tile overview cache is drawn instead of textures.</summary>
    public const int TexturedTileMinimum = 16;

    /// <summary>Below this tile size trees and natural sites stay simple dots readable at overview zoom.</summary>
    public const int SpriteTileMinimum = 12;

    private WorldTerrainMap? world;
    private Texture2D? paletteTexture;
    private ImageTexture? townSiteGuidanceTexture;
    private TownSiteGuidance? currentTownSiteGuidance;
    private Rect2 visibleTiles;
    private int tileSize;
    private int tileGap;
    private bool wrapsEastWest;
    private Vector2I? hoveredTile;
    private Vector2I? selectedTile;
    private Rect2I? selectedBuilding;
    private byte[] trees = [];
    private byte[] naturalObjects = [];
    private readonly Dictionary<int, NatureSprite> campResources = [];
    private readonly List<(TerrainStyle Style, int Piece)> transitionPieces = [];
    private byte[] naturalStages = [];
    private int weatherRegionSize = 32;
    private readonly Dictionary<Vector2I, string> weatherRegions = [];
    private readonly HashSet<Vector2I> townBorderTiles = [];
    private readonly HashSet<Vector2I> roadTiles = [];
    // Saved bridge decks, true when the deck runs east-west.
    private readonly Dictionary<Vector2I, bool> bridgeDecks = [];
    private readonly Dictionary<Vector2I, string> householdPropertyTiles = [];
    private readonly List<(Rect2I Footprint, BuildingKind Kind, BuildingDoor Door)> buildings = [];
    // Road tiles in front of a door, and the side of the tile the door is on.
    private readonly Dictionary<Vector2I, RoadLinks> doorsteps = [];
    private static readonly Color[] HouseholdPropertyColors =
    [
        new("4DC7B9"), new("9D89DF"), new("6AA6E8"), new("E69D70"),
    ];

    public int VisibleTileCount { get; private set; }

    /// <summary>Tiles inside the Town borders currently drawn; zero when borders are hidden.</summary>
    public int TownBorderTileCount => townBorderTiles.Count;

    /// <summary>Tiles tinted as household property; zero when that overlay is hidden.</summary>
    public int HouseholdPropertyTileCount => householdPropertyTiles.Count;

    /// <summary>Camera-visible tile rectangle, in tiles; x can run past the seam on wrapped worlds.</summary>
    public Rect2 VisibleTiles => visibleTiles;

    public int TileSize => tileSize;

    public int Stride => tileSize + tileGap;

    public bool WrapsEastWest => wrapsEastWest;

    public WorldTerrainMap? World => world;

    public TownSiteGuidance? CurrentTownSiteGuidance => currentTownSiteGuidance;

    public int WeatherRegionSize => weatherRegionSize;

    /// <summary>Changes whenever the map or its weather regions change, for layers that cache weather.</summary>
    public int WeatherVersion { get; private set; }

    /// <summary>Whether any region currently has rain, a storm or snow.</summary>
    public bool HasActiveWeather { get; private set; }

    /// <summary>Whether the current zoom draws generated ground textures rather than flat overview pixels.</summary>
    public bool DrawsGroundTextures => tileSize >= TexturedTileMinimum || tileGap != 0;

    public WorldTerrainLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void SetWorld(WorldTerrainMap map)
    {
        world = map;
        townSiteGuidanceTexture = null;
        currentTownSiteGuidance = null;
        // At overview scale, thousands of individual draw commands are much
        // slower than one nearest-neighbor pixel per tile from the same map.
        // This is a render cache, not a separate regional art set.
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
                image.SetPixel(x, y, map.DisplayColorAt(x, y));
        paletteTexture = ImageTexture.CreateFromImage(image);
        trees = new byte[checked(map.Width * map.Height)];
        naturalObjects = new byte[checked(map.Width * map.Height)];
        campResources.Clear();
        naturalStages = new byte[checked(map.Width * map.Height)];
        weatherRegions.Clear();
        HasActiveWeather = false;
        WeatherVersion++;
        townBorderTiles.Clear();
        roadTiles.Clear();
        bridgeDecks.Clear();
        householdPropertyTiles.Clear();
        QueueRedraw();
    }

    /// <summary>Shows provisional Town-site advice over open land; this never gates a map click.</summary>
    public void SetTownSiteGuidance(TownSiteGuidance? guidance)
    {
        if (ReferenceEquals(currentTownSiteGuidance, guidance)) return;
        if (guidance is not null)
        {
            var currentWorld = world;
            if (currentWorld is null || guidance.Width != currentWorld.Width ||
                guidance.Height != currentWorld.Height)
                throw new InvalidOperationException("Town-site guidance must match the current terrain map.");
        }
        currentTownSiteGuidance = guidance;
        townSiteGuidanceTexture = null;
        if (guidance is not null)
        {
            var image = Image.CreateEmpty(guidance.Width, guidance.Height, false, Image.Format.Rgba8);
            for (var y = 0; y < guidance.Height; y++)
                for (var x = 0; x < guidance.Width; x++)
                {
                    var assessment = guidance.At(x, y);
                    if (assessment.IsBuildableGround)
                        image.SetPixel(x, y, GuidanceColor(assessment.GuidanceStrength));
                }
            townSiteGuidanceTexture = ImageTexture.CreateFromImage(image);
        }
        QueueRedraw();
    }

    private static Color GuidanceColor(float strength)
    {
        var low = new Color(0.75f, 0.31f, 0.20f, 0.22f);
        var middle = new Color(0.91f, 0.67f, 0.27f, 0.22f);
        var high = new Color(0.27f, 0.72f, 0.43f, 0.22f);
        return strength < 0.5f
            ? low.Lerp(middle, strength * 2)
            : middle.Lerp(high, (strength - 0.5f) * 2);
    }

    public void SetWeatherRegions(int regionSize, IReadOnlyList<OwnerWeatherRegion> regions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionSize);
        ArgumentNullException.ThrowIfNull(regions);
        var next = regions.ToDictionary(region => new Vector2I(region.X, region.Y),
            region => region.Weather.ToLowerInvariant());
        if (regionSize == weatherRegionSize && next.Count == weatherRegions.Count &&
            next.All(entry => weatherRegions.TryGetValue(entry.Key, out var existing) && existing == entry.Value))
            return;
        weatherRegionSize = regionSize;
        weatherRegions.Clear();
        foreach (var entry in next) weatherRegions.Add(entry.Key, entry.Value);
        HasActiveWeather = weatherRegions.Values.Any(weather => weather is "rain" or "storm" or "snow");
        WeatherVersion++;
        QueueRedraw();
    }

    public void SetTownBorders(IReadOnlyList<OwnerWorldTown> towns)
    {
        ArgumentNullException.ThrowIfNull(towns);
        var next = towns.SelectMany(town => town.BorderTiles)
            .Select(point => new Vector2I(point.X, point.Y)).ToHashSet();
        if (next.Count == townBorderTiles.Count && next.SetEquals(townBorderTiles)) return;
        townBorderTiles.Clear();
        townBorderTiles.UnionWith(next);
        QueueRedraw();
    }

    public void SetRoads(IReadOnlyList<OwnerWorldPosition> roads)
    {
        ArgumentNullException.ThrowIfNull(roads);
        var next = roads.Select(point => new Vector2I(point.X, point.Y)).ToHashSet();
        if (next.Count == roadTiles.Count && next.SetEquals(roadTiles)) return;
        roadTiles.Clear();
        roadTiles.UnionWith(next);
        QueueRedraw();
    }

    /// <summary>Draws the same saved bridge decks that movement uses; a deck is not drawn from terrain alone.</summary>
    public void SetBridges(IReadOnlyList<OwnerWorldBridge> bridges)
    {
        ArgumentNullException.ThrowIfNull(bridges);
        var next = new Dictionary<Vector2I, bool>();
        foreach (var bridge in bridges)
            foreach (var tile in bridge.Span)
                next[new Vector2I(tile.X, tile.Y)] = bridge.Axis == "east_west";
        if (next.Count == bridgeDecks.Count && next.All(entry =>
                bridgeDecks.TryGetValue(entry.Key, out var eastWest) && eastWest == entry.Value)) return;
        bridgeDecks.Clear();
        foreach (var entry in next) bridgeDecks.Add(entry.Key, entry.Value);
        QueueRedraw();
    }

    public void SetHouseholdProperties(IReadOnlyList<OwnerWorldPlacedBuilding> buildings)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        var next = new Dictionary<Vector2I, string>();
        foreach (var building in buildings.Where(item => item.HouseholdId is not null))
            for (var y = 0; y < building.Height; y++)
                for (var x = 0; x < building.Width; x++)
                    next[new Vector2I(building.Position.X + x, building.Position.Y + y)] = building.HouseholdId!;
        if (next.Count == householdPropertyTiles.Count && next.All(entry =>
                householdPropertyTiles.TryGetValue(entry.Key, out var owner) && owner == entry.Value)) return;
        householdPropertyTiles.Clear();
        foreach (var entry in next) householdPropertyTiles.Add(entry.Key, entry.Value);
        QueueRedraw();
    }

    /// <summary>Placed buildings and legacy camp objects that have a building look.</summary>
    public void SetBuildings(IReadOnlyList<OwnerWorldPlacedBuilding> placed, IReadOnlyList<OwnerWorldObject> objects)
    {
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(objects);
        var next = placed.Select(building =>
            {
                var footprint = new Rect2I(building.Position.X, building.Position.Y,
                    Math.Max(1, building.Width), Math.Max(1, building.Height));
                var entrance = building.Entrance is { } tile ? new Vector2I(tile.X, tile.Y) : (Vector2I?)null;
                return (footprint, BuildingSprites.KindFor(building.Tags), BuildingDoor.Facing(footprint, entrance));
            })
            .Concat(objects.Where(item => BuildingSprites.KindForObject(item.Kind) is not null)
                .Select(item => (new Rect2I(item.Position.X, item.Position.Y, 1, 1), BuildingSprites.KindForObject(item.Kind)!.Value,
                    BuildingDoor.Default)))
            .ToList();
        if (next.SequenceEqual(buildings)) return;
        buildings.Clear();
        buildings.AddRange(next);
        doorsteps.Clear();
        foreach (var (footprint, _, door) in buildings)
        {
            if (door.Tile is not { } along) continue;
            var (tile, toward) = door.Side switch
            {
                DoorSide.North => (new Vector2I(footprint.Position.X + along, footprint.Position.Y - 1), RoadLinks.DoorSouth),
                DoorSide.East => (new Vector2I(footprint.End.X, footprint.Position.Y + along), RoadLinks.DoorWest),
                DoorSide.West => (new Vector2I(footprint.Position.X - 1, footprint.Position.Y + along), RoadLinks.DoorEast),
                _ => (new Vector2I(footprint.Position.X + along, footprint.End.Y), RoadLinks.DoorNorth),
            };
            if (wrapsEastWest && world is not null) tile.X = Mod(tile.X, world.Width);
            doorsteps[tile] = doorsteps.GetValueOrDefault(tile) | toward;
        }
        QueueRedraw();
    }

    public int BuildingSpriteCount => buildings.Count;

    public string WeatherAt(int x, int y)
    {
        if (world is null || y < 0 || y >= world.Height || (!wrapsEastWest && (x < 0 || x >= world.Width)))
            return "unknown";
        var canonicalX = wrapsEastWest ? Mod(x, world.Width) : x;
        return weatherRegions.GetValueOrDefault(new Vector2I(canonicalX / weatherRegionSize, y / weatherRegionSize), "clear");
    }

    public void SetTrees(IReadOnlyList<OwnerWorldResource> resources)
    {
        if (world is null) return;
        var next = new byte[checked(world.Width * world.Height)];
        foreach (var resource in resources)
        {
            var x = resource.Position.X;
            var y = resource.Position.Y;
            if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
            if (resource.TreeKind is not { } species) continue;
            // The host sends the stage it read from the saved growth state.
            // Older hosts sent it only for orchards, so derive the rest.
            var stage = resource.TreeStage ?? (species == "orchard" ? "fruiting"
                : resource.IsPlanted ? "sapling"
                : resource.Quantity == 0 || resource.State != "available" ? "stump" : "mature");
            if (TreeArtManifest.For(species, stage) is not { Code: > 0 } art) continue;
            var index = y * world.Width + x;
            if (next[index] != 0)
                throw new InvalidDataException("Two trees occupy one visible tile.");
            next[index] = art.Code;
        }
        trees = next;
        QueueRedraw();
    }

    public void SetCamera(Rect2 visible, int size, int gap, bool wrap)
    {
        visibleTiles = visible;
        tileSize = size;
        tileGap = gap;
        wrapsEastWest = wrap;
        var bounds = VisibleBounds();
        VisibleTileCount = bounds.Width * bounds.Height;
        QueueRedraw();
    }

    public Vector2I? HoveredTile => hoveredTile;
    public Vector2I? SelectedTile => selectedTile;

    public void SetSelectedTile(Vector2I? tile)
    {
        if (selectedTile == tile) return;
        selectedTile = tile;
        QueueRedraw();
    }

    /// <summary>The footprint of the selected building, outlined like a selected tile.</summary>
    public Rect2I? SelectedBuilding => selectedBuilding;

    public void SetSelectedBuilding(Rect2I? footprint)
    {
        if (selectedBuilding == footprint) return;
        selectedBuilding = footprint;
        QueueRedraw();
    }

    public string? TreeStageAt(int x, int y)
    {
        if (world is null || x < 0 || y < 0 || x >= world.Width || y >= world.Height) return null;
        return TreeArtManifest.ForCode(trees[y * world.Width + x])?.Stage;
    }

    public string? NaturalObjectNameAt(int x, int y)
    {
        if (world is null || x < 0 || y < 0 || x >= world.Width || y >= world.Height) return null;
        return naturalObjects[y * world.Width + x] switch
        {
            1 => "Berry bush",
            2 => "Wild greens",
            3 => "Fiber plant",
            4 => "Reeds",
            5 => "Stone outcrop",
            6 => "Wild seed patch",
            7 => "Fertile soil",
            8 => "Iron outcrop",
            9 => "Gold outcrop",
            10 => "Diamond outcrop",
            11 => "Clay bank",
            _ => null,
        };
    }

    public string? NaturalObjectStageAt(int x, int y)
    {
        if (world is null || x < 0 || y < 0 || x >= world.Width || y >= world.Height) return null;
        return naturalStages[y * world.Width + x] switch
        {
            1 => "depleted",
            2 => "regrowing",
            _ => NaturalObjectNameAt(x, y) is null ? null : "available",
        };
    }

    public void SetNaturalObjects(IReadOnlyList<OwnerWorldResource> resources)
    {
        if (world is null) return;
        ArgumentNullException.ThrowIfNull(resources);
        var next = new byte[checked(world.Width * world.Height)];
        var stages = new byte[next.Length];
        foreach (var resource in resources)
        {
            var kind = resource.NaturalObjectKind switch
            {
                "berry_bush" => (byte)1,
                "wild_greens" => (byte)2,
                "fiber_plant" => (byte)3,
                "reeds" => (byte)4,
                "stone_outcrop" => (byte)5,
                "fallen_wood" => (byte)12,
                "wild_seed_patch" => (byte)6,
                "fertile_soil" => (byte)7,
                "iron_outcrop" => (byte)8,
                "gold_outcrop" => (byte)9,
                "diamond_outcrop" => (byte)10,
                "clay_bank" => (byte)11,
                _ => (byte)0,
            };
            if (kind == 0) continue;
            var x = resource.Position.X;
            var y = resource.Position.Y;
            if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
            var index = y * world.Width + x;
            if (trees[index] != 0 || next[index] != 0)
                throw new InvalidDataException("Generated natural objects cannot overlap another tree or natural object.");
            next[index] = kind;
            stages[index] = resource.Quantity == 0 || resource.State != "available"
                ? resource.IsRenewable ? (byte)2 : (byte)1
                : (byte)0;
        }
        naturalObjects = next;
        naturalStages = stages;
        // Older camp resources carry only a resource kind; draw them with the
        // matching site's sprite where no generated site already stands.
        campResources.Clear();
        foreach (var resource in resources)
        {
            if (resource.TreeKind is not null || resource.NaturalObjectKind is not null ||
                NatureSprites.ForCampResource(resource.Kind) is not { } sprite) continue;
            var x = resource.Position.X;
            var y = resource.Position.Y;
            if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
            var index = y * world.Width + x;
            if (trees[index] != 0 || next[index] != 0) continue;
            campResources[index] = resource.Quantity == 0 || resource.State != "available"
                ? resource.IsRenewable ? NatureSprite.Regrowing : NatureSprite.Depleted
                : sprite;
        }
        QueueRedraw();
    }

    public int CampResourceSpriteCount => campResources.Count;

    public void SetHoveredTile(Vector2I? tile)
    {
        if (hoveredTile == tile) return;
        hoveredTile = tile;
        QueueRedraw();
    }

    private (int Left, int Top, int Width, int Height) VisibleBounds()
    {
        if (world is null || tileSize <= 0) return (0, 0, 0, 0);
        var left = Mathf.FloorToInt(visibleTiles.Position.X);
        var top = Math.Clamp(Mathf.FloorToInt(visibleTiles.Position.Y), 0, world.Height);
        var right = Mathf.CeilToInt(visibleTiles.End.X);
        if (!wrapsEastWest)
        {
            left = Math.Clamp(left, 0, world.Width);
            right = Math.Clamp(right, left, world.Width);
        }
        var bottom = Math.Clamp(Mathf.CeilToInt(visibleTiles.End.Y), top, world.Height);
        return (left, top, right - left, bottom - top);
    }

    public override void _Draw()
    {
        if (world is null) return;
        var bounds = VisibleBounds();
        var stride = tileSize + tileGap;
        if (tileSize < TexturedTileMinimum && tileGap == 0 && paletteTexture is not null)
        {
            var end = bounds.Left + bounds.Width;
            for (var x = bounds.Left; x < end;)
            {
                var sourceX = wrapsEastWest ? Mod(x, world.Width) : x;
                var width = Math.Min(end - x, world.Width - sourceX);
                DrawTextureRectRegion(paletteTexture,
                    new Rect2(x * stride, bounds.Top * stride, width * stride, bounds.Height * stride),
                    new Rect2(sourceX, bounds.Top, width, bounds.Height));
                x += width;
            }
        }
        else
        {
            // Zoomed in far enough to show detail: draw each tile's generated
            // pixel-art ground, then soft edges where a neighboring land
            // surface reaches into it, or a rounded shore on water tiles.
            var atlasSize = TerrainTextures.AtlasTileSize(tileSize);
            var atlas = TerrainTextures.Atlas(atlasSize);
            var edges = TerrainTransitions.Atlas(atlasSize);
            var coasts = CoastEdges.Atlas(atlasSize);
            var water = WaterTextures.Atlas(atlasSize);
            var hills = TerrainTextures.HillAtlas(atlasSize);
            for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            {
                for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
                {
                    var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                    var tile = new Rect2(new Vector2(x * stride, y * stride), new Vector2(tileSize, tileSize));
                    var style = world.StyleAt(mapX, y);
                    if (TerrainTextures.IsWater(style))
                    {
                        // Water comes from a larger repeating block, so its
                        // patches and crests continue across tile edges.
                        DrawTextureRectRegion(water, tile, WaterTextures.Region(style, mapX, y, atlasSize));
                        // Lighter water fans in first (a river mouth into the
                        // sea), then the rounded shore goes on top.
                        TerrainTransitions.CollectWater(world, mapX, y, wrapsEastWest, transitionPieces);
                        foreach (var (over, piece) in transitionPieces)
                            DrawTextureRectRegion(edges, tile, TerrainTransitions.Region(over, piece, atlasSize));
                        DrawCoast(coasts, tile, mapX, y, style, atlasSize);
                        continue;
                    }
                    DrawTextureRectRegion(atlas, tile, TerrainTextures.Region(style, TerrainTextures.VariantAt(mapX, y), atlasSize));
                    TerrainTransitions.Collect(world, mapX, y, wrapsEastWest, transitionPieces);
                    foreach (var (over, piece) in transitionPieces)
                        DrawTextureRectRegion(edges, tile, TerrainTransitions.Region(over, piece, atlasSize));
                    // Hills are relief over the tile's own ground, not a
                    // separate surface, so grass or snow still shows through.
                    if (world.IsHillAt(mapX, y))
                        DrawTextureRectRegion(hills, tile, TerrainTextures.HillRegion(
                            (int)(PixelArt.Hash(mapX, y, 61) % TerrainTextures.VariantCount), atlasSize));
                }
            }
        }
        DrawTownSiteGuidance(bounds, stride);
        DrawRoads(bounds, stride);
        DrawBridges(bounds, stride);
        DrawBuildings(bounds, stride);
        // Trees are objects, not baked ground colors: keep them visible both
        // above full-size tiles and above the small-tile palette cache.
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                var tree = trees[y * world.Width + (wrapsEastWest ? Mod(x, world.Width) : x)];
                if (tree != 0) DrawTree(new Vector2(x * stride, y * stride), tree);
                var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                var index = y * world.Width + mapX;
                if (naturalObjects[index] != 0)
                    DrawNaturalObject(new Vector2(x * stride, y * stride), naturalObjects[index], naturalStages[index]);
                else if (campResources.TryGetValue(index, out var campSprite))
                    DrawCampResource(new Vector2(x * stride, y * stride), campSprite);
            }
        DrawHouseholdProperties(bounds, stride);
        DrawTownBorders(bounds, stride);
        if (hoveredTile is { } hover && tileSize > 0 &&
            hover.Y >= bounds.Top && hover.Y < bounds.Top + bounds.Height)
        {
            var inset = tileSize >= 8 ? 1f : 0f;
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                if ((wrapsEastWest ? Mod(x, world.Width) : x) != hover.X) continue;
                DrawRect(new Rect2(new Vector2(x * stride + inset, hover.Y * stride + inset),
                    new Vector2(tileSize - inset * 2, tileSize - inset * 2)),
                    new Color("FFF0B5"), filled: false, width: tileSize >= 8 ? 2 : 1);
            }
        }
        if (selectedTile is { } selected && tileSize > 0 &&
            selected.Y >= bounds.Top && selected.Y < bounds.Top + bounds.Height)
        {
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                if ((wrapsEastWest ? Mod(x, world.Width) : x) != selected.X) continue;
                DrawRect(new Rect2(new Vector2(x * stride + 1, selected.Y * stride + 1),
                    new Vector2(tileSize - 2, tileSize - 2)),
                    new Color("FFD166"), filled: false, width: tileSize >= 12 ? 3 : 2);
            }
        }
        if (selectedBuilding is { } building && tileSize > 0 &&
            building.End.Y > bounds.Top && building.Position.Y < bounds.Top + bounds.Height)
        {
            for (var x = bounds.Left - building.Size.X + 1; x < bounds.Left + bounds.Width; x++)
            {
                if ((wrapsEastWest ? Mod(x, world.Width) : x) != building.Position.X) continue;
                DrawRect(new Rect2(new Vector2(x * stride + 1, building.Position.Y * stride + 1),
                    new Vector2(building.Size.X * stride - tileGap - 2, building.Size.Y * stride - tileGap - 2)),
                    new Color("FFD166"), filled: false, width: tileSize >= 12 ? 3 : 2);
            }
        }
    }

    private void DrawTownSiteGuidance((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null || townSiteGuidanceTexture is null || bounds.Width == 0 || bounds.Height == 0)
            return;
        var end = bounds.Left + bounds.Width;
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
        {
            for (var x = bounds.Left; x < end;)
            {
                var sourceX = wrapsEastWest ? Mod(x, world.Width) : x;
                var span = Math.Min(end - x, world.Width - sourceX);
                DrawTextureRectRegion(townSiteGuidanceTexture,
                    new Rect2(x * stride, y * stride, span * stride, stride),
                    new Rect2(sourceX, y, span, 1));
                x += span;
            }
        }
    }

    /// <summary>
    /// Rounds one water tile's shore: a lighter shallow band and (on coasts
    /// and lakes) a thin foam line under the land that reaches in from each
    /// neighbor, all following the same wandering edge. Rivers keep a quieter
    /// bank without foam.
    /// </summary>
    private void DrawCoast(Texture2D coasts, Rect2 tile, int x, int y, TerrainStyle style, int atlasSize)
    {
        if (world is null) return;
        TerrainTransitions.CollectCoast(world, x, y, wrapsEastWest, transitionPieces);
        if (transitionPieces.Count == 0) return;
        var river = style == TerrainStyle.River;
        var shallow = CoastEdges.ShallowColor(style);
        foreach (var (_, piece) in transitionPieces)
            DrawTextureRectRegion(coasts, tile,
                CoastEdges.Region(river ? CoastEdges.RiverShallowRow : CoastEdges.ShallowRow, piece, atlasSize), shallow);
        if (!river)
            foreach (var (_, piece) in transitionPieces)
                DrawTextureRectRegion(coasts, tile, CoastEdges.Region(CoastEdges.FoamRow, piece, atlasSize), CoastEdges.Foam);
        foreach (var (land, piece) in transitionPieces)
            DrawTextureRectRegion(coasts, tile, CoastEdges.Region(CoastEdges.LandRow, piece, atlasSize),
                TerrainTextures.BaseColor(land));
    }

    private void DrawNaturalObject(Vector2 position, byte kind, byte stage)
    {
        if (tileSize >= SpriteTileMinimum && NatureSprites.ForNaturalObject(kind, stage) is { } sprite)
        {
            DrawNatureSprite(position, sprite);
            return;
        }
        var center = position + new Vector2(tileSize * 0.5f, tileSize * 0.56f);
        var scale = Math.Max(1f, tileSize / 24f);
        if (stage == 1)
        {
            DrawCircle(center, Math.Max(2f, tileSize * 0.18f), new Color("77766D", 0.78f));
            DrawCircle(center, Math.Max(1f, tileSize * 0.1f), new Color("A69A81", 0.72f));
            return;
        }
        if (stage == 2)
        {
            DrawCircle(center, Math.Max(1.5f, tileSize * 0.10f), new Color("658451", 0.74f));
            return;
        }
        switch (kind)
        {
            case 1: // berry bush
                DrawCircle(center, tileSize * 0.24f, new Color("426744"));
                DrawCircle(center + new Vector2(-tileSize * 0.11f, -tileSize * 0.05f), tileSize * 0.14f, new Color("629052"));
                DrawCircle(center + new Vector2(tileSize * 0.10f, -tileSize * 0.10f), tileSize * 0.035f * scale, new Color("C4564B"));
                DrawCircle(center + new Vector2(tileSize * 0.15f, tileSize * 0.02f), tileSize * 0.035f * scale, new Color("C4564B"));
                break;
            case 2: // wild greens
                DrawLeaf(center + new Vector2(-tileSize * 0.12f, 0), tileSize * 0.21f, new Color("587D48"));
                DrawLeaf(center + new Vector2(tileSize * 0.10f, -tileSize * 0.03f), tileSize * 0.19f, new Color("749653"));
                break;
            case 3: // fiber plant
            case 4: // reeds
                var stemColor = kind == 4 ? new Color("887D49") : new Color("748953");
                for (var stem = -1; stem <= 1; stem++)
                {
                    var basePoint = center + new Vector2(stem * tileSize * 0.09f, tileSize * 0.12f);
                    DrawLine(basePoint, basePoint + new Vector2(stem * -scale, -tileSize * 0.32f), stemColor, scale);
                }
                break;
            case 5: // stone outcrop
                DrawCircle(center + new Vector2(-tileSize * 0.08f, tileSize * 0.01f), tileSize * 0.19f, new Color("77776F"));
                DrawCircle(center + new Vector2(tileSize * 0.08f, -tileSize * 0.04f), tileSize * 0.15f, new Color("A29E91"));
                DrawLine(center + new Vector2(-tileSize * 0.12f, -tileSize * 0.05f),
                    center + new Vector2(tileSize * 0.02f, -tileSize * 0.15f), new Color("D1CCC0", 0.8f), scale);
                break;
            case 8: // iron outcrop
                DrawCircle(center + new Vector2(-tileSize * 0.08f, tileSize * 0.01f), tileSize * 0.19f, new Color("676A68"));
                DrawCircle(center + new Vector2(tileSize * 0.08f, -tileSize * 0.04f), tileSize * 0.15f, new Color("93918A"));
                DrawLine(center + new Vector2(-tileSize * 0.12f, -tileSize * 0.03f),
                    center + new Vector2(-tileSize * 0.01f, -tileSize * 0.14f), new Color("AA7658"), scale * 1.2f);
                DrawLine(center + new Vector2(tileSize * 0.02f, tileSize * 0.02f),
                    center + new Vector2(tileSize * 0.15f, -tileSize * 0.09f), new Color("B7805B"), scale);
                break;
            case 9: // gold outcrop
                DrawCircle(center + new Vector2(-tileSize * 0.08f, tileSize * 0.01f), tileSize * 0.19f, new Color("6D6B5E"));
                DrawCircle(center + new Vector2(tileSize * 0.08f, -tileSize * 0.04f), tileSize * 0.15f, new Color("8C8875"));
                DrawCircle(center + new Vector2(-tileSize * 0.10f, -tileSize * 0.05f), scale * 1.5f, new Color("E2C35E"));
                DrawCircle(center + new Vector2(tileSize * 0.05f, -tileSize * 0.11f), scale * 1.3f, new Color("F1D878"));
                DrawCircle(center + new Vector2(tileSize * 0.13f, tileSize * 0.04f), scale, new Color("D8B64F"));
                break;
            case 10: // diamond outcrop
                DrawCircle(center + new Vector2(-tileSize * 0.07f, tileSize * 0.03f), tileSize * 0.18f, new Color("686F72"));
                DrawLine(center + new Vector2(-tileSize * 0.05f, 0),
                    center + new Vector2(0, -tileSize * 0.18f), new Color("A8E1E4"), scale * 1.5f);
                DrawLine(center + new Vector2(0, -tileSize * 0.18f),
                    center + new Vector2(tileSize * 0.09f, 0), new Color("E5FFFF"), scale * 1.5f);
                DrawLine(center + new Vector2(tileSize * 0.09f, 0),
                    center + new Vector2(0, tileSize * 0.09f), new Color("84C8D0"), scale * 1.5f);
                DrawLine(center + new Vector2(0, tileSize * 0.09f),
                    center + new Vector2(-tileSize * 0.05f, 0), new Color("B6F1EF"), scale * 1.5f);
                break;
            case 11: // clay bank
                DrawCircle(center, tileSize * 0.20f, new Color("946A4E"));
                DrawLine(center + new Vector2(-tileSize * 0.13f, -tileSize * 0.04f),
                    center + new Vector2(tileSize * 0.12f, -tileSize * 0.07f), new Color("D1A37A"), scale * 1.2f);
                DrawLine(center + new Vector2(-tileSize * 0.10f, tileSize * 0.04f),
                    center + new Vector2(tileSize * 0.15f, tileSize * 0.01f), new Color("684B3C"), scale);
                break;
            case 6: // wild seed patch
                for (var stem = -1; stem <= 1; stem++)
                {
                    var basePoint = center + new Vector2(stem * tileSize * 0.1f, tileSize * 0.1f);
                    DrawLine(basePoint, basePoint + new Vector2(0, -tileSize * 0.22f), new Color("907F43"), scale);
                    DrawCircle(basePoint + new Vector2(0, -tileSize * 0.24f), scale * 1.5f, new Color("C3AE65"));
                }
                break;
            case 7: // fertile soil site
                DrawCircle(center, tileSize * 0.19f, new Color("5A4635", 0.78f));
                DrawLine(center + new Vector2(-tileSize * 0.12f, -tileSize * 0.03f),
                    center + new Vector2(tileSize * 0.11f, -tileSize * 0.07f), new Color("B69B69"), scale);
                break;
        }
    }

    private void DrawLeaf(Vector2 center, float radius, Color color)
    {
        DrawCircle(center, Math.Max(1f, radius * 0.68f), color);
        DrawLine(center + new Vector2(-radius * 0.4f, radius * 0.4f),
            center + new Vector2(radius * 0.4f, -radius * 0.4f), new Color("B6C583", 0.78f),
            Math.Max(1f, radius * 0.12f));
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;

    private void DrawHouseholdProperties((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null || householdPropertyTiles.Count == 0 || tileSize <= 0) return;
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                var canonicalX = wrapsEastWest ? Mod(x, world.Width) : x;
                if (!householdPropertyTiles.TryGetValue(new Vector2I(canonicalX, y), out var householdId)) continue;
                var color = HouseholdPropertyColors[HouseholdColorIndex(householdId)];
                var tile = new Rect2(new Vector2(x * stride, y * stride), new Vector2(tileSize, tileSize));
                DrawRect(tile, new Color(color.R, color.G, color.B, 0.22f));
                DrawRect(tile, new Color(color.R, color.G, color.B, 0.88f), filled: false,
                    width: Math.Clamp(tileSize / 24f, 1f, 3f));
            }
    }

    private static int HouseholdColorIndex(string householdId)
    {
        uint hash = 2166136261;
        foreach (var character in householdId)
        {
            hash ^= character;
            hash = unchecked(hash * 16777619);
        }
        return (int)(hash % HouseholdPropertyColors.Length);
    }

    /// <summary>
    /// Town borders as a pale dashed line just inside the border's outer edge,
    /// with a faint shadow so it stays visible on snow and sand. Too small for
    /// dashes, at overview zoom, it is a thin solid line.
    /// </summary>
    private void DrawTownBorders((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null || townBorderTiles.Count == 0 || tileSize <= 0) return;
        var dash = new Color("FFF1C9");
        var shadow = new Color(0.1f, 0.07f, 0.04f, 0.35f);
        var width = Math.Max(1.5f, tileSize / 14f);
        var dashed = tileSize >= SpriteTileMinimum;
        var inset = dashed ? width : 0.5f;
        float t = tileSize;
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                var canonicalX = wrapsEastWest ? Mod(x, world.Width) : x;
                if (!townBorderTiles.Contains(new Vector2I(canonicalX, y))) continue;
                var origin = new Vector2(x * stride, y * stride);
                if (!ContainsTownTile(canonicalX, y - 1)) Edge(origin, new Vector2(1, 0), new Vector2(0, 1));
                if (!ContainsTownTile(canonicalX + 1, y)) Edge(origin + new Vector2(t, 0), new Vector2(0, 1), new Vector2(-1, 0));
                if (!ContainsTownTile(canonicalX, y + 1)) Edge(origin + new Vector2(t, t), new Vector2(-1, 0), new Vector2(0, -1));
                if (!ContainsTownTile(canonicalX - 1, y)) Edge(origin + new Vector2(0, t), new Vector2(0, -1), new Vector2(1, 0));
            }

        // One tile edge, running along `along` from `start`; three dashes per edge.
        void Edge(Vector2 start, Vector2 along, Vector2 inward)
        {
            var from = start + inward * inset;
            if (!dashed)
            {
                DrawLine(from, from + along * t, dash with { A = 0.9f }, 1f);
                return;
            }
            for (var d = t * 0.08f; d < t; d += t / 3f)
            {
                var a = from + along * d;
                var b = from + along * Math.Min(t, d + t / 5.5f);
                DrawLine(a + new Vector2(1, 1), b + new Vector2(1, 1), shadow, width);
                DrawLine(a, b, dash, width);
            }
        }

        bool ContainsTownTile(int x, int y) => y >= 0 && y < world.Height &&
            (wrapsEastWest ? townBorderTiles.Contains(new Vector2I(Mod(x, world.Width), y)) :
                x >= 0 && x < world.Width && townBorderTiles.Contains(new Vector2I(x, y)));
    }

    /// <summary>
    /// Building roofs at their footprints (a flat roof color when zoomed out),
    /// drawn again one world-width away when the map wraps so a building on
    /// the seam stays whole.
    /// </summary>
    private void DrawBuildings((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null || buildings.Count == 0 || tileSize <= 0) return;
        var visible = new Rect2I(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var atlasSize = BuildingSprites.AtlasTileSize(tileSize);
        foreach (var (footprint, kind, door) in buildings)
        {
            foreach (var shift in wrapsEastWest ? new[] { -world.Width, 0, world.Width } : [0])
            {
                var placed = footprint with { Position = footprint.Position + new Vector2I(shift, 0) };
                if (!placed.Intersects(visible)) continue;
                var rect = new Rect2(placed.Position.X * stride, placed.Position.Y * stride,
                    placed.Size.X * stride - tileGap, placed.Size.Y * stride - tileGap);
                if (tileSize < SpriteTileMinimum)
                    DrawRect(rect, BuildingSprites.RoofColor(kind));
                else
                    DrawTextureRect(BuildingSprites.Texture(kind, footprint.Size.X, footprint.Size.Y, atlasSize, door), rect, false);
            }
        }
    }

    /// <summary>
    /// Packed-dirt Road pieces joined along the Road, with smooth diagonals
    /// and doorstep paths. At overview zoom the same links are drawn as flat
    /// lines.
    /// </summary>
    private void DrawRoads((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null || roadTiles.Count == 0 || tileSize <= 0) return;
        if (tileSize < SpriteTileMinimum)
        {
            DrawRoadLines(bounds, stride, RoadSprites.WornEdge, Math.Clamp(tileSize / 2.2f, 1.5f, 6f));
            DrawRoadLines(bounds, stride, RoadSprites.Dirt, Math.Clamp(tileSize / 3.5f, 1f, 4f));
            return;
        }
        var atlasSize = BuildingSprites.AtlasTileSize(tileSize);
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                var links = RoadLinksAt(x, y);
                if (!RoadSprites.Draws(links)) continue;
                var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                if (links.HasFlag(RoadLinks.Road)) links |= doorsteps.GetValueOrDefault(new Vector2I(mapX, y));
                var variant = (int)(PixelArt.Hash(mapX, y, 7) % RoadSprites.VariantCount);
                var dark = RoadSprites.NeedsDarkEdge(world.StyleAt(mapX, y));
                DrawTextureRect(RoadSprites.Texture(links, variant, atlasSize, dark),
                    new Rect2(x * stride, y * stride, tileSize, tileSize), false);
            }
    }

    /// <summary>Which of a tile's neighbours are Road, and whether it is Road itself.</summary>
    private RoadLinks RoadLinksAt(int x, int y)
    {
        var links = RoadLinks.None;
        if (IsRoad(x, y)) links |= RoadLinks.Road;
        if (IsRoad(x, y - 1)) links |= RoadLinks.North;
        if (IsRoad(x + 1, y)) links |= RoadLinks.East;
        if (IsRoad(x, y + 1)) links |= RoadLinks.South;
        if (IsRoad(x - 1, y)) links |= RoadLinks.West;
        // Diagonals only matter for Road tiles and for tiles between two Roads.
        if (!links.HasFlag(RoadLinks.Road) && System.Numerics.BitOperations.PopCount((uint)links) < 2) return links;
        if (IsRoad(x + 1, y - 1)) links |= RoadLinks.NorthEast;
        if (IsRoad(x + 1, y + 1)) links |= RoadLinks.SouthEast;
        if (IsRoad(x - 1, y + 1)) links |= RoadLinks.SouthWest;
        if (IsRoad(x - 1, y - 1)) links |= RoadLinks.NorthWest;
        return links;
    }

    private bool IsRoad(int x, int y) => world is not null && y >= 0 && y < world.Height &&
        (wrapsEastWest || x >= 0 && x < world.Width) &&
        roadTiles.Contains(new Vector2I(wrapsEastWest ? Mod(x, world.Width) : x, y));

    private void DrawRoadLines((int Left, int Top, int Width, int Height) bounds, int stride, Color color, float width)
    {
        if (world is null) return;
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                if (!IsRoad(x, y)) continue;
                var center = new Vector2(x * stride + tileSize / 2f, y * stride + tileSize / 2f);
                DrawRect(new Rect2(center - new Vector2(width / 2f, width / 2f), new Vector2(width, width)), color);
                foreach (var (dx, dy) in new (int X, int Y)[] { (1, 0), (0, 1), (1, 1), (-1, 1) })
                {
                    if (!IsRoad(x + dx, y + dy)) continue;
                    // A diagonal is drawn only where no straight path joins the two tiles.
                    if (dx != 0 && dy != 0 && (IsRoad(x + dx, y) || IsRoad(x, y + dy))) continue;
                    DrawLine(center, center + new Vector2(dx * stride, dy * stride), color, width);
                }
            }
    }

    private void DrawBridges((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null || bridgeDecks.Count == 0 || tileSize <= 0) return;
        // A plank deck reaching a little onto each bank, with dark side rails
        // and, when large enough to read, cross planks.
        var deck = new Color("A47A4C");
        var rail = new Color("4F3522");
        var plank = new Color("7C5836");
        var breadth = Math.Max(2f, tileSize * 0.62f);
        var overhang = tileSize * 0.3f;
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                if (!bridgeDecks.TryGetValue(new Vector2I(mapX, y), out var eastWest)) continue;
                var center = new Vector2(x * stride + tileSize / 2f, y * stride + tileSize / 2f);
                var length = stride + overhang * 2;
                var size = eastWest ? new Vector2(length, breadth) : new Vector2(breadth, length);
                var rect = new Rect2(center - size / 2f, size);
                DrawRect(rect, deck);
                if (tileSize >= SpriteTileMinimum)
                {
                    var planks = Math.Max(2, tileSize / 6);
                    for (var index = 1; index < planks * 2; index++)
                    {
                        var offset = -length / 2f + index * length / (planks * 2);
                        var from = eastWest ? center + new Vector2(offset, -breadth / 2f) : center + new Vector2(-breadth / 2f, offset);
                        var to = eastWest ? center + new Vector2(offset, breadth / 2f) : center + new Vector2(breadth / 2f, offset);
                        DrawLine(from, to, plank, 1f);
                    }
                }
                var railWidth = Math.Max(1f, tileSize / 14f);
                if (eastWest)
                {
                    DrawLine(rect.Position, rect.Position + new Vector2(rect.Size.X, 0), rail, railWidth);
                    DrawLine(rect.End - new Vector2(rect.Size.X, 0), rect.End, rail, railWidth);
                }
                else
                {
                    DrawLine(rect.Position, rect.Position + new Vector2(0, rect.Size.Y), rail, railWidth);
                    DrawLine(rect.End - new Vector2(0, rect.Size.Y), rect.End, rail, railWidth);
                }
            }
    }

    /// <summary>A generated tree or natural-site sprite filling its tile.</summary>
    private void DrawCampResource(Vector2 position, NatureSprite sprite)
    {
        if (tileSize >= SpriteTileMinimum)
        {
            DrawNatureSprite(position, sprite);
            return;
        }
        // Overview zoom: one small dot in the resource's main color.
        var color = sprite switch
        {
            NatureSprite.WoodPile => new Color("8A6440"),
            NatureSprite.StoneOutcrop => new Color("8C8A82"),
            NatureSprite.FertileSoil => new Color("5E4A36"),
            NatureSprite.WildSeedPatch => new Color("C8B066"),
            NatureSprite.Depleted => new Color("77766D", 0.78f),
            _ => new Color("4F7A45"),
        };
        DrawCircle(position + new Vector2(tileSize * 0.5f, tileSize * 0.56f), Math.Max(1.5f, tileSize * 0.2f), color);
    }

    private void DrawNatureSprite(Vector2 position, NatureSprite sprite)
    {
        var size = NatureSprites.AtlasTileSize(tileSize);
        DrawTextureRectRegion(NatureSprites.Atlas(size), new Rect2(position, new Vector2(tileSize, tileSize)),
            NatureSprites.Region(sprite, size));
    }

    private void DrawTree(Vector2 position, byte tree)
    {
        if (tileSize >= SpriteTileMinimum && NatureSprites.ForTree(tree) is { } sprite)
        {
            DrawNatureSprite(position, sprite);
            return;
        }
        var center = position + new Vector2(tileSize * 0.5f, tileSize * 0.5f);
        if (tree == 9)
        {
            DrawCircle(center, Math.Max(2f, tileSize * 0.12f), new Color("795539"));
            DrawCircle(center - new Vector2(0, tileSize * 0.08f), Math.Max(2f, tileSize * 0.19f),
                new Color("6F9749"));
            return;
        }
        if (tree is 3 or 4)
        {
            DrawCircle(center, Math.Max(2f, tileSize * 0.16f), new Color("735036"));
            DrawCircle(center, Math.Max(1f, tileSize * 0.08f), new Color("A77C4C"));
            return;
        }
        if (tree is 5 or 6)
        {
            DrawCircle(center, Math.Max(2f, tileSize * 0.11f), new Color("735036"));
            DrawCircle(center - new Vector2(0, tileSize * 0.09f), Math.Max(2f, tileSize * 0.17f),
                tree == 6 ? new Color("7BA88B") : new Color("94B465"));
            return;
        }
        DrawCircle(center, Math.Max(2f, tileSize * 0.32f), new Color("273F2E"));
        var canopy = tree == 2 ? new Color("3E705D") : new Color("5F8744");
        DrawCircle(center - new Vector2(tileSize * 0.04f, tileSize * 0.05f),
            Math.Max(2f, tileSize * 0.27f), canopy);
        if (tileSize >= 20)
            DrawCircle(center - new Vector2(tileSize * 0.1f, tileSize * 0.12f),
                tileSize * 0.09f, tree == 2 ? new Color("7BA88B") : new Color("94B465"));
        if (tree == 7 && tileSize >= 14)
        {
            var fruitColor = new Color("DE8B4E");
            DrawCircle(center + new Vector2(tileSize * 0.13f, tileSize * 0.04f),
                Math.Max(1f, tileSize * 0.045f), fruitColor);
            DrawCircle(center + new Vector2(-tileSize * 0.12f, tileSize * 0.11f),
                Math.Max(1f, tileSize * 0.045f), fruitColor);
            DrawCircle(center + new Vector2(tileSize * 0.01f, -tileSize * 0.13f),
                Math.Max(1f, tileSize * 0.045f), fruitColor);
        }
    }
}
