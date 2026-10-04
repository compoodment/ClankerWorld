using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal static class FoodCapacityTestFixture
{
    internal const string Household = "household:camp-alpha";
    internal const string House = "first-town-house-a";

    internal static async Task<(PrivateWorldRuntimeState State, string Actor, GridPoint HousePosition)> Generated(string seed)
    {
        using var setup = NormalPathWorld.CreateGenerated(seed, _ => new Choices());
        for (var tick = 0; tick < 8; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        state = SettlementWeatherTestFixture.WithWeather(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? house.Position : person.Position,
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(10_000),
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
        }, WeatherKind.Clear);
        return (state, actor, house.Position);
    }

    internal static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    internal static PrivateWorldRuntimeState WithFullness(PrivateWorldRuntimeState state, string actor, int fullness) =>
        state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = fullness } : person).ToArray(),
        };

    internal static InventoryCheckpoint WithoutPersonalCargo(PrivateWorldRuntimeState state, string actor) =>
        state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
        };

    internal static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, Choices choices) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? choices : new Choices());

    internal static async Task AssertReplay(PrivateWorldRuntime world, string actor, params string[] allowed)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, new Choices(allowed));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    internal sealed class Choices(params string[] allowed) : IDecisionProvider
    {
        internal List<string[]> Offers { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offers.Add(request.Observation.Candidates.Select(candidate => candidate.Id).ToArray());
            var permitted = request.Observation.Candidates.Where(candidate =>
                allowed.Any(prefix => candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = permitted.Length == 0
                        ? [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")]
                        : permitted,
                },
            }, cancellationToken);
        }
    }
}
