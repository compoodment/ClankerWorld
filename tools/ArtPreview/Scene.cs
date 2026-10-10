using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>
/// The art a scene is drawn with. Defaults are the game's current generators;
/// a proposal replaces the families it redraws so the same scene can be
/// rendered with current and proposed art for comparison.
/// </summary>
public sealed class ArtSet
{
    public string Name = "current";
    public Func<TerrainStyle, int, int, Image> Tile = TerrainTextures.Tile;
    public Func<TerrainStyle, Color> GroundColour = TerrainTextures.BaseColor;
    public Func<int, int, Image> Hill = TerrainTextures.HillOverlay;
    public Func<TerrainStyle, int, int, int, Image> WaterTile = (style, x, y, size) =>
        WaterTextures.Atlas(size).GetImage().GetRegion((Rect2I)WaterTextures.Region(style, x, y, size));
    public Func<TerrainStyle, int, int, Image> EdgePiece = TerrainTransitions.Piece;
    public Func<int, int, int, Image> CoastPiece = CoastEdges.Piece;
    public Func<RoadLinks, int, int, bool, Image> Road = RoadSprites.Render;
    public Func<NatureSprite, int, Image> Nature = NatureSprites.Sprite;
    public Func<BuildingKind, int, int, int, BuildingDoor, Image> Building = BuildingSprites.Render;
    /// <summary>variant, life stage, facing (0 S, 1 SW, 2 W, 3 NW, 4 N, 5 NE, 6 E, 7 SE), frame, size.</summary>
    public Func<int, int, int, int, int, Image> Agent = (variant, stage, facing, frame, size) =>
        AgentSprites.Sprite(variant, stage, facing, (AgentFrame)frame, size);
    /// <summary>Bridge deck over one tile; null selects the earlier overhanging drawing for comparison.</summary>
    public Func<bool, int, Image>? Bridge = RoadSprites.BridgeDeck;
    /// <summary>
    /// Optional relief drawn over the whole ground pass, before roads: given
    /// the map and the tile size it returns an image of the full map
    /// (map.Width × tileSize by map.Height × tileSize) to blend on top, so
    /// mountains, peaks and hills can be drawn as landforms spanning many
    /// tiles from the map's elevation instead of one tile at a time. Null
    /// selects the earlier per-tile mountain tiles and hill overlays for comparison.
    /// </summary>
    public Func<WorldTerrainMap, int, Image?>? Relief = (map, size) =>
        ReliefRenderer.Render(map, new Rect2I(0, 0, map.Width, map.Height), size);
}

/// <summary>A proposal that also wants the reference scene drawn with its art.</summary>
public interface IArtSetProvider
{
    string Name { get; }
    void Apply(ArtSet set);
}

public sealed record SceneAgent(Vector2I Tile, int Variant, string Stage, int Facing = 0, int Frame = 0);
public sealed record SceneBuilding(Rect2I Footprint, BuildingKind Kind, BuildingDoor Door);

