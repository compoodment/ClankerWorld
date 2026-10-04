using Godot;

namespace ClankerWorld.GodotClient.UI;

public static partial class BuildingSprites
{
    private static partial class ApprovedArt
    {
        private static DoorSide PortLandSide(int tilesWide, int tilesHigh, DoorSide door) => tilesHigh >= tilesWide
            ? door == DoorSide.South ? DoorSide.South : DoorSide.North
            : door == DoorSide.West ? DoorSide.West : DoorSide.East;

        /// <summary>
        /// The Port's layout frame. The Port is laid out once with the land at
        /// the top ("across" runs along the shore, "out" from the land over the
        /// water) and mapped into the footprint for each of its four rotations.
        /// Only positions are mapped; every piece is drawn in place with the
        /// north-west light, so a rotated Port is lit like any other building.
        /// </summary>
        private readonly record struct PortFrame(DoorSide Land, float Width, float Height)
        {
            /// <summary>The length of the Port from the land end to the sea end, in units.</summary>
            public float Length => Land is DoorSide.North or DoorSide.South ? Height : Width;

            /// <summary>The width of the Port along the shore, in units.</summary>
            public float Breadth => Land is DoorSide.North or DoorSide.South ? Width : Height;

            /// <summary>A rectangle in the frame (across, out, size across, size out) to footprint units.</summary>
            public Rect2 Map(float across, float outward, float sizeAcross, float sizeOut) => Land switch
            {
                DoorSide.North => new Rect2(across, outward, sizeAcross, sizeOut),
                DoorSide.South => new Rect2(across, Height - outward - sizeOut, sizeAcross, sizeOut),
                DoorSide.West => new Rect2(outward, across, sizeOut, sizeAcross),
                _ => new Rect2(Width - outward - sizeOut, across, sizeOut, sizeAcross),
            };

            public Rect2 Map(Rect2 r) => Map(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);

            /// <summary>A point in the frame to footprint units.</summary>
            public Vector2 Point(float across, float outward) => Land switch
            {
                DoorSide.North => new Vector2(across, outward),
                DoorSide.South => new Vector2(across, Height - outward),
                DoorSide.West => new Vector2(outward, across),
                _ => new Vector2(Width - outward, across),
            };

            /// <summary>The top-left corner of a small square of <paramref name="size"/> units placed at (across, out).</summary>
            public Vector2 Corner(float across, float outward, float size) => Map(across, outward, size, size).Position;
        }

        /// <summary>
        /// B3/B6 Port: one row on land, a plank shed whose door faces the Road;
        /// the other three rows are a plank pier on piles over the water,
        /// widening to a T-head with iron bollards and goods waiting. The pier
        /// keeps open water on both long sides, where boats tie up. Drawn in any
        /// of the four rotations (<see cref="PortLandSide"/>).
        /// <paramref name="paintedBoat"/> adds the moored boat of the approved
        /// round-1 drawing; round 2 leaves the water free for the real boat.
        /// </summary>
        private static void PaintPort(Plate p, int w, int h, BuildingDoor door, bool paintedBoat)
        {
            var frame = new PortFrame(PortLandSide(w / 32, h / 32, door.Side), w, h);
            float breadth = frame.Breadth, length = frame.Length;
            const float shedBottom = 27;
            var pier = new Rect2(breadth / 2f - 12, shedBottom - 3, 24, length - 30 - (shedBottom - 3));
            var head = new Rect2(6, length - 30, breadth - 12, 24);
            foreach (var deck in new[] { pier, head })
            {
                var px = p.Px(frame.Map(deck));
                p.Fill(new Rect2I(px.Position.X + p.P(2), px.Position.Y + p.P(3), px.Size.X, px.Size.Y), Shadow);
            }
            if (paintedBoat) Boat(p, frame.Map(new Rect2(pier.End.X + 4, 46, 9, 28)));
            Deck(p, p.Px(frame.Map(pier)), 201);
            Deck(p, p.Px(frame.Map(head)), 202);
            // Piles standing proud of the deck edges.
            void PostAt(float across, float outward)
            {
                var corner = frame.Corner(across, outward, 3);
                Post(p, corner.X, corner.Y);
            }
            for (var y = 38f; y < head.Position.Y - 4; y += 16)
            {
                PostAt(pier.Position.X - 1, y);
                PostAt(pier.End.X - 2, y);
            }
            foreach (var (x, y) in new[] { (head.Position.X - 1, head.Position.Y - 1), (head.End.X - 2, head.Position.Y - 1), (head.Position.X - 1, head.End.Y - 2), (head.End.X - 2, head.End.Y - 2) })
                PostAt(x, y);
            // Bollards along the outer edge of the T-head, and one on the pier where a boat ties up.
            foreach (var (x, y) in new[] { (head.Position.X + 6, head.End.Y - 4), (head.End.X - 6, head.End.Y - 4), (breadth / 2f, head.End.Y - 4), (pier.End.X - 3, 48f) })
            {
                var at = frame.Point(x, y);
                Bollard(p, at.X, at.Y);
            }
            // Mooring line from the pier bollard to the painted boat's bow.
            if (paintedBoat)
            {
                var (from, to) = (frame.Point(pier.End.X - 2, 48), frame.Point(pier.End.X + 7.5f, 47.5f));
                p.Canvas.Line(from.X, from.Y, to.X, to.Y, Cloth.Shade);
            }
            // Goods waiting on the T-head.
            foreach (var (x, y) in new[] { (head.Position.X + 9, head.Position.Y + 6), (head.Position.X + 16, head.Position.Y + 8) })
            {
                var corner = frame.Corner(x, y, 6);
                DeckCrate(p, corner.X, corner.Y);
            }
            var coil = frame.Point(head.End.X - 12, head.Position.Y + 9);
            p.Canvas.Ring(coil.X, coil.Y, 2.5f, Cloth.Shade);
            p.Canvas.Disc(coil.X, coil.Y, 1.2f, Cloth.Base);
            // The shed on the shore.
            var recipe = new Recipe(Timber, Material.Shingle, RoofShape.Hip, 203);
            var shed = new Rect2(9, 5, breadth - 18, shedBottom - 5);
            var roof = p.Px(frame.Map(shed));
            PaintRoof(p, roof, recipe);
            var middle = p.P(Fit(door.Tile is { } t ? t * 32 + 16 : breadth / 2f, shed.Position.X + 6, shed.End.X - 6));
            var next = Door(p, roof, frame.Land, middle, 3, 2);
            if (door.Tile is not null) Path(p, roof, frame.Land, middle, next);
        }

