using Godot;

namespace ClankerWorld.GodotClient.UI;

public static partial class BuildingSprites
{
    private static partial class ApprovedArt
    {
        // ------------------------------------------------------------------
        // Animal yard: option A of the October 7 art review, a split-rail pen
        // on trampled earth, copied from tools/ArtPreview/Proposed/Yards.cs.
        // ------------------------------------------------------------------

        private static readonly Ramp YardTimber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
        private static readonly Ramp YardDirt = Ramp.Of("6E5538", "977852", "B99A6B", "C9AC7C", "D9C08F");
        private static readonly Ramp YardThatch = Ramp.Of("6B5528", "A98A45", "D2AE5E", "E6C77B", "F0DA9A");
        private static readonly Ramp YardLeaf = Ramp.Of("3C5F2E", "4C7A3A", "5E8C45", "79A657", "9BC66F");
        private static readonly Ramp YardWater = Ramp.Of("3A5F7A", "4A7B9D", "598FB3", "6A9FC0", "8ABBD6");
        private static readonly Color YardShadow = new(0.05f, 0.08f, 0.05f, 0.28f);

        /// <summary>
        /// The yard drawn at 32 px: trampled earth inside a two-rail fence with
        /// grass surviving along it, an open gate on the door side, a water
        /// trough just inside the gate and a heap of hay in the far corner. At
        /// 16 px the same picture is halved, as the Port is.
        /// </summary>
        private static Image Yard(int tilesWide, int tilesHigh, int tilePixels, BuildingDoor door)
        {
            var w = tilesWide * 32;
            var h = tilesHigh * 32;
            var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            image.Fill(Colors.Transparent);
            var c = new PixelCanvas(image, new Rect2I(0, 0, w, h), 1f);
            var gate = Gate(w, h, door);
            var seed = tilesWide * 31 + tilesHigh * 7;
            EarthFloor(c, w, h, seed, grassEdge: true);
            Trough(c, TroughSpot(w, h, door, gate));
            HayPile(c, FeedSpot(w, h, door));
            RailFence(c, w, h, door, gate, YardTimber, rails: 2, postGap: 10);
            if (tilePixels != 32) image.Resize(tilesWide * tilePixels, tilesHigh * tilePixels, Image.Interpolation.Nearest);
            return image;
        }

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

