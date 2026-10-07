using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>How a building in an abandoned Town has weathered.</summary>
public enum BuildingNeglect : byte
{
    /// <summary>Lived in, or in a Town that is not abandoned.</summary>
    None,
    /// <summary>Look A: faded, mossy and weedy, with the door boarded up.</summary>
    Neglected,
    /// <summary>Look B, after a full season abandoned: also a hole in the roof, fallen planks and a sapling.</summary>
    FallingApart,
}

public static partial class BuildingSprites
{
    /// <summary>Keep the subject-name seed used by the approved abandoned proposal.</summary>
    public static int NeglectSalt(BuildingKind kind, int tilesWide, int tilesHigh)
    {
        var name = kind switch
        {
            BuildingKind.TailorShop => "tailor",
            BuildingKind.MarketStall => "stall",
            BuildingKind.AnimalYard => "yard",
            _ => kind.ToString().ToLowerInvariant(),
        };
        return $"{name}.{tilesWide}x{tilesHigh}".Length * 131;
    }

    /// <summary>
    /// The abandoned looks approved in the October 7 art review, copied from
    /// <c>tools/ArtPreview/Proposed/BuildingStates.cs</c>. They are drawn
    /// over a building's own 32 px picture, so they always match its art.
    /// </summary>
    private static class Neglect
    {
        // The approved preview samples top-left pixels; Godot's nearest resize
        // samples their centres and changes the mid-zoom picture.
        public static Image Resize(Image image, int width, int height)
        {
            var sourceWidth = image.GetWidth();
            var sourceHeight = image.GetHeight();
            var source = image.GetData();
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                    var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
                    Array.Copy(source, (sourceY * sourceWidth + sourceX) * 4, pixels, (y * width + x) * 4, 4);
                }
            return Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        }

