using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;
using ArtPreview.Proposed.Animals;

namespace ArtPreview.Proposed.Yards;

/// <summary>
/// Round 4 (October 7): three ways to draw the animal yard, which today uses
/// a provisional drawing nobody reviewed. Each option is drawn over the
/// yard's footprint in 32-unit tile space, with the gate on the door side
/// (centred on the door tile when there is one), a water trough near the gate
/// and feed farther in, lit from the north-west with shadows to the
/// south-east like the approved buildings.
/// <list type="bullet">
/// <item>A: a split-rail pen on trampled earth, grass surviving along the fence.</item>
/// <item>B: a woven wattle pen with a thatched lean-to shelter along the back and straw bedding under it.</item>
/// <item>C: a grass paddock behind a light post-and-rail fence; only the gate, the path and the trough are worn bare.</item>
/// </list>
/// Each option is shown at 2×2, 2×4 and 4×2 and with an east gate, empty and with animals inside.
/// </summary>
public sealed class YardsProposal : IArtProposal
{
    public string Family => "animal-yard";

    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight);

    private static readonly Ramp Timber = new(new("3F2A1A"), new("6E4E31"), new("8A6440"), new("A77C52"), new("D2AC77"));
    private static readonly Ramp GreyTimber = new(new("343C43"), new("59656F"), new("758390"), new("97A5B0"), new("AEBBC4"));
    private static readonly Ramp Dirt = new(new("6E5538"), new("977852"), new("B99A6B"), new("C9AC7C"), new("D9C08F"));
    private static readonly Ramp Thatch = new(new("6B5528"), new("A98A45"), new("D2AE5E"), new("E6C77B"), new("F0DA9A"));
    private static readonly Ramp Leaf = new(new("3C5F2E"), new("4C7A3A"), new("5E8C45"), new("79A657"), new("9BC66F"));
    private static readonly Ramp Lake = new(new("3A5F7A"), new("4A7B9D"), new("598FB3"), new("6A9FC0"), new("8ABBD6"));
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    public enum Option { A, B, C }

    public IEnumerable<Entry> Render()
    {
        foreach (var option in new[] { Option.A, Option.B, Option.C })
        {
            var label = option switch
            {
                Option.A => "A, split-rail pen on trampled earth",
                Option.B => "B, wattle pen with a thatched lean-to",
                _ => "C, grass paddock with a post-and-rail fence",
            };
            yield return new(Family, $"{option}.2x2", Scene(option, 2, 2, new BuildingDoor(DoorSide.South), false), $"{label}: 2×2, gate to the south, empty.");
            yield return new(Family, $"{option}.2x2.animals", Scene(option, 2, 2, new BuildingDoor(DoorSide.South), true), "The same yard with a cow, a calf and two hens in it.");
            yield return new(Family, $"{option}.2x4", Scene(option, 2, 4, new BuildingDoor(DoorSide.South), true), "2×4 after one expansion, with animals.");
            yield return new(Family, $"{option}.4x2", Scene(option, 4, 2, new BuildingDoor(DoorSide.East), true), "4×2 with the gate to the east.");
        }
    }

    /// <summary>The yard on grass, with animals inside if asked.</summary>
    private static Image Scene(Option option, int tilesWide, int tilesHigh, BuildingDoor door, bool animals)
    {
        var yard = Draw(option, tilesWide, tilesHigh, door);
        var ground = Proposals_OnGrass(tilesWide, tilesHigh);
        Sheet.Blend(ground, yard, 0, 0);
        if (!animals) return ground;
        var w = tilesWide * 32;
        var h = tilesHigh * 32;
        // A few animals placed by hand, clear of the trough, the feed and the gate (sprite corners in pixels).
        var placements = (tilesWide, tilesHigh) switch
        {
            (2, 2) => new List<(Image Sprite, int X, int Y)>
            {
                (AnimalsProposal.Cow(2, 32), 18, 4), (AnimalsProposal.Calf(7, 32), 2, 22), (AnimalsProposal.Chicken(6, 32), 30, 18),
            },
            (2, 4) => new List<(Image Sprite, int X, int Y)>
            {
                (AnimalsProposal.Cow(4, 32), 22, 20), (AnimalsProposal.Calf(3, 32), 4, 30), (AnimalsProposal.Sheep(1, false, 32), 6, 56),
                (AnimalsProposal.Lamb(2, 32), 28, 66), (AnimalsProposal.Chicken(6, 32), 4, 84), (AnimalsProposal.Chick(5, 32), 14, 88),
            },
            _ => new List<(Image Sprite, int X, int Y)>
            {
                (AnimalsProposal.Cow(2, 32), 28, 2), (AnimalsProposal.Calf(1, 32), 18, 28), (AnimalsProposal.Sheep(6, false, 32), 62, 22),
                (AnimalsProposal.Lamb(7, 32), 82, 4), (AnimalsProposal.Chicken(3, 32), 52, 2), (AnimalsProposal.Chick(4, 32), 46, 36),
            },
        };
        foreach (var (sprite, x, y) in placements) Sheet.Blend(ground, sprite, x, y);
        return ground;
    }

    private static Image Proposals_OnGrass(int tilesWide, int tilesHigh)
    {
        var ground = Image.CreateEmpty(tilesWide * 32, tilesHigh * 32, false, Image.Format.Rgba8);
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
                ground.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(x + 5, y + 2), 32),
                    new Rect2I(0, 0, 32, 32), new Vector2I(x * 32, y * 32));
        return ground;
    }

    /// <summary>One option over its footprint, transparent outside the yard.</summary>
    public static Image Draw(Option option, int tilesWide, int tilesHigh, BuildingDoor door)
    {
        var w = tilesWide * 32;
        var h = tilesHigh * 32;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var c = new PixelCanvas(image, new Rect2I(0, 0, w, h), 1f);
        var gate = Gate(w, h, door);
        var seed = tilesWide * 31 + tilesHigh * 7 + (int)option * 101;
        switch (option)
        {
            case Option.A:
                EarthFloor(c, w, h, seed, grassEdge: true);
                Trough(c, TroughSpot(w, h, door, gate));
                HayPile(c, FeedSpot(w, h, door));
                RailFence(c, w, h, door, gate, Timber, rails: 2, postGap: 10);
                break;
            case Option.B:
                EarthFloor(c, w, h, seed, grassEdge: false);
                var shelter = ShelterBand(w, h, door);
                Straw(c, shelter.Grow(4).Intersection(new Rect2(5, 5, w - 10, h - 10)), seed);
                Trough(c, TroughSpot(w, h, door, gate));
                Wattle(c, w, h, door, gate);
                LeanTo(c, shelter, door);
                break;
            default:
                WornPath(c, w, h, door, gate, seed);
                Trough(c, TroughSpot(w, h, door, gate));
                HayBale(c, FeedSpot(w, h, door));
                RailFence(c, w, h, door, gate, GreyTimber, rails: 1, postGap: 8);
                break;
        }
        return image;
    }

    // ------------------------------------------------------------------ layout

    /// <summary>The gate's middle along its side, in units, kept clear of the corners.</summary>
    private static float Gate(int w, int h, BuildingDoor door)
    {
        var horizontal = door.Side is DoorSide.South or DoorSide.North;
        var length = horizontal ? w : h;
        var middle = door.Tile is { } tile ? tile * 32 + 16 : length / 2f;
        return Math.Clamp(middle, 12, length - 12);
    }

    /// <summary>Where the trough stands: just inside the gate, to one side of it.</summary>
    private static Rect2 TroughSpot(int w, int h, BuildingDoor door, float gate)
    {
        const float length = 13, depth = 5;
        return door.Side switch
        {
            DoorSide.North => new(Math.Clamp(gate + 9, 6, w - 6 - length), 8, length, depth),
            DoorSide.East => new(w - 8 - depth, Math.Clamp(gate + 9, 6, h - 6 - length), depth, length),
            DoorSide.West => new(8, Math.Clamp(gate + 9, 6, h - 6 - length), depth, length),
            _ => new(Math.Clamp(gate + 9, 6, w - 6 - length), h - 13 - depth, length, depth),
        };
    }

    /// <summary>Where the feed goes: the corner farthest from the gate, on the north-west where possible.</summary>
    private static Vector2 FeedSpot(int w, int h, BuildingDoor door) => door.Side switch
    {
        DoorSide.North => new(11, h - 13),
        DoorSide.West => new(w - 12, 12),
        _ => new(12, 12),
    };

    /// <summary>The strip the lean-to roof covers: along the side opposite the gate.</summary>
    private static Rect2 ShelterBand(int w, int h, BuildingDoor door)
    {
        const float depth = 13;
        return door.Side switch
        {
            DoorSide.North => new(4, h - 4 - depth, w - 8, depth),
            DoorSide.East => new(4, 4, depth, h - 8),
            DoorSide.West => new(w - 4 - depth, 4, depth, h - 8),
            _ => new(4, 4, w - 8, depth),
        };
    }

    // ------------------------------------------------------------------ ground

    /// <summary>Trampled earth inside the fence, mottled with hoof marks; optionally grass surviving along the fence.</summary>
    private static void EarthFloor(PixelCanvas c, int w, int h, int seed, bool grassEdge)
    {
        for (var y = 3; y < h - 3; y++)
            for (var x = 3; x < w - 3; x++)
            {
                var n = PixelArt.Hash(x, y, seed) % 23;
                var color = n == 0 ? Dirt.Shade : n == 1 ? Dirt.Light : n == 2 && (x + y) % 3 == 0 ? Dirt.Edge : Dirt.Base;
                c.Dot(x, y, color);
            }
        // Hoof marks: little paired dark nicks.
        for (var k = 0; k < w * h / 160; k++)
        {
            var hx = 6 + (int)(PixelArt.Hash(k, 3, seed) % (uint)(w - 12));
            var hy = 6 + (int)(PixelArt.Hash(k, 9, seed) % (uint)(h - 12));
            c.Dot(hx, hy, Dirt.Shade);
            c.Dot(hx + 2, hy, Dirt.Shade);
        }
        if (!grassEdge) return;
        // Grass the animals do not reach survives in tufts just inside the fence.
        for (var x = 4; x < w - 4; x++)
        {
            Tuft(c, x, 4, seed, 5);
            Tuft(c, x, h - 6, seed + 1, 5);
        }
        for (var y = 6; y < h - 6; y++)
        {
            Tuft(c, 4, y, seed + 2, 5);
            Tuft(c, w - 6, y, seed + 3, 5);
        }
    }

    private static void Tuft(PixelCanvas c, int x, int y, int seed, int odds)
    {
        if (PixelArt.Hash(x, y, seed) % (uint)odds != 0) return;
        var tall = PixelArt.Hash(y, x, seed) % 2 == 0;
        c.Dot(x, y, Leaf.Base);
        c.Dot(x + 1, y, Leaf.Shade);
        if (tall) c.Dot(x, y - 1, Leaf.Light);
    }

    /// <summary>Straw bedding scattered thick under the shelter and thinning out from it.</summary>
    private static void Straw(PixelCanvas c, Rect2 area, int seed)
    {
        for (var y = (int)area.Position.Y; y < (int)area.End.Y; y++)
            for (var x = (int)area.Position.X; x < (int)area.End.X; x++)
            {
                var n = PixelArt.Hash(x, y, seed + 5) % 7;
                if (n < 3) c.Dot(x, y, n == 0 ? Thatch.Light : n == 1 ? Thatch.Base : Thatch.Shade);
            }
    }

    /// <summary>Option C: grass stays, worn bare in a path from the gate to the trough and in a patch around them.</summary>
    private static void WornPath(PixelCanvas c, int w, int h, BuildingDoor door, float gate, int seed)
    {
        var trough = TroughSpot(w, h, door, gate);
        var gatePoint = door.Side switch
        {
            DoorSide.North => new Vector2(gate, 4),
            DoorSide.East => new Vector2(w - 4, gate),
            DoorSide.West => new Vector2(4, gate),
            _ => new Vector2(gate, h - 4),
        };
        var feed = FeedSpot(w, h, door);
        void Patch(Vector2 centre, float rx, float ry)
        {
            for (var y = (int)(centre.Y - ry - 2); y <= centre.Y + ry + 2; y++)
                for (var x = (int)(centre.X - rx - 2); x <= centre.X + rx + 2; x++)
                {
                    if (x < 3 || y < 3 || x >= w - 3 || y >= h - 3) continue;
                    var dx = (x + 0.5f - centre.X) / rx;
                    var dy = (y + 0.5f - centre.Y) / ry;
                    var d = dx * dx + dy * dy;
                    var ragged = 1f + (PixelArt.Hash(x, y, seed + 11) % 5) * 0.08f;
                    if (d > ragged) continue;
                    var n = PixelArt.Hash(x, y, seed) % 17;
                    c.Dot(x, y, d > 0.75f ? Dirt.Light : n == 0 ? Dirt.Shade : Dirt.Base);
                }
        }
        // A worn path of overlapping patches from the gate to the trough, then on to the feed.
        var troughMiddle = trough.Position + trough.Size / 2;
        for (var t = 0f; t <= 1f; t += 0.08f) Patch(gatePoint.Lerp(troughMiddle, t), 4.5f, 4.5f);
        for (var t = 0f; t <= 1f; t += 0.04f) Patch(troughMiddle.Lerp(feed, t), 2.8f, 2.8f);
        Patch(troughMiddle, 10, 8);
        Patch(feed, 8, 7);
    }

    // ------------------------------------------------------------------ fittings

    /// <summary>A plank trough of water with a dark rim, lit along its north-west edges.</summary>
    private static void Trough(PixelCanvas c, Rect2 r)
    {
        var x = r.Position.X;
        var y = r.Position.Y;
        var tw = r.Size.X;
        var th = r.Size.Y;
        c.Rect(x + 1, y + 1, tw, th, Shadow);
        c.Rect(x, y, tw, th, Timber.Edge);
        c.Rect(x + 1, y + 1, tw - 2, th - 2, Timber.Shade);
        c.Rect(x + 1, y, tw - 2, 1, Timber.Light);
        c.Rect(x, y + 1, 1, th - 2, Timber.Light);
        c.Rect(x + 2, y + 2, tw - 4, th - 4, Lake.Base);
        c.Rect(x + 2, y + 2, tw - 4, 1, Lake.Highlight);
        c.Dot(x + tw - 3, y + th - 3, Lake.Shade);
    }

    /// <summary>A loose heap of hay with a few stray strands.</summary>
    private static void HayPile(PixelCanvas c, Vector2 at)
    {
        c.Ellipse(at.X + 1.5f, at.Y + 2, 7, 5.5f, Shadow);
        c.Lumpy(at.X, at.Y, 6.5f, Thatch.Edge, 7, 2);
        c.Lumpy(at.X, at.Y, 5.6f, Thatch.Shade, 7, 2);
        c.Lumpy(at.X - 0.8f, at.Y - 0.8f, 4.4f, Thatch.Base, 6, 1);
        c.Lumpy(at.X - 1.8f, at.Y - 1.8f, 2.4f, Thatch.Light, 5, 0);
        c.Line(at.X + 4, at.Y + 5, at.X + 8, at.Y + 6, Thatch.Base);
        c.Line(at.X - 6, at.Y + 4, at.X - 8, at.Y + 7, Thatch.Shade);
        c.Dot(at.X - 2, at.Y - 3, Thatch.Highlight);
    }

    /// <summary>Option C: a tied rectangular bale, one end opened and spilling.</summary>
    private static void HayBale(PixelCanvas c, Vector2 at)
    {
        float x = at.X - 6, y = at.Y - 4;
        c.Rect(x + 1, y + 1, 12, 8, Shadow);
        c.Rect(x, y, 12, 8, Thatch.Edge);
        c.Rect(x + 1, y + 1, 10, 6, Thatch.Base);
        c.Rect(x + 1, y + 1, 10, 1, Thatch.Light);
        c.Rect(x + 1, y + 6, 10, 1, Thatch.Shade);
        c.Rect(x + 4, y, 1, 8, Timber.Shade);
        c.Rect(x + 8, y, 1, 8, Timber.Shade);
        for (var k = 0; k < 5; k++) c.Dot(x + 12 + k % 3, y + 2 + k, k % 2 == 0 ? Thatch.Base : Thatch.Light);
    }

    /// <summary>
    /// A rail fence a few units inside the footprint: posts at an even
    /// spacing, one or two rails between them with a lit top and a shadow
    /// below, and a plank gate with a diagonal brace on the door side.
    /// </summary>
    private static void RailFence(PixelCanvas c, int w, int h, BuildingDoor door, float gate, Ramp wood, int rails, int postGap)
    {
        const float inset = 2, gateHalf = 6;
        float left = inset, top = inset, right = w - inset - 2, bottom = h - inset - 2;
        bool InGate(DoorSide side, float along) => side == door.Side && MathF.Abs(along - gate) < gateHalf;

        void HorizontalRun(float y, DoorSide side)
        {
            for (var x = left + 2; x < right; x++)
            {
                if (InGate(side, x + 0.5f)) continue;
                for (var k = 0; k < rails; k++)
                {
                    var ry = y + k * 2 - (rails - 1);
                    c.Rect(x, ry, 1, 1, wood.Base);
                    c.Rect(x, ry - 0.0f, 1, 1, k == 0 ? wood.Light : wood.Base);
                }
                c.Rect(x, y + rails, 1, 1, Shadow);
            }
        }
        void VerticalRun(float x, DoorSide side)
        {
            for (var y = top + 2; y < bottom; y++)
            {
                if (InGate(side, y + 0.5f)) continue;
                for (var k = 0; k < rails; k++)
                    c.Rect(x + k * 2 - (rails - 1) + 1, y, 1, 1, k == 0 ? wood.Light : wood.Base);
                c.Rect(x + rails + 1, y, 1, 1, Shadow);
            }
        }
        HorizontalRun(top + 1, DoorSide.North);
        HorizontalRun(bottom + 1, DoorSide.South);
        VerticalRun(left, DoorSide.West);
        VerticalRun(right, DoorSide.East);

        void Post(float x, float y)
        {
            c.Rect(x + 1, y + 1, 3, 3, Shadow);
            c.Rect(x, y, 3, 3, wood.Edge);
            c.Rect(x, y, 2, 2, wood.Base);
            c.Dot(x, y, wood.Highlight);
        }
        for (var x = left; x <= right + 0.1f; x += SpacingFor(right - left, postGap))
        {
            if (!InGate(DoorSide.North, x + 1.5f) || MathF.Abs(x + 1.5f - gate) >= gateHalf - 1) Post(x, top);
            if (!InGate(DoorSide.South, x + 1.5f) || MathF.Abs(x + 1.5f - gate) >= gateHalf - 1) Post(x, bottom);
        }
        for (var y = top + SpacingFor(bottom - top, postGap); y < bottom - 1; y += SpacingFor(bottom - top, postGap))
        {
            if (!InGate(DoorSide.West, y + 1.5f) || MathF.Abs(y + 1.5f - gate) >= gateHalf - 1) Post(left, y);
            if (!InGate(DoorSide.East, y + 1.5f) || MathF.Abs(y + 1.5f - gate) >= gateHalf - 1) Post(right, y);
        }
        GatePosts(c, door, gate, gateHalf, left, top, right, bottom, wood, Post);
    }

    /// <summary>An even post spacing near <paramref name="gap"/> that ends exactly on the far corner.</summary>
    private static float SpacingFor(float length, int gap) => length / MathF.Max(1, MathF.Round(length / gap));

    /// <summary>
    /// The gate stands open: a gap in the fence between two stout capped
    /// posts, trampled ground running through it out to the footprint's edge
    /// (as a doorstep path does), and the pale plank gate swung inward from
    /// one post, lying across the ground with its shadow.
    /// </summary>
    private static void GatePosts(PixelCanvas c, BuildingDoor door, float gate, float half, float left, float top, float right, float bottom, Ramp wood, Action<float, float> post) =>
        OpenGate(c, door, gate, half, door.Side switch
        {
            DoorSide.North => top,
            DoorSide.West => left,
            DoorSide.East => right,
            _ => bottom,
        }, wood);

    /// <summary>An open gate at <paramref name="line"/>, the fence's position across its side; used by every option.</summary>
    private static void OpenGate(PixelCanvas c, BuildingDoor door, float gate, float half, float line, Ramp wood)
    {
        var horizontal = door.Side is DoorSide.South or DoorSide.North;
        var inward = door.Side is DoorSide.South or DoorSide.East ? -1f : 1f;
        // Along the side (a) and across it (b) to a screen point.
        Vector2 P(float a, float b) => horizontal ? new Vector2(a, b) : new Vector2(b, a);
        // Trampled ground through the opening, from inside the fence to the footprint's edge.
        var edge = door.Side is DoorSide.South or DoorSide.East ? line + 4 : 0f;
        var from = MathF.Min(edge, line + inward * 4);
        var to = MathF.Max(edge, line + inward * 4);
        for (var a = gate - half + 1; a < gate + half - 1; a++)
            for (var b = from; b < to; b++)
            {
                var point = P(a, b);
                var n = PixelArt.Hash((int)point.X, (int)point.Y, 77) % 6;
                c.Dot(point.X, point.Y, n == 0 ? Dirt.Shade : n == 1 ? Dirt.Light : Dirt.Base);
            }
        void Stout(float a, float b)
        {
            var at = P(a, b);
            c.Rect(at.X + 1, at.Y + 1, 4, 4, Shadow);
            c.Rect(at.X, at.Y, 4, 4, wood.Edge);
            c.Rect(at.X, at.Y, 3, 3, wood.Base);
            c.Rect(at.X, at.Y, 2, 1, wood.Highlight);
            c.Dot(at.X, at.Y + 1, wood.Light);
        }
        // The gate leaf: two planks hinged on the first post, swung into the yard.
        var length = half * 2 - 4;
        var hinge = gate - half + 1;
        for (var k = 0; k < length; k++)
        {
            var b = line + 1 + inward * (k + 2);
            var plankA = P(hinge, b);
            var plankB = P(hinge + 1, b);
            var shade = P(hinge + 2, b + 1);
            c.Dot(shade.X, shade.Y, Shadow);
            c.Dot(plankA.X, plankA.Y, k % 4 == 1 ? wood.Shade : wood.Highlight);
            c.Dot(plankB.X, plankB.Y, k % 4 == 1 ? wood.Edge : wood.Light);
        }
        Stout(gate - half - 2, line - 0.5f);
        Stout(gate + half - 2, line - 0.5f);
    }

    /// <summary>Option B: a woven wattle fence, a three-unit band of hurdles with stakes, open at the gate.</summary>
    private static void Wattle(PixelCanvas c, int w, int h, BuildingDoor door, float gate)
    {
        const float gateHalf = 6;
        bool InGate(DoorSide side, float along) => side == door.Side && MathF.Abs(along - gate) < gateHalf;
        Color Weave(int along, int across) => ((along / 2 + across) % 2 == 0) ? Timber.Light : Timber.Base;
        for (var x = 2; x < w - 2; x++)
            for (var k = 0; k < 3; k++)
            {
                if (!InGate(DoorSide.North, x + 0.5f)) c.Dot(x, 2 + k, k == 0 ? Timber.Light : Weave(x, k));
                if (!InGate(DoorSide.South, x + 0.5f)) c.Dot(x, h - 5 + k, k == 2 ? Timber.Shade : Weave(x, k));
            }
        for (var y = 5; y < h - 5; y++)
            for (var k = 0; k < 3; k++)
            {
                if (!InGate(DoorSide.West, y + 0.5f)) c.Dot(2 + k, y, k == 0 ? Timber.Light : Weave(y, k));
                if (!InGate(DoorSide.East, y + 0.5f)) c.Dot(w - 5 + k, y, k == 2 ? Timber.Shade : Weave(y, k));
            }
        // Shadows inside the north and west hurdles and outside the south and east ones.
        for (var x = 5; x < w - 5; x++) if (!InGate(DoorSide.North, x + 0.5f)) c.Dot(x, 5, Shadow);
        for (var y = 5; y < h - 5; y++) if (!InGate(DoorSide.West, y + 0.5f)) c.Dot(5, y, Shadow);
        for (var x = 3; x < w - 1; x++) if (!InGate(DoorSide.South, x + 0.5f)) c.Dot(x, h - 2, Shadow);
        for (var y = 3; y < h - 1; y++) if (!InGate(DoorSide.East, y + 0.5f)) c.Dot(w - 2, y, Shadow);
        // Stakes: dark tops every six units, and stout posts at the corners and the gate.
        for (var x = 2; x < w - 2; x += 6)
        {
            if (!InGate(DoorSide.North, x + 0.5f)) c.Dot(x, 3, Timber.Edge);
            if (!InGate(DoorSide.South, x + 0.5f)) c.Dot(x, h - 4, Timber.Edge);
        }
        for (var y = 8; y < h - 4; y += 6)
        {
            if (!InGate(DoorSide.West, y + 0.5f)) c.Dot(3, y, Timber.Edge);
            if (!InGate(DoorSide.East, y + 0.5f)) c.Dot(w - 4, y, Timber.Edge);
        }
        void Stout(float x, float y)
        {
            c.Rect(x, y, 3, 3, Timber.Edge);
            c.Rect(x, y, 2, 2, Timber.Shade);
            c.Dot(x, y, Timber.Highlight);
        }
        Stout(1, 1); Stout(w - 4, 1); Stout(1, h - 4); Stout(w - 4, h - 4);
        OpenGate(c, door, gate, gateHalf, door.Side switch
        {
            DoorSide.North => 2,
            DoorSide.West => 2,
            DoorSide.East => w - 5,
            _ => h - 5,
        }, Timber);
    }

    /// <summary>Option B: a thatched lean-to along the back, sloping down toward the yard, with its eave shadow.</summary>
    private static void LeanTo(PixelCanvas c, Rect2 band, BuildingDoor door)
    {
        var x = band.Position.X;
        var y = band.Position.Y;
        var bw = band.Size.X;
        var bh = band.Size.Y;
        var horizontal = door.Side is DoorSide.South or DoorSide.North;
        // Eave shadow on the yard side (south or east of the roof, or wherever the yard is).
        switch (door.Side)
        {
            case DoorSide.North: c.Rect(x + 1, y - 2, bw, 2, Shadow); break;
            case DoorSide.East: c.Rect(x + bw, y + 1, 2, bh, Shadow); break;
            case DoorSide.West: c.Rect(x - 2, y + 1, 2, bh, Shadow); break;
            default: c.Rect(x + 1, y + bh, bw, 2, Shadow); break;
        }
        c.Rect(x, y, bw, bh, Thatch.Edge);
        c.Rect(x + 1, y + 1, bw - 2, bh - 2, Thatch.Base);
        // Courses run along the eave; the high back edge is lit, the low eave shaded.
        if (horizontal)
        {
            for (var row = 3f; row < bh - 1; row += 3)
                for (var px = x + 1; px < x + bw - 1; px++)
                    if ((int)(px + row) % 4 != 0) c.Dot(px, y + row, Thatch.Shade);
            var back = door.Side == DoorSide.North ? y + bh - 2 : y + 1;
            var eave = door.Side == DoorSide.North ? y + 1 : y + bh - 2;
            c.Rect(x + 1, back, bw - 2, 1, Thatch.Highlight);
            c.Rect(x + 1, eave, bw - 2, 1, Thatch.Edge);
            for (var px = x + 2; px < x + bw - 2; px += 5) c.Dot(px, eave - (door.Side == DoorSide.North ? -1 : 1), Thatch.Light);
        }
        else
        {
            for (var col = 3f; col < bw - 1; col += 3)
                for (var py = y + 1; py < y + bh - 1; py++)
                    if ((int)(py + col) % 4 != 0) c.Dot(x + col, py, Thatch.Shade);
            var back = door.Side == DoorSide.West ? x + bw - 2 : x + 1;
            var eave = door.Side == DoorSide.West ? x + 1 : x + bw - 2;
            c.Rect(back, y + 1, 1, bh - 2, Thatch.Highlight);
            c.Rect(eave, y + 1, 1, bh - 2, Thatch.Edge);
        }
        // The roof's two end posts show at the eave corners.
        var corners = door.Side switch
        {
            DoorSide.North => new[] { new Vector2(x + 1, y), new Vector2(x + bw - 3, y) },
            DoorSide.East => new[] { new Vector2(x + bw - 2, y + 1), new Vector2(x + bw - 2, y + bh - 3) },
            DoorSide.West => new[] { new Vector2(x, y + 1), new Vector2(x, y + bh - 3) },
            _ => new[] { new Vector2(x + 1, y + bh - 2), new Vector2(x + bw - 3, y + bh - 2) },
        };
        foreach (var corner in corners)
        {
            c.Rect(corner.X, corner.Y, 2, 2, Timber.Edge);
            c.Dot(corner.X, corner.Y, Timber.Light);
        }
    }
}
