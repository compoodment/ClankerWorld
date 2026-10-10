using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The eighteen approved 32 px handcart drawings from the building art review:
/// empty and loaded in eight directions, plus loaded carts being pulled east
/// and south-east, and their approved 16 px versions for medium zoom (#914),
/// halved from them. The independent proposal remains the pixel reference.
/// </summary>
public static class HandcartSprites
{
    private static readonly Dictionary<(int Facing, bool Loaded, bool Pulled, int Size), Texture2D> Textures = [];
    private static readonly Facing[] Facings =
        [Facing.South, Facing.SouthWest, Facing.West, Facing.NorthWest, Facing.North, Facing.NorthEast, Facing.East, Facing.SouthEast];
    private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
    private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
    private static readonly Ramp Cloth = Ramp.Of("75674D", "A09170", "CABC99", "E8DCC0", "FFF5DF");
    private static readonly Color SmallShadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    /// <summary>
    /// Facings use <see cref="AgentSprites"/> order. Unsupported pulled poses use the parked drawing.
    /// Sizes below 24 px get the approved 16 px drawing.
    /// </summary>
    public static Image Sprite(int facing, bool loaded, bool pulled, int size = 32)
    {
        var pose = Normalize(facing, loaded, pulled);
        var approved = Handcart(Facings[pose.Facing], pose.Loaded, pose.Pulled);
        return size >= 24 ? approved : PixelArt.HalveSprite(approved);
    }

    /// <summary>One cached texture for each approved drawing, at 32 px or 16 px.</summary>
    public static Texture2D Texture(int facing, bool loaded, bool pulled, int size = 32)
    {
        var pose = Normalize(facing, loaded, pulled);
        var key = (pose.Facing, pose.Loaded, pose.Pulled, size >= 24 ? 32 : 16);
        if (Textures.TryGetValue(key, out var texture)) return texture;
        texture = ImageTexture.CreateFromImage(Sprite(pose.Facing, pose.Loaded, pose.Pulled, key.Item4));
        Textures.Add(key, texture);
        return texture;
    }

    private static (int Facing, bool Loaded, bool Pulled) Normalize(int facing, bool loaded, bool pulled)
    {
        facing = (facing % AgentSprites.FacingCount + AgentSprites.FacingCount) % AgentSprites.FacingCount;
        return (facing, loaded, pulled && loaded && facing is 6 or 7);
    }

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

    private static Image Handcart(Facing facing, bool loaded, bool pulled)
    {
        if (IsDiagonal(facing)) return HandcartDiagonal(facing, loaded, pulled);
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        var bed = f.Box(-13, -6, 16, 12);
        Rect2I[] wheels = [f.Box(-10, -10, 10, 4), f.Box(-10, 6, 10, 4)];
        Rect2I[] hubs = [f.Box(-6, -11, 2, 1), f.Box(-6, 10, 2, 1)];
        var tip = pulled ? 10 : 12;
        Rect2I[] shafts = [f.Box(-1, -5, tip + 1, 2), f.Box(-1, 3, tip + 1, 2)];

        // L2 shadow in one pass. Lifted shafts throw theirs further from them.
        var shadow = new ShadowMask(32, 32);
        shadow.Add(bed, 1, 2);
        foreach (var wheel in wheels) shadow.Add(wheel, 1, 2);
        var (liftX, liftY) = pulled ? (3, 5) : (1, 1);
        foreach (var shaft in shafts) shadow.Add(shaft, liftX, liftY);
        shadow.Paint(p, SmallShadow);

        foreach (var shaft in shafts) Pole(p, shaft, Timber);
        // Rope wrapped round the last three pixels of each shaft, for the puller's hands.
        foreach (var v in new[] { -5, 3 })
            for (var u = tip - 3; u < tip; u++)
                p.Fill(f.Box(u, v, 1, 2), (u - tip) % 2 == 0 ? Timber.Shade : Timber.Highlight);
        foreach (var wheel in wheels) Tyre(p, wheel);
        foreach (var hub in hubs) p.Fill(hub, Iron.Shade);
        CartBed(p, bed, facing is Facing.East or Facing.West);
        if (loaded) CartLoad(p, f, facing);
        else
        {
            // A coil of rope in the back corner, ready to lash a load.
            var coil = f.Map(-6.5f, 0.5f);
            RopeCoil(p, coil.X, coil.Y);
        }
        return p.Image;
    }

