using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Pixel-art icons for stored items, generated at the size they are shown.
/// Each item kind has its own picture; a kind without one yet (for example
/// new content) shows a plain crate, so the owner still sees it counted.
/// </summary>
public static class ItemIcons
{
    private static readonly Color Outline = new("3B2A1E");
    private static readonly Dictionary<(string Kind, int Size), ImageTexture> Cache = [];

    private static readonly Dictionary<string, Action<PixelCanvas>> Painters = new(StringComparer.Ordinal)
    {
        ["wood"] = PaintWood,
        ["stone"] = canvas => PaintRock(canvas, new Color("6A6862"), new Color("8E8C86"), new Color("B8B5AC"), null),
        ["iron_ore"] = canvas => PaintRock(canvas, new Color("5E5A57"), new Color("7E7874"), new Color("A39C95"), new Color("D0773A")),
        ["clay"] = PaintClay,
        ["iron"] = PaintIngot,
        ["fiber"] = PaintFiber,
        ["grain"] = PaintGrain,
        ["flour"] = PaintFlour,
        ["seed"] = PaintSeeds,
        ["food"] = PaintApple,
        ["berries"] = PaintBerries,
        ["bread"] = PaintBread,
        ["clothing"] = PaintTunic,
        ["wooden_axe"] = PaintAxe,
        ["wooden_pickaxe"] = PaintPickaxe,
        ["tool"] = PaintHammer,
    };

    /// <summary>Whether this kind has its own picture rather than the crate.</summary>
    public static bool Has(string kind) => Painters.ContainsKey(kind);

    public static ImageTexture Texture(string kind, int size)
    {
        var key = (kind, size);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(Render(kind, size));
        Cache[key] = texture;
        return texture;
    }

    public static Image Render(string kind, int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        // Icons are drawn on a 16-unit grid and scaled to the requested size.
        var canvas = new PixelCanvas(image, new Rect2I(0, 0, size, size), size / 16f);
        (Painters.GetValueOrDefault(kind) ?? PaintCrate)(canvas);
        return image;
    }

    private static void Box(PixelCanvas c, float x, float y, float w, float h, Color fill) =>
        c.Rect(x, y, w, h, fill);

    private static void OutlinedBox(PixelCanvas c, float x, float y, float w, float h, Color fill)
    {
        c.Rect(x - 1, y - 1, w + 2, h + 2, Outline);
        c.Rect(x, y, w, h, fill);
    }

    private static void OutlinedDisc(PixelCanvas c, float x, float y, float r, Color fill)
    {
        c.Disc(x, y, r + 1, Outline);
        c.Disc(x, y, r, fill);
    }

    private static void PaintWood(PixelCanvas c)
    {
        var bark = new Color("7A4E2A");
        var barkLight = new Color("A06A3A");
        var face = new Color("DDB47C");
        var ring = new Color("A87A48");
        foreach (var (x, y) in new[] { (3f, 3f), (5f, 9f) })
        {
            OutlinedBox(c, x + 2, y, 9, 4, bark);
            Box(c, x + 2, y, 9, 1, barkLight);
            OutlinedDisc(c, x + 2, y + 2, 2.5f, face);
            c.Dot(x + 2, y + 2, ring);
            c.Ring(x + 2, y + 2, 1.6f, ring);
        }
    }

    private static void PaintRock(PixelCanvas c, Color dark, Color mid, Color light, Color? fleck)
    {
        c.Ellipse(8, 9.5f, 6.8f, 5.3f, Outline);
        c.Ellipse(8, 9.5f, 5.8f, 4.3f, mid);
        c.Ellipse(9.5f, 11.5f, 4f, 2.2f, dark);
        c.Ellipse(6.5f, 8f, 2.6f, 1.6f, light);
        c.Ellipse(12.5f, 13f, 2.4f, 1.8f, Outline);
        c.Ellipse(12.5f, 13f, 1.6f, 1.1f, mid);
        if (fleck is { } ore)
            foreach (var (x, y) in new[] { (5f, 10f), (8f, 8f), (10f, 11f), (11f, 8f), (7f, 12f) })
                c.Rect(x, y, 1, 1, ore);
    }

    private static void PaintClay(PixelCanvas c)
    {
        c.Lumpy(8, 9, 6.2f, Outline, 5, 1);
        c.Lumpy(8, 9, 5.2f, new Color("B5653E"), 5, 1);
        c.Ellipse(6.5f, 7.5f, 2.2f, 1.4f, new Color("D6875C"));
        c.Line(9, 7, 11, 11, new Color("8A4528"));
    }

