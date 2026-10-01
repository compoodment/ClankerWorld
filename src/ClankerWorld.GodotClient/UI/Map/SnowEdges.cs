using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Soft edge pieces for snow and tundra snow, approved in round three of the
/// October 1 art review. Each keeps the corner reaches of the ordinary piece
/// (the same <see cref="TerrainTransitions.Mask"/>, held exactly near the
/// piece's ends so neighbouring pieces still meet) and the first rows next to
/// the snow tile. Between the ends the snow thins out instead of stopping: a
/// smooth noise field, thresholded more strictly the further a pixel lies
/// beyond the ordinary edge, leaves the snowpack broken near its rim, then
/// clumps, then the odd half-transparent grain of frost, never past
/// <see cref="TerrainTransitions.MaximumReach"/>. The result is shaded as a low
/// drift: rims facing the north-west light take the light step, lee rims
/// facing south-east the shade step, and rims are part transparent.
/// </summary>
internal static class SnowEdges
{
    private const int Empty = 0, Snow = 1, Frost = 2;

    /// <summary>The style guide's Snow ramp: light and shade steps.</summary>
    private static readonly Color Light = new("DCE5E0");
    private static readonly Color Shade = new("B8C6C4");

    /// <summary>
    /// Pixels at each end of a side, or beside each tile edge of a corner,
    /// kept exactly as the ordinary piece, so the snow crosses the tile edge at
    /// the corner's shared reach.
    /// </summary>
    private const int Margin = 1;

    /// <summary>Past the margin, softening ramps in over this many pixels.</summary>
    private const float SoftenOver = 3f;

    public static bool Applies(TerrainStyle style) => style is TerrainStyle.Snow or TerrainStyle.TundraSnow;

    /// <summary>Writes one soft snow piece into RGBA atlas bytes.</summary>
    public static void Write(byte[] data, int stride, int left, int top, int size, TerrainStyle style, int piece)
    {
        // The seed the ordinary pieces use, so the solid core wanders the same way.
        var seed = PixelArt.Hash((int)style + 1, piece + 1, size);
        var solid = TerrainTransitions.Mask(piece, size, seed, gaps: false);
        var salt = (int)(seed % 1000003);
        var kind = piece < TerrainTransitions.OuterCornerPiece(0, 0) ? Side(solid, piece, size, salt) : Corner(solid, piece, size, salt);
        Tidy(kind, solid, size);
        Paint(data, stride, left, top, kind, size, TerrainTextures.BaseColor(style));
    }

    /// <summary>
    /// The noise threshold a pixel must beat to hold snow, from how far it
    /// lies beyond the ordinary edge in pixels (negative inside it): about four
    /// in five pixels hold snow three pixels inside, under half just outside,
    /// and the odd clump still four pixels out.
    /// </summary>
    private static float SnowThreshold(float beyond) => 0.5f + 0.1f * beyond;

    /// <summary>The threshold for a half-transparent frost pixel beyond the snow: sparse patches that thin out with distance.</summary>
    private static float FrostThreshold(float beyond) => 0.6f + 0.06f * beyond;

    /// <summary>
    /// Smooth value noise over a piece's local coordinates, stretched to
    /// spread across [0, 1]: blobs about four pixels across roughened by a
    /// two-pixel octave at 32 px, two-pixel blobs at 16 px, so thresholds make
    /// clumps rather than grain.
    /// </summary>
    private static float Noise(int u, int v, int size, int salt) => Stretch(size >= 32
        ? 0.68f * Value(u, v, 4, salt) + 0.32f * Value(u, v, 2, salt + 7)
        : Value(u, v, 2, salt));

    /// <summary>Frost patches: their own noise, three-pixel blobs at 32 px.</summary>
    private static float FrostNoise(int u, int v, int size, int salt) => Stretch(Value(u, v, size >= 32 ? 3 : 2, salt + 31));

    /// <summary>Interpolated value noise bunches up round one half; this spreads it back out.</summary>
    private static float Stretch(float value) => Math.Clamp(0.5f + (value - 0.5f) * 1.7f, 0f, 1f);

