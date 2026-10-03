using System.Globalization;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Harness;

/// <summary>A generated map has no valid place for its first Town.</summary>
public sealed class GeographyClearingUnavailableException : InvalidOperationException
{
    public GeographyClearingUnavailableException()
        : base("The generated geography has no suitable base-camp clearing.") { }
}

/// <summary>An attempted map that could not provide a playable clearing.</summary>
public sealed record GeographyCandidateFailure(int Attempt, string Reason);

/// <summary>Measured visibility and connected regions for one deterministic candidate.</summary>
public sealed record GeographyCandidateReport(
    int Attempt,
    int DryLandTiles,
    int ForestTiles,
    int MountainTiles,
    double ForestPercent,
    double MountainPercent,
    int ForestRegionCount,
    int LargestForestRegion,
    int MountainRegionCount,
    int LargestMountainRegion,
    bool ForestTargetApplicable,
    bool MountainTargetApplicable,
    bool ForestTargetMet,
    bool MountainTargetMet)
{
    public bool TargetsApplicable => ForestTargetApplicable || MountainTargetApplicable;

    public bool MeetsTargets =>
        (!ForestTargetApplicable || ForestTargetMet) &&
        (!MountainTargetApplicable || MountainTargetMet);

    public IReadOnlyList<string> UnmetTargets
    {
        get
        {
            var unmet = new List<string>(2);
            if (ForestTargetApplicable && !ForestTargetMet)
                unmet.Add($"Forest {ForestPercent.ToString("F1", CultureInfo.InvariantCulture)}% (target 20–40%)");
            if (MountainTargetApplicable && !MountainTargetMet)
                unmet.Add($"Mountains {MountainPercent.ToString("F1", CultureInfo.InvariantCulture)}% (target 5–12%)");
            return unmet;
        }
    }

    internal int MissedTargetCount =>
        (ForestTargetApplicable && !ForestTargetMet ? 1 : 0) +
        (MountainTargetApplicable && !MountainTargetMet ? 1 : 0);

    internal double TargetMissDistance =>
        (ForestTargetApplicable ? DistanceOutside(ForestPercent, 20, 40) / 20 : 0) +
        (MountainTargetApplicable ? DistanceOutside(MountainPercent, 5, 12) / 7 : 0);

    // A tie-break only. Mountains are already shaped into whole massifs, so
    // only forest connectedness counts; preferring one large mountain region
    // would always pick the world with the fewest massifs.
    internal double RegionCohesion =>
        ForestTiles == 0 ? 0 : (double)LargestForestRegion / ForestTiles;

    private static double DistanceOutside(double value, double minimum, double maximum) =>
        value < minimum ? minimum - value : value > maximum ? value - maximum : 0;
}

/// <summary>The selected map and all candidates measured for its preview.</summary>
public sealed record GeographyCandidateSelection(
    GeographyOptions Options,
    SeededMap Map,
    IReadOnlyList<GeographyCandidateReport> Candidates)
{
    public GeographyCandidateReport Selected => Candidates.Single(candidate => candidate.Attempt == Map.GenerationAttempt);
    public IReadOnlyList<GeographyCandidateFailure> FailedCandidates { get; init; } = [];
}

/// <summary>
/// Tries a small, fixed number of seed-derived maps for the trial Balanced
/// Small/Medium visibility targets. Terrain classification remains owned by
/// <see cref="GeneratedCampMapGenerator"/>.
/// </summary>
public static class GeographyCandidateSelector
{
    public const double MinimumForestPercent = 20;
    public const double MaximumForestPercent = 40;
    public const double MinimumMountainPercent = 5;
    public const double MaximumMountainPercent = 12;

    public static GeographyCandidateSelection Select(GeographyOptions options) =>
        Select(options, static candidateOptions => GenerateCandidate(candidateOptions));

    internal static GeographyCandidateSelection Select(GeographyOptions options,
        Func<GeographyOptions, SeededMap> generateCandidate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(generateCandidate);
        if (options.Size is not (WorldSizePreset.Small or WorldSizePreset.Medium))
            throw new ArgumentException("Coverage selection supports playable Small and Medium worlds only.", nameof(options));

        var (forestTargetApplicable, mountainTargetApplicable) = ApplicableTargets(options);
        var attemptCount = forestTargetApplicable || mountainTargetApplicable
            ? GeographyGenerator.MaximumCandidateAttempts
            : 1;
        var reports = new List<GeographyCandidateReport>(attemptCount);
        var failures = new List<GeographyCandidateFailure>(attemptCount);
        SeededMap? selectedMap = null;
        GeographyCandidateReport? selectedReport = null;
        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var candidateOptions = options with { CandidateAttempt = attempt };
            SeededMap map;
            try { map = generateCandidate(candidateOptions); }
            catch (GeographyClearingUnavailableException)
            {
                failures.Add(new GeographyCandidateFailure(attempt, "no-clearing"));
                continue;
            }
            var report = Measure(candidateOptions, map, forestTargetApplicable, mountainTargetApplicable);
            reports.Add(report);
            if (selectedReport is null || Compare(report, selectedReport) < 0)
            {
                selectedMap = map;
                selectedReport = report;
            }
        }

