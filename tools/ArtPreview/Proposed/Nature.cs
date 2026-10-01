using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Nature;

/// <summary>
/// Nature mockups (rounds 1 and 2): trees and natural sites redrawn gently
/// over the current <see cref="NatureSprites"/> look, plus the picked,
/// harvested, depleted and regrowing states the game design agrees on. Every sprite keeps its silhouette
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

    /// <summary>
    /// Host rock of the diamond outcrop: the style guide's Slate ramp, which
    /// sits on today's blue-grey diamond-outcrop stones (4C5356, 6A7276,
    /// 98A2A6), so the cool rock sets off the pale crystals.
    /// </summary>
    private static readonly Ramp Slate = Ramp.Of("2B2E33", "4A4E55", "62666E", "80858E", "9A9FA7");
    private static readonly Ramp Diamond = Ramp.Of("3F7E86", "5FB4BE", "7FD3DC", "B4EEF2", "E8FFFF");

    /// <summary>The Fertile soil ramp, for loosened earth under a regrowing sprout and a bare depleted patch.</summary>
    private static readonly Ramp Soil = Ramp.Of("4A3A2A", "5C4B35", "735F45", "86704F", "9A8460");

    /// <summary>
    /// Reed stems and blades: today's two olive greens (6E7F46, 8A9A55) as the
    /// base and light steps, with a darker shade, an olive edge and one pale
    /// highlight added around them, the way the fibre plant ramp was built.
    /// </summary>
    private static readonly Ramp Reed = Ramp.Of("3D4A2B", "57653A", "6E7F46", "8A9A55", "A6B36C");

    /// <summary>
    /// Gravel left by quarrying iron rock: the Iron ramp with its middle steps
    /// stained a quarter of the way toward Rust shade, as the outcrop's faces
    /// are, so it reads warmer than the gold outcrop's grey gravel.
    /// </summary>
    private static readonly Ramp IronGravel = new(Iron.Edge, Iron.Shade.Lerp(Rust.Shade, 0.25f),
        Iron.Base.Lerp(Rust.Shade, 0.25f), Iron.Light.Lerp(Rust.Shade, 0.25f), Iron.Highlight);

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
        // Round 2.
        ("ConiferStump", NatureSprite.ConiferStump, "A darker cut face than the broadleaf stump, with close rings, a scaly bark ring, five slender roots and a bead of amber resin."),
        ("ConiferSapling", NatureSprite.ConiferSapling, "A small seven-point star in the lighter needle steps, lit north-west, outlined; the points stay at 16 px."),
        ("Reeds", NatureSprite.Reeds, "Today's standing reeds, redrawn: three olive clumps of stems fanning up, with dark cattail heads lit on the north-west."),
        ("ReedsHarvested", null, "The clumps cut low: short stubs with pale cut ends and the low leaves, no cattail heads."),
        ("WildGreensPicked", null, "The rosette with its big outer leaves picked: short cut stalks with pale ends round the young inner leaves."),
        ("FiberPlantHarvested", null, "The same dark clump with every blade cut short; each stub ends in a pale cut."),
        ("HerbPatchPicked", null, "The herb clump with its sprig tips and flowers snipped off: shorter stems with their lower leaves."),
        ("DiamondOutcrop", NatureSprite.DiamondOutcrop, "Blue-grey slate boulders holding pale faceted crystals lit from the north-west; cyan dashes at 16 px."),
        ("IronOutcropDepleted", null, "The iron outcrop quarried flat: iron-grey gravel, a dug hollow, rust-stained chips."),
        ("GoldOutcropDepleted", null, "The gold outcrop quarried flat: grey gravel and hollow with a few dull gold traces."),
        ("DiamondOutcropDepleted", null, "The diamond outcrop quarried flat: slate gravel and hollow with one dull crystal shard."),
        ("ClayBankDepleted", null, "The clay bank dug out: a low crest of the old bank on the north-west round a wide pit of fresh clay."),
        ("Regrowing", NatureSprite.Regrowing, "A fresh sprout, two seed leaves and a young leaf, on a small patch of loosened earth; for any regrowing site."),
        ("Depleted", NatureSprite.Depleted, "Fallback for a used-up site without its own art: a bare scuffed patch of earth with a few pebbles and a broken twig."),
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
            case "StoneOutcropDepleted": Rubble(layers, Boulders, Ore.None); return layers.Compose(Rock.Edge);
            case "IronOutcrop": Outcrop(layers, Ore.Iron); return layers.Compose(Iron.Edge);
            case "GoldOutcrop": Outcrop(layers, Ore.Gold); return layers.Compose(Rock.Edge);
            case "ClayBank": ClayBank(layers); return layers.Compose(Bank.Edge);
            case "HerbPatch": HerbPatch(layers); return layers.Compose(Canopy.Edge);
            case "ConiferStump": ConiferStump(layers); return layers.Compose(Timber.Edge);
            // Like the conifer, the 16 px sapling draws its own edge star so its points stay apart.
            case "ConiferSapling": ConiferSapling(layers); return layers.Compose(Needle.Edge, outline: layers.Fine);
            // Reed blades carry their own south-east edge copy, like the fibre plant, so they stay slender.
            case "Reeds": Reeds(layers, harvested: false); return layers.Compose(Reed.Edge, outline: false);
            case "ReedsHarvested": Reeds(layers, harvested: true); return layers.Compose(Reed.Edge, outline: false);
            case "WildGreensPicked": WildGreensPicked(layers); return layers.Compose(Canopy.Edge);
            case "FiberPlantHarvested": FiberPlantHarvested(layers); return layers.Compose(Fiber.Edge, outline: false);
            case "HerbPatchPicked": HerbPatchPicked(layers); return layers.Compose(Canopy.Edge);
            case "DiamondOutcrop": Outcrop(layers, Ore.Diamond); return layers.Compose(Slate.Edge);
            case "IronOutcropDepleted": Rubble(layers, IronGravel, Ore.Iron); return layers.Compose(Iron.Edge);
            case "GoldOutcropDepleted": Rubble(layers, Rock, Ore.Gold); return layers.Compose(Rock.Edge);
            case "DiamondOutcropDepleted": Rubble(layers, Slate, Ore.Diamond); return layers.Compose(Slate.Edge);
            case "ClayBankDepleted": ClayPit(layers); return layers.Compose(Bank.Edge);
            case "Regrowing": Regrowing(layers); return layers.Compose(Canopy.Edge);
            case "Depleted": BareGround(layers); return layers.Compose(Soil.Edge);
            default: throw new ArgumentOutOfRangeException(nameof(id), id, "not a proposed nature asset");
        }
    }

    private enum Ore { None, Iron, Gold, Diamond }

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
        // Diamond-bearing rock is the cool blue-grey slate, as today's diamond outcrop is.
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
    /// One crystal at 32 px, seen from above (N3): a small rhombus whose
    /// north-west facets are Diamond highlight and light and whose south-east
    /// facets are Diamond base and shade, with a Diamond-edge pixel or two
    /// where it meets the rock on its shaded side. The large one is five
    /// pixels across, the small one three. Pixel coordinates.
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
                        'H' => Diamond.Highlight, 'L' => Diamond.Light, 'B' => Diamond.Base,
                        'S' => Diamond.Shade, 'E' => Diamond.Edge, _ => null,
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
    /// A depleted outcrop: the site quarried flat. A gravel patch with a
    /// darker rim lies on the ground layer (no outline, so it reads as level
    /// ground), holding a shallow dug hollow (shaded north-west wall, lit
    /// south-east lip) and a few left-over chips. It keeps the outcrop's own
    /// stone colours (N4) and has nothing raised, so it never reads as a
    /// smaller outcrop. An ore outcrop leaves a dull trace of its ore: a rust
    /// stain, a few gold specks or one crystal shard, never a bright fleck,
    /// so it does not look as if there is still ore to take.
    /// </summary>
    private static void Rubble(Layers s, Ramp stone, Ore ore)
    {
        s.Shadow(17, 19, 8, 4.5f);
        var g = s.GroundCanvas;
        g.Lumpy(16, 17, 10, stone.Shade with { A = 0.6f }, 6, 1);
        g.Lumpy(15.6f, 16.6f, 9, stone.Base with { A = 0.8f }, 6, 1);
        // The hollow: lit far lip, dark near wall, floor.
        g.Ellipse(16.8f, 17.6f, 5.6f, 3.7f, stone.Light);
        g.Ellipse(16.2f, 17, 5.2f, 3.3f, stone.Edge with { A = 0.85f });
        g.Ellipse(16.9f, 17.7f, 4.2f, 2.5f, stone.Shade);
        if (s.Fine)
        {
            // Gravel: a few pale grains on the apron and in the hollow.
            foreach (var (x, y) in new[] { (9, 13), (12, 22), (22, 13), (24, 20), (8, 18), (18, 18) })
                g.Dot(x, y, stone.Light);
        }
        // Left-over chips on the rim; the smallest are dropped at 16 px.
        var c = s.Canvas;
        foreach (var (x, y, r) in new[] { (9.5f, 15.5f, 2.2f), (23f, 22f, 2.4f), (21.5f, 11.5f, 1.7f), (12.5f, 23f, 1.6f) })
        {
            if (!s.Fine && r < 2) continue;
            c.Disc(x, y, r, stone.Shade);
            c.Disc(x - 0.5f, y - 0.5f, r - 0.8f, stone.Light);
            if (s.Fine && r >= 2) c.Dot(x - 1, y - 1, stone.Highlight);
        }
        RubbleTrace(s, ore);
    }

    /// <summary>The dull ore trace left in a quarried outcrop (N4): rust on iron rock, dull gold specks, one crystal shard.</summary>
    private static void RubbleTrace(Layers s, Ore ore)
    {
        var g = s.GroundCanvas;
        var c = s.Canvas;
        switch (ore)
        {
            case Ore.Iron:
                if (s.Fine)
                {
                    // A rust stain across the hollow floor and on two chips.
                    g.Line(14, 18, 19, 19, Rust.Shade with { A = 0.8f });
                    g.Dot(16, 17, Rust.Edge);
                    c.Dot(9, 15, Rust.Shade);
                    c.Dot(22, 22, Rust.Shade);
                }
                else c.Dot(23, 22, Rust.Shade);
                break;
            case Ore.Gold:
                // Single pixels in Gold base and shade: duller and smaller than the outcrop's 2×2 flecks.
                if (s.Fine)
                {
                    g.Dot(15, 18, Gold.Base);
                    g.Dot(22, 15, Gold.Base);
                    g.Dot(11, 21, Gold.Shade);
                }
                else g.Dot(17, 18, Gold.Base);
                break;
            case Ore.Diamond:
                // One broken shard in the hollow, in the dull crystal steps.
                if (s.Fine)
                {
                    g.Dot(17, 18, Diamond.Shade);
                    g.Dot(18, 18, Diamond.Edge);
                    g.Dot(12, 21, Diamond.Shade with { A = 0.7f });
                }
                else g.Dot(17, 18, Diamond.Shade);
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
    // ───────────────────────────── Round 2 ─────────────────────────────

    /// <summary>
    /// Conifer stump: told apart from the broadleaf stump by a darker cut face
    /// (Timber base, as today's conifer stump is darker) with two close rings,
    /// a scaly bark ring with dark plate notches, five slender roots spread
    /// evenly, and a bead of amber resin on the rim as its colour accent (S4).
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
    /// Reeds: three clumps of standing stems, kept from today's sprite
    /// because the brown cattail head is what tells reeds from the fibre
    /// plant. Each clump is a small olive tuft (edge rim, shade, lit
    /// north-west) with stems fanning up and out of it, two low leaves
    /// splaying sideways, and cattail heads on the tallest stems: a dark
    /// Timber-shade spike along the stem with a Timber-edge copy to the
    /// south-east, a lit north-west pixel and a pale stalk tip beyond. Stems
    /// leaning toward the light are lighter. At 16 px each head is a brown
    /// pixel over an edge pixel. Harvested, the stems are cut low: short stubs
    /// with pale cut ends and the low leaves, no heads (N4).
    /// </summary>
    private static void Reeds(Layers s, bool harvested)
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
                    ReedStem(s, pass, root, angle, harvested ? 3 + i % 2 : length);
                }
            }
        // The tuft at the foot of each clump.
        foreach (var (root, _) in clumps)
        {
            Lobed(s, root.X, root.Y, 2.4f + s.Pixel, Reed.Edge, 5, 1, 0.15f);
            Lobed(s, root.X, root.Y, 2.4f, Reed.Shade, 5, 1, 0.15f);
            if (s.Fine) c.Dot(root.X - 1, root.Y - 1, Reed.Light);
        }
        foreach (var (root, stems) in clumps)
            foreach (var (angle, length, head) in stems)
            {
                var dir = Vector2.FromAngle(angle);
                if (harvested)
                {
                    // A pale cut end on each stub.
                    if (s.Fine) c.Dot(root.X + dir.X * 3.4f, root.Y + dir.Y * 3.4f, Reed.Highlight);
                    continue;
                }
                if (!head) continue;
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
    /// Picked wild greens (N4): the big outer leaves are gone, leaving short
    /// cut stalks with pale cut ends round the heart and the three young inner
    /// leaves. At 16 px a small five-stalk rosette with a pale centre.
    /// </summary>
    private static void WildGreensPicked(Layers s)
    {
        s.Shadow(17, 18, 6.5f, 5);
        var c = s.Canvas;
        var leaves = s.Fine ? 7 : 5;
        for (var i = 0; i < leaves; i++)
        {
            var angle = i * Mathf.Tau / leaves + 0.45f;
            var lit = FacesLight(angle);
            var dir = Vector2.FromAngle(angle);
            var color = lit ? Canopy.Light : Canopy.Base;
            c.Leaf(16 + dir.X * 3.2f, 16 + dir.Y * 3.2f, 2.6f, 1.5f, angle, color, color);
            if (s.Fine) c.Dot(16 + dir.X * 5.4f, 16 + dir.Y * 5.4f, GreensVein);
        }
        c.Disc(16, 16, 1.8f, s.Fine ? Canopy.Shade : GreensVein);
        if (!s.Fine) return;
        for (var i = 0; i < 3; i++)
        {
            var angle = i * Mathf.Tau / 3 - 1.2f;
            var dir = Vector2.FromAngle(angle);
            c.Leaf(16 + dir.X * 2.4f, 16 + dir.Y * 2.4f, 2.4f, 1.3f, angle, Canopy.Highlight, GreensVein);
        }
    }

    /// <summary>
    /// Harvested fibre plant (N4): the same dark clump with every blade cut
    /// to a short stub, drawn the same way (south-east edge copy, lit stubs
    /// toward the light), each stub ending in a pale cut.
    /// </summary>
    private static void FiberPlantHarvested(Layers s)
    {
        s.Shadow(17, 18.5f, 7, 4.8f);
        var c = s.Canvas;
        // The fibre plant's eleven blade angles, cut to stubs that alternate long and short so they stay apart.
        ReadOnlySpan<(float Angle, float Length)> stubs =
        [
            (0.1f, 6.5f), (0.75f, 5), (1.35f, 6.5f), (1.95f, 5), (2.5f, 6.5f), (3.05f, 5),
            (3.65f, 7), (4.15f, 5), (4.7f, 7), (5.25f, 5), (5.8f, 6.5f),
        ];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < stubs.Length; i++)
            {
                if (!s.Fine && i % 3 == 1) continue;
                var (angle, length) = stubs[i];
                var half = length / 2;
                var dir = Vector2.FromAngle(angle);
                var offset = pass == 0 ? s.Pixel : 0;
                var lit = FacesLight(angle, 0.1f);
                var color = pass == 0 ? Fiber.Edge : lit ? Fiber.Light : Fiber.Base;
                c.Leaf(16 + dir.X * (half + 1) + offset, 16 + dir.Y * (half + 1) + offset, half, 0.9f, angle, color, color);
            }
            if (pass == 0) c.Disc(16 + s.Pixel, 16.5f + s.Pixel, 3.6f, Fiber.Edge);
        }
        c.Disc(16, 16.5f, 3.4f, Fiber.Shade);
        c.Disc(15, 15.5f, 1.6f, Fiber.Base);
        if (!s.Fine) return;
        foreach (var (angle, length) in stubs)
        {
            var dir = Vector2.FromAngle(angle);
            c.Dot(16 + dir.X * (length + 0.3f), 16 + dir.Y * (length + 0.3f), FacesLight(angle, 0.1f) ? Fiber.Highlight : Fiber.Light);
        }
    }

    /// <summary>
    /// Picked herb patch (N4): the herb clump with its sprig tips and flowers
    /// snipped off. Each stem is cut to about half its length with a pale cut
    /// end and keeps its lower leaf pair. At 16 px a smaller lobed clump with
    /// no flower dots.
    /// </summary>
    private static void HerbPatchPicked(Layers s)
    {
        s.Shadow(17.5f, 19, 7, 5);
        var c = s.Canvas;
        if (!s.Fine)
        {
            Lobed(s, 16, 16, 5.2f, Canopy.Base, 7, 0.5f, 0.24f);
            Lobed(s, 15, 15, 2.8f, Canopy.Light, 5, 1, 0.2f);
            return;
        }
        c.Disc(16, 16.5f, 2.2f, Canopy.Shade);
        // The herb patch's seven sprigs, as drawn on the unpicked patch.
        ReadOnlySpan<(float Angle, float Length)> sprigs =
        [
            (0.2f, 7), (1.1f, 6), (1.9f, 7.5f), (2.8f, 6.5f), (3.7f, 7.5f), (4.6f, 6.5f), (5.45f, 7),
        ];
        for (var index = 0; index < sprigs.Length; index++)
        {
            var (angle, length) = sprigs[index];
            var dir = Vector2.FromAngle(angle);
            var lit = FacesLight(angle, -0.2f);
            var leaf = lit ? Canopy.Light : Canopy.Base;
            var cut = length * 0.8f;
            c.Line(16, 16.5f, 16 + dir.X * cut, 16.5f + dir.Y * cut, Canopy.Shade);
            // The lower leaf pair stays; of the upper pair only one leaf is left, on alternate sides, so the clump looks snipped.
            foreach (var (along, side) in new[] { (0.45f, -0.75f), (0.45f, 0.75f), (0.72f, index % 2 == 0 ? -0.75f : 0.75f) })
            {
                var leafAngle = angle + side;
                var at = new Vector2(16, 16.5f) + dir * (length * along) + Vector2.FromAngle(leafAngle) * 1.4f;
                c.Leaf(at.X, at.Y, 1.6f, 1f, leafAngle, leaf, leaf);
            }
            c.Dot(16 + dir.X * (cut + 0.5f), 16.5f + dir.Y * (cut + 0.5f), GreensVein);
        }
        // The heart of the clump, filled with leaves so it does not read as a ring.
        c.Disc(16, 16.5f, 2.2f, Canopy.Base);
        c.Disc(15.5f, 16, 1.2f, Canopy.Light);
    }

    /// <summary>
    /// Depleted clay bank: the bank dug away, drawn like the quarried stone
    /// outcrop so the depleted sites read as one family. A trodden apron of
    /// bank brown lies on the ground layer round a shallow dug hollow of fresh
    /// clay (lit south-east lip, shaded north-west wall, floor with spade
    /// scrapes); a low hump of the old bank survives on the north-west, and
    /// two dug clods are left behind. It keeps the bank's own colours (N4)
    /// and nothing tall remains, so it never reads as a smaller bank.
    /// </summary>
    private static void ClayPit(Layers s)
    {
        s.Shadow(17, 19, 8.5f, 5);
        var g = s.GroundCanvas;
        g.Lumpy(16, 17, 10.5f, Bank.Shade with { A = 0.6f }, 6, 1);
        g.Lumpy(15.6f, 16.6f, 9.5f, Bank.Base with { A = 0.8f }, 6, 1);
        g.Ellipse(17.8f, 18.6f, 6.4f, 4.2f, Clay.Light);
        g.Ellipse(17.2f, 18, 6, 3.8f, Clay.Edge with { A = 0.9f });
        g.Ellipse(17.9f, 18.7f, 4.9f, 3, Clay.Shade);
        if (s.Fine)
        {
            g.Line(17, 18, 18, 20, Clay.Base);
            g.Line(20, 17.5f, 21, 19.5f, Clay.Base);
            // Crumbs of bank soil on the apron.
            foreach (var (x, y) in new[] { (9, 15), (24, 14), (12, 23), (25, 20) }) g.Dot(x, y, Bank.Light);
        }
        // The low hump of the old bank on the north-west, lit on its crest, with a grass tuft.
        Lobed(s, 11, 12, 4.4f, Bank.Shade, 4, 2, 0.15f);
        Lobed(s, 10.5f, 11.5f, 3.5f, Bank.Base, 4, 2, 0.15f);
        if (s.Fine)
        {
            s.Canvas.Disc(9.6f, 10.6f, 1.3f, Bank.Light);
            s.Canvas.Line(12, 10, 13, 8.5f, GrassTuft);
        }
        // Dug clods of fresh clay left on the apron.
        Mark(s, 24, 23, Clay.Base, Clay.Light);
        Mark(s, 22, 12, Clay.Base, Clay.Light);
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