    private static float Value(int u, int v, int lattice, int salt)
    {
        // The lattice is turned about 35 degrees off the edge, so its rows
        // never line up into stripes running along the edge.
        const float cos = 0.819f, sin = 0.574f;
        var fu = ((u + 0.5f) * cos + (v + 0.5f) * sin) / lattice;
        var fv = (-(u + 0.5f) * sin + (v + 0.5f) * cos) / lattice;
        var iu = (int)MathF.Floor(fu);
        var iv = (int)MathF.Floor(fv);
        var tu = fu - iu;
        var tv = fv - iv;
        tu = tu * tu * (3 - 2 * tu);
        tv = tv * tv * (3 - 2 * tv);
        float At(int a, int b) => PixelArt.Hash(a, b, salt) % 1024 / 1023f;
        return Mathf.Lerp(Mathf.Lerp(At(iu, iv), At(iu + 1, iv), tu), Mathf.Lerp(At(iu, iv + 1), At(iu + 1, iv + 1), tu), tv);
    }

    /// <summary>A side piece in its local coordinates: u along the edge, v depth away from the snow tile.</summary>
    private static int[] Side(bool[] solid, int piece, int size, int salt)
    {
        var side = piece / TerrainTransitions.EdgePiece(1, 0, 0, 0);
        var deepest = TerrainTransitions.MaximumReach(size) - 1;
        var fine = size >= 32;
        int Index(int u, int v)
        {
            var (px, py) = side switch
            {
                0 => (u, v),
                1 => (size - 1 - v, u),
                2 => (u, size - 1 - v),
                _ => (v, u),
            };
            return py * size + px;
        }
        var kind = new int[size * size];
        for (var u = 0; u < size; u++)
        {
            var depth = 0;
            while (depth < size && solid[Index(u, depth)]) depth++;
            var fromEnd = Math.Min(u, size - 1 - u);
            var taper = Math.Clamp((fromEnd - Margin + 1) / SoftenOver, 0f, 1f);
            // The snowpack may break up only in its outer two rows at 32 px
            // (never at 16 px), so no gaps line up along the tile edge.
            var solidTo = fine ? Math.Max(2, depth - 2) : depth;
            for (var v = 0; v < size; v++)
            {
                var original = solid[Index(u, v)];
                if (fromEnd < Margin || v > deepest)
                {
                    kind[Index(u, v)] = original ? Snow : Empty;
                    continue;
                }
                var beyond = v + 0.5f - depth;
                // Near the ends a higher bar holds back new snow and frost.
                var held = 0.6f * (1 - taper);
                // At 16 px there is no room for detached clumps: snow only grows one-pixel bumps on the edge.
                if (!fine && beyond > 1) held = 1;
                if (v < Math.Min(depth, solidTo) || (original && taper < 1) || Noise(u, v, size, salt) > SnowThreshold(beyond) + held)
                    kind[Index(u, v)] = Snow;
                else if (beyond > -1 && FrostNoise(u, v, size, salt) > FrostThreshold(beyond) + 0.6f * (1 - taper))
                    kind[Index(u, v)] = Frost;
            }
        }
        return kind;
    }