        private readonly record struct NeglectRamp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight);

        private static readonly NeglectRamp NeglectTimber = new(new("3F2A1A"), new("6E4E31"), new("8A6440"), new("A77C52"), new("D2AC77"));
        private static readonly NeglectRamp NeglectLeaf = new(new("3C5F2E"), new("4C7A3A"), new("5E8C45"), new("79A657"), new("9BC66F"));
        private static readonly Color NeglectSoot = new("2A2622");
        private static readonly Color NeglectFlower = new("F2CC5E");
        private static readonly Color NeglectShadow = new(0.05f, 0.08f, 0.05f, 0.28f);

        /// <summary>What part of a building weathers and how it breaks.</summary>
        private enum Shape { Building, Fence, Pier }

        /// <summary>One building's picture and the structure inside it that weathers.</summary>
        private sealed record Look(Image Finished, bool[,] Mask, int Width, int Height, DoorSide Door, float DoorMiddle, Shape Kind, bool Doorway, int Salt);

        /// <summary>The weathered 32 px picture of one building.</summary>
        public static Image Draw(BuildingKind kind, int tilesWide, int tilesHigh, BuildingDoor door, bool ruin)
        {
            var finished = Render(kind, tilesWide, tilesHigh, 32, door);
            var w = tilesWide * 32;
            var h = tilesHigh * 32;
            var mask = new bool[w, h];
            var plan = Plan(kind, tilesWide, tilesHigh, door);
            var roofed = kind != BuildingKind.Port && ApprovedArt.PlanFor(kind, tilesWide, tilesHigh, door, 32) is not null;
            var shape = kind switch
            {
                BuildingKind.Port => Shape.Pier,
                BuildingKind.AnimalYard => Shape.Fence,
                _ => Shape.Building,
            };
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (roofed)
                        mask[x, y] = Inside(plan.Roof, x, y) || plan.Wing is { } wing && Inside(wing, x, y);
                    else
                        // A yard weathers only its fence, five units in from the footprint's edge.
                        mask[x, y] = finished.GetPixel(x, y).A > 0.9f &&
                            (shape != Shape.Fence || x < 6 || y < 6 || x >= w - 6 || y >= h - 6);
                }
            var doorway = roofed || kind == BuildingKind.Silo;
            return Apply(new Look(finished, mask, w, h, door.Side, plan.DoorMiddle, shape, doorway, NeglectSalt(kind, tilesWide, tilesHigh)), ruin);
        }

        private static bool Inside(Rect2 area, int x, int y) =>
            x >= (int)MathF.Round(area.Position.X) && x < (int)MathF.Round(area.End.X) &&
            y >= (int)MathF.Round(area.Position.Y) && y < (int)MathF.Round(area.End.Y);

        /// <summary>
        /// The neglected (A) or falling-apart (B) look over the building's own
        /// picture, transparent outside the structure and its weeds. Blended
        /// colours are snapped to the 8-bit steps Godot stores.
        /// </summary>
        private static Image Apply(Look s, bool ruin)
        {
            var mask = s.Mask;
            var b = Bounds(mask);
            var w = s.Width;
            var h = s.Height;
            var art = (Image)s.Finished.Duplicate();
            var salt = s.Salt + (ruin ? 7 : 0);
            var roofed = s.Kind == Shape.Building;

            // Faded, greyer and a little darker; the structure more than its yard and doorstep.
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var c = art.GetPixel(x, y);
                    if (c.A <= 0) continue;
                    var amount = mask[x, y] ? (ruin ? 0.42f : 0.26f) : 0.16f;
                    var grey = new Color(c.Luminance, c.Luminance, c.Luminance, c.A);
                    art.SetPixel(x, y, PixelArt.Snap(c.Lerp(grey, amount).Darkened(mask[x, y] ? (ruin ? 0.14f : 0.08f) : 0.05f) with { A = c.A }));
                }

            // Moss in clumps on the structure, more on the shaded south-east half.
            var clumps = Math.Max(2, b.Size.X * b.Size.Y / (ruin ? 70 : 140));
            for (var k = 0; k < clumps; k++)
            {
                var cx = b.Position.X + 2 + (int)(Hash(k, 1, salt) % (uint)Math.Max(1, b.Size.X - 4));
                var cy = b.Position.Y + 2 + (int)(Hash(k, 2, salt) % (uint)Math.Max(1, b.Size.Y - 4));
                if (Hash(k, 3, salt) % 3 == 0 && cx + cy < b.Position.X + b.Position.Y + (b.Size.X + b.Size.Y) / 2) continue;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -2; dx <= 1; dx++)
                    {
                        if (!In(mask, cx + dx, cy + dy) || Hash(cx + dx, cy + dy, salt) % 3 == 0) continue;
                        var c = art.GetPixel(cx + dx, cy + dy);
                        art.SetPixel(cx + dx, cy + dy, PixelArt.Snap(c.Lerp((dx + dy) < 0 ? NeglectLeaf.Base : NeglectLeaf.Shade, 0.75f) with { A = c.A }));
                    }
            }

            // A few missing shingles or boards: dark gaps on the structure, away from its rim.
            var gaps = Math.Max(2, b.Size.X * b.Size.Y / (ruin ? 120 : 260));
            for (var k = 0; k < gaps; k++)
            {
                var gx = b.Position.X + 3 + (int)(Hash(k, 5, salt) % (uint)Math.Max(1, b.Size.X - 6));
                var gy = b.Position.Y + 3 + (int)(Hash(k, 6, salt) % (uint)Math.Max(1, b.Size.Y - 6));
                if (!In(mask, gx, gy) || !In(mask, gx + 1, gy)) continue;
                var color = s.Kind == Shape.Pier ? Colors.Transparent : NeglectSoot;
                art.SetPixel(gx, gy, color);
                if (Hash(k, 7, salt) % 2 == 0) art.SetPixel(gx + 1, gy, color);
            }

            if (ruin)
            {
                if (roofed) RoofHole(art, s, b, salt);
                else Breaks(art, s, b, salt);
            }

            if (ruin && roofed) FallenPlanks(art, s, b, salt);
            Weeds(art, s, b, ruin, salt);
            if (s.Doorway && roofed) BoardedDoor(art, s, b);
            if (ruin && s.Kind is Shape.Building or Shape.Fence) Sapling(art, s, b, salt);
            return art;
        }

        /// <summary>B: a ragged hole in the front half of the roof, broken rafters over the dark inside.</summary>
        private static void RoofHole(Image art, Look s, Rect2I b, int salt)
        {
            var cx = b.Position.X + b.Size.X * 0.64f;
            var cy = b.Position.Y + b.Size.Y * 0.6f;
            var rx = MathF.Max(4.5f, b.Size.X * 0.22f);
            var ry = MathF.Max(4f, b.Size.Y * 0.2f);
            var horizontal = s.Door is DoorSide.South or DoorSide.North;
            for (var y = b.Position.Y; y < b.End.Y; y++)
                for (var x = b.Position.X; x < b.End.X; x++)
                {
                    if (!In(s.Mask, x, y) || Depth(s.Mask, x, y, 3) <= 3) continue;
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    var d = dx * dx + dy * dy;
                    var ragged = 1f - (Hash(x, y, salt + 9) % 4) * 0.09f;
                    if (d > ragged) continue;
                    var across = horizontal ? x : y;
                    var broken = Hash(across, (horizontal ? y : x) / 3, salt) % 4 == 0;
                    var color = d > ragged - 0.18f ? NeglectTimber.Edge
                        : across % 4 == 0 && !broken ? NeglectTimber.Shade
                        : NeglectSoot;
                    art.SetPixel(x, y, color);
                }
        }

        /// <summary>B for fences, piers, bridges and lanterns: a few short pieces missing.</summary>
        private static void Breaks(Image art, Look s, Rect2I b, int salt)
        {
            var lengthwise = b.Size.Y >= b.Size.X;
            for (var k = 0; k < 3; k++)
            {
                var at = (int)(Hash(k, 11, salt) % (uint)Math.Max(1, (lengthwise ? b.Size.Y : b.Size.X) - 10)) + 5;
                var span = 3 + (int)(Hash(k, 12, salt) % 3);
                for (var a = at; a < at + span; a++)
                    for (var c = 0; c < (lengthwise ? b.Size.X : b.Size.Y); c++)
                    {
                        var x = lengthwise ? b.Position.X + c : b.Position.X + a;
                        var y = lengthwise ? b.Position.Y + a : b.Position.Y + c;
                        if (!In(s.Mask, x, y)) continue;
                        // Fences lose rail between posts; decks lose whole planks.
                        if (s.Kind == Shape.Fence && Hash(x, y, salt) % 3 == 0) continue;
                        if (s.Kind is Shape.Pier && c > (lengthwise ? b.Size.X : b.Size.Y) * 0.7f) continue;
                        art.SetPixel(x, y, Colors.Transparent);
                    }
            }
        }

        /// <summary>B: planks fallen from the roof hole, lying on the ground outside the front wall.</summary>
        private static void FallenPlanks(Image image, Look s, Rect2I b, int salt)
        {
            var x = (int)(b.Position.X + b.Size.X * 0.62f);
            var y = s.Door == DoorSide.North ? b.Position.Y - 4 : b.End.Y + 1;
            if (y + 3 >= image.GetHeight()) y = b.End.Y - 2;
            for (var k = 0; k < 7; k++)
            {
                Blend(image, x + k, y + k / 3, NeglectTimber.Base);
                Blend(image, x + k, y + k / 3 - 1, NeglectTimber.Light);
                Blend(image, x + k + 1, y + k / 3 + 1, NeglectShadow);
            }
            for (var k = 0; k < 5; k++)
            {
                Blend(image, x - 3 + k, y + 2, NeglectTimber.Shade);
                Blend(image, x - 3 + k, y + 3, NeglectShadow);
            }
        }

        /// <summary>Weeds and long grass round the structure and over its doorstep, thicker on a ruin.</summary>
        private static void Weeds(Image image, Look s, Rect2I b, bool ruin, int salt)
        {
            var w = image.GetWidth();
            var h = image.GetHeight();
            // Match the short-end fallback in ApprovedArt.PortLandSide.
            var landSide = h >= w
                ? s.Door == DoorSide.South ? DoorSide.South : DoorSide.North
                : s.Door == DoorSide.West ? DoorSide.West : DoorSide.East;
            var odds = ruin ? 4u : 7u;
            var pathOdds = ruin ? 7u : 11u;
            for (var y = 1; y < h - 1; y++)
                for (var x = 1; x < w - 1; x++)
                {
                    if (In(s.Mask, x, y)) continue;
                    var near = Near(s.Mask, x, y, 4);
                    var onPath = s.Finished.GetPixel(x, y).A > 0.9f;
                    if (near > 4 && !onPath) continue;
                    // The approved pier has weeds on its land row or column.
                    if (s.Kind == Shape.Pier && !(landSide switch
                    {
                        DoorSide.North => y < 32,
                        DoorSide.South => y >= h - 32,
                        DoorSide.West => x < 32,
                        _ => x >= w - 32,
                    })) continue;
                    if (Hash(x, y, salt + 21) % (onPath && near > 4 ? pathOdds : odds) != 0) continue;
                    var tall = Hash(y, x, salt) % 2 == 0;
                    Blend(image, x, y, NeglectLeaf.Base);
                    Blend(image, x + 1, y, NeglectLeaf.Shade);
                    Blend(image, x, y - 1, NeglectLeaf.Light);
                    if (tall) Blend(image, x, y - 2, ruin && Hash(x, y, 4) % 9 == 0 ? NeglectFlower : NeglectLeaf.Highlight);
                }
        }

        /// <summary>Two crossed planks nailed over the door, at its place on the front wall.</summary>
        private static void BoardedDoor(Image image, Look s, Rect2I b)
        {
            var (x, y) = s.Door switch
            {
                DoorSide.North => ((int)s.DoorMiddle - 4, b.Position.Y - 1),
                DoorSide.East => (b.End.X - 5, (int)s.DoorMiddle - 3),
                DoorSide.West => (b.Position.X - 2, (int)s.DoorMiddle - 3),
                _ => ((int)s.DoorMiddle - 4, b.End.Y - 4),
            };
            for (var k = 0; k < 8; k++)
            {
                var down = y + k * 5 / 8;
                var up = y + 4 - k * 5 / 8;
                Blend(image, x + k + 1, down + 1, NeglectShadow);
                Blend(image, x + k, down, NeglectTimber.Highlight);
                Blend(image, x + k, up, NeglectTimber.Light);
            }
            Blend(image, x, y, NeglectSoot);
            Blend(image, x + 7, y + 4, NeglectSoot);
            Blend(image, x, y + 4, NeglectSoot);
            Blend(image, x + 7, y, NeglectSoot);
        }

        /// <summary>B: a young tree come up by a back corner where nobody clears it any more.</summary>
        private static void Sapling(Image image, Look s, Rect2I b, int salt)
        {
            var w = image.GetWidth();
            var h = image.GetHeight();
            var candidates = new (int X, int Y)[] { (b.End.X + 2, b.Position.Y + 4), (b.Position.X - 6, b.Position.Y + 4), (b.End.X - 6, b.Position.Y + 4), (b.Position.X + 5, b.Position.Y + 5) };
            // With no clear ground, it grows hard against the back corner, its crown leaning over the eave.
            var fallback = (X: Math.Min(b.End.X + 1, w - 4), Y: Math.Max(b.Position.Y, 4));
            var (x, y) = candidates.FirstOrDefault(c => c.X >= 4 && c.X <= w - 5 && c.Y >= 4 && c.Y <= h - 5 && !In(s.Mask, c.X, c.Y), fallback);
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    var d = dx * dx + dy * dy;
                    if (d <= 12) Blend(image, x + dx + 2, y + dy + 2, NeglectShadow);
                }
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    var d = dx * dx + dy * dy;
                    var lobe = 10 + (int)(Hash(x + dx, y + dy, salt) % 4);
                    if (d > lobe) continue;
                    var rim = d > lobe - 5;
                    Blend(image, x + dx, y + dy, rim ? (dx + dy < 0 ? NeglectLeaf.Base : NeglectLeaf.Edge) : dx + dy < -1 ? NeglectLeaf.Light : NeglectLeaf.Base);
                }
            Blend(image, x - 1, y - 2, NeglectLeaf.Highlight);
        }

        private static Rect2I Bounds(bool[,] mask)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (var y = 0; y < mask.GetLength(1); y++)
                for (var x = 0; x < mask.GetLength(0); x++)
                    if (mask[x, y])
                    {
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
            return maxX < 0 ? new Rect2I(0, 0, 1, 1) : new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static bool In(bool[,] mask, int x, int y) => x >= 0 && y >= 0 && x < mask.GetLength(0) && y < mask.GetLength(1) && mask[x, y];

        /// <summary>How far a mask pixel is from the mask's edge, counted in whole pixels (1 on the rim), up to <paramref name="limit"/>.</summary>
        private static int Depth(bool[,] mask, int x, int y, int limit)
        {
            for (var d = 1; d <= limit; d++)
                for (var dy = -d; dy <= d; dy++)
                    for (var dx = -d; dx <= d; dx++)
                        if ((Math.Abs(dx) == d || Math.Abs(dy) == d) && !In(mask, x + dx, y + dy)) return d;
            return limit + 1;
        }

        /// <summary>How close an empty pixel is to the mask, in whole pixels, up to <paramref name="limit"/> (limit + 1 when farther).</summary>
        private static int Near(bool[,] mask, int x, int y, int limit)
        {
            for (var d = 1; d <= limit; d++)
                for (var dy = -d; dy <= d; dy++)
                    for (var dx = -d; dx <= d; dx++)
                        if ((Math.Abs(dx) == d || Math.Abs(dy) == d) && In(mask, x + dx, y + dy)) return d;
            return limit + 1;
        }

        private static void Blend(Image image, int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) return;
            image.SetPixel(x, y, color.A >= 0.999f ? color : PixelArt.Snap(image.GetPixel(x, y).Blend(color)));
        }

        private static uint Hash(int x, int y, int salt) => PixelArt.Hash(x, y, salt);
    }
}
