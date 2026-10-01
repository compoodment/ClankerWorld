using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Glyphs;

/// <summary>
/// Proposed interface glyphs: the 32 existing <see cref="PixelGlyph"/> icons,
/// with the ones that broke the set's weight or alignment redrawn, plus 14
/// new glyphs the agreed interface needs. Every glyph is a 12 × 12 two-colour
/// bitmap in the <see cref="PixelIcons"/> format ("#" main, "o" accent, "."
/// empty), so an approved one can be pasted straight into that class.
/// <para>
/// Rules (style guide G1, G2): nothing in the outer pixel ring, so the live
/// area is the 10 × 10 square from (1, 1) to (10, 10); line icons use
/// two-pixel strokes (Close, Back, Menu, Pause, Plus, Arrow, Check); outlined
/// shapes use a one-pixel main outline with an accent fill or accent details
/// (Folder, Book, Speech, Coin); round icons share Info's 10 × 10 ring; and a
/// glyph's box is centred on the grid's middle line (5.5) wherever its shape
/// allows. The four cloud glyphs share one cloud.
/// </para>
/// </summary>
public sealed class GlyphsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "glyphs";
    public string Name => "glyphs";

    /// <summary>The grid size of every glyph, as in <see cref="PixelIcons.Grid"/>.</summary>
    public const int Grid = 12;

    /// <summary>The Light theme's ink, used for "#".</summary>
    private static readonly Color Ink = new("33261A");

    /// <summary>The Light theme's green accent, used for "o".</summary>
    private static readonly Color Accent = new("3E7D3A");

    /// <summary>
    /// Existing glyphs that are redrawn, keyed by their <see cref="PixelGlyph"/>
    /// name, each with the reason shown in the review note. Glyphs not listed
    /// here are kept exactly as the game draws them.
    /// </summary>
    private static readonly Dictionary<string, (string Why, string[] Rows)> Redrawn = new()
    {
        ["Map"] = ("Redrawn: the old one was a thin frame with a V, which reads as an envelope. Now a folded three-panel map with a dotted route.",
            ["............", "............", ".###....###.", ".#..####.o#.", ".#..#..#o.#.", ".#..#.o#..#.", ".#..#o.#..#.", ".#.o#..#..#.", ".#o.####..#.", ".###....###.", "............", "............"]),
        ["Play"] = ("Redrawn: it was ten rows tall beside Pause's eight. Now the same eight rows as Pause, nudged right so it looks centred.",
            ["............", "............", "....##......", "....###.....", "....####....", "....#####...", "....#####...", "....####....", "....###.....", "....##......", "............", "............"]),
        ["Sun"] = ("Redrawn: the disc sat half a pixel up and left and the rays were uneven. Now a centred disc with eight evenly spaced rays.",
            ["............", ".....oo.....", "..o......o..", "....####....", "...######...", ".o.######.o.", ".o.######.o.", "...######...", "....####....", "..o......o..", ".....oo.....", "............"]),
        ["Cloud"] = ("Redrawn: it touched the left edge and read as a mound. Now a two-bump cloud with a flat base, centred, shared by Rain, Snow and Storm.",
            ["............", "............", "............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..########..", "............", "............", "............"]),
        ["Rain"] = ("Redrawn: its cloud touched the top edge and differed from Cloud. Now the shared cloud with slanted drops below.",
            ["............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..########..", "............", "...o..o..o..", "..o..o..o...", ".o..o..o....", "............"]),
        ["Snow"] = ("Redrawn: its cloud touched the top edge. Now the shared cloud with the same staggered flakes below.",
            ["............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..########..", "............", ".o...o...o..", "...o...o....", ".o...o...o..", "............"]),
        ["Storm"] = ("Redrawn: its cloud touched the top edge. Now the shared cloud with the same zigzag bolt breaking out of its base.",
            ["............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..###oo###..", "....oo......", "...oooo.....", ".....oo.....", "....oo......", "............"]),
        ["Leaf"] = ("Redrawn: the stem ran into the bottom-left corner pixel. Same leaf with a stem one pixel shorter, moved down to centre it.",
            ["............", "............", "........###.", "......####o.", ".....###o##.", "....##o####.", "...#o#####..", "...o####....", "..o.###.....", ".o..........", "............", "............"]),
        ["Globe"] = ("Redrawn: it was twelve pixels wide and touched both sides. Now Info's ring with the same continents.",
            ["............", "....####....", "..##oo..##..", "..#ooo.o.#..", ".#.oooo...#.", ".#..oo..oo#.", ".#.....ooo#.", ".#....ooo.#.", "..#....o.#..", "..##....##..", "....####....", "............"]),
        ["Find"] = ("Redrawn: the crosshair ticks ran off all four edges. Same reticle with the ticks kept inside the ring.",
            ["............", "...######...", "..#..##..#..", ".#...##...#.", ".#........#.", ".###.oo.###.", ".###.oo.###.", ".#........#.", ".#...##...#.", "..#..##..#..", "...######...", "............"]),
        ["Close"] = ("Redrawn: the X was seven rows by eight and sat half a pixel high. Now a square 8 × 8 X centred on the grid.",
            ["............", "............", "..##....##..", "...##..##...", "....####....", ".....##.....", ".....##.....", "....####....", "...##..##...", "..##....##..", "............", "............"]),
        ["Back"] = ("Redrawn: it sat one row higher than Close, Menu and Pause. Same chevron, one row lower.",
            ["............", "............", "......##....", ".....##.....", "....##......", "...##.......", "...##.......", "....##......", ".....##.....", "......##....", "............", "............"]),
        ["Key"] = ("Redrawn: the bow touched the left edge. Same key with a shaft one pixel shorter.",
            ["............", "............", "............", "..###.......", ".#ooo#......", ".#o.o######.", ".#ooo######.", "..###..##.#.", ".......##.#.", "............", "............", "............"]),
        ["Pencil"] = ("Redrawn: the tip end touched the right edge. Same pencil one pixel to the left.",
            ["............", "........##..", ".......#oo#.", "......#oo#..", ".....#oo#...", "....#oo#....", "...#oo#.....", "..#oo#......", ".#oo#.......", ".#o#........", ".##.........", "............"]),
        ["Speech"] = ("Redrawn: the bubble was twelve pixels wide and touched both sides. Same bubble two pixels narrower.",
            ["............", "..########..", ".#oooooooo#.", ".#o######o#.", ".#oooooooo#.", ".#o####ooo#.", ".#oooooooo#.", "..########..", "...##.......", "...#........", "............", "............"]),
    };

    /// <summary>New glyphs for the agreed interface, in the order the brief lists them, each with a review note.</summary>
    private static readonly (string Id, string Note, string[] Rows)[] Added =
    [
        ("Market", "A market stall: a striped awning, a row of goods under it, the counter and its legs.",
            ["............", "..########..", ".#oo#oo#oo#.", ".#oo#oo#oo#.", ".##########.", "..#......#..", "..#oooooo#..", ".##########.", ".##########.", "..#......#..", "..#......#..", "............"]),
        ("Coin", "A coin with a square hole, smaller than the round icons so it never reads as Info.",
            ["............", "............", "....####....", "...#oooo#...", "..#oooooo#..", "..#oo##oo#..", "..#oo##oo#..", "..#oooooo#..", "...#oooo#...", "....####....", "............", "............"]),
        ("Heart", "A solid heart, 10 × 8, centred.",
            ["............", "............", "..##....##..", ".####..####.", ".##########.", ".##########.", "..########..", "...######...", "....####....", ".....##.....", "............", "............"]),
        ("Hammer", "A hammer at the same angle as Pencil: solid head, accent handle.",
            ["............", "......#.....", ".....###....", "....#####...", ".....#####..", "......#####.", ".....o.###..", "....ooo.#...", "...ooo......", "..ooo.......", "..oo........", "............"]),
        ("Clock", "Info's ring with accent hands at three o'clock.",
            ["............", "....####....", "..##....##..", "..#..oo..#..", ".#...oo...#.", ".#...oooo.#.", ".#...oooo.#.", ".#........#.", "..#......#..", "..##....##..", "....####....", "............"]),
        ("Boat", "A sailing boat for the Port: hull, mast and an accent sail.",
            ["............", ".....#......", ".....#o.....", ".....#oo....", ".....#ooo...", ".....#oooo..", ".....#ooooo.", ".....#......", ".##########.", "..########..", "...######...", "............"]),
        ("Horse", "A horse's head facing left, with an accent mane and eye.",
            ["............", ".....#.#....", ".....####...", "....##o##o..", "...######oo.", "..#######oo.", ".########oo.", ".###..###oo.", "......###oo.", ".....####oo.", "....#####oo.", "............"]),
        ("Warning", "A warning triangle with an accent fill and an exclamation mark.",
            ["............", ".....##.....", "....#oo#....", "....#oo#....", "...#o##o#...", "...#o##o#...", "..#oo##oo#..", "..#oooooo#..", ".#ooo##ooo#.", ".##########.", "............", "............"]),
        ("Check", "A tick in two-pixel strokes.",
            ["............", "............", "............", ".........##.", "........##..", ".......##...", ".##...##....", "..##.##.....", "...###......", "....#.......", "............", "............"]),
        ("Plus", "Two-pixel strokes, 8 × 8, centred.",
            ["............", "............", ".....##.....", ".....##.....", ".....##.....", "..########..", "..########..", ".....##.....", ".....##.....", ".....##.....", "............", "............"]),
        ("Minus", "The Plus bar on its own.",
            ["............", "............", "............", "............", "............", "..########..", "..########..", "............", "............", "............", "............", "............"]),
        ("Arrow", "Points east; flip it in code for west, rotate for north and south. The head is Back's chevron.",
            ["............", "............", "......##....", ".......##...", "........##..", ".##########.", ".##########.", "........##..", ".......##...", "......##....", "............", "............"]),
        ("Lock", "A padlock: one-pixel shackle like Link, solid body, accent keyhole.",
            ["............", "....####....", "...#....#...", "...#....#...", "...#....#...", "..########..", "..########..", "..###oo###..", "..###oo###..", "..########..", "..########..", "............"]),
        ("Trash", "A bin: handle, lid, and a ribbed body with an accent fill.",
            ["............", "....####....", ".##########.", "............", "..########..", "..#o#oo#o#..", "..#o#oo#o#..", "..#o#oo#o#..", "..#o#oo#o#..", "..#o#oo#o#..", "...######...", "............"]),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var glyph in Enum.GetValues<PixelGlyph>())
        {
            var id = glyph.ToString();
            if (Redrawn.TryGetValue(id, out var redrawn))
                yield return new Entry(Family, id, Draw(id, redrawn.Rows, Ink, Accent), redrawn.Why);
            else
                yield return new Entry(Family, id, PixelIcons.Texture(glyph, Ink, Accent, 1).GetImage(), "Unchanged.");
        }
        foreach (var (id, note, rows) in Added)
            yield return new Entry(Family, id, Draw(id, rows, Ink, Accent), "New. " + note);
    }

    /// <summary>Glyphs are interface icons, not part of the map scene, so no delegate changes.</summary>
    public void Apply(ArtSet set)
    {
    }

    /// <summary>
    /// Draws one glyph at 1× on transparent, the way <see cref="PixelIcons.Texture"/>
    /// does, after checking it is 12 × 12, uses only ".", "#" and "o", and
    /// leaves the outer pixel ring empty (G1).
    /// </summary>
    public static Image Draw(string id, string[] rows, Color main, Color accent)
    {
        if (rows.Length != Grid || rows.Any(row => row.Length != Grid))
            throw new ArgumentException($"Glyph {id} must be {Grid} rows of {Grid} characters.");
        var image = Bitmap.Empty(Grid, Grid);
        for (var y = 0; y < Grid; y++)
            for (var x = 0; x < Grid; x++)
            {
                var key = rows[y][x];
                if (key == '.') continue;
                if (key is not ('#' or 'o'))
                    throw new ArgumentException($"Glyph {id} uses '{key}' at ({x}, {y}); only '.', '#' and 'o' are allowed.");
                if (x == 0 || y == 0 || x == Grid - 1 || y == Grid - 1)
                    throw new ArgumentException($"Glyph {id} draws on the outer pixel ring at ({x}, {y}).");
                image.SetPixel(x, y, key == '#' ? main : accent);
            }
        return image;
    }
}