    private static void Pole(Plate p, Rect2I pole, Ramp ramp)
    {
        p.Fill(pole, ramp.Edge);
        if (pole.Size.X >= pole.Size.Y) p.Fill(pole.Position.X, pole.Position.Y, pole.Size.X, 1, ramp.Light);
        else p.Fill(pole.Position.X, pole.Position.Y, 1, pole.Size.Y, ramp.Light);
    }

    private static void Tyre(Plate p, Rect2I tyre)
    {
        int left = tyre.Position.X, top = tyre.Position.Y, right = tyre.End.X - 1, bottom = tyre.End.Y - 1;
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if ((x == left || x == right) == false || (y != top && y != bottom))
                    p.Put(x, y, Iron.Edge);
        int ix = left + 1, iy = top + 1, w = tyre.Size.X - 2, h = tyre.Size.Y - 2;
        if (w <= 0 || h <= 0) return;
        var flat = w >= h;
        p.Fill(ix, iy, w, h, Iron.Base);
        if (flat) p.Fill(ix, iy, w, 1, Iron.Light);
        else p.Fill(ix, iy, 1, h, Iron.Light);
        p.Put(ix + (flat ? 2 : 0), iy + (flat ? 0 : 2), Iron.Highlight);
        if (flat)
        {
            p.Fill(ix, iy, 1, h, Iron.Shade);
            p.Fill(ix + w - 1, iy, 1, h, Iron.Shade);
        }
        else
        {
            p.Fill(ix, iy, w, 1, Iron.Shade);
            p.Fill(ix, iy + h - 1, w, 1, Iron.Shade);
        }
    }

    private static void CartBed(Plate p, Rect2I bed, bool eastWest)
    {
        p.Fill(bed, Timber.Edge);
        int x = bed.Position.X + 1, y = bed.Position.Y + 1, w = bed.Size.X - 2, h = bed.Size.Y - 2;
        p.Fill(x, y, w, h, Timber.Base);
        p.Fill(x, y + h - 1, w, 1, Timber.Shade);
        p.Fill(x + w - 1, y, 1, h, Timber.Shade);
        p.Fill(x, y, w, 1, Timber.Light);
        p.Fill(x, y, 1, h, Timber.Light);
        // Floor: planks laid across the cart inside the boards; the north and west boards shade the planks beside them.
        int fx = x + 1, fy = y + 1, fw = w - 2, fh = h - 2;
        var seam = Toward(Timber, 2, 1, 0.65f);
        if (eastWest)
            for (var column = 3; column < fw - 1; column += 4) p.Fill(fx + column, fy, 1, fh, seam);
        else
            for (var row = 3; row < fh - 1; row += 4) p.Fill(fx, fy + row, fw, 1, seam);
        p.Fill(fx, fy, fw, 1, Toward(Timber, 2, 1, 0.5f));
        p.Fill(fx, fy, 1, fh, Toward(Timber, 2, 1, 0.5f));
        // Iron corner fittings, lit at the north-west corner.
        p.Put(x, y, Iron.Highlight);
        p.Put(x + w - 1, y, Iron.Light);
        p.Put(x, y + h - 1, Iron.Light);
        p.Put(x + w - 1, y + h - 1, Iron.Base);
    }

    private static void CartLoad(Plate p, Heading f, Facing facing)
    {
        Log(p, f, -15, -6, 12, 5);
        Log(p, f, -15, 1, 12, 5);
        Log(p, f, -14, -3, 10, 6);
        p.Fill(f.Box(-8, -6, 1, 12), Timber.Light);
        foreach (var v in new[] { -6, 5 }) p.Fill(f.Box(-8, v, 1, 1), Timber.Edge);
        var sack = f.Map(-0.5f, 0.5f);
        LyingSack(p, sack.X, sack.Y, alongX: facing is Facing.North or Facing.South);
    }

    private static void Log(Plate p, Heading f, int u, int v, int length, int width)
    {
        var box = f.Box(u, v, length, width);
        p.Fill(box, Timber.Edge);
        int x = box.Position.X + 1, y = box.Position.Y + 1, w = box.Size.X - 2, h = box.Size.Y - 2;
        var flat = w >= h;
        p.Fill(x, y, w, h, Timber.Shade);
        if (flat) p.Fill(x, y, w, 1, Timber.Base);
        else p.Fill(x, y, 1, h, Timber.Base);
        if (flat && h > 2) p.Fill(x, y + h - 1, w, 1, Toward(Timber, 1, 0, 0.45f));
        if (!flat && w > 2) p.Fill(x + w - 1, y, 1, h, Toward(Timber, 1, 0, 0.45f));
        // End grain: the back pixel row of the interior, with the heart in the middle.
        p.Fill(f.Box(u + 1, v + 1, 1, width - 2), Timber.Highlight);
        p.Fill(f.Box(u + 1, v + width / 2, 1, 1), Timber.Light);
    }

