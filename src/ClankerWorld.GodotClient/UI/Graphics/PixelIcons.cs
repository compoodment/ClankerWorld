using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Interface icons drawn on a 12×12 pixel grid.</summary>
public enum PixelGlyph
{
    Map,
    Filter,
    Info,
    Person,
    House,
    Scroll,
    PersonPlus,
    Menu,
    Play,
    Pause,
    Sun,
    Cloud,
    Rain,
    Snow,
    Storm,
    Flower,
    Leaf,
    Snowflake,
    Gear,
    Book,
    Box,
    Door,
    Close,
    Back,
    Globe,
    Folder,
    Link,
}

/// <summary>
/// The interface's pixel icons: two-color 12×12 bitmaps ("#" main, "o"
/// accent) scaled by whole numbers so their edges stay crisp. Weather and
/// season icons keep natural colors in both themes; the rest follow the
/// current palette's ink and accent.
/// </summary>
public static class PixelIcons
{
    public const int Grid = 12;
    private static readonly Dictionary<(PixelGlyph, Color, Color, int), ImageTexture> Cache = [];

    private static readonly Dictionary<PixelGlyph, string[]> Glyphs = new()
    {
        [PixelGlyph.Map] = ["............", "............", ".##########.", ".#o......o#.", ".#.o....o.#.", ".#..o..o..#.", ".#...oo...#.", ".#..o.....#.", ".#.o......#.", ".##########.", "............", "............"],
        [PixelGlyph.Filter] = ["............", ".##########.", ".#oooooooo#.", "..#oooooo#..", "...#oooo#...", "....#oo#....", "....#oo#....", "....#oo#....", "....#oo#....", ".....##.....", "............", "............"],
        [PixelGlyph.Info] = ["............", "....####....", "..##....##..", "..#..oo..#..", ".#........#.", ".#...oo...#.", ".#...oo...#.", ".#...oo...#.", "..#..oo..#..", "..##....##..", "....####....", "............"],
        [PixelGlyph.Person] = ["............", ".....##.....", "....####....", "....####....", ".....##.....", "...######...", "..########..", "..#.####.#..", "....####....", "....#..#....", "....#..#....", "............"],
        [PixelGlyph.House] = ["............", ".....##.....", "....####....", "...######...", "..########..", ".##########.", "..#oo##oo#..", "..#oo##oo#..", "..########..", "..###..###..", "..###..###..", "............"],
        [PixelGlyph.Scroll] = ["............", "..########..", ".#o######o#.", "..#......#..", "..#.####.#..", "..#......#..", "..#.###..#..", "..#......#..", "..#.####.#..", ".#o######o#.", "..########..", "............"],
        [PixelGlyph.PersonPlus] = ["............", "....##......", "...####.....", "...####..o..", "....##..ooo.", "..######.o..", ".########...", ".#.####.#...", "...####.....", "...#..#.....", "...#..#.....", "............"],
        [PixelGlyph.Menu] = ["............", "............", ".##########.", ".##########.", "............", ".##########.", ".##########.", "............", ".##########.", ".##########.", "............", "............"],
        [PixelGlyph.Play] = ["............", "...#........", "...##.......", "...###......", "...####.....", "...#####....", "...#####....", "...####.....", "...###......", "...##.......", "...#........", "............"],
        [PixelGlyph.Pause] = ["............", "............", "...##..##...", "...##..##...", "...##..##...", "...##..##...", "...##..##...", "...##..##...", "...##..##...", "...##..##...", "............", "............"],
        [PixelGlyph.Sun] = ["............", ".....o......", "..o.....o...", "....###.....", "...#####....", ".o.#####.o..", "...#####....", "....###.....", "..o.....o...", ".....o......", "............", "............"],
        [PixelGlyph.Cloud] = ["............", "............", "....###.....", "...#####....", "..#######...", ".#########..", "###########.", "###########.", ".#########..", "............", "............", "............"],
        [PixelGlyph.Rain] = ["....###.....", "...#####....", "..#######...", ".#########..", ".#########..", "............", "..o..o..o...", ".o..o..o....", "............", "..o..o..o...", ".o..o..o....", "............"],
        [PixelGlyph.Snow] = ["....###.....", "...#####....", "..#######...", ".#########..", ".#########..", "............", ".o...o...o..", "............", "...o...o....", "............", ".o...o...o..", "............"],
        [PixelGlyph.Storm] = ["....###.....", "...#####....", "..#######...", ".#########..", ".#########..", ".....oo.....", "....oo......", "...oooo.....", ".....oo.....", "....oo......", "...oo.......", "............"],
        [PixelGlyph.Flower] = ["............", ".....o......", "....ooo.....", "...oo#oo....", "....ooo.....", ".....o......", ".....#......", "...#.#......", "....##.##...", ".....##.....", ".....#......", "............"],
        [PixelGlyph.Leaf] = ["............", "........###.", "......####o.", ".....###o##.", "....##o####.", "...#o#####..", "...o####....", "..o.###.....", ".o..........", "o...........", "............", "............"],
        [PixelGlyph.Snowflake] = ["............", ".....#......", "...#.#.#....", "....###.....", ".#..#o#..#..", "..#######...", ".#..#o#..#..", "....###.....", "...#.#.#....", ".....#......", "............", "............"],
        [PixelGlyph.Gear] = ["............", ".....##.....", "..#.####.#..", "..########..", "...##..##...", ".###....###.", ".###....###.", "...##..##...", "..########..", "..#.####.#..", ".....##.....", "............"],
        [PixelGlyph.Book] = ["............", "..########..", "..#oooooo#..", "..#o####o#..", "..#oooooo#..", "..#o###oo#..", "..#oooooo#..", "..#o####o#..", "..#oooooo#..", "..########..", "...#....#...", "............"],
        [PixelGlyph.Box] = ["............", "...######...", "..#o####o#..", ".##########.", ".#o......o#.", ".#...##...#.", ".#...##...#.", ".#........#.", ".#o......o#.", ".##########.", "............", "............"],
        [PixelGlyph.Door] = ["............", "######......", "#oooo#..#...", "#oooo#..##..", "#oooo#..###.", "#oo#o#######", "#oooo#######", "#oooo#..###.", "#oooo#..##..", "#oooo#..#...", "######......", "............"],
        [PixelGlyph.Close] = ["............", "............", "..##....##..", "...##..##...", "....####....", ".....##.....", "....####....", "...##..##...", "..##....##..", "............", "............", "............"],
        [PixelGlyph.Back] = ["............", "......##....", ".....##.....", "....##......", "...##.......", "...##.......", "....##......", ".....##.....", "......##....", "............", "............", "............"],
        [PixelGlyph.Globe] = ["............", "....####....", "..##oo..##..", ".#ooo.o...#.", ".#.oooo...#.", "#...oo..oo.#", "#......ooo.#", ".#....ooo.#.", ".#.....o..#.", "..##....##..", "....####....", "............"],
        [PixelGlyph.Link] = ["............", "....####....", "...#....#...", "...#....#...", "...#.oo.#...", ".....oo.....", ".....oo.....", "...#.oo.#...", "...#....#...", "...#....#...", "....####....", "............"],
        [PixelGlyph.Folder] = ["............", "............", ".####.......", ".#oo#######.", ".#oooooooo#.", ".#oooooooo#.", ".#oooooooo#.", ".#oooooooo#.", ".##########.", "............", "............", "............"],
    };

