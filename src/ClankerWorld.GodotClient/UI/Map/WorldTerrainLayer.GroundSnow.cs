using Godot;

namespace ClankerWorld.GodotClient.UI;

public partial class WorldTerrainLayer
{
    // Display-only history of weather actually received. A fresh view does not infer earlier snowfall.
    private readonly Dictionary<Vector2I, float> groundSnowRegions = [];
    private readonly HashSet<Vector2I> previousSnowRegions = [];
    private readonly Dictionary<string, Vector2I> snowWalkers = new(StringComparer.Ordinal);
    private readonly Queue<(Vector2 Position, long Tick)> snowPrints = [];
    private readonly Dictionary<(TerrainStyle Style, int Variant, RoadLinks Links, int RoadVariant, int Size), ImageTexture> snowTextures = [];
    private readonly Dictionary<Vector2I, (ImageTexture Texture, int[] Counts)> snowOverviewTextures = [];
    private readonly HashSet<Vector2I> snowOverviewBlocked = [];
    private readonly HashSet<Vector2I> snowOverviewRoads = [];
    private long? groundSnowTick;
    private int groundSnowRegionSize;
    private float snowTicksPerHour = 60;

    public int SnowFootprintCount => snowPrints.Count;
    public int DrawnSnowTileCount { get; private set; }
    public int DrawnSnowFootprintCount { get; private set; }
    public int SnowOverviewDrawCount { get; private set; }

    public void ResetGroundSnow()
    {
        groundSnowRegions.Clear();
        previousSnowRegions.Clear();
        snowWalkers.Clear();
        snowPrints.Clear();
        snowOverviewTextures.Clear();
        groundSnowTick = null;
        QueueRedraw();
    }

