using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>One drawable state of a tree or natural resource site.</summary>
public enum NatureSprite : byte
{
    Broadleaf,
    Conifer,
    BroadleafStump,
    ConiferStump,
    BroadleafSapling,
    ConiferSapling,
    OrchardFruiting,
    OrchardPicked,
    OrchardGrowing,
    BerryBush,
    WildGreens,
    FiberPlant,
    Reeds,
    StoneOutcrop,
    IronOutcrop,
    GoldOutcrop,
    DiamondOutcrop,
    ClayBank,
    WildSeedPatch,
    FertileSoil,
    Depleted,
    Regrowing,
    WoodPile,
}

/// <summary>
/// Provisional top-down pixel-art sprites for trees and natural sites,
/// generated deterministically at 32×32 (and 16×16 for mid zoom) on a
/// transparent background. Each sprite stays inside its tile, keeps a soft
/// shadow to the south-east, and uses a darker outline so it reads on any
/// ground texture.
/// </summary>
public static class NatureSprites
{
    private static readonly int SpriteCount = Enum.GetValues<NatureSprite>().Length;
    private static readonly Dictionary<int, Image> Images = [];
    private static readonly Dictionary<int, ImageTexture> Textures = [];
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    /// <summary>Tree codes used by the terrain layer: 1/2 mature, 3/4 stump, 5/6 sapling, 7–9 orchard stages.</summary>
    public static NatureSprite? ForTree(byte code) => code switch
    {
        1 => NatureSprite.Broadleaf,
        2 => NatureSprite.Conifer,
        3 => NatureSprite.BroadleafStump,
        4 => NatureSprite.ConiferStump,
        5 => NatureSprite.BroadleafSapling,
        6 => NatureSprite.ConiferSapling,
        7 => NatureSprite.OrchardFruiting,
        8 => NatureSprite.OrchardPicked,
        9 => NatureSprite.OrchardGrowing,
        _ => null,
    };

    /// <summary>Natural-object codes and stages used by the terrain layer; stage 1 is depleted, 2 regrowing.</summary>
    public static NatureSprite? ForNaturalObject(byte kind, byte stage) => stage switch
    {
        1 => NatureSprite.Depleted,
        2 => NatureSprite.Regrowing,
        _ => kind switch
        {
            1 => NatureSprite.BerryBush,
            2 => NatureSprite.WildGreens,
            3 => NatureSprite.FiberPlant,
            4 => NatureSprite.Reeds,
            5 => NatureSprite.StoneOutcrop,
            6 => NatureSprite.WildSeedPatch,
            7 => NatureSprite.FertileSoil,
            8 => NatureSprite.IronOutcrop,
            9 => NatureSprite.GoldOutcrop,
            10 => NatureSprite.DiamondOutcrop,
            11 => NatureSprite.ClayBank,
            _ => null,
        },
    };

    /// <summary>
    /// Older camp resources record only a resource kind, not a natural site;
    /// they borrow the matching site's look, and gathered wood is a log pile.
    /// </summary>
    public static NatureSprite? ForCampResource(string kind) => kind switch
    {
        "food" => NatureSprite.BerryBush,
        "construction" => NatureSprite.WoodPile,
        "stone" => NatureSprite.StoneOutcrop,
        "fiber" => NatureSprite.FiberPlant,
        "seed" => NatureSprite.WildSeedPatch,
        "fertile_land" => NatureSprite.FertileSoil,
        _ => null,
    };

    public static int AtlasTileSize(int drawnTileSize) => drawnTileSize >= 24 ? 32 : 16;