/// <summary>A small hand-laid Town corner that exercises every art family.</summary>
public sealed class SceneSpec
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Hydrology { get; init; } = [];
    public byte[] Surface { get; init; } = [];
    public byte[] Vegetation { get; init; } = [];
    public byte[] Elevation { get; init; } = [];
    public HashSet<Vector2I> Roads { get; } = [];
    public Dictionary<Vector2I, bool> Bridges { get; } = [];
    public List<SceneBuilding> Buildings { get; } = [];
    public Dictionary<Vector2I, NatureSprite> Nature { get; } = [];
    public List<SceneAgent> Agents { get; } = [];

    public WorldTerrainMap Map()
    {
        var length = Width * Height;
        var climate = new byte[length];
        Array.Fill(climate, (byte)2);
        var terrain = Convert.ToBase64String(new byte[length]);
        var layers = new OwnerWorldPackedMapLayers(Width, Height, "map-layers-v1",
            Convert.ToBase64String(climate), Convert.ToBase64String(Elevation), Convert.ToBase64String(Hydrology),
            Convert.ToBase64String(Surface), Convert.ToBase64String(Vegetation));
        return WorldTerrainMap.FromPacked(new OwnerWorldPackedTerrain(Width, Height, "terrain-kind-v1", terrain), layers);
    }

    /// <summary>
    /// A 32 × 20 tile mountain range for judging mountains, peaks and hills
    /// as whole landforms: a long massif with a peaked spine running
    /// west-north-west to east-south-east, a smaller outlier, a foothill band
    /// at the game's hill thresholds, a river skirting the range and a Town
    /// edge in the south-west.
    /// </summary>
    public static SceneSpec MountainRange()
    {
        const int w = 32, h = 20;
        var hydrology = new byte[w * h];
        var surface = new byte[w * h];
        var vegetation = new byte[w * h];
        var elevation = new byte[w * h];
        int I(int x, int y) => y * w + x;
        float Noise(float x, float y, int salt)
        {
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            float R(int a, int b) => PixelArt.Hash(a, b, salt) % 1000 / 1000f;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(R(ix, iy), R(ix + 1, iy), sx), Mathf.Lerp(R(ix, iy + 1), R(ix + 1, iy + 1), sx), sy);
        }
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                // Distance from the range's spine, a gently bending line.
                var spineY = 6.5f + x * 0.22f + MathF.Sin(x * 0.35f) * 1.2f;
                var across = MathF.Abs(y - spineY) / (3.4f + 1.2f * Noise(x * 0.3f, 0, 3));
                var along = Mathf.SmoothStep(1f, 5f, x) * Mathf.SmoothStep(31f, 25f, x);
                var range = MathF.Max(0, 1 - across) * along;
                // A small outlier massif to the north-east.
                var outlier = MathF.Max(0, 1 - new Vector2((x - 26f) / 3.2f, (y - 2.5f) / 2.4f).Length());
                var height = 150 + 115 * MathF.Max(range, outlier * 0.9f) + 22 * (Noise(x * 0.45f, y * 0.45f, 9) - 0.5f);
                elevation[I(x, y)] = (byte)Math.Clamp((int)height, 0, 255);
            }
        // A river along the south of the range, two tiles wide.
        for (var x = 0; x < w; x++)
        {
            var ry = Math.Clamp((int)MathF.Round(13.5f + MathF.Sin(x * 0.28f) * 1.2f), 0, h - 2);
            hydrology[I(x, ry)] = 3;
            hydrology[I(x, ry + 1)] = 3;
        }
        // Forest on the western foothills, a little bare rock high up.
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (hydrology[I(x, y)] != 0) continue;
                if (x < 7 && y < 9 && elevation[I(x, y)] < 215) { vegetation[I(x, y)] = 2; if (x < 4) surface[I(x, y)] = 5; }
                if (elevation[I(x, y)] is >= 205 and < 215 && Noise(x * 0.8f, y * 0.8f, 21) > 0.62f) surface[I(x, y)] = 2;
            }
        var scene = new SceneSpec { Width = w, Height = h, Hydrology = hydrology, Surface = surface, Vegetation = vegetation, Elevation = elevation };
        for (var x = 0; x < 9; x++) scene.Roads.Add(new(x, 19));
        scene.Roads.Add(new(5, 18));
        scene.Buildings.Add(new(new Rect2I(3, 17, 1, 2), BuildingKind.House, new BuildingDoor(DoorSide.South, 0)));
        scene.Buildings.Add(new(new Rect2I(6, 17, 1, 1), BuildingKind.House, new BuildingDoor(DoorSide.West, 0)));
        foreach (var (x, y) in new[] { (1, 1), (3, 2), (2, 4), (5, 3), (0, 6), (4, 6), (6, 1) })
            if (hydrology[I(x, y)] == 0 && elevation[I(x, y)] < 215) scene.Nature[new(x, y)] = (x + y) % 2 == 0 ? NatureSprite.Conifer : NatureSprite.Broadleaf;
        scene.Agents.Add(new(new(7, 19), 0, "adult", 6));
        return scene;
    }

    /// <summary>The standard reference scene: 20 × 12 tiles.</summary>
    public static SceneSpec TownCorner()
    {
        const int w = 20, h = 12;
        var hydrology = new byte[w * h];
        var surface = new byte[w * h];
        var vegetation = new byte[w * h];
        var elevation = new byte[w * h];
        Array.Fill(elevation, (byte)100);
        int I(int x, int y) => y * w + x;
        // River two tiles wide, a lake in the south-east corner.
        for (var y = 0; y < h; y++) { hydrology[I(15, y)] = 3; hydrology[I(16, y)] = 3; }
        for (var y = 9; y < h; y++) for (var x = 17; x < w; x++) hydrology[I(x, y)] = 2;
        // Mountains in the north-east with a hill band and some bare rock.
        elevation[I(19, 0)] = 250;
        elevation[I(18, 0)] = 225; elevation[I(18, 1)] = 225; elevation[I(19, 1)] = 225;
        foreach (var (x, y) in new[] { (17, 0), (17, 1), (17, 2), (17, 3), (18, 2), (19, 2), (18, 3), (19, 3), (17, 4), (18, 4) })
            elevation[I(x, y)] = 195;
        surface[I(17, 0)] = 2; surface[I(17, 1)] = 2; surface[I(18, 2)] = 2;
        // A dense grove in the north-west with forest grass around it.
        for (var y = 0; y < 3; y++) for (var x = 0; x < 4; x++) { surface[I(x, y)] = 5; vegetation[I(x, y)] = 2; }
        foreach (var (x, y) in new[] { (4, 0), (5, 0), (6, 0), (4, 1), (5, 1), (4, 2), (0, 3), (1, 3), (2, 3), (3, 3), (0, 4) })
            vegetation[I(x, y)] = 2;
        // Fields south of the Farmhouse.
        for (var y = 9; y < h; y++) for (var x = 0; x < 4; x++) surface[I(x, y)] = 7;
        var scene = new SceneSpec { Width = w, Height = h, Hydrology = hydrology, Surface = surface, Vegetation = vegetation, Elevation = elevation };
        // Main street with a bridge, a side street north and south, and a diagonal branch.
        for (var x = 0; x < w; x++) if (x is not (15 or 16)) scene.Roads.Add(new(x, 6));
        scene.Bridges[new(15, 6)] = true;
        scene.Bridges[new(16, 6)] = true;
        foreach (var y in new[] { 2, 3, 4, 5 }) scene.Roads.Add(new(8, y));
        foreach (var y in new[] { 7, 8 }) scene.Roads.Add(new(5, y));
        scene.Roads.Add(new(12, 5));
        scene.Roads.Add(new(13, 4));
        scene.Buildings.AddRange(
        [
            new(new Rect2I(6, 2, 2, 2), BuildingKind.Warehouse, new BuildingDoor(DoorSide.East, 1)),
            new(new Rect2I(9, 2, 1, 2), BuildingKind.House, new BuildingDoor(DoorSide.West, 0)),
            new(new Rect2I(4, 5, 1, 1), BuildingKind.House, new BuildingDoor(DoorSide.South, 0)),
            new(new Rect2I(10, 4, 2, 2), BuildingKind.Blacksmith, new BuildingDoor(DoorSide.South, 0)),
            new(new Rect2I(12, 7, 1, 1), BuildingKind.TailorShop, new BuildingDoor(DoorSide.North, 0)),
            new(new Rect2I(2, 7, 1, 2), BuildingKind.Farmhouse, new BuildingDoor(DoorSide.North, 0)),
            new(new Rect2I(4, 8, 1, 1), BuildingKind.Silo, new BuildingDoor(DoorSide.West, 0)),
            new(new Rect2I(6, 8, 1, 1), BuildingKind.House, new BuildingDoor(DoorSide.West, 0)),
            new(new Rect2I(13, 7, 2, 1), BuildingKind.House, new BuildingDoor(DoorSide.North, 0)),
            new(new Rect2I(18, 7, 1, 1), BuildingKind.House, new BuildingDoor(DoorSide.North, 0)),
        ]);
        var grove = new[] { NatureSprite.Broadleaf, NatureSprite.Conifer, NatureSprite.Broadleaf, NatureSprite.Conifer };
        for (var y = 0; y < 3; y++) for (var x = 0; x < 4; x++) scene.Nature[new(x, y)] = grove[(x + y) % 4];
        scene.Nature[new(2, 1)] = NatureSprite.BroadleafStump;
        scene.Nature[new(3, 2)] = NatureSprite.ConiferSapling;
        scene.Nature[new(5, 0)] = NatureSprite.Broadleaf;
        scene.Nature[new(4, 1)] = NatureSprite.Conifer;
        scene.Nature[new(1, 3)] = NatureSprite.Broadleaf;
        scene.Nature[new(3, 3)] = NatureSprite.BroadleafSapling;
        scene.Nature[new(0, 4)] = NatureSprite.OrchardFruiting;
        scene.Nature[new(1, 4)] = NatureSprite.OrchardPicked;
        scene.Nature[new(3, 4)] = NatureSprite.BerryBush;
        scene.Nature[new(12, 9)] = NatureSprite.WildGreens;
        scene.Nature[new(9, 9)] = NatureSprite.FiberPlant;
        scene.Nature[new(14, 10)] = NatureSprite.Reeds;
        scene.Nature[new(14, 1)] = NatureSprite.Reeds;
        scene.Nature[new(17, 2)] = NatureSprite.StoneOutcrop;
        scene.Nature[new(18, 3)] = NatureSprite.IronOutcrop;
        scene.Nature[new(14, 8)] = NatureSprite.ClayBank;
        scene.Nature[new(10, 9)] = NatureSprite.Depleted;
        scene.Nature[new(18, 5)] = NatureSprite.Broadleaf;
        scene.Nature[new(19, 8)] = NatureSprite.Conifer;
        scene.Nature[new(8, 10)] = NatureSprite.Broadleaf;
        scene.Agents.AddRange(
        [
            new(new(3, 6), 0, "adult", 6),
            new(new(8, 4), 1, "adult", 4),
            new(new(7, 7), 2, "child", 2),
            new(new(12, 6), 3, "elder", 0),
            new(new(11, 8), 4, "adult", 7),
            new(new(17, 6), 5, "adult", 6),
        ]);
        return scene;
    }
}