        /// <summary>A plank deck: planks laid across it, seams one step darker, ragged plank ends and edge stringers.</summary>
        private static void Deck(Plate p, Rect2I deck, int salt)
        {
            var depth = p.Small ? 2 : 4;
            var eastWest = deck.Size.Y >= deck.Size.X;
            int length = eastWest ? deck.Size.X : deck.Size.Y, breadth = eastWest ? deck.Size.Y : deck.Size.X;
            for (var j = 0; j < breadth; j++)
            {
                var course = j / depth;
                var row = j % depth;
                var shortStart = PixelArt.Hash(course, 0, salt) % 2 == 0;
                var shortEnd = PixelArt.Hash(course, 1, salt) % 2 == 0;
                var tone = PixelArt.Hash(course, 2, salt) % 4 == 0 ? 3 : 2;
                for (var i = 0; i < length; i++)
                {
                    if ((i == 0 && shortStart) || (i == length - 1 && shortEnd)) continue;
                    Color c;
                    if (row == depth - 1) c = Timber.Shade;
                    else if (i <= 1 || i >= length - 2) c = Timber.Shade;
                    else if (row == 0 && !p.Small) c = Timber[tone + 1];
                    else c = Timber[tone];
                    if (!p.Small && row != depth - 1 && PixelArt.Hash(i, j, salt + 4) % 37 == 0) c = Timber.Shade;
                    if (eastWest) p.Put(deck.Position.X + i, deck.Position.Y + j, c);
                    else p.Put(deck.Position.X + j, deck.Position.Y + i, c);
                }
            }
        }

        /// <summary>A pile head standing at a deck edge: timber, lit north-west, with a small shadow.</summary>
        private static void Post(Plate p, float x, float y)
        {
            var px = p.P(x);
            var py = p.P(y);
            var size = p.Small ? 2 : 3;
            p.Fill(px + 1, py + 1, size, size, SmallShadow);
            p.Fill(px, py, size, size, Timber.Edge);
            if (!p.Small) p.Put(px + 1, py + 1, Timber.Light);
        }

        /// <summary>B6 Port: an iron mooring bollard, a dark ring with a lit cap.</summary>
        private static void Bollard(Plate p, float x, float y)
        {
            var c = p.Canvas;
            c.Disc(x + 1, y + 1.5f, 2.4f, SmallShadow);
            c.Disc(x, y, 2.4f, Iron.Edge);
            c.Disc(x - 0.3f, y - 0.3f, 1.5f, Iron.Base);
            c.Dot(x - 1, y - 1, Iron.Highlight);
        }

        /// <summary>A closed crate on the deck: lid boards with a seam, lit top edge.</summary>
        private static void DeckCrate(Plate p, float x, float y)
        {
            var c = p.Canvas;
            c.Rect(x + 1, y + 2, 6, 6, SmallShadow);
            c.Rect(x, y, 6, 6, Timber.Edge);
            c.Rect(x + 1, y + 1, 4, 4, Timber.Light);
            c.Rect(x + 1, y + 1, 4, 1, Timber.Highlight);
            c.Rect(x + 1, y + 3, 4, 1, Timber.Base);
        }

        /// <summary>A rowing boat moored beside the pier: hull, darker inside, two thwarts, shadow on the water.</summary>
        private static void Boat(Plate p, Rect2 area)
        {
            var c = p.Canvas;
            var cx = area.Position.X + area.Size.X / 2;
            var cy = area.Position.Y + area.Size.Y / 2;
            var rx = area.Size.X / 2;
            var ry = area.Size.Y / 2;
            c.Ellipse(cx + 2, cy + 3, rx, ry, Shadow);
            c.Ellipse(cx, cy, rx, ry, Timber.Edge);
            c.Ellipse(cx - 0.3f, cy, rx - 1, ry - 1, Timber.Light);
            c.Ellipse(cx + 0.2f, cy + 0.5f, rx - 2.2f, ry - 3, Timber.Shade);
            c.Rect(cx - rx + 2, cy - 5, rx * 2 - 4, 1, Timber.Base);
            c.Rect(cx - rx + 2, cy + 4, rx * 2 - 4, 1, Timber.Base);
        }
    }
}
