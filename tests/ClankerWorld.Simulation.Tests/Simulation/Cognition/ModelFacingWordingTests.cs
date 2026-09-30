using System.Collections.Concurrent;
using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Finds words the game no longer uses for its agents and places. A whole-word
/// match keeps legitimate words such as "campfire" from tripping a check.
/// </summary>
internal static class RetiredWording
{
    private static readonly string[] Words = ["inhabitant", "settlement", "camp"];

    public static string? Find(string text)
    {
        foreach (var word in Words)
        {
            for (var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); index >= 0;
                 index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase))
            {
                var end = index + word.Length;
                var startsWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                var endsWord = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (startsWord && endsWord)
                {
                    return word;
                }
            }
        }

        return null;
    }
}

public sealed class ModelFacingWordingTests
{
    [Fact]
    public void RetiredWordingFinderMatchesWholeWordsOnly()
    {
        Assert.Equal("inhabitant", RetiredWording.Find("one inhabitant of a town"));
        Assert.Equal("inhabitant", RetiredWording.Find("{\"inhabitant_id\":\"a\"}"));
        Assert.Equal("settlement", RetiredWording.Find("A settlement."));
        Assert.Equal("camp", RetiredWording.Find("household:camp-alpha"));
        Assert.Null(RetiredWording.Find("The private campfire promise."));
        Assert.Null(RetiredWording.Find("An agent living in a Town."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfferedChoiceDescriptionsUseCurrentWording(bool withRoles)
    {
        var recorder = new DescriptionRecorder();
        using var seed = new PrivateWorldRuntime("wording-sweep", _ => recorder, startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < positions.Length; index++)
        {
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), positions[index]);
        }

        seed.StartWorld();
        seed.StageStarterContent();
        for (var tick = 0; tick < 9; tick++)
        {
            _ = await seed.AdvanceOneTickAsync();
        }

        PrivateWorldRuntime world = seed;
        PrivateWorldRuntime? restored = null;
        if (withRoles)
        {
            // Roles unlock building, recipes, hauling and lessons, which a normal game does not offer yet.
            var state = seed.ExportState();
            var society = state.Society.Society;
            var adults = society.Inhabitants.Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
            society = SocietyFixture.AssignRole(society, adults[0], SocietyWorkRole.Farmer).Checkpoint;
            society = SocietyFixture.AssignRole(society, adults[1], SocietyWorkRole.Builder).Checkpoint;
            society = SocietyFixture.AssignRole(society, adults[2], SocietyWorkRole.Trader).Checkpoint;
            society = SocietyFixture.AssignRole(society, adults[3], SocietyWorkRole.Teacher).Checkpoint;
            state = state with { Society = state.Society with { Society = society } };
            restored = PrivateWorldRuntime.Restore(
                PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => recorder);
            world = restored;
        }

        try
        {
            for (var tick = 0; tick < 1_800; tick++)
            {
                if (!(await world.AdvanceOneTickAsync()).Advanced)
                {
                    break;
                }
            }
        }
        finally
        {
            restored?.Dispose();
        }

        // A sweep that saw almost nothing would pass without proving anything.
        Assert.True(recorder.Kinds.Count >= 6, $"Only {recorder.Kinds.Count} kinds of choice were offered.");
        foreach (var description in recorder.Descriptions.Keys)
        {
            Assert.True(RetiredWording.Find(description) is null, $"Retired wording in an offered choice: {description}");
        }
    }

    private sealed class DescriptionRecorder : IDecisionProvider
    {
        public ConcurrentDictionary<string, byte> Descriptions { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, byte> Kinds { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
            {
                Descriptions[candidate.Description] = 0;
                Kinds[candidate.Id.Split(':')[0]] = 0;
            }

            return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }
}
