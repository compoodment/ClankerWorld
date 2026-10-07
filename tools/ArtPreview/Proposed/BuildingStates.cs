using ArtPreview;
using ArtPreview.Proposed.Buildings;
using ArtPreview.Proposed.Yards;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.BuildingStates;

/// <summary>
/// Round 4 (October 7): how every building and built thing looks while it is
/// being built and after it is abandoned. Each state is drawn from the
/// thing's own approved picture, so the stages and the ruin always match the
/// finished art, lit from the north-west in STYLE.md ramp colours.
/// <para>
/// Building, in three stages before the finished picture: (1) the site is
/// cleared to bare earth, staked out with a string line, and timber and stone
/// are piled beside it; (2) a stone footing and a timber sill go round the
/// outline with posts at the corners and along the walls, the door gap left
/// open, and floorboards are half laid; (3) the walls are up and the roof is
/// half on, finished from the back so the open rafters face the Road, with a
/// ladder against the front. Fences, piers, bridges and lanterns follow the
/// same order with their own parts: posts or piles first, then the frame, then
/// the deck, rails or fitting.
/// </para>
/// <para>
/// Abandoned, in two strengths to choose from: A (neglected) fades and greys
/// the roof a little, adds moss, a few missing shingles, weeds round the walls
/// and over the doorstep, and boards the door with two crossed planks; B
/// (falling apart) adds to that a hole in the roof showing broken rafters,
/// fallen planks on the ground, thicker weeds and a sapling. Fences, piers and
/// bridges lose rails or planks instead of a roof.
/// </para>
/// </summary>
public sealed class ConstructionProposal : IArtProposal
{
    public string Family => "construction";

    public IEnumerable<Entry> Render()
    {
        foreach (var s in States.Subjects())
        {
            yield return new(Family, $"{s.Id}.1", States.Stage(s, 1), s.Id == "house.1x1" ? "Stage 1: cleared earth, stakes and a string line, timber and stone piled ready." : null);
            yield return new(Family, $"{s.Id}.2", States.Stage(s, 2), s.Id == "house.1x1" ? "Stage 2: stone footing, timber sill and posts, the door gap open, floorboards half laid." : null);
            yield return new(Family, $"{s.Id}.3", States.Stage(s, 3), s.Id == "house.1x1" ? "Stage 3: walls up, the roof half on from the back, open rafters and a ladder at the front." : null);
            yield return new(Family, $"{s.Id}.done", States.Finished(s), s.Id == "house.1x1" ? "Finished: the approved picture, unchanged." : null);
        }
    }
}

/// <summary>The abandoned looks, A (neglected) and B (falling apart), beside each finished picture.</summary>
public sealed class AbandonedProposal : IArtProposal
{
    public string Family => "abandoned";

    public IEnumerable<Entry> Render()
    {
        foreach (var s in States.Subjects())
        {
            yield return new(Family, $"{s.Id}.done", States.Finished(s), s.Id == "house.1x1" ? "Lived in: the approved picture." : null);
            yield return new(Family, $"{s.Id}.A", States.Abandoned(s, ruin: false), s.Id == "house.1x1" ? "A, neglected: faded roof, moss, a few missing shingles, weeds, the door boarded up." : null);
            yield return new(Family, $"{s.Id}.B", States.Abandoned(s, ruin: true), s.Id == "house.1x1" ? "B, falling apart: as A with a hole in the roof, fallen planks, thicker weeds and a sapling." : null);
        }
    }
}

/// <summary>What the state drawings need to know about one building or built thing.</summary>
internal enum Build { Building, Fence, Pier, Bridge, Lantern }

internal sealed record Subject(string Id, int TilesWide, int TilesHigh, Image Finished, bool[,] Mask, DoorSide Door,
    float DoorMiddle, Func<Image> Ground, Vector2 Back, Build Kind, bool Doorway, int Salt = 0);

