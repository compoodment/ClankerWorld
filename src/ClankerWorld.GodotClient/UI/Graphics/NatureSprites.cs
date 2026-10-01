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
/// Top-down pixel-art sprites for trees and natural sites, generated
/// deterministically at 32×32 (and 16×16 for mid zoom) on a transparent
/// background. Each sprite stays inside its tile and keeps a soft shadow to
/// the south-east. The sprites the owner approved in the first art review
/// (see <see cref="IsApproved"/>) are lit from the north-west and carry a
/// one-pixel outline in their darkest colour (the fibre plant's blades take a
/// dark south-east edge instead); the others keep their earlier provisional
/// drawing with a darker disc behind the shape.
/// </summary>
public static class NatureSprites
{
    private static readonly int SpriteCount = Enum.GetValues<NatureSprite>().Length;
    private static readonly Dictionary<int, Image> Images = [];
    private static readonly Dictionary<int, ImageTexture> Textures = [];
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    /// <summary>The sprite for a terrain-layer tree code, as listed in <see cref="TreeArtManifest"/>.</summary>
    public static NatureSprite? ForTree(byte code) => TreeArtManifest.ForCode(code)?.Sprite;

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
        {
            var cell = new Rect2I((int)sprite * size, 0, size, size);
            if (IsApproved(sprite)) PaintApproved(new Layers(image, cell), sprite);
            else Paint(new PixelCanvas(image, cell, size / 32f), sprite);
        }
        Images[size] = image;
        return image;
    }

    /// <summary>The earlier provisional drawing, kept for the sprites the art review has not redrawn yet.</summary>
    private static void Paint(PixelCanvas canvas, NatureSprite sprite)
    {
        switch (sprite)
        {
            case NatureSprite.ConiferStump:
                canvas.Ellipse(17, 18, 7, 5, Shadow);
                canvas.Disc(16, 16, 6, new Color("4E3524"));
                canvas.Disc(16, 16, 5, new Color("8C6A48"));
                canvas.Ring(16, 16, 3, new Color("735036"));
                canvas.Disc(16, 16, 1, new Color("735036"));
                canvas.Dot(14, 13, new Color("C09A6B"));
                break;
            case NatureSprite.ConiferSapling:
                var radius = 5.5f;
                canvas.Ellipse(17, 19, radius, radius * 0.7f, Shadow);
                canvas.Disc(16, 17, 1, new Color("735036"));
                canvas.Star(16, 16, radius, radius * 0.5f, new Color("2F5B45"), 7);
                canvas.Lumpy(15, 15, radius * 0.6f, new Color("7BA88B"), 4, 3);
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
            case NatureSprite.DiamondOutcrop:
                canvas.Ellipse(18, 20, 11, 7, Shadow);
                var (dark, mid, light) = (new Color("4C5356"), new Color("6A7276"), new Color("98A2A6"));
                canvas.Boulder(12, 18, 7, dark, mid, light);
                canvas.Boulder(20, 15, 8, dark, mid, light);
                canvas.Boulder(18, 22, 5, dark, mid, light);
                foreach (var (x, y) in new[] { (20, 13), (12, 17), (19, 21) })
                    canvas.Crystal(x, y, new Color("7FD3DC"), new Color("E8FFFF"));
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

    // ----------------------------------------------------------------------
    // Approved art (first art review, 2026-10-01).
    // ----------------------------------------------------------------------

    /// <summary>A five-step colour ramp from the art style guide: edge (outline), shade, base, light, highlight.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));
    }

    // The bush uses the forest-grass ramp so it stays darker than a tree
    // canopy. The fibre plant keeps its earlier yellow-greens arranged as a
    // ramp with the grass edge for its dark side.
    private static readonly Ramp Canopy = Ramp.Of("2E4A2A", "476B36", "557D3E", "6C9A4B", "8DB660");
    private static readonly Ramp OrchardCanopy = Ramp.Of("3C5F2E", "4C7A3A", "5E8C45", "79A657", "9BC66F");
    private static readonly Ramp Needle = Ramp.Of("1F3E31", "2F5B45", "3F7358", "5E9278", "86B89A");
    private static readonly Ramp Bush = Ramp.Of("2E4D35", "395F41", "426D4B", "4E7D56", "5E8E64");
    private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
    private static readonly Ramp Rock = Ramp.Of("4A4542", "625B56", "756D68", "8B837D", "A49C95");
    private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
    private static readonly Ramp Rust = Ramp.Of("7A4426", "A9643C", "B7774C", "C98A5A", "E0A070");
    private static readonly Ramp Gold = Ramp.Of("8A6A1E", "B8902E", "D9AE3C", "F2CC5E", "FFE28A");
    private static readonly Ramp Clay = Ramp.Of("6A3020", "8E4428", "B8623C", "D5825A", "EFA882");
    private static readonly Ramp Berry = Ramp.Of("7A2A2E", "A33A3F", "C4474B", "F08A8A", "FFC2C2");
    private static readonly Ramp Fiber = Ramp.Of("3B5E3A", "556E3C", "6E8A4B", "8FA863", "A8BE78");
    private static readonly Ramp Fruit = Ramp.Of("9A4E1E", "C8702E", "E0893F", "F6C27A", "FFE0A8");

    /// <summary>
    /// The weathered top of a clay bank: each step is halfway between the Clay
    /// and Fertile soil ramps, so the site keeps its brown while the freshly
    /// dug cut shows the Clay ramp.
    /// </summary>
    private static readonly Ramp Bank = Ramp.Of("5A3525", "75472E", "966040", "AD7954", "C49671");

    /// <summary>Boulders of a plain stone outcrop: the Rock ramp one step lighter, with one added highlight for the lit bevel.</summary>
    private static readonly Ramp Boulders = Ramp.Of("4A4542", "756D68", "8B837D", "A49C95", "B9B2AB");

    /// <summary>The pale leaf vein of wild greens.</summary>
    private static readonly Color GreensVein = new("B6CF8A");

    /// <summary>Light grass tufts growing on a weathered bank.</summary>
    private static readonly Color GrassTuft = new("6FA069");

    private enum Ore { None, Iron, Gold }

    /// <summary>The sprites the owner approved in the first art review; the rest keep the earlier drawing.</summary>
    private static bool IsApproved(NatureSprite sprite) => sprite is NatureSprite.Broadleaf or NatureSprite.Conifer or
        NatureSprite.BroadleafStump or NatureSprite.BroadleafSapling or NatureSprite.OrchardGrowing or
        NatureSprite.OrchardFruiting or NatureSprite.OrchardPicked or NatureSprite.BerryBush or NatureSprite.WildGreens or
        NatureSprite.FiberPlant or NatureSprite.StoneOutcrop or NatureSprite.IronOutcrop or NatureSprite.GoldOutcrop or
        NatureSprite.ClayBank;

    /// <summary>Draws an approved sprite into its atlas cell.</summary>
    private static void PaintApproved(Layers layers, NatureSprite sprite)
    {
        switch (sprite)
        {
            case NatureSprite.Broadleaf: Broadleaf(layers); layers.Compose(Canopy.Edge); break;
            // At 16 px the conifer draws its own edge star, so the outline does not fill the gaps between its points.
            case NatureSprite.Conifer: Conifer(layers); layers.Compose(Needle.Edge, outline: layers.Fine); break;
            case NatureSprite.BroadleafStump: Stump(layers); layers.Compose(Timber.Edge); break;
            case NatureSprite.BroadleafSapling: Sapling(layers); layers.Compose(Canopy.Edge); break;
            case NatureSprite.OrchardGrowing:
            case NatureSprite.OrchardFruiting:
            case NatureSprite.OrchardPicked:
                Orchard(layers, sprite);
                layers.Compose(OrchardCanopy.Edge);
                break;
            case NatureSprite.BerryBush: BerryBush(layers); layers.Compose(Bush.Edge); break;
            case NatureSprite.WildGreens: WildGreens(layers); layers.Compose(Canopy.Edge); break;
            case NatureSprite.FiberPlant: FiberPlant(layers); layers.Compose(Fiber.Edge, outline: false); break;
            case NatureSprite.StoneOutcrop: Outcrop(layers, Ore.None); layers.Compose(Rock.Edge); break;
            case NatureSprite.IronOutcrop: Outcrop(layers, Ore.Iron); layers.Compose(Iron.Edge); break;
            case NatureSprite.GoldOutcrop: Outcrop(layers, Ore.Gold); layers.Compose(Rock.Edge); break;
            case NatureSprite.ClayBank: ClayBank(layers); layers.Compose(Bank.Edge); break;
            default: throw new ArgumentOutOfRangeException(nameof(sprite), sprite, "Not an approved sprite.");
        }
    }

    /// <summary>
    /// The two layers of one approved sprite: the body, which gets the edge
    /// outline, and the ground (the atlas cell itself), which holds only the
    /// soft shadow and ground marks so the outline never wraps them.
    /// Coordinates are in 32-unit tile space at both sizes.
    /// </summary>
    private sealed class Layers
    {
        private readonly Image atlas;
        private readonly Rect2I cell;

        public Layers(Image atlas, Rect2I cell)
        {
            this.atlas = atlas;
            this.cell = cell;
            Size = cell.Size.X;
            Unit = Size / 32f;
            Body = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
            Body.Fill(Colors.Transparent);
            Canvas = new PixelCanvas(Body, new Rect2I(0, 0, Size, Size), Unit);
            GroundCanvas = new PixelCanvas(atlas, cell, Unit);
        }

        public Image Body { get; }
        public PixelCanvas Canvas { get; }
        public PixelCanvas GroundCanvas { get; }
        public float Unit { get; }
        public int Size { get; }

        /// <summary>True at 32 px, where single-pixel details are worth drawing.</summary>
        public bool Fine => Unit >= 1;

        /// <summary>One real pixel expressed in tile units: 1 at 32 px, 2 at 16 px.</summary>
        public float Pixel => 1 / Unit;

        /// <summary>The ground shadow: an ellipse already offset toward the south-east by the caller.</summary>
        public void Shadow(float centerX, float centerY, float radiusX, float radiusY) =>
            GroundCanvas.Ellipse(centerX, centerY, radiusX, radiusY, NatureSprites.Shadow);

        /// <summary>Finishes the sprite: outlines the body in <paramref name="edge"/> and lays it over the ground.</summary>
        public void Compose(Color edge, bool outline = true)
        {
            if (outline) Outline(edge);
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var over = Body.GetPixel(x, y);
                    if (over.A <= 0) continue;
                    var target = cell.Position + new Vector2I(x, y);
                    atlas.SetPixelv(target, atlas.GetPixelv(target).Blend(over));
                }
        }

        /// <summary>A one-pixel outline in a fixed edge colour around the body's silhouette (four neighbours).</summary>
        private void Outline(Color edge)
        {
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && Body.GetPixel(x, y).A > 0.5f;
            var outline = new List<Vector2I>();
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (Body.GetPixel(x, y).A <= 0 && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)))
                        outline.Add(new Vector2I(x, y));
            foreach (var point in outline) Body.SetPixelv(point, edge);
        }

        /// <summary>Blends one pixel of the body (pixel coordinates), clipped to the sprite.</summary>
        public void Blend(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            Body.SetPixel(x, y, Body.GetPixel(x, y).Blend(color));
        }

        /// <summary>
        /// A canopy or mound like <see cref="PixelCanvas.Lumpy"/>, with the lobe
        /// depth adjustable: 0.1 matches Lumpy, 0.2 gives a bushier silhouette.
        /// </summary>
        public void Lobed(float centerX, float centerY, float radius, Color color, int lobes, float phase, float depth)
        {
            var cx = centerX * Unit;
            var cy = centerY * Unit;
            var r = radius * Unit;
            for (var y = (int)(cy - r - 2); y <= (int)(cy + r + 2); y++)
                for (var x = (int)(cx - r - 2); x <= (int)(cx + r + 2); x++)
                {
                    var offset = new Vector2(x + 0.5f - cx, y + 0.5f - cy);
                    var edge = r * (1 - depth + depth * Mathf.Sin(offset.Angle() * lobes + phase));
                    if (offset.Length() <= edge) Blend(x, y, color);
                }
        }

        /// <summary>
        /// An ellipse painted only where the body already has pixels: drawn
        /// one pixel larger than a boulder just before the boulder itself, it
        /// leaves a crease line where the boulder overlaps the ones behind it.
        /// </summary>
        public void EllipseOverBody(float centerX, float centerY, float radiusX, float radiusY, Color color)
        {
            var cx = centerX * Unit;
            var cy = centerY * Unit;
            var rx = radiusX * Unit;
            var ry = radiusY * Unit;
            for (var y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                for (var x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size || Body.GetPixel(x, y).A <= 0) continue;
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Blend(x, y, color);
                }
        }

        /// <summary>Clears the body inside an ellipse (tile units), for a bite dug out of a mound; the outline then follows the cut.</summary>
        public void ClearEllipse(float centerX, float centerY, float radiusX, float radiusY)
        {
            var cx = centerX * Unit;
            var cy = centerY * Unit;
            var rx = radiusX * Unit;
            var ry = radiusY * Unit;
            for (var y = Math.Max(0, (int)(cy - ry - 1)); y <= Math.Min(Size - 1, (int)(cy + ry + 1)); y++)
                for (var x = Math.Max(0, (int)(cx - rx - 1)); x <= Math.Min(Size - 1, (int)(cx + rx + 1)); x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Body.SetPixel(x, y, Colors.Transparent);
                }
        }

        /// <summary>Fills a polygon given in tile units; a pixel is inside when its centre is (even-odd rule).</summary>
        public void Polygon(Color color, Vector2[] points)
        {
            var scaled = points.Select(point => point * Unit).ToArray();
            var top = (int)MathF.Floor(scaled.Min(point => point.Y));
            var bottom = (int)MathF.Ceiling(scaled.Max(point => point.Y));
            var left = (int)MathF.Floor(scaled.Min(point => point.X));
            var right = (int)MathF.Ceiling(scaled.Max(point => point.X));
            for (var y = top; y <= bottom; y++)
                for (var x = left; x <= right; x++)
                    if (Inside(scaled, x + 0.5f, y + 0.5f)) Blend(x, y, color);
        }

        /// <summary>Draws the polygon edges whose outward side faces the north-west light, for a lit bevel.</summary>
        public void LitEdges(Color color, Vector2[] points)
        {
            var area = 0f;
            for (var i = 0; i < points.Length; i++)
                area += points[i].Cross(points[(i + 1) % points.Length]);
            for (var i = 0; i < points.Length; i++)
            {
                var from = points[i];
                var to = points[(i + 1) % points.Length];
                var along = to - from;
                var outward = new Vector2(along.Y, -along.X) * MathF.Sign(area);
                if (outward.X + outward.Y < 0) Canvas.Line(from.X, from.Y, to.X, to.Y, color);
            }
        }

        private static bool Inside(Vector2[] polygon, float x, float y)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>
    /// A berry, clod or ore fleck that reads at both sizes: at 32 px a 2×2
    /// block with its north-west pixel in the highlight; at 16 px one pixel,
    /// or a two-pixel dash when <paramref name="dash16"/> is set.
    /// </summary>
    private static void Mark(Layers s, int x, int y, Color color, Color highlight, bool dash16 = false)
    {
        if (s.Fine)
        {
            s.Canvas.Disc(x, y, 1.3f, color);
            s.Canvas.Dot(x - 1, y - 1, highlight);
            return;
        }
        s.Canvas.Dot(x, y, color);
        if (dash16) s.Canvas.Dot(x + 2, y, color);
    }

    /// <summary>True when a direction (an angle in radians, y pointing south) faces the north-west light.</summary>
    private static bool FacesLight(float angle, float threshold = 0.2f) => Mathf.Cos(angle + Mathf.Pi * 0.75f) > threshold;

    /// <summary>
    /// An irregular pentagon around a centre, used for the flat top face of a
    /// boulder. The fixed radius factors keep every face slightly lopsided;
    /// <paramref name="turn"/> rotates it so no two faces match.
    /// </summary>
    private static Vector2[] Facet(float centerX, float centerY, float radiusX, float radiusY, float turn)
    {
        ReadOnlySpan<float> factors = [1f, 0.8f, 1.08f, 0.88f, 0.96f];
        var points = new Vector2[factors.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = turn + i * Mathf.Tau / points.Length;
            points[i] = new Vector2(centerX + Mathf.Cos(angle) * radiusX * factors[i], centerY + Mathf.Sin(angle) * radiusY * factors[i]);
        }
        return points;
    }

    /// <summary>
    /// Broadleaf: a lumpy canopy with a shade rim on the south-east, light
    /// and highlight pulled toward the north-west and a few leaf dimples.
    /// </summary>
    private static void Broadleaf(Layers s)
    {
        s.Shadow(17, 18, 12, 10);
        s.Lobed(16, 15, 11.5f, Canopy.Shade, 7, 1, 0.12f);
        s.Lobed(15.2f, 14.2f, 10.6f, Canopy.Base, 7, 1, 0.12f);
        s.Lobed(14, 13, 7.5f, Canopy.Light, 5, 2, 0.13f);
        // The highlight disc sits (−3, −3) from the canopy centre at 32 px; at 16 px one step further so it still reads north-west.
        if (s.Fine) s.Canvas.Disc(13, 12, 3, Canopy.Highlight);
        else s.Canvas.Disc(11.5f, 10.5f, 2.6f, Canopy.Highlight);
        if (!s.Fine) return;
        // Leaf dimples: single darker pixels where leaf clusters meet.
        foreach (var (x, y) in new[] { (21, 10), (24, 16), (11, 20), (18, 22), (22, 21) }) s.Canvas.Dot(x, y, Canopy.Shade);
        foreach (var (x, y) in new[] { (17, 9), (10, 16), (18, 16) }) s.Canvas.Dot(x, y, Canopy.Base);
    }

    /// <summary>Conifer: nine-point star layers, each inner layer stepped toward the light, with a small lit tip.</summary>
    private static void Conifer(Layers s)
    {
        s.Shadow(17, 18, 11, 9);
        var c = s.Canvas;
        if (!s.Fine) c.Star(16, 15, 12.5f, 7, Needle.Edge, 9);
        c.Star(16, 15, 11.5f, 6.5f, Needle.Shade, 9);
        c.Star(15.3f, 14.3f, 9, 5, Needle.Base, 9);
        c.Star(14.6f, 13.6f, 6, 3.2f, Needle.Light, 9);
        c.Rect(14, 13, 2, 2, Needle.Highlight);
        if (!s.Fine) return;
        // Lit needle tips on the north-west points of the base layer.
        foreach (var (x, y) in new[] { (9, 11), (12, 7), (17, 6) }) c.Dot(x, y, Needle.Light);
    }

    /// <summary>
    /// Broadleaf stump: four short rounded roots splaying low (lit when they
    /// face the light), a bark ring, and the round cut face with a lit
    /// north-west rim, one growth ring, the pith and a split running south-east.
    /// </summary>
    private static void Stump(Layers s)
    {
        s.Shadow(17, 18.5f, 8.5f, 6);
        var c = s.Canvas;
        // Root directions in radians clockwise from east: east-south-east, south-south-west, west, north-east.
        foreach (var angle in new[] { 0.5f, 1.95f, 3.25f, 5.35f })
        {
            var lit = FacesLight(angle, -0.3f);
            // At 16 px only the two roots on the shadow side stay, so the stump keeps a compact silhouette.
            if (!s.Fine && lit) continue;
            var dir = Vector2.FromAngle(angle);
            var color = lit ? Timber.Base : Timber.Shade;
            c.Leaf(16 + dir.X * 6, 16 + dir.Y * 6, 2.4f, 1.8f, angle, color, color);
        }
        c.Disc(16, 16, 6.2f, Timber.Shade);
        c.Disc(16, 16, 5, s.Fine ? Timber.Highlight : Timber.Light);
        c.Disc(16.5f, 16.5f, 4.6f, Timber.Light);
        c.Ring(16.2f, 16.2f, 2.8f, Timber.Base);
        c.Disc(16.2f, 16.2f, 0.9f, Timber.Shade);
        if (!s.Fine) return;
        // The split from the pith toward the south-east, and bark texture on the shaded side.
        c.Line(17, 17, 20, 20, Timber.Shade);
        foreach (var (x, y) in new[] { (21, 14), (19, 21), (13, 21) }) c.Dot(x, y, Timber.Edge);
    }

    /// <summary>Broadleaf sapling: a small young canopy in the lighter canopy steps, lit from the north-west.</summary>
    private static void Sapling(Layers s)
    {
        s.Shadow(17.5f, 19.5f, 7, 5);
        s.Lobed(16, 16, 6.4f, Canopy.Base, 5, 1, 0.18f);
        s.Lobed(15.4f, 15.4f, 5.6f, Canopy.Light, 5, 1, 0.18f);
        s.Lobed(14.6f, 14.6f, 3.2f, Canopy.Highlight, 4, 2, 0.18f);
        if (!s.Fine) return;
        // A centre vein on two of the young leaves.
        s.Canvas.Line(16, 16, 19, 18, Canopy.Base);
        s.Canvas.Line(16, 16, 13, 19, Canopy.Base);
    }

    /// <summary>
    /// Orchard tree: the broadleaf canopy in the orchard ramp, a smaller crown
    /// while growing, and five fruit, each lit north-west, while fruiting.
    /// </summary>
    private static void Orchard(Layers s, NatureSprite stage)
    {
        if (stage == NatureSprite.OrchardGrowing)
        {
            OrchardCrown(s, 16, 16, 8f, 6);
            return;
        }
        OrchardCrown(s, 16, 15, 11.5f, 7);
        if (stage == NatureSprite.OrchardFruiting)
            foreach (var (x, y) in new[] { (21, 11), (11, 18), (18, 21), (15, 7), (24, 17) })
                FruitOn(s, x, y);
    }

    /// <summary>
    /// A broadleaf canopy in the orchard ramp: the shadow offset (+1, +3), a
    /// shade body, the base pulled north-west so a shade rim stays on the
    /// south-east, a light lobe and a highlight disc offset (−3, −3), and a few
    /// leaf dimples at 32 px. Offsets scale with the radius so a young tree is lit alike.
    /// </summary>
    private static void OrchardCrown(Layers s, float cx, float cy, float radius, int lobes)
    {
        var k = radius / 11.5f;
        s.Shadow(cx + 1, cy + 3, radius + 0.5f, radius * 0.86f);
        s.Lobed(cx, cy, radius, OrchardCanopy.Shade, lobes, 1, 0.13f);
        s.Lobed(cx - 0.8f * k, cy - 0.8f * k, radius - 0.9f * k, OrchardCanopy.Base, lobes, 1, 0.13f);
        s.Lobed(cx - 2 * k, cy - 2 * k, 7.5f * k, OrchardCanopy.Light, 5, 2, 0.13f);
        s.Canvas.Disc(cx - 3 * k, cy - 3 * k, 3 * k, OrchardCanopy.Highlight);
        if (!s.Fine) return;
        // Leaf dimples: single darker pixels where leaf clusters meet, placed relative to the centre.
        foreach (var (dx, dy) in new[] { (5f, -5f), (8f, 1f), (-5f, 5f), (2f, 7f), (6f, 6f) })
            s.Canvas.Dot(cx + dx * k, cy + dy * k, OrchardCanopy.Shade);
        foreach (var (dx, dy) in new[] { (1f, -6f), (-6f, 1f), (2f, 1f) })
            s.Canvas.Dot(cx + dx * k, cy + dy * k, OrchardCanopy.Base);
    }

    /// <summary>
    /// One orchard fruit. At 32 px a five-pixel round disc in the fruit base,
    /// its north and west pixels in the light step with the highlight at the
    /// north, and a shade pixel at the south-east; at 16 px one base pixel.
    /// </summary>
    private static void FruitOn(Layers s, int x, int y)
    {
        if (!s.Fine)
        {
            s.Blend(x / 2, y / 2, Fruit.Base);
            return;
        }
        s.Blend(x, y - 1, Fruit.Highlight);
        s.Blend(x - 1, y, Fruit.Light);
        s.Blend(x, y, Fruit.Base);
        s.Blend(x + 1, y, Fruit.Base);
        s.Blend(x, y + 1, Fruit.Base);
        s.Blend(x + 1, y + 1, Fruit.Shade);
    }

    /// <summary>Berry bush: a dark lobed bush lit from the north-west, with berries that stay visible at 16 px.</summary>
    private static void BerryBush(Layers s)
    {
        s.Shadow(17, 20, 10, 7);
        s.Lobed(16, 17, 9.5f, Bush.Shade, 9, 4, 0.14f);
        s.Lobed(15.2f, 16.2f, 8.6f, Bush.Base, 9, 4, 0.14f);
        s.Lobed(14, 15, 5, Bush.Highlight, 6, 5, 0.14f);
        foreach (var (x, y) in new[] { (20, 14), (12, 19), (18, 21), (22, 18), (15, 12), (10, 15) })
            Mark(s, x, y, Berry.Base, Berry.Light, dash16: true);
    }

    /// <summary>Wild greens: a rosette of broad leaves, the ones toward the light in the light step, with pale veins.</summary>
    private static void WildGreens(Layers s)
    {
        s.Shadow(17.5f, 18.5f, 9.5f, 8);
        var c = s.Canvas;
        var leaves = s.Fine ? 7 : 5;
        for (var i = 0; i < leaves; i++)
        {
            var angle = i * Mathf.Tau / leaves + 0.45f;
            var lit = FacesLight(angle);
            var dir = Vector2.FromAngle(angle);
            c.Leaf(16 + dir.X * 4.8f, 16 + dir.Y * 4.8f, 5.2f, 3.1f, angle,
                lit ? Canopy.Light : Canopy.Base, lit ? GreensVein : Canopy.Highlight);
        }
        c.Disc(16, 16, 1.8f, s.Fine ? Canopy.Shade : GreensVein);
        if (!s.Fine) return;
        // Three young inner leaves over the heart of the rosette.
        for (var i = 0; i < 3; i++)
        {
            var angle = i * Mathf.Tau / 3 - 1.2f;
            var dir = Vector2.FromAngle(angle);
            c.Leaf(16 + dir.X * 2.4f, 16 + dir.Y * 2.4f, 2.4f, 1.3f, angle, Canopy.Highlight, GreensVein);
        }
    }

    /// <summary>
    /// Fibre plant: a dark clump with eleven tapered blades of uneven length,
    /// the blades toward the light in the light step with a lit ridge. Each
    /// blade carries a copy of itself in the edge step one pixel south-east
    /// instead of a full outline, so the blades stay slender and the light
    /// stays north-west.
    /// </summary>
    private static void FiberPlant(Layers s)
    {
        s.Shadow(17, 19, 9, 6);
        var c = s.Canvas;
        // Blade angles (radians, clockwise from east) and lengths, irregular on purpose so the clump does not look like a star.
        ReadOnlySpan<(float Angle, float Length)> blades =
        [
            (0.1f, 9.5f), (0.75f, 8.5f), (1.35f, 10), (1.95f, 8), (2.5f, 9.5f), (3.05f, 9),
            (3.65f, 10.5f), (4.15f, 9), (4.7f, 10.5f), (5.25f, 9), (5.8f, 10),
        ];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < blades.Length; i++)
            {
                // At 16 px every third blade is left out so the rest stay apart.
                if (!s.Fine && i % 3 == 1) continue;
                var (angle, length) = blades[i];
                var half = length / 2;
                var dir = Vector2.FromAngle(angle);
                // The blade is a narrow leaf whose inner tip sits inside the clump.
                var cx = 16 + dir.X * (half + 1);
                var cy = 16 + dir.Y * (half + 1);
                var offset = pass == 0 ? s.Pixel : 0;
                var lit = FacesLight(angle, 0.1f);
                var color = pass == 0 ? Fiber.Edge : lit ? Fiber.Light : Fiber.Base;
                var ridge = pass == 0 ? Fiber.Edge : lit ? Fiber.Highlight : Fiber.Light;
                c.Leaf(cx + offset, cy + offset, half, 1.1f, angle, color, ridge);
            }
            if (pass == 0) c.Disc(16 + s.Pixel, 16.5f + s.Pixel, 3.6f, Fiber.Edge);
        }
        c.Disc(16, 16.5f, 3.4f, Fiber.Shade);
        c.Disc(15, 15.5f, 1.6f, Fiber.Base);
    }

    /// <summary>
    /// A boulder seen from above: a crease where it overlaps the boulders
    /// behind it, a shaded south-east side, a rounded body and a flat top
    /// face pulled toward the light with a lit bevel on its north-west edges.
    /// <paramref name="faceTint"/> stains the face (rust on iron rock).
    /// </summary>
    private static void Boulder(Layers s, float cx, float cy, float rx, float ry, Ramp stone, float turn, Color? faceTint = null)
    {
        var c = s.Canvas;
        s.EllipseOverBody(cx, cy, rx + s.Pixel, ry + s.Pixel, stone.Edge);
        c.Ellipse(cx, cy, rx, ry, stone.Shade);
        c.Ellipse(cx - 0.8f, cy - 0.9f, rx - 1.2f, ry - 1.2f, stone.Base);
        var face = Facet(cx - rx * 0.22f, cy - ry * 0.26f, rx * 0.6f, ry * 0.58f, turn);
        var light = faceTint is { } tint ? stone.Light.Lerp(tint, 0.3f) : stone.Light;
        s.Polygon(light, face);
        if (s.Fine) s.LitEdges(stone.Highlight, face);
        else c.Dot(cx - rx * 0.5f, cy - ry * 0.55f, stone.Highlight);
    }

    /// <summary>Stone, iron or gold outcrop: three boulders with loose stones; ore shows as rust veins or gold flecks.</summary>
    private static void Outcrop(Layers s, Ore ore)
    {
        s.Shadow(17, 20, 11, 7);
        var c = s.Canvas;
        // Plain stone is the lightest, gold-bearing rock mid grey, iron rock darkest, so the three differ in value as well as flecks.
        var stone = ore switch { Ore.Iron => Iron, Ore.Gold => Rock, _ => Boulders };
        Color? tint = ore == Ore.Iron ? Rust.Shade : null;
        // Back to front, so each nearer boulder creases over the one behind it.
        Boulder(s, 20, 14, 7.5f, 7, stone, 0.3f, tint);
        Boulder(s, 11, 18, 6.5f, 6, stone, 1.4f, tint);
        Boulder(s, 19, 22.5f, 4.6f, 4, stone, 2.2f);
        if (s.Fine)
        {
            // Two loose stones, clear of the mass so each gets its own outline, and a crack on the big boulder.
            c.Disc(27, 24.5f, 1.4f, stone.Base);
            c.Dot(26, 23, stone.Light);
            c.Disc(5, 26, 1.2f, stone.Base);
            c.Dot(4, 25, stone.Light);
            c.Line(23, 10, 25, 13, stone.Shade);
        }
        switch (ore)
        {
            case Ore.Iron:
                if (s.Fine)
                {
                    // A two-pixel rust vein across the big boulder and a short one on the west boulder.
                    c.Line(16, 12, 21, 17, Rust.Shade);
                    c.Line(17, 12, 22, 17, Rust.Base);
                    c.Line(8, 19, 11, 21.5f, Rust.Base);
                }
                Mark(s, 10, 16, Rust.Base, Rust.Highlight, dash16: true);
                Mark(s, 21, 11, Rust.Base, Rust.Highlight, dash16: true);
                Mark(s, 19, 22, Rust.Light, Rust.Highlight, dash16: true);
                break;
            case Ore.Gold:
                foreach (var (x, y) in new[] { (19, 12), (23, 16), (10, 17), (18, 22) })
                    Mark(s, x, y, Gold.Light, Gold.Highlight, dash16: true);
                if (s.Fine) foreach (var (x, y) in new[] { (14, 20), (22, 11), (8, 20) }) c.Dot(x, y, Gold.Base);
                break;
        }
    }

    /// <summary>
    /// Clay bank: a lobed bank in weathered browns lit on its north-west
    /// crest, with a bite dug out of its south-east side: the outline follows
    /// the cut, and the level floor shows fresh clay with the bank's shadow
    /// across it, spade scrapes and two dug clods.
    /// </summary>
    private static void ClayBank(Layers s)
    {
        s.Shadow(17, 20, 11, 7);
        var c = s.Canvas;
        s.Lobed(16, 15.5f, 10.5f, Bank.Shade, 5, 6, 0.1f);
        s.Lobed(15.3f, 14.8f, 9.6f, Bank.Base, 5, 6, 0.1f);
        s.Lobed(13, 12.5f, 5.5f, Bank.Light, 4, 2, 0.12f);
        if (s.Fine) c.Disc(11.5f, 11, 1.6f, Bank.Highlight);
        // The bite dug out of the south-east: the bank is cut away there, so
        // its outline follows the cut as a crest line. The floor lies on the
        // ground layer (level ground, no outline) in fresh clay, with the
        // bank's own shadow cast across its north-west side.
        s.ClearEllipse(20.5f, 20.5f, 5.6f, 4.3f);
        var g = s.GroundCanvas;
        g.Ellipse(20.7f, 20.7f, 5.8f, 4.5f, Clay.Edge);
        g.Ellipse(21.3f, 21.2f, 5.3f, 4, Clay.Shade);
        if (s.Fine)
        {
            // Two parallel spade scrapes of fresher clay on the floor, and grass tufts on the weathered crest.
            g.Line(20, 20, 21, 22.5f, Clay.Base);
            g.Line(23, 19.5f, 24, 22, Clay.Base);
            c.Line(8, 15, 9, 13, GrassTuft);
            c.Line(10, 15, 9, 13, GrassTuft);
            c.Line(17, 8, 18, 6.5f, GrassTuft);
        }
        // Dug-out clods of fresh clay: one thrown up on the bank, one left on the floor.
        Mark(s, 12, 19, Clay.Base, Clay.Light);
        Mark(s, 26, 23, Clay.Base, Clay.Light);
    }
}
