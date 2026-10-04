using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Issue #683: mountains generate as a few large massifs with foothills. The
/// massif counts, minimum size, hill reach and stone reach are provisional
/// until the owner reviews generated maps; these tests check the rules hold.
/// </summary>
public sealed class MountainMassifTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(WorldSizePreset.Small, true)]
    [InlineData(WorldSizePreset.Small, false)]
    [InlineData(WorldSizePreset.Medium, true)]
    [InlineData(WorldSizePreset.Medium, false)]
    public void BalancedWorldsHaveAFewWholeMassifsWithinTheMountainTarget(WorldSizePreset size, bool wrap)
    {
        var (minimum, maximum) = TerrainPlacementRules.MassifCount(size);
        var counts = new HashSet<int>();
        foreach (var seed in Seeds(size, "massif-count"))
        {
            // Every candidate New World may pick, not only the selected one.
            var selection = GeographyCandidateSelector.Select(Balanced(seed, size, wrap));
            foreach (var candidate in selection.Candidates)
            {
                output.WriteLine($"{seed} attempt {candidate.Attempt}: {candidate.MountainRegionCount} massifs, " +
                    $"{candidate.MountainPercent:F1}% mountains, largest {candidate.LargestMountainRegion} tiles");
                Assert.InRange(candidate.MountainRegionCount, minimum, maximum);
                Assert.True(candidate.MountainTargetApplicable);
                Assert.InRange(candidate.MountainPercent, GeographyCandidateSelector.MinimumMountainPercent,
                    GeographyCandidateSelector.MaximumMountainPercent);
                counts.Add(candidate.MountainRegionCount);
            }
            Assert.All(MountainRegions(selection.Map), region =>
                Assert.True(region.Tiles.Count >= TerrainPlacementRules.MinimumMassifTiles,
                    $"{seed}: a {region.Tiles.Count}-tile mountain patch is below the minimum massif size."));
        }
        // The count varies between worlds rather than always taking one end.
        Assert.True(counts.Count > 1, $"Only {string.Join(", ", counts)} massifs were seen.");
    }

    [Theory]
    [InlineData(WorldSizePreset.Small, true)]
    [InlineData(WorldSizePreset.Small, false)]
    [InlineData(WorldSizePreset.Medium, true)]
    [InlineData(WorldSizePreset.Medium, false)]
    public void PeaksRunAlongEachMassifsCrestInsideAMountainRim(WorldSizePreset size, bool wrap)
    {
        foreach (var seed in Seeds(size, "massif-crest"))
        {
            var map = GeneratedCampMapGenerator.Generate(Balanced(seed, size, wrap));
            foreach (var region in MountainRegions(map))
            {
                var peaks = region.Tiles.Where(tile => map.ElevationLevels![Index(map, tile)] >=
                    SeededMap.PeakElevationThreshold).ToArray();
                Assert.NotEmpty(peaks);
                // A peak is never at the map's edge, beside water or beside
                // lowland: a walkable mountain rim surrounds the crest.
                Assert.All(peaks, peak =>
                {
                    Assert.InRange(peak.Y, 1, map.Height - 2);
                    if (!wrap) Assert.InRange(peak.X, 1, map.Width - 2);
                    foreach (var near in EightNeighbours(map, peak))
                        Assert.True(IsMountain(map, near), $"{seed}: peak {peak} touches non-mountain {near}.");
                });

                // The crest is a long, thin line along the massif's own axis.
                var massifAxis = PrincipalAxis(region.Unwrapped);
                var positions = region.Tiles.Select((tile, index) => (tile, index))
                    .ToDictionary(item => item.tile, item => region.Unwrapped[item.index]);
                var crestAxis = PrincipalAxis(peaks.Select(peak => positions[peak]).ToArray());
                var angle = Math.Acos(Math.Min(1, Math.Abs(massifAxis.X * crestAxis.X + massifAxis.Y * crestAxis.Y))) * 180 / Math.PI;
                output.WriteLine($"{seed}: massif {region.Tiles.Count} tiles, elongation {massifAxis.Elongation:F1}; " +
                    $"crest {peaks.Length} peaks, elongation {crestAxis.Elongation:F1}, {angle:F0}° off the axis");
                Assert.True(massifAxis.Elongation >= 1.5, $"{seed}: the massif reads as a round blob.");
                if (peaks.Length < 12) continue;
                Assert.True(crestAxis.Elongation >= 3, $"{seed}: the peaks do not form a crest.");
                Assert.True(angle <= 30, $"{seed}: the crest runs {angle:F0}° off the massif's axis.");
            }
        }
    }

    [Theory]
    [InlineData(WorldSizePreset.Small, true)]
    [InlineData(WorldSizePreset.Small, false)]
    [InlineData(WorldSizePreset.Medium, true)]
    [InlineData(WorldSizePreset.Medium, false)]
    public void PeaksNeverWallLandOffAndTheStartCanWalkToStoneAndOre(WorldSizePreset size, bool wrap)
    {
        foreach (var seed in Seeds(size, "massif-reach"))
        {
            var map = GeneratedCampMapGenerator.Generate(Balanced(seed, size, wrap));
            // Within every piece of land, the ground that is not a peak is one
            // walkable piece: peaks never cut any of it off, including the
            // first Town's clearing.
            var land = Label(map, point => map.HydrologyAt(point) == WaterKind.Land);
            var open = Label(map, point => map.HydrologyAt(point) == WaterKind.Land &&
                map.ElevationAt(point) < SeededMap.PeakElevationThreshold);
            var openPieces = new Dictionary<int, int>();
            for (var index = 0; index < open.Length; index++)
            {
                if (open[index] < 0 || openPieces.TryAdd(land[index], open[index])) continue;
                Assert.True(openPieces[land[index]] == open[index],
                    $"{seed}: peaks split the land at {index % map.Width},{index / map.Width}.");
            }

            var start = map.GetResource("berry-patch").Position;
            Assert.Contains(map.Resources, resource => resource.Kind == "stone" &&
                map.FootDistance(start, resource.Position) <= TerrainPlacementRules.FirstTownStoneReach + 1 &&
                map.IsReachableFromCampOnFoot(resource.Position));
            foreach (var deposit in new[] { "stone_outcrop", "iron_outcrop", "gold_outcrop", "diamond_outcrop" })
                Assert.Contains(map.Resources, resource => resource.NaturalObjectKind == deposit);
            Assert.All(map.Resources.Where(resource => resource.NaturalObjectKind is "iron_outcrop" or "gold_outcrop" or "diamond_outcrop"),
                resource => Assert.True(IsMountain(map, resource.Position)));
        }
    }

    [Fact]
    public void HillsWidenWithTheMassifAndTheClientDrawsTheSameBand()
    {
        for (var tiles = 0; tiles <= 6_000; tiles++)
            Assert.Equal(TerrainPlacementRules.HillReach(tiles), HillBand.Reach(tiles));
        Assert.Equal(TerrainPlacementRules.MinimumHillReach, TerrainPlacementRules.HillReach(1));
        Assert.Equal(TerrainPlacementRules.MaximumHillReach, TerrainPlacementRules.HillReach(5_000));

        // A small and a large mountain on high flat ground: the large one's
        // hill band reaches further.
        const int width = 120;
        const int height = 80;
        var elevation = Enumerable.Repeat((byte)200, width * height).ToArray();
        var water = new byte[width * height];
        for (var y = 38; y < 41; y++)
            for (var x = 10; x < 13; x++)
                elevation[y * width + x] = 230;
        for (var y = 10; y < 70; y++)
            for (var x = 50; x < 110; x++)
                elevation[y * width + x] = 230;
        var hills = TerrainPlacementRules.ClassifyHills(elevation, water, width, height, wrapEastWest: false);
        Assert.Equal(hills, HillBand.Classify(elevation, water, width, height, wrapsEastWest: false));
        Assert.Equal(3, Enumerable.Range(0, 10).Count(x => hills[39 * width + x]));
        Assert.True(hills[39 * width + 15] && !hills[39 * width + 16]);
        var largeReach = TerrainPlacementRules.HillReach(60 * 60);
        Assert.Equal(TerrainPlacementRules.MaximumHillReach, largeReach);
        Assert.True(hills[39 * width + 50 - largeReach] && !hills[39 * width + 50 - largeReach - 1]);

        // On generated maps the client's copy marks exactly the same tiles,
        // and every dry tile within a massif's reach has risen into a hill.
        foreach (var (size, wrap) in new[] { (WorldSizePreset.Small, true), (WorldSizePreset.Small, false),
                     (WorldSizePreset.Medium, true), (WorldSizePreset.Medium, false) })
        {
            var map = GeneratedCampMapGenerator.Generate(Balanced($"massif-hills-{size}-{wrap}", size, wrap));
            var simulation = TerrainPlacementRules.ClassifyHills(map.ElevationLevels!, map.HydrologyKinds!,
                map.Width, map.Height, map.WrapsEastWest);
            Assert.Equal(simulation, HillBand.Classify(map.ElevationLevels!, map.HydrologyKinds!,
                map.Width, map.Height, map.WrapsEastWest));
            foreach (var region in MountainRegions(map))
            {
                var reach = TerrainPlacementRules.HillReach(region.Tiles.Count);
                var distance = new Dictionary<GridPoint, int>();
                var queue = new Queue<GridPoint>();
                foreach (var tile in region.Tiles)
                {
                    distance[tile] = 0;
                    queue.Enqueue(tile);
                }
                while (queue.TryDequeue(out var current))
                {
                    if (distance[current] == reach) continue;
                    foreach (var near in EightNeighbours(map, current))
                        if (distance.TryAdd(near, distance[current] + 1)) queue.Enqueue(near);
                }
                Assert.All(distance.Where(item => item.Value > 0 && map.HydrologyAt(item.Key) == WaterKind.Land &&
                        !IsMountain(map, item.Key)),
                    item => Assert.True(simulation[Index(map, item.Key)],
                        $"{size}: {item.Key}, {item.Value} tiles from a {region.Tiles.Count}-tile massif, is not a hill."));
            }
        }
    }

    [Fact]
    public void GenerationStaysTheSameForASeedAndDiffersBetweenSeeds()
    {
        foreach (var wrap in new[] { true, false })
        {
            var options = Balanced("massif-replay", WorldSizePreset.Small, wrap);
            var first = GeographyGenerator.Generate(options);
            var again = GeographyGenerator.Generate(options);
            var other = GeographyGenerator.Generate(options with { Seed = "massif-replay-2" });
            var same = true;
            var different = false;
            for (var y = 0; y < first.Height; y++)
                for (var x = 0; x < first.Width; x++)
                {
                    same &= first.At(x, y) == again.At(x, y) && first.DownstreamAt(x, y) == again.DownstreamAt(x, y);
                    different |= first.At(x, y).Elevation != other.At(x, y).Elevation;
                }
            Assert.True(same);
            Assert.True(different);
            Assert.Equal(GeneratedCampMapGenerator.Generate(options).ManifestDigest,
                GeneratedCampMapGenerator.Generate(options).ManifestDigest);
        }
    }

    [Fact]
    public void AWorldFromTheOlderTerrainGeneratorIsRefusedAndItsSaveKept()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(
            new GeographyOptions("older-terrain", WorldSizePreset.Small, BalancedVisibilityVersion: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(
            new GeographyOptions("older-terrain", WorldSizePreset.Small, ClimateMode: ClimateMode.Uniform,
                BalancedVisibilityVersion: 1)));

        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-massifs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var geography = new GeographyOptions("older-terrain", WorldSizePreset.Small,
                HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
            var file = new PrivateWorldStateFile(Path.Combine(directory, "world.json"),
                newWorldPace: WorldStartPace.FounderSetup, newWorldGeography: geography);
            using (var created = file.LoadOrCreate(geography.Seed))
                Assert.Equal(GeographyGenerator.CurrentBalancedVisibilityVersion,
                    created.ExportState().Geography!.BalancedVisibilityVersion);

            var document = JsonNode.Parse(File.ReadAllBytes(file.Path))!.AsObject();
            var saved = document["state"]!["geography"]!.AsObject();
            Assert.Equal(GeographyGenerator.CurrentBalancedVisibilityVersion, (int)saved["balancedVisibilityVersion"]!);
            saved["balancedVisibilityVersion"] = 1;
            var olderBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
            File.WriteAllBytes(file.Path, olderBytes);

            var error = Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(geography.Seed));
            Assert.Equal(GeographyGenerator.OlderTerrainVersionMessage, error.Message);
            Assert.Equal(olderBytes, File.ReadAllBytes(file.Path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record MountainRegion(List<GridPoint> Tiles, (double X, double Y)[] Unwrapped);

    private readonly record struct Axis(double X, double Y, double Elongation);

    private static GeographyOptions Balanced(string seed, WorldSizePreset size, bool wrap) =>
        new(seed, size, wrap, WaterPercent: 50, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);

    private static IEnumerable<string> Seeds(WorldSizePreset size, string prefix) =>
        Enumerable.Range(0, size == WorldSizePreset.Small ? 6 : 3).Select(index => $"{prefix}-{size}-{index}");

    private static int Index(SeededMap map, GridPoint point) => point.Y * map.Width + point.X;

    private static bool IsMountain(SeededMap map, GridPoint point) =>
        map.HydrologyAt(point) == WaterKind.Land && map.ElevationAt(point) >= SeededMap.MountainElevationThreshold;

    private static IEnumerable<GridPoint> EightNeighbours(SeededMap map, GridPoint point)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var near = map.WrapColumn(new GridPoint(point.X + dx, point.Y + dy));
                if (map.Contains(near)) yield return near;
            }
    }

    // Connected mountain regions, joined diagonally and across an enabled
    // seam, with coordinates unwrapped so a region crossing the seam stays whole.
    private static List<MountainRegion> MountainRegions(SeededMap map)
    {
        var regions = new List<MountainRegion>();
        var seen = new HashSet<GridPoint>();
        foreach (var tile in map.Tiles)
        {
            if (!IsMountain(map, tile.Position) || !seen.Add(tile.Position)) continue;
            var tiles = new List<GridPoint>();
            var unwrapped = new List<(double X, double Y)>();
            var queue = new Queue<(GridPoint Point, int X)>();
            queue.Enqueue((tile.Position, tile.Position.X));
            while (queue.TryDequeue(out var current))
            {
                tiles.Add(current.Point);
                unwrapped.Add((current.X, current.Point.Y));
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var near = map.WrapColumn(new GridPoint(current.Point.X + dx, current.Point.Y + dy));
                        if (!map.Contains(near) || !IsMountain(map, near) || !seen.Add(near)) continue;
                        queue.Enqueue((near, current.X + dx));
                    }
            }
            regions.Add(new MountainRegion(tiles, unwrapped.ToArray()));
        }
        return regions;
    }

    // The direction of greatest spread and how much longer than wide the
    // points are, from their covariance.
    private static Axis PrincipalAxis(IReadOnlyList<(double X, double Y)> points)
    {
        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);
        var xx = points.Average(point => (point.X - meanX) * (point.X - meanX)) + 1 / 12.0;
        var yy = points.Average(point => (point.Y - meanY) * (point.Y - meanY)) + 1 / 12.0;
        var xy = points.Average(point => (point.X - meanX) * (point.Y - meanY));
        var trace = xx + yy;
        var root = Math.Sqrt((xx - yy) * (xx - yy) / 4 + xy * xy);
        var major = trace / 2 + root;
        var minor = trace / 2 - root;
        var angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
        return new Axis(Math.Cos(angle), Math.Sin(angle), Math.Sqrt(major / minor));
    }

    private static int[] Label(SeededMap map, Func<GridPoint, bool> include)
    {
        var labels = Enumerable.Repeat(-1, map.Width * map.Height).ToArray();
        var next = 0;
        foreach (var tile in map.Tiles)
        {
            if (labels[Index(map, tile.Position)] >= 0 || !include(tile.Position)) continue;
            var queue = new Queue<GridPoint>();
            labels[Index(map, tile.Position)] = next;
            queue.Enqueue(tile.Position);
            while (queue.TryDequeue(out var current))
                foreach (var (dx, dy) in new (int X, int Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                {
                    var near = map.WrapColumn(new GridPoint(current.X + dx, current.Y + dy));
                    if (!map.Contains(near) || labels[Index(map, near)] >= 0 || !include(near)) continue;
                    labels[Index(map, near)] = next;
                    queue.Enqueue(near);
                }
            next++;
        }
        return labels;
    }
}
