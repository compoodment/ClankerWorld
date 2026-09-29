using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The ClankerWorld logo: wood-grain letters either side of a friendly robot
/// waving in front of a little planet. It is drawn from fixed pixel data at
/// 181 × 42 logical pixels and shown at whole-number scales so it stays crisp.
/// <see cref="Icon"/> gives the same robot and planet as a square icon.
/// </summary>
public static class MenuLogo
{
    public const int Width = 181;
    public const int Height = 42;

    private static readonly Dictionary<char, string[]> Letters = new()
    {
        ['C'] = ["..#######", ".########", "###......", "###......", "###......", "###......", "###......", "###......", "###......", "###......", ".########", "..#######"],
        ['L'] = ["###.....", "###.....", "###.....", "###.....", "###.....", "###.....", "###.....", "###.....", "###.....", "###.....", "########", "########"],
        ['A'] = ["..#####..", ".#######.", "###...###", "###...###", "###...###", "#########", "#########", "###...###", "###...###", "###...###", "###...###", "###...###"],
        ['N'] = ["###....###", "###....###", "####...###", "#####..###", "######.###", "###.######", "###..#####", "###...####", "###....###", "###....###", "###....###", "###....###"],
        ['K'] = ["###...###", "###..###.", "###.###..", "######...", "#####....", "####.....", "#####....", "######...", "###.###..", "###..###.", "###...###", "###...###"],
        ['E'] = ["########", "########", "###.....", "###.....", "###.....", "#######.", "#######.", "###.....", "###.....", "###.....", "########", "########"],
        ['R'] = ["########.", "#########", "###...###", "###...###", "###...###", "#########", "########.", "###.###..", "###..###.", "###...###", "###...###", "###...###"],
        ['W'] = ["###......###", "###......###", "###......###", "###......###", "###..##..###", "###..##..###", "###.####.###", "###.####.###", "############", "#####..#####", "####....####", "###......###"],
        ['O'] = ["..#####..", ".#######.", "###...###", "###...###", "###...###", "###...###", "###...###", "###...###", "###...###", "###...###", ".#######.", "..#####.."],
        ['D'] = ["#######..", "########.", "###...###", "###...###", "###...###", "###...###", "###...###", "###...###", "###...###", "###...###", "########.", "#######.."],
    };

    private static readonly Color Outline = new("1A1E2C");
    private static readonly Color Light = new("E6EEF6");
    private static readonly Color Steel = new("B8C6D8");
    private static readonly Color DarkSteel = new("8492AC");
    private static readonly Color Screen = new("202C40");
    private static readonly Color Glow = new("88EEE4");
    private static readonly Color GlowDim = new("56B2B2");
    private static readonly Color Brass = new("D6A05C");
    private static readonly Color BrassDark = new("9E6A3A");
    private static readonly Color BrassLight = new("F4CC8C");
    private static readonly Color Scarf = new("72B456");
    private static readonly Color ScarfDark = new("4A8A42");

    public static Image Create()
    {
        var canvas = new Canvas(Width, Height);
        var left = TextMask("CLANKER", out var leftStarts);
        var right = TextMask("WORLD", out var rightStarts);
        const int gap = 3, emblemWidth = 38, emblemHeight = 40, textTop = 11;
        var rightX = 3 + left.GetLength(1) + gap * 2 + emblemWidth;
        var letters = new bool[Height, Width];
        Stamp(letters, left, 3, textTop);
        Stamp(letters, right, rightX, textTop);
        WoodLetters(canvas, letters, textTop);
        foreach (var (starts, originX) in new[] { (leftStarts, 3), (rightStarts, rightX) })
            foreach (var start in starts)
            {
                canvas.Put(originX + start + 1, textTop + 2, new Color("D6D2C8"));
                canvas.Put(originX + start + 1, textTop + 3, new Color("6E6862"));
            }
        var emblem = new Canvas(emblemWidth, emblemHeight);
        Planet(emblem, emblemWidth / 2f - 0.5f, 16.5f, 15.5f);
        emblem.Outline(emblem.Filled(), Outline);
        Robot(emblem, 7, 13);
        canvas.Paste(emblem, 3 + left.GetLength(1) + gap, 0);
        return canvas.ToImage();
    }

