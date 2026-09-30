using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// What a tile's Road piece joins: its orthogonal and diagonal Road
/// neighbours, whether the tile is Road itself, and which side a building's
/// door opens onto it.
/// </summary>
[Flags]
public enum RoadLinks : ushort
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8,
    NorthEast = 16,
    SouthEast = 32,
    SouthWest = 64,
    NorthWest = 128,
    Road = 256,
    DoorNorth = 512,
    DoorEast = 1024,
    DoorSouth = 2048,
    DoorWest = 4096,
}

/// <summary>
/// Packed-dirt Road pieces, generated for each combination of neighbours at 32
/// or 16 px per tile. A Road tile draws a rounded centre with an arm towards
/// each orthogonal Road neighbour, so pieces join only along the Road. A
/// diagonal step draws as one smooth diagonal where neither corner tile is
/// Road; a tile beside that corner draws its share of the diagonal. Where two
/// neighbours and the tile between them are all Road, the corner fills in. A
/// doorstep path runs from the centre to a building's door.
/// </summary>
public static class RoadSprites
{
    public const int VariantCount = 4;
    public static readonly Color Dirt = new("B99A6B");
    public static readonly Color WornEdge = new("977852");
    private static readonly Color DarkEdge = new("7A5E3E");
    private static readonly Color LightPebble = new("D1B98D");
    private static readonly Color DarkPebble = new("8C7050");
    private static readonly Color Mottle = new("B39365");
    private const float Radius = 8f;
    private const float DoorstepRadius = 3.5f;
    private static readonly Dictionary<(RoadLinks Links, int Variant, int Tile, bool Dark), ImageTexture> Cache = [];
    private static readonly bool[] DarkGround = Enum.GetValues<TerrainStyle>()
        .Select(style => NeedsDarkEdgeOn(TerrainTextures.BaseColor(style))).ToArray();

    /// <summary>
    /// Whether a Road on this ground takes a solid darker edge to stay visible:
    /// ground paler than the dirt, such as sand and snow, or close to its colour.
    /// </summary>
    public static bool NeedsDarkEdge(TerrainStyle style) => DarkGround[(int)style];

    private static bool NeedsDarkEdgeOn(Color ground)
    {
        var dr = ground.R - Dirt.R;
        var dg = ground.G - Dirt.G;
        var db = ground.B - Dirt.B;
        return MathF.Sqrt(dr * dr + dg * dg + db * db) < 0.22f || ground.Luminance > Dirt.Luminance;
    }

    /// <summary>Whether a tile has anything to draw: it is Road, or a diagonal Road passes its corner.</summary>
    public static bool Draws(RoadLinks links) => links.HasFlag(RoadLinks.Road) ||
        Corner(links, RoadLinks.North | RoadLinks.East, RoadLinks.NorthEast) ||
        Corner(links, RoadLinks.East | RoadLinks.South, RoadLinks.SouthEast) ||
        Corner(links, RoadLinks.South | RoadLinks.West, RoadLinks.SouthWest) ||
        Corner(links, RoadLinks.West | RoadLinks.North, RoadLinks.NorthWest);

    private static bool Corner(RoadLinks links, RoadLinks sides, RoadLinks between) =>
        links.HasFlag(sides) && !links.HasFlag(between);

    public static ImageTexture Texture(RoadLinks links, int variant, int tilePixels, bool darkEdge)
    {
        var key = (links, variant, tilePixels, darkEdge);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(Render(links, variant, tilePixels, darkEdge));
        Cache[key] = texture;
        return texture;
    }

