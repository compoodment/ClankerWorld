using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Art approved in round three of the October 1 review: snow and tundra
    /// snow thin into half-transparent frost at their edges while other
    /// surfaces keep their soft rim, and cacti stand on some cactus-cover
    /// tiles, never on a Road, and only at sprite zoom.
    /// </summary>
    private async Task VerifyDesertAndSnowArtAsync()
    {
        static bool HasFrost(TerrainStyle style, int size)
        {
            for (var piece = 0; piece < TerrainTransitions.PieceCount; piece++)
            {
                var image = TerrainTransitions.Piece(style, piece, size);
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                        if (image.GetPixel(x, y).A is > 0.3f and < 0.55f) return true;
            }
            return false;
        }
        foreach (var size in new[] { 16, 32 })
        {
            if (!HasFrost(TerrainStyle.Snow, size) || !HasFrost(TerrainStyle.TundraSnow, size))
                throw new InvalidOperationException($"Snow edges must thin into frost at {size}px.");
            if (HasFrost(TerrainStyle.Grass, size) || HasFrost(TerrainStyle.Rock, size))
                throw new InvalidOperationException($"Only snow edges take the frost; other surfaces keep their rim at {size}px.");
        }

        // A small desert: cactus cover on sand in the west half, bare sand in the east.
        const int w = 12, h = 10;
        var elevation = Enumerable.Repeat((byte)100, w * h).ToArray();
        var surface = Enumerable.Repeat((byte)1, w * h).ToArray();
        var vegetation = new byte[w * h];
        for (var y = 0; y < h; y++) for (var x = 0; x < w / 2; x++) vegetation[y * w + x] = 5;
        var desert = ReliefMap(w, h, elevation, new byte[w * h], surface, vegetation, wrap: false);
        if (!desert.IsCactusCoverAt(0, 0) || desert.IsCactusCoverAt(w - 1, 0) || desert.StyleAt(0, 0) != TerrainStyle.DesertBrush)
            throw new InvalidOperationException("Cactus cover on sand must read as desert brush with cacti; bare sand has none.");
        var layer = new WorldTerrainLayer();
        AddChild(layer);
        try
        {
            layer.SetWorld(desert);
            var cover = 0;
            var withCactus = new List<Vector2I>();
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (desert.IsCactusCoverAt(x, y)) cover++;
                    if (layer.CactusAt(x, y) is not null)
                    {
                        if (!desert.IsCactusCoverAt(x, y))
                            throw new InvalidOperationException("Cacti grow only on cactus cover.");
                        withCactus.Add(new Vector2I(x, y));
                    }
                }
            if (withCactus.Count == 0 || withCactus.Count * 2 > cover)
                throw new InvalidOperationException($"Cactus cover must hold a few cacti, not one on every tile: {withCactus.Count} of {cover}.");
            layer.SetRoads([new OwnerWorldPosition(withCactus[0].X, withCactus[0].Y)]);
            if (layer.CactusAt(withCactus[0].X, withCactus[0].Y) is not null)
                throw new InvalidOperationException("A Road clears the cactus from its tile.");

            layer.SetCamera(new Rect2(0, 0, w, h), 32, 0, false);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (layer.CactusSpriteCount != withCactus.Count - 1)
                throw new InvalidOperationException($"Each cactus off the Road must be drawn: {layer.CactusSpriteCount} of {withCactus.Count - 1}.");
            layer.SetCamera(new Rect2(0, 0, w, h), 8, 0, false);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (layer.CactusSpriteCount != 0)
                throw new InvalidOperationException("The overview zoom shows desert brush colour, not cactus sprites.");
        }
        finally
        {
            layer.QueueFree();
        }
    }
}
