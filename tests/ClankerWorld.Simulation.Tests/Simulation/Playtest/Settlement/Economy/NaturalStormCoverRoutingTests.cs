using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class NaturalStormCoverRoutingTests
{
    private const string Actor = "agent:00000000000000000000000000000099";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreachableNearestCoverDoesNotHideReachableProtectionAcrossRestart(bool blockFirstReachable)
    {
        var state = StormState(blockFirstReachable ? [new GridPoint(89, 51)] : []);
        using var world = PrivateWorldRuntime.Restore(state, id => new CoverProvider(id == Actor));
        for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => new CoverProvider(id == Actor));
        for (var tick = 0; tick < 18; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var sheltered = restored.Inhabitants.Single(person => person.InhabitantId == Actor);
        Assert.NotEqual(new GridPoint(88, 55), sheltered.Position);
        Assert.Equal(VegetationCover.Forest, state.Map.VegetationAt(sheltered.Position));
        Assert.Equal(0, sheltered.MoveWaitTicks);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith(Actor + ":no_route", StringComparison.Ordinal));
        if (blockFirstReachable) Assert.NotEqual(new GridPoint(89, 51), sheltered.Position);
    }

    [Fact]
    public async Task SurroundedAgentDoesNotRepeatedlyAttemptAnUnreachableRefuge()
    {
        var initial = StormState([]);
        var neighbors = initial.Map.FootNeighbors(new GridPoint(88, 55)).Where(initial.Map.IsPassable).ToArray();
        var probe = new CoverProvider(true);
        using var world = PrivateWorldRuntime.Restore(StormState(neighbors), id => id == Actor ? probe : new CoverProvider(false));
        for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(new GridPoint(88, 55), world.Inhabitants.Single(person => person.InhabitantId == Actor).Position);
        Assert.DoesNotContain(probe.Seen, request => request.Observation.Candidates.Any(item => item.Id == "seek_warmth"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith(Actor + ":no_route", StringComparison.Ordinal));
    }

    private static PrivateWorldRuntimeState StormState(GridPoint[] blockers)
    {
        var geography = new GeographyOptions("storm-routing-review-0", WorldSizePreset.Small);
        using var setup = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: geography);
        setup.InitializeFirstTownContent();
        setup.AcceptFirstTownLayout(new GridPoint(122, 56));
        for (var index = 0; index < 4; index++)
            setup.PlaceFounder("founder:" + (index + 1).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), new GridPoint(117 + index, 51));
        setup.StartWorld();
        setup.AddAgent(Actor, new GridPoint(88, 55));
        var map = setup.ExportState().Map;
        var blockerPositions = new Dictionary<string, GridPoint>();
        for (var index = 0; index < blockers.Length; index++)
        {
            var placement = map.Tiles.First(tile => map.IsBuildable(tile.Position) &&
                !map.Resources.Any(site => site.Position == tile.Position) &&
                !setup.Inhabitants.Any(person => person.Position == tile.Position)).Position;
            var id = "agent:" + (index + 100).ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
            setup.AddAgent(id, placement);
            blockerPositions.Add(id, blockers[index]);
        }
        var state = setup.ExportState();
        return state with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { HungerBasisPoints = 9_000, Survival = new SurvivalCondition(WarmthBasisPoints: 4_000) }
                : blockerPositions.TryGetValue(person.InhabitantId, out var blockerPosition)
                    ? person with { Position = blockerPosition }
                    : person).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Storm },
            },
        };
    }

    private sealed class CoverProvider(bool seekCover) : IDecisionProvider
    {
        public List<CognitionDecisionRequest> Seen { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add(request);
            var choice = request.Observation.Candidates.FirstOrDefault(item => seekCover && item.Id == "seek_warmth")
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
