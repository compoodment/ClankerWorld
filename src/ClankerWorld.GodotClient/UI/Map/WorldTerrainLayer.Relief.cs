using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The relief pass: mountains, peaks and hills drawn as one landform from the
/// map's elevation by <see cref="ReliefRenderer"/>, over the ground and under
/// Roads. Relief is cached in chunks of <see cref="ReliefChunkTiles"/> square
/// tiles for each atlas size. A chunk is rendered on a worker thread, so the
/// frame never waits for it: chunks nearest the middle of the view first,
/// then the ring just outside it, so panning rarely reaches a chunk that is
/// not ready. Until a chunk is ready it keeps the per-tile mountain tiles and
/// hill overlays, and a chunk that was on screen meanwhile fades in over
/// <see cref="ReliefFadeMilliseconds"/> instead of appearing at once.
/// Heights never change after a world loads, so the cache is only rebuilt
/// when the world changes.
/// </summary>
public partial class WorldTerrainLayer
{
    /// <summary>Width and height of one cached relief chunk, in tiles.</summary>
    public const int ReliefChunkTiles = 16;

    /// <summary>How long a chunk that finished while on screen takes to fade in over the per-tile art.</summary>
    public const int ReliefFadeMilliseconds = 200;

    // Least recently drawn relief textures are dropped beyond this size.
    private const long ReliefTextureBudgetBytes = 96L * 1024 * 1024;
    private const int ReliefChunkLimit = 4096;
    private static readonly int ReliefWorkers = Math.Clamp(System.Environment.ProcessorCount - 1, 1, 4);

    private readonly Dictionary<(int X, int Y, int Size), ReliefChunk> reliefChunks = [];
    private readonly List<ReliefChunk> renderingRelief = [];
    // Textures from a previous world, kept until the next draw replaces the commands that use them.
    private readonly List<ImageTexture> retiredRelief = [];
    private long reliefTextureBytes;
    private long reliefDrawSerial;
    // The chunk the ground pass last asked about, and whether relief covers it.
    private (int X, int Y, int Size)? lastReliefKey;
    private bool lastReliefCovers;
    private ulong reliefDrawAtMs;

    private enum ReliefState { Waiting, Rendering, Ready, Failed }

    private sealed class ReliefChunk((int X, int Y, int Size) key, Rect2I tiles)
    {
        public (int X, int Y, int Size) Key { get; } = key;
        public Rect2I Tiles { get; } = tiles;
        public ReliefState State { get; set; }
        public Task<byte[]?>? Job { get; set; }
        public ImageTexture? Texture { get; set; }
        public long Bytes { get; set; }
        public long LastDrawn { get; set; }
        // Shown on screen before it was ready, so it fades in rather than popping.
        public bool SeenPending { get; set; }
        public ulong ReadyAtMs { get; set; }
    }

    /// <summary>Relief chunks currently held as textures.</summary>
    public int ReliefTextureCount => reliefChunks.Values.Count(chunk => chunk.Texture is not null);

    /// <summary>Visible relief chunks still being rendered or waiting for a worker.</summary>
    public int PendingReliefChunkCount =>
        reliefChunks.Values.Count(chunk => chunk.State is ReliefState.Waiting or ReliefState.Rendering);

    /// <summary>Hill tiles drawn with the per-tile hill overlay in the last detailed draw, because their relief was not ready.</summary>
    public int HillOverlayTileCount { get; private set; }

    /// <summary>Relief chunks still fading in over the per-tile art in the last detailed draw.</summary>
    public int FadingReliefChunkCount { get; private set; }

    /// <summary>Chunks just outside the view whose relief was started ahead of time, since the world loaded.</summary>
    public int PrefetchedReliefChunkCount { get; private set; }

    /// <summary>Relief needs whole tiles side by side; a gapped debug grid keeps the per-tile art.</summary>
    private bool DrawsRelief => tileGap == 0;

    public override void _Ready() => SetProcess(renderingRelief.Count > 0);