    private static void RopeCoil(Plate p, float cx, float cy)
    {
        p.Disc(cx + 0.6f, cy + 0.8f, 3.2f, SmallShadow);
        p.Disc(cx, cy, 3.2f, Timber.Shade);
        p.Disc(cx, cy, 2.5f, Timber.Highlight);
        p.Disc(cx + 0.3f, cy + 0.3f, 1.6f, Timber.Shade);
        p.Disc(cx, cy, 1.0f, Timber.Light);
        var x = (int)cx;
        var y = (int)cy;
        p.Put(x, y, Timber.Edge);
        p.Put(x - 2, y - 1, Cloth.Light);
        p.Put(x + 2, y + 2, Timber.Light);
    }

    private static void LyingSack(Plate p, float cx, float cy, bool alongX)
    {
        var c = p.Canvas;
        var (rx, ry) = alongX ? (3.5f, 2.5f) : (2.5f, 3.5f);
        c.Ellipse(cx + 1, cy + 1.5f, rx, ry, SmallShadow);
        c.Ellipse(cx, cy, rx, ry, Cloth.Edge);
        c.Ellipse(cx + 0.4f, cy + 0.4f, rx - 0.9f, ry - 0.9f, Cloth.Shade);
        c.Ellipse(cx - 0.3f, cy - 0.3f, rx - 1.2f, ry - 1.2f, Cloth.Base);
        c.Ellipse(cx - 1, cy - 1, 0.9f, 0.9f, Cloth.Light);
        // A fold across the body where the cloth sags, and the neck tied off at the west or north end.
        var (x, y) = ((int)cx, (int)cy);
        if (alongX) p.Put(x + 1, y, Cloth.Shade);
        else p.Put(x, y + 1, Cloth.Shade);
        if (alongX)
        {
            var end = (int)(cx - rx);
            p.Put(end, y, Timber.Shade);
            p.Put(end - 1, y, Cloth.Light);
            p.Put(end - 2, y - 1, Cloth.Edge);
            p.Put(end - 2, y, Cloth.Base);
            p.Put(end - 2, y + 1, Cloth.Edge);
        }
        else
        {
            var end = (int)(cy - ry);
            p.Put(x, end, Timber.Shade);
            p.Put(x, end - 1, Cloth.Light);
            p.Put(x - 1, end - 2, Cloth.Edge);
            p.Put(x, end - 2, Cloth.Base);
            p.Put(x + 1, end - 2, Cloth.Edge);
        }
    }

    private static Image HandcartDiagonal(Facing facing, bool loaded, bool pulled)
    {
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        var tip = pulled ? 10f : 12f;
        var bed = Mask.Of(f, (u, v) => u >= -13 && u < 3 && MathF.Abs(v) < 6);
        // Three pixels a row, as heavy as the cardinal two-pixel shafts, four pixels either side of the middle.
        var shafts = new[] { -4f, 4f }.Select(side => Mask.Of(f, (u, v) => u >= -1 && u < tip && MathF.Abs(v - side) < 1.05f)).ToArray();
        var wheels = new[] { -8f, 8f }.Select(side => Mask.Of(f, (u, v) => Stadium(u, v, -8, -2, side) <= 2.05f)).ToArray();
        var hubs = new[] { -10.5f, 10.5f }.Select(side => Mask.Of(f, (u, v) => MathF.Abs(u + 5) < 1.05f && MathF.Abs(v - side) < 0.75f)).ToArray();

        // L2 shadow in one pass, as the cardinal cart; lifted shafts throw theirs further from them.
        var shadow = new ShadowMask(32, 32);
        foreach (var (x, y) in bed.Pixels()) shadow.Add(x + 1, y + 2);
        foreach (var wheel in wheels)
            foreach (var (x, y) in wheel.Pixels()) shadow.Add(x + 1, y + 2);
        var (liftX, liftY) = pulled ? (3, 5) : (1, 1);
        foreach (var shaft in shafts)
            foreach (var (x, y) in shaft.Pixels()) shadow.Add(x + liftX, y + liftY);
        shadow.Paint(p, SmallShadow);

        // Shafts: lit on top and the edge step along the side away from the light, then rope round the
        // last three pixels, wound in alternating turns.
        foreach (var shaft in shafts)
            foreach (var (x, y) in shaft.Pixels())
            {
                var local = f.Local(x + 0.5f, y + 0.5f);
                Color c = shaft.Rim(x, y) && !shaft.Lit(x, y) ? Timber.Edge : Timber.Light;
                if (local.X >= tip - 3) c = f.Lattice(x, y).Along % 2 == 0 ? Timber.Shade : Timber.Highlight;
                p.Put(x, y, c);
            }
        foreach (var wheel in wheels) TyreDiagonal(p, f, wheel);
        foreach (var hub in hubs)
            foreach (var (x, y) in hub.Pixels()) p.Put(x, y, Iron.Shade);
        CartBedDiagonal(p, f, bed);
        if (loaded) CartLoadDiagonal(p, f);
        else
        {
            var coil = f.Map(-6.5f, 0.5f);
            RopeCoil(p, coil.X, coil.Y);
        }
        return p.Image;
    }

