using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// What an agent sprite shows besides its facing: standing still, the two
/// steps of a walk, or carrying, working, talking or hurt.
/// </summary>
public enum AgentFrame
{
    Still = 0,
    Walk1 = 1,
    Walk2 = 2,
    Carry = 3,
    Work = 4,
    Talk = 5,
    Hurt = 6,
}

/// <summary>
/// Agent sprites: friendly people seen straight from above, a round head over
/// a shirt with hands at the shoulders, in eight facings, two walk frames and
/// carry, work, talk and hurt frames, with infant, child, adult and elder looks
/// and six appearance variants without genders. Worn clothing does not change
/// the sprite; the shirt color is part of the appearance variant.
/// <para>
/// The head stays over the shoulders and keeps a small lift toward the top of
/// the screen, so every facing still shows the shirt below the head. Facing is
/// shown only by where things sit: the face and fringe on the facing side of
/// the head, a nose that pokes out of the outline when seen sideways, the
/// shoulders turning across the facing, the hands at the shoulder tips with the
/// near one reaching forward, and the feet poking out ahead. Light comes from
/// the north-west on hair and shirt whatever the facing.
/// </para>
/// This is the drawing the owner approved on 2026-10-01 in the agent art
/// review, kept pixel for pixel. Everything is deterministic and uses the
/// game's skin, hair and shirt palettes with the 1E2226 outline.
/// </summary>
public static class AgentSprites
{
    private static readonly Dictionary<(int Side, int Frame), IReadOnlyList<(Vector2I Position, Color Color)>> WaitingFrames = new();

    /// <summary>The eight frames of the approved circling spark, cached as ordered pixel layers.</summary>
    public static IReadOnlyList<(Vector2I Position, Color Color)> WaitingSparkFrame(int side, int frame)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(side, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(side, 78);
        frame = ((frame % 8) + 8) % 8;
        if (WaitingFrames.TryGetValue((side, frame), out var cached)) return cached;
        var pixels = new List<(Vector2I Position, Color Color)>();
        void Pixel(int x, int y, Color color) => pixels.Add((new Vector2I(x, y), color));
        var Ink = new Color("1E1712");
        var Gold = new Color("F2C14E");
        var Spark = new Color("FFF6D0");
        var center = new Vector2(side / 2f, side / 2f - (side * 1.35f) * 0.33f);
        var radius = new Vector2((side * 1.35f) * 0.24f, (side * 1.35f) * 0.08f);
        Vector2 At(float step) => center + new Vector2(MathF.Cos(step / 8f * MathF.Tau) * radius.X, MathF.Sin(step / 8f * MathF.Tau) * radius.Y);
        for (var trail = 2; trail >= 1; trail--)
        {
            var point = At(frame - trail * 0.5f);
            Pixel((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), Gold with { A = 0.6f / trail });
        }
        var spark = At(frame);
        var behind = MathF.Sin(frame / 8f * MathF.Tau) < 0;
        var strength = behind ? 0.7f : 1f;
        var x = (int)MathF.Floor(spark.X);
        var y = (int)MathF.Floor(spark.Y);
        // A plus-shaped spark with a dark rim, so it reads on grass, road or roof.
        var arm = side >= 24 ? 2 : 1;
        for (var dy = -arm - 1; dy <= arm + 1; dy++)
            for (var dx = -arm - 1; dx <= arm + 1; dx++)
            {
                var onPlus = (dx == 0 && Math.Abs(dy) <= arm) || (dy == 0 && Math.Abs(dx) <= arm);
                var nearPlus = !onPlus && (Math.Abs(dx) <= 1 && Math.Abs(dy) <= arm || Math.Abs(dy) <= 1 && Math.Abs(dx) <= arm) &&
                    Math.Abs(dx) + Math.Abs(dy) <= arm + 1;
                if (nearPlus) Pixel(x + dx, y + dy, Ink with { A = 0.75f * strength });
            }
        for (var d = -arm; d <= arm; d++)
        {
            var tip = Math.Abs(d) == arm && arm > 1;
            Pixel(x + d, y, (d == 0 ? Spark : tip ? Gold with { A = 0.8f } : Gold) with { A = strength });
            if (d != 0) Pixel(x, y + d, (tip ? Gold with { A = 0.8f } : Gold) with { A = strength });
        }
        return WaitingFrames[(side, frame)] = pixels.AsReadOnly();
    }

    public const int VariantCount = 6;
    public const int StageCount = 4;

    /// <summary>Facings in the game's order: 0 S, 1 SW, 2 W, 3 NW, 4 N, 5 NE, 6 E, 7 SE.</summary>
    public const int FacingCount = 8;

    /// <summary>Facing south, toward the bottom of the screen: the default when nothing says otherwise.</summary>
    public const int South = 0;

    public const int FrameCount = 7;

    // Sprite sheets per drawn size (32 or 16): one of the south-facing stills
    // of every variant and stage, and one per variant and stage holding every
    // facing and frame. A cell is drawn the first time something asks for it,
    // because one sprite takes a few milliseconds to draw in the engine.
    private static readonly Dictionary<int, Sheet> StillSheets = [];
    private static readonly Dictionary<(int Size, int Variant, int Stage), Sheet> PoseSheets = [];