    /// <summary>Turns finished relief renders into textures on the main thread and redraws, and keeps redrawing while a chunk fades in.</summary>
    public override void _Process(double delta)
    {
        var finished = FadingReliefChunkCount > 0;
        for (var index = renderingRelief.Count - 1; index >= 0; index--)
        {
            var chunk = renderingRelief[index];
            if (chunk.Job is not { IsCompleted: true } job) continue;
            renderingRelief.RemoveAt(index);
            chunk.Job = null;
            // Redraw even for a chunk of a previous world: its worker is free
            // now, and the next draw starts a chunk that was waiting for one.
            finished = true;
            if (!reliefChunks.TryGetValue(chunk.Key, out var current) || !ReferenceEquals(current, chunk)) continue;
            if (!job.IsCompletedSuccessfully)
            {
                chunk.State = ReliefState.Failed;
                GD.PushWarning($"relief_chunk_failed x={chunk.Tiles.Position.X} y={chunk.Tiles.Position.Y} " +
                    $"size={chunk.Key.Size} error={job.Exception?.GetBaseException().GetType().Name}");
                continue;
            }
            chunk.State = ReliefState.Ready;
            chunk.ReadyAtMs = Time.GetTicksMsec();
            if (job.Result is not { } pixels) continue;
            using var image = Image.CreateFromData(chunk.Tiles.Size.X * chunk.Key.Size, chunk.Tiles.Size.Y * chunk.Key.Size,
                false, Image.Format.Rgba8, pixels);
            chunk.Texture = ImageTexture.CreateFromImage(image);
            chunk.Bytes = pixels.Length;
            reliefTextureBytes += pixels.Length;
        }
        if (finished) QueueRedraw();
        if (renderingRelief.Count == 0 && FadingReliefChunkCount == 0) SetProcess(false);
    }

    /// <summary>Forgets every relief chunk; called when the world changes.</summary>
    private void ResetRelief()
    {
        foreach (var chunk in reliefChunks.Values)
            if (chunk.Texture is { } texture) retiredRelief.Add(texture);
        reliefChunks.Clear();
        reliefTextureBytes = 0;
        lastReliefKey = null;
        PrefetchedReliefChunkCount = 0;
        FadingReliefChunkCount = 0;
    }

    /// <summary>
    /// Starts a draw: frees textures of a previous world, whose draw commands
    /// were cleared before this draw, and forgets the last chunk lookup, since
    /// chunks may have become ready since the last draw.
    /// </summary>
    private void BeginReliefDraw()
    {
        reliefDrawAtMs = Time.GetTicksMsec();
        foreach (var texture in retiredRelief) texture.Dispose();
        retiredRelief.Clear();
        lastReliefKey = null;
        FadingReliefChunkCount = 0;
    }

    /// <summary>
    /// Whether this draw shows relief over the tile, so its per-tile hill
    /// overlay is left out. While a chunk fades in, the overlay stays under it.
    /// </summary>
    private bool ReliefCovers(int mapX, int y, int atlasSize)
    {
        if (!DrawsRelief) return false;
        var key = (mapX / ReliefChunkTiles, y / ReliefChunkTiles, atlasSize);
        if (lastReliefKey != key)
        {
            lastReliefKey = key;
            lastReliefCovers = ShownRelief(key) is { } shown && FadeOf(shown, reliefDrawAtMs) >= 1f;
        }
        return lastReliefCovers;
    }

    /// <summary>How far a ready chunk has faded in, from 0 to 1; one never seen before it was ready shows at once.</summary>
    private static float FadeOf(ReliefChunk chunk, ulong now) =>
        !chunk.SeenPending ? 1f : Math.Clamp((now - chunk.ReadyAtMs) / (float)ReliefFadeMilliseconds, 0f, 1f);