    public static Image Render(RoadLinks links, int variant, int tilePixels, bool darkEdge)
    {
        var image = Image.CreateEmpty(tilePixels, tilePixels, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var segments = Segments(links).ToArray();
        var road = links.HasFlag(RoadLinks.Road);
        var scale = 32f / tilePixels;
        var edgeWidth = (darkEdge ? 2f : 1.4f) * scale;
        for (var py = 0; py < tilePixels; py++)
            for (var px = 0; px < tilePixels; px++)
            {
                // Distances are measured on the 32-unit tile whatever the output size.
                var x = (px + 0.5f) * scale;
                var y = (py + 0.5f) * scale;
                var inside = float.MaxValue;
                foreach (var (ax, ay, bx, by, radius) in segments)
                    inside = Math.Min(inside, SegmentDistance(x, y, ax, ay, bx, by) - radius);
                if (road && InFilledCorner(links, x, y)) inside = Math.Min(inside, -Radius);
                if (inside > 0) continue;
                var hash = PixelArt.Hash(px + variant * 97 + (road ? 0 : 31), py, 3 + tilePixels) % 100;
                var edge = inside > -edgeWidth;
                // Ordinary edges fray; on pale ground the edge stays solid and darker.
                if (edge && !darkEdge && hash < 30) continue;
                image.SetPixel(px, py, edge ? darkEdge ? DarkEdge : WornEdge
                    : hash < 6 ? LightPebble : hash < 10 ? DarkPebble : hash < 40 ? Mottle : Dirt);
            }
        return image;
    }

    private static IEnumerable<(float Ax, float Ay, float Bx, float By, float Radius)> Segments(RoadLinks links)
    {
        if (!links.HasFlag(RoadLinks.Road))
        {
            foreach (var band in CornerBands(links)) yield return band;
            yield break;
        }
        yield return (16, 16, 16, 16, Radius);
        if (links.HasFlag(RoadLinks.North)) yield return (16, 16, 16, -16, Radius);
        if (links.HasFlag(RoadLinks.East)) yield return (16, 16, 48, 16, Radius);
        if (links.HasFlag(RoadLinks.South)) yield return (16, 16, 16, 48, Radius);
        if (links.HasFlag(RoadLinks.West)) yield return (16, 16, -16, 16, Radius);
        // A diagonal step joins directly only where no straight path does.
        if (Diagonal(links, RoadLinks.NorthEast, RoadLinks.North, RoadLinks.East)) yield return (16, 16, 48, -16, Radius);
        if (Diagonal(links, RoadLinks.SouthEast, RoadLinks.South, RoadLinks.East)) yield return (16, 16, 48, 48, Radius);
        if (Diagonal(links, RoadLinks.SouthWest, RoadLinks.South, RoadLinks.West)) yield return (16, 16, -16, 48, Radius);
        if (Diagonal(links, RoadLinks.NorthWest, RoadLinks.North, RoadLinks.West)) yield return (16, 16, -16, -16, Radius);
        if (links.HasFlag(RoadLinks.DoorNorth)) yield return (16, 16, 16, -6, DoorstepRadius);
        if (links.HasFlag(RoadLinks.DoorEast)) yield return (16, 16, 38, 16, DoorstepRadius);
        if (links.HasFlag(RoadLinks.DoorSouth)) yield return (16, 16, 16, 38, DoorstepRadius);
        if (links.HasFlag(RoadLinks.DoorWest)) yield return (16, 16, -6, 16, DoorstepRadius);
    }

    private static bool Diagonal(RoadLinks links, RoadLinks corner, RoadLinks first, RoadLinks second) =>
        links.HasFlag(corner) && !links.HasFlag(first) && !links.HasFlag(second);

    /// <summary>
    /// For a tile that isn't Road: the part of a diagonal Road between two of
    /// its orthogonal neighbours that crosses its corner, drawn when the tile
    /// on the far side of that corner isn't Road either.
    /// </summary>
    private static IEnumerable<(float, float, float, float, float)> CornerBands(RoadLinks links)
    {
        if (links.HasFlag(RoadLinks.Road)) yield break;
        if (Corner(links, RoadLinks.North | RoadLinks.East, RoadLinks.NorthEast)) yield return (16, -16, 48, 16, Radius);
        if (Corner(links, RoadLinks.East | RoadLinks.South, RoadLinks.SouthEast)) yield return (48, 16, 16, 48, Radius);
        if (Corner(links, RoadLinks.South | RoadLinks.West, RoadLinks.SouthWest)) yield return (16, 48, -16, 16, Radius);
        if (Corner(links, RoadLinks.West | RoadLinks.North, RoadLinks.NorthWest)) yield return (-16, 16, 16, -16, Radius);
    }

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
}