    private static readonly Color[] Shirts =
    [
        new("3F6FA8"), new("B0523E"), new("4E8A5A"), new("C19A3A"), new("7A5A9E"), new("3E8C8C"),
    ];
    private static readonly Color[] Skins =
    [
        new("F0C8A0"), new("C99A6E"), new("8D5E3C"), new("E2B48A"), new("5E3B24"), new("B8845A"),
    ];
    private static readonly Color[] Hairs =
    [
        new("3A2A1C"), new("6B4226"), new("1E1A18"), new("A8742E"), new("2E2420"), new("7A3A22"),
    ];
    private static readonly Color ElderHair = new("D9D6CF");
    private static readonly Color Outline = new("1E2226");
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);
    private static readonly Color TimberEdge = new("3F2A1A");
    private static readonly Color TimberShade = new("6E4E31");
    private static readonly Color TimberBase = new("8A6440");
    private static readonly Color TimberLight = new("A77C52");
    private static readonly Color TimberHighlight = new("D2AC77");
    private static readonly Color ClothShade = new("CABC99");
    private static readonly Color ClothLight = new("E8DCC0");
    private static readonly Color ClothHighlight = new("FFF5DF");
    private static readonly Color BlanketBase = new("E9E1CF");
    private static readonly Color BlanketFold = new("CFC5AE");
    private static readonly Color IronShade = new("524C48");
    private static readonly Color IronBase = new("6C6560");
    private static readonly Color IronHighlight = new("A69E98");
    // Dark trousers show only mid-step, between the shirt and a boot.
    private static readonly Color Trousers = new("4A4E55");

    /// <summary>Screen direction the light comes from (north-west), as a unit vector.</summary>
    private static readonly Vector2 TowardLight = new Vector2(-1, -1).Normalized();

    /// <summary>Stable appearance variant for an agent ID, so the same person always looks the same.</summary>
    public static int VariantFor(string agentId) => (int)(PixelArt.Hash(agentId.Length, StableCode(agentId), 5) % VariantCount);

    public static int StageIndex(string? ageBand) => ageBand switch
    {
        "infant" => 0,
        "child" => 1,
        "elder" => 3,
        _ => 2,
    };

    /// <summary>
    /// The facing nearest to a move of <paramref name="dx"/> tiles east and
    /// <paramref name="dy"/> tiles south; no move faces south.
    /// </summary>
    public static int FacingToward(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return South;
        // Facing k points at the screen angle 90° + 45°·k (y down), so south is 0 and west is 2.
        var steps = (int)Math.Round((Math.Atan2(dy, dx) - Math.PI / 2) / (Math.PI / 4));
        return Wrap(steps, FacingCount);
    }

    /// <summary>
    /// The south-facing stills of every variant (columns) and life stage
    /// (rows); <see cref="Region"/> draws each one the first time it is asked for.
    /// </summary>
    public static ImageTexture Atlas(int size) => StillSheet(CellSize(size)).Texture;

    /// <summary>Where one variant and life stage sits in <see cref="Atlas"/>, drawn there first if needed.</summary>
    public static Rect2 Region(int variant, int stage, int size)
    {
        size = CellSize(size);
        DrawnStill(variant, stage, size);
        return StillCell(variant, stage, size);
    }

    /// <summary>One variant and life stage standing still and facing south.</summary>
    public static Image Sprite(int variant, int stage, int size)
    {
        size = CellSize(size);
        return DrawnStill(variant, stage, size).Image.GetRegion(StillCell(variant, stage, size));
    }

    /// <summary>
    /// The sheet of every facing (rows) and frame (columns) of one variant and
    /// life stage, with this facing and frame drawn on it.
    /// </summary>
    public static ImageTexture PoseAtlas(int variant, int stage, int facing, AgentFrame frame, int size) =>
        DrawnPose(variant, stage, facing, frame, CellSize(size)).Texture;

    /// <summary>Where one facing and frame sits in <see cref="PoseAtlas"/>.</summary>
    public static Rect2 PoseRegion(int facing, AgentFrame frame, int size) => PoseCell(facing, frame, CellSize(size));

    /// <summary>One variant and life stage in any facing and frame.</summary>
    public static Image Sprite(int variant, int stage, int facing, AgentFrame frame, int size)
    {
        size = CellSize(size);
        return DrawnPose(variant, stage, facing, frame, size).Image.GetRegion(PoseCell(facing, frame, size));
    }

    /// <summary>The drawing has a 32 px version and a 16 px mid-zoom version.</summary>
    private static int CellSize(int size) => size >= 24 ? 32 : 16;

    private static Rect2I StillCell(int variant, int stage, int size) =>
        new(Wrap(variant, VariantCount) * size, ClampStage(stage) * size, size, size);

    private static Rect2I PoseCell(int facing, AgentFrame frame, int size) =>
        new(FrameIndex(frame) * size, Wrap(facing, FacingCount) * size, size, size);

    private static int ClampStage(int stage) => Math.Clamp(stage, 0, StageCount - 1);

    /// <summary>Unknown frames draw the still.</summary>
    private static int FrameIndex(AgentFrame frame) => frame is >= AgentFrame.Still and <= AgentFrame.Hurt ? (int)frame : 0;

    private static int Wrap(int value, int count) => ((value % count) + count) % count;

    private static Sheet StillSheet(int size)
    {
        if (!StillSheets.TryGetValue(size, out var sheet))
        {
            sheet = new Sheet(VariantCount, StageCount, size);
            StillSheets[size] = sheet;
        }
        return sheet;
    }

    private static Sheet DrawnStill(int variant, int stage, int size)
    {
        variant = Wrap(variant, VariantCount);
        stage = ClampStage(stage);
        var sheet = StillSheet(size);
        sheet.Draw(variant, stage, variant, stage, South, AgentFrame.Still);
        return sheet;
    }

    private static Sheet DrawnPose(int variant, int stage, int facing, AgentFrame frame, int size)
    {
        (int Size, int Variant, int Stage) key = (size, Wrap(variant, VariantCount), ClampStage(stage));
        if (!PoseSheets.TryGetValue(key, out var sheet))
        {
            sheet = new Sheet(FrameCount, FacingCount, size);
            PoseSheets[key] = sheet;
        }
        facing = Wrap(facing, FacingCount);
        var column = FrameIndex(frame);
        sheet.Draw(column, facing, key.Variant, key.Stage, facing, (AgentFrame)column);
        return sheet;
    }

    /// <summary>
    /// A grid of sprite cells on one image and texture. Each cell is drawn the
    /// first time it is asked for, and the texture then takes the new pixels.
    /// </summary>
    private sealed class Sheet
    {
        private readonly int columns;
        private readonly int size;
        private readonly bool[] drawn;
        private ImageTexture? texture;

        public Sheet(int columns, int rows, int size)
        {
            this.columns = columns;
            this.size = size;
            drawn = new bool[columns * rows];
            Image = Blank(columns * size, rows * size);
        }

        public Image Image { get; }

        public ImageTexture Texture => texture ??= ImageTexture.CreateFromImage(Image);

        public void Draw(int column, int row, int variant, int stage, int facing, AgentFrame frame)
        {
            var cell = row * columns + column;
            if (drawn[cell]) return;
            Image.BlitRect(Render(variant, stage, facing, frame, size), new Rect2I(0, 0, size, size),
                new Vector2I(column * size, row * size));
            drawn[cell] = true;
            texture?.Update(Image);
        }
    }

    private static Image Blank(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        return image;
    }

    private static int StableCode(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in value) hash = hash * 31 + character;
            return hash;
        }
    }

    // ---------------------------------------------------------------- figures

    /// <summary>
    /// The measurements of one figure in pixels. Along means along the facing,
    /// across means toward the figure's right. HeadBack moves the head back
    /// along the facing, HeadLift moves it toward the top of the screen.
    /// FootAhead is how far the toes poke out beyond the shoulder outline and
    /// FootDrop how far the feet sit toward the bottom of the screen, because
    /// they are on the ground below the lifted shoulders. Unit is the scale
    /// relative to an adult at 32 px and Edge the outline width.
    /// </summary>
    private readonly record struct Build(
        float HeadR, float HeadBack, float HeadLift,
        float ShoulderAcross, float ShoulderAlong,
        float HandR, float HandAt, float Reach, float LeadReach,
        float FootAcross, float FootAlong, float FootAt, float FootAhead, float FootDrop,
        Vector2 Center, float Unit, float Edge)
    {
        /// <summary>An adult in a 32 px cell: head disc 11 px, shoulders 18 × 11.</summary>
        public static Build Adult32 => new(5.5f, 1.0f, 3.0f, 9.2f, 5.8f, 1.55f, 9.6f, 1.0f, 1.4f, 1.5f, 1.6f, 3.0f, 2.4f, 1.5f, new(16, 16f), 1f, 1f);

        /// <summary>An adult in a 16 px cell: 7 px head, 10 × 6 shoulders.</summary>
        public static Build Adult16 => new(2.6f, 0.3f, 2.0f, 5.0f, 3.0f, 0.6f, 5.4f, 0.5f, 0.8f, 0.6f, 1.0f, 1.6f, 1.5f, 0.5f, new(8, 8.5f), 0.5f, 1f);

        /// <summary>
        /// The same figure at <paramref name="k"/> of its size (0.78 for a
        /// child), with the head shrunk less so children keep a larger head.
        /// </summary>
        public Build Scaled(float k, float headK) => this with
        {
            HeadR = HeadR * headK,
            HeadBack = HeadBack * k,
            HeadLift = HeadLift * k,
            ShoulderAcross = ShoulderAcross * k,
            ShoulderAlong = ShoulderAlong * k,
            HandR = HandR * k,
            HandAt = HandAt * k,
            Reach = Reach * k,
            LeadReach = LeadReach * k,
            FootAcross = FootAcross * k,
            FootAlong = FootAlong * k,
            FootAt = FootAt * k,
            FootAhead = FootAhead * k,
            FootDrop = FootDrop * k,
            Unit = Unit * k,
        };
    }

    /// <summary>Unit vector of a facing on screen (y down): 0 is south, then clockwise through west.</summary>
    private static Vector2 Direction(int facing) => Vector2.FromAngle(MathF.PI / 2 + facing * MathF.PI / 4);

    /// <summary>The figure's own right-hand side, perpendicular to the facing.</summary>
    private static Vector2 Side(Vector2 direction) => new(-direction.Y, direction.X);

    /// <summary>
    /// How deep the face shows below the fringe, at the front of the head: a
    /// full band facing south, less sideways, a sliver on the back diagonals,
    /// none facing north, where the hair whorl shows instead. It shrinks
    /// toward north because the slight lift means we see faces from the south.
    /// </summary>
    private static float FaceDepth(int facing) => facing switch
    {
        0 => 2.6f,
        1 or 7 => 2.2f,
        2 or 6 => 1.8f,
        3 or 5 => 1.0f,
        _ => 0f,
    };

    /// <summary>One agent sprite, transparent, in a square cell of <paramref name="size"/> pixels (32 or 16).</summary>
    private static Image Render(int variant, int stage, int facing, AgentFrame frame, int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var paint = new Painter(image);
        var skin = Skins[variant];
        var hair = stage == 3 ? ElderHair : Hairs[variant];
        var shirt = Shirts[variant];
        if (stage == 0)
        {
            Infant(paint, Direction(facing), skin, hair, size);
            return image;
        }
        var build = size >= 24 ? Build.Adult32 : Build.Adult16;
        // A child is 0.78 of an adult, its head shrunk only to 0.86 so it
        // keeps a child's larger head, and sits half a pixel lower so its feet
        // stand where an adult's do.
        if (stage == 1) build = build.Scaled(0.78f, 0.86f) with { Center = build.Center + new Vector2(0, 0.5f) * build.Unit };
        // An elder stoops: the head comes about a pixel forward, and the
        // shoulders and hands draw in slightly.
        if (stage == 3) build = build with { HeadBack = build.HeadBack - 1.25f * build.Unit, ShoulderAcross = build.ShoulderAcross - 0.4f * build.Unit, HandAt = build.HandAt - 0.4f * build.Unit };
        Figure(paint, build, facing, frame, skin, hair, shirt, elder: stage == 3);
        return image;
    }

    /// <summary>
    /// Draws a standing or walking person: shadow, feet, shoulders, hands,
    /// head, then the frame's prop. Every part is placed along the facing and
    /// its side vector, so one routine serves all eight facings; shading is
    /// placed on screen so the light stays north-west.
    /// </summary>
    private static void Figure(Painter paint, Build b, int facing, AgentFrame frame, Color skin, Color hair, Color shirt, bool elder)
    {
        var d = Direction(facing);
        var s = Side(d);
        var c = b.Center;
        var p = b.Unit;
        var small = p < 0.7f;

        // Walk cycle: feet alternate ±2 px, hands swing 1 px against them.
        var (leftStep, rightStep, leftSwing, rightSwing) = frame switch
        {
            AgentFrame.Walk1 => (2f, -2f, -1f, 1f),
            AgentFrame.Walk2 => (-2f, 2f, 1f, -1f),
            AgentFrame.Hurt => (0.5f, -2f, 0f, 0f),      // the limp: one foot dragged behind
            _ => (0f, 0f, 0f, 0f),
        };
        var stepScale = small ? 0.5f : p;
        leftStep *= stepScale; rightStep *= stepScale; leftSwing *= stepScale; rightSwing *= stepScale;

        // The near hand (the one toward the bottom of the screen) reaches forward when seen sideways.
        var leftLead = -s.Y > 0.3f ? b.LeadReach : 0f;
        var rightLead = s.Y > 0.3f ? b.LeadReach : 0f;

        // Ground shadow to the south-east, kept inside the cell.
        paint.Oval(c + new Vector2(1, 3) * (small ? 0.5f : p), d, b.ShoulderAlong + 1.2f * p, b.ShoulderAcross + 0.2f, Shadow);

        // Feet: boots whose toes poke out ahead of the shoulders. Facing away
        // (N, NW, NE) the toes would sit beside the top of the head and read as
        // ears, so there the feet stay hidden under the body and a walking step
        // shows the heel of the back foot behind instead.
        var awayFromViewer = d.Y < -0.3f;
        var bootEdge = small ? 0.6f : 0.85f;
        var drop = new Vector2(0, b.FootDrop);
        foreach (var (side, step) in new[] { (-1f, leftStep), (1f, rightStep) })
        {
            var across = s * (side * b.FootAt);
            Vector2 boot;
            if (!awayFromViewer)
            {
                var toe = b.ShoulderAlong + b.Edge + b.FootAhead;
                boot = c + d * (toe - b.FootAlong - bootEdge + step) + across + drop;
            }
            else if (step < -0.1f)
            {
                var heel = b.ShoulderAlong + b.Edge + b.FootAhead * 0.75f;
                boot = c - d * (heel - b.FootAlong - bootEdge) + across + drop;
            }
            else continue;
            if (!small) Leg(paint, c + across * 0.85f + drop * 0.4f, boot, b);
            Boot(paint, boot, d, b, small);
        }

        Shoulders(paint, c, d, b, shirt);

        // The head sits back from the chest, but never lower on screen than the
        // lift puts it, so a figure facing away still shows its shirt below the head.
        var back = -d * b.HeadBack;
        var head = c + new Vector2(back.X, MathF.Min(back.Y, 0)) - new Vector2(0, b.HeadLift);
        if (!small)
        {
            // The head is above the shoulders, so it casts a soft shade onto the shirt to its south-east.
            var cast = head - TowardLight * 1.6f;
            var reach = b.HeadR + b.Edge;
            paint.Oval(c, d, b.ShoulderAlong, b.ShoulderAcross, shirt.Darkened(0.22f), (q, _, _) => (q + c - cast).Length() <= reach);
        }

        var leftHand = c - s * b.HandAt + d * (b.Reach + leftLead + leftSwing);
        var rightHand = c + s * b.HandAt + d * (b.Reach + rightLead + rightSwing);

        switch (frame)
        {
            case AgentFrame.Carry:
                {
                    var crate = c + d * (b.ShoulderAlong + 2.2f * p);
                    Crate(paint, crate, d, p);
                    // Hands grip the crate's sides, just outside it so they still show
                    // beside the head when the figure faces away.
                    leftHand = crate - s * (5.2f * p) - d * (0.5f * p);
                    rightHand = crate + s * (5.2f * p) - d * (0.5f * p);
                    break;
                }
            case AgentFrame.Work:
                // The leading (right) hand comes out and forward to swing the pick.
                rightHand = c + s * (b.HandAt + 0.6f * p) + d * (b.Reach + 2.4f * p);
                Pick(paint, rightHand, d, s, p);
                break;
        }

        if (elder && frame is AgentFrame.Still or AgentFrame.Walk1 or AgentFrame.Walk2 or AgentFrame.Hurt or AgentFrame.Talk)
        {
            // The cane goes in the hand nearer the viewer (the right hand facing straight
            // north or south) so it is never hidden behind the body.
            var leftIsNear = -s.Y > 0.3f;
            Cane(paint, leftIsNear ? leftHand : rightHand, d, leftIsNear ? -s : s, b, small);
        }

        Hand(paint, leftHand, -s, b, skin);
        Hand(paint, rightHand, s, b, skin);

        Head(paint, head, b, d, s, facing, skin, hair, small);

        if (frame == AgentFrame.Hurt) Bandage(paint, head, b, d, s, small);
        if (frame == AgentFrame.Talk) SpeechMark(paint, head, b.HeadR, small);
    }

    /// <summary>
    /// The shoulders: an oval across the facing in the shirt colour with the
    /// outline, a one-pixel shade band on the south-east and a lit rim on the
    /// north-west.
    /// </summary>
    private static void Shoulders(Painter paint, Vector2 c, Vector2 d, Build b, Color shirt)
    {
        var along = b.ShoulderAlong;
        var across = b.ShoulderAcross;
        paint.Oval(c, d, along + b.Edge, across + b.Edge, Outline);
        paint.Oval(c, d, along, across, shirt);
        var rim = b.Unit < 0.7f ? 0.9f : 1.4f;
        paint.Oval(c, d, along, across, shirt.Darkened(0.22f), (q, _, _) => !Painter.InOval(q + TowardLight * -rim, d, along, across));
        if (b.Unit >= 0.7f)
            paint.Oval(c, d, along, across, shirt.Lightened(0.13f), (q, _, _) => !Painter.InOval(q + TowardLight * 1.1f, d, along, across));
    }

    /// <summary>
    /// A hand: a skin disc with its own outline so it reads over the shirt. At
    /// 16 px a full outline would make a dark blob, so the hand is one skin
    /// pixel on the shoulder outline with one outline pixel beyond it.
    /// </summary>
    private static void Hand(Painter paint, Vector2 at, Vector2 outward, Build b, Color skin)
    {
        if (b.Unit < 0.7f)
        {
            // Step along the main axis only, so the outline pixel always touches the hand.
            var step = MathF.Abs(outward.X) >= MathF.Abs(outward.Y) - 0.01f
                ? new Vector2(MathF.Sign(outward.X), 0)
                : new Vector2(0, MathF.Sign(outward.Y));
            paint.Dot(at + step, Outline);
            paint.Dot(at, skin);
            return;
        }
        paint.Disc(at, b.HandR + 0.85f, Outline);
        paint.Disc(at, b.HandR, skin);
    }

    /// <summary>
    /// A trouser leg from under the shirt to a boot. It is drawn before the
    /// shoulders, so it only shows when a step carries the boot clear of the
    /// shirt and the foot would otherwise float.
    /// </summary>
    private static void Leg(Painter paint, Vector2 hip, Vector2 boot, Build b)
    {
        var axis = boot - hip;
        var length = axis.Length();
        if (length < 0.5f) return;
        var direction = axis / length;
        var middle = (hip + boot) / 2;
        var half = b.FootAcross * 0.85f;
        paint.Oval(middle, direction, length / 2 + 0.85f, half + 0.85f, Outline);
        paint.Oval(middle, direction, length / 2, half, Trousers);
    }

    /// <summary>A boot seen from above: a short Timber shade oval with the outline, toe toward the facing.</summary>
    private static void Boot(Painter paint, Vector2 at, Vector2 d, Build b, bool small)
    {
        if (small)
        {
            paint.Oval(at, d, b.FootAlong + 0.6f, b.FootAcross + 0.4f, Outline);
            paint.Dot(at + d * 0.5f, TimberShade);
            return;
        }
        paint.Oval(at, d, b.FootAlong + 0.85f, b.FootAcross + 0.85f, Outline);
        paint.Oval(at, d, b.FootAlong, b.FootAcross, TimberShade);
    }

    /// <summary>
    /// The head from above: outline and skin disc, hair over the crown and
    /// back with the face showing on the facing side below a fringe, a nose
    /// hint (a darker dot facing south, a pixel poking out of the outline
    /// sideways), a whorl when facing north, and north-west light on the hair.
    /// </summary>
    private static void Head(Painter paint, Vector2 h, Build b, Vector2 d, Vector2 s, int facing, Color skin, Color hair, bool small)
    {
        var r = b.HeadR;
        var p = b.Unit;
        var face = FaceDepth(facing) * (small ? 0.55f : p);

        // A nose seen sideways pokes one pixel out of the silhouette.
        var noseOut = facing is 1 or 2 or 3 or 5 or 6 or 7;
        if (noseOut)
        {
            paint.Disc(h + d * (r + 0.7f), small ? 0.6f : 1.1f, Outline);
        }

        paint.Disc(h, r + b.Edge, Outline);
        paint.Disc(h, r, skin);
        if (noseOut) paint.Dot(h + d * (r + 0.55f), skin);

        // Hair: an oval from the back of the head to the fringe line, as wide as
        // the head, so the uncovered front reads as a face crescent.
        var front = r - face;
        var hairAlong = (r + front) / 2;
        var back = r - hairAlong;
        var hairCenter = h - d * back;
        bool InHair(Vector2 q, float t, float u) => (t - back) * (t - back) + u * u <= r * r + 0.3f;
        paint.Oval(hairCenter, d, hairAlong + 0.05f, r + 0.6f, hair, (q, t, u) => InHair(q, t, u));

        if (!small)
        {
            // Shade on the south-east rim of the hair, and a thin sheen arc just
            // inside the north-west rim.
            var hairShade = hair.Darkened(0.28f);
            var sheen = hair.Lightened(0.22f);
            var offset = hairCenter - h;
            paint.Oval(hairCenter, d, hairAlong + 0.05f, r + 0.6f, hairShade,
                (q, t, u) => InHair(q, t, u) && (q + offset - TowardLight * 1.5f).Length() > r);
            paint.Oval(hairCenter, d, hairAlong + 0.05f, r + 0.6f, sheen, (q, t, u) =>
            {
                var fromCentre = q + offset;
                var distance = fromCentre.Length();
                return InHair(q, t, u) && distance > r - 2.3f && distance < r - 1.0f && fromCentre.Dot(TowardLight) > 0.82f * distance;
            });
        }
        else
        {
            paint.Dot(h + TowardLight * (r * 0.6f), hair.Lightened(0.16f));
        }

        if (face > 0.01f && !small)
        {
            // A strand of fringe dips into the face on the figure's left, parting the hair.
            if (face >= 1.6f) paint.Dot(h + d * (front + 0.5f) - s * 2.0f, hair);
            // Nose hint: a darker skin dot just inside the front of the face.
            if (facing is 0 or 1 or 7) paint.Dot(h + d * (r - 0.9f), skin.Darkened(0.2f));
        }
        else if (face <= 0.01f && !small)
        {
            // Facing north: a small whorl on the crown, a dark centre with a
            // two-pixel lighter curl around it.
            var crown = h + new Vector2(0.5f, 0.5f);
            var curl = hair.Lightened(0.2f);
            paint.Dot(crown, hair.Darkened(0.3f));
            paint.Dot(crown + new Vector2(1, 0), curl);
            paint.Dot(crown + new Vector2(0, 1), curl);
        }
    }

    /// <summary>A crate from the Timber ramp, 8 × 6, held ahead of the chest.</summary>
    private static void Crate(Painter paint, Vector2 at, Vector2 d, float p)
    {
        var s = Side(d);
        var halfAlong = 3f * p;
        var halfAcross = 4f * p;
        paint.Box(at, d, halfAlong + 1, halfAcross + 1, TimberEdge);
        paint.Box(at, d, halfAlong, halfAcross, TimberBase);
        // Lit north-west rim, shaded south-east, a plank seam across the lid.
        paint.Box(at, d, halfAlong, halfAcross, TimberLight, (q, _, _) => q.Dot(TowardLight) > 2.2f * p);
        paint.Box(at, d, halfAlong, halfAcross, TimberShade, (q, _, _) => q.Dot(TowardLight) < -2.6f * p);
        if (p >= 0.7f)
        {
            paint.Line(at - s * (halfAcross - 0.5f) - d * 0.5f, at + s * (halfAcross - 0.5f) - d * 0.5f, TimberShade);
            paint.Dot(at + TowardLight * 3.2f, TimberHighlight);
        }
    }

    /// <summary>
    /// A pick swung out from the leading hand: a Timber handle, an iron head
    /// across its end with a glint, and a faint arc behind it to show the swing.
    /// </summary>
    private static void Pick(Painter paint, Vector2 hand, Vector2 d, Vector2 s, float p)
    {
        var swing = (d * 0.55f + s * 0.84f).Normalized();
        var across = new Vector2(-swing.Y, swing.X);
        var tip = hand + swing * (8.5f * p);
        // The swing arc: a few faint pixels curving back toward the body.
        for (var k = 1; k <= 4; k++)
        {
            var angle = -0.34f * k;
            paint.Dot(hand + swing.Rotated(angle) * (9.5f * p), new Color(1, 1, 1, 0.42f - 0.08f * k));
        }
        paint.Line(hand + swing * 0.8f + across * 0.5f, tip + across * 0.5f, Outline);
        paint.Line(hand + swing * 0.8f - across * 0.5f, tip - across * 0.5f, Outline);
        paint.Line(hand + swing * 0.8f, tip, TimberLight);
        paint.Box(tip, swing, 1.1f * p + 0.9f, 3.6f * p + 0.9f, Outline);
        paint.Box(tip, swing, 1.1f * p, 3.6f * p, IronBase);
        paint.Box(tip, swing, 1.1f * p, 3.6f * p, IronShade, (q, _, _) => q.Dot(TowardLight) < -1.0f);
        paint.Dot(tip + TowardLight * 1.2f, IronHighlight);
    }

    /// <summary>
    /// An elder's one-pixel cane with its crook showing just above the hand.
    /// It is planted a little ahead and outside, on the ground, which sits
    /// lower on screen than the hands, like the feet.
    /// </summary>
    private static void Cane(Painter paint, Vector2 hand, Vector2 d, Vector2 outward, Build b, bool small)
    {
        var p = b.Unit;
        var ground = new Vector2(0, b.FootDrop + (small ? 1.5f : 3.5f * p));
        var tip = hand + d * (small ? 1.5f : 3.0f * p) + outward * (small ? 0.5f : 1.5f * p) + ground;
        if (small)
        {
            paint.Line(hand, tip, TimberEdge);
            return;
        }
        var top = hand - (tip - hand).Normalized() * 2.0f;
        paint.Line(top, tip, TimberEdge);
        paint.Dot(top - outward * 1.0f, TimberEdge);   // the crook
        paint.Dot(tip, TimberLight);
    }

    /// <summary>A cream bandage wrapped across the crown, ear to ear, with a knot on one side.</summary>
    private static void Bandage(Painter paint, Vector2 h, Build b, Vector2 d, Vector2 s, bool small)
    {
        var r = b.HeadR;
        var band = h - d * (small ? 0.2f : 0.6f);
        var half = small ? 0.55f : 1.2f;   // one pixel row at 16 px
        bool OnHead(Vector2 q, float t, float u) => (q + (band - h)).Length() <= r + 0.25f;
        paint.Box(band, d, half, r + 1, ClothHighlight, OnHead);
        if (small) return;
        paint.Box(band, d, half, r + 1, ClothShade, (q, t, u) => OnHead(q, t, u) && q.Dot(TowardLight) < -2.5f);
        // The knot: two short tails off the figure's left side of the head.
        var knot = band - s * (r + 0.4f);
        paint.Dot(knot, ClothLight);
        paint.Dot(knot - s * 1f - d * 1f, ClothLight);
        paint.Dot(knot - s * 1f + d * 1f, ClothShade);
    }

    /// <summary>A 5 × 4 speech mark off the head to the north-east, with a one-pixel tail toward the head.</summary>
    private static void SpeechMark(Painter paint, Vector2 h, float r, bool small)
    {
        if (small)
        {
            var at16 = h + new Vector2(r + 1.5f, -r - 1.0f);
            paint.Rect(at16.X - 1, at16.Y - 1, 4, 3, Outline);
            paint.Rect(at16.X, at16.Y, 2, 1, ClothHighlight);
            return;
        }
        var x = (int)MathF.Floor(h.X + r + 1.5f);
        var y = (int)MathF.Floor(h.Y - r - 4.5f);
        paint.Rect(x, y, 7, 6, Outline);
        paint.Rect(x + 1, y + 1, 5, 4, ClothHighlight);
        paint.Pixel(x, y, Colors.Transparent);
        paint.Pixel(x + 6, y, Colors.Transparent);
        paint.Pixel(x + 6, y + 5, Colors.Transparent);
        paint.Pixel(x + 1, y + 1, ClothLight);
        paint.Pixel(x - 1, y + 6, Outline);            // tail toward the head
        paint.Pixel(x, y + 5, Outline);
        paint.Pixel(x + 2, y + 2, Outline.Lerp(ClothHighlight, 0.2f));
        paint.Pixel(x + 4, y + 2, Outline.Lerp(ClothHighlight, 0.2f));
    }

    /// <summary>
    /// An infant stays a wrapped bundle lying along the facing, head at the
    /// back end looking up: a lit blanket with a swaddle band and a fold, a
    /// face with a nose dot, and a hair cap at the top of the head.
    /// </summary>
    private static void Infant(Painter paint, Vector2 d, Color skin, Color hair, int size)
    {
        var p = size / 32f;
        var small = size < 24;
        var c = new Vector2(16, 17.5f) * p;
        var s = Side(d);
        const float along = 8.5f, across = 6.2f;
        paint.Oval(c + new Vector2(1, 3) * p, d, along * p, (across - 0.5f) * p, Shadow);
        paint.Oval(c, d, along * p + 1, across * p + 1, Outline);
        paint.Oval(c, d, along * p, across * p, BlanketBase);
        if (!small)
        {
            paint.Oval(c, d, along, across, BlanketFold, (q, _, _) => !Painter.InOval(q + TowardLight * -1.5f, d, along, across));
            paint.Oval(c, d, along, across, ClothHighlight, (q, _, _) => !Painter.InOval(q + TowardLight * 1.2f, d, along, across));
            // The swaddle band across the middle and a fold toward the feet end.
            paint.Box(c + d * 1.5f, d, 0.9f, across, BlanketFold, (q, t, u) => Painter.InOval(q + d * 1.5f, d, along, across));
            paint.Line(c + d * 4.5f - s * 3f, c + d * 6.5f + s * 1.5f, BlanketFold);
        }
        var head = c - d * (5.0f * p);
        var r = small ? 2.0f : 3.7f;
        paint.Disc(head, r + 1, Outline);
        paint.Disc(head, r, skin);
        // Hair cap on the far half of the head, the face toward the feet end.
        paint.Oval(head - d * (1.3f * p), d, r * 0.62f, r + 0.4f, hair, (q, t, u) => (q - d * (1.3f * p)).Length() <= r + 0.2f && t < -0.2f);
        if (!small)
        {
            paint.Dot(head + d * 1.2f, skin.Darkened(0.2f));
            paint.Dot(head - d * 2.4f + TowardLight * 1.0f, hair.Lightened(0.26f));
        }
    }

    // ---------------------------------------------------------------- painter

    /// <summary>
    /// Alpha-blending pixel painter in pixel units, clipped to its image.
    /// Shapes are sampled at pixel centres; ovals and boxes take a direction
    /// so the same call draws a figure at any facing. The optional keep
    /// predicate receives the pixel's offset from the shape centre (on screen,
    /// then along and across the direction) to carve bands and crescents.
    /// </summary>
    private sealed class Painter(Image image)
    {
        public delegate bool Keep(Vector2 offset, float along, float across);

        /// <summary>Whether an offset from an oval's centre lies inside it.</summary>
        public static bool InOval(Vector2 offset, Vector2 direction, float along, float across)
        {
            var t = offset.Dot(direction);
            var u = offset.Dot(new Vector2(-direction.Y, direction.X));
            var rt = MathF.Max(along, 0.55f);
            var ru = MathF.Max(across, 0.55f);
            return (t * t) / (rt * rt) + (u * u) / (ru * ru) <= 1f;
        }

        public void Pixel(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) return;
            if (color.A <= 0)
            {
                image.SetPixel(x, y, Colors.Transparent);
                return;
            }
            image.SetPixel(x, y, image.GetPixel(x, y).Blend(color));
        }

        public void Dot(Vector2 at, Color color) => Pixel((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), color);

        public void Disc(Vector2 center, float radius, Color color) => Oval(center, new Vector2(0, 1), radius, radius, color);

        /// <summary>An oval with half depth <paramref name="along"/> the direction and half width across it.</summary>
        public void Oval(Vector2 center, Vector2 direction, float along, float across, Color color, Keep? keep = null)
        {
            var side = new Vector2(-direction.Y, direction.X);
            var rt = MathF.Max(along, 0.55f);
            var ru = MathF.Max(across, 0.55f);
            var reach = MathF.Max(rt, ru) + 1;
            for (var y = (int)MathF.Floor(center.Y - reach); y <= (int)MathF.Ceiling(center.Y + reach); y++)
                for (var x = (int)MathF.Floor(center.X - reach); x <= (int)MathF.Ceiling(center.X + reach); x++)
                {
                    var q = new Vector2(x + 0.5f - center.X, y + 0.5f - center.Y);
                    var t = q.Dot(direction);
                    var u = q.Dot(side);
                    if ((t * t) / (rt * rt) + (u * u) / (ru * ru) > 1f) continue;
                    if (keep is null || keep(q, t, u)) Pixel(x, y, color);
                }
        }

        /// <summary>A rectangle turned to the direction: half depth along it, half width across.</summary>
        public void Box(Vector2 center, Vector2 direction, float halfAlong, float halfAcross, Color color, Keep? keep = null)
        {
            var side = new Vector2(-direction.Y, direction.X);
            var reach = MathF.Max(halfAlong, halfAcross) * 1.5f + 1;
            for (var y = (int)MathF.Floor(center.Y - reach); y <= (int)MathF.Ceiling(center.Y + reach); y++)
                for (var x = (int)MathF.Floor(center.X - reach); x <= (int)MathF.Ceiling(center.X + reach); x++)
                {
                    var q = new Vector2(x + 0.5f - center.X, y + 0.5f - center.Y);
                    var t = q.Dot(direction);
                    var u = q.Dot(side);
                    if (MathF.Abs(t) > halfAlong || MathF.Abs(u) > halfAcross) continue;
                    if (keep is null || keep(q, t, u)) Pixel(x, y, color);
                }
        }

        /// <summary>An axis-aligned rectangle from its top-left corner.</summary>
        public void Rect(float x, float y, float width, float height, Color color)
        {
            var left = (int)MathF.Floor(x);
            var top = (int)MathF.Floor(y);
            for (var py = top; py < top + (int)MathF.Round(height); py++)
                for (var px = left; px < left + (int)MathF.Round(width); px++)
                    Pixel(px, py, color);
        }

        /// <summary>A one-pixel line.</summary>
        public void Line(Vector2 from, Vector2 to, Color color)
        {
            var steps = Math.Max(1, (int)MathF.Ceiling(from.DistanceTo(to) * 2));
            int lastX = int.MinValue, lastY = int.MinValue;
            for (var step = 0; step <= steps; step++)
            {
                var point = from.Lerp(to, step / (float)steps);
                var x = (int)MathF.Floor(point.X);
                var y = (int)MathF.Floor(point.Y);
                if (x == lastX && y == lastY) continue;
                lastX = x;
                lastY = y;
                Pixel(x, y, color);
            }
        }
    }
}
