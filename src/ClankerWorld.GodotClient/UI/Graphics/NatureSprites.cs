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
    Cactus,
    CactusTall,
    CactusPad,
}

/// <summary>
/// Top-down pixel-art sprites for trees and natural sites, generated
/// deterministically at 32×32 (and 16×16 for mid zoom) on a transparent
/// background. Each sprite stays inside its tile and keeps a soft shadow to
/// the south-east. The sprites the owner approved in the first and second art
/// reviews (see <see cref="IsApproved"/>) are lit from the north-west and carry
/// a one-pixel outline in their darkest colour (the fibre plant's blades and
/// the reeds take a dark south-east edge instead); the others keep their
/// earlier provisional drawing with a darker disc behind the shape. The desert
/// cacti from the third review are drawn by <see cref="CactusSprites"/>.
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
            12 => NatureSprite.WoodPile,
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
            if (CactusSprites.Draws(sprite)) CactusSprites.Paint(image, cell, sprite);
            else if (IsApproved(sprite)) PaintApproved(new Layers(image, cell), sprite);
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
        }
    }

    // ----------------------------------------------------------------------
    // Approved art (first and second art reviews, 2026-10-01).
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
    private static readonly Ramp Diamond = Ramp.Of("3F7E86", "5FB4BE", "7FD3DC", "B4EEF2", "E8FFFF");

    /// <summary>Host rock of the diamond outcrop: the style guide's Slate ramp, cool blue-grey to set off the pale crystals.</summary>
    private static readonly Ramp Slate = Ramp.Of("2B2E33", "4A4E55", "62666E", "80858E", "9A9FA7");

    /// <summary>The Fertile soil ramp, for loosened earth under a regrowing sprout and a bare depleted patch.</summary>
    private static readonly Ramp Soil = Ramp.Of("4A3A2A", "5C4B35", "735F45", "86704F", "9A8460");

    /// <summary>
    /// Reed stems and blades: the earlier reeds' two olive greens as the base
    /// and light steps, with a darker shade, an olive edge and one pale
    /// highlight added around them, the way the fibre plant ramp was built.
    /// </summary>
    private static readonly Ramp Reed = Ramp.Of("3D4A2B", "57653A", "6E7F46", "8A9A55", "A6B36C");

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

    private enum Ore { None, Iron, Gold, Diamond }

    /// <summary>The sprites the owner approved in the art reviews; the rest keep the earlier drawing.</summary>
    private static bool IsApproved(NatureSprite sprite) => sprite is NatureSprite.Broadleaf or NatureSprite.Conifer or
        NatureSprite.BroadleafStump or NatureSprite.BroadleafSapling or NatureSprite.OrchardGrowing or
        NatureSprite.OrchardFruiting or NatureSprite.OrchardPicked or NatureSprite.BerryBush or NatureSprite.WildGreens or
        NatureSprite.FiberPlant or NatureSprite.StoneOutcrop or NatureSprite.IronOutcrop or NatureSprite.GoldOutcrop or
        NatureSprite.ClayBank or NatureSprite.ConiferStump or NatureSprite.ConiferSapling or NatureSprite.Reeds or
        NatureSprite.DiamondOutcrop or NatureSprite.Regrowing or NatureSprite.Depleted;

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
            case NatureSprite.ConiferStump: ConiferStump(layers); layers.Compose(Timber.Edge); break;
            // Like the conifer, the 16 px sapling draws its own edge star so its points stay apart.
            case NatureSprite.ConiferSapling: ConiferSapling(layers); layers.Compose(Needle.Edge, outline: layers.Fine); break;
            // Reed blades carry their own south-east edge copy, like the fibre plant, so they stay slender.
            case NatureSprite.Reeds: Reeds(layers); layers.Compose(Reed.Edge, outline: false); break;
            case NatureSprite.DiamondOutcrop: Outcrop(layers, Ore.Diamond); layers.Compose(Slate.Edge); break;
            case NatureSprite.Regrowing: Regrowing(layers); layers.Compose(Canopy.Edge); break;
            case NatureSprite.Depleted: BareGround(layers); layers.Compose(Soil.Edge); break;
            default: throw new ArgumentOutOfRangeException(nameof(sprite), sprite, "Not an approved sprite.");
        }
    }

    /// <summary>
    /// The two layers of one approved sprite: the body, which gets the edge
    /// outline, and the ground (the atlas cell itself), which holds only the
    /// soft shadow and ground marks so the outline never wraps them.
    /// Coordinates are in 32-unit tile space at both sizes. Every blended
    /// pixel is snapped to an 8-bit step (<see cref="PixelArt.Snap"/>), so
    /// shadows and part-transparent ground marks match the reviewed pictures.
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
            Canvas = new PixelCanvas(Body, new Rect2I(0, 0, Size, Size), Unit, snap: true);
            GroundCanvas = new PixelCanvas(atlas, cell, Unit, snap: true);
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
                    atlas.SetPixelv(target, PixelArt.Snap(atlas.GetPixelv(target).Blend(over)));
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
            Body.SetPixel(x, y, PixelArt.Snap(Body.GetPixel(x, y).Blend(color)));
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

    /// <summary>
    /// Stone, iron, gold or diamond outcrop: three boulders with loose stones;
    /// ore shows as rust veins, gold flecks or pale faceted crystals.
    /// </summary>
    private static void Outcrop(Layers s, Ore ore)
    {
        s.Shadow(17, 20, 11, 7);
        var c = s.Canvas;
        // Plain stone is the lightest, gold-bearing rock mid grey, iron rock darkest, so the three differ in value as well as flecks.
        // Diamond-bearing rock is the cool blue-grey slate.
        var stone = ore switch { Ore.Iron => Iron, Ore.Gold => Rock, Ore.Diamond => Slate, _ => Boulders };
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
            case Ore.Diamond:
                if (s.Fine)
                {
                    // A cluster of two crystals on the big boulder, one on each of the others.
                    Gem(s, 20, 13, large: true);
                    Gem(s, 23, 16, large: false);
                    Gem(s, 10, 18, large: true);
                    Gem(s, 19, 22, large: false);
                }
                else
                    foreach (var (x, y) in new[] { (19, 12), (10, 17), (18, 22) })
                        Mark(s, x, y, Diamond.Light, Diamond.Highlight, dash16: true);
                break;
        }
    }

    /// <summary>
    /// One crystal at 32 px, seen from above: a small rhombus whose north-west
    /// facets are Diamond highlight and light and whose south-east facets are
    /// Diamond base and shade, with a Diamond-edge pixel or two where it meets
    /// the rock on its shaded side. The large one is five pixels across, the
    /// small one three. Pixel coordinates.
    /// </summary>
    private static void Gem(Layers s, int x, int y, bool large)
    {
        var c = s.Canvas;
        if (large)
        {
            // Rows from north to south: H highlight, L light, B base, S shade, E edge, '.' rock.
            ReadOnlySpan<string> rows = ["..H..", ".HLB.", "HLLBS", ".BBSE", "..SE."];
            for (var row = 0; row < rows.Length; row++)
                for (var col = 0; col < rows[row].Length; col++)
                {
                    Color? color = rows[row][col] switch
                    {
                        'H' => Diamond.Highlight,
                        'L' => Diamond.Light,
                        'B' => Diamond.Base,
                        'S' => Diamond.Shade,
                        'E' => Diamond.Edge,
                        _ => null,
                    };
                    if (color is { } pixel) c.Dot(x - 2 + col, y - 2 + row, pixel);
                }
            return;
        }
        c.Dot(x, y - 1, Diamond.Highlight);
        c.Dot(x - 1, y, Diamond.Highlight);
        c.Dot(x, y, Diamond.Light);
        c.Dot(x + 1, y, Diamond.Shade);
        c.Dot(x, y + 1, Diamond.Shade);
        c.Dot(x + 1, y + 1, Diamond.Edge);
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

    /// <summary>
    /// Conifer stump: told apart from the broadleaf stump by a darker cut face
    /// (Timber base, as the earlier conifer stump was darker) with two close rings,
    /// a scaly bark ring with dark plate notches, five slender roots spread
    /// evenly, and a bead of amber resin on the rim as its colour accent.
    /// </summary>
    private static void ConiferStump(Layers s)
    {
        s.Shadow(17, 18.5f, 8.5f, 6);
        var c = s.Canvas;
        // Root directions in radians clockwise from east, spread evenly round the trunk.
        foreach (var angle in new[] { 0.3f, 1.5f, 2.65f, 3.85f, 5.1f })
        {
            var lit = FacesLight(angle, -0.3f);
            // At 16 px only the roots on the shadow side stay, as on the broadleaf stump.
            if (!s.Fine && lit) continue;
            var dir = Vector2.FromAngle(angle);
            var color = lit ? Timber.Base : Timber.Shade;
            c.Leaf(16 + dir.X * 5.8f, 16 + dir.Y * 5.8f, 2.3f, 1.5f, angle, color, color);
        }
        c.Disc(16, 16, 6.2f, Timber.Shade);
        c.Disc(16, 16, 5, Timber.Light);
        c.Disc(16.5f, 16.5f, 4.6f, Timber.Base);
        if (s.Fine)
        {
            // Bark plates: dark notches round the bark ring, on every side but the lit north-west.
            foreach (var (x, y) in new[] { (22, 13), (21, 20), (17, 22), (12, 21), (10, 18), (22, 17) }) c.Dot(x, y, Timber.Edge);
            // Two close growth rings and the pith.
            c.Ring(16.4f, 16.4f, 3.4f, Timber.Shade);
            c.Ring(16.4f, 16.4f, 1.8f, Timber.Shade);
            c.Dot(16, 16, Timber.Edge);
            // The resin bead on the north-east rim: gold base with a bright top pixel.
            c.Dot(19, 12, Gold.Base);
            c.Dot(20, 12, Gold.Shade);
            c.Dot(19, 11, Gold.Highlight);
            return;
        }
        c.Dot(16, 16, Timber.Shade);
        c.Dot(19, 12, Gold.Base);
    }

    /// <summary>
    /// Conifer sapling: a small seven-point star in the lighter needle steps,
    /// each inner layer stepped toward the north-west light like the grown
    /// conifer. At 16 px it draws its own edge star so the points stay apart.
    /// </summary>
    private static void ConiferSapling(Layers s)
    {
        s.Shadow(17.5f, 19.5f, 6.5f, 4.6f);
        var c = s.Canvas;
        if (!s.Fine) c.Star(16, 16, 8.4f, 4.6f, Needle.Edge, 7);
        c.Star(16, 16, 7.2f, 3.8f, Needle.Base, 7);
        c.Star(15.4f, 15.4f, 5.2f, 2.8f, Needle.Light, 7);
        c.Star(14.9f, 14.9f, 2.6f, 1.4f, Needle.Highlight, 7);
        if (!s.Fine) return;
        // Shade on the south-east points, where they turn away from the light.
        foreach (var (x, y) in new[] { (20, 19), (17, 21), (21, 16) }) c.Dot(x, y, Needle.Shade);
    }

    /// <summary>
    /// One reed stem or blade: a one-pixel line from <paramref name="from"/>
    /// along <paramref name="angle"/> that bends by <paramref name="bend"/>
    /// radians over its length. Drawn in two passes like the fibre plant:
    /// pass 0 lays an edge-step copy one pixel south-east, pass 1 the stem,
    /// lit when it leans toward the light. Tile units.
    /// </summary>
    private static void ReedStem(Layers s, int pass, Vector2 from, float angle, float length, float bend = 0)
    {
        var offset = pass == 0 ? s.Pixel : 0;
        var color = pass == 0 ? Reed.Edge : FacesLight(angle, 0.3f) ? Reed.Light : Reed.Base;
        const int Segments = 3;
        var point = from;
        for (var i = 0; i < Segments; i++)
        {
            var next = point + Vector2.FromAngle(angle + bend * (i + 0.5f) / Segments) * (length / Segments);
            s.Canvas.Line(point.X + offset, point.Y + offset, next.X + offset, next.Y + offset, color);
            point = next;
        }
    }

    /// <summary>
    /// Reeds: three clumps of standing stems, kept from the earlier sprite
    /// because the brown cattail head is what tells reeds from the fibre
    /// plant. Each clump is a small olive tuft (edge rim, shade, lit
    /// north-west) with stems fanning up and out of it, two low leaves
    /// splaying sideways, and cattail heads on the tallest stems: a dark
    /// Timber-shade spike along the stem with a Timber-edge copy to the
    /// south-east, a lit north-west pixel and a pale stalk tip beyond. Stems
    /// leaning toward the light are lighter. At 16 px each head is a brown
    /// pixel over an edge pixel.
    /// </summary>
    private static void Reeds(Layers s)
    {
        var c = s.Canvas;
        // Clump bases and their stems: angle (radians clockwise from east, -1.57 is north), length, and whether a head tops it.
        var clumps = new (Vector2 Base, (float Angle, float Length, bool Head)[] Stems)[]
        {
            (new Vector2(10.5f, 21), [(-2.05f, 10, true), (-1.62f, 12.5f, true), (-1.25f, 9, false)]),
            (new Vector2(17, 19.5f), [(-1.8f, 12, true), (-1.4f, 10, false), (-2.3f, 8, false), (-1.0f, 11, true)]),
            (new Vector2(22.5f, 22.5f), [(-1.55f, 10.5f, true), (-1.15f, 8.5f, false), (-0.75f, 9, false)]),
        };
        s.Shadow(17.5f, 23, 9.5f, 4);
        for (var pass = 0; pass < 2; pass++)
            foreach (var (root, stems) in clumps)
            {
                // Two low leaves splaying west and east, drooping a little.
                ReedStem(s, pass, root, Mathf.Pi + 0.35f, 5.5f, -0.5f);
                ReedStem(s, pass, root, -0.35f, 5, 0.5f);
                for (var i = 0; i < stems.Length; i++)
                {
                    var (angle, length, _) = stems[i];
                    // At 16 px only the first two stems of each clump stay, so the rest do not merge.
                    if (!s.Fine && i >= 2) continue;
                    ReedStem(s, pass, root, angle, length);
                }
            }
        // The tuft at the foot of each clump.
        foreach (var (root, _) in clumps)
        {
            s.Lobed(root.X, root.Y, 2.4f + s.Pixel, Reed.Edge, 5, 1, 0.15f);
            s.Lobed(root.X, root.Y, 2.4f, Reed.Shade, 5, 1, 0.15f);
            if (s.Fine) c.Dot(root.X - 1, root.Y - 1, Reed.Light);
        }
        foreach (var (root, stems) in clumps)
            foreach (var (angle, length, head) in stems)
            {
                if (!head) continue;
                var dir = Vector2.FromAngle(angle);
                var at = root + dir * (length - 2.6f);
                if (!s.Fine)
                {
                    c.Dot(at.X, at.Y, Timber.Shade);
                    c.Dot(at.X + 2, at.Y + 2, Timber.Edge);
                    continue;
                }
                c.Dot(root.X + dir.X * (length + 0.6f), root.Y + dir.Y * (length + 0.6f), Reed.Highlight);
                c.Leaf(at.X + 1, at.Y + 1, 2.2f, 1.15f, angle, Timber.Edge, Timber.Edge);
                c.Leaf(at.X, at.Y, 2.2f, 1.15f, angle, Timber.Shade, Timber.Shade);
                c.Dot(at.X - 1 - dir.X, at.Y - dir.Y, Timber.Base);
            }
    }

    /// <summary>
    /// Regrowing: a generic sprout for any regrowing site. A patch of
    /// loosened earth on the ground layer holds a fresh sprout seen from
    /// above: two seed leaves spreading almost flat to the west and east (the
    /// west one lit) and a young leaf rising to the north-east, outlined in
    /// the canopy edge, with two tiny shoots coming up beside it at 32 px.
    /// </summary>
    private static void Regrowing(Layers s)
    {
        var g = s.GroundCanvas;
        g.Lumpy(16.4f, 17.6f, 7, Soil.Shade with { A = 0.5f }, 5, 1);
        g.Ellipse(16, 17.2f, 5.8f, 4, Soil.Base with { A = 0.75f });
        if (s.Fine) foreach (var (x, y) in new[] { (11, 18), (20, 20), (13, 20), (21, 15) }) g.Dot(x, y, Soil.Light);
        s.Shadow(17, 18.6f, 4.6f, 2.6f);
        var c = s.Canvas;
        // Seed leaves spreading almost flat to the west and east (the west one lit), and a young leaf rising to the north-east.
        c.Leaf(12.9f, 16.6f, 3, 2.1f, Mathf.Pi + 0.15f, Canopy.Light, Canopy.Highlight);
        c.Leaf(19.1f, 16.6f, 3, 2.1f, -0.15f, Canopy.Base, Canopy.Light);
        c.Leaf(16.8f, 14, 2.2f, 1.4f, -1.25f, Canopy.Highlight, Canopy.Highlight);
        c.Disc(16, 16.8f, 1.1f, Canopy.Shade);
        if (!s.Fine) return;
        // Two tiny shoots coming up beside it.
        c.Dot(11, 21, Canopy.Light);
        c.Dot(22, 19, Canopy.Light);
    }

    /// <summary>
    /// Depleted (generic fallback): for a used-up site with no depleted art of
    /// its own, a bare scuffed patch of dry earth on the ground layer (soft
    /// soil-shade rim, soil-light floor, a few crumbs and dents), with two small
    /// pebbles and a broken twig left on it. Neutral and low, so it reads as
    /// "nothing left here" for any plant.
    /// </summary>
    private static void BareGround(Layers s)
    {
        var g = s.GroundCanvas;
        g.Lumpy(16.5f, 17.5f, 8.5f, Soil.Shade with { A = 0.4f }, 5, 3);
        g.Ellipse(16, 17, 7.2f, 5f, Soil.Light with { A = 0.6f });
        if (s.Fine)
        {
            foreach (var (x, y) in new[] { (12, 15), (19, 14), (14, 20), (21, 19) }) g.Dot(x, y, Soil.Highlight);
            foreach (var (x, y) in new[] { (13, 16), (20, 15), (17, 19) }) g.Dot(x, y, Soil.Edge with { A = 0.6f });
        }
        s.Shadow(17, 19, 6, 3.5f);
        var c = s.Canvas;
        // Two pebbles.
        c.Disc(11.5f, 18, 1.5f, Rock.Base);
        c.Dot(11, 17, Rock.Highlight);
        c.Disc(21, 20.5f, 1.2f, Rock.Base);
        if (s.Fine) c.Dot(20, 20, Rock.Light);
        // A broken twig lying north-west to south-east, lit on its north end.
        if (!s.Fine) return;
        c.Line(15, 13, 20, 16, Timber.Light);
        c.Dot(15, 13, Timber.Highlight);
        c.Line(18, 15, 19, 13, Timber.Base);
    }
}
