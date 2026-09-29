using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Rounded coasts and river banks. The neighboring land reaches a narrow,
/// wandering band into each water tile, using the same corner reaches as the
/// land-to-land edges so shorelines continue from tile to tile. A lighter
/// shallow band and, on coasts and lakes, a thin foam line follow that shape
/// instead of running in straight strips; rivers keep a quieter bank without
/// foam. Pieces are white masks tinted when drawn, so one small atlas serves
/// every land and water style.
/// </summary>
public static class CoastEdges
{
    public const int LandRow = 0;
    public const int ShallowRow = 1;
    public const int FoamRow = 2;
    public const int RiverShallowRow = 3;
    private const int Rows = 4;
    private static readonly Dictionary<int, Image> Images = [];
    private static readonly Dictionary<int, ImageTexture> Textures = [];

    public static readonly Color Foam = new(0.94f, 0.97f, 0.95f, 0.7f);

    public static Color ShallowColor(TerrainStyle water) => TerrainTextures.BaseColor(water).Lightened(0.16f);

    /// <summary>How far the shallow band reaches past the land, in atlas pixels.</summary>
    public static int Band(bool river, int atlasTileSize) => river
        ? (atlasTileSize >= 32 ? 2 : 1)
        : (atlasTileSize >= 32 ? 3 : 2);

    public static ImageTexture Atlas(int atlasTileSize)
    {
        if (Textures.TryGetValue(atlasTileSize, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(AtlasImage(atlasTileSize));
        Textures[atlasTileSize] = texture;
        return texture;
    }

    public static Rect2 Region(int row, int piece, int atlasTileSize) =>
        new(piece * atlasTileSize, row * atlasTileSize, atlasTileSize, atlasTileSize);

    /// <summary>One generated mask piece, for inspection and tests.</summary>
    public static Image Piece(int row, int piece, int atlasTileSize) =>
        AtlasImage(atlasTileSize).GetRegion(new Rect2I(piece * atlasTileSize, row * atlasTileSize,
            atlasTileSize, atlasTileSize));

    private static Image AtlasImage(int size)
    {
        if (Images.TryGetValue(size, out var cached)) return cached;
        var width = size * TerrainTransitions.PieceCount;
        var data = new byte[width * size * Rows * 4];
        for (var piece = 0; piece < TerrainTransitions.PieceCount; piece++)
        {
            // Style-independent shapes, so the band and foam can follow any
            // land's edge; no gaps, which would read as specks of foam.
            var land = TerrainTransitions.Mask(piece, size, PixelArt.Hash(7, piece + 1, size), gaps: false);
            var left = piece * size;
            TerrainTransitions.WriteMask(data, width, left, LandRow * size, size, land, Colors.White, softRim: true);
            TerrainTransitions.WriteMask(data, width, left, ShallowRow * size, size, Grow(land, size, Band(false, size)), Colors.White, softRim: false);
            TerrainTransitions.WriteMask(data, width, left, RiverShallowRow * size, size, Grow(land, size, Band(true, size)), Colors.White, softRim: false);
            var foam = Grow(land, size, 1);
            for (var index = 0; index < foam.Length; index++) foam[index] &= !land[index];
            TerrainTransitions.WriteMask(data, width, left, FoamRow * size, size, foam, Colors.White, softRim: false);
        }
        var image = Image.CreateFromData(width, size * Rows, false, Image.Format.Rgba8, data);
        Images[size] = image;
        return image;
    }

    /// <summary>
    /// The mask grown by a rounded distance. Beyond the tile the land is taken
    /// to continue as it meets the tile's border, which matches the shared
    /// corner reaches, so neighboring pieces grow to the same depth there.
    /// </summary>
    private static bool[] Grow(bool[] mask, int size, int distance)
    {
        var grown = new bool[mask.Length];
        var limit = (distance + 0.5f) * (distance + 0.5f);
        for (var py = 0; py < size; py++)
            for (var px = 0; px < size; px++)
            {
                var found = false;
                for (var dy = -distance; dy <= distance && !found; dy++)
                    for (var dx = -distance; dx <= distance && !found; dx++)
                    {
                        if (dx * dx + dy * dy > limit) continue;
                        var sx = Math.Clamp(px + dx, 0, size - 1);
                        var sy = Math.Clamp(py + dy, 0, size - 1);
                        found = mask[sy * size + sx];
                    }
                grown[py * size + px] = found;
            }
        return grown;
    }
}
