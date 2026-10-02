using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Agents;

/// <summary>
/// Proposed agent sprites: the same friendly people seen from above, a round
/// head over a shirt with hands at the shoulders, now with eight facings,
/// walk frames, action frames, life stages and a first horse.
/// <para>
/// The head stays over the shoulders (style guide A1) and keeps today's small
/// lift toward the top of the screen, so every facing still shows the shirt
/// below the head as the current sprites do. Facing is shown only by where
/// things sit (A2): the face and fringe on the facing side of the head, a nose
/// that pokes out of the outline when seen sideways, the shoulders turning
/// across the facing, the hands at the shoulder tips with the near one
/// reaching forward, and the feet poking out ahead. Light comes from the
/// north-west on hair and shirt whatever the facing (L1).
/// </para>
/// Everything is deterministic and uses the game's skin, hair and shirt
/// palettes with the 1E2226 outline.
/// </summary>
public sealed class AgentsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "agents";
    public string Name => "agents";

    public const int FacingCount = 8;
    public const int VariantCount = 6;

    /// <summary>Facing names in the game's order: 0 S, 1 SW, 2 W, 3 NW, 4 N, 5 NE, 6 E, 7 SE.</summary>
    public static readonly string[] FacingNames = ["S", "SW", "W", "NW", "N", "NE", "E", "SE"];

    /// <summary>Animation and action frames an agent can be drawn in.</summary>
    public enum Pose { Still, Walk1, Walk2, Carry, Work, Talk, Hurt }

    // The palettes stay exactly as the game has them (style guide section 2).
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
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);   // L2 sprite shadow
    private static readonly Color TimberEdge = new("3F2A1A");
    private static readonly Color TimberShade = new("6E4E31");
    private static readonly Color TimberBase = new("8A6440");
    private static readonly Color TimberLight = new("A77C52");
    private static readonly Color TimberHighlight = new("D2AC77");
    private static readonly Color ClothShade = new("CABC99");
    private static readonly Color ClothLight = new("E8DCC0");
    private static readonly Color ClothHighlight = new("FFF5DF");
    private static readonly Color BlanketBase = new("E9E1CF");   // today's infant blanket
    private static readonly Color BlanketFold = new("CFC5AE");
    private static readonly Color IronShade = new("524C48");
    private static readonly Color IronBase = new("6C6560");
    private static readonly Color IronHighlight = new("A69E98");
    private static readonly Color ManeColor = new("6B4226");      // Hair 1 (A6)
    private static readonly Color Trousers = new("4A4E55");       // Slate shade: legs show only mid-step

    /// <summary>How far apart a rider's hands are on the reins, as a share of the standing hand spread.</summary>
    private const float ReinGrip = 0.45f;

    /// <summary>Screen direction the light comes from (north-west, L1), as a unit vector.</summary>
    private static readonly Vector2 TowardLight = new Vector2(-1, -1).Normalized();

    // ---------------------------------------------------------------- entries

    public IEnumerable<Entry> Render()
    {
        var shown = new List<(string Id, Image Sprite, int Size, string? Note)>();
        void Add(string id, Image sprite, int size = 32, string? note = null) => shown.Add((id, sprite, size, note));

        for (var facing = 0; facing < FacingCount; facing++)
            Add($"adult.v0.{FacingNames[facing]}", Sprite(0, 2, facing, Pose.Still, 32), note: FacingNote(facing));
        foreach (var facing in new[] { 0, 2, 4, 6 })
        {
            Add($"adult.v0.walk1.{FacingNames[facing]}", Sprite(0, 2, facing, Pose.Walk1, 32),
                note: facing == 0 ? "Left foot 2 px ahead, right foot 2 px back; hands swing 1 px the other way." : null);
            Add($"adult.v0.walk2.{FacingNames[facing]}", Sprite(0, 2, facing, Pose.Walk2, 32),
                note: facing == 0 ? "The opposite step." : null);
        }
        Add("adult.v0.carry.S", Sprite(0, 2, 0, Pose.Carry, 32), note: "An 8×6 Timber crate held ahead, a hand on each side.");
        Add("adult.v0.work.S", Sprite(0, 2, 0, Pose.Work, 32), note: "A pick swung out from the leading hand, with a faint swing arc.");
        Add("adult.v0.talk.S", Sprite(0, 2, 0, Pose.Talk, 32), note: "A 5×4 speech mark off the head to the north-east.");
        Add("adult.v0.hurt.S", Sprite(0, 2, 0, Pose.Hurt, 32), note: "A cream bandage across the head and an uneven, limping stance.");
        Add("child.v2.S", Sprite(2, 1, 0, Pose.Still, 32), note: "0.78 scale with a slightly larger head for its size.");
        Add("elder.v3.S", Sprite(3, 3, 0, Pose.Still, 32), note: "Grey hair, head a pixel forward (the stoop), a one-pixel cane.");
        Add("infant.v0.S", Sprite(0, 0, 0, Pose.Still, 32), note: "Still a wrapped bundle, now with a swaddle band and a face looking up.");
        Add("adult.v1.S", Sprite(1, 2, 0, Pose.Still, 32), note: "Same drawing, variant 1 colours.");
        Add("adult.v4.S", Sprite(4, 2, 0, Pose.Still, 32), note: "Same drawing, variant 4 colours.");
        Add("horse.S", Horse(0, false, 32), note: "Timber body, ears and a pale blaze, Hair 1 mane and tail, Timber edge saddle.");
        Add("horse.E", Horse(6, false, 32));
        Add("horse.rider.E", Horse(6, true, 32), note: "An adult on the saddle: boots on both flanks, hands on the reins.");
        Add("adult.v0.S.16", Sprite(0, 2, 0, Pose.Still, 16), 16, "16 px: 7 px head, 10×6 shoulders, outline kept (A7).");
        Add("adult.v0.E.16", Sprite(0, 2, 6, Pose.Still, 16), 16);

        var onGrass = shown.Select(e => new Entry(Family, e.Id, Bitmap.Over(TerrainTextures.Tile(TerrainStyle.Grass, 0, e.Size), e.Sprite, 0, 0), e.Note)).ToList();
        var bare = shown.Select(e => new Entry(Family, e.Id + ".sprite", e.Sprite)).ToList();

        var (strip, stripOnGrass) = Turnaround();
        onGrass.Insert(0, new Entry(Family, "adult.v0.turnaround", stripOnGrass, "The eight facings in order: S, SW, W, NW, N, NE, E, SE."));
        // The contact sheet sizes each column to its widest picture, so the two
        // wide strips go in the first column and the other columns stay narrow.
        var all = new List<Entry>(onGrass);
        var stripAt = (all.Count + 7) / 8 * 8 - all.Count;
        bare.Insert(Math.Min(stripAt, bare.Count), new Entry(Family, "adult.v0.turnaround.sprite", strip));
        all.AddRange(bare);
        return all;
    }

    /// <summary>The owner-facing note for one still facing.</summary>
    private static string? FacingNote(int facing) => facing switch
    {
        0 => "Face, fringe and nose at the bottom of the head; both hands beside the shoulders; both feet below.",
        2 => "Face on the west side with the nose poking out; the near hand reaches forward; feet to the west.",
        4 => "All hair with a whorl on the crown and the shirt's back below; feet stay hidden (a step shows a heel).",
        _ => null,
    };

    /// <summary>Adult variant 0 in all eight facings side by side, bare and on grass.</summary>
    private static (Image Bare, Image OnGrass) Turnaround()
    {
        var bare = Bitmap.Empty(32 * FacingCount, 32);
        var ground = Bitmap.Empty(32 * FacingCount, 32);
        for (var facing = 0; facing < FacingCount; facing++)
        {
            var figure = Sprite(0, 2, facing, Pose.Still, 32);
            Sheet.Blend(bare, figure, facing * 32, 0);
            ground.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(facing, 0), 32), new Rect2I(0, 0, 32, 32), new Vector2I(facing * 32, 0));
            Sheet.Blend(ground, figure, facing * 32, 0);
        }
        return (bare, ground);
    }

    /// <summary>
    /// Draws every variant (0–5), life stage (0–3), facing (0–7) and frame at
    /// 32 and 16 px with this drawing. Frames: 0 still, 1 and 2 walk, 3 carry,
    /// 4 work, 5 talk, 6 hurt; anything else draws the still.
    /// </summary>
    public void Apply(ArtSet set)
    {
        set.Agent = (variant, stage, facing, frame, size) =>
        {
            var pose = frame switch { 1 => Pose.Walk1, 2 => Pose.Walk2, 3 => Pose.Carry, 4 => Pose.Work, 5 => Pose.Talk, 6 => Pose.Hurt, _ => Pose.Still };
            return Sprite(Wrap(variant, VariantCount), Math.Clamp(stage, 0, 3), Wrap(facing, FacingCount), pose, size >= 24 ? 32 : 16);
        };
    }

    private static int Wrap(int value, int count) => ((value % count) + count) % count;

    // ---------------------------------------------------------------- figures

    /// <summary>
    /// The measurements of one figure in pixels. Along means along the facing,
    /// across means toward the figure's right. HeadBack moves the head back
    /// along the facing, HeadLift moves it toward the top of the screen (the
    /// small lift today's sprites have). FootAhead is how far the toes poke out
    /// beyond the shoulder outline and FootDrop how far the feet sit toward the
    /// bottom of the screen, because they are on the ground below the lifted
    /// shoulders. Unit is the scale relative to an adult at 32 px and Edge the
    /// outline width.
    /// </summary>
    private readonly record struct Build(
        float HeadR, float HeadBack, float HeadLift,
        float ShoulderAcross, float ShoulderAlong,
        float HandR, float HandAt, float Reach, float LeadReach,
        float FootAcross, float FootAlong, float FootAt, float FootAhead, float FootDrop,
        Vector2 Center, float Unit, float Edge)
    {
        /// <summary>An adult in a 32 px cell: head disc 11 px, shoulders 18 × 11 (S3).</summary>
        public static Build Adult32 => new(5.5f, 1.0f, 3.0f, 9.2f, 5.8f, 1.55f, 9.6f, 1.0f, 1.4f, 1.5f, 1.6f, 3.0f, 2.4f, 1.5f, new(16, 16f), 1f, 1f);

        /// <summary>An adult in a 16 px cell: 7 px head, 10 × 6 shoulders (A7).</summary>
        public static Build Adult16 => new(2.6f, 0.3f, 2.0f, 5.0f, 3.0f, 0.6f, 5.4f, 0.5f, 0.8f, 0.6f, 1.0f, 1.6f, 1.5f, 0.5f, new(8, 8.5f), 0.5f, 1f);

        /// <summary>
        /// The same figure at <paramref name="k"/> of its size (0.78 for a child,
        /// A5), with the head shrunk less so children keep a larger head.
        /// </summary>
        public Build Scaled(float k, float headK) => this with
        {
            HeadR = HeadR * headK, HeadBack = HeadBack * k, HeadLift = HeadLift * k,
            ShoulderAcross = ShoulderAcross * k, ShoulderAlong = ShoulderAlong * k,
            HandR = HandR * k, HandAt = HandAt * k, Reach = Reach * k, LeadReach = LeadReach * k,
            FootAcross = FootAcross * k, FootAlong = FootAlong * k, FootAt = FootAt * k, FootAhead = FootAhead * k, FootDrop = FootDrop * k,
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
    /// none facing north, where the hair whorl shows instead (A2). It shrinks
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
    public static Image Sprite(int variant, int stage, int facing, Pose pose, int size)
    {
        var image = Bitmap.Empty(size, size);
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
        // A child is 0.78 of an adult (A5), its head shrunk only to 0.86 so it
        // keeps a child's larger head, and sits half a pixel lower so its feet
        // stand where an adult's do.
        if (stage == 1) build = build.Scaled(0.78f, 0.86f) with { Center = build.Center + new Vector2(0, 0.5f) * build.Unit };
        // An elder stoops: the head comes about a pixel forward (A5), and the
        // shoulders and hands draw in slightly.
        if (stage == 3) build = build with { HeadBack = build.HeadBack - 1.25f * build.Unit, ShoulderAcross = build.ShoulderAcross - 0.4f * build.Unit, HandAt = build.HandAt - 0.4f * build.Unit };
        Figure(paint, build, facing, pose, skin, hair, shirt, elder: stage == 3, mounted: false);
        return image;
    }

    /// <summary>
    /// Draws a standing or walking person: shadow, feet, shoulders, hands,
    /// head, then the pose's prop. Every part is placed along the facing and
    /// its side vector, so one routine serves all eight facings; shading is
    /// placed on screen so the light stays north-west.
    /// </summary>
    private static void Figure(Painter paint, Build b, int facing, Pose pose, Color skin, Color hair, Color shirt, bool elder, bool mounted)
    {
        var d = Direction(facing);
        var s = Side(d);
        var c = b.Center;
        var p = b.Unit;
        var small = p < 0.7f;

        // Walk cycle (A3): feet alternate ±2 px, hands swing 1 px against them.
        var (leftStep, rightStep, leftSwing, rightSwing) = pose switch
        {
            Pose.Walk1 => (2f, -2f, -1f, 1f),
            Pose.Walk2 => (-2f, 2f, 1f, -1f),
            Pose.Hurt => (0.5f, -2f, 0f, 0f),      // the limp: one foot dragged behind
            _ => (0f, 0f, 0f, 0f),
        };
        var stepScale = small ? 0.5f : p;
        leftStep *= stepScale; rightStep *= stepScale; leftSwing *= stepScale; rightSwing *= stepScale;

        // The near hand (the one toward the bottom of the screen) reaches forward when seen sideways.
        var leftLead = -s.Y > 0.3f ? b.LeadReach : 0f;
        var rightLead = s.Y > 0.3f ? b.LeadReach : 0f;

        if (!mounted)
        {
            // Ground shadow to the south-east (L2), kept inside the cell.
            paint.Oval(c + new Vector2(1, 3) * (small ? 0.5f : p), d, b.ShoulderAlong + 1.2f * p, b.ShoulderAcross + 0.2f, Shadow);

            // Feet: boots whose toes poke out ahead of the shoulders (A2). Facing
            // away (N, NW, NE) the toes would sit beside the top of the head and
            // read as ears, so there the feet stay hidden under the body and a
            // walking step shows the heel of the back foot behind instead.
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
        if (mounted)
        {
            // On a saddle both hands come forward and in to hold the reins.
            leftHand = c - s * (b.HandAt * ReinGrip) + d * (b.ShoulderAlong + 1.2f * p);
            rightHand = c + s * (b.HandAt * ReinGrip) + d * (b.ShoulderAlong + 1.2f * p);
        }

        switch (pose)
        {
            case Pose.Carry:
            {
                var crate = c + d * (b.ShoulderAlong + 2.2f * p);
                Crate(paint, crate, d, p);
                // Hands grip the crate's sides, just outside it so they still show
                // beside the head when the figure faces away.
                leftHand = crate - s * (5.2f * p) - d * (0.5f * p);
                rightHand = crate + s * (5.2f * p) - d * (0.5f * p);
                break;
            }
            case Pose.Work:
                // The leading (right) hand comes out and forward to swing the pick.
                rightHand = c + s * (b.HandAt + 0.6f * p) + d * (b.Reach + 2.4f * p);
                Pick(paint, rightHand, d, s, p);
                break;
        }

        if (elder && !mounted && pose is Pose.Still or Pose.Walk1 or Pose.Walk2 or Pose.Hurt or Pose.Talk)
        {
            // The cane goes in the hand nearer the viewer (the right hand facing straight
            // north or south) so it is never hidden behind the body.
            var leftIsNear = -s.Y > 0.3f;
            Cane(paint, leftIsNear ? leftHand : rightHand, d, leftIsNear ? -s : s, b, small);
        }

        Hand(paint, leftHand, -s, b, skin);
        Hand(paint, rightHand, s, b, skin);

        Head(paint, head, b, d, s, facing, skin, hair, small);

        if (pose == Pose.Hurt) Bandage(paint, head, b, d, s, small);
        if (pose == Pose.Talk) SpeechMark(paint, head, b.HeadR, small);
    }

    /// <summary>
    /// The shoulders: an oval across the facing in the shirt colour with the
    /// outline, a one-pixel shade band on the south-east and a lit rim on the
    /// north-west (A4, L1).
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
            // inside the north-west rim (L1).
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

    /// <summary>A crate from the Timber ramp, 8 × 6, held ahead of the chest (A3).</summary>
    private static void Crate(Painter paint, Vector2 at, Vector2 d, float p)
    {
        var s = Side(d);
        var halfAlong = 3f * p;
        var halfAcross = 4f * p;
        paint.Box(at, d, halfAlong + 1, halfAcross + 1, TimberEdge);
        paint.Box(at, d, halfAlong, halfAcross, TimberBase);
        // Lit north-west rim, shaded south-east, two plank seams across the lid.
        paint.Box(at, d, halfAlong, halfAcross, TimberLight, (q, _, _) => q.Dot(TowardLight) > 2.2f * p);
        paint.Box(at, d, halfAlong, halfAcross, TimberShade, (q, _, _) => q.Dot(TowardLight) < -2.6f * p);
        if (p >= 0.7f)
        {
            paint.Line(at - s * (halfAcross - 0.5f) - d * 0.5f, at + s * (halfAcross - 0.5f) - d * 0.5f, TimberShade);
            paint.Dot(at + TowardLight * 3.2f, TimberHighlight);
        }
    }

    /// <summary>
    /// A pick swung out from the leading hand (A3): a Timber handle, an iron
    /// head across its end with a glint, and a faint arc behind it to show the
    /// swing.
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
    /// An elder's one-pixel cane (A5) with its crook showing just above the
    /// hand. It is planted a little ahead and outside, on the ground, which
    /// sits lower on screen than the hands, like the feet.
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

    /// <summary>A cream bandage wrapped across the crown, ear to ear, with a knot on one side (A3).</summary>
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

    /// <summary>A 5 × 4 speech mark off the head to the north-east, with a one-pixel tail toward the head (A3).</summary>
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
    /// An infant stays a wrapped bundle (A5) lying along the facing, head at
    /// the back end looking up: a lit blanket with a swaddle band and a fold,
    /// a face with a nose dot, and a hair cap at the top of the head.
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

    // ---------------------------------------------------------------- horses

    /// <summary>
    /// A first horse seen from above (A6; mounts come later). The body is one
    /// outlined silhouette made of a rounded rump, a barrel and a narrower
    /// chest, then a neck and a long head with two ears and a darker muzzle
    /// toward the facing, in the Timber ramp with a lit north-west rim. A
    /// Hair 1 mane runs down the neck and a tail tuft swings behind; a Timber
    /// edge saddle sits on the barrel. With <paramref name="rider"/> an adult
    /// sits on the saddle with boots on both flanks and hands on the reins.
    /// </summary>
    public static Image Horse(int facing, bool rider, int size)
    {
        var image = Bitmap.Empty(size, size);
        var paint = new Painter(image);
        var p = size / 32f;
        var d = Direction(facing);
        var s = Side(d);
        var o = new Vector2(16, 16) * p - d * (0.6f * p);   // the withers, a little behind the cell centre
        Vector2 At(float along, float across = 0) => o + d * (along * p) + s * (across * p);

        // Parts as (centre along the facing, half length, half width), tail to muzzle.
        (float At, float Along, float Across)[] body =
        [
            (-6.0f, 4.6f, 4.9f),   // rump
            (-1.2f, 5.8f, 5.5f),   // barrel
            (4.0f, 2.8f, 4.2f),    // chest
            (7.2f, 2.8f, 2.3f),    // neck, narrower than the head ahead of it
            (10.3f, 1.9f, 2.6f),   // jowls and forehead
            (12.6f, 2.4f, 1.9f),   // the long face down to the muzzle
        ];
        bool InBody(Vector2 point)
        {
            foreach (var (at, along, across) in body)
                if (Painter.InOval(point - At(at), d, along * p, across * p)) return true;
            // The ears: two small nubs at the back corners of the head.
            return Painter.InOval(point - At(8.9f, -2.5f), d, 1.2f * p, 0.9f * p)
                || Painter.InOval(point - At(8.9f, 2.5f), d, 1.2f * p, 0.9f * p);
        }

        paint.Oval(o + new Vector2(1, 3) * p, d, 13.5f * p, 5.4f * p, Shadow);

        // Tail: a tuft swinging a little to one side, under the rump.
        var tailRoot = At(-10.0f);
        var tailTip = At(-14.0f, -1.4f);
        var tailAxis = (tailTip - tailRoot).Normalized();
        bool InTail(Vector2 point) => Painter.InOval(point - (tailRoot + tailTip) / 2, tailAxis, 2.6f * p, 1.4f * p);
        paint.Silhouette(InTail, Outline, ManeColor, ManeColor.Darkened(0.25f), ManeColor.Lightened(0.15f), 1.2f, 1.0f);

        paint.Silhouette(InBody, Outline, TimberBase, TimberShade, TimberLight, 1.6f, 1.3f);

        // Head: darker muzzle with nostrils, a pale blaze down the face, inner ears.
        paint.Oval(At(14.0f), d, 1.4f * p, 1.9f * p, TimberShade, (q, _, _) => InBody(q + At(14.0f)));
        paint.Dot(At(14.4f, -0.8f), TimberEdge);
        paint.Dot(At(14.4f, 0.8f), TimberEdge);
        paint.Line(At(10.6f), At(13.0f), ClothLight);
        paint.Dot(At(8.9f, -2.5f), TimberShade);
        paint.Dot(At(8.9f, 2.5f), TimberShade);

        // Mane: down the middle of the neck from a forelock between the ears to the withers.
        paint.Box(At(5.9f, 0.4f), d, 3.4f * p, 1.0f * p, ManeColor);
        paint.Box(At(5.9f, 0.4f), d, 3.4f * p, 1.0f * p, ManeColor.Darkened(0.25f), (q, _, _) => q.Dot(TowardLight) < -0.4f);
        paint.Dot(At(9.4f), ManeColor);

        // Saddle: a Timber edge seat with a lighter pommel toward the head.
        var seat = At(-1.0f);
        paint.Box(seat, d, 3.0f * p, 3.5f * p, TimberEdge, (q, t, u) => MathF.Abs(t) < 2.6f * p || MathF.Abs(u) < 3.1f * p);
        paint.Box(seat, d, 2.0f * p, 2.5f * p, TimberShade);
        paint.Box(seat, d, 2.0f * p, 2.5f * p, TimberBase, (q, _, _) => q.Dot(TowardLight) > 1.2f * p);
        paint.Dot(At(1.6f), TimberLight);

        if (!rider) return image;

        // The rider: an adult torso on the saddle, boots in the stirrups on both flanks.
        var build = (size >= 24 ? Build.Adult32 : Build.Adult16).Scaled(0.8f, 0.86f) with { Center = seat - d * (0.4f * p) };
        foreach (var side in new[] { -1f, 1f })
        {
            var boot = At(1.6f, side * 7.0f);
            paint.Oval(boot, d, 1.8f * p + 0.85f, 1.3f * p + 0.85f, Outline);
            paint.Oval(boot, d, 1.8f * p, 1.3f * p, TimberShade);
        }
        // Reins from the hands to the bridle at the back of the head.
        var hands = build.Center + d * (build.ShoulderAlong + 1.2f * p);
        paint.Line(hands - s * (build.HandAt * ReinGrip), At(10.2f, -2.4f), TimberEdge);
        paint.Line(hands + s * (build.HandAt * ReinGrip), At(10.2f, 2.4f), TimberEdge);
        Figure(paint, build, facing, Pose.Still, Skins[0], Hairs[0], Shirts[0], elder: false, mounted: true);
        return image;
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

        /// <summary>
        /// Fills every pixel whose centre lies inside a shape, given as a test
        /// on screen points, with a one-pixel outline around it. Pixels whose
        /// neighbour away from the light falls outside take the shade colour,
        /// and those whose neighbour toward the light falls outside the light
        /// colour (L1), so joined parts share one outline and one set of rims.
        /// </summary>
        public void Silhouette(Func<Vector2, bool> inside, Color outline, Color fill, Color shade, Color light, float shadeRim, float lightRim)
        {
            for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    if (inside(point))
                    {
                        var color = !inside(point - TowardLight * shadeRim) ? shade
                            : !inside(point + TowardLight * lightRim) ? light
                            : fill;
                        Pixel(x, y, color);
                    }
                    else if (inside(point + new Vector2(1, 0)) || inside(point - new Vector2(1, 0))
                             || inside(point + new Vector2(0, 1)) || inside(point - new Vector2(0, 1)))
                    {
                        Pixel(x, y, outline);
                    }
                }
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
