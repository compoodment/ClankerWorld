using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Static stock A pixels on clear Town ground, underneath night, labels, agents and weather.</summary>
public partial class StoredStockLayer : Control
{
    private WorldTerrainLayer? terrain;
    private StoredStockPile[] piles = [];
    private readonly List<StockPaint> atlasPaints = [];
    private readonly List<StockPaint> drawnFrame = [];
    private WorldTerrainMap? drawnWorld;
    private Rect2 drawnCamera;
    private int drawnSize;

    public StoredStockLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Follow(WorldTerrainLayer map) => terrain = map;

    public void SetPiles(IReadOnlyList<StoredStockPile> next)
    {
        if (next.SequenceEqual(piles)) return;
        piles = next.ToArray();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (terrain is null || !IsVisibleInTree()) return;
        if (piles.Length > 0 && (!ReferenceEquals(terrain.World, drawnWorld) ||
            terrain.VisibleTiles != drawnCamera || terrain.TileSize != drawnSize)) QueueRedraw();
    }

    public override void _Draw()
    {
        drawnFrame.Clear();
        if (terrain?.World is not { } world) return;
        drawnWorld = world;
        drawnCamera = terrain.VisibleTiles;
        drawnSize = terrain.TileSize;
        if (drawnSize < WorldTerrainLayer.SpriteTileMinimum) return;
        var atlas = BuildingSprites.AtlasTileSize(drawnSize);
        var scale = terrain.Stride / (float)atlas;
        foreach (var pile in piles)
        {
            var bounds = new Rect2(pile.Tile, Vector2.One);
            if (bounds.End.Y < drawnCamera.Position.Y || bounds.Position.Y > drawnCamera.End.Y) continue;
            var first = world.WrapsEastWest ? (int)MathF.Ceiling((drawnCamera.Position.X - bounds.End.X) / world.Width) : 0;
            var last = world.WrapsEastWest ? (int)MathF.Floor((drawnCamera.End.X - bounds.Position.X) / world.Width) : 0;
            for (var copy = first; copy <= last; copy++)
            {
                var shift = new Vector2(copy * world.Width, 0);
                if (!new Rect2(bounds.Position + shift, bounds.Size).Intersects(drawnCamera)) continue;
                atlasPaints.Clear();
                // Keep the proposal's world-pixel casts before scaling or translating a wrapped copy.
                var at = ((Vector2)pile.Tile + StoredStockArt.Offset(pile.Kind)) * atlas;
                StoredStockArt.Append(atlasPaints, pile.Kind, pile.Level, at, atlas);
                foreach (var paint in atlasPaints)
                    drawnFrame.Add(paint with
                    {
                        Area = new Rect2(paint.Area.Position * scale + shift * terrain.Stride,
                        paint.Area.Size * scale)
                    });
            }
        }
        foreach (var paint in drawnFrame) DrawRect(paint.Area, paint.Color);
    }
}