    /// <summary>Sizes written to the Windows program icon.</summary>
    public static readonly int[] IconSizes = [16, 32, 48, 64, 128, 256];

    /// <summary>
    /// The robot and planet on their own, for the window and program icon.
    /// 16, 32 and 48 px are separate pixel designs; 64, 128 and 256 px double
    /// the 32 px design so every size stays crisp.
    /// </summary>
    public static Image Icon(int size)
    {
        if (size > 48 && size % 32 == 0)
        {
            var image = Icon(32);
            image.Resize(size, size, Image.Interpolation.Nearest);
            return image;
        }
        if (size == 48)
        {
            var large = new Canvas(48, 48);
            Planet(large, 23.5f, 20.5f, 20);
            large.Outline(large.Filled(), Outline);
            Robot(large, 12, 20);
            return large.ToImage();
        }
        if (size >= 32)
        {
            var canvas = new Canvas(32, 32);
            Planet(canvas, 15.5f, 13.5f, 13);
            canvas.Outline(canvas.Filled(), Outline);
            Robot(canvas, 4, 5);
            return canvas.ToImage();
        }
        var small = new Canvas(16, 16);
        Planet(small, 7.5f, 7.5f, 6.5f);
        small.Outline(small.Filled(), Outline);
        var head = new Dictionary<Vector2I, Color>();
        for (var y = 6; y < 11; y++)
            for (var x = 4; x < 12; x++)
                head[new Vector2I(x, y)] = x < 10 ? Light : Steel;
        foreach (var corner in new Vector2I[] { new(4, 6), new(11, 6), new(4, 10), new(11, 10) }) head.Remove(corner);
        for (var y = 7; y < 10; y++)
            for (var x = 5; x < 11; x++)
                head[new Vector2I(x, y)] = Screen;
        head[new Vector2I(6, 8)] = Glow;
        head[new Vector2I(9, 8)] = Glow;
        head[new Vector2I(7, 9)] = Glow;
        head[new Vector2I(8, 9)] = Glow;
        head[new Vector2I(7, 4)] = new Color("FFDE7C");
        head[new Vector2I(8, 4)] = new Color("FFDE7C");
        head[new Vector2I(7, 5)] = DarkSteel;
        head[new Vector2I(8, 5)] = DarkSteel;
        small.Outline(Mask(head.Keys, 16, 16), Outline, diagonal: false);
        foreach (var (point, color) in head) small.Put(point.X, point.Y, color);
        return small.ToImage();
    }

