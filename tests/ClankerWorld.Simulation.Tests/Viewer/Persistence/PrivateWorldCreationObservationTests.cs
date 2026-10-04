using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldCreationObservationTests
{
    [Fact]
    public void FreshProductionCheckpointRequestsWorldCreationWithoutReplacingTheSavedWorld()
    {
        using var checkpoint = new TemporaryCheckpoint();
        var file = ProductionFile(checkpoint.Path);
        using var world = file.LoadOrCreate("camp-alpha");
        var originalBytes = File.ReadAllBytes(checkpoint.Path);
        var state = world.ExportState();
        Assert.Null(state.Geography);
        Assert.Equal((6, 5), (state.Map.Width, state.Map.Height));
        Assert.NotEmpty(state.Map.CampObjects);
        Assert.Empty(state.Inhabitants);
        Assert.Empty(world.FounderSetup!.FounderIds);
        Assert.False(world.FounderSetup.Started);
        Assert.True(world.Society.IsPaused);
        Assert.Equal(0, world.WorldTick);

        var store = new OwnerWorldObservationStore(world);
        var snapshot = store.GetSnapshot();
        Assert.True(snapshot.FounderSetup!.RequiresWorldCreation);
        var cached = store.GetReconnectBaseline(snapshot.LatestEventId, snapshot.WorldId,
            snapshot.MapManifestDigest, snapshot.MapLayersDigest).Snapshot;
        Assert.True(cached.FounderSetup!.RequiresWorldCreation);
        Assert.Equal(snapshot.WorldId, cached.WorldId);
        Assert.Equal(originalBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        AssertCheckpointPreserved(file, world, requiresCreation: true);
        Assert.Equal(originalBytes, File.ReadAllBytes(checkpoint.Path));
    }

    [Fact]
    public void CreatedGeographyRemainsARealWorldWhenCachedTerrainIsOmitted()
    {
        // Follow Preview/Create's real candidate selection, not the retired
        // base-camp generator or a hand-authored geography flag in a save.
        var options = new GeographyOptions("camp-alpha", WorldSizePreset.Small,
            HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
        var selected = GeographyCandidateSelector.Select(options);
        using var world = PrivateWorldRuntime.CreateFromGeneratedGeography("camp-alpha",
            selected.Options, selected.Map);
        world.InitializeFirstTownContent();
        var state = world.ExportState();
        Assert.NotNull(state.Geography);
        Assert.Equal((256, 128), (state.Map.Width, state.Map.Height));
        Assert.Empty(state.Map.CampObjects);
        Assert.Empty(world.FounderSetup!.FounderIds);
        Assert.False(world.FounderSetup.Started);
        Assert.Equal(0, world.WorldTick);
        Assert.NotEmpty(world.WorldContent.Buildings);

        var store = new OwnerWorldObservationStore(world);
        var snapshot = store.GetSnapshot();
        Assert.False(snapshot.FounderSetup!.RequiresWorldCreation);
        Assert.NotNull(snapshot.PackedTerrain);
        Assert.NotNull(snapshot.PackedMapLayers);
        var cached = store.GetReconnectBaseline(snapshot.LatestEventId, snapshot.WorldId,
            snapshot.MapManifestDigest, snapshot.MapLayersDigest).Snapshot;
        Assert.Null(cached.PackedTerrain);
        Assert.Null(cached.PackedMapLayers);
        Assert.False(cached.FounderSetup!.RequiresWorldCreation);
        Assert.Equal(snapshot.WorldId, cached.WorldId);
        Assert.Equal(snapshot.MapManifestDigest, cached.MapManifestDigest);

        using var checkpoint = new TemporaryCheckpoint();
        AssertCheckpointPreserved(ProductionFile(checkpoint.Path), world, requiresCreation: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FounderWorkStaysAccessibleBeforeTimeStartsEvenAfterUndo(bool undo)
    {
        using var checkpoint = new TemporaryCheckpoint();
        var file = ProductionFile(checkpoint.Path);
        using var world = file.LoadOrCreate("founder-work");
        const string founder = "founder:88900000000000000000000000000001";
        world.PlaceFounder(founder, new GridPoint(0, 0));
        if (undo) Assert.Equal(0, world.UndoLastFounder(founder));
        else Assert.Equal(founder, Assert.Single(world.FounderSetup!.FounderIds));
        Assert.Equal(0, world.WorldTick);
        Assert.False(world.FounderSetup!.Started);
        if (undo) Assert.Empty(world.Inhabitants);
        Assert.False(new OwnerWorldObservationStore(world).GetSnapshot().FounderSetup!.RequiresWorldCreation);

        AssertCheckpointPreserved(file, world, requiresCreation: false);
    }

    [Fact]
    public void AuthoredDesignStaysAccessibleBeforeAnyFounderOrBuildingExists()
    {
        using var checkpoint = new TemporaryCheckpoint();
        var file = ProductionFile(checkpoint.Path);
        using var world = file.LoadOrCreate("authored-world");
        var design = BuildingDesign.Create("My first shelter", "shelter", 2);
        world.ProposeContent(design);
        Assert.Equal(design.PackageId, Assert.Single(world.Content.Packages).Manifest.PackageId);
        Assert.Empty(world.Inhabitants);
        Assert.Empty(world.WorldSimulation.Buildings);
        Assert.Equal(0, world.WorldTick);
        Assert.False(new OwnerWorldObservationStore(world).GetSnapshot().FounderSetup!.RequiresWorldCreation);

        AssertCheckpointPreserved(file, world, requiresCreation: false);
    }

    [Fact]
    public async Task GenericStateFileKeepsItsExistingLegacyWorldAndProgress()
    {
        using var checkpoint = new TemporaryCheckpoint();
        // The production host opts into FounderSetup. The general persistence
        // fixture still creates its existing, playable Legacy world by default.
        var file = new PrivateWorldStateFile(checkpoint.Path);
        using var world = file.LoadOrCreate("legacy-world");
        Assert.Null(world.FounderSetup);
        Assert.NotEmpty(world.Inhabitants);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Pause();
        Assert.Equal(1, world.WorldTick);
        Assert.Null(new OwnerWorldObservationStore(world).GetSnapshot().FounderSetup);

        AssertCheckpointPreserved(file, world, requiresCreation: false);
    }

    private static PrivateWorldStateFile ProductionFile(string path) => new(path,
        newWorldPace: WorldStartPace.FounderSetup, allowDifferentSavedSeed: true);

    private static void AssertCheckpointPreserved(PrivateWorldStateFile file, PrivateWorldRuntime world,
        bool requiresCreation)
    {
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        file.Save(world);
        Assert.Equal(saved, File.ReadAllBytes(file.Path));
        using var reopened = file.LoadOrCreate(world.ExportState().WorldSeed);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reopened.ExportState()));
        var snapshot = new OwnerWorldObservationStore(reopened).GetSnapshot();
        Assert.Equal(requiresCreation, snapshot.FounderSetup?.RequiresWorldCreation ?? false);
        Assert.Equal(saved, File.ReadAllBytes(file.Path));
    }

    private sealed class TemporaryCheckpoint : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clankerworld-new-world-");
        public string Path => System.IO.Path.Combine(directory.FullName, "world.json");
        public void Dispose() => directory.Delete(recursive: true);
    }
}
