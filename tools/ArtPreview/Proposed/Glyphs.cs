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
/// <para>
/// Round 3: every glyph the owner approved in round 2 is kept pixel for
/// pixel. Hammer, Map, Pencil and Play get one fix each for the owner's
/// notes. Arrow, Rain, Snow, Storm, Sun and Speech were sent back without a
/// note, so each comes as two options: "<c>.a</c>" stays close to today's game
/// glyph (or, for the new Arrow, to round 2) and only fixes its size and
/// position, and "<c>.b</c>" is a fresh, clearer drawing; the b weather icons
/// use the approved Cloud. A "weather.set" review picture shows the weather
/// icons side by side.
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
        ["Map"] = ("Round 3: a folded paper map. Three panels folded like an accordion, so the top and bottom edges zigzag, with the middle panel shaded and a dotted route crossing the map.",
            ["............", "....#.....#.", "...###...##.", "..#.#o#.#o#.", ".#..#oo#..#.", ".#..#oo#o.#.", ".#.o#oo#..#.", ".#..#oo#..#.", ".#o#.#o#.#..", ".##...###...", ".#.....#....", "............"]),
        ["Play"] = ("Round 3: a true triangle, exactly the same above and below its middle line, with single-pixel corners. The same eight rows as Pause, and half a pixel right of centre so it looks centred.",
            ["............", "............", "...#........", "...###......", "...#####....", "...#######..", "...#######..", "...#####....", "...###......", "...#........", "............", "............"]),
        ["Cloud"] = ("Redrawn: it touched the left edge and read as a mound. Now a two-bump cloud with a flat base, centred, shared by Rain, Snow and Storm.",
            ["............", "............", "............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..########..", "............", "............", "............"]),
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
        ["Pencil"] = ("Round 3: a sharpened pencil. From the top: a green eraser, a dark band, the green body, then the pale shaved wood and the green lead at the point. The body is a pixel wider so it no longer reads as a stick.",
            ["............", "........##..", ".......#oo#.", "......###o#.", ".....#oo##..", "....#ooo#...", "...#ooo#....", "..#ooo#.....", ".#..o#......", ".o..#.......", ".oo#........", "............"]),
    };

    /// <summary>
    /// Round 3: glyphs the owner sent back without a note, each drawn two ways.
    /// Option a stays close to today's game glyph (for the new Arrow, to its
    /// round-2 drawing) and only fixes size and position; option b is a fresh,
    /// clearer drawing. The b weather icons reuse the approved Cloud.
    /// </summary>
    private static readonly Dictionary<string, (string Note, string[] Rows)[]> Options = new()
    {
        ["Sun"] =
        [
            ("Close to today's sun: the same eight-sided disc and ring of short rays, one pixel bigger so it sits exactly in the middle (today's sits half a pixel up and left).",
                ["............", ".....oo.....", "..o......o..", ".....##.....", "....####....", ".o.######.o.", ".o.######.o.", "....####....", ".....##.....", "..o......o..", ".....oo.....", "............"]),
            ("Fresh: a smaller square disc with eight rays two pixels long pointing straight out, the classic pixel sun.",
                ["............", ".....oo.....", "..o..oo..o..", "...o....o...", "....####....", ".oo.####.oo.", ".oo.####.oo.", "....####....", "...o....o...", "..o..oo..o..", ".....oo.....", "............"]),
        ],
        ["Rain"] =
        [
            ("Close to today's rain: the same mound cloud and two rows of slanted drops, one row shorter so it stays off the top edge, and centred.",
                ["............", "....####....", "..########..", ".##########.", ".##########.", "............", "...o..o..o..", "..o..o..o...", "............", "...o..o..o..", "..o..o..o...", "............"]),
            ("Fresh: the approved Cloud with four short streaks falling straight down, staggered like real rain.",
                ["............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..########..", "............", "..o....o....", "..o.o..o.o..", "....o....o..", "............"]),
        ],
        ["Snow"] =
        [
            ("Close to today's snow: the mound cloud from Rain a with the same staggered flakes, off the top edge.",
                ["............", "....####....", "..########..", ".##########.", ".##########.", "............", ".o...o...o..", "............", "...o...o....", "............", ".o...o...o..", "............"]),
            ("Fresh: the approved Cloud with two plus-shaped snowflakes.",
                ["............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".##########.", "..########..", "............", "...o....o...", "..ooo..ooo..", "...o....o...", "............"]),
        ],
        ["Storm"] =
        [
            ("Close to today's storm: the mound cloud from Rain a with today's zigzag bolt, off the top edge and centred under the cloud.",
                ["............", "....####....", "..########..", ".##########.", ".##########.", "......oo....", ".....oo.....", "....oooo....", "......oo....", ".....oo.....", "....oo......", "............"]),
            ("Fresh: the approved Cloud with today's zigzag bolt starting inside its base, centred.",
                ["............", ".....####...", "..##.#####..", ".##########.", ".##########.", ".#####oo###.", "..###oo###..", "....oooo....", "......oo....", ".....oo.....", "....oo......", "............"]),
        ],
        ["Speech"] =
        [
            ("Close to today's bubble: the same outline, green fill, two text lines and tail, ten pixels wide instead of twelve and centred top to bottom, with a clear green row around each line.",
                ["............", "..########..", ".#oooooooo#.", ".#o######o#.", ".#oooooooo#.", ".#oooooooo#.", ".#o####ooo#.", ".#oooooooo#.", "..########..", "...##.......", "...#........", "............"]),
            ("Fresh: a solid rounded bubble with three green dots, someone talking, which stays readable at small size.",
                ["............", "...######...", "..########..", ".##########.", ".#oo#oo#oo#.", ".#oo#oo#oo#.", ".##########.", "..########..", "...######...", "...##.......", "...#........", "............"]),
        ],
        ["Arrow"] =
        [
            ("Close to round 2: Back's chevron as the head on a two-pixel shaft, now in the same 8 × 8 box as Close and Plus.",
                ["............", "............", ".....##.....", "......##....", ".......##...", "..########..", "..########..", ".......##...", "......##....", ".....##.....", "............", "............"]),
            ("Fresh: a solid arrowhead like the one on Door, on a two-pixel shaft, 8 × 6 and centred.",
                ["............", "............", "............", ".......#....", ".......##...", "..########..", "..########..", ".......##...", ".......#....", "............", "............", "............"]),
        ],
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
        ("Hammer", "A hammer at the same angle as Pencil: solid head, accent handle. Round 3: the handle now runs straight into the middle of the head with no gap, and the head is unchanged.",
            ["............", "......#.....", ".....###....", "....#####...", ".....#####..", ".....o#####.", "....ooo###..", "...ooo..#...", "..ooo.......", ".ooo........", ".oo.........", "............"]),
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
        // Round 3 draws Arrow two ways; see Options.
        ("Arrow", "Points east; flip it in code for west, rotate for north and south.", []),
        ("Lock", "A padlock: one-pixel shackle like Link, solid body, accent keyhole.",
            ["............", "....####....", "...#....#...", "...#....#...", "...#....#...", "..########..", "..########..", "..###oo###..", "..###oo###..", "..########..", "..########..", "............"]),
        ("Trash", "A bin: handle, lid, and a ribbed body with an accent fill.",
            ["............", "....####....", ".##########.", "............", "..########..", "..#o#oo#o#..", "..#o#oo#o#..", "..#o#oo#o#..", "..#o#oo#o#..", "..#o#oo#o#..", "...######...", "............"]),
    ];

    /// <summary>The weather icons shown together in the "weather.set" review picture, left to right.</summary>
    private static readonly string[] WeatherSetIds = ["Sun.a", "Sun.b", "Cloud", "Rain.a", "Rain.b", "Snow.a", "Snow.b", "Storm.a", "Storm.b"];

    /// <summary>The parchment the weather set is shown on, as on the items and glyph sheets.</summary>
    private static readonly Color Parchment = new("E9DCC0");

    public IEnumerable<Entry> Render()
    {
        var entries = new List<Entry>();
        foreach (var glyph in Enum.GetValues<PixelGlyph>())
        {
            var id = glyph.ToString();
            if (Options.TryGetValue(id, out var options))
                entries.AddRange(OptionEntries(id, options));
            else if (Redrawn.TryGetValue(id, out var redrawn))
                entries.Add(new Entry(Family, id, Draw(id, redrawn.Rows, Ink, Accent), redrawn.Why));
            else
                entries.Add(new Entry(Family, id, PixelIcons.Texture(glyph, Ink, Accent, 1).GetImage(), "Unchanged."));
        }
        foreach (var (id, note, rows) in Added)
        {
            if (Options.TryGetValue(id, out var options))
                entries.AddRange(OptionEntries(id, options, "New. " + note + " "));
            else
                entries.Add(new Entry(Family, id, Draw(id, rows, Ink, Accent), "New. " + note));
        }
        // The contact sheet sizes each column to its widest picture, so the wide
        // weather set goes first and only the first column widens.
        yield return new Entry(Family, "weather.set", WeatherSet(entries),
            "Review aid, not an asset: left to right " + string.Join(", ", WeatherSetIds)
            + ", at 3× on parchment, so the weather icons can be judged as one family. Cloud is the approved round-2 drawing.");
        foreach (var entry in entries)
            yield return entry;
    }

    /// <summary>Entries "id.a", "id.b" for a glyph drawn two ways in round 3.</summary>
    private IEnumerable<Entry> OptionEntries(string id, (string Note, string[] Rows)[] options, string prefix = "")
    {
        for (var i = 0; i < options.Length; i++)
        {
            var optionId = $"{id}.{(char)('a' + i)}";
            yield return new Entry(Family, optionId, Draw(optionId, options[i].Rows, Ink, Accent), $"{prefix}Round 3, option {(char)('a' + i)}. {options[i].Note}");
        }
    }

    /// <summary>
    /// The weather icons from <see cref="WeatherSetIds"/> in one row at 3× on
    /// parchment, a little apart and with a wider gap between glyphs than
    /// between the options of one glyph.
    /// </summary>
    private static Image WeatherSet(IReadOnlyList<Entry> entries)
    {
        const int scale = 3;
        const int cell = Grid * scale;
        const int pad = 2 * scale;
        var pictures = WeatherSetIds.Select(id => entries.Single(entry => entry.Id == id).Image).ToList();
        // A wider gap where the glyph changes (Sun.b | Cloud | Rain.a ...) than between options.
        var gaps = WeatherSetIds.Skip(1).Select((id, i) => id.Split('.')[0] == WeatherSetIds[i].Split('.')[0] ? pad : 3 * pad).ToList();
        var width = pad * 2 + cell * pictures.Count + gaps.Sum();
        var image = Sheet.Flat(width, cell + pad * 2, Parchment);
        var x = pad;
        for (var i = 0; i < pictures.Count; i++)
        {
            Sheet.Blend(image, Sheet.Upscale(pictures[i], scale), x, pad);
            x += cell + (i < gaps.Count ? gaps[i] : 0);
        }
        return image;
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