    private static float Stadium(float u, float v, float from, float to, float at)
    {
        var along = Math.Clamp(u, from, to);
        return MathF.Sqrt((u - along) * (u - along) + (v - at) * (v - at));
    }

    private static void TyreDiagonal(Plate p, Heading f, Mask tyre)
    {
        var tread = tyre.Inner();
        (int X, int Y)? glint = null;
        foreach (var (x, y) in tyre.Pixels())
        {
            if (!tread[x, y])
            {
                p.Put(x, y, Iron.Edge);
                continue;
            }
            var u = f.Local(x + 0.5f, y + 0.5f).X;
            var lit = tread.Lit(x, y);
            p.Put(x, y, u < -8.6f || u > -1.4f ? Iron.Shade : lit ? Iron.Light : Iron.Base);
            if (lit && u is > -7.5f and < -2.5f && (glint is null || x + y < glint.Value.X + glint.Value.Y)) glint = (x, y);
        }
        if (glint is { } g) p.Put(g.X, g.Y, Iron.Highlight);
    }

    private static void CartBedDiagonal(Plate p, Heading f, Mask bed)
    {
        var boards = bed.Inner();
        var floor = boards.Inner();
        var seam = Toward(Timber, 2, 1, 0.65f);
        foreach (var (x, y) in bed.Pixels())
        {
            Color c;
            if (!boards[x, y]) c = Timber.Edge;
            else if (!floor[x, y])
            {
                var light = boards.Light(x, y);
                c = light > 0.3f ? Timber.Light : light < -0.3f ? Timber.Shade : Toward(Timber, 2, 3, 0.5f);
            }
            else if (floor.Light(x, y) > 0.3f) c = Toward(Timber, 2, 1, 0.5f);
            else c = f.Lattice(x, y).Along is -11 or -5 ? seam : Timber.Base;
            p.Put(x, y, c);
        }
        // Iron corner fittings on the boards: the pixel furthest into each corner, the one nearest the light brightest.
        var corners = new List<(int X, int Y)>();
        foreach (var (cu, cv) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            (int X, int Y) best = (0, 0);
            var reach = float.MinValue;
            foreach (var (x, y) in boards.Pixels())
            {
                if (floor[x, y]) continue;
                var local = f.Local(x + 0.5f, y + 0.5f);
                var score = cu * local.X + cv * local.Y;
                if (score > reach) (reach, best) = (score, (x, y));
            }
            corners.Add(best);
        }
        var ordered = corners.OrderBy(c => c.X + c.Y).ToList();
        for (var i = 0; i < ordered.Count; i++)
            p.Put(ordered[i].X, ordered[i].Y, i == 0 ? Iron.Highlight : i == ordered.Count - 1 ? Iron.Base : Iron.Light);
    }

