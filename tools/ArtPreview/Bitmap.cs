using Godot;

namespace ArtPreview;

/// <summary>
/// Hand-drawn pixel art as text, the way the game's item icons are written:
/// a palette string of "kRRGGBB" or "kRRGGBBAA" entries separated by spaces,
/// where k is the letter used in the rows, and rows of letters with '.' for
/// transparent. Every row must have the same length.
/// </summary>
public static class Bitmap
{
    public static Image Draw(string palette, params string[] rows)
    {
        var colors = palette.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(entry => entry[0], entry => new Color(entry[1..]));
        var height = rows.Length;
        var width = rows[0].Length;
        if (rows.Any(row => row.Length != width))
            throw new ArgumentException($"Rows differ in width: {string.Join(",", rows.Select(r => r.Length))}");
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var key = rows[y][x];
                if (key == '.') continue;
                if (!colors.TryGetValue(key, out var color))
                    throw new ArgumentException($"Row {y} uses '{key}' which the palette does not define.");
                image.SetPixel(x, y, color);
            }
        return image;
    }

    /// <summary>
    /// Adds a one-pixel outline around the opaque silhouette, like the game's
    /// item icons: each empty pixel beside the silhouette takes a dark shade of
    /// the darkest neighbouring colour, mixed toward <paramref name="ink"/>.
    /// </summary>
    public static Image Outlined(Image art, Color ink, bool diagonal = false)
    {
        var width = art.GetWidth();
        var height = art.GetHeight();
        var result = art.Duplicate();
        var steps = diagonal
            ? new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) }
            : new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (art.GetPixel(x, y).A > 0) continue;
                Color? darkest = null;
                foreach (var (dx, dy) in steps)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var near = art.GetPixel(nx, ny);
                    if (near.A > 0 && (darkest is null || near.Luminance < darkest.Value.Luminance)) darkest = near;
                }
                if (darkest is { } edge) result.SetPixel(x, y, (edge * 0.42f).Lerp(ink, 0.45f) with { A = 1 });
            }
        return result;
    }

    public static Image FlipX(Image art)
    {
        var copy = art.Duplicate();
        copy.FlipX();
        return copy;
    }

    /// <summary>Pastes <paramref name="sprite"/> onto a copy of <paramref name="target"/> with alpha blending.</summary>
    public static Image Over(Image target, Image sprite, int x, int y)
    {
        var copy = target.Duplicate();
        Sheet.Blend(copy, sprite, x, y);
        return copy;
    }

    /// <summary>A transparent canvas of the given size.</summary>
    public static Image Empty(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        return image;
    }

    /// <summary>Halves a 32 px sprite to 16 px by keeping the most opaque of each 2×2 block's majority colour; a starting point for 16 px variants.</summary>
    public static Image Halve(Image art)
    {
        var width = art.GetWidth() / 2;
        var height = art.GetHeight() / 2;
        var result = Empty(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var block = new[] { art.GetPixel(x * 2, y * 2), art.GetPixel(x * 2 + 1, y * 2), art.GetPixel(x * 2, y * 2 + 1), art.GetPixel(x * 2 + 1, y * 2 + 1) };
                var opaque = block.Where(c => c.A > 0).ToList();
                if (opaque.Count < 2) continue;
                var r = opaque.Average(c => c.R);
                var g = opaque.Average(c => c.G);
                var b = opaque.Average(c => c.B);
                var a = opaque.Average(c => c.A);
                result.SetPixel(x, y, new Color(r, g, b, a));
            }
        return result;
    }
}
