using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyOrchardSaplingAppearance(OwnerWorldSnapshot sample)
    {
        var original = renderedMapSnapshot;
        var originalZoom = cameraZoom;
        var originalCenter = cameraCenterTiles;
        var orchard = new OwnerWorldResource("planted-orchard", "fruit", new(1, 1), true,
            "depleted", 0, 1, 1, 3, "autumn", "orchard", true, TreeStage: "sapling");
        var wood = new OwnerWorldResource("wood-sapling", "construction", new(0, 0), true,
            "depleted", 0, 1, 1, 6, "spring", "broadleaf", true, TreeStage: "sapling");
        var conifer = wood with { Id = "conifer-sapling", TreeKind = "conifer", Position = new(3, 3) };
        var map = sample with { Resources = [orchard, wood, conifer], PlacedBuildings = [], Inhabitants = [] };
        try
        {
            foreach (var stage in new[] { "sapling", "growing", "fruiting", "picked", "sapling" })
            {
                var changed = orchard with
                {
                    TreeStage = stage,
                    IsPlanted = stage == "sapling",
                    Quantity = stage == "fruiting" ? 1 : 0,
                    State = stage == "fruiting" ? "available" : "depleted",
                };
                var packet = map with { Resources = [changed, wood, conifer] };
                for (var refresh = 0; refresh < 2; refresh++)
                {
                    // Each wire refresh has fresh records and lists, as after reconnect.
                    RenderMap(JsonSerializer.Deserialize<OwnerWorldSnapshot>(
                        JsonSerializer.Serialize(packet, CompatibilitySmokeJsonOptions), CompatibilitySmokeJsonOptions)!);
                    if (terrainLayer.TreeStageAt(1, 1) != stage || terrainLayer.TreeStageAt(0, 0) != "sapling" ||
                        terrainLayer.TreeStageAt(3, 3) != "sapling" || mapObjectVisuals.ContainsKey("resource:" + orchard.Id))
                        throw new InvalidOperationException($"Orchard {stage} and wood saplings must remain visible on their own tiles after wire refresh.");
                }
                var art = TreeArtManifest.For("orchard", stage);
                if (art is not { Code: > 0, Review: TreeArtManifest.Approved } || NatureSprites.ForTree(art.Code) != art.Sprite ||
                    TreeArtManifest.ForCode(art.Code)?.Stage != stage)
                    throw new InvalidOperationException($"Orchard {stage} needs approved drawable art and its own stage code.");
            }
            var sapling = TreeArtManifest.For("orchard", "sapling")!;
            var growing = TreeArtManifest.For("orchard", "growing")!;
            foreach (var size in new[] { 16, 32 })
            {
                var pixels = NatureSprites.Sprite(sapling.Sprite!.Value, size);
                if (sapling.Code == growing.Code || sapling.AssetId != growing.AssetId ||
                    !pixels.GetData().AsSpan().SequenceEqual(NatureSprites.Sprite(growing.Sprite!.Value, size).GetData()) ||
                    !pixels.GetData().Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0))
                    throw new InvalidOperationException("Orchard saplings must reuse the visible approved growing art at both atlas sizes while preserving their own stage.");
            }
            cameraCenterTiles = new Vector2(2, 2);
            cameraZoom = 1;
            UpdateMapGeometry(map);
            cameraZoom = 32f / currentTileSize;
            UpdateMapGeometry(map);
            ZoomAt(mapCanvas.Size / 2, zoomIn: true);
            ZoomAt(mapCanvas.Size / 2, zoomIn: false);
            if (terrainLayer.TreeStageAt(1, 1) != "sapling")
                throw new InvalidOperationException("Actual zoom commands must keep the planted orchard visible.");
            try
            {
                terrainLayer.SetTrees([orchard, orchard with { Id = "duplicate-orchard" }]);
                throw new InvalidOperationException("Two visible orchard saplings must still be rejected on one tile.");
            }
            catch (InvalidDataException) { }
            RenderMap(map with { WorldId = "orchard-empty-world", Resources = [] });
            if (terrainLayer.TreeStageAt(1, 1) is not null)
                throw new InvalidOperationException("Changing worlds must remove the old orchard.");
            RenderMap(map);
            if (terrainLayer.TreeStageAt(1, 1) != "sapling")
                throw new InvalidOperationException("Returning to the orchard world must restore the visible sapling.");
        }
        finally
        {
            if (original is not null) RenderMap(original);
            cameraZoom = originalZoom;
            cameraCenterTiles = originalCenter;
            if (original is not null) UpdateMapGeometry(original);
        }
    }
}
