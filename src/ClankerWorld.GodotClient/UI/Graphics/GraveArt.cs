using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The second-round grave sprites, drawn on a 32 px grid like the game's
/// approved art: a dark outline, light from the north-west, shade to the
/// south-east and a soft shadow cast south-east on the ground.
/// </summary>
public static class GraveArt
{
    private static readonly Dictionary<char, Color> Palette = new()
    {
        [','] = new Color(0.05f, 0.08f, 0.05f, 0.28f),
        ['O'] = new("1E1712"),
        ['E'] = new("3F2A1A"),
        ['S'] = new("6E4E31"),
        ['B'] = new("8A6440"),
        ['L'] = new("A77C52"),
        ['H'] = new("D2AC77"),
        ['e'] = new("2B2E33"),
        ['s'] = new("62666E"),
        ['b'] = new("80858E"),
        ['l'] = new("9A9FA7"),
        ['h'] = new("B9BEC4"),
        ['i'] = new("4A4E55"),
        ['d'] = new("6E5538"),
        ['m'] = new("977852"),
        ['n'] = new("B99A6B"),
        ['g'] = new("4A7033"),
        ['G'] = new("6E9A48"),
        ['M'] = new("5B7A3A"),
    };

    private sealed class Grid
    {
        public readonly char[,] Cells = new char[32, 32];
        public Grid() { for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) Cells[x, y] = '.'; }
        public void Rect(int x0, int y0, int x1, int y1, char c) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Set(x, y, c); }
        public void Set(int x, int y, char c) { if (x is >= 0 and < 32 && y is >= 0 and < 32) Cells[x, y] = c; }
        public void Shadow(int x0, int y0, int x1, int y1) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) if (x is >= 0 and < 32 && y is >= 0 and < 32 && Cells[x, y] == '.') Cells[x, y] = ','; }

        /// <summary>A low earth mound: dark rim, mid fill, a lit north-west crown and grass tufts at its foot.</summary>
        public void Mound(int cx, int cy, float rx, float ry)
        {
            for (var y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                for (var x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                {
                    var d = MathF.Sqrt(MathF.Pow((x + 0.5f - cx) / rx, 2) + MathF.Pow((y + 0.5f - cy) / ry, 2));
                    if (d > 1) continue;
                    var lit = (x + 0.5f - cx) / rx + (y + 0.5f - cy) / ry < -0.55f;
                    Set(x, y, d > 0.82f ? 'd' : lit ? 'n' : 'm');
                }
            foreach (var (x, y, c) in new[] { (cx - (int)rx - 1, cy + 1, 'g'), (cx - (int)rx, cy, 'G'), (cx + (int)rx, cy + 1, 'g'), (cx + (int)rx + 1, cy, 'G'), (cx + 2, cy + (int)ry + 1, 'g') })
                Set(x, y, c);
        }

        public Image ToImage()
        {
            var bytes = new byte[32 * 32 * 4];
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var c = Palette.TryGetValue(Cells[x, y], out var colour) ? colour : new Color(0, 0, 0, 0);
                    var i = (y * 32 + x) * 4;
                    bytes[i] = (byte)Math.Round(c.R * 255); bytes[i + 1] = (byte)Math.Round(c.G * 255);
                    bytes[i + 2] = (byte)Math.Round(c.B * 255); bytes[i + 3] = (byte)Math.Round(c.A * 255);
                }
            return Image.CreateFromData(32, 32, false, Image.Format.Rgba8, bytes);
        }
    }

    public static Image Cross()
    {
        var g = new Grid();
        g.Shadow(15, 8, 20, 27); g.Shadow(10, 12, 25, 16);
        g.Rect(13, 5, 18, 26, 'O'); g.Rect(8, 10, 23, 14, 'O');
        g.Rect(14, 6, 17, 25, 'B'); g.Rect(9, 11, 22, 13, 'B');
        // Light on the north and west edges, shade on the south and east.
        g.Rect(14, 6, 17, 6, 'H'); g.Rect(14, 7, 14, 25, 'L'); g.Rect(9, 11, 22, 11, 'L'); g.Rect(9, 11, 9, 13, 'H'); g.Set(14, 6, 'H');
        g.Rect(17, 7, 17, 25, 'S'); g.Rect(10, 13, 22, 13, 'S'); g.Rect(18, 11, 22, 11, 'L');
        // Grain and the joint where the arm crosses the post.
        g.Set(15, 16, 'E'); g.Set(16, 19, 'E'); g.Set(15, 21, 'S'); g.Set(12, 12, 'S'); g.Set(20, 12, 'S'); g.Rect(14, 13, 17, 13, 'E');
        g.Mound(16, 26, 8.5f, 3.2f);
        g.Rect(14, 24, 17, 24, 'S'); // the post entering the mound
        return g.ToImage();
    }

    public static Image Headstone()
    {
        var g = new Grid();
        g.Shadow(12, 9, 24, 27);
        // A plinth, then the stone with a rounded top.
        g.Rect(8, 22, 23, 26, 'O'); g.Rect(9, 23, 22, 25, 's'); g.Rect(9, 23, 22, 23, 'b'); g.Rect(9, 23, 9, 25, 'l'); g.Rect(10, 25, 22, 25, 'e');
        g.Rect(10, 8, 21, 22, 'O'); g.Rect(12, 6, 19, 7, 'O'); g.Set(11, 7, 'O'); g.Set(20, 7, 'O');
        g.Rect(11, 9, 20, 21, 'b'); g.Rect(12, 8, 19, 8, 'b'); g.Rect(13, 7, 18, 7, 'h');
        g.Rect(12, 8, 19, 8, 'h'); g.Rect(11, 9, 11, 21, 'l'); g.Set(11, 9, 'h');
        g.Rect(20, 9, 20, 21, 's'); g.Rect(12, 21, 20, 21, 's');
        // A carved cross and two lines of lettering.
        g.Rect(15, 10, 16, 14, 'i'); g.Rect(13, 11, 18, 12, 'i'); g.Rect(15, 10, 15, 14, 'e');
        g.Rect(13, 16, 18, 16, 'i'); g.Rect(14, 18, 17, 18, 'i');
        // A little moss low on the west side.
        g.Set(11, 19, 'M'); g.Set(11, 20, 'M'); g.Set(12, 20, 'M'); g.Set(9, 24, 'M');
        g.Mound(16, 28, 7.5f, 2.6f);
        return g.ToImage();
    }
    /// <summary>Stable SHA-256 parity of the saved person ID selects one of the two approved markers.</summary>
    public static bool HeadstoneFor(string personId) =>
        (System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(personId))[0] & 1) != 0;

    public static Image Sprite(bool headstone, int size)
    {
        var image = headstone ? Headstone() : Cross();
        if (size == 32) return image;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        // Use the approved preview's top-left nearest samples explicitly,
        // so the native engine and preview select identical mid-zoom pixels.
        var source = image.GetData();
        var bytes = new byte[checked(size * size * 4)];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                Array.Copy(source, ((y * 32 / size) * 32 + x * 32 / size) * 4,
                    bytes, (y * size + x) * 4, 4);
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, bytes);
    }

    /// <summary>Hold for a recorded calendar year, then fade over one recorded calendar day.</summary>
    public static float Opacity(long elapsedTicks, int ticksPerDay, int daysPerYear)
    {
        if (elapsedTicks < 0) return 0;
        var year = (long)ticksPerDay * daysPerYear;
        return 1 - (float)Math.Clamp((elapsedTicks - year) / (double)ticksPerDay, 0, 1);
    }
}