/// <summary>Composes a scene the way the map layer draws it, at 32 or 16 px per tile.</summary>
public static class SceneComposer
{
    public const float AgentSpriteScale = 1.35f;

    public static Image Render(SceneSpec spec, ArtSet art, int tileSize = 32)
    {
        var map = spec.Map();
        var atlasSize = TerrainTextures.AtlasTileSize(tileSize);
        var image = Image.CreateEmpty(spec.Width * tileSize, spec.Height * tileSize, false, Image.Format.Rgba8);
        image.Fill(new Color("000000"));
        var pieces = new List<(TerrainStyle Style, int Piece)>();
        for (var y = 0; y < spec.Height; y++)
            for (var x = 0; x < spec.Width; x++)
            {
                var style = map.StyleAt(x, y);
                var px = x * tileSize;
                var py = y * tileSize;
                if (TerrainTextures.IsWater(style))
                {
                    Place(image, art.WaterTile(style, x, y, atlasSize), px, py, tileSize);
                    TerrainTransitions.CollectWater(map, x, y, false, pieces);
                    foreach (var (over, piece) in pieces) Place(image, art.EdgePiece(over, piece, atlasSize), px, py, tileSize);
                    TerrainTransitions.CollectCoast(map, x, y, false, pieces);
                    if (pieces.Count > 0)
                    {
                        var river = style == TerrainStyle.River;
                        var shallow = CoastEdges.ShallowColor(style);
                        foreach (var (_, piece) in pieces)
                            Place(image, Tint(art.CoastPiece(river ? CoastEdges.RiverShallowRow : CoastEdges.ShallowRow, piece, atlasSize), shallow), px, py, tileSize);
                        if (!river)
                            foreach (var (_, piece) in pieces)
                                Place(image, Tint(art.CoastPiece(CoastEdges.FoamRow, piece, atlasSize), CoastEdges.Foam), px, py, tileSize);
                        foreach (var (land, piece) in pieces)
                            Place(image, Tint(art.CoastPiece(CoastEdges.LandRow, piece, atlasSize), art.GroundColour(land)), px, py, tileSize);
                    }
                    continue;
                }
                Place(image, art.Tile(style, TerrainTextures.VariantAt(x, y), atlasSize), px, py, tileSize);
                TerrainTransitions.Collect(map, x, y, false, pieces);
                foreach (var (over, piece) in pieces) Place(image, art.EdgePiece(over, piece, atlasSize), px, py, tileSize);
                if (art.Relief is null && map.IsHillAt(x, y))
                    Place(image, art.Hill((int)(PixelArt.Hash(x, y, 61) % TerrainTextures.VariantCount), atlasSize), px, py, tileSize);
            }
        if (art.Relief?.Invoke(map, tileSize) is { } relief)
            Sheet.Blend(image, relief, 0, 0);

        // Roads, with doorstep paths toward each building's door.
        var doorsteps = new Dictionary<Vector2I, RoadLinks>();
        foreach (var building in spec.Buildings)
        {
            if (building.Kind == BuildingKind.Silo) continue;
            var f = building.Footprint;
            var along = building.Door.Tile ?? 0;
            var (tile, toward) = building.Door.Side switch
            {
                DoorSide.North => (new Vector2I(f.Position.X + along, f.Position.Y - 1), RoadLinks.DoorSouth),
                DoorSide.East => (new Vector2I(f.End.X, f.Position.Y + along), RoadLinks.DoorWest),
                DoorSide.West => (new Vector2I(f.Position.X - 1, f.Position.Y + along), RoadLinks.DoorEast),
                _ => (new Vector2I(f.Position.X + along, f.End.Y), RoadLinks.DoorNorth),
            };
            doorsteps[tile] = doorsteps.GetValueOrDefault(tile) | toward;
        }
        // The approved deck stays inside its tile. Only a Road in line with
        // the deck joins it, matching WorldTerrainLayer.RoadLinksAt.
        var deckJoins = art.Bridge is not null;
        for (var y = 0; y < spec.Height; y++)
            for (var x = 0; x < spec.Width; x++)
            {
                var links = RoadLinksAt(spec, x, y, deckJoins);
                if (!RoadSprites.Draws(links) || spec.Bridges.ContainsKey(new(x, y))) continue;
                if (links.HasFlag(RoadLinks.Road)) links |= doorsteps.GetValueOrDefault(new Vector2I(x, y));
                var variant = (int)(PixelArt.Hash(x, y, 7) % RoadSprites.VariantCount);
                var dark = RoadSprites.NeedsDarkEdge(map.StyleAt(x, y));
                Place(image, art.Road(links, variant, BuildingSprites.AtlasTileSize(tileSize), dark), x * tileSize, y * tileSize, tileSize);
            }

        foreach (var (tile, eastWest) in spec.Bridges)
        {
            if (art.Bridge is { } bridge)
            {
                Place(image, bridge(eastWest, atlasSize), tile.X * tileSize, tile.Y * tileSize, tileSize);
                continue;
            }
            DrawCurrentBridge(image, tile, eastWest, tileSize);
        }

        foreach (var building in spec.Buildings)
        {
            var f = building.Footprint;
            var sprite = art.Building(building.Kind, f.Size.X, f.Size.Y, BuildingSprites.AtlasTileSize(tileSize), building.Door);
            Place(image, sprite, f.Position.X * tileSize, f.Position.Y * tileSize, tileSize * f.Size.X, tileSize * f.Size.Y);
        }

        var natureSize = NatureSprites.AtlasTileSize(tileSize);
        foreach (var (tile, sprite) in spec.Nature.OrderBy(entry => entry.Key.Y).ThenBy(entry => entry.Key.X))
            Place(image, art.Nature(sprite, natureSize), tile.X * tileSize, tile.Y * tileSize, tileSize);

        foreach (var agent in spec.Agents)
        {
            var drawn = (int)MathF.Round(tileSize * AgentSpriteScale);
            var sprite = art.Agent(agent.Variant, AgentSprites.StageIndex(agent.Stage), agent.Facing, agent.Frame, drawn >= 24 ? 32 : 16);
            var center = new Vector2(agent.Tile.X * tileSize + tileSize / 2f, agent.Tile.Y * tileSize + tileSize / 2f);
            Place(image, sprite, (int)MathF.Round(center.X - drawn / 2f), (int)MathF.Round(center.Y - drawn / 2f), drawn);
        }
        return image;
    }

