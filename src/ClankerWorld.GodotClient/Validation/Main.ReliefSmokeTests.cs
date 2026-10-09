using System.Security.Cryptography;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    // SHA-256 of the approved relief proposal (tools/ArtPreview/Proposed/Relief.cs)
    // for the preview's reference Town corner, at 32 and 16 px per tile.
    private const string ReliefTownCorner32Digest = "1F40A5D22376601AC406907A52121B0FE9491217200D22C8319F4865D9001821";
    private const string ReliefTownCorner16Digest = "5A67789040A8922CDB18EECFA99838C7756883DFFBA45D919F78F84E16AECF54";

    /// <summary>
    /// Mountains, peaks and hills are one relief layer drawn from elevation in
    /// chunks: chunks rendered apart join exactly, also across the east–west
    /// seam; Mountain and Peak tiles are fully covered; the drawing is
    /// deterministic and matches the approved art byte for byte; a map
    /// without mountains needs no relief; and the terrain layer keeps the
    /// per-tile hill art for a chunk until its relief is ready, then draws
    /// the relief instead.
    /// </summary>
    private async Task VerifyMountainReliefAsync()
    {
        var town = ReliefTownCorner();
        foreach (var (size, digest) in new[] { (32, ReliefTownCorner32Digest), (16, ReliefTownCorner16Digest) })
        {
            var pixels = ReliefRenderer.RenderPixels(town, new Rect2I(0, 0, town.Width, town.Height), size)
                ?? throw new InvalidOperationException("The reference Town corner must have relief around its mountain.");
            if (Convert.ToHexString(SHA256.HashData(pixels)) != digest)
                throw new InvalidOperationException($"Relief at {size} px must match the approved art pixel for pixel.");
        }

        foreach (var wrap in new[] { false, true })
        {
            var range = ReliefRange(wrap, flat: false);
            foreach (var size in new[] { 32, 16 })
            {
                var width = range.Width * size;
                var whole = ReliefRenderer.RenderPixels(range, new Rect2I(0, 0, range.Width, range.Height), size)
                    ?? throw new InvalidOperationException("A mountain range must have relief.");
                if (!whole.AsSpan().SequenceEqual(ReliefRenderer.RenderPixels(range, new Rect2I(0, 0, range.Width, range.Height), size)))
                    throw new InvalidOperationException($"Relief at {size} px must be the same every time it is drawn.");

                // Chunks as the terrain layer caches them, including the narrower last column.
                for (var top = 0; top < range.Height; top += WorldTerrainLayer.ReliefChunkTiles)
                    for (var left = 0; left < range.Width; left += WorldTerrainLayer.ReliefChunkTiles)
                    {
                        var tiles = new Rect2I(left, top, Math.Min(WorldTerrainLayer.ReliefChunkTiles, range.Width - left),
                            Math.Min(WorldTerrainLayer.ReliefChunkTiles, range.Height - top));
                        var chunk = ReliefRenderer.RenderPixels(range, tiles, size);
                        if (!ReliefRegionMatches(whole, width, chunk, tiles.Size.X * size, left * size, top * size, tiles.Size.Y * size))
                            throw new InvalidOperationException($"Relief chunk at ({left}, {top}) and {size} px must join its neighbours exactly.");
                    }

                // A chunk reaching past the east edge continues into the west edge.
                if (wrap)
                {
                    var across = ReliefRenderer.RenderPixels(range, new Rect2I(range.Width - 8, 0, 16, range.Height), size)!;
                    var acrossWidth = 16 * size;
                    for (var y = 0; y < range.Height * size; y++)
                        if (!across.AsSpan(y * acrossWidth * 4, 8 * size * 4).SequenceEqual(whole.AsSpan((y * width + width - 8 * size) * 4, 8 * size * 4)) ||
                            !across.AsSpan((y * acrossWidth + 8 * size) * 4, 8 * size * 4).SequenceEqual(whole.AsSpan(y * width * 4, 8 * size * 4)))
                            throw new InvalidOperationException($"Relief at {size} px must join across the east-west seam.");
                }

                var massifPixels = 0;
                for (var y = 0; y < range.Height * size; y++)
                    for (var x = 0; x < width; x++)
                    {
                        if (range.StyleAt(x / size, y / size) is not (TerrainStyle.Mountain or TerrainStyle.Peak)) continue;
                        massifPixels++;
                        if (whole[(y * width + x) * 4 + 3] != 255)
                            throw new InvalidOperationException($"Relief must cover Mountain and Peak tiles completely at {size} px.");
                    }
                if (massifPixels == 0) throw new InvalidOperationException("The relief test range needs Mountain and Peak tiles.");
            }
        }

        var lowland = ReliefRange(wrap: true, flat: true);
        for (var top = 0; top < lowland.Height; top += WorldTerrainLayer.ReliefChunkTiles)
            for (var left = 0; left < lowland.Width; left += WorldTerrainLayer.ReliefChunkTiles)
            {
                var tiles = new Rect2I(left, top, Math.Min(WorldTerrainLayer.ReliefChunkTiles, lowland.Width - left),
                    Math.Min(WorldTerrainLayer.ReliefChunkTiles, lowland.Height - top));
                if (ReliefRenderer.HasReliefNear(lowland, tiles) || ReliefRenderer.RenderPixels(lowland, tiles, 32) is not null)
                    throw new InvalidOperationException("Lowland without mountains or hills must need no relief.");
            }

        // The terrain layer: per-tile hills while chunks render off the main
        // thread, then one texture per chunk with relief and no hill overlays,
        // faded in, and the ring round a smaller view drawn ahead of time.
        var layer = new WorldTerrainLayer();
        AddChild(layer);
        try
        {
            var world = ReliefRange(wrap: true, flat: false);
            var expected = 0;
            for (var top = 0; top < world.Height; top += WorldTerrainLayer.ReliefChunkTiles)
                for (var left = 0; left < world.Width; left += WorldTerrainLayer.ReliefChunkTiles)
                    if (ReliefRenderer.HasReliefNear(world, new Rect2I(left, top, Math.Min(WorldTerrainLayer.ReliefChunkTiles, world.Width - left),
                            Math.Min(WorldTerrainLayer.ReliefChunkTiles, world.Height - top))))
                        expected++;
            layer.SetWorld(world);
            layer.SetCamera(new Rect2(0, 0, world.Width, world.Height), 32, 0, true);
            await WaitForRelief(layer, () => layer.HillOverlayTileCount > 0, "draw the map");
            if (layer.PendingReliefChunkCount == 0 || layer.ReliefTextureCount == expected)
                throw new InvalidOperationException("Relief must render off the main thread, with per-tile hills shown meanwhile.");
            // A chunk that finishes while on screen fades in over the per-tile art instead of appearing at once.
            await WaitForRelief(layer, () => layer.FadingReliefChunkCount > 0, "start fading");
            var sawFade = layer.FadingReliefChunkCount > 0;
            layer.SetCamera(new Rect2(0, 0, world.Width, world.Height), 8, 0, true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (layer.FadingReliefChunkCount != 0)
                throw new InvalidOperationException("Switching to overview during a fade must stop the detailed fade redraws.");
            layer.SetCamera(new Rect2(0, 0, world.Width, world.Height), 32, 0, true);
            await WaitForRelief(layer, () =>
            {
                sawFade |= layer.FadingReliefChunkCount > 0;
                return layer.PendingReliefChunkCount == 0 && layer.HillOverlayTileCount == 0 && layer.FadingReliefChunkCount == 0;
            }, "finish rendering");
            if (expected == 0 || layer.ReliefTextureCount != expected)
                throw new InvalidOperationException($"Each chunk near mountains needs one relief texture: {layer.ReliefTextureCount} of {expected}.");
            if (!sawFade)
                throw new InvalidOperationException("Relief that finishes while on screen must fade in over the per-tile art.");

            // A small view also draws the ring of chunks just outside it, so panning finds them ready.
            foreach (var wrap in new[] { true, false })
            {
                var viewWorld = ReliefRange(wrap, flat: false);
                layer.SetWorld(viewWorld);
                layer.SetCamera(new Rect2(0, 0, 12, 10), 32, 0, wrap);
                int RingWithRelief(params (int Left, int Top)[] chunks) => chunks.Count(chunk => ReliefRenderer.HasReliefNear(viewWorld,
                    new Rect2I(chunk.Left, chunk.Top, Math.Min(WorldTerrainLayer.ReliefChunkTiles, viewWorld.Width - chunk.Left),
                        Math.Min(WorldTerrainLayer.ReliefChunkTiles, viewWorld.Height - chunk.Top))));
                var visible = RingWithRelief((0, 0));
                // The west neighbor on the 40-column wrapped map is its partial last chunk.
                var withRing = wrap
                    ? RingWithRelief((32, 0), (0, 0), (16, 0), (32, 16), (0, 16), (16, 16))
                    : RingWithRelief((0, 0), (16, 0), (0, 16), (16, 16));
                await WaitForRelief(layer, () => layer.PendingReliefChunkCount == 0 && layer.PrefetchedReliefChunkCount > 0, "draw ahead of the view");
                if (withRing <= visible || layer.ReliefTextureCount != withRing || layer.PrefetchedReliefChunkCount != withRing - visible)
                    throw new InvalidOperationException($"The ring round a small view must be drawn ahead of time, and nothing farther: " +
                        $"wrap={wrap}, textures={layer.ReliefTextureCount}, expected={withRing}, prefetched={layer.PrefetchedReliefChunkCount}.");
            }

            // The overview zoom keeps its one-pixel-per-tile colours.
            layer.SetWorld(world);
            layer.SetCamera(new Rect2(0, 0, world.Width, world.Height), 8, 0, true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (layer.PendingReliefChunkCount != 0 || layer.ReliefTextureCount != 0)
                throw new InvalidOperationException("The overview zoom must not draw relief.");

            layer.SetWorld(lowland);
            layer.SetCamera(new Rect2(0, 0, lowland.Width, lowland.Height), 32, 0, true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (layer.PendingReliefChunkCount != 0 || layer.ReliefTextureCount != 0 || layer.HillOverlayTileCount != 0)
                throw new InvalidOperationException("A world without mountains must create no relief textures.");
        }
        finally
        {
            layer.QueueFree();
        }
    }

    private async Task WaitForRelief(WorldTerrainLayer layer, Func<bool> done, string step)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60))
                throw new InvalidOperationException($"The relief layer did not {step} in time: pending={layer.PendingReliefChunkCount}, textures={layer.ReliefTextureCount}.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Whether a chunk's pixels equal the same region of a whole render; a chunk with no relief must cover a clear region.</summary>
    private static bool ReliefRegionMatches(byte[] whole, int wholeWidth, byte[]? chunk, int chunkWidth, int left, int top, int rows)
    {
        for (var y = 0; y < rows; y++)
        {
            var region = whole.AsSpan(((top + y) * wholeWidth + left) * 4, chunkWidth * 4);
            if (chunk is null ? region.ContainsAnyExcept((byte)0) : !region.SequenceEqual(chunk.AsSpan(y * chunkWidth * 4, chunkWidth * 4)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// A 40 × 26-tile test range: a ridge with peaks along its spine that
    /// crosses the east–west seam, a river through it, a lake, forest on high
    /// slopes and bare rock at its foot. Flat, it is the same map as plain lowland.
    /// </summary>
    private static WorldTerrainMap ReliefRange(bool wrap, bool flat)
    {
        const int w = 40, h = 26;
        var elevation = new byte[w * h];
        var hydrology = new byte[w * h];
        var surface = new byte[w * h];
        var vegetation = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var spine = 9 + Math.Abs(x % 20 - 10) * 3 / 10;
                var across = Math.Abs(y - spine);
                var i = y * w + x;
                elevation[i] = flat ? (byte)120 : (byte)Math.Clamp(255 - 12 * across + (int)(PixelArt.Hash(x, y, 5) % 9) - 4, 150, 255);
                if (x is 30 or 31) hydrology[i] = 3;
                else if (x is >= 2 and <= 5 && y is >= 19 and <= 23) hydrology[i] = 2;
                else if (x is >= 10 and <= 15 && across >= 2) vegetation[i] = 2;
                else if (across == 4 && x % 7 == 0) surface[i] = 2;
            }
        return ReliefMap(w, h, elevation, hydrology, surface, vegetation, wrap);
    }

    /// <summary>The art preview's reference Town corner (tools/ArtPreview/Scene.cs): a small mountain in the north-east with hills, a river and a lake.</summary>
    private static WorldTerrainMap ReliefTownCorner()
    {
        const int w = 20, h = 12;
        var hydrology = new byte[w * h];
        var surface = new byte[w * h];
        var vegetation = new byte[w * h];
        var elevation = new byte[w * h];
        Array.Fill(elevation, (byte)100);
        for (var y = 0; y < h; y++) { hydrology[y * w + 15] = 3; hydrology[y * w + 16] = 3; }
        for (var y = 9; y < h; y++) for (var x = 17; x < w; x++) hydrology[y * w + x] = 2;
        elevation[19] = 250;
        elevation[18] = 225; elevation[w + 18] = 225; elevation[w + 19] = 225;
        foreach (var (x, y) in new[] { (17, 0), (17, 1), (17, 2), (17, 3), (18, 2), (19, 2), (18, 3), (19, 3), (17, 4), (18, 4) })
            elevation[y * w + x] = 195;
        surface[17] = 2; surface[w + 17] = 2; surface[2 * w + 18] = 2;
        for (var y = 0; y < 3; y++) for (var x = 0; x < 4; x++) { surface[y * w + x] = 5; vegetation[y * w + x] = 2; }
        foreach (var (x, y) in new[] { (4, 0), (5, 0), (6, 0), (4, 1), (5, 1), (4, 2), (0, 3), (1, 3), (2, 3), (3, 3), (0, 4) })
            vegetation[y * w + x] = 2;
        for (var y = 9; y < h; y++) for (var x = 0; x < 4; x++) surface[y * w + x] = 7;
        return ReliefMap(w, h, elevation, hydrology, surface, vegetation, wrap: false);
    }

    private static WorldTerrainMap ReliefMap(int w, int h, byte[] elevation, byte[] hydrology, byte[] surface, byte[] vegetation, bool wrap) =>
        WorldTerrainMap.FromPacked(new OwnerWorldPackedTerrain(w, h, "terrain-kind-v1", Convert.ToBase64String(new byte[w * h])),
            new OwnerWorldPackedMapLayers(w, h, "map-layers-v1", Convert.ToBase64String(Enumerable.Repeat((byte)2, w * h).ToArray()),
                Convert.ToBase64String(elevation), Convert.ToBase64String(hydrology), Convert.ToBase64String(surface),
                Convert.ToBase64String(vegetation)), wrap);
}
