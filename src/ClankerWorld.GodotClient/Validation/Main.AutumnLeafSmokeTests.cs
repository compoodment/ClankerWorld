using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyAutumnLeavesAsync()
    {
        var original = renderedMapSnapshot!;
        var authoring = original.Authoring ?? new OwnerWorldAuthoringState(true, 0, 0, 0,
            original.MapManifestDigest, original.MapManifestDigest, "clear", "summer", []);
        try
        {
            var systems = original.WorldSystems ?? new OwnerWorldSystemsSummary("summer", "clear", 0, 0, 0, 0, 0);
            RenderMap(original with { Authoring = null, WorldSystems = systems with { Season = "autumn" } });
            if (!terrainLayer.AutumnLeavesEnabled)
                throw new InvalidOperationException("A world-summary autumn must enable leaves when authoring observations are absent.");
            RenderMap(original with { Authoring = null, WorldSystems = systems with { Season = "winter" } });
            if (terrainLayer.AutumnLeavesEnabled)
                throw new InvalidOperationException("A world-summary winter must remove autumn leaves.");
            RenderMap(original with { Authoring = null, WorldSystems = null });
            if (terrainLayer.AutumnLeavesEnabled)
                throw new InvalidOperationException("An unknown observed season must not retain autumn leaves.");
            RenderMap(original with { Authoring = authoring with { Season = "autumn" }, WorldSystems = systems with { Season = "winter" } });
            if (!terrainLayer.AutumnLeavesEnabled)
                throw new InvalidOperationException("Authoring season must take precedence over the world summary, as it does for landscape colors.");
            RenderMap(original with { Authoring = authoring with { Season = "autumn" } });
            if (!terrainLayer.AutumnLeavesEnabled) throw new InvalidOperationException("The live map must enable leaves from the host's autumn observation.");
            RenderMap(original with { Authoring = authoring with { Season = "spring" } });
            if (terrainLayer.AutumnLeavesEnabled) throw new InvalidOperationException("The next observed spring must remove autumn leaves.");
        }
        finally
        {
            RenderMap(original);
        }

        const int Width = 12;
        OwnerWorldTile[] Tiles(string terrain) => Enumerable.Range(0, Width * Width)
            .Select(index => new OwnerWorldTile(index % Width, index / Width, terrain)).ToArray();
        var map = WorldTerrainMap.FromTiles(Tiles("meadow"), Width, Width);
        var layer = new WorldTerrainLayer();
        AddChild(layer);
        try
        {
            layer.SetWorld(map);
            layer.SetCamera(new Rect2(0, 0, Width, Width), 32, 0, false);
            layer.SetAutumnLeaves("autumn");
            var tree = new OwnerWorldResource("leaf-tree", "construction", new(5, 5), true,
                "available", 1, 1, 1, 6, "spring", "broadleaf", TreeStage: "mature");
            foreach (var (kind, stage, expected) in new[]
                     {
                         ("broadleaf", "mature", true), ("conifer", "mature", false),
                         ("broadleaf", "sapling", false), ("broadleaf", "stump", false),
                         ("orchard", "fruiting", true), ("orchard", "picked", true), ("orchard", "growing", false),
                     })
            {
                layer.SetTrees([tree with { TreeKind = kind, TreeStage = stage }]);
                await DrawLeaves();
                if ((layer.AutumnLeafDrawCount > 0) != expected || layer.AutumnLeafDrawCount > AutumnLeaves.Count)
                    throw new InvalidOperationException($"Autumn leaves must follow mature broadleaf/orchard stages only: {kind}/{stage} drew {layer.AutumnLeafDrawCount}.");
            }
            layer.SetTrees([tree]);
            foreach (var size in new[] { 32, 16 })
            {
                layer.SetCamera(new Rect2(0, 0, Width, Width), size, 0, false);
                await DrawLeaves();
                if (layer.AutumnLeafDrawCount != 14) throw new InvalidOperationException("Open autumn ground must show the approved fourteen sparse leaves at both zooms.");
            }
            foreach (var season in new[] { "winter", "spring", "summer", null })
            {
                layer.SetAutumnLeaves(season);
                await DrawLeaves();
                if (layer.AutumnLeafDrawCount != 0) throw new InvalidOperationException("Leaves must vanish outside autumn, including an unknown season.");
            }
            layer.SetAutumnLeaves("autumn");
            layer.SetRoads(Tiles("meadow").Select(tile => new OwnerWorldPosition(tile.X, tile.Y)).ToArray());
            await DrawLeaves();
            if (layer.AutumnLeafDrawCount != 0) throw new InvalidOperationException("Ground leaves must never paint Roads.");
            layer.SetRoads([]);
            foreach (var water in new[] { "lake", "river", "ocean", "water" })
            {
                layer.SetWorld(WorldTerrainMap.FromTiles(Tiles(water), Width, Width));
                layer.SetTrees([tree]);
                await DrawLeaves();
                if (layer.AutumnLeafDrawCount != 0) throw new InvalidOperationException($"Ground leaves must never paint {water}.");
            }
            layer.SetWorld(map);
            layer.SetTrees([tree]);
            layer.SetCamera(new Rect2(0, 0, 2, 2), 32, 0, false);
            await DrawLeaves();
            if (layer.AutumnLeafDrawCount != 0) throw new InvalidOperationException("Distant leaves must not draw outside the camera.");
            layer.SetCamera(new Rect2(0, 0, Width, Width), 8, 0, false);
            await DrawLeaves();
            if (layer.AutumnLeafDrawCount != 0 || layer.GetChildCount() != 0)
                throw new InvalidOperationException("Overview leaves must not add tiny draw commands or nodes.");
            layer.SetTrees([tree with { Position = new(0, 5) }]);
            layer.SetCamera(new Rect2(-1, 4, 2, 3), 32, 0, true);
            await DrawLeaves();
            if (layer.AutumnLeafDrawCount <= 0 || layer.AutumnLeafDrawCount > 14)
                throw new InvalidOperationException("Leaves must remain visible and bounded at the wrapped seam.");
            const int ForestSize = 192;
            layer.SetWorld(WorldTerrainMap.FromPacked(new OwnerWorldPackedTerrain(ForestSize, ForestSize,
                "terrain-kind-v1", Convert.ToBase64String(Enumerable.Repeat((byte)8, ForestSize * ForestSize).ToArray()))));
            layer.SetTrees(Enumerable.Range(0, ForestSize * ForestSize).Select(index => tree with
            {
                Id = $"forest-tree-{index}",
                Position = new(index % ForestSize, index / ForestSize),
            }).ToArray());
            layer.SetAutumnLeaves("autumn");
            layer.MeasureDrawCost = true;
            foreach (var size in new[] { 32, 16 })
            {
                layer.SetCamera(new Rect2(40, 40, 12, 12), size, 0, false);
                for (var frame = 0; frame < 3; frame++) await DrawLeaves();
                var costs = new List<double>();
                var maximum = 0;
                for (var frame = 0; frame < 10; frame++)
                {
                    await DrawLeaves();
                    costs.Add(layer.LastDrawMilliseconds);
                    maximum = Math.Max(maximum, layer.AutumnLeafDrawCount);
                }
                costs.Sort();
                if (maximum <= 0 || maximum > 14 * 14 * 14 || layer.GetChildCount() != 0)
                    throw new InvalidOperationException($"A large autumn forest must draw only camera-bounded leaves without adding nodes: leaves={maximum}, nodes={layer.GetChildCount()}, size={size}.");
                GD.Print($"NATIVE_AUTUMN_FOREST world=192x192 camera=12x12 tileSize={size} samples=10 maximumLeaves={maximum} medianDrawMs={costs[5]:F3} p95DrawMs={costs[9]:F3}");
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            long checksum = 0;
            for (var index = 0; index < 10_000; index++)
            {
                var leaf = AutumnLeaves.At(index % 100, index / 100, index % 14, 32);
                checksum += leaf.X + leaf.Y;
            }
            if (GC.GetAllocatedBytesForCurrentThread() != before) throw new InvalidOperationException("Leaf geometry must not allocate per leaf.");
            GD.Print($"NATIVE_AUTUMN_LEAVES shape=14 zooms=32,16 stageSeasonRoadWaterCameraWrapControls=passed allocatedBytes=0 checksum={checksum}");
        }
        finally
        {
            layer.QueueFree();
        }

        async Task DrawLeaves()
        {
            layer.QueueRedraw();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