    /// <summary>The current client's Road-to-Road and aligned Road-to-deck links.</summary>
    internal static RoadLinks RoadLinksAt(SceneSpec spec, int x, int y, bool deckJoins)
    {
        bool IsRoad(int px, int py) => spec.Roads.Contains(new(px, py));
        bool IsDeck(int px, int py, bool eastWest) => deckJoins &&
            spec.Bridges.TryGetValue(new(px, py), out var axis) && axis == eastWest;
        var links = RoadLinks.None;
        var road = IsRoad(x, y);
        if (road) links |= RoadLinks.Road;
        if (IsRoad(x, y - 1) || (road && IsDeck(x, y - 1, false))) links |= RoadLinks.North;
        if (IsRoad(x + 1, y) || (road && IsDeck(x + 1, y, true))) links |= RoadLinks.East;
        if (IsRoad(x, y + 1) || (road && IsDeck(x, y + 1, false))) links |= RoadLinks.South;
        if (IsRoad(x - 1, y) || (road && IsDeck(x - 1, y, true))) links |= RoadLinks.West;
        if (!road && System.Numerics.BitOperations.PopCount((uint)links) < 2) return links;
        if (IsRoad(x + 1, y - 1)) links |= RoadLinks.NorthEast;
        if (IsRoad(x + 1, y + 1)) links |= RoadLinks.SouthEast;
        if (IsRoad(x - 1, y + 1)) links |= RoadLinks.SouthWest;
        if (IsRoad(x - 1, y - 1)) links |= RoadLinks.NorthWest;
        return links;
    }

