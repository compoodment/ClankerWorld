using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class GeographyCandidateFailureTests
{
    [Theory]
    [InlineData("audit-town-2", 0, 2, 0, 1)]
    [InlineData("audit-town-13", 2, 0, 1, 2)]
    public async Task UnavailableClearingDoesNotDiscardTheBestPlayableCandidateOrItsSavedIdentity(
        string seed, int selectedAttempt, int unavailableAttempt, int firstAvailable, int lastAvailable)
    {
        var options = Options(seed);
        var attempted = new List<int>();
        // Massifs leave these seeds playable. Control only the unavailable
        // attempt; every surviving map still uses normal deterministic generation.
        SeededMap GenerateWithUnavailableClearing(GeographyOptions candidateOptions)
        {
            attempted.Add(candidateOptions.CandidateAttempt);
            if (candidateOptions.CandidateAttempt == unavailableAttempt)
                throw new GeographyClearingUnavailableException();
            return GeographyCandidateSelector.GenerateCandidate(candidateOptions);
        }
        var failure = Assert.Throws<GeographyClearingUnavailableException>(() =>
            GenerateWithUnavailableClearing(options with { CandidateAttempt = unavailableAttempt }));
        Assert.Equal("The generated geography has no suitable base-camp clearing.", failure.Message);
        var direct = GeographyCandidateSelector.GenerateCandidate(options with { CandidateAttempt = selectedAttempt });
        Assert.True(MapAcceptance.Validate(direct, allowEmptyCamp: true).IsValid);

        attempted.Clear();
        var selection = GeographyCandidateSelector.Select(options, GenerateWithUnavailableClearing);

        Assert.Equal(new[] { 0, 1, 2 }, attempted);
        Assert.Equal(selectedAttempt, selection.Selected.Attempt);
        Assert.Equal(new[] { firstAvailable, lastAvailable }, selection.Candidates.Select(candidate => candidate.Attempt));
        var unavailable = Assert.Single(selection.FailedCandidates);
        Assert.Equal(unavailableAttempt, unavailable.Attempt);
        Assert.Equal("no-clearing", unavailable.Reason);
        Assert.True(selection.Selected.ForestTargetApplicable);
        Assert.False(selection.Selected.MountainTargetApplicable);
        Assert.InRange(selection.Selected.ForestPercent, 20, 40);
        Assert.True(selection.Selected.MeetsTargets);
        Assert.Equal(options with { CandidateAttempt = selectedAttempt }, selection.Options);
        Assert.Equal(direct.ManifestDigest, selection.Map.ManifestDigest);
        Assert.Equal(MapLayerManifestCodec.Digest(direct), MapLayerManifestCodec.Digest(selection.Map));
        attempted.Clear();
        var repeated = GeographyCandidateSelector.Select(options, GenerateWithUnavailableClearing);
        Assert.Equal(new[] { 0, 1, 2 }, attempted);
        Assert.Equal(selection.Candidates, repeated.Candidates);
        Assert.Equal(selection.FailedCandidates, repeated.FailedCandidates);
        Assert.Equal(selection.Map.ManifestDigest, repeated.Map.ManifestDigest);

        using var created = PrivateWorldRuntime.CreateFromGeneratedGeography(seed, selection.Options,
            selection.Map, _ => new DeterministicDecisionProvider());
        created.InitializeFirstTownContent();
        created.AcceptFirstTownLayout(selection.Map.Resources.Single(item => item.Id == "berry-patch").Position);
        var founderSites = selection.Map.Tiles.Select(tile => tile.Position).Where(point =>
            selection.Map.IsBuildable(point) && created.Towns.Single().BorderTiles.Contains(point) &&
            !selection.Map.CampObjects.Any(item => item.Position == point) &&
            !selection.Map.Resources.Any(item => item.Position == point)).Take(4).ToArray();
        Assert.Equal(4, founderSites.Length);
        for (var index = 0; index < founderSites.Length; index++)
            created.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founderSites[index]);
        created.StartWorld();
        Assert.True((await created.AdvanceOneTickAsync()).Advanced);
        created.Validate();
        Assert.Equal(5, created.ExportState().WorldSimulation!.Buildings.Count);
        Assert.Equal(4, created.Inhabitants.Count);

        var bytes = PrivateWorldRuntimeCodec.Encode(created.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new DeterministicDecisionProvider());
        Assert.Equal(selectedAttempt, restored.ExportState().Map.GenerationAttempt);
        Assert.Equal(selection.Options, restored.ExportState().Geography);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SettingsWithNoPlayableCandidateStillRefuseGeneration(bool trialTargetsApplicable)
    {
        // Explicit generation failures exercise the full three-attempt bound
        // when a target applies, and the single attempt when neither does.
        var options = Options("controlled-no-clearing") with
        {
            ForestCover = trialTargetsApplicable ? GenerationAmount.Normal : GenerationAmount.High,
        };
        var attempted = new List<int>();
        SeededMap GenerateWithUnavailableClearing(GeographyOptions candidateOptions)
        {
            attempted.Add(candidateOptions.CandidateAttempt);
            throw new GeographyClearingUnavailableException();
        }
        var direct = Assert.Throws<GeographyClearingUnavailableException>(() =>
            GenerateWithUnavailableClearing(options));
        Assert.Equal("The generated geography has no suitable base-camp clearing.", direct.Message);

        attempted.Clear();
        var selected = Assert.Throws<GeographyClearingUnavailableException>(() =>
            GeographyCandidateSelector.Select(options, GenerateWithUnavailableClearing));
        Assert.Equal(direct.Message, selected.Message);
        Assert.Equal(trialTargetsApplicable ? new[] { 0, 1, 2 } : new[] { 0 }, attempted);
    }

    [Fact]
    public void APlayableNonwinningAttemptKeepsItsOwnIdentityAcrossStrictRestore()
    {
        // Attempt 1 is playable but misses the forest target. A saved map is
        // regenerated directly, even when another candidate would rank better.
        var options = Options("audit-town-0") with { CandidateAttempt = 1 };
        var map = GeographyCandidateSelector.GenerateCandidate(options);
        Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
        Assert.False(GeographyCandidateSelector.Measure(options, map).MeetsTargets);
        using var created = PrivateWorldRuntime.CreateFromGeneratedGeography(options.Seed, options, map);
        var bytes = PrivateWorldRuntimeCodec.Encode(created.ExportState());

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));

        Assert.Equal(1, restored.ExportState().Map.GenerationAttempt);
        Assert.Equal(options, restored.ExportState().Geography);
        Assert.Equal(map.ManifestDigest, restored.ExportState().Map.ManifestDigest);
        Assert.Equal(MapLayerManifestCodec.Digest(map), MapLayerManifestCodec.Digest(restored.ExportState().Map));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static GeographyOptions Options(string seed) => new(seed, WorldSizePreset.Small,
        WaterPercent: 80, LatitudeCooling: false, HydrologyVersion: 1,
        MountainRelief: GenerationAmount.High);
}
