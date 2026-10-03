using Godot;

namespace ClankerWorld.GodotClient.UI;

public static partial class BuildingSprites
{
    private static partial class ApprovedArt
    {
        private static readonly Ramp Fruit = Ramp.Of("9A4E1E", "C8702E", "E0893F", "F6C27A", "FFE0A8");

        /// <summary>
        /// Round 3 Market: a timber market hall with no stalls inside it; the
        /// stalls stand outside on the approved packed-earth plaza. A
        /// hipped roof of timber planks carries a louvred lantern along its ridge
        /// that flies the approved Market's gold pennant. Along the door side a
        /// lower lean-to roof covers an open arcade, with a sign of trading
        /// scales over the way in and the doorstep and path beyond its eave. It
        /// is laid out once with the door "down" and turned to the door side by
        /// <see cref="MarketFrame"/>; every piece is lit from the north-west.
        /// </summary>
        private static void PaintMarket(Plate p, Recipe recipe, int w, int h, BuildingDoor door)
        {
            var side = door.Side;
            var frame = new MarketFrame(Opposite(side), w, h);
            float breadth = frame.Breadth, length = frame.Length;
            // The arcade's eave stands seven units in from the footprint edge, leaving room for the doorstep and path.
            const float front = 7, arcadeDepth = 12;
            var arcade = p.Px(frame.Map(4, length - front - arcadeDepth, breadth - 8, arcadeDepth));
            var hall = p.Px(frame.Map(3, 3, breadth - 6, length - front - arcadeDepth - 2));
            var middle = p.P(Fit(door.Tile is { } t ? t * 32 + 16 : breadth / 2f, 14, breadth - 14));
            // The hall roof's eave shadow falls across the lean-to; where both shadows land outside, only one is laid.
            var hallShadow = new Rect2I(hall.Position.X + p.P(2), hall.Position.Y + p.P(3), hall.Size.X, hall.Size.Y);
            LeanTo(p, arcade, side, recipe with { Salt = 225 }, hallShadow);
            ArcadePosts(p, arcade, side, middle);
            PaintRoof(p, hall, recipe);
            Lantern(p, hall);
            var sign = Inward(p, arcade, side, middle, 6);
            ScalesSign(p, sign.X, sign.Y);
            var (from, to) = Span(p, middle, 3);
            var next = Step(p, arcade, side, from, to, 1, p.Small ? 1 : 3);
            if (door.Tile is not null) Path(p, arcade, side, middle, next);
        }

        /// <summary>The side across the footprint from <paramref name="side"/>.</summary>
        private static DoorSide Opposite(DoorSide side) => side switch
        {
            DoorSide.North => DoorSide.South,
            DoorSide.East => DoorSide.West,
            DoorSide.West => DoorSide.East,
            _ => DoorSide.North,
        };

        /// <summary>
        /// A lean-to roof over <paramref name="area"/> (pixels): one face sloping
        /// down toward <paramref name="front"/>, laid in the recipe's courses
        /// along that eave, with the eave shadow and the one-pixel edge. The main
        /// roof, painted after it, lays its own eave shadow across it, so the
        /// lean-to leaves out its shadow where <paramref name="shadowed"/> already has one.
        /// </summary>
        private static void LeanTo(Plate p, Rect2I area, DoorSide front, Recipe recipe, Rect2I shadowed)
        {
            int x0 = area.Position.X, y0 = area.Position.Y, w = area.Size.X, h = area.Size.Y;
            if (w < 4 || h < 4) return;
            var shadow = new Rect2I(x0 + p.P(2), y0 + p.P(3), w, h);
            for (var y = shadow.Position.Y; y < shadow.End.Y; y++)
                for (var x = shadow.Position.X; x < shadow.End.X; x++)
                    if (!shadowed.HasPoint(new Vector2I(x, y))) p.Put(x, y, Shadow);
            var face = front switch
            {
                DoorSide.North => Face.North,
                DoorSide.East => Face.East,
                DoorSide.West => Face.West,
                _ => Face.South,
            };
            for (var j = 0; j < h; j++)
                for (var i = 0; i < w; i++)
                {
                    if (i == 0 || j == 0 || i == w - 1 || j == h - 1)
                    {
                        p.Put(x0 + i, y0 + j, recipe.Roof.Edge);
                        continue;
                    }
                    // "along" follows the eave, "across" counts up the slope from it, as on a main roof face.
                    var (along, across) = front switch
                    {
                        DoorSide.North => (i - 1, j - 1),
                        DoorSide.East => (j - 1, w - 2 - i),
                        DoorSide.West => (j - 1, i - 1),
                        _ => (i - 1, h - 2 - j),
                    };
                    p.Put(x0 + i, y0 + j, Surface(recipe, face, along, across, p.Small));
                }
        }

