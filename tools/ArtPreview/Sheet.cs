using Godot;

namespace ArtPreview;

/// <summary>One rendered picture with the asset ID it shows.</summary>
public sealed record Entry(string Family, string Id, Image Image, string? Note = null);

/// <summary>Nearest-neighbour helpers and a tiny caption font for contact sheets.</summary>
public static class Sheet
{
    public static Image Upscale(Image source, int scale)
    {
        if (scale <= 1) return source.Duplicate();
        var result = Image.CreateEmpty(source.GetWidth() * scale, source.GetHeight() * scale, false, Image.Format.Rgba8);
        for (var y = 0; y < source.GetHeight(); y++)
            for (var x = 0; x < source.GetWidth(); x++)
            {
                var color = source.GetPixel(x, y);
                result.FillRect(new Rect2I(x * scale, y * scale, scale, scale), color);
            }
        return result;
    }

    /// <summary>Alpha-composites <paramref name="source"/> onto <paramref name="target"/> at the given position.</summary>
    public static void Blend(Image target, Image source, int x, int y)
    {
        for (var sy = 0; sy < source.GetHeight(); sy++)
            for (var sx = 0; sx < source.GetWidth(); sx++)
            {
                var tx = x + sx;
                var ty = y + sy;
                if (tx < 0 || ty < 0 || tx >= target.GetWidth() || ty >= target.GetHeight()) continue;
                var over = source.GetPixel(sx, sy);
                if (over.A <= 0) continue;
                target.SetPixel(tx, ty, target.GetPixel(tx, ty).Blend(over));
            }
    }

    public static void Fill(Image target, int x, int y, int width, int height, Color color) =>
        target.FillRect(new Rect2I(x, y, width, height), color);

    /// <summary>A checker that shows transparency without hiding soft shadows.</summary>
    public static Image Checker(int width, int height, int cell = 8)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var a = new Color("8F8F8F");
        var b = new Color("7A7A7A");
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                image.SetPixel(x, y, ((x / cell + y / cell) % 2 == 0) ? a : b);
        return image;
    }

    /// <summary>
    /// Lays entries out in a grid: each cell shows the picture at <paramref name="scale"/>
    /// over the given backdrop (a flat colour or a checker) with its ID in a 3×5 pixel
    /// caption beneath. Pictures larger than the cell get their own wider cell.
    /// </summary>
    public static Image Grid(IReadOnlyList<Entry> entries, int scale, int columns, Color? backdrop, int captionScale = 2)
    {
        var pad = 6 * captionScale;
        var captionHeight = 6 * captionScale + 2;
        var cells = entries.Select(entry =>
        {
            var w = entry.Image.GetWidth() * scale;
            var h = entry.Image.GetHeight() * scale;
            var captionWidth = Font.Width(entry.Id) * captionScale;
            return (entry, Width: Math.Max(w, captionWidth), Height: h);
        }).ToList();
        var columnWidths = new int[columns];
        var rowHeights = new List<int>();
        for (var i = 0; i < cells.Count; i++)
        {
            var c = i % columns;
            var r = i / columns;
            columnWidths[c] = Math.Max(columnWidths[c], cells[i].Width);
            if (rowHeights.Count <= r) rowHeights.Add(0);
            rowHeights[r] = Math.Max(rowHeights[r], cells[i].Height);
        }
        var totalWidth = columnWidths.Sum() + pad * (columns + 1);
        var totalHeight = rowHeights.Sum(h => h + captionHeight) + pad * (rowHeights.Count + 1);
        var sheet = Image.CreateEmpty(totalWidth, totalHeight, false, Image.Format.Rgba8);
        sheet.Fill(new Color("2B2B2B"));
        for (var i = 0; i < cells.Count; i++)
        {
            var c = i % columns;
            var r = i / columns;
            var x = pad + columnWidths.Take(c).Sum() + pad * c;
            var y = pad + rowHeights.Take(r).Sum(h => h + captionHeight) + pad * r;
            var (entry, width, height) = cells[i];
            var picture = Upscale(entry.Image, scale);
            var cellWidth = columnWidths[c];
            var cellHeight = rowHeights[r];
            var backing = backdrop is { } flat
                ? Flat(picture.GetWidth(), picture.GetHeight(), flat)
                : Checker(picture.GetWidth(), picture.GetHeight(), 4 * scale);
            var px = x + (cellWidth - picture.GetWidth()) / 2;
            sheet.BlitRect(backing, new Rect2I(0, 0, backing.GetWidth(), backing.GetHeight()), new Vector2I(px, y));
            Blend(sheet, picture, px, y);
            Font.Draw(sheet, entry.Id, x, y + cellHeight + 2, captionScale, new Color("E6E0D0"));
        }
        return sheet;
    }

    public static Image Flat(int width, int height, Color color)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(color);
        return image;
    }

    /// <summary>Two pictures side by side with captions, for current-versus-proposed pairs.</summary>
    public static Image Pair(string leftCaption, Image left, string rightCaption, Image right, int scale, Color? backdrop)
    {
        var entries = new List<Entry> { new("pair", leftCaption, left), new("pair", rightCaption, right) };
        return Grid(entries, scale, 2, backdrop);
    }
}

