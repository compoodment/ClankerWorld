using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class CachedTerrainObservationTests
{
    [Theory]
    [InlineData(WorldSizePreset.Small, 750_000L)]
    [InlineData(WorldSizePreset.Medium, 3_300_000L)]
    public void CachedRefreshAvoidsRepeatedMapSizedAllocations(WorldSizePreset size, long bytesPerRefreshBudget)
    {
        var geography = new GeographyOptions("cached-observation-cost-audit", size);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var store = new OwnerWorldObservationStore(world);
        var initial = store.GetSnapshot();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        ViewerWorldSnapshot Refresh() => store.GetReconnectBaseline(0, initial.WorldId,
            initial.MapManifestDigest, initial.MapLayersDigest).Snapshot;
        for (var warm = 0; warm < 5; warm++) _ = Refresh();

        // Broad allocation budgets leave room for the dynamic projection while
        // detecting either the old layer hashing or fertility-index rebuild.
        const int samples = 20;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var sample = 0; sample < samples; sample++) _ = Refresh();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated <= bytesPerRefreshBudget * samples,
            $"Cached {size} refreshes allocated {allocated / samples:N0} bytes each; budget {bytesPerRefreshBudget:N0}.");
        var cached = Refresh();
        Assert.Null(cached.PackedTerrain);
        Assert.Null(cached.PackedMapLayers);
        Assert.Equal(initial.MapLayersDigest, cached.MapLayersDigest);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task WarmTerrainProjectionKeepsLiveFieldsAndRefreshesAfterReloadAndWorldSwitch()
    {
        var (state, farmer, _, point) = FarmFieldTests.PreparedFarmer("cached-fields-and-switch");
        using var world = FarmFieldTests.Restore(state);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var store = new OwnerWorldObservationStore(world);
        var initial = store.GetSnapshot();
        Assert.Empty(initial.Fields!);

        Assert.True(world.StartFieldWork(farmer, point, FarmWorkKind.Till).Accepted);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var currentBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var refreshed = store.GetReconnectBaseline(initial.LatestEventId, initial.WorldId,
            initial.MapManifestDigest, initial.MapLayersDigest).Snapshot;
        var field = Assert.Single(refreshed.Fields!);
        Assert.Equal(2, field.WorkRemaining);
        Assert.Equal(new LandFertility(state.Map, state.WorldSeed).At(point), field.Fertility);
        Assert.True(refreshed.LatestEventId > initial.LatestEventId);
        Assert.Equal(MapLayerManifestCodec.Digest(world.ExportState().Map), refreshed.MapLayersDigest);
        var cachedFields = store.GetReconnectBaseline(refreshed.LatestEventId, refreshed.WorldId,
            refreshed.MapManifestDigest, refreshed.MapLayersDigest).Snapshot;
        Assert.Equal(refreshed.Fields, cachedFields.Fields);
        Assert.Null(cachedFields.PackedMapLayers);
        Assert.Equal(currentBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        world.Pause();
        world.LoadPausedCheckpoint(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Empty(store.GetSnapshot().Fields!);

        var geography = new GeographyOptions("cached-replacement-world", WorldSizePreset.Small);
        using var replacement = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var expected = new OwnerWorldObservationStore(replacement).GetSnapshot();
        world.SwitchPausedWorld(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(replacement.ExportState())));
        var switched = store.GetReconnectBaseline(initial.LatestEventId, initial.WorldId,
            initial.MapManifestDigest, initial.MapLayersDigest).Snapshot;
        Assert.NotEqual(initial.MapLayersDigest, switched.MapLayersDigest);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(switched));
        var cached = store.GetReconnectBaseline(switched.LatestEventId, switched.WorldId,
            switched.MapManifestDigest, switched.MapLayersDigest).Snapshot;
        Assert.Null(cached.PackedTerrain);
        Assert.Null(cached.PackedMapLayers);
        Assert.Equal(expected.MapLayersDigest, cached.MapLayersDigest);
    }
}