    private static void PaintIngot(PixelCanvas c)
    {
        c.Rect(2, 6, 12, 7, Outline);
        c.Rect(4, 5, 8, 1, Outline);
        c.Rect(4, 6, 8, 3, new Color("CDD4DA"));
        c.Rect(3, 7, 1, 2, new Color("CDD4DA"));
        c.Rect(12, 7, 1, 2, new Color("CDD4DA"));
        c.Rect(3, 9, 10, 3, new Color("8E979F"));
        c.Rect(3, 11, 10, 1, new Color("6E767D"));
        c.Rect(5, 6, 3, 1, Colors.White);
    }

    private static void PaintFiber(PixelCanvas c)
    {
        // A bound bundle of stalks, flaring at both ends.
        var shades = new[] { new Color("C9B26A"), new Color("E3D08C"), new Color("B09A52") };
        c.Rect(4, 2, 8, 13, Outline);
        c.Rect(3, 2, 1, 3, Outline);
        c.Rect(12, 2, 1, 3, Outline);
        c.Rect(3, 12, 1, 3, Outline);
        c.Rect(12, 12, 1, 3, Outline);
        for (var i = 0; i < 8; i++)
        {
            var x = 4 + i;
            var flare = i < 4 ? -0.6f : 0.6f;
            c.Line(x + flare, 3, x, 8, shades[i % 3]);
            c.Line(x, 9, x + flare, 14, shades[(i + 1) % 3]);
        }
        OutlinedBox(c, 4, 8, 8, 2, new Color("7A4E2A"));
        Box(c, 4, 8, 8, 1, new Color("A0703C"));
    }

    private static void PaintGrain(PixelCanvas c)
    {
        var stalk = new Color("B89A48");
        var ear = new Color("E8C862");
        var earDark = new Color("C49A38");
        foreach (var x in new[] { 5f, 8f, 11f })
        {
            c.Line(x, 6, 8, 15, Outline);
            c.Line(x + 0.5f, 6, 8.5f, 15, stalk);
        }
        foreach (var x in new[] { 5f, 8f, 11f })
        {
            c.Ellipse(x, 4.5f, 1.8f, 3.4f, Outline);
            c.Ellipse(x, 4.5f, 1f, 2.6f, ear);
            c.Dot(x, 5, earDark);
            c.Dot(x, 3, earDark);
        }
        OutlinedBox(c, 6, 10, 5, 1, new Color("7A4E2A"));
    }

    private static void PaintFlour(PixelCanvas c)
    {
        c.Ellipse(8, 10, 5.8f, 5.2f, Outline);
        c.Ellipse(8, 10, 4.8f, 4.2f, new Color("EDE3CC"));
        c.Ellipse(9.5f, 12, 2.8f, 1.8f, new Color("CFC2A4"));
        c.Rect(6, 3, 4, 3, Outline);
        c.Rect(7, 3, 2, 2, new Color("EDE3CC"));
        OutlinedBox(c, 6, 5, 4, 1, new Color("A0703C"));
        c.Rect(6, 9, 4, 1, new Color("B8A98A"));
    }

    private static void PaintSeeds(PixelCanvas c)
    {
        var seed = new Color("9A6A3A");
        var light = new Color("C99A62");
        foreach (var (x, y) in new[] { (5f, 6f), (10f, 5f), (7.5f, 10f), (12f, 11f), (4.5f, 12f) })
        {
            c.Ellipse(x, y, 2.2f, 3f, Outline);
            c.Ellipse(x, y, 1.3f, 2.1f, seed);
            c.Dot(x - 0.5f, y - 1, light);
        }
    }

    private static void PaintApple(PixelCanvas c)
    {
        OutlinedDisc(c, 8, 10, 5, new Color("C8402E"));
        c.Disc(6.5f, 8.5f, 1.4f, new Color("EE7A5C"));
        c.Ellipse(10, 12.5f, 2.2f, 1.4f, new Color("962C20"));
        c.Line(8, 3, 8.5f, 6, Outline);
        c.Leaf(10.5f, 4, 3, 1.6f, -0.5f, new Color("5A8A3A"), new Color("3E6628"));
    }

