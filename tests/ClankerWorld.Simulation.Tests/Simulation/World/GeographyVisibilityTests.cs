using System.Diagnostics;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class GeographyVisibilityTests(ITestOutputHelper output)
{
    [Fact]
    public void BalancedSmallAndMediumUseThreeDeterministicCandidatesAndReportCoverage()
    {
        var scenarios = new[]
        {
            new GeographyOptions("issue-409-small-wrapped", WorldSizePreset.Small, WrapEastWest: true),
            new GeographyOptions("issue-409-small-bounded", WorldSizePreset.Small, WrapEastWest: false),
            new GeographyOptions("issue-409-medium-wrapped", WorldSizePreset.Medium, WrapEastWest: true),
            new GeographyOptions("issue-409-medium-bounded", WorldSizePreset.Medium, WrapEastWest: false),
        };
        using var process = Process.GetCurrentProcess();
        var peakBefore = process.PeakWorkingSet64;
        var stopwatch = Stopwatch.StartNew();
        var selections = new List<(GeographyOptions Options, GeographyCandidateSelection Selection)>();

        foreach (var options in scenarios)
        {
            var selection = GeographyCandidateSelector.Select(options);
            Assert.Equal(3, selection.Candidates.Count);
            Assert.Equal(0, selection.Candidates[0].Attempt);
            Assert.Equal(1, selection.Candidates[1].Attempt);
            Assert.Equal(2, selection.Candidates[2].Attempt);
            Assert.All(selection.Candidates, candidate =>
            {
                Assert.True(candidate.DryLandTiles > 0);
                Assert.InRange(candidate.ForestPercent, 0, 100);
                Assert.InRange(candidate.MountainPercent, 0, 100);
                Assert.True(candidate.ForestTargetApplicable);
                Assert.True(candidate.MountainTargetApplicable);
            });
            Assert.Equal(selection.Selected.Attempt, selection.Options.CandidateAttempt);
            selections.Add((options, selection));
        }

        stopwatch.Stop();
        process.Refresh();
        var peakDeltaMiB = Math.Max(0, process.PeakWorkingSet64 - peakBefore) / 1024d / 1024d;
        foreach (var (options, selection) in selections)
        {
            var report = selection.Selected;
            Assert.True(report.MeetsTargets,
                $"{options.Size} wrap={options.WrapEastWest} selected candidate {report.Attempt} " +
                $"missed {string.Join(", ", report.UnmetTargets)}.");
            output.WriteLine($"{options.Size} wrap={options.WrapEastWest} attempt={report.Attempt} " +
                $"forest={report.ForestPercent:F2}% regions={report.ForestRegionCount} " +
                $"mountain={report.MountainPercent:F2}% regions={report.MountainRegionCount} " +
                $"candidates={selection.Candidates.Count}");
            foreach (var candidate in selection.Candidates)
                output.WriteLine($"  candidate={candidate.Attempt} forest={candidate.ForestPercent:F2}% " +
                    $"mountain={candidate.MountainPercent:F2}% meets={candidate.MeetsTargets}");

            var recreated = GeographyCandidateSelector.GenerateCandidate(
                options with { CandidateAttempt = report.Attempt });
            Assert.Equal(selection.Map.ManifestDigest, recreated.ManifestDigest);
            Assert.Equal(selection.Map.GenerationAttempt, recreated.GenerationAttempt);
        }
        output.WriteLine($"fixed-matrix scenarios={scenarios.Length} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F0} " +
            $"process_peak_working_set_delta_mib={peakDeltaMiB:F1}");
    }

    [Fact]
    public void SelectedCandidateAttemptSurvivesCreateSaveAndRestoreExactly()
    {
        var options = new GeographyOptions("issue-409-medium-wrapped", WorldSizePreset.Medium);
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
    public void UnsupportedBalancedVisibilityRevisionIsRefused()
    {
        var options = new GeographyOptions("old-balanced-visibility", WorldSizePreset.Small,
            BalancedVisibilityVersion: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(options));
    }

    [Fact]
    public void LowAndHighControlsAreReportedWithoutTheirNormalTargets()
    {
        var options = new GeographyOptions("issue-409-control-scope", WorldSizePreset.Small,
            ForestCover: GenerationAmount.High, MountainRelief: GenerationAmount.Low);

        var selection = GeographyCandidateSelector.Select(options);
        var report = Assert.Single(selection.Candidates);

        Assert.False(report.ForestTargetApplicable);
        Assert.False(report.MountainTargetApplicable);
        Assert.False(report.TargetsApplicable);
        Assert.Empty(report.UnmetTargets);
        Assert.True(report.DryLandTiles > 0);
        Assert.InRange(report.ForestPercent, 0, 100);
        Assert.InRange(report.MountainPercent, 0, 100);
    }

    [Theory]
    [InlineData(ClimateMode.Uniform, ClimateZone.Dry)]
    [InlineData(ClimateMode.Dominant, ClimateZone.Polar)]
    public void IncompatibleClimateChoicesReportCoverageWithoutApplyingTrialTargets(
        ClimateMode mode, ClimateZone climate)
    {
        var selection = GeographyCandidateSelector.Select(new GeographyOptions(
            $"issue-409-{mode}-{climate}", WorldSizePreset.Small,
            ClimateMode: mode, SelectedClimate: climate));

        var report = Assert.Single(selection.Candidates);
        Assert.False(report.TargetsApplicable);
        Assert.False(report.ForestTargetApplicable);
        Assert.False(report.MountainTargetApplicable);
        Assert.True(report.DryLandTiles > 0);
    }

    [Fact]
    public void ConnectedRegionsWrapHorizontallyButDoNotWrapAcrossPolarRows()
    {
        var tiles = Enumerable.Range(0, 9)
            .Select(index => new TerrainTile(new GridPoint(index % 3, index / 3), TerrainKind.Meadow))
            .ToArray();
        tiles[0] = tiles[0] with { Terrain = TerrainKind.Forest };
        tiles[2] = tiles[2] with { Terrain = TerrainKind.Forest };
        tiles[7] = tiles[7] with { Terrain = TerrainKind.Forest };
        var wrapped = new SeededMap(3, 3, 0, tiles, [], [], string.Empty) { WrapsEastWest = true };
        var bounded = wrapped with { WrapsEastWest = false };
        var options = new GeographyOptions("region-seam", WorldSizePreset.Small);

        Assert.Equal(2, GeographyCandidateSelector.Measure(options, wrapped).ForestRegionCount);
        Assert.Equal(3, GeographyCandidateSelector.Measure(options, bounded).ForestRegionCount);
    }
}