internal static class States
{
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight);

    private static readonly Ramp Timber = new(new("3F2A1A"), new("6E4E31"), new("8A6440"), new("A77C52"), new("D2AC77"));
    private static readonly Ramp Rock = new(new("4A4542"), new("625B56"), new("756D68"), new("8B837D"), new("A49C95"));
    private static readonly Ramp Dirt = new(new("6E5538"), new("977852"), new("B99A6B"), new("C9AC7C"), new("D9C08F"));
    private static readonly Ramp Leaf = new(new("3C5F2E"), new("4C7A3A"), new("5E8C45"), new("79A657"), new("9BC66F"));
    private static readonly Color String = new("E8DCC0");
    private static readonly Color Soot = new("2A2622");
    private static readonly Color Flower = new("F2CC5E");
    private static readonly Color Shadow = new(0.05f, 0.08f, 0.05f, 0.28f);

    // ------------------------------------------------------------------ subjects

    public static IEnumerable<Subject> Subjects()
    {
        yield return Roofed("house.1x1", BuildingKind.House, 1, 1);
        yield return Roofed("house.2x2", BuildingKind.House, 2, 2);
        yield return Roofed("farmhouse.1x1", BuildingKind.Farmhouse, 1, 1);
        yield return Roofed("farmhouse.2x2", BuildingKind.Farmhouse, 2, 2);
        yield return Roofed("warehouse.2x2", BuildingKind.Warehouse, 2, 2);
        yield return Roofed("blacksmith.1x2", BuildingKind.Blacksmith, 1, 2);
        yield return Roofed("tailor.1x1", BuildingKind.TailorShop, 1, 1);
        yield return Roofed("store.1x2", BuildingKind.Store, 1, 2);
        yield return Roofed("workshop.1x1", BuildingKind.Workshop, 1, 1);
        yield return Roofed("clinic.1x2", BuildingKind.Clinic, 1, 2);
        yield return Roofed("restaurant.2x2", BuildingKind.Restaurant, 2, 2);
        yield return Opaque("silo.1x1", BuildingSprites.Render(BuildingKind.Silo, 1, 1, 32), 1, 1, DoorSide.South, 16, () => Grass(1, 1), new(0, -1), Build.Building, doorway: true)
            with { Salt = BuildingSprites.NeglectSalt(BuildingKind.Silo, 1, 1) };
        yield return Roofed("townhall.3x4", BuildingKind.TownHall, 3, 4);
        yield return Roofed("market.2x2", BuildingKind.Market, 2, 2);
        yield return Opaque("stall.1x1", BuildingSprites.Render(BuildingKind.MarketStall, 1, 1, 32), 1, 1, DoorSide.South, 16, () => Grass(1, 1), new(0, -1), Build.Building, doorway: false)
            with { Salt = BuildingSprites.NeglectSalt(BuildingKind.MarketStall, 1, 1) };
        var portDoor = new BuildingDoor(DoorSide.South, 1);
        yield return Opaque("port.2x4", BuildingSprites.Render(BuildingKind.Port, 2, 4, 32, portDoor), 2, 4, DoorSide.South, 48,
            () => BuildingsProposal.HarbourGround(2, 4, DoorSide.South, TerrainStyle.Lake), new(0, 1), Build.Pier, doorway: false)
            with { Salt = BuildingSprites.NeglectSalt(BuildingKind.Port, 2, 4) };
        yield return Fence("yard.2x2", YardsProposal.Draw(YardsProposal.Option.A, 2, 2, new BuildingDoor(DoorSide.South)), 2, 2);
        yield return Opaque("lantern.stone", Lantern(LanternStyle.Stone), 1, 1, DoorSide.South, 16, () => Grass(1, 1), new(0, 1), Build.Lantern, doorway: false);
        yield return Opaque("lantern.hanging", Lantern(LanternStyle.Hanging), 1, 1, DoorSide.South, 16, () => Grass(1, 1), new(0, 1), Build.Lantern, doorway: false);
        yield return Opaque("bridge.3", BridgeDeck(), 5, 1, DoorSide.West, 16, () => RiverGround(), new(-1, 0), Build.Bridge, doorway: false);
    }

    private static Subject Roofed(string id, BuildingKind kind, int w, int h, DoorSide side = DoorSide.South)
    {
        var door = new BuildingDoor(side);
        var plan = BuildingSprites.Plan(kind, w, h, door);
        var mask = new bool[w * 32, h * 32];
        FillMask(mask, plan.Roof);
        if (plan.Wing is { } wing) FillMask(mask, wing);
        return new(id, w, h, BuildingSprites.Render(kind, w, h, 32, door), mask, side, plan.DoorMiddle, () => Grass(w, h), BackOf(side), Build.Building, true,
            BuildingSprites.NeglectSalt(kind, w, h));
    }

    private static Subject Opaque(string id, Image finished, int w, int h, DoorSide side, float doorMiddle, Func<Image> ground, Vector2 back, Build kind, bool doorway)
    {
        var mask = new bool[w * 32, h * 32];
        for (var y = 0; y < h * 32; y++)
            for (var x = 0; x < w * 32; x++)
                mask[x, y] = finished.GetPixel(x, y).A > 0.9f;
        return new(id, w, h, finished, mask, side, doorMiddle, ground, back, kind, doorway);
    }

    /// <summary>A yard: only the fence band, five units in from the footprint's edge, counts as the structure.</summary>
    private static Subject Fence(string id, Image finished, int w, int h)
    {
        var mask = new bool[w * 32, h * 32];
        for (var y = 0; y < h * 32; y++)
            for (var x = 0; x < w * 32; x++)
                mask[x, y] = (x < 6 || y < 6 || x >= w * 32 - 6 || y >= h * 32 - 6) && finished.GetPixel(x, y).A > 0.9f;
        return new(id, w, h, finished, mask, DoorSide.South, w * 16, () => Grass(w, h), new(0, -1), Build.Fence, false,
            BuildingSprites.NeglectSalt(BuildingKind.AnimalYard, w, h));
    }

    private static Vector2 BackOf(DoorSide door) => door switch
    {
        DoorSide.North => new(0, 1),
        DoorSide.East => new(-1, 0),
        DoorSide.West => new(1, 0),
        _ => new(0, -1),
    };

    private static void FillMask(bool[,] mask, Rect2 area)
    {
        for (var y = (int)MathF.Round(area.Position.Y); y < (int)MathF.Round(area.End.Y); y++)
            for (var x = (int)MathF.Round(area.Position.X); x < (int)MathF.Round(area.End.X); x++)
                if (x >= 0 && y >= 0 && x < mask.GetLength(0) && y < mask.GetLength(1)) mask[x, y] = true;
    }

    /// <summary>A street lantern's daytime fitting, standing in the middle of a tile beside a Road to its east.</summary>
    private static Image Lantern(LanternStyle style)
    {
        var image = Bitmap.Empty(32, 32);
        var post = style == LanternStyle.Stone ? new Vector2(16, 17) : new Vector2(9, 18);
        foreach (var cell in NightLightShapes.StreetLantern(style, post, new Vector2(1, 0), 0f, 0f, 7))
        {
            if (cell.Kind != LightCellKind.Paint) continue;
            for (var y = (int)MathF.Floor(cell.Area.Position.Y); y < (int)MathF.Ceiling(cell.Area.End.Y); y++)
                for (var x = (int)MathF.Floor(cell.Area.Position.X); x < (int)MathF.Ceiling(cell.Area.End.X); x++)
                    Blend(image, x, y, cell.Color);
        }
        return image;
    }

    /// <summary>A three-span bridge deck across a river, with a grass bank tile at each end.</summary>
    private static Image BridgeDeck()
    {
        var image = Bitmap.Empty(5 * 32, 32);
        var deck = RoadSprites.BridgeDeck(true, 32);
        for (var tile = 1; tile <= 3; tile++) Sheet.Blend(image, deck, tile * 32, 0);
        return image;
    }

    private static Image Grass(int w, int h)
    {
        var ground = Image.CreateEmpty(w * 32, h * 32, false, Image.Format.Rgba8);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                ground.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(x + 3, y + 1), 32), new Rect2I(0, 0, 32, 32), new Vector2I(x * 32, y * 32));
        return ground;
    }

    private static Image RiverGround()
    {
        var ground = Grass(5, 1);
        var atlas = WaterTextures.Atlas(32).GetImage();
        for (var x = 1; x <= 3; x++) ground.BlitRect(atlas, (Rect2I)WaterTextures.Region(TerrainStyle.River, x, 0, 32), new Vector2I(x * 32, 0));
        return ground;
    }

    // ------------------------------------------------------------------ geometry

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

    // ------------------------------------------------------------------ construction

    public static Image Finished(Subject s)
    {
        var image = s.Ground();
        Sheet.Blend(image, s.Finished, 0, 0);
        return image;
    }

    /// <summary>A construction stage over its ground.</summary>
    public static Image Stage(Subject s, int stage)
    {
        var image = s.Ground();
        Sheet.Blend(image, StageArt(s, stage, image), 0, 0);
        return image;
    }

    /// <summary>
    /// A construction stage on its own, transparent outside the site, as the
    /// game draws it over the map. <paramref name="ground"/> is read only to
    /// keep a bridge's piles off its banks; a pier's land is the tile row on
    /// its door side.
    /// </summary>
    public static Image StageArt(Subject s, int stage, Image? ground = null)
    {
        var image = Bitmap.Empty(s.TilesWide * 32, s.TilesHigh * 32);
        var mask = s.Mask;
        var b = Bounds(mask);
        var w = s.TilesWide * 32;
        var h = s.TilesHigh * 32;
        var onWater = s.Kind is Build.Pier or Build.Bridge;

        // Cleared earth over the site and a little round it (never over water).
        if (!onWater)
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var near = In(mask, x, y) ? 0 : Near(mask, x, y, 2);
                    if (near > 2 || (near == 2 && Hash(x, y, 3) % 3 == 0)) continue;
                    if (s.Kind == Build.Fence && !In(mask, x, y) && near > 1) continue;
                    var n = Hash(x, y, 5) % 19;
                    Blend(image, x, y, n == 0 ? Dirt.Shade : n == 1 ? Dirt.Light : Dirt.Base);
                }

        switch (s.Kind)
        {
            case Build.Fence:
                FenceStage(image, s, b, stage);
                break;
            case Build.Lantern:
                LanternStage(image, s, b, stage);
                break;
            default:
                if (stage == 1) StakeOut(image, s, b, onWater, ground);
                else Frame(image, s, b, stage, ground);
                break;
        }
        Materials(image, s, b, stage);
        return image;
    }

    /// <summary>Stage 1: corner stakes with a string line between them (piles already driven over water).</summary>
    private static void StakeOut(Image image, Subject s, Rect2I b, bool onWater, Image? ground = null)
    {
        if (onWater)
        {
            Piles(image, s, b, ground);
            return;
        }
        int left = b.Position.X, top = b.Position.Y, right = b.End.X - 1, bottom = b.End.Y - 1;
        for (var x = left; x <= right; x++) { Blend(image, x, top, String); Blend(image, x, bottom, String); }
        for (var y = top; y <= bottom; y++) { Blend(image, left, y, String); Blend(image, right, y, String); }
        foreach (var (x, y) in new[] { (left, top), (right, top), (left, bottom), (right, bottom) }) Stake(image, x - 1, y - 1);
    }

    private static void Stake(Image image, int x, int y)
    {
        Rect(image, x + 1, y + 1, 2, 2, Shadow);
        Rect(image, x, y, 2, 2, Timber.Edge);
        Blend(image, x, y, Timber.Highlight);
    }

    /// <summary>Piles driven along both edges of a pier or bridge every six pixels, following its outline, each with a ripple; none on land.</summary>
    private static void Piles(Image image, Subject s, Rect2I b, Image? ground)
    {
        var lengthwise = b.Size.Y >= b.Size.X;
        var ripple = new Color(1, 1, 1, 0.35f);
        void Pile(int x, int y)
        {
            if (OnLand(s, x, y) || s.Kind == Build.Bridge && ground is not null && IsLand(ground, x, y)) return;
            Blend(image, x - 1, y, ripple); Blend(image, x + 2, y + 1, ripple);
            Rect(image, x, y, 2, 2, Timber.Edge);
            Blend(image, x, y, Timber.Light);
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
    /// Stages 2 and 3 for buildings, piers and bridges: a footing and sill
    /// round the outline with posts, the door gap open, floorboards; at stage 3
    /// the walls stand and the finished roof covers the back of the structure,
    /// with open rafters over the rest and a ladder at the front.
    /// </summary>
    private static void Frame(Image image, Subject s, Rect2I b, int stage, Image? ground = null)
    {
        var mask = s.Mask;
        var w = mask.GetLength(0);
        var h = mask.GetLength(1);
        var timberOnly = s.Kind is Build.Pier or Build.Bridge;
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
        if (timberOnly && stage == 2) Piles(image, s, b, ground);
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
                    var ramp = stage == 2 ? Rock : Timber;
                    Blend(image, x, y, litRim ? ramp.Light : shadeRim ? ramp.Shade : ramp.Base);
                    continue;
                }
                if (depth == 3 && !timberOnly)
                {
                    if (!InDoorGap(x, y)) Blend(image, x, y, stage == 2 ? Timber.Base : Timber.Shade);
                    continue;
                }
                // Inside: floorboards across the structure, laid from the back.
                var planked = stage == 3 || t < 0.6f;
                var row = horizontalDoor || timberOnly && b.Size.Y >= b.Size.X ? y : x;
                if (planked)
                {
                    var board = row % 3;
                    Blend(image, x, y, board == 2 ? Timber.Shade : (row / 3 + (horizontalDoor ? x : y) / 9) % 2 == 0 ? Timber.Light : Timber.Base);
                }
                else if (timberOnly) { if (depth <= 2) Blend(image, x, y, Timber.Shade); }
                else if (row % 6 == 0) Blend(image, x, y, Timber.Shade);
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
                    Blend(image, x, y, across % 4 == 0 ? Timber.Base : across % 4 == 1 ? Timber.Light : new Color(Soot, 0.55f));
                }
            }
        if (!timberOnly) Ladder(image, s, b);
    }

    /// <summary>Posts at the outline's corners and every ten pixels along it, kept out of the door gap.</summary>
    private static void Posts(Image image, Subject s, Rect2I b, Func<int, int, bool> inDoorGap)
    {
        var mask = s.Mask;
        void Post(int x, int y)
        {
            if (!In(mask, x, y) || inDoorGap(x, y) || inDoorGap(x + 2, y + 2)) return;
            Rect(image, x + 1, y + 1, 3, 3, Shadow);
            Rect(image, x, y, 3, 3, Timber.Edge);
            Rect(image, x, y, 2, 2, Timber.Base);
            Blend(image, x, y, Timber.Highlight);
        }
        for (var x = b.Position.X; x <= b.End.X - 3; x += 10) { Post(x, b.Position.Y); Post(x, b.End.Y - 3); }
        for (var y = b.Position.Y + 10; y <= b.End.Y - 3; y += 10) { Post(b.Position.X, y); Post(b.End.X - 3, y); }
        Post(b.End.X - 3, b.Position.Y);
        Post(b.End.X - 3, b.End.Y - 3);
    }

    /// <summary>A short ladder leaning on the front wall beside the door.</summary>
    private static void Ladder(Image image, Subject s, Rect2I b)
    {
        var x = (int)MathF.Round(s.Door is DoorSide.South or DoorSide.North ? Math.Clamp(s.DoorMiddle + 7, b.Position.X + 2, b.End.X - 6) : b.End.X - 6);
        var y = s.Door == DoorSide.North ? b.Position.Y - 3 : b.End.Y - 6;
        for (var k = 0; k < 8; k++)
        {
            Blend(image, x, y + k, Timber.Light);
            Blend(image, x + 3, y + k, Timber.Base);
            Blend(image, x + 4, y + k, Shadow);
            if (k % 2 == 1) { Blend(image, x + 1, y + k, Timber.Highlight); Blend(image, x + 2, y + k, Timber.Light); }
        }
    }

    /// <summary>Fences: stakes and string, then posts and rails up the back half, then the whole fence before the yard is worn in.</summary>
    private static void FenceStage(Image image, Subject s, Rect2I b, int stage)
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

    /// <summary>Lanterns: a dug hole with stones beside it, then the post or pillar stub, then the whole fitting.</summary>
    private static void LanternStage(Image image, Subject s, Rect2I b, int stage)
    {
        var centre = new Vector2(b.Position.X + b.Size.X / 2f, b.End.Y - 3);
        if (stage == 1)
        {
            for (var y = -3; y <= 3; y++)
                for (var x = -3; x <= 3; x++)
                    if (x * x + y * y <= 9) Blend(image, (int)centre.X + x, (int)centre.Y + y, x * x + y * y <= 4 ? Soot : Dirt.Shade);
            return;
        }
        for (var y = 0; y < s.Mask.GetLength(1); y++)
            for (var x = 0; x < s.Mask.GetLength(0); x++)
            {
                if (!s.Mask[x, y]) continue;
                if (stage == 2 && new Vector2(x + 0.5f, y + 0.5f).DistanceTo(centre) > 4.5f) continue;
                Blend(image, x, y, s.Finished.GetPixel(x, y));
            }
    }

    /// <summary>A log stack, a stone heap and a plank stack beside the site, shrinking as the work goes on.</summary>
    private static void Materials(Image image, Subject s, Rect2I b, int stage)
    {
        var w = s.TilesWide * 32;
        var h = s.TilesHigh * 32;
        if (s.Kind is Build.Pier or Build.Bridge)
        {
            // Over water the timber waits on the bank.
            var (x, y) = s.Kind == Build.Bridge ? (3, 10) : (2, h - 12);
            if (stage < 3) LogStack(image, x, y, stage == 1 ? 3 : 2);
            if (stage == 1) StoneHeap(image, x + 18, y + 4);
            return;
        }
        if (s.Kind == Build.Lantern)
        {
            if (stage == 1) StoneHeap(image, 6, 8);
            if (stage < 3) LogStack(image, 18, 6, 1);
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
            Rect(image, x + 1, ly + 3, 11, 1, Shadow);
            Rect(image, x, ly, 11, 3, Timber.Edge);
            Rect(image, x, ly, 10, 2, Timber.Base);
            Rect(image, x, ly, 10, 1, Timber.Light);
            Rect(image, x + 9, ly, 2, 2, Timber.Highlight);
            Blend(image, x + 10, ly + 1, Timber.Shade);
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
                    Blend(image, cx + dx + 1, cy + dy + 1, Shadow);
                }
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    var d = dx * dx + dy * dy;
                    if (d > r * r + 1) continue;
                    var rim = d > (r - 1) * (r - 1);
                    Blend(image, cx + dx, cy + dy, rim ? (dx + dy < 0 ? Rock.Light : Rock.Edge) : dx + dy < 0 ? Rock.Highlight : Rock.Base);
                }
        }
        Stone(x + 2, y + 2, 2);
        Stone(x + 6, y + 3, 2);
        Stone(x + 4, y, 2);
    }

    // ------------------------------------------------------------------ abandoned

    /// <summary>The abandoned look over its ground.</summary>
    public static Image Abandoned(Subject s, bool ruin)
    {
        var image = s.Ground();
        Sheet.Blend(image, AbandonedArt(s, ruin), 0, 0);
        return image;
    }

    /// <summary>
    /// The abandoned look on its own, transparent outside the structure and
    /// its weeds, as the game draws it over the map: blended colours are
    /// snapped to the 8-bit steps Godot stores, so the client matches it
    /// pixel for pixel.
    /// </summary>
    public static Image AbandonedArt(Subject s, bool ruin)
    {
        var mask = s.Mask;
        var b = Bounds(mask);
        var w = s.TilesWide * 32;
        var h = s.TilesHigh * 32;
        var art = s.Finished.Duplicate();
        var salt = s.Salt + (ruin ? 7 : 0);
        var roofed = s.Kind == Build.Building;

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
                    art.SetPixel(cx + dx, cy + dy, PixelArt.Snap(c.Lerp((dx + dy) < 0 ? Leaf.Base : Leaf.Shade, 0.75f) with { A = c.A }));
                }
        }

        // A few missing shingles or boards: dark gaps on the structure, away from its rim.
        var gaps = Math.Max(2, b.Size.X * b.Size.Y / (ruin ? 120 : 260));
        for (var k = 0; k < gaps; k++)
        {
            var gx = b.Position.X + 3 + (int)(Hash(k, 5, salt) % (uint)Math.Max(1, b.Size.X - 6));
            var gy = b.Position.Y + 3 + (int)(Hash(k, 6, salt) % (uint)Math.Max(1, b.Size.Y - 6));
            if (!In(mask, gx, gy) || !In(mask, gx + 1, gy)) continue;
            var color = s.Kind is Build.Pier or Build.Bridge ? Colors.Transparent : Soot;
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
        if (ruin && s.Kind is Build.Building or Build.Fence) Sapling(art, s, b, salt);
        return art;
    }

    /// <summary>B: a ragged hole in the front half of the roof, broken rafters over the dark inside.</summary>
    private static void RoofHole(Image art, Subject s, Rect2I b, int salt)
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
                var color = d > ragged - 0.18f ? Timber.Edge
                    : across % 4 == 0 && !broken ? Timber.Shade
                    : Soot;
                art.SetPixel(x, y, color);
            }
    }

    /// <summary>B for fences, piers, bridges and lanterns: a few short pieces missing.</summary>
    private static void Breaks(Image art, Subject s, Rect2I b, int salt)
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
                    // Fences lose rail between posts; decks lose whole planks; a lantern only weathers.
                    if (s.Kind == Build.Lantern) continue;
                    if (s.Kind == Build.Fence && Hash(x, y, salt) % 3 == 0) continue;
                    if (s.Kind is Build.Pier && c > (lengthwise ? b.Size.X : b.Size.Y) * 0.7f) continue;
                    art.SetPixel(x, y, Colors.Transparent);
                }
        }
    }

    /// <summary>B: planks fallen from the roof hole, lying on the ground outside the front wall.</summary>
    private static void FallenPlanks(Image image, Subject s, Rect2I b, int salt)
    {
        var x = (int)(b.Position.X + b.Size.X * 0.62f);
        var y = s.Door == DoorSide.North ? b.Position.Y - 4 : b.End.Y + 1;
        if (y + 3 >= image.GetHeight()) y = b.End.Y - 2;
        for (var k = 0; k < 7; k++)
        {
            Blend(image, x + k, y + k / 3, Timber.Base);
            Blend(image, x + k, y + k / 3 - 1, Timber.Light);
            Blend(image, x + k + 1, y + k / 3 + 1, Shadow);
        }
        for (var k = 0; k < 5; k++)
        {
            Blend(image, x - 3 + k, y + 2, Timber.Shade);
            Blend(image, x - 3 + k, y + 3, Shadow);
        }
    }

    /// <summary>Weeds and long grass round the structure and over its doorstep, thicker on a ruin.</summary>
    private static void Weeds(Image image, Subject s, Rect2I b, bool ruin, int salt)
    {
        var w = image.GetWidth();
        var h = image.GetHeight();
        var odds = ruin ? 4u : 7u;
        var pathOdds = ruin ? 7u : 11u;
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                if (In(s.Mask, x, y)) continue;
                var near = Near(s.Mask, x, y, 4);
                var onPath = s.Finished.GetPixel(x, y).A > 0.9f;
                if (near > 4 && !onPath) continue;
                // Over water nothing grows: a pier gets no weeds, a bridge only on its banks.
                if (s.Kind == Build.Pier || s.Kind == Build.Bridge && x >= 32 && x < w - 32) continue;
                if (Hash(x, y, salt + 21) % (onPath && near > 4 ? pathOdds : odds) != 0) continue;
                var tall = Hash(y, x, salt) % 2 == 0;
                Blend(image, x, y, Leaf.Base);
                Blend(image, x + 1, y, Leaf.Shade);
                Blend(image, x, y - 1, Leaf.Light);
                if (tall) Blend(image, x, y - 2, ruin && Hash(x, y, 4) % 9 == 0 ? Flower : Leaf.Highlight);
            }
    }

    /// <summary>A pier's landward tile row, on its door side, where no piles are driven.</summary>
    private static bool OnLand(Subject s, int x, int y) => s.Kind == Build.Pier && s.Door switch
    {
        DoorSide.North => y < 32,
        DoorSide.East => x >= s.TilesWide * 32 - 32,
        DoorSide.West => x < 32,
        _ => y >= s.TilesHigh * 32 - 32,
    };

    /// <summary>A pixel shows land when it is mostly green rather than water blue.</summary>
    private static bool IsLand(Image image, int x, int y)
    {
        var c = image.GetPixel(x, y);
        return c.G > c.B;
    }

    /// <summary>Two crossed planks nailed over the door, at its place on the front wall.</summary>
    private static void BoardedDoor(Image image, Subject s, Rect2I b)
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
            Blend(image, x + k + 1, down + 1, Shadow);
            Blend(image, x + k, down, Timber.Highlight);
            Blend(image, x + k, up, Timber.Light);
        }
        Blend(image, x, y, Soot);
        Blend(image, x + 7, y + 4, Soot);
        Blend(image, x, y + 4, Soot);
        Blend(image, x + 7, y, Soot);
    }

    /// <summary>B: a young tree come up by a back corner where nobody clears it any more.</summary>
    private static void Sapling(Image image, Subject s, Rect2I b, int salt)
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
                if (d <= 12) Blend(image, x + dx + 2, y + dy + 2, Shadow);
            }
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                var d = dx * dx + dy * dy;
                var lobe = 10 + (int)(Hash(x + dx, y + dy, salt) % 4);
                if (d > lobe) continue;
                var rim = d > lobe - 5;
                Blend(image, x + dx, y + dy, rim ? (dx + dy < 0 ? Leaf.Base : Leaf.Edge) : dx + dy < -1 ? Leaf.Light : Leaf.Base);
            }
        Blend(image, x - 1, y - 2, Leaf.Highlight);
    }
}