    private static void CartLoadDiagonal(Plate p, Heading f)
    {
        // The top log's cut end lines up with the others here: set one pixel in, as on the cardinal cart,
        // its end would turn into a notch on the 45° stairs.
        var logs = new[]
        {
            Mask.Of(f, (u, v) => u >= -15 && u < -3 && v >= -6 && v < -1),
            Mask.Of(f, (u, v) => u >= -15 && u < -3 && v >= 1 && v < 6),
            Mask.Of(f, (u, v) => u >= -15 && u < -4 && v >= -3 && v < 3),
        };
        foreach (var log in logs)
        {
            var wood = log.Inner();
            var back = wood.Pixels().Min(px => f.Local(px.X + 0.5f, px.Y + 0.5f).X);
            var heart = wood.Pixels().Where(px => f.Local(px.X + 0.5f, px.Y + 0.5f).X < back + 0.8f)
                .Select(px => (px, f.Local(px.X + 0.5f, px.Y + 0.5f).Y)).ToList();
            var middle = heart.Count == 0 ? 0 : heart.Average(h => h.Item2);
            var core = heart.Count == 0 ? (-1, -1) : heart.OrderBy(h => MathF.Abs(h.Item2 - (float)middle)).First().px;
            foreach (var (x, y) in log.Pixels())
            {
                Color c;
                if (!wood[x, y]) c = Timber.Edge;
                else if (f.Local(x + 0.5f, y + 0.5f).X < back + 0.8f) c = (x, y) == core ? Timber.Light : Timber.Highlight;
                else if (wood.Rim(x, y)) c = wood.Lit(x, y) ? Timber.Base : Toward(Timber, 1, 0, 0.45f);
                else c = Timber.Shade;
                p.Put(x, y, c);
            }
        }
        // The lashing: a rope across all three logs, its ends tucked under in the edge step.
        var load = logs[0].Or(logs[1]).Or(logs[2]);
        var rope = load.Pixels().Where(px => MathF.Abs(f.Local(px.X + 0.5f, px.Y + 0.5f).X + 7.5f) < 0.55f).ToList();
        if (rope.Count > 0)
        {
            var across = rope.Select(px => f.Local(px.X + 0.5f, px.Y + 0.5f).Y).ToList();
            foreach (var (x, y) in rope)
            {
                var v = f.Local(x + 0.5f, y + 0.5f).Y;
                p.Put(x, y, v <= across.Min() + 0.1f || v >= across.Max() - 0.1f ? Timber.Edge : Timber.Light);
            }
        }
        SackDiagonal(p, f, -0.5f, 0.5f);
    }

    private static void SackDiagonal(Plate p, Heading f, float cu, float cv)
    {
        // Radii along the cart (u) and across it (v): the sack lies across the bed.
        Mask Body(float ru, float rv, float dx, float dy) => new((x, y) =>
        {
            var local = f.Local(x + 0.5f - dx, y + 0.5f - dy);
            var a = (local.X - cu) / ru;
            var b = (local.Y - cv) / rv;
            return a * a + b * b <= 1;
        });
        var centre = f.Map(cu, cv);
        var ground = new ShadowMask(32, 32);
        foreach (var (x, y) in Body(2.5f, 3.5f, 0, 0).Pixels()) ground.Add(x + 1, y + 1);
        ground.Paint(p, SmallShadow);
        foreach (var (x, y) in Body(2.5f, 3.5f, 0, 0).Pixels()) p.Put(x, y, Cloth.Edge);
        foreach (var (x, y) in Body(1.6f, 2.6f, 0.4f, 0.4f).Pixels()) p.Put(x, y, Cloth.Shade);
        foreach (var (x, y) in Body(1.3f, 2.3f, -0.3f, -0.3f).Pixels()) p.Put(x, y, Cloth.Base);
        p.Put((int)(centre.X - 1), (int)(centre.Y - 1), Cloth.Light);
        var fold = f.Map(cu, cv + 1);
        p.Put((int)fold.X, (int)fold.Y, Cloth.Shade);
        // The neck at the end of the long axis nearer the north-west.
        var ends = new[] { -1f, 1f }.Select(side => (side, at: f.Map(cu, cv + side * 3.5f))).OrderBy(e => e.at.X + e.at.Y).First();
        var outward = f.Right * ends.side;
        void PutAt(Vector2 at, Color c) => p.Put((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), c);
        var tie = f.Map(cu, cv + ends.side * 3.0f);
        PutAt(tie, Timber.Shade);
        PutAt(tie + outward * 1.2f, Cloth.Light);
        var flare = tie + outward * 2.4f;
        PutAt(flare, Cloth.Base);
        PutAt(flare + f.Forward * 1.1f, Cloth.Edge);
        PutAt(flare - f.Forward * 1.1f, Cloth.Edge);
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