    /// <summary>A glyph drawn in the given colors, scaled by a whole number.</summary>
    public static ImageTexture Texture(PixelGlyph glyph, Color main, Color accent, int scale = 2)
    {
        scale = Math.Max(1, scale);
        var key = (glyph, main, accent, scale);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var rows = Glyphs[glyph];
        var image = Image.CreateEmpty(Grid * scale, Grid * scale, false, Image.Format.Rgba8);
        for (var y = 0; y < Grid; y++)
            for (var x = 0; x < Grid; x++)
            {
                var color = rows[y][x] switch
                {
                    '#' => main,
                    'o' => accent,
                    _ => new Color(0, 0, 0, 0),
                };
                if (color.A <= 0) continue;
                image.FillRect(new Rect2I(x * scale, y * scale, scale, scale), color);
            }
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }

    /// <summary>An interface icon in the current theme: ink with the palette's accent.</summary>
    public static ImageTexture Themed(PixelGlyph glyph, Color accent, int scale = 2) =>
        Texture(glyph, UiTheme.Current.Ink, accent, scale);

    /// <summary>Weather keeps its natural colors so it reads the same in both themes.</summary>
    public static ImageTexture Weather(string weather, int scale = 2)
    {
        var dark = UiTheme.Current.Name == "dark";
        var cloud = dark ? new Color("B7A485") : new Color("6E5A45");
        return weather.ToLowerInvariant() switch
        {
            "rain" => Texture(PixelGlyph.Rain, cloud, dark ? new Color("7FB4E0") : new Color("2F6FA3"), scale),
            "storm" => Texture(PixelGlyph.Storm, cloud, dark ? new Color("FFD166") : new Color("C98A12"), scale),
            "snow" => Texture(PixelGlyph.Snow, cloud, dark ? new Color("FFFFFF") : new Color("6E8FA8"), scale),
            "cloudy" => Texture(PixelGlyph.Cloud, cloud, cloud, scale),
            _ => Texture(PixelGlyph.Sun, dark ? new Color("F2C14E") : new Color("C98A12"), dark ? new Color("F2C14E") : new Color("C98A12"), scale),
        };
    }

    /// <summary>Season icons: a spring flower, summer sun, autumn leaf and winter snowflake.</summary>
    public static ImageTexture Season(string season, int scale = 2)
    {
        var dark = UiTheme.Current.Name == "dark";
        return season.ToLowerInvariant() switch
        {
            "summer" => Texture(PixelGlyph.Sun, dark ? new Color("F2C14E") : new Color("C98A12"), dark ? new Color("F2C14E") : new Color("C98A12"), scale),
            "autumn" or "fall" => Texture(PixelGlyph.Leaf, dark ? new Color("E8913A") : new Color("B8612A"), dark ? new Color("F4C27A") : new Color("7A3E14"), scale),
            "winter" => Texture(PixelGlyph.Snowflake, dark ? new Color("BFE3F7") : new Color("4F7EA0"), dark ? new Color("FFFFFF") : new Color("9FC6DE"), scale),
            _ => Texture(PixelGlyph.Flower, dark ? new Color("8DBA6A") : new Color("4A7033"), dark ? new Color("E88AAE") : new Color("B8457A"), scale),
        };
    }
}
