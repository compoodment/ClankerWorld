using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class NativeOrchardObservationTests
{
    [Fact]
    public async Task PlantingUpdatesResourcesWithoutChangingTerrainInputs()
    {
        var (state, actor, target, _) = await ObserveEmptyTile();
        using var world = Restore(state, actor);
        var store = new OwnerWorldObservationStore(world);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var beforeJson = JsonSerializer.Serialize(store.GetSnapshot(), options);
        var before = JsonSerializer.Deserialize<OwnerWorldSnapshot>(beforeJson, options)!;
        var result = world.PlantTree(actor, TreeGrowthRules.Orchard, "orchard-observation-seed", target);
        Assert.True(result.Planted, result.Message);
        var afterJson = JsonSerializer.Serialize(store.GetSnapshot(), options);
        var after = JsonSerializer.Deserialize<OwnerWorldSnapshot>(afterJson, options)!;
        Assert.NotNull(before.PackedTerrain);
        Assert.NotNull(before.PackedMapLayers);
        Assert.Equal(before.WorldId, after.WorldId);
        Assert.Equal(before.WrapsEastWest, after.WrapsEastWest);
        Assert.Equal(before.PackedTerrain, after.PackedTerrain);
        Assert.Equal(before.PackedMapLayers, after.PackedMapLayers);
        Assert.Equal(before.MapLayersDigest, after.MapLayersDigest);
        Assert.NotEqual(before.Authoring?.CurrentMapManifestDigest ?? before.MapManifestDigest,
            after.Authoring?.CurrentMapManifestDigest ?? after.MapManifestDigest);
        Assert.Equal(before.Resources.Count + 1, after.Resources.Count);
        Assert.Contains(after.Resources, resource => resource.Id == result.TreeId && resource.Position == new OwnerWorldPosition(target.X, target.Y));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        loaded.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        output.WriteLine($"Native planting resources {before.Resources.Count} -> {after.Resources.Count}; identical packed ground/layers and layer digest, changed manifest.");
        if (Environment.GetEnvironmentVariable("CLANKER_TERRAIN_CACHE_CAPTURE") is { Length: > 0 } capture)
        {
            Directory.CreateDirectory(capture);
            await File.WriteAllTextAsync(Path.Combine(capture, "before.json"), beforeJson);
            await File.WriteAllTextAsync(Path.Combine(capture, "after.json"), afterJson);
        }
    }
}