        /// <summary>Trampled earth inside the fence, mottled with hoof marks; optionally grass surviving along the fence.</summary>
        private static void EarthFloor(PixelCanvas c, int w, int h, int seed, bool grassEdge)
        {
            for (var y = 3; y < h - 3; y++)
                for (var x = 3; x < w - 3; x++)
                {
                    var n = PixelArt.Hash(x, y, seed) % 23;
                    var color = n == 0 ? YardDirt.Shade : n == 1 ? YardDirt.Light : n == 2 && (x + y) % 3 == 0 ? YardDirt.Edge : YardDirt.Base;
                    c.Dot(x, y, color);
                }
            // Hoof marks: little paired dark nicks.
            for (var k = 0; k < w * h / 160; k++)
            {
                var hx = 6 + (int)(PixelArt.Hash(k, 3, seed) % (uint)(w - 12));
                var hy = 6 + (int)(PixelArt.Hash(k, 9, seed) % (uint)(h - 12));
                c.Dot(hx, hy, YardDirt.Shade);
                c.Dot(hx + 2, hy, YardDirt.Shade);
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
            c.Dot(x, y, YardLeaf.Base);
            c.Dot(x + 1, y, YardLeaf.Shade);
            if (tall) c.Dot(x, y - 1, YardLeaf.Light);
        }

        /// <summary>A plank trough of water with a dark rim, lit along its north-west edges.</summary>
        private static void Trough(PixelCanvas c, Rect2 r)
        {
            var x = r.Position.X;
            var y = r.Position.Y;
            var tw = r.Size.X;
            var th = r.Size.Y;
            c.Rect(x + 1, y + 1, tw, th, YardShadow);
            c.Rect(x, y, tw, th, YardTimber.Edge);
            c.Rect(x + 1, y + 1, tw - 2, th - 2, YardTimber.Shade);
            c.Rect(x + 1, y, tw - 2, 1, YardTimber.Light);
            c.Rect(x, y + 1, 1, th - 2, YardTimber.Light);
            c.Rect(x + 2, y + 2, tw - 4, th - 4, YardWater.Base);
            c.Rect(x + 2, y + 2, tw - 4, 1, YardWater.Highlight);
            c.Dot(x + tw - 3, y + th - 3, YardWater.Shade);
        }

        /// <summary>A loose heap of hay with a few stray strands.</summary>
        private static void HayPile(PixelCanvas c, Vector2 at)
        {
            c.Ellipse(at.X + 1.5f, at.Y + 2, 7, 5.5f, YardShadow);
            c.Lumpy(at.X, at.Y, 6.5f, YardThatch.Edge, 7, 2);
            c.Lumpy(at.X, at.Y, 5.6f, YardThatch.Shade, 7, 2);
            c.Lumpy(at.X - 0.8f, at.Y - 0.8f, 4.4f, YardThatch.Base, 6, 1);
            c.Lumpy(at.X - 1.8f, at.Y - 1.8f, 2.4f, YardThatch.Light, 5, 0);
            c.Line(at.X + 4, at.Y + 5, at.X + 8, at.Y + 6, YardThatch.Base);
            c.Line(at.X - 6, at.Y + 4, at.X - 8, at.Y + 7, YardThatch.Shade);
            c.Dot(at.X - 2, at.Y - 3, YardThatch.Highlight);
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
                        c.Rect(x, y + k * 2 - (rails - 1), 1, 1, k == 0 ? wood.Light : wood.Base);
                    c.Rect(x, y + rails, 1, 1, YardShadow);
                }
            }
            void VerticalRun(float x, DoorSide side)
            {
                for (var y = top + 2; y < bottom; y++)
                {
                    if (InGate(side, y + 0.5f)) continue;
                    for (var k = 0; k < rails; k++)
                        c.Rect(x + k * 2 - (rails - 1) + 1, y, 1, 1, k == 0 ? wood.Light : wood.Base);
                    c.Rect(x + rails + 1, y, 1, 1, YardShadow);
                }
            }
            HorizontalRun(top + 1, DoorSide.North);
            HorizontalRun(bottom + 1, DoorSide.South);
            VerticalRun(left, DoorSide.West);
            VerticalRun(right, DoorSide.East);

            void Post(float x, float y)
            {
                c.Rect(x + 1, y + 1, 3, 3, YardShadow);
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
                Post(left, y);
                Post(right, y);
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
            var from = MathF.Min(edge, line - inward * 4);
            var to = MathF.Max(edge, line - inward * 4) + (door.Side is DoorSide.South or DoorSide.East ? 0 : 4);
            for (var a = gate - half + 1; a < gate + half - 1; a++)
                for (var b = from; b < to; b++)
                {
                    var point = P(a, b);
                    var n = PixelArt.Hash((int)point.X, (int)point.Y, 77) % 6;
                    c.Dot(point.X, point.Y, n == 0 ? YardDirt.Shade : n == 1 ? YardDirt.Light : YardDirt.Base);
                }
            void Stout(float a, float b)
            {
                var at = P(a, b);
                c.Rect(at.X + 1, at.Y + 1, 4, 4, YardShadow);
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
                c.Dot(shade.X, shade.Y, YardShadow);
                c.Dot(plankA.X, plankA.Y, k % 4 == 1 ? wood.Shade : wood.Highlight);
                c.Dot(plankB.X, plankB.Y, k % 4 == 1 ? wood.Edge : wood.Light);
            }
            Stout(gate - half - 2, line - 0.5f);
            Stout(gate + half - 2, line - 0.5f);
        }
    }
}