    /// <summary>Draws a sprite scaled to a square of <paramref name="size"/> px, nearest neighbour.</summary>
    private static void Place(Image target, Image sprite, int x, int y, int size) => Place(target, sprite, x, y, size, size);

    private static void Place(Image target, Image sprite, int x, int y, int width, int height)
    {
        if (sprite.GetWidth() == width && sprite.GetHeight() == height)
        {
            Sheet.Blend(target, sprite, x, y);
            return;
        }
        var scaled = sprite.Duplicate();
        scaled.Resize(width, height, Image.Interpolation.Nearest);
        Sheet.Blend(target, scaled, x, y);
    }

    /// <summary>A white mask multiplied by a colour, as DrawTextureRectRegion with a modulate does.</summary>
    public static Image Tint(Image mask, Color tint)
    {
        var result = mask.Duplicate();
        for (var y = 0; y < result.GetHeight(); y++)
            for (var x = 0; x < result.GetWidth(); x++)
            {
                var m = mask.GetPixel(x, y);
                result.SetPixel(x, y, new Color(m.R * tint.R, m.G * tint.G, m.B * tint.B, m.A * tint.A));
            }
        return result;
    }

    /// <summary>The earlier plank deck, retained for explicit comparisons.</summary>
    private static void DrawCurrentBridge(Image image, Vector2I tile, bool eastWest, int tileSize)
    {
        var deck = new Color("A47A4C");
        var rail = new Color("4F3522");
        var plank = new Color("7C5836");
        var breadth = Math.Max(2f, tileSize * 0.62f);
        var overhang = tileSize * 0.3f;
        var center = new Vector2(tile.X * tileSize + tileSize / 2f, tile.Y * tileSize + tileSize / 2f);
        var length = tileSize + overhang * 2;
        var size = eastWest ? new Vector2(length, breadth) : new Vector2(breadth, length);
        var rect = new Rect2(center - size / 2f, size);
        FillRect(image, rect, deck);
        if (tileSize >= 12)
        {
            var planks = Math.Max(2, tileSize / 6);
            for (var index = 1; index < planks * 2; index++)
            {
                var offset = -length / 2f + index * length / (planks * 2);
                var from = eastWest ? center + new Vector2(offset, -breadth / 2f) : center + new Vector2(-breadth / 2f, offset);
                var to = eastWest ? center + new Vector2(offset, breadth / 2f) : center + new Vector2(breadth / 2f, offset);
                Line(image, from, to, plank, 1f);
            }
        }
        var railWidth = Math.Max(1f, tileSize / 14f);
        if (eastWest)
        {
            Line(image, rect.Position, rect.Position + new Vector2(rect.Size.X, 0), rail, railWidth);
            Line(image, rect.End - new Vector2(rect.Size.X, 0), rect.End, rail, railWidth);
        }
        else
        {
            Line(image, rect.Position, rect.Position + new Vector2(0, rect.Size.Y), rail, railWidth);
            Line(image, rect.End - new Vector2(0, rect.Size.Y), rect.End, rail, railWidth);
        }
    }

