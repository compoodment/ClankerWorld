using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Small deterministic helpers for the client's code-generated pixel art.
/// Every image is generated from fixed seeds, so it is identical on every
/// machine and run and needs no bundled binary asset.
/// </summary>
internal static class PixelArt
{
    public static Color Shade(Color color, float amount) =>
        amount >= 0 ? color.Lightened(amount) : color.Darkened(-amount);

    /// <summary>Well-mixed tile hash, used to pick texture variants without visible repeats.</summary>
    public static uint Hash(int x, int y, int salt = 0)
    {
        unchecked
        {
            var hash = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)salt * 0xCB1AB31Fu;
            hash ^= hash >> 15;
            hash *= 0x2C1B3C6Du;
            hash ^= hash >> 12;
            hash *= 0x297A2D39u;
            hash ^= hash >> 15;
            return hash;
        }
    }

    public static void Put(Image image, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) return;
        image.SetPixel(x, y, color);
    }

    public static void Fill(Image image, Rect2I area, Color color)
    {
        for (var y = area.Position.Y; y < area.End.Y; y++)
            for (var x = area.Position.X; x < area.End.X; x++)
                Put(image, x, y, color);
    }

    /// <summary>Deterministic xorshift stream for placing art details.</summary>
    public struct Stream
    {
        private uint state;

        public Stream(uint seed) => state = seed == 0 ? 0x9E3779B9u : seed;

        public uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public int Range(int minimumInclusive, int maximumExclusive) =>
            maximumExclusive <= minimumInclusive
                ? minimumInclusive
                : minimumInclusive + (int)(Next() % (uint)(maximumExclusive - minimumInclusive));
    }
}
