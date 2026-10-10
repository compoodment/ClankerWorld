using Godot;

namespace ClankerWorld.GodotClient.UI;

public partial class WorldTerrainLayer
{
    internal bool AutumnLeavesEnabled { get; private set; }
    internal int AutumnLeafDrawCount { get; private set; }

    /// <summary>Ground leaves follow the observed season and vanish outside autumn.</summary>
    public void SetAutumnLeaves(string? season)
    {
        var enabled = string.Equals(season, "autumn", StringComparison.OrdinalIgnoreCase);
        if (enabled == AutumnLeavesEnabled) return;
        AutumnLeavesEnabled = enabled;
        QueueRedraw();
    }

    private void DrawAutumnLeaves((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        AutumnLeafDrawCount = 0;
        if (!AutumnLeavesEnabled || world is null || tileSize < SpriteTileMinimum) return;
        // A neighboring trunk can drop a leaf into the camera. Keep the same
        // world-coordinate seeds when panning or displaying a wrapped copy.
        var left = wrapsEastWest ? bounds.Left - 1 : Math.Max(0, bounds.Left - 1);
        var right = wrapsEastWest ? bounds.Left + bounds.Width + 1 : Math.Min(world.Width, bounds.Left + bounds.Width + 1);
        for (var y = Math.Max(0, bounds.Top - 1); y < Math.Min(world.Height, bounds.Top + bounds.Height + 1); y++)
            for (var x = left; x < right; x++)
            {
                var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                if (!AutumnLeaves.FallsFrom(NatureSprites.ForTree(trees[y * world.Width + mapX]))) continue;
                for (var index = 0; index < AutumnLeaves.Count; index++)
                {
                    var leaf = AutumnLeaves.At(mapX, y, index, tileSize);
                    var rectangle = new Rect2(x * stride + leaf.X - mapX * tileSize, y * stride + leaf.Y - y * tileSize, leaf.Size, leaf.Size);
                    if (!rectangle.Intersects(new Rect2(visibleTiles.Position * stride, visibleTiles.Size * stride)) ||
                        !AutumnLeafGround(rectangle.Position, stride) ||
                        !AutumnLeafGround(new Vector2(rectangle.End.X - 1, rectangle.Position.Y), stride) ||
                        !AutumnLeafGround(new Vector2(rectangle.Position.X, rectangle.End.Y - 1), stride) ||
                        !AutumnLeafGround(rectangle.End - Vector2.One, stride)) continue;
                    DrawRect(rectangle, leaf.Color);
                    AutumnLeafDrawCount++;
                }
            }
    }

    private bool AutumnLeafGround(Vector2 pixel, int stride)
    {
        if (world is null) return false;
        var x = (int)MathF.Floor(pixel.X / stride);
        var y = (int)MathF.Floor(pixel.Y / stride);
        if (y < 0 || y >= world.Height || !wrapsEastWest && (x < 0 || x >= world.Width)) return false;
        if (wrapsEastWest) x = Mod(x, world.Width);
        var tile = new Vector2I(x, y);
        return world.StyleAt(x, y) is not (TerrainStyle.Ocean or TerrainStyle.Lake or TerrainStyle.River or TerrainStyle.ShallowWater) &&
            !roadTiles.Contains(tile) && !marketPlazaTiles.Contains(tile) && !bridgeDecks.ContainsKey(tile) && !buildingTiles.Contains(tile);
    }
}
