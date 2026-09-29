using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A data-drawn atlas of the current world, not a second set of terrain art.
/// Its bright rectangle is the part visible in the main world view.
/// </summary>
public partial class WorldOverview : Control
{
    private Texture2D? atlasTexture;
    private int mapWidth;
    private int mapHeight;
    private Rect2 visibleTiles;
    private readonly HashSet<Vector2I> roadTiles = [];
    private bool dragging;
    private Vector2 dragOffset;

    public event Action<Vector2>? CenterRequested;

    public Rect2 VisibleTiles => visibleTiles;
    public bool WrapsEastWest { get; set; }
    public bool ShowCameraBounds { get; set; } = true;
    public Vector2? MarkerTile { get; set; }

    public WorldOverview()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        CustomMinimumSize = new Vector2(230, 130);
        TooltipText = "Click to jump there, or drag the bright box to move the view.";
        Resized += QueueRedraw;
    }

    public void SetWorld(WorldTerrainMap world)
    {
        mapWidth = world.Width;
        mapHeight = world.Height;
        roadTiles.Clear();
        // The overview is data art, not a second sprite set. Each atlas pixel
        // summarizes its part of the world, so redraw cost is bounded by the
        // atlas resolution instead of millions of canvas rectangles.
        var atlasWidth = Math.Min(mapWidth, 256);
        var atlasHeight = Math.Min(mapHeight, 128);
        var pixelCount = checked(atlasWidth * atlasHeight);
        var colors = new float[checked(pixelCount * 4)];
        var counts = new int[pixelCount];
        for (var y = 0; y < mapHeight; y++)
        {
            var atlasY = y * atlasHeight / mapHeight;
            for (var x = 0; x < mapWidth; x++)
            {
                var atlasX = x * atlasWidth / mapWidth;
                var pixel = atlasY * atlasWidth + atlasX;
                var color = world.DisplayColorAt(x, y);
                var offset = pixel * 4;
                colors[offset] += color.R;
                colors[offset + 1] += color.G;
                colors[offset + 2] += color.B;
                colors[offset + 3] += color.A;
                counts[pixel]++;
            }
        }
        var image = Image.CreateEmpty(atlasWidth, atlasHeight, false, Image.Format.Rgba8);
        for (var y = 0; y < atlasHeight; y++)
        {
            for (var x = 0; x < atlasWidth; x++)
            {
                var pixel = y * atlasWidth + x;
                var offset = pixel * 4;
                var scale = 1f / Math.Max(1, counts[pixel]);
                image.SetPixel(x, y, new Color(colors[offset] * scale, colors[offset + 1] * scale,
                    colors[offset + 2] * scale, colors[offset + 3] * scale));
            }
        }
        atlasTexture = ImageTexture.CreateFromImage(image);
        QueueRedraw();
    }

    public void SetVisibleTiles(Rect2 bounds)
    {
        visibleTiles = bounds;
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

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("101A1E"));
        if (mapWidth <= 0 || mapHeight <= 0)
        {
            return;
        }

        var atlas = AtlasRect();
        DrawRect(atlas, new Color("273A3D"));
        if (atlasTexture is not null) DrawTextureRect(atlasTexture, atlas, tile: false);

        var roadColor = new Color("D9BC87");
        foreach (var tile in roadTiles)
        {
            var center = atlas.Position + new Vector2(
                (tile.X + 0.5f) * atlas.Size.X / mapWidth,
                (tile.Y + 0.5f) * atlas.Size.Y / mapHeight);
            DrawRect(new Rect2(center - Vector2.One, Vector2.One * 2), roadColor);
        }

        DrawRect(atlas, new Color("AFC4BA"), filled: false, width: 1);
        if (MarkerTile is { } markerTile)
        {
            var marker = atlas.Position + new Vector2(
                markerTile.X * atlas.Size.X / mapWidth,
                markerTile.Y * atlas.Size.Y / mapHeight);
            DrawRect(new Rect2(marker - new Vector2(3, 3), new Vector2(6, 6)),
                new Color("FFF0B5"));
        }
        if (!ShowCameraBounds) return;
        var top = atlas.Position.Y + visibleTiles.Position.Y * atlas.Size.Y / mapHeight;
        var height = Math.Max(2, visibleTiles.Size.Y * atlas.Size.Y / mapHeight);
        if (!WrapsEastWest || visibleTiles.Size.X >= mapWidth)
        {
            var left = WrapsEastWest ? atlas.Position.X :
                atlas.Position.X + visibleTiles.Position.X * atlas.Size.X / mapWidth;
            var width = WrapsEastWest ? atlas.Size.X :
                Math.Max(2, visibleTiles.Size.X * atlas.Size.X / mapWidth);
            DrawCameraBounds(new Rect2(left, top, width, height));
            return;
        }

        // The camera may straddle either side of the cylindrical seam. Draw
        // both visible pieces on the atlas rather than a rectangle outside it.
        for (var copy = -1; copy <= 1; copy++)
        {
            var start = Math.Max(0, visibleTiles.Position.X - copy * mapWidth);
            var end = Math.Min(mapWidth, visibleTiles.End.X - copy * mapWidth);
            if (end <= start) continue;
            DrawCameraBounds(new Rect2(
                atlas.Position.X + start * atlas.Size.X / mapWidth, top,
                Math.Max(2, (end - start) * atlas.Size.X / mapWidth), height));
        }
    }

    private void DrawCameraBounds(Rect2 bounds)
    {
        DrawRect(bounds, new Color("FFF0B5", 0.15f));
        DrawRect(bounds, new Color("FFF0B5"), filled: false, width: 2);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (mapWidth <= 0 || mapHeight <= 0)
        {
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            dragging = button.Pressed && AtlasRect().HasPoint(button.Position);
            if (dragging)
            {
                var point = ToCameraTilePoint(button.Position);
                dragOffset = visibleTiles.HasPoint(point) ? point - visibleTiles.GetCenter() : Vector2.Zero;
                CenterRequested?.Invoke(point - dragOffset);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && dragging)
        {
            CenterRequested?.Invoke(ToCameraTilePoint(motion.Position) - dragOffset);
            AcceptEvent();
        }
    }

    private Vector2 ToTilePoint(Vector2 position)
    {
        var atlas = AtlasRect();
        return new Vector2(
            WrapsEastWest
                ? (position.X - atlas.Position.X) * mapWidth / atlas.Size.X
                : Math.Clamp((position.X - atlas.Position.X) * mapWidth / atlas.Size.X, 0, mapWidth),
            Math.Clamp((position.Y - atlas.Position.Y) * mapHeight / atlas.Size.Y, 0, mapHeight));
    }

    private Vector2 ToCameraTilePoint(Vector2 position)
    {
        var point = ToTilePoint(position);
        if (WrapsEastWest)
            point.X += MathF.Round((visibleTiles.GetCenter().X - point.X) / mapWidth) * mapWidth;
        return point;
    }

    private Rect2 AtlasRect()
    {
        var available = Size - new Vector2(12, 12);
        var scale = Math.Min(available.X / mapWidth, available.Y / mapHeight);
        var size = new Vector2(mapWidth * scale, mapHeight * scale);
        return new Rect2((Size - size) / 2, size);
    }
}
