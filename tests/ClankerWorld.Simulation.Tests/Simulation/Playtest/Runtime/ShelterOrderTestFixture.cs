using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal static class ShelterOrderTestFixture
{
    internal const string Alpha = "household:camp-alpha";
    internal const string Beta = "household:camp-beta";
    private static readonly Lazy<byte[]> Baseline = new(CreateBaseline);

    internal static PrivateWorldRuntimeState Prepared() => PrivateWorldRuntimeCodec.Decode(Baseline.Value);

    internal static string Actor(PrivateWorldRuntimeState state, string household = Alpha) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;

    internal static PlacedBuilding House(PrivateWorldRuntimeState state, string household = Alpha) =>
        state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));

    internal static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    internal static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, string actor, GridPoint position)
    {
        var oldPosition = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = position, LastDecisionContext = null }
                : person.Position == position ? person with { Position = oldPosition } : person).ToArray(),
        };
    }

    internal static PrivateWorldRuntimeState WithStorm(PrivateWorldRuntimeState state, int? endsAt = null)
    {
        var systems = state.WorldSystems! with
        {
            RegionalWeather = null,
            Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Storm },
            Config = state.WorldSystems.Config with
            {
                WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
            },
        };
        systems = RegionalWeatherRules.Initialize(systems, state.Map);
        if (endsAt is { } end)
            systems = systems with
            {
                RegionalWeather = systems.RegionalWeather! with
                {
                    Episodes = systems.RegionalWeather.Episodes.Select(episode => episode with
                    {
                        EndsAt = end,
                        SevereAllowedAt = end + ((long)systems.Config.TicksPerDay + 1) / 2,
                    }).ToArray(),
                },
            };
        return state with { WorldSystems = systems };
    }

    internal static PrivateWorldRuntimeState WithClearWeather(PrivateWorldRuntimeState state) => state with
    {
        WorldSystems = state.WorldSystems! with
        {
            RegionalWeather = null,
            Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            Config = state.WorldSystems.Config with
            {
                WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
            },
        },
    };

    internal static GridPoint Approach(PrivateWorldRuntimeState state, GridPoint destination, int distance = 4)
    {
        var occupied = state.Inhabitants.Select(person => person.Position).ToHashSet();
        var seen = new HashSet<GridPoint> { destination };
        var pending = new Queue<(GridPoint Point, int Distance)>();
        pending.Enqueue((destination, 0));
        while (pending.TryDequeue(out var step))
        {
            if (step.Distance == distance && state.Map.FootDistance(step.Point, destination) >= distance &&
                !state.WorldSimulation!.Buildings.Any(building => building.Position == step.Point)) return step.Point;
            if (step.Distance >= distance) continue;
            foreach (var next in state.Map.FootNeighbors(step.Point))
                if (!occupied.Contains(next) &&
                    (!state.Map.IsDiagonalFootStep(step.Point, next) ||
                     !occupied.Contains(new(next.X, step.Point.Y)) && !occupied.Contains(new(step.Point.X, next.Y))) &&
                    seen.Add(next)) pending.Enqueue((next, step.Distance + 1));
        }
        throw new InvalidOperationException("The compact shelter fixture needs a clear approach.");
    }

    private static byte[] CreateBaseline()
    {
        using var generated = NormalPathWorld.CreateGenerated("personal-storage-orders", _ => new Idle());
        var state = generated.ExportState();
        state = WithInventory(state, state.Society.Society.Inventory with { Lots = [] }) with
        {
            JevEnabled = true,
            Survival = new(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = new(),
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
            }).ToArray(),
        };
        state = At(state, Actor(state), House(state).Position);
        return PrivateWorldRuntimeCodec.Encode(state);
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}
