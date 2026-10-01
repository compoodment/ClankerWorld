using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Pixel-art icons for stored items. Each is drawn by hand on a 16 × 16 grid
/// in three or four shades, and gets a one-pixel outline all the way round
/// its silhouette, in a darker shade of whatever it borders, so the outline
/// is always complete. Icons scale only by whole numbers (16, 32, 48 px) so
/// they stay crisp. An item kind without its own icon yet (for example new
/// content) shows a plain crate, so it is still counted.
/// </summary>
public static class ItemIcons
{
    public const int Grid = 16;
    private const string Fallback = "crate";
    private static readonly Color Ink = new("2A1A10");
    private static readonly Dictionary<(string Kind, int Size), ImageTexture> Cache = [];
    private static readonly Dictionary<string, Image> Art = [];

    /// <summary>Each row is one line of pixels; '.' is empty and every other letter is a palette colour.</summary>
    private sealed record Icon(string Palette, string[] Rows);

    private static readonly Dictionary<string, Icon> Icons = new(StringComparer.Ordinal)
    {
        ["wood"] = new("b6B4423 m8E5C30 lB07A42 fE6C48E rC0925A c9C6A3A",
        [
            "................",
            "................",
            ".....ffllllllll.",
            "....frrfmmmmmmm.",
            "....frcfmmbmmmm.",
            "....frrfbmbbbmb.",
            ".....ffbbbbbbbb.",
            "................",
            "..fflllllllll...",
            ".frrfmmmmmmmm...",
            ".frcfmmbmmmbm...",
            ".frrfbmbbbmbb...",
            "..ffbbbbbbbbb...",
            "................",
            "................",
            "................",
        ]),
        ["stone"] = new("hD6D3CA lAEABA3 m8A8780 d66645E k4A4844",
        [
            "................",
            "................",
            "........hl......",
            "......hhhll.....",
            ".....hhhhlll....",
            "...hhhhhllllm...",
            "..lhhhhllllmmm..",
            "..llhhllllmmmmd.",
            ".lllllllllmmmmd.",
            ".llllllllmmmmdd.",
            ".mllllllmmmmddd.",
            ".mmmlllmmmmmddd.",
            "..mmmmmmmmmdddk.",
            "...mmmmmmdddkk..",
            ".....kkkkkkk....",
            "................",
        ]),
        ["clay"] = new("hEFA882 lD5825A mB8623C d8E4428 k6A3020",
        [
            "................",
            "................",
            "................",
            "....hhhhhhhh....",
            "...hllllllllh...",
            "..hllddlllllll..",
            "..lllllllddlll..",
            ".mllllllllllllm.",
            ".mmmmmmmmmmmmmm.",
            ".mmmmmmmmmmmmmd.",
            ".dmmmmmmmmmmmdd.",
            "..dddddddddddd..",
            "................",
            "................",
            "................",
            "................",
        ]),
        ["storage_pot"] = new("hEBCB88 lC08A4C m985A34 d714027 k35251D",
        [
            "................",
            "................",
            "......kkkk......",
            ".....kllllk.....",
            "....kllllllk....",
            "...kllllllllk...",
            "..kllllllllllk..",
            ".kllhhhhllllllk.",
            "..kllhllllmllk..",
            "..kllddddddllk..",
            ".kllllllllllllk.",
            "..kllllllllllk..",
            "...kllllllllk...",
            "....kkkkkkkk....",
            "................",
            "................",
        ]),
        ["water_jug"] = new("hF0D69A lC58B50 m965B39 d75432B k32241D w65B6D5",
        [
            "................",
            "................",
            "......kkkk......",
            ".....kllllk.....",
            ".....kllllk.....",
            "....kllllllk....",
            "...kllllllllk...",
            "..kllllllllllk..",
            "..kllmmmmmmllk..",
            "..klmwwwwwmllk..",
            "..klmwwwwwmllk..",
            "..kllmmmmmmllk..",
            "..kllllllllllk..",
            "...kllllllllk...",
            "....kkkkkkkk....",
            "................",
        ]),
        ["fresh_water"] = new("hD9FEEE l8AD9F4 m398FBD d24658B k243A60",
        [
            "................",
            "................",
            ".......kk.......",
            "......kllk......",
            ".....kllllk.....",
            "....kllllllk....",
            "...kllllllllk...",
            "..kllllllllllk..",
            ".kllhhhhhhhhllk.",
            "..klhlllllllhk..",
            "..kllmmmmmmllk..",
            "..kllmmmmmmllk..",
            "...kllmmmmllk...",
            "....kllmmllk....",
            ".....kllllk.....",
            "................",
        ]),
        ["iron_ore"] = new("hA69E98 l8A827C m6C6560 d524C48 k3E3A37 oC8662E OF2A060",
        [
            "................",
            "................",
            "........hl......",
            "......hhOll.....",
            ".....hhoOlll....",
            "...hhhhhlloOm...",
            "..lhoOhllllmmm..",
            "..llhhlllommmmd.",
            ".llllOolllmmmmd.",
            ".llllllllmmoOdd.",
            ".mlloOllmmmmddd.",
            ".mmmlllmmOommdd.",
            "..mmmmmmmmmdddk.",
            "...mmmmmmdddkk..",
            ".....kkkkkkk....",
            "................",
        ]),
        ["iron"] = new("hEEF2F4 lBFC8CF m939CA5 d6A727B k50575E",
        [
            "................",
            "................",
            "......hhhh......",
            ".....hllllh.....",
            "....mmmmmmmm....",
            "....mhmmmmmd....",
            "....dddddddk....",
            "...hlllllllll...",
            "..hlllllllllll..",
            ".mmmmmmmmmmmmmd.",
            ".mhmmmmmmmmmmmd.",
            ".mmmmmmmmmmmmmd.",
            ".dddddddddddddk.",
            "................",
            "................",
            "................",
        ]),
        ["fiber"] = new("hF0E0A0 lD8C47A mB8A458 d8E7C3E k6E5E2C",
        [
            "................",
            "................",
            "......llll......",
            "....llhhllmm....",
            "...lhhlldlmmm...",
            "..lhllddllmmmd..",
            "..llddllmmddmd..",
            ".llddllmmddmmdd.",
            ".lddllmmddmmddd.",
            ".llllmmddmmdddd.",
            ".mllmmddmmmdddd.",
            "..mmmddmmddddd..",
            "..mmddmmdddddd..",
            "...mmmddddddm...",
            ".....dddddd..mm.",
            "................",
        ]),
        ["grain"] = new("eE2B64A EF6D882 sB8963E S8A6E2C t7A4E2A",
        [
            "................",
            ".......E........",
            "......eEe.......",
            "...E..EeE..E....",
            "..eEe.eEe.eEe...",
            "..EeE.EeE.EeE...",
            "..eEe..s..eEe...",
            "..EeE..s..EeE...",
            "...s...s...s....",
            "....s..s..s.....",
            ".....s.s.s......",
            "......sss.......",
            ".....ttttt......",
            "......sSs.......",
            ".....s.S.s......",
            "................",
        ]),
        ["flour"] = new("WF8F2E2 wE8DEC6 gCDBF9E GA89878 t8A5A30 sC8A060",
        [
            "................",
            "......ww.ww.....",
            ".......www......",
            "......ttttt.....",
            ".....wWWwwww....",
            "....wWWwwwwwg...",
            "...wWWwwwwwwgg..",
            "..wWwwwwwwwggg..",
            ".wwWwwwwwwwwggg.",
            ".wwwwwwwwwwwggg.",
            ".wwwwwwwwwwgggG.",
            ".gwwwwwwwwggggG.",
            "..ggggggggggGG..",
            "...GGGGGGGGGG...",
            "................",
            "................",
        ]),
        ["seed"] = new("lC8985E m9A6A3A d6A4222",
        [
            "................",
            "................",
            "...ll.....ll....",
            "..lmmm...lmmm...",
            "..mmmd...mmmd...",
            "..mmdd...mmdd...",
            "...dd.....dd....",
            "................",
            "......ll........",
            ".....lmmm...ll..",
            ".....mmmd..lmmm.",
            ".....mmdd..mmmd.",
            "......dd...mmdd.",
            "............dd..",
            "................",
            "................",
        ]),
        // A seed for planting a tree, kept apart from crop seed.
        ["tree_seed"] = new("C9C6E42 c7A5230 k553820 nC8904A NE4B272 d8E5E2C s4E3218",
        [
            "................",
            "........s.......",
            ".......ss.......",
            "....CCCCcccc....",
            "...CCcCcCcCcc...",
            "..CCcCcCcCcCck..",
            "..ccccccccccckk.",
            "...nNNnnnnnnd...",
            "...NNnnnnnnnd...",
            "...Nnnnnnnnnd...",
            "....nnnnnnndd...",
            "....nnnnnnndd...",
            ".....nnnnndd....",
            "......nnndd.....",
            ".......nd.......",
            "................",
        ]),
        ["food"] = new("rC23A2A RE0584A hF8A08A d8A2418 s5E3A1E g6AA640 G3E6E28",
        [
            "................",
            "........s.gg....",
            "........sgGGg...",
            "...RRRR.srrrr...",
            "..RhhRRRrrrrrr..",
            ".RhhRRRrrrrrrrr.",
            ".RhRRRrrrrrrrrr.",
            ".RRRRrrrrrrrrrd.",
            ".RRRrrrrrrrrrrd.",
            ".RRrrrrrrrrrrdd.",
            ".rrrrrrrrrrrrdd.",
            "..rrrrrrrrrrdd..",
            "..rrrrrrrrrddd..",
            "...rrrr..rddd...",
            "................",
            "................",
        ]),
        // Orchard fruit, kept apart from foraged food.
        ["fruit"] = new("yC9C24E YDCD872 hF2EEB0 d8E8428 s5E3A1E g6AA640 G3E6E28",
        [
            "................",
            "........s.......",
            "........sgg.....",
            ".......yyGGg....",
            "......yYyyy.....",
            "......Yhyyy.....",
            ".....yYhyyyd....",
            "....yYhyyyyyd...",
            "...yYyyyyyyyyd..",
            "..yYyyyyyyyyydd.",
            "..Yhyyyyyyyyydd.",
            "..yyyyyyyyyyydd.",
            "..dyyyyyyyyyddd.",
            "...ddyyyyyyddd..",
            ".....ddddddd....",
            "................",
        ]),
        ["berries"] = new("m7E3A96 hC89AE6 lA060C0 d4E2262 g6AA640 G3E6E28 s5E3A1E",
        [
            "................",
            ".......gg.......",
            "......gGGg......",
            ".......s.s......",
            ".....lm...lm....",
            "....lhmm.lhmm...",
            "....mmmd.mmmd...",
            ".....dd...dd....",
            "...lm...lm......",
            "..lhmm.lhmm.lm..",
            "..mmmd.mmmdlhmm.",
            "...dd...dd.mmmd.",
            "............dd..",
            "................",
            "................",
            "................",
        ]),
        ["bread"] = new("cC07A34 CDC9C52 hF2C27E d8A5222 k6A3C18",
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            ".....CCCCCC.....",
            "...CChhChhCCc...",
            "..ChhCChhCChcc..",
            ".CChCCChCCChccc.",
            ".cCCCCCCCCCcccd.",
            ".cccccccccccccd.",
            "..cccccccccccdd.",
            "...kdddddddddk..",
            "................",
            "................",
            "................",
        ]),
        ["cloth"] = new("hFFF5DF lE8DCC0 mCABC99 dA09170 k75674D",
        [
            "................",
            "................",
            "................",
            "....hhhhhhhhh...",
            "...hlllllllllm..",
            "..hlllllllllll..",
            ".hllllllllllllm.",
            ".hlllhllllllmmd.",
            ".hlllhlmmmmlmmd.",
            ".hlllhmmmmmlmmd.",
            ".hlllhlmmmmlmmd.",
            "..lllllmmllmmdd.",
            "...mmmmmmmmdddk.",
            "....ddddddddkk..",
            "................",
            "................",
        ]),
        ["clothing"] = new("b4A6E9E B6C92C4 h92B4DE d32507E t7A4E2A TC09A5A",
        [
            "................",
            "................",
            "...bbbb..bbbb...",
            "..bbBBBd.dBBBbb.",
            ".bbBBBBBdBBBBbb.",
            ".bbBhBBBBBBhBbb.",
            ".bbbBhBBBBBBbbb.",
            ".ddbBBBBBBBBbdd.",
            "....BBBBBBBB....",
            "....ttttTttt....",
            "....bBBBBBBb....",
            "....bBBBBBBb....",
            "....bBBBBBBb....",
            "....dddddddd....",
            "................",
            "................",
        ]),
        ["wooden_axe"] = new("HC08448 D7A4E26 g8C8984 GB8B5AE k5E5C57 EE4E1D8",
        [
            "................",
            "..E......HD.....",
            ".EGGGGGGGHDk....",
            ".EGgggggGHDk....",
            ".EGgggggkHDk....",
            ".EGggggkkHDk....",
            ".EGgggkkkHDk....",
            "..Ekkkkk.HD.....",
            ".........HD.....",
            ".........HD.....",
            ".........HD.....",
            ".........HD.....",
            ".........HD.....",
            ".........DD.....",
            "................",
            "................",
        ]),
        ["wooden_pickaxe"] = new("HC08448 D7A4E26 g8C8984 GB8B5AE k5E5C57",
        [
            "................",
            "................",
            "....GGGHDGGG....",
            "..GGgggHDgggkk..",
            ".Ggk...HD...kgk.",
            ".gk....HD....kk.",
            ".k.....HD.....k.",
            ".......HD.......",
            ".......HD.......",
            ".......HD.......",
            ".......HD.......",
            ".......HD.......",
            ".......HD.......",
            ".......DD.......",
            "................",
            "................",
        ]),
        ["tool"] = new("HC08448 D7A4E26 g8C8984 GB8B5AE k5E5C57",
        [
            "................",
            "................",
            "..kGGGGGGGGk....",
            "..kGgggggggk....",
            "..kkkkHDkkkk....",
            "......HD........",
            "......HD........",
            "......HD........",
            "......HD........",
            "......HD........",
            "......HD........",
            "......HD........",
            "......HD........",
            "......DD........",
            "................",
            "................",
        ]),
        ["crate"] = new("wB07A42 WD09A5A d7A4E26",
        [
            "................",
            "................",
            ".WWWWWWWWWWWWWW.",
            ".WddwwwwwwwwwdW.",
            ".WwddwwwwwwwwwW.",
            ".WwwddwwwwwwwwW.",
            ".WwwwddwwwwwwwW.",
            ".WWWWWWWWWWWWWW.",
            ".WwwwwwwddwwwwW.",
            ".WwwwwwwwddwwwW.",
            ".WwwwwwwwwddwwW.",
            ".WwwwwwwwwwddwW.",
            ".WdwwwwwwwwwddW.",
            ".dddddddddddddd.",
            "................",
            "................",
        ]),
    };

    /// <summary>The item kinds that have their own icon.</summary>
    public static IReadOnlyCollection<string> Kinds => Icons.Keys.Where(kind => kind != Fallback).ToArray();

    /// <summary>Whether this kind has its own icon rather than the crate.</summary>
    public static bool Has(string kind) => kind != Fallback && Icons.ContainsKey(kind);

    public static ImageTexture Texture(string kind, int size)
    {
        var key = (kind, size);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(Render(kind, size));
        Cache[key] = texture;
        return texture;
    }

    /// <summary>
    /// The icon at the largest whole-number scale that fits <paramref name="size"/>,
    /// centred on a transparent square of that size.
    /// </summary>
    public static Image Render(string kind, int size)
    {
        var art = OutlinedArt(Icons.ContainsKey(kind) ? kind : Fallback);
        var scale = Math.Max(1, size / Grid);
        var offset = (size - Grid * scale) / 2;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        for (var y = 0; y < Grid; y++)
            for (var x = 0; x < Grid; x++)
            {
                var color = art.GetPixel(x, y);
                if (color.A > 0) image.FillRect(new Rect2I(offset + x * scale, offset + y * scale, scale, scale), color);
            }
        return image;
    }

    /// <summary>The 16 × 16 art with its outline, drawn once per kind.</summary>
    public static Image OutlinedArt(string kind)
    {
        if (Art.TryGetValue(kind, out var cached)) return cached;
        var icon = Icons[kind];
        var palette = icon.Palette.Split(' ').ToDictionary(entry => entry[0], entry => new Color(entry[1..]));
        var fill = new Color?[Grid, Grid];
        for (var y = 0; y < Grid; y++)
            for (var x = 0; x < Grid; x++)
                if (icon.Rows[y][x] is not '.' and var key) fill[x, y] = palette[key];
        var image = Image.CreateEmpty(Grid, Grid, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        for (var y = 0; y < Grid; y++)
            for (var x = 0; x < Grid; x++)
            {
                if (fill[x, y] is { } color)
                {
                    image.SetPixel(x, y, color);
                    continue;
                }
                // An empty pixel beside the silhouette becomes outline, in a
                // dark shade of the darkest colour it touches.
                Color? darkest = null;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (x + dx is >= 0 and < Grid && y + dy is >= 0 and < Grid && fill[x + dx, y + dy] is { } near &&
                        (darkest is null || near.Luminance < darkest.Value.Luminance))
                        darkest = near;
                if (darkest is { } edge) image.SetPixel(x, y, (edge * 0.42f).Lerp(Ink, 0.45f) with { A = 1 });
            }
        Art[kind] = image;
        return image;
    }

    /// <summary>Whether every row is 16 wide and no pixel touches the edge, so the outline fits.</summary>
    public static bool FitsGrid(string kind) => Icons.TryGetValue(kind, out var icon) && icon.Rows.Length == Grid &&
        icon.Rows.All(row => row.Length == Grid) &&
        icon.Rows[0].All(pixel => pixel == '.') && icon.Rows[^1].All(pixel => pixel == '.') &&
        icon.Rows.All(row => row[0] == '.' && row[^1] == '.');
}