    private static void FillRect(Image image, Rect2 rect, Color color)
    {
        var left = (int)MathF.Round(rect.Position.X);
        var top = (int)MathF.Round(rect.Position.Y);
        var right = (int)MathF.Round(rect.End.X);
        var bottom = (int)MathF.Round(rect.End.Y);
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
                if (x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight())
                    image.SetPixel(x, y, image.GetPixel(x, y).Blend(color));
    }

    public static void Line(Image image, Vector2 from, Vector2 to, Color color, float width)
    {
        var steps = Math.Max(1, (int)MathF.Ceiling(from.DistanceTo(to) * 2));
        var half = Math.Max(0, (int)MathF.Round(width / 2f) - (width <= 1.5f ? 1 : 0));
        var touched = new HashSet<Vector2I>();
        for (var step = 0; step <= steps; step++)
        {
            var point = from.Lerp(to, step / (float)steps);
            for (var dy = -half; dy <= half; dy++)
                for (var dx = -half; dx <= half; dx++)
                {
                    var p = new Vector2I((int)MathF.Floor(point.X) + dx, (int)MathF.Floor(point.Y) + dy);
                    if (!touched.Add(p)) continue;
                    if (p.X >= 0 && p.Y >= 0 && p.X < image.GetWidth() && p.Y < image.GetHeight())
                        image.SetPixel(p.X, p.Y, image.GetPixel(p.X, p.Y).Blend(color));
                }
        }
    }
}
