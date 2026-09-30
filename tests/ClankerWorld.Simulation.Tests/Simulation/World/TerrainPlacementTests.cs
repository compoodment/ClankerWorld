using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Issue #461: sand, forest floor and hills on newly generated worlds. The
/// measured bounds are provisional; they check the rules' shape, not
/// owner-approved density targets.
/// </summary>
public sealed class TerrainPlacementTests(ITestOutputHelper output)
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
    public void SandComesOnlyFromTheDesertAndBeachRulesAndCarriesNoTreesOrPlants()
    {
        foreach (var options in Matrix.Append(Current("terrain-dry", WorldSizePreset.Small) with
        {
            ClimateMode = ClimateMode.Uniform,
            SelectedClimate = ClimateZone.Dry,
            LatitudeCooling = false,
        }))
        {
            var map = GeneratedCampMapGenerator.Generate(options);
            var geography = GeographyGenerator.Generate(options);
            Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
            var oceanShore = 0;
            var beaches = 0;
            var riverBanks = 0;
            var sandyRiverBanks = 0;
            foreach (var tile in map.Tiles)
            {
                var point = tile.Position;
                if (map.HydrologyAt(point) != WaterKind.Land) continue;
                var besideOcean = Beside(map, point, WaterKind.Ocean);
                var sand = map.SurfaceAt(point) == SurfaceKind.Sand;
                if (besideOcean && map.ElevationAt(point) < TerrainPlacementRules.BeachMaximumElevation &&
                    map.ClimateAt(point) is not (ClimateZone.Polar or ClimateZone.Cold))
                {
                    oceanShore++;
                    if (sand) beaches++;
                }
                if (Beside(map, point, WaterKind.River) && !besideOcean)
                {
                    riverBanks++;
                    if (sand) sandyRiverBanks++;
                }
                if (!sand) continue;
                var desert = map.ClimateAt(point) == ClimateZone.Dry &&
                    geography.At(point.X, point.Y).Rainfall <= TerrainPlacementRules.DesertMaximumRainfall;
                var beach = besideOcean && map.ElevationAt(point) < TerrainPlacementRules.BeachMaximumElevation &&
                    map.ClimateAt(point) is not (ClimateZone.Polar or ClimateZone.Cold);
                Assert.True(desert || beach,
                    $"Sand at {point} in {options.Seed} is neither desert nor an ocean beach.");
                Assert.True(map.VegetationAt(point) is not (VegetationCover.Forest or VegetationCover.Grass),
                    $"Sand at {point} in {options.Seed} carries forest or grass cover.");
            }
            Assert.DoesNotContain(map.Resources, resource => TerrainPlacementRules.IsOrdinaryVegetation(resource) &&
                map.SurfaceAt(resource.Position) == SurfaceKind.Sand);
            output.WriteLine($"{options.Seed}: beaches {beaches}/{oceanShore} ocean-shore tiles; " +
                $"sandy river banks {sandyRiverBanks}/{riverBanks}");
            // Rivers keep grass banks except where a desert reaches them.
            if (options.ClimateMode == ClimateMode.Balanced && riverBanks > 0)
                Assert.True(sandyRiverBanks * 10 <= riverBanks, $"{options.Seed} outlines its rivers with sand.");
            // Beaches form stretches of a shore, not an outline of every coast.
            if (options.ClimateMode == ClimateMode.Balanced && oceanShore > 100)
                Assert.InRange(beaches * 100 / oceanShore, 5, 60);
        }
    }

    [Fact]
    public void BeachesAndGrovesContinueAcrossAWrappedSeamButNotOffAFlatMapEdge()
    {
        // Neighboring columns sit one unit apart in noise space everywhere,
        // including across the seam, so stretches of beach and grove carry on.
        foreach (var (layer, frequency) in new[]
                 {
                     ("beach", TerrainPlacementRules.BeachNoiseFrequency),
                     ("forest-grove", TerrainPlacementRules.GroveNoiseFrequency),
                 })
        {
            var noise = GeographyGenerator.LayerNoise("terrain-seam", layer, frequency, 256, wrapEastWest: true);
            var seam = Enumerable.Range(0, 128).Max(y => Math.Abs(noise(255, y) - noise(0, y)));
            var interior = Enumerable.Range(0, 128).Max(y => Math.Abs(noise(127, y) - noise(128, y)));
            Assert.True(seam <= interior * 2 + 0.01f, $"{layer} jumps at the seam: {seam} against {interior}.");
        }
        foreach (var wrap in new[] { true, false })
        {
            var map = GeneratedCampMapGenerator.Generate(Current("terrain-seam", WorldSizePreset.Small) with
            {
                WrapEastWest = wrap,
            });
            foreach (var y in Enumerable.Range(0, map.Height))
                foreach (var x in new[] { 0, map.Width - 1 })
                {
                    var point = new GridPoint(x, y);
                    if (map.SurfaceAt(point) != SurfaceKind.Sand || map.ClimateAt(point) == ClimateZone.Dry) continue;
                    // A beach needs real ocean beside it: across the seam only
                    // when the world wraps, never off the edge of a flat map.
                    Assert.True(Beside(map, point, WaterKind.Ocean));
                }
        }
    }

    [Fact]
    public void ForestFloorAndForestGrassAreMeasuredSeparatelyAcrossFixedWorlds()
    {
        var cases = new Dictionary<string, ForestMeasure>(StringComparer.Ordinal)
        {
            ["all"] = new(),
            ["shoreline"] = new(),
            ["inland river"] = new(),
            ["wrap seam"] = new(),
            ["mountain edge"] = new(),
        };
        foreach (var options in Matrix)
        {
            var map = GeneratedCampMapGenerator.Generate(options);
            var objects = map.Resources.Where(resource => resource.TreeKind is not null || resource.NaturalObjectKind is not null)
                .ToDictionary(resource => resource.Position);
            var perMap = new ForestMeasure();
            foreach (var tile in map.Tiles)
            {
                var point = tile.Position;
                var surface = map.SurfaceAt(point);
                var forestCover = map.VegetationAt(point) == VegetationCover.Forest;
                if (surface == SurfaceKind.ForestFloor)
                    Assert.True(forestCover, $"Forest floor at {point} in {options.Seed} lies outside a forest.");
                if (!forestCover || surface is not (SurfaceKind.ForestFloor or SurfaceKind.Grass)) continue;
                objects.TryGetValue(point, out var standing);
                var measures = new List<ForestMeasure> { cases["all"], perMap };
                if (Beside(map, point, WaterKind.Ocean)) measures.Add(cases["shoreline"]);
                if (Beside(map, point, WaterKind.River)) measures.Add(cases["inland river"]);
                if (map.WrapsEastWest && (point.X == 0 || point.X == map.Width - 1)) measures.Add(cases["wrap seam"]);
                if (map.IsHillAt(point)) measures.Add(cases["mountain edge"]);
                foreach (var measure in measures)
                    measure.Add(surface == SurfaceKind.ForestFloor, standing);
            }
            output.WriteLine($"{options.Seed}: {perMap}");
            if (options.ClimateMode != ClimateMode.Dominant)
                Assert.True(perMap.FloorTiles > 0, $"{options.Seed} has a forest but no grove.");
        }
        foreach (var (name, measure) in cases)
        {
            output.WriteLine($"{name}: {measure}");
            Assert.True(measure.FloorTiles + measure.GrassTiles > 0, $"The fixed worlds never measured the {name} case.");
            // Provisional bounds: forest floor carries a tree or plant on at
            // least nine tiles in ten, mostly trees; forest grass has
            // scattered trees rather than none or a dense stand.
            if (measure.FloorTiles > 0)
            {
                Assert.True(measure.FloorOccupied * 10 >= measure.FloorTiles * 9, $"{name} forest floor is too empty.");
                Assert.True(measure.FloorTrees * 4 >= measure.FloorOccupied * 3, $"{name} forest floor is not mostly trees.");
            }
        }
        var all = cases["all"];
        Assert.InRange(all.GrassTrees * 1000 / all.GrassTiles, 5, 150);
    }

    [Fact]
    public void HillsFormABaseAroundMountainsAndWalkLikeGrass()
    {
        var map = GeneratedCampMapGenerator.Generate(Current("terrain-hills", WorldSizePreset.Small) with
        {
            MountainRelief = GenerationAmount.High,
        });
        var mountains = map.Tiles.Select(tile => tile.Position)
            .Where(point => map.HydrologyAt(point) == WaterKind.Land &&
                map.ElevationAt(point) >= SeededMap.MountainElevationThreshold).ToArray();
        var hills = map.Tiles.Select(tile => tile.Position).Where(map.IsHillAt).ToArray();
        Assert.NotEmpty(mountains);
        Assert.NotEmpty(hills);
        var grass = map.Tiles.Select(tile => tile.Position).First(point => !map.IsHillAt(point) &&
            map.SurfaceAt(point) == SurfaceKind.Grass && map.IsPassable(point));
        foreach (var hill in hills)
        {
            Assert.Equal(WaterKind.Land, map.HydrologyAt(hill));
            Assert.InRange(map.ElevationAt(hill)!.Value, TerrainPlacementRules.HillMinimumElevation,
                SeededMap.MountainElevationThreshold - 1);
            Assert.InRange(mountains.Min(mountain => map.FootDistance(hill, mountain)), 1, TerrainPlacementRules.HillReach);
            // A visual layer only: hills keep their own ground and cost the
            // same to walk and build on as grass.
            Assert.NotEqual(SurfaceKind.Rock, map.SurfaceAt(hill));
            Assert.True(map.IsPassable(hill));
            Assert.True(map.IsBuildable(hill));
            Assert.Equal(map.FootTravelCost(grass), map.FootTravelCost(hill));
        }
        // The band is a readable base: every high-enough land tile touching a
        // mountain belongs to it, so no gaps open between hills and peaks.
        foreach (var mountain in mountains)
            foreach (var near in Around(map, mountain))
                if (map.HydrologyAt(near) == WaterKind.Land &&
                    map.ElevationAt(near) is >= TerrainPlacementRules.HillMinimumElevation and < SeededMap.MountainElevationThreshold)
                    Assert.True(map.IsHillAt(near), $"Land at {near} beside a mountain is not part of its hill base.");
        output.WriteLine($"mountains {mountains.Length}, hills {hills.Length}");
    }

    [Fact]
    public void HillBandFollowsTheWrapSettingAtTheSeam()
    {
        const int width = 12;
        const int height = 5;
        var elevation = Enumerable.Repeat((byte)200, width * height).ToArray();
        var hydrology = new byte[width * height];
        elevation[2 * width] = 230;
        hydrology[4 * width + 2] = (byte)WaterKind.Lake;

        var wrapped = TerrainPlacementRules.ClassifyHills(elevation, hydrology, width, height, wrapEastWest: true);
        var flat = TerrainPlacementRules.ClassifyHills(elevation, hydrology, width, height, wrapEastWest: false);

        Assert.False(wrapped[2 * width]);
        Assert.True(wrapped[2 * width + 3]);
        Assert.False(wrapped[2 * width + 4]);
        Assert.True(wrapped[2 * width + width - 1]);
        Assert.True(wrapped[2 * width + width - 3]);
        Assert.False(wrapped[2 * width + width - 4]);
        Assert.False(flat[2 * width + width - 1]);
        Assert.True(flat[2 * width + 3]);
        // Water and low ground are never hills, even beside a mountain.
        Assert.False(wrapped[4 * width + 2]);
        elevation[width + 1] = TerrainPlacementRules.HillMinimumElevation - 1;
        Assert.False(TerrainPlacementRules.ClassifyHills(elevation, hydrology, width, height, true)[width + 1]);
        // Maps without elevation and water layers have no hills at all.
        var fixture = SeededMapGenerator.Generate("terrain-fixture");
        Assert.All(fixture.Tiles, tile => Assert.False(fixture.IsHillAt(tile.Position)));
    }

    [Fact]
    public void NewWorldReplaysTheSameTerrainAndKeepsItThroughSaveAndRestore()
    {
        var options = Current("terrain-replay", WorldSizePreset.Small);
        var first = GeneratedCampMapGenerator.Generate(options);
        var again = GeneratedCampMapGenerator.Generate(options);
        Assert.Equal(first.ManifestDigest, again.ManifestDigest);
        Assert.Equal(MapLayerManifestCodec.Digest(first), MapLayerManifestCodec.Digest(again));
        Assert.Equal(first.Resources, again.Resources);
        Assert.NotEqual(first.SurfaceKinds, GeneratedCampMapGenerator.Generate(options with { Seed = "terrain-replay-2" }).SurfaceKinds);

        using var world = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: options);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        var map = restored.ExportState().Map;
        Assert.Equal(first.ManifestDigest, map.ManifestDigest);
        Assert.Equal(MapLayerManifestCodec.Digest(first), MapLayerManifestCodec.Digest(map));
        Assert.DoesNotContain(map.Resources, resource => TerrainPlacementRules.IsOrdinaryVegetation(resource) &&
            map.SurfaceAt(resource.Position) == SurfaceKind.Sand);
    }

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

    private static bool Beside(SeededMap map, GridPoint point, WaterKind water) =>
        new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }.Any(step =>
        {
            var x = point.X + step.Item1;
            if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
            var near = new GridPoint(x, point.Y + step.Item2);
            return map.Contains(near) && map.HydrologyAt(near) == water;
        });

    private static IEnumerable<GridPoint> Around(SeededMap map, GridPoint point)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var x = point.X + dx;
                if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
                var near = new GridPoint(x, point.Y + dy);
                if (map.Contains(near)) yield return near;
            }
    }

    private sealed class ForestMeasure
    {
        public int FloorTiles { get; private set; }
        public int FloorOccupied { get; private set; }
        public int FloorTrees { get; private set; }
        public int GrassTiles { get; private set; }
        public int GrassTrees { get; private set; }

        public void Add(bool forestFloor, MapResource? standing)
        {
            if (forestFloor)
            {
                FloorTiles++;
                if (standing is not null && TerrainPlacementRules.IsOrdinaryVegetation(standing)) FloorOccupied++;
                if (standing?.TreeKind is not null) FloorTrees++;
                return;
            }
            GrassTiles++;
            if (standing?.TreeKind is not null) GrassTrees++;
        }

        public override string ToString() =>
            $"forest floor {FloorOccupied}/{FloorTiles} holding a tree or plant ({FloorTrees} trees); " +
            $"forest grass {GrassTrees}/{GrassTiles} with a tree";
    }
}
