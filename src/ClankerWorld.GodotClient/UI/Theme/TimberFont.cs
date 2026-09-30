using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Timber, the heading lettering: thin capitals drawn on a pixel grid from the
/// logo's letter shapes. It is built in code as a bitmap font and only scales by
/// whole numbers, so every stroke stays a crisp block of pixels. Lowercase
/// letters show as capitals; anything else falls back to the body font.
/// </summary>
public static class TimberFont
{
    /// <summary>Capital height in font pixels.</summary>
    public const int CapHeight = 7;

    private const int Descent = 3;
    private const int SpaceAdvance = 3;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".####", "#....", "#....", "#....", "#....", "#....", ".####"],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".####", "#....", "#....", "#.###", "#...#", "#...#", ".####"],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = ["###", ".#.", ".#.", ".#.", ".#.", ".#.", "###"],
        ['J'] = ["....#", "....#", "....#", "....#", "#...#", "#...#", ".###."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
        ['4'] = ["#...#", "#...#", "#...#", "#####", "....#", "....#", "....#"],
        ['5'] = ["#####", "#....", "#....", "####.", "....#", "....#", "####."],
        ['6'] = [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", "..#..", "..#..", "..#.."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
        ['.'] = [".", ".", ".", ".", ".", ".", "#"],
        [','] = ["..", "..", "..", "..", "..", ".#", "#."],
        [':'] = [".", "#", ".", ".", ".", "#", "."],
        ['\''] = ["#", "#", ".", ".", ".", ".", "."],
        ['!'] = ["#", "#", "#", "#", "#", ".", "#"],
        ['?'] = [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
        ['-'] = ["...", "...", "...", "###", "...", "...", "..."],
        ['/'] = ["....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...."],
        ['&'] = [".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#"],
        ['('] = [".#", "#.", "#.", "#.", "#.", "#.", ".#"],
        [')'] = ["#.", ".#", ".#", ".#", ".#", ".#", "#."],
        ['+'] = [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
        ['·'] = [".", ".", ".", "#", ".", ".", "."],
        ['%'] = ["##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##"],
    };

    /// <summary>Characters Timber draws itself, before lowercase is folded to capitals.</summary>
    public static IReadOnlyCollection<char> Characters => Glyphs.Keys;

    /// <summary>
    /// Builds the font with one font pixel per screen pixel at
    /// <paramref name="pixelSize"/> and larger sizes at whole multiples of it.
    /// </summary>
    public static FontFile Create(int pixelSize, Font fallback)
    {
        var font = new FontFile
        {
            FontName = "ClankerWorld Timber",
            FixedSize = pixelSize,
            FixedSizeScaleMode = TextServer.FixedSizeScaleMode.IntegerOnly,
            Antialiasing = TextServer.FontAntialiasing.None,
            GenerateMipmaps = false,
            AllowSystemFallback = false,
            Fallbacks = [fallback],
        };
        var size = new Vector2I(pixelSize, 0);
        font.SetCacheAscent(0, pixelSize, pixelSize - Descent);
        font.SetCacheDescent(0, pixelSize, Descent);

        var atlasWidth = Glyphs.Values.Sum(rows => rows[0].Length + 1) + 1;
        var atlas = Image.CreateEmpty(atlasWidth, CapHeight + 2, false, Image.Format.La8);
        var x = 1;
        foreach (var (character, rows) in Glyphs)
        {
            var width = rows[0].Length;
            for (var row = 0; row < rows.Length; row++)
                for (var column = 0; column < width; column++)
                    if (rows[row][column] == '#') atlas.SetPixel(x + column, row + 1, Colors.White);
            var region = new Rect2(x, 1, width, rows.Length);
            foreach (var code in character == char.ToLowerInvariant(character)
                ? new[] { character } : new[] { character, char.ToLowerInvariant(character) })
            {
                font.SetGlyphAdvance(0, pixelSize, code, new Vector2(width + 1, 0));
                font.SetGlyphOffset(0, size, code, new Vector2(0, -rows.Length));
                font.SetGlyphSize(0, size, code, region.Size);
                font.SetGlyphUVRect(0, size, code, region);
                font.SetGlyphTextureIdx(0, size, code, 0);
            }
            x += width + 1;
        }
        font.SetGlyphAdvance(0, pixelSize, ' ', new Vector2(SpaceAdvance, 0));
        font.SetTextureImage(0, size, 0, atlas);
        return font;
    }
}