    /// <summary>
    /// The ready relief to show for a chunk: at this atlas size, or else, just
    /// after zooming across the 32 px step, the same landform already drawn
    /// at the other size, scaled, until this size is ready.
    /// </summary>
    private ReliefChunk? ShownRelief((int X, int Y, int Size) key)
    {
        if (reliefChunks.GetValueOrDefault(key) is { State: ReliefState.Ready } chunk) return chunk;
        return reliefChunks.GetValueOrDefault(key with { Size = key.Size == 32 ? 16 : 32 }) is { State: ReliefState.Ready } other
            ? other : null;
    }

    /// <summary>
    /// Draws the relief of every visible chunk that is ready, after the ground
    /// and before Roads, and starts rendering chunks seen for the first time.
    /// A chunk on a wrapping world is drawn wherever its columns show.
    /// </summary>
    private void DrawRelief((int Left, int Top, int Width, int Height) bounds, int stride, int atlasSize)
    {
        if (world is null || !DrawsRelief || bounds.Width <= 0 || bounds.Height <= 0) return;
        reliefDrawSerial++;
        var now = reliefDrawAtMs;
        var fading = 0;
        var waiting = new List<ReliefChunk>();
        var end = bounds.Left + bounds.Width;
        for (var top = bounds.Top / ReliefChunkTiles * ReliefChunkTiles; top < bounds.Top + bounds.Height; top += ReliefChunkTiles)
        {
            var rows = Math.Min(ReliefChunkTiles, world.Height - top);
            for (var x = bounds.Left; x < end;)
            {
                var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                var left = mapX / ReliefChunkTiles * ReliefChunkTiles;
                var columns = Math.Min(ReliefChunkTiles, world.Width - left);
                var chunk = ReliefChunkAt(new Rect2I(left, top, columns, rows), atlasSize);
                if (ShownRelief(chunk.Key) is { } shown)
                {
                    // Marked drawn, so trimming below keeps a texture this draw uses.
                    shown.LastDrawn = reliefDrawSerial;
                    var fade = FadeOf(shown, now);
                    if (fade < 1f) fading++;
                    if (shown.Texture is { } texture)
                        DrawTextureRect(texture, new Rect2((x - (mapX - left)) * stride, top * stride, columns * stride, rows * stride),
                            false, new Color(1, 1, 1, fade));
                }
                else
                {
                    chunk.SeenPending = true;
                    if (chunk.State == ReliefState.Waiting) waiting.Add(chunk);
                }
                x += left + columns - mapX;
            }
        }
        FadingReliefChunkCount = fading;
        if (fading > 0) SetProcess(true);
        var middle = new Vector2(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        StartNearest(waiting, middle);
        // Only once everything on screen has a worker: the ring just outside the view, so a pan finds it ready.
        if (waiting.All(chunk => chunk.State != ReliefState.Waiting)) Prefetch(bounds, atlasSize, middle);
        TrimRelief();
    }

    private ReliefChunk ReliefChunkAt(Rect2I tiles, int atlasSize)
    {
        var key = (tiles.Position.X / ReliefChunkTiles, tiles.Position.Y / ReliefChunkTiles, atlasSize);
        if (!reliefChunks.TryGetValue(key, out var chunk))
        {
            chunk = new ReliefChunk(key, tiles);
            // Most of a map is lowland far from any mountain: no texture, no work.
            if (!ReliefRenderer.HasReliefNear(world!, tiles)) chunk.State = ReliefState.Ready;
            reliefChunks[key] = chunk;
            lastReliefKey = null;
        }
        chunk.LastDrawn = reliefDrawSerial;
        return chunk;
    }

    /// <summary>Starts waiting chunks on free workers, nearest the middle of the view first.</summary>
    private void StartNearest(IEnumerable<ReliefChunk> waiting, Vector2 middle)
    {
        foreach (var chunk in waiting.OrderBy(chunk => ReliefDistanceSquared(chunk, middle)))
        {
            if (renderingRelief.Count >= ReliefWorkers) return;
            if (chunk.State == ReliefState.Waiting) StartRelief(chunk);
        }
    }

    private float ReliefDistanceSquared(ReliefChunk chunk, Vector2 middle)
    {
        var offset = (Vector2)chunk.Tiles.GetCenter() - middle;
        if (wrapsEastWest)
        {
            var across = Math.Abs(offset.X) % world!.Width;
            offset.X = Math.Min(across, world.Width - across);
        }
        return offset.LengthSquared();
    }

    /// <summary>Starts the chunks in a one-chunk ring round the view on any workers left free.</summary>
    private void Prefetch((int Left, int Top, int Width, int Height) bounds, int atlasSize, Vector2 middle)
    {
        if (renderingRelief.Count >= ReliefWorkers) return;
        var ring = new List<ReliefChunk>();
        var top = Math.Max(0, (bounds.Top / ReliefChunkTiles - 1) * ReliefChunkTiles);
        var bottom = Math.Min(world!.Height, bounds.Top + bounds.Height + ReliefChunkTiles);
        var columnCount = (world.Width + ReliefChunkTiles - 1) / ReliefChunkTiles;
        var columns = new HashSet<int>();
        for (var x = bounds.Left; x < bounds.Left + bounds.Width;)
        {
            var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
            var column = mapX / ReliefChunkTiles;
            for (var neighbor = column - 1; neighbor <= column + 1; neighbor++)
                if (wrapsEastWest) columns.Add(Mod(neighbor, columnCount));
                else if (neighbor >= 0 && neighbor < columnCount) columns.Add(neighbor);
            var left = column * ReliefChunkTiles;
            x += left + Math.Min(ReliefChunkTiles, world.Width - left) - mapX;
        }
        for (var row = top; row < bottom; row += ReliefChunkTiles)
            foreach (var column in columns)
            {
                var left = column * ReliefChunkTiles;
                var chunk = ReliefChunkAt(new Rect2I(left, row, Math.Min(ReliefChunkTiles, world.Width - left),
                    Math.Min(ReliefChunkTiles, world.Height - row)), atlasSize);
                if (chunk.State == ReliefState.Waiting && !ring.Contains(chunk)) ring.Add(chunk);
            }
        var started = renderingRelief.Count;
        StartNearest(ring, middle);
        PrefetchedReliefChunkCount += renderingRelief.Count - started;
    }

    /// <summary>
    /// Renders one chunk on a worker thread. Everything the worker shares with
    /// the main thread (the shoreline lookup, the map's derived styles and
    /// hills) is built here first, so the worker reads finished data only and
    /// touches no engine objects.
    /// </summary>
    private void StartRelief(ReliefChunk chunk)
    {
        var map = world!;
        var size = chunk.Key.Size;
        var tiles = chunk.Tiles;
        ReliefRenderer.Prepare(size);
        _ = map.StyleAt(0, 0);
        _ = map.IsHillAt(0, 0);
        chunk.Job = Task.Run(() => ReliefRenderer.RenderPixels(map, tiles, size));
        chunk.State = ReliefState.Rendering;
        renderingRelief.Add(chunk);
        SetProcess(true);
    }

    /// <summary>
    /// Drops the least recently drawn chunks once the cached textures pass
    /// their budget. Chunks drawn this frame, or still rendering, stay; the
    /// others are not used by any current draw command, so their textures can
    /// be freed at once.
    /// </summary>
    private void TrimRelief()
    {
        if (reliefTextureBytes <= ReliefTextureBudgetBytes && reliefChunks.Count <= ReliefChunkLimit) return;
        var stale = reliefChunks.Values
            .Where(chunk => chunk.LastDrawn != reliefDrawSerial && chunk.State != ReliefState.Rendering)
            .OrderBy(chunk => chunk.LastDrawn)
            .ToList();
        foreach (var chunk in stale)
        {
            if (reliefTextureBytes <= ReliefTextureBudgetBytes * 3 / 4 && reliefChunks.Count <= ReliefChunkLimit * 3 / 4) break;
            reliefChunks.Remove(chunk.Key);
            if (chunk.Texture is not { } texture) continue;
            reliefTextureBytes -= chunk.Bytes;
            chunk.Texture = null;
            texture.Dispose();
        }
        lastReliefKey = null;
    }
}
