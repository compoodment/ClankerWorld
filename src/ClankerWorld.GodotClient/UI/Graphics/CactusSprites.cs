using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The three desert cacti approved in round three of the October 1 art review,
/// drawn into their <see cref="NatureSprites"/> atlas cells: a barrel cactus, a
/// saguaro seen from above and a prickly-pear clump. They use the conifer
/// canopy greens, lit from the north-west, with a soft shadow to the south-east
/// and a one-pixel outline in the darkest step. The 16 px versions are drawn
/// separately and keep one clear mark each.
/// </summary>
internal static class CactusSprites
{
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));
    }

    /// <summary>The cactus body: the conifer canopy greens, a cool blue-green that stands out on warm sand.</summary>
    private static readonly Ramp Flesh = Ramp.Of("1F3E31", "2F5B45", "3F7358", "5E9278", "86B89A");

    /// <summary>Pale spines: the Cloth light step.</summary>
    private static readonly Color Spine = new("E8DCC0");

    /// <summary>The barrel's woolly crown.</summary>
    private static readonly Color Wool = new("FFF5DF");

    private static readonly Ramp Gold = Ramp.Of("8A6A1E", "B8902E", "D9AE3C", "F2CC5E", "FFE28A");
    private static readonly Ramp Berry = Ramp.Of("7A2A2E", "A33A3F", "C4474B", "F08A8A", "FFC2C2");
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    public static bool Draws(NatureSprite sprite) => sprite is NatureSprite.Cactus or NatureSprite.CactusTall or NatureSprite.CactusPad;

    /// <summary>
    /// The cactus standing on a cactus-cover tile, if any: about one tile in
    /// five, as sparse as the approved desert picture, with the three kinds
    /// equally common. Chosen from the tile's position, so it never changes.
    /// </summary>
    public static NatureSprite? ForTile(int x, int y)
    {
        var roll = PixelArt.Hash(x, y, 157);
        if (roll % 5 != 0) return null;
        return (roll / 5 % 3) switch
        {
            0 => NatureSprite.Cactus,
            1 => NatureSprite.CactusTall,
            _ => NatureSprite.CactusPad,
        };
    }

    /// <summary>Draws one cactus into its atlas cell.</summary>
    public static void Paint(Image atlas, Rect2I cell, NatureSprite sprite)
    {
        var layers = new Layers(cell.Size.X);
        switch (sprite)
        {
            case NatureSprite.Cactus: Barrel(layers); break;
            case NatureSprite.CactusTall: Saguaro(layers); break;
            case NatureSprite.CactusPad: PricklyPear(layers); break;
            default: throw new ArgumentOutOfRangeException(nameof(sprite), sprite, "not a cactus");
        }
        layers.Compose(atlas, cell, Flesh.Edge);
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
            // The woolly crown ringed by four small flowers.
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
        s.Shadow(16.5f, 17, 5.5f, 5);
        s.Shadow(19, 19.5f, 4.5f, 4.2f);
        s.Shadow(21.5f, 22, 3.6f, 3.3f);
        s.Shadow(8, 18.5f, 3.5f, 3);
        s.Shadow(25, 22.5f, 3, 2.8f);
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
        Pad(s, 9.5f, 19, 5.6f, 3.6f, -0.55f, 1);
        Pad(s, 22, 19.5f, 5.6f, 3.6f, 0.55f, 2);
        Pad(s, 15.5f, 22.5f, 5.2f, 3.3f, 0.05f, 3);
        Pad(s, 12, 12, 5.4f, 3.5f, -1.0f, 4);
        Pad(s, 20, 12, 5.4f, 3.5f, 1.0f, 5);
        Pad(s, 16, 16.5f, 5, 3.3f, 0.15f, 6);
        Fruit(s, 9, 7.5f);
        Fruit(s, 23.5f, 7.5f);
        Fruit(s, 20.5f, 15.5f);
    }

    /// <summary>A prickly-pear fruit: a rounded three-pixel berry, lit north-west and shaded south-east; one pixel at 16 px.</summary>
    private static void Fruit(Layers s, float x, float y)
    {
        var c = s.Canvas;
        if (!s.Fine)
        {
            c.Dot(x, y, Berry.Base);
            return;
        }
        c.Dot(x, y - 1, Berry.Light);
        c.Dot(x - 1, y, Berry.Light);
        c.Dot(x, y, Berry.Base);
        c.Dot(x + 1, y, Berry.Base);
        c.Dot(x, y + 1, Berry.Base);
        c.Dot(x + 1, y + 1, Berry.Shade);
    }

    /// <summary>
    /// One prickly-pear pad: an oval at an angle with a crease against the
    /// pads already drawn, a one-pixel shade rim on its south-east, a lit patch
    /// toward the north-west, and areoles as faint dots across it.
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
        // Areoles on a loose diamond grid; about one in five is left out so no
        // two pads match, and the one toward the light carries a pale spine.
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
    /// clean ramp-step zones, a slightly scalloped rim, and at 32 px a dark
    /// groove between each pair of ribs with an areole on every rib crest.
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
    /// A horizontal cactus arm seen from above: a tube with rounded ends, lit
    /// along its north-west side and shaded along its south-east side.
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
    /// A sprite in two layers: the opaque body, outlined when composed, over a
    /// shadow held as one mask so overlapping shadow ellipses never darken twice.
    /// </summary>
    private sealed class Layers
    {
        private readonly bool[] shadow;

        public Layers(int size)
        {
            Size = size;
            Unit = size / 32f;
            Body = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            Body.Fill(Colors.Transparent);
            Canvas = new PixelCanvas(Body, new Rect2I(0, 0, size, size), Unit);
            shadow = new bool[size * size];
        }

        public Image Body { get; }
        public PixelCanvas Canvas { get; }
        public float Unit { get; }
        public int Size { get; }

        /// <summary>True at 32 px, where single-pixel details are worth drawing.</summary>
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

        /// <summary>
        /// Writes the finished sprite into its atlas cell: the body, a one-pixel
        /// outline in the edge step round it, and the shadow under the rest.
        /// The shadow is snapped to an 8-bit step, because Godot stores
        /// channels by truncating where the reviewed drawing rounded.
        /// </summary>
        public void Compose(Image atlas, Rect2I cell, Color edge)
        {
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && Body.GetPixel(x, y).A > 0.5f;
            var shade = PixelArt.Snap(CactusSprites.Shadow);
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var body = Body.GetPixel(x, y);
                    var color = body.A > 0 ? body
                        : Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1) ? edge
                        : shadow[y * Size + x] ? shade
                        : (Color?)null;
                    if (color is { } value) atlas.SetPixel(cell.Position.X + x, cell.Position.Y + y, value);
                }
        }
    }
}