/// <summary>A 3×5 pixel capital font for captions on review sheets.</summary>
public static class Font
{
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = ["010", "101", "111", "101", "101"],
        ['B'] = ["110", "101", "110", "101", "110"],
        ['C'] = ["011", "100", "100", "100", "011"],
        ['D'] = ["110", "101", "101", "101", "110"],
        ['E'] = ["111", "100", "110", "100", "111"],
        ['F'] = ["111", "100", "110", "100", "100"],
        ['G'] = ["011", "100", "101", "101", "011"],
        ['H'] = ["101", "101", "111", "101", "101"],
        ['I'] = ["111", "010", "010", "010", "111"],
        ['J'] = ["001", "001", "001", "101", "010"],
        ['K'] = ["101", "101", "110", "101", "101"],
        ['L'] = ["100", "100", "100", "100", "111"],
        ['M'] = ["101", "111", "111", "101", "101"],
        ['N'] = ["110", "101", "101", "101", "101"],
        ['O'] = ["010", "101", "101", "101", "010"],
        ['P'] = ["110", "101", "110", "100", "100"],
        ['Q'] = ["010", "101", "101", "011", "001"],
        ['R'] = ["110", "101", "110", "101", "101"],
        ['S'] = ["011", "100", "010", "001", "110"],
        ['T'] = ["111", "010", "010", "010", "010"],
        ['U'] = ["101", "101", "101", "101", "011"],
        ['V'] = ["101", "101", "101", "101", "010"],
        ['W'] = ["101", "101", "111", "111", "101"],
        ['X'] = ["101", "101", "010", "101", "101"],
        ['Y'] = ["101", "101", "010", "010", "010"],
        ['Z'] = ["111", "001", "010", "100", "111"],
        ['0'] = ["010", "101", "101", "101", "010"],
        ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["110", "001", "010", "100", "111"],
        ['3'] = ["110", "001", "010", "001", "110"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "110", "001", "110"],
        ['6'] = ["011", "100", "110", "101", "010"],
        ['7'] = ["111", "001", "010", "010", "010"],
        ['8'] = ["010", "101", "010", "101", "010"],
        ['9'] = ["010", "101", "011", "001", "110"],
        [' '] = ["000", "000", "000", "000", "000"],
        ['-'] = ["000", "000", "111", "000", "000"],
        ['_'] = ["000", "000", "000", "000", "111"],
        ['.'] = ["000", "000", "000", "000", "010"],
        [':'] = ["000", "010", "000", "010", "000"],
        ['/'] = ["001", "001", "010", "100", "100"],
        ['('] = ["010", "100", "100", "100", "010"],
        [')'] = ["010", "001", "001", "001", "010"],
        ['+'] = ["000", "010", "111", "010", "000"],
        ['x'] = ["000", "101", "010", "101", "000"],
        ['>'] = ["100", "010", "001", "010", "100"],
        ['='] = ["000", "111", "000", "111", "000"],
        ['?'] = ["110", "001", "010", "000", "010"],
    };

    public static int Width(string text) => text.Length * 4 - 1;

    public static void Draw(Image target, string text, int x, int y, int scale, Color color)
    {
        var cursor = x;
        foreach (var raw in text)
        {
            var character = raw == 'x' ? 'x' : char.ToUpperInvariant(raw);
            var glyph = Glyphs.TryGetValue(character, out var g) ? g : Glyphs['?'];
            for (var row = 0; row < 5; row++)
                for (var column = 0; column < 3; column++)
                    if (glyph[row][column] == '1')
                        target.FillRect(new Rect2I(cursor + column * scale, y + row * scale, scale, scale), color);
            cursor += 4 * scale;
        }
    }
}
