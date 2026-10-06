using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved rowing boat, with shipped or working oars in eight directions.</summary>
public static class BoatSprites
{
    private static readonly Dictionary<(int Facing, bool Rowing), Texture2D> Textures = [];
    private static readonly Facing[] Facings =
        [Facing.South, Facing.SouthWest, Facing.West, Facing.NorthWest, Facing.North, Facing.NorthEast, Facing.East, Facing.SouthEast];
    private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
    private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
    private static readonly Ramp River = Ramp.Of("2F5A75", "3B7294", "4786AB", "5695B8", "7FB4CF");
    private static readonly Color SmallShadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    public static Image Sprite(int facing, bool rowing) => Boat(Facings[Normalize(facing)], rowing);

    public static Texture2D Texture(int facing, bool rowing)
    {
        var key = (Normalize(facing), rowing);
        if (!Textures.TryGetValue(key, out var texture))
        {
            texture = ImageTexture.CreateFromImage(Sprite(key.Item1, rowing));
            Textures.Add(key, texture);
        }
        return texture;
    }

    private static int Normalize(int facing) => (facing % AgentSprites.FacingCount + AgentSprites.FacingCount) % AgentSprites.FacingCount;

    private enum Facing { South, East, North, West, SouthEast, NorthEast, NorthWest, SouthWest }

    private static bool IsDiagonal(Facing facing) => facing >= Facing.SouthEast;

    private static Color Toward(Ramp r, int step, int toward, float amount) => r[step].Lerp(r[toward], amount);

    private readonly record struct Heading(Facing Facing)
    {
        private const float Centre = 16;
        private const float Half = 0.70710678f;

        /// <summary>The unit vector the vehicle points along, in tile pixels (y down).</summary>
        public Vector2 Forward => Facing switch
        {
            Facing.East => new Vector2(1, 0),
            Facing.West => new Vector2(-1, 0),
            Facing.South => new Vector2(0, 1),
            Facing.North => new Vector2(0, -1),
            Facing.SouthEast => new Vector2(Half, Half),
            Facing.NorthEast => new Vector2(Half, -Half),
            Facing.NorthWest => new Vector2(-Half, -Half),
            _ => new Vector2(-Half, Half),
        };

        /// <summary>The unit vector to the vehicle's right: a quarter turn clockwise from <see cref="Forward"/>.</summary>
        public Vector2 Right => new(-Forward.Y, Forward.X);

        /// <summary>A point of the frame in tile pixels.</summary>
        public Vector2 Map(float u, float v) => Facing switch
        {
            Facing.East => new Vector2(Centre + u, Centre + v),
            Facing.West => new Vector2(Centre - u, Centre - v),
            Facing.South => new Vector2(Centre - v, Centre + u),
            Facing.North => new Vector2(Centre + v, Centre - u),
            _ => new Vector2(Centre, Centre) + Forward * u + Right * v,
        };

        /// <summary>A tile pixel position in the frame (the inverse of <see cref="Map"/>).</summary>
        public Vector2 Local(float x, float y) => Facing switch
        {
            Facing.East => new Vector2(x - Centre, y - Centre),
            Facing.West => new Vector2(Centre - x, Centre - y),
            Facing.South => new Vector2(y - Centre, Centre - x),
            Facing.North => new Vector2(Centre - y, x - Centre),
            _ => new Vector2(new Vector2(x - Centre, y - Centre).Dot(Forward), new Vector2(x - Centre, y - Centre).Dot(Right)),
        };

        /// <summary>
        /// The diagonal lattice of a 45° facing: steps along and across the
        /// vehicle in units of half a pixel's diagonal, so a pixel centre always
        /// lands on whole numbers and seams and bands fall on clean 45° stairs.
        /// </summary>
        public (int Along, int Across) Lattice(int x, int y)
        {
            var local = Local(x + 0.5f, y + 0.5f);
            return ((int)MathF.Round(local.X * 1.41421356f), (int)MathF.Round(local.Y * 1.41421356f));
        }

        /// <summary>A direction of the frame in tile pixels.</summary>
        public Vector2 Direction(float u, float v) => Map(u, v) - Map(0, 0);

        /// <summary>The pixel rectangle covering [u, u + du) × [v, v + dv) of the frame.</summary>
        public Rect2I Box(float u, float v, float du, float dv)
        {
            var a = Map(u, v);
            var b = Map(u + du, v + dv);
            var left = (int)MathF.Round(MathF.Min(a.X, b.X));
            var top = (int)MathF.Round(MathF.Min(a.Y, b.Y));
            return new Rect2I(left, top, (int)MathF.Round(MathF.Max(a.X, b.X)) - left, (int)MathF.Round(MathF.Max(a.Y, b.Y)) - top);
        }
    }