    /// <summary>Accumulate one hour of observed snowfall; melt over two hours after it stops.</summary>
    public void ObserveGroundSnow(long tick, int ticksPerDay, IReadOnlyList<(string Id, Vector2I Tile)> walkers, bool paused)
    {
        if (world is null) return;
        if (tick < groundSnowTick || groundSnowRegionSize != weatherRegionSize) ResetGroundSnow();
        if (!snowOverviewBlocked.SetEquals(buildingTiles.Concat(constructionTiles).Concat(bridgeDecks.Keys)) ||
            !snowOverviewRoads.SetEquals(roadTiles.Concat(marketPlazaTiles)))
        {
            snowOverviewTextures.Clear();
            snowOverviewBlocked.Clear();
            snowOverviewBlocked.UnionWith(buildingTiles.Concat(constructionTiles).Concat(bridgeDecks.Keys));
            snowOverviewRoads.Clear();
            snowOverviewRoads.UnionWith(roadTiles.Concat(marketPlazaTiles));
        }
        groundSnowRegionSize = weatherRegionSize;
        snowTicksPerHour = Math.Max(1, ticksPerDay) / 24f;
        var elapsed = groundSnowTick is { } prior ? (tick - prior) / snowTicksPerHour : 0;
        // Only known snow regions get an entry; dry regions never turn white.
        foreach (var (region, weather) in weatherRegions)
        {
            if (weather != "snow" || region.X < 0 || region.Y < 0 ||
                region.X * (long)weatherRegionSize >= world.Width || region.Y * (long)weatherRegionSize >= world.Height) continue;
            groundSnowRegions.TryAdd(region, 0);
        }
        foreach (var region in groundSnowRegions.Keys.ToArray())
        {
            var cover = groundSnowRegions[region];
            cover = Math.Clamp(cover + (previousSnowRegions.Contains(region) ? elapsed : -elapsed / 2), 0, 1);
            if (cover == 0 && weatherRegions.GetValueOrDefault(region) != "snow") groundSnowRegions.Remove(region);
            else groundSnowRegions[region] = cover;
        }
        previousSnowRegions.Clear();
        foreach (var (region, weather) in weatherRegions)
            if (weather == "snow" && region.X >= 0 && region.Y >= 0 &&
                region.X * (long)weatherRegionSize < world.Width && region.Y * (long)weatherRegionSize < world.Height)
                previousSnowRegions.Add(region);
        while (snowPrints.TryPeek(out var print) && tick - print.Tick >= snowTicksPerHour) snowPrints.Dequeue();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, tile) in walkers)
        {
            present.Add(id);
            if (!paused && elapsed > 0 && snowWalkers.TryGetValue(id, out var from)) AddSnowFootprints(from, tile, tick);
            snowWalkers[id] = tile;
        }
        foreach (var id in snowWalkers.Keys.Where(id => !present.Contains(id)).ToArray()) snowWalkers.Remove(id);
        groundSnowTick = tick;
        if (elapsed > 0) QueueRedraw();
    }

    public float GroundSnowAt(int x, int y)
    {
        if (world is null || y < 0 || y >= world.Height || (!wrapsEastWest && (x < 0 || x >= world.Width))) return 0;
        x = wrapsEastWest ? Mod(x, world.Width) : x;
        if (!CanSnowAt(x, y)) return 0;
        return groundSnowRegions.GetValueOrDefault(new Vector2I(x / weatherRegionSize, y / weatherRegionSize));
    }

    private bool CanSnowAt(int x, int y)
    {
        if (world is null || x < 0 || y < 0 || x >= world.Width || y >= world.Height) return false;
        var tile = new Vector2I(x, y);
        var style = world.StyleAt(x, y);
        if (TerrainTextures.IsWater(style) || style is TerrainStyle.Snow or TerrainStyle.TundraSnow ||
            bridgeDecks.ContainsKey(tile) || buildingTiles.Contains(tile) || constructionTiles.Contains(tile)) return false;
        return true;
    }

    private void AddSnowFootprints(Vector2I from, Vector2I to, long tick)
    {
        if (world is null) return;
        var dx = to.X - from.X;
        if (wrapsEastWest && Math.Abs(dx) > world.Width / 2) dx += dx > 0 ? -world.Width : world.Width;
        var movement = new Vector2(dx, to.Y - from.Y);
        if (Math.Max(Math.Abs(dx), Math.Abs(to.Y - from.Y)) > 3 || movement.LengthSquared() == 0) return;
        var direction = movement.Normalized();
        var across = new Vector2(-direction.Y, direction.X) * (2.5f / 32);
        for (var distance = 0f; distance < movement.Length(); distance += 0.28f)
        {
            var position = new Vector2(from.X + 0.5f, from.Y + 0.6f) + direction * distance +
                across * (((int)(distance / 0.28f) % 2 == 0) ? -1 : 1);
            if (wrapsEastWest) position.X = Mathf.PosMod(position.X, world.Width);
            if (GroundSnowAt(Mathf.FloorToInt(position.X), Mathf.FloorToInt(position.Y)) <= 0) continue;
            snowPrints.Enqueue((position, tick));
            while (snowPrints.Count > 4096) snowPrints.Dequeue();
        }
    }

    private void DrawGroundSnow((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        DrawnSnowTileCount = 0;
        DrawnSnowFootprintCount = 0;
        SnowOverviewDrawCount = 0;
        if (world is null || groundSnowRegions.Count == 0) return;
        if (!DrawsGroundTextures)
        {
            DrawOverviewSnow(bounds, stride);
            return;
        }
        var size = TerrainTextures.AtlasTileSize(tileSize);
        for (var y = bounds.Top; y < bounds.Top + bounds.Height; y++)
            for (var x = bounds.Left; x < bounds.Left + bounds.Width; x++)
            {
                var cover = GroundSnowAt(x, y);
                if (cover <= 0) continue;
                var mapX = wrapsEastWest ? Mod(x, world.Width) : x;
                var tile = new Rect2(x * stride, y * stride, tileSize, tileSize);
                {
                    var links = tileSize >= SpriteTileMinimum ? RoadLinksAt(x, y) : RoadLinks.None;
                    if (links.HasFlag(RoadLinks.Road)) links |= doorsteps.GetValueOrDefault(new Vector2I(mapX, y));
                    var key = (world.StyleAt(mapX, y), TerrainTextures.VariantAt(mapX, y), links,
                        (int)(PixelArt.Hash(mapX, y, 7) % RoadSprites.VariantCount), size);
                    if (!snowTextures.TryGetValue(key, out var texture))
                    {
                        // Keep generation and texture memory bounded as the player pans.
                        if (snowTextures.Count >= 256) snowTextures.Remove(snowTextures.Keys.First());
                        texture = ImageTexture.CreateFromImage(GroundSnowSprites.OverlayTile(key.Item1, key.Item2, key.Item3, key.Item4, size));
                        snowTextures.Add(key, texture);
                    }
                    DrawTextureRect(texture, tile, false, Colors.White with { A = cover });
                }
                DrawnSnowTileCount++;
            }
        if (tileSize < SpriteTileMinimum || groundSnowTick is not { } tick) return;
        foreach (var (position, madeAt) in snowPrints)
        {
            if (GroundSnowAt(Mathf.FloorToInt(position.X), Mathf.FloorToInt(position.Y)) <= 0) continue;
            var alpha = GroundSnowSprites.PrintAlpha((tick - madeAt) / snowTicksPerHour) *
                GroundSnowAt(Mathf.FloorToInt(position.X), Mathf.FloorToInt(position.Y));
            var first = wrapsEastWest ? (int)Math.Floor((bounds.Left - position.X) / world.Width) : 0;
            var last = wrapsEastWest ? (int)Math.Ceiling((bounds.Left + bounds.Width - position.X) / world.Width) : 0;
            for (var copy = first; copy <= last; copy++)
            {
                var placed = position + new Vector2(copy * world.Width, 0);
                if (!new Rect2(bounds.Left, bounds.Top, bounds.Width, bounds.Height).HasPoint(placed)) continue;
                foreach (var (area, color) in GroundSnowSprites.Print((int)(placed.X * stride), (int)(placed.Y * stride), size, alpha))
                    DrawRect(new Rect2(area.Position, new Vector2(area.Size.X, area.Size.Y) * (tileSize / (float)size)), color);
                DrawnSnowFootprintCount++;
            }
        }
    }

    private void DrawOverviewSnow((int Left, int Top, int Width, int Height) bounds, int stride)
    {
        if (world is null) return;
        const int chunkSize = 32, rowSize = chunkSize + 1;
        var visible = new Rect2I(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        foreach (var (region, cover) in groundSnowRegions)
        {
            if (cover <= 0) continue;
            var regionRect = new Rect2I(region * weatherRegionSize, new(weatherRegionSize, weatherRegionSize))
                .Intersection(new Rect2I(0, 0, world.Width, world.Height));
            var firstCopy = wrapsEastWest ? (int)Math.Floor((bounds.Left - regionRect.End.X) / (double)world.Width) : 0;
            var lastCopy = wrapsEastWest ? (int)Math.Ceiling((bounds.Left + bounds.Width - regionRect.Position.X) / (double)world.Width) : 0;
            for (var copy = firstCopy; copy <= lastCopy; copy++)
            {
                var shift = new Vector2I(copy * world.Width, 0);
                var part = visible.Intersection(regionRect with { Position = regionRect.Position + shift });
                if (!part.HasArea()) continue;
                var canonical = part with { Position = part.Position - shift };
                for (var cy = canonical.Position.Y / chunkSize; cy <= (canonical.End.Y - 1) / chunkSize; cy++)
                    for (var cx = canonical.Position.X / chunkSize; cx <= (canonical.End.X - 1) / chunkSize; cx++)
                    {
                        var key = new Vector2I(cx, cy);
                        var origin = key * chunkSize;
                        var area = canonical.Intersection(new Rect2I(origin, new(chunkSize, chunkSize)));
                        if (!snowOverviewTextures.TryGetValue(key, out var cached))
                        {
                            using var image = Image.CreateEmpty(chunkSize, chunkSize, false, Image.Format.Rgba8);
                            var counts = new int[rowSize * rowSize];
                            for (var y = 0; y < chunkSize; y++)
                                for (var x = 0; x < chunkSize; x++)
                                {
                                    var eligible = CanSnowAt(origin.X + x, origin.Y + y);
                                    if (eligible)
                                        image.SetPixel(x, y, GroundSnowSprites.Overlay(world.DisplayColorAt(origin.X + x, origin.Y + y),
                                            IsRoad(origin.X + x, origin.Y + y)));
                                    counts[(y + 1) * rowSize + x + 1] = (eligible ? 1 : 0) +
                                        counts[y * rowSize + x + 1] + counts[(y + 1) * rowSize + x] - counts[y * rowSize + x];
                                }
                            if (snowOverviewTextures.Count >= 256) snowOverviewTextures.Remove(snowOverviewTextures.Keys.First());
                            cached = (ImageTexture.CreateFromImage(image), counts);
                            snowOverviewTextures.Add(key, cached);
                        }
                        var local = area with { Position = area.Position - origin };
                        var count = cached.Counts[local.End.Y * rowSize + local.End.X] -
                            cached.Counts[local.Position.Y * rowSize + local.End.X] -
                            cached.Counts[local.End.Y * rowSize + local.Position.X] +
                            cached.Counts[local.Position.Y * rowSize + local.Position.X];
                        if (count == 0) continue;
                        DrawTextureRectRegion(cached.Texture, new Rect2((area.Position + shift) * stride, area.Size * stride),
                            new Rect2(local.Position, local.Size), Colors.White with { A = cover });
                        DrawnSnowTileCount += count;
                        SnowOverviewDrawCount++;
                    }
            }
        }
    }
}