    /// <summary>A Windows .ico holding every <see cref="IconSizes"/> image as PNG data.</summary>
    public static byte[] IconFile()
    {
        var images = IconSizes.Select(size => Icon(size).SavePngToBuffer()).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)IconSizes.Length);
        var offset = 6 + 16 * IconSizes.Length;
        for (var i = 0; i < IconSizes.Length; i++)
        {
            var edge = (byte)(IconSizes[i] >= 256 ? 0 : IconSizes[i]);
            writer.Write(edge);
            writer.Write(edge);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)images[i].Length);
            writer.Write((uint)offset);
            offset += images[i].Length;
        }
        foreach (var image in images) writer.Write(image);
        writer.Flush();
        return stream.ToArray();
    }

    private static bool[,] TextMask(string text, out List<int> starts)
    {
        const int spacing = 2;
        var width = text.Sum(letter => Letters[letter][0].Length) + spacing * (text.Length - 1);
        var mask = new bool[12, width];
        starts = [];
        var x = 0;
        foreach (var letter in text)
        {
            var rows = Letters[letter];
            starts.Add(x);
            for (var y = 0; y < rows.Length; y++)
                for (var column = 0; column < rows[y].Length; column++)
                    mask[y, x + column] = rows[y][column] == '#';
            x += rows[0].Length + spacing;
        }
        return mask;
    }

    private static void Stamp(bool[,] target, bool[,] source, int originX, int originY)
    {
        for (var y = 0; y < source.GetLength(0); y++)
            for (var x = 0; x < source.GetLength(1); x++)
                if (source[y, x]) target[originY + y, originX + x] = true;
    }

    private static bool[,] Mask(IEnumerable<Vector2I> points, int width, int height)
    {
        var mask = new bool[height, width];
        foreach (var point in points)
            if (point.X >= 0 && point.Y >= 0 && point.X < width && point.Y < height)
                mask[point.Y, point.X] = true;
        return mask;
    }

    /// <summary>Carved-wood fill with grain, a straight two-pixel drop and a dark outline.</summary>
    private static void WoodLetters(Canvas canvas, bool[,] letters, int top)
    {
        var height = letters.GetLength(0);
        var width = letters.GetLength(1);
        bool At(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && letters[y, x];
        bool Body(int x, int y) => At(x, y) || At(x, y - 1) || At(x, y - 2);
        var body = new bool[height, width];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                body[y, x] = Body(x, y);
        canvas.Outline(body, new Color("28180E"));
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (At(x, y)) continue;
                if (At(x, y - 1)) canvas.Put(x, y, new Color("684226"));
                else if (At(x, y - 2)) canvas.Put(x, y, new Color("563620"));
            }
        Color[] stops = [new("F0CC8C"), new("D29E60"), new("A66E3C")];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (!At(x, y)) continue;
                var row = Mathf.Clamp(y - top, 0, 11) / 11f * (stops.Length - 1);
                var index = Mathf.Min((int)row, stops.Length - 2);
                var color = stops[index].Lerp(stops[index + 1], row - index);
                if (!At(x, y + 1)) color = new Color("80522E");
                if (!At(x, y - 1)) color = new Color("FCE6B2");
                else if (y >= top + 2 && y <= top + 10 && PixelArt.Hash(x / 4, y, 3) % 4 == 0 && PixelArt.Hash(x, y, 5) % 3 != 0)
                    color = new Color(color.R * 0.88f, color.G * 0.88f, color.B * 0.88f);
                canvas.Put(x, y, color);
            }
    }

    /// <summary>A small planet lit from the upper left, with ice, beaches, forests, clouds and a glow.</summary>
    private static void Planet(Canvas canvas, float centerX, float centerY, float radius)
    {
        var land = new Dictionary<Vector2I, bool>();
        var points = new List<(int X, int Y, float U, float V)>();
        for (var y = (int)(centerY - radius - 1); y <= (int)(centerY + radius + 1); y++)
            for (var x = (int)(centerX - radius - 1); x <= (int)(centerX + radius + 1); x++)
            {
                if (new Vector2(x - centerX, y - centerY).Length() > radius) continue;
                var u = (x - centerX) / radius;
                var v = (y - centerY) / radius;
                var height = Mathf.Sin(u * 3.2f + 0.6f) * 0.9f + Mathf.Sin(v * 2.7f - u * 1.4f + 1.9f) * 0.8f +
                    Mathf.Sin((u - v) * 5.3f + 0.4f) * 0.45f + Mathf.Cos((u + v) * 6.1f) * 0.3f;
                land[new Vector2I(x, y)] = height > 0.35f;
                points.Add((x, y, u, v));
            }
        bool IsLand(int x, int y, bool outside) => land.TryGetValue(new Vector2I(x, y), out var value) ? value : outside;
        foreach (var (x, y, u, v) in points)
        {
            var isLand = IsLand(x, y, false);
            var coast = isLand && (!IsLand(x + 1, y, true) || !IsLand(x - 1, y, true) || !IsLand(x, y + 1, true) || !IsLand(x, y - 1, true));
            Color color;
            if (v < -0.88f || (v < -0.8f && Mathf.Sin(u * 9) > 0.3f)) color = new Color("EEF4F8");
            else if (isLand)
                color = coast ? new Color("DACA92")
                    : Mathf.Sin(u * 7.3f + v * 2.1f) + Mathf.Sin(v * 6.7f - 1.1f) > 0.6f ? new Color("4E8E48") : new Color("74B65A");
            else
            {
                var nearLand = false;
                for (var dy = -2; dy <= 2 && !nearLand; dy += 2)
                    for (var dx = -2; dx <= 2 && !nearLand; dx += 2)
                        nearLand = IsLand(x + dx, y + dy, false);
                color = nearLand ? new Color("6CACE0") : new Color("4684C6");
            }
            var light = -(u * 0.6f + v * 0.8f);
            if (light < -0.45f) color = !isLand && v >= -0.8f ? new Color("3264A2") : Darken(color, 0.72f);
            else if (light < -0.1f) color = Darken(color, 0.86f);
            else if (light > 0.65f && !isLand) color = new Color(Mathf.Min(1, color.R * 1.12f), Mathf.Min(1, color.G * 1.12f), Mathf.Min(1, color.B * 1.12f));
            canvas.Put(x, y, color);
        }
        foreach (var (startU, startV, length) in new[] { (-0.55f, -0.35f, 5), (0.1f, 0.25f, 6), (-0.2f, 0.62f, 4) })
            for (var k = 0; k < length; k++)
            {
                var x = (int)(centerX + startU * radius) + k;
                var y = (int)(centerY + startV * radius) + (k == 0 || k == length - 1 ? 1 : 0);
                if (new Vector2(x - centerX, y - centerY).Length() < radius - 1)
                    canvas.Put(x, y, canvas.Get(x, y).Lerp(new Color("FAFCFF"), 0.7f));
            }
        foreach (var (x, y, u, v) in points)
        {
            var distance = new Vector2(x - centerX, y - centerY).Length();
            if (distance > radius - 1 && (x - centerX) + (y - centerY) < radius * 0.3f)
                canvas.Put(x, y, canvas.Get(x, y).Lerp(new Color("B0E2F6"), 0.5f));
        }
    }

    private static Color Darken(Color color, float amount) => new(color.R * amount, color.G * amount, color.B * amount);

    /// <summary>The waving robot: screen face, rosy cheeks, heart light, scarf, brass bolts and boots.</summary>
    private static void Robot(Canvas canvas, int originX, int originY)
    {
        var pixels = new Dictionary<Vector2I, Color>();
        void Rect(int x0, int y0, int x1, int y1, Color color)
        {
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    pixels[new Vector2I(x, y)] = color;
        }
        void Dot(int x, int y, Color color) => pixels[new Vector2I(x, y)] = color;

        Rect(11, 2, 12, 4, DarkSteel);
        Dot(11, 2, Steel);
        Rect(11, 0, 12, 0, new Color("FFDE7C"));
        Rect(11, 1, 12, 1, new Color("FFA646"));
        Rect(4, 5, 19, 13, Light);
        Rect(18, 5, 19, 13, Steel);
        Rect(4, 13, 19, 13, Steel);
        Dot(18, 13, DarkSteel);
        Dot(19, 13, DarkSteel);
        foreach (var corner in new Vector2I[] { new(4, 5), new(19, 5), new(4, 13), new(19, 13) }) pixels.Remove(corner);
        Rect(6, 7, 17, 11, Screen);
        foreach (var (x, y) in new[] { (7, 9), (8, 8), (9, 8), (10, 9), (13, 9), (14, 8), (15, 8), (16, 9) }) Dot(x, y, Glow);
        foreach (var (x, y) in new[] { (10, 11), (11, 11), (12, 11), (13, 11) }) Dot(x, y, Glow);
        Dot(9, 10, GlowDim);
        Dot(14, 10, GlowDim);
        Dot(6, 10, new Color("F08496"));
        Dot(17, 10, new Color("F08496"));
        foreach (var boltX in new[] { 2, 20 })
        {
            Rect(boltX, 8, boltX + 1, 10, Brass);
            Dot(boltX, 8, BrassLight);
            Dot(boltX + 1, 10, BrassDark);
        }
        Rect(6, 14, 17, 14, Scarf);
        Rect(6, 15, 17, 15, ScarfDark);
        foreach (var (x, y, color) in new[] { (16, 16, Scarf), (17, 16, ScarfDark), (17, 17, Scarf), (18, 17, ScarfDark), (18, 18, Scarf) })
            Dot(x, y, color);
        Rect(6, 16, 15, 21, Light);
        Rect(14, 16, 17, 21, Steel);
        Rect(8, 17, 13, 20, DarkSteel);
        foreach (var (x, y) in new[] { (9, 17), (12, 17), (9, 18), (10, 18), (11, 18), (12, 18), (10, 19), (11, 19) }) Dot(x, y, new Color("FF7060"));
        Dot(9, 17, new Color("FFAA96"));
        Rect(6, 21, 17, 21, Brass);
        Dot(11, 21, BrassLight);
        Dot(12, 21, BrassLight);
        Rect(3, 16, 4, 19, DarkSteel);
        Dot(3, 16, Steel);
        Rect(2, 20, 4, 21, Steel);
        Dot(2, 20, Light);
        Dot(4, 21, DarkSteel);
        foreach (var (x, y) in new[] { (18, 16), (19, 16), (20, 15), (21, 15) }) Dot(x, y, DarkSteel);
        Rect(21, 12, 23, 14, Steel);
        Dot(21, 12, Light);
        Dot(23, 14, DarkSteel);
        Dot(24, 13, Steel);
        Rect(8, 22, 9, 23, DarkSteel);
        Rect(14, 22, 15, 23, DarkSteel);
        foreach (var bootX in new[] { 7, 13 })
        {
            Rect(bootX, 24, bootX + 3, 25, Brass);
            Dot(bootX, 24, BrassLight);
            Rect(bootX, 25, bootX + 3, 25, BrassDark);
        }

        var placed = pixels.Keys.Select(point => point + new Vector2I(originX, originY));
        canvas.Outline(Mask(placed, canvas.Width, canvas.Height), Outline);
        foreach (var (point, color) in pixels) canvas.Put(originX + point.X, originY + point.Y, color);
    }

    private sealed class Canvas(int width, int height)
    {
        private readonly Color?[,] pixels = new Color?[height, width];

        public int Width { get; } = width;
        public int Height { get; } = height;

        public void Put(int x, int y, Color color)
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height) pixels[y, x] = color;
        }

        public Color Get(int x, int y) => pixels[y, x] ?? Colors.Transparent;

        public bool[,] Filled()
        {
            var mask = new bool[Height, Width];
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    mask[y, x] = pixels[y, x] is not null;
            return mask;
        }

        /// <summary>Draws a one-pixel ring around the mask without covering it.</summary>
        public void Outline(bool[,] mask, Color color, bool diagonal = true)
        {
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    if (mask[y, x]) continue;
                    var touches = false;
                    for (var dy = -1; dy <= 1 && !touches; dy++)
                        for (var dx = -1; dx <= 1 && !touches; dx++)
                        {
                            if ((dx == 0 && dy == 0) || (!diagonal && dx != 0 && dy != 0)) continue;
                            var nx = x + dx;
                            var ny = y + dy;
                            touches = nx >= 0 && ny >= 0 && nx < Width && ny < Height && mask[ny, nx];
                        }
                    if (touches) pixels[y, x] = color;
                }
        }

        public void Paste(Canvas source, int originX, int originY)
        {
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    if (source.pixels[y, x] is { } color) Put(originX + x, originY + y, color);
        }

        public Image ToImage()
        {
            var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    if (pixels[y, x] is { } color) image.SetPixel(x, y, color);
            return image;
        }
    }
}