    public static ImageTexture Atlas(int size)
    {
        if (Textures.TryGetValue(size, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(AtlasImage(size));
        Textures[size] = texture;
        return texture;
    }

    public static Rect2 Region(NatureSprite sprite, int size) => new((int)sprite * size, 0, size, size);

    public static Image Sprite(NatureSprite sprite, int size) =>
        AtlasImage(size).GetRegion(new Rect2I((int)sprite * size, 0, size, size));

    private static Image AtlasImage(int size)
    {
        if (Images.TryGetValue(size, out var cached)) return cached;
        var image = Image.CreateEmpty(size * SpriteCount, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        foreach (var sprite in Enum.GetValues<NatureSprite>())
            Paint(new PixelCanvas(image, new Rect2I((int)sprite * size, 0, size, size), size / 32f), sprite);
        Images[size] = image;
        return image;
    }

    private static void Paint(PixelCanvas canvas, NatureSprite sprite)
    {
        switch (sprite)
        {
            case NatureSprite.Broadleaf:
            case NatureSprite.OrchardFruiting:
            case NatureSprite.OrchardPicked:
                var orchard = sprite != NatureSprite.Broadleaf;
                canvas.Ellipse(18, 19, 12, 10, Shadow);
                canvas.Lumpy(16, 15, 12.5f, new Color("2E4A2A"), 7, 1);
                canvas.Lumpy(16, 15, 11.5f, orchard ? new Color("5E8C45") : new Color("557D3E"), 7, 1);
                canvas.Lumpy(14, 13, 7.5f, orchard ? new Color("79A657") : new Color("6C9A4B"), 5, 2);
                canvas.Disc(11, 10, 3, orchard ? new Color("9BC66F") : new Color("8DB660"));
                if (sprite == NatureSprite.OrchardFruiting)
                    foreach (var (x, y) in new[] { (21, 12), (11, 18), (18, 20), (15, 8), (23, 18) })
                        canvas.Fruit(x, y, new Color("E0893F"), new Color("F6C27A"));
                break;
            case NatureSprite.Conifer:
                canvas.Ellipse(18, 19, 11, 9, Shadow);
                canvas.Star(16, 15, 12.5f, 7, new Color("1F3E31"), 9);
                canvas.Star(16, 15, 11.5f, 6, new Color("2F5B45"), 9);
                canvas.Star(16, 15, 8, 4, new Color("3F7358"), 9);
                canvas.Star(16, 15, 4.5f, 2, new Color("5E9278"), 9);
                canvas.Disc(16, 15, 1, new Color("86B89A"));
                break;
            case NatureSprite.BroadleafStump:
            case NatureSprite.ConiferStump:
                canvas.Ellipse(17, 18, 7, 5, Shadow);
                canvas.Disc(16, 16, 6, new Color("4E3524"));
                canvas.Disc(16, 16, 5, sprite == NatureSprite.ConiferStump ? new Color("8C6A48") : new Color("9C7650"));
                canvas.Ring(16, 16, 3, new Color("735036"));
                canvas.Disc(16, 16, 1, new Color("735036"));
                canvas.Dot(14, 13, new Color("C09A6B"));
                break;
            case NatureSprite.BroadleafSapling:
            case NatureSprite.ConiferSapling:
            case NatureSprite.OrchardGrowing:
                var young = sprite switch
                {
                    NatureSprite.ConiferSapling => (Dark: new Color("2F5B45"), Light: new Color("7BA88B")),
                    NatureSprite.OrchardGrowing => (Dark: new Color("4C7A3A"), Light: new Color("9BC66F")),
                    _ => (Dark: new Color("4C7A3A"), Light: new Color("94B465")),
                };
                var radius = sprite == NatureSprite.OrchardGrowing ? 7.5f : 5.5f;
                canvas.Ellipse(17, 19, radius, radius * 0.7f, Shadow);
                canvas.Disc(16, 17, 1, new Color("735036"));
                if (sprite == NatureSprite.ConiferSapling)
                    canvas.Star(16, 16, radius, radius * 0.5f, young.Dark, 7);
                else
                    canvas.Lumpy(16, 16, radius, young.Dark, 5, 1);
                canvas.Lumpy(15, 15, radius * 0.6f, young.Light, 4, 3);
                break;
            case NatureSprite.BerryBush:
                canvas.Ellipse(17, 20, 10, 7, Shadow);
                canvas.Lumpy(16, 17, 10, new Color("263E27"), 9, 4);
                canvas.Lumpy(16, 17, 9, new Color("426744"), 9, 4);
                canvas.Lumpy(14, 15, 5, new Color("5C8A4E"), 6, 5);
                foreach (var (x, y) in new[] { (20, 14), (12, 19), (18, 21), (22, 18), (15, 12), (10, 15) })
                    canvas.Fruit(x, y, new Color("C4474B"), new Color("F08A8A"));
                break;
            case NatureSprite.WildGreens:
                canvas.Ellipse(17, 20, 9, 6, Shadow);
                canvas.Leaf(11, 17, 7, 4, -0.7f, new Color("4E7A3F"), new Color("B6CF8A"));
                canvas.Leaf(20, 16, 7, 4, 0.8f, new Color("5E8C48"), new Color("C1D993"));
                canvas.Leaf(16, 11, 6, 3.5f, 0.1f, new Color("6E9A52"), new Color("C9DE9E"));
                canvas.Leaf(16, 20, 6, 3.5f, 1.6f, new Color("557F43"), new Color("B6CF8A"));
                break;
            case NatureSprite.FiberPlant:
                canvas.Ellipse(17, 19, 9, 6, Shadow);
                for (var blade = 0; blade < 9; blade++)
                {
                    var angle = blade * Mathf.Tau / 9 + 0.3f;
                    canvas.Line(16, 16, 16 + Mathf.Cos(angle) * 11, 16 + Mathf.Sin(angle) * 11,
                        blade % 2 == 0 ? new Color("6E8A4B") : new Color("8FA863"));
                }
                canvas.Disc(16, 16, 2, new Color("556E3C"));
                break;
            case NatureSprite.Reeds:
                canvas.Ellipse(17, 19, 10, 6, Shadow);
                foreach (var (x, y) in new[] { (10, 12), (15, 9), (21, 11), (12, 19), (18, 17), (23, 20), (16, 23) })
                {
                    canvas.Line(x, y + 4, x - 1, y - 2, new Color("6E7F46"));
                    canvas.Line(x + 1, y + 4, x + 2, y - 1, new Color("8A9A55"));
                    canvas.Disc(x, y - 2, 1, new Color("7A5534"));
                }
                break;
            case NatureSprite.StoneOutcrop:
            case NatureSprite.IronOutcrop:
            case NatureSprite.GoldOutcrop:
            case NatureSprite.DiamondOutcrop:
                canvas.Ellipse(18, 20, 11, 7, Shadow);
                var stone = sprite switch
                {
                    NatureSprite.IronOutcrop => (Dark: new Color("4F4F4C"), Mid: new Color("6E6D68"), Light: new Color("97948B")),
                    NatureSprite.GoldOutcrop => (Dark: new Color("56534A"), Mid: new Color("77725F"), Light: new Color("A29C84")),
                    NatureSprite.DiamondOutcrop => (Dark: new Color("4C5356"), Mid: new Color("6A7276"), Light: new Color("98A2A6")),
                    _ => (Dark: new Color("5B5A55"), Mid: new Color("807E76"), Light: new Color("AAA79C")),
                };
                canvas.Boulder(12, 18, 7, stone.Dark, stone.Mid, stone.Light);
                canvas.Boulder(20, 15, 8, stone.Dark, stone.Mid, stone.Light);
                canvas.Boulder(18, 22, 5, stone.Dark, stone.Mid, stone.Light);
                if (sprite == NatureSprite.IronOutcrop)
                {
                    canvas.Line(17, 12, 22, 17, new Color("A9643C"));
                    canvas.Line(9, 17, 13, 20, new Color("B7774C"));
                    canvas.Dot(22, 12, new Color("C98A5A"));
                }
                else if (sprite == NatureSprite.GoldOutcrop)
                    foreach (var (x, y) in new[] { (19, 13), (22, 16), (11, 17), (18, 21) })
                        canvas.Fruit(x, y, new Color("D9AE3C"), new Color("FFE28A"));
                else if (sprite == NatureSprite.DiamondOutcrop)
                    foreach (var (x, y) in new[] { (20, 13), (12, 17), (19, 21) })
                        canvas.Crystal(x, y, new Color("7FD3DC"), new Color("E8FFFF"));
                break;
            case NatureSprite.ClayBank:
                canvas.Ellipse(17, 20, 11, 7, Shadow);
                canvas.Lumpy(16, 17, 11, new Color("6A4633"), 5, 6);
                canvas.Lumpy(16, 17, 10, new Color("946A4E"), 5, 6);
                canvas.Ellipse(13, 14, 4, 2, new Color("B98A65"));
                canvas.Line(10, 20, 15, 19, new Color("6E4B38"));
                canvas.Line(18, 22, 23, 18, new Color("6E4B38"));
                break;
            case NatureSprite.WildSeedPatch:
                canvas.Ellipse(17, 20, 9, 6, Shadow);
                foreach (var (x, y) in new[] { (11, 13), (16, 10), (21, 13), (13, 19), (19, 18), (16, 23) })
                {
                    canvas.Line(x, y + 4, x, y, new Color("8C7A3F"));
                    canvas.Ellipse(x, y - 1, 1.5f, 2, new Color("C8B066"));
                }
                break;
            case NatureSprite.FertileSoil:
                canvas.Ellipse(16, 17, 12, 9, new Color("4A3A2A"));
                canvas.Ellipse(16, 16, 11, 8, new Color("5E4A36"));
                for (var row = 0; row < 4; row++)
                    canvas.Line(8, 11 + row * 3.5f, 24, 11 + row * 3.5f, new Color("4A3A2A"));
                canvas.Dot(12, 12, new Color("8E7552"));
                canvas.Dot(20, 19, new Color("8E7552"));
                break;
            case NatureSprite.Depleted:
                canvas.Ellipse(16, 17, 8, 5, new Color("6C6452", 0.7f));
                foreach (var (x, y) in new[] { (12, 16), (18, 15), (15, 19), (20, 19) })
                    canvas.Disc(x, y, 1, new Color("8C8577"));
                break;
            case NatureSprite.WoodPile:
                // Seen from above: logs lying side by side, a second layer
                // across the middle, with pale cut ends on the east side.
                canvas.Ellipse(17, 18, 12, 9, Shadow);
                foreach (var (y, top) in new[] { (9f, false), (13f, false), (17f, false), (21f, false), (11f, true), (15f, true), (19f, true) })
                {
                    var left = top ? 9f : 6f;
                    var right = top ? 24f : 26f;
                    canvas.Rect(left, y - 2, right - left, 4, new Color("3F2A1A"));
                    canvas.Rect(left, y - 1.5f, right - left, 3, top ? new Color("8A6440") : new Color("6E4E31"));
                    canvas.Rect(left + 1, y - 1.5f, right - left - 2, 1, top ? new Color("A77C52") : new Color("85603D"));
                    canvas.Disc(right, y, 2, new Color("3F2A1A"));
                    canvas.Disc(right, y, 1.5f, new Color("D2AC77"));
                    if (canvas.Unit >= 1) canvas.Dot(right, y, new Color("9C7447"));
                }
                break;
            case NatureSprite.Regrowing:
                canvas.Ellipse(16, 19, 5, 3, new Color("5A4635", 0.55f));
                canvas.Line(16, 19, 16, 14, new Color("587D40"));
                canvas.Leaf(13, 14, 3, 1.8f, -0.6f, new Color("6E9A4B"), new Color("A6C77A"));
                canvas.Leaf(19, 14, 3, 1.8f, 0.6f, new Color("6E9A4B"), new Color("A6C77A"));
                break;
        }
    }
}
