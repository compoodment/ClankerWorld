using Godot;

namespace ClankerWorld.GodotClient.UI;

public static partial class BuildingSprites
{
    private static readonly Dictionary<(BuildingKind Kind, int Width, int Height, int Tile, BuildingDoor Door, int Stage), ImageTexture> SiteCache = [];

    /// <summary>
    /// A building under construction at stage 1, 2 or 3 (October 7 art
    /// review): a cleared, staked-out site with materials, then the footing,
    /// sill, posts and half a floor, then the walls with the roof half on from
    /// the back. Drawn at 32 px over the building's own approved picture and
    /// halved for 16 px tiles, as the Port is.
    /// </summary>
    public static ImageTexture ConstructionTexture(BuildingKind kind, int width, int height, int tilePixels, BuildingDoor door, int stage)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        stage = Math.Clamp(stage, 1, 3);
        var key = (kind, width, height, tilePixels, door, stage);
        if (SiteCache.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(RenderConstruction(kind, width, height, tilePixels, door, stage));
        SiteCache[key] = texture;
        return texture;
    }

    public static Image RenderConstruction(BuildingKind kind, int width, int height, int tilePixels, BuildingDoor door, int stage)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        var site = Construction.Draw(kind, width, height, door, Math.Clamp(stage, 1, 3));
        if (tilePixels != 32) site.Resize(width * tilePixels, height * tilePixels, Image.Interpolation.Nearest);
        return site;
    }

    /// <summary>The flat colour of a construction site at overview zoom: cleared earth, or timber for a pier over water.</summary>
    public static Color SiteColor(BuildingKind kind) => kind == BuildingKind.Port ? new("8A6440") : new("B99A6B");

    private static readonly Dictionary<(int Tile, int Stage), ImageTexture> LanternSiteCache = [];

    /// <summary>
    /// The materials on a street lantern's own tile while it is built: a stone
    /// heap and a log at stage 1, the log alone at stage 2, nothing once the
    /// fitting stands. The fitting itself goes up on the Road edge with the
    /// finished lamp (see <see cref="StreetLanternSite"/>).
    /// </summary>
    public static ImageTexture? LanternSiteTexture(int tilePixels, int stage)
    {
        if (stage >= 3) return null;
        stage = Math.Max(1, stage);
        if (LanternSiteCache.TryGetValue((tilePixels, stage), out var cached)) return cached;
        var image = Construction.LanternMaterials(stage);
        if (tilePixels != 32) image.Resize(tilePixels, tilePixels, Image.Interpolation.Nearest);
        var texture = ImageTexture.CreateFromImage(image);
        LanternSiteCache[(tilePixels, stage)] = texture;
        return texture;
    }

    /// <summary>The approved construction stages, copied from <c>tools/ArtPreview/Proposed/BuildingStates.cs</c>.</summary>
    private static class Construction
    {
        private readonly record struct SiteRamp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight);

        private static readonly SiteRamp SiteTimber = new(new("3F2A1A"), new("6E4E31"), new("8A6440"), new("A77C52"), new("D2AC77"));
        private static readonly SiteRamp SiteRock = new(new("4A4542"), new("625B56"), new("756D68"), new("8B837D"), new("A49C95"));
        private static readonly SiteRamp SiteDirt = new(new("6E5538"), new("977852"), new("B99A6B"), new("C9AC7C"), new("D9C08F"));
        private static readonly Color SiteString = new("E8DCC0");
        private static readonly Color SiteSoot = new("2A2622");
        private static readonly Color SiteShadow = new(0.05f, 0.08f, 0.05f, 0.28f);

        /// <summary>What kind of structure is going up: a roofed building, a fence, or a pier over water.</summary>
        private enum Shape { Building, Fence, Pier }

        /// <summary>One site: the finished picture, the structure inside it, which way it faces and which side is built first.</summary>
        private sealed record Look(Image Finished, bool[,] Mask, int Width, int Height, DoorSide Door, float DoorMiddle, Vector2 Back, Shape Kind, bool Doorway);

        public static Image Draw(BuildingKind kind, int tilesWide, int tilesHigh, BuildingDoor door, int stage)
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
                    mask[x, y] = roofed
                        ? Inside(plan.Roof, x, y) || plan.Wing is { } wing && Inside(wing, x, y)
                        : finished.GetPixel(x, y).A > 0.9f && (shape != Shape.Fence || x < 6 || y < 6 || x >= w - 6 || y >= h - 6);
            // Work starts at the back, so the open frame faces the Road; a pier is built out from the land.
            var back = door.Side switch
            {
                DoorSide.North => new Vector2(0, 1),
                DoorSide.East => new Vector2(-1, 0),
                DoorSide.West => new Vector2(1, 0),
                _ => new Vector2(0, -1),
            };
            if (shape == Shape.Pier) back = -back;
            var middle = kind == BuildingKind.Silo ? tilesWide * 16f : plan.DoorMiddle;
            return Apply(new Look(finished, mask, w, h, door.Side, middle, back, shape, roofed || kind == BuildingKind.Silo), stage);
        }

        private static bool Inside(Rect2 area, int x, int y) =>
            x >= (int)MathF.Round(area.Position.X) && x < (int)MathF.Round(area.End.X) &&
            y >= (int)MathF.Round(area.Position.Y) && y < (int)MathF.Round(area.End.Y);

        /// <summary>One construction stage on its own, transparent outside the site.</summary>
        private static Image Apply(Look s, int stage)
        {
            var image = Image.CreateEmpty(s.Width, s.Height, false, Image.Format.Rgba8);
            image.Fill(Colors.Transparent);
            var mask = s.Mask;
            var b = Bounds(mask);
            var w = s.Width;
            var h = s.Height;
            var onWater = s.Kind is Shape.Pier;

            // Cleared earth over the site and a little round it (never over water).
            if (!onWater)
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var near = In(mask, x, y) ? 0 : Near(mask, x, y, 2);
                        if (near > 2 || (near == 2 && Hash(x, y, 3) % 3 == 0)) continue;
                        if (s.Kind == Shape.Fence && !In(mask, x, y) && near > 1) continue;
                        var n = Hash(x, y, 5) % 19;
                        Blend(image, x, y, n == 0 ? SiteDirt.Shade : n == 1 ? SiteDirt.Light : SiteDirt.Base);
                    }

            switch (s.Kind)
            {
                case Shape.Fence:
                    FenceStage(image, s, b, stage);
                    break;
                default:
                    if (stage == 1) StakeOut(image, s, b, onWater);
                    else Frame(image, s, b, stage);
                    break;
            }
            Materials(image, s, b, stage);
            return image;
        }

        /// <summary>Stage 1: corner stakes with a string line between them (piles already driven over water).</summary>
        private static void StakeOut(Image image, Look s, Rect2I b, bool onWater)
        {
            if (onWater)
            {
                Piles(image, s, b);
                return;
            }
            int left = b.Position.X, top = b.Position.Y, right = b.End.X - 1, bottom = b.End.Y - 1;
            for (var x = left; x <= right; x++) { Blend(image, x, top, SiteString); Blend(image, x, bottom, SiteString); }
            for (var y = top; y <= bottom; y++) { Blend(image, left, y, SiteString); Blend(image, right, y, SiteString); }
            foreach (var (x, y) in new[] { (left, top), (right, top), (left, bottom), (right, bottom) }) Stake(image, x - 1, y - 1);
        }

        private static void Stake(Image image, int x, int y)
        {
            Rect(image, x + 1, y + 1, 2, 2, SiteShadow);
            Rect(image, x, y, 2, 2, SiteTimber.Edge);
            Blend(image, x, y, SiteTimber.Highlight);
        }

        /// <summary>Piles driven along both edges of a pier every six pixels, following its outline, each with a ripple; none on land.</summary>
        private static void Piles(Image image, Look s, Rect2I b)
        {
            var lengthwise = b.Size.Y >= b.Size.X;
            var ripple = new Color(1, 1, 1, 0.35f);
            void Pile(int x, int y)
            {
                if (OnLand(s, x, y)) return;
                Blend(image, x - 1, y, ripple); Blend(image, x + 2, y + 1, ripple);
                Rect(image, x, y, 2, 2, SiteTimber.Edge);
                Blend(image, x, y, SiteTimber.Light);
            }
            for (var a = (lengthwise ? b.Position.Y : b.Position.X) + 2; a < (lengthwise ? b.End.Y : b.End.X) - 1; a += 6)
            {
                int first = -1, last = -1;
                for (var c = lengthwise ? b.Position.X : b.Position.Y; c < (lengthwise ? b.End.X : b.End.Y); c++)
                {
                    if (!(lengthwise ? In(s.Mask, c, a) : In(s.Mask, a, c))) continue;
                    if (first < 0) first = c;
                    last = c;
                }
                if (first < 0) continue;
                if (lengthwise) { Pile(first + 1, a); Pile(last - 2, a); }
                else { Pile(a, first + 1); Pile(a, last - 2); }
            }
        }

        /// <summary>
        /// Stages 2 and 3 for buildings and piers: a footing and sill
        /// round the outline with posts, the door gap open, floorboards; at stage 3
        /// the walls stand and the finished roof covers the back of the structure,
        /// with open rafters over the rest and a ladder at the front.
        /// </summary>
        private static void Frame(Image image, Look s, Rect2I b, int stage)
        {
            var mask = s.Mask;
            var w = mask.GetLength(0);
            var h = mask.GetLength(1);
            var timberOnly = s.Kind is Shape.Pier;
            var horizontalDoor = s.Door is DoorSide.South or DoorSide.North;
            bool InDoorGap(int x, int y)
            {
                if (!s.Doorway) return false;
                var along = horizontalDoor ? x + 0.5f : y + 0.5f;
                var onSide = s.Door switch
                {
                    DoorSide.North => y < b.Position.Y + 3,
                    DoorSide.East => x > b.End.X - 4,
                    DoorSide.West => x < b.Position.X + 3,
                    _ => y > b.End.Y - 4,
                };
                return onSide && MathF.Abs(along - s.DoorMiddle) < 3.5f;
            }
            if (timberOnly && stage == 2) Piles(image, s, b);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (!mask[x, y]) continue;
                    var depth = Depth(mask, x, y, 3);
                    var t = FromBack(b, s.Back, x, y);
                    if (depth <= 2 && !timberOnly)
                    {
                        if (InDoorGap(x, y)) continue;
                        // Stage 2: stone footing, lit on its north-west rim; stage 3: the walls' timber tops.
                        var litRim = depth == 1 && (!In(mask, x - 1, y) || !In(mask, x, y - 1));
                        var shadeRim = depth == 1 && (!In(mask, x + 1, y) || !In(mask, x, y + 1));
                        var ramp = stage == 2 ? SiteRock : SiteTimber;
                        Blend(image, x, y, litRim ? ramp.Light : shadeRim ? ramp.Shade : ramp.Base);
                        continue;
                    }
                    if (depth == 3 && !timberOnly)
                    {
                        if (!InDoorGap(x, y)) Blend(image, x, y, stage == 2 ? SiteTimber.Base : SiteTimber.Shade);
                        continue;
                    }
                    // Inside: floorboards across the structure, laid from the back.
                    var planked = stage == 3 || t < 0.6f;
                    var row = horizontalDoor || timberOnly && b.Size.Y >= b.Size.X ? y : x;
                    if (planked)
                    {
                        var board = row % 3;
                        Blend(image, x, y, board == 2 ? SiteTimber.Shade : (row / 3 + (horizontalDoor ? x : y) / 9) % 2 == 0 ? SiteTimber.Light : SiteTimber.Base);
                    }
                    else if (timberOnly) { if (depth <= 2) Blend(image, x, y, SiteTimber.Shade); }
                    else if (row % 6 == 0) Blend(image, x, y, SiteTimber.Shade);
                }
            if (!timberOnly) Posts(image, s, b, InDoorGap);
            if (stage < 3) return;

            // The finished roof over the back of the structure, its leading edge stepped course by course.
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (!mask[x, y]) continue;
                    var t = FromBack(b, s.Back, x, y);
                    var step = ((horizontalDoor ? x : y) / 4 % 2) * 0.05f;
                    if (t < 0.52f + step)
                    {
                        var c = s.Finished.GetPixel(x, y);
                        if (c.A > 0) Blend(image, x, y, c);
                    }
                    else if (!timberOnly && Depth(mask, x, y, 3) > 3)
                    {
                        // Open rafters over the dark interior.
                        var across = horizontalDoor ? x : y;
                        Blend(image, x, y, across % 4 == 0 ? SiteTimber.Base : across % 4 == 1 ? SiteTimber.Light : new Color(SiteSoot, 0.55f));
                    }
                }
            if (!timberOnly) Ladder(image, s, b);
        }

        /// <summary>Posts at the outline's corners and every ten pixels along it, kept out of the door gap.</summary>
        private static void Posts(Image image, Look s, Rect2I b, Func<int, int, bool> inDoorGap)
        {
            var mask = s.Mask;
            void Post(int x, int y)
            {
                if (!In(mask, x, y) || inDoorGap(x, y) || inDoorGap(x + 2, y + 2)) return;
                Rect(image, x + 1, y + 1, 3, 3, SiteShadow);
                Rect(image, x, y, 3, 3, SiteTimber.Edge);
                Rect(image, x, y, 2, 2, SiteTimber.Base);
                Blend(image, x, y, SiteTimber.Highlight);
            }
            for (var x = b.Position.X; x <= b.End.X - 3; x += 10) { Post(x, b.Position.Y); Post(x, b.End.Y - 3); }
            for (var y = b.Position.Y + 10; y <= b.End.Y - 3; y += 10) { Post(b.Position.X, y); Post(b.End.X - 3, y); }
            Post(b.End.X - 3, b.Position.Y);
            Post(b.End.X - 3, b.End.Y - 3);
        }

        /// <summary>A short ladder leaning on the front wall beside the door.</summary>
        private static void Ladder(Image image, Look s, Rect2I b)
        {
            var x = (int)MathF.Round(s.Door is DoorSide.South or DoorSide.North ? Math.Clamp(s.DoorMiddle + 7, b.Position.X + 2, b.End.X - 6) : b.End.X - 6);
            var y = s.Door == DoorSide.North ? b.Position.Y - 3 : b.End.Y - 6;
            for (var k = 0; k < 8; k++)
            {
                Blend(image, x, y + k, SiteTimber.Light);
                Blend(image, x + 3, y + k, SiteTimber.Base);
                Blend(image, x + 4, y + k, SiteShadow);
                if (k % 2 == 1) { Blend(image, x + 1, y + k, SiteTimber.Highlight); Blend(image, x + 2, y + k, SiteTimber.Light); }
            }
        }

        /// <summary>Fences: stakes and string, then posts and rails up the back half, then the whole fence before the yard is worn in.</summary>
        private static void FenceStage(Image image, Look s, Rect2I b, int stage)
        {
            if (stage == 1)
            {
                StakeOut(image, s, b, false);
                return;
            }
            var mask = s.Mask;
            for (var y = 0; y < mask.GetLength(1); y++)
                for (var x = 0; x < mask.GetLength(0); x++)
                {
                    if (!mask[x, y]) continue;
                    if (stage == 2 && FromBack(b, s.Back, x, y) > 0.5f) continue;
                    var c = s.Finished.GetPixel(x, y);
                    if (c.A > 0) Blend(image, x, y, c);
                }
            if (stage == 2) StakeOut(image, s, b, false);
        }

        /// <summary>A log stack, a stone heap and a plank stack beside the site, shrinking as the work goes on.</summary>
        private static void Materials(Image image, Look s, Rect2I b, int stage)
        {
            var w = s.Width;
            var h = s.Height;
            if (s.Kind == Shape.Pier)
            {
                // Over water the timber waits on the bank.
                var (x, y) = (2, h - 12);
                if (stage < 3) LogStack(image, x, y, stage == 1 ? 3 : 2);
                if (stage == 1) StoneHeap(image, x + 18, y + 4);
                return;
            }
            // The first corner of the footprint with room clear of the site, else the back corner over it.
            var candidates = new (int X, int Y)[] { (w - 16, h - 10), (2, h - 10), (w - 16, 2), (2, 2), (w / 2 - 7, h - 10) };
            (int X, int Y) spot = candidates.FirstOrDefault(c => Clear(s.Mask, c.X, c.Y, 14, 8), (X: -1, Y: -1));
            if (spot.X < 0)
            {
                if (stage > 1) return;
                spot = (b.End.X - 16, b.Position.Y + 3);
            }
            var logs = stage switch { 1 => 3, 2 => 2, _ => 1 };
            LogStack(image, spot.X, spot.Y, logs);
            if (stage == 1) StoneHeap(image, spot.X + 10, spot.Y + 4);
        }

        /// <summary>A street lantern's materials on its own 32 px tile, placed as in the approved lantern stages.</summary>
        public static Image LanternMaterials(int stage)
        {
            var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            image.Fill(Colors.Transparent);
            if (stage == 1) StoneHeap(image, 6, 8);
            if (stage < 3) LogStack(image, 18, 6, 1);
            return image;
        }

        private static bool Clear(bool[,] mask, int x0, int y0, int w, int h)
        {
            if (x0 < 1 || y0 < 1 || x0 + w > mask.GetLength(0) - 1 || y0 + h > mask.GetLength(1) - 1) return false;
            for (var y = y0 - 2; y < y0 + h + 2; y++)
                for (var x = x0 - 2; x < x0 + w + 2; x++)
                    if (In(mask, x, y)) return false;
            return true;
        }

        private static void LogStack(Image image, int x, int y, int logs)
        {
            for (var k = logs - 1; k >= 0; k--)
            {
                var ly = y + k * 2;
                Rect(image, x + 1, ly + 3, 11, 1, SiteShadow);
                Rect(image, x, ly, 11, 3, SiteTimber.Edge);
                Rect(image, x, ly, 10, 2, SiteTimber.Base);
                Rect(image, x, ly, 10, 1, SiteTimber.Light);
                Rect(image, x + 9, ly, 2, 2, SiteTimber.Highlight);
                Blend(image, x + 10, ly + 1, SiteTimber.Shade);
            }
        }

        private static void StoneHeap(Image image, int x, int y)
        {
            void Stone(int cx, int cy, int r)
            {
                for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                    {
                        var d = dx * dx + dy * dy;
                        if (d > r * r + 1) continue;
                        Blend(image, cx + dx + 1, cy + dy + 1, SiteShadow);
                    }
                for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                    {
                        var d = dx * dx + dy * dy;
                        if (d > r * r + 1) continue;
                        var rim = d > (r - 1) * (r - 1);
                        Blend(image, cx + dx, cy + dy, rim ? (dx + dy < 0 ? SiteRock.Light : SiteRock.Edge) : dx + dy < 0 ? SiteRock.Highlight : SiteRock.Base);
                    }
            }
            Stone(x + 2, y + 2, 2);
            Stone(x + 6, y + 3, 2);
            Stone(x + 4, y, 2);
        }

        /// <summary>A pier's landward tile row, on its door side, where no piles are driven.</summary>
        private static bool OnLand(Look s, int x, int y) => s.Kind == Shape.Pier && s.Door switch
        {
            DoorSide.North => y < 32,
            DoorSide.East => x >= s.Width - 32,
            DoorSide.West => x < 32,
            _ => y >= s.Height - 32,
        };

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

        /// <summary>A pixel's place from the back of the structure (0) to its front (1).</summary>
        private static float FromBack(Rect2I b, Vector2 back, int x, int y)
        {
            if (back.Y < 0) return (y - b.Position.Y) / (float)b.Size.Y;
            if (back.Y > 0) return (b.End.Y - 1 - y) / (float)b.Size.Y;
            if (back.X < 0) return (x - b.Position.X) / (float)b.Size.X;
            return (b.End.X - 1 - x) / (float)b.Size.X;
        }

        private static void Blend(Image image, int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) return;
            image.SetPixel(x, y, color.A >= 0.999f ? color : PixelArt.Snap(image.GetPixel(x, y).Blend(color)));
        }

        private static void Rect(Image image, int x, int y, int w, int h, Color color)
        {
            for (var py = y; py < y + h; py++)
                for (var px = x; px < x + w; px++)
                    Blend(image, px, py, color);
        }

        private static uint Hash(int x, int y, int salt) => PixelArt.Hash(x, y, salt);
    }
}
