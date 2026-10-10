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

    /// <summary>
    /// A colour moved to the nearest of the 8-bit steps an image stores.
    /// Godot truncates a channel when it stores it, so a blended or
    /// part-transparent colour is snapped first to keep the game's pixels
    /// exactly as drawn in the reviewed pictures.
    /// </summary>
    public static Color Snap(Color color) => new(Step(color.R), Step(color.G), Step(color.B), Step(color.A));

    private static float Step(float channel) => Math.Clamp(MathF.Round(channel * 255f), 0f, 255f) / 255f;

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

    /// <summary>
    /// A 16 px drawing made from an approved 32 px sprite (owner approval
    /// for the handcart and boat, October 7, #914). Each 2 × 2 block becomes
    /// its most common solid colour, or on a tie the one nearest the block's
    /// average brightness; a block with a single solid pixel keeps it, so
    /// one-pixel shafts and oars stay; a block of only shadow keeps the
    /// shadow. Then every pixel on the outside of the silhouette takes the
    /// darkest colour of its block, so the drawing keeps its dark rim.
    /// </summary>
    public static Image HalveSprite(Image approved)
    {
        var small = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        small.Fill(Colors.Transparent);
        var rims = new Color?[16, 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                var block = new[]
                {
                    approved.GetPixel(2 * x, 2 * y), approved.GetPixel(2 * x + 1, 2 * y),
                    approved.GetPixel(2 * x, 2 * y + 1), approved.GetPixel(2 * x + 1, 2 * y + 1),
                };
                var solid = block.Where(c => c.A > 0.6f).ToArray();
                if (solid.Length > 0)
                {
                    var mean = solid.Average(c => c.Luminance);
                    var pick = solid.GroupBy(c => (MathF.Round(c.R * 255), MathF.Round(c.G * 255), MathF.Round(c.B * 255)))
                        .OrderByDescending(group => group.Count())
                        .ThenBy(group => MathF.Abs(group.First().Luminance - mean))
                        .First().First();
                    small.SetPixel(x, y, pick);
                    rims[x, y] = solid.MinBy(c => c.Luminance);
                    continue;
                }
                var shade = block.Where(c => c.A > 0.02f).ToArray();
                if (shade.Length >= 2) small.SetPixel(x, y, shade[0]);
            }
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < 16 && y < 16 && rims[x, y] is not null;
        var rimmed = (Image)small.Duplicate();
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                if (rims[x, y] is { } rim && (!Solid(x + 1, y) || !Solid(x - 1, y) || !Solid(x, y + 1) || !Solid(x, y - 1)) &&
                    small.GetPixel(x, y).Luminance - rim.Luminance > 0.12f)
                    rimmed.SetPixel(x, y, rim);
        return rimmed;
    }
}
