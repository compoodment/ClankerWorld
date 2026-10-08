using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyTerrainCacheRefreshAsync()
    {
        var original = renderedMapSnapshot;
        var originalCenter = cameraCenterTiles;
        var originalZoom = cameraZoom;
        try
        {
            var range = ReliefRange(wrap: true, flat: false);
            var count = range.Width * range.Height;
            string Layer(Func<int, int, byte?> read) => Convert.ToBase64String(Enumerable.Range(0, count)
                .Select(index => read(index % range.Width, index / range.Width)!.Value).ToArray());
            var before = new OwnerWorldSnapshot("terrain-cache-smoke", 0, "before-planting", [], [], [], null, 0)
            {
                WrapsEastWest = true,
                PackedTerrain = new(range.Width, range.Height, "terrain-kind-v1", Convert.ToBase64String(new byte[count])),
                PackedMapLayers = new(range.Width, range.Height, "map-layers-v1", Layer(range.ClimateAt), Layer(range.ElevationAt),
                    Layer(range.HydrologyAt), Layer(range.SurfaceAt), Layer(range.VegetationAt)),
                MapLayersDigest = "unchanged-layers",
            };
            RenderMap(before);
            cameraCenterTiles = new Vector2(range.Width / 2f, range.Height / 2f);
            cameraZoom = 1;
            UpdateMapGeometry(before);
            await WaitForRelief(terrainLayer, () => terrainLayer.ReliefTextureCount > 0 &&
                terrainLayer.PendingReliefChunkCount == 0 && terrainLayer.HillOverlayTileCount == 0, "warm the terrain refresh fixture");
            var cachedMap = terrainMap ?? throw new InvalidOperationException("The terrain refresh fixture needs decoded terrain.");
            var cachedRelief = terrainLayer.ReliefTextureCount;
            var pixelRegion = new Rect2I(0, 0, range.Width, range.Height);
            var cachedPixels = ReliefRenderer.RenderPixels(cachedMap, pixelRegion, 32)!;
            var cachedCenter = cameraCenterTiles;
            var cachedZoom = cameraZoom;
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var refreshed = System.Text.Json.JsonSerializer.Deserialize<OwnerWorldSnapshot>(
                System.Text.Json.JsonSerializer.Serialize(before, options), options)!;
            RenderMap(refreshed);
            AssertCached("an identical wire refresh");
            var tree = new OwnerWorldResource("planted-orchard", "food", new(2, 2), false, "available", 4, 4, 0, 0, "spring")
            {
                TreeKind = "orchard",
                TreeStage = "fruiting",
            };
            var planted = refreshed with { WorldTick = 1, MapManifestDigest = "after-planting", Resources = [tree] };
            RenderMap(planted);
            AssertCached("a resource-only planting refresh");
            if (terrainLayer.TreeStageAt(2, 2) != "fruiting")
                throw new InvalidOperationException("A retained terrain cache must still show the newly observed tree.");
            var changedStock = planted with { Resources = [tree with { Quantity = 0, TreeStage = "picked" }] };
            RenderMap(changedStock);
            AssertCached("a tree stage and quantity update");
            if (terrainLayer.TreeStageAt(2, 2) != "picked" || renderedMapSnapshot!.Resources.Single().Quantity != 0)
                throw new InvalidOperationException("A retained terrain cache must still update tree stages and quantities.");
            RenderMap(changedStock with { Resources = [], MapLayersDigest = "metadata-only-layer-digest" });
            AssertCached("resource removal and unchanged layer contents");
            if (terrainLayer.TreeStageAt(2, 2) is not null ||
                !cachedPixels.AsSpan().SequenceEqual(ReliefRenderer.RenderPixels(terrainMap!, pixelRegion, 32)))
                throw new InvalidOperationException("Resource refreshes must remove trees and preserve the exact relief pixels.");

            var changedTerrain = Convert.FromBase64String(before.PackedTerrain!.Data);
            changedTerrain[0] = 1;
            VerifyRebuilt(before with { PackedTerrain = before.PackedTerrain with { Data = Convert.ToBase64String(changedTerrain) } });
            if (terrainMap!.At(0, 0) != 2)
                throw new InvalidOperationException("Changed packed ground must be decoded even when manifests are unchanged.");
            var changedElevation = Convert.FromBase64String(before.PackedMapLayers!.Elevation);
            changedElevation[0] ^= 1;
            VerifyRebuilt(before with { PackedMapLayers = before.PackedMapLayers with { Elevation = Convert.ToBase64String(changedElevation) } });
            if (terrainMap!.ElevationAt(0, 0) != changedElevation[0])
                throw new InvalidOperationException("Changed layer bytes must be decoded even when their digest is unchanged.");
            VerifyRebuilt(before);
            VerifyRebuilt(before with { WrapsEastWest = false });
            if (terrainMap!.WrapsEastWest)
                throw new InvalidOperationException("Changed wrapping must update the decoded map.");
            VerifyRebuilt(before);
            VerifyRebuilt(before with
            {
                PackedTerrain = before.PackedTerrain with { Width = range.Height, Height = range.Width },
                PackedMapLayers = before.PackedMapLayers with { Width = range.Height, Height = range.Width },
            });
            if (terrainMap!.Width != range.Height || terrainMap.Height != range.Width)
                throw new InvalidOperationException("Changed dimensions must rebuild the map even with identical encoded bytes.");
            VerifyRebuilt(before);
            VerifyRebuilt(before with { WorldId = "another-terrain-cache-world" });

            var tiles = Enumerable.Range(0, count).Select(index => new OwnerWorldTile(index % range.Width, index / range.Width, "meadow")).ToArray();
            var legacy = before with { PackedTerrain = null, PackedMapLayers = null, Tiles = tiles, MapLayersDigest = null };
            VerifyRebuilt(legacy);
            var legacyMap = terrainMap;
            RenderMap(System.Text.Json.JsonSerializer.Deserialize<OwnerWorldSnapshot>(
                System.Text.Json.JsonSerializer.Serialize(legacy, options), options)!);
            if (!ReferenceEquals(legacyMap, terrainMap) || terrainMap!.HasMapLayers)
                throw new InvalidOperationException("Identical legacy tile refreshes must retain their decoded map without inventing layers.");
            // Protect the cache key even if a caller reuses and changes its legacy tile array.
            tiles[0] = tiles[0] with { Terrain = "mountain" };
            VerifyRebuilt(legacy);
            if (terrainMap!.At(0, 0) != 3)
                throw new InvalidOperationException("Changed legacy ground must invalidate the cached tile copy.");
            var complete = legacy with { PackedMapLayers = before.PackedMapLayers, MapLayersDigest = before.MapLayersDigest };
            VerifyRebuilt(complete);
            if (!terrainMap!.HasMapLayers || terrainMap.ElevationAt(0, 0) != range.ElevationAt(0, 0))
                throw new InvalidOperationException("Previously absent layers must become available on the same world and manifest.");
            var beforeReconnect = terrainMap;
            ResetDisplayedWorldContext();
            RenderMap(complete);
            if (ReferenceEquals(beforeReconnect, terrainMap) || terrainLayer.World != terrainMap)
                throw new InvalidOperationException("A reset world context must rebuild and display the accepted terrain.");

            void AssertCached(string step)
            {
                if (!ReferenceEquals(cachedMap, terrainMap) || terrainLayer.ReliefTextureCount != cachedRelief ||
                    terrainLayer.PendingReliefChunkCount != 0 || cameraCenterTiles != cachedCenter || cameraZoom != cachedZoom)
                    throw new InvalidOperationException($"Terrain and warm relief must survive {step}: sameMap={ReferenceEquals(cachedMap, terrainMap)}, " +
                        $"relief={terrainLayer.ReliefTextureCount}/{cachedRelief}, pending={terrainLayer.PendingReliefChunkCount}.");
            }

            void VerifyRebuilt(OwnerWorldSnapshot snapshot)
            {
                var previousMap = terrainMap;
                RenderMap(snapshot);
                if (ReferenceEquals(previousMap, terrainMap) || terrainLayer.World != terrainMap || terrainLayer.ReliefTextureCount != 0)
                    throw new InvalidOperationException("Changed terrain inputs must rebuild decoded terrain and invalidate old relief immediately.");
            }
        }
        finally
        {
            if (original is not null) RenderMap(original);
            cameraCenterTiles = originalCenter;
            cameraZoom = originalZoom;
            if (original is not null) UpdateMapGeometry(original);
        }
    }
}