    /// <summary>
    /// A corner piece in local coordinates counting away from its corner:
    /// softened on the diagonal by distance beyond the ordinary arc, exact
    /// beside both tile edges, so the edges of neighbouring tiles meet it.
    /// </summary>
    private static int[] Corner(bool[] solid, int piece, int size, int salt)
    {
        var innerStart = TerrainTransitions.InnerCornerPiece(0, 0);
        var inner = piece >= innerStart;
        var corner = (piece - (inner ? innerStart : TerrainTransitions.OuterCornerPiece(0, 0))) / TerrainTransitions.Levels;
        var deepest = TerrainTransitions.MaximumReach(size) - 1;
        int Index(int u, int v)
        {
            var (px, py) = corner switch
            {
                0 => (size - 1 - u, v),
                1 => (size - 1 - u, size - 1 - v),
                2 => (u, size - 1 - v),
                _ => (u, v),
            };
            return py * size + px;
        }
        // The ordinary arc, measured along the diagonal.
        var along = 0;
        while (along < size && solid[Index(along, along)]) along++;
        var arc = along * MathF.Sqrt(2);
        var kind = new int[size * size];
        for (var v = 0; v < size; v++)
            for (var u = 0; u < size; u++)
            {
                var original = solid[Index(u, v)];
                if (u < Margin || v < Margin || u > deepest || v > deepest)
                {
                    kind[Index(u, v)] = original ? Snow : Empty;
                    continue;
                }
                var beyond = MathF.Sqrt((u + 0.5f) * (u + 0.5f) + (v + 0.5f) * (v + 0.5f)) - arc;
                var held = 0.6f * (1 - Math.Clamp((Math.Min(u, v) - Margin + 1) / SoftenOver, 0f, 1f));
                // As on the sides, only the outer two pixels of the arc may break up, never at 16 px.
                var firm = original && (size < 32 || beyond < -2);
                if (firm || (original && held > 0) || Noise(u, v, size, salt) > SnowThreshold(beyond) + held) kind[Index(u, v)] = Snow;
                else if (beyond > -1 && FrostNoise(u, v, size, salt) > FrostThreshold(beyond) + held) kind[Index(u, v)] = Frost;
            }
        return kind;
    }

    /// <summary>
    /// Folds single pixels away: a lone new snow pixel becomes a
    /// half-transparent grain of frost (a lone pixel of the ordinary piece
    /// stays), and a one-pixel hole inside the snowpack fills in. The tile
    /// border is left as it is, so the corner reaches still meet.
    /// </summary>
    private static void Tidy(int[] kind, bool[] original, int size)
    {
        var source = (int[])kind.Clone();
        bool SnowAt(int x, int y) => source[y * size + x] == Snow;
        for (var y = 1; y < size - 1; y++)
            for (var x = 1; x < size - 1; x++)
            {
                var neighbours = (SnowAt(x - 1, y) ? 1 : 0) + (SnowAt(x + 1, y) ? 1 : 0) +
                    (SnowAt(x, y - 1) ? 1 : 0) + (SnowAt(x, y + 1) ? 1 : 0);
                var here = source[y * size + x];
                if (here == Snow && neighbours == 0 && !original[y * size + x]) kind[y * size + x] = Frost;
                else if (here == Empty && neighbours == 4) kind[y * size + x] = Snow;
            }
    }

    /// <summary>
    /// Colours the piece as a low drift: base inside, the light step on rims
    /// facing north or west, the shade step on lee rims facing south or east,
    /// lit rims part transparent, frost at about half alpha. Off-piece pixels
    /// count as snow, as in <see cref="TerrainTransitions.WriteMask"/>, so no
    /// rim appears along the tile edge.
    /// </summary>
    private static void Paint(byte[] data, int stride, int left, int top, int[] kind, int size, Color baseColor)
    {
        bool SnowAt(int x, int y) => x < 0 || y < 0 || x >= size || y >= size || kind[y * size + x] == Snow;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var here = kind[y * size + x];
                if (here == Empty) continue;
                Color color;
                if (here == Frost)
                {
                    color = Light with { A = 0.45f };
                }
                else
                {
                    var lit = !SnowAt(x, y - 1) || !SnowAt(x - 1, y);
                    var lee = !SnowAt(x, y + 1) || !SnowAt(x + 1, y);
                    // At 32 px the lee slope is two pixels deep: the step behind a lee rim is shaded too.
                    var leeSlope = size >= 32 && !lit && (!SnowAt(x, y + 2) || !SnowAt(x + 2, y));
                    color = (lit, lee) switch
                    {
                        (false, false) => leeSlope ? Shade : baseColor,
                        (true, false) => Light with { A = 0.75f },
                        (false, true) => Shade,
                        _ => baseColor with { A = 0.65f },
                    };
                }
                // Stored by rounding, as the reviewed drawing was.
                var offset = ((top + y) * stride + left + x) * 4;
                data[offset] = (byte)MathF.Round(color.R * 255);
                data[offset + 1] = (byte)MathF.Round(color.G * 255);
                data[offset + 2] = (byte)MathF.Round(color.B * 255);
                data[offset + 3] = (byte)MathF.Round(color.A * 255);
            }
    }
}
