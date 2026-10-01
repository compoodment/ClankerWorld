using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Desert;

/// <summary>
/// Two additions agreed after the October 1 playtest. Cacti come back as
/// plant cover on desert sand only: a barrel cactus, a tall saguaro-like
/// cactus and a prickly-pear clump, drawn like the approved nature sprites
/// (north-west light, south-east ground shadow, a one-pixel outline in the
/// ramp's edge step, readable at 16 px). And snow gets a softer edge where it
/// reaches into neighbouring land: instead of a flat white band with a crisp
/// rim, the snowpack is shaded like a low drift (lit rim to the north-west,
/// the lee step to the south-east) and thins out into scattered clumps and
/// half-transparent frost. Every other surface keeps today's edge pieces.
/// Drawing is deterministic: cactus parts sit at fixed positions, and the
/// snow edge comes from noise hashed with <see cref="PixelArt.Hash"/> per piece.
/// </summary>
public sealed class DesertProposal : IArtProposal, IArtSetProvider
{
    public string Family => "desert";
    public string Name => "desert";

    /// <summary>A five-step colour ramp from the style guide: edge (outline), shade, base, light, highlight.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));
    }

    /// <summary>
    /// Cactus flesh: the style guide's conifer canopy ramp. Its cool blue-green
    /// reads as a succulent against warm sand, and no new hue family is needed.
    /// </summary>
    private static readonly Ramp Flesh = Ramp.Of("1F3E31", "2F5B45", "3F7358", "5E9278", "86B89A");

    /// <summary>Spines and areoles: the Cloth light step, pale straw against the green.</summary>
    private static readonly Color Spine = new("E8DCC0");

    /// <summary>The brightest wool on the barrel cactus crown: the Cloth highlight step.</summary>
    private static readonly Color Wool = new("FFF5DF");

    /// <summary>Barrel cactus crown flowers: Gold light petals round a Gold base centre.</summary>
    private static readonly Ramp Gold = Ramp.Of("8A6A1E", "B8902E", "D9AE3C", "F2CC5E", "FFE28A");

    /// <summary>Prickly-pear fruit: the Berry ramp, the clump's one colour accent at 16 px.</summary>
    private static readonly Ramp Berry = Ramp.Of("7A2A2E", "A33A3F", "C4474B", "F08A8A", "FFC2C2");

    /// <summary>The style guide's Snow ramp; the base is replaced by each snow style's own overview colour.</summary>
    private static readonly Ramp SnowRamp = Ramp.Of("9AAAA8", "B8C6C4", "CCD7D1", "DCE5E0", "F4F8F6");

    /// <summary>The L2 ground shadow colour.</summary>
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    /// <summary>The three cacti: Id and the owner's one-line note.</summary>
    private static readonly (string Id, string Note)[] Cacti =
    [
        ("Cactus", "A round barrel cactus seen from above: ribs radiating from a crown of small yellow flowers, spines as pale dots, a small pup beside it."),
        ("CactusTall", "A tall saguaro-like cactus from straight above: a ribbed round crown with two arms ending in smaller ribbed tops, spines as pale dots."),
        ("CactusPad", "A prickly-pear clump of overlapping oval pads with dotted areoles and three red fruits on the top pads."),
    ];

    /// <summary>
    /// The three cacti over sand and desert brush at 32 and 16 px plus their
    /// bare sprites, a desert patch to judge them in place, and the snow edge
    /// drawn with today's pieces and with the proposed ones.
    /// </summary>
    public IEnumerable<Entry> Render()
    {
        foreach (var (id, note) in Cacti)
        {
            yield return new Entry(Family, id, Bitmap.Over(Ground(TerrainStyle.Sand, 0, 32), Paint(id, 32), 0, 0), note);
            yield return new Entry(Family, id + ".on_brush", Bitmap.Over(Ground(TerrainStyle.DesertBrush, 0, 32), Paint(id, 32), 0, 0), note + " Over desert brush.");
            yield return new Entry(Family, id + ".sprite", Paint(id, 32), note);
        }
        foreach (var (id, note) in Cacti)
        {
            yield return new Entry(Family, id + ".16", Bitmap.Over(Ground(TerrainStyle.Sand, 0, 16), Paint(id, 16), 0, 0), note);
            yield return new Entry(Family, id + ".on_brush.16", Bitmap.Over(Ground(TerrainStyle.DesertBrush, 0, 16), Paint(id, 16), 0, 0), note + " Over desert brush.");
        }
        yield return new Entry(Family, "desert.patch", DesertPatch(32),
            "A 6 x 5 tile stretch of sand and desert brush with a few cacti, to judge how sparse cover reads.");
        yield return new Entry(Family, "desert.patch.16", DesertPatch(16), "The same stretch in the 16 px mid-zoom atlas.");

        var current = new ArtSet();
        var proposed = new ArtSet();
        Apply(proposed);
        var spec = SnowPatch();
        yield return new Entry(Family, "snow.edge.before", SceneComposer.Render(spec, current, 32),
            "Today: snow reaches into grass and rock as a flat white band with a crisp rim.");
        yield return new Entry(Family, "snow.edge.after", SceneComposer.Render(spec, proposed, 32),
            "Proposed: the same pieces shaded like a low drift, thinning into clumps and dust; rock and grass edges unchanged.");
        yield return new Entry(Family, "snow.edge.before.16", SceneComposer.Render(spec, current, 16), "Today, 16 px atlas.");
        yield return new Entry(Family, "snow.edge.after.16", SceneComposer.Render(spec, proposed, 16), "Proposed, 16 px atlas.");
    }

    /// <summary>
    /// Swaps in the soft snow edge pieces; every other surface's pieces come
    /// back unchanged from <see cref="TerrainTransitions.Piece"/>. Cacti have
    /// no <see cref="NatureSprite"/> yet, so the nature delegate is untouched.
    /// </summary>
    public void Apply(ArtSet set)
    {
        var cache = new Dictionary<(TerrainStyle, int, int), Image>();
        set.EdgePiece = (style, piece, size) =>
        {
            if (style is not (TerrainStyle.Snow or TerrainStyle.TundraSnow)) return TerrainTransitions.Piece(style, piece, size);
            if (!cache.TryGetValue((style, piece, size), out var image))
                cache[(style, piece, size)] = image = SnowEdge.Piece(style, piece, size);
            return image;
        };
    }

    /// <summary>
    /// The approved ground under a cactus. Sand is already the approved tile in
    /// the game; desert brush was approved in the round-1 terrain proposal but
    /// is not in the game yet, so it is taken from that proposal until it moves
    /// into <see cref="TerrainTextures"/>.
    /// </summary>
    private static Image Ground(TerrainStyle style, int variant, int size) => style == TerrainStyle.DesertBrush
        ? Terrain.TerrainProposal.Tile(style, variant, size)
        : TerrainTextures.Tile(style, variant, size);

    // ---------------------------------------------------------------- cacti

    /// <summary>Draws one cactus at 32 or 16 px on a transparent square.</summary>
    private static Image Paint(string id, int size)
    {
        var layers = new Layers(size);
        switch (id)
        {
            case "Cactus": Barrel(layers); break;
            case "CactusTall": Saguaro(layers); break;
            case "CactusPad": PricklyPear(layers); break;
            default: throw new ArgumentOutOfRangeException(nameof(id), id, "not a proposed cactus");
        }
        return layers.Compose(Flesh.Edge);
    }

    /// <summary>
    /// Barrel cactus: a large ribbed dome with a small pup to the south-west,
    /// both lit from the north-west, and a ring of yellow flowers round a
    /// woolly crown that stays as one gold pixel at 16 px.
    /// </summary>
    private static void Barrel(Layers s)
    {
        s.Shadow(17, 18, 8.5f, 7.5f);
        s.Shadow(10, 23.5f, 3.6f, 3);
        RibbedDome(s, 9, 20.5f, 3.6f, 7, 0.4f);
        s.CreaseDisc(16, 15, 8.4f, Flesh.Edge);
        RibbedDome(s, 16, 15, 7.6f, 10, 0.2f);
        var c = s.Canvas;
        if (s.Fine)
        {
            // The woolly crown (Cloth light) ringed by four small flowers.
            c.Rect(15, 14, 2, 2, Spine);
            c.Dot(15, 14, Wool);
            foreach (var (x, y) in new[] { (15, 12), (18, 14), (16, 17), (13, 15) })
            {
                c.Dot(x, y, Gold.Light);
                c.Dot(x + 1, y, Gold.Base);
            }
        }
        else
        {
            c.Dot(16, 14, Gold.Light);
        }
    }

    /// <summary>
    /// Saguaro from straight above: two arms reach out from the trunk as lit
    /// tubes and turn up into smaller ribbed tops; the trunk's crown, the
    /// highest part, is drawn last. A longer shadow trailing south-east tells
    /// it apart from the low cacti.
    /// </summary>
    private static void Saguaro(Layers s)
    {
        // The trunk's shadow trails south-east to show its height; each arm top casts a small one.
        s.Shadow(16.5f, 17, 5.5f, 5);
        s.Shadow(19, 19.5f, 4.5f, 4.2f);
        s.Shadow(21.5f, 22, 3.6f, 3.3f);
        s.Shadow(8, 18.5f, 3.5f, 3);
        s.Shadow(25, 22.5f, 3, 2.8f);
        // West arm and east-south-east arm: lit tubes that turn up into rounded tops.
        Tube(s, 14, 14, 6.5f, 15, 2.3f);
        Tube(s, 16, 15, 23.5f, 19, 2.2f);
        RibbedDome(s, 6.5f, 14.5f, 3.2f, 7, 0.6f);
        // Just under three pixels, so the disc has no single-pixel nub at its top and bottom.
        RibbedDome(s, 23.5f, 18.5f, 2.9f, 7, 0.1f);
        s.CreaseDisc(15, 13.5f, 6.6f, Flesh.Edge);
        RibbedDome(s, 15, 13.5f, 5.8f, 10, 0.3f);
    }

    /// <summary>
    /// Prickly pear: oval pads overlapping in a clump, the lower ones drawn
    /// first, each with a crease where it overlaps the pad behind, a lit
    /// north-west side and dotted areoles; red fruits sit on the top pads.
    /// </summary>
    private static void PricklyPear(Layers s)
    {
        s.Shadow(17, 19.5f, 10, 7.5f);
        if (!s.Fine)
        {
            // At 16 px three larger pads keep their oval shapes apart.
            Pad(s, 10, 18.5f, 6.6f, 4.4f, -0.6f, 1);
            Pad(s, 22, 18.5f, 6.6f, 4.4f, 0.6f, 2);
            Pad(s, 16, 12, 6, 4.2f, 0.05f, 3);
            Fruit(s, 15, 7);
            Fruit(s, 26, 15);
            return;
        }
        // Lower pads, then the higher ones in the middle.
        Pad(s, 9.5f, 19, 5.6f, 3.6f, -0.55f, 1);
        Pad(s, 22, 19.5f, 5.6f, 3.6f, 0.55f, 2);
        Pad(s, 15.5f, 22.5f, 5.2f, 3.3f, 0.05f, 3);
        Pad(s, 12, 12, 5.4f, 3.5f, -1.0f, 4);
        Pad(s, 20, 12, 5.4f, 3.5f, 1.0f, 5);
        Pad(s, 16, 16.5f, 5, 3.3f, 0.15f, 6);
        // Fruits on the outer rims of the two back pads and the top one.
        Fruit(s, 9, 7.5f);
        Fruit(s, 23.5f, 7.5f);
        Fruit(s, 20.5f, 15.5f);
    }

    /// <summary>A prickly-pear fruit: a rounded three-pixel berry shape, lit north-west and shaded south-east; one pixel at 16 px.</summary>
    private static void Fruit(Layers s, float x, float y)
    {
        if (s.Fine)
        {
            var c = s.Canvas;
            c.Dot(x, y - 1, Berry.Light);
            c.Dot(x - 1, y, Berry.Light);
            c.Dot(x, y, Berry.Base);
            c.Dot(x + 1, y, Berry.Base);
            c.Dot(x, y + 1, Berry.Base);
            c.Dot(x + 1, y + 1, Berry.Shade);
        }
        else
        {
            s.Canvas.Dot(x, y, Berry.Base);
        }
    }

    /// <summary>
    /// One prickly-pear pad: an oval at an angle with a crease against the
    /// pads already drawn, a one-pixel shade rim on its south-east, a lit
    /// patch toward the north-west, and areoles as faint dots across it.
    /// </summary>
    private static void Pad(Layers s, float cx, float cy, float rx, float ry, float angle, int salt)
    {
        s.OvalOverBody(cx, cy, rx + s.Pixel, ry + s.Pixel, angle, Flesh.Edge);
        s.Oval(cx, cy, rx, ry, angle, Flesh.Shade);
        s.Oval(cx - 0.5f * s.Pixel, cy - 0.5f * s.Pixel, rx - s.Pixel, ry - s.Pixel, angle, Flesh.Base);
        s.Oval(cx - 1.2f, cy - 1.2f, rx * 0.55f, ry * 0.5f, angle, Flesh.Light);
        if (!s.Fine) return;
        var axis = Vector2.FromAngle(angle);
        var across = axis.Orthogonal();
        // Areoles on a loose diamond grid (fractions of the pad's radii along
        // and across it); about one in five is left out so no two pads match,
        // and the one toward the light carries a pale spine.
        ReadOnlySpan<(float Along, float Across)> areoles = [(-0.55f, -0.1f), (0f, -0.5f), (0f, 0.45f), (0.55f, 0.1f), (-0.1f, 0f)];
        for (var index = 0; index < areoles.Length; index++)
        {
            if (PixelArt.Hash(salt, index, 17) % 5 == 0) continue;
            var point = new Vector2(cx, cy) + axis * areoles[index].Along * rx + across * areoles[index].Across * ry;
            s.Canvas.Dot(point.X, point.Y, index == 1 ? Spine : Flesh.Highlight);
        }
    }

    /// <summary>
    /// A ribbed cactus top seen from above: a dome lit from the north-west in
    /// clean ramp-step zones (shade crescent to the south-east, a highlight
    /// toward the north-west), a slightly scalloped rim, and at 32 px a dark
    /// groove running out from near the centre between each pair of ribs,
    /// with an areole on every rib crest (a pale spine on the lit half, a
    /// faint one on the shaded half).
    /// </summary>
    private static void RibbedDome(Layers s, float centerX, float centerY, float radius, int ribs, float phase)
    {
        var cx = centerX * s.Unit;
        var cy = centerY * s.Unit;
        var r = radius * s.Unit;
        var ribbed = s.Fine && r >= 4;
        var steps = new Dictionary<(int, int), int>();
        for (var y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
            for (var x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
            {
                var dx = (x + 0.5f - cx) / r;
                var dy = (y + 0.5f - cy) / r;
                var rho = MathF.Sqrt(dx * dx + dy * dy);
                // Rib crests push the rim out a little, so the outline is gently scalloped.
                var crest = MathF.Cos(MathF.Atan2(dy, dx) * ribs + phase);
                if (rho > (ribbed ? 0.95f + 0.05f * crest : 1f)) continue;
                var step = DomeStep(dx, dy, rho);
                steps[(x, y)] = step;
                s.Set(x, y, StepColor(Flesh, step));
            }
        if (!ribbed) return;
        for (var index = 0; index < ribs; index++)
        {
            // Grooves sit where the crest wave is lowest; areoles halfway between them.
            var groove = (Mathf.Pi * (2 * index + 1) - phase) / ribs;
            var touched = new HashSet<(int, int)>();
            for (var t = 0.34f; t <= 0.9f; t += 0.5f / r)
            {
                var point = ((int)(cx + MathF.Cos(groove) * r * t), (int)(cy + MathF.Sin(groove) * r * t));
                // A groove is the shade step on the lit side of the dome and the edge step on its shaded side.
                if (touched.Add(point) && steps.TryGetValue(point, out var step))
                    s.Set(point.Item1, point.Item2, StepColor(Flesh, Math.Min(step - 1, 0)));
            }
            var crestAngle = (Mathf.Tau * index - phase) / ribs;
            var ax = (int)(cx + MathF.Cos(crestAngle) * r * 0.7f);
            var ay = (int)(cy + MathF.Sin(crestAngle) * r * 0.7f);
            var litHalf = MathF.Cos(crestAngle) + MathF.Sin(crestAngle) < 0.3f;
            if (steps.ContainsKey((ax, ay))) s.Set(ax, ay, litHalf ? Spine : Flesh.Highlight);
        }
    }

    /// <summary>
    /// A horizontal cactus arm seen from above: a tube from one point to
    /// another with rounded ends, lit along its north-west side and shaded
    /// along its south-east side like a lying cylinder.
    /// </summary>
    private static void Tube(Layers s, float fromX, float fromY, float toX, float toY, float halfWidth)
    {
        var a = new Vector2(fromX, fromY) * s.Unit;
        var b = new Vector2(toX, toY) * s.Unit;
        var w = halfWidth * s.Unit;
        var along = b - a;
        var left = (int)(MathF.Min(a.X, b.X) - w - 1);
        var right = (int)(MathF.Max(a.X, b.X) + w + 1);
        var top = (int)(MathF.Min(a.Y, b.Y) - w - 1);
        var bottom = (int)(MathF.Max(a.Y, b.Y) + w + 1);
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var t = Math.Clamp((p - a).Dot(along) / along.LengthSquared(), 0f, 1f);
                var offset = p - (a + along * t);
                var distance = offset.Length();
                if (distance > w) continue;
                s.Set(x, y, StepColor(Flesh, DomeStep(offset.X / w, offset.Y / w, distance / w)));
            }
    }

    /// <summary>
    /// The ramp step of a point on a dome or tube: 0 shade, 1 base, 2 light,
    /// 3 highlight, from a surface normal lit from the north-west and above.
    /// </summary>
    private static int DomeStep(float dx, float dy, float rho)
    {
        var nz = MathF.Sqrt(MathF.Max(0, 1 - rho * rho));
        // Light direction (−1, −1, 1.4) normalised.
        var light = (-dx - dy) * 0.4880f + nz * 0.6832f;
        // Cut-offs chosen so a dome shows a small highlight spot north-west of
        // its centre, a light half, a base band and a shade crescent south-east.
        return light > 0.86f ? 3 : light > 0.62f ? 2 : light > 0.22f ? 1 : 0;
    }

    /// <summary>The colour of a ramp step: −1 or less is the edge step, 3 or more the highlight.</summary>
    private static Color StepColor(Ramp ramp, int step) => step switch
    {
        <= -1 => ramp.Edge,
        0 => ramp.Shade,
        1 => ramp.Base,
        2 => ramp.Light,
        _ => ramp.Highlight,
    };

    /// <summary>
    /// A sprite in two layers: the body, outlined when composed, over a
    /// ground layer holding the L2 shadow as one mask so overlapping shadow
    /// ellipses never darken twice.
    /// </summary>
    private sealed class Layers
    {
        public readonly Image Body;
        public readonly PixelCanvas Canvas;
        public readonly float Unit;
        public readonly int Size;
        private readonly bool[] shadow;

        public Layers(int size)
        {
            Size = size;
            Unit = size / 32f;
            Body = Bitmap.Empty(size, size);
            Canvas = new PixelCanvas(Body, new Rect2I(0, 0, size, size), Unit);
            shadow = new bool[size * size];
        }

        /// <summary>True at 32 px, where single-pixel details are worth drawing (S1).</summary>
        public bool Fine => Unit >= 1;

        /// <summary>One real pixel expressed in tile units: 1 at 32 px, 2 at 16 px.</summary>
        public float Pixel => 1 / Unit;

        /// <summary>Adds an ellipse (tile units) to the ground shadow; the caller offsets it south-east.</summary>
        public void Shadow(float centerX, float centerY, float radiusX, float radiusY)
        {
            var cx = centerX * Unit;
            var cy = centerY * Unit;
            var rx = radiusX * Unit;
            var ry = radiusY * Unit;
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) shadow[y * Size + x] = true;
                }
        }

        /// <summary>Sets one opaque body pixel (pixel coordinates), clipped to the sprite.</summary>
        public void Set(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            Body.SetPixel(x, y, color);
        }

        /// <summary>A filled ellipse rotated by <paramref name="angle"/> (tile units).</summary>
        public void Oval(float centerX, float centerY, float radiusX, float radiusY, float angle, Color color) =>
            OvalPixels(centerX, centerY, radiusX, radiusY, angle, (x, y) => Set(x, y, color));

        /// <summary>A rotated ellipse painted only over existing body pixels: drawn one pixel larger just before a part, it leaves a crease where the part overlaps what is behind it.</summary>
        public void OvalOverBody(float centerX, float centerY, float radiusX, float radiusY, float angle, Color color) =>
            OvalPixels(centerX, centerY, radiusX, radiusY, angle, (x, y) =>
            {
                if (x >= 0 && y >= 0 && x < Size && y < Size && Body.GetPixel(x, y).A > 0) Set(x, y, color);
            });

        /// <summary>A disc painted only over existing body pixels, for a crease round a dome.</summary>
        public void CreaseDisc(float centerX, float centerY, float radius, Color color) =>
            OvalOverBody(centerX, centerY, radius, radius, 0, color);

        private void OvalPixels(float centerX, float centerY, float radiusX, float radiusY, float angle, Action<int, int> plot)
        {
            var cx = centerX * Unit;
            var cy = centerY * Unit;
            var rx = Math.Max(0.6f, radiusX * Unit);
            var ry = Math.Max(0.6f, radiusY * Unit);
            var reach = (int)MathF.Ceiling(MathF.Max(rx, ry)) + 1;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            for (var y = (int)cy - reach; y <= (int)cy + reach; y++)
                for (var x = (int)cx - reach; x <= (int)cx + reach; x++)
                {
                    var ox = x + 0.5f - cx;
                    var oy = y + 0.5f - cy;
                    var u = (ox * cos + oy * sin) / rx;
                    var v = (-ox * sin + oy * cos) / ry;
                    if (u * u + v * v <= 1f) plot(x, y);
                }
        }

        /// <summary>The finished sprite: the shadow, then the body with a one-pixel outline in the edge step (L4).</summary>
        public Image Compose(Color edge)
        {
            var result = Bitmap.Empty(Size, Size);
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (shadow[y * Size + x]) result.SetPixel(x, y, DesertProposal.Shadow);
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && Body.GetPixel(x, y).A > 0.5f;
            var outlined = Body.Duplicate();
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (Body.GetPixel(x, y).A <= 0 && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)))
                        outlined.SetPixel(x, y, edge);
            Sheet.Blend(result, outlined, 0, 0);
            return result;
        }
    }

    // ---------------------------------------------------------- desert patch

    /// <summary>
    /// A 6 × 5 tile stretch of sand with patches of desert brush, drawn by the
    /// scene composer with today's edges, and a few cacti placed on top.
    /// </summary>
    private static Image DesertPatch(int tileSize)
    {
        const int w = 6, h = 5;
        var surface = new byte[w * h];
        var vegetation = new byte[w * h];
        Array.Fill(surface, (byte)1);
        // Desert brush (cactus cover on sand) in two loose patches.
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (3, 2), (4, 2), (4, 3), (5, 3), (3, 3), (5, 4) })
            vegetation[y * w + x] = 5;
        var elevation = new byte[w * h];
        Array.Fill(elevation, (byte)100);
        var spec = new SceneSpec { Width = w, Height = h, Hydrology = new byte[w * h], Surface = surface, Vegetation = vegetation, Elevation = elevation };
        var art = new ArtSet { Tile = Ground };
        var image = SceneComposer.Render(spec, art, tileSize);
        var size = NatureSprites.AtlasTileSize(tileSize);
        foreach (var (x, y, id) in new[] { (1, 1, "CactusTall"), (4, 0, "Cactus"), (3, 3, "CactusPad"), (0, 3, "Cactus"), (5, 2, "CactusTall"), (2, 4, "CactusPad") })
            Sheet.Blend(image, Paint(id, size), x * tileSize, y * tileSize);
        return image;
    }

    // ------------------------------------------------------------ snow edges

    /// <summary>
    /// A 6 × 4 tile patch where snow meets grass and rock: a snowfield in the
    /// west, rock in the north-east, grass in the south, and one lone snow
    /// tile so every outer and inner corner shows.
    /// </summary>
    private static SceneSpec SnowPatch()
    {
        const int w = 6, h = 4;
        // 0 grass, 2 rock, 3 snow (the map's surface codes).
        byte[] surface =
        [
            3, 3, 3, 2, 2, 2,
            3, 3, 3, 3, 2, 0,
            3, 3, 0, 0, 2, 0,
            3, 0, 0, 3, 0, 0,
        ];
        var elevation = new byte[w * h];
        Array.Fill(elevation, (byte)100);
        return new SceneSpec { Width = w, Height = h, Hydrology = new byte[w * h], Surface = surface, Vegetation = new byte[w * h], Elevation = elevation };
    }

    /// <summary>
    /// Soft snow edge pieces. Each keeps today's corner reaches (the same mask
    /// <see cref="TerrainTransitions"/> paints, held exactly near the piece's
    /// ends so neighbouring pieces still meet) and the first rows next to the
    /// snow tile. Between the ends the snow thins out instead of stopping: a
    /// smooth noise field, thresholded more strictly the further a pixel lies
    /// beyond today's edge, leaves the snowpack broken near its rim, then
    /// clumps, then the odd half-transparent grain, never past
    /// <see cref="TerrainTransitions.MaximumReach"/>. The result is shaded as a
    /// low drift: rims facing the north-west light take the light step, lee
    /// rims facing south-east the shade step, and rims are part transparent.
    /// </summary>
    private static class SnowEdge
    {
        private const int Empty = 0, Snow = 1, Frost = 2;
        private static readonly int PiecesPerSide = TerrainTransitions.EdgePiece(1, 0, 0, 0);
        private static readonly int OuterCorner = TerrainTransitions.OuterCornerPiece(0, 0);
        private static readonly int InnerCorner = TerrainTransitions.InnerCornerPiece(0, 0);

        public static Image Piece(TerrainStyle style, int piece, int size)
        {
            // The seed TerrainTransitions paints with, so the solid core wanders exactly as today's piece.
            var seed = PixelArt.Hash((int)style + 1, piece + 1, size);
            var solid = TerrainTransitions.Mask(piece, size, seed, gaps: false);
            var salt = (int)(seed % 1000003);
            var kind = piece < OuterCorner ? Side(solid, piece, size, salt) : Corner(solid, piece, size, salt);
            Tidy(kind, solid, size);
            return Shade(kind, size, TerrainTextures.BaseColor(style));
        }

        /// <summary>
        /// Pixels at each end of a side, or beside each tile edge of a corner,
        /// kept exactly as today's piece, so the snow crosses the tile edge at
        /// the corner's shared reach.
        /// </summary>
        private const int Margin = 1;

        /// <summary>Past the margin, softening ramps in over this many pixels.</summary>
        private const float SoftenOver = 3f;

        /// <summary>
        /// The noise threshold a pixel must beat to hold snow, from how far it
        /// lies beyond today's edge in pixels (negative inside it): about four
        /// in five pixels hold snow three pixels inside, under half just
        /// outside, and the odd clump still four pixels out.
        /// </summary>
        private static float SnowThreshold(float beyond) => 0.5f + 0.1f * beyond;

        /// <summary>The threshold for a half-transparent frost pixel beyond the snow: sparse patches that thin out with distance.</summary>
        private static float FrostThreshold(float beyond) => 0.6f + 0.06f * beyond;

        /// <summary>
        /// Smooth value noise over a piece's local coordinates, stretched to
        /// spread across [0, 1]: blobs about four pixels across roughened by a
        /// two-pixel octave at 32 px, two-pixel blobs at 16 px, so thresholds
        /// make clumps rather than grain.
        /// </summary>
        private static float Noise(int u, int v, int size, int salt) => Stretch(size >= 32
            ? 0.68f * Value(u, v, 4, salt) + 0.32f * Value(u, v, 2, salt + 7)
            : Value(u, v, 2, salt));

        /// <summary>Frost patches: their own noise, three-pixel blobs at 32 px.</summary>
        private static float FrostNoise(int u, int v, int size, int salt) => Stretch(Value(u, v, size >= 32 ? 3 : 2, salt + 31));

        /// <summary>Interpolated value noise bunches up round one half; this spreads it back out.</summary>
        private static float Stretch(float value) => Math.Clamp(0.5f + (value - 0.5f) * 1.7f, 0f, 1f);

        private static float Value(int u, int v, int lattice, int salt)
        {
            // The lattice is turned about 35 degrees off the edge, so its rows
            // never line up into stripes running along the edge.
            const float cos = 0.819f, sin = 0.574f;
            var fu = ((u + 0.5f) * cos + (v + 0.5f) * sin) / lattice;
            var fv = (-(u + 0.5f) * sin + (v + 0.5f) * cos) / lattice;
            var iu = (int)MathF.Floor(fu);
            var iv = (int)MathF.Floor(fv);
            var tu = fu - iu;
            var tv = fv - iv;
            tu = tu * tu * (3 - 2 * tu);
            tv = tv * tv * (3 - 2 * tv);
            float At(int a, int b) => PixelArt.Hash(a, b, salt) % 1024 / 1023f;
            return Mathf.Lerp(Mathf.Lerp(At(iu, iv), At(iu + 1, iv), tu), Mathf.Lerp(At(iu, iv + 1), At(iu + 1, iv + 1), tu), tv);
        }

        /// <summary>A side piece in its local coordinates: u along the edge, v depth away from the snow tile.</summary>
        private static int[] Side(bool[] solid, int piece, int size, int salt)
        {
            var side = piece / PiecesPerSide;
            var deepest = TerrainTransitions.MaximumReach(size) - 1;
            var fine = size >= 32;
            int Index(int u, int v)
            {
                var (px, py) = side switch
                {
                    0 => (u, v),
                    1 => (size - 1 - v, u),
                    2 => (u, size - 1 - v),
                    _ => (v, u),
                };
                return py * size + px;
            }
            var kind = new int[size * size];
            for (var u = 0; u < size; u++)
            {
                var depth = 0;
                while (depth < size && solid[Index(u, depth)]) depth++;
                var fromEnd = Math.Min(u, size - 1 - u);
                var taper = Math.Clamp((fromEnd - Margin + 1) / SoftenOver, 0f, 1f);
                // The snowpack may break up only in its outer two rows at 32 px
                // (never at 16 px), so no gaps line up along the tile edge.
                var solidTo = fine ? Math.Max(2, depth - 2) : depth;
                for (var v = 0; v < size; v++)
                {
                    var original = solid[Index(u, v)];
                    if (fromEnd < Margin || v > deepest)
                    {
                        kind[Index(u, v)] = original ? Snow : Empty;
                        continue;
                    }
                    var beyond = v + 0.5f - depth;
                    // Near the ends a higher bar holds back new snow and frost.
                    var held = 0.6f * (1 - taper);
                    // At 16 px there is no room for detached clumps: snow only grows one-pixel bumps on the edge.
                    if (!fine && beyond > 1) held = 1;
                    if (v < Math.Min(depth, solidTo) || (original && taper < 1) || Noise(u, v, size, salt) > SnowThreshold(beyond) + held)
                        kind[Index(u, v)] = Snow;
                    else if (beyond > -1 && FrostNoise(u, v, size, salt) > FrostThreshold(beyond) + 0.6f * (1 - taper))
                        kind[Index(u, v)] = Frost;
                }
            }
            return kind;
        }

        /// <summary>
        /// A corner piece in local coordinates counting away from its corner:
        /// softened on the diagonal by distance beyond today's arc, exact
        /// beside both tile edges, so the edges of neighbouring tiles meet it.
        /// </summary>
        private static int[] Corner(bool[] solid, int piece, int size, int salt)
        {
            var inner = piece >= InnerCorner;
            var corner = (piece - (inner ? InnerCorner : OuterCorner)) / TerrainTransitions.Levels;
            var deepest = TerrainTransitions.MaximumReach(size) - 1;
            int Index(int u, int v)
            {
                var (px, py) = corner switch
                {
                    0 => (size - 1 - u, v),
                    1 => (size - 1 - u, size - 1 - v),
                    2 => (u, size - 1 - v),
                    _ => (u, v),
                };
                return py * size + px;
            }
            // Today's arc, measured along the diagonal.
            var along = 0;
            while (along < size && solid[Index(along, along)]) along++;
            var arc = along * MathF.Sqrt(2);
            var kind = new int[size * size];
            for (var v = 0; v < size; v++)
                for (var u = 0; u < size; u++)
                {
                    var original = solid[Index(u, v)];
                    if (u < Margin || v < Margin || u > deepest || v > deepest)
                    {
                        kind[Index(u, v)] = original ? Snow : Empty;
                        continue;
                    }
                    var beyond = MathF.Sqrt((u + 0.5f) * (u + 0.5f) + (v + 0.5f) * (v + 0.5f)) - arc;
                    var held = 0.6f * (1 - Math.Clamp((Math.Min(u, v) - Margin + 1) / SoftenOver, 0f, 1f));
                    // As on the sides, only the outer two pixels of today's arc may break up, never at 16 px.
                    var firm = original && (size < 32 || beyond < -2);
                    if (firm || (original && held > 0) || Noise(u, v, size, salt) > SnowThreshold(beyond) + held) kind[Index(u, v)] = Snow;
                    else if (beyond > -1 && FrostNoise(u, v, size, salt) > FrostThreshold(beyond) + held) kind[Index(u, v)] = Frost;
                }
            return kind;
        }

        /// <summary>
        /// Folds single pixels away: a lone new snow pixel becomes a
        /// half-transparent grain of frost (a lone pixel of today's piece
        /// stays), and a one-pixel hole inside the snowpack fills in. The tile
        /// border is left as it is, so the corner reaches still meet.
        /// </summary>
        private static void Tidy(int[] kind, bool[] original, int size)
        {
            var source = (int[])kind.Clone();
            bool SnowAt(int x, int y) => source[y * size + x] == Snow;
            for (var y = 1; y < size - 1; y++)
                for (var x = 1; x < size - 1; x++)
                {
                    var neighbours = (SnowAt(x - 1, y) ? 1 : 0) + (SnowAt(x + 1, y) ? 1 : 0) +
                        (SnowAt(x, y - 1) ? 1 : 0) + (SnowAt(x, y + 1) ? 1 : 0);
                    var here = source[y * size + x];
                    if (here == Snow && neighbours == 0 && !original[y * size + x]) kind[y * size + x] = Frost;
                    else if (here == Empty && neighbours == 4) kind[y * size + x] = Snow;
                }
        }

        /// <summary>
        /// Colours the piece as a low drift: base inside, the light step on rims
        /// facing north or west, the shade step on lee rims facing south or east,
        /// lit rims part transparent, frost at about half alpha. Off-piece
        /// pixels count as snow, as in <see cref="TerrainTransitions.WriteMask"/>,
        /// so no rim appears along the tile edge.
        /// </summary>
        private static Image Shade(int[] kind, int size, Color baseColor)
        {
            var image = Bitmap.Empty(size, size);
            bool SnowAt(int x, int y) => x < 0 || y < 0 || x >= size || y >= size || kind[y * size + x] == Snow;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var here = kind[y * size + x];
                    if (here == Empty) continue;
                    if (here == Frost)
                    {
                        image.SetPixel(x, y, SnowRamp.Light with { A = 0.45f });
                        continue;
                    }
                    var lit = !SnowAt(x, y - 1) || !SnowAt(x - 1, y);
                    var lee = !SnowAt(x, y + 1) || !SnowAt(x + 1, y);
                    // At 32 px the lee slope is two pixels deep: the step behind a lee rim is shaded too.
                    var leeSlope = size >= 32 && !lit && (!SnowAt(x, y + 2) || !SnowAt(x + 2, y));
                    image.SetPixel(x, y, (lit, lee) switch
                    {
                        (false, false) => leeSlope ? SnowRamp.Shade : baseColor,
                        (true, false) => SnowRamp.Light with { A = 0.75f },
                        (false, true) => SnowRamp.Shade,
                        _ => baseColor with { A = 0.65f },
                    });
                }
            return image;
        }
    }
}
