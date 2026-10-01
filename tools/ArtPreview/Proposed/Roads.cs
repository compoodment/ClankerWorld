using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Roads;

/// <summary>
/// Round-1 proposal for the Road pieces and the bridge deck (STYLE.md section 7).
/// The piece geometry is the game's <see cref="RoadSprites"/> logic, kept as it
/// is so every neighbour combination still joins only along the Road. What
/// changes is how each piece is painted from its distance field: a worn edge in
/// the Road dirt shade step whose line wobbles and frays with half-alpha rim
/// pixels (R1), calm dry patches instead of per-pixel grain, at most six
/// pebbles in two greys with a small south-east shadow (R2), a five-pixel
/// doorstep path (R3) and an edge-step rim on sand and snow. The bridge is a
/// plank deck with seams, rails, posts and a soft shadow on the water (R4).
/// </summary>
public sealed class RoadsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "roads";
    public string Name => "roads";

    // Road dirt ramp (STYLE.md section 2).
    private static readonly Color DirtEdge = new("6E5538");
    private static readonly Color DirtShade = new("977852");
    private static readonly Color DirtBase = new("B99A6B");
    private static readonly Color DirtLight = new("C9AC7C");
    // Rock ramp light and highlight: the two pebble greys (R2).
    private static readonly Color PebbleLight = new("8B837D");
    private static readonly Color PebbleHighlight = new("A49C95");
    // Timber ramp for the bridge (R4).
    private static readonly Color TimberEdge = new("3F2A1A");
    private static readonly Color TimberShade = new("6E4E31");
    private static readonly Color TimberBase = new("8A6440");
    private static readonly Color TimberLight = new("A77C52");
    // The L2 ground-shadow colour at 30%, cast by the deck onto the water.
    private static readonly Color DeckShadow = new(0.05f, 0.08f, 0.05f, 0.30f);

    /// <summary>Half-width of the Road body in 32-unit tile space, as in RoadSprites.</summary>
    private const float Radius = 8f;

    public IEnumerable<Entry> Render()
    {
        const RoadLinks ew = RoadLinks.Road | RoadLinks.East | RoadLinks.West;
        const RoadLinks ns = RoadLinks.Road | RoadLinks.North | RoadLinks.South;
        const RoadLinks ne = RoadLinks.Road | RoadLinks.North | RoadLinks.East;
        var samples = new (string Id, RoadLinks Links, string Note)[]
        {
            ("straight_ns", ns, "Worn shade-step edge that wobbles and frays; calm dry patches; a few pebbles."),
            ("straight_ew", ew, "Same treatment east-west."),
            ("corner_ne", ne, "Rounded bend keeps the game's geometry."),
            ("tee_nes", ns | RoadLinks.East, "Junction; the edge stays continuous round both inside corners."),
            ("cross", ns | ew, "Crossroads."),
            ("end_n", RoadLinks.Road | RoadLinks.North, "Dead end with the rounded cap."),
            ("diag_ne_sw", RoadLinks.Road | RoadLinks.NorthEast | RoadLinks.SouthWest, "Diagonal; the wobbling edge hides the stair-steps."),
            ("doorstep_s", ew | RoadLinks.DoorNorth, "Doorstep path five pixels wide toward a door on the north (R3)."),
        };
        foreach (var (id, links, note) in samples)
            yield return new(Family, id, OverGround(TerrainStyle.Grass, links, 0, 32), note);
        foreach (var style in new[] { TerrainStyle.Sand, TerrainStyle.Snow })
            yield return new(Family, $"straight_ew.on_{style}", OverGround(style, ew, 0, 32),
                "Pale ground: the worn edge uses the darkest dirt step and never breaks, so the Road stays visible (R1).");
        foreach (var (id, links) in new[] { ("straight_ns.16", ns), ("straight_ew.16", ew), ("corner_ne.16", ne) })
            yield return new(Family, id, OverGround(TerrainStyle.Grass, links, 0, 16),
                "Mid-zoom atlas: a one-pixel worn edge, a dry patch or two and one pebble.");

        foreach (var size in new[] { 32, 16 })
            foreach (var eastWest in new[] { true, false })
            {
                var id = $"bridge.{(eastWest ? "ew" : "ns")}{(size == 32 ? "" : ".16")}";
                var sprite = Bridge(eastWest, size);
                yield return new(Family, id, Bitmap.Over(RiverTile(size, 0), sprite, 0, 0),
                    size == 32
                        ? "Plank deck 20 px wide: 3 px planks, 1 px seams, 2 px rails, posts at both ends, 30% shadow on the water (R4)."
                        : "10 px deck in alternating plank tones (no seams), one-pixel rails, post nubs.");
                yield return new(Family, id + ".sprite", sprite);
            }
        yield return new(Family, "crossing.today", Crossing(false),
            "Two deck tiles between banks with today's links: the bank Road ends in a cap short of the deck.");
        yield return new(Family, "crossing.rule", Crossing(true),
            "The same crossing if a deck tile counted as Road for its neighbours: the bank Road runs onto the deck.");
    }

    public void Apply(ArtSet set)
    {
        set.Road = Road;
        set.Bridge = Bridge;
    }

    /// <summary>A Road piece composited over the matching ground tile, as the baseline sheet shows it.</summary>
    private static Image OverGround(TerrainStyle style, RoadLinks links, int variant, int tilePixels) =>
        Bitmap.Over(TerrainTextures.Tile(style, 0, tilePixels), Road(links, variant, tilePixels, RoadSprites.NeedsDarkEdge(style)), 0, 0);

    /// <summary>One river tile from the water block, as the scene draws it under a deck.</summary>
    private static Image RiverTile(int tilePixels, int column) =>
        WaterTextures.Block(TerrainStyle.River, tilePixels).GetRegion(new Rect2I(column * tilePixels, 0, tilePixels, tilePixels));

    // ------------------------------------------------------------------ Road

    /// <summary>What a pixel of a Road piece shows, decided from the distance field.</summary>
    private enum Band { None, Rim, Edge, InnerEdge, Core, PathEdge, PathCore }

    /// <summary>
    /// Paints one Road piece. The signed depth of each pixel inside the Road
    /// body, nudged by a gentle tile-periodic wobble, decides its band: a
    /// half-alpha rim just outside, a worn edge one or two pixels wide, or the
    /// packed core, which takes dry patches and pebbles. A doorstep path is a
    /// separate, unwobbled band so it stays exactly five pixels wide.
    /// </summary>
    public static Image Road(RoadLinks links, int variant, int tilePixels, bool darkEdge)
    {
        var image = Bitmap.Empty(tilePixels, tilePixels);
        var road = links.HasFlag(RoadLinks.Road);
        var large = tilePixels >= 32;
        var body = BodySegments(links);
        var doors = road ? DoorSegments(links, large) : [];
        if (body.Count == 0) return image;
        var scale = 32f / tilePixels;
        var salt = 3 + tilePixels + variant * 97 + (road ? 0 : 31);
        var bands = new Band[tilePixels * tilePixels];
        var coreDepth = new float[tilePixels * tilePixels];
        for (var py = 0; py < tilePixels; py++)
            for (var px = 0; px < tilePixels; px++)
            {
                // Distances are measured on the 32-unit tile whatever the output size.
                var x = (px + 0.5f) * scale;
                var y = (py + 0.5f) * scale;
                var bodyDepth = Depth(body, x, y);
                if (road && InFilledCorner(links, x, y)) bodyDepth = Math.Max(bodyDepth, Radius);
                var depth = bodyDepth / scale;
                var band = BodyBand(depth, px, py, tilePixels, salt);
                if (band != Band.Core && doors.Count > 0)
                {
                    var door = Depth(doors, x, y) / scale;
                    if (door >= 1f) band = Band.PathCore;
                    else if (door >= 0f && band is Band.None or Band.Rim) band = Band.PathEdge;
                }
                bands[py * tilePixels + px] = band;
                coreDepth[py * tilePixels + px] = band == Band.Core ? depth : 0f;
            }
        var dry = DryPatches(bands, coreDepth, tilePixels, salt);
        for (var py = 0; py < tilePixels; py++)
            for (var px = 0; px < tilePixels; px++)
            {
                var index = py * tilePixels + px;
                var color = bands[index] == Band.Core
                    ? dry[index] ? DirtLight : DirtBase
                    : Paint(bands[index], px, py, tilePixels, salt, darkEdge);
                if (color.A > 0) image.SetPixel(px, py, color);
            }
        Pebbles(image, bands, coreDepth, tilePixels, salt);
        return image;
    }

    /// <summary>
    /// Sorts a pixel of the Road body by its depth in output pixels. A smooth
    /// wobble (±0.75 px at 32, ±0.45 px at 16) moves the edge in and out over
    /// runs of several pixels, and a second field makes the worn edge one or
    /// two pixels wide, so the margin reads as worn rather than as a stroke.
    /// </summary>
    private static Band BodyBand(float depth, int px, int py, int tilePixels, int salt)
    {
        var large = tilePixels >= 32;
        var wobble = 2f * TileNoise(px, py, tilePixels, 4, salt + 101, 0.5f) - 1f;
        var nudged = depth + (large ? 0.75f : 0.45f) * wobble;
        var wide = large && TileNoise(px, py, tilePixels, 8, salt + 202, 0.6f) > 0.5f;
        if (nudged < -0.75f) return Band.None;
        if (nudged < 0f) return Band.Rim;
        if (nudged < 1f) return Band.Edge;
        if (wide && nudged < 2f) return Band.InnerEdge;
        return Band.Core;
    }

    /// <summary>
    /// The colour of one pixel by band. On ordinary ground the rim is a
    /// half-alpha shade pixel with gaps, and the edge has short half-alpha
    /// breaks; on pale ground (sand, snow) the edge is the solid edge step with
    /// an unbroken half-alpha rim, so the Road always has an outline there.
    /// </summary>
    private static Color Paint(Band band, int px, int py, int tilePixels, int salt, bool darkEdge)
    {
        var large = tilePixels >= 32;
        // Breaks come in two-pixel runs so the edge frays rather than sparkles.
        var run = PixelArt.Hash(px >> 1, py >> 1, salt + 11) % 100;
        switch (band)
        {
            case Band.Rim:
                if (darkEdge) return new Color(DirtEdge, 0.5f);
                return run < (large ? 55 : 25) ? new Color(DirtShade, 0.45f) : Colors.Transparent;
            case Band.Edge:
                if (darkEdge) return DirtEdge;
                return run < 12 ? new Color(DirtShade, 0.6f) : DirtShade;
            case Band.InnerEdge:
                return DirtShade;
            case Band.PathEdge:
                if (darkEdge) return DirtEdge;
                return run < 15 ? new Color(DirtShade, 0.6f) : DirtShade;
            case Band.PathCore:
                return DirtBase;
            default:
                return Colors.Transparent;
        }
    }

    /// <summary>
    /// Dry patches on the packed core (STYLE.md T1 applied to Roads): two or
    /// three lumpy spots in the light step, four to seven pixels across,
    /// centred deep in the core so they favour the crown of the Road and never
    /// reach a tile edge. Each spot's outline bulges in a few soft lobes, so it
    /// reads as worn ground rather than a dot. The 16 px pieces have none: a
    /// spot that small turns into a cross, so they take light clods instead.
    /// </summary>
    private static bool[] DryPatches(Band[] bands, float[] coreDepth, int tilePixels, int salt)
    {
        var dry = new bool[tilePixels * tilePixels];
        if (tilePixels < 32) return dry;
        var stream = new PixelArt.Stream((uint)(salt + 7) * 2246822519u);
        var wanted = 2 + stream.Range(0, 2);
        const int margin = 4;
        var centres = new List<(int X, int Y)>();
        for (var attempt = 0; attempt < 40 && centres.Count < wanted; attempt++)
        {
            var cx = stream.Range(margin, tilePixels - margin);
            var cy = stream.Range(margin, tilePixels - margin);
            if (coreDepth[cy * tilePixels + cx] < 4f) continue;
            if (centres.Any(c => Math.Abs(c.X - cx) + Math.Abs(c.Y - cy) < 9)) continue;
            centres.Add((cx, cy));
            var rx = 2f + stream.Range(0, 3) * 0.6f;
            var ry = 1.6f + stream.Range(0, 2) * 0.6f;
            var phase = stream.Range(0, 628) / 100f;
            var lobes = 4 + stream.Range(0, 2);
            for (var y = (int)(cy - ry - 2); y <= cy + ry + 2; y++)
                for (var x = (int)(cx - rx - 2); x <= cx + rx + 2; x++)
                {
                    if (x < 0 || y < 0 || x >= tilePixels || y >= tilePixels) continue;
                    if (bands[y * tilePixels + x] != Band.Core) continue;
                    var dx = (x - cx) / rx;
                    var dy = (y - cy) / ry;
                    var reach = 1f + 0.15f * MathF.Sin(lobes * MathF.Atan2(dy, dx) + phase);
                    if (dx * dx + dy * dy < reach * reach) dry[y * tilePixels + x] = true;
                }
        }
        return dry;
    }

    /// <summary>
    /// Smooth value noise on a lattice <paramref name="cell"/> pixels apart, in
    /// [0, 1]. Lattice points on the tile border all take <paramref name="border"/>,
    /// so the field agrees across every seam whatever the neighbour's variant.
    /// </summary>
    private static float TileNoise(int px, int py, int tilePixels, int cell, int salt, float border)
    {
        var count = tilePixels / cell;
        var fx = (px + 0.5f) / cell;
        var fy = (py + 0.5f) / cell;
        var ix = (int)fx;
        var iy = (int)fy;
        var tx = Smooth(fx - ix);
        var ty = Smooth(fy - iy);
        float At(int lx, int ly) => lx <= 0 || ly <= 0 || lx >= count || ly >= count
            ? border
            : PixelArt.Hash(lx, ly, salt) % 1000 / 999f;
        var top = Mathf.Lerp(At(ix, iy), At(ix + 1, iy), tx);
        var bottom = Mathf.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), tx);
        return Mathf.Lerp(top, bottom, ty);
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    // Small marks stamped on the packed core: h/l pebble greys, L dirt light, s dirt shade.
    /// <summary>A 2×2 pebble lit from the north-west with its (+1, +1) shadow (L1, L2).</summary>
    private static readonly string[] BigPebble = ["hl.", "lls", ".ss"];
    /// <summary>A 2×1 pebble with a shadow pixel under its east end.</summary>
    private static readonly string[] SmallPebble = ["hl", ".s"];
    /// <summary>A single grey stone, lit on top.</summary>
    private static readonly string[] TinyPebble = ["h", "s"];
    /// <summary>A dry clod: a lit lump of dirt with its shaded underside.</summary>
    private static readonly string[] Clod = ["LL", ".s"];
    /// <summary>A smaller clod or footprint scuff.</summary>
    private static readonly string[] SmallClod = ["L", "s"];

    /// <summary>
    /// Pebbles and clods on the packed core, placed by a deterministic stream
    /// and kept at least four pixels apart so they never gather into grain.
    /// At 32 px: two or three pebbles (R2, at most six), the first a 2×2
    /// stone, and three to five clods. At 16 px: one single-pixel pebble and
    /// one or two light clods of one or two pixels.
    /// </summary>
    private static void Pebbles(Image image, Band[] bands, float[] coreDepth, int tilePixels, int salt)
    {
        var large = tilePixels >= 32;
        var stream = new PixelArt.Stream((uint)salt * 2654435761u);
        var stamps = new List<string[]>();
        if (large)
        {
            stamps.Add(BigPebble);
            for (var i = 1 + stream.Range(0, 2); i > 0; i--) stamps.Add(stream.Range(0, 2) == 0 ? SmallPebble : TinyPebble);
            for (var i = 3 + stream.Range(0, 3); i > 0; i--) stamps.Add(stream.Range(0, 3) == 0 ? Clod : SmallClod);
        }
        else
        {
            stamps.Add(["h"]);
            for (var i = 1 + stream.Range(0, 2); i > 0; i--) stamps.Add(stream.Range(0, 2) == 0 ? ["LL"] : ["L"]);
        }
        var minimumDepth = large ? 2f : 1f;
        bool Clear(int x, int y) =>
            x >= 0 && y >= 0 && x < tilePixels && y < tilePixels &&
            bands[y * tilePixels + x] == Band.Core && coreDepth[y * tilePixels + x] >= minimumDepth;
        var placed = new List<(int X, int Y)>();
        foreach (var stamp in stamps)
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var x = stream.Range(1, tilePixels - 1 - stamp[0].Length);
                var y = stream.Range(1, tilePixels - 1 - stamp.Length);
                var fits = true;
                for (var dy = 0; dy < stamp.Length && fits; dy++)
                    for (var dx = 0; dx < stamp[dy].Length && fits; dx++)
                        fits = stamp[dy][dx] == '.' || Clear(x + dx, y + dy);
                if (!fits || placed.Any(p => Math.Abs(p.X - x) < 4 && Math.Abs(p.Y - y) < 4)) continue;
                placed.Add((x, y));
                for (var dy = 0; dy < stamp.Length; dy++)
                    for (var dx = 0; dx < stamp[dy].Length; dx++)
                    {
                        var color = stamp[dy][dx] switch
                        {
                            'h' => PebbleHighlight,
                            'l' => PebbleLight,
                            'L' => DirtLight,
                            's' => DirtShade,
                            _ => Colors.Transparent,
                        };
                        if (color.A > 0) image.SetPixel(x + dx, y + dy, color);
                    }
                break;
            }
    }

    /// <summary>How far inside the nearest segment a point lies, in tile units; negative outside.</summary>
    private static float Depth(List<Segment> segments, float x, float y)
    {
        var depth = float.MinValue;
        foreach (var s in segments)
            depth = Math.Max(depth, s.Radius - SegmentDistance(x, y, s.Ax, s.Ay, s.Bx, s.By));
        return depth;
    }

    private readonly record struct Segment(float Ax, float Ay, float Bx, float By, float Radius);

    /// <summary>The Road body: RoadSprites' centre disc, arms, diagonals and corner bands, unchanged.</summary>
    private static List<Segment> BodySegments(RoadLinks links)
    {
        var segments = new List<Segment>();
        if (!links.HasFlag(RoadLinks.Road))
        {
            // A tile that isn't Road draws its share of a diagonal crossing its corner.
            if (Corner(links, RoadLinks.North | RoadLinks.East, RoadLinks.NorthEast)) segments.Add(new(16, -16, 48, 16, Radius));
            if (Corner(links, RoadLinks.East | RoadLinks.South, RoadLinks.SouthEast)) segments.Add(new(48, 16, 16, 48, Radius));
            if (Corner(links, RoadLinks.South | RoadLinks.West, RoadLinks.SouthWest)) segments.Add(new(16, 48, -16, 16, Radius));
            if (Corner(links, RoadLinks.West | RoadLinks.North, RoadLinks.NorthWest)) segments.Add(new(-16, 16, 16, -16, Radius));
            return segments;
        }
        segments.Add(new(16, 16, 16, 16, Radius));
        if (links.HasFlag(RoadLinks.North)) segments.Add(new(16, 16, 16, -16, Radius));
        if (links.HasFlag(RoadLinks.East)) segments.Add(new(16, 16, 48, 16, Radius));
        if (links.HasFlag(RoadLinks.South)) segments.Add(new(16, 16, 16, 48, Radius));
        if (links.HasFlag(RoadLinks.West)) segments.Add(new(16, 16, -16, 16, Radius));
        // A diagonal step joins directly only where no straight path does.
        if (Diagonal(links, RoadLinks.NorthEast, RoadLinks.North, RoadLinks.East)) segments.Add(new(16, 16, 48, -16, Radius));
        if (Diagonal(links, RoadLinks.SouthEast, RoadLinks.South, RoadLinks.East)) segments.Add(new(16, 16, 48, 48, Radius));
        if (Diagonal(links, RoadLinks.SouthWest, RoadLinks.South, RoadLinks.West)) segments.Add(new(16, 16, -16, 48, Radius));
        if (Diagonal(links, RoadLinks.NorthWest, RoadLinks.North, RoadLinks.West)) segments.Add(new(16, 16, -16, -16, Radius));
        return segments;
    }

    /// <summary>
    /// Doorstep paths from the centre to each door (R3): five pixels wide at
    /// 32 px and three at 16 px. The axis sits half a pixel off the tile centre
    /// so the odd width lands on whole pixels; the path runs past the tile edge
    /// so it meets the building's doorstep stone.
    /// </summary>
    private static List<Segment> DoorSegments(RoadLinks links, bool large)
    {
        var axis = large ? 16.5f : 17f;
        var radius = large ? 2.5f : 3f;
        var segments = new List<Segment>();
        if (links.HasFlag(RoadLinks.DoorNorth)) segments.Add(new(axis, 16, axis, -6, radius));
        if (links.HasFlag(RoadLinks.DoorSouth)) segments.Add(new(axis, 16, axis, 38, radius));
        if (links.HasFlag(RoadLinks.DoorEast)) segments.Add(new(16, axis, 38, axis, radius));
        if (links.HasFlag(RoadLinks.DoorWest)) segments.Add(new(16, axis, -6, axis, radius));
        return segments;
    }

    private static bool Corner(RoadLinks links, RoadLinks sides, RoadLinks between) =>
        links.HasFlag(sides) && !links.HasFlag(between);

    private static bool Diagonal(RoadLinks links, RoadLinks corner, RoadLinks first, RoadLinks second) =>
        links.HasFlag(corner) && !links.HasFlag(first) && !links.HasFlag(second);

    /// <summary>Where two neighbours and the tile between them are all Road, the corner fills in.</summary>
    private static bool InFilledCorner(RoadLinks links, float x, float y) =>
        x >= 16 && y < 16 && links.HasFlag(RoadLinks.North | RoadLinks.East | RoadLinks.NorthEast) ||
        x >= 16 && y >= 16 && links.HasFlag(RoadLinks.South | RoadLinks.East | RoadLinks.SouthEast) ||
        x < 16 && y >= 16 && links.HasFlag(RoadLinks.South | RoadLinks.West | RoadLinks.SouthWest) ||
        x < 16 && y < 16 && links.HasFlag(RoadLinks.North | RoadLinks.West | RoadLinks.NorthWest);

    private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0f : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0f, 1f);
        var qx = ax + t * dx - px;
        var qy = ay + t * dy - py;
        return MathF.Sqrt(qx * qx + qy * qy);
    }

    // ---------------------------------------------------------------- Bridge

    /// <summary>
    /// One tile of bridge deck (R4). The east-west deck is drawn and the
    /// north-south one is its transpose, which keeps the north-west light and
    /// puts the wider shadow on the east. The deck runs edge to edge so deck
    /// tiles join; each end carries half a post, so two deck tiles meet at one
    /// whole post and the bank ends show a slim end post.
    /// </summary>
    public static Image Bridge(bool eastWest, int tilePixels)
    {
        var image = Bitmap.Empty(tilePixels, tilePixels);
        if (tilePixels >= 32) DeckEastWest32(image);
        else DeckEastWest16(image);
        return eastWest ? image : Transposed(image);
    }

    /// <summary>
    /// 32 px east-west deck: a one-pixel shadow on the water to the north and a
    /// three-pixel one to the south, a 20 px deck of 3 px planks with 1 px
    /// seams in Timber shade (one plank in four weathered darker, a nail at
    /// each end), 2 px rails in Timber edge and half posts at both ends.
    /// </summary>
    private static void DeckEastWest32(Image image)
    {
        const int top = 6, bottom = 26; // deck rows [top, bottom), rails included
        FillRows(image, top - 1, top, DeckShadow);
        FillRows(image, bottom, bottom + 3, DeckShadow);
        for (var x = 0; x < 32; x++)
        {
            var plank = x / 4;
            var seam = x % 4 == 3;
            var worn = PixelArt.Hash(plank, 0, 900) % 4 == 0;
            var color = seam ? TimberShade : worn ? TimberBase : TimberLight;
            for (var y = top + 2; y < bottom - 2; y++) image.SetPixel(x, y, color);
        }
        // A nail at each end of every plank, just inside the rails.
        for (var plank = 0; plank < 8; plank++)
        {
            image.SetPixel(plank * 4 + 1, top + 3, TimberShade);
            image.SetPixel(plank * 4 + 1, bottom - 4, TimberShade);
        }
        FillRows(image, top, top + 2, TimberEdge);
        FillRows(image, bottom - 2, bottom, TimberEdge);
        foreach (var railTop in new[] { top - 1, bottom - 3 })
        {
            HalfPost(image, 0, railTop, westHalf: false);
            HalfPost(image, 30, railTop, westHalf: true);
        }
    }

    /// <summary>
    /// Half of a 4×4 post, two pixels wide, standing one pixel proud of its
    /// two-pixel rail on each side. The west half carries the lit cap pixels
    /// toward the north-west, so the halves of two meeting deck tiles make one
    /// outlined post with a lit cap.
    /// </summary>
    private static void HalfPost(Image image, int x, int y, bool westHalf)
    {
        for (var dy = 0; dy < 4; dy++)
            for (var dx = 0; dx < 2; dx++)
                image.SetPixel(x + dx, y + dy, TimberEdge);
        var inner = westHalf ? x + 1 : x;
        image.SetPixel(inner, y + 1, westHalf ? TimberLight : TimberBase);
        image.SetPixel(inner, y + 2, westHalf ? TimberBase : TimberShade);
    }

    /// <summary>16 px east-west deck: 10 px, plank tones alternating every two pixels, 1 px rails, post nubs and a soft shadow.</summary>
    private static void DeckEastWest16(Image image)
    {
        const int top = 3, bottom = 13; // deck rows [top, bottom), rails included
        FillRows(image, top - 1, top, DeckShadow);
        FillRows(image, bottom, bottom + 2, DeckShadow);
        for (var x = 0; x < 16; x++)
        {
            var color = x % 4 < 2 ? TimberLight : TimberBase;
            for (var y = top + 1; y < bottom - 1; y++) image.SetPixel(x, y, color);
        }
        FillRows(image, top, top + 1, TimberEdge);
        FillRows(image, bottom - 1, bottom, TimberEdge);
        foreach (var x in new[] { 0, 15 })
        {
            image.SetPixel(x, top - 1, TimberEdge);
            image.SetPixel(x, bottom, TimberEdge);
        }
    }

    private static void FillRows(Image image, int from, int to, Color color)
    {
        for (var y = from; y < to; y++)
            for (var x = 0; x < image.GetWidth(); x++)
                image.SetPixel(x, y, color);
    }

    /// <summary>Swaps x and y, turning an east-west drawing into a north-south one.</summary>
    private static Image Transposed(Image source)
    {
        var result = Bitmap.Empty(source.GetHeight(), source.GetWidth());
        for (var y = 0; y < source.GetHeight(); y++)
            for (var x = 0; x < source.GetWidth(); x++)
                result.SetPixel(y, x, source.GetPixel(x, y));
        return result;
    }

    /// <summary>
    /// A two-tile north-south bridge between two bank Road tiles. With
    /// <paramref name="suggested"/> false the links are today's, where a deck
    /// tile is not Road and each bank tile ends in a cap; with it true a deck
    /// tile counts as Road for its neighbours, so the bank Road runs onto the deck.
    /// </summary>
    private static Image Crossing(bool suggested)
    {
        var image = Bitmap.Empty(32, 128);
        const RoadLinks through = RoadLinks.Road | RoadLinks.North | RoadLinks.South;
        for (var tile = 0; tile < 4; tile++)
        {
            var deck = tile is 1 or 2;
            Image ground;
            if (deck) ground = Bitmap.Over(RiverTile(32, tile), Bridge(false, 32), 0, 0);
            else
            {
                var links = suggested ? through : tile == 0 ? RoadLinks.Road | RoadLinks.North : RoadLinks.Road | RoadLinks.South;
                ground = Bitmap.Over(TerrainTextures.Tile(TerrainStyle.Grass, tile / 3, 32), Road(links, tile, 32, false), 0, 0);
            }
            Sheet.Blend(image, ground, 0, tile * 32);
        }
        return image;
    }
}
