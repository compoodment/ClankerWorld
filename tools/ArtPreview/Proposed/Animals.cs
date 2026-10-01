using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Animals;

/// <summary>
/// Proposed animal sprites: the agreed livestock (chicken, sheep, cow) and the
/// rest of the horse, seen straight from above in the same outlined,
/// north-west-lit style as the approved agents and first horse.
/// <para>
/// Every animal is built the way the approved horse is: a few ovals placed
/// along the facing (rump, barrel, chest, neck, head) joined into one
/// silhouette with a single 1E2226 outline, a shaded rim on the south-east and
/// a lit rim on the north-west (L1), and the L2 ground shadow. Because every
/// part is placed along the facing and its side vector, one routine per animal
/// draws every facing. The horse code is copied unchanged from the approved
/// agents proposal so the new facings match horse.S, horse.E and
/// horse.rider.E pixel for pixel.
/// </para>
/// Livestock have no predators and no legs show from above, like the horse.
/// Everything is deterministic.
/// </summary>
public sealed class AnimalsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "animals";
    public string Name => "animals";

    /// <summary>Facing names in the game's order: 0 S, 1 SW, 2 W, 3 NW, 4 N, 5 NE, 6 E, 7 SE.</summary>
    public static readonly string[] FacingNames = ["S", "SW", "W", "NW", "N", "NE", "E", "SE"];

    /// <summary>The four facings animals are drawn in, in the order the review lists them.</summary>
    private static readonly int[] ShownFacings = [0, 6, 4, 2];   // S, E, N, W

    // Agent palettes, used for the rider (unchanged from the approved agents proposal).
    private static readonly Color[] Shirts = [new("3F6FA8")];
    private static readonly Color[] Skins = [new("F0C8A0")];
    private static readonly Color[] Hairs = [new("3A2A1C")];
    private static readonly Color Outline = new("1E2226");                      // agent outline (L5), shared by animals
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);   // L2 sprite shadow

    // Horse colours, exactly as approved: the Timber ramp, Hair 1 mane, Cloth light blaze.
    private static readonly Color TimberEdge = new("3F2A1A");
    private static readonly Color TimberShade = new("6E4E31");
    private static readonly Color TimberBase = new("8A6440");
    private static readonly Color TimberLight = new("A77C52");
    private static readonly Color ClothLight = new("E8DCC0");
    private static readonly Color ManeColor = new("6B4226");

    /// <summary>A fill colour with the darker step used on its south-east rim and the lighter one on its north-west rim.</summary>
    private readonly record struct Ramp(Color Fill, Color Shade, Color Light);

    // Livestock ramps, every step taken from the style guide's ramps (section 2).
    /// <summary>Cow hide: Cloth light with Cloth base in the shade and Cloth highlight on the lit rim.</summary>
    private static readonly Ramp CowHide = new(new("E8DCC0"), new("CABC99"), new("FFF5DF"));
    /// <summary>Cow patches: Hair 4, darkened to Hair 2 in the shade, Hair 0 on the lit rim.</summary>
    private static readonly Ramp CowPatch = new(new("2E2420"), new("1E1A18"), new("3A2A1C"));
    /// <summary>Horns: Timber highlight, so they show tan against the cream hide.</summary>
    private static readonly Ramp Horn = new(new("D2AC77"), new("A77C52"), new("FFF5DF"));
    private static readonly Color MuzzlePink = new("FFC2C2");    // Berry highlight
    private static readonly Color NostrilPink = new("F08A8A");   // Berry light
    /// <summary>Wool: Cloth light, Cloth base in the shade, Cloth highlight on the lit rim.</summary>
    private static readonly Ramp Wool = new(new("E8DCC0"), new("CABC99"), new("FFF5DF"));
    /// <summary>A shorn fleece: one step darker than wool, so the short coat reads as skin and stubble.</summary>
    private static readonly Ramp Shorn = new(new("CABC99"), new("A09170"), new("E8DCC0"));
    /// <summary>A sheep's black face and ears: the Iron ramp's dark steps, lighter than the outline so it still reads.</summary>
    private static readonly Ramp SheepFace = new(new("524C48"), new("3E3A37"), new("6C6560"));
    /// <summary>A red hen's back and breast: the Rust ramp.</summary>
    private static readonly Ramp HenBody = new(new("B7774C"), new("A9643C"), new("C98A5A"));
    /// <summary>Her paler golden neck and head: the Rust ramp one step up.</summary>
    private static readonly Ramp HenHead = new(new("C98A5A"), new("B7774C"), new("E0A070"));
    /// <summary>Her folded wings: the Rust ramp one step down.</summary>
    private static readonly Ramp HenWing = new(new("A9643C"), new("7A4426"), new("B7774C"));
    /// <summary>The hen's dark tail: the Timber ramp.</summary>
    private static readonly Ramp HenTail = new(new("6E4E31"), new("3F2A1A"), new("8A6440"));
    private static readonly Color Comb = new("C4474B");          // Berry base
    private static readonly Color CombLight = new("F08A8A");     // Berry light
    private static readonly Color Beak = new("F2CC5E");          // Gold light

    /// <summary>How far apart a rider's hands are on the reins, as a share of the standing hand spread.</summary>
    private const float ReinGrip = 0.45f;

    /// <summary>Screen direction the light comes from (north-west, L1), as a unit vector.</summary>
    private static readonly Vector2 TowardLight = new Vector2(-1, -1).Normalized();

    // ---------------------------------------------------------------- entries

    public IEnumerable<Entry> Render()
    {
        var shown = new List<(string Id, Image Sprite, int Size, string? Note)>();
        void Add(string id, Image sprite, int size = 32, string? note = null) => shown.Add((id, sprite, size, note));

        foreach (var facing in ShownFacings)
            Add($"cow.{FacingNames[facing]}", Cow(facing, 32), note: facing == 0
                ? "Cream hide with irregular dark patches, ears out to the sides with short tan horns in front, a pink muzzle and a tufted tail."
                : null);
        foreach (var facing in ShownFacings)
            Add($"sheep.{FacingNames[facing]}", Sheep(facing, false, 32), note: facing == 0
                ? "A lumpy cream fleece lit from the north-west, a black face with ears out to the sides and a wool top-knot."
                : null);
        Add("sheep.shorn.E", Sheep(6, true, 32), note: "After shearing: a slimmer, smooth body one step darker, the same black face.");
        foreach (var facing in ShownFacings)
            Add($"chicken.{FacingNames[facing]}", Chicken(facing, 32), note: facing == 0
                ? "A red hen: a round Rust body, darker folded wings, a paler head with a red comb, a yellow beak and a dark fanned tail."
                : null);
        Add("cow.E.16", Cow(6, 16), 16, "16 px: the patched cream block with a pink nose.");
        Add("sheep.E.16", Sheep(6, false, 16), 16, "16 px: the round cream fleece with a dark head.");
        Add("chicken.E.16", Chicken(6, 16), 16, "16 px: a small rust body with a red comb and a dark tail.");
        Add("horse.N", Horse(4, false, 32), note: "The approved horse drawing turned north: mane, ears and blaze toward the top.");
        Add("horse.W", Horse(2, false, 32), note: "The approved horse drawing turned west.");
        Add("horse.rider.S", Horse(0, true, 32), note: "Rider facing you: face and fringe, hands on the reins over the neck, boots on both flanks.");
        Add("horse.rider.N", Horse(4, true, 32), note: "Rider seen from behind: hair whorl, hands hidden in front, reins running to the bridle.");
        Add("horse.rider.W", Horse(2, true, 32), note: "Rider facing west: the nose pokes out, both hands on the reins.");

        var onGrass = shown.Select(e => new Entry(Family, e.Id, Bitmap.Over(TerrainTextures.Tile(TerrainStyle.Grass, 0, e.Size), e.Sprite, 0, 0), e.Note)).ToList();
        var bare = shown.Select(e => new Entry(Family, e.Id + ".sprite", e.Sprite)).ToList();
        // The contact sheet sizes each column to its widest picture, so the wide
        // lineup goes first (as the agents turnaround does) and only the first column widens.
        var all = new List<Entry>
        {
            new(Family, "horse.lineup", HorseLineup(),
                "Review aid, not an asset: the horse in all four facings, top row bare, bottom row ridden, in the order S, E, N, W. S and E are the approved drawings."),
        };
        all.AddRange(onGrass);
        all.AddRange(bare);
        return all;
    }

    /// <summary>Animals are not yet drawn in the reference scene, so no delegate changes.</summary>
    public void Apply(ArtSet set)
    {
    }

    /// <summary>The horse in S, E, N, W on grass, bare on the top row and ridden on the bottom row.</summary>
    private static Image HorseLineup()
    {
        var image = Bitmap.Empty(32 * ShownFacings.Length, 64);
        for (var column = 0; column < ShownFacings.Length; column++)
            for (var row = 0; row < 2; row++)
            {
                var ground = TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(column, row), 32);
                image.BlitRect(ground, new Rect2I(0, 0, 32, 32), new Vector2I(column * 32, row * 32));
                Sheet.Blend(image, Horse(ShownFacings[column], row == 1, 32), column * 32, row * 32);
            }
        return image;
    }

    /// <summary>Unit vector of a facing on screen (y down): 0 is south, then clockwise through west.</summary>
    private static Vector2 Direction(int facing) => Vector2.FromAngle(MathF.PI / 2 + facing * MathF.PI / 4);

    /// <summary>The figure's own right-hand side, perpendicular to the facing.</summary>
    private static Vector2 Side(Vector2 direction) => new(-direction.Y, direction.X);

    /// <summary>
    /// Places body parts for one animal: <see cref="At"/> turns a position
    /// along the facing and across it (toward the animal's right), in 32 px
    /// units, into a screen point, and <see cref="InPart"/> tests a screen
    /// point against an oval placed that way, so parts and markings stay on
    /// the same place on the animal whatever the facing.
    /// </summary>
    private readonly record struct BodyFrame(Vector2 Origin, Vector2 Forward, Vector2 Right, float Unit)
    {
        public Vector2 At(float along, float across = 0) => Origin + Forward * (along * Unit) + Right * (across * Unit);

        /// <summary>Whether a screen point lies inside an oval given in body units.</summary>
        public bool InPart(Vector2 point, float along, float across, float halfAlong, float halfAcross) =>
            Painter.InOval(point - At(along, across), Forward, halfAlong * Unit, halfAcross * Unit);

        /// <summary>A frame for a sprite of <paramref name="size"/> px whose body centre sits <paramref name="back"/> units behind the cell centre.</summary>
        public static BodyFrame For(int facing, int size, float back)
        {
            var unit = size / 32f;
            var forward = Direction(facing);
            return new BodyFrame(new Vector2(16, 16) * unit - forward * (back * unit), forward, Side(forward), unit);
        }
    }

    // ---------------------------------------------------------------- cow

    /// <summary>
    /// A dairy cow from above: a broad, boxy body (hips squared off by the
    /// hip bones, a deep barrel, a chest), a short neck and a broad head with
    /// ears out to the sides, short tan horns in front of them and a pink
    /// muzzle with nostrils. The hide is cream (Cloth ramp) with irregular dark
    /// patches fixed to the body, so they turn with it, and a thin tail with a
    /// dark tuft lies back from the rump. It is a little shorter than the horse
    /// but wider, and its pale patched coat keeps the two apart even at 16 px.
    /// </summary>
    public static Image Cow(int facing, int size)
    {
        var image = Bitmap.Empty(size, size);
        var paint = new Painter(image);
        var small = size < 24;
        var f = BodyFrame.For(facing, size, 1.4f);

        // Parts as (centre along the facing, half length, half width), rump to muzzle.
        (float At, float Along, float Across)[] body =
        [
            (-6.4f, 4.2f, 5.8f),   // rump
            (-1.4f, 6.0f, 6.6f),   // barrel
            (3.8f, 2.8f, 5.2f),    // chest
            (6.6f, 2.0f, 3.0f),    // short neck
            (9.4f, 1.9f, 2.7f),    // poll and forehead
            (11.6f, 2.1f, 2.4f),   // face
            (13.0f, 1.4f, 2.3f),   // broad muzzle
        ];
        bool InCore(Vector2 q)
        {
            foreach (var (at, along, across) in body)
                if (f.InPart(q, at, 0, along, across)) return true;
            // The hip bones square off the back corners of the rump.
            return f.InPart(q, -8.2f, -4.3f, 1.9f, 1.9f) || f.InPart(q, -8.2f, 4.3f, 1.9f, 1.9f);
        }
        bool InEar(Vector2 q) => f.InPart(q, 8.6f, -3.8f, 0.9f, 1.7f) || f.InPart(q, 8.6f, 3.8f, 0.9f, 1.7f);
        bool InHorn(Vector2 q) => f.InPart(q, 10.1f, -2.9f, 0.8f, 1.4f) || f.InPart(q, 10.1f, 2.9f, 0.8f, 1.4f);
        bool Inside(Vector2 q) => InCore(q) || InEar(q) || InHorn(q);

        // Patches, each a few overlapping ovals (along, across, half length,
        // half width) so the edges are irregular: a saddle over the hips, a
        // large one on the barrel, a shoulder spot, a hip spot and an eye patch.
        (float At, float Across, float Along, float Wide)[] patches = small
            ?
            [
                (-5.8f, 2.2f, 3.2f, 3.2f), (0.6f, -2.6f, 3.4f, 3.4f), (2.6f, -4.0f, 1.6f, 1.6f),
            ]
            :
            [
                (-6.8f, 2.6f, 2.6f, 2.4f), (-4.6f, 3.9f, 1.8f, 1.6f),
                (0.0f, -2.6f, 3.0f, 2.8f), (-2.4f, -4.3f, 1.8f, 1.8f), (2.0f, -1.0f, 1.6f, 1.6f),
                (4.0f, 3.5f, 1.7f, 1.6f),
                (-7.6f, -3.6f, 1.2f, 1.1f),
                (10.9f, -1.5f, 1.4f, 1.3f),
            ];
        bool InPatch(Vector2 q)
        {
            foreach (var (at, across, along, wide) in patches)
                if (f.InPart(q, at, across, along, wide)) return true;
            return false;
        }

        paint.Oval(f.At(1.0f) + new Vector2(1, 3) * f.Unit, f.Forward, 12.4f * f.Unit, 6.6f * f.Unit, Shadow);

        // Tail: a thin rope lying back from the rump with a dark tuft at the end.
        if (!small)
        {
            var tailRoot = f.At(-10.0f, 0.6f);
            var tailTip = f.At(-13.2f, 1.6f);
            var tailAxis = (tailTip - tailRoot).Normalized();
            bool InRope(Vector2 q) => Painter.InOval(q - (tailRoot + tailTip) / 2, tailAxis, 2.0f, 0.6f);
            bool InTuft(Vector2 q) => Painter.InOval(q - tailTip, tailAxis, 1.2f, 1.0f);
            paint.Coat(q => InRope(q) || InTuft(q), Outline, q => InTuft(q) ? CowPatch : CowHide, 1.0f, 0.8f);
        }

        var shadeRim = small ? 0.8f : 1.6f;
        var lightRim = small ? 0.7f : 1.3f;
        paint.Coat(Inside, Outline, q => InHorn(q) && !InCore(q) ? Horn : InPatch(q) ? CowPatch : CowHide, shadeRim, lightRim);

        // Muzzle: Berry highlight pink with two nostrils.
        paint.Oval(f.At(13.3f), f.Forward, 1.3f * f.Unit, 2.2f * f.Unit, MuzzlePink, (q, _, _) => InCore(q + f.At(13.3f)));
        if (small) return image;
        paint.Dot(f.At(14.0f, -1.0f), NostrilPink);
        paint.Dot(f.At(14.0f, 1.0f), NostrilPink);
        return image;
    }

    // ---------------------------------------------------------------- sheep

    /// <summary>
    /// A sheep from above: a fleece made of a body oval ringed with lumps, so
    /// the outline is scalloped and every lump gets its own lit and shaded
    /// edge, a few rounded tufts inside lit from the north-west, and a black
    /// face with ears out to the sides under a wool top-knot.
    /// <paramref name="shorn"/> draws the same sheep after shearing: a slimmer,
    /// smooth body one ramp step darker with a sprinkle of stubble. It is
    /// shorter and rounder than the cow and has no patches.
    /// </summary>
    public static Image Sheep(int facing, bool shorn, int size)
    {
        var image = Bitmap.Empty(size, size);
        var paint = new Painter(image);
        var small = size < 24;
        var f = BodyFrame.For(facing, size, 0.5f);

        // The fleece: a core oval plus a ring of lumps centred on its edge.
        const float coreAt = -1.8f;
        var coreAlong = shorn ? 5.0f : 5.2f;
        var coreAcross = shorn ? 3.9f : 4.0f;
        var lumpCount = small ? 8 : 11;
        var lumpRadius = small ? 2.1f : 1.9f;
        var lumps = new List<(float At, float Across)>();
        if (!shorn)
            for (var k = 0; k < lumpCount; k++)
            {
                // Half a step off the axis so no lump sits right where the head joins.
                var angle = (k + 0.5f) * MathF.Tau / lumpCount;
                lumps.Add((coreAt + MathF.Cos(angle) * coreAlong, MathF.Sin(angle) * coreAcross));
            }
        bool InFleece(Vector2 q)
        {
            if (f.InPart(q, coreAt, 0, coreAlong, coreAcross)) return true;
            foreach (var (at, across) in lumps)
                if (f.InPart(q, at, across, lumpRadius, lumpRadius)) return true;
            // The top-knot: a tuft of wool over the back of the head (a shorn sheep keeps a small one).
            return shorn ? f.InPart(q, 4.6f, 0, 0.9f, 1.2f) : f.InPart(q, 5.0f, 0, 1.7f, 1.9f);
        }
        bool InHead(Vector2 q) =>
            f.InPart(q, 6.6f, 0, 2.3f, 2.3f)                 // crown and cheeks
            || f.InPart(q, 8.4f, 0, 1.7f, 1.8f)              // the face, ending in a blunt nose
            || f.InPart(q, 6.0f, -3.0f, 0.8f, 1.4f)          // ears out to the sides
            || f.InPart(q, 6.0f, 3.0f, 0.8f, 1.4f);
        bool Inside(Vector2 q) => InFleece(q) || InHead(q);

        paint.Oval(f.At(-0.6f) + new Vector2(1, 3) * f.Unit, f.Forward, 8.8f * f.Unit, (shorn ? 4.2f : 5.6f) * f.Unit, Shadow);

        var shadeRim = small ? 0.8f : 1.4f;
        var lightRim = small ? 0.7f : 1.2f;
        var fleece = shorn ? Shorn : Wool;
        paint.Coat(Inside, Outline, q => InFleece(q) ? fleece : SheepFace, shadeRim, lightRim);
        if (small) return image;

        // Only texture pixels well inside the fleece, so the silhouette rims stay clean.
        bool Interior(Vector2 q) => InFleece(q - TowardLight * 1.6f) && InFleece(q + TowardLight * 1.4f) && !InHead(q);
        if (!shorn)
        {
            // Tufts: small discs with a shaded south-east crescent and a lit north-west one.
            (float At, float Across)[] tufts = [(-5.2f, -1.4f), (-4.4f, 2.0f), (-2.0f, -0.2f), (0.4f, -2.2f), (0.8f, 1.8f), (2.8f, -0.2f)];
            const float tuftRadius = 1.7f;
            foreach (var (at, across) in tufts)
            {
                var centre = f.At(at, across);
                paint.Oval(centre, f.Forward, tuftRadius, tuftRadius, Wool.Shade,
                    (q, _, _) => (q - TowardLight * 1.1f).Length() > tuftRadius && Interior(q + centre));
                paint.Oval(centre, f.Forward, tuftRadius, tuftRadius, Wool.Light,
                    (q, _, _) => (q + TowardLight * 1.1f).Length() > tuftRadius && (q - TowardLight * 1.1f).Length() <= tuftRadius && Interior(q + centre));
            }
        }
        else
        {
            // Stubble: a sparse, fixed sprinkle of the shade step on the shorn coat.
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    if (Interior(point) && PixelArt.Hash(x, y, 4127) % 6 == 0) paint.Pixel(x, y, Shorn.Shade);
                }
        }
        // A lighter nose tip on the dark face.
        paint.Dot(f.At(9.4f), SheepFace.Light);
        return image;
    }

    // ---------------------------------------------------------------- chicken

    /// <summary>
    /// A red hen from above: a plump round body with a breast, in the Rust
    /// ramp, darker folded wings down both sides, a dark Timber tail at the
    /// back, and a smaller, paler head with a red comb along its crown and a
    /// yellow beak poking out of the outline the way an agent's nose does. It
    /// is drawn a little larger than life so it reads beside a person.
    /// </summary>
    public static Image Chicken(int facing, int size)
    {
        var image = Bitmap.Empty(size, size);
        var paint = new Painter(image);
        var small = size < 24;
        var f = BodyFrame.For(facing, size, 0.0f);

        // The tail: three feather lobes fanned at the back.
        bool InTail(Vector2 q) =>
            f.InPart(q, -5.2f, 0, 1.7f, 1.1f) || f.InPart(q, -4.7f, -1.5f, 1.6f, 1.0f) || f.InPart(q, -4.7f, 1.5f, 1.6f, 1.0f);
        bool InBody(Vector2 q) =>
            f.InPart(q, -0.6f, 0, 3.4f, 4.1f)        // round body
            || f.InPart(q, 1.6f, 0, 2.1f, 3.0f);     // breast
        bool InHead(Vector2 q) => f.InPart(q, 4.4f, 0, 1.9f, 1.9f);
        // Folded wings along both sides, their shoulders just proud of the body outline.
        bool InWing(Vector2 q) => f.InPart(q, -1.2f, -2.8f, 2.8f, 1.5f) || f.InPart(q, -1.2f, 2.8f, 2.8f, 1.5f);
        bool Inside(Vector2 q) => InTail(q) || InBody(q) || InHead(q) || (!small && InWing(q));
        Ramp FeathersAt(Vector2 q) =>
            InHead(q) && !InBody(q) ? HenHead
            : !small && InWing(q) ? HenWing
            : InBody(q) ? HenBody
            : HenTail;

        paint.Oval(f.At(-0.4f) + new Vector2(1, 3) * f.Unit, f.Forward, 6.0f * f.Unit, 4.2f * f.Unit, Shadow);

        // The beak pokes out of the outline ahead of the head, like an agent's nose.
        var beak = f.At(small ? 6.4f : 6.8f);
        paint.Disc(beak, small ? 0.6f : 1.1f, Outline);
        paint.Coat(Inside, Outline, FeathersAt, small ? 0.8f : 1.2f, small ? 0.6f : 1.0f);
        paint.Dot(small ? f.At(5.8f) : f.At(6.6f), Beak);

        // Comb: a red ridge along the crown, lit at its front.
        if (small)
        {
            paint.Dot(f.At(4.2f), Comb);
            return image;
        }
        paint.Box(f.At(4.4f), f.Forward, 1.3f, 0.8f, Comb);
        paint.Dot(f.At(5.5f), CombLight);
        return image;
    }

    // ---------------------------------------------------------------- horse

    /// <summary>
    /// The horse from the approved agents proposal, copied unchanged so every
    /// facing matches horse.S, horse.E and horse.rider.E. The body is one
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
        Rider(paint, build, facing, Skins[0], Hairs[0], Shirts[0]);
        return image;
    }

    // ---------------------------------------------------------------- rider

    /// <summary>
    /// The measurements of one figure in pixels, copied from the approved
    /// agents proposal (only the fields a rider uses are read here). Along
    /// means along the facing, across toward the figure's right. HeadBack
    /// moves the head back along the facing, HeadLift toward the top of the
    /// screen. Unit is the scale relative to an adult at 32 px and Edge the
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

        /// <summary>The same figure at <paramref name="k"/> of its size, with the head shrunk by <paramref name="headK"/>.</summary>
        public Build Scaled(float k, float headK) => this with
        {
            HeadR = HeadR * headK, HeadBack = HeadBack * k, HeadLift = HeadLift * k,
            ShoulderAcross = ShoulderAcross * k, ShoulderAlong = ShoulderAlong * k,
            HandR = HandR * k, HandAt = HandAt * k, Reach = Reach * k, LeadReach = LeadReach * k,
            FootAcross = FootAcross * k, FootAlong = FootAlong * k, FootAt = FootAt * k, FootAhead = FootAhead * k, FootDrop = FootDrop * k,
            Unit = Unit * k,
        };
    }

    /// <summary>
    /// How deep the face shows below the fringe: a full band facing south,
    /// less sideways, a sliver on the back diagonals, none facing north (A2).
    /// </summary>
    private static float FaceDepth(int facing) => facing switch
    {
        0 => 2.6f,
        1 or 7 => 2.2f,
        2 or 6 => 1.8f,
        3 or 5 => 1.0f,
        _ => 0f,
    };

    /// <summary>
    /// A seated rider: the mounted branch of the approved agents proposal's
    /// figure, unchanged. Shoulders, the head's soft shade on the shirt, both
    /// hands forward on the reins, then the head; no feet or shadow, because
    /// the horse draws the boots and casts the shadow.
    /// </summary>
    private static void Rider(Painter paint, Build b, int facing, Color skin, Color hair, Color shirt)
    {
        var d = Direction(facing);
        var s = Side(d);
        var c = b.Center;
        var p = b.Unit;
        var small = p < 0.7f;

        Shoulders(paint, c, d, b, shirt);

        // The head sits back from the chest, but never lower on screen than the lift puts it.
        var back = -d * b.HeadBack;
        var head = c + new Vector2(back.X, MathF.Min(back.Y, 0)) - new Vector2(0, b.HeadLift);
        if (!small)
        {
            // The head is above the shoulders, so it casts a soft shade onto the shirt to its south-east.
            var cast = head - TowardLight * 1.6f;
            var reach = b.HeadR + b.Edge;
            paint.Oval(c, d, b.ShoulderAlong, b.ShoulderAcross, shirt.Darkened(0.22f), (q, _, _) => (q + c - cast).Length() <= reach);
        }

        // On a saddle both hands come forward and in to hold the reins.
        var leftHand = c - s * (b.HandAt * ReinGrip) + d * (b.ShoulderAlong + 1.2f * p);
        var rightHand = c + s * (b.HandAt * ReinGrip) + d * (b.ShoulderAlong + 1.2f * p);
        Hand(paint, leftHand, -s, b, skin);
        Hand(paint, rightHand, s, b, skin);

        Head(paint, head, b, d, s, facing, skin, hair, small);
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

    /// <summary>A hand: a skin disc with its own outline so it reads over the shirt (one pixel at 16 px).</summary>
    private static void Hand(Painter paint, Vector2 at, Vector2 outward, Build b, Color skin)
    {
        if (b.Unit < 0.7f)
        {
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
    /// The head from above: outline and skin disc, hair over the crown and
    /// back with the face showing on the facing side below a fringe, a nose
    /// hint, a whorl when facing north, and north-west light on the hair.
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

        // Hair: an oval from the back of the head to the fringe line, as wide as the head.
        var front = r - face;
        var hairAlong = (r + front) / 2;
        var back = r - hairAlong;
        var hairCenter = h - d * back;
        bool InHair(Vector2 q, float t, float u) => (t - back) * (t - back) + u * u <= r * r + 0.3f;
        paint.Oval(hairCenter, d, hairAlong + 0.05f, r + 0.6f, hair, (q, t, u) => InHair(q, t, u));

        if (!small)
        {
            // Shade on the south-east rim of the hair, and a thin sheen arc inside the north-west rim (L1).
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
            // Facing north: a small whorl on the crown.
            var crown = h + new Vector2(0.5f, 0.5f);
            var curl = hair.Lightened(0.2f);
            paint.Dot(crown, hair.Darkened(0.3f));
            paint.Dot(crown + new Vector2(1, 0), curl);
            paint.Dot(crown + new Vector2(0, 1), curl);
        }
    }

    // ---------------------------------------------------------------- painter

    /// <summary>
    /// Alpha-blending pixel painter in pixel units, clipped to its image, copied
    /// from the approved agents proposal with one addition, <see cref="Coat"/>.
    /// Shapes are sampled at pixel centres; ovals and boxes take a direction
    /// so the same call draws an animal at any facing. The optional keep
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
        public void Silhouette(Func<Vector2, bool> inside, Color outline, Color fill, Color shade, Color light, float shadeRim, float lightRim) =>
            Coat(inside, outline, _ => new Ramp(fill, shade, light), shadeRim, lightRim);

        /// <summary>
        /// <see cref="Silhouette"/> with a colour ramp chosen per pixel, for
        /// coats with markings (patches, a darker tail or face): the rims still
        /// follow the whole silhouette, each in the shade or light step of the
        /// ramp under that pixel.
        /// </summary>
        public void Coat(Func<Vector2, bool> inside, Color outline, Func<Vector2, Ramp> rampAt, float shadeRim, float lightRim)
        {
            for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    if (inside(point))
                    {
                        var ramp = rampAt(point);
                        var color = !inside(point - TowardLight * shadeRim) ? ramp.Shade
                            : !inside(point + TowardLight * lightRim) ? ramp.Light
                            : ramp.Fill;
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