    private static void PaintBerries(PixelCanvas c)
    {
        var berry = new Color("7A3A8E");
        var shine = new Color("B87ACC");
        c.Leaf(8, 4, 4, 2, 0.2f, new Color("5A8A3A"), new Color("3E6628"));
        foreach (var (x, y) in new[] { (5.5f, 9f), (10f, 8.5f), (8f, 12.5f), (12f, 12f), (4.5f, 13f) })
            c.Disc(x, y, 2.9f, Outline);
        foreach (var (x, y) in new[] { (5.5f, 9f), (10f, 8.5f), (8f, 12.5f), (12f, 12f), (4.5f, 13f) })
        {
            c.Disc(x, y, 2f, berry);
            c.Dot(x - 0.7f, y - 0.8f, shine);
        }
    }

    private static void PaintBread(PixelCanvas c)
    {
        c.Ellipse(8, 10, 7, 4.6f, Outline);
        c.Ellipse(8, 10, 6, 3.6f, new Color("C8883E"));
        c.Ellipse(8, 9, 5, 2.4f, new Color("E0A85A"));
        foreach (var x in new[] { 5f, 8f, 11f })
            c.Line(x - 1, 10, x + 1, 8, new Color("9A5E26"));
    }

    private static void PaintTunic(PixelCanvas c)
    {
        var cloth = new Color("5A7A9A");
        var dark = new Color("40597A");
        c.Rect(3, 3, 10, 12, Outline);
        c.Rect(1, 4, 3, 6, Outline);
        c.Rect(12, 4, 3, 6, Outline);
        c.Rect(4, 4, 8, 10, cloth);
        c.Rect(2, 5, 2, 4, cloth);
        c.Rect(12, 5, 2, 4, cloth);
        c.Rect(6, 3, 4, 2, Outline);
        c.Rect(7, 4, 2, 1, dark);
        c.Rect(4, 11, 8, 1, new Color("8A6A3A"));
        c.Rect(4, 12, 8, 2, dark);
    }

    private static void Handle(PixelCanvas c, float fromX, float fromY, float toX, float toY)
    {
        for (var offset = -1f; offset <= 1f; offset += 0.5f)
            c.Line(fromX + offset, fromY, toX + offset, toY, Outline);
        c.Line(fromX, fromY, toX, toY, new Color("A0703C"));
        c.Line(fromX + 0.5f, fromY, toX + 0.5f, toY, new Color("7A4E2A"));
    }

    private static void PaintAxe(PixelCanvas c)
    {
        Handle(c, 4, 15, 9, 2);
        // A stone head lashed across the handle, its blade curving out to the right.
        c.Ellipse(12, 5.5f, 3.4f, 4.4f, Outline);
        c.Rect(7, 3, 5, 5, Outline);
        c.Ellipse(12, 5.5f, 2.4f, 3.4f, new Color("8E8C86"));
        c.Rect(8, 4, 4, 3, new Color("8E8C86"));
        c.Line(14, 3, 14, 8, new Color("C9C6BC"));
        c.Rect(8, 6, 4, 1, new Color("6A6862"));
        c.Rect(9, 3, 1, 5, new Color("A0703C"));
    }

    private static void PaintPickaxe(PixelCanvas c)
    {
        Handle(c, 4, 15, 9, 5);
        for (var i = 0; i <= 10; i++)
        {
            var t = i / 10f;
            var x = 2 + t * 12;
            var y = 5 - MathF.Sin(t * MathF.PI) * 3;
            c.Disc(x, y, 1.6f, Outline);
        }
        for (var i = 0; i <= 10; i++)
        {
            var t = i / 10f;
            c.Disc(2 + t * 12, 5 - MathF.Sin(t * MathF.PI) * 3, 0.8f, new Color("C9A06A"));
        }
    }

    private static void PaintHammer(PixelCanvas c)
    {
        Handle(c, 5, 15, 9, 6);
        OutlinedBox(c, 5, 3, 8, 4, new Color("8E979F"));
        Box(c, 5, 3, 8, 1, new Color("CDD4DA"));
    }

    private static void PaintCrate(PixelCanvas c)
    {
        OutlinedBox(c, 3, 4, 10, 10, new Color("A0703C"));
        Box(c, 3, 4, 10, 1, new Color("C9965C"));
        Box(c, 3, 8.5f, 10, 1, new Color("7A4E2A"));
        c.Line(4, 5, 12, 13, new Color("7A4E2A"));
    }
}
