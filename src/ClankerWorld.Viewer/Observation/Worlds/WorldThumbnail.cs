using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// A small picture of a world's land and water for the Load World list: the
/// commonest terrain in each block of tiles, at most <see cref="MaximumWidth"/>
/// pixels wide and packed like the world's own terrain. The catalog keeps it,
/// so the list can show it without reading the world's save again.
/// </summary>
public sealed record WorldThumbnail(int Width, int Height, string Encoding, string Data)
{
    public const int MaximumWidth = 96;

    public static WorldThumbnail From(SeededMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var width = Math.Min(MaximumWidth, map.Width);
        var height = Math.Clamp((int)Math.Round(width * (double)map.Height / map.Width), 1, map.Height);
        var terrain = new byte[checked(map.Width * map.Height)];
        foreach (var tile in map.Tiles)
            terrain[tile.Position.Y * map.Width + tile.Position.X] = checked((byte)tile.Terrain);
        var pixels = new byte[checked(width * height)];
        var counts = new int[256];
        for (var py = 0; py < height; py++)
        {
            var top = py * map.Height / height;
            var bottom = Math.Max(top + 1, (py + 1) * map.Height / height);
            for (var px = 0; px < width; px++)
            {
                var left = px * map.Width / width;
                var right = Math.Max(left + 1, (px + 1) * map.Width / width);
                Array.Clear(counts);
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                        counts[terrain[y * map.Width + x]]++;
                var commonest = 0;
                for (var kind = 1; kind < counts.Length; kind++)
                    if (counts[kind] > counts[commonest]) commonest = kind;
                pixels[py * width + px] = (byte)commonest;
            }
        }
        return new WorldThumbnail(width, height, "terrain-kind-v1", Convert.ToBase64String(pixels));
    }
}
