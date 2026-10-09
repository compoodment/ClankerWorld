using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class GeographyVisibilityTests
{
    [Fact]
    public void PausedCheckpointLoadRefusesAnotherGeneratedWorldWithTheSameSeed()
    {
        var options = new GeographyOptions("same-seed-checkpoint-load", WorldSizePreset.Small,
            HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
        using var first = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: options);
        using var second = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: options with { WaterPercent = 65 });
        first.Pause();
        second.Pause();
        var firstState = first.ExportState();
        var secondBytes = PrivateWorldRuntimeCodec.Encode(second.ExportState());
        Assert.Equal(firstState.WorldSeed, second.ExportState().WorldSeed);
        Assert.NotEqual(first.Society.WorldId, second.Society.WorldId);

        Assert.Throws<InvalidDataException>(() => second.LoadPausedCheckpoint(firstState));
        Assert.Equal(secondBytes, PrivateWorldRuntimeCodec.Encode(second.ExportState()));
        second.LoadPausedCheckpoint(PrivateWorldRuntimeCodec.Decode(secondBytes));
        second.Validate();
        Assert.Equal(secondBytes, PrivateWorldRuntimeCodec.Encode(second.ExportState()));
    }

    [Fact]
    public void RestoringGeneratedCheckpointKeepsItsExistingSeedBasedIdentity()
    {
        var options = new GeographyOptions("existing-generated-world-identity", WorldSizePreset.Small,
            HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
        var selected = GeographyCandidateSelector.Select(options);
        using var created = PrivateWorldRuntime.CreateFromGeneratedGeography(options.Seed, selected.Options, selected.Map);
        // Before generated maps had their own IDs, their society used the same
        // native FounderSetup genesis as the base camp, identified by the seed.
        using var oldGenesis = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup);
        var state = created.ExportState() with { Society = oldGenesis.ExportState().Society };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        restored.Validate();
        Assert.Equal(options.Seed, restored.Society.WorldId);
        Assert.Equal(selected.Options, restored.ExportState().Geography);
        Assert.Equal(selected.Map.ManifestDigest, restored.ExportState().Map.ManifestDigest);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeedIdentifiedGeneratedWorldStillRejectsAnIdenticalNewCreation(bool planted)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-legacy-generated-catalog-");
        try
        {
            var options = new GeographyOptions("existing-catalog-generated-identity", WorldSizePreset.Small,
                HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
            var selected = GeographyCandidateSelector.Select(options);
            using var generated = PrivateWorldRuntime.CreateFromGeneratedGeography(options.Seed, selected.Options, selected.Map);
            using var oldGenesis = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup);
            var oldState = generated.ExportState() with { Society = oldGenesis.ExportState().Society };
            if (planted)
            {
                using var setup = PrivateWorldRuntime.Restore(oldState);
                setup.InitializeFirstTownContent();
                var anchor = NormalPathWorld.FindStartingTownSite(oldState.Map);
                setup.AcceptFirstTownLayout(anchor);
                var target = oldState.Map.Tiles.Select(tile => tile.Position).First(point =>
                    oldState.Map.FootDistance(anchor, point) >= 16 &&
                    oldState.Map.IsBuildable(point) && TreeGrowthRules.GroundRefusal(oldState.Map, point) is null &&
                    !oldState.Map.Resources.Any(resource => resource.Position == point) &&
                    setup.WorldSystems.Chunks.Single(chunk => chunk.Coordinate == ChunkRules.ToChunkCoordinate(point, chunk.ChunkSize))
                        .Resources.Count < setup.WorldSystems.Config.MaxResourcesPerChunk);
                const string actor = "founder:12980000000000000000000000000001";
                setup.PlaceFounder(actor, target);
                var inventory = InventoryFixture.AddLot(setup.Society.Inventory, "legacy-tree-seed", TreeGrowthRules.TreeSeedItem, actor, 1);
                using var planting = PrivateWorldRuntime.Restore(FarmFieldTests.WithInventory(setup.ExportState(), inventory));
                var result = planting.PlantTree(actor, TreeGrowthRules.Broadleaf, "legacy-tree-seed", target);
                Assert.True(result.Planted, result.Message);
                planting.Validate();
                oldState = planting.ExportState();
                Assert.NotEqual(selected.Map.ManifestDigest, oldState.Map.ManifestDigest);
                Assert.Equal(selected.Options, oldState.Geography);
            }
            var settings = new WorldAutosaveSettings(options.Seed, true, 5, 3, DateTimeOffset.MinValue, -1);
            var catalog = new WorldCatalogStore(Path.Combine(directory.FullName, "active.json"), oldState, [], settings);
            var existingId = catalog.Active().Id;
            var bytes = PrivateWorldRuntimeCodec.Encode(oldState);

            Assert.Throws<InvalidOperationException>(() => catalog.Add("A new name", generated.ExportState()));
            Assert.Single(catalog.Capture().Worlds);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(catalog.Read(existingId)));

            using var different = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup,
                geographyOptions: options with { Size = WorldSizePreset.Medium });
            var added = catalog.Add("Different map", different.ExportState());
            Assert.NotEqual(options.Seed, added.WorldId);
            Assert.Equal(2, catalog.Capture().Worlds.Count);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(catalog.Read(existingId)));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void SelectedCandidateAttemptSurvivesCreateSaveAndRestoreExactly()
    {
        var options = new GeographyOptions("issue-409-medium-wrapped", WorldSizePreset.Medium,
            WaterPercent: 50, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
        var selection = GeographyCandidateSelector.Select(options);
        var selectedOptions = selection.Options;
        using var created = PrivateWorldRuntime.CreateFromGeneratedGeography(
            selectedOptions.Seed, selectedOptions, selection.Map);
        var bytes = PrivateWorldRuntimeCodec.Encode(created.ExportState());

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var state = restored.ExportState();
        Assert.Equal(selection.Selected.Attempt, state.Map.GenerationAttempt);
        Assert.Equal(selection.Selected.Attempt, state.Geography!.CandidateAttempt);
        Assert.Equal(selectedOptions, state.Geography);
        Assert.Equal(selection.Map.ManifestDigest, state.Map.ManifestDigest);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(state));
    }

    [Fact]
    public void GeneratedGeographyCreationRejectsAMapFromAnotherCandidateAttempt()
    {
        var options = new GeographyOptions("issue-409-candidate-identity", WorldSizePreset.Small);
        var otherAttempt = GeographyCandidateSelector.GenerateCandidate(options with { CandidateAttempt = 1 });

        Assert.Throws<ArgumentException>(() => PrivateWorldRuntime.CreateFromGeneratedGeography(
            options.Seed, options, otherAttempt));
    }

    [Fact]
    public void UnsupportedBalancedVisibilityRevisionIsRefused()
    {
        var options = new GeographyOptions("old-balanced-visibility", WorldSizePreset.Small,
            BalancedVisibilityVersion: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(options));
    }

}
