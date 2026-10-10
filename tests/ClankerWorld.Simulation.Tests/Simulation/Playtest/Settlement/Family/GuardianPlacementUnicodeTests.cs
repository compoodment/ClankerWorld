using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class GuardianPlacementUnicodeTests
{
    private static readonly Lazy<Task<byte[]>> Escorting = new(CreateEscorting);

    [Theory]
    [InlineData(34, true, 256)]
    [InlineData(35, false, 255)]
    [InlineData(36, false, 256)]
    public async Task NativeGuardianContextKeepsWholeRenamedCharactersAndAcceptedCare(int namePrefix,
        bool includesEmoji, int noteLength)
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var child = scenario.Children[0];
        var state = PrivateWorldRuntimeCodec.Decode(await Escorting.Value);
        var placement = state.Inhabitants.Single(person => person.InhabitantId == child).GuardianPlacement!;
        var actor = placement.CaregiverId;
        // A controlled cold guardian interrupts its accepted escort through the
        // runtime's own warmth blocker. No derived note or blocker is injected.
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child
                ? person with { LastDecisionContext = null }
                : person.InhabitantId == actor ? person with
                {
                    LastDecisionContext = null,
                    Survival = person.Survival! with { WarmthBasisPoints = 0 },
                } : person).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Storm);
        var provider = new Capture(actor);
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        var name = new string('a', namePrefix) + "😀 Vale";
        Assert.True(world.RenameAgent(child, name));
        for (var tick = 0; tick < 6 && provider.Observations.IsEmpty; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var observation = Assert.Single(provider.Observations);
        observation.Validate();
        var note = Assert.IsType<string>(observation.Self!.HousingNote);
        Assert.Contains("The guardian is meeting urgent food or warmth needs.", note, StringComparison.Ordinal);
        var childStart = note.IndexOf(" Child: ", StringComparison.Ordinal) + 8;
        Assert.Equal(220, childStart); // Keep these rows at, before and after the actual cutoff.
        Assert.Equal(noteLength, note.Length);
        Assert.Equal(new string('a', namePrefix) + (includesEmoji ? "😀" : string.Empty), note[childStart..]);
        var wire = JsonSerializer.Deserialize<InhabitantObservation>(JsonSerializer.Serialize(observation))!;
        Assert.Equal(note, wire.Self!.HousingNote);
        _ = new UTF8Encoding(false, true).GetBytes(note);
        Assert.Equal(name, world.Society.GetInhabitant(child).Name);
        _ = new UTF8Encoding(false, true).GetBytes(name);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Capture(actor));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        var after = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        Assert.Equal(placement, after with { Blocker = placement.Blocker });
        Assert.Equal(placement.CaregiverId, world.Society.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Equal(state.Society.Society.GetInhabitant(child).HouseholdId, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
    }

    private static async Task<byte[]> CreateEscorting()
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        using var world = GuardianPlacementTestFixture.Restore(scenario.State,
            GuardianPlacementTestFixture.Accepting(scenario));
        await GuardianPlacementTestFixture.Accepted(world, scenario);
        await GuardianPlacementTestFixture.Until(world,
            () => GuardianPlacementTestFixture.Person(world, scenario.Children[0]).GuardianPlacement?.Stage == "escorting");
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private sealed class Capture(string actor) : IDecisionProvider
    {
        internal ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId == actor && request.Observation.Self?.HousingNote is not null)
                Observations.Enqueue(request.Observation);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, "safe_idle", 1,
                request.Observation.Candidates.ToDictionary(item => item.Id, item => item.Id == "safe_idle" ? 1d : 0d)));
        }
    }
}