        /// <summary>
        /// The Market's ridge lantern: a raised, louvred vent along the middle of
        /// the ridge that lets the air out of the hall. From above it is a small
        /// hipped roof of grey slate shingles sitting astride the main ridge, its
        /// shadow falling south-east across the planks, with the approved
        /// Market's gold pennant flying from its peak.
        /// </summary>
        private static void Lantern(Plate p, Rect2I hall)
        {
            int iw = hall.Size.X - 2, ih = hall.Size.Y - 2;
            var horizontal = iw >= ih;
            var length = horizontal ? iw : ih;
            var breadth = horizontal ? ih : iw;
            var ridge = (breadth + 1) / 2 - 1;
            var along = Math.Max(p.P(12), length * 2 / 5);
            var across = p.Small ? 5 : 9;
            int centreAlong = length / 2, top = ridge - across / 2;
            var roof = horizontal
                ? new Rect2I(hall.Position.X + 1 + centreAlong - along / 2, hall.Position.Y + 1 + top, along, across)
                : new Rect2I(hall.Position.X + 1 + top, hall.Position.Y + 1 + centreAlong - along / 2, across, along);
            PaintRoof(p, roof, new Recipe(Slate, Material.Shingle, RoofShape.Hip, 226));
            var peak = RidgeEnds(roof.Position.X + 1, roof.Position.Y + 1, roof.Size.X - 2, roof.Size.Y - 2);
            var (px, py) = peak[0];
            Pennant(p, px + (peak[1].X - px) / 2 + (p.Small ? 0 : 1), py + (peak[1].Y - py) / 2);
        }

        /// <summary>
        /// The arcade's posts along the lean-to's eave, two flanking the way in
        /// and the rest spaced evenly to the corners: each a timber post head lit
        /// on its north-west pixel, with a small shadow.
        /// </summary>
        private static void ArcadePosts(Plate p, Rect2I arcade, DoorSide side, int middle)
        {
            // At 16 px a post would be a single edge-coloured pixel on the eave, so the arcade shows by its roof alone (B7).
            if (p.Small) return;
            const int size = 3;
            var horizontal = side is DoorSide.South or DoorSide.North;
            var start = (horizontal ? arcade.Position.X : arcade.Position.Y) + 1;
            var end = (horizontal ? arcade.End.X : arcade.End.Y) - 1 - size;
            var gap = p.P(6);
            var spots = new List<int>();
            // Posts from a door-side post out to a corner; a short run keeps only its corner post.
            void Run(int corner, int flank)
            {
                if (Math.Abs(flank - corner) < p.P(8))
                {
                    spots.Add(corner);
                    return;
                }
                var bays = Math.Max(1, (int)MathF.Round(Math.Abs(flank - corner) / (float)p.P(11)));
                for (var k = 0; k <= bays; k++) spots.Add(corner + (flank - corner) * k / bays);
            }
            Run(start, middle - gap - size);
            Run(end, middle + gap);
            foreach (var t in spots.Distinct())
            {
                var (x, y) = side switch
                {
                    DoorSide.North => (t, arcade.Position.Y - size / 2),
                    DoorSide.East => (arcade.End.X - 1 - size / 2, t),
                    DoorSide.West => (arcade.Position.X - size / 2, t),
                    _ => (t, arcade.End.Y - 1 - size / 2),
                };
                p.Fill(x + 1, y + 1, size, size, SmallShadow);
                p.Fill(x, y, size, size, Timber.Edge);
                p.Put(x + 1, y + 1, Timber.Light);
            }
        }

        /// <summary>B6 Market: a cream sign board with gold trading scales: a beam on a post, a pan hanging from each end.</summary>
        private static void ScalesSign(Plate p, int cx, int cy)
        {
            if (p.Small)
            {
                p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
                p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
                p.Fill(cx - 1, cy - 1, 2, 2, Cloth.Light);
                p.Fill(cx - 1, cy - 1, 2, 1, Gold.Base);
                p.Put(cx, cy, Gold.Shade);
                return;
            }
            p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
            p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
            p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
            p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
            string[] scales =
            [
                "...h...",
                "lllllll",
                "s..b..s",
                "bb.b.bb",
                "...b...",
                "..sbs..",
            ];
            for (var j = 0; j < scales.Length; j++)
                for (var i = 0; i < scales[j].Length; i++)
                {
                    Color? c = scales[j][i] switch
                    {
                        'h' => Gold.Highlight,
                        'l' => Gold.Light,
                        'b' => Gold.Base,
                        's' => Gold.Shade,
                        _ => null,
                    };
                    if (c is { } colour) p.Put(cx - 3 + i, cy - 3 + j, colour);
                }
        }

        /// <summary>A market stall on its own tile, facing the door side, with a gap for the path.</summary>
        private static void PaintStallLot(Plate p, int w, int h, BuildingDoor door)
        {
            var area = new Rect2(4, 4, w - 8, h - 9);
            Stall(p, area, door.Side, Berry, Fruit, 5);
            if (door.Tile is null) return;
            // The path runs from the crates to the footprint edge along the door axis.
            var middle = p.P(door.Tile.Value * 32 + 16);
            var lot = p.Px(area);
            var front = door.Side switch
            {
                DoorSide.North => new Rect2I(lot.Position.X, lot.Position.Y, lot.Size.X, 1),
                DoorSide.East => new Rect2I(lot.End.X - 1, lot.Position.Y, 1, lot.Size.Y),
                DoorSide.West => new Rect2I(lot.Position.X, lot.Position.Y, 1, lot.Size.Y),
                _ => new Rect2I(lot.Position.X, lot.End.Y - 1, lot.Size.X, 1),
            };
            Path(p, front, door.Side, middle, 1);
        }

