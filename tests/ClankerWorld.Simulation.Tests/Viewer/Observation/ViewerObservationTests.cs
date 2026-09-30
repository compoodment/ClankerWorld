using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ViewerObservationTests
{
    [Fact]
    public void GeneratedReconnectOmitsOnlyAlreadyHeldTerrain()
    {
        var geography = new GeographyOptions("cached-terrain", WorldSizePreset.Small);
        using var runtime = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var store = new OwnerWorldObservationStore(runtime);
        var initial = store.GetReconnectBaseline(0).Snapshot;
        Assert.NotNull(initial.PackedTerrain);
        Assert.NotNull(initial.PackedMapLayers);
        Assert.Equal(MapLayerManifestCodec.Digest(runtime.ExportState().Map), initial.MapLayersDigest);
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var client = System.Text.Json.JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(initial, options), options)!;
        Assert.Equal(initial.PackedMapLayers!.Width, client.PackedMapLayers!.Width);
        Assert.Equal(initial.PackedMapLayers.Height, client.PackedMapLayers.Height);
        Assert.Equal(initial.PackedMapLayers.Encoding, client.PackedMapLayers.Encoding);
        Assert.Equal(initial.PackedMapLayers.Climate, client.PackedMapLayers.Climate);
        Assert.Equal(initial.PackedMapLayers.Elevation, client.PackedMapLayers.Elevation);
        Assert.Equal(initial.PackedMapLayers.Hydrology, client.PackedMapLayers.Hydrology);
        Assert.Equal(initial.PackedMapLayers.Surface, client.PackedMapLayers.Surface);
        Assert.Equal(initial.PackedMapLayers.Vegetation, client.PackedMapLayers.Vegetation);
        Assert.Equal(initial.MapLayersDigest, client.MapLayersDigest);
        Assert.Contains("owner-terrain-delta.v1", store.GetOwnerHandshake().ServerCapabilities);
        Assert.Contains("owner-map-layer-delta.v1", store.GetOwnerHandshake().ServerCapabilities);

        var unchanged = store.GetReconnectBaseline(0, initial.WorldId, initial.MapManifestDigest,
            initial.MapLayersDigest).Snapshot;
        Assert.Null(unchanged.PackedTerrain);
        Assert.Null(unchanged.PackedMapLayers);
        Assert.Empty(unchanged.Tiles);
        Assert.Equal(initial.MapManifestDigest, unchanged.MapManifestDigest);
        Assert.Equal(initial.MapLayersDigest, unchanged.MapLayersDigest);
        var layersUnchangedButTerrainClaimedWithoutTheLayerDigest = store.GetReconnectBaseline(0,
            initial.WorldId, initial.MapManifestDigest, new string('0', 64)).Snapshot;
        Assert.Null(layersUnchangedButTerrainClaimedWithoutTheLayerDigest.PackedTerrain);
        Assert.NotNull(layersUnchangedButTerrainClaimedWithoutTheLayerDigest.PackedMapLayers);
        Assert.NotNull(store.GetReconnectBaseline(0, "other-world", initial.MapManifestDigest).Snapshot.PackedTerrain);
        Assert.NotNull(store.GetReconnectBaseline(0, initial.WorldId, "wrong-digest").Snapshot.PackedTerrain);
    }

    [Fact]
    public void ResourceProjectionCarriesAuthoritativeStockAndRegrowthIntoTheClient()
    {
        using var runtime = new PrivateWorldRuntime("ecology-projection");
        var snapshot = new OwnerWorldObservationStore(runtime).GetSnapshot();
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var client = System.Text.Json.JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot, options), options)!;
        foreach (var resource in runtime.WorldSystems.Ecology.Resources)
        {
            var projected = snapshot.Resources.Single(item => item.Id == resource.Id);
            Assert.Equal(resource.Quantity, projected.Quantity);
            Assert.Equal(resource.Capacity, projected.Capacity);
            Assert.Equal(resource.RegenerationAmount, projected.RegenerationAmount);
            Assert.Equal(resource.RegenerationIntervalDays, projected.RegenerationIntervalDays);
            Assert.Equal(resource.RegenerationSeason.ToString().ToLowerInvariant(), projected.RegenerationSeason);
            var displayed = client.Resources.Single(item => item.Id == resource.Id);
            Assert.Equal(projected.Quantity, displayed.Quantity);
            Assert.Equal(projected.Capacity, displayed.Capacity);
            Assert.Equal(projected.RegenerationSeason, displayed.RegenerationSeason);
        }
    }

    [Fact]
    public async Task CropJobsAreVisibleAlongsideWorkstationJobs()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        Assert.True(runtime.StageStarterContent());
        for (var tick = 0; tick < 150; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
        }
        var snapshot = new OwnerWorldObservationStore(runtime).GetSnapshot();
        var state = runtime.ExportState();
        Assert.NotEmpty(state.WorldSimulation!.CropBuilds!);
        var expected = state.WorldSimulation.ProductionJobs.Concat(state.WorldSimulation.CropBuilds!).Select(item => item.JobId).Order(StringComparer.Ordinal);
        Assert.Equal(expected, snapshot.ProductionJobs.Select(item => item.JobId));
        Assert.Equal(snapshot.ProductionJobs.Count, snapshot.WorldSystems!.ProductionJobCount);
    }

    [Fact]
    public void EmptyPopulationRemainsObservableAfterRestore()
    {
        using var genesis = new PrivateWorldRuntime("playtest-alpha");
        var state = genesis.ExportState();
        var society = state.Society.Society;
        foreach (var inhabitant in society.Inhabitants)
        {
            society = ClankerWorld.Simulation.Society.SocietyFixture.Kill(society, inhabitant.Id,
                ClankerWorld.Simulation.Society.SocietyDeathCause.NaturalAge).Checkpoint;
        }
        state = state with { Society = state.Society with { Society = society }, Inhabitants = [] };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        var baseline = new OwnerWorldObservationStore(restored).GetReconnectBaseline(0);
        Assert.Empty(baseline.Snapshot.Inhabitants);
        Assert.Null(baseline.Snapshot.Actor);
        Assert.Equal(state.Society.Society.WorldTick, baseline.Snapshot.WorldTick);
    }

    [Fact]
    public void OwnerInhabitantKnowledgeIsBoundedToLocalPerceptionAndItsCommittedRoute()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        var store = new OwnerWorldObservationStore(runtime);

        var snapshot = store.GetSnapshot();
        var inhabitant = Assert.Single(snapshot.Inhabitants);
        var knowledge = inhabitant.SpatialKnowledge;

        Assert.Equal(inhabitant.Position, knowledge.CurrentTile);
        Assert.All(knowledge.PerceivedTiles, tile => Assert.Contains(tile, knowledge.KnownTiles));
        Assert.All(inhabitant.Route.Steps, step => Assert.Contains(step, knowledge.KnownTiles));
        if (inhabitant.Route.Destination is not null)
        {
            Assert.Contains(inhabitant.Route.Destination, knowledge.KnownTiles);
        }

        Assert.Equal(
            knowledge.KnownTiles
                .Distinct()
                .OrderBy(tile => tile.Y)
                .ThenBy(tile => tile.X),
            knowledge.KnownTiles);
        Assert.True(
            knowledge.KnownTiles.Count < snapshot.Tiles.Count,
            "The fixture must not project the complete server map as inhabitant knowledge.");
    }

    [Fact]
    public void PrivateWorldObservationProjectsTheActiveSettlementAndNeeds()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        var store = new OwnerWorldObservationStore(runtime);

        var snapshot = store.GetSnapshot();

        Assert.Equal("playtest-alpha", snapshot.WorldId);
        Assert.Equal(4, snapshot.Inhabitants.Count);
        Assert.All(snapshot.Inhabitants, inhabitant =>
        {
            Assert.False(inhabitant.IsDraft);
            Assert.Equal("active", inhabitant.Lifecycle);
            Assert.Contains(inhabitant.DecisionFactors, factor => factor.Key == "personality");
            Assert.Contains(inhabitant.DecisionFactors, factor => factor.Key == "household");
            Assert.NotEmpty(inhabitant.SpatialKnowledge.PerceivedTiles);
            Assert.Contains(
                inhabitant.Relationships,
                relationship => relationship.Type == "household_membership" &&
                    relationship.State == "accepted" &&
                    relationship.OtherPartyId == "household:camp-alpha");
        });
        Assert.NotNull(snapshot.Cognition);
        Assert.NotNull(snapshot.WorldSystems);
        Assert.Equal("spring", snapshot.WorldSystems!.Season);
        Assert.Equal(3, snapshot.WorldSystems.EcologyResourceCount);
        Assert.Equal(1, snapshot.WorldSystems.FactionCount);
        Assert.Equal(4, snapshot.Inhabitants.Select(inhabitant => inhabitant.Id).Distinct().Count());
    }

    [Fact]
    public async Task PrivateWorldObservationExposesPublicIntentionsWithoutModelTrace()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        _ = await runtime.AdvanceOneTickAsync();

        var snapshot = new OwnerWorldObservationStore(runtime).GetSnapshot();

        Assert.Contains(snapshot.Inhabitants, inhabitant =>
            inhabitant.PublicIntention is { WorldTick: 1, Summary: not null });
        Assert.Equal(4, snapshot.Cognition!.Decisions!.Count);
        Assert.All(snapshot.Cognition.Decisions, decision =>
        {
            Assert.Equal(1, decision.WorldTick);
            Assert.Equal("deterministic", decision.Provider);
            Assert.Contains(snapshot.Inhabitants, inhabitant => inhabitant.Id == decision.InhabitantId);
        });
        Assert.All(snapshot.Inhabitants.Where(inhabitant => inhabitant.PublicIntention is not null), inhabitant =>
        {
            Assert.DoesNotContain("chain", inhabitant.PublicIntention!.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prompt", inhabitant.PublicIntention.Summary, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task PrivateWorldObservationReplaysItsOwnEventCursor()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        _ = await runtime.AdvanceOneTickAsync();
        var store = new OwnerWorldObservationStore(runtime);

        var snapshot = store.GetSnapshot();
        var suffix = store.GetEventsAfter(1);

        Assert.Equal(1, snapshot.WorldTick);
        Assert.Equal(snapshot.WorldTick, suffix.SnapshotTick);
        Assert.All(suffix.Events, worldEvent => Assert.True(worldEvent.EventId > 1));
        Assert.Contains(suffix.Events, worldEvent => worldEvent.Kind == "tick_advanced");
    }
}
