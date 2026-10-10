using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Camera-bounded chimney smoke from occupied Houses and working forges, on the weather's paused clock.</summary>
public partial class SmokeLayer : Control
{
    private WorldTerrainLayer? terrain;
    private WeatherLayer? weather;
    private BuildingLight[] buildings = [];
    private readonly List<SmokeSpan> drawnFrame = [];
    private Rect2 drawnCamera;
    private int drawnSize;
    private double drawnTime = -1;

    public SmokeLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Follow(WorldTerrainLayer map, WeatherLayer clock)
    {
        terrain = map;
        weather = clock;
    }

    public void SetBuildings(IReadOnlyList<BuildingLight> next)
    {
        var active = next.Where(building => SmokeArt.InUse(building.Kind, building.Occupied, building.Working)).ToArray();
        if (active.SequenceEqual(buildings)) return;
        buildings = active;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (terrain is null || weather is null || !IsVisibleInTree()) return;
        var time = Math.Floor(weather.AnimationTime / SmokeArt.FrameSeconds) * SmokeArt.FrameSeconds;
        if (buildings.Length > 0 && (time != drawnTime || terrain.VisibleTiles != drawnCamera || terrain.TileSize != drawnSize))
            QueueRedraw();
    }

    public override void _Draw()
    {
        drawnFrame.Clear();
        if (terrain?.World is not { } world || weather is null) return;
        drawnCamera = terrain.VisibleTiles;
        drawnSize = terrain.TileSize;
        drawnTime = Math.Floor(weather.AnimationTime / SmokeArt.FrameSeconds) * SmokeArt.FrameSeconds;
        if (drawnSize < WorldTerrainLayer.SpriteTileMinimum) return;
        var atlas = BuildingSprites.AtlasTileSize(drawnSize);
        var scale = terrain.Stride / (float)atlas;
        for (var index = 0; index < buildings.Length; index++)
        {
            var building = buildings[index];
            var bounds = new Rect2(building.Footprint.Position, building.Footprint.Size).Grow(2);
            if (bounds.End.Y < drawnCamera.Position.Y || bounds.Position.Y > drawnCamera.End.Y) continue;
            var first = world.WrapsEastWest ? (int)MathF.Ceiling((drawnCamera.Position.X - bounds.End.X) / world.Width) : 0;
            var last = world.WrapsEastWest ? (int)MathF.Floor((drawnCamera.End.X - bounds.Position.X) / world.Width) : 0;
            for (var copy = first; copy <= last; copy++)
            {
                var shift = new Vector2(copy * world.Width, 0);
                if (!new Rect2(bounds.Position + shift, bounds.Size).Intersects(drawnCamera)) continue;
                if (building.Kind is not { } kind || SmokeArt.Source(kind, building.Footprint.Size.X,
                        building.Footprint.Size.Y, atlas, building.Door) is not { } source) continue;
                // Rasterize in the approved world-pixel space before scaling; local-space
                // float rounding can change a disc boundary by one pixel. Wrapped copies
                // reuse those same pixels and translate only their final draw coordinates.
                var atlasSource = source + (Vector2)building.Footprint.Position * atlas;
                SmokeArt.AppendColumn(drawnFrame, atlasSource, atlas, drawnTime, index, shift * terrain.Stride, scale);
            }
        }
        foreach (var span in drawnFrame) DrawRect(span.Area, span.Color);
    }
}
