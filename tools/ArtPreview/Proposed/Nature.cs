using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Nature;

/// <summary>
/// Round-1 nature mockups: trees and natural sites redrawn gently over the
/// current <see cref="NatureSprites"/> look. Every sprite keeps its silhouette
/// and hue family and gains a north-west light (STYLE L1), a one-pixel outline
/// in its ramp's edge step (L4), a south-east ground shadow (L2) and marks
/// that still read at 16 px (S1, S4). Picked and depleted states keep the
/// site's own colours (N4). Drawing is deterministic and uses only
/// <see cref="PixelCanvas"/> plus the small helpers in this class.
/// </summary>
public sealed class NatureProposal : IArtProposal, IArtSetProvider
{
    public string Family => "nature";
    public string Name => "nature";

    /// <summary>A five-step colour ramp from the style guide: edge (outline), shade, base, light, highlight.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));
    }

    // Ramps from STYLE.md section 2. The bush uses the forest-grass ramp so it
    // stays darker than a tree canopy, as it is today. The fibre plant keeps
    // its current yellow-greens (6E8A4B, 8FA863, 556E3C) arranged as a ramp
    // with the grass edge for its dark side.
    private static readonly Ramp Canopy = Ramp.Of("2E4A2A", "476B36", "557D3E", "6C9A4B", "8DB660");
    private static readonly Ramp Needle = Ramp.Of("1F3E31", "2F5B45", "3F7358", "5E9278", "86B89A");
    private static readonly Ramp Bush = Ramp.Of("2E4D35", "395F41", "426D4B", "4E7D56", "5E8E64");
    private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
    private static readonly Ramp Rock = Ramp.Of("4A4542", "625B56", "756D68", "8B837D", "A49C95");
    private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
    private static readonly Ramp Rust = Ramp.Of("7A4426", "A9643C", "B7774C", "C98A5A", "E0A070");
    private static readonly Ramp Gold = Ramp.Of("8A6A1E", "B8902E", "D9AE3C", "F2CC5E", "FFE28A");
    private static readonly Ramp Clay = Ramp.Of("6A3020", "8E4428", "B8623C", "D5825A", "EFA882");
    private static readonly Ramp Berry = Ramp.Of("7A2A2E", "A33A3F", "C4474B", "F08A8A", "FFC2C2");
    private static readonly Ramp Cloth = Ramp.Of("75674D", "A09170", "CABC99", "E8DCC0", "FFF5DF");
    private static readonly Ramp Fiber = Ramp.Of("3B5E3A", "556E3C", "6E8A4B", "8FA863", "A8BE78");

    /// <summary>
    /// The weathered top of a clay bank: each step is halfway between the Clay
    /// and Fertile soil ramps, which lands on today's clay-bank brown (946A4E)
    /// so the site keeps its hue while the freshly dug cut shows the Clay ramp.
    /// </summary>
    private static readonly Ramp Bank = Ramp.Of("5A3525", "75472E", "966040", "AD7954", "C49671");

    /// <summary>
    /// Boulders of a plain stone outcrop: the Rock ramp one step lighter, as
    /// today's outcrop is lighter than rock ground, with one added highlight
    /// step (B9B2AB) above Rock highlight for the lit bevel.
    /// </summary>
    private static readonly Ramp Boulders = Ramp.Of("4A4542", "756D68", "8B837D", "A49C95", "B9B2AB");

    /// <summary>The pale leaf vein of today's wild greens (B6CF8A), kept as the greens' accent.</summary>
    private static readonly Color GreensVein = new("B6CF8A");

    /// <summary>A grass-light tuft colour (Grass light, 6FA069) for grass growing on a weathered bank.</summary>
    private static readonly Color GrassTuft = new("6FA069");

    /// <summary>The L2 ground shadow colour.</summary>
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    /// <summary>Every round-1 asset: its Id, the current sprite it replaces (none for a new state) and the owner's one-line note.</summary>
    private static readonly (string Id, NatureSprite? Replaces, string Note)[] Assets =
    [
        ("Broadleaf", NatureSprite.Broadleaf, "Same lumpy canopy; shade rim on the south-east, highlight pulled north-west, edge outline, leaf dimples."),
        ("Conifer", NatureSprite.Conifer, "Same star layers, inner layers stepped toward the light, edge outline; points kept at 16 px."),
        ("BroadleafStump", NatureSprite.BroadleafStump, "Round cut face with a ring, lit rim and split, bark ring and four short roots (two at 16 px)."),
        ("BroadleafSapling", NatureSprite.BroadleafSapling, "A small young canopy in the lighter canopy steps, lit north-west, outlined."),
        ("BerryBush", NatureSprite.BerryBush, "Darker bush in the forest-grass ramp, lit side, berries as 2x2 with a highlight and 2 px dashes at 16 px."),
        ("BerryBushPicked", null, "Same bush without its berries; small bare stalks where they were."),
        ("WildGreens", NatureSprite.WildGreens, "Rosette of broad leaves with pale veins, lit leaves toward the light, outlined."),
        ("FiberPlant", NatureSprite.FiberPlant, "A dark clump with tapered blades of uneven length, each with a south-east edge line instead of a heavy outline."),
        ("StoneOutcrop", NatureSprite.StoneOutcrop, "Three boulders with flat lit top faces, creases between them and two loose stones."),
        ("StoneOutcropDepleted", null, "The quarried site: a flat grey gravel patch with a dug hollow and a few chips, in the outcrop's stone colours."),
        ("IronOutcrop", NatureSprite.IronOutcrop, "Darker iron-grey boulders with rust-stained faces, rust veins and blocks that stay at 16 px."),
        ("GoldOutcrop", NatureSprite.GoldOutcrop, "Rock boulders with gold flecks in gold light and highlight; two-pixel dashes at 16 px."),
        ("ClayBank", NatureSprite.ClayBank, "Brown bank lit on its crest with a bite dug out of the south-east: crest line, fresh clay floor, spade scrapes, clods."),
        ("HerbPatch", null, "New: a low clump of small-leaved sprigs with small pale flowers that have a gold centre."),
    ];

    /// <summary>Which current sprites this proposal replaces in the scene.</summary>
    private static readonly Dictionary<NatureSprite, string> Replaced = Assets
        .Where(asset => asset.Replaces is not null)
        .ToDictionary(asset => asset.Replaces!.Value, asset => asset.Id);

    public IEnumerable<Entry> Render()
    {
        var grass32 = TerrainTextures.Tile(TerrainStyle.Grass, 0, 32);
        var grass16 = TerrainTextures.Tile(TerrainStyle.Grass, 0, 16);
        foreach (var (id, _, note) in Assets)
            yield return new Entry(Family, id, Bitmap.Over(grass32, Paint(id, 32), 0, 0), note);
        foreach (var (id, _, note) in Assets)
            yield return new Entry(Family, id + ".sprite", Paint(id, 32), note);
        foreach (var (id, _, note) in Assets)
            yield return new Entry(Family, id + ".16", Bitmap.Over(grass16, Paint(id, 16), 0, 0), note);
    }

    public void Apply(ArtSet set)
    {
        var cache = new Dictionary<(NatureSprite, int), Image>();
        set.Nature = (sprite, size) =>
        {
            if (!Replaced.TryGetValue(sprite, out var id)) return NatureSprites.Sprite(sprite, size);
            if (!cache.TryGetValue((sprite, size), out var image))
                cache[(sprite, size)] = image = Paint(id, size);
            return image;
        };
    }

    /// <summary>Draws one asset at 32 or 16 px on a transparent square.</summary>
    private static Image Paint(string id, int size)
    {
        var layers = new Layers(size);
        switch (id)
        {
            case "Broadleaf": Broadleaf(layers); return layers.Compose(Canopy.Edge);
            // At 16 px the conifer draws its own edge star, like today, so the outline does not fill the gaps between its points.
            case "Conifer": Conifer(layers); return layers.Compose(Needle.Edge, outline: layers.Fine);
            case "BroadleafStump": Stump(layers); return layers.Compose(Timber.Edge);
            case "BroadleafSapling": Sapling(layers); return layers.Compose(Canopy.Edge);
            case "BerryBush": BerryBush(layers, true); return layers.Compose(Bush.Edge);
            case "BerryBushPicked": BerryBush(layers, false); return layers.Compose(Bush.Edge);
            case "WildGreens": WildGreens(layers); return layers.Compose(Canopy.Edge);
            case "FiberPlant": FiberPlant(layers); return layers.Compose(Fiber.Edge, outline: false);
            case "StoneOutcrop": Outcrop(layers, Ore.None); return layers.Compose(Rock.Edge);
            case "StoneOutcropDepleted": Rubble(layers); return layers.Compose(Rock.Edge);
            case "IronOutcrop": Outcrop(layers, Ore.Iron); return layers.Compose(Iron.Edge);
            case "GoldOutcrop": Outcrop(layers, Ore.Gold); return layers.Compose(Rock.Edge);
            case "ClayBank": ClayBank(layers); return layers.Compose(Bank.Edge);
            case "HerbPatch": HerbPatch(layers); return layers.Compose(Canopy.Edge);
            default: throw new ArgumentOutOfRangeException(nameof(id), id, "not a round-1 nature asset");
        }
    }

    private enum Ore { None, Iron, Gold }

    /// <summary>
    /// The two layers of one sprite: the body, which gets the edge outline,
    /// and the ground, which holds only the soft shadow and ground marks so the
    /// outline never wraps them. Coordinates are in 32-unit tile space at both sizes.
    /// </summary>
    private sealed class Layers
    {
        public readonly Image Body;
        public readonly Image Ground;
        public readonly PixelCanvas Canvas;
        public readonly PixelCanvas GroundCanvas;
        public readonly float Unit;
        public readonly int Size;

        public Layers(int size)
        {
            Size = size;
            Unit = size / 32f;
            Body = Bitmap.Empty(size, size);
            Ground = Bitmap.Empty(size, size);
            var cell = new Rect2I(0, 0, size, size);
            Canvas = new PixelCanvas(Body, cell, Unit);
            GroundCanvas = new PixelCanvas(Ground, cell, Unit);
        }

        /// <summary>True at 32 px, where single-pixel details are worth drawing (S1).</summary>
        public bool Fine => Unit >= 1;

        /// <summary>One real pixel expressed in tile units: 1 at 32 px, 2 at 16 px.</summary>
        public float Pixel => 1 / Unit;

        /// <summary>The L2 ground shadow: an ellipse already offset toward the south-east by the caller.</summary>
        public void Shadow(float centerX, float centerY, float radiusX, float radiusY) =>
            GroundCanvas.Ellipse(centerX, centerY, radiusX, radiusY, NatureProposal.Shadow);

        /// <summary>The finished sprite: the outlined body over the ground layer.</summary>
        public Image Compose(Color edge, bool outline = true)
        {
            var result = Ground.Duplicate();
            Sheet.Blend(result, outline ? Outlined(Body, edge) : Body, 0, 0);
            return result;
        }

        /// <summary>Blends one pixel of the body (pixel coordinates), clipped to the sprite.</summary>
        public void Blend(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            Body.SetPixel(x, y, Body.GetPixel(x, y).Blend(color));
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

        /// <summary>Draws the polygon edges whose outward side faces the north-west light (L1), for a lit bevel.</summary>
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

    /// <summary>A one-pixel outline in a fixed edge colour around the body's silhouette (four neighbours, STYLE L4).</summary>
    private static Image Outlined(Image body, Color edge)
    {
        var size = body.GetWidth();
        var result = body.Duplicate();
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < size && y < size && body.GetPixel(x, y).A > 0.5f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (body.GetPixel(x, y).A > 0) continue;
                if (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1))
                    result.SetPixel(x, y, edge);
            }
        return result;
    }

    /// <summary>
    /// A canopy or mound like <see cref="PixelCanvas.Lumpy"/>, with the lobe
    /// depth adjustable: 0.1 matches Lumpy, 0.2 gives a bushier silhouette.
    /// </summary>
    private static void Lobed(Layers s, float centerX, float centerY, float radius, Color color, int lobes, float phase, float depth)
    {
        var cx = centerX * s.Unit;
        var cy = centerY * s.Unit;
        var r = radius * s.Unit;
        for (var y = (int)(cy - r - 2); y <= (int)(cy + r + 2); y++)
            for (var x = (int)(cx - r - 2); x <= (int)(cx + r + 2); x++)
            {
                var offset = new Vector2(x + 0.5f - cx, y + 0.5f - cy);
                var edge = r * (1 - depth + depth * Mathf.Sin(offset.Angle() * lobes + phase));
                if (offset.Length() <= edge) s.Blend(x, y, color);
            }
    }

    /// <summary>
    /// A berry, clod or ore fleck that reads at both sizes: at 32 px a 2×2
    /// block with its north-west pixel in the highlight; at 16 px one pixel,
    /// or a two-pixel dash when <paramref name="dash16"/> is set (N3).
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
    /// boulder or slab. The fixed radius factors keep every face slightly
    /// lopsided; <paramref name="turn"/> rotates it so no two faces match.
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
    /// Broadleaf: the current lumpy canopy with a shade rim on the south-east,
    /// light and highlight pulled toward the north-west (N2) and a few leaf dimples.
    /// </summary>
    private static void Broadleaf(Layers s)
    {
        s.Shadow(17, 18, 12, 10);
        Lobed(s, 16, 15, 11.5f, Canopy.Shade, 7, 1, 0.12f);
        Lobed(s, 15.2f, 14.2f, 10.6f, Canopy.Base, 7, 1, 0.12f);
        Lobed(s, 14, 13, 7.5f, Canopy.Light, 5, 2, 0.13f);
        // The highlight disc sits (−3, −3) from the canopy centre at 32 px; at 16 px one step further so it still reads north-west.
        if (s.Fine) s.Canvas.Disc(13, 12, 3, Canopy.Highlight);
        else s.Canvas.Disc(11.5f, 10.5f, 2.6f, Canopy.Highlight);
        if (!s.Fine) return;
        // Leaf dimples: single darker pixels where leaf clusters meet.
        foreach (var (x, y) in new[] { (21, 10), (24, 16), (11, 20), (18, 22), (22, 21) }) s.Canvas.Dot(x, y, Canopy.Shade);
        foreach (var (x, y) in new[] { (17, 9), (10, 16), (18, 16) }) s.Canvas.Dot(x, y, Canopy.Base);
    }

    /// <summary>Conifer: the current nine-point star layers, each inner layer stepped toward the light, with a small lit tip.</summary>
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
        Lobed(s, 16, 16, 6.4f, Canopy.Base, 5, 1, 0.18f);
        Lobed(s, 15.4f, 15.4f, 5.6f, Canopy.Light, 5, 1, 0.18f);
        Lobed(s, 14.6f, 14.6f, 3.2f, Canopy.Highlight, 4, 2, 0.18f);
        if (!s.Fine) return;
        // A centre vein on two of the young leaves.
        s.Canvas.Line(16, 16, 19, 18, Canopy.Base);
        s.Canvas.Line(16, 16, 13, 19, Canopy.Base);
    }

    /// <summary>Berry bush: a dark lobed bush lit from the north-west; with berries, or with bare stalks once picked (N4).</summary>
    private static void BerryBush(Layers s, bool berries)
    {
        s.Shadow(17, 20, 10, 7);
        Lobed(s, 16, 17, 9.5f, Bush.Shade, 9, 4, 0.14f);
        Lobed(s, 15.2f, 16.2f, 8.6f, Bush.Base, 9, 4, 0.14f);
        Lobed(s, 14, 15, 5, Bush.Highlight, 6, 5, 0.14f);
        foreach (var (x, y) in new[] { (20, 14), (12, 19), (18, 21), (22, 18), (15, 12), (10, 15) })
        {
            if (berries) Mark(s, x, y, Berry.Base, Berry.Light, dash16: true);
            else if (s.Fine)
            {
                // A bare stalk: a short brown twig, lit at its north-west end, where the berries hung.
                s.Canvas.Dot(x - 1, y - 1, Timber.Light);
                s.Canvas.Dot(x, y, Timber.Shade);
            }
        }
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
    /// Depleted stone outcrop: the site quarried flat. A grey gravel patch
    /// with a darker rim lies on the ground layer (no outline, so it reads as
    /// level ground), holding a shallow dug hollow (shaded north-west wall,
    /// lit south-east lip) and a few left-over chips. It keeps the outcrop's
    /// own stone colours (N4) and has nothing raised, so it never reads as a
    /// smaller outcrop.
    /// </summary>
    private static void Rubble(Layers s)
    {
        s.Shadow(17, 19, 8, 4.5f);
        var g = s.GroundCanvas;
        g.Lumpy(16, 17, 10, Boulders.Shade with { A = 0.6f }, 6, 1);
        g.Lumpy(15.6f, 16.6f, 9, Boulders.Base with { A = 0.8f }, 6, 1);
        // The hollow: lit far lip, dark near wall, floor.
        g.Ellipse(16.8f, 17.6f, 5.6f, 3.7f, Boulders.Light);
        g.Ellipse(16.2f, 17, 5.2f, 3.3f, Boulders.Edge with { A = 0.85f });
        g.Ellipse(16.9f, 17.7f, 4.2f, 2.5f, Boulders.Shade);
        if (s.Fine)
        {
            // Gravel: a few pale grains on the apron and in the hollow.
            foreach (var (x, y) in new[] { (9, 13), (12, 22), (22, 13), (24, 20), (8, 18), (18, 18) })
                g.Dot(x, y, Boulders.Light);
        }
        // Left-over chips on the rim; the smallest are dropped at 16 px.
        var c = s.Canvas;
        foreach (var (x, y, r) in new[] { (9.5f, 15.5f, 2.2f), (23f, 22f, 2.4f), (21.5f, 11.5f, 1.7f), (12.5f, 23f, 1.6f) })
        {
            if (!s.Fine && r < 2) continue;
            c.Disc(x, y, r, Boulders.Shade);
            c.Disc(x - 0.5f, y - 0.5f, r - 0.8f, Boulders.Light);
            if (s.Fine && r >= 2) c.Dot(x - 1, y - 1, Boulders.Highlight);
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
        Lobed(s, 16, 15.5f, 10.5f, Bank.Shade, 5, 6, 0.1f);
        Lobed(s, 15.3f, 14.8f, 9.6f, Bank.Base, 5, 6, 0.1f);
        Lobed(s, 13, 12.5f, 5.5f, Bank.Light, 4, 2, 0.12f);
        if (s.Fine) c.Disc(11.5f, 11, 1.6f, Bank.Highlight);
        // The bite dug out of the south-east: the bank is cut away there, so
        // its outline follows the cut as a crest line. The floor lies on the
        // ground layer (level ground, no outline) in fresh clay, with the
        // bank's own shadow cast across its north-west side (L1).
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
    /// Herb patch: a low clump of seven sprigs, each a stem with paired small
    /// leaves (lit toward the light), with small pale flowers (four petals
    /// round a gold centre) on four sprig tips. At 16 px the sprigs become a
    /// lobed clump and each flower one pale pixel.
    /// </summary>
    private static void HerbPatch(Layers s)
    {
        s.Shadow(17.5f, 19.5f, 8.5f, 6.5f);
        var c = s.Canvas;
        if (!s.Fine)
        {
            Lobed(s, 16, 16, 6.6f, Canopy.Base, 7, 0.5f, 0.24f);
            Lobed(s, 15, 15, 4, Canopy.Light, 5, 1, 0.2f);
            foreach (var (x, y) in new[] { (11, 12), (20, 11), (21, 19), (13, 20) }) c.Dot(x, y, Cloth.Highlight);
            return;
        }
        c.Disc(16, 16.5f, 3f, Canopy.Shade);
        ReadOnlySpan<(float Angle, float Length)> sprigs =
        [
            (0.2f, 7), (1.1f, 6), (1.9f, 7.5f), (2.8f, 6.5f), (3.7f, 7.5f), (4.6f, 6.5f), (5.45f, 7),
        ];
        foreach (var (angle, length) in sprigs)
        {
            var dir = Vector2.FromAngle(angle);
            var lit = FacesLight(angle, -0.2f);
            var leaf = lit ? Canopy.Light : Canopy.Base;
            c.Line(16, 16.5f, 16 + dir.X * length, 16.5f + dir.Y * length, Canopy.Shade);
            // Two leaf pairs along the stem, angled forward, and one tip leaf.
            foreach (var along in new[] { 0.45f, 0.78f })
                foreach (var side in new[] { -0.75f, 0.75f })
                {
                    var leafAngle = angle + side;
                    var at = new Vector2(16, 16.5f) + dir * (length * along) + Vector2.FromAngle(leafAngle) * 1.4f;
                    c.Leaf(at.X, at.Y, 1.6f, 1f, leafAngle, leaf, leaf);
                }
            var tip = new Vector2(16, 16.5f) + dir * (length + 0.6f);
            c.Leaf(tip.X, tip.Y, 1.6f, 1.1f, angle, lit ? Canopy.Highlight : Canopy.Light, lit ? Canopy.Highlight : Canopy.Light);
        }
        // Flowers: four pale petals round a gold centre, on alternate sprig tips.
        foreach (var index in new[] { 0, 2, 4, 6 })
        {
            var (angle, length) = sprigs[index];
            var at = new Vector2(16, 16.5f) + Vector2.FromAngle(angle) * (length + 0.5f);
            var x = MathF.Round(at.X);
            var y = MathF.Round(at.Y);
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }) c.Dot(x + dx, y + dy, Cloth.Highlight);
            c.Dot(x, y, Gold.Light);
        }
    }
}
