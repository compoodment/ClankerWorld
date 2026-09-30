using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FoodRoutingTests
{
    private const string Actor = "agent:00000000000000000000000000000099";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HungryAgentReachesFoodDespiteOccupiedNearestRouteAcrossReload(bool blockNearest)
    {
        var options = new GeographyOptions("audit-food-route-13", WorldSizePreset.Small);
        using var setup = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var map = setup.ExportState().Map;
        var anchor = map.Resources.Single(item => item.Id == "berry-patch").Position;
        setup.InitializeFirstTownContent();
        setup.AcceptFirstTownLayout(anchor);
        var buildings = setup.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(setup.WorldContent.Buildings.Single(item =>
                item.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var placements = map.Tiles.Where(tile => Math.Abs(tile.Position.X - anchor.X) <= 5 &&
            Math.Abs(tile.Position.Y - anchor.Y) <= 5 && map.IsBuildable(tile.Position) &&
            !buildings.Contains(tile.Position) && !map.Resources.Any(item => item.Position == tile.Position))
            .Take(5).Select(tile => tile.Position).ToArray();
        Assert.Equal(5, placements.Length);
        for (var index = 0; index < 4; index++)
            setup.PlaceFounder($"founder:{index + 1:D32}", placements[index]);
        setup.StartWorld();
        setup.AddAgent(Actor, new GridPoint(64, 121));
        setup.AddAgent("agent:00000000000000000000000000000100", blockNearest ? new GridPoint(65, 125) : placements[4]);
        var state = setup.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { HungerBasisPoints = 1_800 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new FoodProvider());
        for (var tick = 0; tick < 20; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new FoodProvider());
        for (var tick = 0; tick < 180; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        restored.Validate();
        var result = restored.ExportState();
        Assert.Contains(result.Events, item => item.Kind == "food_harvested" && item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
        Assert.True(restored.Inhabitants.Single(person => person.InhabitantId == Actor).HungerBasisPoints > 1_800);
        Assert.DoesNotContain(result.Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith(Actor + ":no_route", StringComparison.Ordinal));
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result));
    }

    private sealed class FoodProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.InhabitantId == Actor
                ? request.Observation.Candidates.FirstOrDefault(item => item.Id is "consume_food" or "harvest_food" or "seek_food") : null;
            choice ??= request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
