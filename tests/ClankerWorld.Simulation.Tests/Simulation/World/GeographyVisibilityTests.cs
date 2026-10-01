using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class GeographyVisibilityTests
{
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