    private sealed class ShadowMask(int width, int height)
    {
        private readonly bool[] covered = new bool[width * height];

        public void Add(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < width && y < height) covered[y * width + x] = true;
        }

        public void Add(Rect2I area, int dx, int dy)
        {
            for (var y = area.Position.Y; y < area.End.Y; y++)
                for (var x = area.Position.X; x < area.End.X; x++)
                    Add(x + dx, y + dy);
        }

        public void Paint(Plate p, Color color)
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (covered[y * width + x]) p.Put(x, y, color);
        }
    }

    private sealed class Mask
    {
        private const int Size = 32;
        private readonly bool[] on = new bool[Size * Size];

        public Mask(Func<int, int, bool> covers)
        {
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    on[y * Size + x] = covers(x, y);
        }

        /// <summary>The pixels of a part written in the vehicle's frame (u forward, v to its right).</summary>
        public static Mask Of(Heading f, Func<float, float, bool> inside) => new((x, y) =>
        {
            var local = f.Local(x + 0.5f, y + 0.5f);
            return inside(local.X, local.Y);
        });

        public bool this[int x, int y] => x >= 0 && y >= 0 && x < Size && y < Size && on[y * Size + x];

        /// <summary>Every covered pixel, row by row.</summary>
        public IEnumerable<(int X, int Y)> Pixels()
        {
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (on[y * Size + x]) yield return (x, y);
        }

        /// <summary>Whether a covered pixel has an open pixel above, below or beside it.</summary>
        public bool Rim(int x, int y) => this[x, y] && (!this[x - 1, y] || !this[x + 1, y] || !this[x, y - 1] || !this[x, y + 1]);

        /// <summary>The part less its outer ring.</summary>
        public Mask Inner() => new((x, y) => this[x, y] && !Rim(x, y));

        /// <summary>The part and another together.</summary>
        public Mask Or(Mask other) => new((x, y) => this[x, y] || other[x, y]);

        /// <summary>
        /// How squarely a ring pixel faces the north-west light, from its open
        /// sides: 1 facing it, −1 facing away, 0 side-on (or not on the ring).
        /// </summary>
        public float Light(int x, int y)
        {
            float nx = 0, ny = 0;
            if (!this[x - 1, y]) nx -= 1;
            if (!this[x + 1, y]) nx += 1;
            if (!this[x, y - 1]) ny -= 1;
            if (!this[x, y + 1]) ny += 1;
            var length = MathF.Sqrt(nx * nx + ny * ny);
            return length == 0 ? 0 : -(nx + ny) / (length * 1.41421356f);
        }

        /// <summary>
        /// Whether a ring pixel takes the lit tone of a two-tone part: it faces
        /// the light, or it is side-on and open to the north (the way the
        /// cardinal sprites light the north row of an east-west pole).
        /// </summary>
        public bool Lit(int x, int y)
        {
            var light = Light(x, y);
            return light > 0.3f || (light > -0.3f && !this[x, y - 1]);
        }
    }

    private static float HalfBeam(float u)
    {
        const float half = 14;
        var t = (u + half) / (half * 2);
        if (t < 0 || t > 1) return -1;
        if (t < 0.36f)
        {
            var s = (0.36f - t) / 0.36f;
            return 4.4f + 1.8f * MathF.Sqrt(1 - s * s);
        }
        var bow = (t - 0.36f) / 0.64f;
        return 6.2f * (1 - MathF.Pow(bow, 2.3f));
    }

    private static Image Boat(Facing facing, bool oarsOut)
    {
        if (IsDiagonal(facing)) return BoatDiagonal(facing, oarsOut);
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        // How far a pixel lies inside the hull's edge (negative outside), and its place in the frame.
        (float Depth, Vector2 Local) Inside(int x, int y)
        {
            var local = f.Local(x + 0.5f, y + 0.5f);
            var beam = HalfBeam(local.X);
            if (beam < 0) return (-9, local);
            return (MathF.Min(beam - MathF.Abs(local.Y), local.X + 14), local);
        }
        bool Thwart(float u) => u is >= -6 and < -4 or >= 3 and < 5;
        var light = new Vector2(-0.7071f, -0.7071f);

        // L2 shadow on the water, and a broken ripple of light where the hull meets it.
        var shadow = new ShadowMask(32, 32);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
                if (Inside(x, y).Depth > 0) shadow.Add(x + 2, y + 3);
        shadow.Paint(p, SmallShadow);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var (depth, _) = Inside(x, y);
                if (depth is <= -1.3f or > 0) continue;
                if (PixelArt.Hash(x, y, 233) % 2 == 0) continue;
                p.Put(x, y, River.Highlight with { A = 0.45f });
            }

        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var (depth, local) = Inside(x, y);
                if (depth <= 0) continue;
                var (u, v) = (local.X, local.Y);
                // Which way this part of the hull faces, for the north-west light.
                var sternward = local.X + 14 < HalfBeam(u) - MathF.Abs(v);
                var outward = sternward ? f.Direction(-1, 0) : f.Direction(u > 4 ? 0.8f : 0, MathF.Sign(v));
                var facingLight = outward.Normalized().Dot(light);
                Color c;
                if (depth < 1) c = Timber.Edge;
                else if (depth < 2) c = facingLight > 0.15f ? Timber.Light : facingLight < -0.15f ? Timber.Base : Toward(Timber, 2, 3, 0.5f);
                else if (depth < 2.9f) c = facingLight > 0.15f ? Toward(Timber, 1, 0, 0.55f) : Timber.Base;
                else if (u > 6 || u < -10) c = Timber.Base;
                else if (Thwart(u)) c = Timber.Base;
                else c = MathF.Abs(MathF.Abs(v) - 2) < 0.5f ? Toward(Timber, 1, 0, 0.45f) : Timber.Shade;
                p.Put(x, y, c);
            }
        // Thwarts and the decks: their north and west edges catch the light.
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var (depth, local) = Inside(x, y);
                if (depth < 2.9f) continue;
                var plank = Thwart(local.X) || local.X > 6 || local.X < -10;
                if (!plank) continue;
                bool PlankAt(int px, int py)
                {
                    var (d, l) = Inside(px, py);
                    return d >= 2.9f && (Thwart(l.X) || l.X > 6 || l.X < -10);
                }
                if (!PlankAt(x, y - 1) || !PlankAt(x - 1, y)) p.Put(x, y, Timber.Light);
            }

        if (oarsOut)
            foreach (var side in new[] { -1, 1 })
            {
                var handle = f.Map(3.5f, side * 2.5f);
                var rowlock = f.Map(-0.5f, side * 6.2f);
                var blade = f.Map(-9.5f, side * 10.5f);
                var c = p.Canvas;
                // The blade lies flat on the water with a little ripple round it.
                var (rx, ry) = facing is Facing.East or Facing.West ? (2.6f, 1.4f) : (1.4f, 2.6f);
                c.Ellipse(blade.X + 1, blade.Y + 1.5f, rx, ry, SmallShadow);
                c.Ellipse(blade.X, blade.Y, rx + 0.9f, ry + 0.9f, River.Highlight with { A = 0.35f });
                c.Line(handle.X, handle.Y, rowlock.X, rowlock.Y, Timber.Light);
                c.Line(rowlock.X, rowlock.Y, blade.X, blade.Y, Timber.Light);
                c.Ellipse(blade.X, blade.Y, rx, ry, Timber.Edge);
                c.Ellipse(blade.X - 0.3f, blade.Y - 0.3f, rx - 0.8f, ry - 0.8f, Timber.Light);
                p.Put((int)rowlock.X, (int)rowlock.Y, Iron.Light);
            }
        else
            foreach (var side in new[] { -1, 1 })
            {
                // Shipped oars lie along the floor on the thwarts, blades toward the stern.
                var from = f.Map(-9.5f, side * 2.5f);
                var to = f.Map(6.5f, side * 2.5f);
                p.Canvas.Line(from.X, from.Y, to.X, to.Y, Timber.Light);
                var blade = f.Box(-10, side < 0 ? -3.5f : 1.5f, 4, 2);
                p.Fill(blade, Timber.Light);
                var oarlock = f.Map(-0.5f, side * 5.5f);
                p.Put((int)oarlock.X, (int)oarlock.Y, Iron.Light);
            }
        // An iron ring at the bow for the mooring line.
        var ring = f.Map(9.5f, 0.5f);
        p.Put((int)ring.X, (int)ring.Y, Iron.Light);
        return p.Image;
    }

    private static Image BoatDiagonal(Facing facing, bool oarsOut)
    {
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        // The boat sits one pixel toward its bow, so the oars trailing behind it stay inside the tile; the
        // blades lie a little nearer the hull than on the cardinal boat for the same reason.
        var shift = new Vector2(MathF.Sign(f.Forward.X), MathF.Sign(f.Forward.Y));
        Vector2 Local(float x, float y) => f.Local(x - shift.X, y - shift.Y);
        Vector2 Map(float u, float v) => f.Map(u, v) + shift;
        int Across(int x, int y) => f.Lattice(x - (int)shift.X, y - (int)shift.Y).Across;
        float Depth(float x, float y)
        {
            var local = Local(x, y);
            var beam = HalfBeam(local.X);
            return beam < 0 ? -9 : MathF.Min(beam - MathF.Abs(local.Y), local.X + 14);
        }
        bool Thwart(float u) => u is >= -6 and < -4 or >= 3 and < 5;
        bool Plank(float u) => Thwart(u) || u > 6 || u < -10;
        var light = new Vector2(-0.7071f, -0.7071f);
        var hull = new Mask((x, y) => Depth(x + 0.5f, y + 0.5f) > 0);
        var gunwale = hull.Inner();
        var wall = gunwale.Inner();
        var floor = wall.Inner();

        var shadow = new ShadowMask(32, 32);
        foreach (var (x, y) in hull.Pixels()) shadow.Add(x + 2, y + 3);
        shadow.Paint(p, SmallShadow);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var depth = Depth(x + 0.5f, y + 0.5f);
                if (depth is <= -1.3f or > 0 || hull[x, y]) continue;
                if (PixelArt.Hash(x, y, 233) % 2 == 0) continue;
                p.Put(x, y, River.Highlight with { A = 0.45f });
            }

        foreach (var (x, y) in hull.Pixels())
        {
            var local = Local(x + 0.5f, y + 0.5f);
            var (u, v) = (local.X, local.Y);
            var sternward = u + 14 < HalfBeam(u) - MathF.Abs(v);
            var outward = sternward ? f.Direction(-1, 0) : f.Direction(u > 4 ? 0.8f : 0, MathF.Sign(v));
            var facingLight = outward.Normalized().Dot(light);
            Color c;
            if (!gunwale[x, y]) c = Timber.Edge;
            else if (!wall[x, y]) c = facingLight > 0.15f ? Timber.Light : facingLight < -0.15f ? Timber.Base : Toward(Timber, 2, 3, 0.5f);
            else if (!floor[x, y]) c = facingLight > 0.15f ? Toward(Timber, 1, 0, 0.55f) : Timber.Base;
            else if (Plank(u)) c = Timber.Base;
            else c = Math.Abs(Across(x, y)) == 3 ? Toward(Timber, 1, 0, 0.45f) : Timber.Shade;
            p.Put(x, y, c);
        }
        // Thwarts and decks: their north and west edges catch the light, as on the cardinal boat.
        bool PlankAt(int x, int y) => floor[x, y] && Plank(Local(x + 0.5f, y + 0.5f).X);
        foreach (var (x, y) in floor.Pixels())
            if (PlankAt(x, y) && (!PlankAt(x, y - 1) || !PlankAt(x - 1, y))) p.Put(x, y, Timber.Light);

        if (oarsOut)
            foreach (var side in new[] { -1, 1 })
            {
                var handle = Map(3.5f, side * 2.5f);
                var rowlock = Map(-0.5f, side * 6.2f);
                var blade = Map(-8.8f, side * 9.6f);
                var c = p.Canvas;
                Oval(p, f, blade + new Vector2(1, 1.5f), 2.6f, 1.4f, SmallShadow);
                Oval(p, f, blade, 3.5f, 2.3f, River.Highlight with { A = 0.35f });
                c.Line(handle.X, handle.Y, rowlock.X, rowlock.Y, Timber.Light);
                c.Line(rowlock.X, rowlock.Y, blade.X, blade.Y, Timber.Light);
                Oval(p, f, blade, 2.6f, 1.4f, Timber.Edge);
                Oval(p, f, blade - new Vector2(0.3f, 0.3f), 1.8f, 0.6f, Timber.Light);
                p.Put((int)rowlock.X, (int)rowlock.Y, Iron.Light);
            }
        else
            foreach (var side in new[] { -1, 1 })
            {
                var from = Map(-9.5f, side * 2.5f);
                var to = Map(6.5f, side * 2.5f);
                p.Canvas.Line(from.X, from.Y, to.X, to.Y, Timber.Light);
                var blade = new Mask((x, y) =>
                {
                    var local = Local(x + 0.5f, y + 0.5f);
                    return local.X >= -10 && local.X < -6 && MathF.Abs(local.Y - side * 2.5f) < 1.05f;
                });
                foreach (var (x, y) in blade.Pixels()) p.Put(x, y, Timber.Light);
                var oarlock = Map(-0.5f, side * 5.5f);
                p.Put((int)oarlock.X, (int)oarlock.Y, Iron.Light);
            }
        var ring = Map(9.5f, 0.5f);
        p.Put((int)ring.X, (int)ring.Y, Iron.Light);
        return p.Image;
    }

    private static void Oval(Plate p, Heading f, Vector2 centre, float ru, float rv, Color color)
    {
        for (var y = (int)(centre.Y - ru - 1); y <= (int)(centre.Y + ru + 1); y++)
            for (var x = (int)(centre.X - ru - 1); x <= (int)(centre.X + ru + 1); x++)
            {
                var offset = new Vector2(x + 0.5f - centre.X, y + 0.5f - centre.Y);
                var a = offset.Dot(f.Forward) / ru;
                var b = offset.Dot(f.Right) / rv;
                if (a * a + b * b <= 1) p.Put(x, y, color);
            }
    }

    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));

        /// <summary>Step 0 is the edge, 4 the highlight; out-of-range steps clamp.</summary>
        public Color this[int step] => step switch
        {
            <= 0 => Edge,
            1 => Shade,
            2 => Base,
            3 => Light,
            _ => Highlight,
        };
    }

    private sealed class Plate
    {
        public Plate(int width, int height, float scale)
        {
            Width = width;
            Height = height;
            Scale = scale;
            Image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            Image.Fill(Colors.Transparent);
        }

        public Image Image { get; }
        private int Width { get; }
        private int Height { get; }
        private float Scale { get; }
        public PixelCanvas Canvas => new(Image, new Rect2I(0, 0, Width, Height), Scale, snap: true);

        public void Put(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || color.A <= 0) return;
            Image.SetPixel(x, y, PixelArt.Snap(Image.GetPixel(x, y).Blend(color)));
        }

        public void Fill(Rect2I area, Color color)
        {
            for (var y = area.Position.Y; y < area.End.Y; y++)
                for (var x = area.Position.X; x < area.End.X; x++)
                    Put(x, y, color);
        }

        public void Fill(int x, int y, int width, int height, Color color) => Fill(new Rect2I(x, y, width, height), color);

        public void Disc(float x, float y, float radius, Color color) =>
            Canvas.Disc(x / Scale, y / Scale, radius / Scale, color);
    }
}
