using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class MedicinalHerbResourceTests
{
    [Theory]
    [InlineData(true, ResourceAbundance.Sparse)]
    [InlineData(false, ResourceAbundance.Abundant)]
    public void GeneratedHerbsAreReachablePlantsIncludedInEcologyChunksAndCurrentSave(
        bool wrapEastWest, ResourceAbundance abundance)
    {
        var options = new GeographyOptions("care-generated-herbs", WorldSizePreset.Small,
            WrapEastWest: wrapEastWest, ResourceAbundance: abundance);
        var generated = GeneratedCampMapGenerator.Generate(options);
        var repeated = GeneratedCampMapGenerator.Generate(options);
        var patch = Assert.Single(generated.Resources, resource => resource.Kind == CareContent.MedicinalHerbs);
        Assert.Equal("medicinal_herb_patch", patch.NaturalObjectKind);
        Assert.True(patch.IsRenewable);
        Assert.True(TerrainPlacementRules.IsOrdinaryVegetation(patch));
        Assert.True(TerrainPlacementRules.CanHoldOrdinaryVegetation(generated.SurfaceAt(patch.Position)!.Value));
        Assert.True(generated.IsReachableOnFoot(
            generated.Resources.Single(resource => resource.Id == "berry-patch").Position, patch.Position));
        Assert.Equal(generated.ManifestDigest, repeated.ManifestDigest);
        Assert.Equal(patch, repeated.Resources.Single(resource => resource.Id == patch.Id));

        using var world = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: options);
        var state = world.ExportState();
        var ecology = state.WorldSystems!.Ecology.GetResource(patch.Id);
        Assert.Equal(CareContent.MedicinalHerbs, ecology.Kind);
        Assert.Equal(patch.Position, ecology.Position);
        Assert.True(ecology.IsRenewable);
        Assert.True(ecology.Quantity > 0);
        Assert.True(ecology.RegenerationAmount > 0);
        Assert.True(ecology.RegenerationIntervalDays > 0);
        var chunk = Assert.Single(state.WorldSystems.Chunks,
            item => item.Resources.Any(resource => resource.ResourceId == patch.Id));
        var metadata = Assert.Single(chunk.Resources, resource => resource.ResourceId == patch.Id);
        Assert.Equal(CareContent.MedicinalHerbs, metadata.Kind);
        Assert.True(metadata.IsRenewable);
        Assert.Equal(ChunkRules.ToLocalPoint(patch.Position, chunk.ChunkSize), metadata.LocalPosition);
        Assert.All(state.WorldSystems.Chunks, item =>
            Assert.InRange(item.Resources.Count, 0, TerrainPlacementRules.GeneratedResourcesPerChunk));

        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var loaded = restored.ExportState();
        Assert.Equal(patch, loaded.Map.Resources.Single(resource => resource.Id == patch.Id));
        Assert.Equal(ecology, loaded.WorldSystems!.Ecology.GetResource(patch.Id));
        Assert.Equal(metadata, loaded.WorldSystems.Chunks.Single(item => item.Coordinate == chunk.Coordinate)
            .Resources.Single(resource => resource.ResourceId == patch.Id));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded));
    }

    [Fact]
    public void HarvestedHerbsKeepTheirSavedDepletionAndRegenerateOnTheExistingEcologySchedule()
    {
        using var world = new PrivateWorldRuntime("care-herb-regrowth", startPace: WorldStartPace.FounderSetup);
        var state = world.ExportState();
        var patch = Assert.Single(state.Map.Resources, resource => resource.Kind == CareContent.MedicinalHerbs);
        var ecology = state.WorldSystems!.Ecology.GetResource(patch.Id);
        var harvested = EcologyRules.Harvest(ecology, ecology.Quantity);
        Assert.True(harvested.IsValid);
        Assert.Equal(ecology.Quantity, harvested.Yield);
        var depleted = Assert.IsType<EcologyResource>(harvested.Resource);
        Assert.Equal(0, depleted.Quantity);
        Assert.Equal(EcologyResourceState.Depleted, depleted.State);
        Assert.False(EcologyRules.Harvest(depleted, 1).IsValid);

        var saved = state with
        {
            Resources = state.Resources.Select(resource => resource.ResourceId == patch.Id
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == patch.Id
                        ? depleted : resource).ToArray(),
                },
            },
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        var loaded = restored.ExportState();
        Assert.Equal(depleted, loaded.WorldSystems!.Ecology.GetResource(patch.Id));
        Assert.Equal(ResourceState.Depleted, loaded.Resources.Single(resource => resource.ResourceId == patch.Id).State);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded));

        var config = loaded.WorldSystems.Config;
        var before = EcologyRules.Regenerate(depleted, WorldCalendarRules.FromTick(0, config), config);
        Assert.Equal(0, before.Quantity);
        var regrown = EcologyRules.Regenerate(depleted,
            WorldCalendarRules.FromTick(depleted.NextRegenerationDay * config.TicksPerDay, config), config);
        Assert.Equal(ecology.RegenerationAmount, regrown.Quantity);
        Assert.Equal(EcologyResourceState.Available, regrown.State);
        Assert.Equal(depleted.NextRegenerationDay + ecology.RegenerationIntervalDays, regrown.NextRegenerationDay);
        Assert.True(EcologyRules.Harvest(regrown, 1).IsValid);
    }

    [Fact]
    public void MapAcceptanceRefusesHerbsMovedOntoSand()
    {
        var map = GeneratedCampMapGenerator.Generate(new GeographyOptions("terrain-matrix-0", WorldSizePreset.Small,
            WaterPercent: 50, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion));
        var herbs = Assert.Single(map.Resources, resource => resource.Kind == CareContent.MedicinalHerbs);
        var sand = map.Tiles.First(tile => map.SurfaceAt(tile.Position) == SurfaceKind.Sand &&
            map.Resources.All(resource => resource.Position != tile.Position)).Position;
        var moved = map with
        {
            Resources = map.Resources.Select(resource => resource.Id == herbs.Id
                ? resource with { Position = sand } : resource).ToArray(),
        };
        moved = moved with { ManifestDigest = MapManifestCodec.Digest(moved) };
        var result = MapAcceptance.Validate(moved, allowEmptyCamp: true);
        Assert.False(result.IsValid);
        Assert.Contains("sand", result.Failure, StringComparison.Ordinal);
    }
}
