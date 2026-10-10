using Godot;

namespace ClankerWorld.GodotClient.UI;

public readonly record struct GraveMarker(string PersonId, Vector2I Tile, bool Headstone, float Opacity);
internal readonly record struct GraveDraw(Rect2 Area, bool Headstone, float Opacity, int AtlasSize);

/// <summary>Camera-bounded graves on open ground, beneath night, map labels and living agents.</summary>
public partial class GraveLayer : Control
{
    private WorldTerrainLayer? terrain;
    private GraveMarker[] graves = [];
    private readonly Dictionary<(bool Headstone, int Size), ImageTexture> textures = [];
    private readonly List<GraveDraw> drawnFrame = [];
    private WorldTerrainMap? drawnWorld;
    private Rect2 drawnCamera;
    private int drawnSize;

    public GraveLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Follow(WorldTerrainLayer map) => terrain = map;

    public void SetGraves(IReadOnlyList<GraveMarker> next)
    {
        if (next.SequenceEqual(graves)) return;
        graves = next.ToArray();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (terrain is null || !IsVisibleInTree()) return;
        if (graves.Length > 0 && (!ReferenceEquals(terrain.World, drawnWorld) ||
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
        foreach (var grave in graves)
        {
            var bounds = new Rect2(grave.Tile, Vector2.One);
            if (bounds.End.Y < drawnCamera.Position.Y || bounds.Position.Y > drawnCamera.End.Y) continue;
            var first = world.WrapsEastWest ? (int)MathF.Ceiling((drawnCamera.Position.X - bounds.End.X) / world.Width) : 0;
            var last = world.WrapsEastWest ? (int)MathF.Floor((drawnCamera.End.X - bounds.Position.X) / world.Width) : 0;
            for (var copy = first; copy <= last; copy++)
            {
                var shifted = new Rect2(bounds.Position + new Vector2(copy * world.Width, 0), bounds.Size);
                if (!shifted.Intersects(drawnCamera)) continue;
                drawnFrame.Add(new(new Rect2(shifted.Position * terrain.Stride, shifted.Size * terrain.Stride),
                    grave.Headstone, grave.Opacity, atlas));
            }
        }
        foreach (var draw in drawnFrame)
        {
            var key = (draw.Headstone, draw.AtlasSize);
            if (!textures.TryGetValue(key, out var texture))
            {
                texture = ImageTexture.CreateFromImage(GraveArt.Sprite(draw.Headstone, draw.AtlasSize));
                textures.Add(key, texture);
            }
            DrawTextureRect(texture, draw.Area, false, new Color(1, 1, 1, draw.Opacity));
        }
    }
}
