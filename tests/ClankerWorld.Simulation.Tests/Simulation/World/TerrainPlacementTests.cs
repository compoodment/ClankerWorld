using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Issue #461: sand, forest floor and hills on newly generated worlds. The
/// measured bounds are provisional; they check the rules' shape, not
/// owner-approved density targets.
/// </summary>
public sealed class TerrainPlacementTests
{
    // The fixed matrix the forest and sand measurements run over: default
    // Balanced worlds, other climate choices, a flat map and busier settings.
    private static readonly GeographyOptions[] Matrix =
    [
        Current("terrain-matrix-0", WorldSizePreset.Small),
        Current("terrain-matrix-1", WorldSizePreset.Small) with { WrapEastWest = false },
        Current("terrain-matrix-2", WorldSizePreset.Medium),
        Current("terrain-matrix-3", WorldSizePreset.Small) with
        {
            ClimateMode = ClimateMode.Uniform, SelectedClimate = ClimateZone.Tropical, LatitudeCooling = false,
        },
        Current("terrain-matrix-4", WorldSizePreset.Small) with
        {
            ClimateMode = ClimateMode.Uniform, SelectedClimate = ClimateZone.Temperate,
        },
        Current("terrain-matrix-5", WorldSizePreset.Small) with
        {
            ClimateMode = ClimateMode.Dominant, SelectedClimate = ClimateZone.Dry,
        },
        Current("terrain-matrix-6", WorldSizePreset.Small) with
        {
            ForestCover = GenerationAmount.High, MountainRelief = GenerationAmount.High,
            ResourceAbundance = ResourceAbundance.Abundant,
        },
    ];

    [Fact]
    public void MapAcceptanceRefusesATreeOrPlantOnSand()
    {
        var map = GeneratedCampMapGenerator.Generate(Current("terrain-matrix-0", WorldSizePreset.Small));
        var sand = map.Tiles.First(tile => map.SurfaceAt(tile.Position) == SurfaceKind.Sand &&
            map.Resources.All(resource => resource.Position != tile.Position)).Position;
        foreach (var misplaced in new[]
                 {
                     map.Resources.First(resource => resource.TreeKind is "broadleaf" or "conifer"),
                     map.Resources.First(resource => resource.Id.StartsWith("wild-", StringComparison.Ordinal) &&
                         resource.NaturalObjectKind is "berry_bush" or "wild_greens"),
                 })
        {
            var moved = map with
            {
                Resources = map.Resources.Select(resource =>
                    resource.Id == misplaced.Id ? resource with { Position = sand } : resource).ToArray(),
            };
            moved = moved with { ManifestDigest = MapManifestCodec.Digest(moved) };
            var result = MapAcceptance.Validate(moved, allowEmptyCamp: true);
            Assert.False(result.IsValid);
            Assert.Contains("sand", result.Failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeneratedSitesLeaveRoomInEveryChunkForSitesAddedLater()
    {
        var options = Current("terrain-budget", WorldSizePreset.Medium) with
        {
            ForestCover = GenerationAmount.High,
            ResourceAbundance = ResourceAbundance.Abundant,
        };
        var map = GeneratedCampMapGenerator.Generate(options);
        Assert.All(map.Resources.GroupBy(resource => ChunkRules.ToChunkCoordinate(resource.Position)),
            chunk => Assert.InRange(chunk.Count(), 1, TerrainPlacementRules.GeneratedResourcesPerChunk));
        Assert.True(TerrainPlacementRules.GeneratedResourcesPerChunk + 3 <= WorldSystemsConfig.Default.MaxResourcesPerChunk);
        // Rare deposits are placed before filler trees, so a busy forest map
        // still has its iron, gold and diamonds.
        Assert.Contains(map.Resources, resource => resource.NaturalObjectKind == "iron_outcrop");
        Assert.Contains(map.Resources, resource => resource.NaturalObjectKind == "gold_outcrop");
        Assert.Contains(map.Resources, resource => resource.NaturalObjectKind == "diamond_outcrop");
    }

    // New worlds use the current hydrology revision, as New World does.
    private static GeographyOptions Current(string seed, WorldSizePreset size) =>
        new(seed, size, WaterPercent: 50, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);

}
