using System.Reflection;
using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyResourceAppearanceReuse(OwnerWorldSnapshot original)
    {
        var oldZoom = cameraZoom;
        var oldCenter = cameraCenterTiles;
        OwnerWorldResource[] resources =
        [
            new("reuse-tree", "wood", new(1, 1), true, "available", 3, 3, TreeKind: "broadleaf", TreeStage: "sapling"),
            new("reuse-site", "food", new(2, 1), true, "available", 3, 3, NaturalObjectKind: "berry_bush"),
            new("reuse-camp", "construction", new(3, 1), false, "available", 3, 3),
        ];
        var snapshot = original with { WorldId = "resource-reuse-smoke", Resources = resources };
        byte[] Index(string field) => (byte[])typeof(WorldTerrainLayer)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terrainLayer)!;
        (byte[] Trees, byte[] Sites, byte[] Stages) Indexes() => (Index("trees"), Index("naturalObjects"), Index("naturalStages"));
        void Retained((byte[] Trees, byte[] Sites, byte[] Stages) before, string context)
        {
            var after = Indexes();
            if (!ReferenceEquals(before.Trees, after.Trees) || !ReferenceEquals(before.Sites, after.Sites) ||
                !ReferenceEquals(before.Stages, after.Stages))
                throw new InvalidOperationException($"Unchanged resource appearance must retain all three indexes during {context}.");
        }
        NatureSprite CampSprite() => ((Dictionary<int, NatureSprite>)typeof(WorldTerrainLayer)
            .GetField("campResources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terrainLayer)!)[1 * 4 + 3];
        void Show() => RenderMap(snapshot);

        try
        {
            Show();
            var before = Indexes();
            cameraZoom = 1;
            Show();
            for (var i = 0; i < 4; i++)
            {
                ZoomAt(mapCanvas.Size / 2, zoomIn: true);
                ZoomAt(mapCanvas.Size / 2, zoomIn: false);
            }
            Retained(before, "real zoom commands");
            // A new wire observation owns a different list and different record instances.
            snapshot = snapshot with
            {
                Resources = JsonSerializer.Deserialize<OwnerWorldResource[]>(JsonSerializer.Serialize(resources))!,
            };
            Show();
            Retained(before, "a deserialized observation refresh");
            resources[0] = resources[0] with { Quantity = 2, Capacity = 4, Id = "same-art-new-id" };
            snapshot = snapshot with { Resources = resources };
            Show();
            Retained(before, "a positive quantity and nonvisual metadata change");

            // Mutate the same array without changing its count: collection identity is not a cache key.
            foreach (var stage in new[] { "mature", "stump", "sapling" })
            {
                resources[0] = resources[0] with { TreeStage = stage };
                Show();
                if (terrainLayer.TreeStageAt(1, 1) != stage)
                    throw new InvalidOperationException($"A same-count tree change must display {stage} immediately.");
            }
            resources[0] = resources[0] with { TreeStage = null, IsPlanted = false, Quantity = 0 };
            Show();
            if (terrainLayer.TreeStageAt(1, 1) != "stump")
                throw new InvalidOperationException("A legacy tree without an explicit stage must still show felling.");
            resources[0] = resources[0] with { IsPlanted = true, Quantity = 3 };
            Show();
            if (terrainLayer.TreeStageAt(1, 1) != "sapling")
                throw new InvalidOperationException("A legacy planted tree must still show a sapling.");
            resources[0] = resources[0] with { Position = new(0, 1) };
            Show();
            if (terrainLayer.TreeStageAt(1, 1) is not null || terrainLayer.TreeStageAt(0, 1) != "sapling")
                throw new InvalidOperationException("Moving a planted tree must clear its old tile and index its new tile.");

            foreach (var (quantity, state, renewable, expected) in new[]
            {
                (0, "available", true, "regrowing"), (3, "depleted", false, "depleted"),
                (3, "available", true, "available"),
            })
            {
                resources[1] = resources[1] with { Quantity = quantity, State = state, IsRenewable = renewable };
                Show();
                if (terrainLayer.NaturalObjectStageAt(2, 1) != expected)
                    throw new InvalidOperationException($"A same-count site change must display {expected} immediately.");
            }
            resources[1] = resources[1] with { NaturalObjectKind = "stone_outcrop", Position = new(2, 2) };
            Show();
            if (terrainLayer.NaturalObjectNameAt(2, 1) is not null || terrainLayer.NaturalObjectNameAt(2, 2) != "Stone outcrop")
                throw new InvalidOperationException("Changing a site's type and location must replace its indexed appearance.");

            before = Indexes();
            resources[2] = resources[2] with { Quantity = 0 };
            Show();
            Retained(before, "a camp-only depletion");
            if (CampSprite() != NatureSprite.Depleted)
                throw new InvalidOperationException("A camp-only depletion must still refresh its sprite.");
            resources[2] = resources[2] with { Kind = "food", Quantity = 3, IsRenewable = true };
            Show();
            Retained(before, "a camp-only kind and regeneration change");
            if (CampSprite() != NatureSprite.BerryBush)
                throw new InvalidOperationException("A camp-only kind and regeneration change must refresh its sprite.");
            snapshot = snapshot with { Resources = resources[..2] };
            Show();
            if (terrainLayer.CampResourceSpriteCount != 0)
                throw new InvalidOperationException("Removing a camp resource must remove its retained sprite.");

            // An unchanged natural-site key must still reject a newly overlapping tree.
            resources[0] = resources[0] with { Position = resources[1].Position, TreeStage = "mature" };
            terrainLayer.SetTrees(resources);
            try
            {
                terrainLayer.SetNaturalObjects(resources);
                throw new InvalidOperationException("A retained natural index must reject a new tree overlap.");
            }
            catch (InvalidDataException) { }
            resources[0] = resources[0] with { Position = new(0, 1) };
            snapshot = snapshot with { WorldId = "resource-reuse-reset", Resources = resources };
            Show();
            if (terrainLayer.TreeStageAt(0, 1) != "mature" || terrainLayer.CampResourceSpriteCount != 1)
                throw new InvalidOperationException("A world reset must repopulate resources even when their appearance keys match.");
            before = Indexes();
            snapshot = snapshot with
            {
                MapManifestDigest = "resource-reuse-resized",
                PackedTerrain = new(5, 4, "terrain-kind-v1", Convert.ToBase64String(new byte[20])),
            };
            Show();
            if (ReferenceEquals(before.Trees, Index("trees")) || Index("trees").Length != 20 ||
                terrainLayer.TreeStageAt(0, 1) != "mature" || terrainLayer.NaturalObjectNameAt(2, 2) != "Stone outcrop")
                throw new InvalidOperationException("A resized map must rebuild correctly dimensioned resource indexes.");
            snapshot = snapshot with { Resources = [] };
            Show();
            if (terrainLayer.TreeStageAt(0, 1) is not null || terrainLayer.NaturalObjectNameAt(2, 2) is not null ||
                terrainLayer.CampResourceSpriteCount != 0)
                throw new InvalidOperationException("Removing resources must clear all retained appearance.");
            GD.Print("Resource appearance reuse checks passed.");
        }
        finally
        {
            cameraZoom = oldZoom;
            cameraCenterTiles = oldCenter;
            RenderMap(original);
        }
    }
}