        /// <summary>
        /// One stall in <paramref name="area"/> (units): an awning at the back
        /// sloping toward <paramref name="front"/>, and crates of produce on the
        /// counter in front of it, in two pairs with the middle clear for the path.
        /// </summary>
        private static void Stall(Plate p, Rect2 area, DoorSide front, Ramp stripe, Ramp produce, int salt)
        {
            var across = front is DoorSide.South or DoorSide.North;
            var alongSize = across ? area.Size.X : area.Size.Y;
            var depthSize = across ? area.Size.Y : area.Size.X;
            var awningDepth = MathF.Round(depthSize * 0.6f);
            Awning(p, p.Px(Orient(area, front, 0, 0, alongSize, awningDepth)), front, stripe);
            var crateDepth = Math.Min(5f, depthSize - awningDepth);
            var crates = new[] { 1f, 6f, alongSize - 11, alongSize - 6 };
            var ramps = new[] { produce, Leaf, Berry, Gold };
            for (var i = 0; i < crates.Length; i++)
            {
                if (crates[i] + 5 > alongSize) continue;
                Crate(p, p.Px(Orient(area, front, crates[i], awningDepth, 5, crateDepth)), ramps[(i + salt) % ramps.Length]);
            }
        }

        /// <summary>
        /// Maps a rectangle written in a frame where <paramref name="front"/>
        /// is "down" (depth runs from the back toward the front) into units.
        /// </summary>
        private static Rect2 Orient(Rect2 area, DoorSide front, float along, float depth, float alongSize, float depthSize) => front switch
        {
            DoorSide.North => new Rect2(area.Position.X + along, area.End.Y - depth - depthSize, alongSize, depthSize),
            DoorSide.East => new Rect2(area.Position.X + depth, area.Position.Y + along, depthSize, alongSize),
            DoorSide.West => new Rect2(area.End.X - depth - depthSize, area.Position.Y + along, depthSize, alongSize),
            _ => new Rect2(area.Position.X + along, area.Position.Y + depth, alongSize, depthSize),
        };

        /// <summary>A small open crate of produce: timber rim, produce heaped inside with a lit top-left.</summary>
        private static void Crate(Plate p, Rect2I box, Ramp produce)
        {
            if (box.Size.X < 2 || box.Size.Y < 2) return;
            p.Fill(new Rect2I(box.Position.X + 1, box.Position.Y + 1, box.Size.X, box.Size.Y), SmallShadow);
            p.Fill(box, Timber.Edge);
            var inner = new Rect2I(box.Position.X + 1, box.Position.Y + 1, box.Size.X - 2, box.Size.Y - 2);
            if (inner.Size.X <= 0 || inner.Size.Y <= 0)
            {
                p.Fill(box.Position.X, box.Position.Y, 1, 1, produce.Base);
                return;
            }
            p.Fill(inner, produce.Base);
            p.Put(inner.Position.X, inner.Position.Y, produce.Light);
            if (inner.Size.X > 2) p.Put(inner.Position.X + 2, inner.Position.Y, produce.Light);
            if (inner.Size.Y > 1) p.Put(inner.End.X - 1, inner.End.Y - 1, produce.Shade);
        }

        /// <summary>The Market's finial, on the hall's lantern: a pole top with a little gold pennant flying east.</summary>
        private static void Pennant(Plate p, int x, int y)
        {
            if (p.Small)
            {
                p.Put(x, y, Timber.Edge);
                p.Put(x + 1, y, Gold.Light);
                return;
            }
            p.Fill(x - 1, y - 1, 2, 2, Timber.Edge);
            p.Fill(x + 1, y - 2, 4, 1, Gold.Light);
            p.Fill(x + 1, y - 1, 3, 1, Gold.Base);
            p.Put(x + 1, y, Gold.Shade);
        }

        /// <summary>Maps the approved door-down layout into footprint units without rotating its lighting.</summary>
        private readonly record struct MarketFrame(DoorSide Land, float Width, float Height)
        {
            public float Length => Land is DoorSide.North or DoorSide.South ? Height : Width;
            public float Breadth => Land is DoorSide.North or DoorSide.South ? Width : Height;

            public Rect2 Map(float across, float outward, float sizeAcross, float sizeOut) => Land switch
            {
                DoorSide.North => new Rect2(across, outward, sizeAcross, sizeOut),
                DoorSide.South => new Rect2(across, Height - outward - sizeOut, sizeAcross, sizeOut),
                DoorSide.West => new Rect2(outward, across, sizeOut, sizeAcross),
                _ => new Rect2(Width - outward - sizeOut, across, sizeOut, sizeAcross),
            };
        }
    }
}