        if (selectedReport is null || selectedMap is null)
            throw new GeographyClearingUnavailableException();
        return new GeographyCandidateSelection(options with { CandidateAttempt = selectedReport.Attempt },
            selectedMap, reports)
        { FailedCandidates = failures };
    }

    /// <summary>Recreates one saved candidate without searching or changing its identity.</summary>
    public static SeededMap GenerateCandidate(GeographyOptions options, bool includeLegacyBedroll = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.CandidateAttempt is < 0 or >= GeographyGenerator.MaximumCandidateAttempts)
            throw new ArgumentOutOfRangeException(nameof(options), "The selected geography candidate is invalid.");

        var map = GeneratedCampMapGenerator.Generate(options, includeLegacyBedroll);
        var withAttempt = map with { GenerationAttempt = options.CandidateAttempt, ManifestDigest = string.Empty };
        return withAttempt with { ManifestDigest = MapManifestCodec.Digest(withAttempt) };
    }

    public static GeographyCandidateReport Measure(GeographyOptions options, SeededMap map)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(map);
        var (forestTargetApplicable, mountainTargetApplicable) = ApplicableTargets(options);
        return Measure(options, map, forestTargetApplicable, mountainTargetApplicable);
    }

    private static GeographyCandidateReport Measure(GeographyOptions options, SeededMap map,
        bool forestTargetApplicable, bool mountainTargetApplicable)
    {
        var dryLandTiles = 0;
        var forestTiles = 0;
        var mountainTiles = 0;
        var forest = new bool[checked(map.Width * map.Height)];
        var mountain = new bool[forest.Length];
        foreach (var tile in map.Tiles)
        {
            var index = checked(tile.Position.Y * map.Width + tile.Position.X);
            if (tile.Terrain is TerrainKind.Ocean or TerrainKind.Lake or TerrainKind.River) continue;
            dryLandTiles++;
            if (tile.Terrain == TerrainKind.Forest)
            {
                forest[index] = true;
                forestTiles++;
            }
            else if (tile.Terrain is TerrainKind.Mountain or TerrainKind.Peak)
            {
                mountain[index] = true;
                mountainTiles++;
            }
        }

        var forestRegions = CountRegions(forest, map.Width, map.Height, map.WrapsEastWest);
        var mountainRegions = CountRegions(mountain, map.Width, map.Height, map.WrapsEastWest);
        var forestPercent = dryLandTiles == 0 ? 0 : 100d * forestTiles / dryLandTiles;
        var mountainPercent = dryLandTiles == 0 ? 0 : 100d * mountainTiles / dryLandTiles;
        return new GeographyCandidateReport(map.GenerationAttempt, dryLandTiles, forestTiles, mountainTiles,
            forestPercent, mountainPercent, forestRegions.Count, forestRegions.Largest,
            mountainRegions.Count, mountainRegions.Largest,
            forestTargetApplicable, mountainTargetApplicable,
            !forestTargetApplicable || forestPercent is >= MinimumForestPercent and <= MaximumForestPercent,
            !mountainTargetApplicable || mountainPercent is >= MinimumMountainPercent and <= MaximumMountainPercent);
    }

    private static (bool Forest, bool Mountain) ApplicableTargets(GeographyOptions options)
    {
        if (options.Size is not (WorldSizePreset.Small or WorldSizePreset.Medium) ||
            options.ClimateMode != ClimateMode.Balanced)
            return (false, false);

        // The trial targets describe the Normal settings. Low/High remain real
        // player choices and are not forced back into the Normal coverage band.
        return (options.ForestCover == GenerationAmount.Normal,
            options.MountainRelief == GenerationAmount.Normal);
    }

    private static int Compare(GeographyCandidateReport left, GeographyCandidateReport right)
    {
        var compared = left.MissedTargetCount.CompareTo(right.MissedTargetCount);
        if (compared != 0) return compared;
        compared = left.TargetMissDistance.CompareTo(right.TargetMissDistance);
        if (compared != 0) return compared;
        compared = right.RegionCohesion.CompareTo(left.RegionCohesion);
        return compared != 0 ? compared : left.Attempt.CompareTo(right.Attempt);
    }

    private static (int Count, int Largest) CountRegions(bool[] feature, int width, int height, bool wrap)
    {
        var visited = new bool[feature.Length];
        var queue = new int[feature.Length];
        var count = 0;
        var largest = 0;
        for (var start = 0; start < feature.Length; start++)
        {
            if (!feature[start] || visited[start]) continue;
            count++;
            var head = 0;
            var tail = 0;
            var size = 0;
            visited[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                var index = queue[head++];
                size++;
                var x = index % width;
                var y = index / width;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0) continue;
                        var nextY = y + offsetY;
                        if (nextY < 0 || nextY >= height) continue;
                        var nextX = x + offsetX;
                        if (wrap)
                            nextX = (nextX + width) % width;
                        else if (nextX < 0 || nextX >= width)
                            continue;
                        var next = nextY * width + nextX;
                        if (!feature[next] || visited[next]) continue;
                        visited[next] = true;
                        queue[tail++] = next;
                    }
            }
            largest = Math.Max(largest, size);
        }
        return (count, largest);
    }
}
